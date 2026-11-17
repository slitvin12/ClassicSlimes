using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// SR2 calls Object.Destroy on face materials it believes are per-slime instances. Stock face materials are assets,
    /// which Unity refuses to destroy, but ours are runtime materials, so the shared mouth of a whole slime kind got
    /// destroyed and rendered magenta. Destroy calls on our shared face materials are skipped.
    /// </summary>
    [HarmonyPatch]
    static class DestroyGuard
    {
        public static int Blocked;
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Destroy" && m.Name != "DestroyImmediate") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(UnityEngine.Object)) yield return m;
            }
        }

        static bool Prefix(UnityEngine.Object __0)
        {
            if (Faces.Protected.Count == 0 || (object)__0 == null) return true;
            IntPtr p;
            try { p = __0.Pointer; } catch { return true; }
            if (!Faces.Protected.Contains(p)) return true;
            if (Blocked++ == 0) ClassicSlimesMod.Log.Msg("[faces] the game tried to destroy shared face material " + __0.name + " - kept alive (further cases are silent)");
            return false;
        }
    }

    /// <summary>
    /// CottonLibrary puts every custom slime and largo into SlimeDefinitions.Slimes twice, and the game logs
    /// "An item with the same key has already been added" for each duplicate on every RefreshIndexes call
    /// (~2100 errors per launch). Duplicate entries are dropped before the index is rebuilt.
    /// </summary>
    [HarmonyPatch(typeof(SlimeDefinitions), nameof(SlimeDefinitions.RefreshIndexes))]
    static class SlimeIndexDedup
    {
        public static int Removed;
        static bool _failed;

        static void Prefix(SlimeDefinitions __instance)
        {
            if (_failed) return;
            try
            {
                var arr = __instance.Slimes;
                if (arr == null || arr.Length < 2) return;
                var seen = new HashSet<IntPtr>();
                List<SlimeDefinition> keep = null;
                for (int i = 0; i < arr.Length; i++)
                {
                    var d = arr[i];
                    if (d == null || seen.Add(d.Pointer)) { keep?.Add(d); continue; }
                    if (keep == null) { keep = new List<SlimeDefinition>(arr.Length); for (int j = 0; j < i; j++) keep.Add(arr[j]); }
                }
                if (keep == null) return;
                if (Removed == 0) ClassicSlimesMod.Log.Msg($"[defs] dropped {arr.Length - keep.Count} duplicate SlimeDefinitions.Slimes entries (double registration) - further cases are silent");
                Removed += arr.Length - keep.Count;
                __instance.Slimes = Gc.Array(keep);
            }
            catch (Exception e) { _failed = true; ClassicSlimesMod.Log.Warning("[defs] dedup failed: " + e.Message); }
        }
    }

    /// <summary>
    /// CottonLibrary's largo builder reads _SloomberTop/Middle/BottomColor from both parents' body materials for every
    /// largo; the SR2 body shader has no _SloomberMiddleColor, so each largo logged two Material.GetColor errors.
    /// Same lookup, guarded by HasProperty (missing colours come back as zero, like before).
    /// </summary>
    [HarmonyPatch]
    static class SloomberPaletteGuard
    {
        static MethodBase _target;

        static bool Prepare()
        {
            try { _target = AccessTools.Method(typeof(CottonLibrary.Library), "INTERNAL_GetSloomberPalette"); } catch { _target = null; }
            return _target != null;
        }

        static MethodBase TargetMethod() => _target;

        static bool Prefix(SlimeAppearance __0, ref SlimeAppearance.Palette __result)
        {
            try
            {
                Material mat = null;
                var st = __0 != null ? __0._structures : null;
                if (st != null)
                    foreach (var s in st)
                        if (s?.Element != null && s.Element.Type == SlimeAppearanceElement.ElementType.BODY && s.DefaultMaterials != null && s.DefaultMaterials.Count > 0)
                        { mat = s.DefaultMaterials[0]; break; }
                __result = new SlimeAppearance.Palette
                {
                    Ammo = new Color32(255, 255, 255, 255),
                    Top = Get(mat, "_SloomberTopColor"),
                    Middle = Get(mat, "_SloomberMiddleColor"),
                    Bottom = Get(mat, "_SloomberBottomColor"),
                };
                return false;
            }
            catch { return true; }
        }

        static Color Get(Material m, string prop) => m != null && m.HasProperty(prop) ? m.GetColor(prop) : default;
    }

    /// <summary>Water (SR2 CalmedByWaterSpray) pops quantum ghosts, fizzles mosaic glints and reveals glitch slimes.</summary>
    [HarmonyPatch(typeof(CalmedByWaterSpray), nameof(CalmedByWaterSpray.AddLiquid))]
    static class WaterPatch
    {
        static void Postfix(CalmedByWaterSpray __instance)
        {
            try
            {
                var go = __instance.gameObject;
                var q = go.GetComponent<QuantumSlime>(); if (q != null) q.OnWater();
                var m = go.GetComponent<MosaicSlime>(); if (m != null) m.OnWater();
                var g = go.GetComponent<GlitchSlime>(); if (g != null) g.OnWater();
            }
            catch { }
        }
    }

    /// <summary>Quicksilver slimes get overcharged by energy food (rad plorts, any plort a little).</summary>
    [HarmonyPatch(typeof(SlimeEat), nameof(SlimeEat.InvokeOnEat))]
    static class EatPatch
    {
        static void Postfix(SlimeEat __instance, IdentifiableType otherId)
        {
            try
            {
                if (otherId == null) return;
                var qs = __instance.GetComponent<QuicksilverSlime>();
                if (qs == null) return;
                var rad = SlimeFactory.Spec("Rad")?.Plort;
                if (rad != null && otherId == rad) qs.Overcharge(60f);
                else if (otherId.IsPlort) qs.Overcharge(15f);
            }
            catch { }
        }
    }

    /// <summary>Quicksilver slimes never hop: SR2's random hopping is skipped (QuicksilverSlime's runner drives them).</summary>
    [HarmonyPatch(typeof(SlimeRandomMove), nameof(SlimeRandomMove.Action))]
    static class QsNoHopMove
    {
        static bool Prefix(SlimeRandomMove __instance)
        {
            try { return QuicksilverSlime.NoHop.Count == 0 || !QuicksilverSlime.NoHop.ContainsKey(__instance.Pointer); }
            catch { return true; }
        }
    }

    /// <summary>...and instead of leaping at food they race to it.</summary>
    [HarmonyPatch(typeof(GotoConsumable), nameof(GotoConsumable.Action))]
    static class QsNoHopFood
    {
        static bool Prefix(GotoConsumable __instance)
        {
            try
            {
                if (QuicksilverSlime.NoHop.Count == 0 || !QuicksilverSlime.NoHop.TryGetValue(__instance.Pointer, out var qs) || qs == null) return true;
                var t = __instance._target;
                if (t != null) qs.RunTo(t.transform.position);
                return false;
            }
            catch { return true; }
        }
    }

    /// <summary>Diagnostics: material property dump and a row of candidate materials in front of the player.</summary>
    public static class Gallery
    {
        static readonly string[] Candidates =
        {
            "RadiantAura_strong", "RadiantAura_light", "SphereLightAdditiveGradientRadiant", "SphereLightAdditiveGradientRadiantPhosphor",
            "DroneHologram_Wings", "fx phaseLemon", "LightningMoteGlow_Outer", "LightningMoteGlow_Inner", "fxDiamondGlint", "envLabGlass",
            "QuantumDroneResourceVFX", "slimeTwinBase", "slimeFlutterWing", "slimeGoldBase", "slimeHyperBase", "slimePhosphorBase",
        };

        public static void DumpProps()
        {
            var sb = new StringBuilder();
            foreach (var n in Candidates) { var m = Mat.Find(n); if (m != null) Diag.DumpMatValues(sb, m); else sb.AppendLine("== MISSING " + n); }
            foreach (var kv in Visuals.Mats) Diag.DumpMatValues(sb, kv.Value);
            File.WriteAllText(Path.Combine(Diag.Dir, "matprops.txt"), sb.ToString());
        }

        public static void Show()
        {
            var p = U.Player;
            if (p == null) return;
            var root = new GameObject("ClassicSlimesGallery");
            var cam = Camera.main;
            var basis = cam != null ? cam.transform : p.transform;
            var fwd = basis.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            root.transform.position = basis.position + fwd * 6f + Vector3.up * 0.2f;
            int i = 0;
            foreach (var n in Candidates)
            {
                var m = Mat.Find(n);
                if (m != null) Visuals.Sphere("G_" + n, root.transform, right * ((i - Candidates.Length / 2f) * 1.25f), 0.5f, m).transform.SetParent(root.transform, true);
                i++;
            }
            i = 0;
            foreach (var kv in Visuals.Mats)
            {
                Visuals.Sphere("O_" + kv.Key, root.transform, right * ((i - Visuals.Mats.Count / 2f) * 1.25f) + Vector3.up * 1.4f, 0.5f, kv.Value);
                i++;
            }
            ClassicSlimesMod.Log.Msg("Gallery: " + string.Join(", ", Candidates) + " || " + string.Join(", ", Visuals.Mats.Keys));
        }
    }

    /// <summary>
    /// Ensures classic SR1 foods (Oca Oca, Silver Parsnip, Phase Lemon) are accepted by GordoSnare.FoodTypeGroup
    /// and always render visibly with their child mesh/materials/world-scale on Gordo Snares.
    /// </summary>
    [HarmonyPatch]
    static class GordoSnareBaitPatch
    {
        static readonly Dictionary<IntPtr, IdentifiableType> _lastBait = new();

        static void EnsureSnareFoodGroup(GordoSnare snare)
        {
            var fg = snare != null ? snare.FoodTypeGroup : null;
            if (fg == null) return;
            foreach (var f in Foods.All)
            {
                var t = f.Type;
                if (t == null) continue;
                try
                {
                    if (fg._memberTypes != null && !fg._memberTypes.Contains(t)) fg._memberTypes.Add(t);
                    var rt = fg.GetRuntimeObject();
                    if (rt != null && !rt.IsMember(t)) rt.AddMember(t);
                }
                catch { }
            }
        }

        public static void EnsureBaitVisual(GordoSnare snare, IdentifiableType baitType)
        {
            if (snare == null) return;
            EnsureSnareFoodGroup(snare);
            if (baitType != null) _lastBait[snare.Pointer] = baitType;
            else _lastBait.TryGetValue(snare.Pointer, out baitType);
            if (baitType == null)
            {
                // Fallback: resolve from Bait's IdentifiableActor if instantiated from prefab
                var bGo = snare.Bait;
                var ida = bGo != null ? bGo.GetComponentInChildren<IdentifiableActor>(true) : null;
                if (ida != null) baitType = ida.identType;
            }
            if (baitType == null || baitType.prefab == null) return;
            Foods.FixLemon(baitType.prefab, false);
            var baitGo = snare.Bait;
            if (baitGo == null) return;
            Foods.FixLemon(baitGo, true);

            MeshFilter srcMf = null;
            MeshRenderer srcMr = null;
            foreach (var mf in baitType.prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null) { srcMf = mf; srcMr = mr; break; }
            }
            if (srcMf == null || srcMr == null) return;

            var bMf = baitGo.GetComponent<MeshFilter>();
            var bMr = baitGo.GetComponent<MeshRenderer>();
            if (bMf != null && bMr != null)
            {
                if (bMf.sharedMesh == null) bMf.sharedMesh = srcMf.sharedMesh;
                if (bMr.sharedMaterial == null || bMr.sharedMaterials == null || bMr.sharedMaterials.Length == 0)
                    bMr.sharedMaterials = srcMr.sharedMaterials;
                bMr.enabled = true;
                bool isOurs = Foods.All.Exists(x => x.Type == baitType);
                if (isOurs)
                {
                    var ws = srcMf.transform.lossyScale;
                    var ps = baitGo.transform.parent != null ? baitGo.transform.parent.lossyScale : Vector3.one;
                    if (ps.x > 1e-4f && ps.y > 1e-4f && ps.z > 1e-4f)
                        baitGo.transform.localScale = new Vector3(ws.x / ps.x, ws.y / ps.y, ws.z / ps.z);
                }
            }
            foreach (var cmr in baitGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (cmr == null) continue;
                var cmf = cmr.GetComponent<MeshFilter>();
                if (cmf != null && cmf.sharedMesh != null) { cmr.enabled = true; cmr.gameObject.SetActive(true); }
            }
            if (!baitGo.activeSelf) baitGo.SetActive(true);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GordoSnare), nameof(GordoSnare.Awake))]
        static void PostAwake(GordoSnare __instance)
        {
            try { EnsureSnareFoodGroup(__instance); } catch { }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GordoSnare), nameof(GordoSnare.AttachBait))]
        static void PostAttach(GordoSnare __instance, IdentifiableType __0)
        {
            try { EnsureBaitVisual(__instance, __0); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("GordoSnare AttachBait visual: " + e.Message); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GordoSnare), nameof(GordoSnare.SetModel))]
        static void PostSetModel(GordoSnare __instance)
        {
            try { if (!__instance.HasSnaredGordo()) EnsureBaitVisual(__instance, null); }
            catch { }
        }
    }

    /// <summary>
    /// Allows Quicksilver slimes to sit calmly on benches, couches, chairs and #1 Slime Stages (SlimeAttachPoint)
    /// without their ground-runner / flee impulse snapping the ConfigurableJoint.
    /// </summary>
    [HarmonyPatch]
    static class QuicksilverBenchPatch
    {
        [HarmonyPrefix, HarmonyPatch(typeof(SlimeAttachPoint), nameof(SlimeAttachPoint.OnTriggerEnter))]
        static void PreTrigger(SlimeAttachPoint __instance, Collider col)
        {
            try
            {
                if (__instance == null || col == null || __instance._slime != null) return;
                var rb = col.attachedRigidbody;
                var qs = rb != null ? rb.GetComponent<QuicksilverSlime>() : col.GetComponentInParent<QuicksilverSlime>();
                if (qs != null) qs.EnterSeatGrace(1.0f);
            }
            catch { }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SlimeAttachPoint), nameof(SlimeAttachPoint.OnAttach))]
        static void PostAttach(SlimeAttachPoint __instance)
        {
            try
            {
                var go = __instance != null ? __instance._slime : null;
                var qs = go != null ? go.GetComponent<QuicksilverSlime>() : null;
                if (qs == null) return;
                qs.SetSeated(true);
                if (__instance._joint != null)
                {
                    __instance._joint.breakForce = float.PositiveInfinity;
                    __instance._joint.breakTorque = float.PositiveInfinity;
                }
            }
            catch { }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SlimeAttachPoint), nameof(SlimeAttachPoint.OnDetach))]
        static void PreDetach(SlimeAttachPoint __instance)
        {
            try
            {
                var go = __instance != null ? __instance._slime : null;
                var qs = go != null ? go.GetComponent<QuicksilverSlime>() : null;
                if (qs != null) qs.SetSeated(false);
            }
            catch { }
        }
    }

    /// <summary>Unlocks Slimepedia entries for Oca Oca, Silver Parsnip and Phase Lemon when collected.</summary>
    [HarmonyPatch]
    static class FoodPediaUnlockPatch
    {
        [HarmonyPostfix, HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Awake))]
        static void PostPediaAwake()
        {
            try { Foods.EnsurePediaDirectorLinks(); } catch { }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Unlock), typeof(IdentifiableType), typeof(bool))]
        static void PreUnlockIdent(PediaDirector __instance, IdentifiableType __0)
        {
            try { if (__instance != null && __0 != null) Foods.EnsurePediaDirectorLinks(); } catch { }
        }

        static void UnlockFoodIfNeeded(IdentifiableType t)
        {
            if (t == null || Foods.FoodPediaEntries.Count == 0) return;
            if (!Foods.FoodPediaEntries.TryGetValue(t.Pointer, out var entry) || entry == null) return;
            var pd = SceneContext.Instance != null ? SceneContext.Instance.PediaDirector : null;
            if (pd == null) return;
            Foods.EnsurePediaDirectorLinks();
            if (!pd.IsUnlocked(entry)) pd.Unlock(entry, true);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem), nameof(Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem.ConsumeVacuumable))]
        static void PreVacConsume(GameObject __0)
        {
            try
            {
                if (__0 == null) return;
                var ida = __0.GetComponent<IdentifiableActor>();
                if (ida != null) UnlockFoodIfNeeded(ida.identType);
            }
            catch { }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem), nameof(Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem.StartConsuming))]
        static void PreStartConsuming(IdentifiableType __1)
        {
            try { UnlockFoodIfNeeded(__1); } catch { }
        }
    }

    /// <summary>
    /// Classic*Gordo prefabs intentionally keep _id blank so fixed spots set their deterministic id before Awake;
    /// when another tool (e.g. Creator Toolkit spawner) instantiates the prefab directly, assign a unique id before GameModel registration.
    /// </summary>
    [HarmonyPatch(typeof(GordoEat), nameof(GordoEat.Awake))]
    static class ClassicGordoSpawnIdPatch
    {
        static void Prefix(GordoEat __instance)
        {
            try
            {
                if (__instance == null || !string.IsNullOrEmpty(__instance._id)) return;
                if (__instance.GetComponentInParent<GordoSnare>() != null) return;
                var gi = __instance.GetComponent<GordoIdentifiable>();
                if (gi == null || !Gordos.IsClassicGordoType(gi.identType)) return;
                __instance._id = Gordos.IdPrefix + "Spawn_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            catch { }
        }
    }
}

