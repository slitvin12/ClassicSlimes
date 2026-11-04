using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;

namespace ClassicSlimes
{
    public static partial class Visuals
    {
        // SR1's Rad shader is not a phosphor core. A lime blob must not replace the pink/red body colour.
        internal static void RadLargoSurface(Material m, bool phosphor)
        {
            if (m == null) return;
            if (!phosphor)
            {
                m.Kw("_ENABLEPHOSCORE_ON", false);
                m.Flt("_EnablePhosCore", 0f);
                m.Flt("_GlowStrength", 0f);
                m.Flt("_RadExoticCore", 0f);
            }
            m.Flt("_GlossPower", 0.52f);
            Mat.Track(m);
        }

        // Native tile body keeps SR2 contact squash. The separate SimpleGlass coat must receive the same
        // vertex warp through ElasticGlassRig; skinning or scaling an unrelated sphere does not match it.
        internal static void MosaicSurface(Material m, Color top, Color mid, Color bottom, float tint, bool recolor = true)
        {
            if (m == null) return;
            var normal = Res.Tex("nrm_mosaic", true);
            if (normal != null)
            {
                m.Tx("_Normal", normal);
                m.Kw("_ENABLENORMALS_ON", true); m.Flt("_EnableNormals", 1f);
                m.Flt("_NormalUV1", 0f); m.Flt("_NormalSmoothTop", 0f);
                m.Flt("_NormalPanX", 0f); m.Flt("_NormalPanY", 0f);
                m.SetTextureScale("_Normal", Vector2.one); m.SetTextureOffset("_Normal", Vector2.zero);
            }
            Color[] ramp = { top, mid, bottom };
            if (recolor)
            {
                m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
                m.Tx("_ColorMask", Res.Tex(MosaicMask, true) ?? Res.Tex("msk_mosaic", true));
                m.Flt("_ColorMaskUV1", 0f);
                m.SetTextureScale("_ColorMask", Vector2.one); m.SetTextureOffset("_ColorMask", Vector2.zero);
                m.Palette(Color.Lerp(new Color(0.08f, 0.20f, 0.48f), top, tint),
                    Color.Lerp(new Color(0.06f, 0.16f, 0.40f), mid, tint),
                    Color.Lerp(new Color(0.05f, 0.12f, 0.32f), bottom, tint));
                string[] channels = { "Red", "Green", "Blue" };
                var colors = MosaicTileColors;
                for (int i = 0; i < channels.Length; i++)
                {
                    // Height is already in the smooth mask; constant channel colours avoid a second, misaligned ramp.
                    m.Col("_" + channels[i] + "TopColor", Color.Lerp(colors[i], top, tint));
                    m.Col("_" + channels[i] + "MiddleColor", Color.Lerp(colors[i], mid, tint));
                    m.Col("_" + channels[i] + "BottomColor", Color.Lerp(colors[i], bottom, tint));
                    ramp[i] = Color.Lerp(colors[i], ramp[i], tint);
                }
                m.Flt("_SmoothR", 0f); m.Flt("_SmoothG", 0f); m.Flt("_SmoothB", 0f);
            }
            m.Flt("_GlossPower", 0.82f);
            m.Flt("_BodyLightingContribution", 0.85f);
            NeutralScatter(m);
            MosaicShimmer(m, ramp[0], ramp[1], ramp[2], tint > 0f ? MosaicLargoShimmer : MosaicBaseShimmer);
            Mat.Track(m);
        }

        /// <summary>Blue / emerald / amber glass of the base Mosaic slime (one per mask channel).</summary>
        static readonly Color[] MosaicTileColors = { new(0.08f, 0.38f, 0.98f), new(0.06f, 0.78f, 0.38f), new(1f, 0.40f, 0.035f) };

        /// <summary>
        /// Strength of the rainbow shimmer (SR2 radiant effect, 0 = off, 1 = a fully holographic radiant body) on the base
        /// Mosaic and on mosaic largos, and how much of the hue wheel is mixed into the slime's own tile colours.
        /// Tuned in game against the SR1 guide: the tiles keep their colour, rainbow tints run over them with the view.
        /// </summary>
        public static float MosaicBaseShimmer = 0.25f, MosaicLargoShimmer = 0.15f, MosaicShimmerRainbow = 0.4f;

        static bool _shimmerWarned;

        /// <summary>
        /// SR1's SR/Slime/Mosaic runs the glass through a hue ramp (tarr_ramp) as the view angle changes. SR2's slime body
        /// shader has the same kind of effect for radiant slimes: noise + parallax picking colours from a ramp. Forced at
        /// part strength with a ramp of the slime's own colours mixed with a hue wheel, the tiles keep their colour and
        /// shimmer in rainbow tints. A radiant variant keeps its own, full radiant look.
        /// </summary>
        internal static void MosaicShimmer(Material m, Color a, Color b, Color c, float force)
        {
            if (m == null || force <= 0f || !m.HasProperty("_ForceRadiantEffect") || m.IsKeywordEnabled("_ENABLERADIANTEFFECT_ON")) return;
            var src = Mat.Find("slimePinkBaseRadiant");
            if (src == null || !src.HasProperty("_RadiantNoisePattern"))
            {
                if (!_shimmerWarned) ClassicSlimesMod.Log.Warning("Mosaic shimmer: no slimePinkBaseRadiant material, glass stays without rainbow tiles");
                _shimmerWarned = true;
                return;
            }
            foreach (var p in new[] { "_RadiantNoisePattern", "_RadiantParallaxPattern", "_RadiantFresnelGradient" }) m.Tx(p, src.GetTexture(p));
            foreach (var p in new[] { "_ParallaxRadiant", "_NoiseIntensityRadiant", "_RadiantEmissionAmount" }) m.Flt(p, src.GetFloat(p));
            m.Tx("_ColorShiftGradient", ShimmerRamp(a, b, c));
            m.Kw("_ENABLERADIANTEFFECT_ON", true); m.Flt("_EnableRadiantEffect", 1f);
            m.Kw("_TOGGLERADIANTEFFECT_ON", true); m.Flt("_ToggleRadiantEffect", 1f);
            m.Flt("_ForceRadiantEffect", force);
        }

        /// <summary>128x1 sRGB ramp: the three colours in a loop, mixed with two turns of the hue wheel (SR1 tarr_ramp).</summary>
        static Texture2D ShimmerRamp(Color a, Color b, Color c)
        {
            // ColorUtility is stripped from the game build ("Method unstripping failed"), so the key is built by hand.
            static string Hex(Color x) => $"{B(x.r):X2}{B(x.g):X2}{B(x.b):X2}";
            static int B(float v) => (int)(Mathf.Clamp01(v) * 255f + 0.5f);
            float k = Mathf.Clamp01(MosaicShimmerRainbow);
            var cols = new[] { a, b, c, a };
            return Gfx.Generated($"mosaicRamp_{Hex(a)}{Hex(b)}{Hex(c)}_{k:0.00}", 128, 1, (x, y) =>
            {
                float t = x / 127f * 3f;
                int i = Mathf.Min((int)t, 2);
                var own = Color.Lerp(cols[i], cols[i + 1], t - i);
                var hue = Color.HSVToRGB(x / 64f % 1f, 1f, 1f);
                var r = Color.Lerp(own, hue, k);
                r.a = 1f;
                return r;
            }, false);
        }

        /// <summary>Share of the largo's own (partner) colours in its mosaic tiles; the rest is the blue/emerald/amber glass.</summary>
        public static float MosaicLargoTint = 0.9f;

        // Effects, auras and orbiting bits are not glass in SR1: they keep their material and get no coat.
        static readonly HashSet<string> MosaicBare = new(StringComparer.OrdinalIgnoreCase)
        { "MosaicShell", "HyperShades", "HyperAura", "SloomberCloud", "SloomberFX", "ShineRotator", "TwinOrbitals",
          "ShadowFX", "ShadowAura", "FireFlames", "LuckyCatCoin" };

        /// <summary>
        /// SR1 mosaic largos are glass all over: ears, spikes, rings, petals included. A part on the slime body shader
        /// gets the same tiles in the largo colours; special part shaders (crystal, tangle flower, no-wobble rocks) keep
        /// their look. The "_MosaicPart" name lets ElasticGlassRig put the clear coat over every one of them.
        /// </summary>
        static Material MosaicPart(Material src, Color top, Color mid, Color bottom, float tint)
        {
            var shader = src.shader != null ? src.shader.name : "";
            if (!shader.StartsWith("SR/AMP/Slime/")) return null;
            var m = new Material(src) { name = src.name + "_MosaicPart", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (shader == "SR/AMP/Slime/Body") MosaicSurface(m, top, mid, bottom, tint, !m.IsKeywordEnabled("_BODYCOLORING_SLOOMBER"));
            else { m.Flt("_GlossPower", 0.82f); Mat.Track(m); }
            return m;
        }

        // Post-process all default/radiant appearances, including a largo whose body came from the other parent.
        // Keep its stripe/crack keywords and textures: the mosaic mask and normal map are separate shader features.
        static void MosaicNative(SlimeAppearance app, float tint)
        {
            var pal = app._colorPalette;
            var source = app._structures;
            // Bodies first: ears, rings and wings that share the body material must get the same new material.
            var bodies = new Dictionary<IntPtr, Material>();
            foreach (var s in source)
            {
                if (s?.Element == null || s.Element.Type != EType.BODY) continue;
                var mats = s.DefaultMaterials;
                if (mats == null || mats.Length == 0 || mats[0] == null) continue;
                var src = mats[0];
                if (src.name.Contains("_MosaicNative") || bodies.ContainsKey(src.Pointer)) continue;
                Material m;
                if (src.shader != null && src.shader.name == "SR/AMP/Slime/Body")
                    m = new Material(src) { name = src.name + "_MosaicNative", hideFlags = HideFlags.DontUnloadUnusedAsset };
                else
                    m = Mat.Clone(src.name + "_MosaicNative", "slimePinkBase", "slimeRockBase");
                if (m == null) continue;
                // Sloomber's star mode is exclusive with MULTI; keep that parent's markings and add only tile bevels.
                MosaicSurface(m, pal.Top, pal.Middle, pal.Bottom, tint, !m.IsKeywordEnabled("_BODYCOLORING_SLOOMBER"));
                bodies[src.Pointer] = m;
            }
            var structures = new List<SlimeAppearanceStructure>();
            int parts = 0;
            foreach (var s in source)
            {
                if (s?.Element == null) { structures.Add(s); continue; }
                if (s.Element.Name == "MosaicShell") continue;
                var mats = s.DefaultMaterials;
                if (mats == null || mats.Length == 0) { structures.Add(s); continue; }
                bool bare = MosaicBare.Contains(s.Element.Name), changed = false;
                var list = new List<Material>();
                for (int k = 0; k < mats.Length; k++)
                {
                    var mk = mats[k];
                    if (mk == null) { list.Add(null); continue; }
                    if (mk.name.Contains("MosaicCoat")) { changed = true; continue; }
                    if (bodies.TryGetValue(mk.Pointer, out var body)) { list.Add(body); changed = true; if (s.Element.Type != EType.BODY) parts++; continue; }
                    if (k == 0 && !bare && s.Element.Type != EType.BODY && !mk.name.Contains("_Mosaic"))
                    {
                        var part = MosaicPart(mk, pal.Top, pal.Middle, pal.Bottom, tint);
                        if (part != null) { list.Add(part); changed = true; parts++; continue; }
                    }
                    list.Add(mk);
                }
                if (!changed) { structures.Add(s); continue; }
                var copy = new SlimeAppearanceStructure(s);
                copy.DefaultMaterials = Gc.Array(list);
                structures.Add(copy);
            }
            app._structures = Gc.Array(structures);
            if (parts > 0) ClassicSlimesMod.Log.Msg($"[mosaic] {app.name}: {bodies.Count} body + {parts} parts in glass");
        }

        /// <summary>How much lighter the SR1 spiral is than the quantum largo body (top level, towards white).</summary>
        public static float QuantumBandLift = 0.4f;

        // SR1 quantum largo body: one palette and the single SR1 spiral band, baked into this body mesh's UVs.
        // Crack / stripe keywords of the partner body stay; Sloomber's star mode excludes MULTI (palette only).
        // The band is a lighter shade of the body at every height (SR1: a glossy lemon slime, not a white stripe).
        internal static void QuantumSurface(Material m, Mesh mesh, Color top, Color mid, Color bottom)
        {
            if (m == null) return;
            m.Palette(top, mid, bottom);
            if (m.IsKeywordEnabled("_BODYCOLORING_SLOOMBER")) { Mat.Track(m); return; }
            m.Kw("_ENABLETWINEFFECT_ON", false); m.Flt("_EnableTwinEffect", 0f);
            m.Kw("_ENABLEVORTEX_ON", false); m.Flt("_EnableVortex", 0f);
            m.Kw("_ENABLEHYPEREFFECT_ON", false); m.Flt("_EnableHyperEffect", 0f);
            m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
            Texture2D swirl = null; int set = 0;
            try { swirl = Swirl.Shared(mesh, out set); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Quantum largo swirl: " + e.Message); }
            if (swirl != null)
            {
                m.Tx("_ColorMask", swirl);
                m.SetTextureScale("_ColorMask", Vector2.one); m.SetTextureOffset("_ColorMask", Vector2.zero);
                m.Flt("_ColorMaskUV1", set);
            }
            else
            {
                m.Tx("_ColorMask", Res.Tex("msk_quantumSpiral", true, false) ?? Gfx.SwirlMask);
                m.SetTextureScale("_ColorMask", Gfx.SwirlScale); m.SetTextureOffset("_ColorMask", Gfx.SwirlOffset);
                m.Flt("_ColorMaskUV1", 0f);
            }
            m.Col("_RedTopColor", Color.Lerp(top, Color.white, QuantumBandLift));
            m.Col("_RedMiddleColor", Color.Lerp(mid, top, 0.65f));
            m.Col("_RedBottomColor", Color.Lerp(bottom, mid, 0.65f));
            foreach (var ch in new[] { "Green", "Blue" })
            {
                m.Col("_" + ch + "TopColor", top); m.Col("_" + ch + "MiddleColor", mid); m.Col("_" + ch + "BottomColor", bottom);
            }
            m.Flt("_SmoothR", 0f); m.Flt("_SmoothG", 0f); m.Flt("_SmoothB", 0f);
            m.Flt("_GlossPower", 0.72f);
            Mat.Track(m);
        }
    }
}
