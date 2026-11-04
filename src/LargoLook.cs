using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2Cpp;
using UnityEngine;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;

namespace ClassicSlimes
{
    /// <summary>
    /// How the classic slimes look inside largos with CottonForge's smart largo engine (<c>CottonLibrary.Forge.LargoStyle</c>).
    /// Soft dependency: on the original Cotton the styles are skipped and the Auto flags are ignored (Cotton's merge as before).
    /// </summary>
    static class LargoLook
    {
        /// <summary>Fork flags AutoBody | AutoFace | AutoColors (0x1C00): the fork picks body, face and colours by the styles below.</summary>
        internal const CottonLibrary.Library.LargoSettings Auto = (CottonLibrary.Library.LargoSettings)0x1C00;

        /// <summary>True when the loaded CottonLibrary is the fork with the smart largo engine.</summary>
        public static bool Available { get; private set; }

        /// <summary>Call before the largos are generated (SaveDirectorLoaded, before SlimeFactory.CreateAll).</summary>
        internal static void Register()
        {
            try { Available = Type.GetType("CottonLibrary.Forge.LargoStyle, CottonLibrary") != null; }
            catch { Available = false; }
            if (!Available) { ClassicSlimesMod.Log.Msg("Largos: original Cotton merge (no CottonForge smart largos)"); return; }
            try
            {
                RegisterStyles();
                ClassicSlimesMod.Log.Msg("Largos: CottonForge styles + SR1 Rad / Quantum / Mosaic largo looks registered (glass parts v3)");
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Largo styles failed: " + e.Message); }
        }

        /// <summary>
        /// True when the fork's engine built this largo (not its legacy merge): the largo then has its own radiant
        /// variants (no radiant PinkRock), so <c>ClearRadiant</c> must not remove them.
        /// </summary>
        internal static bool Blended(SlimeDefinition largo)
        {
            if (!Available || largo == null) return false;
            try { return BlendedCore(largo); } catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool BlendedCore(SlimeDefinition largo)
        {
            var info = CottonLibrary.Forge.LargoBlend.InfoOf(largo);
            return info != null && !info.Legacy;
        }

        // SR1 material values (extract/materials.txt). Never overwrite a radiant variant's own palette.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ClassicRad(CottonLibrary.Forge.LargoBuild c)
        {
            if (c.IsRadiant || c.BodyMaterial == null) return;
            Color top, mid, bottom;
            if (c.Has("Pink"))
            {
                top = new Color(1f, 0.4039f, 0.651f); mid = new Color(1f, 0.2118f, 0.2706f); bottom = new Color(0.4275f, 0f, 0.2706f);
            }
            else if (c.Has("Boom"))
            {
                top = new Color(1f, 0.3882f, 0.3882f); mid = new Color(0.8078f, 0f, 0f); bottom = new Color(0.4196f, 0f, 0f);
                c.BodyMaterial.Col("_CrackColor", new Color(0.2861f, 1f, 0f));
                c.BodyMaterial.Flt("_CrackNoiseSpeed", 0.25f); c.BodyMaterial.Flt("_CrackAmount", 0.4821f);
                c.BodyMaterial.Flt("_CrackColorRange", -0.1f);
            }
            else if (c.Has("Rock"))
            {
                top = new Color(0.2275f, 0.9059f, 0.4627f); mid = new Color(0f, 0.5059f, 0f); bottom = new Color(0.1059f, 0.2314f, 0f);
            }
            else
            {
                top = new Color(0.2704f, 0.8971f, 0.4822f); mid = new Color(0f, 0.5f, 0.0655f); bottom = new Color(0.1116f, 0.2279f, 0f);
            }
            c.BodyMaterial.Palette(top, mid, bottom);
            if (c.Appearance != null)
            {
                var p = c.Appearance._colorPalette;
                p.Top = top; p.Middle = mid; p.Bottom = bottom;
                c.Appearance._colorPalette = p; c.Appearance.ColorPaletteNew = p;
                c.Appearance._splatColor = mid; c.Appearance.SetSplatColor();
            }
            Visuals.Mats[c.Largo.name] = Mat.Track(c.BodyMaterial);
            ClassicSlimesMod.Log.Msg("[classic look] " + c.Largo.name + ": SR1 palette, original parent markings, no phosphor blob");
        }

        // ------------------------------------------------------------ SR1 largo palettes (Steam guide 2376297882)
        static readonly Color RadTop = new(0.2704f, 0.8971f, 0.4822f), RadMid = new(0f, 0.5f, 0.0655f), RadBottom = new(0.1116f, 0.2279f, 0f);
        // SR1 quantum largos are lemon (slimeQuantumBase top 1 / 0.98 / 0.37, middle 1 / 0.68 / 0); SR2's lighting turned
        // the old 0.64-green middle orange, so the body sits between the SR1 top and middle colours.
        static readonly Color GoldTop = new(1f, 0.96f, 0.45f), GoldMid = new(1f, 0.82f, 0.16f), GoldBottom = new(0.90f, 0.62f, 0.06f);
        static readonly Color PinkTop = new(1f, 0.4039f, 0.651f), PinkMid = new(1f, 0.2118f, 0.2706f), PinkBottom = new(0.4275f, 0f, 0.2706f);
        static readonly Color LimeTop = new(0.78f, 0.98f, 0.25f), LimeMid = new(0.50f, 0.82f, 0.06f), LimeBottom = new(0.22f, 0.45f, 0.02f);
        static readonly Color BoomTop = new(1f, 0.3882f, 0.3882f), BoomMid = new(0.8078f, 0f, 0f), BoomBottom = new(0.4196f, 0f, 0f);

        /// <summary>
        /// Effect colours of SR2-only bodies that the palette does not reach: Twin's mirror glow, Sloomber's starry gradient
        /// and Hyper's fresnel rim stayed cyan / blue / yellow on green Rad and golden Quantum largos.
        /// </summary>
        static void EffectColors(Material m, Color top, Color mid, Color bottom)
        {
            if (m == null) return;
            m.Col("_TwinTopColor", Color.Lerp(top, Color.white, 0.25f)); m.Col("_TwinMiddleColor", top); m.Col("_TwinBottomColor", mid);
            m.Col("_SloomberTopColor", top); m.Col("_SloomberBottomColor", mid);
            if (m.HasProperty("_HyperFresnelColor"))
            {
                var h = Color.Lerp(top, Color.white, 0.3f); h.a = m.GetColor("_HyperFresnelColor").a;
                m.SetColor("_HyperFresnelColor", h);
            }
        }

        /// <summary>Parts that keep their own look in Rad / Quantum largos (SR1: crystals, honeycomb, effects).</summary>
        static readonly HashSet<string> OwnParts = new(StringComparer.OrdinalIgnoreCase)
        { "CrystalSpikes", "HoneyPlate", "TwinOrbitals", "HyperShades", "HyperAura", "SloomberCloud", "SloomberFX",
          "ShineRotator", "LuckyCatCoin", "FireFlames", "MosaicShell" };
        /// <summary>Rock parts: SR1 keeps them stone grey on a green / golden largo.</summary>
        static readonly HashSet<string> StoneParts = new(StringComparer.OrdinalIgnoreCase) { "RockSpines", "RocksBall" };

        static Color Grey(Color c) { float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; return new Color(l, l, l * 1.04f, c.a); }

        /// <summary>The parent's original materials of a part (not the copy already mixed into the largo palette).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material> Own(CottonLibrary.Forge.LargoBuild c, string element)
        {
            foreach (var app in new[] { c.FirstAppearance, c.SecondAppearance })
            {
                var list = app != null ? app._structures : null;
                if (list == null) continue;
                foreach (var s in list)
                    if (s?.Element != null && string.Equals(s.Element.Name, element, StringComparison.OrdinalIgnoreCase) && s.DefaultMaterials != null)
                        return s.DefaultMaterials;
            }
            return null;
        }

        static void Tint(Material m, Color top, Color mid, Color bottom, float identity, bool grey)
        {
            Color L(string p, Color to)
            {
                var own = m.HasProperty(p) ? m.GetColor(p) : to;
                return grey ? Grey(own) : Color.Lerp(to, own, identity);
            }
            m.Palette(L("_TopColor", top), L("_MiddleColor", mid), L("_BottomColor", bottom));
            if (!m.IsKeywordEnabled("_BODYCOLORING_MULTI")) return;
            foreach (var ch in new[] { "Red", "Green", "Blue" })
            {
                m.Col("_" + ch + "TopColor", L("_" + ch + "TopColor", top));
                m.Col("_" + ch + "MiddleColor", L("_" + ch + "MiddleColor", mid));
                m.Col("_" + ch + "BottomColor", L("_" + ch + "BottomColor", bottom));
            }
        }

        /// <summary>
        /// SR1 Rad / Quantum largos: one palette over the body AND the partner's parts (ears, wings, rings, petals),
        /// keeping <paramref name="identity"/> of the part's own colours. Crystals, honeycomb and effects keep their
        /// original look, rock spikes turn stone grey.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Paint(CottonLibrary.Forge.LargoBuild c, Color top, Color mid, Color bottom, float identity)
        {
            var body = c.BodyMaterial;
            if (body != null) { body.Palette(top, mid, bottom); EffectColors(body, top, mid, bottom); }
            if (c.Appearance != null)
            {
                var p = c.Appearance._colorPalette;
                p.Top = top; p.Middle = mid; p.Bottom = bottom;
                c.Appearance._colorPalette = p; c.Appearance.ColorPaletteNew = p;
                c.Appearance._splatColor = mid; c.Appearance.SetSplatColor();
            }
            int painted = 0, stoned = 0;
            for (int i = 0; i < c.Structures.Count; i++)
            {
                var s = c.Structures[i];
                if (s?.Element == null || s.Element.Type == EType.BODY) continue;
                string name = s.Element.Name;
                // SR2 names the rock parts differently from SR1 (e.g. spine crown / spike ball): match any "Rock" part.
                bool own = OwnParts.Contains(name),
                     stone = StoneParts.Contains(name) || name.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0;
                var mats = s.DefaultMaterials;
                if (own || stone)
                {
                    var orig = Own(c, name);
                    if (orig != null) mats = orig;
                }
                if (mats == null || mats.Length == 0) continue;
                var list = new List<Material>();
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = mats[k];
                    string shader = m != null && m.shader != null ? m.shader.name : "";
                    bool crystal = shader.Contains("Crystal");
                    if (m == null || own || (body != null && m.Pointer == body.Pointer) || !shader.StartsWith("SR/AMP/Slime/Body") || (stone && crystal))
                    { list.Add(m); continue; }
                    var copy = new Material(m) { name = m.name + (stone ? "_stone" : "_classic"), hideFlags = HideFlags.DontUnloadUnusedAsset };
                    if (stone) stoned++;
                    Tint(copy, top, mid, bottom, identity, stone);
                    list.Add(Mat.Track(copy)); painted++;
                }
                var copyStructure = new SlimeAppearanceStructure(s);
                copyStructure.DefaultMaterials = Gc.Array(list);
                c.Structures[i] = copyStructure;
            }
            if (body != null) Visuals.Mats[c.Largo.name] = Mat.Track(body);
            ClassicSlimesMod.Log.Msg($"[classic look] {c.Largo.name}: SR1 palette on body + {painted} part materials ({stoned} stone)");
        }

        static Mesh BodyMesh(CottonLibrary.Forge.LargoBuild c)
        {
            foreach (var s in c.Structures)
            {
                if (s?.Element == null || s.Element.Type != EType.BODY) continue;
                var prefabs = s.Element.Prefabs;
                if (prefabs == null || prefabs.Length == 0 || prefabs[0] == null) continue;
                var smr = prefabs[0].GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) return smr.sharedMesh;
            }
            return null;
        }

        // Rad: green body and green partner parts (SR1). Pink, Boom, Phosphor and Honey keep their own colours
        // (recipes); Quantum paints the lime swirl itself.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RadBuild(CottonLibrary.Forge.LargoBuild c)
        {
            Visuals.RadLargoSurface(c.BodyMaterial, c.Has("Phosphor"));
            if (c.IsRadiant || c.Largo == null) return;
            if (c.Has("Pink") || c.Has("Boom") || c.Has("Phosphor") || c.Has("Honey") || c.Has("Quantum")) return;
            Paint(c, RadTop, RadMid, RadBottom, 0.12f);
        }

        // Quantum: golden body with the SR1 swirl, golden partner parts. Pink stays pink and Rad lime (with the swirl),
        // Boom keeps its red cracked body.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void QuantumBuild(CottonLibrary.Forge.LargoBuild c)
        {
            if (c.IsRadiant || c.Largo == null || c.BodyMaterial == null) return;
            if (c.Has("Boom"))
            {
                // SR1: the red cracked Boom body with golden cracks and a lighter red spiral.
                Visuals.QuantumSurface(c.BodyMaterial, BodyMesh(c), BoomTop, BoomMid, BoomBottom);
                c.BodyMaterial.Col("_CrackColor", new Color(1f, 0.86f, 0.12f));
                Visuals.Mats[c.Largo.name] = Mat.Track(c.BodyMaterial);
                ClassicSlimesMod.Log.Msg($"[classic look] {c.Largo.name}: red body, golden cracks, SR1 spiral");
                return;
            }
            Color top = GoldTop, mid = GoldMid, bottom = GoldBottom;
            if (c.Has("Pink")) { top = PinkTop; mid = PinkMid; bottom = PinkBottom; }
            else if (c.Has("Rad")) { top = LimeTop; mid = LimeMid; bottom = LimeBottom; }
            Visuals.QuantumSurface(c.BodyMaterial, BodyMesh(c), top, mid, bottom);
            Paint(c, top, mid, bottom, 0.12f);
        }

        // Only JIT-compiled when the fork is present (fork-only types).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RegisterStyles()
        {
            const int High = CottonLibrary.Forge.LargoPriority.High, Low = CottonLibrary.Forge.LargoPriority.Low;
            // Rad body and Rad colours dominate (SR1: green Rad largos with green ears / rings / petals).
            // Keep the radiation aura, but never transplant a bright phosphor core into a non-phosphor hybrid.
            CottonLibrary.Forge.LargoStyle.Of("Rad").Body(High).Face(Low).Colors(3f).KeepOwnColors("*").OnBuild(RadBuild);
            // Explicit SR1 reference pairs: inherit the actual stripe/crack/body setup, not Rad's blank green body.
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Pink").Body("Pink").ColorsOf("Pink").OnBuild(ClassicRad);
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Tabby").Body("Tabby").ColorsOf("Rad").OnBuild(ClassicRad);
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Rock").Body("Rock").ColorsOf("Rad").OnBuild(ClassicRad);
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Boom").Body("Boom").ColorsOf("Boom").OnBuild(ClassicRad);
            // SR1: white glowing phosphor body, amber honey body with its comb.
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Phosphor").Body("Phosphor").ColorsOf("Phosphor");
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Honey").Body("Honey").ColorsOf("Honey");
            // SR1: lime quantum swirl with the quantum eyes; green mosaic glass with a plain face (no rad grin blob).
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Quantum").Body("Rad").Face("Quantum").Palette(LimeTop, LimeMid, LimeBottom);
            CottonLibrary.Forge.LargoRecipe.For("Rad", "Mosaic").Body("Rad").Face("Mosaic").ColorsOf("Rad");
            // Quantum: the partner's body shape, painted golden with the swirl (QuantumBuild); golden icon colours.
            CottonLibrary.Forge.LargoStyle.Of("Quantum").Body(Low).Face(Low).Colors(3f).KeepOwnColors("*").OnBuild(QuantumBuild);
            CottonLibrary.Forge.LargoRecipe.For("Quantum", "Pink").Body("Pink").ColorsOf("Pink");
            CottonLibrary.Forge.LargoRecipe.For("Quantum", "Boom").Body("Boom").ColorsOf("Boom");
            CottonLibrary.Forge.LargoRecipe.For("Quantum", "Mosaic").ColorsOf("Quantum");
            // Mosaic: the partner's body and colours (SR1: a pink, red, amber ... mosaic, not a blue blend); MosaicLargo turns
            // the body AND its parts into coated, shimmering glass tiles. "Second" = the other parent's palette.
            CottonLibrary.Forge.LargoStyle.Of("Mosaic").Body(Low).Face(Low).Colors(CottonLibrary.Forge.LargoColorMode.Second).KeepOwnColors("*");
        }
    }
}
