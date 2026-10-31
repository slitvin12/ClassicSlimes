using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace ClassicSlimes
{
    public class SlimeSpec
    {
        public string Name;          // internal SR2 name, e.g. "Rad"
        public string Key;           // SR1 localisation key part, e.g. "rad"
        public Color32 Top, Mid, Bot, Ammo;
        public bool Largo;
        public Library.LargoSettings LargoSettings = Library.LargoSettings.KeepSecondBody | Library.LargoSettings.KeepSecondFace | Library.LargoSettings.MergeColors;
        public IdentifiableTypeGroup[] FoodGroups = new IdentifiableTypeGroup[0];
        public string[] FavoriteHints = new string[0];
        public string[] ToyHints = new string[0];
        public string[] ExtraFoodHints = new string[0];
        public bool HasPlort = true;
        public float PlortValue = 30f, PlortSaturation = 10f;
        public Library.SpawnLocations Zones;
        public MelonPreferences_Entry<float> Chance;
        public MelonPreferences_Entry<bool> Enabled;
        public float Scale = 1f;
        public Action<SlimeSpec> Customize;
        public Action<SlimeDefinition> CustomizeLargo;

        public SlimeDefinition Def;
        public IdentifiableType Plort;
    }

    public static class SlimeFactory
    {
        public static readonly List<SlimeSpec> Specs = new();
        public static readonly Dictionary<string, SlimeSpec> ByName = new();
        static Dictionary<string, IdentifiableType> _idents;

        static Color32 C(float r, float g, float b) => new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255), 255);

        public static SlimeSpec Spec(string name) => ByName.TryGetValue(name, out var s) ? s : null;

        static void BuildSpecs()
        {
            Specs.Clear(); ByName.Clear();
            // Colours = SR1 SlimeAppearance palettes (Top / Middle / Bottom / Ammo)
            Add(new SlimeSpec
            {
                Name = "Rad", Key = "rad",
                Top = C(0.44f, 0.90f, 0.48f), Mid = C(0.27f, 0.62f, 0.31f), Bot = C(0.13f, 0.36f, 0.16f), Ammo = C(0.129f, 0.792f, 0.149f),   // SR2-style rad icon
                Largo = true,
                FoodGroups = new[] { Library.veggies },
                FavoriteHints = new[] { "OcaOca", "Ocaoca", "Oca", "Beet" },
                ToyHints = new[] { "PowerCell", "NightLight" },
                ExtraFoodHints = new[] { "SpicyTofu", "Tofu" },
                PlortValue = 36f, PlortSaturation = 9f,
                Zones = Library.SpawnLocations.StarlightStand | Library.SpawnLocations.EmberValley,
                Chance = Cfg.SpawnRad, Enabled = Cfg.EnableRad,
                Customize = Visuals.Rad, CustomizeLargo = Visuals.RadLargo,
            });
            Add(new SlimeSpec
            {
                Name = "Quantum", Key = "quantum",
                Top = C(1f, 0.976f, 0.368f), Mid = C(1f, 0.678f, 0f), Bot = C(0.530f, 0.581f, 0.483f), Ammo = C(0.753f, 0.553f, 0.161f),
                Largo = true,
                FoodGroups = new[] { Library.fruits },
                FavoriteHints = new[] { "PhaseLemon", "Lemon", "Pogo" },
                ToyHints = new[] { "PuzzleCube", "GyroTop" },
                ExtraFoodHints = new[] { "SpicyTofu", "Tofu" },
                PlortValue = 48f, PlortSaturation = 8f,
                Zones = Library.SpawnLocations.PowderfallBluffs | Library.SpawnLocations.LabyrinthDreamland,
                Chance = Cfg.SpawnQuantum, Enabled = Cfg.EnableQuantum,
                Customize = Visuals.Quantum, CustomizeLargo = Visuals.QuantumLargo,
            });
            Add(new SlimeSpec
            {
                Name = "Mosaic", Key = "mosaic",
                Top = C(0.185f, 0.263f, 0.493f), Mid = C(0.256f, 0.904f, 0f), Bot = C(1f, 0.283f, 0.235f), Ammo = C(0.075f, 0.593f, 0.728f),
                Largo = true,
                FoodGroups = new[] { Library.veggies },
                FavoriteHints = new[] { "SilverParsnip", "Parsnip", "Onion" },
                ToyHints = new[] { "DiscoBall", "CrystalBall" },
                ExtraFoodHints = new[] { "SpicyTofu", "Tofu" },
                PlortValue = 54f, PlortSaturation = 8f,
                Zones = Library.SpawnLocations.EmberValley | Library.SpawnLocations.LabyrinthLavadepths,
                Chance = Cfg.SpawnMosaic, Enabled = Cfg.EnableMosaic,
                Customize = Visuals.Mosaic, CustomizeLargo = Visuals.MosaicLargo,
            });
            Add(new SlimeSpec
            {
                Name = "Quicksilver", Key = "quicksilver",
                Top = C(0.541f, 0.667f, 0.663f), Mid = C(0.384f, 0.420f, 0.439f), Bot = C(0.180f, 0.149f, 0.208f), Ammo = C(0.482f, 0.498f, 0.545f),
                Largo = false,
                ToyHints = new[] { "RocketShip" },
                // SR1: quicksilver slimes only "eat" electricity. In SR2 they feed on energy: rad plorts ("mobile batteries").
                FoodGroups = new IdentifiableTypeGroup[0],
                ExtraFoodHints = new string[0],
                PlortValue = 90f, PlortSaturation = 6f,
                Zones = Library.SpawnLocations.LabyrinthWaterworks | Library.SpawnLocations.LabyrinthHub,
                Chance = Cfg.SpawnQuicksilver, Enabled = Cfg.EnableQuicksilver,
                Customize = Visuals.Quicksilver,
            });
            Add(new SlimeSpec
            {
                Name = "Glitch", Key = "glitch",
                Top = C(0.96f, 0.47f, 0.75f), Mid = C(0.89f, 0.32f, 0.64f), Bot = C(0.72f, 0.16f, 0.50f), Ammo = C(0.894f, 0.271f, 0.604f),   // SR1 shader drew it pink
                Largo = false, HasPlort = false,
                FoodGroups = new IdentifiableTypeGroup[0],
                Zones = Library.SpawnLocations.LabyrinthHub | Library.SpawnLocations.LabyrinthWaterworks,
                Chance = Cfg.SpawnGlitch, Enabled = Cfg.EnableGlitch,
                Customize = Visuals.Glitch,
            });
        }

        static void Add(SlimeSpec s) { Specs.Add(s); ByName[s.Name] = s; }

        static void IndexIdents()
        {
            _idents = new Dictionary<string, IdentifiableType>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                if (t != null && !_idents.ContainsKey(t.name)) _idents[t.name] = t;
        }

        /// <summary>Find an identifiable whose name contains one of the hints (in order), optionally restricted to a group.</summary>
        public static IdentifiableType FindIdent(string[] hints, IdentifiableTypeGroup group = null, bool toy = false)
        {
            if (_idents == null) IndexIdents();
            foreach (var h in hints)
            {
                foreach (var kv in _idents)
                {
                    var t = kv.Value;
                    if (t == null || kv.Key.IndexOf(h, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (toy && t.TryCast<ToyDefinition>() == null) continue;
                    if (!toy && t.TryCast<ToyDefinition>() != null) continue;
                    if (t.TryCast<SlimeDefinition>() != null || t.IsPlort) continue;
                    if (group != null && !group.IsMember(t)) continue;
                    return t;
                }
            }
            return null;
        }

        public static void CreateAll()
        {
            BuildSpecs();
            IndexIdents();
            var pink = Library.GetSlime("Pink");
            foreach (var s in Specs)
            {
                if (!s.Enabled.Value) { ClassicSlimesMod.Log.Msg($"{s.Name} slime disabled in config"); continue; }
                try { Create(s, pink); ClassicSlimesMod.Log.Msg($"Created {s.Name} slime"); }
                catch (Exception e) { ClassicSlimesMod.Log.Error($"Creating {s.Name} failed: {e}"); }
            }
            // quicksilver feeds on rad plorts
            var qs = Spec("Quicksilver"); var rad = Spec("Rad");
            if (qs?.Def != null)
            {
                try
                {
                    var food = rad?.Plort ?? Library.GetPlort("PhosphorPlort");
                    if (food != null) { qs.Def.AddExtraEatIdent(food); qs.Def.AddFavorite(food); }
                    qs.Def.RefreshEatmap();
                }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("Quicksilver diet: " + e.Message); }
            }
            try { Loc.PreRegisterLargos(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Largo names: " + e.Message); }
            Spawns.Register();
        }

        static void Create(SlimeSpec s, SlimeDefinition pink)
        {
            var icon = Res.Sprite("iconSlime" + s.Name);
            var plortIcon = s.HasPlort ? Res.Sprite("iconPlort" + s.Name) : null;

            if (s.HasPlort)
            {
                s.Plort = Library.CreatePlortType(s.Name, s.Mid, plortIcon, "IdentifiableType." + s.Name + "Plort", s.PlortValue, s.PlortSaturation);
                var plortObj = Library.CreatePrefab("plort" + s.Name, Library.GetPlort("PinkPlort").prefab);
                s.Plort.localizedName = Library.CreateLocalizedString("l." + s.Key + "_plort");
                s.Plort.SetObjectPrefab(plortObj);
                plortObj.SetObjectIdent(s.Plort);
                Library.SetPlortColor(s.Top, s.Mid, s.Bot, plortObj);
                s.Plort.MakeVaccable();
                try { if (Library.plorts != null && !Library.plorts.IsMember(s.Plort)) { Library.plorts._memberTypes.Add(s.Plort); } } catch { }
            }

            s.Def = Library.CreateSlimeDef(s.Name, s.Ammo, icon, UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]), s.Name + "Default", "SlimeDefinition." + s.Name, s.Largo, s.LargoSettings);
            var def = s.Def;
            def.localizedName = Library.CreateLocalizedString("l." + s.Key + "_slime");
            var obj = Library.CreatePrefab("slime" + s.Name, pink.prefab);
            def.SetObjectPrefab(obj);
            obj.SetObjectIdent(def);
            def.SetSlimeColor(s.Top, s.Mid, s.Bot);
            var app = def.AppearancesDefault[0];
            app.SetPalette(app.Structures[0].DefaultMaterials[0], def);
            app._splatColor = s.Mid;
            app.SetSplatColor();
            app._icon = icon;

            def.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(s.FoodGroups.Where(g => g != null).ToArray());
            if (s.Plort != null) def.AddProduceIdent(s.Plort);
            def.Diet.FavoriteProductionCount = 2;
            var fav = s.FoodGroups.Length > 0 ? FindIdent(s.FavoriteHints, s.FoodGroups[0]) : null;
            if (fav != null) { def.AddFavorite(fav); ClassicSlimesMod.Log.Msg($"  {s.Name} favorite food: {fav.name}"); }
            foreach (var h in s.ExtraFoodHints)
            {
                var extra = FindIdent(new[] { h });
                if (extra != null) { def.AddExtraEatIdent(extra); break; }
            }
            var toy = FindIdent(s.ToyHints, null, true);
            if (toy != null)
            {
                def.FavoriteToyIdents = new Il2CppReferenceArray<ToyDefinition>(new[] { toy.Cast<ToyDefinition>() });
                ClassicSlimesMod.Log.Msg($"  {s.Name} favorite toy: {toy.name}");
            }
            else def.FavoriteToyIdents = new Il2CppReferenceArray<ToyDefinition>(0);
            def.RefreshEatmap();
            def.MakeVaccable();

            try { s.Customize?.Invoke(s); }
            catch (Exception e) { ClassicSlimesMod.Log.Error($"Customize {s.Name} failed: {e}"); }

            try
            {
                Library.CreatePediaEntryForSlime(def, s.Key + "_slime",
                    Library.CreateLocalizedString("m.intro." + s.Key + "_slime", "Pedia"),
                    Library.CreateLocalizedString("m.slimeology." + s.Key + "_slime", "Pedia"),
                    Library.CreateLocalizedString("m.risks." + s.Key + "_slime", "Pedia"),
                    Library.CreateLocalizedString("m.plortonomics." + s.Key + "_slime", "Pedia"));
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Pedia for {s.Name} failed: {e.Message}"); }
        }

        /// <summary>Cotton creates largos after SaveDirectorLoaded; give them proper names and our visuals.</summary>
        /// <summary>
        /// Cotton's CreateCompleteLargo fills SlimeModules with Get&lt;GameObject&gt;("moduleSlime" + name), which is null for
        /// custom base slimes. A null module aborts slime generation, so the largo's SubbehaviourPlexer never picks a
        /// behaviour and throws NullReferenceException every FixedUpdate (the largo just sits there).
        /// Replace null entries with the base slime's own module (classic slimes reuse the Pink module).
        /// </summary>
        static bool FixModules(SlimeDefinition d)
        {
            var mods = d.SlimeModules;
            var bases = d.BaseSlimes;
            int len = Math.Max(mods?.Length ?? 0, bases?.Length ?? 0);
            var list = new List<GameObject>();
            bool hadNull = mods == null;
            for (int i = 0; i < len; i++)
            {
                GameObject m = mods != null && i < mods.Length ? mods[i] : null;
                if (m == null)
                {
                    hadNull = true;
                    var b = bases != null && i < bases.Length ? bases[i] : null;
                    if (b != null && b.SlimeModules != null)
                        foreach (var bm in b.SlimeModules) if (bm != null) { m = bm; break; }
                }
                if (m != null && !list.Any(x => x.Pointer == m.Pointer)) list.Add(m);
            }
            if (!hadNull || list.Count == 0) return false;
            d.SlimeModules = Gc.Array(list);
            if (Diag.Enabled) ClassicSlimesMod.Log.Msg($"  [largo] {d.name} modules -> {string.Join(",", list.Select(x => x.name))}");
            return true;
        }

        /// <summary>
        /// Cotton builds every largo from a copy of the PinkRock prefab and then Destroy()s RockSlimeRoll on it, but the
        /// SubbehaviourPlexer keeps a serialized _subbehaviors array that still references the destroyed component.
        /// Every spawned largo then throws NullReferenceException in SubbehaviourPlexer.GetBestBehaviour each FixedUpdate,
        /// never selects a behaviour and just sits there. Rebuild the list from live components (this also enables
        /// behaviours that Cotton's MergeComponents copied from the parent slimes, e.g. Tabby's StalkConsumable).
        /// </summary>
        internal static bool FixPlexer(GameObject prefab, bool addMissing)
        {
            if (prefab == null) return false;
            var px = prefab.GetComponent<Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer>();
            if (px == null) return false;
            var cur = px._subbehaviors;
            var list = new List<Il2CppMonomiPark.SlimeRancher.Subbehaviour>();
            bool dead = cur == null;
            if (cur != null)
                foreach (var sb in cur)
                {
                    if (sb == null) { dead = true; continue; }
                    if (!list.Any(x => x.Pointer == sb.Pointer)) list.Add(sb);
                }
            int before = list.Count;
            if (addMissing || dead)
                foreach (var sb in prefab.GetComponents<Il2CppMonomiPark.SlimeRancher.Subbehaviour>())
                    if (sb != null && !list.Any(x => x.Pointer == sb.Pointer)) list.Add(sb);
            if (!dead && list.Count == before) return false;
            px._subbehaviors = Gc.Array(list);
            if (Diag.Enabled) ClassicSlimesMod.Log.Msg($"  [largo] {prefab.name} AI list {(cur?.Length ?? 0)} -> {list.Count}{(dead ? " (removed dead entries)" : "")}");
            return true;
        }

        public static void PostProcessLargos()
        {
            int n = 0, total = 0, largos = 0, fixedModules = 0, fixedPlexers = 0, otherPlexers = 0;
            var seen = new HashSet<IntPtr>();
            foreach (var d in GameContext.Instance.SlimeDefinitions.Slimes)
            {
                total++;
                if (d == null || !d.IsLargo || d.BaseSlimes == null) continue;
                largos++;
                if (!seen.Add(d.Pointer)) continue;
                SlimeSpec mine = null;
                foreach (var b in d.BaseSlimes) { var sp = b != null ? Spec(b.name) : null; if (sp != null && sp.Def == b) { mine = sp; break; } }
                if (mine == null)
                {
                    // Other Cotton largos (e.g. Rubber) have the same broken behaviour list; only dead entries are repaired.
                    try { if (FixPlexer(d.prefab, false)) otherPlexers++; } catch { }
                    continue;
                }
                n++;
                try { if (FixPlexer(d.prefab, true)) fixedPlexers++; } catch (Exception e) { ClassicSlimesMod.Log.Warning($"Largo AI {d.name}: {e.Message}"); }
                try { if (FixModules(d)) fixedModules++; } catch (Exception e) { ClassicSlimesMod.Log.Warning($"Largo modules {d.name}: {e.Message}"); }
                try { Loc.NameLargo(d); } catch (Exception e) { ClassicSlimesMod.Log.Warning($"Largo name {d.name}: {e.Message}"); }
                foreach (var b in d.BaseSlimes)
                {
                    var sp = b != null ? Spec(b.name) : null;
                    if (sp == null || sp.Def != b) continue;
                    try { sp.CustomizeLargo?.Invoke(d); } catch (Exception e) { ClassicSlimesMod.Log.Warning($"Largo visuals {d.name}: {e}"); }
                }
            }
            ClassicSlimesMod.Log.Msg($"Slime defs: {total}, largos: {largos}, unique: {seen.Count}");
            ClassicSlimesMod.Log.Msg($"Post-processed {n} classic largos (AI behaviour lists fixed: {fixedPlexers}, slime modules fixed: {fixedModules}; other Cotton largos AI fixed: {otherPlexers}), behaviour mappings: {Behaviours.Mapped}");
        }
    }
}
