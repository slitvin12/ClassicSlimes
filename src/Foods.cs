using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
using Entry = Il2Cpp.ResourceSpawnerDefinition.WeightedResourceEntry;

namespace ClassicSlimes
{
    /// <summary>
    /// SR1 foods of the classic slimes. SR2 still ships Oca Oca, Silver Parsnip and Phase Lemon (models, icons, garden
    /// patches/trees and resource growers) but they are not registered as foods; this enables them and adds them to wild
    /// veggie patches / fruit trees of the SR1-like zones.
    /// </summary>
    public static class Foods
    {
        public class FoodSpec
        {
            public string Ident, Key; public bool Fruit;
            public Library.SpawnLocations Zones;
            public Func<float> Share;
            public IdentifiableType Type;
            public string[] Scenes = new string[0];
        }

        public static readonly List<FoodSpec> All = new()
        {
            new FoodSpec { Ident = "OcaocaVeggie", Key = "l.ocaoca_veggie", Zones = Library.SpawnLocations.StarlightStand | Library.SpawnLocations.EmberValley, Share = () => Cfg.WildFoodShare.Value },
            new FoodSpec { Ident = "ParsnipVeggie", Key = "l.parsnip_veggie", Zones = Library.SpawnLocations.EmberValley | Library.SpawnLocations.RainbowFields, Share = () => Cfg.WildFoodShare.Value },
            new FoodSpec { Ident = "LemonFruit", Key = "l.lemon_fruit", Fruit = true, Zones = Library.SpawnLocations.PowderfallBluffs | Library.SpawnLocations.StarlightStand, Share = () => Cfg.WildFoodShare.Value },
        };

        public static bool Ready;

        static void Invoke(string name, string refId, IdentifiableType t)
        {
            var m = typeof(Library).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { ClassicSlimesMod.Log.Warning("Cotton " + name + " not found"); return; }
            m.Invoke(null, new object[] { refId, t });
        }

        static void AddTo(IdentifiableTypeGroup g, IdentifiableType t)
        {
            if (g == null || t == null) return;
            var all = Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>();
            var queue = new Queue<IdentifiableTypeGroup>();
            var visited = new HashSet<IntPtr>();
            queue.Enqueue(g);
            visited.Add(g.Pointer);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == null) continue;
                try
                {
                    if (cur._memberTypes != null && !cur._memberTypes.Contains(t)) cur._memberTypes.Add(t);
                    var rt = cur.GetRuntimeObject();
                    if (rt != null && !rt.IsMember(t)) rt.AddMember(t);
                }
                catch (Exception e) { ClassicSlimesMod.Log.Warning($"group {cur.name} runtime: {e.Message}"); }
                foreach (var pg in all)
                {
                    if (pg == null || pg._memberGroups == null || !visited.Add(pg.Pointer)) continue;
                    try
                    {
                        bool contains = false;
                        foreach (var mg in pg._memberGroups) if (mg != null && mg.Pointer == cur.Pointer) { contains = true; break; }
                        if (contains) queue.Enqueue(pg);
                    }
                    catch { visited.Remove(pg.Pointer); }
                }
            }
        }

        /// <summary>Called before the slimes are created (their favourites are looked up in the food groups).</summary>
        public static void Setup()
        {
            bool enabled = Cfg.EnableFoods.Value;
            if (!enabled) ClassicSlimesMod.Log.Msg("SR1 foods disabled in config (registering load IDs only)");
            var byName = new Dictionary<string, IdentifiableType>();
            foreach (var t in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                if (t != null && !byName.ContainsKey(t.name)) byName[t.name] = t;
            foreach (var f in All)
            {
                try
                {
                    if (!byName.TryGetValue(f.Ident, out var t) || t == null) { ClassicSlimesMod.Log.Warning($"Food {f.Ident} not found in this game version"); continue; }
                    f.Type = t;
                    string refId = string.IsNullOrEmpty(t.referenceId) ? "IdentifiableType." + t.name : t.referenceId;
                    t.referenceId = refId;
                    Invoke("INTERNAL_SetupLoadForIdent", refId, t);
                    if (!enabled) continue;
                    var group = f.Fruit ? Library.fruits : Library.veggies;
                    AddTo(group, t);
                    SlimeFactory.RegisterGroupByName("IdentifiableTypesGroup", t);
                    SlimeFactory.RegisterGroupByName(f.Fruit ? "FruitQuantumCloudGroup" : "VeggieQuantumCloudGroup", t);
                    if (t.groupType == null) t.groupType = group;
                    Library.MakeVaccable(t);
                    t.localizedName = Library.CreateLocalizedString(f.Key);
                    string shown = null;
                    try { shown = t.localizedName?.GetLocalizedString(); } catch { }
                    f.Scenes = Library.GetSceneNamesFromSpawnerZones(f.Zones).Select(x => x.ToLowerInvariant()).ToArray();
                    ClassicSlimesMod.Log.Msg($"  food {t.name} ref={refId} group={group?.name} name='{shown}' wild=[{string.Join(",", f.Scenes)}]");
                }
                catch (Exception e) { ClassicSlimesMod.Log.Error($"Food {f.Ident}: {e}"); }
            }
            if (!enabled) { SetupPhaseLemonLoadOnly(byName); return; }
            Ready = All.Any(f => f.Type != null);
            if (!Ready) return;
            // food-group based diets were built before the new members existed
            int n = 0;
            foreach (var d in Resources.FindObjectsOfTypeAll<SlimeDefinition>())
            {
                if (d == null || d.IsLargo) continue;
                try { d.RefreshEatmap(); n++; } catch { }
            }
            ClassicSlimesMod.Log.Msg($"  refreshed {n} base slime diets");
            try { FixLemonModels(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon mesh: " + e.Message); }
            try { FixFoodRootMeshes(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("food root meshes: " + e.Message); }
            try { SetupPhaseLemon(byName); } catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon tree: " + e); }
            try { RegisterFoodPedia(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("food pedia: " + e); }
        }

        // SR2's dormant veggieOcaoca, veggieParsnip and fruitLemon prefabs carry a dummy empty MeshFilter (sharedMesh=null)
        // on the root GameObject above model_ocaoca / model_parsnip / model_pogofruit. Removing it fails on persistent assets
        // and breaks QuantumDroneResourceFX, so instead assign the real mesh onto the empty root MeshFilter.
        static void FixFoodRootMeshes()
        {
            foreach (var f in All)
            {
                var prefab = f.Type != null ? f.Type.prefab : null;
                if (prefab == null) continue;
                var rootMf = prefab.GetComponent<MeshFilter>();
                if (rootMf == null || rootMf.sharedMesh != null) continue;
                Mesh childMesh = null;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf == rootMf || mf.sharedMesh == null) continue;
                    if (mf.name.IndexOf("FX", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    childMesh = mf.sharedMesh;
                    break;
                }
                if (childMesh == null && f.Ident == "LemonFruit") childMesh = Res.Mesh("phaseLemon");
                if (childMesh != null)
                {
                    rootMf.sharedMesh = childMesh;
                    ClassicSlimesMod.Log.Msg($"  populated empty root MeshFilter on {prefab.name} -> {childMesh.name}");
                }
            }
        }

        public static readonly Dictionary<IntPtr, Il2CppMonomiPark.SlimeRancher.Pedia.IdentifiablePediaEntry> FoodPediaEntries = new();

        public static void RegisterFoodPedia()
        {
            Il2CppMonomiPark.SlimeRancher.Pedia.IdentifiablePediaEntry carrotEntry = null, pogoEntry = null;
            foreach (var e in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.IdentifiablePediaEntry>())
            {
                if (e == null) continue;
                if (e.name == "CarrotVeggie" || (e._identifiableType != null && e._identifiableType.name == "CarrotVeggie")) carrotEntry ??= e;
                else if (e.name == "PogoFruit" || (e._identifiableType != null && e._identifiableType.name == "PogoFruit")) pogoEntry ??= e;
            }
            foreach (var f in All)
            {
                var t = f.Type;
                if (t == null) continue;
                var tpl = f.Fruit ? (pogoEntry ?? carrotEntry) : (carrotEntry ?? pogoEntry);
                if (tpl == null) { ClassicSlimesMod.Log.Warning("Food pedia: no template IdentifiablePediaEntry"); return; }
                string suffix = f.Key.StartsWith("l.") ? f.Key.Substring(2) : t.name.ToLowerInvariant();
                var entry = Library.CreateIdentPediaEntry(
                    t,
                    suffix,
                    tpl,
                    Library.CreateLocalizedString("m.intro." + suffix, "Pedia"),
                    Library.PediaCategoryType.Resources,
                    Library.PediaDetail.Params(
                        Library.PediaDetail.Create(0, Library.CreateLocalizedString("m.desc." + suffix, "Pedia"), Library.PediaDetailType.About),
                        Library.PediaDetail.Create(1, Library.CreateLocalizedString("m.ranch." + suffix, "Pedia"), Library.PediaDetailType.HowToUse)
                    )
                );
                if (entry != null)
                {
                    try
                    {
                        t._fullArt = new UnityEngine.AddressableAssets.AssetReferenceT<Sprite>("");
                        t._requiresFullArt = false;
                    }
                    catch { }
                    if (t.icon != null) PediaArt.Register(t, t.icon);
                    FoodPediaEntries[t.Pointer] = entry;
                    ClassicSlimesMod.Log.Msg($"  registered Slimepedia resource entry: {t.name} -> {entry.name}");
                }
            }
            EnsurePediaDirectorLinks();
        }

        public static void EnsurePediaDirectorLinks()
        {
            ClearPhaseFromIgnoreOnLoad(SceneContext.Instance != null ? SceneContext.Instance.GameModel : null);
            foreach (var pd in Resources.FindObjectsOfTypeAll<PediaDirector>())
            {
                if (pd == null || pd._identDict == null) continue;
                foreach (var kv in FoodPediaEntries)
                {
                    var entry = kv.Value;
                    var ident = entry != null ? entry._identifiableType : null;
                    if (ident == null) continue;
                    try
                    {
                        if (!pd._identDict.ContainsKey(ident))
                            pd._identDict.Add(ident, entry);
                    }
                    catch { }
                }
                foreach (var s in SlimeFactory.Specs)
                {
                    if (s?.Def == null || s.Plort == null) continue;
                    try
                    {
                        var se = pd.GetEntry(s.Def);
                        if (se != null && !pd._identDict.ContainsKey(s.Plort))
                            pd._identDict.Add(s.Plort, se);
                    }
                    catch { }
                }
            }
        }

        // ------------------------------------------------------------------ phase lemon model
        // SR2 still has the lemon fruit, its tree and the phaseLemon material, but the fruit mesh itself (SR1
        // model_phaseLemon) is gone: model_pogofruit of fruitLemon and the tree spawn joints have empty MeshFilters, so only
        // the glow of the lemon was visible. The SR1 mesh uses the same UV layout as SR2's alb_frt_lemon / msk_frt_lemon.
        public static int LemonMeshes;
        static Material _lemonMat;

        static bool LemonMat(Material m) => m != null && (m.name ?? "").StartsWith("phaseLemon", StringComparison.OrdinalIgnoreCase);

        static bool IsFxObj(GameObject go, Material m) =>
            (go != null && go.name.IndexOf("FX", StringComparison.OrdinalIgnoreCase) >= 0)
            || (m != null && (m.name ?? "").StartsWith("fx ", StringComparison.OrdinalIgnoreCase));

        /// <summary>Empty lemon MeshFilters under root get the SR1 mesh (all = any empty renderer, e.g. tree spawn joints).</summary>
        internal static int FixLemon(GameObject root, bool all)
        {
            if (root == null || !Cfg.EnableFoods.Value) return 0;
            var mesh = Res.Mesh("phaseLemon");
            if (mesh == null) return 0;
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.name == PhaseHoloName) continue;
                var mat = mr.sharedMaterial;
                bool isFx = IsFxObj(mf.gameObject, mat);
                if (isFx)
                {
                    // FX Aura keeps its "fx phaseLemon" quantum agitation shader; only supply a mesh if missing.
                    if (all && mf.sharedMesh == null) { mf.sharedMesh = mesh; n++; }
                    continue;
                }
                if (mf.sharedMesh != null) continue;
                if (!LemonMat(mat))
                {
                    if (!all || (mat != null && mat.name.IndexOf("lemon", StringComparison.OrdinalIgnoreCase) < 0 && mf.name.IndexOf("SpawnJoint", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    if (_lemonMat != null) mr.sharedMaterial = _lemonMat;
                }
                mf.sharedMesh = mesh; n++;
            }
            LemonMeshes += n;
            return n;
        }

        static void FixLemonModels()
        {
            var lemon = All.FirstOrDefault(f => f.Ident == "LemonFruit")?.Type;
            if (lemon?.prefab == null) return;
            foreach (var mr in lemon.prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (LemonMat(mr.sharedMaterial)) { _lemonMat = mr.sharedMaterial; break; }
            int n = FixLemon(lemon.prefab, false);
            // other lemon prefabs that are loaded already (tree models, garden trees)
            foreach (var d in Resources.FindObjectsOfTypeAll<ResourceGrowerDefinition>())
                if (d != null && d._primaryResourceType == lemon && d._prefab != null) n += FixLemon(d._prefab, true);
            ClassicSlimesMod.Log.Msg($"  phase lemon: SR1 fruit mesh on {n} renderers (material {(_lemonMat != null ? _lemonMat.name : "missing")})");
        }

        // ------------------------------------------------------------------ SR1 phase lemon tree
        // SR1 lemon trees grow see-through *phasing* lemons that can't be vacuumed. Shooting any other fruit into the tree
        // phases that fruit out and knocks a solid phase lemon loose (SR1 LemonPhasor). SR2 still ships the phasing lemon
        // (LemonPhase / fruitPhasingLemon: not vacuumable, vanishes with FX LemonPhaseOut once its ripe time is over) and
        // the LemonPhasor class, but its lemon trees grow plain lemons and have no phasor trigger.
        static IdentifiableType _phase, _lemon;
        static Material _phaseHolo;
        static GameObject _phaseOutFx, _phaseInFx;
        static bool _phaseInTried;
        static int _phaseLog;
        public static int PhaseTrees, PhaseTriggers, PhaseHits;
        const string PhasorName = "ClassicSlimes_LemonPhasor", PhaseHoloName = "ClassicSlimes_PhaseHolo";
        const string PhaseLemonKey = "1051811259578094a8004e87615f38b6";   // LemonPhase.asset in the addressables catalog

        public class PhaseLemon { public GameObject Go, HoloGo; public IntPtr Ptr; public MeshRenderer Solid; public ResourceCycle Rc; public float Next; public bool On, Seen, Ripe; }

        internal static void ClearPhaseFromIgnoreOnLoad(Il2CppMonomiPark.SlimeRancher.DataModel.GameModel gm)
        {
            if (gm == null || _phase == null) return;
            try
            {
                if (gm.ignoreOnLoad != null)
                {
                    gm.ignoreOnLoad._memberTypes?.Remove(_phase);
                    var rt = gm.ignoreOnLoad.GetRuntimeObject();
                    if (rt != null && rt.IsMember(_phase))
                    {
                        rt._memberTypes?.Remove(_phase);
                        rt._memberIndices?.Remove(_phase);
                    }
                }
                if (gm.phaseLemonIdent == _phase) gm.phaseLemonIdent = null;
            }
            catch { }
        }

        static void SetupPhaseLemonLoadOnly(Dictionary<string, IdentifiableType> byName)
        {
            byName.TryGetValue("LemonPhase", out var p);
            if (p == null || p.prefab == null)
            {
                try { var l = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<IdentifiableType>(PhaseLemonKey).WaitForCompletion(); if (l != null) p = l; }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon: addressable load failed: " + e.Message); }
            }
            if (p == null) return;
            if (p.hideFlags == HideFlags.None) p.hideFlags = HideFlags.DontUnloadUnusedAsset;
            string refId = string.IsNullOrEmpty(p.referenceId) ? "IdentifiableType." + p.name : p.referenceId;
            p.referenceId = refId;
            Invoke("INTERNAL_SetupLoadForIdent", refId, p);
            _phase = p;
        }

        internal static void ClearSession()
        {
            Spots.Clear();
            Added.Clear();
            _logged = 0;
            PhaseTrees = 0;
            PhaseTriggers = 0;
            PhaseHits = 0;
            _phaseLog = 0;
        }

        static void SetupPhaseLemon(Dictionary<string, IdentifiableType> byName)
        {
            _lemon = All.FirstOrDefault(f => f.Ident == "LemonFruit")?.Type;
            if (_lemon == null) return;
            byName.TryGetValue("LemonPhase", out var p);
            if (p == null || p.prefab == null)
            {
                // no vanilla group references it, so it is not loaded: load it from its addressable (asset GUID key)
                try { var l = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<IdentifiableType>(PhaseLemonKey).WaitForCompletion(); if (l != null) p = l; }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon: addressable load failed: " + e.Message); }
            }
            if (p == null || p.prefab == null) { ClassicSlimesMod.Log.Warning($"phase lemon: LemonPhase not found ({(p == null ? "no type" : "no prefab")})"); return; }
            if (p.hideFlags == HideFlags.None) p.hideFlags = HideFlags.DontUnloadUnusedAsset;
            string refId = string.IsNullOrEmpty(p.referenceId) ? "IdentifiableType." + p.name : p.referenceId;
            p.referenceId = refId;
            Invoke("INTERNAL_SetupLoadForIdent", refId, p);   // saves may hold phasing lemons, also after the trees are turned off
            _phase = p;
            var rc = p.prefab.GetComponent<ResourceCycle>();
            if (rc != null)
            {
                _phaseOutFx = SanitizePhaseFx(rc.DestroyFX, "PhaseLemonOutClean");
                rc.DestroyFX = _phaseOutFx;
            }
            FixLemon(p.prefab, false);
            var lemonMesh = Res.Mesh("phaseLemon");
            // solid body = the real lemon look (PhaseLemonTree flickers it and adds a scanline shell)
            foreach (var mr in p.prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.name == PhaseHoloName) continue;
                if (IsFxObj(mr.gameObject, mr.sharedMaterial))
                {
                    var fxMf = mr.GetComponent<MeshFilter>();
                    if (fxMf != null && fxMf.sharedMesh == null && lemonMesh != null) fxMf.sharedMesh = lemonMesh;
                    continue;
                }
                var bodyMf = mr.GetComponent<MeshFilter>();
                if (bodyMf != null && bodyMf.sharedMesh == null && lemonMesh != null) bodyMf.sharedMesh = lemonMesh;
                if (_lemonMat != null && (LemonMat(mr.sharedMaterial) || mr.sharedMaterial == null)) mr.sharedMaterial = _lemonMat;
            }
            int n = 0;
            if (Cfg.PhaseLemonTrees.Value)
                foreach (var d in Resources.FindObjectsOfTypeAll<ResourceGrowerDefinition>()) if (PhaseGrower(d)) n++;
            ClassicSlimesMod.Log.Msg($"  phase lemon: {p.name} ref={refId} fx={(_phaseOutFx != null ? _phaseOutFx.name : "none")} trees={(Cfg.PhaseLemonTrees.Value ? n + " growers" : "off")}");
        }

        /// <summary>Lemon tree growers grow phasing lemons instead of plain ones (in place: garden and wild lemon trees).</summary>
        static bool PhaseGrower(ResourceGrowerDefinition d)
        {
            if (d == null || _phase == null || d._primaryResourceType == null || d._primaryResourceType.name != "LemonFruit") return false;
            var res = d._resources;
            if (res == null) return false;
            bool any = false;
            foreach (var e in res)
                if (e != null && e.ResourceIdentifiableType != null && e.ResourceIdentifiableType.name == "LemonFruit") { e.ResourceIdentifiableType = _phase; any = true; }
            return any;
        }

        static Vector3 LocalPosIn(Transform root, Transform node)
        {
            var p = Vector3.zero;
            for (var cur = node; cur != null && cur != root; cur = cur.parent)
                p = cur.localPosition + cur.localRotation * Vector3.Scale(p, cur.localScale);
            return p;
        }

        static void AddPhasor(SpawnResource sr)
        {
            if (sr.transform.Find(PhasorName) != null) return;
            var go = new GameObject(PhasorName);
            // ActorTrigger collides with both Layers.Actor and Layers.Launched at point-blank ranch range, and is excluded from vac/UI/gadget raycasts.
            go.layer = Layers.ActorTrigger;
            go.transform.SetParent(sr.transform, false);
            Vector3 c = new Vector3(0f, 3.0f, 0f);
            float r = 2.2f;
            var joints = sr.SpawnJoints;
            if (joints != null && joints.Length > 0)
            {
                var pts = new List<Vector3>();
                foreach (var j in joints) if (j != null) pts.Add(LocalPosIn(sr.transform, j.transform));
                if (pts.Count > 0)
                {
                    var sum = Vector3.zero;
                    foreach (var q in pts) sum += q;
                    c = sum / pts.Count;
                    float m = 0f;
                    foreach (var q in pts) m = Mathf.Max(m, (q - c).magnitude);
                    r = Mathf.Clamp(m + 0.55f, 1.4f, 2.9f);
                }
            }
            go.transform.localPosition = c;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = r;
            var t = go.AddComponent<PhaseLemonTree>();
            t.Sr = sr;
            if (!_phaseInTried) { _phaseInTried = true; try { _phaseInFx = SanitizePhaseFx(Fx.Kept("FX LemonPhaseIn"), "PhaseLemonInClean"); } catch { } }
            PhaseTrees++;
        }

        internal static GameObject SpawnActor(IdentifiableType t, Vector3 pos, Quaternion rot, Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneGroup sceneGroup = null)
        {
            var sc = SRSingleton<SceneContext>.Instance;
            var group = sceneGroup ?? sc.RegionRegistry.CurrentSceneGroup;
            Il2CppMonomiPark.SlimeRancher.DataModel.ActorModel model = null;
            try { model = sc.GameModel.InstantiateActorModel(t, group, pos, rot, false); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"InstantiateActorModel({t.name}): {e.Message}"); }
            if (model != null)
            {
                var go = InstantiationHelpers.InstantiateActorFromModel(model);
                CoopCompat.AnnounceSpawn(go);
                return go;
            }
            return InstantiationHelpers.InstantiateActor(t.prefab, group, pos, rot, false, SlimeAppearance.AppearanceSaveSet.NONE,
                SlimeAppearance.AppearanceSaveSet.NONE, new Il2CppSystem.Nullable<Il2CppMonomiPark.SlimeRancher.Player.AmmoSlot.AmmoMetadata>(), false, false);
        }

        static Material _phaseSparkMat;
        static Material PhaseSparkMat
        {
            get
            {
                if (_phaseSparkMat != null) return _phaseSparkMat;
                var m = Mat.Clone("PhaseLemonSpark", "Gold Slime Sparkle");
                if (m != null)
                {
                    m.Tx("_MainTex", Res.Tex("fx_star4", false, false) ?? Gfx.Generated("phaseDot", 16, 16, (x, y) =>
                    {
                        float dx = (x + 0.5f) / 8f - 1f, dy = (y + 0.5f) / 8f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        return new Color(1f, 1f, 1f, a * a);
                    }, false));
                    m.Col("_TintColor", new Color(1f, 0.90f, 0.32f, 0.85f));
                }
                _phaseSparkMat = m;
                return _phaseSparkMat;
            }
        }

        static bool BrokenFxMat(Material m)
        {
            if (m == null) return true;
            var sh = m.shader;
            if (sh == null || !sh.isSupported) return true;
            string sn = sh.name ?? "";
            if (sn.Contains("InternalError") || sn.Contains("Error") || sn.Contains("Hidden/")) return true;
            bool anyTexProp = false, anyValidTex = false;
            foreach (var p in new[] { "_MainTex", "_BaseColorMap", "_UnlitColorMap", "_MainTexture", "_Tex" })
            {
                if (!m.HasProperty(p)) continue;
                anyTexProp = true;
                if (m.GetTexture(p) != null) anyValidTex = true;
            }
            if (anyTexProp && !anyValidTex) return true;
            return false;
        }

        static void ScrubFxObject(GameObject root)
        {
            if (root == null) return;
            var fallback = PhaseSparkMat;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var psr = r.TryCast<ParticleSystemRenderer>();
                if (psr != null && psr.trailMaterial != null && BrokenFxMat(psr.trailMaterial))
                    psr.trailMaterial = fallback;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) { r.enabled = false; continue; }
                bool changed = false, ok = true;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!BrokenFxMat(mats[i])) continue;
                    if (fallback != null) { mats[i] = fallback; changed = true; }
                    else ok = false;
                }
                if (!ok) r.enabled = false;
                else if (changed) r.sharedMaterials = mats;
            }
        }

        static GameObject SanitizePhaseFx(GameObject src, string cleanName)
        {
            if (src == null) return null;
            var copy = Fx.Kept(cleanName);
            if (copy != null) return copy;
            var host = new GameObject(cleanName + "_Host");
            host.SetActive(false);
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            var clone = Object.Instantiate(src, host.transform);
            clone.name = cleanName;
            ScrubFxObject(clone);
            return clone;
        }

        static void PlayFx(GameObject fx, Vector3 pos)
        {
            try
            {
                if (fx != null)
                {
                    var inst = FXHelpers.SpawnAndPlayFX(fx, pos, Quaternion.identity);
                    if (inst != null) ScrubFxObject(inst);
                }
                else Fx.Play("FX Despawn", pos, 0.55f);
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon fx: " + e.Message); }
        }

        static bool _swappingPhaseLemon;

        /// <summary>SR1 LemonPhasor.OnTriggerEnter: a thrown fruit (not a lemon) knocks a ripe phasing lemon loose.</summary>
        internal static void OnPhaseTrigger(PhaseLemonTree tree, Collider col)
        {
            if (col == null || col.isTrigger || tree == null || tree.Sr == null || _lemon == null) return;
            var rb = col.attachedRigidbody;
            var go = rb != null ? rb.gameObject : col.gameObject;
            var ida = go.GetComponent<IdentifiableActor>();
            var t = ida != null ? ida.identType : null;
            if (t == null || t == _lemon || t == _phase || !InGroup(Library.fruits, t)) return;
            if (!CoopCompat.IsSpawnAuthority(tree.transform.position)) return;
            var vac = go.GetComponent<Il2CppMonomiPark.SlimeRancher.Vacuumable>();
            if (vac != null && vac.IsHeld()) return;
            if (!tree.Handled.Add(go.Pointer)) return;
            PhaseTriggers++;
            var joint = tree.Sr.PickRipeResourceJoint();
            var body = joint != null ? joint.connectedBody : null;
            if (Diag.Enabled && _phaseLog++ < 12)
                ClassicSlimesMod.Log.Msg($"[phaselemon] {t.name} layer={go.layer} hit {tree.Sr.name}: ripe joint={(joint != null ? joint.name : "none")} body={(body != null ? body.name : "-")}");
            if (body == null) return;
            Vector3 pos = joint.transform.position;
            Quaternion rot = joint.transform.rotation;
            Vector3 fruitPos = go.transform.position;
            bool prevSwap = _swappingPhaseLemon;
            _swappingPhaseLemon = true;
            try
            {
                try { body.GetComponent<ResourceCycle>()?.DetachFromJoint(); } catch { }
                try { joint.connectedBody = null; } catch { }
                CoopCompat.DestroySharedActor(body.gameObject, "ClassicSlimes.LemonPhasor", false);
            }
            finally { _swappingPhaseLemon = prevSwap; }
            var sg = tree.Sr.GetComponentInParent<Il2CppMonomiPark.SlimeRancher.Regions.Region>()?.SceneGroup;
            var lemon = SpawnActor(_lemon, pos, rot, sg);
            if (lemon != null)
            {
                FixLemon(lemon, false);
                try
                {
                    var rc = lemon.GetComponent<ResourceCycle>();
                    if (rc != null)
                    {
                        if (rc.GetState() != ResourceCycle.State.EDIBLE) rc.MakeEdible();
                        var lrb = lemon.GetComponent<Rigidbody>();
                        if (lrb != null) rc.Eject(lrb);
                    }
                }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon eject: " + e.Message); }
            }
            PlayFx(_phaseInFx != null ? _phaseInFx : _phaseOutFx, pos);
            PlayFx(_phaseOutFx, fruitPos);
            CoopCompat.DestroySharedActor(go, "ClassicSlimes.LemonPhasor", false);
            PhaseHits++;
        }

        static Material PhaseHoloMat
        {
            get
            {
                if (_phaseHolo != null) return _phaseHolo;
                var src = Visuals.HoloMat;
                if (src == null) return null;
                _phaseHolo = new Material(src) { name = "PhaseLemonHolo", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _phaseHolo.SetColor("_UnlitColor", new Color(1.6f, 1.45f, 0.5f, 1f));
                _phaseHolo.SetTextureScale("_UnlitColorMap", new Vector2(2f, 3f));
                return Mat.Track(_phaseHolo);
            }
        }

        static PhaseLemon MakePhaseLook(GameObject go)
        {
            MeshRenderer solid = null; MeshFilter mf = null;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.name == PhaseHoloName || IsFxObj(mr.gameObject, mr.sharedMaterial)) continue;
                var m = mr.sharedMaterial;
                if (m == null || (m != _lemonMat && !LemonMat(m))) continue;
                var f = mr.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                solid = mr; mf = f; break;
            }
            if (solid == null) return null;
            GameObject holoGo = solid.transform.Find(PhaseHoloName)?.gameObject;
            var holo = PhaseHoloMat;
            if (holo != null && holoGo == null)
            {
                holoGo = new GameObject(PhaseHoloName);
                holoGo.layer = solid.gameObject.layer;
                holoGo.transform.SetParent(solid.transform, false);
                holoGo.transform.localScale = Vector3.one * 1.06f;
                holoGo.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var hr = holoGo.AddComponent<MeshRenderer>();
                hr.sharedMaterial = holo;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hr.receiveShadows = false;
            }
            var rc = go.GetComponent<ResourceCycle>();
            bool ripe = false;
            try { ripe = rc == null || rc.GetState() == ResourceCycle.State.RIPE; } catch { ripe = true; }
            solid.enabled = !ripe;
            if (holoGo != null) holoGo.SetActive(ripe);
            return new PhaseLemon { Go = go, HoloGo = holoGo, Ptr = go.Pointer, Solid = solid, Rc = rc, Ripe = ripe, Next = Time.time + U.Rand(0.1f, 0.8f) };
        }

        static int _holoFrame;
        static float _holoJump;
        static Vector2 _holoOfs;

        internal static void CleanupDetachedPhaseLemons(SpawnResource sr)
        {
            if (sr == null || _phase == null || _swappingPhaseLemon) return;
            var joints = sr.SpawnJoints;
            if (joints == null) return;
            foreach (var j in joints)
            {
                var rb = j != null ? j.connectedBody : null;
                if (rb == null) continue;
                var ida = rb.GetComponent<IdentifiableActor>();
                if (ida == null || ida.identType != _phase) continue;
                var go = rb.gameObject;
                var pos = go.transform.position;
                try { rb.GetComponent<ResourceCycle>()?.DetachFromJoint(); } catch { }
                try { j.connectedBody = null; } catch { }
                PlayFx(_phaseOutFx, pos);
                CoopCompat.DestroySharedActor(go, "ClassicSlimes.LemonPhaseDetached", false);
            }
        }

        /// <summary>Phasing look of the lemons on a tree: scanline shell once ripe, and clean vanish if detached from tree.</summary>
        internal static void PhaseTick(PhaseLemonTree t)
        {
            float now = Time.time;
            if (now >= t.NextScan)
            {
                t.NextScan = now + 0.4f;
                foreach (var l in t.Lemons) l.Seen = false;
                var joints = t.Sr != null ? t.Sr.SpawnJoints : null;
                if (joints != null)
                    foreach (var j in joints)
                    {
                        var rb = j != null ? j.connectedBody : null;
                        if (rb == null) continue;
                        var go = rb.gameObject;
                        var l = t.Lemons.Find(x => x.Ptr == go.Pointer && x.Go != null);
                        if (l == null)
                        {
                            var ida = go.GetComponent<IdentifiableActor>();
                            if (ida == null || ida.identType != _phase) continue;
                            l = MakePhaseLook(go);
                            if (l == null) continue;
                            t.Lemons.Add(l);
                        }
                        l.Seen = true;
                        bool ripe = true;
                        if (l.Rc != null) { try { ripe = l.Rc.GetState() == ResourceCycle.State.RIPE; } catch { } }
                        if (ripe != l.Ripe)
                        {
                            l.Ripe = ripe;
                            if (l.HoloGo != null) l.HoloGo.SetActive(ripe);
                            if (l.Solid != null) l.Solid.enabled = !ripe;
                            l.On = !ripe;
                        }
                    }
                for (int i = t.Lemons.Count - 1; i >= 0; i--)
                {
                    var l = t.Lemons[i];
                    if (l.Seen) continue;
                    // Detached outside OnPhaseTrigger (garden demolish / crop clear / DELUXE_GARDEN upgrade / expired): vanish cleanly instead of leaving unvaccable solid lemons on the plot.
                    if (l.Go != null && !_swappingPhaseLemon)
                    {
                        var pos = l.Go.transform.position;
                        if (_phaseOutFx != null) PlayFx(_phaseOutFx, pos);
                        CoopCompat.DestroySharedActor(l.Go, "ClassicSlimes.LemonPhaseDetached", false);
                    }
                    else if (l.Go != null && l.Solid != null)
                    {
                        l.Solid.enabled = true;
                        if (l.HoloGo != null) l.HoloGo.SetActive(false);
                    }
                    t.Lemons.RemoveAt(i);
                }
            }
            foreach (var l in t.Lemons)
            {
                if (!l.Ripe || now < l.Next || l.Solid == null) continue;
                l.On = !l.On;
                l.Next = now + (l.On ? U.Rand(0.05f, 0.16f) : U.Rand(0.35f, 1.4f));
                l.Solid.enabled = l.On;
            }
            if (_phaseHolo != null && Time.frameCount != _holoFrame)
            {
                _holoFrame = Time.frameCount;
                if (now >= _holoJump) { _holoJump = now + U.Rand(0.08f, 0.3f); _holoOfs = new Vector2(U.Rand(0f, 1f), _holoOfs.y + U.Rand(-0.3f, 0.3f)); }
                _phaseHolo.SetTextureOffset("_UnlitColorMap", new Vector2(_holoOfs.x, _holoOfs.y - now * 0.6f));
            }
        }

        /// <summary>lemontree [ripen|hit]: state of the nearest lemon tree; ripen its lemons / drop a pogo fruit into its crown.</summary>
        public static string LemonTree(string arg)
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            SpawnResource best = null; float bd = float.MaxValue;
            foreach (var s in Object.FindObjectsOfType<SpawnResource>())
            {
                var d = s != null ? s._resourceGrowerDefinition : null;
                if (d == null || ((d._primaryResourceType == null || d._primaryResourceType.name != "LemonFruit") && s.transform.Find(PhasorName) == null)) continue;
                float dd = (s.transform.position - cam.transform.position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = s; }
            }
            if (best == null) return $"no lemon tree loaded (phase={(_phase != null ? _phase.name : "null")} trees={PhaseTrees})";
            var def = best._resourceGrowerDefinition;
            var tree = best.GetComponentInChildren<PhaseLemonTree>();
            var sb = new System.Text.StringBuilder();
            sb.Append($"lemon tree {DiagX.Path(best.transform)} d={Mathf.Sqrt(bd):0.0} def={def.name} res=");
            if (def._resources != null)
                foreach (var e in def._resources) sb.Append(e?.ResourceIdentifiableType != null ? e.ResourceIdentifiableType.name + ":" + e.Weight + " " : "null ");
            sb.Append($"| phasor={(tree != null ? "layer " + tree.gameObject.layer + " lemons " + tree.Lemons.Count : "NO")} trees={PhaseTrees} triggers={PhaseTriggers} hits={PhaseHits} holo={_phaseHolo != null} fxIn={(_phaseInFx != null ? _phaseInFx.name : "null")} fxOut={(_phaseOutFx != null ? _phaseOutFx.name : "null")}");
            int i = 0, ripened = 0;
            var joints = best.SpawnJoints;
            if (joints != null)
                foreach (var j in joints)
                {
                    i++;
                    var body = j != null ? j.connectedBody : null;
                    if (body == null) continue;
                    var ida = body.GetComponent<IdentifiableActor>();
                    var rc = body.GetComponent<ResourceCycle>();
                    string st = "?";
                    try { st = rc != null ? rc.GetState().ToString() : "no rc"; } catch { }
                    if (arg == "ripen" && rc != null)
                        try { if (rc.GetState() == ResourceCycle.State.UNRIPE) { rc.Ripen(); ripened++; } } catch (Exception e) { st += " ripenEx:" + e.Message; }
                    sb.Append($" | j{i} {(ida != null && ida.identType != null ? ida.identType.name : body.name)} {st}");
                }
            if (arg == "ripen") sb.Append($" | ripened {ripened}");
            if (arg == "hit" && tree != null)
            {
                var pogo = Ident("PogoFruit");
                var go = pogo != null ? SpawnActor(pogo, tree.transform.position + Vector3.up * 0.5f, Quaternion.identity) : null;
                sb.Append($" | hit: spawned {(go != null ? go.name : "nothing")} in the crown");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ gardens (dev)
        static GardenCatcher NearestGarden(Vector3 p, out float dist)
        {
            GardenCatcher best = null; float bd = float.MaxValue;
            foreach (var g in Object.FindObjectsOfType<GardenCatcher>())
            {
                if (g == null) continue;
                float d = (g.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = g; }
            }
            dist = Mathf.Sqrt(bd);
            return best;
        }

        static IdentifiableType Ident(string name)
            => All.FirstOrDefault(f => f.Ident.Equals(name, StringComparison.OrdinalIgnoreCase))?.Type
               ?? Resources.FindObjectsOfTypeAll<IdentifiableType>().FirstOrDefault(x => x != null && x.name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static string GardenInfo()
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            var g = NearestGarden(cam.transform.position, out float dist);
            if (g == null) return "no garden catcher loaded";
            var sb = new System.Text.StringBuilder();
            sb.Append($"garden {DiagX.Path(g.transform)} d={dist:0.0} act={g.isActiveAndEnabled} plot={(g.Activator != null ? g.Activator.name : "null")} slots={g.Plantable?.Length} dict={g._plantableDict?.Count} fruitGroup={(g.FruitTypeGroup != null ? g.FruitTypeGroup.name : "null")}");
            var probe = new List<IdentifiableType>();
            foreach (var f in All) if (f.Type != null) probe.Add(f.Type);
            var carrot = Ident("CarrotVeggie"); if (carrot != null) probe.Add(carrot);
            var pogo = Ident("PogoFruit"); if (pogo != null) probe.Add(pogo);
            foreach (var t in probe)
            {
                bool inDict = false, can = false; string err = "";
                try { inDict = g._plantableDict != null && g._plantableDict.ContainsKey(t); } catch (Exception e) { err += " dictEx:" + e.Message; }
                try { can = g.CanAccept(t); } catch (Exception e) { err += " canEx:" + e.Message; }
                sb.Append($" | {t.name} dict={inDict} can={can} fruit={InGroup(g.FruitTypeGroup, t)}{err}");
            }
            return sb.ToString();
        }

        /// <summary>plant [ident]: plants straight into the nearest garden (bypasses the trigger).</summary>
        public static string DevPlant(string name)
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            var t = Ident(name);
            if (t == null) return "unknown ident " + name;
            var g = NearestGarden(cam.transform.position, out float dist);
            if (g == null) return "no garden catcher loaded";
            var go = g.Plant(t, false);
            if (go != null) FixLemon(go, true);
            return $"plant {t.name} in {DiagX.Path(g.transform)} (d={dist:0.0}) -> {(go != null ? go.name : "null")}";
        }

        /// <summary>Largo diets too (thousands of definitions, done once after the save is loaded).</summary>
        public static void RefreshLargoDiets()
        {
            if (!Ready) return;
            var sw = Stopwatch.StartNew();
            int n = 0;
            foreach (var d in GameContext.Instance.SlimeDefinitions.Slimes)
            {
                if (d == null || !d.IsLargo) continue;
                try { d.RefreshEatmap(); n++; } catch { }
            }
            ClassicSlimesMod.Log.Msg($"Refreshed {n} largo diets in {sw.ElapsedMilliseconds} ms");
        }

        // ------------------------------------------------------------------ wild patches / trees
        static readonly Dictionary<(IntPtr, string), ResourceGrowerDefinition> _clones = new();
        static readonly HashSet<IntPtr> _mine = new();
        public static readonly Dictionary<string, int> Added = new();

        static bool InGroup(IdentifiableTypeGroup g, IdentifiableType t) { try { return g != null && t != null && g.IsMember(t); } catch { return false; } }

        internal static void OnSpawnResourceAwakePrefix(SpawnResource sr)
        {
            if (!Ready || sr == null) return;
            var def = sr._resourceGrowerDefinition;
            if (def == null) return;
            if (def._primaryResourceType != null && def._primaryResourceType.name == "LemonFruit" && _phase != null && Cfg.PhaseLemonTrees.Value)
            {
                try { PhaseGrower(def); }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon grower: " + e.Message); }
            }
            if (!Cfg.WildFoods.Value) return;
            if (_mine.Contains(def.Pointer)) return;
            string scene = sr.gameObject.scene.name?.ToLowerInvariant() ?? "";
            if (sr.GetComponentInParent<LandPlot>() != null) return;
            var res = def._resources;
            if (res == null || res.Length == 0) return;
            bool veg = false, fruit = false, has = false;
            foreach (var e in res)
            {
                if (e?.ResourceIdentifiableType == null) continue;
                var t = e.ResourceIdentifiableType;
                if (All.Any(f => f.Type == t) || t == _phase) has = true;
                if (InGroup(Library.veggies, t)) veg = true;
                if (InGroup(Library.fruits, t)) fruit = true;
            }
            if (has || (!veg && !fruit) || (def.name ?? "").IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0) return;
            // SR1-like fixed spots: a share of the zone's wild patches / trees grows one SR1 food only (stable per spot)
            string spot = scene + "|" + Spawns.PosKey(sr.transform.position);
            float share = Mathf.Clamp01(Cfg.WildFoodShare.Value);
            FoodSpec pick = null;
            foreach (var f in All)
            {
                if (f.Type == null || f.Fruit != fruit || (!f.Fruit && !veg)) continue;
                if (!f.Scenes.Any(z => scene.Contains(z))) continue;
                if (Spawns.Hash01(f.Ident + "|" + spot) < share) { pick = f; break; }
            }
            if (pick == null) return;
            bool phase = pick.Ident == "LemonFruit" && _phase != null && Cfg.PhaseLemonTrees.Value;
            var grow = phase ? _phase : pick.Type;
            if (!_clones.TryGetValue((def.Pointer, grow.name), out var clone) || clone == null)
            {
                clone = Object.Instantiate(def);
                clone.name = def.name + "_" + grow.name;
                clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
                clone._primaryResourceType = pick.Type;
                clone._resources = Gc.Array(new[] { new Entry { ResourceIdentifiableType = grow, Weight = 1f, MinimumAmount = 0 } });
                _clones[(def.Pointer, grow.name)] = clone;
                _mine.Add(clone.Pointer);
            }
            sr._resourceGrowerDefinition = clone;
            string k = scene + ":" + pick.Ident;
            Added.TryGetValue(k, out int c); Added[k] = c + 1;
            Spots.Add((sr, pick.Ident));
            if (Diag.Enabled && _logged++ < 8) ClassicSlimesMod.Log.Msg($"[food] {scene}/{sr.name} {def.name} -> {grow.name}");
        }

        internal static void OnSpawnResourceAwakePost(SpawnResource sr)
        {
            if (!Ready || sr == null) return;
            var def = sr._resourceGrowerDefinition;
            if (def == null) return;
            if (def._primaryResourceType != null && def._primaryResourceType.name == "LemonFruit")
            {
                FixLemon(sr.gameObject, true);
                if (_phase != null && Cfg.PhaseLemonTrees.Value)
                {
                    try { PhaseGrower(def); AddPhasor(sr); }
                    catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon tree: " + e.Message); }
                }
            }
        }

        internal static bool IsLemonTreeGrower(ResourceGrowerDefinition def) =>
            def != null && def._primaryResourceType != null && def._primaryResourceType == _lemon;

        internal static void ConvertFastForwardGroups(SpawnResource sr, ref Il2CppSystem.Collections.Generic.IEnumerable<SpawnResource.GatherGroup> result)
        {
            if (sr == null || result == null || _phase == null || _lemon == null || !IsLemonTreeGrower(sr._resourceGrowerDefinition)) return;
            var list = new Il2CppSystem.Collections.Generic.List<SpawnResource.GatherGroup>();
            bool changed = false;
            var en = result.Cast<Il2CppSystem.Collections.IEnumerable>().GetEnumerator();
            while (en.MoveNext())
            {
                var g = en.Current != null ? en.Current.TryCast<SpawnResource.GatherGroup>() : null;
                if (g == null) continue;
                if (g.rid == _phase) { list.Add(new SpawnResource.GatherGroup(sr, _lemon, g.rcount)); changed = true; }
                else list.Add(g);
            }
            if (changed) result = list.Cast<Il2CppSystem.Collections.Generic.IEnumerable<SpawnResource.GatherGroup>>();
        }
        static int _logged;
        static readonly List<(SpawnResource sr, string food)> Spots = new();

        /// <summary>foodspots: loaded wild SR1 food patches / trees, nearest first.</summary>
        public static string FoodSpots()
        {
            var cam = Camera.main;
            var p = cam != null ? cam.transform.position : Vector3.zero;
            var live = new List<(float d, string s)>();
            foreach (var (sr, food) in Spots)
            {
                if (sr == null) continue;
                var q = sr.transform.position;
                live.Add(((q - p).magnitude, $"{food} {sr.gameObject.scene.name}/{sr.name} ({q.x:0},{q.y:0},{q.z:0})"));
            }
            live.Sort((a, b) => a.d.CompareTo(b.d));
            return $"food spots loaded={live.Count} share={Cfg.WildFoodShare.Value:0.##} [{string.Join(", ", Added.Select(kv => kv.Key + " x" + kv.Value))}]"
                + string.Join("", live.Take(15).Select(x => $" | {x.d:0} m {x.s}"));
        }
    }

    [HarmonyPatch(typeof(SpawnResource), nameof(SpawnResource.Awake))]
    static class SpawnResourceAwakePatch
    {
        static void Prefix(SpawnResource __instance)
        {
            try { Foods.OnSpawnResourceAwakePrefix(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("SpawnResource prefix: " + e.Message); }
        }

        static void Postfix(SpawnResource __instance)
        {
            try { Foods.OnSpawnResourceAwakePost(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("SpawnResource postfix: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SpawnResource), nameof(SpawnResource.DropFromJoints))]
    static class SpawnResourceDropFromJointsPatch
    {
        static void Prefix(SpawnResource __instance)
        {
            try { Foods.CleanupDetachedPhaseLemons(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("SpawnResource DropFromJoints: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SpawnResource), nameof(SpawnResource.GetFastForwardGroups))]
    static class SpawnResourceFastForwardPatch
    {
        static void Postfix(SpawnResource __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<SpawnResource.GatherGroup> __result)
        {
            try { Foods.ConvertFastForwardGroups(__instance, ref __result); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("SpawnResource GetFastForwardGroups: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.DataModel.GameModel), nameof(Il2CppMonomiPark.SlimeRancher.DataModel.GameModel.Awake))]
    static class GameModelPhaseLemonPatch
    {
        static void Postfix(Il2CppMonomiPark.SlimeRancher.DataModel.GameModel __instance)
        {
            try { Foods.ClearPhaseFromIgnoreOnLoad(__instance); }
            catch { }
        }
    }

    /// <summary>SR1 LemonPhasor on a lemon tree: trigger around the crown + the phasing look of the lemons it grows.</summary>
    public class PhaseLemonTree : MonoBehaviour
    {
        public PhaseLemonTree(IntPtr ptr) : base(ptr) { }

        internal SpawnResource Sr;
        internal readonly HashSet<IntPtr> Handled = new();
        internal readonly List<Foods.PhaseLemon> Lemons = new();
        internal float NextScan;
        bool _warned;

        void OnTriggerEnter(Collider col)
        {
            try { Foods.OnPhaseTrigger(this, col); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon trigger: " + e.Message); }
        }

        void OnTriggerStay(Collider col)
        {
            try { Foods.OnPhaseTrigger(this, col); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon stay: " + e.Message); }
        }

        void Update()
        {
            try { Foods.PhaseTick(this); }
            catch (Exception e) { if (!_warned) { _warned = true; ClassicSlimesMod.Log.Warning("phase lemon tick: " + e); } }
        }

        void LateUpdate() => Handled.Clear();
    }
}
