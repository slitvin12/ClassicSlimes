using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// SR1 quantum swirl baked into the UV layout of SR2's body mesh. Every texel is mapped back to the point of the body
    /// surface it covers (barycentric rasterisation of the mesh in UV space) and the spiral is drawn in polar coordinates
    /// around a direction from the body centre, so the slime shows ONE round spiral whatever its UV islands look like
    /// (a flat spiral texture came out stretched and cut by the SR2 UV seams).
    /// SR1 render (QuantumHappy): one light-gold band of ~1.35 turns, winding clockwise outwards from just left of the
    /// mouth, outer end at the lower left; band width about a third of the spacing.
    /// </summary>
    public static class Swirl
    {
        // mesh space: body centre (0, 0.5, 0), front = +Z, +X = the viewer's left
        public static Vector3 Centre = new Vector3(0f, 0.5f, 0f);
        public static Vector3 Dir = new Vector3(0.2f, -0.12f, 1f);
        public static float Radius = 0.6f;     // outer radius (radians on the body)
        public static float Turns = 1.35f;     // from the outer end to the inner tip
        public static float Tip = 0.28f;       // radius of the inner tip / Radius
        public static float Width = 0.055f;    // half width of the band (radians)
        public static float EndDeg = -120f;    // screen angle of the outer end (0 = viewer's right, 90 = up)
        public static float Soft = 0.025f;     // edge softness (radians)
        public static bool Clockwise = true;   // winding outwards as seen from the front
        public static int UvSet = -1;          // -1 = auto (fewest overlaps), 0 = uv0, 1 = uv1
        public const int N = 512;
        public static string Info = "";
        /// <summary>UV set of the last bake (for _ColorMaskUV1).</summary>
        public static int LastSet;
        public static Mesh BodyMesh;
        static Texture2D _last;

        static void Basis(out Vector3 c, out Vector3 e1, out Vector3 e2)
        {
            c = Dir.normalized;
            e1 = Vector3.Cross(c, Vector3.up).normalized;   // the viewer's right
            e2 = Vector3.Cross(e1, c);                       // up
        }

        /// <summary>Spiral coverage (0-1) of a body point in mesh space.</summary>
        static float At(Vector3 p, Vector3 c, Vector3 e1, Vector3 e2)
        {
            var d = (p - Centre).normalized;
            float r = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, c), -1f, 1f));
            if (r > Radius + Width + Soft * 2f) return 0f;
            float th = Mathf.Atan2(Vector3.Dot(d, e2), Vector3.Dot(d, e1));
            float te = EndDeg * Mathf.Deg2Rad;
            float s = Clockwise ? 1f : -1f;   // clockwise outwards = counter-clockwise walking inwards from the outer end
            float rTip = Radius * Tip;
            float span = 2f * Mathf.PI * Mathf.Max(0.1f, Turns);
            float beta = (Radius - rTip) / span;   // radius lost per radian walked inwards
            float phi0 = Mathf.Repeat(s * (th - te), 2f * Mathf.PI);
            float best = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                float phi = phi0 + 2f * Mathf.PI * k;
                if (phi > span) break;
                best = Mathf.Min(best, Mathf.Abs(r - (Radius - beta * phi)));
            }
            // round caps at both ends of the band
            var q = new Vector2(r * Mathf.Cos(th), r * Mathf.Sin(th));
            var pe = new Vector2(Radius * Mathf.Cos(te), Radius * Mathf.Sin(te));
            float tt = te + s * span;
            var pt = new Vector2(rTip * Mathf.Cos(tt), rTip * Mathf.Sin(tt));
            best = Mathf.Min(best, Mathf.Min((q - pe).magnitude, (q - pt).magnitude));
            return Mathf.Clamp01((Width - best) / Soft + 0.5f);
        }

        /// <summary>Rasterises the mesh triangles in UV space: per texel the body point it covers (first triangle wins).</summary>
        static int Raster(Vector2[] uv, Vector3[] v, int[] tris, int n, Vector3[] pos, bool[] has, out int written)
        {
            int overlaps = 0; written = 0;
            const float eps = 0.02f;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector2 a = uv[i0] * n, b = uv[i1] * n, c = uv[i2] * n;
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, n - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, n - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, n - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, n - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float wa = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
                        float wb = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
                        float wc = 1f - wa - wb;
                        if (wa < -eps || wb < -eps || wc < -eps) continue;
                        var p = v[i0] * wa + v[i1] * wb + v[i2] * wc;
                        int idx = y * n + x;
                        if (has[idx]) { if ((p - pos[idx]).sqrMagnitude > 0.08f * 0.08f) overlaps++; continue; }
                        has[idx] = true; pos[idx] = p; written++;
                    }
            }
            return overlaps;
        }

        static float OverlapFraction(Vector2[] uv, Vector3[] v, int[] tris)
        {
            if (uv == null || uv.Length != v.Length) return 2f;
            const int n = 128;
            var pos = new Vector3[n * n]; var has = new bool[n * n];
            int ov = Raster(uv, v, tris, n, pos, has, out int written);
            return written == 0 ? 2f : ov / (float)written;
        }

        static readonly System.Collections.Generic.Dictionary<string, (Texture2D tex, int set)> _shared = new();
        /// <summary>
        /// Spiral mask for a largo body mesh, baked once per mesh and kept (never destroyed by later bakes,
        /// unlike <see cref="Bake"/>'s single debug texture). <paramref name="set"/> = uv set for _ColorMaskUV1.
        /// </summary>
        public static Texture2D Shared(Mesh mesh, out int set)
        {
            set = 0;
            if (mesh == null) return null;
            if (_shared.TryGetValue(mesh.name, out var e) && e.tex != null) { set = e.set; return e.tex; }
            var keep = _last; _last = null;   // this bake must not destroy the base quantum's mask
            int lastSet = LastSet;
            var tex = Bake(mesh);
            _last = keep;
            if (tex != null) { set = LastSet; _shared[mesh.name] = (tex, set); }
            LastSet = lastSet;
            return tex;
        }

        /// <summary>The spiral mask (red channel) for the body mesh, or null when the mesh can't be read.</summary>
        public static Texture2D Bake(Mesh mesh, bool debug = false)
        {
            if (mesh == null) { Info = "no mesh"; return null; }
            if (!mesh.isReadable) { Info = mesh.name + " not readable"; return null; }
            try
            {
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var uv0 = mesh.uv; var uv1 = mesh.uv2;
                float o0 = OverlapFraction(uv0, verts, tris), o1 = OverlapFraction(uv1, verts, tris);
                int set = UvSet >= 0 ? UvSet : (o1 < 1f && o1 < o0 * 0.5f ? 1 : 0);
                var uv = set == 1 ? uv1 : uv0;
                if (uv == null || uv.Length != verts.Length) { Info = "no uv" + set; return null; }
                var pos = new Vector3[N * N]; var has = new bool[N * N];
                int ov = Raster(uv, verts, tris, N, pos, has, out int written);
                Basis(out var c, out var e1, out var e2);
                var val = new float[N * N];
                for (int i = 0; i < val.Length; i++) if (has[i]) val[i] = At(pos[i], c, e1, e2);
                // grow the islands a few texels so bilinear / mip sampling at the UV seams doesn't pull in black
                var grown = (bool[])has.Clone();
                for (int pass = 0; pass < 4; pass++)
                {
                    var next = (bool[])grown.Clone();
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            int idx = y * N + x;
                            if (grown[idx]) continue;
                            float sum = 0f; int cnt = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                                    int j = yy * N + xx;
                                    if (!grown[j]) continue;
                                    sum += val[j]; cnt++;
                                }
                            if (cnt == 0) continue;
                            val[idx] = sum / cnt; next[idx] = true;
                        }
                    grown = next;
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "ClassicSlimes_quantumSwirl" };
                var px = new Color[N * N];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(val[i], 0f, 0f, 1f);
                tex.SetPixels(px);
                tex.Apply(true);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                LastSet = set;
                Info = $"{mesh.name} v={verts.Length} t={tris.Length / 3} overlap uv0={o0:P1} uv1={(o1 > 1f ? "-" : o1.ToString("P1"))} -> uv{set}, texels {written} (+{ov} overlapping)";
                ClassicSlimesMod.Log.Msg("[swirl] " + Info);
                if (debug) Dump(px, pos, has);
                if (_last != null && _last != tex) Object.Destroy(_last);
                _last = tex;
                return tex;
            }
            catch (Exception e) { Info = e.Message; ClassicSlimesMod.Log.Warning("[swirl] bake: " + e); return null; }
        }

        /// <summary>Debug PNGs in UserData/ClassicSlimes: the mask over the UV islands and the body position of each texel.</summary>
        static void Dump(Color[] px, Vector3[] pos, bool[] has)
        {
            var a = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            var b = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            var pa = new Color[N * N]; var pb = new Color[N * N];
            for (int i = 0; i < pa.Length; i++)
            {
                pa[i] = new Color(px[i].r, has[i] ? 0.25f + 0.5f * px[i].r : 0f, has[i] ? 0.25f : 0f, 1f);
                var p = pos[i] - Centre;
                pb[i] = has[i] ? new Color(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f, 1f) : Color.black;
            }
            a.SetPixels(pa); a.Apply(false);
            b.SetPixels(pb); b.Apply(false);
            File.WriteAllBytes(Path.Combine(Diag.Dir, "qswirl.png"), Il2CppImageConversionManager.EncodeToPNG(a));
            File.WriteAllBytes(Path.Combine(Diag.Dir, "qswirl_pos.png"), Il2CppImageConversionManager.EncodeToPNG(b));
            Object.Destroy(a); Object.Destroy(b);
        }
    }
}
