using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    public static class Behaviours
    {
        public static void Register()
        {
            ClassInjector.RegisterTypeInIl2Cpp<RadSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<QuantumGhost>();
            ClassInjector.RegisterTypeInIl2Cpp<QuantumSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<MosaicGlint>();
            ClassInjector.RegisterTypeInIl2Cpp<MosaicSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<QuicksilverSlime>();
            ClassInjector.RegisterTypeInIl2Cpp<GlitchSlime>();
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

        const float AuraScale = 3.2f;          // SR1 RadAura transform scale (unit sphere)
        const float ExpandFactor = 1.5f, ExpandingTime = 3f, ExpandedTime = 10f, CalmingTime = 0.2f;

        GameObject _aura, _core;
        SECTR_PointSource _loop;
        SlimeEmotions _emo;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        PlayerRadAbsorber _absorber;
        float _factor = 1f, _nextExpand, _expandedUntil, _fallbackTick;

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _aura = Visuals.Sphere("RadAura", transform, new Vector3(0f, 0.5f, 0f), AuraScale, Visuals.RadAuraMat);
            _core = Visuals.Sphere("RadCore", transform, new Vector3(0f, 0.5f, 0f), AuraScale * 0.42f, Visuals.RadCoreMat);
            _loop = Snd.Loop(gameObject, Snd.RadAura);
            _nextExpand = Time.time + Delay();
        }

        float Delay() => Mathf.Lerp(30f, 180f, 1f - U.Agitation(_emo));

        void Update()
        {
            float dt = Time.deltaTime;
            bool calmed = _calm != null && _calm.IsCalmed();
            bool held = _vac != null && _vac.IsHeld();
            if (calmed) _nextExpand += dt;
            else if (Time.time >= _nextExpand && Time.time >= _expandedUntil)
            {
                _expandedUntil = Time.time + ExpandingTime + ExpandedTime;
                _nextExpand = _expandedUntil + Delay();
            }
            float target = calmed ? 0f : (Time.time < _expandedUntil ? ExpandFactor : 1f);
            float rate = (target < 1f || _factor < 1f) ? 1f / CalmingTime : (ExpandFactor - 1f) / ExpandingTime;
            _factor = Mathf.MoveTowards(_factor, target, rate * dt);

            bool show = !held && _factor > 0.02f;
            if (_aura != null)
            {
                if (_aura.activeSelf != show) _aura.SetActive(show);
                if (show) _aura.transform.localScale = Vector3.one * (AuraScale * _factor);
            }
            if (_core != null)
            {
                if (_core.activeSelf != show) _core.SetActive(show);
                if (show) _core.transform.localScale = Vector3.one * (AuraScale * 0.42f * Mathf.Max(_factor, 0.4f));
            }
            Snd.SetPlaying(_loop, show);

            if (!show || !Cfg.RadAuraDamagesPlayer.Value) return;
            float radius = AuraScale * _factor * transform.lossyScale.x;
            var center = transform.position + Vector3.up * (0.5f * transform.lossyScale.y);
            if (U.DistToPlayer(center) > radius + 0.4f) return;
            if (_absorber == null)
            {
                var p = U.Player;
                if (p != null) _absorber = p.GetComponent<PlayerRadAbsorber>() ?? p.GetComponentInChildren<PlayerRadAbsorber>();
            }
            if (_absorber != null) _absorber.Absorb(gameObject, Cfg.RadPerSecond.Value * dt);
            else if (Time.time > _fallbackTick) { _fallbackTick = Time.time + 2f; Dmg.Player(Cfg.RadDamagePerTick.Value, gameObject, "RadAbsorb", "SlimeAttack"); }
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
        float _nextGhost, _nextTeleport;

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _rb = GetComponent<Rigidbody>();
            _nextGhost = Time.time + U.Rand(3f, 8f);
            _nextTeleport = Time.time + TeleportDelay();
        }

        float GhostDelay() => Mathf.Lerp(20f, 5f, U.Agitation(_emo));
        float TeleportDelay() => Mathf.Lerp(30f, 5f, U.Agitation(_emo));

        void Update()
        {
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
                if (_ghosts.Count < MaxGhosts && U.Grounded(transform, s) && U.GroundPoint(transform.position, 2f * s, 10f, out var p))
                    SpawnGhost(p);
            }
            if (Time.time >= _nextTeleport)
            {
                _nextTeleport = Time.time + TeleportDelay();
                if (U.Agitation(_emo) > 0.1f && _ghosts.Count > 0) TeleportTo(_ghosts[U.Rng.Next(_ghosts.Count)]);
            }
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
            var from = transform.position + Vector3.up * 0.5f;
            var to = g.transform.position + Vector3.up * 0.05f;
            Fx.Play("FX Despawn", from);
            Snd.Play(Snd.QuantumWarp, from);
            if (_rb != null) { _rb.position = to; _rb.linearVelocity = Vector3.zero; }
            transform.position = to;
            Fx.Play("FX Slime Spawn", to + Vector3.up * 0.5f);
            Snd.Play(Snd.QuantumWarp, to);
            _ghosts.Remove(g);
            g.Pop(false);
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
        }
    }

    public class QuantumGhost : MonoBehaviour
    {
        public QuantumGhost(IntPtr ptr) : base(ptr) { }

        public QuantumSlime Owner;
        float _born, _life, _phase, _nextTurn;
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
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 2.5f, U.GroundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            else { _vel = -_vel; p = transform.position; }
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
        readonly List<MosaicGlint> _glints = new();
        SlimeEmotions _emo;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        SECTR_PointSource _loop;
        double _nextGlint;
        float _nextPull;

        void Start()
        {
            _emo = GetComponent<SlimeEmotions>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _loop = Snd.Loop(gameObject, Snd.MosaicAura);
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td != null) _nextGlint = td.HoursFromNow(U.Rand(0.05f, 0.4f));
        }

        void Update()
        {
            _glints.RemoveAll(g => g == null);
            bool day = U.IsDay();
            bool held = _vac != null && _vac.IsHeld();
            Snd.SetPlaying(_loop, day && !held);
            if (day && !held && Time.time > _nextPull) { _nextPull = Time.time + 1.5f; PullSlimes(); }
            if (!Cfg.MosaicGlints.Value || held) return;
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td == null || !td.HasReached(_nextGlint)) return;
            float agit = U.Agitation(_emo);
            _nextGlint = td.HoursFromNow(0.5f * (1f - 0.8f * agit));
            bool calmed = _calm != null && _calm.IsCalmed();
            if (day && !calmed && _glints.Count < MaxGlints) SpawnGlint(Mathf.Lerp(7.5f, 30f, agit) * 0.5f);
        }

        public void SpawnGlint(float radius)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = U.Rand(0f, Mathf.PI * 2f), r = U.Rand(1.5f, Mathf.Max(2f, radius));
                var p = transform.position + new Vector3(Mathf.Cos(a) * r, U.Rand(2.5f, 4.5f), Mathf.Sin(a) * r);
                if (Physics.Linecast(transform.position + Vector3.up, p, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                var g = Visuals.MakeGlint(this, p);
                if (g != null) { _glints.Add(g); Snd.Play(Snd.GlintSpawn, p); }
                return;
            }
        }

        void PullSlimes()
        {
            var cols = Physics.OverlapSphere(transform.position, 9f, U.SlimeMask, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<IntPtr>();
            foreach (var c in cols)
            {
                var rb = c != null ? c.attachedRigidbody : null;
                if (rb == null || rb.gameObject == gameObject || !seen.Add(rb.Pointer)) continue;
                if (rb.GetComponent<SlimeEat>() == null || rb.GetComponent<MosaicSlime>() != null) continue;
                var v = rb.GetComponent<Vacuumable>();
                if (v != null && v.IsHeld()) continue;
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

        void OnDestroy()
        {
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
            _fx = Fx.Attach("gold_sparkles", gameObject, Vector3.zero);
            _core = transform.childCount > 0 ? transform.GetChild(0).gameObject : null;
        }

        void Update()
        {
            var td = SceneContext.Instance != null ? SceneContext.Instance.TimeDirector : null;
            if (td == null) return;
            float t = Time.time + _phase;
            _spin += Time.deltaTime * (_state == State.Suspended ? 40f : 160f);
            transform.rotation = Quaternion.Euler(0f, _spin, 0f);
            float pulse = _state == State.Suspended ? 1f + Mathf.Sin(t * 2f) * 0.12f : 1.25f + Mathf.Sin(t * 9f) * 0.25f;
            if (_core != null) _core.transform.localScale = Vector3.one * 0.25f * pulse;

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
            Snd.Play(Snd.GlintBlast, p);
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
    /// <summary>Mochi's quicksilver slimes: lightning-quick dashes; eating energy (rad plorts) overcharges them so touching them zaps.</summary>
    public class QuicksilverSlime : MonoBehaviour
    {
        public QuicksilverSlime(IntPtr ptr) : base(ptr) { }

        Rigidbody _rb;
        Vacuumable _vac;
        SECTR_PointSource _loop;
        float _chargedUntil, _nextDash, _nextZap, _nextSpark;

        public bool Charged => Time.time < _chargedUntil;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _vac = GetComponent<Vacuumable>();
            _loop = Snd.Loop(gameObject, Snd.QsAura);
            _nextDash = Time.time + U.Rand(1f, 4f);
        }

        public void Overcharge(float seconds)
        {
            _chargedUntil = Mathf.Max(_chargedUntil, Time.time) + seconds;
            var p = transform.position + Vector3.up * 0.6f;
            Fx.Play("FX HyperSlimeCharge", p);
            Snd.Play(Snd.QsHit, p);
        }

        void Update()
        {
            bool held = _vac != null && _vac.IsHeld();
            bool charged = Charged;
            Snd.SetPlaying(_loop, charged && !held);
            if (held) return;
            float s = transform.lossyScale.x;
            var center = transform.position + Vector3.up * (0.6f * s);
            if (Time.time > _nextDash)
            {
                _nextDash = Time.time + U.Rand(2.5f, 6f) * (charged ? 0.5f : 1f);
                if (U.Grounded(transform, s)) Dash(charged);
            }
            if (!charged) return;
            if (Time.time > _nextSpark)
            {
                _nextSpark = Time.time + U.Rand(0.35f, 1.1f);
                Fx.Play("FX Hyper Slime Contact", center + UnityEngine.Random.insideUnitSphere * 0.35f * s, 0.6f);
            }
            if (Time.time > _nextZap && U.DistToPlayer(center) < 1.7f * s)
            {
                _nextZap = Time.time + 1.25f;
                if (Dmg.Player(Cfg.QuicksilverShockDamage.Value, gameObject, "LightningStrike", "SlimeAttack"))
                {
                    Snd.Play(Snd.QsHit, center);
                    var pl = U.Player;
                    if (pl != null) Fx.Play("FX Hyper Slime Contact", pl.transform.position + Vector3.up);
                }
            }
        }

        void Dash(bool charged)
        {
            if (_rb == null) return;
            var dir = Quaternion.Euler(0f, U.Rand(-50f, 50f), 0f) * transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            _rb.AddForce(dir.normalized * (charged ? 10f : 7f) + Vector3.up * 2.2f, ForceMode.VelocityChange);
            if (charged) Snd.Play(Snd.QsFizzle, transform.position);
        }
    }

    // =====================================================================================================
    /// <summary>SR1 GlitchSlime/GlitchImposto: takes the form of nearby slimes (but with the wrong face), flickers, and is debugged when vacuumed.</summary>
    public class GlitchSlime : MonoBehaviour
    {
        public GlitchSlime(IntPtr ptr) : base(ptr) { }

        static readonly List<GlitchSlime> All = new();
        static readonly Dictionary<IntPtr, SlimeAppearance> Disguises = new();
        static List<SlimeFace> _faces;

        SlimeAppearanceApplicator _app;
        SlimeAppearance _own, _disguise;
        Vacuumable _vac;
        CalmedByWaterSpray _calm;
        bool _disguised, _debugged;
        float _nextDisguise, _disguiseUntil, _nextFlicker, _flickerEnd, _heldTime;

        void Start()
        {
            All.Add(this);
            _app = GetComponent<SlimeAppearanceApplicator>();
            _vac = GetComponent<Vacuumable>();
            _calm = GetComponent<CalmedByWaterSpray>();
            _own = _app != null ? _app.Appearance : null;
            _nextDisguise = Time.time + U.Rand(4f, 10f);
        }

        void OnDestroy() => All.Remove(this);

        public static void RevertAllForSave()
        {
            foreach (var g in All.ToArray())
                if (g != null && g._disguised) g.Reveal(false);
        }

        void Update()
        {
            if (_app == null || _own == null) return;
            bool held = _vac != null && _vac.IsHeld();
            if (held)
            {
                if (_disguised) Reveal(true);
                _heldTime += Time.deltaTime;
                if (_heldTime > 1.2f && !_debugged) Debug();
                return;
            }
            _heldTime = 0f;
            if (!Cfg.GlitchDisguise.Value) { if (_disguised) Reveal(false); return; }
            if (_calm != null && _calm.IsCalmed()) { if (_disguised) Reveal(true); return; }

            if (!_disguised)
            {
                if (Time.time > _nextDisguise) Disguise();
                return;
            }
            if (_flickerEnd > 0f)
            {
                if (Time.time > _flickerEnd) { _flickerEnd = 0f; Apply(_disguise); }
            }
            else if (Time.time > _nextFlicker)
            {
                _nextFlicker = Time.time + U.Rand(3f, 9f);
                _flickerEnd = Time.time + 0.12f;
                Apply(_own);
            }
            if (Time.time > _disguiseUntil) Reveal(false);
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
            _disguiseUntil = Time.time + U.Rand(25f, 60f);
            _nextFlicker = Time.time + U.Rand(2f, 6f);
            Fx.Play("FX Despawn", transform.position + Vector3.up * 0.5f, 0.6f);
            Snd.Play(Snd.GlitchSplit, transform.position);
            Apply(_disguise);
        }

        public void Reveal(bool fx)
        {
            _disguised = false;
            _flickerEnd = 0f;
            _nextDisguise = Time.time + U.Rand(8f, 20f);
            if (fx) { Fx.Play("FX Despawn", transform.position + Vector3.up * 0.5f, 0.6f); Snd.Play(Snd.GlitchSplit, transform.position); }
            Apply(_own);
        }

        public void OnWater() { if (_disguised) Reveal(true); }

        void Debug()
        {
            _debugged = true;
            var p = transform.position + Vector3.up * 0.5f;
            Fx.Play("fxNewbuckExplosion_small", p);
            Fx.Play("FX Despawn", p);
            Snd.Play(Snd.GlitchSplit, p);
            try
            {
                int reward = Cfg.GlitchDebugReward.Value;
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
            var candidates = new List<SlimeAppearance>();
            foreach (var c in Physics.OverlapSphere(transform.position, 25f, U.SlimeMask, QueryTriggerInteraction.Ignore))
            {
                var rb = c != null ? c.attachedRigidbody : null;
                if (rb == null || rb.gameObject == gameObject || rb.GetComponent<GlitchSlime>() != null) continue;
                var ap = rb.GetComponent<SlimeAppearanceApplicator>();
                var a = ap != null ? ap.Appearance : null;
                if (a != null && !candidates.Contains(a)) candidates.Add(a);
            }
            if (candidates.Count == 0)
            {
                foreach (var n in new[] { "Pink", "Tabby", "Phosphor", "Rock", "Cotton", "Honey", "Boom", "Crystal", "Batty", "Angler", "Ringtail", "Hunter" })
                {
                    try { var d = CottonLibrary.Library.GetSlime(n); if (d != null) candidates.Add(d.AppearancesDefault[0]); } catch { }
                }
            }
            for (int tries = 0; tries < 6 && candidates.Count > 0; tries++)
            {
                var a = candidates[U.Rng.Next(candidates.Count)];
                if (!BonesOk(a)) { candidates.Remove(a); continue; }
                return DisguiseOf(a);
            }
            return null;
        }

        static SlimeAppearance DisguiseOf(SlimeAppearance a)
        {
            if (Disguises.TryGetValue(a.Pointer, out var d) && d != null) return d;
            d = Object.Instantiate(a);
            d.name = "GlitchDisguise_" + a.name;
            d.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var face = WrongFace(a._face);
            if (face != null) d._face = face;
            Disguises[a.Pointer] = d;
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
