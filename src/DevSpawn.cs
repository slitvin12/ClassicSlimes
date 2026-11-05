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
            if (a.Length < 2) return "spawnslime ident [n] [dist] | clear";
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
            int n = a.Length > 2 && int.TryParse(a[2], out var nn) ? Math.Max(1, nn) : 1;
            float dist = a.Length > 3 && float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var dd) ? dd : 3f;
            var fwd = cam.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            int made = 0;
            for (int i = 0; i < n; i++)
            {
                var pos = cam.transform.position + fwd * dist + right * ((i - (n - 1) * 0.5f) * 1.5f);
                if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point + Vector3.up * 0.6f;
                var go = Foods.SpawnActor(t, pos, Quaternion.LookRotation(-fwd, Vector3.up));
                if (go != null) { Spawned.Add(go); made++; }
            }
            return $"spawnslime {t.name}: {made} at {dist:0.0} m, {Spawned.Count} test slimes";
        }
    }
}
