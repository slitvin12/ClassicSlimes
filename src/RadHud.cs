using System;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// SR2 still contains SR1's radiation meter (HUD "Rad Meter": StatusBar + RadMeter with the rad symbol), it is just
    /// switched off. It is placed above the health meter and enabled; RadMeter itself shows it only while irradiated.
    /// Radiation also wears off over time like in SR1 (if the game does not already do it).
    /// </summary>
    public static class RadHud
    {
        public static float LastExposure = -100f;
        public static Vector2 Offset = new(0f, 78f);
        static RadMeter _meter;
        static float _next, _nextFind, _prevRads = -1f, _recoverTick;
        static bool _logged;

        static RadMeter Find()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<RadMeter>())
                if (m != null && m.gameObject.scene.IsValid()) return m;
            return null;
        }

        public static void Place()
        {
            if (_meter == null) return;
            var rt = _meter.GetComponent<RectTransform>();
            var parent = _meter.transform.parent;
            RectTransform health = null;
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                {
                    var c = parent.GetChild(i);
                    if (c != null && c.name.StartsWith("Health", StringComparison.OrdinalIgnoreCase)) { health = c.GetComponent<RectTransform>(); break; }
                }
            if (rt == null) return;
            if (health != null)
            {
                rt.anchorMin = health.anchorMin; rt.anchorMax = health.anchorMax; rt.pivot = health.pivot;
                rt.sizeDelta = health.sizeDelta; rt.localScale = health.localScale;
                rt.anchoredPosition = health.anchoredPosition + Offset;
            }
            if (!_logged) { _logged = true; ClassicSlimesMod.Log.Msg($"Rad meter enabled at {rt.anchoredPosition} (health {(health != null ? health.anchoredPosition.ToString() : "?")})"); }
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var sc = SceneContext.Instance;
            if (sc == null || sc.PlayerState == null) return;
            try
            {
                if (Cfg.RadHud.Value)
                {
                    if (_meter == null && Time.unscaledTime > _nextFind) { _nextFind = Time.unscaledTime + 3f; _meter = Find(); if (_meter != null) Place(); }
                    if (_meter != null && !_meter.gameObject.activeSelf) { Place(); _meter.gameObject.SetActive(true); }
                }
                Recover();
            }
            catch (Exception e) { if (Diag.Enabled) ClassicSlimesMod.Log.Warning("RadHud: " + e.Message); }
        }

        static void Recover()
        {
            var ps = SceneContext.Instance != null ? SceneContext.Instance.PlayerState : null;
            if (ps == null) return;
            float rads = ps.GetCurrRad();
            bool selfRecovered = _prevRads >= 0f && rads < _prevRads && Time.time - _recoverTick > 0.6f;
            if (rads > 0f && !selfRecovered && Time.time - LastExposure > 3f)
            {
                ps.RemoveRads(Cfg.RadRecoveryPerSecond.Value * 0.5f);
                _recoverTick = Time.time;
                rads = ps.GetCurrRad();
            }
            _prevRads = rads;
        }
    }
}
