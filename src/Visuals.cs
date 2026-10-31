using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Audio;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Bone = Il2Cpp.SlimeAppearance.SlimeBone;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;

namespace ClassicSlimes
{
    /// <summary>
    /// Rebuilds the SR1 looks with SR2 assets: SR1 meshes (mosaic tiles, quicksilver body/crest) are re-skinned onto SR2's
    /// slime bones (SR1 and SR2 share the same SlimeBone numbering and rest pose), SR1 textures are plugged into SR2's
    /// slime body shader features (multi-colour mask + normal map, matcap, phosphor glow, hyper fresnel, twin glow).
    /// </summary>
    public static partial class Visuals
    {
        static GameObject _root;
        public static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("ClassicSlimesPrefabs");
                    _root.SetActive(false);
                    _root.hideFlags = HideFlags.HideAndDontSave;
                    Object.DontDestroyOnLoad(_root);
                }
                return _root.transform;
            }
        }

        // SR1 SlimeAppearanceObject.AttachedBones (numeric values identical to SR2's SlimeBone enum)
        static readonly int[] BonesMosaic = { 10, 5, 8, 6, 7, 9 };
        static readonly int[] BonesStd = { 9, 7, 6, 8, 5, 10 };
        const int BoneTop = 10;

        public static readonly Dictionary<string, Material> Mats = new();

        static SlimeAppearance App(SlimeSpec s) => s.Def.AppearancesDefault[0];
        static SlimeAppearance PinkApp => Library.GetSlime("Pink").AppearancesDefault[0];

        static void ClearRadiant(SlimeDefinition d)
        {
            // Cotton clones Pink's definition, so the radiant variant would be a radiant *pink* slime.
            try { d.RadiantBase = null; d.RadiantLargo0 = null; d.RadiantLargo1 = null; } catch { }
        }

        static void SetBodyMaterial(SlimeAppearance app, Material m)
        {
            if (m == null) return;
            app.Structures[0].DefaultMaterials = new Il2CppReferenceArray<Material>(new[] { m });
        }

        static int FaceIndex(SlimeAppearance app)
        {
            var arr = app._structures;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i]?.Element != null && arr[i].Element.Type == EType.FACE) return i;
            return -1;
        }

        static bool HasElement(SlimeAppearance app, string name)
        {
            foreach (var st in app._structures) if (st?.Element != null && st.Element.Name == name) return true;
            return false;
        }

        static void InsertStructure(SlimeAppearance app, SlimeAppearanceStructure st)
        {
            var list = new List<SlimeAppearanceStructure>();
            foreach (var x in app._structures) list.Add(x);
            int fi = FaceIndex(app);
            if (fi < 0) list.Add(st); else list.Insert(fi, st);
            app._structures = new Il2CppReferenceArray<SlimeAppearanceStructure>(list.ToArray());
        }

        static SlimeAppearanceObject BodyTemplate() => PinkApp.Structures[0].Element.Prefabs[0];

        /// <summary>Copy of SR2's skinned LOD0 body object with another mesh, bound to the given SR1 bone list.</summary>
        static SlimeAppearanceObject SkinnedObject(SlimeAppearanceObject template, string name, Mesh mesh, int root, int[] attached, bool shadows)
        {
            var go = Object.Instantiate(template.gameObject, Root);
            go.name = name;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            var sao = go.GetComponent<SlimeAppearanceObject>();
            sao.RootBone = (Bone)root;
            sao.ParentBone = Bone.NONE;
            if (attached != null)
                sao.AttachedBones = new Il2CppStructArray<Bone>(attached.Select(i => (Bone)i).ToArray());
            sao.IgnoreLODIndex = true;
            sao.LODIndex = 0;
            return sao;
        }

        static SlimeAppearanceStructure Structure(SlimeAppearanceStructure template, string elName, EType type, Material mat, bool faces, params SlimeAppearanceObject[] prefabs)
        {
            var st = new SlimeAppearanceStructure(template);
            var el = ScriptableObject.CreateInstance<SlimeAppearanceElement>();
            el.name = elName;
            el.Name = elName;
            el.Type = type;
            el.CastsShadows = true;
            el.MaterialInheritance = SlimeAppearanceElement.MatInheritType.NONE;
            el.hideFlags = HideFlags.DontUnloadUnusedAsset;
            el.Prefabs = new Il2CppReferenceArray<SlimeAppearanceObject>(prefabs);
            st.Element = el;
            st.SupportsFaces = faces;
            if (mat != null) st.DefaultMaterials = new Il2CppReferenceArray<Material>(new[] { mat });
            try
            {
                var em = template.ElementMaterials;
                if (em != null && em.Length > 0)
                {
                    var arr = new SlimeAppearanceMaterials[prefabs.Length];
                    for (int i = 0; i < arr.Length; i++) arr[i] = em[Math.Min(i, em.Length - 1)];
                    st.ElementMaterials = new Il2CppReferenceArray<SlimeAppearanceMaterials>(arr);
                }
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("ElementMaterials: " + e.Message); }
            return st;
        }

        static Material BodyMat(string key, SlimeSpec s, params string[] templates)
        {
            var m = Mat.Clone(key + "Body", templates);
            if (m == null) return null;
            m.Palette(s.Top, s.Mid, s.Bot);
            Mats[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ voices
        static AudioClipDefinition ClipDef(AudioClipDefinition src, string key, string clip)
        {
            if (src == null || clip == null) return src;
            var cue = Snd.Cue(key, false, 0f, 0f, clip);
            if (cue == null) return src;
            var d = Object.Instantiate(src);
            d.name = "ClassicSlimes_" + key;
            d.hideFlags = HideFlags.DontUnloadUnusedAsset;
            d._audioCue = cue;
            return d;
        }

        static void Voices(SlimeSpec s, string fun, string fear, string damage, string jump)
        {
            try
            {
                var src = s.Def.Sounds;
                if (src == null) return;
                var snd = Object.Instantiate(src);
                snd.name = s.Name + "SlimeSounds";
                snd.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (fun != null) { snd._voiceFunClipDefinition = ClipDef(src._voiceFunClipDefinition, s.Name + "Fun", fun); snd._voiceAweClipDefinition = ClipDef(src._voiceAweClipDefinition, s.Name + "Awe", fun); }
                if (fear != null) { snd._voiceFearClipDefinition = ClipDef(src._voiceFearClipDefinition, s.Name + "Fear", fear); snd._voiceAlarmClipDefinition = ClipDef(src._voiceAlarmClipDefinition, s.Name + "Alarm", fear); }
                if (damage != null) snd._voiceDamageClipDefinition = ClipDef(src._voiceDamageClipDefinition, s.Name + "Damage", damage);
                if (jump != null) { snd._voiceJumpClipDefinition = ClipDef(src._voiceJumpClipDefinition, s.Name + "Jump", jump); snd._voiceHighJumpClipDefinition = ClipDef(src._voiceHighJumpClipDefinition, s.Name + "HighJump", jump); }
                s.Def.Sounds = snd;
                var sa = s.Def.prefab.GetComponent<SlimeAudio>();
                if (sa != null) sa.SlimeSounds = snd;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Voices {s.Name}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ Quantum
        /// <summary>
        /// SR1 quantum: warm yellow body with lighter swirl ("@") marks and yellow eyes. SR2: the multi-colour body shader
        /// with a generated swirl mask (red channel = swirls) on the hyper material for a pale holographic rim.
        /// </summary>
        public static void Quantum(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            var m = BodyMat("Quantum", s, "HyperDefaultRingtailDefault_Body", "slimeRingtailBase", "slimeTwinBase", "slimePinkBase");
            if (m != null)
            {
                m.Palette(new Color(1f, 0.82f, 0.26f), new Color(0.98f, 0.64f, 0.10f), new Color(0.85f, 0.45f, 0.06f));   // SR1 icon: golden
                m.Kw("_ENABLETWINEFFECT_ON", false); m.Flt("_EnableTwinEffect", 0f);
                m.Kw("_ENABLEVORTEX_ON", false);
                m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
                m.Tx("_ColorMask", Gfx.SwirlMask);
                m.SetTextureScale("_ColorMask", Gfx.SwirlScale);
                m.SetTextureOffset("_ColorMask", Gfx.SwirlOffset);
                m.Flt("_ColorMaskUV1", 0f);
                m.Col("_RedTopColor", new Color(1f, 0.96f, 0.66f)); m.Col("_RedMiddleColor", new Color(1f, 0.90f, 0.52f)); m.Col("_RedBottomColor", new Color(1f, 0.80f, 0.40f));
                m.Col("_GreenTopColor", new Color(1f, 0.92f, 0.50f)); m.Col("_GreenMiddleColor", new Color(1f, 0.82f, 0.35f)); m.Col("_GreenBottomColor", new Color(1f, 0.65f, 0.25f));
                m.Col("_BlueTopColor", new Color(1f, 0.92f, 0.50f)); m.Col("_BlueMiddleColor", new Color(1f, 0.82f, 0.35f)); m.Col("_BlueBottomColor", new Color(1f, 0.65f, 0.25f));
                m.Flt("_SmoothR", 0f); m.Flt("_SmoothG", 0f); m.Flt("_SmoothB", 0f);
                if (m.IsKeywordEnabled("_ENABLEHYPEREFFECT_ON"))
                {
                    m.Flt("_EnableHyperEffect", 1f);
                    m.Col("_HyperFresnelColor", new Color(1f, 0.95f, 0.55f, 0f));
                    m.Flt("_HyperBodyColorAlbedoIntensity", 1f);
                    m.Flt("_HyperFresnelIntensity", 1.4f);
                    m.Flt("_HyperFresnelSparksNoiseIntensity", 20f);
                    m.Flt("_HyperFresnelSecondaryNoiseIntensity", 10f);
                }
                m.Flt("_GlossPower", 0.6f);
                Keep(m, "Quantum");
                SetBodyMaterial(app, m);
            }
            try { var f = QuantumFace(app._face); if (f != null) app._face = f; }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Quantum face: " + e.Message); }
            Behaviours.Add<QuantumSlime>(s.Def);
        }

        public static void QuantumLargo(SlimeDefinition d)
        {
            ClearRadiant(d);
            Behaviours.Add<QuantumSlime>(d);
        }

        static Material _ghost;
        public static Material GhostMat
        {
            get
            {
                if (_ghost != null) return _ghost;
                _ghost = Mat.Clone("QuantumGhost", "RadiantAura_strong", "RadiantAura_light", "SphereLightAdditiveGradientRadiant");
                if (_ghost != null)
                {
                    _ghost.Col("_Color", new Color(1f, 0.85f, 0.25f, 1f));
                    _ghost.Col("_ColorFar", new Color(1f, 0.5f, 0f, 1f));
                    _ghost.Flt("_ToggleRadiantEffect", 1f);
                    _ghost.Flt("_Alpha", 1f);
                    _ghost.Flt("_Opacity", 0.8f);
                    _ghost.Flt("_VertexOffset", 0f);
                    _ghost.Flt("_VertexOffsetFar", 0f);
                    _ghost.Tint("QuantumGhostRamp", new Color(1f, 0.45f, 0.05f), new Color(1f, 0.75f, 0.2f), new Color(1f, 0.95f, 0.6f));
                    Mats["QuantumGhost"] = _ghost;
                }
                return _ghost;
            }
        }

        static Material _holo;
        /// <summary>
        /// Quantum "phase" blink: a transparent unlit shell with scrolling horizontal scanlines (hologram look).
        /// The texture is scrolled vertically by QuantumSlime while a phase is running.
        /// </summary>
        public static Material HoloMat
        {
            get
            {
                if (_holo != null) return _holo;
                var m = Mat.Clone("QuantumHolo", "DefaultHDParticleMaterial") ?? Mat.CloneByShader("QuantumHolo", "HDRP/Unlit");
                if (m == null) return null;
                m.Tx("_UnlitColorMap", Gfx.ScanlineTex);
                m.Col("_UnlitColor", new Color(1f, 0.93f, 0.55f, 0.85f));
                _holo = Keep(m, "QuantumHolo");
                return _holo;
            }
        }

        /// <summary>Scanline shell around a quantum slime (hidden until a phase starts).</summary>
        public static GameObject MakeHolo(Transform parent)
        {
            var mat = HoloMat;
            if (mat == null || parent == null) return null;
            var go = Gfx.MeshObj("QuantumHolo", parent, Gfx.SphereMesh, mat, new Vector3(0f, 0.5f, 0f), 1f);
            go.transform.localScale = new Vector3(1.12f, 1.02f, 1.12f);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        static Mesh _ghostMesh;
        static Mesh GhostMesh
        {
            get
            {
                if (_ghostMesh != null) return _ghostMesh;
                try
                {
                    var prefabs = PinkApp.Structures[0].Element.Prefabs;
                    for (int i = 1; i < prefabs.Length && _ghostMesh == null; i++)
                    {
                        var mf = prefabs[i].GetComponent<MeshFilter>();
                        if (mf != null) _ghostMesh = mf.sharedMesh;
                    }
                }
                catch { }
                if (_ghostMesh == null) _ghostMesh = Res.Mesh("slime_default");
                return _ghostMesh;
            }
        }

        public static QuantumGhost MakeGhost(QuantumSlime owner, Vector3 pos)
        {
            var go = new GameObject("QuantumGhost");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, U.Rand(0f, 360f), 0f);
            go.transform.localScale = owner.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = GhostMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GhostMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var g = go.AddComponent<QuantumGhost>();
            g.Owner = owner;
            return g;
        }

        // ------------------------------------------------------------------ Quicksilver
        static Mesh ProjectFace(Mesh face, string bodyMesh, Vector3 faceCenter, Vector3 bodyCenter, float push)
        {
            if (face == null || !Res.Data.TryGetValue(bodyMesh, out var bd)) return null;
            var m = Object.Instantiate(face);
            m.name = face.name + "_" + bodyMesh;
            var src = face.vertices;
            int n = src.Length;
            var outV = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var v = src[i];
                var dir = v - faceCenter;
                if (dir.sqrMagnitude < 1e-8f) { outV[i] = v; continue; }
                dir.Normalize();
                float best = -1f;
                for (int t = 0; t + 2 < bd.t.Length; t += 3)
                    if (RayTri(bodyCenter, dir, bd.v[bd.t[t]], bd.v[bd.t[t + 1]], bd.v[bd.t[t + 2]], out float dist) && dist > best) best = dist;
                outV[i] = best > 0f ? bodyCenter + dir * (best + push) : v;
            }
            m.vertices = outV;
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(m);
            return m;
        }

        static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-7f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        public static void Quicksilver(SlimeSpec s)
        {
            ClearRadiant(s.Def);
            var app = App(s);
            // SR1: SR/AMP/Slime/Body/Matcap Stripe with silver_sphere matcap -> SR2 gold slime matcap feature with SR1's silver matcap
            var m = BodyMat("Quicksilver", s, "slimeGoldBase", "slimePinkBase");
            if (m != null)
            {
                m.Tx("_CubemapOverride", Res.Tex("silver_sphere", false, false));
                m.Flt("_EnableMatcap", 1f);
            }
            try
            {
                var bodyMesh = Res.Mesh("slime_quicksilver");
                var crestMesh = Res.Mesh("quicksilvercrest");
                var bodyStruct = app.Structures[0];
                if (bodyMesh != null)
                {
                    var obj = SkinnedObject(BodyTemplate(), "slime_quicksilver_LOD0", bodyMesh, BoneTop, BonesStd, true);
                    var nb = Structure(bodyStruct, "QuicksilverBody", EType.BODY, m, bodyStruct.SupportsFaces, obj);
                    app._structures[0] = nb;
                }
                else SetBodyMaterial(app, m);
                if (crestMesh != null)
                {
                    var obj = SkinnedObject(BodyTemplate(), "quicksilver_crest_LOD0", crestMesh, BoneTop, BonesStd, false);
                    InsertStructure(app, Structure(bodyStruct, "QuicksilverCrest", EType.TOP, m, false, obj));
                }
                // move the SR2 face onto the wider, raised quicksilver body
                int fi = FaceIndex(app);
                if (fi >= 0 && bodyMesh != null)
                {
                    var fs = app._structures[fi];
                    var faceObj = fs.Element.Prefabs[0];
                    var smr = faceObj.GetComponent<SkinnedMeshRenderer>();
                    var proj = smr != null ? ProjectFace(smr.sharedMesh, "slime_quicksilver", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.64f, 0.05f), 0.004f) : null;
                    if (proj != null)
                    {
                        var nf = SkinnedObject(faceObj, "slime_face_quicksilver_LOD0", proj, (int)faceObj.RootBone, null, false);
                        var face = Structure(fs, fs.Element.Name, EType.FACE, null, true, nf);
                        face.Element.MaterialInheritance = fs.Element.MaterialInheritance;
                        app._structures[fi] = face;
                    }
                }
            }
            catch (Exception e) { ClassicSlimesMod.Log.Error("Quicksilver structures: " + e); }
            Voices(s, "vo_quicksilver_happy_01", null, null, null);
            Behaviours.Add<QuicksilverSlime>(s.Def);
        }

    }
}
