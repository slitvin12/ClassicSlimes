using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// gordowatch (dev): polls the classic gordo spots (no Harmony: runtime patches of Destroyer.DestroyActor,
    /// CellDirector.Despawn, GameModel.DestroyIdentifiableModel and GordoRewardsBase.OnInstantiatedReward made from a dev
    /// command killed the game twice on 05.11.2026, coreclr.dll access violation right after patching). Every 0.25 s:
    /// the gordo state (active, eaten count) when it changes; the crates and the spot's own slimes within 30 m of a spot
    /// (new, moved more than 4 m since the last poll, gone = destroyed); every 5 s the actor counts within 25 m on change.
    /// </summary>
    static class GordoDiag
    {
        const float Near = 30f, Watch = 25f, Step = 0.25f;
        static bool _on;
        static MelonLogger.Instance Log => ClassicSlimesMod.Log;

        class Tracked { public GameObject Go; public string Name, Spot; public long Id; public Vector3 Pos; public float Since; }

        public static string On()
        {
            if (_on) return "gordowatch: already on";
            _on = true;
            MelonCoroutines.Start(Loop());
            return "gordowatch: polling " + Gordos.Spots.Count + " spots every " + Step + " s";
        }

        static string V(Vector3 p) => $"({p.x:F1},{p.y:F1},{p.z:F1})";

        /// <summary>gordoeat [r]: eat state of the nearest classic gordo + the actors within r m of its EatTrigger.</summary>
        public static string Eat(float r)
        {
            GordoEat best = null; float bd = float.MaxValue;
            var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
            {
                if (e == null || !e.gameObject.scene.IsValid()) continue;
                float d = (e.transform.position - cam).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return "gordoeat: no gordo";
            var sb = new System.Text.StringBuilder();
            Transform trig = null;
            foreach (var c in best.GetComponentsInChildren<Transform>(true)) if (c.name == "EatTrigger") { trig = c; break; }
            string can = "?", eating = "?";
            try { can = best.CanEat().ToString(); } catch (Exception x) { can = "err " + x.Message; }
            try { eating = best._eating != null ? best._eating.Count.ToString() : "null"; } catch (Exception x) { eating = "err " + x.Message; }
            sb.Append($"gordoeat {best.name}: active {best.gameObject.activeInHierarchy} enabled {best.enabled} eaten {best.GetEatenCount()}/{best.GetTargetCount()} " +
                $"canEat {can} eating {eating} scale {best.transform.lossyScale.x:F2}");
            Vector3 tp = best.transform.position;
            if (trig != null)
            {
                tp = trig.position;
                var sc = trig.GetComponent<SphereCollider>();
                sb.Append($"; trigger at {V(tp)} active {trig.gameObject.activeInHierarchy}");
                if (sc != null) sb.Append($" r {sc.radius * trig.lossyScale.x:F2} enabled {sc.enabled} isTrigger {sc.isTrigger} layer {trig.gameObject.layer}");
                foreach (var mb in trig.GetComponents<MonoBehaviour>()) if (mb != null) sb.Append($" [{mb.GetIl2CppType().Name} enabled {mb.enabled}]");
            }
            foreach (var a in Object.FindObjectsOfType<Identifiable>())
            {
                if (a == null || a.identType == null) continue;
                var p = a.transform.position;
                if ((p - tp).sqrMagnitude > r * r) continue;
                var rb = a.GetComponent<Rigidbody>();
                var col = a.GetComponent<Collider>();
                sb.Append($"\n  {a.identType.name} at {V(p)} {(p - tp).magnitude:F1} m from the trigger, layer {a.gameObject.layer}");
                if (rb != null) sb.Append($", kinematic {rb.isKinematic} sleeping {rb.IsSleeping()} v {rb.linearVelocity.magnitude:F1}");
                if (col != null) sb.Append($", collider {col.enabled}");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- spot safety (gordoedge / gordolanded)

        const float MaxEdge = 25f, HomesEdge = 40f;
        static readonly string[] Dirs = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        static bool TrySpot(string slime, out Gordos.Spot sp)
        {
            foreach (var s in Gordos.Spots)
                if (string.Equals(s.Slime, slime, StringComparison.OrdinalIgnoreCase)) { sp = s; return true; }
            sp = null;
            return false;
        }

        /// <summary>
        /// Distance from p along yaw (0 = +z = north) to the first ground more than 3 m below p (a drop) or above it (a wall).
        /// Gordo bodies are looked through (a live gordo on the spot is not a wall).
        /// </summary>
        static float EdgeAlong(Vector3 p, float yaw, float max, out bool wall)
        {
            wall = false;
            var d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (float r = 0.5f; r <= max; r += 0.5f)
            {
                var q = p + d * r;
                if (!Physics.Raycast(q + Vector3.up * 8f, Vector3.down, out var h, 80f, U.GroundMask, QueryTriggerInteraction.Ignore)) return r;
                for (int i = 0; i < 3 && h.point.y > p.y + 3f && h.collider != null && h.collider.GetComponentInParent<GordoEat>() != null; i++)
                    if (!Physics.Raycast(h.point + Vector3.down * 0.05f, Vector3.down, out h, 80f, U.GroundMask, QueryTriggerInteraction.Ignore)) return r;
                if (h.point.y > p.y + 3f) { wall = true; return r; }
                if (h.point.y < p.y - 3f) return r;
            }
            return max;
        }

        /// <summary>
        /// Smallest distance to a drop over 16 directions (up to max m); a direction closed by a wall first counts as safe.
        /// Without a row a direction is only followed up to the smallest distance so far.
        /// </summary>
        static float MinEdge(Vector3 p, System.Text.StringBuilder row, float max = MaxEdge)
        {
            float min = max;
            for (int k = 0; k < 16; k++)
            {
                float e = EdgeAlong(p, k * 22.5f, row != null ? max : min, out bool wall);
                if (!wall && e < min) min = e;
                row?.Append($" {Dirs[k]} {e:0.#}{(wall ? "w" : "")}");
            }
            return min;
        }

        /// <summary>gordoedge [slime] [scan|homes] | gordoedge x y z (any point, e.g. an old spot or a test gordo).</summary>
        public static string EdgeCmd(string[] a)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            const System.Globalization.NumberStyles num = System.Globalization.NumberStyles.Float;
            if (a.Length > 3 && float.TryParse(a[1], num, inv, out var x) && float.TryParse(a[2], num, inv, out var y)
                && float.TryParse(a[3], num, inv, out var z))
            {
                var row = new System.Text.StringBuilder();
                var p = new Vector3(x, y, z);
                return $"gordoedge {V(p)}: min {MinEdge(p, row):0.#} m |{row}";
            }
            var slime = a.Length > 1 ? a[1] : "Mosaic";
            var mode = a.Length > 2 ? a[2] : "";
            return mode == "homes" ? Homes(slime) : Edge(slime, mode == "scan");
        }

        /// <summary>
        /// gordoedge [slime] homes: the gordospots candidates (flat, dry, clear points by the slime's loaded home spawners)
        /// ranked by the distance to the nearest drop, up to 40 m, then by the homes within 30 m -> gordoedge.txt.
        /// </summary>
        static string Homes(string slime)
        {
            var c = Gordos.CandidateList(slime, out int homes);
            if (homes == 0) return "gordoedge: no loaded homes of " + slime;
            var rows = c.Select(x => (min: MinEdge(x.p, null, HomesEdge), score: x.score, p: x.p, info: x.info))
                        .OrderByDescending(x => x.min).ThenByDescending(x => x.score).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"gordoedge {slime} homes: {rows.Count} candidates from {homes} homes, drop searched up to {HomesEdge:0} m");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new System.Text.StringBuilder();
                if (i < 25) MinEdge(rows[i].p, r, HomesEdge);
                sb.AppendLine($"#{i} {rows[i].p.x:0.0} {rows[i].p.y:0.0} {rows[i].p.z:0.0} min {rows[i].min:0.#} m {rows[i].info} |{r}");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Diag.Dir, "gordoedge.txt"), sb.ToString());
            return $"gordoedge {slime} homes: {rows.Count} candidates from {homes} homes" +
                   string.Join("", rows.Take(5).Select((x, i) => $" | #{i} {x.p.x:0.0} {x.p.y:0.0} {x.p.z:0.0} min {x.min:0.#}"));
        }

        /// <summary>
        /// gordoedge [slime] [scan]: distance from the slime's gordo spot to the edge (ground 3+ m lower) in 16 directions
        /// ("w" = a wall first). `scan`: flat, dry, clear points within 12 m of the spot (the gordospots rules), the ones
        /// farthest from any drop first -> gordoedge.txt.
        /// </summary>
        public static string Edge(string slime, bool scan)
        {
            if (!TrySpot(slime, out var sp)) return "gordoedge: no spot " + slime;
            var row = new System.Text.StringBuilder();
            float now = MinEdge(sp.Pos, row);
            var head = $"gordoedge {sp.Slime} spot {V(sp.Pos)}: min {now:0.#} m |{row}";
            if (!scan) return head;
            var list = new List<(float min, Vector3 p)>();
            for (float dx = -12f; dx <= 12.01f; dx += 1.5f)
                for (float dz = -12f; dz <= 12.01f; dz += 1.5f)
                {
                    var q = sp.Pos + new Vector3(dx, 0f, dz);
                    if (!Physics.Raycast(q + Vector3.up * 8f, Vector3.down, out var h, 20f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    var g = h.point;
                    if (Mathf.Abs(g.y - sp.Pos.y) > 2.5f || h.normal.y < 0.94f) continue;
                    if (!Gordos.Flat(g, 3f, 0.45f) || Gordos.Wet(g)) continue;
                    if (Physics.CheckSphere(g + Vector3.up * 2.6f, 2.1f, U.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    list.Add((MinEdge(g, null), g));
                }
            list.Sort((x, y) => y.min.CompareTo(x.min));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(head);
            for (int i = 0; i < list.Count; i++)
            {
                var r = new System.Text.StringBuilder();
                if (i < 20) MinEdge(list[i].p, r);
                sb.AppendLine($"#{i} {list[i].p.x:0.0} {list[i].p.y:0.0} {list[i].p.z:0.0} min {list[i].min:0.#} m, {(list[i].p - sp.Pos).magnitude:0.0} m from the spot |{r}");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Diag.Dir, "gordoedge.txt"), sb.ToString());
            return $"gordoedge {sp.Slime} scan: {list.Count} points, spot min {now:0.#} m" +
                   string.Join("", list.Take(5).Select((c, i) => $" | #{i} {c.p.x:0.0} {c.p.y:0.0} {c.p.z:0.0} min {c.min:0.#}"));
        }

        /// <summary>
        /// gordolanded [slime] [r] [x y z]: actors within r m (horizontally) of the slime's gordo spot (or of x y z, e.g. a test
        /// gordo) - the slime, its largos, tarrs, crates - split into the spot's level and below it (more than 3 m lower: fell off).
        /// </summary>
        public static string Landed(string slime, float r, Vector3? at = null)
        {
            if (!TrySpot(slime, out var sp)) return "gordolanded: no spot " + slime;
            var c = at ?? sp.Pos;
            int up = 0, down = 0, lup = 0, ldown = 0, tup = 0, tdown = 0, crates = 0;
            var lows = new List<string>();
            foreach (var a in Object.FindObjectsOfType<Identifiable>())
            {
                if (a == null || a.identType == null) continue;
                var p = a.transform.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz > r * r) continue;
                var n = a.identType.name;
                bool low = p.y < c.y - 3f;
                if (n == sp.Slime) { if (low) { down++; lows.Add(V(p)); } else up++; }
                else if (n.Contains(sp.Slime) && !n.EndsWith("Plort") && !n.EndsWith("Gordo")) { if (low) ldown++; else lup++; }
                else if (n.StartsWith("Tarr")) { if (low) tdown++; else tup++; }
                else if (n.StartsWith("Container")) crates++;
            }
            return $"gordolanded {sp.Slime} within {r:0} m of {V(c)}: {sp.Slime} up {up} down {down}; largos up {lup} down {ldown}; " +
                   $"tarr up {tup} down {tdown}; crates {crates}" + (lows.Count > 0 ? "; down at " + string.Join(" ", lows.Take(12)) : "");
        }

        static IEnumerator Loop()
        {
            var tracked = new Dictionary<int, Tracked>();
            var state = new Dictionary<string, string>();
            var last = new Dictionary<string, string>();
            float t0 = Time.unscaledTime, nextSum = 0f;
            while (true)
            {
                float t = 0f;
                while (t < Step) { t += Time.unscaledDeltaTime; yield return null; }
                float now = Time.unscaledTime - t0;
                try
                {
                    // gordo state
                    foreach (var e in Resources.FindObjectsOfTypeAll<GordoEat>())
                    {
                        if (e == null || !e.gameObject.scene.IsValid()) continue;
                        string id;
                        try { id = e._id; } catch { continue; }
                        if (id == null || !id.StartsWith(Gordos.IdPrefix)) continue;
                        int eaten = -9;
                        try { eaten = e.GetEatenCount(); } catch { }
                        var st = $"active {e.gameObject.activeInHierarchy} eaten {eaten} model {(e.GordoModel != null ? e.GordoModel.GordoEatenCount.ToString() : "null")}";
                        if (state.TryGetValue(id, out var o) && o == st) continue;
                        state[id] = st;
                        Log.Msg($"[gordowatch] t={now:F2} gordo {id}: {st}");
                    }
                    var all = Object.FindObjectsOfType<Identifiable>();
                    var seen = new HashSet<int>();
                    foreach (var a in all)
                    {
                        if (a == null || a.identType == null) continue;
                        var n = a.identType.name;
                        var go = a.gameObject;
                        int k = go.GetInstanceID();
                        var p = go.transform.position;
                        if (tracked.TryGetValue(k, out var tr))
                        {
                            seen.Add(k);
                            if ((p - tr.Pos).sqrMagnitude > 16f)
                                Log.Msg($"[gordowatch] t={now:F2} moved {tr.Name} #{tr.Id} {V(tr.Pos)} -> {V(p)} ({tr.Spot} spot)");
                            tr.Pos = p;
                            continue;
                        }
                        foreach (var s in Gordos.Spots)
                        {
                            if ((p - s.Pos).sqrMagnitude > Near * Near) continue;
                            if (!n.StartsWith("Container") && n != s.Slime) break;
                            long id = -1;
                            try { id = Identifiable.GetActorId(go).Value; } catch { }
                            tracked[k] = new Tracked { Go = go, Name = n, Spot = s.Slime, Id = id, Pos = p, Since = now };
                            seen.Add(k);
                            Log.Msg($"[gordowatch] t={now:F2} new {n} #{id} at {V(p)} ({(p - s.Pos).magnitude:F1} m from the {s.Slime} gordo)");
                            break;
                        }
                    }
                    foreach (var k in tracked.Keys.Where(k => !seen.Contains(k)).ToList())
                    {
                        var tr = tracked[k];
                        bool alive = false;
                        try { alive = tr.Go != null && tr.Go.activeInHierarchy; } catch { }
                        Log.Msg($"[gordowatch] t={now:F2} gone {tr.Name} #{tr.Id} last {V(tr.Pos)} ({tr.Spot} spot, tracked {now - tr.Since:F1} s, " +
                            $"{(alive ? "object still there" : tr.Go == null ? "destroyed" : "inactive")})");
                        tracked.Remove(k);
                    }
                    if (now >= nextSum)
                    {
                        nextSum = now + 5f;
                        foreach (var s in Gordos.Spots)
                        {
                            var counts = new SortedDictionary<string, int>();
                            foreach (var a in all)
                            {
                                if (a == null || a.identType == null || (a.transform.position - s.Pos).sqrMagnitude > Watch * Watch) continue;
                                var n = a.identType.name;
                                if (n == "Player") continue;
                                counts[n] = counts.TryGetValue(n, out var c) ? c + 1 : 1;
                            }
                            var line = string.Join(",", counts.Select(kv => kv.Value > 1 ? $"{kv.Key}x{kv.Value}" : kv.Key));
                            if (counts.Count == 0 && !last.ContainsKey(s.Slime)) continue;
                            if (last.TryGetValue(s.Slime, out var l) && l == line) continue;
                            last[s.Slime] = line;
                            Log.Msg($"[gordowatch] t={now:F2} near {s.Slime} gordo (25 m): [{line}]");
                        }
                    }
                }
                catch (Exception e) { Log.Warning("gordowatch: " + e.Message); }
            }
        }
    }
}
