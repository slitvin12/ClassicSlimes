using System;
using System.Collections;
using CottonLibrary;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ClassicSlimes.ClassicSlimesMod), "Classic Slimes", "1.3.7", "ClassicSlimes")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: MelonAdditionalDependencies("CottonLibrary")]

namespace ClassicSlimes
{
    public class ClassicSlimesMod : CottonModInstance<ClassicSlimesMod>
    {
        public static MelonLogger.Instance Log;
        static bool _created, _postLoaded;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Cfg.Init();
            Visuals.DomeStyle = Cfg.RadDomeStyle.Value;
            try { Behaviours.Register(); }
            catch (Exception e) { Log.Error("Register types failed: " + e); }
            try { Library.AddLanguages("localized"); }
            catch (Exception e) { Log.Error("AddLanguages failed: " + e); }
            Log.Msg("Classic Slimes initialized");
        }

        public override void SaveDirectorLoading(Il2CppMonomiPark.SlimeRancher.AutoSaveDirector saveDirector)
        {
            // Covers every Cotton mod's SaveDirectorLoaded and all largo generation (same frame), see Gc.
            Gc.Pause("menu: slime and largo generation");
            MelonCoroutines.Start(ResumeNextFrame());
        }

        static IEnumerator ResumeNextFrame()
        {
            yield return null;
            Gc.Resume("largo generation finished");
            Log.Msg("Largo generation finished");
        }

        public override void SaveDirectorLoaded()
        {
            if (_created) return;
            _created = true;
            if (Diag.Enabled)
            {
                try { Diag.Static(); } catch (Exception e) { Log.Error("Diag.Static failed: " + e); }
            }
            try { Foods.Setup(); }
            catch (Exception e) { Log.Error("Foods.Setup failed: " + e); }
            LargoLook.Register();
            try { SlimeFactory.CreateAll(); }
            catch (Exception e) { Log.Error("CreateAll failed: " + e); }

            try { Gordos.CreateAll(); }
            catch (Exception e) { Log.Error("Gordos.CreateAll failed: " + e); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName == "MainMenuEnvironment")
            {
                try { PediaArt.ClearSession(); } catch { }
                try { Gordos.ClearSession(); } catch { }
                try { Spawns.ClearSession(); } catch { }
                try { Foods.ClearSession(); } catch { }
                try { GordoSnareBaitPatch.ClearSession(); } catch { }
                try { Swirl.ClearSession(); } catch { }
                try { Behaviours.ClearSession(); } catch { }
            }
            try { Gordos.OnSceneLoaded(sceneName); }
            catch (Exception e) { Log.Error("Gordos.OnSceneLoaded failed: " + e); }
        }

        public override void OnSavedGameLoaded()
        {
            if (!_postLoaded)
            {
                _postLoaded = true;
                try { Gc.Window("largo post-processing", SlimeFactory.PostProcessLargos); }
                catch (Exception e) { Log.Error("PostProcessLargos failed: " + e); }
                try { Gc.Window("largo diets", Foods.RefreshLargoDiets); }
                catch (Exception e) { Log.Error("RefreshLargoDiets failed: " + e); }
            }
            try { Foods.EnsurePediaDirectorLinks(); } catch { }
            try { PediaArt.RegisterPersistence(); } catch { }
            try { Behaviours.AttachToLive(); } catch (Exception e) { Log.Error("AttachToLive failed: " + e); }
            if (Diag.Enabled) MelonCoroutines.Start(RuntimeDiag());
        }

        public override void OnUpdate()
        {
            try { Mat.Tick(); } catch (Exception e) { Log.Warning("Mat.Tick: " + e.Message); }
            RadHud.AudioTick();
            RadHud.Tick();
            try { GordoFacets.Tick(); } catch { }
            try { PediaArt.Tick(); } catch { }
            if (Diag.Enabled) DevCmd.Tick();
        }

        public override void PreGameSaving()
        {
            try { GlitchSlime.RevertAllForSave(); } catch (Exception e) { Log.Warning("PreGameSaving: " + e.Message); }
        }

        static IEnumerator RuntimeDiag()
        {
            float t = 0f;
            while (t < 8f) { t += Time.unscaledDeltaTime; yield return null; }
            try { Diag.Runtime(); } catch (Exception e) { Log.Error("Diag.Runtime failed: " + e); }
            try { DiagX.Run(); } catch (Exception e) { Log.Error("DiagX failed: " + e); }
            try { Gallery.DumpProps(); } catch (Exception e) { Log.Error("Gallery.DumpProps failed: " + e); }
        }
    }
}
