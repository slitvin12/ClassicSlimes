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
            if (!Ready || sr == null || !Cfg.WildFoods.Value) return;
            var def = sr._resourceGrowerDefinition;
            if (def == null) return;
            if (_mine.Contains(def.Pointer)) return;
            string scene = sr.gameObject.scene.name?.ToLowerInvariant() ?? "";
            if (_clones.TryGetValue((def.Pointer, scene), out var known)) { if (known != null) sr._resourceGrowerDefinition = known; return; }
            if (sr.GetComponentInParent<LandPlot>() != null) return;
            var res = def._resources;
            if (res == null || res.Length == 0) return;
            bool veg = false, fruit = false, has = false; float sum = 0f;
            foreach (var e in res)
            {
                if (e?.ResourceIdentifiableType == null) continue;
                var t = e.ResourceIdentifiableType;
                if (All.Any(f => f.Type == t)) has = true;
                if (InGroup(Library.veggies, t)) veg = true;
                if (InGroup(Library.fruits, t)) fruit = true;
                sum += e.Weight;
            }
            if (has || sum <= 0f || (!veg && !fruit) || (def.name ?? "").IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0) return;
            var extra = new List<Entry>();
            foreach (var f in All)
            {
                if (f.Type == null || f.Fruit != fruit || (!f.Fruit && !veg)) continue;
                if (!f.Scenes.Any(z => scene.Contains(z))) continue;
                float p = Mathf.Clamp(f.Share(), 0f, 0.9f);
                if (p <= 0f) continue;
                extra.Add(new Entry { ResourceIdentifiableType = f.Type, Weight = sum * p / (1f - p), MinimumAmount = 0 });
            }
            if (extra.Count == 0) { _clones[(def.Pointer, scene)] = null; return; }
            var clone = Object.Instantiate(def);
            clone.name = def.name + "_ClassicSlimes";
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var arr = new Entry[res.Length + extra.Count];
            for (int i = 0; i < res.Length; i++) arr[i] = res[i];
            for (int i = 0; i < extra.Count; i++) arr[res.Length + i] = extra[i];
            clone._resources = new Il2CppReferenceArray<Entry>(arr);
            _clones[(def.Pointer, scene)] = clone;
            _mine.Add(clone.Pointer);
            sr._resourceGrowerDefinition = clone;
            string k = scene + ":" + string.Join("+", extra.Select(e => e.ResourceIdentifiableType.name));
            Added.TryGetValue(k, out int c); Added[k] = c + 1;
            if (Diag.Enabled && _logged++ < 6) ClassicSlimesMod.Log.Msg($"[food] {scene}/{sr.name} {def.name} +{string.Join(",", extra.Select(e => e.ResourceIdentifiableType.name))}");
        }
        static int _logged;
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
}
