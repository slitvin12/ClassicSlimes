using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    public static class Behaviours
    {
        public static void Register()
        {
            ClassInjector.RegisterTypeInIl2Cpp<Billboard>();
            ClassInjector.RegisterTypeInIl2Cpp<SphereBillboard>();
            ClassInjector.RegisterTypeInIl2Cpp<RadSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<QuantumGhost>();
            ClassInjector.RegisterTypeInIl2Cpp<QuantumSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<MosaicGlint>();
            ClassInjector.RegisterTypeInIl2Cpp<MosaicSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<QuicksilverSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<GlitchSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<PhaseLemonTree>();
        }

        // Behaviours are NOT put on prefabs: Cotton clones/merges prefab components for every largo
        // (MergeComponents + Il2Cpp reflection), and the coreclr crashes seen during largo generation made
        // keeping injected MonoBehaviours out of that path the safer choice. They are attached to each
        // spawned actor instead (SlimeEat.Awake postfix below).
        static readonly Dictionary<IntPtr, Action<GameObject>> _byDef = new();

        public static void Add<T>(SlimeDefinition def) where T : Component
        {
            if (def == null) return;
            Action<GameObject> add = go => { if (go.GetComponent<T>() == null) go.AddComponent<T>(); };
            _byDef[def.Pointer] = _byDef.TryGetValue(def.Pointer, out var prev) ? prev + add : add;
        }

        public static int Mapped => _byDef.Count;

        internal static void OnSlimeAwake(SlimeEat eat)
        {
            if (_byDef.Count == 0 || eat == null) return;
            var d = eat.SlimeDefinition;
            if (d != null && _byDef.TryGetValue(d.Pointer, out var add)) add(eat.gameObject);
        }

        internal static void AttachToLive()
        {
            if (_byDef.Count == 0) return;
            foreach (var e in Object.FindObjectsOfType<SlimeEat>(true))
                if (e != null) try { OnSlimeAwake(e); } catch { }
        }

        internal static void ClearSession()
        {
            RadSlime.ClearSession();
            QuantumSlime.ClearSession();
            MosaicSlime.ClearSession();
            QuicksilverSlime.ClearSession();
            GlitchSlime.ClearSession();
        }
    }

    [HarmonyPatch(typeof(SlimeEat), nameof(SlimeEat.Awake))]
    static class SlimeEatAwakePatch
    {
        static void Postfix(SlimeEat __instance)
        {
            try { Behaviours.OnSlimeAwake(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Attach behaviours: " + e.Message); }
        }
    }

    // =====================================================================================================
    /// <summary>SR1 RadSource + RadSlimeExpand: radiation aura that periodically expands (x1.5) and collapses when sprayed with water.</summary>
    public class RadSlime : MonoBehaviour
    {
        public RadSlime(IntPtr ptr) : base(ptr) { }

        // SR1 RadAura: transform scale 3.2 on a sphere of RADIUS 1; our sphere mesh has diameter 1 -> 6.4 (3.0 on 4-6x gordo)
        const float AuraScale = 6.4f, GordoAuraScale = 3.0f;
        const float CoreRatio = 0.62f;
        public static readonly List<RadSlime> All = new();
        public bool NoExpand;

        IntPtr _absorberPlayerPtr;
        float BaseAuraScale => _isGordo ? GordoAuraScale : AuraScale;

        internal static void ClearSession()
        {
            All.Clear();
            _radSpeaker = null;
            _radSpeakerFrame = -1;
        }

        void OnDisable()
        {
            Snd.SetPlaying(_loop, false);
            Gfx.SetEmitting(_motes, false);
            if (_aura != null) _aura.SetActive(false);
            if (_core != null) _core.SetActive(false);
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (_radSpeaker == this) _radSpeaker = null;
        }

        static int _radSpeakerFrame = -1;
        static RadSlime _radSpeaker;
        static bool IsAudioLeader(RadSlime me)
        {
            if (Time.frameCount != _radSpeakerFrame)
            {
                _radSpeakerFrame = Time.frameCount;
                _radSpeaker = null;
                float best = float.MaxValue;
                var pl = U.Player;
                if (pl != null)
                {
                    var pp = pl.transform.position;
                    for (int i = All.Count - 1; i >= 0; i--)
                    {
                        var r = All[i];
                        if (r == null || !r.isActiveAndEnabled || r._factor <= 0.02f || (r._vac != null && r._vac.IsHeld())) continue;
                        var ge = r.GetComponent<GordoEat>();
                        if (ge != null && ge.GetEatenCount() == GordoEat.ALREADY_BURST_FLAG) continue;
                        float maxRange = r._isGordo ? 36f : (r._isLargo ? 28f : 24f);
                        float d2 = (r.transform.position - pp).sqrMagnitude;
                        if (d2 < maxRange * maxRange && d2 < best) { best = d2; _radSpeaker = r; }
                    }
                }
            }
            return _radSpeaker == me;
        }

        /// <summary>Dev: switch the dome look of every live rad slime.</summary>
        public static void RestyleAll()
        {
            foreach (var r in All)
            {
                if (r == null) continue;
                r.BuildAuraVisuals();
            }
        }

        /// <summary>SR1 heat haze: a boiling refraction volume just inside the glass dome (only with the refracting dome).</summary>
        void MakeHaze()
        {
            if (_aura == null || _isGordo || Visuals.DomeIsBillboard(Visuals.DomeStyle) || !Cfg.RadHaze.Value) return;
            var m = Visuals.RadHazeMat;
            if (m != null) Visuals.Sphere("RadHaze", _aura.transform, Vector3.zero, 0.97f, m);
        }
        /// <summary>Dev: an extra sphere (clone of any material, key "RadDist") inside every live aura, e.g. a distortion test.</summary>
        public static void DevExtra(string template, float rel)
        {
            Material m = null;
            if (template != "off")
            {
                m = Mat.Clone("RadDist", template);
                if (m == null) return;
                m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Visuals.Mats["RadDist"] = m;
            }
            foreach (var r in All)
            {
                if (r == null || r._aura == null) continue;
                var old = r._aura.transform.Find("RadDist");
                if (old != null) Destroy(old.gameObject);
                if (m != null) Visuals.Sphere("RadDist", r._aura.transform, Vector3.zero, rel, m);
            }
        }

        const float ExpandFactor = 1.5f, ExpandingTime = 3f, ExpandedTime = 10f, CalmingTime = 0.2f;

        GameObject _aura, _core;
        ParticleSystem _motes;
        SECTR_PointSource _loop;
        SlimeEmotions _emo;
        SlimeAppearanceApplicator _app;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        PlayerRadAbsorber _absorber;
        bool _isLargo, _isGordo, _radiantDome;
        float _factor = 1f, _nextExpand, _expandedUntil, _fallbackTick, _phase;

        bool WantsRadiantDome()
        {
            if (_isGordo || _app == null || _app.Appearance == null) return false;
            var app = _app.Appearance;
            if (!_isLargo) return RadiantLooks.IsRadiantAppearance(app);
            var n = app.name ?? "";
            return n.Contains("RadRadiant", StringComparison.Ordinal);
        }

        void BuildAuraVisuals()
        {
            if (_aura != null) Destroy(_aura);
            if (_core != null) Destroy(_core);
            float scale = BaseAuraScale;
            if (_isGordo)
            {
                _aura = Visuals.MakeGordoDome(transform, new Vector3(0f, 0.5f, 0f), scale, _radiantDome);
                _core = null;
            }
            else
            {
                _aura = Visuals.MakeDome(transform, new Vector3(0f, 0.5f, 0f), scale, -1, _isLargo, _radiantDome);
                _core = MakeCore();
                MakeHaze();
            }
            Color c0 = _radiantDome ? new Color(0.45f, 0.96f, 1.00f, 1f) : new Color(0.75f, 1f, 0.40f, 1f);
            Color c1 = _radiantDome ? new Color(0.32f, 0.42f, 1.00f, 1f) : new Color(0.25f, 1f, 0.30f, 1f);
            var moteMat = _radiantDome ? Visuals.RadMoteRadiantMat : Visuals.RadMoteMat;
            if (_motes == null)
            {
                float rate = _isGordo ? 22f : (_isLargo ? 5f : 14f);
                float size0 = _isGordo ? 0.10f : 0.07f;
                float size1 = _isGordo ? 0.22f : 0.16f;
                float radius = _isGordo ? scale * 0.42f : 2.3f;
                int max = _isGordo ? 56 : (_isLargo ? 18 : 40);
                _motes = Gfx.Sparkles("RadMotes", transform, new Vector3(0f, 0.45f, 0f), moteMat, c0, c1, rate, 2.2f, size0, size1, radius, max, _isGordo ? 0.22f : 0.16f);
                if (_motes != null)
                {
                    try
                    {
                        var mm = _motes.main;
                        mm.gravityModifierMultiplier = -0.05f;
                        if (_isGordo) mm.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                    catch { }
                }
            }
            else
            {
                try
                {
                    var mm = _motes.main;
                    mm.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
                    var r = _motes.GetComponent<ParticleSystemRenderer>();
                    if (r != null && moteMat != null) r.sharedMaterial = moteMat;
                }
                catch { }
            }
        }

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _app = GetComponent<SlimeAppearanceApplicator>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _isGordo = GetComponent<GordoEat>() != null || GetComponent<GordoIdentifiable>() != null;
            try
            {
                var eat = GetComponent<SlimeEat>();
                _isLargo = !_isGordo && ((eat != null && eat.SlimeDefinition != null && eat.SlimeDefinition.IsLargo) || transform.lossyScale.x > 1.35f);
            }
            catch { }
            _radiantDome = WantsRadiantDome();
            BuildAuraVisuals();
            _phase = U.Rand(0f, 10f);
            All.Add(this);
            _loop = Snd.Loop(gameObject, Snd.RadAura);
            _nextExpand = Time.time + Delay();
        }

        static int _rippleFrame = -1;
        /// <summary>The refraction ripples of the shared dome material crawl over the glass all the time (SR1 heat haze).</summary>
        static void ScrollRipples()
        {
            if (_rippleFrame == Time.frameCount) return;
            _rippleFrame = Time.frameCount;
            if (Visuals.DomeStyle != 0) return;
            float t = Time.time;
            var ofs = new Vector2(t * Visuals.RippleSpeed.x, t * Visuals.RippleSpeed.y);
            var m = Visuals.RadDomeMat;
            if (m != null && m.HasProperty("_NormalMap")) m.SetTextureOffset("_NormalMap", ofs);
            var lm = Visuals.LargoDome0();
            if (lm != null && lm != m && lm.HasProperty("_NormalMap")) lm.SetTextureOffset("_NormalMap", ofs);
            Visuals.ScrollExtraDomeNormals(ofs);
        }

        /// <summary>Dev / tests: start the SR1 expansion (x1.5 for 3 + 10 s) now.</summary>
        public void ForceExpand() { _nextExpand = 0f; _expandedUntil = 0f; }

        float Delay() => Mathf.Lerp(30f, 180f, 1f - U.Agitation(_emo));

        /// <summary>SR1 inner glow sphere: only with the SR2-style bubble looks (the SR1 refracting aura is one sphere).</summary>
        GameObject MakeCore() => Visuals.DomeHasCore(Visuals.DomeStyle)
            ? Visuals.Sphere("RadCore", transform, new Vector3(0f, 0.5f, 0f), AuraScale * CoreRatio, Visuals.RadCoreMat)
            : null;

        GordoEat _gordo;
        bool _gordoChecked;

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_gordoChecked) { _gordoChecked = true; _gordo = GetComponent<GordoEat>(); }
            if (!_isGordo)
            {
                bool wantRad = WantsRadiantDome();
                if (wantRad != _radiantDome) { _radiantDome = wantRad; BuildAuraVisuals(); }
            }
            bool burstedGordo = _gordo != null && _gordo.GetEatenCount() == GordoEat.ALREADY_BURST_FLAG;
            bool calmed = burstedGordo || (_calm != null && _calm.IsCalmed());
            bool held = _vac != null && _vac.IsHeld();
            if (calmed) _nextExpand += dt;
            else if (!NoExpand && Time.time >= _nextExpand && Time.time >= _expandedUntil)
            {
                _expandedUntil = Time.time + ExpandingTime + ExpandedTime;
                _nextExpand = _expandedUntil + Delay();
                if (_motes != null && !held) { try { _motes.Emit(14); } catch { } }   // puff of radiation as it swells
            }
            float target = burstedGordo ? 0f : calmed ? 0f : (Time.time < _expandedUntil ? ExpandFactor : 1f);
            float rate = (target < 1f || _factor < 1f) ? 1f / CalmingTime : (ExpandFactor - 1f) / ExpandingTime;
            _factor = burstedGordo ? 0f : Mathf.MoveTowards(_factor, target, rate * dt);

            bool show = !burstedGordo && !held && _factor > 0.02f;
            float baseScale = BaseAuraScale;
            // slow "breathing" of the aura (per slime phase), on top of the SR1 expansion
            float pulse = 1f + 0.035f * Mathf.Sin((Time.time + _phase) * 2.4f);
            ScrollRipples();
            if (_aura != null)
            {
                if (_aura.activeSelf != show) _aura.SetActive(show);
                if (show)
                {
                    if (!_isGordo && Visuals.DomeIsBillboard(Visuals.DomeStyle))
                    {
                        _aura.transform.localScale = Vector3.one * (baseScale * _factor * pulse);
                    }
                    else
                    {
                        // jelly wobble: the three axes swell and shrink out of step, so the dome never sits still
                        float w = Time.time + _phase * 3f;
                        var jelly = new Vector3(1f + 0.05f * Mathf.Sin(w * 2.1f), 1f + 0.055f * Mathf.Sin(w * 2.7f + 1.1f), 1f + 0.05f * Mathf.Sin(w * 1.75f + 2.3f));
                        _aura.transform.localScale = jelly * (baseScale * _factor * pulse);
                        // slow spin + sway: the refraction ripples of the glass dome crawl like SR1's heat haze
                        float t = Time.time + _phase * 7f;
                        _aura.transform.localRotation = Quaternion.Euler(14f * Mathf.Sin(t * 0.37f), t * Visuals.DomeSpin, 10f * Mathf.Sin(t * 0.29f + 1.3f));
                    }
                }
            }
            if (_core != null)
            {
                if (_core.activeSelf != show) _core.SetActive(show);
                if (show) _core.transform.localScale = Vector3.one * (baseScale * CoreRatio * Mathf.Max(_factor, 0.4f) * pulse);
            }
            Gfx.SetEmitting(_motes, show);
            Snd.SetPlaying(_loop, show && IsAudioLeader(this));

            if (dt <= 0f || !show || !Cfg.RadAuraDamagesPlayer.Value) return;
            // BaseAuraScale is the DIAMETER of the (unit-diameter) aura sphere: irradiate only inside what is drawn
            float radius = 0.5f * baseScale * _factor * transform.lossyScale.x;
            var center = transform.position + Vector3.up * (0.5f * transform.lossyScale.y);
            if (U.DistToPlayer(center) > radius + 0.35f) return;
            var p = U.Player;
            if (p != null && p.Pointer != _absorberPlayerPtr)
            {
                _absorberPlayerPtr = p.Pointer;
                _absorber = p.GetComponent<PlayerRadAbsorber>() ?? p.GetComponentInChildren<PlayerRadAbsorber>();
            }
            RadHud.LastExposure = Time.time;
            if (_absorber != null) _absorber.Absorb(gameObject, Cfg.RadPerSecond.Value * dt);
            else
            {
                var ps = SceneContext.Instance != null ? SceneContext.Instance.PlayerState : null;
                if (ps != null)
                {
                    int over = ps.AddRads(Cfg.RadPerSecond.Value * dt);
                    if ((over > 0 || ps.GetCurrRad() >= 100) && Time.time > _fallbackTick)
                    {
                        _fallbackTick = Time.time + 2f;
                        Dmg.Player(Cfg.RadDamagePerTick.Value, gameObject, "RadAbsorb", "SlimeAttack");
                    }
                }
                else if (Time.time > _fallbackTick) { _fallbackTick = Time.time + 2f; Dmg.Player(Cfg.RadDamagePerTick.Value, gameObject, "RadAbsorb", "SlimeAttack"); }
            }
        }
    }

    // =====================================================================================================
    /// <summary>SR1 QuantumSlimeSuperposition: emits "ghosts" (other possible realities) and, when agitated, swaps reality with one of them.</summary>
    public class QuantumSlime : MonoBehaviour
    {
        public QuantumSlime(IntPtr ptr) : base(ptr) { }

        const int MaxGhosts = 5;
        readonly List<QuantumGhost> _ghosts = new();
        SlimeEmotions _emo;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        Rigidbody _rb;
        float _nextGhost, _nextTeleport, _born;
        // glass body: no vertex-shader wobble, so the whole slime squashes / stretches instead (body, face, spiral)
        readonly JellyRig _jelly = new();
        bool _jellyTried;
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] public JellyRig Jelly => _jelly;
        public string JellyInfo() => _jelly.Ok ? _jelly.Info() : $"jelly off (tried {_jellyTried})";

        bool _everBound;

        void OnEnable() { _born = Time.time; }

        void LateUpdate()
        {
            if (!_jellyTried)
            {
                if ((!_everBound && Time.time - _born < 0.3f) || !Cfg.QuantumJelly.Value) return;
                _jellyTried = true;
                _everBound = true;
                if (_spiral == null && Cfg.QuantumJelly.Value) try { MakeSpiral(null, false); } catch { }
                try { _jelly.Init(transform, _spiralBody != null ? _spiralBody : JellyBody()); }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("Quantum jelly: " + e.Message); }
                return;
            }
            if (!_jelly.Ok) return;
            try { _jelly.Tick(); }
            catch (Exception e) { _jelly.Fail(); ClassicSlimesMod.Log.Warning("Quantum jelly: " + e.Message); }
        }

        // largos keep their opaque wobbling body: no glass, no rig
        SkinnedMeshRenderer JellyBody()
        {
            var jelly = Visuals.QuantumJellyMade;
            if (jelly == null) return null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r != null && r.sharedMaterial == jelly) return r;
            return null;
        }

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _rb = GetComponent<Rigidbody>();
            _nextGhost = Time.time + U.Rand(3f, 8f);
            _nextTeleport = Time.time + TeleportDelay();
            try { MakeSpiral(null, false); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Quantum spiral: " + e.Message); }
        }

        float GhostDelay() => Mathf.Lerp(20f, 5f, U.Agitation(_emo));
        float TeleportDelay() => Mathf.Lerp(30f, 5f, U.Agitation(_emo));

        void Update()
        {
            if (_spiral != null && !_spiralPlaced) PlaceSpiral();
            if (_spiral != null && _spiralPlaced && SpiralSpin != 0f)
                _spiral.transform.localRotation = _spiralRot * Quaternion.Euler(0f, 0f, (Spiral3D.Clockwise ? 1f : -1f) * Time.time * SpiralSpin);
            try { UpdatePhase(); } catch (Exception e) { if (!_phaseErr) { _phaseErr = true; ClassicSlimesMod.Log.Warning("Quantum phase: " + e.Message); } }
            try { UpdateWalls(); } catch (Exception e) { if (!_wallErr) { _wallErr = true; ClassicSlimesMod.Log.Warning("Quantum walls: " + e.Message); } }
            _ghosts.RemoveAll(g => g == null);
            if (!Cfg.QuantumTeleports.Value) { if (_ghosts.Count > 0) ClearGhosts(false); return; }
            bool held = _vac != null && _vac.IsHeld();
            bool calmed = _calm != null && _calm.IsCalmed();
            if (held || calmed)
            {
                if (calmed && _ghosts.Count > 0) ClearGhosts(true);
                _nextTeleport = Mathf.Max(_nextTeleport, Time.time + 3f);
                return;
            }
            float s = transform.lossyScale.x;
            if (Time.time >= _nextGhost)
            {
                _nextGhost = Time.time + GhostDelay();
                // a ghost is another place the slime could be: while it is solid (not hungry) never beyond a corral wall
                if (_ghosts.Count < MaxGhosts && U.Grounded(transform, s) && U.GroundPoint(transform.position, 2f * s, 10f, out var p)
                    && (Phasing || !CrossesWall(transform.position, p + Vector3.up * (0.5f * s), s)))
                    SpawnGhost(p);
            }
            if (Time.time >= _nextTeleport)
            {
                _nextTeleport = Time.time + TeleportDelay();
                if ((!CoopCompat.IsCoop || CoopCompat.IsLocallySimulated(_rb)) && U.Agitation(_emo) > 0.1f && _ghosts.Count > 0)
                {
                    var target = ReachableGhost();
                    if (target != null) TeleportTo(target);
                }
            }
        }

        // ------------------------------------------------------------- hologram "phase" blink
        // SR1 quantum shader: now and then the slime drops out of phase - the body turns into a see-through hologram
        // (same shape, still animated: the body materials are swapped for a transparent scanline/static material with a
        // jumping glitch band) for a second or two, flickering back to solid for a frame now and then. Between phases it
        // has short glitch twitches. Fallback (no body material found): hide the renderers and show a scanline sphere.
        class Swap { public Renderer R; public int[] Idx; public Material[] Orig; }
        GameObject _holo;
        Material _holoMat;
        readonly List<Swap> _swapped = new();
        readonly List<Renderer> _hidden = new();
        readonly List<Renderer> _faces = new();   // eyes / mouth renderers: drop out now and then while see-through
        float _nextPhase, _phaseEnd, _nextFlick, _nextGlitch, _nextJump;
        bool _bodyOff, _fullPhase, _phaseErr, _wallErr;
        Vector2 _holoOfs;
        /// <summary>Dev (phase .. hold): no solid flicker-ins during a phase, so screenshots always catch the hologram.</summary>
        public static bool DevHold;
        /// <summary>Dev (qnophase): no hologram phases / glitch twitches at all.</summary>
        public static bool DevNoPhase;

        float PhaseDelay() => Mathf.Lerp(14f, 4f, U.Agitation(_emo)) * U.Rand(0.7f, 1.3f);

        internal static void ClearSession()
        {
            Walls.Clear();
            WallSet.Clear();
            _wallsAt = -999f;
        }

        Material HoloInstance()
        {
            if (_holoMat != null) return _holoMat;
            var src = Visuals.HoloMat;
            if (src == null) return null;
            _holoMat = new Material(src) { name = "QuantumHoloBody", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _holoMat.SetTextureScale("_UnlitColorMap", new Vector2(1f, 1.5f));
            return _holoMat;
        }

        void UpdatePhase()
        {
            float now = Time.time;
            bool held = _vac != null && _vac.IsHeld();
            if (DevNoPhase) { if (_phaseEnd > 0f) EndPhase(); return; }
            if (_phaseEnd > 0f)
            {
                if (now >= _phaseEnd || held || !Cfg.QuantumHologram.Value) { EndPhase(); return; }
                if (_fullPhase && !DevHold && now >= _nextFlick)
                {
                    // mostly hologram, short flicker-ins of the solid body (the delay belongs to the state being entered)
                    bool off = !_bodyOff;
                    _nextFlick = now + (off ? U.Rand(0.15f, 0.45f) : U.Rand(0.03f, 0.07f));
                    SetBodyOff(off);
                }
                if (now >= _nextJump)
                {
                    // glitch: the torn band skips up / down (it scrolls in between), the face drops out now and then
                    _nextJump = now + U.Rand(0.08f, 0.3f);
                    _holoOfs = new Vector2(U.Rand(0f, 1f), _holoOfs.y + U.Rand(-0.3f, 0.3f));
                    if (_bodyOff) SetFace(U.Rand(0f, 1f) < 0.55f);
                }
                var ofs = new Vector2(_holoOfs.x, _holoOfs.y - now * 0.6f);
                if (_holoMat != null) _holoMat.SetTextureOffset("_UnlitColorMap", ofs);
                if (_holo != null && _holoMat != null)
                {
                    foreach (var r in _holo.GetComponentsInChildren<Renderer>(true))
                        if (r != null && r.sharedMaterial != _holoMat) r.sharedMaterial = _holoMat;
                }
                return;
            }
            if (_nextPhase <= 0f) { _nextPhase = now + U.Rand(3f, 10f); _nextGlitch = now + U.Rand(2f, 5f); }
            if (held || !Cfg.QuantumHologram.Value) return;
            if (now >= _nextPhase) { StartPhase(U.Rand(1.2f, 2.2f)); return; }
            if (now >= _nextGlitch)
            {
                _nextGlitch = now + Mathf.Lerp(8f, 2.5f, U.Agitation(_emo)) * U.Rand(0.7f, 1.3f);
                if (_nextPhase - now > 1f) StartPhase(U.Rand(0.12f, 0.25f), false);
            }
        }

        public void StartPhase(float seconds) => StartPhase(seconds, true);

        void StartPhase(float seconds, bool full)
        {
            float now = Time.time;
            if (_phaseEnd > 0f) { _phaseEnd = Mathf.Max(_phaseEnd, now + seconds); _fullPhase |= full; return; }
            _phaseEnd = now + seconds;
            _fullPhase = full;
            _nextFlick = now + U.Rand(0.25f, 0.5f);
            _nextJump = 0f;
            if (full) _nextPhase = _phaseEnd + PhaseDelay();
            _nextGlitch = Mathf.Max(_nextGlitch, _phaseEnd + U.Rand(1.5f, 4f));
            _swapped.Clear(); _hidden.Clear(); _faces.Clear();
            var holo = HoloInstance();
            var all = GetComponentsInChildren<Renderer>();
            foreach (var r in all)
            {
                if (!Phaseable(r) || holo == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                var idx = new List<int>();
                var orig = new Material[mats.Length];
                bool face = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    orig[i] = m;
                    var sh = m != null ? m.shader : null;
                    if (sh == null) continue;
                    if (sh.name.Contains("Slime/Body") || m == Visuals.QuantumJellyMade) idx.Add(i);   // + the glass body
                    else if (sh.name.Contains("Slime/Face")) face = true;
                }
                if (idx.Count > 0) _swapped.Add(new Swap { R = r, Idx = idx.ToArray(), Orig = orig });
                else if (face || r.name == "MosaicElasticGlass" || r.name == "MosaicSlimeFacets") _faces.Add(r);
            }
            if (_swapped.Count == 0)
            {
                _faces.Clear();
                if (_holo == null) _holo = Visuals.MakeHolo(transform);
                if (_holo != null) _holo.SetActive(true);
                foreach (var r in all) if (Phaseable(r)) _hidden.Add(r);
            }
            SetBodyOff(true);
        }

        bool Phaseable(Renderer r)
        {
            if (r == null || !r.enabled || r.TryCast<ParticleSystemRenderer>() != null) return false;
            if (_holo != null && r.transform.IsChildOf(_holo.transform)) return false;
            if (r.GetComponentInParent<RadSlime>() != null && r.name.StartsWith("Rad")) return false;   // largo auras
            return true;
        }

        void SetBodyOff(bool off)
        {
            _bodyOff = off;
            foreach (var s in _swapped)
            {
                if (s.R == null) continue;
                var mats = s.R.sharedMaterials;
                foreach (var i in s.Idx)
                    if (i < mats.Length && (off || mats[i] == _holoMat))
                        mats[i] = off ? _holoMat : s.Orig[i];
                s.R.sharedMaterials = mats;
            }
            foreach (var r in _hidden) if (r != null) r.enabled = !off;
            if (_spiral != null) _spiral.SetActive(!off);   // the hologram has no spiral inside
            SetFace(!off);
        }

        void SetFace(bool on)
        {
            foreach (var r in _faces) if (r != null) r.enabled = on;
        }

        // ------------------------------------------------------------- SR1 spiral inside the jelly
        GameObject _spiral;
        SkinnedMeshRenderer _spiralBody;
        bool _spiralPlaced;
        /// <summary>Spiral centre from the body centre in body heights (x right, y up, z towards the face), size in body heights.</summary>
        public static Vector3 SpiralOfs = new(0f, 0.10f, 0.27f);   // around the mouth (SR2's face sits high)
        public static float SpiralSize = 1f;
        /// <summary>Slow spin of the spiral about its axis (degrees per second).</summary>
        public static float SpiralSpin = 15f;
        Quaternion _spiralRot = Quaternion.identity;

        bool MakeSpiral(Material mat, bool force)
        {
            if (!force && !Cfg.QuantumJelly.Value) return false;
            var jelly = Visuals.QuantumJellyMat();
            SkinnedMeshRenderer body = null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r != null ? r.sharedMaterial : null;
                if (m == null) continue;
                if ((jelly != null && m == jelly) || (force && m.shader != null && m.shader.name.Contains("Slime/Body"))) { body = r; break; }
            }
            if (body == null) return false;   // largos keep their opaque body: no spiral
            var sm = mat != null ? mat : Visuals.QuantumSpiralMat;
            if (sm == null) return false;
            RemoveSpiral();
            _spiral = Gfx.MeshObj("QSpiral", transform, Spiral3D.Mesh, sm, Vector3.zero, 1f);
            _spiralBody = body;
            _spiralPlaced = false;
            PlaceSpiral();
            if (_bodyOff) _spiral.SetActive(false);
            return true;
        }

        void PlaceSpiral()
        {
            if (_spiral == null || _spiralBody == null) return;
            var b = U.RootBounds(transform, _spiralBody);
            float h = b.size.y;
            if (h < 0.2f) return;
            // below the bone root, so the spiral follows the jelly (squash / stretch, lean) and the body animations
            var st = _spiral.transform;
            var parent = JellyRig.VisRoot(transform, _spiralBody) ?? transform;
            if (st.parent != parent) st.SetParent(parent, false);
            st.position = transform.TransformPoint(b.center + SpiralOfs * h);
            _spiralRot = Quaternion.Inverse(parent.rotation) * transform.rotation;
            st.localRotation = _spiralRot;
            float rel = Mathf.Max(1e-4f, parent.lossyScale.x / Mathf.Max(1e-4f, transform.lossyScale.x));
            st.localScale = Vector3.one * (h * SpiralSize / rel);
            _spiralPlaced = true;
        }

        public void RemoveSpiral() { if (_spiral != null) Object.Destroy(_spiral); _spiral = null; }
        public bool RebuildSpiral(Material mat) => MakeSpiral(mat, true);
        public void ReplaceSpiral() { _spiralPlaced = false; PlaceSpiral(); }
        public string SpiralInfo() => _spiral == null ? "no spiral" : $"spiral at {_spiral.transform.localPosition} scale {_spiral.transform.localScale.x:0.00} body {U.RootBounds(transform, _spiralBody).center} h {U.RootBounds(transform, _spiralBody).size.y:0.00}";

        void EndPhase()
        {
            _phaseEnd = 0f;
            SetBodyOff(false);
            _swapped.Clear(); _hidden.Clear(); _faces.Clear();
            if (_holo != null) _holo.SetActive(false);
        }

        // vacuumed / unloaded mid-phase: never leave the hologram material or a hidden face behind
        void OnDisable()
        {
            if (_phaseEnd > 0f) EndPhase();
            try { RestoreWalls(); } catch { }
            ClearGhosts(false);
            _jelly.Restore();
            _jellyTried = false;
        }

        // ------------------------------------------------------------- phasing through corral walls
        // Hungry (above the game's own cutoff SlimeEmotions.HUNGRY_CUTOFF: the hungry face) -> the slime is "elsewhere"
        // enough to drift through corral walls (collisions with every wall collider of every corral (WallUpgrader
        // standard + upgrade walls) are ignored). Fed again -> solid again at once; if it is still inside a wall it steps
        // back to the side it came from. While solid, its ghosts and teleports never cross a corral wall either.
        // Before 1.3.4 a fed slime swapped places with ghosts that had wandered through the walls, kept ignoring the
        // walls until it was out of the wall (on either side), and new / normally fed slimes (hunger 0.5) counted as hungry.
        static readonly List<Collider> Walls = new();
        static readonly HashSet<IntPtr> WallSet = new();
        static float _wallsAt = -999f;
        readonly List<Collider> _ignored = new();
        readonly HashSet<IntPtr> _ignoredSet = new();
        Collider[] _cols;
        float _nextWallCheck;
        Vector3 _lastClear;     // last spot where it was not inside a corral wall
        bool _hasClear;
        // corral walls around the slime with their bounds, cached: its ghosts test them every few frames
        readonly List<Collider> _nearWalls = new();
        readonly List<Vector3> _nearMin = new(), _nearMax = new();
        float _nearAt = -999f;
        Vector3 _nearPos;
        public bool Phasing => _ignored.Count > 0;

        static void RefreshWalls()
        {
            if (Walls.Count > 0 && Walls[0] == null) _wallsAt = -999f;
            if (Time.time - _wallsAt < 10f) return;
            _wallsAt = Time.time;
            Walls.Clear(); WallSet.Clear();
            foreach (var w in Object.FindObjectsOfType<WallUpgrader>())
            {
                if (w == null) continue;
                foreach (var root in new[] { w.StandardWalls, w.UpgradeWalls })
                {
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                        if (c != null && !c.isTrigger && c.enabled && c.gameObject.activeInHierarchy && WallSet.Add(c.Pointer)) Walls.Add(c);
                }
            }
        }

        public static int WallCount { get { RefreshWalls(); return Walls.Count; } }

        void RefreshNearWalls()
        {
            var pos = transform.position;
            if (Time.time - _nearAt < 2f && (pos - _nearPos).sqrMagnitude < 25f) return;
            _nearAt = Time.time; _nearPos = pos;
            RefreshWalls();
            _nearWalls.Clear(); _nearMin.Clear(); _nearMax.Clear();
            foreach (var w in Walls)
            {
                if (w == null) continue;
                var b = w.bounds;
                if (b.SqrDistance(pos) > 1600f) continue;   // 40 m: ghosts appear within 10 m and wander a few metres
                _nearWalls.Add(w); _nearMin.Add(b.min); _nearMax.Add(b.max);
            }
        }

        /// <summary>True when the straight way a -> b (both at body-root height) goes through a corral wall.</summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        public bool CrossesWall(Vector3 a, Vector3 b, float scale)
        {
            RefreshNearWalls();
            if (_nearWalls.Count == 0) return false;
            var mid = (a + b) * 0.5f;
            float reach = (b - a).magnitude * 0.5f + 1.5f * scale + 0.5f;
            // the root sits about half a body above the ground: one ray near the feet, one at body height
            var feet = Vector3.up * (-0.2f * scale);
            var body = Vector3.up * (0.4f * scale);
            for (int i = 0; i < _nearWalls.Count; i++)
            {
                if (U.SqrDistance(_nearMin[i], _nearMax[i], mid) > reach * reach) continue;
                var w = _nearWalls[i];
                if (w == null) continue;
                if (U.SegmentHits(w, a + feet, b + feet) || U.SegmentHits(w, a + body, b + body)) return true;
            }
            return false;
        }

        /// <summary>A ghost to swap places with. While solid, the ghosts beyond a corral wall collapse first; hungry
        /// (phasing) -> any ghost, as before.</summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        QuantumGhost ReachableGhost()
        {
            if (!Phasing) DropGhostsBeyondWalls();
            _ghosts.RemoveAll(g => g == null);
            return _ghosts.Count > 0 ? _ghosts[U.Rng.Next(_ghosts.Count)] : null;
        }

        /// <summary>Ghosts on the other side of a corral wall from a solid slime collapse (e.g. right after it ate).</summary>
        void DropGhostsBeyondWalls()
        {
            float s = transform.lossyScale.x;
            var from = transform.position;
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                if (g == null) { _ghosts.RemoveAt(i); continue; }
                if (!CrossesWall(from, g.transform.position + Vector3.up * (0.5f * s), s)) continue;
                _ghosts.RemoveAt(i);
                g.Pop(true);
            }
        }

        bool InsideIgnoredWall()
        {
            float r = 0.55f * transform.lossyScale.x;
            foreach (var c in Physics.OverlapSphere(transform.position, r, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && _ignoredSet.Contains(c.Pointer)) return true;
            return false;
        }

        void UpdateWalls()
        {
            if (Time.time < _nextWallCheck) return;
            _nextWallCheck = Time.time + 0.3f;
            bool held = _vac != null && _vac.IsHeld();
            bool want = Cfg.QuantumPhaseWalls.Value && !held && U.IsHungry(_emo);
            if (want)
            {
                RefreshWalls();
                if (_cols == null)
                {
                    var l = new List<Collider>();
                    foreach (var c in GetComponentsInChildren<Collider>()) if (c != null && !c.isTrigger) l.Add(c);
                    _cols = l.ToArray();
                }
                var pos = transform.position;
                foreach (var w in Walls)
                {
                    if (w == null || _ignoredSet.Contains(w.Pointer)) continue;
                    if (w.bounds.SqrDistance(pos) > 400f) continue;
                    foreach (var c in _cols) if (c != null) Physics.IgnoreCollision(c, w, true);
                    _ignored.Add(w); _ignoredSet.Add(w.Pointer);
                }
                bool inside = _ignored.Count > 0 && InsideIgnoredWall();
                if (!inside) { _lastClear = transform.position; _hasClear = true; }
                else if (_phaseEnd <= 0f && Cfg.QuantumHologram.Value) StartPhase(0.6f);
                return;
            }
            if (_ignored.Count == 0) { _lastClear = transform.position; _hasClear = true; return; }
            // fed while still inside a wall: it does not finish the pass, it steps back to the side it came from
            // (held by the vacuum -> the vacuum pulls it out; the walls come back once it is out, as before)
            if (InsideIgnoredWall() && (held || (CoopCompat.IsCoop && !CoopCompat.IsLocallySimulated(_rb)) || !StepBackOutOfWall())) return;
            RestoreWalls();
            DropGhostsBeyondWalls();
        }

        bool StepBackOutOfWall()
        {
            if (!_hasClear) return false;
            var pos = transform.position;
            if ((_lastClear - pos).sqrMagnitude > 16f) return false;   // it teleported meanwhile: wait until it is out, as before
            var to = _lastClear + Vector3.up * 0.05f;
            if (_rb != null) { _rb.position = to; if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero; }
            transform.position = to;
            if (Cfg.QuantumHologram.Value) StartPhase(U.Rand(0.4f, 0.6f));
            return true;
        }

        void RestoreWalls()
        {
            if (_cols != null)
                foreach (var w in _ignored)
                    if (w != null) foreach (var c in _cols) if (c != null) Physics.IgnoreCollision(c, w, false);
            _ignored.Clear(); _ignoredSet.Clear();
        }

        public void SpawnGhost(Vector3 p)
        {
            var g = Visuals.MakeGhost(this, p);
            if (g == null) return;
            _ghosts.Add(g);
            Snd.Play(Snd.QuantumGhost, p);
        }

        void TeleportTo(QuantumGhost g)
        {
            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            var from = transform.position + Vector3.up * (0.5f * s);
            var to = g.transform.position + Vector3.up * (0.5f * s + 0.05f);
            Fx.Play("FX Despawn", from);
            Snd.Play(Snd.QuantumWarp, from);
            if (_rb != null) { _rb.position = to; if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero; }
            transform.position = to;
            Fx.Play("FX Slime Spawn", to + Vector3.up * 0.5f);
            Snd.Play(Snd.QuantumWarp, to);
            _ghosts.Remove(g);
            g.Pop(false);
            // arrives out of phase: a short see-through hologram flicker before it is solid again
            if (Cfg.QuantumHologram.Value) StartPhase(U.Rand(0.6f, 0.9f));
        }

        /// <summary>Dev: swap reality with a ghost right now (spawns one beside the slime if it has none).</summary>
        public void DevTeleport()
        {
            _ghosts.RemoveAll(g => g == null);
            if (_ghosts.Count == 0)
            {
                var cam = Camera.main;
                SpawnGhost(transform.position + (cam != null ? cam.transform.right : Vector3.right) * 1.6f);
            }
            if (_ghosts.Count > 0) TeleportTo(_ghosts[U.Rng.Next(_ghosts.Count)]);
        }

        public void OnWater() => ClearGhosts(true);

        void ClearGhosts(bool fx)
        {
            foreach (var g in _ghosts) if (g != null) g.Pop(fx);
            _ghosts.Clear();
        }

        void OnDestroy()
        {
            foreach (var g in _ghosts) if (g != null) Object.Destroy(g.gameObject);
            _ghosts.Clear();
            try { RestoreWalls(); } catch { }
            if (_holoMat != null) Object.Destroy(_holoMat);
        }
    }

    public class QuantumGhost : MonoBehaviour
    {
        public QuantumGhost(IntPtr ptr) : base(ptr) { }

        public QuantumSlime Owner;
        float _born, _life, _phase, _nextTurn, _nextWallProbe;
        Vector3 _vel, _scale;

        void Start()
        {
            _born = Time.time;
            _life = U.Rand(40f, 70f);
            _phase = U.Rand(0f, 10f);
            _scale = transform.localScale;
            Fx.Play("FX Slime Spawn", transform.position + Vector3.up * 0.5f, 0.7f);
        }

        void Update()
        {
            if (Owner == null || Time.time - _born > _life) { Pop(true); return; }
            float dt = Time.deltaTime;
            if (Time.time > _nextTurn)
            {
                _nextTurn = Time.time + U.Rand(1.5f, 4f);
                float a = U.Rand(0f, Mathf.PI * 2f);
                _vel = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * U.Rand(0.15f, 0.7f);
            }
            var p = transform.position + _vel * dt;
            bool moved = true;
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 2.5f, U.GroundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            else { _vel = -_vel; p = transform.position; moved = false; }
            // the ghost of a solid (not hungry) slime turns back at a corral wall instead of drifting through it
            if (moved && Time.time >= _nextWallProbe && !Owner.Phasing)
            {
                _nextWallProbe = Time.time + 0.1f;
                float s = Owner.transform.lossyScale.x, speed = _vel.magnitude;
                if (speed > 1e-3f)
                {
                    var lift = Vector3.up * (0.5f * s);
                    var ahead = p + _vel * (0.6f * s / speed);
                    if (Owner.CrossesWall(transform.position + lift, ahead + lift, s)) { _vel = -_vel; p = transform.position; }
                }
            }
            transform.position = p;
            if (_vel.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_vel), dt * 2f);
            float t = (Time.time + _phase) * 2.4f;
            float sq = Mathf.Sin(t) * 0.07f;
            float flick = (Mathf.Sin(t * 7.3f) > 0.97f) ? 0.92f : 1f;
            transform.localScale = new Vector3(_scale.x * (1f + sq), _scale.y * (1f - sq), _scale.z * (1f + sq)) * flick;
        }

        public void Pop(bool fx)
        {
            if (fx)
            {
                Fx.Play("FX Despawn", transform.position + Vector3.up * 0.5f, 0.7f);
                Snd.Play(Snd.QuantumGhostPop, transform.position);
            }
            Destroy(gameObject);
        }
    }

    // =====================================================================================================
    /// <summary>SR1 GlintController + MosaicAttractor: shimmering tiles lure slimes in daylight; glints form in the air, ripen, fall and burst into flame.</summary>
    public class MosaicSlime : MonoBehaviour
    {
        public MosaicSlime(IntPtr ptr) : base(ptr) { }

        const int MaxGlints = 3;
        public static readonly List<MosaicSlime> All = new();
        static int _mosaicSpeakerFrame = -1;
        static MosaicSlime _mosaicSpeaker;

        internal static void ClearSession()
        {
            All.Clear();
            _mosaicSpeaker = null;
            _mosaicSpeakerFrame = -1;
        }
        static bool IsAudioLeader(MosaicSlime me)
        {
            if (Time.frameCount != _mosaicSpeakerFrame)
            {
                _mosaicSpeakerFrame = Time.frameCount;
                _mosaicSpeaker = null;
                float best = 11f * 11f;
                var pl = U.Player;
                if (pl != null)
                {
                    var pp = pl.transform.position;
                    for (int i = All.Count - 1; i >= 0; i--)
                    {
                        var m = All[i];
                        if (m == null || !m.isActiveAndEnabled || (m._vac != null && m._vac.IsHeld())) continue;
                        float d2 = (m.transform.position - pp).sqrMagnitude;
                        if (d2 < best) { best = d2; _mosaicSpeaker = m; }
                    }
                }
            }
            return _mosaicSpeaker == me;
        }

        readonly List<MosaicGlint> _glints = new();
        SlimeEmotions _emo;
        SlimeAppearanceApplicator _app;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        Rigidbody _rb;
        SECTR_PointSource _loop;
        bool _isLargo, _lastRadiant;
        double _nextGlint;
        float _nextPull, _nextFlare;
        ParticleSystem _stars;
        readonly GameObject[] _flares = new GameObject[2];
        readonly float[] _flareT = { -1f, -1f };
        readonly float[] _flareSize = new float[2];

        bool IsRadiant => _app != null && RadiantLooks.IsRadiantAppearance(_app.Appearance);

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _app = GetComponent<SlimeAppearanceApplicator>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _rb = GetComponent<Rigidbody>();
            try { var eat = GetComponent<SlimeEat>(); _isLargo = (eat != null && eat.SlimeDefinition != null && eat.SlimeDefinition.IsLargo) || transform.lossyScale.x > 1.35f; } catch { }
            All.Add(this);
            _loop = Snd.Loop(gameObject, Snd.MosaicAura);
            _lastRadiant = IsRadiant;
            // SR1 look: white four-point stars twinkling on the glass + occasional glint flares (opal/cyan-pink on Radiant)
            _stars = Gfx.Sparkles("MosaicStars", transform, new Vector3(0f, 0.55f, 0f), Visuals.StarMat,
                _lastRadiant ? new Color(1f, 0.78f, 0.98f, 1f) : Color.white,
                _lastRadiant ? new Color(0.55f, 0.92f, 1f, 1f) : new Color(0.7f, 0.9f, 1f, 1f),
                _isLargo ? 0.9f : 2.2f, 0.75f, 0.20f, _isLargo ? 0.34f : 0.46f, 0.5f, _isLargo ? 4 : 8);
            for (int i = 0; i < _flares.Length; i++)
            {
                _flares[i] = Gfx.Billboard("MosaicFlare", transform, Visuals.FlareMat, Vector3.zero, 0f);
                _flares[i].SetActive(false);
                var bb = _flares[i].GetComponent<Billboard>(); if (bb != null) bb.Spin = i == 0 ? 60f : -45f;
            }
            _nextFlare = Time.time + U.Rand(0.8f, 2.8f);
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td != null) _nextGlint = td.HoursFromNow(U.Rand(0.15f, 0.7f));
        }

        void Flares(bool show)
        {
            float now = Time.time;
            if (show && now > _nextFlare)
            {
                _nextFlare = now + (_isLargo ? U.Rand(2.8f, 6.5f) : U.Rand(1.0f, 2.8f));
                for (int i = 0; i < _flares.Length; i++)
                {
                    if (_flares[i] == null || _flareT[i] >= 0f) continue;
                    var d = UnityEngine.Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.1f;
                    _flares[i].transform.localPosition = new Vector3(0f, 0.5f, 0f) + d.normalized * 0.52f;
                    _flareT[i] = 0f; _flareSize[i] = _isLargo ? U.Rand(0.32f, 0.55f) : U.Rand(0.50f, 0.85f);
                    _flares[i].SetActive(true);
                    break;
                }
            }
            for (int i = 0; i < _flares.Length; i++)
            {
                if (_flares[i] == null || _flareT[i] < 0f) continue;
                _flareT[i] += Time.deltaTime / 0.55f;
                if (_flareT[i] >= 1f || !show) { _flareT[i] = -1f; _flares[i].SetActive(false); continue; }
                float k = Mathf.Sin(_flareT[i] * Mathf.PI);
                _flares[i].transform.localScale = Vector3.one * (_flareSize[i] * k * k);
            }
        }

        void Update()
        {
            _glints.RemoveAll(g => g == null);
            bool rad = IsRadiant;
            if (rad != _lastRadiant)
            {
                _lastRadiant = rad;
                if (_stars != null)
                {
                    try
                    {
                        var mm = _stars.main;
                        mm.startColor = new ParticleSystem.MinMaxGradient(
                            rad ? new Color(1f, 0.78f, 0.98f, 1f) : Color.white,
                            rad ? new Color(0.55f, 0.92f, 1f, 1f) : new Color(0.7f, 0.9f, 1f, 1f));
                    }
                    catch { }
                }
            }
            bool day = U.IsDay();
            bool held = _vac != null && _vac.IsHeld();
            Gfx.SetEmitting(_stars, !held);
            Flares(!held);
            Snd.SetPlaying(_loop, day && !held && IsAudioLeader(this));
            bool sim = !CoopCompat.IsCoop || CoopCompat.IsLocallySimulated(_rb);
            if (sim && day && !held && Time.time > _nextPull) { _nextPull = Time.time + 1.5f; PullSlimes(); }
            if (!sim || !Cfg.MosaicGlints.Value || held) return;
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td == null || !td.HasReached(_nextGlint)) return;
            float agit = U.Agitation(_emo);
            _nextGlint = td.HoursFromNow((_isLargo ? 1.35f : 0.65f) * (1f - 0.75f * agit));
            bool calmed = _calm != null && _calm.IsCalmed();
            if (day && !calmed && _glints.Count < (_isLargo ? 1 : MaxGlints)) SpawnGlint(Mathf.Lerp(7.5f, 30f, agit) * 0.5f);
        }

        public void SpawnGlint(float radius)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = U.Rand(0f, Mathf.PI * 2f), r = U.Rand(1.5f, Mathf.Max(2f, radius));
                var p = transform.position + new Vector3(Mathf.Cos(a) * r, U.Rand(2.5f, 4.5f), Mathf.Sin(a) * r);
                if (Physics.Linecast(transform.position + Vector3.up, p, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                var g = Visuals.MakeGlint(this, p);
                if (g != null) { _glints.Add(g); Snd.PlayThrottled(Snd.GlintSpawn, p, "glintSpawn", 4.5f); }
                return;
            }
        }

        void PullSlimes()
        {
            var cols = Physics.OverlapSphere(transform.position, 9f, U.SlimeMask, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<IntPtr>();
            var eye = transform.position + Vector3.up * 0.5f;
            foreach (var c in cols)
            {
                var rb = c != null ? c.attachedRigidbody : null;
                if (rb == null || rb.isKinematic || rb.gameObject == gameObject || !seen.Add(rb.Pointer)) continue;
                var eat = rb.GetComponent<SlimeEat>();
                if (eat == null || eat.SlimeDefinition == null || eat.SlimeDefinition.name.IndexOf("tarr", StringComparison.OrdinalIgnoreCase) >= 0 || rb.GetComponent<MosaicSlime>() != null) continue;
                var sj = rb.GetComponent<SafeJointReference>();
                if ((sj != null && sj.Joint != null) || rb.GetComponent<Joint>() != null) continue;
                var v = rb.GetComponent<Vacuumable>();
                if (v != null && v.IsHeld()) continue;
                if (Physics.Linecast(eye, rb.position + Vector3.up * 0.5f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                var dir = transform.position - rb.position;
                dir.y = 0f;
                float d = dir.magnitude;
                if (d < 1.8f) continue;
                rb.AddForce(dir / d * 1.4f + Vector3.up * 0.6f, ForceMode.VelocityChange);
            }
        }

        public void OnWater()
        {
            foreach (var g in _glints) if (g != null) g.Fizzle();
            _glints.Clear();
        }

        // Native contact squash stays active. The glass follows its actual warped vertices, not a scaled sphere.
        ElasticGlassRig _glass;
        readonly JellyRig _jelly = new();
        float _born;
        bool _jellyTried, _everBound;
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] public JellyRig Jelly => _jelly;

        void OnEnable() { _born = Time.time; }

        void LateUpdate()
        {
            if (Visuals.MosaicGlassMode == 3)
            {
                if (!_everBound && Time.time - _born < 0.3f) return;
                _everBound = true;
                try { (_glass ??= new ElasticGlassRig(transform)).Sync(); }
                catch (Exception e) { ClassicSlimesMod.Log.Warning("Mosaic glass bind: " + e.Message); }
                return;
            }
            if (_glass != null) { _glass.Dispose(); _glass = null; }
            if (!_jellyTried)
            {
                if (Visuals.MosaicGlassMode != 0 || (!_everBound && Time.time - _born < 0.3f) || !Cfg.MosaicJelly.Value) return;
                _jellyTried = true;
                _everBound = true;
                try { _jelly.Init(transform, JellyBody()); } catch (Exception e) { ClassicSlimesMod.Log.Warning("Mosaic jelly: " + e.Message); }
                return;
            }
            if (!_jelly.Ok) return;
            try { _jelly.Tick(); }
            catch (Exception e) { _jelly.Fail(); ClassicSlimesMod.Log.Warning("Mosaic jelly: " + e.Message); }
        }

        SkinnedMeshRenderer JellyBody()
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r != null ? r.sharedMaterial : null;
                if (m == null || !m.name.Contains("Mosaic") || m.name.Contains("Glass") || m.name.Contains("Shell") || m.name.Contains("Coat")) continue;
                return r;
            }
            return null;
        }

        public string JellyInfo() => _glass != null ? _glass.Info() : _jelly.Ok ? _jelly.Info() : $"glass pending / jelly off (tried {_jellyTried})";

        void OnDisable()
        {
            if (_glass != null) { _glass.Dispose(); _glass = null; }
            foreach (var r in GetComponentsInChildren<Renderer>(true)) SlimeFacets.RestoreTiles(r);
            _jelly.Restore();
            _jellyTried = false;
            Snd.SetPlaying(_loop, false);
            Gfx.SetEmitting(_stars, false);
            Flares(false);
            foreach (var g in _glints) if (g != null) Object.Destroy(g.gameObject);
            _glints.Clear();
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (_mosaicSpeaker == this) _mosaicSpeaker = null;
            if (_glass != null) { _glass.Dispose(); _glass = null; }
            foreach (var g in _glints) if (g != null) Object.Destroy(g.gameObject);
            _glints.Clear();
        }
    }

    public class MosaicGlint : MonoBehaviour
    {
        public MosaicGlint(IntPtr ptr) : base(ptr) { }

        enum State { Suspended, Ready, Free }
        public MosaicSlime Owner;
        State _state = State.Suspended;
        double _stateEnd;
        float _phase, _spin;
        GameObject _fx, _core;

        void Start()
        {
            _phase = U.Rand(0f, 6f);
            var td = SceneContext.Instance.TimeDirector;
            _stateEnd = td.HoursFromNow(1f * U.Rand(0.8f, 1.2f));      // SR1: suspended for ~1 game hour
            _core = transform.childCount > 0 ? transform.GetChild(0).gameObject : null;
        }

        void Update()
        {
            if (Owner == null || !Owner.isActiveAndEnabled) { Destroy(gameObject); return; }
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td == null) return;
            float t = Time.time + _phase;
            float spinSpeed = _state == State.Suspended ? 40f : 160f;
            _spin += Time.deltaTime * spinSpeed;
            var bb = _core != null ? _core.GetComponent<Billboard>() : null;
            if (bb != null) bb.Spin = spinSpeed;
            float pulse = _state == State.Suspended ? 1f + Mathf.Sin(t * 2f) * 0.12f : 1.25f + Mathf.Sin(t * 9f) * 0.25f;
            if (_core != null) _core.transform.localScale = Vector3.one * 1.1f * pulse;

            switch (_state)
            {
                case State.Suspended:
                    transform.position += Vector3.up * (Mathf.Sin(t * 0.8f) * 0.08f * Time.deltaTime);
                    if (td.HasReached(_stateEnd)) { _state = State.Ready; _stateEnd = td.HoursFromNow(0.5f); }
                    break;
                case State.Ready:
                    if (td.HasReached(_stateEnd)) { _state = State.Free; _stateEnd = td.HoursFromNow(0.5f); }
                    break;
                case State.Free:
                    var drift = new Vector3(Mathf.Sin(t * 0.7f), -1.2f, Mathf.Cos(t * 0.6f)) * (0.55f * Time.deltaTime);
                    var next = transform.position + drift;
                    if (Physics.Linecast(transform.position, next + Vector3.down * 0.2f, U.GroundMask, QueryTriggerInteraction.Ignore) || td.HasReached(_stateEnd))
                    { Burst(); return; }
                    transform.position = next;
                    break;
            }
        }

        void Burst()
        {
            var p = transform.position;
            Fx.Play("FX FireSlimeDeath", p);
            Snd.PlayThrottled(Snd.GlintBlast, p, "glintBlast", 2.5f);
            int ignited = 0; bool hitPlayer = false;
            if (Cfg.MosaicGlintFire.Value)
            {
                foreach (var c in Physics.OverlapSphere(p, 3.5f, U.SlimeMask, QueryTriggerInteraction.Ignore))
                {
                    var ig = c != null && c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<SlimeIgniteReact>() : null;
                    if (ig != null) { try { ig.Ignite(gameObject); ignited++; } catch { } }
                }
                if (U.DistToPlayer(p) < 3.5f) hitPlayer = Dmg.Player(15, gameObject, "BoomSlimeExplode", "SlimeAttack");
            }
            if (Diag.Enabled) ClassicSlimesMod.Log.Msg($"[glint] burst at {p} ignited={ignited} playerHit={hitPlayer} fire={Cfg.MosaicGlintFire.Value}");
            Destroy(gameObject);
        }

        public void Fizzle()
        {
            Fx.Play("FX waterSplat", transform.position, 0.6f);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_fx != null) Fx.Stop(_fx);
        }
    }

    // =====================================================================================================
    /// <summary>
    /// Mochi's quicksilver slimes: liquid-mercury chrome that darts about in lightning-quick dashes (leaving a silver
    /// streak) and catches the light with little star glints. Eating energy (rad plorts) overcharges them: the body turns
    /// to electric-blue chrome with SR2's hyper-slime sparks, static crackles around it, dashes turn into zig-zags and
    /// touching it zaps. Over its last two seconds the charge flickers out, then it discharges with a fizzle.
    /// </summary>
    public class QuicksilverSlime : MonoBehaviour
    {
        public QuicksilverSlime(IntPtr ptr) : base(ptr) { }

        class Swap { public Renderer R; public int I; public Material Orig, Applied; }

        static readonly Color StreakHead = new(0.86f, 0.9f, 1f, 0.55f), StreakTail = new(0.55f, 0.62f, 0.75f, 0f);
        static readonly Color ChargedHead = new(0.45f, 0.86f, 1f, 0.8f), ChargedTail = new(0.15f, 0.45f, 1f, 0f);
        static readonly Color RadStreakHead = new(1.00f, 0.78f, 0.90f, 0.65f), RadStreakTail = new(0.62f, 0.30f, 0.78f, 0f);
        static readonly Color RadChargedHead = new(1.00f, 0.85f, 0.35f, 0.88f), RadChargedTail = new(0.92f, 0.25f, 0.82f, 0f);

        SlimeAppearanceApplicator _app;
        Rigidbody _rb;
        Vacuumable _vac;
        SECTR_PointSource _loop;
        ParticleSystem _crackle, _shine;
        TrailRenderer _streak;
        readonly List<Swap> _swaps = new();
        float _chargedUntil, _nextDash, _nextZap, _nextSpark, _nextCrackle, _nextStatic, _nextShine, _zigAt = -1f;
        bool _wasCharged, _chargedLook, _streakOn, _lastRadiant;
        Vector3 _zigDir;

        bool IsRadiant => _app != null && RadiantLooks.IsRadiantAppearance(_app.Appearance);

        // ground runner (SR1 quicksilver never hops: it races around like a car on a track)
        /// <summary>SR2 hopping subbehaviours (random move, food leaps) of the quicksilver slimes -> their runner.</summary>
        public static readonly Dictionary<IntPtr, QuicksilverSlime> NoHop = new();
        readonly List<IntPtr> _noHopKeys = new();
        float _yaw, _turnRate, _nextTurn, _pauseUntil, _nextPause, _goalUntil, _boostUntil;
        Vector3 _home, _goal;
        bool _homeSet;
        // chased: it outruns the rancher, jinks out of the vacuum cone and resists the vacuum until it tires out
        // (it has to be kept in the cone for a while), runs up walls and ledges and drops down when it has to
        float _stamina = 1f, _tiredUntil, _jinkSide = 1f, _nextJink, _climbUntil, _nextClimbRoll, _fleeUntil, _fledAt = -100f;
        Vector3 _climbTop, _climbDir;
        bool _noForces, _wasTired;
        public static float RunSpeed = 9f, ChargedSpeed = 13f, FleeSpeed = 13f, MaxSpeed = 24f, Grip = 34f, FleeGrip = 55f, TetherRadius = 40f;
        public static float FleeRadius = 12f, ResistTime = 1.4f, TiredTime = 2.8f, ClimbHeight = 5f, ClimbSpeed = 7.5f;
        /// <summary>Dev: pretend the vacpack is sucking along the camera's view until then.</summary>
        public static float DevAimUntil;

        internal static void ClearSession()
        {
            NoHop.Clear();
            _plAt = -1f;
            _vacItem = null;
        }

        bool _seated;
        float _seatGraceUntil;
        public bool IsSeated
        {
            get
            {
                var sj = GetComponent<SafeJointReference>();
                var j = GetComponent<Joint>();
                bool hasJoint = (sj != null && sj.Joint != null) || j != null;
                if (_seated && !hasJoint && Time.time >= _seatGraceUntil) _seated = false;
                return _seated || hasJoint || Time.time < _seatGraceUntil;
            }
        }
        public void SetSeated(bool on)
        {
            _seated = on;
            if (on)
            {
                _fleeUntil = 0f; _climbUntil = 0f; _boostUntil = 0f; _zigAt = -1f;
                SetNoForces(false);
                if (_rb != null && !_rb.isKinematic) _rb.linearVelocity = Vector3.zero;
            }
        }
        public void EnterSeatGrace(float sec = 0.2f)
        {
            _seatGraceUntil = Time.time + sec;
            _fleeUntil = 0f; _climbUntil = 0f; _boostUntil = 0f; _zigAt = -1f;
            SetNoForces(false);
        }

        public bool Charged => Time.time < _chargedUntil;
        public bool Tired => Time.time < _tiredUntil;

        void Start()
        {
            _app = GetComponent<SlimeAppearanceApplicator>();
            _rb = GetComponent<Rigidbody>();
            _vac = GetComponent<Vacuumable>();
            _loop = Snd.Loop(gameObject, Snd.QsAura);
            float now = Time.time;
            _nextDash = now + U.Rand(1f, 4f);
            _nextStatic = now + U.Rand(2f, 6f);
            _nextShine = now + U.Rand(0.5f, 2.5f);
            _lastRadiant = IsRadiant;
            // static crackle: small stars around the body (electric-blue or rose-gold on Radiant)
            _crackle = Gfx.Sparkles("QsCrackle", transform, new Vector3(0f, 0.5f, 0f), Visuals.StarMat,
                _lastRadiant ? new Color(1.00f, 0.90f, 0.65f, 1f) : new Color(0.8f, 0.96f, 1f, 1f),
                _lastRadiant ? new Color(1.00f, 0.42f, 0.85f, 1f) : new Color(0.3f, 0.68f, 1f, 1f),
                0f, 0.2f, 0.1f, 0.26f, 0.55f, 24, 0.5f);
            // chrome shine: a white star glint on the upper body now and then
            _shine = Gfx.Sparkles("QsShine", transform, new Vector3(0f, 0.78f, 0.12f), Visuals.StarMat,
                Color.white, new Color(0.86f, 0.93f, 1f, 1f), 0f, 0.42f, 0.22f, 0.36f, 0.26f, 3);
            _streak = Gfx.Ribbon("QsStreak", transform, new Vector3(0f, 0.42f, 0f), Visuals.QsStreakMat, 0.22f, 0.62f, 0.04f,
                _lastRadiant ? RadStreakHead : StreakHead, _lastRadiant ? RadStreakTail : StreakTail);
            if (_streak != null) { _streak.textureMode = LineTextureMode.Stretch; _streak.emitting = false; }
            _yaw = YawOf(transform.forward);
            _nextPause = now + U.Rand(5f, 10f);
            if (Cfg.QuicksilverRuns.Value)
            {
                try { foreach (var c in GetComponentsInChildren<SlimeRandomMove>(true)) { NoHop[c.Pointer] = this; _noHopKeys.Add(c.Pointer); } } catch { }
                try { foreach (var c in GetComponentsInChildren<GotoConsumable>(true)) { NoHop[c.Pointer] = this; _noHopKeys.Add(c.Pointer); } } catch { }
            }
        }

        void OnDestroy()
        {
            foreach (var k in _noHopKeys) NoHop.Remove(k);
        }

        static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>SR2's food search (GotoConsumable) found something: race there instead of leaping.</summary>
        public void RunTo(Vector3 p) { _goal = p; _goalUntil = Time.time + 0.5f; }

        enum Ahead { Clear, Wall, Drop }

        static int _waterMask = -1;
        static int WaterMask
        {
            get
            {
                if (_waterMask < 0) { int w = LayerMask.NameToLayer("Water"); _waterMask = 1 << (w >= 0 ? w : 4); }
                return _waterMask;
            }
        }

        /// <summary>
        /// What is ahead in that direction (look-ahead grows with speed): a wall, a drop it shouldn't take (deeper than
        /// maxDrop body sizes, water, the void) or clear ground.
        /// </summary>
        static Ahead Look(Vector3 pos, Vector3 dir, float s, float speed, float maxDrop, out RaycastHit wall)
        {
            float look = (0.9f + speed * 0.13f) * s;
            var o = pos + Vector3.up * (0.5f * s);
            if (Physics.SphereCast(o, 0.25f * s, dir, out wall, look, U.GroundMask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.55f) return Ahead.Wall;
            var p = o + dir * look;
            if (!Physics.Raycast(p, Vector3.down, out var g, maxDrop * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return Ahead.Drop;
            if (Physics.Raycast(p, Vector3.down, out var wHit, g.distance, WaterMask, QueryTriggerInteraction.Collide) && (g.distance - wHit.distance > 0.65f * s))
                return Ahead.Drop;   // deep water before the ground; wade through shallow puddles
            return Ahead.Clear;
        }

        // the rancher as the quicksilvers see it (shared, sensed once per physics step): ground speed, the vacpack's suction
        static float _plAt = -1f, _plSpeed, _vacRange;
        static Vector3 _plPos, _vacEye, _vacFwd;
        static bool _vacOn;
        static Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem _vacItem;

        static void Sense()
        {
            float t = Time.fixedTime;
            if (t == _plAt) return;
            var pl = U.Player;
            _vacOn = false;
            if (pl == null) { _plAt = -1f; return; }
            var p = pl.transform.position;
            float dt = t - _plAt;
            if (_plAt >= 0f && dt > 0f && dt < 0.5f)
            {
                var d = p - _plPos; d.y = 0f;
                float sp = d.magnitude / dt;
                if (sp < 60f) _plSpeed = Mathf.Lerp(_plSpeed, sp, 1f - Mathf.Exp(-dt * 5f));   // (not a teleport)
            }
            _plAt = t; _plPos = p;
            var cam = Camera.main;
            if (cam == null) return;
            try
            {
                if (_vacItem == null) _vacItem = SceneContext.Instance.PlayerState.VacuumItem;
                if (_vacItem != null && _vacItem._vacMode == Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.VacuumItem.VacMode.VAC)
                {
                    _vacOn = true;
                    _vacRange = Mathf.Max(6f, _vacItem.MaxVacDist);
                }
            }
            catch { _vacItem = null; }
            if (!_vacOn && Time.time < DevAimUntil) { _vacOn = true; _vacRange = 12f; }
            if (!_vacOn) return;
            _vacEye = cam.transform.position;
            _vacFwd = cam.transform.forward;
        }

        /// <summary>In the vacpack's suction cone (in range, near the aim line).</summary>
        static bool InVac(Vector3 center)
        {
            if (!_vacOn) return false;
            var to = center - _vacEye;
            float d = to.magnitude;
            if (d > _vacRange + 1.5f || d < 0.01f) return false;
            return Vector3.Angle(_vacFwd, to) < (d < 3f ? 35f : 22f);
        }

        /// <summary>Stamina: drains while it is in the suction cone (it resists: no vacuum forces), then it is tired out for a moment.</summary>
        void Stamina(float now, float dt, bool aimed, bool charged)
        {
            bool tired = now < _tiredUntil;
            if (_wasTired && !tired) _stamina = 0.5f;   // got half its breath back
            _wasTired = tired;
            if (!tired)
            {
                if (aimed)
                {
                    _stamina -= dt / (ResistTime * (charged ? 1.6f : 1f));
                    if (_stamina <= 0f) { _stamina = 0f; _tiredUntil = now + TiredTime * (charged ? 0.6f : 1f); tired = true; Exhausted(); }
                }
                else _stamina = Mathf.Min(1f, _stamina + dt / 4f);
            }
            SetNoForces(aimed && !tired);
        }

        void SetNoForces(bool on)
        {
            if (on == _noForces || _vac == null) return;
            _noForces = on;
            try { _vac.PreventForcesFromWeaponVacuum = on; } catch { }
        }

        void Exhausted()
        {
            var p = transform.position + Vector3.up * (0.6f * transform.lossyScale.x);
            Snd.Play(Snd.QsFizzle, p);
            if (_crackle != null) _crackle.Emit(6);
            _boostUntil = 0f;
            _climbUntil = 0f;
            _zigAt = -1f;
            if (Cfg.Diagnostics.Value) ClassicSlimesMod.Log.Msg($"Quicksilver {name} tired out");
        }

        /// <summary>
        /// The runner: long sweeping curves at speed (charged: faster, tighter), swerving from walls and drops, back
        /// towards home when it strays, short pauses now and then. Chased (rancher close or the vacpack on it): flees
        /// faster than the rancher runs, jinking across the suction cone; walls with a reachable top get climbed.
        /// </summary>
        void FixedUpdate()
        {
            if (IsSeated)
            {
                SetNoForces(false);
                _climbUntil = 0f;
                _fleeUntil = 0f;
                _boostUntil = 0f;
                return;
            }
            if (!Cfg.QuicksilverRuns.Value || _rb == null || _rb.isKinematic || (_vac != null && (_vac.IsHeld() || _vac.IsLaunched())))
            {
                SetNoForces(false);
                _climbUntil = 0f;
                return;
            }
            Sense();
            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            float now = Time.time, dt = Time.fixedDeltaTime;
            var pos = transform.position;
            bool charged = Charged;
            bool aimed = InVac(pos + Vector3.up * (0.5f * s));
            Stamina(now, dt, aimed, charged);
            bool tired = now < _tiredUntil;
            if (now < _climbUntil) { Climb(pos, s, charged); return; }
            if (!U.Grounded(transform, s)) return;
            if (tired && aimed) return;   // worn out: the vacuum gets it
            if (!_homeSet || (pos - _home).sqrMagnitude > 60f * 60f) { _home = pos; _homeSet = true; }   // carried somewhere new
            if (now > _nextTurn) { _nextTurn = now + U.Rand(0.7f, 1.8f); _turnRate = U.Rand(-95f, 95f) * (charged ? 1.6f : 1f); }
            var pl = CoopCompat.NearestPlayer(pos, out var nearPlPos, out _);
            var away = Vector3.zero;
            float plDist = float.MaxValue;
            if (pl != null) { away = pos - nearPlPos; away.y = 0f; plDist = away.magnitude; }
            if (!tired && pl != null && (aimed || plDist < FleeRadius * (charged ? 0.6f : 1f))) _fleeUntil = now + 1.5f;
            bool fleeing = !tired && now < _fleeUntil;
            float? target = null;
            float speed;
            if (fleeing)
            {
                _fledAt = now;
                var run = plDist > 0.01f ? away / plDist : transform.forward;
                if (aimed)
                {
                    // jink across the suction cone, switching sides now and then
                    if (now > _nextJink) { _nextJink = now + U.Rand(0.35f, 0.8f); if (U.Rng.NextDouble() < 0.55) _jinkSide = -_jinkSide; }
                    var side = Vector3.Cross(Vector3.up, _vacFwd); side.y = 0f;
                    if (side.sqrMagnitude > 1e-4f) run = (side.normalized * (1.4f * _jinkSide) + run).normalized;
                }
                target = YawOf(run);
                speed = Mathf.Min(Mathf.Max(FleeSpeed, _plSpeed * 1.25f + 2.5f) * (charged ? 1.2f : 1f), MaxSpeed * (charged ? 1.15f : 1f));
            }
            else
            {
                if (now < _goalUntil) target = YawOf(_goal - pos);
                else
                {
                    var toHome = _home - pos; toHome.y = 0f;
                    if (toHome.magnitude > TetherRadius)
                    {
                        if (now - _fledAt < 3f) _home = pos;   // ran off somewhere new: roam there
                        else target = YawOf(toHome);
                    }
                }
                speed = now < _pauseUntil ? 0f : (charged ? ChargedSpeed : RunSpeed) * (now < _boostUntil ? 1.5f : 1f);
                if (tired) speed = RunSpeed * 0.3f;
            }
            float maxTurn = (fleeing ? 400f : charged ? 320f : 210f) * dt;
            if (target.HasValue) _yaw = Mathf.MoveTowardsAngle(_yaw, target.Value, maxTurn);
            else _yaw += _turnRate * dt;
            var dir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            if (speed > 0f)
            {
                // drops: chased it jumps down from anything that isn't too high, roaming only when it is up above its home
                float maxDrop = fleeing ? 10f : pos.y > _home.y + 1.5f ? 6f : 2.1f;
                var a = Look(pos, dir, s, speed, maxDrop, out var wall);
                if (a == Ahead.Wall && !tired && TryClimb(now, pos, dir, s, wall, fleeing || pos.y < _home.y - 1.5f)) return;
                if (a != Ahead.Clear)
                {
                    bool l = Look(pos, Quaternion.Euler(0f, -60f, 0f) * dir, s, speed, maxDrop, out _) != Ahead.Clear;
                    bool r = Look(pos, Quaternion.Euler(0f, 60f, 0f) * dir, s, speed, maxDrop, out _) != Ahead.Clear;
                    float side = (!l && r) ? -1f : (l && !r) ? 1f : (_turnRate >= 0f ? 1f : -1f);
                    _yaw += side * (l && r ? 540f : 400f) * (fleeing ? 1.3f : 1f) * dt;
                    if (l && r) speed *= 0.35f;   // dead end: brake and swing round
                    _turnRate = side * Mathf.Abs(_turnRate);
                    dir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                }
            }
            // grip: pull the horizontal velocity towards the heading, keep the vertical one (no hops)
            var v = _rb.linearVelocity;
            var dv = dir * speed - new Vector3(v.x, 0f, v.z);
            float maxDv = (fleeing ? FleeGrip : Grip) * (charged ? 1.5f : 1f) * (tired ? 0.4f : 1f) * dt;
            if (dv.magnitude > maxDv) dv = dv.normalized * maxDv;
            _rb.AddForce(dv, ForceMode.VelocityChange);
            if (fleeing) _rb.AddForce(Vector3.down * 12f, ForceMode.Acceleration);   // hugs the ground at speed
            if (speed > 0.5f) _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(dir, Vector3.up), 10f * dt));
            if (now > _nextPause)
            {
                _nextPause = now + U.Rand(5f, 12f);
                if (!charged && !fleeing) _pauseUntil = now + U.Rand(0.6f, 1.8f);
            }
        }

        /// <summary>A wall ahead with a top it can reach (up to ClimbHeight bodies): run up it (roaming: now and then).</summary>
        bool TryClimb(float now, Vector3 pos, Vector3 dir, float s, RaycastHit wall, bool eager)
        {
            if (!eager)
            {
                if (now < _nextClimbRoll) return false;
                _nextClimbRoll = now + U.Rand(1.5f, 3f);
                if (U.Rng.NextDouble() > 0.45) return false;
            }
            if (!Physics.Raycast(pos + Vector3.up * (0.1f * s), Vector3.down, out var gnd, 1.5f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            float maxH = ClimbHeight * s, baseY = gnd.point.y;
            var wallDir = -wall.normal; wallDir.y = 0f;
            if (wallDir.sqrMagnitude < 0.01f) return false;
            wallDir.Normalize();
            // headroom (no overhang above it), then the top of the wall just past its face
            if (Physics.Raycast(pos, Vector3.up, maxH + 0.8f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            var probe = new Vector3(wall.point.x, baseY + maxH + 0.8f * s, wall.point.z) + wallDir * (0.8f * s);
            if (Physics.CheckSphere(probe, 0.3f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;   // taller than that
            if (!Physics.Raycast(probe, Vector3.down, out var top, maxH + 0.8f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            float h = top.point.y - baseY;
            if (top.normal.y < 0.6f || h < 0.5f * s || h > maxH) return false;
            if (Physics.CheckSphere(top.point + Vector3.up * (0.75f * s), 0.4f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;   // no room up there
            if (Physics.Linecast(new Vector3(pos.x, top.point.y + 0.75f * s, pos.z), top.point + Vector3.up * (0.75f * s), U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (Physics.Raycast(top.point + Vector3.up * 0.5f, Vector3.down, 1f, WaterMask, QueryTriggerInteraction.Collide)) return false;
            _climbTop = top.point;
            _climbDir = wallDir;
            _climbUntil = now + h / (ClimbSpeed * Mathf.Sqrt(s)) + 0.8f;
            _yaw = YawOf(wallDir);
            if (Cfg.Diagnostics.Value) ClassicSlimesMod.Log.Msg($"Quicksilver {name} climbs {h:0.0} m");
            return true;
        }

        /// <summary>Running up the wall (straight up along its face), over the edge at the top.</summary>
        void Climb(Vector3 pos, float s, bool charged)
        {
            if (pos.y >= _climbTop.y + 0.75f * s)
            {
                _climbUntil = 0f;
                var v = _rb.linearVelocity;
                _rb.linearVelocity = _climbDir * (Mathf.Max(RunSpeed, new Vector2(v.x, v.z).magnitude) * 0.8f) + Vector3.up * 1.5f;
                return;
            }
            _rb.linearVelocity = _climbDir * 1.2f + Vector3.up * (ClimbSpeed * (charged ? 1.4f : 1f) * Mathf.Sqrt(s));
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(_climbDir, Vector3.up), 12f * Time.fixedDeltaTime));
        }

        /// <summary>Dev: runner state.</summary>
        public string DevInfo()
        {
            float now = Time.time;
            var v = _rb != null ? _rb.linearVelocity : Vector3.zero;
            return $"{name} v {new Vector2(v.x, v.z).magnitude:0.0}/{v.y:0.0} stamina {_stamina:0.00} tired {now < _tiredUntil} flee {now < _fleeUntil} climb {now < _climbUntil} " +
                   $"charged {Charged} grounded {U.Grounded(transform, Mathf.Max(0.3f, transform.lossyScale.x))} rancher {_plSpeed:0.0} m/s vac {_vacOn} in {InVac(transform.position + Vector3.up * 0.5f)} noForces {_noForces} home {Vector3.Distance(_home, transform.position):0}m";
        }

        void OnDisable()
        {
            try { if (_chargedLook) SetChargedLook(false); } catch { }
            SetNoForces(false);
            _climbUntil = 0f;
        }

        public void Overcharge(float seconds)
        {
            float now = Time.time;
            _chargedUntil = Mathf.Max(_chargedUntil, now) + seconds;
            var p = transform.position + Vector3.up * (0.6f * transform.lossyScale.x);
            Fx.Play("FX HyperSlimeCharge", p, 0.6f);
            Snd.Play(Snd.QsHit, p);
            if (_crackle != null) _crackle.Emit(12);
            _nextDash = Mathf.Min(_nextDash, now + 0.4f);   // jolts into motion
        }

        void Update()
        {
            float now = Time.time;
            bool rad = IsRadiant;
            if (rad != _lastRadiant)
            {
                _lastRadiant = rad;
                if (_crackle != null)
                {
                    try
                    {
                        var mm = _crackle.main;
                        mm.startColor = new ParticleSystem.MinMaxGradient(
                            rad ? new Color(1.00f, 0.90f, 0.65f, 1f) : new Color(0.8f, 0.96f, 1f, 1f),
                            rad ? new Color(1.00f, 0.42f, 0.85f, 1f) : new Color(0.3f, 0.68f, 1f, 1f));
                    }
                    catch { }
                }
            }
            bool seated = IsSeated;
            bool held = (_vac != null && _vac.IsHeld()) || seated;
            bool charged = Charged;
            float left = _chargedUntil - now;
            // the charge flickers out over its last two seconds
            bool look = charged && (left > 2f || Mathf.Repeat(now * (left < 0.8f ? 9f : 4.5f), 1f) < 0.62f);
            if (look != _chargedLook) SetChargedLook(look);
            if (_wasCharged && !charged) Discharge();
            _wasCharged = charged;
            Snd.SetPlaying(_loop, charged && !held);
            UpdateStreak(held, charged);
            if (held) return;
            float s = transform.lossyScale.x;
            var center = transform.position + Vector3.up * (0.6f * s);
            if (now > _nextShine)
            {
                _nextShine = now + U.Rand(1.2f, 3.5f);
                if (_shine != null) _shine.Emit(1);
            }
            bool posed = _rb != null && _rb.isKinematic;
            if (_zigAt > 0f && now > _zigAt) Zig(posed);
            if (now > _nextDash)
            {
                _nextDash = now + U.Rand(2.5f, 6f) * (charged ? 0.45f : 1f);
                if (!posed && !Tired && now >= _climbUntil && U.Grounded(transform, s)) Dash(charged, center);
            }
            if (!charged)
            {
                if (now > _nextStatic)   // a little static now and then, even uncharged
                {
                    _nextStatic = now + U.Rand(3f, 8f);
                    if (_crackle != null) _crackle.Emit(U.Rng.Next(1, 3));
                }
                return;
            }
            if (now > _nextCrackle)
            {
                _nextCrackle = now + U.Rand(0.06f, 0.3f);
                if (_crackle != null) _crackle.Emit(U.Rng.Next(1, 4));
            }
            if (now > _nextSpark)
            {
                _nextSpark = now + U.Rand(0.35f, 1.1f);
                Fx.Play("FX Hyper Slime Contact", center + UnityEngine.Random.insideUnitSphere * 0.35f * s, 0.6f);
            }
            if (now > _nextZap && U.DistToPlayer(center) < 1.7f * s)
            {
                _nextZap = now + 1.25f;
                if (Dmg.Player(Cfg.QuicksilverShockDamage.Value, gameObject, "LightningStrike", "SlimeAttack"))
                {
                    Snd.Play(Snd.QsHit, center);
                    var pl = U.Player;
                    if (pl != null) Fx.Play("FX Hyper Slime Contact", pl.transform.position + Vector3.up);
                    if (_crackle != null) _crackle.Emit(8);
                }
            }
        }

        /// <summary>Overcharged look: swap the quicksilver body/crest materials for the electric-blue (or Radiant plasma) charged copy.</summary>
        void SetChargedLook(bool on)
        {
            _chargedLook = on;
            bool rad = IsRadiant;
            if (on)
            {
                _swaps.Clear();
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r == null || r.TryCast<ParticleSystemRenderer>() != null || r.TryCast<TrailRenderer>() != null) continue;
                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    bool any = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        bool isRadMat = m.name.Contains("QsRadiant", StringComparison.Ordinal);
                        bool isBaseMat = m.name.Contains("Quicksilver", StringComparison.Ordinal);
                        if (!isRadMat && !isBaseMat) continue;
                        var target = (isRadMat || rad) ? (RadiantLooks.QsRadiantChargedMat ?? Visuals.QsChargedMat) : Visuals.QsChargedMat;
                        if (target == null || m == target) continue;
                        _swaps.Add(new Swap { R = r, I = i, Orig = m, Applied = target });
                        mats[i] = target;
                        any = true;
                    }
                    if (any) r.sharedMaterials = mats;
                }
                return;
            }
            foreach (var sw in _swaps)
            {
                if (sw.R == null) continue;
                var mats = sw.R.sharedMaterials;
                if (mats == null || sw.I >= mats.Length || mats[sw.I] != sw.Applied) continue;   // re-applied by the game meanwhile
                mats[sw.I] = sw.Orig;
                sw.R.sharedMaterials = mats;
            }
            _swaps.Clear();
        }

        void Discharge()
        {
            var p = transform.position + Vector3.up * (0.6f * transform.lossyScale.x);
            Fx.Play("FX Hyper Slime Contact", p, 0.8f);
            Snd.Play(Snd.QsFizzle, p);
            if (_crackle != null) _crackle.Emit(10);
        }

        /// <summary>Silver (charged: electric-blue / radiant rose-plasma) streak while it moves fast; hysteresis so it doesn't stutter.</summary>
        void UpdateStreak(bool held, bool charged)
        {
            if (_streak == null) return;
            float v = _rb != null && !_rb.isKinematic ? _rb.linearVelocity.magnitude : 0f;
            bool on = !held && (_streakOn ? v > 2.5f : v > 4f);
            if (on != _streakOn) { _streakOn = on; Gfx.SetEmitting(_streak, on); }
            if (!on) return;
            bool rad = IsRadiant;
            _streak.startColor = rad ? (charged ? RadChargedHead : RadStreakHead) : (charged ? ChargedHead : StreakHead);
            _streak.endColor = rad ? (charged ? RadChargedTail : RadStreakTail) : (charged ? ChargedTail : StreakTail);
        }

        /// <summary>Dev: dash right now (away from the camera's right so it crosses the view).</summary>
        public void DevDash() => Dash(Charged, transform.position + Vector3.up * 0.6f);

        void Dash(bool charged, Vector3 center)
        {
            if (_rb == null || _rb.isKinematic) return;
            Vector3 dir;
            var pl = U.Player;
            if (!charged && pl != null && U.DistToPlayer(center) < 9f && U.Rng.NextDouble() < 0.7)
                dir = Quaternion.Euler(0f, U.Rand(-35f, 35f), 0f) * (transform.position - pl.transform.position);   // skittish: darts away
            else
                dir = Quaternion.Euler(0f, U.Rand(-50f, 50f), 0f) * transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            if (Cfg.QuicksilverRuns.Value)
            {
                // a burst of speed along the ground (the runner keeps it on its new heading for a moment)
                _yaw = YawOf(dir);
                _boostUntil = Time.time + 0.7f;
                _pauseUntil = 0f;
                _rb.AddForce(dir * (charged ? 6f : 4f), ForceMode.VelocityChange);
            }
            else _rb.AddForce(dir * (charged ? 10f : 7f) + Vector3.up * 2.2f, ForceMode.VelocityChange);
            if (!charged) return;
            Snd.Play(Snd.QsFizzle, transform.position);
            if (_crackle != null) _crackle.Emit(4);
            if (U.Rng.NextDouble() < 0.6)   // lightning zig-zag: a sharp turn mid-dash
            {
                float turn = U.Rand(55f, 95f) * (U.Rng.NextDouble() < 0.5 ? -1f : 1f);
                _zigDir = Quaternion.Euler(0f, turn, 0f) * dir;
                _zigAt = Time.time + U.Rand(0.18f, 0.3f);
            }
        }

        void Zig(bool posed)
        {
            _zigAt = -1f;
            if (posed || _rb == null) return;
            var v = _rb.linearVelocity;
            if (Cfg.QuicksilverRuns.Value) { _rb.linearVelocity = new Vector3(_zigDir.x * 9f, v.y, _zigDir.z * 9f); _yaw = YawOf(_zigDir); _boostUntil = Time.time + 0.4f; }
            else _rb.linearVelocity = _zigDir * 9f + Vector3.up * Mathf.Max(v.y, 1.4f);
            if (_crackle != null) _crackle.Emit(5);
        }
    }

    // =====================================================================================================
    /// <summary>
    /// SR1 GlitchSlime/GlitchImposto: takes the form of nearby slimes (but with the wrong face), flickers, and is debugged
    /// when vacuumed. Glitch bursts tear its pixel body into shifted rows / stripes / scan lines with mint colour flashes
    /// and a spray of pixels; now and then it blinks a few metres away (sometimes snapping back like lag). Water
    /// stabilises it for a while, the rancher close by makes it jittery (and its true form shows through a disguise more
    /// often), and in the vacuum it falls apart until it is debugged.
    /// </summary>
    public class GlitchSlime : MonoBehaviour
    {
        public GlitchSlime(IntPtr ptr) : base(ptr) { }

        static readonly List<GlitchSlime> All = new();
        public static bool DevNoDisguise;
        /// <summary>Dev: hold one body mask (0-2 pixel masks, 3+ tear masks; -1 = animate) with or without the colour flash.</summary>
        public static int DevMask = -1;
        public static bool DevFlash;
        static readonly Dictionary<(IntPtr, bool), SlimeAppearance> Disguises = new();
        static readonly HashSet<IntPtr> DisguisePtrs = new();
        static List<SlimeFace> _faces;

        public static readonly Dictionary<IntPtr, GlitchSlime> FleeNoHop = new();
        readonly List<IntPtr> _fleeNoHopKeys = new();

        SlimeAppearanceApplicator _app;
        SlimeAppearance _own, _disguise;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        Rigidbody _rb;
        bool _disguised, _debugged, _inBurst, _lastRadiant;
        float _nextDisguise, _disguiseUntil, _nextFlicker, _flickerEnd, _heldTime;
        float _nextBlink, _rubberAt = -1f, _calmUntil, _nextJitter, _nextWaterFx, _nextHeldFx;
        int _blinkFrame = -10;
        Vector3 _rubberPos;
        // caught + thrown -> smooth fast ground-hugging panic run
        float _fleeUntil, _nextFleeBlink, _fleePendingAt = -1f, _fleeSteerSide = 1f, _fleeLockUntil, _nextFleeFx;
        Vector3 _fleeDir;
        bool _wasHeld, _wasLaunched;
        public static float FleeTime = 10f, FleeHop = 11f, FleeLift = 0f, FleeSpeed = 14.5f, FleeAccel = 62f;
        public bool Fleeing => Time.time < _fleeUntil;
        bool IsRadiant => _own != null && RadiantLooks.IsRadiantAppearance(_own);

        ParticleSystem _pixels, _trail, _burst;
        TrailRenderer _ribbon, _ribbon2;
        static int _maskFrame = -1, _waterMask = -1;
        static float _nextMask, _nextStrip, _burstUntil, _nextBurst;
        static bool _wasBurst, _flashOn, _flashSaved, _flashOnRad, _flashSavedRad;

        static int WaterMask => _waterMask >= 0 ? _waterMask : (_waterMask = 1 << (LayerMask.NameToLayer("Water") >= 0 ? LayerMask.NameToLayer("Water") : 4));

        static readonly string[] FlashProps = { "_BlueTopColor", "_BlueMiddleColor", "_BlueBottomColor", "_GreenTopColor", "_GreenMiddleColor", "_GreenBottomColor" };
        static readonly Color[] FlashCols =
        {
            new(0.62f, 1f, 0.9f), new(0.5f, 1f, 0.86f), new(0.38f, 0.94f, 0.78f),       // light cells -> SR1 mint
            new(0.16f, 0.36f, 0.66f), new(0.12f, 0.28f, 0.58f), new(0.08f, 0.2f, 0.48f)  // dark cells -> deep blue
        };
        static readonly Color[] FlashColsRad =
        {
            new(1.00f, 0.98f, 0.55f), new(1.00f, 0.86f, 0.30f), new(1.00f, 0.70f, 0.16f), // light cells -> overdrive gold
            new(0.28f, 0.98f, 1.00f), new(0.16f, 0.85f, 1.00f), new(0.68f, 0.22f, 1.00f)  // dark cells -> cyber cyan/violet
        };
        static readonly Color[] _flashOrig = new Color[6];
        static readonly Color[] _flashOrigRad = new Color[6];

        public static bool Bursting => Time.time < _burstUntil;

        /// <summary>Glitch burst on every glitch body (they share one material): torn masks and colour flashes.</summary>
        public static void TriggerBurst(float seconds)
        {
            float end = Time.time + seconds;
            if (end > _burstUntil) _burstUntil = end;
            _nextMask = 0f;
        }

        static void ApplyMaskAndFlash(string matKey, Texture tex, bool wantFlash, ref bool flashOn, ref bool flashSaved, Color[] orig, Color[] cols)
        {
            if (!Visuals.Mats.TryGetValue(matKey, out var m) || m == null) return;
            if (tex != null) m.SetTexture("_ColorMask", tex);
            if (!flashSaved)
            {
                for (int i = 0; i < FlashProps.Length; i++) orig[i] = m.GetColor(FlashProps[i]);
                flashSaved = true;
            }
            if (wantFlash == flashOn) return;
            flashOn = wantFlash;
            for (int i = 0; i < FlashProps.Length; i++) m.SetColor(FlashProps[i], wantFlash ? cols[i] : orig[i]);
        }

        /// <summary>
        /// SR1's glitch shader made the pixel cells jump around: swap between a few generated cell masks, and every few
        /// seconds run a burst of torn masks (shifted rows, stripes, scan lines) with mint/blue (or gold/cyan) colour flashes.
        /// </summary>
        static void AnimateMask()
        {
            if (_maskFrame == Time.frameCount) return;
            _maskFrame = Time.frameCount;
            var masks = Visuals.GlitchMasks;
            if (masks == null || masks.Length == 0) return;
            var tears = Visuals.GlitchTears;
            int nt = tears != null ? tears.Length : 0;
            float now = Time.time;
            if (DevMask >= 0)
            {
                var t = DevMask < masks.Length ? masks[DevMask] : DevMask - masks.Length < nt ? tears[DevMask - masks.Length] : null;
                ApplyMaskAndFlash("Glitch", t, DevFlash, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
                ApplyMaskAndFlash("GlitchRadiant", t, DevFlash, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
                return;
            }
            if (now > _nextBurst)
            {
                _nextBurst = now + U.Rand(3f, 8f);
                TriggerBurst(U.Rand(0.15f, 0.4f));
            }
            if (now < _burstUntil && nt > 0)
            {
                _wasBurst = true;
                if (now < _nextMask) return;
                _nextMask = now + U.Rand(0.035f, 0.085f);
                var tex = U.Rng.NextDouble() < 0.75 ? tears[U.Rng.Next(nt)] : masks[U.Rng.Next(masks.Length)];
                bool flash = U.Rng.NextDouble() < 0.4;
                ApplyMaskAndFlash("Glitch", tex, flash, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
                ApplyMaskAndFlash("GlitchRadiant", tex, flash, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
                return;
            }
            if (_wasBurst)
            {
                _wasBurst = false;
                _nextMask = 0f;
                ApplyMaskAndFlash("Glitch", null, false, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
                ApplyMaskAndFlash("GlitchRadiant", null, false, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
            }
            if (now < _nextMask) return;
            bool quick = U.Rng.NextDouble() < 0.25;
            _nextMask = now + (quick ? U.Rand(0.05f, 0.12f) : U.Rand(0.4f, 1.6f));
            var normTex = masks[U.Rng.Next(masks.Length)];
            ApplyMaskAndFlash("Glitch", normTex, false, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
            ApplyMaskAndFlash("GlitchRadiant", normTex, false, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
        }

        /// <summary>SR1's trail shader re-randomised its pixel cells constantly: cycle the generated strips.</summary>
        static void AnimateStrip()
        {
            if (Time.time < _nextStrip) return;
            _nextStrip = Time.time + U.Rand(0.06f, 0.14f);
            var strips = Visuals.GlitchStrips;
            if (strips == null || strips.Length == 0) return;
            for (int i = 0; i < 2; i++)
            {
                if (!Visuals.Mats.TryGetValue("GlitchRibbon" + i, out var m) || m == null) continue;
                m.SetTexture("_MainTex", strips[U.Rng.Next(strips.Length)]);
                m.SetTextureOffset("_MainTex", new Vector2((float)U.Rng.NextDouble(), 0f));
            }
        }

        void SyncTrailPalette(bool rad)
        {
            Color p0 = rad ? new Color(1.00f, 0.86f, 0.24f, 1f) : new Color(0.35f, 1f, 0.9f, 1f);
            Color p1 = rad ? new Color(0.26f, 0.96f, 1.00f, 1f) : new Color(1f, 0.45f, 0.8f, 1f);
            foreach (var ps in new[] { _pixels, _burst, _trail })
            {
                if (ps == null) continue;
                try { var mm = ps.main; mm.startColor = new ParticleSystem.MinMaxGradient(p0, p1); } catch { }
            }
            if (_ribbon != null)
            {
                _ribbon.startColor = rad ? new Color(1.00f, 0.78f, 0.14f, 1f) : new Color(1f, 0.16f, 0.70f, 1f);
                _ribbon.endColor = rad ? new Color(1.00f, 0.32f, 0.72f, 0.1f) : new Color(0.85f, 0.2f, 1f, 0.1f);
            }
            if (_ribbon2 != null)
            {
                _ribbon2.startColor = rad ? new Color(0.28f, 0.98f, 1.00f, 1f) : new Color(0.25f, 1f, 0.40f, 1f);
                _ribbon2.endColor = rad ? new Color(0.55f, 0.22f, 1.00f, 0f) : new Color(0.3f, 1f, 0.9f, 0f);
            }
        }

        void Start()
        {
            All.Add(this);
            _app = GetComponent<SlimeAppearanceApplicator>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _rb = GetComponent<Rigidbody>();
            _own = _app != null ? _app.Appearance : null;
            _lastRadiant = IsRadiant;

            // the slime's origin is the body centre (body = -0.49..0.49): everything sits on / just behind the body
            _pixels = Gfx.Sparkles("GlitchPixels", transform, new Vector3(0f, PixelY, 0f), Visuals.PixelMat, new Color(0.35f, 1f, 0.9f, 1f), new Color(1f, 0.45f, 0.8f, 1f), 3.5f, 1.1f, 0.06f, 0.13f, 0.6f, 12, 0.15f);
            _burst = Gfx.Trail("GlitchBurst", transform, new Vector3(0f, PixelY, 0f), Visuals.PixelMat, new Color(0.35f, 1f, 0.9f, 1f), new Color(1f, 0.42f, 0.78f, 1f), 0f, 0.75f, 0.07f, 0.17f, 0.5f, 60);
            if (_burst != null) { var main = _burst.main; main.startSpeed = Gfx.Range(0.4f, 1.8f); }
            if (Cfg.GlitchTrail.Value)
            {
                float sc = Mathf.Max(0.3f, transform.lossyScale.x);
                _ribbon = Gfx.Ribbon("GlitchRibbon", transform, new Vector3(0f, RibbonY, 0f), Visuals.RibbonMat(0), RibbonTime, RibbonW * sc, RibbonW * 0.35f * sc,
                    new Color(1f, 0.16f, 0.70f, 1f), new Color(0.85f, 0.2f, 1f, 0.1f));
                _ribbon2 = Gfx.Ribbon("GlitchRibbonTop", transform, new Vector3(0f, Ribbon2Y, 0f), Visuals.RibbonMat(1), Ribbon2Time, Ribbon2W * sc, Ribbon2W * 0.25f * sc,
                    new Color(0.25f, 1f, 0.40f, 1f), new Color(0.3f, 1f, 0.9f, 0f));
                _trail = Gfx.Trail("GlitchTrail", transform, new Vector3(0f, TrailY, 0f), Visuals.PixelMat, new Color(0.35f, 1f, 0.9f, 1f), new Color(1f, 0.3f, 0.78f, 1f), 16f, 1.8f, 0.09f, 0.21f, 0.4f, 120);
            }
            if (_lastRadiant) SyncTrailPalette(true);
            try { foreach (var c in GetComponentsInChildren<SlimeRandomMove>(true)) { FleeNoHop[c.Pointer] = this; _fleeNoHopKeys.Add(c.Pointer); } } catch { }
            try { foreach (var c in GetComponentsInChildren<GotoConsumable>(true)) { FleeNoHop[c.Pointer] = this; _fleeNoHopKeys.Add(c.Pointer); } } catch { }
            float now = Time.time;
            _nextDisguise = now + U.Rand(4f, 10f);
            _nextBlink = now + U.Rand(8f, 20f);
            _nextJitter = now + U.Rand(1f, 3f);
        }

        void OnDestroy()
        {
            All.Remove(this);
            foreach (var k in _fleeNoHopKeys) FleeNoHop.Remove(k);
            try
            {
                if (All.Count == 0)
                {
                    if (_flashOn) ApplyMaskAndFlash("Glitch", null, false, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
                    if (_flashOnRad) ApplyMaskAndFlash("GlitchRadiant", null, false, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
                }
            }
            catch { }
        }

        // trail layout (local, origin = body centre): wide magenta ribbon through the lower body, narrow green one above it
        public static float RibbonY = -0.12f, Ribbon2Y = 0.2f, TrailY = -0.08f, PixelY = 0.05f;
        public static float RibbonTime = 0.9f, RibbonW = 0.95f, Ribbon2Time = 0.7f, Ribbon2W = 0.45f;

        /// <summary>Dev "gtrail": move / resize the trails of the live glitch slimes.</summary>
        public static void DevTrail()
        {
            foreach (var g in All)
            {
                if (g == null) continue;
                float sc = Mathf.Max(0.3f, g.transform.lossyScale.x);
                if (g._ribbon != null)
                {
                    g._ribbon.transform.localPosition = new Vector3(0f, RibbonY, 0f);
                    g._ribbon.time = RibbonTime; g._ribbon.startWidth = RibbonW * sc; g._ribbon.endWidth = RibbonW * 0.35f * sc;
                }
                if (g._ribbon2 != null)
                {
                    g._ribbon2.transform.localPosition = new Vector3(0f, Ribbon2Y, 0f);
                    g._ribbon2.time = Ribbon2Time; g._ribbon2.startWidth = Ribbon2W * sc; g._ribbon2.endWidth = Ribbon2W * 0.25f * sc;
                }
                if (g._trail != null) g._trail.transform.localPosition = new Vector3(0f, TrailY, 0f);
                if (g._pixels != null) g._pixels.transform.localPosition = new Vector3(0f, PixelY, 0f);
                if (g._burst != null) g._burst.transform.localPosition = new Vector3(0f, PixelY, 0f);
            }
        }

        internal static void ClearSession()
        {
            try
            {
                if (_flashOn) ApplyMaskAndFlash("Glitch", null, false, ref _flashOn, ref _flashSaved, _flashOrig, FlashCols);
                if (_flashOnRad) ApplyMaskAndFlash("GlitchRadiant", null, false, ref _flashOnRad, ref _flashSavedRad, _flashOrigRad, FlashColsRad);
            }
            catch { }
            _flashOn = false;
            _flashOnRad = false;
            FleeNoHop.Clear();
            All.Clear();
        }

        public static void ReapplyAll()
        {
            foreach (var g in All.ToArray())
            {
                if (g == null || g._app == null) continue;
                if (!g._disguised) g._own = g._app.Appearance;
                if (g._own != null && !g._disguised) g.Apply(g._own);
            }
        }

        public static void RevertAllForSave()
        {
            foreach (var g in All.ToArray())
                if (g != null && g._disguised) g.Reveal(false);
        }

        void Update()
        {
            try { AnimateMask(); } catch { }
            if (_app != null && _app.Appearance != null && !DisguisePtrs.Contains(_app.Appearance.Pointer) && _app.Appearance != _own)
            {
                _own = _app.Appearance;
                _disguised = false;
            }
            if (_own == null && _app != null && !_disguised)
                _own = _app.Appearance ?? SlimeFactory.Spec("Glitch")?.Def?.AppearancesDefault?[0];
            if (_app == null || _own == null) return;
            bool rad = IsRadiant;
            if (rad != _lastRadiant) { _lastRadiant = rad; SyncTrailPalette(rad); }
            float now = Time.time;
            bool held = _vac != null && _vac.IsHeld();
            bool calm = now < _calmUntil || (_calm != null && _calm.IsCalmed());
            bool show = !held && !_disguised && Time.frameCount > _blinkFrame + 1;   // no streak across a blink
            Gfx.SetEmitting(_pixels, show);
            Gfx.SetEmitting(_trail, show);
            Gfx.SetEmitting(_ribbon, show);
            Gfx.SetEmitting(_ribbon2, show);
            if (_ribbon != null) AnimateStrip();
            bool burst = Bursting;
            if (!calm && burst && !_inBurst && !held && !_disguised && _pixels != null) _pixels.Emit(U.Rng.Next(3, 8));
            _inBurst = burst;
            if (held)
            {
                _rubberAt = -1f;
                _wasHeld = true;
                _fleeUntil = 0f;
                if (_disguised) Reveal(true);
                _heldTime += Time.deltaTime;
                // falling apart in the vacuum: constant tearing and a pixel spray until it is debugged
                TriggerBurst(0.1f);
                if (now > _nextHeldFx) { _nextHeldFx = now + 0.07f; if (_burst != null) _burst.Emit(2); }
                if (_heldTime > 1.2f && !_debugged && CoopCompat.IsHeldByLocalVac(_vac)) Debug();
                return;
            }
            _heldTime = 0f;
            // caught (its disguise blown) and thrown or let go: it panics and runs off as soon as it lands
            bool launched = false;
            try { launched = _vac != null && _vac.IsLaunched(); } catch { }
            bool thrown = launched && !_wasLaunched;
            _wasLaunched = launched;
            if ((_wasHeld || thrown) && Cfg.GlitchFlees.Value && _fleePendingAt < 0f) _fleePendingAt = now;
            _wasHeld = false;
            if (_fleePendingAt >= 0f && now - _fleePendingAt > 0.25f && _rb != null && !_rb.isKinematic && U.Grounded(transform, Mathf.Max(0.3f, transform.lossyScale.x)))
                StartFlee();
            if (now < _fleeUntil) { Flee(now); return; }
            if (_rubberAt > 0f && now > _rubberAt)   // lag: snaps back to where it blinked from
            {
                _rubberAt = -1f;
                if (!_disguised && !launched && _rb != null && !_rb.isKinematic) Jump(_rubberPos, false);
            }
            if (!Cfg.GlitchDisguise.Value || DevNoDisguise) { if (_disguised) Reveal(false); }
            else if (calm) { if (_disguised) Reveal(true); }
            else if (!_disguised) { if (now > _nextDisguise) Disguise(); }
            else UpdateDisguise(now);
            if (_disguised || calm) return;
            // jittery with the rancher close by: extra little bursts
            float dp = CoopCompat.DistToNearestPlayer(transform.position + Vector3.up * 0.5f);
            if (dp < 5f && now > _nextJitter)
            {
                _nextJitter = now + U.Rand(0.8f, 2.2f);
                TriggerBurst(U.Rand(0.08f, 0.16f));
                if (_pixels != null) _pixels.Emit(U.Rng.Next(2, 5));
            }
            if (now > _nextBlink && dp < 60f) Blink(false);
        }

        void UpdateDisguise(float now)
        {
            if (_flickerEnd > 0f)
            {
                if (now > _flickerEnd) { _flickerEnd = 0f; Apply(_disguise); }
            }
            else if (now > _nextFlicker)
            {
                // the true form shows through now and then - much more often with the rancher close by
                bool near = CoopCompat.DistToNearestPlayer(transform.position + Vector3.up * 0.5f) < 5f;
                _nextFlicker = now + (near ? U.Rand(0.8f, 2.5f) : U.Rand(3f, 9f));
                _flickerEnd = now + (near ? U.Rand(0.12f, 0.25f) : 0.12f);
                Apply(_own);
                if (_burst != null) _burst.Emit(near ? 6 : 3);
                TriggerBurst(_flickerEnd - now);
            }
            if (now > _disguiseUntil) Reveal(false);
        }

        void Apply(SlimeAppearance a)
        {
            if (a == null || _app == null) return;
            try { _app.Appearance = a; _app.ApplyAppearance(); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Glitch apply: " + e.Message); }
        }

        public void ForceDisguise() => Disguise();

        void Disguise()
        {
            _nextDisguise = Time.time + U.Rand(6f, 15f);
            var target = PickTarget();
            if (target == null) return;
            _disguise = target;
            _disguised = true;
            _rubberAt = -1f;
            _disguiseUntil = Time.time + U.Rand(25f, 60f);
            _nextFlicker = Time.time + U.Rand(2f, 6f);
            Fx.Play("FX Despawn", transform.position + Vector3.up * 0.5f, 0.6f);
            Snd.Play(Snd.GlitchSplit, transform.position);
            if (_burst != null) _burst.Emit(14);
            Apply(_disguise);
        }

        public void Reveal(bool fx)
        {
            _disguised = false;
            _flickerEnd = 0f;
            _nextDisguise = Time.time + U.Rand(8f, 20f);
            if (fx)
            {
                Fx.Play("FX Despawn", transform.position + Vector3.up * 0.5f, 0.6f);
                Snd.Play(Snd.GlitchSplit, transform.position);
                if (_burst != null) _burst.Emit(12);
                TriggerBurst(0.3f);
            }
            Apply(_own);
        }

        /// <summary>Panic: sprints smoothly away over clear terrain, skirting rocks and cliffs without hopping into the air.</summary>
        public void StartFlee()
        {
            float now = Time.time;
            _fleePendingAt = -1f;
            _fleeUntil = now + FleeTime;
            _nextFleeBlink = now + U.Rand(1.1f, 2.0f);
            _fleeSteerSide = U.Rng.NextDouble() < 0.5 ? -1f : 1f;
            _fleeLockUntil = 0f;
            _fleeDir = Vector3.zero;
            _rubberAt = -1f;
            _nextDisguise = Mathf.Max(_nextDisguise, _fleeUntil + U.Rand(4f, 10f));
            _nextBlink = Mathf.Max(_nextBlink, _fleeUntil + U.Rand(4f, 10f));
            if (_disguised) Reveal(false);
            if (Cfg.Diagnostics.Value) { var pl = U.Player; ClassicSlimesMod.Log.Msg($"Glitch flee {name}: player {(pl != null ? Vector3.Distance(pl.transform.position, transform.position).ToString("0.0") : "-")} m"); }
            Snd.Play(Snd.GlitchSplit, transform.position);
            if (_burst != null) _burst.Emit(12);
            TriggerBurst(0.3f);
        }

        static readonly float[] SteerAngles = { 30f, 55f, 80f, 110f, 140f, 165f };

        static bool CanFleeAlong(Vector3 origin, Vector3 dir, float refGroundY, float s, float dist)
        {
            if (Physics.SphereCast(origin + Vector3.up * (0.38f * s), 0.26f * s, dir, out var wallHit, dist, U.GroundMask, QueryTriggerInteraction.Ignore)
                && wallHit.normal.y < 0.82f)
                return false;
            foreach (float mul in new[] { 0.55f, 1.0f })
            {
                var probe = origin + dir * (dist * mul) + Vector3.up * (1.1f * s);
                if (!Physics.Raycast(probe, Vector3.down, out var gHit, 4.2f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) return false;
                if (gHit.normal.y < 0.80f) return false;
                if (gHit.point.y - refGroundY > 0.48f * s) return false;
                if (Physics.Raycast(probe, Vector3.down, out var wHit, gHit.distance, WaterMask, QueryTriggerInteraction.Collide)
                    && (gHit.distance - wHit.distance > 0.35f * s))
                    return false;
            }
            return true;
        }

        static bool SafeFlatGround(Vector3 around, Vector3 dir, float minR, float maxR, float spreadDeg, float originY, float s, out Vector3 point)
        {
            dir.y = 0f;
            bool random = dir.sqrMagnitude < 1e-4f;
            if (!random) dir.Normalize();
            var lift = Vector3.up * (0.5f * s);
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = random
                    ? Quaternion.Euler(0f, U.Rand(0f, 360f), 0f) * Vector3.forward
                    : Quaternion.Euler(0f, U.Rand(-spreadDeg, spreadDeg), 0f) * dir;
                var probe = around + d * U.Rand(minR, maxR) + Vector3.up * (2.0f * s);
                if (Physics.Linecast(around + lift, probe, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 6.5f * s, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.82f || hit.point.y > originY + 0.55f * s) continue;
                if (Physics.Linecast(around + lift, hit.point + lift, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(probe, Vector3.down, out var wHit, hit.distance, WaterMask, QueryTriggerInteraction.Collide)
                    && (hit.distance - wHit.distance > 0.35f * s))
                    continue;
                point = hit.point;
                return true;
            }
            point = around;
            return false;
        }

        void FixedUpdate()
        {
            float now = Time.time;
            if (now >= _fleeUntil || _rb == null || _rb.isKinematic) return;
            if (_vac != null && (_vac.IsHeld() || _vac.IsLaunched())) return;
            if (CoopCompat.IsCoop && !CoopCompat.IsLocallySimulated(_rb)) return;

            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            var pos = transform.position;
            float dt = Time.fixedDeltaTime;
            bool grounded = Physics.Raycast(pos + Vector3.up * (0.42f * s), Vector3.down, out var gHit, 1.45f * s, U.GroundMask, QueryTriggerInteraction.Ignore);
            float groundY = grounded ? gHit.point.y : pos.y - 0.5f * s;

            var pl = CoopCompat.NearestPlayer(pos, out var nearPlPos, out _);
            var away = pl != null ? pos - nearPlPos : transform.forward;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;

            // Already pushed onto a boulder/cliff face (>38.7 deg): slide immediately downhill off the rock.
            if (grounded && gHit.normal.y < 0.78f)
            {
                var down = Vector3.ProjectOnPlane(Vector3.down, gHit.normal);
                down.y = 0f;
                if (down.sqrMagnitude > 1e-4f)
                {
                    _fleeDir = (down.normalized * 0.75f + away * 0.25f).normalized;
                    _fleeLockUntil = now + 0.2f;
                }
            }
            else
            {
                float lookDist = 2.8f * s;
                if (now >= _fleeLockUntil || _fleeDir.sqrMagnitude < 0.25f || !CanFleeAlong(pos, _fleeDir, groundY, s, lookDist))
                {
                    if (CanFleeAlong(pos, away, groundY, s, lookDist))
                    {
                        _fleeDir = away;
                    }
                    else
                    {
                        bool found = false;
                        foreach (float ang in SteerAngles)
                        {
                            foreach (float sign in new[] { _fleeSteerSide, -_fleeSteerSide })
                            {
                                var cand = Quaternion.Euler(0f, sign * ang, 0f) * away;
                                if (!CanFleeAlong(pos, cand, groundY, s, lookDist)) continue;
                                _fleeDir = cand;
                                _fleeSteerSide = sign;
                                _fleeLockUntil = now + 0.32f;
                                found = true;
                                break;
                            }
                            if (found) break;
                        }
                        if (!found) { _fleeDir = -away; _fleeLockUntil = now + 0.25f; }
                    }
                }
            }

            if (_fleeDir.sqrMagnitude < 1e-4f) _fleeDir = away;

            if (grounded)
            {
                var tang = Vector3.ProjectOnPlane(_fleeDir, gHit.normal);
                tang = tang.sqrMagnitude > 1e-4f ? tang.normalized : _fleeDir;
                if (tang.y > 0.16f) { tang.y = 0.16f; tang.Normalize(); }
                var targetVel = tang * FleeSpeed;
                var nv = Vector3.MoveTowards(_rb.linearVelocity, targetVel, FleeAccel * dt);
                nv.y = Mathf.Min(nv.y, Mathf.Max(0.12f, targetVel.y));
                _rb.linearVelocity = nv;
                _rb.AddForce(-gHit.normal * 22f, ForceMode.Acceleration);
            }
            else
            {
                var v = _rb.linearVelocity;
                var horiz = Vector2.MoveTowards(new Vector2(v.x, v.z), new Vector2(_fleeDir.x, _fleeDir.z) * (FleeSpeed * 0.8f), 18f * dt);
                _rb.linearVelocity = new Vector3(horiz.x, Mathf.Min(v.y, 0.15f), horiz.y);
                _rb.AddForce(Vector3.down * 24f, ForceMode.Acceleration);
            }
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(_fleeDir, Vector3.up), 16f * dt));
        }

        void Flee(float now)
        {
            if (_rb == null || _rb.isKinematic) return;
            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            var pos = transform.position;
            var pl = CoopCompat.NearestPlayer(pos, out var nearPlPos, out _);
            if (pl != null && Vector3.Distance(pos, nearPlPos) > 34f && now > _fleeUntil - FleeTime + 1.5f) { _fleeUntil = now; return; }
            if (now > _nextFleeFx)
            {
                _nextFleeFx = now + U.Rand(0.14f, 0.24f);
                if (_pixels != null) _pixels.Emit(U.Rng.Next(2, 5));
                if (U.Rng.NextDouble() < 0.4) TriggerBurst(U.Rand(0.06f, 0.14f));
            }
            if (now > _nextFleeBlink && U.Grounded(transform, s))
            {
                _nextFleeBlink = now + U.Rand(1.4f, 2.4f);
                var away = _fleeDir.sqrMagnitude > 0.25f ? _fleeDir : (pl != null ? pos - nearPlPos : transform.forward);
                if (SafeFlatGround(pos, away, 4.0f * s, 7.5f * s, 32f, pos.y, s, out var to))
                    Jump(to, true);
            }
        }

        /// <summary>Water stabilises it: reveals a disguise, then no disguises or blinks for a while.</summary>
        public void OnWater()
        {
            float now = Time.time;
            _calmUntil = now + 10f;
            _fleeUntil = 0f;
            _fleePendingAt = -1f;
            _rubberAt = -1f;
            _nextDisguise = Mathf.Max(_nextDisguise, _calmUntil);
            if (_disguised) { Reveal(true); return; }
            if (now < _nextWaterFx) return;
            _nextWaterFx = now + 0.5f;
            if (_pixels != null) _pixels.Emit(6);
            TriggerBurst(0.2f);
        }

        /// <summary>Teleport glitch: vanishes in a pixel burst and pops up a few metres away; sometimes it lags back.</summary>
        public void Blink(bool dev)
        {
            float now = Time.time;
            _nextBlink = now + U.Rand(10f, 25f);
            if (_rb == null || (_rb.isKinematic && !dev) || (_vac != null && _vac.IsHeld())) return;
            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            if (!dev && !U.Grounded(transform, s)) return;
            var from = transform.position;
            if (dev)
            {
                var lift = Vector3.up * (0.5f * s);
                for (int i = 0; i < 4; i++)
                {
                    if (!U.GroundPoint(from, 2f * s, 5f * s, out var to)) continue;
                    if (Physics.Linecast(from + lift, to + lift, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    Jump(to, true);
                    if (U.Rng.NextDouble() < 0.3) { _rubberPos = from; _rubberAt = now + U.Rand(0.3f, 0.45f); }
                    return;
                }
                return;
            }
            if (SafeFlatGround(from, Vector3.zero, 2f * s, 5f * s, 180f, from.y, s, out var flatTo))
            {
                Jump(flatTo, true);
                if (U.Rng.NextDouble() < 0.3) { _rubberPos = from; _rubberAt = now + U.Rand(0.3f, 0.45f); }
            }
        }

        void Jump(Vector3 to, bool big)
        {
            float s = Mathf.Max(0.3f, transform.lossyScale.x);
            var from = transform.position;
            if (_burst != null) _burst.Emit(big ? 16 : 9);
            Fx.Play("FX Despawn", from + Vector3.up * (0.5f * s), big ? 0.45f : 0.3f);
            Snd.Play(Snd.GlitchSplit, from);
            Gfx.SetEmitting(_trail, false);
            Gfx.SetEmitting(_ribbon, false);
            Gfx.SetEmitting(_ribbon2, false);
            to += Vector3.up * (0.5f * s + 0.05f);
            if (_rb != null) { _rb.position = to; if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero; }
            transform.position = to;
            _blinkFrame = Time.frameCount;
            if (_burst != null) _burst.Emit(big ? 10 : 6);
            TriggerBurst(big ? 0.3f : 0.18f);
        }

        void Debug()
        {
            _debugged = true;
            bool rad = IsRadiant;
            var p = transform.position + Vector3.up * 0.5f;
            if (rad)
            {
                var bigFx = Fx.Play("fxNewbuckExplosion_Large", p) ?? Fx.Play("fxNewbuckExplosion_Med", p) ?? Fx.Play("fxNewbuckExplosion_small", p);
                if (bigFx == null) Fx.Play("fxNewbuckExplosion_small", p, 1.5f);
            }
            else Fx.Play("fxNewbuckExplosion_small", p);
            Fx.Play("FX Despawn", p);
            Snd.Play(Snd.GlitchSplit, p);
            if (_burst != null)   // the last pixels stay behind
            {
                try { _burst.Emit(30); _burst.transform.SetParent(null, true); Destroy(_burst.gameObject, 1.5f); } catch { }
                _burst = null;
            }
            try
            {
                int reward = Cfg.GlitchDebugReward.Value * (rad ? 4 : 1);
                if (reward > 0) SceneContext.Instance.PlayerState.AddCurrency(CurrencyDefinitionExtensions.GetDefaultCurrencyIfNull(null), reward, true, null);
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Glitch reward: " + e.Message); }
            try { Destroyer.DestroyActor(gameObject, "ClassicSlimes.GlitchDebugged", false); }
            catch { Destroy(gameObject); }
        }

        bool BonesOk(SlimeAppearance a)
        {
            try
            {
                var lookup = _app._boneLookup;
                foreach (var st in a._structures)
                {
                    if (st?.Element?.Prefabs == null) continue;
                    foreach (var o in st.Element.Prefabs)
                    {
                        if (o == null) continue;
                        if (o.RootBone != SlimeAppearance.SlimeBone.NONE && !lookup.ContainsKey(o.RootBone)) return false;
                        if (o.ParentBone != SlimeAppearance.SlimeBone.NONE && !lookup.ContainsKey(o.ParentBone)) return false;
                        if (o.AttachedBones != null) foreach (var b in o.AttachedBones) if (!lookup.ContainsKey(b)) return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        SlimeAppearance PickTarget()
        {
            bool rad = IsRadiant;
            var candidates = new List<SlimeAppearance>();
            foreach (var c in Physics.OverlapSphere(transform.position, 25f, U.SlimeMask, QueryTriggerInteraction.Ignore))
            {
                var rb = c != null ? c.attachedRigidbody : null;
                if (rb == null || rb.gameObject == gameObject || rb.GetComponent<GlitchSlime>() != null) continue;
                var ap = rb.GetComponent<SlimeAppearanceApplicator>();
                var sd = ap != null ? ap.SlimeDefinition : null;
                var a = ap != null ? ap.Appearance : null;
                if (a == null) continue;
                if (rad && !RadiantLooks.IsRadiantAppearance(a) && sd != null)
                    a = sd.RadiantBase ?? sd.RadiantLargo0 ?? a;
                else if (!rad && RadiantLooks.IsRadiantAppearance(a) && sd?.AppearancesDefault != null && sd.AppearancesDefault.Length > 0)
                    a = sd.AppearancesDefault[0];
                if (a != null && !candidates.Contains(a)) candidates.Add(a);
            }
            if (candidates.Count == 0)
            {
                foreach (var n in new[] { "Pink", "Tabby", "Phosphor", "Rock", "Cotton", "Honey", "Boom", "Crystal", "Batty", "Angler", "Ringtail", "Hunter" })
                {
                    try
                    {
                        var d = CottonLibrary.Library.GetSlime(n);
                        if (d == null) continue;
                        var app = rad ? (d.RadiantBase ?? d.AppearancesDefault?[0]) : d.AppearancesDefault?[0];
                        if (app != null) candidates.Add(app);
                    }
                    catch { }
                }
            }
            for (int tries = 0; tries < 6 && candidates.Count > 0; tries++)
            {
                var a = candidates[U.Rng.Next(candidates.Count)];
                if (!BonesOk(a)) { candidates.Remove(a); continue; }
                return DisguiseOf(a, rad);
            }
            return null;
        }

        static SlimeAppearance DisguiseOf(SlimeAppearance a, bool radiant)
        {
            if (Disguises.TryGetValue((a.Pointer, radiant), out var d) && d != null) return d;
            d = Object.Instantiate(a);
            d.name = (radiant ? "GlitchRadDisguise_" : "GlitchDisguise_") + a.name;
            d.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!radiant) d._appearType = SlimeAppearance.AppearanceType.DEFAULT;
            var face = WrongFace(a._face);
            if (face != null) d._face = face;
            Disguises[(a.Pointer, radiant)] = d;
            DisguisePtrs.Add(d.Pointer);
            return d;
        }

        static SlimeFace WrongFace(SlimeFace actual)
        {
            if (_faces == null)
            {
                _faces = new List<SlimeFace>();
                foreach (var f in Resources.FindObjectsOfTypeAll<SlimeFace>()) if (f != null && !_faces.Contains(f)) _faces.Add(f);
            }
            var options = _faces.FindAll(f => f != null && f != actual);
            return options.Count > 0 ? options[U.Rng.Next(options.Count)] : null;
        }
    }
}
