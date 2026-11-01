using System;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// Whole-slime squash &amp; stretch for slimes with glass renderers (mosaic glass shell, quantum glass body).
    /// SR2 wobbles a slime in its body's vertex shader (driven by the rubber bone's jiggle). A glass shader can't, so the
    /// tiles poked through the rigid mosaic shell and the face slid over the rigid quantum body. The rig sets the rubber
    /// bone's shader strength to 0 (no renderer wobbles on its own) and runs its own spring instead: it lags behind every
    /// change of the slime's velocity (landing, starting, stopping, being shot) and leans / stretches the appearance root
    /// (body, face, glass and everything parented to it) about the bottom of the body, volume kept.
    /// </summary>
    public sealed class JellyRig
    {
        /// <summary>Spring -&gt; lean / stretch gain, max lean (deg), max stretch (fraction of the height), spring
        /// frequency (Hz), damping ratio, and how much of a velocity change goes into the spring.</summary>
        public static float Gain = 1f, MaxLean = 16f, MaxStretch = 0.22f, Freq = 2.4f, Damp = 0.22f, Inertia = 0.45f;
        static int _logs;

        Transform _root, _vis;
        RubberBoneEffectShader _rbe;
        Rigidbody _rb;
        float _rbeStrength, _h = 1f;
        Vector3 _vPos, _vScale, _wPos, _wScale, _pivot, _x, _v, _lastVel, _lastOff;
        Quaternion _vRot, _wRot;
        int _upAxis = 1;
        bool _haveVel;

        public bool Ok { get; private set; }
        public Transform Vis => _vis;

        static void Log(string who, string m) { if (_logs++ < 8) ClassicSlimesMod.Log.Msg("Jelly " + who + ": " + m); }

        /// <summary>Top ancestor of the body's bones below the slime root (SR2: "Appearance").</summary>
        public static Transform VisRoot(Transform root, SkinnedMeshRenderer body)
        {
            if (root == null || body == null) return null;
            Transform bone = body.rootBone;
            if (bone == null && body.bones != null && body.bones.Length > 0) bone = body.bones[0];
            if (bone == null) return null;
            var t = bone;
            while (t.parent != null && t.parent != root) t = t.parent;
            return t.parent == root ? t : null;
        }

        public bool Init(Transform root, SkinnedMeshRenderer body)
        {
            Ok = false;
            _root = root;
            if (root == null) return false;
            if (body == null) { Log(root.name, "no body renderer"); return false; }
            _rbe = root.GetComponentInChildren<RubberBoneEffectShader>();
            if (_rbe == null) { Log(root.name, "no rubber bone"); return false; }
            var t = VisRoot(root, body);
            if (t == null) { Log(root.name, "no bone root below the slime"); return false; }
            _vis = t;
            _vPos = _wPos = t.localPosition; _vRot = _wRot = t.localRotation; _vScale = _wScale = t.localScale;
            var up = Quaternion.Inverse(_vRot) * Vector3.up;
            float ax = Mathf.Abs(up.x), ay = Mathf.Abs(up.y), az = Mathf.Abs(up.z);
            _upAxis = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
            var b = U.RootBounds(root, body);
            _h = Mathf.Max(0.3f, b.size.y);
            _pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            _rb = root.GetComponent<Rigidbody>();
            _x = _v = Vector3.zero; _haveVel = false;
            _rbeStrength = _rbe.strength;
            _rbe.strength = 0f;
            Ok = true;
            Log(root.name, $"root {_vis.name} up {_upAxis} h {_h:0.00} pivot {_pivot} rubber {_rbe.preset} {_rbeStrength}");
            return true;
        }

        public void Kick(Vector3 localVel) { _v += localVel; }

        // the top of the slime lags behind every change of the body's velocity (spring-damper, slime-local space)
        Vector3 Spring()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return _x;
            var vel = _rb != null && !_rb.isKinematic ? _rb.linearVelocity : Vector3.zero;
            if (_haveVel)
            {
                var dv = _root.InverseTransformDirection(vel - _lastVel);
                if (dv.sqrMagnitude > 400f) dv = dv.normalized * 20f;   // vacuum shots, teleports
                _v -= dv * Inertia;
            }
            _lastVel = vel; _haveVel = true;
            float w = Freq * 2f * Mathf.PI;
            _v += (-w * w * _x - 2f * Damp * w * _v) * dt;
            _x += _v * dt;
            if (_x.sqrMagnitude > 0.36f) _x = _x.normalized * 0.6f;
            return _x;
        }

        public void Tick()
        {
            if (!Ok) return;
            var t = _vis;
            if (t == null || _rbe == null || _root == null) { Ok = false; return; }
            // moved by someone else (animation, vacuum): that is the new rest pose
            if ((t.localPosition - _wPos).sqrMagnitude > 1e-8f || Quaternion.Angle(t.localRotation, _wRot) > 0.05f || (t.localScale - _wScale).sqrMagnitude > 1e-8f)
            { _vPos = t.localPosition; _vRot = t.localRotation; _vScale = t.localScale; }
            if (_rbe.strength != 0f) { _rbeStrength = _rbe.strength; _rbe.strength = 0f; }
            var off = Spring() * Gain;
            _lastOff = off;
            float h = _h;
            // lean: the top follows the sideways jiggle (rotation about the bottom of the slime)
            var hor = new Vector3(off.x, 0f, off.z);
            float ang = Mathf.Min(MaxLean, Mathf.Atan2(hor.magnitude, h) * Mathf.Rad2Deg);
            var rot = ang > 0.01f ? Quaternion.AngleAxis(ang, Vector3.Cross(Vector3.up, hor).normalized) : Quaternion.identity;
            // stretch / squash: the vertical jiggle, volume kept
            float sy = 1f + Mathf.Clamp(off.y / h, -MaxStretch, MaxStretch), sxz = 1f / Mathf.Sqrt(sy);
            var sl = new Vector3(_upAxis == 0 ? sy : sxz, _upAxis == 1 ? sy : sxz, _upAxis == 2 ? sy : sxz);
            t.localPosition = _pivot + rot * Vector3.Scale(new Vector3(sxz, sy, sxz), _vPos - _pivot);
            t.localRotation = rot * _vRot;
            t.localScale = Vector3.Scale(_vScale, sl);
            _wPos = t.localPosition; _wRot = t.localRotation; _wScale = t.localScale;
        }

        /// <summary>Rest pose back, SR2's own wobble back (vacuumed, unloaded, disabled).</summary>
        public void Restore()
        {
            if (!Ok) return;
            Ok = false;
            try
            {
                if (_vis != null) { _vis.localPosition = _vPos; _vis.localRotation = _vRot; _vis.localScale = _vScale; }
                if (_rbe != null) _rbe.strength = _rbeStrength;
            }
            catch { }
        }

        public void Fail() { Ok = false; }

        public string Info() => Ok && _vis != null && _rbe != null
            ? $"root {_vis.name} h {_h:0.00} pivot {_pivot} rubber {_rbe.preset} str {_rbeStrength} (now {_rbe.strength}) x {_x} v {_v} off {_lastOff} rot {_vis.localEulerAngles} scale {_vis.localScale}"
            : "jelly off";
    }
}
