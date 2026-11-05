using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// Adds the classic slimes to existing wild slime spawners of the chosen zones.
    /// The member weight is computed so that the slime gets roughly the configured share
    /// of each spawn constraint (Cotton's helper can create extra constraints instead).
    /// </summary>
    public static class Spawns
    {
        static readonly List<(SlimeSpec spec, string[] scenes)> _entries = new();
        public static readonly Dictionary<string, int> AddedPerScene = new();
        public static readonly Dictionary<int, string> Modified = new();

        public static void Register()
        {
            _entries.Clear();
            foreach (var s in SlimeFactory.Specs)
            {
                if (s.Def == null || s.Chance == null) continue;
                float p = Mathf.Clamp(s.Chance.Value, 0f, 0.9f);
                if (p <= 0f) continue;
                var scenes = Library.GetSceneNamesFromSpawnerZones(s.Zones).Select(x => x.ToLowerInvariant()).ToArray();
                _entries.Add((s, scenes));
                ClassicSlimesMod.Log.Msg($"  {s.Name} spawns in [{string.Join(",", scenes)}] share={p:0.##}");
            }
        }

        static readonly string[] Excluded = { "gold", "lucky", "tarr", "yolky" };

        static readonly HashSet<IntPtr> _touched = new();
        public static int ReAdded;

        /// <summary>Adds the classic slimes to the spawner's slime constraints; returns how many members were added (idempotent).</summary>
        static int Apply(DirectedActorSpawner spawner, string scene)
        {
            var constraints = spawner.Constraints;
            if (constraints == null) return 0;
            int added = 0;
            foreach (var (spec, scenes) in _entries)
            {
                if (!scenes.Any(z => scene.Contains(z))) continue;
                float p = Mathf.Clamp(spec.Chance.Value, 0f, 0.9f);
                if (Cfg.SpawnHomes.Value)
                {
                    // SR1-like fixed spots: only the slime's homes (a stable ~30 % of the zone's spawners) spawn it, but often
                    if (!IsHome(spawner, scene, spec)) continue;
                    p = Mathf.Clamp(p * 4f, 0.25f, 0.8f);
                }
                foreach (var c in constraints)
                {
                    if (c == null || c.Feral || c.Slimeset == null || c.Slimeset.Members == null) continue;
                    var members = c.Slimeset.Members;
                    if (members.Length == 0) continue;
                    float sum = 0f; bool skip = false, already = false, anySlime = false;
                    foreach (var m in members)
                    {
                        if (m == null || m.IdentType == null) continue;
                        string n = m.IdentType.name.ToLowerInvariant();
                        if (Excluded.Any(x => n.Contains(x))) skip = true;
                        if (m.IdentType == spec.Def) already = true;
                        if (m.IdentType.TryCast<SlimeDefinition>() != null) anySlime = true;
                        sum += m.Weight;
                    }
                    if (skip || already || !anySlime || sum <= 0f) continue;
                    var mem = new SlimeSet.Member { _prefab = spec.Def.prefab, IdentType = spec.Def, Weight = sum * p / (1f - p) };
                    var arr = new SlimeSet.Member[members.Length + 1];
                    for (int i = 0; i < members.Length; i++) arr[i] = members[i];
                    arr[members.Length] = mem;
                    c.Slimeset.Members = Gc.Array(arr);   // write-barrier safe (runs during gameplay)
                    added++;
                }
            }
            return added;
        }

        public const float HomeFraction = 0.3f;
        static readonly List<(DirectedActorSpawner sp, string scene, string names)> _homes = new();
        static readonly HashSet<IntPtr> _homeSet = new();

        /// <summary>Stable hash in [0,1) (string.GetHashCode is randomised per process).</summary>
        internal static float Hash01(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Spot key of a scene object (1 m grid).</summary>
        internal static string PosKey(Vector3 p) => $"{Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.y)},{Mathf.RoundToInt(p.z)}";

        static bool IsHome(DirectedActorSpawner sp, string scene, SlimeSpec spec)
            => Hash01(spec.Name + "|" + scene + "|" + PosKey(sp.transform.position)) < HomeFraction;

        /// <summary>homes: loaded home spawners of the classic slimes, nearest first.</summary>
        public static string HomesInfo()
        {
            var cam = Camera.main;
            var p = cam != null ? cam.transform.position : Vector3.zero;
            var live = new List<(float d, string s)>();
            var per = new Dictionary<string, int>();
            foreach (var h in _homes)
            {
                if (h.sp == null) continue;
                var q = h.sp.transform.position;
                live.Add(((q - p).magnitude, $"{h.names} {h.scene}/{h.sp.name} ({q.x:0},{q.y:0},{q.z:0})"));
                foreach (var n in h.names.Split('+')) { per.TryGetValue(h.scene + ":" + n, out int c); per[h.scene + ":" + n] = c + 1; }
            }
            live.Sort((a, b) => a.d.CompareTo(b.d));
            return $"homes on={Cfg.SpawnHomes.Value} loaded={live.Count} [{string.Join(", ", per.Select(kv => kv.Key + " x" + kv.Value))}]"
                + string.Join("", live.Take(15).Select(x => $" | {x.d:0} m {x.s}"));
        }

        /// <summary>Loaded home spawners of a classic slime (gordo spot search).</summary>
        internal static List<(Vector3 pos, string scene)> HomesOf(string slime)
        {
            var r = new List<(Vector3, string)>();
            foreach (var h in _homes)
                if (h.sp != null && h.names.Split('+').Contains(slime)) r.Add((h.sp.transform.position, h.scene));
            return r;
        }

        static bool IsWildSlimeSpawner(DirectedActorSpawner spawner) =>
            spawner.TryCast<DirectedSlimeSpawner>() != null && spawner.TryCast<DirectedAnimalSpawner>() == null;

        internal static void OnSpawnerAwake(DirectedActorSpawner spawner)
        {
            if (_entries.Count == 0 || spawner == null || !IsWildSlimeSpawner(spawner)) return;
            string scene = spawner.gameObject.scene.name?.ToLowerInvariant() ?? "";
            if (!_entries.Any(e => e.scenes.Any(z => scene.Contains(z)))) return;
            _touched.Add(spawner.Pointer);
            if (Cfg.SpawnHomes.Value && _homeSet.Add(spawner.Pointer))
            {
                var names = _entries.Where(e => e.scenes.Any(z => scene.Contains(z)) && IsHome(spawner, scene, e.spec)).Select(e => e.spec.Name).ToList();
                if (names.Count > 0) _homes.Add((spawner, scene, string.Join("+", names)));
            }
            int k = Apply(spawner, scene);
            if (k <= 0) return;
            AddedPerScene.TryGetValue(scene, out int was);
            AddedPerScene[scene] = was + k;
            Modified[spawner.GetInstanceID()] = scene + "/" + spawner.name;
            if (Diag.Enabled && _awakeLogged++ < 4)
            {
                var parts = new List<string>();
                foreach (var c in spawner.Constraints)
                {
                    var names = new List<string>();
                    if (c?.Slimeset?.Members != null) foreach (var m in c.Slimeset.Members) names.Add(m?.IdentType != null ? m.IdentType.name : "null");
                    parts.Add(string.Join(",", names));
                }
                ClassicSlimesMod.Log.Msg($"[spawn] awake {spawner.GetIl2CppType().Name} {scene}/{spawner.name} id={spawner.GetInstanceID()} +{k} -> {string.Join(" | ", parts)}");
            }
        }
        static int _awakeLogged;

        /// <summary>
        /// The game rebuilds the spawners' slime sets after Awake (our members were gone by the time the spawner ran),
        /// so the members are re-checked right before the spawner evaluates its constraints.
        /// </summary>
        internal static void BeforeSpawnCheck(DirectedActorSpawner spawner)
        {
            if (spawner == null || !_touched.Contains(spawner.Pointer)) return;
            string scene = spawner.gameObject.scene.name?.ToLowerInvariant() ?? "";
            int k = Apply(spawner, scene);
            if (k > 0) { ReAdded += k; Modified[spawner.GetInstanceID()] = scene + "/" + spawner.name; }
        }
    }

    // NB: CanSpawn/GetEligibleConstraints take Nullable<float>; Harmony trampolines for those crash in Il2CppInterop
    // (NullReferenceException in Memmove every frame). These two have no nullable parameters.
    [HarmonyPatch(typeof(DirectedActorSpawner), nameof(DirectedActorSpawner.CanSpawnSomething))]
    static class SpawnerCanSpawnSomethingPatch
    {
        static void Prefix(DirectedActorSpawner __instance)
        {
            try { Spawns.BeforeSpawnCheck(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(DirectedActorSpawner), nameof(DirectedActorSpawner.Spawn))]
    static class SpawnerSpawnPatch
    {
        static void Prefix(DirectedActorSpawner __instance)
        {
            try { Spawns.BeforeSpawnCheck(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(DirectedActorSpawner), "Awake")]
    static class SpawnerAwakePatch
    {
        static void Postfix(DirectedActorSpawner __instance)
        {
            try { Spawns.OnSpawnerAwake(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Spawner patch: " + e.Message); }
        }
    }
}
