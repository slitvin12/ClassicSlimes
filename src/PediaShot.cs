using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// Dev: slimepedia pictures from the live game models. <c>pediashot ident [yaw] [tilt] [fill] [name]</c> spawns the actor
    /// in front of the camera, freezes the game, hides the HUD / vacpack and grabs the frame over a black, a white and again
    /// a black opaque backdrop (UserData/ClassicSlimes/pedia/name_k.png, _w.png, _k2.png + name.txt with the screen rect).
    /// tools/pediamatte.py turns them into a transparent picture (difference matting; glass refracts the plain backdrop, so
    /// see-through bodies keep their look).
    /// </summary>
    public static class PediaShot
    {
        public static bool Busy;
        static void L(string m) => ClassicSlimesMod.Log.Msg("[pediashot] " + m);

        sealed class State
        {
            public GameObject Go, Backdrop;
            public Material Mat;
            public readonly List<Canvas> Canvases = new();
            public readonly List<Renderer> Hidden = new();
            public float TimeScale = 1f;
            public bool Frozen;
        }

        public static void Start(string ident, float yaw, float tilt, float fill, string name)
        {
            if (Busy) { L("busy"); return; }
            IdentifiableType t = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                if (x != null && string.Equals(x.name, ident, StringComparison.OrdinalIgnoreCase)) { t = x; break; }
            if (t == null || t.prefab == null) { L("unknown ident " + ident); return; }
            MelonCoroutines.Start(Run(t, yaw, tilt, fill, string.IsNullOrEmpty(name) ? t.name : name));
        }

        static Bounds Measure(GameObject go)
        {
            bool any = false; var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.TryCast<ParticleSystemRenderer>() != null) continue;
                var rb = r.bounds;
                if (rb.size.magnitude > 6f) continue;   // auras / domes
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        static Material BackdropMat()
        {
            Material src = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null || m.shader.name != "HDRP/Unlit") continue;
                if (m.renderQueue >= 2450 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || m.IsKeywordEnabled("_ALPHATEST_ON")) continue;
                src = m; break;
            }
            Material mat = null;
            if (src != null) mat = new Material(src);
            else { var sh = Shader.Find("HDRP/Unlit"); if (sh != null) mat = new Material(sh); }
            if (mat == null) return null;
            mat.name = "ClassicSlimes_PediaBackdrop";
            if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.black);
            L($"backdrop material from {(src != null ? src.name : "Shader.Find")} rq={mat.renderQueue}");
            return mat;
        }

        static void SetBackdrop(State st, Color c)
        {
            if (st.Mat == null) return;
            st.Mat.SetColor("_UnlitColor", c);
            if (st.Mat.HasProperty("_Color")) st.Mat.SetColor("_Color", c);
        }

        static IEnumerator Run(IdentifiableType t, float yaw, float tilt, float fill, string name)
        {
            Busy = true;
            var st = new State();
            string dir = Path.Combine(Diag.Dir, "pedia");
            Directory.CreateDirectory(dir);
            try
            {
                var cam = Camera.main;
                if (cam == null) { L("no camera"); yield break; }
                var ct = cam.transform;
                var fwd = ct.forward; var up = ct.up; var right = ct.right;
                var flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                flat.Normalize();
                st.Go = Foods.SpawnActor(t, ct.position + fwd * 3f, Quaternion.LookRotation(-flat, Vector3.up));
                if (st.Go == null) { L("spawn failed " + t.name); yield break; }
                for (int f = 0; f < 20; f++) yield return null;
                if (st.Go == null) { L("actor gone"); yield break; }
                foreach (var rb in st.Go.GetComponentsInChildren<Rigidbody>()) { if (!rb.isKinematic) rb.velocity = Vector3.zero; rb.isKinematic = true; }
                var rot = Quaternion.AngleAxis(tilt, right) * Quaternion.LookRotation(-flat, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
                st.Go.transform.rotation = rot;
                float halfV = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float dist = 3f;
                for (int pass = 0; pass < 3; pass++)
                {
                    yield return null;
                    var b = Measure(st.Go);
                    float size = Mathf.Max(b.size.y, b.size.x, 0.05f);
                    dist = Mathf.Max(0.45f, size / (2f * Mathf.Tan(halfV) * Mathf.Clamp(fill, 0.1f, 0.95f)));
                    var target = ct.position + fwd * dist;
                    st.Go.transform.position += target - b.center;
                    st.Go.transform.rotation = rot;
                }
                var bb = Measure(st.Go);
                // opaque backdrop well behind the subject (shadows off, nothing else in the frame)
                st.Mat = BackdropMat();
                if (st.Mat == null) { L("no backdrop material"); yield break; }
                st.Backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
                st.Backdrop.name = "ClassicSlimes_PediaBackdrop";
                var col = st.Backdrop.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
                float bdDist = dist + bb.extents.magnitude * 2f + 1.5f;
                st.Backdrop.transform.position = ct.position + fwd * bdDist;
                st.Backdrop.transform.rotation = Quaternion.LookRotation(fwd, up);
                float h = 2f * bdDist * Mathf.Tan(halfV) * 1.5f;
                st.Backdrop.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                var br = st.Backdrop.GetComponent<MeshRenderer>();
                br.sharedMaterial = st.Mat;
                br.shadowCastingMode = ShadowCastingMode.Off;
                br.receiveShadows = false;
                // HUD + vacpack off
                foreach (var c in Object.FindObjectsOfType<Canvas>()) if (c != null && c.enabled) { c.enabled = false; st.Canvases.Add(c); }
                foreach (var r in ct.root.GetComponentsInChildren<Renderer>())
                    if (r != null && r.enabled && (r.transform.position - ct.position).sqrMagnitude < 4f) { r.enabled = false; st.Hidden.Add(r); }
                st.TimeScale = Time.timeScale; Time.timeScale = 0f; st.Frozen = true;
                var passes = new[] { ("k", new Color(0f, 0f, 0f, 1f)), ("w", new Color(1f, 1f, 1f, 1f)), ("k2", new Color(0f, 0f, 0f, 1f)) };
                foreach (var (suffix, c) in passes)
                {
                    SetBackdrop(st, c);
                    for (int f = 0; f < 24; f++) yield return null;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + "_" + suffix + ".png"), 1);
                    for (int f = 0; f < 4; f++) yield return null;
                }
                // subject rect on screen (top-left origin)
                bb = Measure(st.Go);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var sp = cam.WorldToScreenPoint(corner);
                    x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                    y0 = Mathf.Min(y0, Screen.height - sp.y); y1 = Mathf.Max(y1, Screen.height - sp.y);
                }
                var inv = CultureInfo.InvariantCulture;
                File.WriteAllText(Path.Combine(dir, name + ".txt"), string.Format(inv,
                    "screen {0} {1}\nrect {2:0} {3:0} {4:0} {5:0}\nident {6}\ndist {7:0.00} fov {8:0.0} yaw {9} tilt {10}\n",
                    Screen.width, Screen.height, x0, y0, x1, y1, t.name, dist, cam.fieldOfView, yaw, tilt));
                L($"{name}: {t.name} at {dist:0.00} m, rect ({x0:0},{y0:0})-({x1:0},{y1:0}) of {Screen.width}x{Screen.height}");
            }
            finally
            {
                try
                {
                    if (st.Frozen) Time.timeScale = st.TimeScale <= 0f ? 1f : st.TimeScale;
                    foreach (var c in st.Canvases) if (c != null) c.enabled = true;
                    foreach (var r in st.Hidden) if (r != null) r.enabled = true;
                    if (st.Backdrop != null) Object.Destroy(st.Backdrop);
                    if (st.Go != null) Destroyer.DestroyActor(st.Go, "ClassicSlimes.PediaShot", true);
                }
                catch (Exception e) { L("restore: " + e.Message); }
                Busy = false;
            }
        }

        /// <summary>pedialist: every pedia entry (type, identifiable, plort or not) -> pedialist.txt.</summary>
        public static string List()
        {
            var sb = new StringBuilder();
            int n = 0, plorts = 0;
            foreach (var e in Resources.FindObjectsOfTypeAll<PediaEntry>())
            {
                if (e == null) continue;
                n++;
                var ie = e.TryCast<IdentifiablePediaEntry>();
                var id = ie != null ? ie._identifiableType : null;
                if (id != null && id.IsPlort) plorts++;
                string title = "";
                try { title = e.Title != null ? e.Title.GetLocalizedString() : ""; } catch { }
                sb.AppendLine($"{e.GetIl2CppType().Name} {e.name} id={(id != null ? id.name : "-")} plort={(id != null && id.IsPlort)} title='{title}' fullArt={e.RequiresFullArt}");
            }
            File.WriteAllText(Path.Combine(Diag.Dir, "pedialist.txt"), sb.ToString());
            return $"pedialist: {n} entries, {plorts} for plorts -> pedialist.txt";
        }
    }
}
