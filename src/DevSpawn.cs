using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>spawnslime ident [n] [dist] | spawnslime clear: live test slimes on the ground in front of the camera, removed again by clear.</summary>
    public static class DevSpawn
    {
        static readonly List<GameObject> Spawned = new();

        public static string Cmd(string[] a)
        {
            if (a.Length < 2) return "spawnslime ident [n] [dist] [radiant] | clear";
            if (a[1] == "clear")
            {
                int k = 0;
                foreach (var g in Spawned) if (g != null) { Destroyer.DestroyActor(g, "ClassicSlimes.DevSpawn", true); k++; }
                Spawned.Clear();
                return $"spawnslime clear: {k} removed";
            }
            IdentifiableType t = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                if (x != null && string.Equals(x.name, a[1], StringComparison.OrdinalIgnoreCase)) { t = x; break; }
            if (t == null || t.prefab == null) return "spawnslime: no " + a[1];
            var cam = Camera.main;
            if (cam == null) return "spawnslime: no camera";
            bool wantRadiant = false;
            int radSidePref = -1;
            int n = 1; float dist = 3f; int numIdx = 0;
            for (int i = 2; i < a.Length; i++)
            {
                if (string.Equals(a[i], "radiant", StringComparison.OrdinalIgnoreCase)) { wantRadiant = true; radSidePref = -1; continue; }
                if (string.Equals(a[i], "radiant0", StringComparison.OrdinalIgnoreCase)) { wantRadiant = true; radSidePref = 0; continue; }
                if (string.Equals(a[i], "radiant1", StringComparison.OrdinalIgnoreCase)) { wantRadiant = true; radSidePref = 1; continue; }
                if (numIdx == 0 && int.TryParse(a[i], out var nn)) { n = Math.Max(1, nn); numIdx++; }
                else if (float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var dd)) { dist = dd; numIdx++; }
            }
            var fwd = cam.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            int made = 0;
            var sd = wantRadiant ? t.TryCast<SlimeDefinition>() : null;
            SlimeDefinition radBaseDef = null;
            SlimeAppearance radApp = null;
            if (sd != null)
            {
                if (!sd.IsLargo)
                {
                    radBaseDef = sd;
                    radApp = sd.RadiantBase;
                }
                else
                {
                    var bases = sd.BaseSlimes;
                    if (bases != null && bases.Length >= 2)
                    {
                        int side = radSidePref;
                        if (side < 0)
                        {
                            for (int k = 0; k < bases.Length; k++)
                                if (bases[k] != null && SlimeFactory.Spec(bases[k].name) != null && bases[k].RadiantBase != null) { side = k; break; }
                        }
                        if (side < 0)
                        {
                            for (int k = 0; k < bases.Length; k++)
                                if (bases[k] != null && bases[k].RadiantBase != null) { side = k; break; }
                        }
                        if (side < 0) side = 0;
                        side = Math.Clamp(side, 0, bases.Length - 1);
                        radBaseDef = bases[side];
                        radApp = (radBaseDef != null ? sd.GetRadiantAppearance(radBaseDef) : null) ?? (side == 0 ? sd.RadiantLargo0 : sd.RadiantLargo1);
                    }
                    else
                    {
                        radApp = sd.RadiantLargo0 ?? sd.RadiantLargo1;
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                var pos = cam.transform.position + fwd * dist + right * ((i - (n - 1) * 0.5f) * 1.5f);
                if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 20f, U.GroundMask, QueryTriggerInteraction.Ignore)) pos = hit.point + Vector3.up * 0.6f;
                var go = Foods.SpawnActor(t, pos, Quaternion.LookRotation(-fwd, Vector3.up));
                if (go != null)
                {
                    if (wantRadiant)
                    {
                        try
                        {
                            var sr = go.GetComponent<Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant>();
                            if (sr != null)
                            {
                                if (radBaseDef != null) sr.SetRadiant(radBaseDef);
                                else sr.SetRadiant();
                            }
                            if (radApp != null)
                            {
                                var app = go.GetComponent<SlimeAppearanceApplicator>();
                                if (app != null && app.Appearance != radApp) { app.Appearance = radApp; app.ApplyAppearance(); }
                            }
                        }
                        catch { }
                    }
                    Spawned.Add(go); made++;
                }
            }
            return $"spawnslime {t.name}{(wantRadiant ? " radiant" : "")}: {made} at {dist:0.0} m, {Spawned.Count} test slimes";
        }
    }
}
