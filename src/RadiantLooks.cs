using System;
using System.Collections.Generic;
using CottonLibrary;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;
using Expr = Il2Cpp.SlimeFace.SlimeExpression;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// Builds SR2 1.3.0 RadiantBase variants for Mosaic, Rad, Glitch and Quicksilver before CottonForge generates largos.
    /// CottonForge's LargoBlend then generates RADIANT_LARGO_0 / RADIANT_LARGO_1 automatically for Rad and Mosaic largos.
    /// </summary>
    public static class RadiantLooks
    {
        public static bool Available { get; private set; }
        public static SlimeAppearance.AppearanceType RadiantBaseType { get; private set; } = (SlimeAppearance.AppearanceType)1;
        public static SlimeAppearance.AppearanceSaveSet DefaultSaveSet { get; private set; }

        public static SlimeFace GlitchRadSR1 { get; private set; }
        public static SlimeFace GlitchRadSR2 { get; private set; }
        public static Material QsRadiantChargedMat { get; private set; }

        static readonly Dictionary<string, SlimeFace> _radLargoRadiantFaces = new();
        static readonly List<(SlimeSpec Spec, Color Top, Color Mid, Color Bot)> _builtPediaSpecs = new();
        static Il2CppMonomiPark.SlimeRancher.Pedia.PediaCategory _radCategory;

        public static bool IsRadiantAppearance(SlimeAppearance app) =>
            app != null && (app._appearType != SlimeAppearance.AppearanceType.DEFAULT
                || (!string.IsNullOrEmpty(app.name) && app.name.Contains("Radiant", StringComparison.Ordinal)));

        public static SlimeFace GlitchFace(bool sr1) => sr1 ? GlitchRadSR1 : GlitchRadSR2;

        public static void BuildAll()
        {
            _builtPediaSpecs.Clear();
            SlimeAppearance pinkRad = null;
            try { pinkRad = Library.GetSlime("Pink")?.RadiantBase; } catch { }
            if (pinkRad == null)
            {
                Available = false;
                ClassicSlimesMod.Log.Msg("Radiants: Pink.RadiantBase not found on this game build, skipping radiant variants");
                return;
            }
            Available = true;
            RadiantBaseType = pinkRad._appearType;
            var pinkDef = Library.GetSlime("Pink")?.AppearancesDefault?[0];
            if (pinkDef != null) DefaultSaveSet = pinkDef.SaveSet;

            int built = 0;
            if (TryBuild("Mosaic", s => BuildMosaicRadiant(s, pinkRad))) built++;
            if (TryBuild("Rad", s => BuildRadRadiant(s, pinkRad))) built++;
            if (TryBuild("Glitch", s => BuildGlitchRadiant(s, pinkRad))) built++;
            if (TryBuild("Quicksilver", s => BuildQuicksilverRadiant(s, pinkRad))) built++;
            ClassicSlimesMod.Log.Msg($"Radiants: built {built} classic RadiantBase variants");
            try { RegisterRadiantSpawns(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Radiant spawns register: " + e.Message); }
            try { RegisterRadiantPediaEntries(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Radiant pedia register: " + e.Message); }
        }

        public static void RegisterRadiantPediaEntries()
        {
            if (!Available || _builtPediaSpecs.Count == 0) return;
            Il2CppMonomiPark.SlimeRancher.Pedia.RadiantSlimePediaEntry pinkRadEntry = null;
            foreach (var e in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.RadiantSlimePediaEntry>())
            {
                if (e == null) continue;
                if (e.SlimeDefinition != null && e.SlimeDefinition.name == "Pink") { pinkRadEntry = e; break; }
                pinkRadEntry ??= e;
            }
            if (pinkRadEntry == null)
            {
                ClassicSlimesMod.Log.Warning("Radiant pedia: no template RadiantSlimePediaEntry found");
                return;
            }

            foreach (var item in _builtPediaSpecs)
            {
                var s = item.Spec;
                if (s?.Def == null || s.Def.RadiantBase == null) continue;
                if (PediaArt.RadiantBySlime.ContainsKey(s.Def.Pointer)) continue;
                var re = Object.Instantiate(pinkRadEntry);
                Object.DontDestroyOnLoad(re);
                re.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                re.name = "radiant_" + s.Key + "_slime";
                re._slimeDefinition = s.Def;
                re._title = s.Def.localizedName;
                try { re._description = CottonLibrary.Library.CreateLocalizedString("m.intro." + s.Key + "_slime", "Pedia"); } catch { }
                string pid = null;
                try { pid = re.PersistenceId; } catch { }
                string pinkPid = null;
                try { pinkPid = pinkRadEntry.PersistenceId; } catch { }
                if (string.IsNullOrEmpty(pid) || pid == pinkPid)
                    re._persistenceId = re.name;
                var iconSp = s.Def.RadiantBase._icon ?? s.Def.icon;
                try { re.SetIconAndArt(iconSp, new UnityEngine.AddressableAssets.AssetReferenceT<Sprite>("")); }
                catch { }
                var cardSp = MakeRadiantPediaArt(s, item.Top, item.Mid, item.Bot) ?? iconSp;
                PediaArt.RegisterRadiant(s.Def, re, cardSp);
            }
            EnsureRadiantPediaCategory();
            PediaArt.RegisterPersistence();
        }

        public static void EnsureRadiantPediaCategory()
        {
            if (PediaArt.RadiantBySlime.Count == 0) return;
            if (_radCategory == null)
            {
                foreach (var cfg in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirectorConfiguration>())
                    if (cfg?.PediaCategory != null) { _radCategory = cfg.PediaCategory; break; }
            }
            if (_radCategory == null)
            {
                foreach (var cat in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.PediaCategory>())
                {
                    if (cat == null || cat._items == null) continue;
                    foreach (var it in cat._items)
                    {
                        if (it != null && it.TryCast<Il2CppMonomiPark.SlimeRancher.Pedia.RadiantSlimePediaEntry>() != null)
                        {
                            _radCategory = cat;
                            break;
                        }
                    }
                    if (_radCategory != null) break;
                }
            }
            if (_radCategory == null) return;

            var arrList = new List<Il2CppMonomiPark.SlimeRancher.Pedia.PediaEntry>();
            var seen = new HashSet<IntPtr>();
            if (_radCategory._items != null)
            {
                foreach (var it in _radCategory._items)
                    if (it != null && seen.Add(it.Pointer)) arrList.Add(it);
            }
            int added = 0;
            foreach (var kv in PediaArt.RadiantBySlime)
            {
                var re = kv.Value;
                if (re != null && seen.Add(re.Pointer)) { arrList.Add(re); added++; }
            }
            if (added > 0) _radCategory._items = Gc.Array(arrList);

            try
            {
                var rt = _radCategory.GetRuntimeCategory()?.TryCast<Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory>();
                EnsureRadiantEntriesInRuntimeList(rt?._items);
            }
            catch { }
            try
            {
                var rt = SceneContext.Instance?.PediaDirector?.FindRuntimeCategory(_radCategory.Cast<Il2CppMonomiPark.SlimeRancher.Pedia.IPediaCategory>())?.TryCast<Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory>();
                EnsureRadiantEntriesInRuntimeList(rt?._items);
            }
            catch { }
            if (added > 0)
                ClassicSlimesMod.Log.Msg($"Radiants: registered {PediaArt.RadiantBySlime.Count} classic Radiant entries in Pedia category '{_radCategory.name}'");
        }

        internal static void EnsureRadiantEntriesInRuntimeList(Il2CppSystem.Collections.Generic.List<Il2CppMonomiPark.SlimeRancher.Pedia.PediaEntry> list)
        {
            if (list == null || PediaArt.RadiantBySlime.Count == 0) return;
            var seen = new HashSet<IntPtr>();
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) seen.Add(list[i].Pointer);
            foreach (var kv in PediaArt.RadiantBySlime)
                if (kv.Value != null && seen.Add(kv.Value.Pointer)) list.Add(kv.Value);
        }

        public static void RegisterRadiantSpawns()
        {
            if (!Available) return;
            var classicDefs = new List<SlimeDefinition>();
            foreach (var n in new[] { "Mosaic", "Rad", "Glitch", "Quicksilver" })
            {
                var d = SlimeFactory.Spec(n)?.Def;
                if (d != null && d.RadiantBase != null) classicDefs.Add(d);
            }
            if (classicDefs.Count == 0) return;

            var addedNames = new List<string>();
            foreach (var cfg in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeConfig>())
            {
                if (cfg == null) continue;
                var existing = cfg._radiantShuffleBagSizes;
                int bagSize = 150;
                var list = new List<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeConfig.RadiantShuffleBagConfigEntry>();
                var seen = new HashSet<IntPtr>();
                if (existing != null)
                {
                    foreach (var e in existing)
                    {
                        if (e == null) continue;
                        list.Add(e);
                        if (e.Slime != null)
                        {
                            seen.Add(e.Slime.Pointer);
                            if (e.BagSize > 0 && (e.Slime.name == "Crystal" || e.Slime.name == "Pink"))
                                bagSize = e.BagSize;
                        }
                    }
                }
                int addedHere = 0;
                foreach (var def in classicDefs)
                {
                    if (!seen.Add(def.Pointer)) continue;
                    list.Add(new Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeConfig.RadiantShuffleBagConfigEntry
                    {
                        Slime = def,
                        BagSize = bagSize
                    });
                    addedHere++;
                    if (!addedNames.Contains(def.name)) addedNames.Add(def.name);
                }
                if (addedHere > 0) cfg._radiantShuffleBagSizes = Gc.Array(list);
            }
            foreach (var rsd in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector>())
            {
                if (rsd == null) continue;
                try { rsd.ResizeBagsIfNeeded(); } catch { }
            }
            if (addedNames.Count > 0)
                ClassicSlimesMod.Log.Msg($"Radiants: registered wild spawn bags for [{string.Join(", ", addedNames)}]");
        }

        static bool TryBuild(string name, Action<SlimeSpec> build)
        {
            var s = SlimeFactory.Spec(name);
            if (s?.Def == null || s.Def.AppearancesDefault == null || s.Def.AppearancesDefault.Length == 0) return false;
            try { build(s); return true; }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Radiant {name} failed: {e}"); return false; }
        }

        // ------------------------------------------------------------------ shared body/app helpers

        /// <summary>Copies SR2 radiant parallax/noise/rim onto any Slime/Body material with an optional tinted rainbow ramp.</summary>
        public static void EnableRadiantOnBody(Material dst, Color top, Color mid, Color bot, float rainbowMix = 0.5f)
        {
            if (dst == null || !dst.HasProperty("_EnableRadiantEffect")) return;
            var src = Mat.Find("slimePinkBaseRadiant");
            if (src != null)
            {
                foreach (var tp in new[] { "_RadiantNoisePattern", "_RadiantParallaxPattern", "_RadiantFresnelGradient" })
                    if (src.HasProperty(tp)) dst.Tx(tp, src.GetTexture(tp));
                foreach (var fp in new[] { "_ParallaxRadiant", "_NoiseIntensityRadiant", "_RadiantEmissionAmount" })
                    if (src.HasProperty(fp)) dst.Flt(fp, src.GetFloat(fp));
                if (rainbowMix >= 0.99f && src.HasProperty("_ColorShiftGradient"))
                    dst.Tx("_ColorShiftGradient", src.GetTexture("_ColorShiftGradient"));
            }
            if (rainbowMix < 0.99f)
                dst.Tx("_ColorShiftGradient", RadiantRamp(dst.name, top, mid, bot, rainbowMix));
            dst.Kw("_ENABLERADIANTEFFECT_ON", true);
            dst.Flt("_EnableRadiantEffect", 1f);
            dst.Kw("_TOGGLERADIANTEFFECT_ON", true);
            dst.Flt("_ToggleRadiantEffect", 1f);
            dst.Flt("_ForceRadiantEffect", 0f);
        }

        static Texture2D RadiantRamp(string key, Color top, Color mid, Color bot, float rainbowMix)
        {
            static int B(float v) => (int)(Mathf.Clamp01(v) * 255f + 0.5f);
            string id = $"radRamp_{B(top.r):X2}{B(mid.g):X2}{B(bot.b):X2}_{rainbowMix:0.00}_{key.GetHashCode():X}";
            var cols = new[] { top, mid, bot, top };
            return Gfx.Generated(id, 128, 1, (x, y) =>
            {
                float t = x / 127f * 3f;
                int i = Mathf.Min((int)t, 2);
                var own = Color.Lerp(cols[i], cols[i + 1], t - i);
                var hue = Color.HSVToRGB((x / 64f) % 1f, 0.9f, 1f);
                var c = Color.Lerp(own, hue, Mathf.Clamp01(rainbowMix));
                c.a = 1f;
                return c;
            }, false);
        }

        static Sprite MakeRadiantIcon(SlimeSpec s, Color top, Color mid, Color bot) =>
            MakeRadiantArtwork(s, "iconSlime" + s.Name + ".png", "iconSlime" + s.Name + "Radiant", top, mid, bot) ?? s.Def.icon;

        static Sprite MakeRadiantPediaArt(SlimeSpec s, Color top, Color mid, Color bot) =>
            MakeRadiantArtwork(s, "pediaSlime" + s.Name + ".png", "pediaSlime" + s.Name + "Radiant", top, mid, bot)
            ?? MakeRadiantArtwork(s, "iconSlime" + s.Name + ".png", "pediaSlime" + s.Name + "Radiant", top, mid, bot);

        static Sprite MakeRadiantArtwork(SlimeSpec s, string resFile, string spriteName, Color top, Color mid, Color bot)
        {
            try
            {
                var bytes = Res.Bytes(resFile);
                if (bytes == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Il2CppImageConversionManager.LoadImage(tex, bytes, false);
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (c.a < 0.02f) continue;
                    Color.RGBToHSV(c, out _, out float sat, out float val);
                    if (val < 0.16f || (sat < 0.15f && val > 0.88f)) continue;   // keep ink outlines and eye specular highlights
                    float y = (float)(i / w) / Mathf.Max(1, h - 1);
                    var pal = y > 0.5f ? Color.Lerp(mid, top, (y - 0.5f) * 2f) : Color.Lerp(bot, mid, y * 2f);
                    float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    var shifted = Color.Lerp(c, pal * Mathf.Clamp(lum * 1.45f + 0.12f, 0.35f, 1.35f), 0.72f);
                    // subtle iridescent glint across upper-left
                    float diag = (i % w) / (float)w + y;
                    float ir = Mathf.Clamp01(1f - Mathf.Abs(diag - 1.15f) * 3.5f) * 0.25f;
                    shifted = Color.Lerp(shifted, Color.Lerp(top, Color.white, 0.45f), ir);
                    shifted.a = c.a;
                    px[i] = shifted;
                }
                // crisp 4-point radiant star sparkle in top-right corner
                int sx = (int)(w * 0.80f), sy = (int)(h * 0.80f), sr = Math.Max(10, w / 10);
                for (int dy = -sr; dy <= sr; dy++)
                {
                    for (int dx = -sr; dx <= sr; dx++)
                    {
                        int xx = sx + dx, yy = sy + dy;
                        if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                        float adx = Mathf.Abs(dx) / (float)sr, ady = Mathf.Abs(dy) / (float)sr;
                        float star = Mathf.Max(0f, 1f - Mathf.Sqrt(adx * 0.22f + ady * 1.9f)) * (1f - ady);
                        star = Mathf.Max(star, Mathf.Max(0f, 1f - Mathf.Sqrt(ady * 0.22f + adx * 1.9f)) * (1f - adx));
                        float core = Mathf.Clamp01(1f - Mathf.Sqrt(adx * adx + ady * ady) * 2.5f);
                        float glow = Mathf.Clamp01(star * 1.15f + core * 0.8f);
                        if (glow <= 0.02f) continue;
                        int idx = yy * w + xx;
                        var dst = px[idx];
                        var sc = Color.Lerp(mid, Color.white, Mathf.Clamp01(glow * 1.2f));
                        float a = Mathf.Max(dst.a, glow);
                        px[idx] = new Color(Mathf.Lerp(dst.r, sc.r, glow), Mathf.Lerp(dst.g, sc.g, glow), Mathf.Lerp(dst.b, sc.b, glow), a);
                    }
                }
                tex.SetPixels(px);
                tex.Apply(false, true);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = spriteName;
                tex.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(tex);
                var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
                sp.name = spriteName;
                sp.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(sp);
                return sp;
            }
            catch { return null; }
        }

        static SlimeAppearance FinalizeRadiantApp(SlimeSpec s, SlimeAppearance pinkRad, Material bodyMat, Material partMat, SlimeFace face,
            Color top, Color mid, Color bot, Color ammo, Color radColor)
        {
            var def = s.Def;
            var defaultApp = def.AppearancesDefault[0];
            var radApp = Object.Instantiate(defaultApp);
            Object.DontDestroyOnLoad(radApp);
            radApp.name = s.Name + "Radiant";
            radApp.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            radApp._appearType = RadiantBaseType;
            radApp.SaveSet = pinkRad.SaveSet;
            radApp._dependentAppearances = new Il2CppReferenceArray<SlimeAppearance>(0);

            var pal = radApp._colorPalette;
            pal.Top = top; pal.Middle = mid; pal.Bottom = bot; pal.Ammo = ammo;
            radApp._colorPalette = pal;
            radApp.ColorPaletteNew = pal;
            radApp._splatColor = mid;
            try { radApp.SetSplatColor(); } catch { }
            radApp._radiantColor = radColor;
            try { radApp.SetRadiantColor(); } catch { }
            radApp._icon = MakeRadiantIcon(s, top, mid, bot);
            if (face != null) radApp._face = face;

            var auraStrong = Mat.Find("RadiantAura_strong");
            var auraLight = Mat.Find("RadiantAura_light") ?? auraStrong;
            var list = new List<SlimeAppearanceStructure>();
            foreach (var st in defaultApp._structures)
            {
                if (st?.Element == null) { list.Add(st); continue; }
                var copy = new SlimeAppearanceStructure(st);
                var t = st.Element.Type;
                if (t == EType.BODY && bodyMat != null)
                {
                    copy.DefaultMaterials = Gc.Array(auraStrong != null ? new[] { bodyMat, auraStrong } : new[] { bodyMat });
                }
                else if (partMat != null && t != EType.FACE && t != EType.AURA && st.DefaultMaterials != null && st.DefaultMaterials.Length > 0)
                {
                    copy.DefaultMaterials = Gc.Array(auraLight != null ? new[] { partMat, auraLight } : new[] { partMat });
                }
                else if (st.DefaultMaterials != null)
                {
                    copy.DefaultMaterials = Gc.Array(st.DefaultMaterials);
                }
                list.Add(copy);
            }

            if (pinkRad._structures != null)
            {
                foreach (var pst in pinkRad._structures)
                {
                    if (pst?.Element == null) continue;
                    string en = pst.Element.Name ?? "";
                    if (pst.Element.Type != EType.AURA && en != "RadiantAuraVFX" && en != "RadiantSphereLight") continue;
                    var copy = new SlimeAppearanceStructure(pst);
                    var dm = pst.DefaultMaterials;
                    if (dm != null && dm.Length > 0 && dm[0] != null)
                    {
                        var mats = new Material[dm.Length];
                        for (int k = 0; k < dm.Length; k++) mats[k] = dm[k];
                        if (en == "RadiantSphereLight" || dm[0].shader?.name?.Contains("SphereLight") == true)
                        {
                            // Never include "Mosaic" or "Quicksilver" in sphere light name so ElasticGlass/SetChargedLook ignore it.
                            var sm = new Material(dm[0]) { name = "ClassicSlimes_AuraSphere_" + s.Name.Substring(0, 2), hideFlags = HideFlags.DontUnloadUnusedAsset };
                            sm.Col("_Color", Color.Lerp(mid, top, 0.5f));
                            sm.Tint("AuraSphere_" + s.Name.Substring(0, 2), bot, mid, top);
                            mats[0] = Mat.Track(sm);
                        }
                        copy.DefaultMaterials = Gc.Array(mats);
                    }
                    list.Add(copy);
                }
            }
            radApp._structures = Gc.Array(list);

            def.RadiantBase = radApp;
            def.AppearancesDefault = Gc.Array(new[] { defaultApp, radApp });

            // Register radiant FIRST and default LAST so defaultApp stays the chosen active appearance!
            var dir = Library.mainAppearanceDirector ?? SceneContext.Instance?.SlimeAppearanceDirector;
            if (dir != null)
            {
                try
                {
                    dir.RegisterDependentAppearances(def, radApp);
                    dir.UpdateChosenSlimeAppearance(def, radApp);
                    dir.RegisterDependentAppearances(def, defaultApp);
                    dir.UpdateChosenSlimeAppearance(def, defaultApp);
                }
                catch { }
            }
            if (!_builtPediaSpecs.Exists(x => x.Spec.Def.Pointer == def.Pointer))
                _builtPediaSpecs.Add((s, top, mid, bot));
            return radApp;
        }

        // ------------------------------------------------------------------ 1. MosaicRadiant (Astral Pearl Opal & Prismatic Rose Diamond)

        static void BuildMosaicRadiant(SlimeSpec s, SlimeAppearance pinkRad)
        {
            Color top = new(0.96f, 0.88f, 1.00f), mid = new(0.78f, 0.28f, 0.92f), bot = new(0.18f, 0.68f, 0.96f),
                  ammo = new(0.86f, 0.42f, 0.96f), radCol = new(0.92f, 0.48f, 1.00f);
            var m = Object.Instantiate(GordoFacets.Tiles ?? Visuals.Mats["Mosaic"]);
            m.name = "ClassicSlimes_MosaicRadiantBody";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.Palette(top, mid, bot);
            m.Col("_RedTopColor", new Color(0.98f, 0.92f, 1.00f));
            m.Col("_RedMiddleColor", new Color(0.88f, 0.78f, 0.98f));
            m.Col("_RedBottomColor", new Color(0.70f, 0.72f, 0.96f));
            m.Col("_GreenTopColor", new Color(1.00f, 0.48f, 0.88f));
            m.Col("_GreenMiddleColor", new Color(0.86f, 0.22f, 0.86f));
            m.Col("_GreenBottomColor", new Color(0.55f, 0.14f, 0.78f));
            m.Col("_BlueTopColor", new Color(0.45f, 0.96f, 1.00f));
            m.Col("_BlueMiddleColor", new Color(0.18f, 0.74f, 0.98f));
            m.Col("_BlueBottomColor", new Color(0.12f, 0.42f, 0.90f));
            m.Flt("_GlossPower", 0.92f);
            EnableRadiantOnBody(m, top, mid, bot, 0.60f);
            GordoFacets.RadiantTiles = Visuals.Keep(m, "MosaicRadiant");
            FinalizeRadiantApp(s, pinkRad, GordoFacets.RadiantTiles, null, null, top, mid, bot, ammo, radCol);
        }

        // ------------------------------------------------------------------ 2. RadRadiant (Cherenkov Cobalt-Cyan & Actinide UV)

        static SlimeFace BuildRadRadiantFace(SlimeFace src)
        {
            if (src == null) return null;
            return Faces.Clone(src, "faceSlimeRadRadiant",
                e =>
                {
                    var m = Visuals.GlowFaceMat(e, true, "_RadRadiant");
                    m.Col("_EyeRed", new Color(0.42f, 0.96f, 1.00f, 1f));
                    m.Col("_EyeGreen", new Color(0.78f, 1.00f, 1.00f, 1f));
                    m.Col("_EyeBlue", new Color(1.00f, 1.00f, 1.00f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));
                    Visuals.Mats["RadRadiantEyes_" + e.name] = m;
                    return m;
                },
                mo =>
                {
                    var m = Visuals.GlowFaceMat(mo, false, "_RadRadiant");
                    if (mo.name == "mouthHappy" && !string.IsNullOrEmpty(Visuals.RadHappyMouth))
                    {
                        var big = Mat.Find(Visuals.RadHappyMouth);
                        if (big != null && big.HasProperty("_FaceAtlas")) m.Tx("_FaceAtlas", big.GetTexture("_FaceAtlas"));
                    }
                    m.Col("_MouthTop", new Color(0.12f, 0.03f, 0.30f, 1f));
                    m.Col("_MouthMid", new Color(0.22f, 0.78f, 1.00f, 1f));
                    m.Col("_MouthBot", new Color(0.74f, 0.98f, 1.00f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));
                    Visuals.Mats["RadRadiantMouth_" + mo.name] = m;
                    return m;
                });
        }

        public static SlimeFace RadLargoRadiantFace(SlimeFace src)
        {
            if (src == null || src.name.EndsWith("_RadLargoRadiant")) return src;
            if (_radLargoRadiantFaces.TryGetValue(src.name, out var have) && have != null) return have;
            var f = Faces.Clone(src, src.name + "_RadLargoRadiant",
                e =>
                {
                    var m = Visuals.GlowFaceMat(e, true, "_RadLargoRadiant");
                    m.Col("_EyeRed", new Color(0.42f, 0.96f, 1.00f, 1f));
                    m.Col("_EyeGreen", new Color(0.78f, 1.00f, 1.00f, 1f));
                    m.Col("_EyeBlue", new Color(1.00f, 1.00f, 1.00f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));
                    return m;
                },
                mo =>
                {
                    var m = Visuals.GlowFaceMat(mo, false, "_RadLargoRadiant");
                    m.Col("_MouthTop", new Color(0.12f, 0.03f, 0.30f, 1f));
                    m.Col("_MouthMid", new Color(0.22f, 0.78f, 1.00f, 1f));
                    m.Col("_MouthBot", new Color(0.74f, 0.98f, 1.00f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));
                    return m;
                });
            if (f != null) _radLargoRadiantFaces[src.name] = f;
            return f ?? src;
        }

        static void BuildRadRadiant(SlimeSpec s, SlimeAppearance pinkRad)
        {
            Color top = new(0.32f, 0.96f, 1.00f), mid = new(0.12f, 0.38f, 1.00f), bot = new(0.36f, 0.06f, 0.68f),
                  ammo = new(0.18f, 0.72f, 1.00f), radCol = new(0.20f, 0.85f, 1.00f);
            var m = Mat.Clone("RadRadiantBody", "slimePhosphorBaseRadiant", "slimePhosphorBase", "slimePinkBase");
            if (m != null)
            {
                m.Palette(top, mid, bot);
                m.Col("_GlowTop", new Color(0.28f, 0.95f, 1.00f, 1f));
                m.Flt("_GlowStrength", 0.24f);
                m.Flt("_GlowSpeed", 0.9f);
                m.Flt("_GlowMin", 0.20f);
                m.Flt("_GlowMax", 0.55f);
                m.Flt("_GlossPower", 0.68f);
                m.Kw("_ENABLEHYPEREFFECT_ON", false); m.Flt("_EnableHyperEffect", 0f);
                m.Kw("_ENABLEVORTEX_ON", false); m.Flt("_EnableVortex", 0f);
                EnableRadiantOnBody(m, top, mid, bot, 0.55f);
                Visuals.Keep(m, "RadRadiant");
            }
            var face = BuildRadRadiantFace(s.Def.AppearancesDefault[0]._face);
            FinalizeRadiantApp(s, pinkRad, m, null, face, top, mid, bot, ammo, radCol);
        }

        // ------------------------------------------------------------------ 3. GlitchRadiant (Solar Overdrive / Golden Cyberpunk)

        static void BuildGlitchRadiantFaces(SlimeFace src)
        {
            if (src == null || GlitchRadSR2 != null) return;
            static void EyeCols(Material m)
            {
                m.Col("_EyeRed", new Color(0.78f, 1.00f, 1.00f, 1f));
                m.Col("_EyeGreen", new Color(0.95f, 0.98f, 1.00f, 1f));
                m.Col("_EyeBlue", new Color(1.00f, 1.00f, 1.00f, 1f));
            }
            static void MouthCols(Material m)
            {
                m.Col("_MouthTop", new Color(0.32f, 0.05f, 0.55f, 1f));
                m.Col("_MouthMid", new Color(0.92f, 0.96f, 1.00f, 1f));
                m.Col("_MouthBot", new Color(1.00f, 0.92f, 0.45f, 1f));
            }
            GlitchRadSR2 = Faces.Clone(src, "faceSlimeGlitchRadiant",
                e => { var m = Object.Instantiate(e); m.name = e.name + "_GlitchRad"; m.hideFlags = HideFlags.DontUnloadUnusedAsset; EyeCols(m); return m; },
                mo => { var m = Object.Instantiate(mo); m.name = mo.name + "_GlitchRad"; m.hideFlags = HideFlags.DontUnloadUnusedAsset; MouthCols(m); return m; });
            GlitchRadSR1 = Faces.CloneByExpr(src, "faceSlimeGlitchRadiantSR1",
                (ex, e) =>
                {
                    var m = Object.Instantiate(e); m.name = $"{e.name}_GlitchRadSR1_{ex}"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var atlas = Res.Tex(Visuals.GlitchAtlas(ex), true, false);
                    if (atlas != null) m.Tx("_FaceAtlas", atlas);
                    EyeCols(m); return m;
                },
                (ex, mo) =>
                {
                    var m = Object.Instantiate(mo); m.name = $"{mo.name}_GlitchRadSR1_{ex}"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var atlas = Res.Tex(Visuals.GlitchAtlas(ex), true, false);
                    if (atlas != null) m.Tx("_FaceAtlas", atlas);
                    MouthCols(m); return m;
                });
        }

        static void BuildGlitchRadiant(SlimeSpec s, SlimeAppearance pinkRad)
        {
            Color top = new(1.00f, 0.90f, 0.28f), mid = new(1.00f, 0.62f, 0.08f), bot = new(0.26f, 0.06f, 0.56f),
                  ammo = new(1.00f, 0.78f, 0.16f), radCol = new(1.00f, 0.82f, 0.22f);
            var m = Mat.Clone("GlitchRadiantBody", "slimeHyperBaseRadiant", "HyperDefaultRingtailDefault_Body", "slimeHyperBase", "slimePinkBase");
            if (m != null)
            {
                m.Palette(top, mid, bot);
                m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
                if (Visuals.GlitchMasks != null && Visuals.GlitchMasks.Length > 0) m.Tx("_ColorMask", Visuals.GlitchMasks[0]);
                m.Flt("_ColorMaskUV1", 0f);
                // R: solar gold, G: electric cyan, B: neon violet-magenta
                m.Col("_RedTopColor", new Color(1.00f, 0.96f, 0.52f)); m.Col("_RedMiddleColor", new Color(1.00f, 0.80f, 0.22f)); m.Col("_RedBottomColor", new Color(0.92f, 0.52f, 0.08f));
                m.Col("_GreenTopColor", new Color(0.55f, 1.00f, 0.98f)); m.Col("_GreenMiddleColor", new Color(0.20f, 0.92f, 0.98f)); m.Col("_GreenBottomColor", new Color(0.12f, 0.58f, 0.88f));
                m.Col("_BlueTopColor", new Color(1.00f, 0.60f, 0.95f)); m.Col("_BlueMiddleColor", new Color(0.82f, 0.28f, 0.88f)); m.Col("_BlueBottomColor", new Color(0.36f, 0.10f, 0.68f));
                m.Kw("_ENABLEHYPEREFFECT_ON", true); m.Flt("_EnableHyperEffect", 1f);
                m.Col("_HyperFresnelColor", new Color(0.30f, 0.98f, 1.00f, 0f));
                m.Flt("_HyperBodyColorAlbedoIntensity", 0.95f);
                m.Flt("_HyperFresnelIntensity", 1.85f);
                m.Flt("_GlossPower", 0.45f);
                EnableRadiantOnBody(m, top, mid, bot, 0.45f);
                Visuals.Keep(m, "GlitchRadiant");
            }
            var srcFace = Library.GetSlime("Pink")?.AppearancesDefault?[0]?._face ?? s.Def.AppearancesDefault[0]._face;
            BuildGlitchRadiantFaces(srcFace);
            var face = GlitchFace(Cfg.GlitchSR1Face.Value) ?? GlitchRadSR2;
            FinalizeRadiantApp(s, pinkRad, m, null, face, top, mid, bot, ammo, radCol);
        }

        // ------------------------------------------------------------------ 4. QuicksilverRadiant (Solar Platinum & Dichroic Rose-Gold Chrome)

        static Texture2D QsRadiantMatcapTex() => Gfx.Generated("matcap_qs_radiant", 256, 256, (x, y) =>
        {
            float u = (x + 0.5f) / 128f - 1f, v = (y + 0.5f) / 128f - 1f;
            float r2 = u * u + v * v;
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(1f, r2)));
            var n = new Vector3(u, v, z).normalized;
            // dichroic chrome: champagne-platinum crown -> vivid rose-gold horizon -> twilight violet bounce below
            Color crown = new(1.00f, 0.96f, 0.92f), rose = new(1.00f, 0.58f, 0.78f), violet = new(0.32f, 0.14f, 0.48f), darkBand = new(0.20f, 0.08f, 0.30f);
            float t = n.y * 0.5f + 0.5f;
            Color c = t > 0.52f ? Color.Lerp(rose, crown, Mathf.Pow((t - 0.52f) / 0.48f, 0.75f)) : Color.Lerp(violet, rose, t / 0.52f);
            // studio horizon contrast band + specular softbox windows
            float horizon = Mathf.Exp(-Mathf.Pow((n.y + 0.08f) * 5.5f, 2f));
            c = Color.Lerp(c, darkBand, horizon * 0.55f);
            float rim = Mathf.Pow(Mathf.Clamp01(1f - z), 2.4f);
            c = Color.Lerp(c, new Color(0.45f, 0.95f, 1.00f), rim * 0.45f);
            float box1 = Mathf.Exp(-((n.x + 0.38f) * (n.x + 0.38f) * 14f + (n.y - 0.55f) * (n.y - 0.55f) * 18f));
            float box2 = Mathf.Exp(-((n.x - 0.44f) * (n.x - 0.44f) * 16f + (n.y - 0.42f) * (n.y - 0.42f) * 20f));
            c += new Color(1f, 0.97f, 0.94f) * ((box1 + box2 * 0.8f) * 0.65f);
            c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = 1f;
            return c;
        }, false);

        static Material BuildQsRadiantChromeMat(string name, Color top, Color mid, Color bot)
        {
            var m = Mat.Clone(name, "slimeGoldBase", "slimePinkBase");
            if (m == null) return null;
            m.Palette(top, mid, bot);
            m.Tx("_CubemapOverride", QsRadiantMatcapTex());
            m.Flt("_EnableMatcap", 1f);
            m.Flt("_OverrideBlend", 0.74f);
            m.Flt("_BodyLightingContribution", 0.65f);
            m.Flt("_GlossPower", 0.92f);
            Visuals.NeutralScatter(m);
            EnableRadiantOnBody(m, top, mid, bot, 0.50f);
            return Visuals.Keep(m, name);
        }

        static SlimeFace BuildQsRadiantFace(SlimeFace src)
        {
            if (src == null) return null;
            return Faces.Clone(src, "faceSlimeQuicksilverRadiant",
                e =>
                {
                    var m = Visuals.GlowFaceMat(e, true, "_QsRadiant");
                    m.Col("_EyeRed", new Color(0.55f, 0.92f, 1.00f, 1f));
                    m.Col("_EyeGreen", new Color(1.00f, 0.96f, 0.78f, 1f));
                    m.Col("_EyeBlue", new Color(1.00f, 1.00f, 1.00f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));
                    return m;
                },
                Visuals.NoMouth);
        }

        static void BuildQuicksilverRadiant(SlimeSpec s, SlimeAppearance pinkRad)
        {
            Color top = new(1.00f, 0.94f, 0.86f), mid = new(0.98f, 0.58f, 0.72f), bot = new(0.48f, 0.22f, 0.58f),
                  ammo = new(0.98f, 0.70f, 0.78f), radCol = new(1.00f, 0.72f, 0.84f);
            var bodyMat = BuildQsRadiantChromeMat("QsRadiantBody", top, mid, bot);
            var crestMat = BuildQsRadiantChromeMat("QsRadiantCrest", top, mid, bot);
            if (bodyMat != null)
            {
                Visuals.Mats["QuicksilverRadiant"] = bodyMat;
                var charged = new Material(bodyMat) { name = "ClassicSlimes_QsRadiantCharged", hideFlags = HideFlags.DontUnloadUnusedAsset };
                charged.Tx("_CubemapOverride", Res.Tex("matcap_mercury_charged", false, false));
                var hyper = Mat.Find("HyperDefaultRingtailDefault_Body") ?? Mat.Find("slimeHyperBase");
                var fres = hyper != null && hyper.HasProperty("_HyperFresnelNoise") ? hyper.GetTexture("_HyperFresnelNoise") : null;
                var sparks = hyper != null && hyper.HasProperty("_HyperSparksNoise") ? hyper.GetTexture("_HyperSparksNoise") : null;
                fres ??= Visuals.GameTex("HyperSlimeFX_03"); sparks ??= Visuals.GameTex("HyperSlimeFX_02");
                if (fres != null && sparks != null)
                {
                    charged.Tx("_HyperFresnelNoise", fres); charged.Tx("_HyperSparksNoise", sparks);
                    if (hyper != null && hyper.HasProperty("_HyperDashGradient")) charged.Tx("_HyperDashGradient", hyper.GetTexture("_HyperDashGradient"));
                    charged.Kw("_ENABLEHYPEREFFECT_ON", true); charged.Flt("_EnableHyperEffect", 1f);
                    charged.Col("_HyperFresnelColor", new Color(1.00f, 0.55f, 0.92f, 0f));
                    charged.Flt("_HyperBodyColorAlbedoIntensity", 1f);
                    charged.Flt("_HyperFresnelIntensity", 1.75f);
                }
                QsRadiantChargedMat = Visuals.Keep(charged, "QuicksilverRadiantCharged");
            }
            var srcFace = Library.GetSlime("Pink")?.AppearancesDefault?[0]?._face ?? s.Def.AppearancesDefault[0]._face;
            var face = BuildQsRadiantFace(srcFace);
            FinalizeRadiantApp(s, pinkRad, bodyMat, crestMat, face, top, mid, bot, ammo, radCol);
        }
    }
}
