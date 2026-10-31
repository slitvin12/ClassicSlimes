using System;
using System.Collections.Generic;
using CottonLibrary;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Damage;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// GC safety. Il2CppInterop 1.5 stores elements of Il2CppReferenceArray / Il2CppStringArray with a raw pointer
    /// write (no IL2CPP GC write barrier), while SR2 runs Unity's incremental GC (boot.config gc-max-time-slice=3).
    /// A freshly created object stored that way during an in-progress incremental cycle can be freed, leaving a
    /// dangling pointer: garbage string lengths ("Length cannot be less than zero", OutOfMemoryException in
    /// Cotton's save setup) and finally an access violation inside coreclr. Cotton does a lot of such writes while
    /// generating largos (MergeStructures, AddString, ...), and so does our own appearance setup.
    /// Workaround: finish the current GC cycle, keep the GC disabled while we/Cotton build the definitions, then
    /// re-enable it (the next cycle starts from scratch and sees every reference). Runtime array writes use
    /// <see cref="Array{T}"/>, which stores through il2cpp_gc_wbarrier_set_field.
    /// </summary>
    public static class Gc
    {
        static int _depth;
        static float _since;

        public static void Pause(string why)
        {
            if (!Cfg.SafeGc.Value || _depth++ > 0) return;
            try
            {
                // UnityEngine.Scripting.GarbageCollector.GCMode is stripped in SR2, so use the IL2CPP API directly.
                IL2CPP.il2cpp_gc_collect(0);      // finish the running incremental cycle
                IL2CPP.il2cpp_gc_disable();
                _since = Time.realtimeSinceStartup;
                ClassicSlimesMod.Log.Msg($"[gc] paused (incremental GC: {Incremental()}): {why}");
            }
            catch (Exception e) { _depth = 0; ClassicSlimesMod.Log.Warning("[gc] pause failed: " + e.Message); }
        }

        public static void Resume(string why)
        {
            if (_depth == 0 || --_depth > 0) return;
            try
            {
                IL2CPP.il2cpp_gc_enable();
                ClassicSlimesMod.Log.Msg($"[gc] resumed after {(Time.realtimeSinceStartup - _since) * 1000f:0} ms: {why}");
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("[gc] resume failed: " + e.Message); }
        }

        [System.Runtime.InteropServices.DllImport("GameAssembly", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
        static extern bool il2cpp_gc_is_incremental();

        public static string Incremental()
        {
            try { return il2cpp_gc_is_incremental() ? "yes" : "no"; } catch { return "?"; }
        }

        public static void Window(string why, Action a)
        {
            Pause(why);
            try { a(); }
            finally { Resume(why); }
        }

        /// <summary>Builds an IL2CPP reference array storing every element through the GC write barrier.</summary>
        public static Il2CppReferenceArray<T> Array<T>(IList<T> items) where T : Il2CppObjectBase
        {
            var arr = new Il2CppReferenceArray<T>(items.Count);
            IntPtr start = IntPtr.Add(arr.Pointer, 4 * IntPtr.Size);   // Il2CppObject (2 ptr) + bounds + max_length
            for (int i = 0; i < items.Count; i++)
                IL2CPP.il2cpp_gc_wbarrier_set_field(arr.Pointer, IntPtr.Add(start, i * IntPtr.Size), items[i] == null ? IntPtr.Zero : items[i].Pointer);
            return arr;
        }
    }

    /// <summary>Material lookup / cloning helpers. SR1 shaders do not exist in SR2 (HDRP), so every look is rebuilt from SR2 materials.</summary>
    public static class Mat
    {
        static Dictionary<string, Material> _cache;

        public static Material Find(string name)
        {
            if (_cache == null || !_cache.TryGetValue(name, out var m) || m == null) Index();
            return _cache.TryGetValue(name, out var r) && r != null ? r : null;
        }

        static bool Healthy(Shader sh) => sh != null && sh.isSupported && !sh.name.StartsWith("Hidden/InternalError");

        static void Index()
        {
            _cache = new Dictionary<string, Material>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.name.StartsWith("ClassicSlimes")) continue;
                // prefer a copy whose shader is alive (asset bundles may hold dead duplicates)
                if (!_cache.TryGetValue(m.name, out var have) || (!Healthy(have.shader) && Healthy(m.shader))) _cache[m.name] = m;
            }
        }

        // ---------------------------------------------------------------- keeper
        // Our materials are cloned from SR2 assets that can live in Addressables bundles. When such a bundle is
        // unloaded (scene change) the shader / texture objects it owned are destroyed and every clone that
        // references them renders as the magenta error shader. The keeper snapshots each clone while it is healthy
        // and re-binds a live shader / texture with the same name when the original dies.
        sealed class Snap { public string Shader; public string[] Kw; public readonly Dictionary<string, string> Tex = new(); }
        static readonly List<Material> _made = new();
        static readonly HashSet<IntPtr> _madeSet = new();
        static readonly Dictionary<IntPtr, Snap> _snaps = new();
        static readonly Dictionary<IntPtr, string> _names = new();
        public static int Destroyed;
        static float _nextCheck;
        public static int Revived;

        public static Material Track(Material m)
        {
            if (m != null && _madeSet.Add(m.Pointer)) { _made.Add(m); _names[m.Pointer] = m.name; }
            return m;
        }

        public static IEnumerable<Material> Made => _made;
        public static bool IsMade(Material m) => m != null && _madeSet.Contains(m.Pointer);

        static Snap Snapshot(Material m)
        {
            var s = new Snap { Shader = m.shader.name };
            var kw = m.shaderKeywords;
            if (kw != null) { s.Kw = new string[kw.Length]; for (int i = 0; i < kw.Length; i++) s.Kw[i] = kw[i]; }
            try
            {
                foreach (var p in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(p);
                    if (t != null && !string.IsNullOrEmpty(t.name)) s.Tex[p] = t.name;
                }
            }
            catch { }
            return s;
        }

        static Dictionary<string, Shader> LiveShaders()
        {
            var d = new Dictionary<string, Shader>();
            foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
                if (Healthy(sh) && !d.ContainsKey(sh.name)) d[sh.name] = sh;
            return d;
        }

        static Dictionary<string, Texture> LiveTextures()
        {
            var d = new Dictionary<string, Texture>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
                if (t != null && !string.IsNullOrEmpty(t.name) && !d.ContainsKey(t.name)) d[t.name] = t;
            return d;
        }

        /// <summary>Called every frame; checks all tracked materials every 2 s.</summary>
        public static void Tick(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;
            Dictionary<string, Shader> shaders = null;
            Dictionary<string, Texture> texes = null;
            for (int i = _made.Count - 1; i >= 0; i--)
            {
                var m = _made[i];
                if (m == null)
                {
                    _made.RemoveAt(i); Destroyed++;
                    _names.TryGetValue(m.Pointer, out var dn);
                    ClassicSlimesMod.Log.Warning($"[mat] tracked material destroyed by the game: {dn} (t={Time.time:F1})");
                    continue;
                }
                _snaps.TryGetValue(m.Pointer, out var s);
                var sh = m.shader;
                if (Healthy(sh))
                {
                    if (s == null) { _snaps[m.Pointer] = Snapshot(m); continue; }
                    if (s.Shader != sh.name) { _snaps[m.Pointer] = Snapshot(m); continue; } // changed on purpose
                    foreach (var kv in s.Tex)
                    {
                        if (m.GetTexture(kv.Key) != null) continue;
                        texes ??= LiveTextures();
                        if (texes.TryGetValue(kv.Value, out var nt)) { m.SetTexture(kv.Key, nt); Revived++; ClassicSlimesMod.Log.Msg($"[mat] re-bound texture {kv.Value} on {m.name}.{kv.Key}"); }
                    }
                    continue;
                }
                if (s == null) continue;
                shaders ??= LiveShaders();
                if (shaders.TryGetValue(s.Shader, out var ns))
                {
                    m.shader = ns;
                    if (s.Kw != null) m.shaderKeywords = s.Kw;
                    Revived++;
                    ClassicSlimesMod.Log.Msg($"[mat] re-bound shader {s.Shader} on {m.name}");
                }
            }
            try { Faces.Tick(); } catch (Exception e) { ClassicSlimesMod.Log.Warning("[faces] tick: " + e.Message); }
        }

        public static Material Clone(string newName, params string[] templates)
        {
            foreach (var t in templates)
            {
                var src = Find(t);
                if (src == null) continue;
                var m = new Material(src);
                m.name = "ClassicSlimes_" + newName;
                m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return Track(m);
            }
            ClassicSlimesMod.Log.Warning($"No template material for {newName} ({string.Join(",", templates)})");
            return null;
        }

        /// <summary>Copy of the first loaded material that uses the given shader.</summary>
        public static Material CloneByShader(string newName, string shaderName)
        {
            foreach (var src in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (src == null || src.name.StartsWith("ClassicSlimes")) continue;
                var sh = src.shader;
                if (!Healthy(sh) || sh.name != shaderName) continue;
                var m = new Material(src) { name = "ClassicSlimes_" + newName, hideFlags = HideFlags.DontUnloadUnusedAsset };
                return Track(m);
            }
            ClassicSlimesMod.Log.Warning($"No material with shader {shaderName} for {newName}");
            return null;
        }

        public static void Col(this Material m, string p, Color c) { if (m != null && m.HasProperty(p)) m.SetColor(p, c); }
        public static void Flt(this Material m, string p, float v) { if (m != null && m.HasProperty(p)) m.SetFloat(p, v); }
        public static void Tx(this Material m, string p, Texture t) { if (m != null && t != null && m.HasProperty(p)) m.SetTexture(p, t); }
        public static void Kw(this Material m, string kw, bool on) { if (m == null) return; if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }
        public static void Palette(this Material m, Color top, Color mid, Color bot) { m.Col("_TopColor", top); m.Col("_MiddleColor", mid); m.Col("_BottomColor", bot); }
        public static Color Mul(this Color c, float f) => new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
        public static Color Lerp(this Color a, Color b, float t) => Color.Lerp(a, b, t);
        public static Color HDR(this Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        /// <summary>Small horizontal colour ramp; replaces the rainbow lookup textures of SR2's radiant/aura shaders.</summary>
        public static Texture2D Ramp(string name, params Color[] stops)
        {
            const int W = 64;
            var t = new Texture2D(W, 4, TextureFormat.RGBA32, false) { name = "ClassicSlimes_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            for (int x = 0; x < W; x++)
            {
                float f = x / (float)(W - 1) * (stops.Length - 1);
                int i = Mathf.Min((int)f, stops.Length - 2);
                var c = Color.Lerp(stops[i], stops[i + 1], f - i);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, c);
            }
            t.Apply(false, true);
            return t;
        }

        /// <summary>Removes the rainbow from radiant-style FX materials by giving them a single-hue ramp.</summary>
        public static void Tint(this Material m, string name, params Color[] stops)
        {
            if (m == null) return;
            var r = Ramp(name, stops);
            m.Tx("_RainbowAura", r);
            m.Tx("_RadiantGradient", r);
            m.Flt("_RainbowSaturation", 0f);
        }
    }

    /// <summary>SR2 particle effect prefabs by name.</summary>
    public static class Fx
    {
        static Dictionary<string, GameObject> _fx;

        public static GameObject Get(string name)
        {
            if (_fx == null || !_fx.TryGetValue(name, out var g) || g == null) Index();
            return _fx.TryGetValue(name, out var r) && r != null ? r : null;
        }

        static readonly Dictionary<string, GameObject> _kept = new();
        static GameObject _keepRoot;

        /// <summary>A private, inactive copy of an FX prefab that survives the unloading of the bundle it came from.</summary>
        public static GameObject Kept(string name)
        {
            if (_kept.TryGetValue(name, out var k) && k != null) return k;
            var src = Get(name);
            if (src == null) return null;
            if (_keepRoot == null)
            {
                _keepRoot = new GameObject("ClassicSlimesFxKeep");
                _keepRoot.SetActive(false);
                Object.DontDestroyOnLoad(_keepRoot);
                _keepRoot.hideFlags = HideFlags.HideAndDontSave;
            }
            k = Object.Instantiate(src, _keepRoot.transform);
            k.name = name;
            _kept[name] = k;
            return k;
        }

        static void Index()
        {
            _fx = new Dictionary<string, GameObject>();
            foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
            {
                if (ps == null) continue;
                var go = ps.gameObject;
                if (go.scene.IsValid()) continue; // assets only, not live scene objects
                var root = go.transform.root.gameObject;
                if (!_fx.ContainsKey(root.name)) _fx[root.name] = root;
                if (!_fx.ContainsKey(go.name)) _fx[go.name] = go;
            }
        }

        public static GameObject Play(string name, Vector3 pos, float scale = 1f)
        {
            var p = Get(name);
            if (p == null) return null;
            try
            {
                var go = FXHelpers.SpawnAndPlayFX(p, pos, Quaternion.identity);
                if (go != null && Math.Abs(scale - 1f) > 0.01f) go.transform.localScale = Vector3.one * scale;
                return go;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"FX {name}: {e.Message}"); return null; }
        }

        public static GameObject Attach(string name, GameObject parent, Vector3 localPos)
        {
            var p = Get(name);
            if (p == null || parent == null) return null;
            try
            {
                var go = FXHelpers.SpawnAndPlayFX(p, parent);
                if (go != null) go.transform.localPosition = localPos;
                return go;
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning($"FX {name}: {e.Message}"); return null; }
        }

        public static void Stop(GameObject fx)
        {
            if (fx == null) return;
            try { FXHelpers.RecycleAndStopFX(fx); } catch { Object.Destroy(fx); }
        }
    }

    /// <summary>SR1 sounds played through SR2's SECTR audio system (so the game's volume sliders apply).</summary>
    public static class Snd
    {
        static readonly Dictionary<string, SECTR_AudioCue> _cues = new();
        static SECTR_AudioCue _tpl;

        static SECTR_AudioCue Template()
        {
            if (_tpl != null) return _tpl;
            try { _tpl = Library.GetSlime("Pink")?.Sounds?._voiceFunClipDefinition?._audioCue; } catch { }
            if (_tpl == null)
                foreach (var c in Resources.FindObjectsOfTypeAll<SECTR_AudioCue>())
                    if (c != null && c.AudioClips != null && c.AudioClips.Count > 0 && !c.Loops) { _tpl = c; break; }
            return _tpl;
        }

        public static SECTR_AudioCue Cue(string key, bool loop, float volume, float maxDistance, params string[] clips)
        {
            if (_cues.TryGetValue(key, out var c) && c != null) return c;
            var tpl = Template();
            if (tpl == null) return null;
            var list = new Il2CppSystem.Collections.Generic.List<SECTR_AudioCue.ClipData>();
            foreach (var n in clips) { var clip = Res.Clip(n); if (clip != null) list.Add(new SECTR_AudioCue.ClipData(clip)); }
            if (list.Count == 0) return null;
            c = Object.Instantiate(tpl);
            c.name = "ClassicSlimes_" + key;
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try { c.Template = null; } catch { }
            c.AudioClips = list;
            c.Loops = loop;
            c.Volume = new Vector2(volume, volume);
            c.Pitch = loop ? new Vector2(1f, 1f) : new Vector2(0.94f, 1.06f);
            if (maxDistance > 0f) c.MaxDistance = maxDistance;
            _cues[key] = c;
            return c;
        }

        public static void Play(SECTR_AudioCue cue, Vector3 pos)
        {
            if (cue == null) return;
            try { SECTR_AudioSystem.Play(cue, pos, false); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Play: " + e.Message); }
        }

        /// <summary>Looping positional source parented to a GameObject.</summary>
        public static SECTR_PointSource Loop(GameObject host, SECTR_AudioCue cue, string name = "ClassicSlimesLoop")
        {
            if (cue == null || host == null) return null;
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(host.transform, false);
            var src = go.AddComponent<SECTR_PointSource>();
            src.Cue = cue;
            src.Loop = true;
            go.SetActive(true);
            return src;
        }

        public static void SetPlaying(SECTR_PointSource src, bool on)
        {
            if (src == null) return;
            try
            {
                if (on && !src.IsPlaying) src.Play();
                else if (!on && src.IsPlaying) src.Stop(false);
            }
            catch { }
        }

        // shared cues
        public static SECTR_AudioCue RadAura => Cue("radAura", true, 0.55f, 25f, "radslime_aura_03");
        public static SECTR_AudioCue QuantumWarp => Cue("quantumWarp", false, 0.9f, 40f, "quantumslime_warp_01", "quantumslime_warp_05");
        public static SECTR_AudioCue QuantumGhost => Cue("quantumGhost", false, 0.6f, 25f, "quantumslime_idle_07");
        public static SECTR_AudioCue QuantumGhostPop => Cue("quantumGhostPop", false, 0.6f, 25f, "quantumslime_despawn_03b");
        public static SECTR_AudioCue MosaicAura => Cue("mosaicAura", true, 0.45f, 20f, "slime_mosaic_aura_03");
        public static SECTR_AudioCue GlintSpawn => Cue("glintSpawn", false, 0.7f, 30f, "slime_mosaic_glint_spawn_01");
        public static SECTR_AudioCue GlintBlast => Cue("glintBlast", false, 1f, 40f, "slime_mosaic_glint_blast_03");
        public static SECTR_AudioCue QsAura => Cue("qsAura", true, 0.6f, 25f, "quicksilver_energy_aura_01");
        public static SECTR_AudioCue QsHit => Cue("qsHit", false, 1f, 30f, "quicksilver_hit_success_01", "quicksilver_hit_success_02");
        public static SECTR_AudioCue QsFizzle => Cue("qsFizzle", false, 0.8f, 25f, "quicksilver_hit_fail_01");
        public static SECTR_AudioCue GlitchSplit => Cue("glitchSplit", false, 0.9f, 30f, "slime_glitch_split_01");
    }

    public static class Dmg
    {
        static readonly Dictionary<string, DamageSourceDefinition> _src = new();

        public static DamageSourceDefinition Source(params string[] names)
        {
            foreach (var n in names)
            {
                if (_src.TryGetValue(n, out var d) && d != null) return d;
                foreach (var s in Resources.FindObjectsOfTypeAll<DamageSourceDefinition>())
                    if (s != null && s.name == n) { _src[n] = s; return s; }
            }
            return null;
        }

        public static bool Player(int amount, GameObject from, params string[] sources)
        {
            if (amount <= 0) return false;
            var p = U.Player;
            if (p == null) return false;
            var d = p.GetComponent<PlayerDamageable>() ?? p.GetComponentInChildren<PlayerDamageable>();
            if (d == null) return false;
            try
            {
                var dmg = new Damage { Amount = amount, DamageSource = Source(sources), SourceObject = from };
                return d.Damage(dmg);
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Damage: " + e.Message); return false; }
        }
    }

    public static class U
    {
        // ignore triggers, player (8), slimes/actors (15), eat triggers (6), tarr-edible (7), ignore-raycast (2)
        public static readonly int GroundMask = Physics.DefaultRaycastLayers & ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 15));
        public const int SlimeMask = 1 << 15;
        public static readonly System.Random Rng = new();

        public static GameObject Player => SceneContext.Instance != null ? SceneContext.Instance.Player : null;

        public static float Rand(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public static float Agitation(SlimeEmotions e)
        {
            try { return e != null ? e.Get(SlimeEmotions.Emotion.AGITATION) : 0f; } catch { return 0f; }
        }

        public static bool IsDay()
        {
            try { var h = SceneContext.Instance.TimeDirector.CurrHour(); return h >= 6f && h < 18.5f; } catch { return true; }
        }

        public static bool Grounded(Transform t, float scale)
        {
            return Physics.Raycast(t.position + Vector3.up * (0.35f * scale), Vector3.down, 0.75f * scale, GroundMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Random reachable point on the ground around a position (used for quantum ghosts).</summary>
        public static bool GroundPoint(Vector3 around, float minR, float maxR, out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                float ang = Rand(0f, Mathf.PI * 2f), r = Rand(minR, maxR);
                var probe = around + new Vector3(Mathf.Cos(ang) * r, 2.5f, Mathf.Sin(ang) * r);
                if (Physics.Linecast(around + Vector3.up * 0.6f, probe, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 7f, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;
                point = hit.point;
                return true;
            }
            point = around;
            return false;
        }

        public static float DistToPlayer(Vector3 pos)
        {
            var p = Player;
            if (p == null) return float.MaxValue;
            return Vector3.Distance(p.transform.position + Vector3.up * 0.9f, pos);
        }
    }
}
