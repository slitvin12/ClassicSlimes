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

        public static ParticleSystem.MinMaxCurve Range(float a, float b)
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

        /// <summary>SR1 glitch trail: a camera-facing world-space ribbon (TrailRenderer) behind the slime.</summary>
        public static TrailRenderer Ribbon(string name, Transform parent, Vector3 localPos, Material mat, float time, float w0, float w1, Color c0, Color c1)
        {
            if (parent == null || mat == null) return null;
            try
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                var tr = go.AddComponent<TrailRenderer>();
                tr.sharedMaterial = mat;
                tr.time = time;
                tr.startWidth = w0; tr.endWidth = w1;
                tr.startColor = c0; tr.endColor = c1;
                tr.minVertexDistance = 0.06f;
                tr.numCapVertices = 3;
                tr.alignment = LineAlignment.View;
                tr.textureMode = LineTextureMode.Tile;
                tr.textureScale = new Vector2(1.4f, 1f);
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.autodestruct = false;
                tr.Clear();
                tr.emitting = true;
                return tr;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"Ribbon {name}: {e.Message}"); return null; }
        }

        public static void SetEmitting(TrailRenderer tr, bool on)
        {
            if (tr == null || tr.emitting == on) return;
            if (on) tr.Clear();   // no streak from where it stopped (vacpack, teleport)
            tr.emitting = on;
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

        /// <summary>Value noise on a lattice that wraps every <paramref name="period"/> cells (tileable).</summary>
        static float TileNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// Tileable wavy normal map for glass refraction (SR1 RadAura heat haze): fBm value-noise heights turned into
        /// tangent-space normals (rgb = n * 0.5 + 0.5, alpha 1, linear).
        /// </summary>
        public static Texture2D NoiseNormal(string key, int n, int freq, int octaves, float amp, int seed)
        {
            if (_gen.TryGetValue(key, out var have) && have != null) return have;
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0f, a = 1f, norm = 0f; int f = freq;
                    for (int o = 0; o < octaves; o++)
                    {
                        v += a * TileNoise(x * f / (float)n, y * f / (float)n, f, seed + o * 31);
                        norm += a; a *= 0.5f; f *= 2;
                    }
                    h[y * n + x] = v / norm;
                }
            return Generated(key, n, n, (x, y) =>
            {
                float dx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                float dy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                var nv = new Vector3(-dx * amp, -dy * amp, 1f).normalized;
                return new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }, true);
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
        /// <summary>Placement of the quantum swirl on the body UVs (tunable live with the "texst" dev command).</summary>
        public static Vector2 SwirlScale = new Vector2(1f, 1f), SwirlOffset = new Vector2(0f, 0.25f);   // centre of the spiral a bit above the mouth

        /// <summary>
        /// SR1 quantum body: ONE big light swirl (~2.6 turns, the arm thins out towards its outer end, bright dot in
        /// the centre) on the golden body, red channel. One swirl per texture tile.
        /// </summary>
        public static Texture2D SwirlMask
        {
            get
            {
                const int N = 512;
                const float R = 165f, Turns = 2.6f;
                float b = R / (2f * Mathf.PI * Turns);
                return Generated("swirlmask1", N, N, (x, y) =>
                {
                    float dx = x - N / 2f, dy = y - N / 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r < R + 2f)
                    {
                        float th = Mathf.Atan2(dy, dx);
                        float ph = (r / b - th) / (2f * Mathf.PI);       // arms where ph is an integer
                        float f = ph - Mathf.Floor(ph);
                        float d = Mathf.Min(f, 1f - f) * b * 2f * Mathf.PI;  // pixels to the nearest arm
                        float half = b * Mathf.PI * (0.42f - 0.18f * Mathf.Clamp01(r / R));
                        a = Mathf.Clamp01((half - d) / 1.5f + 0.5f) * Mathf.Clamp01((R - r) / 10f) * Mathf.Clamp01(r / 4f);
                    }
                    a = Mathf.Max(a, Mathf.Clamp01((b * 1.2f - r) / 2f + 0.5f));
                    return new Color(a, 0f, 0f, 1f);
                }, true);
            }
        }

        public static Texture2D ScanlineTex => Generated("scanlines", 4, 256, (x, y) =>
        {
            float a = 0.10f;
            if (y % 12 < 3) a = 0.75f;
            float band = Mathf.Abs(y - 64f);
            if (band < 22f) a = Mathf.Max(a, 0.55f * (1f - band / 22f) + 0.2f);
            return new Color(1f, 1f, 1f, a);
        }, false);

        /// <summary>Random pixel-cell colour mask for the multi-colour body shader (R/G/B cells, black = base colour).</summary>
        /// <summary>
        /// Glitch trail strip (u = along the ribbon, v = across it): 8x8 pixel cells per tile with random strength and
        /// gaps that grow towards the ribbon edges -> ragged pixel ray. SR2's particle shader uses only the alpha
        /// (the colour comes from the trail's vertex colours).
        /// </summary>
        public static Texture2D GlitchStrip(int seed) => Generated("glitchstrip" + seed, 32, 32, (x, y) =>
        {
            int cx = x / 4, cy = y / 4;
            float edge = Mathf.Abs((cy + 0.5f) / 8f - 0.5f) * 2f;
            if (Hash(cx, cy, seed + 101) > 0.92f - edge * 0.65f) return new Color(1f, 1f, 1f, 0f);
            return new Color(1f, 1f, 1f, 0.45f + 0.55f * Hash(cx, cy, seed + 7));
        }, false, FilterMode.Point);

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

        /// <summary>
        /// A torn pixel mask for glitch bursts: a PixelMask-style grid with a few horizontal bands slid sideways, blanked
        /// to one cell colour, or broken into thin scan lines (SR1's glitch shader tore the body into shifted rows).
        /// </summary>
        public static Texture2D TearMask(int seed, int cells, float pr, float pg, float pb)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var grid = new Color[cells * cells];
            for (int i = 0; i < grid.Length; i++)
            {
                double v = rng.NextDouble();
                grid[i] = v < pr ? new Color(1f, 0f, 0f, 1f) : v < pr + pg ? new Color(0f, 1f, 0f, 1f) : v < pr + pg + pb ? new Color(0f, 0f, 1f, 1f) : new Color(0f, 0f, 0f, 1f);
            }
            const int size = 256;
            int cell = size / cells;
            var kind = new int[size]; var shift = new int[size]; var fill = new Color[size];
            int bands = 3 + rng.Next(3);
            for (int b = 0; b < bands; b++)
            {
                int y0 = rng.Next(size), h = cell / 2 + rng.Next(cell * 2);
                int k = rng.Next(5);   // 0-2 slide sideways, 3 solid stripe, 4 scan lines
                int sh = (rng.Next(2) == 0 ? -1 : 1) * (cell / 2 + rng.Next(cell * 4));
                var col = rng.Next(3) == 0 ? new Color(0f, 1f, 0f, 1f) : new Color(0f, 0f, 1f, 1f);
                for (int y = y0; y < y0 + h && y < size; y++) { kind[y] = k < 3 ? 1 : k - 1; shift[y] = sh; fill[y] = col; }
            }
            return Generated($"tearmask{seed}_{cells}", size, size, (x, y) =>
            {
                int k = kind[y];
                if (k == 2 || (k == 3 && (y & 3) < 2)) return fill[y];
                int xx = k == 1 ? ((x + shift[y]) % size + size) % size : x;
                return grid[(y / cell) * cells + (xx / cell)];
            }, true, FilterMode.Point);
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
