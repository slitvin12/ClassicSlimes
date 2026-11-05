using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// SR1's Mosaic gordo wears a separate faceted glass shell over a plain gordo body: mosaicGordo_LOD0, ~100 big flat
    /// facets, each one UV-mapped onto one tile of the mosaic atlas, so the tile normal map bevels the facet edges and
    /// every facet catches its own reflection (SR1 shader SR/Slime/Mosaic: rainbow ramp, grey glass). Here: the SR1 mesh,
    /// skinned to the gordo's six bone_skin_* bones (SR1's bone order) and fitted to SR2's gordo body, with a copy of the
    /// Mosaic slime glass (SimpleGlass, tile normals, rainbow reflection). Under it the body is a smooth gradient
    /// (SR1 slimeMosaicBase) instead of the slime's small tiles, so the big facets carry the mosaic.
    /// </summary>
    public static class GordoFacets
    {
        public static bool On = true;
        /// <summary>Extra scale on top of the fit to SR2's body (the SR1 shell is ~6 % larger than its body).</summary>
        public static float Scale = 1f;
        /// <summary>Fit the shell to SR2's body bounds (off: SR1's placement, the bones match SR1's).</summary>
        public static bool Fit = true;
        public static Vector3 Offset = Vector3.zero;
        /// <summary>Body under the facets: "smooth" (gradient, no tiles) or "tiles" (the Mosaic slime's body).</summary>
        public static string Body = "smooth";
        /// <summary>Smooth body colours (top / middle / bottom), SR1's slimeMosaicBase: dark blue / green / red-orange.</summary>
        public static Color Top = new(0.185f, 0.263f, 0.493f), Mid = new(0.256f, 0.904f, 0f), Bottom = new(1f, 0.283f, 0.235f);
        /// <summary>Drift of the rainbow reflection relative to Visuals.MosaicIrisSpeed.</summary>
        public static float IrisDrift = 1f;
        /// <summary>0 = SR1's rounded shell normals (chosen in game), 1 = every facet flat (one fresnel colour per facet).</summary>
        public static float Flat = 0f;
        /// <summary>The Mosaic slime's radiant shimmer on the smooth body (rainbow waves under the facets); off: only the shell shimmers.</summary>
        public static bool Shimmer = false;

        public const string ShellName = "mosaicGordo_facets";
        const string MeshName = "mosaicGordo_LOD0";
        static readonly string[] BoneNames = { "bone_skin_top", "bone_skin_bac", "bone_skin_lef", "bone_skin_bot", "bone_skin_fro", "bone_skin_rig" };
        // SR1 slime_gordo mesh bounds, in the same space as the SR1 shell (both children of Vibrating, unscaled)
        static readonly Vector3 Sr1BodyMin = new(-0.583f, -0.024f, -0.610f), Sr1BodyMax = new(0.583f, 0.980f, 0.552f);
        static Material _glass, _smooth, _tiles;
        static readonly int ReflectionId = Shader.PropertyToID("_Reflection");
        static MelonLogger.Instance Log => ClassicSlimesMod.Log;

        /// <summary>Gordos.Create: the Mosaic gordo prefab (inactive, rest pose).</summary>
        public static void Setup(GameObject gordo, SkinnedMeshRenderer body)
        {
            if (gordo == null || body == null) return;
            try
            {
                var mats = body.sharedMaterials;
                if (mats.Length > 0 && _tiles == null) _tiles = mats[0];
                if (!On) return;
                Build(gordo, body, true);
                ApplyBody(body);
            }
            catch (Exception e) { Log.Warning("Mosaic gordo facets: " + e); }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static SkinnedMeshRenderer BodyOf(GameObject gordo)
        {
            foreach (var r in gordo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r != null && r.name.StartsWith("slime_gordo", StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        static GameObject Build(GameObject gordo, SkinnedMeshRenderer body, bool log)
        {
            var old = Find(gordo.transform, ShellName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var mesh = Res.MeshCopy(MeshName);
            var mat = Glass();
            if (mesh == null || mat == null) { Log.Warning("Mosaic gordo facets: " + (mesh == null ? "mesh " + MeshName : "glass material") + " missing"); return null; }
            Flatten(mesh, Flat);
            var bones = new Transform[BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
                if ((bones[i] = Find(gordo.transform, BoneNames[i])) == null) { Log.Warning("Mosaic gordo facets: no bone " + BoneNames[i]); return null; }
            var bt = body.transform;
            var go = new GameObject(ShellName) { layer = body.gameObject.layer };
            go.transform.SetParent(bt.parent, false);
            // fit: SR2's body bounds (in the parent's space) against SR1's, so the shell keeps SR1's margin over the body.
            // A skinned body sits where bone x bind pose puts it (slime_gordo's own 0.25 scale does not count).
            Vector3 min = Sr1BodyMin, max = Sr1BodyMax;
            var mb = body.sharedMesh != null ? body.sharedMesh.bounds : new Bounds();
            var bb = body.bones; var bind = body.sharedMesh != null ? body.sharedMesh.bindposes : null;
            if (Fit && mb.size.sqrMagnitude > 0f && bb != null && bb.Length > 0 && bb[0] != null && bind != null && bind.Length > 0)
            {
                var m2p = bt.parent.worldToLocalMatrix * bb[0].localToWorldMatrix * bind[0];
                min = Vector3.one * float.MaxValue; max = Vector3.one * float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var p = m2p.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? mb.max.x : mb.min.x, (i & 2) != 0 ? mb.max.y : mb.min.y, (i & 4) != 0 ? mb.max.z : mb.min.z));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            }
            var s1 = Sr1BodyMax - Sr1BodyMin;
            var k = new Vector3((max.x - min.x) / s1.x, (max.y - min.y) / s1.y, (max.z - min.z) / s1.z);
            if (k.x < 0.7f || k.x > 1.4f || k.y < 0.7f || k.y > 1.4f || k.z < 0.7f || k.z > 1.4f)
            {
                if (log) Log.Warning($"Mosaic gordo facets: odd body bounds {min:F3}..{max:F3}, SR1 placement used");
                min = Sr1BodyMin; max = Sr1BodyMax; k = Vector3.one;
            }
            k *= Scale;
            var c1 = (Sr1BodyMin + Sr1BodyMax) * 0.5f;
            go.transform.localPosition = (min + max) * 0.5f - Vector3.Scale(k, c1) + Offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = k;
            // bind poses from the rest pose: mesh space = the shell's transform
            var l2w = go.transform.localToWorldMatrix;
            var bp = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bp[i] = bones[i].worldToLocalMatrix * l2w;
            mesh.bindposes = bp;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = body.rootBone != null ? body.rootBone : bones[0];
            smr.sharedMaterial = mat;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
            // culling bounds in the root bone's space, with room for the jiggle
            var toRoot = smr.rootBone.worldToLocalMatrix * l2w;
            var b = mesh.bounds;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = toRoot.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            var size = hi - lo;
            smr.localBounds = new Bounds((lo + hi) * 0.5f, size * 1.5f);
            if (log) Log.Msg($"Mosaic gordo facets: SR1 shell x({k.x:F3}, {k.y:F3}, {k.z:F3}) over body {min:F3}..{max:F3}, root {smr.rootBone.name}");
            return go;
        }

        /// <summary>
        /// Turns the shell's normals towards each facet's own plane. The SR1 mesh has split vertices along the facet edges,
        /// so a facet is a connected group of triangles (1-4 of them, 115 facets).
        /// </summary>
        static void Flatten(Mesh m, float k)
        {
            if (k <= 0f) return;
            Vector3[] v = m.vertices, n = m.normals; int[] t = m.triangles;
            if (v == null || n == null || t == null || n.Length != v.Length) return;
            var p = new int[v.Length];
            for (int i = 0; i < p.Length; i++) p[i] = i;
            int Root(int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
            for (int i = 0; i + 2 < t.Length; i += 3) { int a = Root(t[i]), b = Root(t[i + 1]), c = Root(t[i + 2]); p[a] = c; p[b] = c; }
            var sum = new Vector3[v.Length];
            for (int i = 0; i + 2 < t.Length; i += 3)
                sum[Root(t[i])] += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            k = Mathf.Clamp01(k);
            for (int i = 0; i < v.Length; i++)
            {
                var f = sum[Root(i)];
                if (f.sqrMagnitude < 1e-12f) continue;
                f.Normalize();
                var s = n[i].normalized;
                if (Vector3.Dot(f, s) < 0f) f = -f;
                n[i] = Vector3.Slerp(s, f, k);
            }
            m.normals = n;
            m.RecalculateTangents();
        }

        /// <summary>Copy of the Mosaic slime glass (tune live: flt/col MosaicFacets ...).</summary>
        public static Material Glass()
        {
            if (_glass != null) return _glass;
            var src = Visuals.MosaicElasticGlassMaterial();
            if (src == null) return null;
            var m = Object.Instantiate(src);
            m.name = "ClassicSlimes_MosaicGordoGlass";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // SR1's big facets: deep bevels, strong refraction, a wide rainbow rim (tuned in game)
            m.Flt("_NormalStrength", 5f); m.Flt("_DistortionStrength", 2f); m.Flt("_ReflectionFresnelPower", 1.2f);
            Visuals.Mats["MosaicFacets"] = m;
            _glass = Mat.Track(m);
            return _glass;
        }

        /// <summary>The Mosaic body without its tiles: SR2 default body colouring (top / middle / bottom), no tile normals.</summary>
        static Material Smooth()
        {
            if (_smooth != null) return _smooth;
            if (_tiles == null) return null;
            var m = Object.Instantiate(_tiles);
            m.name = "ClassicSlimes_MosaicGordoBody";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.Kw("_BODYCOLORING_MULTI", false); m.Kw("_BODYCOLORING_DEFAULT", true); m.Flt("_BodyColoring", 0f);
            m.Palette(Top, Mid, Bottom);
            m.Kw("_ENABLENORMALS_ON", false); m.Flt("_EnableNormals", 0f);
            ApplyShimmer(m);
            Visuals.Mats["MosaicGordoBody"] = m;
            _smooth = Mat.Track(m);
            return _smooth;
        }

        static void ApplyShimmer(Material m)
        {
            if (m == null) return;
            m.Kw("_ENABLERADIANTEFFECT_ON", Shimmer); m.Kw("_TOGGLERADIANTEFFECT_ON", Shimmer);
            m.Flt("_EnableRadiantEffect", Shimmer ? 1f : 0f); m.Flt("_ToggleRadiantEffect", Shimmer ? 1f : 0f);
            m.Flt("_ForceRadiantEffect", Shimmer ? 0.25f : 0f);
        }

        static void ApplyBody(SkinnedMeshRenderer body)
        {
            var want = Body == "smooth" && On ? Smooth() : _tiles;
            if (body == null || want == null) return;
            var mats = body.sharedMaterials;
            if (mats.Length == 0 || mats[0] == want) return;
            mats[0] = want;
            body.sharedMaterials = mats;
        }

        /// <summary>Main.OnUpdate: the rainbow reflection slowly runs over the facets, like over the Mosaic slimes.</summary>
        public static void Tick()
        {
            if (_glass == null || Visuals.MosaicIrisSpeed == 0f || IrisDrift == 0f) return;
            float t = Time.time * Visuals.MosaicIrisSpeed * IrisDrift;
            _glass.SetTextureOffset(ReflectionId, new Vector2(t - Mathf.Floor(t), t * 0.37f - Mathf.Floor(t * 0.37f)));
        }

        /// <summary>The Mosaic gordo prefab and its live copies.</summary>
        static List<GameObject> Gordos()
        {
            var r = new List<GameObject>();
            foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
                if (e != null && e.gameObject.name.StartsWith("gordoMosaic", StringComparison.Ordinal)) r.Add(e.gameObject);
            return r;
        }

        /// <summary>gordofacets [on|off|scale k|fit on|off|offset x y z|flat k|shimmer on|off|body smooth|tiles|top|mid|bottom r g b|drift x]: the SR1 facet shell of the Mosaic gordo.</summary>
        public static string Cmd(string[] a)
        {
            string op = a.Length > 1 ? a[1] : "info";
            float F(int i, float d) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
            bool rebuild = false, body = false;
            switch (op)
            {
                case "on": On = true; rebuild = true; body = true; break;
                case "off": On = false; body = true; break;
                case "scale": Scale = F(2, Scale); rebuild = true; break;
                case "body": Body = a.Length > 2 ? a[2] : Body; body = true; break;
                case "top": case "mid": case "bottom":
                    {
                        var c = new Color(F(2, 1f), F(3, 1f), F(4, 1f));
                        if (op == "top") Top = c; else if (op == "mid") Mid = c; else Bottom = c;
                        _smooth?.Palette(Top, Mid, Bottom);
                        break;
                    }
                case "drift": IrisDrift = F(2, IrisDrift); break;
                case "flat": Flat = F(2, Flat); rebuild = true; break;
                case "shimmer": Shimmer = a.Length <= 2 || a[2] != "off"; ApplyShimmer(_smooth); break;
                case "fit": Fit = a.Length <= 2 || a[2] != "off"; rebuild = true; break;
                case "offset": Offset = new Vector3(F(2, 0f), F(3, 0f), F(4, 0f)); rebuild = true; break;
            }
            var sb = new StringBuilder();
            int n = 0;
            foreach (var g in Gordos())
            {
                var bodyR = BodyOf(g);
                if (bodyR == null) continue;
                n++;
                var shell = Find(g.transform, ShellName);
                if (On && (rebuild || shell == null)) shell = Build(g, bodyR, false)?.transform;
                if (shell != null) shell.gameObject.SetActive(On);
                if (body) ApplyBody(bodyR);
                var ss = shell != null ? shell.localScale : Vector3.zero;
                sb.Append($"\n  {g.name} active={g.activeInHierarchy} shell={(shell != null ? (shell.gameObject.activeSelf ? "on" : "off") : "-")} scale=({ss.x:F3}, {ss.y:F3}, {ss.z:F3}) body={bodyR.sharedMaterials[0]?.name}");
            }
            return $"gordofacets {op}: on={On} scale={Scale:F3} fit={Fit} offset={Offset:F3} body={Body} drift={IrisDrift} flat={Flat:F2} shimmer={Shimmer} glass={(_glass != null ? _glass.name : "-")}, {n} gordos{sb}";
        }
    }
}
