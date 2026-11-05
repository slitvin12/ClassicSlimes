using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;
using UnityEngine.Rendering;
using V = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// SimpleGlass has no slime vertex warp. Bake the SAME body mesh (not the old SR1 sphere),
    /// then mirror SR/AMP/Slime/Body's wobble + local contact squash before drawing its clear coat.
    /// The native body, rubber bone, animation and contact physics remain completely untouched.
    /// </summary>
    internal sealed class ElasticGlassRig : IDisposable
    {
        static readonly List<ElasticGlassRig> Live = new();
        static readonly HashSet<string> Unreadable = new();
        internal static bool Show = true;
        internal static double TotalMs;
        static readonly int HeldId = Shader.PropertyToID("_HeldInVac");
        static readonly int AlphaId = Shader.PropertyToID("_AlphaVertexOffset");
        readonly Transform _root;
        readonly List<Coat> _coats = new();
        SquashWobbleViaShader _warp;
        float _scanAt;
        bool _failed;
        int _index = -1, _vertices;
        float _maxWarp;
        double _ms;

        internal ElasticGlassRig(Transform root) { _root = root; Live.Add(this); }

        internal void Sync()
        {
            if (_failed || _root == null || Time.unscaledTime < _scanAt) return;
            _scanAt = Time.unscaledTime + 0.8f;
            if (_warp == null) _warp = _root.GetComponentInChildren<SquashWobbleViaShader>(true);
            var seen = new HashSet<IntPtr>();
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var m = r.sharedMaterial;
                // Tile bodies (slime body shader) plus every glass part of a mosaic largo (ears, spikes, rings, petals).
                if (m == null || !m.name.Contains("Mosaic") || m.shader == null) continue;
                if (m.shader.name != "SR/AMP/Slime/Body" && !m.name.Contains("_MosaicPart")) continue;
                var skin = r.TryCast<SkinnedMeshRenderer>();
                var filter = skin == null ? r.GetComponent<MeshFilter>() : null;
                var src = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (src == null) continue;
                // The elastic coat copies and warps the vertices on the CPU. Unreadable game meshes (Ringtail ears/tail,
                // Angler fins, Flutter wings) get a GPU coat instead: the original mesh (skinned by the same bones), no warp.
                bool gpu = !src.isReadable;
                if (gpu && Unreadable.Add(src.name)) ClassicSlimesMod.Log.Msg($"Elastic mosaic glass: mesh {src.name} ({m.name}) is not readable, rigid GPU coat");
                // the plain Mosaic slime: SR1's facet shell over the smooth body (SlimeFacets); largos keep the tile coat
                bool facet = false;
                if (!gpu && SlimeFacets.IsPlainBody(m)) { m = SlimeFacets.SwapBody(r, m); facet = SlimeFacets.On; }
                seen.Add(r.Pointer);
                bool found = false;
                for (int i = _coats.Count - 1; i >= 0; i--)
                {
                    var c = _coats[i];
                    if (c.Source == null || c.Source.Pointer != r.Pointer) continue;
                    if (c.Original == src && c.Facet == facet && (!facet || c.Version == SlimeFacets.Version)) { c.Material = m; found = true; }
                    else { c.Dispose(); _coats.RemoveAt(i); }
                }
                if (!found) _coats.Add(new Coat(r, skin, src, m, gpu, facet ? _root : null));
            }
            for (int i = _coats.Count - 1; i >= 0; i--)
                if (_coats[i].Source == null || !seen.Contains(_coats[i].Source.Pointer))
                { _coats[i].Dispose(); _coats.RemoveAt(i); }
        }

        public void Dispose()
        {
            Live.Remove(this);
            foreach (var c in _coats) c.Dispose();
            _coats.Clear();
        }

        internal string Info() => $"elastic glass {_coats.Count} LODs / {_vertices} visible verts, index {_index}, warp {_maxWarp:F3}, {_ms:F2} ms (all {TotalMs:F2} ms), show {Show}";

        /// <summary>Rescan every live rig on its next LateUpdate (slimefacets settings changed).</summary>
        internal static void Resync() { foreach (var r in Live) r._scanAt = 0f; }

        internal static string Census()
        {
            int coats = 0, facets = 0, drawn = 0;
            foreach (var r in Live)
                foreach (var c in r._coats)
                {
                    coats++;
                    if (c.Shell == null) continue;
                    facets++;
                    if (c.Renderer != null && c.Renderer.enabled) drawn++;
                }
            return $"{Live.Count} rigs, {coats} coats, {facets} facet shells ({drawn} drawn)";
        }

        static int _irisFrame = -1;
        static readonly int ReflectionId = Shader.PropertyToID("_Reflection");

        internal static void UpdateAll(SlimeSquashWobbleController ctrl)
        {
            if (Live.Count == 0 || ctrl == null) return;
            long start = Stopwatch.GetTimestamp();
            if (Time.frameCount != _irisFrame && Visuals.MosaicIrisSpeed != 0f)
            {
                // the rainbow reflection slowly runs over the glass (one shared material, once per frame)
                _irisFrame = Time.frameCount;
                var glass = Visuals.MosaicElasticGlassMaterial();
                float t = Time.time * Visuals.MosaicIrisSpeed;
                if (glass != null && glass.HasProperty(ReflectionId)) glass.SetTextureOffset(ReflectionId, new Vector2(t - Mathf.Floor(t), (t * 0.37f) - Mathf.Floor(t * 0.37f)));
            }
            var centres = ctrl.squashWobbleCentres; var normals = ctrl.squashNormals;
            var positions = ctrl.squashPositions; var offsets = ctrl.wobbleOffsets;
            if (centres == null || normals == null || positions == null || offsets == null) return;
            var cfg = new Warp { Influence = ctrl._squashInfluenceMultiplier, Bulge = ctrl._squashBulgeMultiplier * 0.3f,
                MaskMin = ctrl._bulgeMaskMin, MaskRange = ctrl._bulgeMaskMax - ctrl._bulgeMaskMin };
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var rig = Live[i];
                if (rig._failed) continue;
                try
                {
                    rig._index = rig._warp != null ? rig._warp.slimeGlobalArrayIndex : -1;
                    int ix = rig._index;
                    bool valid = ix >= 0 && ix < centres.Length && ix < normals.Length && ix < positions.Length && ix < offsets.Length;
                    var w = cfg;
                    if (valid)
                    {
                        var c = centres[ix]; var n = normals[ix]; var s = positions[ix]; var o = offsets[ix];
                        w.Center = Vec(c); w.Normal = Vec(n); w.Surface = Vec(s); w.Delta = Vec(o) - w.Center;
                        w.Radius = c.w * w.Influence; w.Squash = n.w > 0f ? n.w * s.w : 0f; w.Wobble = o.w > 0f;
                        w.Dir = Unit(w.Delta); w.UnitNormal = Unit(w.Normal);
                        w.NormalSq = V.Dot(w.Normal, w.Normal);
                    }
                    rig.Tick(w, valid);
                }
                catch (Exception e)
                {
                    rig._failed = true;
                    foreach (var c in rig._coats) if (c.Renderer != null) c.Renderer.enabled = false;
                    ClassicSlimesMod.Log.Warning("Elastic mosaic glass: " + e);
                }
            }
            TotalMs = TotalMs * 0.9 + 0.1 * (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        void Tick(Warp w, bool valid)
        {
            long start = Stopwatch.GetTimestamp();
            _vertices = 0; _maxWarp = 0f;
            foreach (var c in _coats)
            {
                if (c.Source == null || c.Renderer == null) continue;
                // Unity LOD culling does NOT change Renderer.enabled. Mirror actual source visibility;
                // otherwise three inactive, unskinned low-detail spheres would also draw a rigid clear coat.
                bool draw = Show && valid && c.Source.enabled && c.Source.gameObject.activeInHierarchy && !c.Source.forceRenderingOff && c.Source.isVisible;
                c.Renderer.enabled = draw;
                if (!draw || c.Gpu) continue;
                c.Source.GetPropertyBlock(c.Properties);
                bool held = c.Properties.HasFloat(HeldId) ? c.Properties.GetFloat(HeldId) != 0f : c.Material.HasProperty(HeldId) && c.Material.GetFloat(HeldId) != 0f;
                float alpha = c.Properties.HasFloat(AlphaId) ? c.Properties.GetFloat(AlphaId) : c.Material.HasProperty(AlphaId) ? c.Material.GetFloat(AlphaId) : 0f;
                // useScale: true = vertices in the renderer's own local space. With false Unity keeps the
                // transform's scale inside the vertices, and the coat (a child of that transform) gets it twice:
                // on 2x largos that drew a second, double-size glass bubble around the slime.
                if (c.Skin != null) c.Skin.BakeMesh(c.Mesh, true);
                else if (c.Shell == null) c.Mesh.vertices = c.Rest;
                var verts = c.Skin != null || c.Shell == null ? c.Mesh.vertices : c.Rest;
                var vs = MemoryMarshal.Cast<Vector3, V>(verts.AsSpan());
                var m = c.Transform.localToWorldMatrix; var inv = c.Transform.worldToLocalMatrix;
                var localDelta = Direction(inv, w.Delta);
                bool warp = !held && !c.Rigid;
                if (c.Shell != null)
                {
                    // plain Mosaic slime: the SR1 facet shell rides on the baked body's triangles, then gets the same warp
                    var sh = c.Shell;
                    sh.Place(vs);
                    var ps = sh.Positions; var pn = sh.Normals; var lift = sh.Lift;
                    for (int j = 0; j < ps.Length; j++)
                    {
                        var p = ps[j];
                        var displacement = warp ? Displace(p, w, m, inv, localDelta) : V.Zero;
                        _maxWarp = MathF.Max(_maxWarp, displacement.Length());
                        ps[j] = p + displacement + pn[j] * (lift[j] * alpha);
                    }
                    sh.Upload();
                    _vertices += ps.Length;
                    continue;
                }
                var ns = c.Skin != null ? c.Mesh.normals : c.Normals;
                var normals = MemoryMarshal.Cast<Vector3, V>(ns.AsSpan());
                for (int j = 0; j < vs.Length; j++)
                {
                    var p = vs[j];
                    var displacement = warp ? Displace(p, w, m, inv, localDelta) : V.Zero;
                    _maxWarp = MathF.Max(_maxWarp, displacement.Length());
                    float a = j < c.Alpha.Length ? 1f - c.Alpha[j] : 0f;
                    vs[j] = p + displacement + normals[j] * (a * a * alpha + 0.012f);
                }
                c.Mesh.vertices = verts;
                c.Mesh.RecalculateBounds();
                _vertices += verts.Length;
            }
            _ms = _ms * 0.9 + 0.1 * (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>SR/AMP/Slime/Body's vertex warp: the wobble along the movement, then the local contact squash (+ bulge).</summary>
        static V Displace(V p, in Warp w, in Matrix4x4 m, in Matrix4x4 inv, V localDelta)
        {
            var displacement = V.Zero;
            if (w.Wobble)
            {
                var q = Unit(Point(m, p) - w.Center);
                float t = Smooth((V.Dot(w.Dir, q) * 0.5f + 0.2f) * 1.428571f);
                float vertical = 1f - q.Y * q.Y;
                displacement = localDelta * (t * (0.5f * vertical + 0.5f * t) * 0.5f);
            }
            var pv = p + displacement;
            if (w.Squash != 0f && w.NormalSq > 0.000001f && w.Radius > 0.000001f)
            {
                var world = Point(m, pv); var d = world - w.Surface;
                float influence = Smooth(1f - d.Length() / w.Radius);
                float bulge = Smooth(MathF.Abs(w.MaskRange) > 0.000001f ? (influence - w.MaskMin) / w.MaskRange : 0f);
                var projection = Point(inv, world - w.Normal * (V.Dot(d, w.Normal) / w.NormalSq));
                var radial = Unit(p - w.UnitNormal * V.Dot(p, w.UnitNormal));
                displacement += ((projection - pv) * influence + radial * (w.Bulge * bulge)) * w.Squash;
            }
            return displacement;
        }

        struct Warp
        {
            internal V Center, Normal, Surface, Delta, Dir, UnitNormal;
            internal float Influence, Bulge, MaskMin, MaskRange, Radius, Squash, NormalSq;
            internal bool Wobble;
        }
        static V Vec(Vector4 v) => new(v.x, v.y, v.z);
        static V Unit(V p) { float s = p.LengthSquared(); return s > 1e-12f ? p * (1f / MathF.Sqrt(s)) : V.Zero; }
        static float Smooth(float t) { t = Math.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
        static V Point(Matrix4x4 m, V p) => new(m.m00*p.X + m.m01*p.Y + m.m02*p.Z + m.m03, m.m10*p.X + m.m11*p.Y + m.m12*p.Z + m.m13, m.m20*p.X + m.m21*p.Y + m.m22*p.Z + m.m23);
        static V Direction(Matrix4x4 m, V p) => new(m.m00*p.X + m.m01*p.Y + m.m02*p.Z, m.m10*p.X + m.m11*p.Y + m.m12*p.Z, m.m20*p.X + m.m21*p.Y + m.m22*p.Z);

        sealed class Coat : IDisposable
        {
            internal readonly Renderer Source;
            internal readonly SkinnedMeshRenderer Skin;
            internal readonly Mesh Original, Mesh;
            internal readonly UnityEngine.Renderer Renderer;
            /// <summary>Unreadable source mesh: the coat draws the original mesh on the GPU (skinned by the source bones), no CPU warp.</summary>
            internal readonly bool Gpu;
            internal readonly Transform Transform;
            internal readonly Il2CppStructArray<Vector3> Rest, Normals;
            internal readonly float[] Alpha;
            internal readonly MaterialPropertyBlock Properties = new();
            internal Material Material;
            /// <summary>The source shader has no slime vertex warp ("_NoWobble" parts): the coat must not warp either.</summary>
            internal readonly bool Rigid;
            /// <summary>Plain Mosaic slime (SlimeFacets): Shell = the SR1 facet shell, drawn instead of the body copy (null: its fit failed).</summary>
            internal readonly bool Facet;
            internal readonly int Version;
            internal readonly SlimeFacets.Shell Shell;
            readonly GameObject _go;
            internal Coat(UnityEngine.Renderer source, SkinnedMeshRenderer skin, Mesh original, Material material, bool gpu = false, Transform facets = null)
            {
                Source = source; Skin = skin; Original = original; Material = material; Transform = source.transform; Gpu = gpu;
                Rigid = gpu || material.shader != null && material.shader.name.Contains("NoWobble");
                Facet = facets != null; Version = SlimeFacets.Version;
                _go = new GameObject("MosaicElasticGlass") { layer = source.gameObject.layer };
                _go.transform.SetParent(Transform, false);
                if (gpu)
                {
                    Alpha = Array.Empty<float>();
                    if (skin != null)
                    {
                        var s = _go.AddComponent<SkinnedMeshRenderer>();
                        s.sharedMesh = original; s.bones = skin.bones; s.rootBone = skin.rootBone;
                        s.localBounds = skin.localBounds; s.updateWhenOffscreen = skin.updateWhenOffscreen;
                        Renderer = s;
                    }
                    else
                    {
                        _go.transform.localScale = Vector3.one * 1.015f;   // a hair outside the part
                        _go.AddComponent<MeshFilter>().sharedMesh = original;
                        Renderer = _go.AddComponent<MeshRenderer>();
                    }
                }
                else
                {
                    Rest = original.vertices; Normals = original.normals;
                    var colors = original.colors; Alpha = new float[original.vertexCount];
                    for (int i = 0; i < Alpha.Length; i++) Alpha[i] = colors != null && i < colors.Length ? colors[i].a : 1f;
                    Mesh = Object.Instantiate(original); Mesh.name = "MosaicElasticGlassMesh"; Mesh.MarkDynamic();
                    if (Facet) Shell = SlimeFacets.Shell.Create(facets, source, skin, original, Rest, Normals, Alpha);
                    if (Shell != null) _go.name = SlimeFacets.CoatName;
                    _go.AddComponent<MeshFilter>().sharedMesh = Shell != null ? Shell.Mesh : Mesh;
                    Renderer = _go.AddComponent<MeshRenderer>();
                }
                Renderer.sharedMaterial = (Shell != null ? GordoFacets.Glass() : null) ?? Visuals.MosaicElasticGlassMaterial();
                Renderer.shadowCastingMode = ShadowCastingMode.Off; Renderer.receiveShadows = false;
                Renderer.reflectionProbeUsage = source.reflectionProbeUsage;
                Renderer.lightProbeUsage = source.lightProbeUsage; Renderer.renderingLayerMask = source.renderingLayerMask;
                Renderer.enabled = false;
            }
            public void Dispose() { if (_go != null) Object.Destroy(_go); if (Mesh != null) Object.Destroy(Mesh); if (Shell?.Mesh != null) Object.Destroy(Shell.Mesh); }
        }
    }

    // Native controller fills the shader arrays in LateUpdate. Run after it, not one frame behind it.
    [HarmonyPatch(typeof(SlimeSquashWobbleController), nameof(SlimeSquashWobbleController.LateUpdate))]
    static class ElasticMosaicUpdate
    {
        static void Postfix(SlimeSquashWobbleController __instance) => ElasticGlassRig.UpdateAll(__instance);
    }
}
