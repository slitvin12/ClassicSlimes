using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// Developer hooks (only with UserData/ClassicSlimes/diag.enabled): write commands into
    /// UserData/ClassicSlimes/cmd.txt, they are executed on the main thread and the file is deleted.
    /// gallery | clear | qs [seconds] | glint | ghost | glitch | time hour | lineup [dist] [seconds] | kill
    /// locs | goto group|location [x y z] | census | hp | rad [amount] | radx (expand every rad aura now)
    /// fx name [scale] [dist] | gtex mat prop gameTex | dprof mat srcMat | qsdash | gblink | gburst [sec] | gmask i|off [flash]
    /// </summary>
    public static class DevCmd
    {
        static float _next;
        static readonly List<(Rigidbody rb, float until)> _frozen = new();
        static readonly List<(Transform t, Vector3 a, Vector3 b, float t0, float dur)> _sweeps = new();

        static readonly List<(Transform t, Rigidbody rb, float until, float next)> _tracks = new();

        public static void Tick()
        {
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                var k = _tracks[i];
                if (k.t == null || Time.time > k.until) { _tracks.RemoveAt(i); continue; }
                if (Time.time < k.next) continue;
                _tracks[i] = (k.t, k.rb, k.until, Time.time + 0.25f);
                try { Track(k.t, k.rb); } catch (Exception e) { _tracks.RemoveAt(i); L("track: " + e.Message); }
            }
            for (int i = _sweeps.Count - 1; i >= 0; i--)
            {
                var s = _sweeps[i];
                float k = (Time.time - s.t0) / s.dur;
                if (s.t == null || k > 1f) { _sweeps.RemoveAt(i); continue; }
                s.t.position = Vector3.Lerp(s.a, s.b, k) + Vector3.up * (Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * 0.35f);
            }
            for (int i = _frozen.Count - 1; i >= 0; i--)
            {
                if (Time.time < _frozen[i].until) continue;
                try { if (_frozen[i].rb != null) _frozen[i].rb.isKinematic = false; } catch { }
                _frozen.RemoveAt(i);
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var path = Path.Combine(Diag.Dir, "cmd.txt");
            if (!File.Exists(path)) return;
            string[] lines;
            try { lines = File.ReadAllLines(path); File.Delete(path); } catch { return; }
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.Length == 0) continue;
                try { Run(l); ClassicSlimesMod.Log.Msg("[cmd] " + l); }
                catch (Exception e) { ClassicSlimesMod.Log.Error($"[cmd] {l}: {e}"); }
            }
        }

        static float F(string[] a, int i, float def) =>
            a.Length > i ? float.Parse(a[i], CultureInfo.InvariantCulture) : def;

        static void Run(string l)
        {
            var a = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (a[0].ToLowerInvariant())
            {
                case "gallery": Clear(); Gallery.Show(); break;
                case "clear": Clear(); break;
                case "qs":
                    foreach (var q in UnityEngine.Object.FindObjectsOfType<QuicksilverSlime>()) q.Overcharge(F(a, 1, 30f));
                    break;
                case "plex": Plex(F(a, 1, 25f)); break;
                case "glint":
                    foreach (var m in UnityEngine.Object.FindObjectsOfType<MosaicSlime>()) m.SpawnGlint(3f);
                    break;
                case "ghost":
                    foreach (var q in UnityEngine.Object.FindObjectsOfType<QuantumSlime>()) q.SpawnGhost(q.transform.position + new Vector3(U.Rand(-2f, 2f), 0.5f, U.Rand(-2f, 2f)));
                    break;
                case "glitch":
                    foreach (var g in UnityEngine.Object.FindObjectsOfType<GlitchSlime>()) g.ForceDisguise();
                    break;
                case "time":
                    var td = SceneContext.Instance.TimeDirector;
                    td.FastForwardTo(td.GetNextHour(F(a, 1, 12f)));
                    break;
                case "lineup": Lineup(F(a, 1, 4f), F(a, 2, 8f)); break;
                case "kill":
                    foreach (var go in AllClassic()) Destroyer.DestroyActor(go, "ClassicSlimes.DevCmd", true);
                    break;
                case "locs": Locs(); break;
                case "goto": Goto(a); break;
                case "census": Census(); break;
                case "hp":
                    var ps = SceneContext.Instance.PlayerState;
                    ClassicSlimesMod.Log.Msg($"[cmd] hp={ps.GetCurrHealth()}/{ps.GetMaxHealth()} rad={ps.GetCurrRad()}");
                    break;
                case "dumpx": DiagX.Run(); break;
                case "flt": foreach (var m in Tune(a[1])) m.SetFloat(a[2], F(a, 3, 0f)); break;
                case "col": foreach (var m in Tune(a[1])) m.SetColor(a[2], new Color(F(a, 3, 0f), F(a, 4, 0f), F(a, 5, 0f), F(a, 6, 1f))); break;
                case "kw": foreach (var m in Tune(a[1])) { if (a[3] == "on") m.EnableKeyword(a[2]); else m.DisableKeyword(a[2]); } break;
                case "mats": ClassicSlimesMod.Log.Msg("[cmd] mats: " + string.Join(", ", Visuals.Mats.Keys)); break;
                case "pose": Pose(a.Length > 1 ? a[1] : "Rad", F(a, 2, 2.6f), F(a, 3, 10f), F(a, 4, -0.45f), F(a, 5, 0f)); break;
                case "vac": Vac(a.Length < 2 || a[1] != "on"); break;
                case "face": Face(a); break;   // face yaw [pitch] | face x y z (look at a point)
                case "domes": Domes(); break;
                case "dome": Visuals.DomeStyle = (int)F(a, 1, 0f); RadSlime.RestyleAll(); break;
                case "radx": foreach (var r in RadSlime.All) if (r != null) r.ForceExpand(); break;
                case "texdump": TexDump(a[1], a.Length > 2 ? a[2] : "_MainTex"); break;
                case "glitchface":
                    {
                        var sp = SlimeFactory.Spec("Glitch");
                        var f = Visuals.GlitchFace(a.Length < 2 || a[1] != "sr2");
                        if (sp?.Def != null && f != null) { sp.Def.AppearancesDefault[0]._face = f; GlitchSlime.ReapplyAll(); }
                        break;
                    }
                case "radhud": RadHud.Offset = new Vector2(F(a, 1, 0f), F(a, 2, 78f)); RadHud.Place(); break;
                case "foods":
                    foreach (var fs in Foods.All) L($"food {fs.Ident} type={(fs.Type != null)} name={(fs.Type?.localizedName != null ? fs.Type.localizedName.GetLocalizedString() : "-")} veg={Foods.Ready}");
                    foreach (var kv in Foods.Added) L($"  wild {kv.Key} x{kv.Value}");
                    break;
                case "garden": L(Foods.GardenInfo()); break;   // plantable state of the nearest garden (SR1 foods + carrot / pogo)
                case "homes": L(Spawns.HomesInfo()); break;   // home spawners of the classic slimes, nearest first
                case "gordos": L(Gordos.Info()); break;   // gordos of the loaded scenes, nearest first -> gordos.txt
                case "gordotree": L(Gordos.Tree(a.Length > 1 ? a[1] : "near")); break;   // gordotree [near|Rad|PinkGordo]: hierarchy + materials -> gordotree.txt
                case "gordohere": L(Gordos.Here(a.Length > 1 ? a[1] : "Rad", F(a, 2, 8f))); break;   // gordohere [slime] [dist]: test gordo on the ground ahead
                case "gordoat": L(Gordos.At(a.Length > 4 ? a[4] : "Rad", new Vector3(F(a, 1, 0f), F(a, 2, 0f), F(a, 3, 0f)), F(a, 5, float.NaN))); break;   // gordoat x y z [slime] [yaw]
                case "gordoclear": L(Gordos.ClearTests()); break;   // remove the test gordos and their models
                case "gordospots": L(Gordos.Candidates(a.Length > 1 ? a[1] : "Rad", (int)F(a, 2, 6f))); break;   // gordospots [slime] [n]: candidate spots -> gordospots.txt
                case "gordofeed": L(Gordos.Feed((int)F(a, 1, 1f))); break;   // gordofeed [left]: the nearest gordo needs only `left` more foods
                case "gordofood": L(Gordos.Food(a.Length > 1 ? a[1] : "-", (int)F(a, 2, 1f))); break;   // gordofood [ident|-] [n]: drop foods into its mouth
                case "gordoreset": L(Gordos.Reset(a.Length > 1 ? a[1] : null)); break;   // gordoreset [id]: un-burst our gordos
                case "savegame": GameContext.Instance.AutoSaveDirector.SaveGameAndFlush(); L("savegame: saved"); break;
                case "foodspots": L(Foods.FoodSpots()); break;   // wild SR1 food patches / trees, nearest first
                case "market": L(Market.Info()); break;   // classic plort prices + market board slots
                case "marketui": L(Market.DumpUI()); break;
                case "marketlock": L(Market.Locks()); break;
                case "sellplort": L(Market.Sell(a.Length > 1 ? a[1] : "RadPlort", (int)F(a, 2, 1f))); break;   // sellplort [ident] [n]: drop plorts into the nearest market intake   // which board entries are unlocked (pedia ident map)   // market board hierarchy -> marketui.txt
                case "pediashot": PediaShot.Start(a[1], F(a, 2, 25f), F(a, 3, 12f), F(a, 4, 0.6f), a.Length > 5 ? a[5] : null); break;   // pediashot ident [yaw] [tilt] [fill] [name]
                case "pedialist": L(PediaShot.List()); break;
                case "pediaclose":   // close the slimepedia (after `pedia`)
                    { int n = 0; foreach (var r in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.UI.Pedia.PediaRoot>()) if (r != null && r.gameObject.activeInHierarchy) { r.StartCoroutine(r.ClosePedia()); n++; } L("pediaclose: " + n); }
                    break;
                case "pediaimgs": L(PediaArt.Dump()); break;   // visible images of the open slimepedia -> pediaimgs.txt
                case "lemontree": L(Foods.LemonTree(a.Length > 1 ? a[1] : "")); break;   // lemontree [ripen|hit]: nearest lemon tree, phasing lemons
                case "plant": L(Foods.DevPlant(a.Length > 1 ? a[1] : "LemonFruit")); break;   // plant [ident]: plant into the nearest garden
                case "matinfo": MatInfo(a[1]); break;
                case "facedump":
                    {
                        var sp = SlimeFactory.Spec(a.Length > 1 ? a[1] : "Glitch");
                        var face = sp?.Def?.AppearancesDefault?[0]?._face;
                        L($"facedump {sp?.Name} face={(face != null ? face.name : "null")} healed={Faces.Healed} blocked={DestroyGuard.Blocked} destroyedMats={Mat.Destroyed}");
                        if (face != null)
                            foreach (var (ex, e, mo) in Faces.Read(face))
                                L($"  {ex}: eyes={FN(e)} mouth={FN(mo)}");
                        break;
                    }
                case "props": Props(a[1]); break;
                case "rinfo": RInfo(a.Length > 1 ? a[1] : "Rad"); break;
                case "revive": Mat.Tick(true); L("revived total " + Mat.Revived); break;
                case "killall":
                    {
                        var cam = Camera.main; int n = 0;
                        foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
                        {
                            if (id == null || cam == null || id.identType == null || id.identType.TryCast<SlimeDefinition>() == null) continue;
                            if ((id.transform.position - cam.transform.position).sqrMagnitude > F(a, 1, 20f) * F(a, 1, 20f)) continue;
                            Destroyer.DestroyActor(id.gameObject, "ClassicSlimes.DevCmd", true); n++;
                        }
                        L("killall " + n);
                        break;
                    }
                case "rad":
                    SceneContext.Instance.PlayerState.AddRads(F(a, 1, 50f));
                    break;
                case "pedia": Pedia(a.Length > 1 ? a[1] : "Rad"); break;
                case "texst":
                    foreach (var m in Tune(a[1]))
                    {
                        m.SetTextureScale(a[2], new Vector2(F(a, 3, 1f), F(a, 4, 1f)));
                        m.SetTextureOffset(a[2], new Vector2(F(a, 5, 0f), F(a, 6, 0f)));
                    }
                    if (a[1] == "Quantum" && a[2] == "_ColorMask") { Gfx.SwirlScale = new Vector2(F(a, 3, 1f), F(a, 4, 1f)); Gfx.SwirlOffset = new Vector2(F(a, 5, 0f), F(a, 6, 0f)); }
                    break;
                case "filetex":   // filetex <matKey> <prop> <file in UserData/ClassicSlimes without .png>
                    {
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = "ClassicSlimes_file_" + a[3] };
                        Il2CppImageConversionManager.LoadImage(t, File.ReadAllBytes(Path.Combine(Diag.Dir, a[3] + ".png")));
                        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
                        t.hideFlags = HideFlags.HideAndDontSave;
                        foreach (var m in Tune(a[1])) m.SetTexture(a[2], t);
                        break;
                    }
                case "sweep":   // sweep <def> <span m> <seconds>: slide the nearest (posed, frozen) slime sideways across the view
                    {
                        var cam = Camera.main; if (cam == null) break;
                        GameObject best = null; float bd = float.MaxValue;
                        foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
                        {
                            var t = id != null ? id.identType : null;
                            if (t == null || !string.Equals(t.name, a[1], StringComparison.OrdinalIgnoreCase)) continue;
                            float d = (id.transform.position - cam.transform.position).sqrMagnitude;
                            if (d < bd) { bd = d; best = id.gameObject; }
                        }
                        if (best == null) { L("sweep: no " + a[1]); break; }
                        var r = cam.transform.right * (F(a, 2, 3f) * 0.5f); var p0 = best.transform.position;
                        _sweeps.Add((best.transform, p0 - r, p0 + r, Time.time, F(a, 3, 3f)));
                        break;
                    }
                case "nodisguise": GlitchSlime.DevNoDisguise = a.Length < 2 || a[1] != "off"; break;
                case "restex": foreach (var m in Tune(a[1])) m.SetTexture(a[2], Res.Tex(a[3], true, !(a.Length > 4 && a[4] == "clamp"))); break;
                case "vec": foreach (var m in Tune(a[1])) m.SetVector(a[2], new Vector4(F(a, 3, 0f), F(a, 4, 0f), F(a, 5, 0f), F(a, 6, 0f))); break;
                case "palette": Palettes(); break;
                case "phase":   // phase [sec] [hold]: hologram phase of the nearest quantum (hold = no solid flicker-ins)
                    {
                        var cam = Camera.main; var q = cam != null ? Nearest<QuantumSlime>(cam.transform.position) : null;
                        QuantumSlime.DevHold = a.Length > 2 && a[2] == "hold";
                        if (q != null) q.GetComponent<QuantumSlime>().StartPhase(F(a, 1, 1f)); else L("no quantum slime");
                        break;
                    }
                case "qtp":   // nearest quantum swaps reality with one of its ghosts right now
                    {
                        var cam = Camera.main; var q = cam != null ? Nearest<QuantumSlime>(cam.transform.position) : null;
                        if (q != null) q.GetComponent<QuantumSlime>().DevTeleport(); else L("no quantum slime");
                        break;
                    }
                case "walls":
                    {
                        var cam = Camera.main; var q = cam != null ? Nearest<QuantumSlime>(cam.transform.position) : null;
                        var qs = q != null ? q.GetComponent<QuantumSlime>() : null;
                        L($"walls={QuantumSlime.WallCount} nearest quantum: " + (qs == null ? "none" :
                            $"phasing={qs.Phasing} hunger={U.Hunger(q.GetComponent<Il2CppMonomiPark.SlimeRancher.Slime.SlimeEmotions>()):0.00} dist={(q.transform.position - cam.transform.position).magnitude:0.0}"));
                        break;
                    }
                case "fx":   // fx <name> [scale] [dist]: play a game FX prefab in front of the camera
                    {
                        var cam = Camera.main; if (cam == null) break;
                        var go = Fx.Play(a[1], cam.transform.position + cam.transform.forward * F(a, 3, 2.5f), F(a, 2, 1f));
                        L($"fx {a[1]}: " + (go != null ? "ok" : "not found"));
                        break;
                    }
                case "gtex":   // gtex <matKey> <prop> <game texture>
                    {
                        var t = Visuals.GameTex(a[3]);
                        foreach (var m in Tune(a[1])) m.SetTexture(a[2], t);
                        L($"gtex {a[3]}: " + (t != null ? t.width + "x" + t.height : "not found"));
                        break;
                    }
                case "dprof":   // dprof <matKey> <source material>: copy SR2's diffusion profile (subsurface tint)
                    {
                        var src = Mat.Find(a[2]);
                        if (src == null || !src.HasProperty("_DiffusionProfile")) { L("dprof: no " + a[2]); break; }
                        foreach (var m in Tune(a[1]))
                        {
                            m.SetFloat("_DiffusionProfile", src.GetFloat("_DiffusionProfile"));
                            if (src.HasProperty("_DiffusionProfile_Asset")) m.SetVector("_DiffusionProfile_Asset", src.GetVector("_DiffusionProfile_Asset"));
                        }
                        break;
                    }
                case "qsdash":   // nearest quicksilver dashes now
                    {
                        var cam = Camera.main; var q = cam != null ? Nearest<QuicksilverSlime>(cam.transform.position) : null;
                        if (q != null) q.GetComponent<QuicksilverSlime>().DevDash(); else L("no quicksilver slime");
                        break;
                    }
                case "qsinfo":   // runner state of the nearest quicksilver
                    {
                        var cam = Camera.main; var q = cam != null ? Nearest<QuicksilverSlime>(cam.transform.position) : null;
                        L(q != null ? "qsinfo " + q.GetComponent<QuicksilverSlime>().DevInfo() : "no quicksilver slime");
                        break;
                    }
                case "qsaim":   // qsaim [seconds]: pretend the vacpack sucks along the view (quicksilvers jink and tire out)
                    QuicksilverSlime.DevAimUntil = Time.time + F(a, 1, 5f); break;
                case "gblink":   // nearest glitch blink-teleports now
                    {
                        var cam = Camera.main; var g = cam != null ? Nearest<GlitchSlime>(cam.transform.position) : null;
                        if (g != null) g.GetComponent<GlitchSlime>().Blink(true); else L("no glitch slime");
                        break;
                    }
                case "gburst": GlitchSlime.TriggerBurst(F(a, 1, 0.5f)); break;
                case "rdist":   // rdist <template material|off> [diameter relative to the aura]: extra sphere in every rad aura (key RadDist)
                    RadSlime.DevExtra(a[1], F(a, 2, 0.98f)); break;
                case "dspin": Visuals.DomeSpin = F(a, 1, 9f); break;
                case "rq": foreach (var m in Tune(a[1])) m.renderQueue = (int)F(a, 2, -1f); break;
                case "bodymat":   // bodymat <def> <template|key|=key|off> [key]: body material of the live slimes of a type
                    L("bodymat " + Experiments.BodyMat(a[1], a[2], a.Length > 3 ? a[3] : a[1] + "Body2")); break;
                case "bshell":   // bshell <def> <template|key|=key|off> [key] [inflate] [rq] [facets]: elastic shell from the body mesh
                    L("bshell " + Experiments.Shell(a[1], a[2], a.Length > 3 ? a[3] : a[1] + "Shell", F(a, 4, 0.02f), (int)F(a, 5, 0f), (int)F(a, 6, 0f))); break;
                case "qspiral":   // qspiral <template|key|=key|off> [key] [rq]: inner 3D spiral in the live quantum slimes
                    L("qspiral " + Experiments.QSpiral(a[1], a.Length > 2 ? a[2] : "QSpiral", (int)F(a, 3, 0f))); break;
                case "qsp":   // qsp r0 r1 turns tube z y cup startDeg cw dome: spiral shape (then qspiral default again)
                    Spiral3D.R0 = F(a, 1, Spiral3D.R0); Spiral3D.R1 = F(a, 2, Spiral3D.R1); Spiral3D.Turns = F(a, 3, Spiral3D.Turns);
                    Spiral3D.Tube = F(a, 4, Spiral3D.Tube); Spiral3D.Z = F(a, 5, Spiral3D.Z); Spiral3D.Y = F(a, 6, Spiral3D.Y);
                    Spiral3D.Cup = F(a, 7, Spiral3D.Cup); Spiral3D.StartDeg = F(a, 8, Spiral3D.StartDeg);
                    Spiral3D.Clockwise = F(a, 9, Spiral3D.Clockwise ? 1f : 0f) > 0.5f;
                    Spiral3D.Dome = F(a, 10, Spiral3D.Dome);
                    L("qsp " + Spiral3D.Info); break;
                case "gflee":   // nearest glitch runs away now
                    {
                        var cam = Camera.main; var g = cam != null ? Nearest<GlitchSlime>(cam.transform.position) : null;
                        if (g != null) g.GetComponent<GlitchSlime>().StartFlee(); else L("no glitch slime");
                        break;
                    }
                case "gvac":   // gvac <on|off|throw> [speed]: fake vacpack on the nearest glitch (caught -> released / shot away)
                    {
                        var cam = Camera.main; var g = cam != null ? Nearest<GlitchSlime>(cam.transform.position) : null;
                        var v = g != null ? g.GetComponent<Il2CppMonomiPark.SlimeRancher.Vacuumable>() : null;
                        if (v == null) { L("no glitch slime"); break; }
                        string mode = a.Length > 1 ? a[1] : "on";
                        var rb = g.GetComponent<Rigidbody>();
                        if (mode == "on") { v.SetHeld(true); if (rb != null) rb.isKinematic = true; }   // no vac joint here: pin it instead
                        else
                        {
                            if (rb != null) rb.isKinematic = false;
                            v.SetHeld(false);
                            if (mode == "throw")
                            {
                                v.Launch(Il2CppMonomiPark.SlimeRancher.Vacuumable.LaunchSource.PLAYER);
                                var dir = g.transform.position - cam.transform.position; dir.y = 0f;
                                if (rb != null) rb.linearVelocity = dir.normalized * F(a, 2, 10f) + Vector3.up * 3f;
                            }
                        }
                        L($"gvac {mode}: held {v.IsHeld()} launched {v.IsLaunched()}");
                        break;
                    }
                case "qnophase":   // qnophase [off]: no quantum hologram phases / glitch twitches (steady screenshots)
                    QuantumSlime.DevNoPhase = a.Length < 2 || a[1] != "off"; L("qnophase " + QuantumSlime.DevNoPhase); break;
                case "qspot":   // qspot x y z [size]: quantum spiral centre (body heights from the body centre) and size
                    {
                        QuantumSlime.SpiralOfs = new Vector3(F(a, 1, QuantumSlime.SpiralOfs.x), F(a, 2, QuantumSlime.SpiralOfs.y), F(a, 3, QuantumSlime.SpiralOfs.z));
                        QuantumSlime.SpiralSize = F(a, 4, QuantumSlime.SpiralSize);
                        string info = "";
                        foreach (var q in UnityEngine.Object.FindObjectsOfType<QuantumSlime>()) { q.ReplaceSpiral(); info = q.SpiralInfo(); }
                        L($"qspot {QuantumSlime.SpiralOfs} size {QuantumSlime.SpiralSize} | {info}");
                        break;
                    }
                case "tree":   // tree <def> [depth]: transform hierarchy of the nearest slime of a type
                    {
                        var go = NearestDef(a[1]);
                        if (go == null) { L("tree: no " + a[1]); break; }
                        Tree(go.transform, 0, (int)F(a, 2, 3f));
                        break;
                    }
                case "mglass": // mglass on|off: diagnostics/performance baseline; does not alter the body
                    ElasticGlassRig.Show = a.Length < 2 || a[1] != "off";
                    L("elastic glass " + ElasticGlassRig.Show + $"; all rigs {ElasticGlassRig.TotalMs:F2} ms");
                    break;
                case "iris":   // iris [speed intensity fresnelPower normalStrength]: rainbow shimmer of the mosaic glass
                    {
                        Visuals.MosaicIrisSpeed = F(a, 1, Visuals.MosaicIrisSpeed);
                        var g = Visuals.MosaicElasticGlassMaterial();
                        if (g != null)
                        {
                            if (a.Length > 2) g.SetFloat("_ReflectionIntensity", F(a, 2, Visuals.MosaicIrisIntensity));
                            if (a.Length > 3) g.SetFloat("_ReflectionFresnelPower", F(a, 3, Visuals.MosaicIrisFresnel));
                            if (a.Length > 4) g.SetFloat("_ReflectionNormalStrength", F(a, 4, Visuals.MosaicIrisNormal));
                            L($"iris speed {Visuals.MosaicIrisSpeed} intensity {g.GetFloat("_ReflectionIntensity")} fresnel {g.GetFloat("_ReflectionFresnelPower")} normal {g.GetFloat("_ReflectionNormalStrength")}");
                        }
                    }
                    break;
                case "mjelly":   // mjelly [gain lean stretch freq damp inertia]: glass-slime squash & stretch (+ info of the nearest mosaic / quantum)
                    {
                        JellyRig.Gain = F(a, 1, JellyRig.Gain); JellyRig.MaxLean = F(a, 2, JellyRig.MaxLean); JellyRig.MaxStretch = F(a, 3, JellyRig.MaxStretch);
                        JellyRig.Freq = F(a, 4, JellyRig.Freq); JellyRig.Damp = F(a, 5, JellyRig.Damp); JellyRig.Inertia = F(a, 6, JellyRig.Inertia);
                        var cam = Camera.main;
                        var ms = cam != null ? Nearest<MosaicSlime>(cam.transform.position) : null;
                        var qs = cam != null ? Nearest<QuantumSlime>(cam.transform.position) : null;
                        L($"mjelly gain {JellyRig.Gain} lean {JellyRig.MaxLean} stretch {JellyRig.MaxStretch} freq {JellyRig.Freq} damp {JellyRig.Damp} inertia {JellyRig.Inertia}");
                        L("mjelly mosaic: " + (ms != null ? ms.GetComponent<MosaicSlime>().JellyInfo() : "-"));
                        L("mjelly quantum: " + (qs != null ? qs.GetComponent<QuantumSlime>().JellyInfo() : "-"));
                        break;
                    }
                case "track":   // track <def> [seconds]: log position / speed / ground contact of the nearest slime of a type
                    {
                        var go = NearestDef(a[1]);
                        if (go == null) { L("track: no " + a[1]); break; }
                        _tracks.Add((go.transform, go.GetComponent<Rigidbody>(), Time.time + F(a, 2, 4f), 0f));
                        break;
                    }
                case "mshell":   // mshell on|off: the separate SR1 glass shell renderers of the live mosaic slimes
                    {
                        int n = 0;
                        foreach (var ms in UnityEngine.Object.FindObjectsOfType<MosaicSlime>())
                            foreach (var r in ms.GetComponentsInChildren<Renderer>(true))
                                if (r != null && r.gameObject.name.StartsWith("mosaic_shell")) { r.enabled = a.Length < 2 || a[1] != "off"; n++; }
                        L($"mshell {n} renderers");
                        break;
                    }
                case "mcoat":   // mcoat <template material|off> [key]: second (coat) material on the live mosaic body renderers
                    {
                        string key = a.Length > 2 ? a[2] : "MosaicCoat";
                        Material coat = null;
                        if (a[1] != "off")
                        {
                            coat = Visuals.Mats.TryGetValue(key, out var have) && have != null && a[1] == "keep" ? have : Mat.Clone(key, a[1]);
                            if (coat == null) { L("mcoat: no " + a[1]); break; }
                            coat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                            Visuals.Mats[key] = coat;
                        }
                        int n = 0;
                        foreach (var ms in UnityEngine.Object.FindObjectsOfType<MosaicSlime>())
                            foreach (var r in ms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                var mats = r.sharedMaterials;
                                if (mats == null || mats.Length == 0 || mats[0] == null || !mats[0].name.Contains("MosaicBody")) continue;
                                r.sharedMaterials = new Il2CppReferenceArray<Material>(coat != null ? new[] { mats[0], coat } : new[] { mats[0] });
                                n++;
                            }
                        L($"mcoat {a[1]} -> {n} renderers ({(coat != null ? coat.shader.name : "-")})");
                        break;
                    }
                case "wobble":   // wobble <def> [x y z]: kick SR2's squash / stretch of the nearest slime of that type
                    {
                        var go = NearestDef(a[1]);
                        var rb = go != null ? go.GetComponentInChildren<RubberBoneEffectShader>() : null;
                        if (rb == null) { L("wobble: no rubber bone on " + a[1]); break; }
                        var kick = new Vector3(F(a, 2, 0f), F(a, 3, 3f), F(a, 4, 0f));
                        rb.AddImpulseForce(kick);
                        var mj = go.GetComponent<MosaicSlime>(); var qj = go.GetComponent<QuantumSlime>();
                        var jr = mj != null ? mj.Jelly : qj != null ? qj.Jelly : null;
                        if (jr != null && jr.Ok) { jr.Kick(kick * 0.3f); L("wobble jelly " + jr.Info()); }
                        break;
                    }
                case "qswirl":   // qswirl [dx dy dz radius turns tip width endDeg cw uv]: re-bake the quantum spiral (+ qswirl*.png)
                    {
                        Swirl.Dir = new Vector3(F(a, 1, Swirl.Dir.x), F(a, 2, Swirl.Dir.y), F(a, 3, Swirl.Dir.z));
                        Swirl.Radius = F(a, 4, Swirl.Radius); Swirl.Turns = F(a, 5, Swirl.Turns); Swirl.Tip = F(a, 6, Swirl.Tip);
                        Swirl.Width = F(a, 7, Swirl.Width); Swirl.EndDeg = F(a, 8, Swirl.EndDeg);
                        Swirl.Clockwise = F(a, 9, Swirl.Clockwise ? 1f : 0f) > 0.5f; Swirl.UvSet = (int)F(a, 10, Swirl.UvSet);
                        var t = Swirl.Bake(Swirl.BodyMesh, true);
                        if (t != null)
                            foreach (var m in Tune("Quantum"))
                            {
                                m.SetTexture("_ColorMask", t);
                                m.SetTextureScale("_ColorMask", Vector2.one); m.SetTextureOffset("_ColorMask", Vector2.zero);
                                m.SetFloat("_ColorMaskUV1", Swirl.LastSet);
                            }
                        L("qswirl " + Swirl.Info);
                        break;
                    }
                case "gtrail":   // gtrail y1 y2 ytrail ypix [time1 w1 time2 w2]: glitch trail layout on the live slimes
                    GlitchSlime.RibbonY = F(a, 1, GlitchSlime.RibbonY); GlitchSlime.Ribbon2Y = F(a, 2, GlitchSlime.Ribbon2Y);
                    GlitchSlime.TrailY = F(a, 3, GlitchSlime.TrailY); GlitchSlime.PixelY = F(a, 4, GlitchSlime.PixelY);
                    GlitchSlime.RibbonTime = F(a, 5, GlitchSlime.RibbonTime); GlitchSlime.RibbonW = F(a, 6, GlitchSlime.RibbonW);
                    GlitchSlime.Ribbon2Time = F(a, 7, GlitchSlime.Ribbon2Time); GlitchSlime.Ribbon2W = F(a, 8, GlitchSlime.Ribbon2W);
                    GlitchSlime.DevTrail();
                    break;
                case "gmask":   // gmask <0-2 pixel | 3-7 tear | off> [flash]: hold one glitch body mask
                    GlitchSlime.DevMask = a.Length > 1 && a[1] != "off" ? (int)F(a, 1, 0f) : -1;
                    GlitchSlime.DevFlash = a.Length > 2 && a[2] == "flash";
                    break;
                default: ClassicSlimesMod.Log.Warning("[cmd] unknown: " + l); break;
            }
        }

        /// <summary>
        /// Materials to tune: the registered one, every material with exactly that name (name prefix with a trailing '*'),
        /// and the live copies SR2's appearance code makes of a registered material ("X(Clone)(Clone)..").
        /// </summary>
        static List<Material> Tune(string key)
        {
            var r = new List<Material>();
            bool contains = key.Length > 2 && key.StartsWith("*") && key.EndsWith("*");   // *part*: name contains part
            if (contains) key = key.Substring(1);
            bool prefix = key.Length > 1 && key.EndsWith("*");
            if (prefix) key = key.Substring(0, key.Length - 1);
            if (Visuals.Mats.TryGetValue(key, out var m) && m != null) r.Add(m);
            var clone = m != null ? m.name + "(Clone)" : null;
            foreach (var x in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (x == null || r.Contains(x)) continue;
                var n = x.name;
                if (n == key || (prefix && (contains ? n.Contains(key, StringComparison.Ordinal) : n.StartsWith(key, StringComparison.Ordinal)))
                    || (clone != null && n.StartsWith(clone, StringComparison.Ordinal))) r.Add(x);
            }
            if (r.Count == 0) ClassicSlimesMod.Log.Warning("[cmd] no material " + key);
            return r;
        }

        static List<GameObject> AllClassic()
        {
            var r = new List<GameObject>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<RadSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<QuantumSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<MosaicSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<QuicksilverSlime>()) r.Add(c.gameObject);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<GlitchSlime>()) r.Add(c.gameObject);
            return r;
        }

        /// <summary>The slime / actor of that identifiable type closest to the camera.</summary>
        static void Tree(Transform t, int depth, int max)
        {
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>()) if (c != null && c.TryCast<Transform>() == null) comps.Add(c.GetIl2CppType().Name);
            L($"tree {new string('.', depth * 2)}{t.name} p {t.localPosition} r {t.localEulerAngles} s {t.localScale} [{string.Join(",", comps)}]");
            if (depth >= max) { if (t.childCount > 0) L($"tree {new string('.', depth * 2 + 2)}({t.childCount} children)"); return; }
            for (int i = 0; i < t.childCount; i++) Tree(t.GetChild(i), depth + 1, max);
        }

        static void Track(Transform t, Rigidbody rb)
        {
            var v = rb != null ? rb.velocity : Vector3.zero;
            string extra = "";
            var gl = t.GetComponent<GlitchSlime>(); if (gl != null && gl.Fleeing) extra += " FLEE";
            if (rb != null) extra += $" kin {rb.isKinematic} sleep {rb.IsSleeping()}";
            var pl = U.Player; extra += pl != null ? $" pl {Vector3.Distance(pl.transform.position, t.position):0.0}" : " pl -";
            var cam = Camera.main; if (cam != null) extra += $" cam {Vector3.Distance(cam.transform.position, t.position):0.0}";
            L($"track {t.name} {Time.time:0.00} pos {t.position} hv {new Vector2(v.x, v.z).magnitude:0.0} vy {v.y:0.0} gnd {U.Grounded(t, t.lossyScale.x)} ts {Time.timeScale}{extra}");
        }

        static GameObject NearestDef(string def)
        {
            var cam = Camera.main; if (cam == null) return null;
            var p = cam.transform.position;
            GameObject best = null; float bd = float.MaxValue;
            foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = id != null ? id.identType : null;
                if (t == null || !string.Equals(t.name, def, StringComparison.OrdinalIgnoreCase)) continue;
                float d = (id.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = id.gameObject; }
            }
            return best;
        }

        static GameObject Nearest<T>(Vector3 p) where T : Component
        {
            GameObject best = null; float bd = float.MaxValue;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<T>())
            {
                float d = (c.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c.gameObject; }
            }
            return best;
        }

        /// <summary>Places the nearest slime of every classic type in a row in front of the camera and freezes them.</summary>
        static void Lineup(float dist, float seconds)
        {
            var cam = Camera.main; if (cam == null) return;
            var p = cam.transform.position;
            var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var list = new List<GameObject>
            {
                Nearest<RadSlime>(p), Nearest<QuantumSlime>(p), Nearest<MosaicSlime>(p),
                Nearest<QuicksilverSlime>(p), Nearest<GlitchSlime>(p)
            };
            list.RemoveAll(g => g == null);
            float spacing = 1.7f, start = -(list.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                var pos = p + fwd * dist + right * (start + i * spacing);
                if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                    pos = hit.point + Vector3.up * 0.55f;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { if (!rb.isKinematic) rb.velocity = Vector3.zero; rb.isKinematic = true; _frozen.Add((rb, Time.time + seconds)); }
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            }
        }

        static void L(string m) => ClassicSlimesMod.Log.Msg("[cmd] " + m);

        static void Pedia(string name)
        {
            var spec = SlimeFactory.Spec(name);
            if (spec?.Def == null) { L("no slime " + name); return; }
            var pd = SceneContext.Instance?.PediaDirector;
            if (pd == null) { L("no PediaDirector"); return; }
            var e = pd.GetEntry(spec.Def);
            if (e == null) { L("no pedia entry for " + name); return; }
            pd.Unlock(e, false);
            // already open: switch the page directly (ShowPedia on an open pedia falls back to the first slime)
            var cats = new List<string>();
            foreach (var c in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.PediaCategory>())
                try { if (c != null && c.Contains(e)) cats.Add($"{c.name}:{c.IndexOf(e)}/{c.Count}"); } catch { }
            L($"pedia {name}: unlocked={pd.IsUnlocked(e)} available={e.IsAvailable} categories={string.Join(",", cats)}");
            foreach (var scr in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.UI.Pedia.PediaSlimeAndResourceScreen>())
                if (scr != null && scr.gameObject.activeInHierarchy) { scr.ViewEntry(e); L($"pedia {name}: entry={e.name} (view)"); return; }
            pd.ShowPedia(e, Il2CppMonomiPark.SlimeRancher.Analytics.Event.PediaOpenedAnalyticsEvent.Opener.DEBUG_MENU, null);
            L($"pedia {name}: entry={e.name} art={(PediaArt.For(e) != null ? "ours" : "none")}");
        }

        static void Palettes()
        {
            string c(Color x) => $"({x.r:0.00},{x.g:0.00},{x.b:0.00})";
            foreach (var s in SlimeFactory.Specs)
            {
                if (s.Def?.AppearancesDefault == null) continue;
                foreach (var app in s.Def.AppearancesDefault)
                {
                    if (app == null) continue;
                    var p = app._colorPalette; var n = app.ColorPaletteNew;
                    L($"palette {s.Name}/{app.name}: T{c(p.Top)} M{c(p.Middle)} B{c(p.Bottom)} A{c(p.Ammo)} new T{c(n.Top)} A{c(n.Ammo)} splat{c(app._splatColor)}");
                }
            }
        }

        static string ShInfo(Material m)
        {
            if (m == null) return "<null material>";
            var sh = m.shader;
            if (sh == null) return $"{m.name} shader=<null>";
            return $"{m.name} shader={sh.name}#{sh.GetInstanceID()} ok={sh.isSupported} passes={sh.passCount} rq={m.renderQueue} kw=[{string.Join(" ", (IEnumerable<string>)m.shaderKeywords)}]";
        }

        static void Props(string name)
        {
            Material m = null;
            if (!Visuals.Mats.TryGetValue(name, out m) || m == null)
                foreach (var x in Resources.FindObjectsOfTypeAll<Material>()) if (x != null && x.name == name) { m = x; break; }
            if (m == null || m.shader == null) { L("props: none " + name); return; }
            var sh = m.shader;
            var sb = new System.Text.StringBuilder();
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                var pn = sh.GetPropertyName(i);
                var t = sh.GetPropertyType(i);
                string v;
                switch (t)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: v = m.GetColor(pn).ToString(); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: v = m.GetVector(pn).ToString(); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: var tx = m.GetTexture(pn); v = tx != null ? tx.name : "null"; break;
                    default: v = m.GetFloat(pn).ToString("0.###", CultureInfo.InvariantCulture); break;
                }
                sb.Append(' ').Append(pn).Append('=').Append(v);
            }
            File.WriteAllText(Path.Combine(Diag.Dir, "props_" + name.Replace('/', '_') + ".txt"), ShInfo(m) + "\n" + sb.ToString().Replace(" _", "\n_"));
            L("props written for " + name);
        }

        static void MatInfo(string name)
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && (m.name == name || m.name == "ClassicSlimes_" + name)) L("matinfo " + ShInfo(m) + " id=" + m.GetInstanceID());
            if (Visuals.Mats.TryGetValue(name, out var vm)) L("matinfo key " + ShInfo(vm));
        }

        static void RInfo(string def)
        {
            var cam = Camera.main; if (cam == null) return;
            GameObject best = null; float bd = float.MaxValue;
            foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = id != null ? id.identType : null;
                if (t == null || !string.Equals(t.name, def, StringComparison.OrdinalIgnoreCase)) continue;
                float d = (id.transform.position - cam.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = id.gameObject; }
            }
            if (best == null) { L("rinfo: no " + def); return; }
            foreach (var r in best.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    L($"rinfo {def} {r.gameObject.name} [{r.GetIl2CppType().Name} en={r.enabled} vis={r.isVisible} b={r.bounds.size}] #{i} {ShInfo(mats[i])}");
            }
        }

        /// <summary>Puts the nearest slime of the given definition in front of the camera at eye level and freezes it (for screenshots).</summary>
        static void Pose(string def, float dist, float seconds, float dy, float yaw)
        {
            var cam = Camera.main; if (cam == null) return;
            var p = cam.transform.position;
            GameObject best = null; float bd = float.MaxValue;
            foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = id != null ? id.identType : null;
                if (t == null || !string.Equals(t.name, def, StringComparison.OrdinalIgnoreCase)) continue;
                float d = (id.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = id.gameObject; }
            }
            if (best == null) { L("pose: no " + def); return; }
            // along the real view direction (camera pitch is not always level), facing the camera
            var fwd = cam.transform.forward;
            var up = cam.transform.up;
            var pos = p + fwd * dist + up * dy;
            var rb = best.GetComponent<Rigidbody>();
            if (rb != null) { if (!rb.isKinematic) rb.velocity = Vector3.zero; rb.isKinematic = true; _frozen.Add((rb, Time.time + seconds)); }
            best.transform.position = pos;
            best.transform.rotation = Quaternion.LookRotation(-fwd, up) * Quaternion.Euler(0f, yaw, 0f);
            L($"pose {def} at {pos}");
        }

        static readonly List<Renderer> _hiddenVac = new();
        static void Face(string[] a)
        {
            var cam = Camera.main;
            float yaw = F(a, 1, 0f), pitch = F(a, 2, 0f);
            if (a.Length > 3 && cam != null)
            {
                var d = new Vector3(F(a, 1, 0f), F(a, 2, 0f), F(a, 3, 0f)) - cam.transform.position;
                yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            int n = 0;
            foreach (var cc in UnityEngine.Object.FindObjectsOfType<SRCharacterController>()) { cc.Rotation = Quaternion.Euler(0f, yaw, 0f); n++; }
            foreach (var fc in UnityEngine.Object.FindObjectsOfType<Il2CppMonomiPark.KFC.FirstPerson.FirstPersonCameraController>()) { fc.SetRotation(Quaternion.Euler(pitch, yaw, 0f)); n++; }
            L($"face yaw {yaw:0.0} pitch {pitch:0.0} ({n} controllers)");
        }

        static void Vac(bool hide)
        {
            if (!hide) { foreach (var r in _hiddenVac) if (r != null) r.enabled = true; _hiddenVac.Clear(); return; }
            var cam = Camera.main; if (cam == null) return;
            var root = cam.transform.root;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                if ((r.transform.position - cam.transform.position).sqrMagnitude > 4f) continue;
                r.enabled = false; _hiddenVac.Add(r);
            }
            L($"vac hidden renderers: {_hiddenVac.Count}");
        }

        static GameObject _domes;
        static void Domes()
        {
            if (_domes != null) UnityEngine.Object.Destroy(_domes);
            var cam = Camera.main; if (cam == null) return;
            _domes = new GameObject("ClassicSlimesDomes");
            var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var c = cam.transform.position + fwd * 6.5f + Vector3.down * 0.3f;
            int n = Visuals.DomeNames.Length;
            for (int i = 0; i < n; i++)
            {
                int row = i / 4, col = i % 4;
                var pos = c + right * ((col - 1.5f) * 2.3f) + Vector3.up * (row == 0 ? 1.1f : -1.1f);
                var go = Visuals.MakeDome(_domes.transform, Vector3.zero, 2f, i);
                go.name = "Dome_" + Visuals.DomeNames[i];
                go.transform.position = pos;
                if (i == 0)
                {
                    var core = Visuals.Sphere("Core", _domes.transform, Vector3.zero, 1.2f, Visuals.RadCoreMat);
                    core.transform.position = c + right * 5.9f;
                }
            }
            L("domes: " + string.Join(", ", Visuals.DomeNames) + " + core (right)");
        }

        static void TexDump(string matName, string prop)
        {
            Texture tex = null;
            foreach (var m in Tune(matName)) if (m.HasProperty(prop)) { tex = m.GetTexture(prop); if (tex != null) break; }
            if (tex == null) { foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>()) if (t != null && t.name == matName) { tex = t; break; } }
            if (tex == null) { L("texdump: nothing for " + matName + " " + prop); return; }
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var outT = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outT.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); outT.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var png = Il2CppImageConversionManager.EncodeToPNG(outT);
            var file = Path.Combine(Diag.Dir, "tex_" + tex.name + ".png");
            File.WriteAllBytes(file, png);
            L($"texdump {tex.name} {tex.width}x{tex.height} -> {file}");
        }

        static void Locs()
        {
            L("current group=" + SystemContext.Instance.SceneLoader.CurrentSceneGroup?.name);
            foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                if (g != null && g.IsGameplay) L($"group {g.name} init={g.InitialPlayerPosition}");
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count; i++)
                {
                    var x = d[i];
                    L($"loc {d.name}/{x.name} {x.position} {x.sceneGroup?.name}");
                }
        }

        /// <summary>goto &lt;scene group or editor location&gt; [x y z]: native cross-region teleport.</summary>
        static void Goto(string[] a)
        {
            if (a.Length < 2) return;
            string q = a[1].Replace('_', ' ');
            SceneGroup group = null; Vector3 pos = default; bool havePos = false;
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count && group == null; i++)
                {
                    var x = d[i];
                    if (x.sceneGroup != null && string.Equals(x.name, q, StringComparison.OrdinalIgnoreCase))
                    { group = x.sceneGroup; pos = x.position; havePos = true; }
                }
            if (group == null)
                foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                    if (g != null && g.IsGameplay && g.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) { group = g; break; }
            if (group == null) { L("goto: not found " + q); return; }
            if (a.Length >= 5) { pos = new Vector3(F(a, 2, 0), F(a, 3, 0), F(a, 4, 0)); havePos = true; }
            if (!havePos) pos = group.InitialPlayerPosition;
            if (IPlayerController.TryGet(out var c)) { c.StopAbilitiesForTeleport(); c.ResetVelocity(false); }
            var loader = SystemContext.Instance.SceneLoader;
            var tp = SceneContext.Instance.Player.GetComponentInChildren<TeleportablePlayer>(true);
            L($"goto {group.name} {pos} from {loader.CurrentSceneGroup?.name}");
            if (loader.CurrentSceneGroup != group)
            {
                var prm = new SceneLoadingParameters
                {
                    TeleportPlayer = true, PauseGame = true,
                    TeleportPosition = new Il2CppSystem.Nullable<Vector3> { hasValue = true, value = pos },
                    TeleportRotation = new Il2CppSystem.Nullable<Vector3>(),
                    ReloadAllCoreScenes = false, ReloadAllNonCoreScenes = false
                };
                tp.PrepareForUnmanagedTeleport();
                loader.LoadSceneGroup(group, prm);
            }
            else tp.TeleportTo(pos, group, new Il2CppSystem.Nullable<Vector3>(), false);
        }

        // ---- natural spawn statistics (filled by SpawnLogPatch) ----
        static readonly SortedDictionary<string, int> _spawned = new();
        static int _spawnTotal;

        static string ClassicOf(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            foreach (var s in SlimeFactory.Specs)
                if (n.Contains(s.Name) && !(s.Name == "Rad" && n.Contains("Radiant"))) return s.Name;
            return null;
        }

        static string N(object o) => o is UnityEngine.Object u ? (u == null ? "DEAD" : "ok") : (o == null ? "NULL" : "ok");

        internal static string PlexInfo(Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer p)
        {
            var sb = new System.Text.StringBuilder();
            void F2(string k, Func<object> g) { try { sb.Append(k).Append('=').Append(N(g())).Append(' '); } catch (Exception e) { sb.Append(k).Append("=ERR(").Append(e.GetType().Name).Append(") "); } }
            void B(string k, Func<bool> g) { try { sb.Append(k).Append('=').Append(g() ? 1 : 0).Append(' '); } catch { sb.Append(k).Append("=ERR "); } }
            B("started", () => p._hasStarted);
            F2("meta", () => p._metadata); F2("curr", () => p._currBehavior); F2("vac", () => p._vacuumable);
            F2("col", () => p._ownCollider); F2("sph", () => p._ownSphereCollider); F2("tr", () => p._ownTransform);
            F2("body", () => p._body); F2("emo", () => p._emotions); F2("totem", () => p._colliderTotemLinker);
            F2("chick", () => p._chickenRandomMove); B("hasChick", () => p._hasChickenRandomMove);
            F2("leap", () => p._leapAwayFromPlayer); B("hasLeap", () => p._hasLeapAwayFromPlayer);
            F2("shadow", () => p._shadowSlimeScatter); B("hasShadow", () => p._hasShadowSlimeScatter);
            F2("flee", () => p._fleeThreats); B("hasFlee", () => p._hasFleeThreats);
            B("hasCol", () => p._hasCollider); B("hasSph", () => p._hasSphereCollider); B("blockers>0", () => p._behaviorBlockers > 0);
            try { var m = p._metadata; if (m != null) sb.Append("metaType=").Append(m.GetIl2CppType().Name).Append(' '); } catch { }
            try { var c = p._currBehavior; if (c != null) sb.Append("currType=").Append(c.GetIl2CppType().Name).Append(' '); } catch { }
            try { var arr = p._subbehaviors; int nn = 0; if (arr != null) foreach (var x in arr) if (x == null) nn++; sb.Append("list=").Append(arr == null ? -1 : arr.Length).Append(" dead=").Append(nn).Append(' '); } catch (Exception e) { sb.Append("list=ERR(" + e.GetType().Name + ") "); }
            sb.Append("subs=");
            foreach (var sbh in p.GetComponents<Il2CppMonomiPark.SlimeRancher.Subbehaviour>())
            {
                if (sbh == null) { sb.Append("<null>,"); continue; }
                sb.Append(sbh.GetIl2CppType().Name).Append(sbh.enabled ? "" : "(off)").Append(':');
                try { sb.Append(sbh.Relevancy(true).ToString("0.##")); }
                catch (Exception e) { var m = e.Message ?? ""; int i = m.IndexOf("at ", StringComparison.Ordinal); sb.Append("EXC[").Append(m.Length > 90 ? m.Substring(0, 90) : m).Append(']'); }
                sb.Append(',');
            }
            try { var best = p.GetBestBehaviour(); sb.Append(" best=").Append(best == null ? "NULL" : best.GetIl2CppType().Name); }
            catch (Exception e) { sb.Append(" best=EXC[").Append((e.Message ?? "").Length > 120 ? e.Message.Substring(0, 120) : e.Message).Append(']'); }
            return sb.ToString();
        }

        static void Plex(float radius)
        {
            var pl = SceneContext.Instance.Player.transform.position;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer>())
            {
                if (p == null || Vector3.Distance(pl, p.transform.position) > radius) continue;
                L($"plex {p.gameObject.name}: {PlexInfo(p)}");
            }
        }

        internal static void NoteSpawn(DirectedActorSpawner sp, GameObject obj, IdentifiableType id)
        {
            string n = id != null ? id.name : "null";
            _spawnTotal++;
            _spawned.TryGetValue(n, out int k); _spawned[n] = k + 1;
            if (ClassicOf(n) != null)
            {
                var pl = SceneContext.Instance?.Player;
                float dist = pl != null && obj != null ? Vector3.Distance(pl.transform.position, obj.transform.position) : -1f;
                L($"natural spawn {n} by {sp?.gameObject.scene.name}/{sp?.name} at {obj?.transform.position} dist={dist:0} (total spawns {_spawnTotal})");
            }
        }

        static void Census()
        {
            var pl = SceneContext.Instance.Player.transform.position;
            var byType = new SortedDictionary<string, (int n, float min)>();
            foreach (var ia in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = ia.identType; if (t == null || ClassicOf(t.name) == null) continue;
                float d = Vector3.Distance(pl, ia.transform.position);
                byType.TryGetValue(t.name, out var v);
                byType[t.name] = (v.n + 1, v.n == 0 ? d : Mathf.Min(v.min, d));
            }
            L($"census group={SystemContext.Instance.SceneLoader.CurrentSceneGroup?.name} player={pl}");
            foreach (var kv in byType) L($"  actor {kv.Key} x{kv.Value.n} nearest={kv.Value.min:0}m");
            var sp = new SortedDictionary<string, int>();
            int total = 0, active = 0, withClassic = 0;
            foreach (var s in Resources.FindObjectsOfTypeAll<DirectedSlimeSpawner>())
            {
                if (s == null || !s.gameObject.scene.IsValid() || s.Constraints == null) continue;
                total++; if (s.isActiveAndEnabled) active++;
                bool has = false;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && ClassicOf(m.IdentType.name) != null) has = true;
                if (has) withClassic++;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && ClassicOf(m.IdentType.name) != null)
                            { string key = s.gameObject.scene.name + ":" + m.IdentType.name; sp.TryGetValue(key, out int k); sp[key] = k + 1; }
            }
            L($"  spawners total={total} active={active} withClassic={withClassic}");
            int shown = 0, activeMod = 0, activeSlime = 0;
            foreach (var s in Resources.FindObjectsOfTypeAll<DirectedSlimeSpawner>())
            {
                if (s == null || !s.gameObject.scene.IsValid() || s.Constraints == null || !s.isActiveAndEnabled) continue;
                bool mod = Spawns.Modified.ContainsKey(s.GetInstanceID());
                if (mod) activeMod++;
                bool slimeSet = false;
                foreach (var c in s.Constraints)
                    if (c?.Slimeset?.Members != null)
                        foreach (var m in c.Slimeset.Members)
                            if (m?.IdentType != null && m.IdentType.TryCast<SlimeDefinition>() != null) slimeSet = true;
                if (slimeSet) activeSlime++;
                if (!slimeSet || shown >= 5) continue;
                shown++;
                var parts = new List<string>();
                foreach (var c in s.Constraints)
                {
                    var names = new List<string>();
                    if (c?.Slimeset?.Members != null) foreach (var m in c.Slimeset.Members) names.Add(m?.IdentType != null ? m.IdentType.name + ":" + m.Weight.ToString("0.##") : "null");
                    parts.Add((c != null && c.Feral ? "feral " : "") + string.Join(",", names));
                }
                L($"  spawner {s.GetIl2CppType().Name} {s.gameObject.scene.name}/{s.name} id={s.GetInstanceID()} touched={mod} -> {string.Join(" | ", parts)}");
            }
            L($"  active spawners with slime sets: {activeSlime}/{active}, touched by mod: {activeMod}; modified ids total {Spawns.Modified.Count}; re-added after reset: {Spawns.ReAdded}");
            foreach (var kv in sp) L($"  spawner-members {kv.Key} x{kv.Value}");
            foreach (var kv in Spawns.AddedPerScene) L($"  added-at-awake {kv.Key} x{kv.Value}");
            L($"  natural spawns since start: {_spawnTotal}");
            foreach (var kv in _spawned) if (ClassicOf(kv.Key) != null || kv.Value >= 1) L($"    {kv.Key} x{kv.Value}");
        }

        static string FN(Material m) => (object)m == null ? "ZERO" : m == null ? "DESTROYED" : m.name + (m.shader != null ? "" : " (no shader)");

        static void Clear()
        {
            for (int i = 0; i < 4; i++)
            {
                var g = GameObject.Find("ClassicSlimesGallery");
                if (g == null) break;
                g.name = "x"; UnityEngine.Object.Destroy(g);
            }
        }
    }

    [HarmonyPatch(typeof(DirectedSlimeSpawner), nameof(DirectedSlimeSpawner.OnActorSpawned))]
    static class SpawnLogPatch
    {
        static void Postfix(DirectedSlimeSpawner __instance, GameObject spawnedObj, IdentifiableType id)
        {
            try { if (Diag.Enabled) DevCmd.NoteSpawn(__instance, spawnedObj, id); } catch { }
        }
    }
}
