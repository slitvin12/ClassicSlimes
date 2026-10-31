using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.UI.Pedia;
using UnityEngine;
using UnityEngine.UI;

namespace ClassicSlimes
{
    /// <summary>
    /// Slimepedia picture for our slimes. The cloned pink appearance still points at the Pink Slime full art
    /// (addressable), so the pedia showed a pink slime. SR1's slimepedia used the slime's sticker art, so we put our
    /// icon (or an optional pediaSlimeX.png) into the big picture and keep re-applying it while the page is shown,
    /// because the game loads the full art asynchronously and would overwrite it.
    /// </summary>
    public static class PediaArt
    {
        static readonly Dictionary<IntPtr, Sprite> Art = new();
        // every icon view that currently shows one of our entries (the details page has several of them, e.g. the
        // default and the slime icon highlights; only one is visible) - re-applied every frame while it is active
        static readonly Dictionary<IntPtr, PediaEntryIconAndHighlights> Views = new();
        static readonly List<IntPtr> Drop = new();
        static readonly HashSet<IntPtr> Logged = new();
        static bool _err;

        public static void Register(IdentifiableType ident, Sprite art)
        {
            if (ident == null || art == null) return;
            Art[ident.Pointer] = art;
        }

        public static Sprite For(PediaEntry entry)
        {
            if (entry == null || Art.Count == 0) return null;
            var ie = entry.TryCast<IdentifiablePediaEntry>();
            var id = ie?._identifiableType;
            if (id == null) return null;
            return Art.TryGetValue(id.Pointer, out var sp) ? sp : null;
        }

        public static void Shown(PediaEntryIconAndHighlights view, PediaEntry entry)
        {
            if (view == null) return;
            var sp = For(entry);
            if (sp == null) { Views.Remove(view.Pointer); return; }
            Views[view.Pointer] = view;
            Apply(view, sp);
        }

        static void Apply(PediaEntryIconAndHighlights view, Sprite sp)
        {
            var img = view._primaryIcon;
            if (img == null) return;
            if (img.sprite == null || img.sprite.Pointer != sp.Pointer)
            {
                img.sprite = sp;
                if (Logged.Add(sp.Pointer)) ClassicSlimesMod.Log.Msg($"Pedia art: {sp.name} -> {view.name}");
            }
            img.preserveAspect = true;
            if (!img.enabled) img.enabled = true;
            var c = img.color;
            if (c.a < 1f) img.color = new Color(c.r, c.g, c.b, 1f);
        }

        public static void Tick()
        {
            if (Views.Count == 0) return;
            Drop.Clear();
            foreach (var kv in Views)
            {
                try
                {
                    var v = kv.Value;
                    if (v == null) { Drop.Add(kv.Key); continue; }
                    var sp = For(v._currentEntry);
                    if (sp == null) { Drop.Add(kv.Key); continue; }
                    if (v.isActiveAndEnabled) Apply(v, sp);
                }
                catch (Exception e)
                {
                    Drop.Add(kv.Key);
                    if (!_err) { _err = true; ClassicSlimesMod.Log.Warning("Pedia art: " + e.Message); }
                }
            }
            foreach (var k in Drop) Views.Remove(k);
        }
    }

    [HarmonyPatch(typeof(PediaEntryIconAndHighlights), nameof(PediaEntryIconAndHighlights.SetPediaEntry))]
    static class PediaArtPatch
    {
        static void Postfix(PediaEntryIconAndHighlights __instance, PediaEntry entry)
        {
            try { PediaArt.Shown(__instance, entry); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Pedia art patch: " + e.Message); }
        }
    }
}
