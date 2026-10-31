using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Persist;

namespace ClassicSlimes
{
    /// <summary>
    /// Read-only diagnostics: SR2 builds a dictionary keyed by slime type while loading the radiant-slime data. If a save
    /// references a slime type that is not registered (mod removed, a modded largo whose saving failed to set up, ...),
    /// the key is null and loading of that block fails. This prefix only LOGS such entries (the save is not modified),
    /// so the responsible slime type can be identified.
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
            try
            {
                if (persistence.RadiantsCollected != null)
                    foreach (var kv in persistence.RadiantsCollected) if (!Known(lookups, kv.Key)) unknown.Add("collected:" + Name(lookups, kv.Key));
                if (persistence.RadiantShuffleBags != null)
                    foreach (var kv in persistence.RadiantShuffleBags) if (!Known(lookups, kv.Key)) unknown.Add("bag:" + Name(lookups, kv.Key));
                if (persistence.LostRadiantIdQueue != null)
                    foreach (var id in persistence.LostRadiantIdQueue) if (!Known(lookups, id)) unknown.Add("lost:" + Name(lookups, id));
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Radiant save diag: " + e.Message); }
            if (unknown.Count > 0)
                ClassicSlimesMod.Log.Warning($"Radiant-slime save data references {unknown.Count} unregistered slime type(s): {string.Join(", ", unknown)}");
        }
    }
}
