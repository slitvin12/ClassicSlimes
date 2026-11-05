using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using V = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;
using NM = System.Numerics.Matrix4x4;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// The Mosaic gordo's SR1 facet shell (mosaicGordo_LOD0, 115 bevelled facets, GordoFacets.Glass) on the plain Mosaic
    /// slimes, over the gordo's smooth body (SR1 gradient, no tiles, no radiant waves): the shimmer is on the glass.
    /// Elastic: the shell is not skinned to SR1's six bones. Every shell vertex rides on the nearest triangle of the
    /// slime's own body (found once per body mesh in its rest pose), so it follows the baked body every frame (bones,
    /// jumps, eating), then gets the same SR2 shader wobble / contact squash as the elastic glass coat (ElasticGlassRig).
    /// Largos keep the tile body and its elastic coat.
    /// </summary>
    public static class SlimeFacets
    {
        public static bool On = true;
        /// <summary>Extra scale on top of the fit (the fit keeps SR1's margin of the shell over the gordo body).</summary>
        public static float Scale = 1f;
        /// <summary>Minimum gap between the shell and the body, relative to the body height (the body never pokes through).</summary>
        public static float Gap = 0.01f;
        /// <summary>Body under the facets: "smooth" (the Mosaic gordo's SR1 gradient) or "tiles" (the Mosaic slime's tile body).</summary>
        public static string Body = "smooth";
        /// <summary>Shell mesh: "mosaic_LOD0" (SR1's Mosaic slime: the same 115 facets, slime-shaped) or "mosaicGordo_LOD0" (the gordo's shell).</summary>
        public static string ShellMesh = "mosaic_LOD0";
        /// <summary>Shells without an SR1 body to fit against (mosaic_LOD0 was SR1's body itself): the shell's box over the body's box.</summary>
        public static float Margin = 0.06f;
        public const string CoatName = "MosaicSlimeFacets";

        static readonly Dictionary<int, Fit> Fits = new();
        internal static int Version { get; private set; }
        static MelonLogger.Instance Log => ClassicSlimesMod.Log;

        /// <summary>The plain Mosaic slime body (tiles or the smooth body), not a largo's tinted copy.</summary>
        internal static bool IsPlainBody(Material m)
        {
            if (m == null) return false;
            var tiles = GordoFacets.Tiles; var smooth = GordoFacets.SmoothMat;
            return tiles != null && m == tiles || smooth != null && m == smooth;
        }

        /// <summary>Puts the wanted body (smooth under the facets, else the tiles) on a plain Mosaic body renderer.</summary>
        internal static Material SwapBody(Renderer r, Material m)
        {
            var want = On && Body == "smooth" ? GordoFacets.Smooth() : GordoFacets.Tiles;
            if (want == null || want == m) return m;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return m;
            mats[0] = want;
            r.sharedMaterials = mats;
            return want;
        }

        /// <summary>The shell fitted to one body mesh: per shell vertex its body triangle and its place in that triangle's frame.</summary>
        sealed class Fit
        {
            internal int BodyVerts;
            internal int[] Tri;
            /// <summary>Shell vertex - corner a, in (e1, e2, n / sqrt|n|) of its triangle: follows the triangle's stretch too.</summary>
            internal V[] Off;
            /// <summary>Shell normal / tangent in the triangle's orthonormal frame (t1, t2, n).</summary>
            internal V[] Nrm, Tan;
            internal float[] TanW, Lift;
            internal Mesh Template;
            internal string Info;
        }

        /// <summary>One coat's shell (ElasticGlassRig.Coat): positions / normals / tangents rebuilt from the baked body every frame.</summary>
        internal sealed class Shell
        {
            readonly Fit _f;
            internal readonly Mesh Mesh;
            readonly Il2CppStructArray<Vector3> _pos, _nrm;
            readonly Il2CppStructArray<Vector4> _tan;
            internal float[] Lift => _f.Lift;
            internal Span<V> Positions => MemoryMarshal.Cast<Vector3, V>(_pos.AsSpan());
            internal Span<V> Normals => MemoryMarshal.Cast<Vector3, V>(_nrm.AsSpan());

            Shell(Fit f)
            {
                _f = f;
                Mesh = Object.Instantiate(f.Template);
                Mesh.name = CoatName + "Mesh";
                Mesh.MarkDynamic();
                int n = f.Off.Length;
                _pos = new Il2CppStructArray<Vector3>(n);
                _nrm = new Il2CppStructArray<Vector3>(n);
                _tan = new Il2CppStructArray<Vector4>(n);
            }

            /// <summary>Null when the fit failed: the coat then stays the plain elastic glass.</summary>
            internal static Shell Create(Transform root, Renderer source, SkinnedMeshRenderer skin, Mesh body, Il2CppStructArray<Vector3> rest, Il2CppStructArray<Vector3> restNormals, float[] alpha)
            {
                if (root == null || body == null || rest == null) return null;
                int id = body.GetInstanceID();
                if (!Fits.TryGetValue(id, out var f))
                {
                    try { f = Build(body, rest, restNormals, alpha, MeshToRoot(root, source, skin, body)); }
                    catch (Exception e) { f = null; Log.Warning($"Mosaic slime facets {body.name}: {e}"); }
                    Fits[id] = f;
                }
                return f != null ? new Shell(f) : null;
            }

            internal void Place(ReadOnlySpan<V> body)
            {
                var f = _f;
                if (body.Length < f.BodyVerts) return;
                var pos = Positions; var nrm = Normals;
                var tan = MemoryMarshal.Cast<Vector4, V4>(_tan.AsSpan());
                var tri = f.Tri;
                for (int j = 0; j < pos.Length; j++)
                {
                    int k = j * 3;
                    V a = body[tri[k]], e1 = body[tri[k + 1]] - a, e2 = body[tri[k + 2]] - a, cr = V.Cross(e1, e2);
                    float l = cr.Length();
                    if (l < 1e-12f) continue;
                    V nf = cr / l, ns = cr / MathF.Sqrt(l), t1 = Unit(e1), t2 = V.Cross(nf, t1);
                    var o = f.Off[j];
                    pos[j] = a + e1 * o.X + e2 * o.Y + ns * o.Z;
                    var q = f.Nrm[j];
                    nrm[j] = t1 * q.X + t2 * q.Y + nf * q.Z;
                    var g = f.Tan[j];
                    tan[j] = new V4(t1 * g.X + t2 * g.Y + nf * g.Z, f.TanW[j]);
                }
            }

            internal void Upload()
            {
                Mesh.vertices = _pos;
                Mesh.normals = _nrm;
                Mesh.tangents = _tan;
                Mesh.RecalculateBounds();
            }
        }

        static NM Num(Matrix4x4 m) => new(m.m00, m.m10, m.m20, m.m30, m.m01, m.m11, m.m21, m.m31, m.m02, m.m12, m.m22, m.m32, m.m03, m.m13, m.m23, m.m33);

        /// <summary>Body mesh space -> the slime's root space (current pose, averaged over the bones), for the slime's up / forward.</summary>
        static NM MeshToRoot(Transform root, Renderer source, SkinnedMeshRenderer skin, Mesh body)
        {
            var w2r = root.worldToLocalMatrix;
            if (skin != null)
            {
                var bones = skin.bones; var bind = body.bindposes;
                var sum = new NM(); int n = 0;
                if (bones != null && bind != null)
                    for (int i = 0; i < bones.Length && i < bind.Length; i++)
                    {
                        if (bones[i] == null) continue;
                        sum += Num(w2r * bones[i].localToWorldMatrix * bind[i]);
                        n++;
                    }
                if (n > 0) return sum * (1f / n);
            }
            return Num(w2r * source.transform.localToWorldMatrix);
        }

        static V Unit(V p) { float s = p.LengthSquared(); return s > 1e-20f ? p * (1f / MathF.Sqrt(s)) : V.Zero; }
        static V Vec(Vector3 v) => new(v.x, v.y, v.z);
        /// <summary>A unit vector within ~25 degrees of a mesh axis becomes that axis.</summary>
        static V Snap(V v) => MathF.Abs(v.X) > 0.9f ? new V(MathF.Sign(v.X), 0f, 0f) : MathF.Abs(v.Y) > 0.9f ? new V(0f, MathF.Sign(v.Y), 0f) : MathF.Abs(v.Z) > 0.9f ? new V(0f, 0f, MathF.Sign(v.Z)) : v;
        static double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;

        /// <summary>Closest point of triangle abc to p (Ericson), = a + u (b - a) + v (c - a).</summary>
        static V Closest(V p, V a, V b, V c, out float u, out float v)
        {
            V ab = b - a, ac = c - a, ap = p - a;
            float d1 = V.Dot(ab, ap), d2 = V.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { u = 0f; v = 0f; return a; }
            V bp = p - b; float d3 = V.Dot(ab, bp), d4 = V.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { u = 1f; v = 0f; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { u = d1 / (d1 - d3); v = 0f; return a + ab * u; }
            V cp = p - c; float d5 = V.Dot(ab, cp), d6 = V.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { u = 0f; v = 1f; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { u = 0f; v = d2 / (d2 - d6); return a + ac * v; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { float t = (d4 - d3) / ((d4 - d3) + (d5 - d6)); u = 1f - t; v = t; return b + (c - b) * t; }
            float den = 1f / (va + vb + vc);
            u = vb * den; v = vc * den;
            return a + ab * u + ac * v;
        }

        /// <summary>
        /// The SR1 shell around this body (same fit as the gordo: per axis, SR1's shell-to-gordo-body margin), then every
        /// shell vertex anchored to its nearest body triangle. Rest pose, body mesh space.
        /// </summary>
        static Fit Build(Mesh body, Il2CppStructArray<Vector3> rest, Il2CppStructArray<Vector3> restNormals, float[] alpha, NM meshToRoot)
        {
            long t0 = Stopwatch.GetTimestamp();
            var src = Res.Mesh(ShellMesh);
            if (src == null) { Log.Warning("Mosaic slime facets: mesh " + ShellMesh + " missing"); return null; }
            var rv = MemoryMarshal.Cast<Vector3, V>(rest.AsSpan()).ToArray();
            var rn = restNormals != null && restNormals.Length == rest.Length ? MemoryMarshal.Cast<Vector3, V>(restNormals.AsSpan()).ToArray() : null;
            var rt = body.triangles.AsSpan().ToArray();
            if (rv.Length == 0 || rt.Length < 3) return null;
            // the slime's up / forward in mesh space
            if (!NM.Invert(meshToRoot, out var inv)) inv = NM.Identity;
            var up = Unit(V.TransformNormal(V.UnitY, inv));
            var fw = V.TransformNormal(V.UnitZ, inv);
            fw = Unit(fw - up * V.Dot(fw, up));
            if (up == V.Zero || fw == V.Zero) { up = V.UnitY; fw = V.UnitZ; }
            // the pose at fit time tilts these a little (and the fit is cached per mesh): snap to the mesh axes when close
            up = Snap(up);
            fw = Snap(Unit(fw - up * V.Dot(fw, up)));
            fw = Unit(fw - up * V.Dot(fw, up));
            if (fw == V.Zero) fw = MathF.Abs(up.Z) < 0.9f ? Unit(V.UnitZ - up * up.Z) : Unit(V.UnitX - up * up.X);
            var ri = V.Cross(up, fw);
            V min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var p in rv) { var q = new V(V.Dot(p, ri), V.Dot(p, up), V.Dot(p, fw)); min = V.Min(min, q); max = V.Max(max, q); }
            var sv = MemoryMarshal.Cast<Vector3, V>(src.vertices.AsSpan()).ToArray();
            long tR = Stopwatch.GetTimestamp();
            V size = max - min, s1, c1;
            if (ShellMesh == GordoFacets.MeshName) { s1 = Vec(GordoFacets.Sr1BodyMax - GordoFacets.Sr1BodyMin); c1 = Vec(GordoFacets.Sr1BodyMax + GordoFacets.Sr1BodyMin) * 0.5f; }
            else
            {
                V lo = new(float.MaxValue), hi = new(float.MinValue);
                foreach (var v in sv) { lo = V.Min(lo, v); hi = V.Max(hi, v); }
                s1 = (hi - lo) / (1f + Margin); c1 = (lo + hi) * 0.5f;
            }
            V k = size / s1 * Scale, c2 = (min + max) * 0.5f;
            var snA = src.normals;
            var sn = snA != null && snA.Length == sv.Length ? MemoryMarshal.Cast<Vector3, V>(snA.AsSpan()).ToArray() : new V[sv.Length];
            int n = sv.Length;
            var P = new V[n]; var N = new V[n];
            for (int j = 0; j < n; j++)
            {
                var q = c2 + k * (sv[j] - c1);
                P[j] = ri * q.X + up * q.Y + fw * q.Z;
                var m = sn[j] / k;
                N[j] = Unit(ri * m.X + up * m.Y + fw * m.Z);
            }
            // body triangles: bounding spheres for the search, outward normals for the gap
            int tc = rt.Length / 3;
            var cen = new V[tc]; var rad = new float[tc]; var nout = new V[tc]; var ok = new bool[tc];
            for (int t = 0; t < tc; t++)
            {
                int ia = rt[3 * t], ib = rt[3 * t + 1], ic = rt[3 * t + 2];
                if (ia >= rv.Length || ib >= rv.Length || ic >= rv.Length) continue;
                V a = rv[ia], b = rv[ib], c = rv[ic], cr = V.Cross(b - a, c - a);
                float l = cr.Length();
                if (l < 1e-10f) continue;
                var nf = cr / l;
                if (rn != null && V.Dot(nf, rn[ia] + rn[ib] + rn[ic]) < 0f) nf = -nf;
                nout[t] = nf; ok[t] = true;
                cen[t] = (a + b + c) / 3f;
                rad[t] = MathF.Sqrt(MathF.Max(V.DistanceSquared(a, cen[t]), MathF.Max(V.DistanceSquared(b, cen[t]), V.DistanceSquared(c, cen[t]))));
            }
            float gap = Gap * size.Y, hmin = float.MaxValue, hsum = 0f; int pushed = 0;
            var f = new Fit { BodyVerts = rv.Length, Tri = new int[n * 3], Off = new V[n], Nrm = new V[n], Tan = new V[n], TanW = new float[n], Lift = new float[n] };
            for (int j = 0; j < n; j++)
            {
                var p = P[j]; int bt = -1; float bd = float.MaxValue, bu = 0f, bw = 0f; V bc = default;
                for (int t = 0; t < tc; t++)
                {
                    if (!ok[t]) continue;
                    float dc = V.Distance(p, cen[t]) - rad[t];
                    if (dc > 0f && dc * dc >= bd) continue;
                    var c = Closest(p, rv[rt[3 * t]], rv[rt[3 * t + 1]], rv[rt[3 * t + 2]], out float u, out float w);
                    float d = V.DistanceSquared(p, c);
                    if (d < bd) { bd = d; bt = t; bu = u; bw = w; bc = c; }
                }
                if (bt < 0) return null;
                float h = V.Dot(p - bc, nout[bt]);
                hmin = MathF.Min(hmin, h); hsum += h;
                if (h < gap) { p += nout[bt] * (gap - h); pushed++; }
                P[j] = p;
                int i0 = rt[3 * bt], i1 = rt[3 * bt + 1], i2 = rt[3 * bt + 2];
                f.Tri[3 * j] = i0; f.Tri[3 * j + 1] = i1; f.Tri[3 * j + 2] = i2;
                V a = rv[i0], e1 = rv[i1] - a, e2 = rv[i2] - a, cr = V.Cross(e1, e2);
                float l = cr.Length();
                V ns = cr / MathF.Sqrt(l), d0 = p - a;
                float g = V.Dot(d0, ns) / V.Dot(ns, ns);
                var dt = d0 - ns * g;
                float g11 = V.Dot(e1, e1), g12 = V.Dot(e1, e2), g22 = V.Dot(e2, e2), det = g11 * g22 - g12 * g12, r1 = V.Dot(dt, e1), r2 = V.Dot(dt, e2);
                f.Off[j] = det > 1e-20f ? new V((r1 * g22 - r2 * g12) / det, (r2 * g11 - r1 * g12) / det, g) : new V(bu, bw, g);
                float L(int i) { float x = alpha != null && i < alpha.Length ? 1f - alpha[i] : 0f; return x * x; }
                f.Lift[j] = (1f - bu - bw) * L(i0) + bu * L(i1) + bw * L(i2);
            }
            long tS = Stopwatch.GetTimestamp();
            // the rest shell as the coats' template; its tangents (normal map bevels) go into the triangle frames too
            var tm = new Mesh { name = CoatName + "_" + body.name, hideFlags = HideFlags.HideAndDontSave };
            tm.vertices = MemoryMarshal.Cast<V, Vector3>(P.AsSpan()).ToArray();
            tm.normals = MemoryMarshal.Cast<V, Vector3>(N.AsSpan()).ToArray();
            tm.uv = src.uv;
            tm.triangles = src.triangles;
            tm.RecalculateTangents();
            tm.RecalculateBounds();
            var tg = tm.tangents;
            for (int j = 0; j < n; j++)
            {
                V a = rv[f.Tri[3 * j]], e1 = rv[f.Tri[3 * j + 1]] - a, e2 = rv[f.Tri[3 * j + 2]] - a;
                V nf = Unit(V.Cross(e1, e2)), t1 = Unit(e1), t2 = V.Cross(nf, t1), nn = N[j];
                f.Nrm[j] = new V(V.Dot(nn, t1), V.Dot(nn, t2), V.Dot(nn, nf));
                var tv = tg != null && j < tg.Length ? new V(tg[j].x, tg[j].y, tg[j].z) : t1;
                f.Tan[j] = new V(V.Dot(tv, t1), V.Dot(tv, t2), V.Dot(tv, nf));
                f.TanW[j] = tg != null && j < tg.Length ? tg[j].w : 1f;
            }
            f.Template = tm;
            long tE = Stopwatch.GetTimestamp();
            f.Info = $"{ShellMesh} on {body.name} {rv.Length}v/{tc}t: up ({up.X:F2}, {up.Y:F2}, {up.Z:F2}) fwd ({fw.X:F2}, {fw.Y:F2}, {fw.Z:F2}), body ({size.X:F3}, {size.Y:F3}, {size.Z:F3}), shell x({k.X:F3}, {k.Y:F3}, {k.Z:F3}), gap min {hmin / size.Y:F3} avg {hsum / n / size.Y:F3} (pushed {pushed}/{n}), {Ms(t0, tE):F1} ms (read {Ms(t0, tR):F1}, search {Ms(tR, tS):F1}, mesh {Ms(tS, tE):F1})";
            Log.Msg("Mosaic slime facets: " + f.Info);
            return f;
        }

        /// <summary>slimefacets [on|off|mesh slime|gordo|scale k|margin m|gap g|body smooth|tiles]: the SR1 facet shell of the plain Mosaic slimes.</summary>
        public static string Cmd(string[] a)
        {
            string op = a.Length > 1 ? a[1] : "info";
            float F(int i, float d) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
            bool refit = false;
            switch (op)
            {
                case "on": On = true; break;
                case "off": On = false; break;
                case "scale": Scale = F(2, Scale); refit = true; break;
                case "gap": Gap = F(2, Gap); refit = true; break;
                case "margin": Margin = F(2, Margin); refit = true; break;
                case "mesh": ShellMesh = a.Length > 2 ? (a[2] == "gordo" ? GordoFacets.MeshName : a[2] == "slime" ? "mosaic_LOD0" : a[2]) : ShellMesh; refit = true; break;
                case "body": Body = a.Length > 2 ? a[2] : Body; break;
            }
            if (refit)
            {
                foreach (var f in Fits.Values) if (f?.Template != null) Object.Destroy(f.Template);
                Fits.Clear();
                Version++;
            }
            ElasticGlassRig.Resync();
            var sb = new StringBuilder($"slimefacets {op}: on={On} mesh={ShellMesh} scale={Scale:F3} margin={Margin:F3} gap={Gap:F3} body={Body}, {ElasticGlassRig.Census()}");
            foreach (var f in Fits.Values) if (f != null) sb.Append("\n  ").Append(f.Info);
            return sb.ToString();
        }
    }
}
