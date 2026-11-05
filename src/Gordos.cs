using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CottonLibrary;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// SR1 gordos of the classic slimes: SR1 had a Rad, a Quantum and a Mosaic gordo (50 foods each). Here each one sits
    /// at a fixed spot of its slime's zone. The gordo is a copy of SR2's Pink Gordo (CottonForge CreateGordoType /
    /// CreateGordoObject) with the slime's body material and face; it eats the slime's diet and bursts into slimes of its
    /// type plus rewards. Every placed gordo gets a fixed id (IdHandler._id) before GordoEat.Awake registers it, so SR2
    /// saves its eaten count and the burst like a vanilla gordo.
    /// </summary>
    public static class Gordos
    {
        public class Spot
        {
            public string Slime, Id;
            /// <summary>Name prefix of the zone scene the gordo belongs to (it is moved into it and unloads with it).</summary>
            public string Scene;
            public Vector3 Pos;
            public float Yaw;
        }

        public const string IdPrefix = "gordoClassic";
        public static readonly string[] Slimes = { "Rad", "Quantum", "Mosaic" };
        public static readonly Dictionary<string, IdentifiableType> Types = new();

        /// <summary>Fixed spots (found with gordospots + gordoat): flat dry ground among the slime's home spawners.</summary>
        public static readonly List<Spot> Spots = new()
        {
            // Starlight Strand: the blue-grass meadow by the path, ~100 m from the conservatory gate, among rad homes
            new Spot { Slime = "Rad", Id = IdPrefix + "Rad", Scene = "zoneStrand", Pos = new Vector3(18.3f, 1.5f, -218.3f), Yaw = 135f },
            // Powderfall Bluffs: the snowy hollow with the glowing ice blocks and a fruit tree, among quantum homes
            new Spot { Slime = "Quantum", Id = IdPrefix + "Quantum", Scene = "zoneBluffs_Area1", Pos = new Vector3(-843.5f, 44.3f, 1459.0f), Yaw = 175f },
            // Ember Valley: the sandstone plateau between the rock pillars above the tidepool cave, among mosaic homes
            new Spot { Slime = "Mosaic", Id = IdPrefix + "Mosaic", Scene = "zoneGorge_Area3", Pos = new Vector3(-319.2f, 35.0f, 554.3f), Yaw = 141f },
        };

        /// <summary>Burst reward: the zone's crate, like the vanilla gordos there (SR1: 2 zone crates + a science crate).</summary>
        static readonly Dictionary<string, string> Crates = new()
        {
            ["Rad"] = "ContainerStrand01", ["Quantum"] = "ContainerBluffs01", ["Mosaic"] = "ContainerGorge01",
        };

        static readonly Dictionary<string, GameObject> _live = new();
        static readonly List<GameObject> _tests = new();
        static int _testN;
        const string TestPrefix = IdPrefix + "Test";

        static MelonLogger.Instance Log => ClassicSlimesMod.Log;

        // ---------------------------------------------------------------- types (main menu, after the slimes)

        /// <summary>
        /// The types are created even with ClassicGordos off (only the placement is skipped): a save that has their gordo
        /// models must still resolve the type ids.
        /// </summary>
        public static void CreateAll()
        {
            IdentifiableType pink = null;
            try { pink = Library.Get<IdentifiableType>("PinkGordo"); } catch { }
            if (pink == null || pink.prefab == null) { Log.Warning("Gordos: PinkGordo prefab not found"); return; }
            foreach (var n in Slimes)
            {
                var s = SlimeFactory.Spec(n);
                if (s?.Def == null) continue;
                try { Create(s, pink); }
                catch (Exception e) { Log.Error($"Creating the {n} gordo failed: {e}"); }
            }
        }

        static Material FaceMat(List<(SlimeFace.SlimeExpression, Material, Material)> faces, SlimeFace.SlimeExpression x, bool eyes)
        {
            foreach (var (ex, e, m) in faces)
                if (ex == x) return eyes ? e : m;
            return null;
        }

        static void Create(SlimeSpec s, IdentifiableType pink)
        {
            var app = s.Def.AppearancesDefault[0];
            var icon = Res.Sprite("iconGordo" + s.Name) ?? app._icon;
            var type = Library.CreateGordoType("Classic" + s.Name, icon, Library.CreateLocalizedString("l." + s.Key + "_gordo"),
                "IdentifiableType.Classic" + s.Name + "Gordo");
            // SlimeFace.ExpressionFaces is a value-type array that Il2CppInterop mis-exposes: read it raw (Faces)
            var f = app._face != null ? Faces.Read(app._face) : new List<(SlimeFace.SlimeExpression, Material, Material)>();
            // SR1 gordo face: blink eyes + happy mouth at rest, scared eyes / stuffed mouth while straining, elated when fed
            var fd = new Library.GordoFaceData
            {
                eyesBlink = FaceMat(f, SlimeFace.SlimeExpression.BLINK, true),
                eyesNormal = FaceMat(f, SlimeFace.SlimeExpression.SCARED, true),
                mouthHappy = FaceMat(f, SlimeFace.SlimeExpression.HAPPY, false),
                mouthChompOpen = FaceMat(f, SlimeFace.SlimeExpression.ELATED, false),
                mouthEating = FaceMat(f, SlimeFace.SlimeExpression.CHOMP_CLOSED, false),
            };
            Material body = null;
            try { body = app.Structures[0].DefaultMaterials[0]; } catch { }
            var obj = Library.CreateGordoObject(pink.prefab, type, s.Def, fd, body);
            obj.name = "gordo" + s.Name;
            var eat = obj.GetComponent<GordoEat>();
            eat.TargetCount = Math.Max(1, Cfg.GordoFood.Value);
            try { eat._id = ""; } catch { }
            // the burst spawns GordoRewardsBase._slimeDefinition (CreateGordoObject only sets GordoEat's)
            var rw = obj.GetComponent<GordoRewardsBase>();
            if (rw != null) rw._slimeDefinition = s.Def;
            string crate = "-";
            if (Crates.TryGetValue(s.Name, out var cn))
                try
                {
                    var ct = Library.Get<IdentifiableType>(cn);
                    var gr = obj.GetComponent<GordoRewards>();
                    if (ct != null && ct.prefab != null && gr != null)
                    {
                        gr.RewardPrefabs = new Il2CppReferenceArray<GameObject>(new[] { ct.prefab });
                        crate = ct.prefab.name;
                    }
                }
                catch (Exception e) { Log.Warning($"Gordo {s.Name} reward {cn}: {e.Message}"); }
            var smr = BodyRenderer(obj);
            if (smr != null)
            {
                var mats = smr.sharedMaterials;
                if (mats.Length > 0 && body != null) mats[0] = body;
                if (mats.Length > 1 && fd.eyesBlink != null) mats[1] = fd.eyesBlink;
                if (mats.Length > 2 && fd.mouthHappy != null) mats[2] = fd.mouthHappy;
                smr.sharedMaterials = mats;
            }
            Types[s.Name] = type;
            Log.Msg($"Created {s.Name} gordo: {eat.TargetCount} foods, body {(body != null ? body.name : "-")}, reward {crate}");
        }

        static SkinnedMeshRenderer BodyRenderer(GameObject go)
        {
            SkinnedMeshRenderer first = null;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == null) continue;
                if (r.name.StartsWith("slime_gordo", StringComparison.OrdinalIgnoreCase)) return r;
                first ??= r;
            }
            return first;
        }

        // ---------------------------------------------------------------- placement (zone scene loaded)

        public static void OnSceneLoaded(string scene)
        {
            if (Types.Count == 0 || Spots.Count == 0 || string.IsNullOrEmpty(scene) || !Cfg.ClassicGordos.Value) return;
            foreach (var sp in Spots)
            {
                if (!string.Equals(scene, sp.Scene, StringComparison.OrdinalIgnoreCase)) continue;
                try { Place(sp, SceneManager.GetSceneByName(scene)); }
                catch (Exception e) { Log.Error($"Placing gordo {sp.Id} failed: {e}"); }
            }
        }

        static void Place(Spot sp, Scene scene)
        {
            if (!Types.TryGetValue(sp.Slime, out var type)) return;
            if (_live.TryGetValue(sp.Id, out var old) && old != null) return;
            var go = Spawn(type, sp.Id, sp.Pos, sp.Yaw, scene);
            if (go == null) return;
            _live[sp.Id] = go;
            var eat = go.GetComponent<GordoEat>();
            int eaten = -2;
            try
            {
                var gm = SceneContext.Instance.GameModel;
                eaten = gm != null && gm.gordos.ContainsKey(sp.Id) ? gm.gordos[sp.Id].GordoEatenCount : eat.GetEatenCount();
            }
            catch { }
            var par = go.transform.parent;
            Log.Msg($"Gordo {sp.Id} placed in {scene.name}{(par != null ? "/" + DiagX.Path(par) : "")} at {sp.Pos} (eaten {eaten}/{eat.TargetCount})");
        }

        /// <summary>
        /// Instantiate the gordo inside an inactive holder that already lives in the zone scene: IdHandler._id is set
        /// before GordoEat.Awake runs (it registers the gordo model with that id), and the gordo unloads with the zone.
        /// </summary>
        static GameObject Spawn(IdentifiableType type, string id, Vector3 pos, float yaw, Scene scene)
        {
            var parent = CellParent(pos, scene);
            var holder = new GameObject("ClassicSlimesGordoSpawn");
            holder.SetActive(false);
            try
            {
                if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(holder, scene);
                var go = Object.Instantiate(type.prefab, holder.transform);
                go.name = type.prefab.name + (id != null ? " " + id : " (test)");
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                var eat = go.GetComponent<GordoEat>();
                if (eat != null) eat._id = id ?? "";
                go.transform.SetParent(parent, true);   // active now (or when the sector is) -> Awake registers the id
                var rb = go.GetComponent<GordoRewardsBase>();
                if (rb != null && rb._slimeDefinition == null && eat != null && eat.SlimeDefinition != null) rb._slimeDefinition = eat.SlimeDefinition;
                Extras(go, type);
                return go;
            }
            finally { Object.Destroy(holder); }
        }

        /// <summary>Vanilla gordos live in their cell: zoneX/cellY/Sector/Slimes. Smallest sector of the scene containing pos.</summary>
        static Transform CellParent(Vector3 pos, Scene scene)
        {
            SECTR_Sector best = null; float bv = float.MaxValue;
            try
            {
                foreach (var sec in Resources.FindObjectsOfTypeAll<SECTR_Sector>())
                {
                    if (sec == null || sec.gameObject.scene != scene) continue;
                    var b = sec.TotalBounds;
                    if (!b.Contains(pos)) continue;
                    float v = b.size.x * b.size.y * b.size.z;
                    if (v < bv) { bv = v; best = sec; }
                }
            }
            catch (Exception e) { Log.Warning("Gordo cell lookup: " + e.Message); }
            if (best == null) return null;
            var slimes = best.transform.Find("Slimes");
            return slimes != null ? slimes : best.transform;
        }

        static void Extras(GameObject go, IdentifiableType type)
        {
            var slime = Types.FirstOrDefault(kv => kv.Value.Pointer == type.Pointer).Key;
            try
            {
                // SR1 gordoRad: the radiation aura of a rad slime, scaled with the gordo (SR1: radius 3.2 x gordo scale 4)
                if (slime == "Rad" && go.GetComponent<RadSlime>() == null) go.AddComponent<RadSlime>().NoExpand = true;
            }
            catch (Exception e) { Log.Warning($"Gordo extras {slime}: {e.Message}"); }
        }

        // ---------------------------------------------------------------- dev commands

        static Vector3 CamPos()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        static bool InScene(GordoEat e) => e != null && e.gameObject.scene.IsValid() && e.gameObject.scene.name != "DontDestroyOnLoad";

        static GordoEat Nearest(bool inactive = false)
        {
            var p = CamPos();
            GordoEat best = null; float bd = float.MaxValue;
            foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
            {
                if (!InScene(e) || (!inactive && !e.gameObject.activeInHierarchy)) continue;
                float d = (e.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        static string Row(GordoEat e)
        {
            var t = e.transform; var q = t.position;
            string type = null, rw = "", pe = "-", rs = "-", key = "-";
            int eaten = -2, target = -2;
            try { type = e.GetComponent<GordoIdentifiable>()?.identType?.name; } catch { }
            try
            {
                var r = e.GetComponent<GordoRewards>();
                if (r != null && r.RewardPrefabs != null) rw = string.Join("+", r.RewardPrefabs.Select(x => x != null ? x.name : "null"));
                var rb = e.GetComponent<GordoRewardsBase>();
                if (rb != null)
                {
                    if (rb._slimeDefinition != null) rs = rb._slimeDefinition.name;
                    key = rb.HasKeyReward() + "/" + (rb.SlimeKeyIdentifiableType != null ? rb.SlimeKeyIdentifiableType.name : "-");
                }
            }
            catch { }
            try { eaten = e.GetEatenCount(); target = e.GetTargetCount(); } catch { }
            try { if (e._pediaEntry != null) pe = e._pediaEntry.name; } catch { }
            return $"{e.name} id={e._id} type={type} slime={(e.SlimeDefinition != null ? e.SlimeDefinition.name : "-")} {eaten}/{target} on={e.gameObject.activeInHierarchy} " +
                   $"scene={e.gameObject.scene.name} ({q.x:0.0},{q.y:0.0},{q.z:0.0}) yaw={t.eulerAngles.y:0} scale={t.lossyScale.x:0.##} " +
                   $"parent={(t.parent != null ? DiagX.Path(t.parent) : "-")} rewards=[{rw}] burst={rs} key={key} pedia={pe}";
        }

        /// <summary>gordos: every gordo of the loaded scenes, nearest first (full list in gordos.txt).</summary>
        public static string Info()
        {
            var p = CamPos();
            var rows = new List<(float d, string s, Vector3 at)>();
            foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
                if (InScene(e)) rows.Add(((e.transform.position - p).magnitude, Row(e), e.transform.position));
            rows.Sort((a, b) => a.d.CompareTo(b.d));
            if (rows.Count > 0) rows[0] = (rows[0].d, rows[0].s + " region " + RegionOf(rows[0].at) + " near [" + NearActors(rows[0].at, 10f) + "]", rows[0].at);
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).name);
            string models;
            try
            {
                int total = 0;
                var ours = new List<string>();
                foreach (var kv in SceneContext.Instance.GameModel.gordos)
                {
                    total++;
                    var m = kv.Value;
                    if (kv.Key == null || kv.Key.Length == 0 || kv.Key.StartsWith(IdPrefix))
                        ours.Add($"{kv.Key}={m.GordoEatenCount}/{m.targetCount}{(m.identifiableType != null ? " " + m.identifiableType.name : "")}{(m.gameObj != null ? "" : " noobj")}");
                }
                models = $"models {total}, ours [{string.Join(", ", ours)}]";
            }
            catch (Exception x) { models = "models ERR " + x.Message; }
            var head = $"gordos {rows.Count}; classic types [{string.Join(",", Types.Keys)}], spots {Spots.Count}, live {_live.Count(kv => kv.Value != null)}; " +
                       $"{models}; cam ({p.x:0},{p.y:0},{p.z:0}); scenes [{string.Join(",", scenes)}]";
            try { File.WriteAllText(Path.Combine(Diag.Dir, "gordos.txt"), head + "\n" + string.Join("\n", rows.Select(x => $"{x.d:0} m {x.s}"))); } catch { }
            return head + string.Join("", rows.Take(6).Select(x => $" | {x.d:0} m {x.s}"));
        }

        /// <summary>Region (cell) of the nearest gordo and the scene group it reports.</summary>
        static string RegionOf(Vector3 at)
        {
            try
            {
                GordoEat e = null; float bd = float.MaxValue;
                foreach (var g in Resources.FindObjectsOfTypeAll<GordoEat>())
                {
                    if (!InScene(g)) continue;
                    float d = (g.transform.position - at).sqrMagnitude;
                    if (d < bd) { bd = d; e = g; }
                }
                if (e == null) return "-";
                var reg = e.GetComponentInParent<Il2CppMonomiPark.SlimeRancher.Regions.Region>(true);
                var mem = e.GetComponent<Il2CppMonomiPark.SlimeRancher.Regions.RegionMember>();
                return $"{(reg != null ? reg.name + ":" + (reg.SceneGroup != null ? reg.SceneGroup.name : "nullSG") : "none")} member={(mem != null ? (mem.SceneGroup != null ? mem.SceneGroup.name : "nullSG") : "none")}";
            }
            catch (Exception x) { return "ERR " + x.Message; }
        }

        /// <summary>Actors (slimes, crates, foods...) within r of a point, by type: what a burst left behind.</summary>
        static string NearActors(Vector3 at, float r)
        {
            try
            {
                var counts = new SortedDictionary<string, int>();
                var gm = SceneContext.Instance.GameModel;
                foreach (var a in Object.FindObjectsOfType<Identifiable>())
                {
                    if (a == null || a.identType == null || (a.transform.position - at).magnitude > r) continue;
                    string sg;
                    try
                    {
                        var mdl = gm.GetIdentifiableModel(a.GetActorId());
                        sg = mdl == null ? "nomodel" : mdl.sceneGroup != null ? mdl.sceneGroup.name : "nullSG";
                    }
                    catch { sg = "err"; }
                    var n = a.identType.name + "@" + sg;
                    counts[n] = counts.TryGetValue(n, out var c) ? c + 1 : 1;
                }
                return string.Join(",", counts.Select(kv => kv.Value > 1 ? $"{kv.Key}x{kv.Value}" : kv.Key));
            }
            catch (Exception x) { return "ERR " + x.Message; }
        }

        /// <summary>gordotree [near|Rad|PinkGordo...]: hierarchy, components and materials -> gordotree.txt.</summary>
        public static string Tree(string what)
        {
            GameObject go = null;
            if (string.IsNullOrEmpty(what) || what == "near") go = Nearest(true)?.gameObject;
            else if (Types.TryGetValue(what, out var t)) go = t.prefab;
            else
            {
                foreach (var x in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                    if (x != null && string.Equals(x.name, what, StringComparison.OrdinalIgnoreCase)) { go = x.prefab; break; }
            }
            if (go == null) return "gordotree: not found " + what;
            var sb = new StringBuilder();
            Dump(sb, go.transform, 0);
            File.WriteAllText(Path.Combine(Diag.Dir, "gordotree.txt"), sb.ToString());
            return $"gordotree {go.name}: {sb.Length} chars -> gordotree.txt";
        }

        static void Dump(StringBuilder sb, Transform t, int depth)
        {
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetIl2CppType().Name;
                var r = c.TryCast<Renderer>();
                if (r != null)
                    n += "[" + string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name + "/" + (m.shader != null ? m.shader.name : "?") : "null")) + "]";
                comps.Add(n);
            }
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} act={t.gameObject.activeSelf} lp={t.localPosition} ls={t.localScale} layer={t.gameObject.layer} {string.Join(" ", comps)}");
            for (int i = 0; i < t.childCount; i++) Dump(sb, t.GetChild(i), depth + 1);
        }

        static Scene SceneAt(Collider c) => c != null ? c.gameObject.scene : SceneManager.GetActiveScene();

        static string Key(string slime) => Types.Keys.FirstOrDefault(k => string.Equals(k, slime, StringComparison.OrdinalIgnoreCase));

        /// <summary>gordohere &lt;Rad|Quantum|Mosaic&gt; [dist]: a test gordo on the ground ahead, facing the camera.</summary>
        public static string Here(string slime, float dist)
        {
            var cam = Camera.main;
            if (cam == null) return "gordohere: no camera";
            var f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            return At(slime, cam.transform.position + f * dist, Quaternion.LookRotation(-f).eulerAngles.y);
        }

        /// <summary>
        /// gordoat x y z [slime] [yaw]: a test gordo on the ground below x y z (default yaw: facing the camera). Test gordos
        /// get ids gordoClassicTestN (GordoEat needs a model); gordoclear removes them and their models.
        /// </summary>
        public static string At(string slime, Vector3 p, float yaw)
        {
            var key = Key(slime);
            if (key == null) return "gordoat: no gordo type " + slime;
            if (!Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 80f, U.GroundMask, QueryTriggerInteraction.Ignore)) return "gordoat: no ground";
            if (float.IsNaN(yaw))
            {
                var d = CamPos() - hit.point; d.y = 0f;
                yaw = d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d).eulerAngles.y : 0f;
            }
            var scene = SceneAt(hit.collider);
            var id = TestPrefix + DateTime.Now.ToString("HHmmss") + (++_testN);
            var go = Spawn(Types[key], id, hit.point, yaw, scene);
            if (go != null) _tests.Add(go);
            return $"gordo test {key} {id}: ({hit.point.x:0.0},{hit.point.y:0.0},{hit.point.z:0.0}) yaw {yaw:0} scene {scene.name} " +
                   $"ground {hit.collider.name} normal.y {hit.normal.y:0.00}";
        }

        static bool Flat(Vector3 c, float r, float tol)
        {
            for (int k = 0; k < 8; k++)
            {
                var q = c + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * r;
                if (!Physics.Raycast(q + Vector3.up * 3f, Vector3.down, out var h, 6f, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(h.point.y - c.y) > tol || h.normal.y < 0.8f) return false;
            }
            return true;
        }

        static bool Wet(Vector3 p)
        {
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * 0.6f, 2f, ~0, QueryTriggerInteraction.Collide))
                if (c != null && (c.gameObject.layer == 4 || c.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
                                  || c.name.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }

        /// <summary>
        /// gordospots &lt;slime&gt; [n]: candidate gordo spots around the slime's loaded home spawners - flat, dry, room for a
        /// 4 m gordo, 6+ m off the spawner; score = homes within 30 m. Each with a view point 11 m away -> gordospots.txt.
        /// </summary>
        public static string Candidates(string slime, int n)
        {
            var homes = Spawns.HomesOf(slime);
            if (homes.Count == 0) return "gordospots: no loaded homes of " + slime;
            var list = new List<(float score, Vector3 p, string info)>();
            var seen = new HashSet<string>();
            foreach (var (h, _) in homes)
                for (int r = 0; r < 2; r++)
                    for (int k = 0; k < 8; k++)
                    {
                        var q = h + Quaternion.Euler(0f, k * 45f + r * 22.5f, 0f) * Vector3.forward * (8f + r * 6f);
                        if (!Physics.Raycast(q + Vector3.up * 20f, Vector3.down, out var hit, 45f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                        var g = hit.point;
                        if (hit.normal.y < 0.94f || !seen.Add($"{Mathf.RoundToInt(g.x / 5f)},{Mathf.RoundToInt(g.z / 5f)}")) continue;
                        if (!Flat(g, 3f, 0.45f) || Wet(g)) continue;
                        if (Physics.CheckSphere(g + Vector3.up * 2.6f, 2.1f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                        float dMin = float.MaxValue; int near = 0; Vector3 nh = h;
                        foreach (var (x, _) in homes)
                        {
                            float d = (x - g).magnitude;
                            if (d < 30f) near++;
                            if (d < dMin) { dMin = d; nh = x; }
                        }
                        if (dMin < 6f) continue;
                        // view point: on the ground 11 m toward the nearest home, with a clear line of sight
                        var dir = nh - g; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                        string view = "-";
                        for (int t = 0; t < 8 && view == "-"; t++)
                        {
                            var vq = g + Quaternion.Euler(0f, t * 45f, 0f) * dir * 11f;
                            if (!Physics.Raycast(vq + Vector3.up * 8f, Vector3.down, out var vh, 20f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                            if (Physics.Linecast(vh.point + Vector3.up * 1.6f, g + Vector3.up * 2f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                            view = $"{vh.point.x:0.0} {vh.point.y + 0.2f:0.0} {vh.point.z:0.0}";
                        }
                        list.Add((near + hit.normal.y, g, $"homes30 {near} nearest {dMin:0} m scene {hit.collider.gameObject.scene.name} ground {hit.collider.name} view {view}"));
                    }
            list.Sort((a, b) => b.score.CompareTo(a.score));
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                sb.AppendLine($"#{i} {c.p.x:0.0} {c.p.y:0.0} {c.p.z:0.0} {c.info}");
            }
            File.WriteAllText(Path.Combine(Diag.Dir, "gordospots.txt"), sb.ToString());
            return $"gordospots {slime}: {list.Count} from {homes.Count} homes" + string.Join("", sb.ToString().Split('\n').Take(Math.Max(1, n)).Select(x => " | " + x.Trim()));
        }

        /// <summary>gordoclear: remove the test gordos and their gordo models (also ones restored from a save).</summary>
        public static string ClearTests()
        {
            int n = 0, m = 0;
            foreach (var g in _tests) if (g != null) { Object.Destroy(g); n++; }
            _tests.Clear();
            try
            {
                var model = SceneContext.Instance.GameModel;
                var ids = new List<string>();
                foreach (var kv in model.gordos) if (kv.Key != null && kv.Key.StartsWith(TestPrefix)) ids.Add(kv.Key);
                foreach (var id in ids) { model.DestroyGordoModel(id); m++; }
            }
            catch (Exception x) { Log.Warning("gordoclear: " + x.Message); }
            return $"gordoclear: {n} objects, {m} models";
        }

        /// <summary>gordofeed [left]: the nearest gordo needs only `left` more foods.</summary>
        public static string Feed(int left)
        {
            var e = Nearest();
            if (e == null) return "gordofeed: no gordo";
            try
            {
                int target = e.GetTargetCount();
                e.SetEatenCount(Math.Max(0, target - Math.Max(0, left)));
                return $"gordofeed {e.name}: eaten {e.GetEatenCount()}/{target}";
            }
            catch (Exception x) { return "gordofeed: " + x.Message; }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        /// <summary>gordofood [ident|-] [n]: drop foods into the mouth of the nearest gordo (default: first food of its diet).</summary>
        public static string Food(string ident, int n)
        {
            var e = Nearest();
            if (e == null) return "gordofood: no gordo";
            IdentifiableType t = null;
            if (!string.IsNullOrEmpty(ident) && ident != "-")
                foreach (var x in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                    if (x != null && string.Equals(x.name, ident, StringComparison.OrdinalIgnoreCase)) { t = x; break; }
            if (t == null)
                try { if (e._allEats != null) foreach (var x in e._allEats) if (x != null && x.prefab != null) { t = x; break; } } catch { }
            if (t == null || t.prefab == null) return "gordofood: no food " + ident;
            var trig = Find(e.transform, "EatTrigger");
            var pos = trig != null ? trig.position : e.transform.position + e.transform.forward * 2f + Vector3.up * 2f;
            int made = 0;
            for (int i = 0; i < Math.Max(1, n); i++)
                if (Foods.SpawnActor(t, pos + Vector3.up * (0.15f * i), Quaternion.identity) != null) made++;
            return $"gordofood {e.name}: {made} x {t.name} at ({pos.x:0.0},{pos.y:0.0},{pos.z:0.0})";
        }

        /// <summary>gordoreset [id]: un-burst our gordos (eaten 0, active again) for re-tests.</summary>
        public static string Reset(string id)
        {
            int n = 0;
            foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
            {
                if (!InScene(e) || string.IsNullOrEmpty(e._id) || !e._id.StartsWith(IdPrefix)) continue;
                if (id != null && e._id != id) continue;
                try
                {
                    e.SetEatenCount(0);
                    if (!e.gameObject.activeSelf) e.gameObject.SetActive(true);
                    e.GetComponent<GordoFaceAnimator>()?.SetDefaultState();
                    n++;
                }
                catch (Exception x) { Log.Warning("gordoreset: " + x.Message); }
            }
            return "gordoreset: " + n;
        }
    }
}
