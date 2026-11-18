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
                if (persistence.LostRadiantIdQueue != null)
                    foreach (var id in persistence.LostRadiantIdQueue) if (!Known(lookups, id)) unknown.Add("lost:" + Name(lookups, id));
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Radiant save guard: " + e.Message); }
            if (unknown.Count > 0)
                ClassicSlimesMod.Log.Warning($"Radiant-slime save data pruned/found {unknown.Count} unregistered slime type(s): {string.Join(", ", unknown)}");
        }
    }

    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector), nameof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector.UnlockRadiantPediaEntry))]
    static class RadiantUnlockPediaGuard
    {
        static Exception Finalizer(Exception __exception) => null;
    }
}
