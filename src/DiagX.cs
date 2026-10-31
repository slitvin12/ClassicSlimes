using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>Extra runtime dumps used while porting the SR1 looks (HUD, foods, gardens, FX, materials).</summary>
    public static class DiagX
    {
        static string N(Object o) => o == null ? "null" : o.name;
        static string C(Color c) => $"({c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###})";
        static string V2(Vector2 v) => $"({v.x:0.#},{v.y:0.#})";

        static void Try(StringBuilder sb, string what, Action a)
        {
            try { a(); } catch (Exception e) { sb.AppendLine($"!! {what} failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static readonly string[] MatNames =
        {
            "fxDiamondGlint", "radiantStars_01", "FX Phospher", "FX Gold_Aura", "FX GoldGlow", "ShadowAura", "ShadowAuraPlort", "CoreTarrShield",
            "SimpleGlass", "envLabGlass", "SphereLightRainbowFresnel Particle", "eyeHappyBat", "eyeBlinkBat", "mouthInvoke", "mouthHappyBat", "SlimeHyperAura",
            "RadiantAura_light", "SphereLightAdditiveGradientRadiantPhosphor", "EnergyRing", "slimeBattyBase", "slimeSloomberBase",
        };

        public static void Run()
        {
            var sb = new StringBuilder();
            Try(sb, "mats", () =>
            {
                foreach (var n in MatNames) { var m = Mat.Find(n); if (m != null) Diag.DumpMatValues(sb, m); else sb.AppendLine("== MISSING " + n); }
                // one radiant slime body + a few face materials of the pink slime
                var all = Resources.FindObjectsOfTypeAll<Material>();
                var rad = all.FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "SR/AMP/Slime/Body" && m.IsKeywordEnabled("_ENABLERADIANTEFFECT_ON") && m.name.Contains("Pink"));
                if (rad != null) Diag.DumpMatValues(sb, rad);
                int k = 0;
                foreach (var m in all.Where(m => m != null && m.name.StartsWith("eye") && !m.name.Contains("Twin") && !m.name.Contains("Radiant") && m.name.Length < 16)) { Diag.DumpMatValues(sb, m); if (++k > 3) break; }
                k = 0;
                foreach (var m in all.Where(m => m != null && m.name.StartsWith("mouth") && !m.name.Contains("Twin") && !m.name.Contains("Radiant") && m.name.Length < 16)) { Diag.DumpMatValues(sb, m); if (++k > 2) break; }
            });
            Try(sb, "faces", () =>
            {
                foreach (var nm in new[] { "Pink", "Batty", "Phosphor" })
                {
                    var d = CottonLibrary.Library.GetSlime(nm);
                    var f = d?.AppearancesDefault[0]?.Face;
                    sb.AppendLine($"== FACE {nm} {N(f)}");
                    if (f == null) continue;
                    foreach (var (ex, e, m) in Faces.Read(f)) sb.AppendLine($"   {ex}: eyes={N(e)}({e?.shader?.name}) mouth={N(m)}({m?.shader?.name})");
                }
            });
            Try(sb, "fx", () =>
            {
                foreach (var n in new[] { "gold_sparkles", "FX Stars", "DazedStars", "RadiantAuraVFX", "FX Hyper Slime Trail", "FX Actor Trail", "FX_ShadowTrailParticle" })
                {
                    var go = Fx.Get(n);
                    sb.AppendLine("== FX " + n + " " + (go == null ? "MISSING" : ""));
                    if (go == null) continue;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        var r = ps.GetComponent<ParticleSystemRenderer>();
                        sb.AppendLine($"  PS {ps.name} dur={main.duration} loop={main.loop} life={main.startLifetimeMultiplier} size={main.startSize.m_ConstantMin}/{main.startSize.m_ConstantMax}/{main.startSizeMultiplier} mode={main.startSize.mode} speed={main.startSpeed.constant} max={main.maxParticles} space={main.simulationSpace} col={C(main.startColor.color)} rate={ps.emission.rateOverTimeMultiplier} dist={ps.emission.rateOverDistanceMultiplier} shapeR={ps.shape.radius} sheet={ps.textureSheetAnimation.enabled}:{ps.textureSheetAnimation.numTilesX}x{ps.textureSheetAnimation.numTilesY} mode={r?.renderMode} mat={N(r?.sharedMaterial)}");
                        if (r?.sharedMaterial != null) Diag.DumpMatValues(sb, r.sharedMaterial);
                    }
                    foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) { sb.AppendLine($"  TRAIL {tr.name} time={tr.time} w={tr.widthMultiplier} mat={N(tr.sharedMaterial)}"); if (tr.sharedMaterial != null) Diag.DumpMatValues(sb, tr.sharedMaterial); }
                }
            });
            Try(sb, "hud", () =>
            {
                var em = Object.FindObjectOfType<EnergyMeter>();
                var hm = Object.FindObjectOfType<HealthMeter>();
                var rm = Resources.FindObjectsOfTypeAll<RadMeter>();
                sb.AppendLine($"== HUD energy={N(em)} health={N(hm)} radMeters={rm.Length}");
                foreach (var r in rm) sb.AppendLine($"   RADMETER {N(r)} scene={r.gameObject.scene.name} path={Path(r.transform)} act={r.gameObject.activeInHierarchy}");
                if (em != null)
                {
                    var root = em.transform.parent != null ? em.transform.parent : em.transform;
                    if (root.parent != null) root = root.parent;
                    sb.AppendLine("ROOT " + Path(root));
                    Ui(sb, root, "", 0);
                }
            });
            Try(sb, "foods", () =>
            {
                foreach (var n in new[] { "OcaocaVeggie", "ParsnipVeggie", "LemonFruit", "GingerVeggie", "BeetVeggie", "PogoFruit" })
                {
                    var t = Resources.FindObjectsOfTypeAll<IdentifiableType>().FirstOrDefault(x => x != null && x.name == n);
                    sb.AppendLine($"== FOOD {n} {(t == null ? "MISSING" : "")}");
                    if (t == null) continue;
                    sb.AppendLine($"   ref={t.referenceId} prefab={N(t.prefab)} icon={N(t.icon)} name={(t.localizedName == null ? "null" : t.localizedName.TableEntryReference.Key)} col={C(t.color)} veg={CottonLibrary.Library.veggies?.IsMember(t)} fruit={CottonLibrary.Library.fruits?.IsMember(t)} food={CottonLibrary.Library.food?.IsMember(t)} groupType={N(t.groupType)} pedia={t._pediaPersistenceSuffix}");
                    if (t.prefab != null) Diag.DumpHierarchy(sb, t.prefab.transform, "   ", 0, true);
                    if (t.prefab != null) foreach (var r in t.prefab.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) Diag.DumpMatValues(sb, m);
                }
            });
            Try(sb, "gardens", () =>
            {
                int k = 0;
                foreach (var g in Resources.FindObjectsOfTypeAll<GardenCatcher>())
                {
                    if (g == null) continue;
                    sb.AppendLine($"== GARDEN {Path(g.transform)} scene={g.gameObject.scene.name} slots={g.Plantable?.Length}");
                    if (k++ > 0) continue;
                    foreach (var s in g.Plantable) sb.AppendLine($"   {N(s.IdentType)} -> {N(s.PlantedPrefab)} / {N(s.DeluxePlantedPrefab)}");
                    var beet = g.Plantable.FirstOrDefault(s => s.IdentType != null && s.IdentType.name == "BeetVeggie") ?? g.Plantable.FirstOrDefault();
                    var pogo = g.Plantable.FirstOrDefault(s => s.IdentType != null && s.IdentType.name == "PogoFruit");
                    foreach (var s in new[] { beet, pogo })
                    {
                        if (s?.PlantedPrefab == null) continue;
                        Diag.DumpHierarchy(sb, s.PlantedPrefab.transform, "   ", 0, true);
                        foreach (var sr in s.PlantedPrefab.GetComponentsInChildren<SpawnResource>(true)) Grower(sb, sr._resourceGrowerDefinition);
                    }
                }
            });
            Try(sb, "growers", () =>
            {
                var list = GameContext.Instance.AutoSaveDirector._configuration._resourceGrowers;
                sb.AppendLine($"== GROWERLIST {N(list)} count={list?.items?.Count}");
                if (list?.items != null) foreach (var d in list.items) sb.AppendLine($"   {N(d)} id={d?._persistenceId} prim={N(d?._primaryResourceType)} prefab={N(d?._prefab)}");
                var seen = 0;
                foreach (var sr in Resources.FindObjectsOfTypeAll<SpawnResource>())
                {
                    if (sr == null || !sr.gameObject.scene.IsValid() || sr._landPlot != null) continue;
                    if (seen++ > 60) break;
                    var d = sr._resourceGrowerDefinition;
                    sb.AppendLine($"WILD {sr.gameObject.scene.name}/{sr.name} def={N(d)} [{(d?._resources == null ? "" : string.Join(",", d._resources.Select(e => N(e.ResourceIdentifiableType) + ":" + e.Weight)))}]");
                }
            });
            System.IO.File.WriteAllText(System.IO.Path.Combine(Diag.Dir, "diagx.txt"), sb.ToString());
            ClassicSlimesMod.Log.Msg($"[diag] wrote diagx.txt ({sb.Length} chars)");
        }

        static void Grower(StringBuilder sb, ResourceGrowerDefinition d)
        {
            if (d == null) { sb.AppendLine("   GROWER null"); return; }
            sb.AppendLine($"   GROWER {d.name} id={d._persistenceId} prim={N(d._primaryResourceType)} prefab={N(d._prefab)} min={d._minResources} max={d._maxResources} maxActive={d._maxActiveSpawns} int={d._minSpawnIntervalGameHours}-{d._maxSpawnIntervalGameHours} res=[{(d._resources == null ? "" : string.Join(",", d._resources.Select(e => N(e.ResourceIdentifiableType) + ":" + e.Weight + "/" + e.MinimumAmount)))}]");
        }

        public static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static void Ui(StringBuilder sb, Transform t, string ind, int depth)
        {
            var s = new StringBuilder();
            s.Append($"{ind}{t.name} act={t.gameObject.activeSelf}");
            var rt = t.TryCast<RectTransform>();
            if (rt != null) s.Append($" pos={V2(rt.anchoredPosition)} size={V2(rt.sizeDelta)} amin={V2(rt.anchorMin)} amax={V2(rt.anchorMax)} piv={V2(rt.pivot)} scl={rt.localScale.x:0.##}");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn; try { tn = c.GetIl2CppType().Name; } catch { tn = "?"; }
                if (tn == "RectTransform" || tn == "CanvasRenderer") continue;
                s.Append(" [" + tn);
                var img = c.TryCast<Image>(); if (img != null) s.Append($" spr={N(img.sprite)} col={C(img.color)} type={img.type} fill={img.fillMethod}/{img.fillAmount:0.##} mat={N(img.material)}");
                var tmp = c.TryCast<TMP_Text>(); if (tmp != null) s.Append($" text='{tmp.text}' font={N(tmp.font)} size={tmp.fontSize} col={C(tmp.color)}");
                var lg = c.TryCast<HorizontalOrVerticalLayoutGroup>(); if (lg != null) s.Append($" spacing={lg.spacing}");
                var sbar = c.TryCast<StatusBar>(); if (sbar != null) s.Append($" img={N(sbar.statusImage)} behind={N(sbar.statusImageBehind)} label={N(sbar._label)} min={sbar.minValue} max={sbar.maxValue} cur={sbar.currValue} fmt={sbar._valueFormat}");
                s.Append("]");
            }
            sb.AppendLine(s.ToString());
            if (depth > 7) return;
            for (int i = 0; i < t.childCount; i++) Ui(sb, t.GetChild(i), ind + "  ", depth + 1);
        }
    }
}
