using System;
using System.Collections.Generic;
using CottonLibrary;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using UnityEngine;
using Object = UnityEngine.Object;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;
using Expr = Il2Cpp.SlimeFace.SlimeExpression;

namespace ClassicSlimes
{
    /// <summary>Second pass of the SR1 looks: Rad (glowing face, radiation dome), Mosaic (glass shell + sparkles), Glitch (pixels).</summary>
    public static partial class Visuals
    {
        static readonly Dictionary<string, Texture> _gameTex = new();

        /// <summary>A texture that ships with SR2 (looked up by name once).</summary>
        public static Texture GameTex(string name)
        {
            if (_gameTex.TryGetValue(name, out var t) && t != null) return t;
            foreach (var x in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (x != null && x.name == name) { _gameTex[name] = x; return x; }
            _gameTex[name] = null;
            return null;
        }

        static Material Keep(Material m, string key)
        {
            if (m == null) return null;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Mats[key] = m;
            return Mat.Track(m);
        }

        // ------------------------------------------------------------------ faces

        static SlimeFace _radFace;
        static SlimeFace RadFace(SlimeFace src)
        {
            if (_radFace != null || src == null) return _radFace;
            // SR2-style rad icon: navy eyes with a lime iris, navy smile (plain face shaders, no glow over the face)
            _radFace = Faces.Clone(src, "faceSlimeRad",
                e =>
                {
                    var m = Object.Instantiate(e); m.name = e.name + "_Rad"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    m.Col("_EyeRed", new Color(0.09f, 0.12f, 0.21f, 1f));
                    m.Col("_EyeGreen", new Color(0.66f, 0.90f, 0.28f, 1f));
                    m.Col("_EyeBlue", new Color(0.95f, 1f, 0.80f, 1f));
                    Mats["RadEyes_" + e.name] = m;
                    return m;
                },
                mo =>
                {
                    var m = Object.Instantiate(mo); m.name = mo.name + "_Rad"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    m.Col("_MouthTop", new Color(0.09f, 0.12f, 0.21f, 1f));
                    m.Col("_MouthMid", new Color(0.14f, 0.20f, 0.30f, 1f));
                    m.Col("_MouthBot", new Color(0.45f, 0.76f, 0.30f, 1f));
                    Mats["RadMouth_" + mo.name] = m;
                    return m;
                });
            return _radFace;
        }

        static SlimeFace _glitchFaceSR1, _glitchFaceSR2;
        public static SlimeFace GlitchFace(bool sr1) => sr1 ? _glitchFaceSR1 : _glitchFaceSR2;

        static string GlitchAtlas(Expr ex)
        {
            string n = ex.ToString().ToUpperInvariant();
            if (n.Contains("BLINK") || n.Contains("SLEEP") || n.Contains("TIRED")) return "face_glitch_blink";
            if (n.Contains("SCARED") || n.Contains("WINCE") || n.Contains("FEAR")) return "face_glitch_scared";
            if (n.Contains("ALARM") || n.Contains("ANGRY") || n.Contains("ATTACK") || n.Contains("FEROCIOUS")) return "face_glitch_alarm";
            if (n.Contains("AWE") || n.Contains("INVOKE")) return "face_glitch_awe";
            if (n.Contains("ELATED") || n.Contains("CHOMP") || n.Contains("FED")) return "face_glitch_elated";
            return "face_glitch_happy";
        }

        static void BuildGlitchFaces(SlimeFace src)
        {
            if (src == null || _glitchFaceSR2 != null) return;
            // SR1 icon: white square eyes and a big white mouth
            Action<Material> eyeCols = m =>
            {
                m.Col("_EyeRed", new Color(0.97f, 0.97f, 1f, 1f));
                m.Col("_EyeGreen", new Color(0.85f, 0.9f, 1f, 1f));
                m.Col("_EyeBlue", new Color(1f, 1f, 1f, 1f));
            };
            Action<Material> mouthCols = m =>
            {
                m.Col("_MouthTop", new Color(0.55f, 0.12f, 0.38f, 1f));
                m.Col("_MouthMid", new Color(0.97f, 0.95f, 1f, 1f));
                m.Col("_MouthBot", new Color(1f, 1f, 1f, 1f));
            };
            _glitchFaceSR2 = Faces.Clone(src, "faceSlimeGlitch",
                e => { var m = Object.Instantiate(e); m.name = e.name + "_Glitch"; m.hideFlags = HideFlags.DontUnloadUnusedAsset; eyeCols(m); return m; },
                mo => { var m = Object.Instantiate(mo); m.name = mo.name + "_Glitch"; m.hideFlags = HideFlags.DontUnloadUnusedAsset; mouthCols(m); return m; });

            // SR1 atlases per expression (eyes and mouth of SR1 share one atlas)
            var exprByEyes = new Dictionary<IntPtr, Expr>();
            var exprByMouth = new Dictionary<IntPtr, Expr>();
            foreach (var (ex, e, mo) in Faces.Read(src))
            {
                if (e != null && !exprByEyes.ContainsKey(e.Pointer)) exprByEyes[e.Pointer] = ex;
                if (mo != null && !exprByMouth.ContainsKey(mo.Pointer)) exprByMouth[mo.Pointer] = ex;
            }
            _glitchFaceSR1 = Faces.Clone(src, "faceSlimeGlitchSR1",
                e =>
                {
                    var m = Object.Instantiate(e); m.name = e.name + "_GlitchSR1"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var atlas = Res.Tex(exprByEyes.TryGetValue(e.Pointer, out var ex) ? GlitchAtlas(ex) : "face_glitch_happy", true, false);
                    m.Tx("_FaceAtlas", atlas); eyeCols(m); return m;
                },
                mo =>
                {
                    var m = Object.Instantiate(mo); m.name = mo.name + "_GlitchSR1"; m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var atlas = Res.Tex(exprByMouth.TryGetValue(mo.Pointer, out var ex) ? GlitchAtlas(ex) : "face_glitch_happy", true, false);
                    m.Tx("_FaceAtlas", atlas); mouthCols(m); return m;
                });
        }

        // ------------------------------------------------------------------ Rad
        public static void Rad(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            // SR1: SR/AMP/Slime/Body/Rad (self-lit green) -> SR2 phosphor body (phos-core glow) in rad greens
            var m = BodyMat("Rad", s, "slimePhosphorBase", "slimePinkBase");
            if (m != null)
            {
                m.Col("_GlowTop", new Color(0.30f, 1.35f, 0.38f, 1f));
                m.Flt("_GlowStrength", 0.85f);
                m.Flt("_GlowSpeed", 0.7f);
                m.Flt("_GlowMin", 0.35f);
                m.Flt("_GlowMax", 1f);
                m.Flt("_GlossPower", 0.55f);
                SetBodyMaterial(app, m);
            }
            try { var f = RadFace(app._face); if (f != null) app._face = f; }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Rad face: " + e.Message); }
            Behaviours.Add<RadSlime>(s.Def);
        }

        public static void RadLargo(SlimeDefinition d)
        {
            ClearRadiant(d);
            Behaviours.Add<RadSlime>(d);
        }

        /// <summary>Dome looks (index = Cfg RadDomeStyle / DevCmd "dome i").</summary>
        public static readonly string[] DomeNames = { "bubble", "glass", "shadowAura", "hyperAura", "solarShield", "dreamBubble", "fresnel", "bubbleFx" };
        static readonly Material[] _domes = new Material[8];
        public static int DomeStyle = 0;
        public static bool DomeIsBillboard(int i) => i == 0 || i == 7;

        static Texture2D RadBubbleTex => Gfx.BubbleTex("radBubble", new Color(0.30f, 0.80f, 0.32f), new Color(0.62f, 1f, 0.55f), 0.16f, 0.78f, 1.7f, 7, 4242);

        public static Material Dome(int i)
        {
            i = Mathf.Clamp(i, 0, _domes.Length - 1);
            if (_domes[i] != null) return _domes[i];
            Material m = null;
            switch (i)
            {
                case 0:   // SR2-icon-like bubble on HDRP Unlit (transparent, alpha blended)
                    m = Mat.Clone("RadDome0", "DefaultHDParticleMaterial") ?? Mat.CloneByShader("RadDome0", "HDRP/Unlit");
                    if (m == null) break;
                    m.Tx("_UnlitColorMap", RadBubbleTex);
                    m.Col("_UnlitColor", new Color(1f, 1f, 1f, 1f));
                    break;
                case 7:   // same bubble on SR2's particle shader
                    m = Mat.Clone("RadDome7", "CFX_Particles", "DazedStars");
                    if (m == null) break;
                    foreach (var p in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_MainTexture" }) m.Tx(p, RadBubbleTex);
                    foreach (var p in new[] { "_TintColor", "_Tint", "_Color", "_BaseColor" }) m.Col(p, new Color(1f, 1f, 1f, 1f));
                    m.Flt("_DepthFadeDistance", 0.25f);
                    break;
                case 1:
                    m = Mat.Clone("RadDome1", "envLabGlass");
                    if (m == null) break;
                    m.Col("_Color", new Color(0.18f, 0.85f, 0.25f, 0f));
                    m.Flt("_Opacity", 0.12f);
                    m.Col("_ReflectionTint", new Color(0.4f, 1f, 0.45f, 0f));
                    m.Flt("_ReflectionIntensity", 25f);
                    m.Tx("_NormalMap", GameTex("dptNoiseClouds"));
                    m.Flt("_DistortionStrength", 8f);
                    break;
                case 2:
                    m = Mat.Clone("RadDome2", "ShadowAura") ?? Mat.CloneByShader("RadDome2", "SR/AMP/Particle/ShadowAuraLight");
                    if (m == null) break;
                    m.Col("_Color", new Color(0.12f, 0.75f, 0.18f, 1f));
                    m.Flt("_Opacity", 0.35f);
                    m.Flt("_softness", 0.8f);
                    break;
                case 3:
                    m = Mat.CloneByShader("RadDome3", "HyperSlimeAura");
                    if (m == null) break;
                    foreach (var p in new[] { "_Color", "_AuraColor", "_FresnelColor", "_TintColor", "_MainColor" }) m.Col(p, new Color(0.25f, 1f, 0.3f, 1f));
                    break;
                case 4:
                    m = Mat.CloneByShader("RadDome4", "SR/RanchTech/SolarShield");
                    if (m == null) break;
                    foreach (var p in new[] { "_Color", "_ShieldColor", "_FresnelColor", "_TintColor", "_MainColor", "_EdgeColor" }) m.Col(p, new Color(0.25f, 1f, 0.3f, 1f));
                    break;
                case 5:
                    m = Mat.Clone("RadDome5", "DreamBubbleResource") ?? Mat.CloneByShader("RadDome5", "Custom/DreamBubbleResource");
                    if (m == null) break;
                    foreach (var p in new[] { "_Color", "_BubbleColor", "_FresnelColor", "_TintColor", "_MainColor", "_RimColor" }) m.Col(p, new Color(0.25f, 1f, 0.3f, 1f));
                    break;
                default:
                    m = Mat.Clone("RadDome6", "SphereLightRainbowFresnel Particle");
                    if (m == null) break;
                    m.Tx("_GradientRamp", Mat.Ramp("RadDomeRamp", new Color(0.02f, 0.18f, 0.03f), new Color(0.10f, 0.75f, 0.16f), new Color(0.45f, 1f, 0.35f), new Color(0.85f, 1f, 0.6f)));
                    m.Flt("_GradientSpeed", 0.25f);
                    m.Flt("_BrightnessMultiplier", 1.6f);
                    m.Flt("_FresnelPower", 3.0f);
                    break;
            }
            _domes[i] = Keep(m, "RadDome" + i);
            return _domes[i];
        }

        /// <summary>The radiation dome object: its local scale is the dome diameter (set by RadSlime).</summary>
        public static GameObject MakeDome(Transform parent, Vector3 localPos, float diameter, int style = -1)
        {
            int i = Mathf.Clamp(style < 0 ? DomeStyle : style, 0, _domes.Length - 1);
            var mat = Dome(i);
            if (mat == null && i != 0) { i = 0; mat = Dome(0); }
            if (!DomeIsBillboard(i)) return Sphere("RadAura", parent, localPos, diameter, mat);
            var root = new GameObject("RadAura");
            if (parent != null) { root.transform.SetParent(parent, false); root.layer = parent.gameObject.layer; }
            root.transform.localPosition = localPos;
            root.transform.localScale = Vector3.one * diameter;
            var q = Gfx.MeshObj("RadBubble", root.transform, Gfx.Quad, mat, Vector3.zero, 1f);
            q.AddComponent<SphereBillboard>();
            return root;
        }

        public static Material RadDomeMat => Dome(DomeStyle);

        static Material _radCore;
        /// <summary>SR1 slimeRadAura_core: SR/AMP/Slime/FX/PhosCore Aura with radial perlin noise (same shader exists in SR2).</summary>
        public static Material RadCoreMat
        {
            get
            {
                if (_radCore != null) return _radCore;
                var m = Mat.Clone("RadCore", "FX Phospher", "FX GoldGlow");
                if (m == null) return null;
                m.Col("_Color", new Color(0.12f, 0.9f, 0.2f, 1f));
                m.Tx("_MainTexture", GameTex("perlinnoise") ?? Res.Tex("perlinnoise"));
                m.Col("_RedSpeedXYScaleZW", new Color(0f, 0.5f, 1f, 1f));
                m.Col("_GreenSpeedXYScaleZW", new Color(0f, 0.25f, 1f, 1.5f));
                m.Col("_BlueSpeedXYScaleZW", new Color(0f, -0.5f, 1f, 1f));
                m.Flt("_GlowMin", 1f); m.Flt("_GlowMax", 0.3f); m.Flt("_GlowSpeed", 0.8f);
                m.Flt("_DepthFade", 1f);
                _radCore = Keep(m, "RadCore");
                return _radCore;
            }
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 localPos, float diameter, Material mat) =>
            Gfx.MeshObj(name, parent, Gfx.SphereMesh, mat, localPos, diameter);

        // ------------------------------------------------------------------ Mosaic
        static Material _shell;
        /// <summary>SR1 SR/Slime/Mosaic: glass tiles over the slime -> SR2 SimpleGlass with the SR1 tile normal map and a tile/grout base map.</summary>
        static Material ShellMat()
        {
            if (_shell != null) return _shell;
            var m = Mat.Clone("MosaicGlass", "envLabGlass", "SimpleGlass");
            if (m == null) return null;
            // clear glossy coat over the coloured tiles (reflections + faceted highlights only)
            m.Col("_Color", new Color(0.70f, 0.90f, 1f, 0f));
            m.Flt("_Opacity", 0.10f);
            m.Flt("_Smoothness", 0.97f);
            m.Flt("_Metallic", 0.2f);
            m.Tx("_NormalMap", Res.Tex("nrm_mosaic", true));
            m.Flt("_NormalStrength", 0.8f);
            m.Flt("_BaseMapAlpha", 0f);
            m.Flt("_BaseMapColor", 0f);
            m.Flt("_DistortionStrength", 2f);
            m.Flt("_ReflectionIntensity", 60f);
            m.Col("_ReflectionTint", new Color(0.8f, 0.92f, 1f, 0f));
            _shell = Keep(m, "MosaicShell");
            return _shell;
        }

        /// <summary>
        /// SR1 slimeMosaic_base: msk_mosaic (R/G/B tiles) indexes a rainbow ramp, nrm_mosaic bevels each tile.
        /// SR2 equivalent: the multi-colour body shader (colour mask + normals) with a vertical rainbow per channel,
        /// blue/teal at the top, green/yellow in the middle, orange at the bottom; black mask = bright grout.
        /// The template already uses exactly the keyword set we need (alpha test + multi colour + normals).
        /// </summary>
        static Material MosaicBodyMat(SlimeSpec s)
        {
            var m = Mat.Clone("MosaicBody", "FlutterDefaultPinkDefault_Wings", "slimeFlutterWing", "slimeRingtailBase", "slimePinkBase");
            if (m == null) return null;
            // SR1 mosaic: deep blue glass with green / orange / yellow / sky-blue tiles. SR2's body gradient mostly blends
            // middle and bottom, so the blue base is kept on all three stops and the tile groups shift colour downwards.
            m.Palette(new Color(0.15f, 0.40f, 1f), new Color(0.12f, 0.32f, 0.95f), new Color(0.10f, 0.28f, 0.85f));
            m.Tx("_ColorMask", Res.Tex("msk_mosaic", true));
            m.Flt("_ColorMaskUV1", 0f);
            m.Col("_RedTopColor", new Color(0.45f, 0.80f, 1f)); m.Col("_RedMiddleColor", new Color(0.35f, 0.90f, 0.30f)); m.Col("_RedBottomColor", new Color(1f, 0.50f, 0.10f));
            m.Col("_GreenTopColor", new Color(0.20f, 0.50f, 1f)); m.Col("_GreenMiddleColor", new Color(0.30f, 0.75f, 1f)); m.Col("_GreenBottomColor", new Color(0.40f, 0.90f, 0.30f));
            m.Col("_BlueTopColor", new Color(1f, 0.85f, 0.20f)); m.Col("_BlueMiddleColor", new Color(1f, 0.65f, 0.15f)); m.Col("_BlueBottomColor", new Color(1f, 0.40f, 0.10f));
            m.Flt("_SmoothR", 0f); m.Flt("_SmoothG", 0f); m.Flt("_SmoothB", 0f);
            m.Tx("_Normal", Res.Tex("nrm_mosaic", true));
            m.Flt("_NormalUV1", 0f);
            m.Flt("_NormalSmoothTop", 0f);
            m.Flt("_NormalPanX", 0f); m.Flt("_NormalPanY", 0f);
            m.Flt("_GlossPower", 0.9f);
            m.Flt("_BodyLightingContribution", 0.85f);
            Keep(m, "Mosaic");
            return m;
        }

        static SlimeAppearanceStructure _shellStruct;
        static SlimeAppearanceStructure ShellStructure(SlimeSpec s, SlimeAppearance app)
        {
            var mesh = Res.Mesh("mosaic_LOD0");
            var mat = ShellMat();
            if (mesh == null || mat == null) return null;
            if (_shellStruct == null)
            {
                var obj = SkinnedObject(BodyTemplate(), "mosaic_shell_LOD0", mesh, BoneTop, BonesMosaic, false);
                _shellStruct = Structure(app.Structures[0], "MosaicShell", EType.SURFACE, mat, false, obj);
            }
            return new SlimeAppearanceStructure(_shellStruct);
        }

        public static void Mosaic(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            // SR1 slimeMosaicBase: blue top, green middle, orange-red bottom (seen through the glass)
            var body = MosaicBodyMat(s);
            if (body != null) SetBodyMaterial(app, body);
            var st = ShellStructure(s, app);
            if (st != null) InsertStructure(app, st);
            Behaviours.Add<MosaicSlime>(s.Def);
        }

        public static void MosaicLargo(SlimeDefinition d)
        {
            ClearRadiant(d);
            var s = SlimeFactory.Spec("Mosaic");
            try
            {
                var app = d.AppearancesDefault[0];
                if (s != null && !HasElement(app, "MosaicShell"))
                {
                    var st = ShellStructure(s, app);
                    if (st != null) InsertStructure(app, st);
                }
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Mosaic largo shell {d.name}: {e.Message}"); }
            Behaviours.Add<MosaicSlime>(d);
        }

        static Material _star, _flare, _pixel;
        /// <summary>SR1 fx_mosaicGlint star on SR2's additive sparkle material.</summary>
        public static Material StarMat
        {
            get
            {
                if (_star != null) return _star;
                var m = Mat.Clone("MosaicStar", "Gold Slime Sparkle");
                if (m == null) return null;
                m.Tx("_MainTex", Res.Tex("fx_star4", false, false));
                m.Col("_TintColor", new Color(0.62f, 0.66f, 0.7f, 0.7f));
                _star = Keep(m, "MosaicStar");
                return _star;
            }
        }

        /// <summary>SR2's procedural diamond glint flare.</summary>
        public static Material FlareMat
        {
            get
            {
                if (_flare != null) return _flare;
                var m = Mat.Clone("MosaicFlare", "fxDiamondGlint");
                if (m == null) return null;
                m.Col("_Color", new Color(18f, 20f, 24f, 1f));
                _flare = Keep(m, "MosaicFlare");
                return _flare;
            }
        }

        public static Material PixelMat
        {
            get
            {
                if (_pixel != null) return _pixel;
                var m = Mat.Clone("GlitchPixel", "Gold Slime Sparkle");
                if (m == null) return null;
                m.Tx("_MainTex", Gfx.Square);
                m.Col("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.6f));
                _pixel = Keep(m, "GlitchPixel");
                return _pixel;
            }
        }

        public static Material GlintMat => FlareMat;

        public static MosaicGlint MakeGlint(MosaicSlime owner, Vector3 pos)
        {
            var go = new GameObject("MosaicGlint");
            go.transform.position = pos;
            var core = Gfx.Billboard("GlintCore", go.transform, FlareMat, Vector3.zero, 1.1f);
            var bb = core.GetComponent<Billboard>(); if (bb != null) bb.Spin = 25f;
            Gfx.Sparkles("GlintStars", go.transform, Vector3.zero, StarMat, Color.white, new Color(0.75f, 0.9f, 1f, 1f), 5f, 0.6f, 0.25f, 0.5f, 0.45f, 8);
            var g = go.AddComponent<MosaicGlint>();
            g.Owner = owner;
            return g;
        }

        // ------------------------------------------------------------------ Glitch
        public static Texture2D[] GlitchMasks;

        public static void Glitch(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            // SR1 SR/Slime/Body Glitch: pink body broken into lighter/darker pixel cells with a mint rim
            var m = Mat.Clone("GlitchBody", "HyperDefaultRingtailDefault_Body", "slimeHyperBase", "slimePinkBase");
            if (m != null)
            {
                m.Palette(new Color(0.96f, 0.47f, 0.75f), new Color(0.89f, 0.32f, 0.64f), new Color(0.72f, 0.16f, 0.50f));
                m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
                GlitchMasks = new[] { Gfx.PixelMask(11, 16, 0.2f, 0.16f, 0.07f), Gfx.PixelMask(23, 16, 0.2f, 0.16f, 0.07f), Gfx.PixelMask(37, 16, 0.24f, 0.2f, 0.1f) };
                m.Tx("_ColorMask", GlitchMasks[0]);
                m.Flt("_ColorMaskUV1", 0f);
                m.Col("_RedTopColor", new Color(1f, 0.68f, 0.87f)); m.Col("_RedMiddleColor", new Color(1f, 0.58f, 0.82f)); m.Col("_RedBottomColor", new Color(0.92f, 0.46f, 0.74f));
                m.Col("_GreenTopColor", new Color(0.78f, 0.2f, 0.58f)); m.Col("_GreenMiddleColor", new Color(0.68f, 0.12f, 0.5f)); m.Col("_GreenBottomColor", new Color(0.55f, 0.07f, 0.4f));
                m.Col("_BlueTopColor", new Color(1f, 0.88f, 0.96f)); m.Col("_BlueMiddleColor", new Color(1f, 0.82f, 0.94f)); m.Col("_BlueBottomColor", new Color(0.96f, 0.72f, 0.88f));
                m.Kw("_ENABLEHYPEREFFECT_ON", true); m.Flt("_EnableHyperEffect", 1f);
                m.Col("_HyperFresnelColor", new Color(0.32f, 1f, 0.82f, 0f));
                m.Flt("_HyperBodyColorAlbedoIntensity", 0.95f);
                m.Flt("_HyperFresnelIntensity", 2.2f);
                m.Flt("_GlossPower", 0.35f);
                Keep(m, "Glitch");
                SetBodyMaterial(app, m);
            }
            try
            {
                BuildGlitchFaces(app._face);
                var f = GlitchFace(Cfg.GlitchSR1Face.Value) ?? GlitchFace(false);
                if (f != null) app._face = f;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Glitch face: " + e.Message); }
            Voices(s, "vo_glitch_fun_01", "vo_glitch_fear_01", "vo_glitch_damage_01", "vo_glitch_jump_01");
            try
            {
                var vac = s.Def.prefab.GetComponent<Vacuumable>();
                if (vac != null) vac.Size = VacuumableSize.LARGE; // can't be stored: it gets debugged instead
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Glitch vac size: " + e.Message); }
            Behaviours.Add<GlitchSlime>(s.Def);
        }
    }
}
