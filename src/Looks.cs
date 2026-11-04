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

        /// <summary>Copy of a face material on the glowing variant of its shader (eyeInvoke / mouthInvoke templates).</summary>
        static Material GlowFaceMat(Material src, bool eyes, string suffix)
        {
            var m = Object.Instantiate(src);
            m.name = src.name + suffix;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var tpl = Mat.Find(eyes ? "eyeInvoke" : "mouthInvoke");
            if (tpl != null && m.shader != tpl.shader)
            {
                var atlas = src.HasProperty("_FaceAtlas") ? src.GetTexture("_FaceAtlas") : null;
                m.shader = tpl.shader;
                if (atlas != null) m.SetTexture("_FaceAtlas", atlas);
            }
            if (tpl != null)
            {
                if (tpl.HasProperty("_GlowNoise")) m.Tx("_GlowNoise", tpl.GetTexture("_GlowNoise"));
                if (tpl.HasProperty("_GlowNoiseSpeed")) m.Flt("_GlowNoiseSpeed", tpl.GetFloat("_GlowNoiseSpeed"));
            }
            // swapping the shader resets the queue to 2000; stock faces (and eyeInvoke) draw at 2450 after the body
            m.renderQueue = tpl != null ? tpl.renderQueue : src.renderQueue;
            return m;
        }

        /// <summary>SR1 rad grins with a big open mouth (icon / slimepedia): SR2's elated mouth shape on the happy face.</summary>
        public static string RadHappyMouth = "mouthElated";

        static SlimeFace _radFace;
        static SlimeFace RadFace(SlimeFace src)
        {
            if (_radFace != null || src == null) return _radFace;
            // SR1 rad: small glowing lime eyes, the open mouth is dark green with a glowing lime / yellow inside
            _radFace = Faces.Clone(src, "faceSlimeRad",
                e =>
                {
                    var m = GlowFaceMat(e, true, "_Rad");
                    m.Col("_EyeRed", new Color(0.62f, 1f, 0.16f, 1f));
                    m.Col("_EyeGreen", new Color(0.86f, 1f, 0.34f, 1f));
                    m.Col("_EyeBlue", new Color(1f, 1f, 0.85f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));   // the glow layer drew a white blob + extra smile arcs
                    Mats["RadEyes_" + e.name] = m;
                    return m;
                },
                mo =>
                {
                    var m = GlowFaceMat(mo, false, "_Rad");
                    if (mo.name == "mouthHappy" && !string.IsNullOrEmpty(RadHappyMouth))
                    {
                        var big = Mat.Find(RadHappyMouth);
                        if (big != null && big.HasProperty("_FaceAtlas")) m.Tx("_FaceAtlas", big.GetTexture("_FaceAtlas"));
                    }
                    m.Col("_MouthTop", new Color(0.02f, 0.20f, 0.05f, 1f));
                    m.Col("_MouthMid", new Color(0.50f, 0.95f, 0.14f, 1f));
                    m.Col("_MouthBot", new Color(0.88f, 1f, 0.32f, 1f));
                    m.Col("_GlowColor", new Color(0f, 0f, 0f, 1f));   // the glow layer drew a white blob + extra smile arcs
                    Mats["RadMouth_" + mo.name] = m;
                    return m;
                });
            return _radFace;
        }

        static SlimeFace _quantumFace;
        public static SlimeFace QuantumFace(SlimeFace src)
        {
            if (_quantumFace != null || src == null) return _quantumFace;
            // SR1 quantum: small glowing yellow eyes with a darker orange edge (so they still read on the golden body);
            // the mouth stays stock
            _quantumFace = Faces.Clone(src, "faceSlimeQuantum",
                e =>
                {
                    var m = GlowFaceMat(e, true, "_Quantum");
                    m.Col("_EyeRed", QuantumEye[0]);     // main eye
                    m.Col("_EyeGreen", QuantumEye[1]);   // inner
                    m.Col("_EyeBlue", QuantumEye[2]);    // highlight
                    m.Col("_GlowColor", QuantumEye[3]);
                    Mats["QuantumEyes_" + e.name] = m;
                    return m;
                },
                null);
            return _quantumFace;
        }

        public static readonly Color[] QuantumEye =
        {
            new Color(0.80f, 0.40f, 0.02f, 1f), new Color(1f, 0.90f, 0.22f, 1f), new Color(1f, 1f, 0.85f, 1f), new Color(0f, 0f, 0f, 1f)
        };

        static SlimeFace _qsFace;
        /// <summary>SR1 quicksilver: just the two eyes, no mouth.</summary>
        public static SlimeFace QuicksilverFace(SlimeFace src)
        {
            if (_qsFace != null || src == null) return _qsFace;
            _qsFace = Faces.Clone(src, "faceSlimeQuicksilver", null, NoMouth);
            return _qsFace;
        }

        /// <summary>A mouth that draws nothing (SR2's own "Null Render" face shader; fallback: an empty face atlas).</summary>
        static Material NoMouth(Material mo)
        {
            var m = Object.Instantiate(mo);
            m.name = mo.name + "_None";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var nul = Mat.Find("Null Render");
            if (nul != null && nul.shader != null) m.shader = nul.shader;
            else m.Tx("_FaceAtlas", Gfx.Generated("noMouth", 4, 4, (x, y) => new Color(0f, 0f, 0f, 0f), true));
            Mats["QsMouth_" + mo.name] = m;
            return m;
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
            // SR1: SR/AMP/Slime/Body/Rad (self-lit, very saturated green) -> SR2 phosphor body (phos-core glow).
            // Not the hyper-phosphor material: its polar hyper noise + vortex drew lots of smile-like arcs on the body.
            var m = BodyMat("Rad", s, "slimePhosphorBase", "slimePinkBase");
            if (m != null)
            {
                // Original SR1 body palette; subtle self-light, not a fluorescent phosphor blob.
                m.Palette(new Color(0.2704f, 0.8971f, 0.4822f), new Color(0f, 0.5f, 0.0655f), new Color(0.1116f, 0.2279f, 0f));
                m.Col("_GlowTop", new Color(0.27f, 0.90f, 0.48f, 1f));
                m.Flt("_GlowStrength", 0.16f);
                m.Flt("_GlowSpeed", 0.7f);
                m.Flt("_GlowMin", 0.15f);
                m.Flt("_GlowMax", 0.4f);
                m.Flt("_GlossPower", 0.6f);
                m.Kw("_ENABLEHYPEREFFECT_ON", false); m.Flt("_EnableHyperEffect", 0f);
                m.Kw("_ENABLEVORTEX_ON", false); m.Flt("_EnableVortex", 0f);
                SetBodyMaterial(app, m);
            }
            try { var f = RadFace(app._face); if (f != null) app._face = f; }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Rad face: " + e.Message); }
            Behaviours.Add<RadSlime>(s.Def);
        }

        public static void RadLargo(SlimeDefinition d)
        {
            if (!LargoLook.Blended(d)) ClearRadiant(d);   // CottonForge builds real radiant variants
            Behaviours.Add<RadSlime>(d);
        }

        /// <summary>Dome looks (index = Cfg RadDomeStyle / DevCmd "dome i").</summary>
        public static readonly string[] DomeNames = { "refraction", "bubble", "shadowAura", "hyperAura", "solarShield", "dreamBubble", "fresnel", "bubbleFx" };
        static readonly Material[] _domes = new Material[8];
        public static int DomeStyle = 0;
        public static bool DomeIsBillboard(int i) => i == 1 || i == 7;
        /// <summary>The SR1 inner glow sphere is only drawn with the SR2-style bubble looks.</summary>
        public static bool DomeHasCore(int i) => i == 1 || i == 7;

        /// <summary>Plain bright texture for the glass fresnel "reflection": an even soft green edge instead of cloud blotches.</summary>
        static Texture2D RadRimTex => Gfx.Generated("radRim", 8, 8, (x, y) => new Color(1f, 1f, 1f, 1f), false);

        static Texture2D RadBubbleTex => Gfx.BubbleTex("radBubble", new Color(0.30f, 0.80f, 0.32f), new Color(0.62f, 1f, 0.55f), 0.16f, 0.78f, 1.7f, 7, 4242);

        public static Material Dome(int i)
        {
            i = Mathf.Clamp(i, 0, _domes.Length - 1);
            if (_domes[i] != null) return _domes[i];
            Material m = null;
            switch (i)
            {
                case 1:   // SR2-icon-like bubble on HDRP Unlit (transparent, alpha blended)
                    m = Mat.Clone("RadDome1", "DefaultHDParticleMaterial") ?? Mat.CloneByShader("RadDome1", "HDRP/Unlit");
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
                case 0:   // SR1 RadAura: one dense green sphere that refracts what is behind it, bright green fresnel edge
                    m = Mat.Clone("RadDome0", "envLabGlass", "SimpleGlass");
                    if (m == null) break;
                    m.Col("_Color", new Color(0.10f, 1f, 0.15f, 0f));
                    m.Flt("_Opacity", 0.075f);          // several overlapping domes must not paint every slime neon green
                    m.Flt("_Smoothness", 0.62f);        // less mirror-like: the sky made the edge blue
                    m.Flt("_Metallic", 0f);
                    // SR1 RadAura refracted the world behind it into wavy heat-haze ripples: a tileable noise normal map
                    // (dptNoiseClouds is no normal map: it only shifted the picture), animated by the dome's slow spin
                    m.Tx("_NormalMap", RadNrm ?? GameTex("dptNoiseClouds"));
                    m.SetTextureScale("_NormalMap", new Vector2(2f, 2f));
                    m.Flt("_NormalStrength", 0.4f);
                    m.Tx("_Reflection", RadRimTex);
                    m.Flt("_ReflectionIntensity", 32f);
                    m.Flt("_ReflectionFresnelScale", 1.5f);
                    m.Flt("_ReflectionFresnelPower", 3.6f);    // narrow green rim, clearer centre
                    m.Flt("_ReflectionNormalStrength", 0.1f);
                    m.Col("_ReflectionTint", new Color(0.25f, 1f, 0.22f, 0f));
                    m.Flt("_DistortionStrength", 3f);    // heat haze stays readable when auras overlap
                    m.Flt("_DistortionBlur", 0.015f);
                    m.Flt("_FresnelDistortionBlur", 0.06f);
                    m.Flt("_BaseMapAlpha", 0f);
                    m.Flt("_BaseMapColor", 0f);
                    // one surface only (no inner back side doubling the edge, nothing drawn while standing inside)
                    m.Kw("_DOUBLESIDED_ON", false);
                    m.Flt("_DoubleSidedEnable", 0f);
                    m.Flt("_CullMode", 2f); m.Flt("_CullModeForward", 2f); m.Flt("_TransparentCullMode", 2f);
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
            if (mat == null && i != 1) { i = 1; mat = Dome(1); }
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

        /// <summary>Wavy refraction normals of the SR1-style dome.</summary>
        public static Texture2D RadNrm => Gfx.NoiseNormal("radNrm", 256, 4, 4, 24f, 77);
        /// <summary>Spin of the refracting dome (degrees per second): makes the ripples crawl like SR1's heat haze.</summary>
        public static float DomeSpin = 20f;
        /// <summary>UV scroll of the dome's ripple normal map (per second), on top of the spin.</summary>
        public static Vector2 RippleSpeed = new(0.021f, 0.047f);

        static Material _radHaze;
        /// <summary>
        /// SR1 RadAura heat haze: SR2's noise distortion volume (HeatDistortionVolume) inside the glass dome, much
        /// stronger and faster, so what is seen through the aura boils. Front faces only: no wobbling screen from inside.
        /// </summary>
        public static Material RadHazeMat
        {
            get
            {
                if (_radHaze != null) return _radHaze;
                var m = Mat.Clone("RadHaze", "HeatDistortionVolume");
                if (m == null) return null;
                m.Flt("_Noise_Strength", HazeStrength);
                m.Flt("_NoiseSpeed", -0.22f);
                m.Flt("_NoiseScale", 5f);
                m.Flt("_Opacity", 1f);
                m.Kw("_DOUBLESIDED_ON", false);
                m.Flt("_DoubleSidedEnable", 0f);
                m.Flt("_CullMode", 2f); m.Flt("_CullModeForward", 2f); m.Flt("_TransparentCullMode", 2f);
                _radHaze = Keep(m, "RadHaze");
                return _radHaze;
            }
        }
        public static float HazeStrength = 12f;

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

        static Material _radMote;
        /// <summary>Soft glowing green motes floating inside the rad aura (SR2 additive sparkle material with a round dot).</summary>
        public static Material RadMoteMat
        {
            get
            {
                if (_radMote != null) return _radMote;
                var m = Mat.Clone("RadMote", "Gold Slime Sparkle");
                if (m == null) return null;
                m.Tx("_MainTex", Gfx.Generated("radMote", 32, 32, (x, y) =>
                {
                    float dx = (x + 0.5f) / 16f - 1f, dy = (y + 0.5f) / 16f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    a = Mathf.Clamp01(a * 0.8f + Mathf.Clamp01(1f - d * 2.4f) * 0.6f);   // soft halo + brighter core
                    return new Color(a, a, a, a);
                }, false));
                m.Col("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.8f));
                _radMote = Keep(m, "RadMote");
                return _radMote;
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
            // clear glossy coat over the coloured tiles: a thin glass rim + faceted highlights, no milky haze.
            // Daylight-tuned: opacity / metallic / a wide fresnel (power 2.2) washed the tiles out to pastel, and the
            // stock fresnel blur (0.39) smeared the refracted background into a broad white halo around the slime.
            m.Col("_Color", new Color(0.65f, 0.88f, 1f, 0f));
            m.Flt("_Opacity", 0.04f);
            m.Flt("_Smoothness", 1f);
            m.Flt("_Metallic", 0f);
            m.Tx("_NormalMap", Res.Tex("nrm_mosaic", true));
            m.Flt("_NormalStrength", 2f);
            m.Flt("_BaseMapAlpha", 0f);
            m.Flt("_BaseMapColor", 0f);
            m.Flt("_DistortionStrength", 1.5f);
            m.Flt("_DistortionBlur", 0.01f);
            m.Flt("_FresnelDistortionBlur", 0.05f);
            // SR1 mosaic glass shimmers in rainbow colours (thin-film look): a rainbow reflection map instead of a white
            // one. The tile bevels (reflection normal strength) give every tile its own hue, the view angle and
            // MosaicIrisSpeed (ElasticGlassRig) make the colours run over the glass. Tune live: flt/col MosaicShell ...
            m.Tx("_Reflection", IridescentTex);
            m.Flt("_ReflectionIntensity", MosaicIrisIntensity);
            m.Flt("_ReflectionFresnelScale", 1.8f);
            m.Flt("_ReflectionFresnelPower", MosaicIrisFresnel);
            m.Flt("_ReflectionNormalStrength", MosaicIrisNormal);   // the tile bevels break the reflection into facet glints
            m.Col("_ReflectionTint", new Color(1f, 1f, 1f, 0f));
            _shell = Keep(m, "MosaicShell");
            return _shell;
        }

        // tuned in game against SR1 screenshots (iris 0.03 750 2.4 2): rainbow rims and per-tile hues, tiles still readable
        public static float MosaicIrisIntensity = 750f, MosaicIrisFresnel = 2.4f, MosaicIrisNormal = 2f, MosaicIrisSaturation = 0.85f;
        /// <summary>UV drift per second of the rainbow reflection (0 = colours only follow the view angle).</summary>
        public static float MosaicIrisSpeed = 0.03f;

        /// <summary>
        /// Soft rainbow bands (periodic in u and v, so no seam however SimpleGlass maps it), pastel enough to keep the
        /// rim as bright as the old white map: cyan / violet / pink / gold / green like SR1's mosaic glass.
        /// </summary>
        static Texture2D IridescentTex => Gfx.Generated("mosaicIris", 256, 256, (x, y) =>
        {
            float u = x / 256f, v = y / 256f, tau = Mathf.PI * 2f;
            float hue = u + v + 0.09f * Mathf.Sin(tau * (2f * u + v)) + 0.05f * Mathf.Sin(tau * (u - 3f * v));
            hue -= Mathf.Floor(hue);
            float value = 0.9f + 0.1f * Mathf.Sin(tau * (3f * u + 2f * v));
            var c = Color.HSVToRGB(hue, MosaicIrisSaturation, value);
            c.a = 1f;
            return c;
        }, false);

        /// <summary>Native SR2 slime surface: a continuous blue / emerald / amber tile gradient and bevel normals.</summary>
        static Material MosaicBodyMat(SlimeSpec s)
        {
            var m = Mat.Clone("MosaicBody", "slimePinkBase", "slimeRockBase");
            if (m == null) return null;
            MosaicSurface(m, Color.white, Color.white, Color.white, 0f);
            m.Kw("_ENABLEVORTEX_ON", false); m.Flt("_EnableVortex", 0f);
            Keep(m, "Mosaic");
            return m;
        }

        /// <summary>Smooth height weights, retaining a little per-tile colour variation (tools/mkmosaic.py).</summary>
        public static string MosaicMask = "msk_mosaicGrad";

        static Mesh _shellMesh;
        /// <summary>The SR1 mosaic body pushed out a little along its normals, so the glass reads as a coat over the tiles.</summary>
        static Mesh ShellMesh()
        {
            if (_shellMesh != null) return _shellMesh;
            var m = Res.MeshCopy("mosaic_LOD0");
            if (m == null) return null;
            try
            {
                var v = m.vertices; var n = m.normals;
                if (n != null && n.Length == v.Length)
                {
                    for (int i = 0; i < v.Length; i++) v[i] = v[i] + n[i] * 0.018f;
                    m.vertices = v;
                    m.RecalculateBounds();
                }
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Mosaic shell inflate: " + e.Message); }
            _shellMesh = m;
            return m;
        }

        static SlimeAppearanceStructure _shellStruct;
        static SlimeAppearanceStructure ShellStructure(SlimeSpec s, SlimeAppearance app)
        {
            var mesh = ShellMesh();
            var mat = ShellMat();
            if (mesh == null || mat == null) return null;
            if (_shellStruct == null)
            {
                var obj = SkinnedObject(BodyTemplate(), "mosaic_shell_LOD0", mesh, BoneTop, BonesMosaic, false);
                _shellStruct = Structure(app.Structures[0], "MosaicShell", EType.SURFACE, mat, false, obj);
            }
            return new SlimeAppearanceStructure(_shellStruct);
        }

        /// <summary>
        /// Surface mode: 3 (default) = SimpleGlass coat on the native body's exact skinned + warped vertices.
        /// 2 = tile surface only, 0 = legacy SR1 shell / root JellyRig, 1 = experimental aura coat.
        /// SimpleGlass lacks the native vertex warp; ElasticGlassRig supplies it without changing the body's physics.
        /// </summary>
        public static int MosaicGlassMode = 3;
        internal static Material MosaicElasticGlassMaterial() => ShellMat();
        public static string MosaicCoatTemplate = "RadiantAura_strong";

        static Material _coat;
        static Material MosaicCoatMat()
        {
            if (_coat != null) return _coat;
            var m = Mat.Clone("MosaicCoat", MosaicCoatTemplate, "RadiantAura_strong", "RadiantAura");
            if (m == null) return null;
            m.Col("_Color", new Color(0.55f, 0.85f, 1f, 1f));
            m.Col("_ColorFar", new Color(0.55f, 0.85f, 1f, 1f));
            _coat = Keep(m, "MosaicCoat");
            return _coat;
        }

        public static void Mosaic(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            // Native coloured tiles plus an elastic clear coat, created per live body/LOD by ElasticGlassRig.
            var body = MosaicBodyMat(s);
            if (body != null) SetBodyMaterial(app, body, MosaicGlassMode == 1 ? MosaicCoatMat() : null);
            if (MosaicGlassMode == 0)
            {
                var st = ShellStructure(s, app);
                if (st != null) InsertStructure(app, st);
            }
            Behaviours.Add<MosaicSlime>(s.Def);
        }

        public static void MosaicLargo(SlimeDefinition d)
        {
            bool blended = LargoLook.Blended(d);
            if (!blended) ClearRadiant(d);   // CottonForge builds real radiant variants: they get the shell too
            var s = SlimeFactory.Spec("Mosaic");
            var apps = d.AppearancesDefault;
            for (int ai = 0; ai < (blended ? apps.Length : 1); ai++)
            try
            {
                var app = apps[ai];
                if (app == null) continue;
                if (MosaicGlassMode == 2 || MosaicGlassMode == 3)
                {
                    MosaicNative(app, MosaicLargoTint);
                }
                else if (MosaicGlassMode == 1)
                {
                    // the coat goes on the mosaic-tiled body of the largo (same squash/stretch as the tiles)
                    var coat = MosaicCoatMat();
                    if (coat != null)
                        foreach (var st in app._structures)
                        {
                            var dm = st?.DefaultMaterials;
                            if (dm == null || dm.Length != 1 || dm[0] == null || !dm[0].name.Contains("Mosaic")) continue;
                            st.DefaultMaterials = new Il2CppReferenceArray<Material>(new[] { dm[0], coat });
                        }
                }
                else if (MosaicGlassMode == 0 && s != null && !HasElement(app, "MosaicShell"))
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
                m.Col("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.85f));
                _pixel = Keep(m, "GlitchPixel");
                return _pixel;
            }
        }

        static readonly Material[] _ribbons = new Material[2];
        public static Texture2D[] GlitchStrips;
        /// <summary>
        /// SR1 slimeGlitch_trail: alpha-blended particle material with a pixel-cell strip (swapped by GlitchSlime).
        /// SR2's particle shader only takes the strip's alpha (cells); the colour comes from the trail's vertex colours,
        /// so the pink and the green ribbon each get their own material (GlitchRibbon0/1) to flicker independently.
        /// </summary>
        public static Material RibbonMat(int i)
        {
            i = Mathf.Clamp(i, 0, _ribbons.Length - 1);
            if (_ribbons[i] != null) return _ribbons[i];
            GlitchStrips ??= new[] { Gfx.GlitchStrip(3), Gfx.GlitchStrip(17), Gfx.GlitchStrip(29), Gfx.GlitchStrip(41), Gfx.GlitchStrip(53), Gfx.GlitchStrip(67) };
            var m = Mat.Clone("GlitchRibbon" + i, "DazedStars", "CFX_Particles", "Gold Slime Sparkle");
            if (m == null) return null;
            m.Tx("_MainTex", GlitchStrips[i]);
            // HDR tint: the trail has to stand out like SR1's vivid magenta pixel streak, also in daylight
            m.Col("_Tint", new Color(1.9f, 1.9f, 1.9f, 1f));
            m.Col("_TintColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.Flt("_DepthFadeDistance", 0.1f);
            _ribbons[i] = Keep(m, "GlitchRibbon" + i);
            return _ribbons[i];
        }

        public static Material GlintMat => FlareMat;

        static Material _qsStreak;
        /// <summary>Quicksilver dash streak: a soft camera-facing ribbon (bright core, feathered edges; colour from the trail).</summary>
        public static Material QsStreakMat
        {
            get
            {
                if (_qsStreak != null) return _qsStreak;
                var m = Mat.Clone("QsStreak", "DazedStars", "CFX_Particles", "Gold Slime Sparkle");
                if (m == null) return null;
                m.Tx("_MainTex", Gfx.Generated("qsstreak", 8, 64, (x, y) =>
                {
                    float v = (y + 0.5f) / 64f * 2f - 1f;
                    float a = Mathf.Exp(-v * v * 6f) * 0.7f + Mathf.Exp(-v * v * 45f) * 0.6f;
                    return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }));
                m.Col("_Tint", new Color(1.1f, 1.1f, 1.1f, 1f));
                m.Col("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.8f));
                m.Flt("_DepthFadeDistance", 0.1f);
                _qsStreak = Keep(m, "QsStreak");
                return _qsStreak;
            }
        }

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
        public static Texture2D[] GlitchMasks, GlitchTears;

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
                // glitch bursts (GlitchSlime): torn rows / stripes / scan lines
                GlitchTears = new[] { Gfx.TearMask(5, 16, 0.2f, 0.16f, 0.07f), Gfx.TearMask(19, 16, 0.2f, 0.16f, 0.07f), Gfx.TearMask(31, 16, 0.22f, 0.18f, 0.08f),
                    Gfx.TearMask(47, 16, 0.2f, 0.16f, 0.07f), Gfx.TearMask(59, 16, 0.24f, 0.2f, 0.1f) };
                m.Tx("_ColorMask", GlitchMasks[0]);
                m.Flt("_ColorMaskUV1", 0f);
                m.Col("_RedTopColor", new Color(1f, 0.68f, 0.87f)); m.Col("_RedMiddleColor", new Color(1f, 0.58f, 0.82f)); m.Col("_RedBottomColor", new Color(0.92f, 0.46f, 0.74f));
                m.Col("_GreenTopColor", new Color(0.78f, 0.2f, 0.58f)); m.Col("_GreenMiddleColor", new Color(0.68f, 0.12f, 0.5f)); m.Col("_GreenBottomColor", new Color(0.55f, 0.07f, 0.4f));
                m.Col("_BlueTopColor", new Color(1f, 0.88f, 0.96f)); m.Col("_BlueMiddleColor", new Color(1f, 0.82f, 0.94f)); m.Col("_BlueBottomColor", new Color(0.96f, 0.72f, 0.88f));
                m.Kw("_ENABLEHYPEREFFECT_ON", true); m.Flt("_EnableHyperEffect", 1f);
                m.Col("_HyperFresnelColor", new Color(0.32f, 1f, 0.82f, 0f));
                m.Flt("_HyperBodyColorAlbedoIntensity", 0.95f);
                m.Flt("_HyperFresnelIntensity", 1.7f);   // 2.2 read as a white halo rather than SR1's mint rim
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
