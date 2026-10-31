using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>Small rendering helpers: procedural meshes (with vertex colours for particle shaders), billboards, particle emitters, generated textures.</summary>
    public static class Gfx
    {
        static Mesh _sphere, _quad;

        static T Keep<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(o);
            return o;
        }

        /// <summary>UV sphere, diameter 1, white vertex colours.</summary>
        public static Mesh SphereMesh
        {
            get
            {
                if (_sphere != null) return _sphere;
                const int seg = 40, rings = 28;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
                for (int r = 0; r <= rings; r++)
                {
                    float th = Mathf.PI * r / rings, y = Mathf.Cos(th), s = Mathf.Sin(th);
                    for (int i = 0; i <= seg; i++)
                    {
                        float ph = 2f * Mathf.PI * i / seg;
                        var d = new Vector3(s * Mathf.Cos(ph), y, s * Mathf.Sin(ph));
                        v.Add(d * 0.5f); n.Add(d); uv.Add(new Vector2((float)i / seg, 1f - (float)r / rings)); c.Add(Color.white);
                    }
                }
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = r * (seg + 1) + i, b = a + seg + 1;
                        t.Add(a); t.Add(a + 1); t.Add(b);
                        t.Add(a + 1); t.Add(b + 1); t.Add(b);
                    }
                var m = new Mesh { name = "ClassicSlimes_Sphere" };
                m.vertices = v.ToArray(); m.normals = n.ToArray(); m.uv = uv.ToArray(); m.colors = c.ToArray(); m.triangles = t.ToArray();
                m.RecalculateTangents();
                m.RecalculateBounds();
                _sphere = Keep(m);
                return _sphere;
            }
        }

        /// <summary>Unit quad in the XY plane facing -Z (towards a camera whose rotation it copies).</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                var m = new Mesh { name = "ClassicSlimes_Quad" };
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                m.RecalculateTangents();
                m.RecalculateBounds();
                _quad = Keep(m);
                return _quad;
            }
        }

        public static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, float scale)
        {
            var go = new GameObject(name);
            if (parent != null) { go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer; }
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public static GameObject Billboard(string name, Transform parent, Material mat, Vector3 localPos, float scale)
        {
            var go = MeshObj(name, parent, Quad, mat, localPos, scale);
            go.AddComponent<Billboard>();
            return go;
        }

        static ParticleSystem.MinMaxCurve Range(float a, float b)
        {
            var c = new ParticleSystem.MinMaxCurve(a);
            if (Mathf.Abs(a - b) > 1e-4f) { c.m_Mode = ParticleSystemCurveMode.TwoConstants; c.m_ConstantMin = a; c.m_ConstantMax = b; }
            return c;
        }

        /// <summary>Copy of SR2's gold slime sparkle emitter with another material/colours (keeps its fade-in/out curves).</summary>
        public static ParticleSystem Sparkles(string name, Transform parent, Vector3 localPos, Material mat, Color c0, Color c1,
            float rate, float life, float size0, float size1, float radius, int max, float speed = 0f)
        {
            var src = Fx.Kept("gold_sparkles");
            if (src == null || parent == null) return null;
            try
            {
                var go = Object.Instantiate(src, parent);
                go.name = name;
                go.SetActive(true);
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
                if (ps == null) { Object.Destroy(go); return null; }
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.startLifetimeMultiplier = life;
                main.startSize = Range(size0, size1);
                main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
                main.startSpeed = Range(speed * 0.5f, speed);
                main.maxParticles = max;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.loop = true;
                var em = ps.emission; em.rateOverTimeMultiplier = rate;
                var sh = ps.shape; sh.radius = radius;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null && mat != null) r.sharedMaterial = mat;
                ps.Play(true);
                return ps;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Sparkles {name}: {e.Message}"); return null; }
        }

        /// <summary>World-space trail: the same sparkle emitter, but particles are emitted per metre moved and stay behind.</summary>
        public static ParticleSystem Trail(string name, Transform parent, Vector3 localPos, Material mat, Color c0, Color c1,
            float perMetre, float life, float size0, float size1, float radius, int max)
        {
            var ps = Sparkles(name, parent, localPos, mat, c0, c1, 0f, life, size0, size1, radius, max);
            if (ps == null) return null;
            try
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifierMultiplier = -0.03f;
                var em = ps.emission;
                em.rateOverTimeMultiplier = 0f;
                try { em.rateOverDistanceMultiplier = perMetre; }
                catch { em.rateOverDistance = new ParticleSystem.MinMaxCurve(perMetre); }
                ps.Play(true);
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Trail {name}: {e.Message}"); }
            return ps;
        }

        public static void SetEmitting(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on && !ps.isEmitting) ps.Play(true);
            else if (!on && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ------------------------------------------------------------------ textures
        static readonly Dictionary<string, Texture2D> _gen = new();

        public static Texture2D Generated(string key, int w, int h, Func<int, int, Color> f, bool linear = true, FilterMode filter = FilterMode.Bilinear)
        {
            if (_gen.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { name = "ClassicSlimes_" + key };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels(px);
            t.Apply(true);
            t.filterMode = filter;
            t.wrapMode = TextureWrapMode.Repeat;
            _gen[key] = Keep(t);
            return t;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// Radial "bubble" (a sphere seen from outside): faint mottled interior, fresnel-bright rim, a few light
        /// bubbles like SR2's slime icons. Colour is sRGB, alpha straight.
        /// </summary>
        public static Texture2D BubbleTex(string key, Color inner, Color rim, float a0, float a1, float pow, int dots, int seed)
        {
            var rng = new System.Random(seed);
            var spots = new List<Vector3>();
            for (int i = 0; i < dots; i++)
            {
                float ang = (float)(rng.NextDouble() * Math.PI * 2), rr = 0.35f + 0.5f * (float)rng.NextDouble();
                spots.Add(new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr, 0.025f + 0.05f * (float)rng.NextDouble()));
            }
            const int N = 256;
            return Generated(key, N, N, (x, y) =>
            {
                float u = (x + 0.5f) / (N * 0.5f) - 1f, v = (y + 0.5f) / (N * 0.5f) - 1f;
                float rho = Mathf.Sqrt(u * u + v * v);
                if (rho >= 1f) return new Color(rim.r, rim.g, rim.b, 0f);
                float f = 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - rho * rho));
                float n = ValueNoise(u * 4f + 7f, v * 4f + 3f, seed) * 0.65f + ValueNoise(u * 9f, v * 9f, seed + 1) * 0.35f;
                float a = a0 * (0.6f + 0.8f * n) + a1 * Mathf.Pow(f, pow);
                var col = Color.Lerp(inner, rim, Mathf.Pow(f, 0.7f));
                foreach (var sp in spots)
                {
                    float dd = Mathf.Sqrt((u - sp.x) * (u - sp.x) + (v - sp.y) * (v - sp.y));
                    if (dd < sp.z) { float k = Mathf.Clamp01((sp.z - dd) / (sp.z * 0.35f)); col = Color.Lerp(col, rim, k); a = Mathf.Max(a, 0.5f * k); }
                }
                a *= Mathf.Clamp01((1f - rho) / 0.015f);
                return new Color(col.r, col.g, col.b, Mathf.Clamp01(a));
            }, false);
        }

        /// <summary>Soft-edged white square (glitch pixels).</summary>
        public static Texture2D Square => Generated("square", 32, 32, (x, y) =>
        {
            float d = Mathf.Max(Mathf.Abs(x - 15.5f), Mathf.Abs(y - 15.5f));
            float a = d < 11f ? 1f : Mathf.Clamp01((14f - d) / 3f);
            return new Color(a, a, a, a);
        }, false);

        /// <summary>
        /// Quantum swirl marks for the multi-colour body shader: a few Archimedean spirals ("@" marks) in the red
        /// channel, tileable (wrapped distances), anti-aliased.
        /// </summary>
        public static Texture2D SwirlMask
        {
            get
            {
                const int N = 512;
                var rng = new System.Random(7177);
                var sp = new List<Vector4>();   // x, y, radius, b (arm spacing = 2*pi*b)
                for (int i = 0; i < 7; i++)
                {
                    float r = 34f + 30f * (float)rng.NextDouble();
                    sp.Add(new Vector4((float)rng.NextDouble() * N, (float)rng.NextDouble() * N, r, r / (2f * Mathf.PI * 2.3f)));   // ~2.3 turns
                }
                return Generated("swirlmask", N, N, (x, y) =>
                {
                    float best = 0f;
                    foreach (var c in sp)
                    {
                        float dx = x - c.x, dy = y - c.y;
                        if (dx > N / 2) dx -= N; else if (dx < -N / 2) dx += N;
                        if (dy > N / 2) dy -= N; else if (dy < -N / 2) dy += N;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r > c.z + 4f) continue;
                        float th = Mathf.Atan2(dy, dx);
                        float ph = (r / c.w - th) / (2f * Mathf.PI);       // arms where ph is an integer
                        float f = ph - Mathf.Floor(ph);
                        float d = Mathf.Min(f, 1f - f) * c.w * 2f * Mathf.PI;  // pixels to the nearest arm
                        float half = c.w * 1.1f;                                   // line = ~35% of the arm spacing
                        float a = Mathf.Clamp01((half - d) / 1.5f + 0.5f);
                        a *= Mathf.Clamp01((c.z - r) / 4f) * Mathf.Clamp01(r / 3f);
                        if (a > best) best = a;
                    }
                    return new Color(best, 0f, 0f, 1f);
                }, true);
            }
        }

        /// <summary>Hologram scanlines (white, alpha = line strength) with one brighter sweeping band.</summary>
        public static Texture2D ScanlineTex => Generated("scanlines", 4, 256, (x, y) =>
        {
            float a = 0.10f;
            if (y % 12 < 3) a = 0.75f;
            float band = Mathf.Abs(y - 64f);
            if (band < 22f) a = Mathf.Max(a, 0.55f * (1f - band / 22f) + 0.2f);
            return new Color(1f, 1f, 1f, a);
        }, false);

        /// <summary>Random pixel-cell colour mask for the multi-colour body shader (R/G/B cells, black = base colour).</summary>
        public static Texture2D PixelMask(int seed, int cells, float pr, float pg, float pb)
        {
            var rng = new System.Random(seed);
            var grid = new Color[cells * cells];
            for (int i = 0; i < grid.Length; i++)
            {
                double v = rng.NextDouble();
                grid[i] = v < pr ? new Color(1f, 0f, 0f, 1f) : v < pr + pg ? new Color(0f, 1f, 0f, 1f) : v < pr + pg + pb ? new Color(0f, 0f, 1f, 1f) : new Color(0f, 0f, 0f, 1f);
            }
            const int size = 256;
            int cell = size / cells;
            return Generated($"pixmask{seed}_{cells}", size, size, (x, y) => grid[(y / cell) * cells + (x / cell)], true, FilterMode.Point);
        }
    }

    /// <summary>
    /// Draws a sphere as a camera-facing disc: the quad sits on the silhouette plane of a sphere centred on the
    /// parent (radius = half of the parent's world scale) and is sized to its silhouette, so a radial "bubble"
    /// texture reads as a fresnel sphere from every side. Hidden while the camera is inside the sphere.
    /// </summary>
    public class SphereBillboard : MonoBehaviour
    {
        public SphereBillboard(IntPtr ptr) : base(ptr) { }
        public float Spin = 6f;
        float _angle;
        Renderer _r;

        void LateUpdate()
        {
            var cam = Billboard.Cam;
            var parent = transform.parent;
            if (cam == null || parent == null) return;
            if (_r == null) _r = GetComponent<Renderer>();
            var c = parent.position;
            float R = 0.5f * Mathf.Abs(parent.lossyScale.x);
            var to = cam.transform.position - c;
            float d = to.magnitude;
            bool inside = d < R * 1.03f || R < 1e-3f;
            if (_r != null && _r.enabled == inside) _r.enabled = !inside;
            if (inside) return;
            var dir = to / d;
            float off = R * R / d;
            float rs = R * Mathf.Sqrt(Mathf.Max(0f, 1f - (R / d) * (R / d)));
            _angle += Spin * Time.deltaTime;
            transform.position = c + dir * off;
            transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, _angle);
            float ps = Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.x));
            transform.localScale = Vector3.one * (2f * rs / ps);
        }
    }

    /// <summary>Keeps a quad facing the main camera.</summary>
    public class Billboard : MonoBehaviour
    {
        public Billboard(IntPtr ptr) : base(ptr) { }
        static Camera _cam;
        static int _camFrame = -1;
        public float Spin;
        float _angle;

        public static Camera Cam
        {
            get
            {
                if (_camFrame != Time.frameCount) { _camFrame = Time.frameCount; if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main; }
                return _cam;
            }
        }

        void LateUpdate()
        {
            var c = Cam;
            if (c == null) return;
            _angle += Spin * Time.deltaTime;
            transform.rotation = c.transform.rotation * Quaternion.Euler(0f, 0f, _angle);
        }
    }
}
