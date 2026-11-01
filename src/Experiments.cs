using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// Look experiments on the live slimes of one type (dev commands): swap the body material, add an elastic shell
    /// (inflated copy of the body mesh on the same bones; SR2 squashes it too when its shader has SR2's squash/wobble)
    /// and the inner 3D spiral of the quantum slime.
    /// </summary>
    public static class Experiments
    {
        public static List<GameObject> AllOf(string def)
        {
            var list = new List<GameObject>();
            foreach (var id in Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = id != null ? id.identType : null;
                if (t != null && string.Equals(t.name, def, StringComparison.OrdinalIgnoreCase)) list.Add(id.gameObject);
            }
            return list;
        }

        static readonly HashSet<IntPtr> Swapped = new();

        /// <summary>Body renderer of a slime: first material = the type's body material (Visuals.Mats[def]) or a swapped one.</summary>
        public static SkinnedMeshRenderer Body(GameObject go, string def)
        {
            Visuals.Mats.TryGetValue(def, out var orig);
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == null || r.gameObject.name.StartsWith("DevShell")) continue;
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (Swapped.Contains(r.Pointer) || (orig != null && (m == orig || m.name.StartsWith(orig.name)))) return r;
            }
            return null;
        }

        /// <summary>"=key" = copy of one of our materials, "key" = that material itself, else a copy of a game material.</summary>
        static Material Get(string tpl, string key)
        {
            Material m = null;
            if (tpl.StartsWith("="))
            {
                if (Visuals.Mats.TryGetValue(tpl.Substring(1), out var src) && src != null) m = new Material(src) { name = "ClassicSlimes_" + key };
            }
            else if (Visuals.Mats.TryGetValue(tpl, out var have) && have != null) return have;
            else m = Mat.Clone(key, tpl);
            if (m == null) return null;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Visuals.Mats[key] = m;
            return m;
        }

        /// <summary>bodymat: replace (or restore with "off") the body material of the live slimes of a type.</summary>
        public static string BodyMat(string def, string tpl, string key)
        {
            Material bm = null;
            if (tpl != "off") { bm = Get(tpl, key); if (bm == null) return "no material " + tpl; }
            Visuals.Mats.TryGetValue(def, out var orig);
            int n = 0;
            foreach (var go in AllOf(def))
            {
                var r = Body(go, def);
                if (r == null) continue;
                var mats = r.sharedMaterials;
                mats[0] = bm ?? orig;
                r.sharedMaterials = mats;
                if (bm != null) Swapped.Add(r.Pointer); else Swapped.Remove(r.Pointer);
                n++;
            }
            return $"{n} renderers -> {(bm ?? orig)?.shader?.name}";
        }

        static readonly Dictionary<string, Mesh> Shells = new();

        /// <summary>The body mesh pushed out along its normals; facets &gt; 0 snaps the normals to that many directions (gem facets).</summary>
        public static Mesh Inflated(Mesh src, float d, int facets)
        {
            string k = src.name + "|" + d + "|" + facets;
            if (Shells.TryGetValue(k, out var have) && have != null) return have;
            var m = Object.Instantiate(src);
            m.name = src.name + "_shell";
            Vector3[] v = m.vertices, n = m.normals;
            if (n != null && n.Length == v.Length)
            {
                for (int i = 0; i < v.Length; i++) v[i] = v[i] + n[i] * d;
                m.vertices = v;
                if (facets > 0) m.normals = Facet(n, facets);
                m.RecalculateBounds();
            }
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Shells[k] = m;
            return m;
        }

        public static Vector3[] Facet(Vector3[] n, int k)
        {
            var dirs = new Vector3[k];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < k; i++)
            {
                float y = 1f - (i + 0.5f) / k * 2f, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = ga * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            var o = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++)
            {
                float best = -2f; var bd = n[i];
                foreach (var d in dirs) { float dot = Vector3.Dot(n[i], d); if (dot > best) { best = dot; bd = d; } }
                o[i] = Vector3.Slerp(n[i], bd, 0.85f).normalized;
            }
            return o;
        }

        /// <summary>bshell: extra skinned shell around the live slimes of a type ("off" removes it).</summary>
        public static string Shell(string def, string tpl, string key, float inflate, int rq, int facets)
        {
            Material sm = null;
            if (tpl != "off")
            {
                sm = Get(tpl, key);
                if (sm == null) return "no material " + tpl;
                if (rq > 0) sm.renderQueue = rq;
            }
            int n = 0, sq = 0;
            foreach (var go in AllOf(def))
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "DevShell") Object.Destroy(t.gameObject);
                if (sm == null) continue;
                var body = Body(go, def);
                if (body == null || body.sharedMesh == null) continue;
                var sh = new GameObject("DevShell");
                sh.layer = body.gameObject.layer;
                sh.transform.SetParent(body.transform.parent, false);
                sh.transform.localPosition = body.transform.localPosition;
                sh.transform.localRotation = body.transform.localRotation;
                sh.transform.localScale = body.transform.localScale;
                var smr = sh.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = Inflated(body.sharedMesh, inflate, facets);
                smr.rootBone = body.rootBone;
                smr.bones = body.bones;
                smr.sharedMaterials = new Il2CppReferenceArray<Material>(new[] { sm });
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.localBounds = body.localBounds;
                smr.updateWhenOffscreen = true;
                try { var mpb = new MaterialPropertyBlock(); body.GetPropertyBlock(mpb); smr.SetPropertyBlock(mpb); } catch { }
                var sw = go.GetComponentInChildren<SquashWobbleViaShader>();
                if (sw != null)
                {
                    try
                    {
                        if (sw.renderers != null && !sw.renderers.Contains(smr)) sw.renderers.Add(smr);
                        sw.SetGlobalArrayIndex(sw.slimeGlobalArrayIndex);
                        sq++;
                    }
                    catch (Exception e) { ClassicSlimesMod.Log.Warning("bshell squash: " + e.Message); }
                }
                n++;
            }
            return $"{n} shells ({sm?.shader?.name}), {sq} squash-registered";
        }

        /// <summary>qspiral: the inner 3D spiral tube of the live quantum slimes ("off" removes it).</summary>
        public static string QSpiral(string tpl, string key, int rq)
        {
            Material m = null;
            if (tpl != "off" && tpl != "default")
            {
                m = Get(tpl, key);
                if (m == null) return "no material " + tpl;
                if (rq > 0) m.renderQueue = rq;
            }
            Spiral3D.Dirty();
            int n = 0;
            string info = "";
            foreach (var q in Object.FindObjectsOfType<QuantumSlime>())
            {
                if (tpl == "off") { q.RemoveSpiral(); continue; }
                if (q.RebuildSpiral(m)) { n++; info = q.SpiralInfo(); }
            }
            var sm = m != null ? m : Visuals.QuantumSpiralMat;
            return $"{n} spirals ({(sm != null ? sm.shader.name : "-")}) {Spiral3D.Info} | {info}";
        }
    }

    /// <summary>
    /// SR1 quantum: a light-yellow spiral band INSIDE the translucent yellow jelly, winding ~1.5 turns outwards around the
    /// face. A tube along an Archimedean spiral in the slime's local space (face = +Z), on a dome that follows the front.
    /// </summary>
    public static class Spiral3D
    {
        // in body heights around the spiral centre (QuantumSlime places / scales it from the body bounds)
        public static float R0 = 0.03f, R1 = 0.30f, Turns = 1.75f, Tube = 0.03f, Z = 0f, Y = 0f, Cup = 0f, StartDeg = 100f;
        /// <summary>Radius of the dome the coils lie on (0: flat disc bent back by Cup).</summary>
        public static float Dome = 0.34f;
        static Mesh _mesh;
        public static Mesh Mesh => _mesh != null ? _mesh : (_mesh = Build());
        public static void Dirty() => _mesh = null;
        public static bool Clockwise = true;
        public static string Info => $"r {R0}-{R1} turns {Turns} tube {Tube} z {Z} y {Y} cup {Cup} start {StartDeg} cw {Clockwise} dome {Dome}";
        static Vector3 DomeCentre => new(0f, Y, Z - Dome);

        static Vector3 P(float t)
        {
            float ang = (StartDeg + (Clockwise ? -1f : 1f) * t * Turns * 360f) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(R0, R1, t);
            // dome: the coils lie on a sphere bulging towards the face (still a spiral when seen from the side)
            float z = Dome > 0f ? Z + Mathf.Sqrt(Mathf.Max(0f, Dome * Dome - r * r)) - Dome : Z - Cup * r * r;
            return new Vector3(Mathf.Cos(ang) * r, Y + Mathf.Sin(ang) * r, z);
        }

        public static Mesh Build()
        {
            int seg = Mathf.Max(24, (int)(Turns * 110f)), ring = 12;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                var p = P(t);
                var tan = (P(Mathf.Min(1f, t + 0.002f)) - P(Mathf.Max(0f, t - 0.002f))).normalized;
                var side = Vector3.Cross(Dome > 0f ? (p - DomeCentre).normalized : Vector3.forward, tan);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.up;
                side.Normalize();
                var bin = Vector3.Cross(tan, side).normalized;
                // rounded ends, the band widens a little outwards
                float e = Mathf.Clamp01(t / 0.07f) * Mathf.Clamp01((1f - t) / 0.1f);
                float rad = Tube * Mathf.Lerp(0.6f, 1f, t) * Mathf.Sqrt(e);
                for (int j = 0; j <= ring; j++)
                {
                    float a = j / (float)ring * Mathf.PI * 2f;
                    var d = side * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    v.Add(p + d * rad); nr.Add(d); uv.Add(new Vector2(t, j / (float)ring));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < ring; j++)
                {
                    int a0 = i * (ring + 1) + j, b0 = a0 + ring + 1;
                    tr.Add(a0); tr.Add(a0 + 1); tr.Add(b0);
                    tr.Add(a0 + 1); tr.Add(b0 + 1); tr.Add(b0);
                }
            var m = new Mesh { name = "ClassicSlimes_QuantumSpiral" };
            var col = new Color[v.Count];
            for (int i = 0; i < col.Length; i++) col[i] = Color.white;
            m.vertices = v.ToArray(); m.normals = nr.ToArray(); m.uv = uv.ToArray(); m.colors = col; m.triangles = tr.ToArray();
            m.RecalculateBounds();
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return m;
        }
    }
}
