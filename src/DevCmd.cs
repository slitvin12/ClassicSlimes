using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// Developer hooks (only with UserData/ClassicSlimes/diag.enabled): write commands into
    /// UserData/ClassicSlimes/cmd.txt, they are executed on the main thread and the file is deleted.
    /// gallery | clear | qs [seconds] | glint | ghost | glitch | time hour | lineup [dist] [seconds] | kill
    /// locs | goto group|location [x y z] | census | hp
    /// </summary>
    public static class DevCmd
    {
        static float _next;
        static readonly List<(Rigidbody rb, float until)> _frozen = new();

        public static void Tick()
        {
            for (int i = _frozen.Count - 1; i >= 0; i--)
            {
                if (Time.time < _frozen[i].until) continue;
                try { if (_frozen[i].rb != null) _frozen[i].rb.isKinematic = false; } catch { }
                _frozen.RemoveAt(i);
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var path = Path.Combine(Diag.Dir, "cmd.txt");
            if (!File.Exists(path)) return;
            string[] lines;
            try { lines = File.ReadAllLines(path); File.Delete(path); } catch { return; }
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.Length == 0) continue;
                try { Run(l); ClassicSlimesMod.Log.Msg("[cmd] " + l); }
                catch (Exception e) { ClassicSlimesMod.Log.Error($"[cmd] {l}: {e}"); }
            }
        }

        static float F(string[] a, int i, float def) =>
            a.Length > i ? float.Parse(a[i], CultureInfo.InvariantCulture) : def;

        static void Run(string l)
        {
            var a = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (a[0].ToLowerInvariant())
            {
                case "gallery": Clear(); Gallery.Show(); break;
                case "clear": Clear(); break;
                case "qs":
                    foreach (var q in UnityEngine.Object.FindObjectsOfType<QuicksilverSlime>()) q.Overcharge(F(a, 1, 30f));
                    break;
                case "plex": Plex(F(a, 1, 25f)); break;
                case "glint":
                    foreach (var m in UnityEngine.Object.FindObjectsOfType<MosaicSlime>()) m.SpawnGlint(3f);
                    break;
                case "ghost":
                    foreach (var q in UnityEngine.Object.FindObjectsOfType<QuantumSlime>()) q.SpawnGhost(q.transform.position + new Vector3(U.Rand(-2f, 2f), 0.5f, U.Rand(-2f, 2f)));
                    break;
                case "glitch":
                    foreach (var g in UnityEngine.Object.FindObjectsOfType<GlitchSlime>()) g.ForceDisguise();
                    break;
                case "time":
                    var td = SceneContext.Instance.TimeDirector;
                    td.FastForwardTo(td.GetNextHour(F(a, 1, 12f)));
                    break;
                case "lineup": Lineup(F(a, 1, 4f), F(a, 2, 8f)); break;
                case "kill":
                    foreach (var go in AllClassic()) Destroyer.DestroyActor(go, "ClassicSlimes.DevCmd", true);
                    break;
                case "locs": Locs(); break;
                case "goto": Goto(a); break;
                case "census": Census(); break;
                case "hp":
                    var ps = SceneContext.Instance.PlayerState;
                    ClassicSlimesMod.Log.Msg($"[cmd] hp={ps.GetCurrHealth()}/{ps.GetMaxHealth()} rad={ps.GetCurrRad()}");
                    break;
                default: ClassicSlimesMod.Log.Warning("[cmd] unknown: " + l); break;
            }
        }

        static List<GameObject> AllClassic()
        {
            var r = new List<GameObject>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<RadSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<QuantumSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<MosaicSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<QuicksilverSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<GlitchSlime>()) r.Add(c.gameObject);
            return r;
        }

        static GameObject Nearest<T>(Vector3 p) where T : Component
        {
            GameObject best = null; float bd = float.MaxValue;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<T>())
            {
                float d = (c.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c.gameObject; }
            }
            return best;
        }

        /// <summary>Places the nearest slime of every classic type in a row in front of the camera and freezes them.</summary>
        static void Lineup(float dist, float seconds)
        {
            var cam = Camera.main; if (cam == null) return;
            var p = cam.transform.position;
            var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var list = new List<GameObject>
            {
                Nearest<RadSlime>(p), Nearest<QuantumSlime>(p), Nearest<MosaicSlime>(p),
                Nearest<QuicksilverSlime>(p), Nearest<GlitchSlime>(p)
            };
            list.RemoveAll(g => g == null);
            float spacing = 1.7f, start = -(list.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                var pos = p + fwd * dist + right * (start + i * spacing);
                if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                    pos = hit.point + Vector3.up * 0.55f;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; _frozen.Add((rb, Time.time + seconds)); }
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            }
        }

        static void L(string m) => ClassicSlimesMod.Log.Msg("[cmd] " + m);

        static void Locs()
        {
            L("current group=" + SystemContext.Instance.SceneLoader.CurrentSceneGroup?.name);
            foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                if (g != null && g.IsGameplay) L($"group {g.name} init={g.InitialPlayerPosition}");
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count; i++)
                {
                    var x = d[i];
                    L($"loc {d.name}/{x.name} {x.position} {x.sceneGroup?.name}");
                }
        }

        /// <summary>goto &lt;scene group or editor location&gt; [x y z]: native cross-region teleport.</summary>
        static void Goto(string[] a)
        {
            if (a.Length < 2) return;
            string q = a[1].Replace('_', ' ');
            SceneGroup group = null; Vector3 pos = default; bool havePos = false;
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count && group == null; i++)
                {
                    var x = d[i];
                    if (x.sceneGroup != null && string.Equals(x.name, q, StringComparison.OrdinalIgnoreCase))
                    { group = x.sceneGroup; pos = x.position; havePos = true; }
                }
            if (group == null)
                foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                    if (g != null && g.IsGameplay && g.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) { group = g; break; }
            if (group == null) { L("goto: not found " + q); return; }
            if (a.Length >= 5) { pos = new Vector3(F(a, 2, 0), F(a, 3, 0), F(a, 4, 0)); havePos = true; }
            if (!havePos) pos = group.InitialPlayerPosition;
            if (IPlayerController.TryGet(out var c)) { c.StopAbilitiesForTeleport(); c.ResetVelocity(false); }
            var loader = SystemContext.Instance.SceneLoader;
            var tp = SceneContext.Instance.Player.GetComponentInChildren<TeleportablePlayer>(true);
            L($"goto {group.name} {pos} from {loader.CurrentSceneGroup?.name}");
            if (loader.CurrentSceneGroup != group)
            {
                var prm = new SceneLoadingParameters
                {
                    TeleportPlayer = true, PauseGame = true,
                    TeleportPosition = new Il2CppSystem.Nullable<Vector3> { hasValue = true, value = pos },
                    TeleportRotation = new Il2CppSystem.Nullable<Vector3>(),
                    ReloadAllCoreScenes = false, ReloadAllNonCoreScenes = false
                };
                tp.PrepareForUnmanagedTeleport();
                loader.LoadSceneGroup(group, prm);
            }
            else tp.TeleportTo(pos, group, new Il2CppSystem.Nullable<Vector3>(), false);
        }

        // ---- natural spawn statistics (filled by SpawnLogPatch) ----
        static readonly SortedDictionary<string, int> _spawned = new();
        static int _spawnTotal;

        static string ClassicOf(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            foreach (var s in SlimeFactory.Specs)
                if (n.Contains(s.Name) && !(s.Name == "Rad" && n.Contains("Radiant"))) return s.Name;
            return null;
        }

        static string N(object o) => o is UnityEngine.Object u ? (u == null ? "DEAD" : "ok") : (o == null ? "NULL" : "ok");

        internal static string PlexInfo(Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer p)
        {
            var sb = new System.Text.StringBuilder();
            void F2(string k, Func<object> g) { try { sb.Append(k).Append('=').Append(N(g())).Append(' '); } catch (Exception e) { sb.Append(k).Append("=ERR(").Append(e.GetType().Name).Append(") "); } }
            void B(string k, Func<bool> g) { try { sb.Append(k).Append('=').Append(g() ? 1 : 0).Append(' '); } catch { sb.Append(k).Append("=ERR "); } }
            B("started", () => p._hasStarted);
            F2("meta", () => p._metadata); F2("curr", () => p._currBehavior); F2("vac", () => p._vacuumable);
            F2("col", () => p._ownCollider); F2("sph", () => p._ownSphereCollider); F2("tr", () => p._ownTransform);
            F2("body", () => p._body); F2("emo", () => p._emotions); F2("totem", () => p._colliderTotemLinker);
            F2("chick", () => p._chickenRandomMove); B("hasChick", () => p._hasChickenRandomMove);
            F2("leap", () => p._leapAwayFromPlayer); B("hasLeap", () => p._hasLeapAwayFromPlayer);
            F2("shadow", () => p._shadowSlimeScatter); B("hasShadow", () => p._hasShadowSlimeScatter);
            F2("flee", () => p._fleeThreats); B("hasFlee", () => p._hasFleeThreats);
            B("hasCol", () => p._hasCollider); B("hasSph", () => p._hasSphereCollider); B("blockers>0", () => p._behaviorBlockers > 0);
            try { var m = p._metadata; if (m != null) sb.Append("metaType=").Append(m.GetIl2CppType().Name).Append(' '); } catch { }
            try { var c = p._currBehavior; if (c != null) sb.Append("currType=").Append(c.GetIl2CppType().Name).Append(' '); } catch { }
            try { var arr = p._subbehaviors; int nn = 0; if (arr != null) foreach (var x in arr) if (x == null) nn++; sb.Append("list=").Append(arr == null ? -1 : arr.Length).Append(" dead=").Append(nn).Append(' '); } catch (Exception e) { sb.Append("list=ERR(" + e.GetType().Name + ") "); }
            sb.Append("subs=");
            foreach (var sbh in p.GetComponents<Il2CppMonomiPark.SlimeRancher.Subbehaviour>())
            {
                if (sbh == null) { sb.Append("<null>,"); continue; }
                sb.Append(sbh.GetIl2CppType().Name).Append(sbh.enabled ? "" : "(off)").Append(':');
                try { sb.Append(sbh.Relevancy(true).ToString("0.##")); }
                catch (Exception e) { var m = e.Message ?? ""; int i = m.IndexOf("at ", StringComparison.Ordinal); sb.Append("EXC[").Append(m.Length > 90 ? m.Substring(0, 90) : m).Append(']'); }
                sb.Append(',');
            }
            try { var best = p.GetBestBehaviour(); sb.Append(" best=").Append(best == null ? "NULL" : best.GetIl2CppType().Name); }
            catch (Exception e) { sb.Append(" best=EXC[").Append((e.Message ?? "").Length > 120 ? e.Message.Substring(0, 120) : e.Message).Append(']'); }
            return sb.ToString();
        }

        static void Plex(float radius)
        {
            var pl = SceneContext.Instance.Player.transform.position;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer>())
            {
                if (p == null || Vector3.Distance(pl, p.transform.position) > radius) continue;
                L($"plex {p.gameObject.name}: {PlexInfo(p)}");
            }
        }

        internal static void NoteSpawn(DirectedActorSpawner sp, GameObject obj, IdentifiableType id)
        {
            string n = id != null ? id.name : "null";
            _spawnTotal++;
            _spawned.TryGetValue(n, out int k); _spawned[n] = k + 1;
            if (ClassicOf(n) != null)
            {
                var pl = SceneContext.Instance?.Player;
                float dist = pl != null && obj != null ? Vector3.Distance(pl.transform.position, obj.transform.position) : -1f;
                L($"natural spawn {n} by {sp?.gameObject.scene.name}/{sp?.name} at {obj?.transform.position} dist={dist:0} (total spawns {_spawnTotal})");
            }
        }

        static void Census()
        {
            var pl = SceneContext.Instance.Player.transform.position;
            var byType = new SortedDictionary<string, (int n, float min)>();
            foreach (var ia in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = ia.identType; if (t == null || ClassicOf(t.name) == null) continue;
                float d = Vector3.Distance(pl, ia.transform.position);
                byType.TryGetValue(t.name, out var v);
                byType[t.name] = (v.n + 1, v.n == 0 ? d : Mathf.Min(v.min, d));
            }
            L($"census group={SystemContext.Instance.SceneLoader.CurrentSceneGroup?.name} player={pl}");
            foreach (var kv in byType) L($"  actor {kv.Key} x{kv.Value.n} nearest={kv.Value.min:0}m");
            var sp = new SortedDictionary<string, int>();
            int total = 0, active = 0, withClassic = 0;
            foreach (var s in Resources.FindObjectsOfTypeAll<DirectedSlimeSpawner>())
            {
                if (s == null || !s.gameObject.scene.IsValid() || s.Constraints == null) continue;
                total++; if (s.isActiveAndEnabled) active++;
                bool has = false;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && ClassicOf(m.IdentType.name) != null) has = true;
                if (has) withClassic++;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && ClassicOf(m.IdentType.name) != null)
                            { string key = s.gameObject.scene.name + ":" + m.IdentType.name; sp.TryGetValue(key, out int k); sp[key] = k + 1; }
            }
            L($"  spawners total={total} active={active} withClassic={withClassic}");
            int shown = 0, activeMod = 0, activeSlime = 0;
            foreach (var s in Resources.FindObjectsOfTypeAll<DirectedSlimeSpawner>())
            {
                if (s == null || !s.gameObject.scene.IsValid() || s.Constraints == null || !s.isActiveAndEnabled) continue;
                bool mod = Spawns.Modified.ContainsKey(s.GetInstanceID());
                if (mod) activeMod++;
                bool slimeSet = false;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && m.IdentType.TryCast<SlimeDefinition>() != null) slimeSet = true;
                if (slimeSet) activeSlime++;
                if (!slimeSet || shown >= 5) continue;
                shown++;
                var parts = new List<string>();
                foreach (var c in s.Constraints)
                {
                    var names = new List<string>();
                    if (c?.Slimeset?.Members != null) foreach (var m in c.Slimeset.Members) names.Add(m?.IdentType != null ? m.IdentType.name + ":" + m.Weight.ToString("0.##") : "null");
                    parts.Add((c != null && c.Feral ? "feral " : "") + string.Join(",", names));
                }
                L($"  spawner {s.GetIl2CppType().Name} {s.gameObject.scene.name}/{s.name} id={s.GetInstanceID()} touched={mod} -> {string.Join(" | ", parts)}");
            }
            L($"  active spawners with slime sets: {activeSlime}/{active}, touched by mod: {activeMod}; modified ids total {Spawns.Modified.Count}; re-added after reset: {Spawns.ReAdded}");
            foreach (var kv in sp) L($"  spawner-members {kv.Key} x{kv.Value}");
            foreach (var kv in Spawns.AddedPerScene) L($"  added-at-awake {kv.Key} x{kv.Value}");
            L($"  natural spawns since start: {_spawnTotal}");
            foreach (var kv in _spawned) if (ClassicOf(kv.Key) != null || kv.Value >= 1) L($"    {kv.Key} x{kv.Value}");
        }

        static void Clear()
        {
            for (int i = 0; i < 4; i++)
            {
                var g = GameObject.Find("ClassicSlimesGallery");
                if (g == null) break;
                g.name = "x"; UnityEngine.Object.Destroy(g);
            }
        }
    }

    [HarmonyPatch(typeof(DirectedSlimeSpawner), nameof(DirectedSlimeSpawner.OnActorSpawned))]
    static class SpawnLogPatch
    {
        static void Postfix(DirectedSlimeSpawner __instance, GameObject spawnedObj, IdentifiableType id)
        {
            try { if (Diag.Enabled) DevCmd.NoteSpawn(__instance, spawnedObj, id); } catch { }
        }
    }
}
