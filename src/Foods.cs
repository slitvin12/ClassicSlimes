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
            if (!g._memberTypes.Contains(t)) g._memberTypes.Add(t);
            try
            {
                var rt = g.GetRuntimeObject();
                if (rt != null && !rt.IsMember(t)) rt.AddMember(t);
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"group {g.name} runtime: {e.Message}"); }
        }

        /// <summary>Called before the slimes are created (their favourites are looked up in the food groups).</summary>
        public static void Setup()
        {
            if (!Cfg.EnableFoods.Value) { ClassicSlimesMod.Log.Msg("SR1 foods disabled in config"); return; }
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
                    var group = f.Fruit ? Library.fruits : Library.veggies;
                    AddTo(group, t);
                    if (t.groupType == null) t.groupType = group;
                    Library.MakeVaccable(t);
                    string shown = null;
                    try { shown = t.localizedName?.GetLocalizedString(); } catch { }
                    if (t.localizedName == null || string.IsNullOrEmpty(shown) || shown.StartsWith("No translation", StringComparison.OrdinalIgnoreCase) || shown == f.Key)
                        t.localizedName = Library.CreateLocalizedString(f.Key);
                    f.Scenes = Library.GetSceneNamesFromSpawnerZones(f.Zones).Select(x => x.ToLowerInvariant()).ToArray();
                    ClassicSlimesMod.Log.Msg($"  food {t.name} ref={refId} group={group?.name} name='{shown}' wild=[{string.Join(",", f.Scenes)}]");
                }
                catch (Exception e) { ClassicSlimesMod.Log.Error($"Food {f.Ident}: {e}"); }
            }
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
            try { SetupPhaseLemon(byName); } catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon tree: " + e); }
        }

        // ------------------------------------------------------------------ phase lemon model
        // SR2 still has the lemon fruit, its tree and the phaseLemon material, but the fruit mesh itself (SR1
        // model_phaseLemon) is gone: model_pogofruit of fruitLemon and the tree spawn joints have empty MeshFilters, so only
        // the glow of the lemon was visible. The SR1 mesh uses the same UV layout as SR2's alb_frt_lemon / msk_frt_lemon.
        public static int LemonMeshes;
        static Material _lemonMat;

        static bool LemonMat(Material m) => m != null && (m.name ?? "").StartsWith("phaseLemon", StringComparison.OrdinalIgnoreCase);

        /// <summary>Empty lemon MeshFilters under root get the SR1 mesh (all = any empty renderer, e.g. tree spawn joints).</summary>
        internal static int FixLemon(GameObject root, bool all)
        {
            if (root == null || !Cfg.EnableFoods.Value) return 0;
            var mesh = Res.Mesh("phaseLemon");
            if (mesh == null) return 0;
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh != null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var mat = mr.sharedMaterial;
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

        public class PhaseLemon { public GameObject Go; public IntPtr Ptr; public MeshRenderer Solid; public float Next; public bool On, Seen; }

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
            if (rc != null) _phaseOutFx = rc.DestroyFX;
            FixLemon(p.prefab, false);
            // solid body = the real lemon look (PhaseLemonTree flickers it and adds a scanline shell)
            foreach (var mr in p.prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (mr != null && _lemonMat != null && LemonMat(mr.sharedMaterial)) mr.sharedMaterial = _lemonMat;
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

        static void AddPhasor(SpawnResource sr)
        {
            if (sr.transform.Find(PhasorName) != null) return;
            var go = new GameObject(PhasorName);
            int fruitLayer = _lemon.prefab != null ? _lemon.prefab.layer : 0;
            // Ignore Raycast keeps the crown trigger out of vac / interaction raycasts
            go.layer = Physics.GetIgnoreLayerCollision(2, fruitLayer) ? sr.gameObject.layer : 2;
            go.transform.SetParent(sr.transform, false);
            Vector3 c = new Vector3(0f, 3.0f, 0f);
            float r = 2.4f;
            var joints = sr.SpawnJoints;
            if (joints != null && joints.Length > 0)
            {
                var pts = new List<Vector3>();
                foreach (var j in joints) if (j != null) pts.Add(sr.transform.InverseTransformPoint(j.transform.position));
                if (pts.Count > 0)
                {
                    var sum = Vector3.zero;
                    foreach (var q in pts) sum += q;
                    c = sum / pts.Count;
                    float m = 0f;
                    foreach (var q in pts) m = Mathf.Max(m, (q - c).magnitude);
                    r = Mathf.Clamp(m + 0.9f, 1.5f, 6f);
                }
            }
            go.transform.localPosition = c;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = r;
            var t = go.AddComponent<PhaseLemonTree>();
            t.Sr = sr;
            if (!_phaseInTried) { _phaseInTried = true; try { _phaseInFx = Fx.Kept("FX LemonPhaseIn"); } catch { } }
            PhaseTrees++;
        }

        internal static GameObject SpawnActor(IdentifiableType t, Vector3 pos, Quaternion rot)
        {
            var sc = SRSingleton<SceneContext>.Instance;
            var group = sc.RegionRegistry.CurrentSceneGroup;
            Il2CppMonomiPark.SlimeRancher.DataModel.ActorModel model = null;
            try { model = sc.GameModel.InstantiateActorModel(t, group, pos, rot, false); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"InstantiateActorModel({t.name}): {e.Message}"); }
            if (model != null) return InstantiationHelpers.InstantiateActorFromModel(model);
            return InstantiationHelpers.InstantiateActor(t.prefab, group, pos, rot, false, SlimeAppearance.AppearanceSaveSet.NONE,
                SlimeAppearance.AppearanceSaveSet.NONE, new Il2CppSystem.Nullable<Il2CppMonomiPark.SlimeRancher.Player.AmmoSlot.AmmoMetadata>(), false, false);
        }

        static void PlayFx(GameObject fx, Vector3 pos)
        {
            if (fx == null) return;
            try { FXHelpers.SpawnAndPlayFX(fx, pos, Quaternion.identity); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon fx: " + e.Message); }
        }

        /// <summary>SR1 LemonPhasor.OnTriggerEnter: a thrown fruit (not a lemon) knocks a ripe phasing lemon loose.</summary>
        internal static void OnPhaseTrigger(PhaseLemonTree tree, Collider col)
        {
            if (col == null || col.isTrigger || tree == null || tree.Sr == null || _lemon == null) return;
            var rb = col.attachedRigidbody;
            var go = rb != null ? rb.gameObject : col.gameObject;
            var ida = go.GetComponent<IdentifiableActor>();
            var t = ida != null ? ida.identType : null;
            if (t == null || t == _lemon || t == _phase || !InGroup(Library.fruits, t)) return;
            if (!tree.Handled.Add(go.Pointer)) return;
            PhaseTriggers++;
            var vac = go.GetComponent<Il2CppMonomiPark.SlimeRancher.Vacuumable>();
            if (vac != null && vac.IsHeld()) return;
            var joint = tree.Sr.PickRipeResourceJoint();
            var body = joint != null ? joint.connectedBody : null;
            if (Diag.Enabled && _phaseLog++ < 12)
                ClassicSlimesMod.Log.Msg($"[phaselemon] {t.name} layer={go.layer} hit {tree.Sr.name}: ripe joint={(joint != null ? joint.name : "none")} body={(body != null ? body.name : "-")}");
            if (body == null) return;
            Vector3 pos = joint.transform.position;
            Quaternion rot = joint.transform.rotation;
            Vector3 fruitPos = go.transform.position;
            Destroyer.DestroyActor(body.gameObject, "ClassicSlimes.LemonPhasor", false);
            var lemon = SpawnActor(_lemon, pos, rot);
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
            Destroyer.DestroyActor(go, "ClassicSlimes.LemonPhasor", false);
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
                return _phaseHolo;
            }
        }

        static PhaseLemon MakePhaseLook(GameObject go)
        {
            MeshRenderer solid = null; MeshFilter mf = null;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.name == PhaseHoloName) continue;
                var m = mr.sharedMaterial;
                if (m == null || (m != _lemonMat && !LemonMat(m))) continue;
                var f = mr.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                solid = mr; mf = f; break;
            }
            if (solid == null) return null;
            var holo = PhaseHoloMat;
            if (holo != null && solid.transform.Find(PhaseHoloName) == null)
            {
                var h = new GameObject(PhaseHoloName);
                h.layer = solid.gameObject.layer;
                h.transform.SetParent(solid.transform, false);
                h.transform.localScale = Vector3.one * 1.06f;
                h.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var hr = h.AddComponent<MeshRenderer>();
                hr.sharedMaterial = holo;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hr.receiveShadows = false;
            }
            solid.enabled = false;
            return new PhaseLemon { Go = go, Ptr = go.Pointer, Solid = solid, Next = Time.time + U.Rand(0.1f, 0.8f) };
        }

        static int _holoFrame;
        static float _holoJump;
        static Vector2 _holoOfs;

        /// <summary>Phasing look of the lemons on a tree: scanline shell, the solid body only flickers in now and then.</summary>
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
                    }
                for (int i = t.Lemons.Count - 1; i >= 0; i--)
                {
                    var l = t.Lemons[i];
                    if (l.Seen) continue;
                    if (l.Go != null && l.Solid != null) l.Solid.enabled = true;
                    t.Lemons.RemoveAt(i);
                }
            }
            foreach (var l in t.Lemons)
            {
                if (now < l.Next || l.Solid == null) continue;
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

        internal static void OnSpawnResourceAwake(SpawnResource sr)
        {
            if (!Ready || sr == null) return;
            var def = sr._resourceGrowerDefinition;
            if (def == null) return;
            // garden / wild lemon trees: their fruit spawn joints have no mesh either
            if (def._primaryResourceType != null && def._primaryResourceType.name == "LemonFruit")
            {
                FixLemon(sr.gameObject, true);
                if (_phase != null && Cfg.PhaseLemonTrees.Value)
                {
                    try { PhaseGrower(def); AddPhasor(sr); }
                    catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon tree: " + e.Message); }
                }
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
                clone._resources = new Il2CppReferenceArray<Entry>(new[] { new Entry { ResourceIdentifiableType = grow, Weight = 1f, MinimumAmount = 0 } });
                _clones[(def.Pointer, grow.name)] = clone;
                _mine.Add(clone.Pointer);
            }
            sr._resourceGrowerDefinition = clone;
            if (phase) { try { AddPhasor(sr); } catch (Exception e) { ClassicSlimesMod.Log.Warning("phase lemon tree: " + e.Message); } }
            string k = scene + ":" + pick.Ident;
            Added.TryGetValue(k, out int c); Added[k] = c + 1;
            Spots.Add((sr, pick.Ident));
            if (Diag.Enabled && _logged++ < 8) ClassicSlimesMod.Log.Msg($"[food] {scene}/{sr.name} {def.name} -> {grow.name}");
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
            try { Foods.OnSpawnResourceAwake(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("SpawnResource patch: " + e.Message); }
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

        void Update()
        {
            try { Foods.PhaseTick(this); }
            catch (Exception e) { if (!_warned) { _warned = true; ClassicSlimesMod.Log.Warning("phase lemon tick: " + e); } }
        }

        void LateUpdate() => Handled.Clear();
    }
}
