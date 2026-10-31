using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Damage;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClassicSlimes
{
    public static class Diag
    {
        public static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "ClassicSlimes");
        public static bool Enabled => Cfg.Diagnostics.Value || File.Exists(Path.Combine(Dir, "diag.enabled"));

        static string N(UnityEngine.Object o) => o == null ? "null" : o.name;
        static string TN(Il2CppSystem.Object o) { try { return o == null ? "null" : o.GetIl2CppType().Name; } catch { return "?"; } }
        static string C(Color c) => $"({c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###})";
        static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

        static void Write(string file, StringBuilder sb)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, file), sb.ToString());
            ClassicSlimesMod.Log.Msg($"[diag] wrote {file} ({sb.Length} chars)");
        }

        static void Try(StringBuilder sb, string what, Action a)
        {
            try { a(); } catch (Exception e) { sb.AppendLine($"!! {what} failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static void Static()
        {
            var sb = new StringBuilder();
            var shaders = new HashSet<Shader>();
            var gc = GameContext.Instance;
            var defs = gc.SlimeDefinitions.Slimes;
            sb.AppendLine("== SLIMES " + defs.Length);
            SlimeDefinition pink = null;
            foreach (var d in defs)
            {
                if (d == null) continue;
                if (d.name == "Pink") pink = d;
                Try(sb, "def " + d.name, () => DumpDef(sb, d, shaders));
            }
            Write("slimes.txt", sb);

            // prefab hierarchies
            sb = new StringBuilder();
            HashSet<string> pinkSet = null;
            if (pink != null)
            {
                sb.AppendLine("== PINK PREFAB");
                Try(sb, "pink hierarchy", () => DumpHierarchy(sb, pink.prefab.transform, "", 0, true));
                pinkSet = CompSet(pink.prefab);
            }
            foreach (var d in defs)
            {
                if (d == null || d.IsLargo || d.prefab == null || d == pink) continue;
                Try(sb, "diff " + d.name, () =>
                {
                    var set = CompSet(d.prefab);
                    var extra = pinkSet == null ? set.ToList() : set.Where(x => !pinkSet.Contains(x)).ToList();
                    var missing = pinkSet == null ? new List<string>() : pinkSet.Where(x => !set.Contains(x)).ToList();
                    sb.AppendLine($"== {d.name} prefab={d.prefab.name} +[{string.Join("; ", extra)}] -[{string.Join("; ", missing)}]");
                });
            }
            // a couple of interesting prefabs fully
            foreach (var nm in new[] { "Phosphor", "Hyper", "Sloomber", "Crystal", "Puddle", "Fire", "Twin", "Boom", "Angler" })
            {
                var d = defs.FirstOrDefault(x => x != null && x.name == nm);
                if (d?.prefab == null) continue;
                sb.AppendLine("== FULL " + nm);
                Try(sb, "full " + nm, () => DumpHierarchy(sb, d.prefab.transform, "", 0, true));
            }
            // modules
            Try(sb, "modules", () =>
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go == null || !go.name.StartsWith("moduleSlime")) continue;
                    if (go.transform.parent != null) continue;
                    sb.AppendLine($"== MODULE {go.name} comps=[{string.Join(",", go.GetComponentsInChildren<Component>(true).Select(c => TN(c)))}]");
                }
            });
            Write("prefabs.txt", sb);

            // shaders used by slimes
            sb = new StringBuilder();
            foreach (var sh in shaders)
            {
                if (sh == null) continue;
                Try(sb, "shader " + sh.name, () =>
                {
                    sb.Append($"== SHADER {sh.name} rq={sh.renderQueue} :");
                    int n = sh.GetPropertyCount();
                    for (int i = 0; i < n; i++) sb.Append($" {sh.GetPropertyName(i)}:{sh.GetPropertyType(i)}");
                    sb.AppendLine();
                });
            }
            if (pink != null)
            {
                foreach (var st in pink.AppearancesDefault[0]._structures)
                    foreach (var m in st.DefaultMaterials) Try(sb, "matvals", () => DumpMatValues(sb, m));
            }
            foreach (var nm in new[] { "Phosphor", "Hyper", "Sloomber", "Crystal", "Twin", "Gold", "Puddle", "Fire", "Shadow" })
            {
                var d = defs.FirstOrDefault(x => x != null && x.name == nm);
                if (d == null) continue;
                foreach (var st in d.AppearancesDefault[0]._structures)
                    foreach (var m in st.DefaultMaterials) Try(sb, "matvals", () => DumpMatValues(sb, m));
            }
            Write("shaders.txt", sb);

            // all shaders + materials
            sb = new StringBuilder();
            Try(sb, "allshaders", () =>
            {
                var all = Resources.FindObjectsOfTypeAll<Shader>().Where(s => s != null).Select(s => s.name).Distinct().OrderBy(s => s).ToList();
                sb.AppendLine("== ALL SHADERS " + all.Count);
                foreach (var s in all) sb.AppendLine(s);
            });
            Try(sb, "allmats", () =>
            {
                var mats = Resources.FindObjectsOfTypeAll<Material>().Where(m => m != null).ToList();
                sb.AppendLine("== ALL MATERIALS " + mats.Count);
                var seen = new HashSet<string>();
                foreach (var m in mats.OrderBy(m => m.name))
                {
                    string sn = m.shader != null ? m.shader.name : "null";
                    string key = m.name + "|" + sn;
                    if (!seen.Add(key)) continue;
                    string kw = "";
                    try { kw = string.Join(" ", m.shaderKeywords); } catch { }
                    sb.AppendLine($"{m.name} | {sn} | rq={m.renderQueue} | {kw}");
                }
            });
            Write("materials.txt", sb);

            // identifiables & groups
            sb = new StringBuilder();
            Try(sb, "groups", () =>
            {
                foreach (var g in Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>().OrderBy(g => g.name))
                {
                    var mem = new List<string>();
                    if (g._memberTypes != null) foreach (var t in g._memberTypes) mem.Add(N(t));
                    var sub = new List<string>();
                    if (g._memberGroups != null) foreach (var t in g._memberGroups) sub.Add(N(t));
                    sb.AppendLine($"GROUP {g.name}: [{string.Join(",", mem)}] sub=[{string.Join(",", sub)}]");
                }
            });
            Try(sb, "idents", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<IdentifiableType>().OrderBy(t => t.name))
                    sb.AppendLine($"IDENT {t.name} type={TN(t)} plort={t.IsPlort} ref={t.referenceId} prefab={N(t.prefab)} icon={N(t.icon)}");
            });
            Try(sb, "damage", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<DamageSourceDefinition>()) sb.AppendLine($"DMG {t.name} fx={N(t._playerDamageFX)} cue={N(t._playerDamageCue)}");
            });
            Try(sb, "mixers", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<UnityEngine.Audio.AudioMixerGroup>()) sb.AppendLine($"MIXER {t.name} mixer={N(t.audioMixer)}");
            });
            Try(sb, "glints", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<GlintAppearance>()) sb.AppendLine($"GLINTAPP {t.name} {N(t.SuspendedGlintPrefab)} {N(t.ReadyGlintPrefab)} {N(t.FreeGlintPrefab)}");
            });
            Try(sb, "cues", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<SECTR_AudioCue>().OrderBy(x => x.name)) sb.AppendLine($"CUE {t.name}");
            });
            Try(sb, "pedia", () =>
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.IdentifiablePediaEntry>().OrderBy(x => x.name)) sb.AppendLine($"PEDIA {t.name}");
            });
            Try(sb, "fxprefabs", () =>
            {
                var names = new HashSet<string>();
                foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
                {
                    if (ps == null) continue;
                    var root = ps.transform.root.gameObject;
                    if (root.scene.IsValid()) continue;
                    names.Add(root.name);
                }
                foreach (var n in names.OrderBy(x => x)) sb.AppendLine("FX " + n);
            });
            Try(sb, "lights", () =>
            {
                foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (l == null) continue;
                    var root = l.transform.root.gameObject;
                    if (root.scene.IsValid()) continue;
                    sb.AppendLine($"LIGHT {root.name}/{l.name} type={l.type} range={l.range} int={l.intensity} col={C(l.color)} comps=[{string.Join(",", l.GetComponents<Component>().Select(c => TN(c)))}]");
                }
            });
            Write("idents.txt", sb);
        }

        static void DumpDef(StringBuilder sb, SlimeDefinition d, HashSet<Shader> shaders)
        {
            sb.AppendLine($"# {d.name} largo={d.IsLargo} canLargo={d.CanLargofy} scale={d.PrefabScale} prefab={N(d.prefab)} ref={d.referenceId} color={C(d.color)} pedia={d._pediaPersistenceSuffix}");
            if (d.IsLargo) return;
            var di = d.Diet;
            if (di != null)
            {
                sb.AppendLine($"  diet groups=[{J(di.MajorFoodIdentifiableTypeGroups)}] fav=[{J(di.FavoriteIdents)}] add=[{J(di.AdditionalFoodIdents)}] prod=[{J(di.ProduceIdents)}] favCount={di.FavoriteProductionCount} edPlort={N(di.EdiblePlortIdentifiableTypeGroup)} eatmap={di.EatMap?.Count}");
            }
            sb.AppendLine($"  toys=[{J(d.FavoriteToyIdents)}] base={N(d.BaseModule)} modules=[{J(d.SlimeModules)}] zones=[{J(d.NativeZones)}] sounds={N(d.Sounds)}");
            if (d.AppearancesDefault == null) return;
            foreach (var app in d.AppearancesDefault)
            {
                if (app == null) continue;
                var p = app._colorPalette;
                sb.AppendLine($"  app {app.name} face={N(app._face)} qubit={N(app._qubitAppearance)} shocked={N(app._shockedAppearance)} glint={N(app._glintAppearance)} crystal={N(app._crystalAppearance)} explosion={N(app._explosionAppearance)} splat={C(app._splatColor)} pal T{C(p.Top)} M{C(p.Middle)} B{C(p.Bottom)} A{C(p.Ammo)} icon={N(app._icon)}");
                if (app._structures == null) continue;
                foreach (var s in app._structures)
                {
                    if (s == null) { sb.AppendLine("   struct null"); continue; }
                    var e = s.Element;
                    var mats = new List<string>();
                    if (s.DefaultMaterials != null)
                        foreach (var m in s.DefaultMaterials) { mats.Add(N(m) + "/" + (m != null && m.shader != null ? m.shader.name : "-")); if (m != null && m.shader != null) shaders.Add(m.shader); }
                    sb.AppendLine($"   struct el={N(e)} type={(e != null ? e.Type.ToString() : "-")} inherit={(e != null ? e.MaterialInheritance.ToString() : "-")} faces={s.SupportsFaces} mats=[{string.Join(", ", mats)}] elMats={s.ElementMaterials?.Length}");
                    if (e?.Prefabs == null) continue;
                    foreach (var po in e.Prefabs) DumpSAO(sb, po);
                }
            }
        }

        static string J<T>(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T> arr) where T : UnityEngine.Object
        {
            if (arr == null) return "";
            return string.Join(",", arr.Select(x => N(x)));
        }

        static void DumpSAO(StringBuilder sb, SlimeAppearanceObject po)
        {
            if (po == null) { sb.AppendLine("    sao null"); return; }
            var bones = new List<string>();
            if (po.AttachedBones != null) foreach (var b in po.AttachedBones) bones.Add(b.ToString());
            sb.AppendLine($"    sao {po.name} root={po.RootBone} parent={po.ParentBone} attached=[{string.Join(",", bones)}] lod={po.LODIndex} ignoreLod={po.IgnoreLODIndex} pos={V(po.transform.localPosition)} scale={V(po.transform.localScale)} comps=[{string.Join(",", po.GetComponents<Component>().Select(c => TN(c)))}]");
            foreach (var r in po.GetComponentsInChildren<Renderer>(true))
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                Mesh mesh = null;
                if (smr != null) mesh = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                string mi = "nomesh";
                if (mesh != null)
                {
                    int bp = -1; try { bp = mesh.bindposes.Length; } catch { }
                    mi = $"{mesh.name} vc={mesh.vertexCount} sub={mesh.subMeshCount} readable={mesh.isReadable} bindposes={bp} bounds=c{V(mesh.bounds.center)} e{V(mesh.bounds.extents)}";
                }
                string bi = smr != null ? $" smrBones={smr.bones?.Length} rootBone={N(smr.rootBone)}" : "";
                sb.AppendLine($"     rend {TN(r)} {r.name} {mi}{bi} mats=[{string.Join(",", r.sharedMaterials.Select(m => N(m)))}] shadows={r.shadowCastingMode}");
            }
        }

        static HashSet<string> CompSet(GameObject go)
        {
            var set = new HashSet<string>();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                set.Add(TPath(c.transform, go.transform) + ":" + TN(c));
            }
            return set;
        }

        static string TPath(Transform t, Transform root)
        {
            if (t == root) return ".";
            var parts = new List<string>();
            while (t != null && t != root) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void DumpHierarchy(StringBuilder sb, Transform t, string indent, int depth, bool details)
        {
            var comps = t.GetComponents<Component>();
            var cn = new List<string>();
            foreach (var c in comps)
            {
                string s = TN(c);
                if (details)
                {
                    try
                    {
                        var cb = c.TryCast<CalmedByWaterSpray>(); if (cb != null) s += $"(red={cb.AgitationReduction},hours={cb.CalmedHours})";
                        var col = c.TryCast<SphereCollider>(); if (col != null) s += $"(r={col.radius},trig={col.isTrigger},c={V(col.center)})";
                        var rb = c.TryCast<Rigidbody>(); if (rb != null) s += $"(mass={rb.mass},drag={rb.drag})";
                        var l = c.TryCast<Light>(); if (l != null) s += $"(type={l.type},range={l.range},int={l.intensity},col={C(l.color)})";
                        var au = c.TryCast<AudioSource>(); if (au != null) s += $"(mixer={N(au.outputAudioMixerGroup)},clip={N(au.clip)},spatial={au.spatialBlend})";
                        var dm = c.TryCast<Il2CppMonomiPark.SlimeRancher.Damage.Damager>(); if (dm != null) s += $"(amt={dm.DamageAmount},src={N(dm.DamageSource)})";
                    }
                    catch { }
                }
                cn.Add(s);
            }
            sb.AppendLine($"{indent}{t.name} act={t.gameObject.activeSelf} layer={t.gameObject.layer} pos={V(t.localPosition)} scl={V(t.localScale)} [{string.Join(", ", cn)}]");
            if (depth > 8) return;
            for (int i = 0; i < t.childCount; i++) DumpHierarchy(sb, t.GetChild(i), indent + "  ", depth + 1, details);
        }

        internal static void DumpMatValues(StringBuilder sb, Material m)
        {
            if (m == null) return;
            var sh = m.shader;
            sb.Append($"== MAT {m.name} shader={sh?.name} rq={m.renderQueue} kw=[{string.Join(" ", m.shaderKeywords)}] :");
            if (sh != null)
            {
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    string pn = sh.GetPropertyName(i);
                    var pt = sh.GetPropertyType(i);
                    string v;
                    switch (pt)
                    {
                        case ShaderPropertyType.Color: v = C(m.GetColor(pn)); break;
                        case ShaderPropertyType.Vector: { var x = m.GetVector(pn); v = $"({x.x:0.###},{x.y:0.###},{x.z:0.###},{x.w:0.###})"; break; }
                        case ShaderPropertyType.Texture: v = N(m.GetTexture(pn)); break;
                        case ShaderPropertyType.Int: v = m.GetInt(pn).ToString(); break;
                        default: v = m.GetFloat(pn).ToString("0.###"); break;
                    }
                    sb.Append($" {pn}={v}");
                }
            }
            sb.AppendLine();
        }

        public static void Runtime()
        {
            var sb = new StringBuilder();
            var sc = SceneContext.Instance;
            Try(sb, "scenes", () =>
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    sb.AppendLine("SCENE " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
            });
            Try(sb, "player", () =>
            {
                var p = sc.Player;
                sb.AppendLine("== PLAYER " + N(p));
                if (p != null) DumpHierarchy(sb, p.transform, "", 0, true);
                var ra = p.GetComponentInChildren<PlayerRadAbsorber>(true);
                sb.AppendLine($"RADABSORBER {(ra != null ? ra.name + " src=" + N(ra._damageSource) + " audio=" + N(ra.RadAudio) : "none")} rad={sc.PlayerState.GetCurrRad()} hp={sc.PlayerState.GetCurrHealth()}/{sc.PlayerState.GetMaxHealth()}");
            });
            Try(sb, "spawners", () =>
            {
                var bySc = new Dictionary<string, int>();
                foreach (var sp in Resources.FindObjectsOfTypeAll<DirectedSlimeSpawner>())
                {
                    if (sp == null || !sp.gameObject.scene.IsValid()) continue;
                    string scn = sp.gameObject.scene.name;
                    bySc.TryGetValue(scn, out int cnt);
                    bySc[scn] = cnt + 1;
                    if (cnt >= 4) continue;
                    sb.AppendLine($"SPAWNER {scn}/{sp.name} r={sp.Radius}");
                    foreach (var c in sp.Constraints)
                    {
                        var mem = c.Slimeset?.Members;
                        string ms = mem == null ? "" : string.Join(",", mem.Select(x => N(x.IdentType) + ":" + x.Weight.ToString("0.##")));
                        sb.AppendLine($"   c w={c.Weight} mode={c.Window?.TimeMode} feral={c.Feral} [{ms}]");
                    }
                }
                foreach (var kv in bySc) sb.AppendLine($"SPAWNERCOUNT {kv.Key} {kv.Value}");
            });
            Write("runtime.txt", sb);
        }
    }
}
