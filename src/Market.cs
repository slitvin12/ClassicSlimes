using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>Plort market of the classic plorts: makes room on the board (vanilla has 25 slots, CottonForge trims the
    /// rest) + dev info (prices, board slots, board layout dump).</summary>
    public static class Market
    {
        internal static string LastGrow = "not run";

        /// <summary>Runs before MarketUI.Start (and before CottonForge's trim): the board is a horizontal row of
        /// PricesPanel columns (one-column grids of 82 px cells); vanilla uses 9 of the 10 columns, two of them with 2 cells
        /// only. Adds the unused columns, then fills short columns up to the tallest, then (if needed) adds rows while
        /// they fit the board height.</summary>
        internal static void Grow(MarketUI ui)
        {
            if (ui == null || ui._config == null || ui._config._plorts == null || ui.pricesPanels == null) return;
            var seen = new HashSet<string>();
            int need = 0;
            foreach (var p in ui._config._plorts)
            {
                if (p == null) continue;
                string id = p.IdentType != null ? (p.IdentType.ReferenceId ?? p.IdentType.name) : null;
                if (!string.IsNullOrEmpty(id) && !seen.Add(id)) continue;
                need++;
            }
            var list = new List<MarketUI.PricesPanelEntry>();
            var used = new HashSet<IntPtr>();
            int slots = 0, tallest = 0;
            foreach (var e in ui.pricesPanels)
            {
                if (e == null || e.panel == null) continue;
                list.Add(e); used.Add(e.panel.Pointer); slots += e.entryCount; tallest = Math.Max(tallest, e.entryCount);
            }
            int before = slots;
            if (need <= slots) { LastGrow = $"need {need} <= {slots} slots"; return; }
            int added = 0;
            if (ui.pricesPanelGroup != null)
            {
                var g = ui.pricesPanelGroup.transform;
                for (int i = 0; i < g.childCount; i++)
                {
                    var c = g.GetChild(i).gameObject;
                    if (used.Contains(c.Pointer) || c.GetComponent<UnityEngine.UI.GridLayoutGroup>() == null) continue;
                    var e = new MarketUI.PricesPanelEntry { panel = c, entryCount = 0 };
                    if (!c.activeSelf) c.SetActive(true);
                    list.Add(e); used.Add(c.Pointer); added++;
                }
            }
            if (tallest <= 0) tallest = 3;
            int maxRows = tallest;
            try
            {
                var rt = ui.GetComponent<RectTransform>();
                var gl = list.Count > 0 ? list[0].panel.GetComponent<UnityEngine.UI.GridLayoutGroup>() : null;
                if (rt != null && gl != null)
                {
                    float h = rt.rect.height, cell = gl.cellSize.y + gl.spacing.y, pad = gl.padding.top + gl.padding.bottom - gl.spacing.y;
                    maxRows = Math.Max(tallest, (int)Math.Floor((h - pad) / cell));
                }
            }
            catch { }
            // fill: first up to the tallest column (new columns first, they are empty), then one more row at a time
            for (int rows = tallest; rows <= maxRows && slots < need; rows++)
            {
                bool progress = true;
                while (slots < need && progress)
                {
                    progress = false;
                    MarketUI.PricesPanelEntry best = null;
                    foreach (var e in list) if (e.entryCount < rows && (best == null || e.entryCount < best.entryCount)) best = e;
                    if (best != null) { best.entryCount++; slots++; progress = true; }
                }
            }
            if (added > 0 || slots != before)
                ui.pricesPanels = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MarketUI.PricesPanelEntry>(list.ToArray());
            LastGrow = $"need {need}, slots {before} -> {slots} (+{added} columns, max {maxRows} rows)";
            MelonLoader.MelonLogger.Msg("[ClassicSlimes] Market board: " + LastGrow);
        }

        [HarmonyPatch(typeof(MarketUI), nameof(MarketUI.Start))]
        static class GrowPatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.Last)]   // after CottonForge adds the plorts (400), before its trim (int.MinValue)
            static void Prefix(MarketUI __instance)
            {
                if (Cfg.MarketBoard == null || !Cfg.MarketBoard.Value) return;
                try { Grow(__instance); } catch (Exception e) { MelonLoader.MelonLogger.Warning("[ClassicSlimes] market board: " + e.Message); }
            }
        }

        public static string Info()
        {
            var sb = new StringBuilder("market: grow=" + LastGrow);
            var econ = SceneContext.Instance != null ? SceneContext.Instance.PlortEconomyDirector : null;
            foreach (var s in SlimeFactory.Specs)
            {
                if (s.Plort == null) continue;
                int cur = 0, delta = 0; bool ok = false; string ex = "";
                try { ok = econ != null && econ.TryGetMarketValues(s.Plort, out cur, out delta); } catch (Exception e) { ex = " ex:" + e.Message; }
                bool sell = false;
                try { sell = Library.IsSellable(s.Plort); } catch { }
                sb.Append($" | {s.Plort.name} sellable={sell} price={(ok ? cur.ToString() : "none")} d={delta}{ex}");
            }
            foreach (var ui in Resources.FindObjectsOfTypeAll<MarketUI>())
            {
                if (ui == null || !ui.gameObject.scene.IsValid()) continue;
                int slots = 0, panels = 0;
                if (ui.pricesPanels != null) foreach (var p in ui.pricesPanels) if (p != null) { slots += p.entryCount; panels++; }
                var names = new List<string>();
                if (ui._config != null && ui._config._plorts != null) foreach (var e in ui._config._plorts) names.Add(e?.IdentType != null ? e.IdentType.name : "null");
                sb.Append($" | board {DiagX.Path(ui.transform)} active={ui.gameObject.activeInHierarchy} pos={ui.transform.position} fwd={ui.transform.forward} panels={panels} slots={slots} entries={names.Count}: {string.Join(",", names)}");
            }
            return sb.ToString();
        }

        /// <summary>Dev: unlock state of every board entry (MarketUI.IsPlortUnlocked + the pedia ident map behind it).</summary>
        public static string Locks()
        {
            var sb = new StringBuilder("marketlock:");
            var pd = SceneContext.Instance != null ? SceneContext.Instance.PediaDirector : null;
            if (pd == null) return "marketlock: no pedia director";
            foreach (var ui in Object.FindObjectsOfType<MarketUI>())
            {
                if (ui == null || ui._config == null || ui._config._plorts == null) continue;
                foreach (var e in ui._config._plorts)
                {
                    if (e == null || e.IdentType == null) continue;
                    bool mk = false, pu = false, sold = false; string en = "-";
                    try { mk = ui.IsPlortUnlocked(e); sold = ui.HasPlortBeenSold(e); } catch (Exception x) { en = "ex " + x.Message; }
                    try { pu = pd.IsUnlocked(e.IdentType); var pe = pd.GetEntry(e.IdentType); if (pe != null) en = pe.name; } catch { }
                    sb.Append($" {e.IdentType.name}={(mk ? 1 : 0)}/{(sold ? 1 : 0)}/{(pu ? 1 : 0)}:{en}");
                }
                break;
            }
            int custom = 0;
            try
            {
                if (pd._customIdentToEntryMap != null)
                    foreach (var it in pd._customIdentToEntryMap)
                    {
                        if (it == null) continue;
                        custom++;
                        if (custom <= 40) sb.Append($" | map {(it.Ident != null ? it.Ident.name : "null")}->{(it.Entry != null ? it.Entry.name : "null")}");
                    }
            }
            catch (Exception x) { sb.Append(" map ex " + x.Message); }
            sb.Append($" | custom map {custom}, identDict {(pd._identDict != null ? pd._identDict.Count : -1)}");
            return sb.ToString();
        }

        /// <summary>Dev: spawns plorts right inside the nearest market intake (ScorePlort trigger), as if the rancher shot them in.</summary>
        public static string Sell(string ident, int n)
        {
            IdentifiableType t = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<IdentifiableType>())
                if (x != null && string.Equals(x.name, ident, StringComparison.OrdinalIgnoreCase)) { t = x; break; }
            if (t == null || t.prefab == null) return "sellplort: unknown " + ident;
            var cam = Camera.main;
            var from = cam != null ? cam.transform.position : Vector3.zero;
            ScorePlort best = null; float bd = float.MaxValue;
            foreach (var sp in Object.FindObjectsOfType<ScorePlort>())
            {
                if (sp == null) continue;
                float d = (sp.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = sp; }
            }
            if (best == null) return "sellplort: no market intake loaded";
            var col = best.GetComponent<Collider>();
            var pos = col != null ? col.bounds.center : best.transform.position;
            int made = 0;
            for (int i = 0; i < Math.Max(1, n); i++)
                if (Foods.SpawnActor(t, pos + Vector3.up * (0.1f * i), Quaternion.identity) != null) made++;
            return $"sellplort: {made} x {t.name} into {DiagX.Path(best.transform)} at {pos} ({Mathf.Sqrt(bd):0} m)";
        }

        public static string DumpUI()
        {
            var sb = new StringBuilder();
            foreach (var ui in Object.FindObjectsOfType<MarketUI>())
            {
                sb.AppendLine($"MarketUI {DiagX.Path(ui.transform)} group={(ui.pricesPanelGroup != null ? DiagX.Path(ui.pricesPanelGroup.transform) : "null")} prefab={(ui.priceEntryPrefab != null ? ui.priceEntryPrefab.name : "null")} empty={(ui.priceEntryEmptyPrefab != null ? ui.priceEntryEmptyPrefab.name : "null")}");
                if (ui.pricesPanels != null)
                    foreach (var p in ui.pricesPanels) sb.AppendLine($"  panel {(p?.panel != null ? DiagX.Path(p.panel.transform) : "null")} count={p?.entryCount}");
                Dump(sb, ui.transform, 0, 6);
                if (ui.priceEntryPrefab != null) { sb.AppendLine("entry prefab:"); Dump(sb, ui.priceEntryPrefab.transform, 1, 3); }
            }
            File.WriteAllText(Path.Combine(Diag.Dir, "marketui.txt"), sb.ToString());
            return "marketui.txt written (" + sb.Length + " chars)";
        }

        static void Dump(StringBuilder sb, Transform t, int depth, int max)
        {
            var rt = t.TryCast<RectTransform>();
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                var hv = c.TryCast<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
                if (hv != null) n += $"(sp={hv.spacing} ctrlW={hv.childControlWidth} ctrlH={hv.childControlHeight} expW={hv.childForceExpandWidth} expH={hv.childForceExpandHeight} pad={hv.padding.left},{hv.padding.right},{hv.padding.top},{hv.padding.bottom})";
                var gl = c.TryCast<UnityEngine.UI.GridLayoutGroup>();
                if (gl != null) n += $"(cell={gl.cellSize} sp={gl.spacing} constraint={gl.constraint}:{gl.constraintCount})";
                var le = c.TryCast<UnityEngine.UI.LayoutElement>();
                if (le != null) n += $"(prefH={le.preferredHeight} minH={le.minHeight} flexH={le.flexibleHeight})";
                var cs = c.TryCast<UnityEngine.UI.ContentSizeFitter>();
                if (cs != null) n += $"(fitH={cs.verticalFit} fitW={cs.horizontalFit})";
                comps.Add(n);
            }
            string r = rt != null ? $" pos={rt.anchoredPosition} size={rt.rect.size} amin={rt.anchorMin} amax={rt.anchorMax} piv={rt.pivot} scl={rt.localScale}" : $" lpos={t.localPosition}";
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} act={t.gameObject.activeSelf}{r} [{string.Join(", ", comps)}]");
            int n2 = t.childCount;
            if (depth >= max) { if (n2 > 0) sb.AppendLine($"{new string(' ', depth * 2 + 2)}... {n2} children"); return; }
            for (int i = 0; i < n2; i++)
            {
                if (i >= 6 && depth >= 3) { sb.AppendLine($"{new string(' ', depth * 2 + 2)}... +{n2 - i} more"); break; }
                Dump(sb, t.GetChild(i), depth + 1, max);
            }
        }
    }
}
