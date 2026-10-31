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
}
