using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Persist;

namespace ClassicSlimes
{
    /// <summary>
    /// Guard against missing slime types in radiant-slime save data: if a save references a slime type that is not
    /// registered (mod removed, a slime disabled in config, etc.), null keys break RadiantSlimesModel.Push.
    /// Log and prune unregistered keys from dictionary blocks before Push runs.
    /// </summary>
    [HarmonyPatch(typeof(RadiantSlimesModel), nameof(RadiantSlimesModel.Push))]
    static class RadiantPushDiag
    {
        static bool Known(ILoadReferenceTranslation lookups, int id)
        {
            try { return lookups.GetIdentifiableType(id) != null; } catch { return false; }
        }

        static string Name(ILoadReferenceTranslation lookups, int id)
        {
            try { return lookups.GetIdentifiableReferenceId(id) ?? ("#" + id); } catch { return "#" + id; }
        }

        static void Prefix(RadiantSlimesV03 persistence, ILoadReferenceTranslation lookups)
        {
            if (persistence == null || lookups == null) return;
            var unknown = new List<string>();
            var badCollected = new List<int>();
            var badBags = new List<int>();
            try
            {
                if (persistence.RadiantsCollected != null)
                {
                    foreach (var kv in persistence.RadiantsCollected)
                        if (!Known(lookups, kv.Key)) { badCollected.Add(kv.Key); unknown.Add("collected:" + Name(lookups, kv.Key)); }
                    foreach (var k in badCollected) persistence.RadiantsCollected.Remove(k);
                }
                if (persistence.RadiantShuffleBags != null)
                {
                    foreach (var kv in persistence.RadiantShuffleBags)
                        if (!Known(lookups, kv.Key)) { badBags.Add(kv.Key); unknown.Add("bag:" + Name(lookups, kv.Key)); }
                    foreach (var k in badBags) persistence.RadiantShuffleBags.Remove(k);
                }
                var lostIds = persistence.LostRadiantIdQueue;
                var lostTimes = persistence.LostRadiantTimeQueue;
                if (lostIds != null)
                {
                    for (int i = lostIds.Count - 1; i >= 0; i--)
                    {
                        int id = lostIds[i];
                        if (Known(lookups, id)) continue;
                        unknown.Add("lost:" + Name(lookups, id));
                        lostIds.RemoveAt(i);
                        if (lostTimes != null && i < lostTimes.Count) lostTimes.RemoveAt(i);
                    }
                }
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Radiant save guard: " + e.Message); }
            if (unknown.Count > 0)
                ClassicSlimesMod.Log.Warning($"Radiant-slime save data pruned {unknown.Count} unregistered slime type(s): {string.Join(", ", unknown)}");
        }
    }

    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector), nameof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector.UnlockRadiantPediaEntry))]
    static class RadiantUnlockPediaGuard
    {
        static Exception Finalizer(Exception __exception) => null;
    }

    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant), nameof(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant.SetRadiantAppearance))]
    static class SlimeRadiantAppearanceSaveGuard
    {
        static bool Prefix(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant __instance, ref Il2Cpp.SlimeDefinition radiantBaseSlimeDef)
        {
            try
            {
                if (__instance == null) return true;
                var def = __instance.GetComponent<Il2Cpp.Identifiable>()?.identType?.TryCast<Il2Cpp.SlimeDefinition>();
                if (def == null) return true;
                if (!def.IsLargo)
                {
                    if (def.RadiantBase != null) return true;
                    if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
                    return false;
                }
                if (radiantBaseSlimeDef != null)
                {
                    Il2Cpp.SlimeAppearance cur = null;
                    try { cur = def.GetRadiantAppearance(radiantBaseSlimeDef); } catch { }
                    if (cur != null) return true;
                }
                var bases = def.BaseSlimes;
                if (bases != null)
                {
                    for (int i = 0; i < bases.Length; i++)
                    {
                        var bs = bases[i];
                        if (bs == null) continue;
                        Il2Cpp.SlimeAppearance alt = null;
                        try { alt = def.GetRadiantAppearance(bs); } catch { }
                        if (alt != null)
                        {
                            radiantBaseSlimeDef = bs;
                            if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = bs;
                            return true;
                        }
                    }
                }
                if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
                return false;
            }
            catch { return true; }
        }

        static Exception Finalizer(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant __instance, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                if (__instance != null && __instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
            }
            catch { }
            return null;
        }
    }

    [HarmonyPatch(typeof(GameModelPullHelpers), nameof(GameModelPullHelpers.PullActors))]
    static class PullActorsGlitchRevertPatch
    {
        static void Prefix()
        {
            try { GlitchSlime.RevertAllForSave(); }
            catch { }
        }
    }
}
