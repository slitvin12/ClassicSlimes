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
    /// icon (or an optional pediaSlimeX.png) into the big picture and keep re-applying it for a few seconds, because
    /// the game loads the full art asynchronously and would overwrite it.
    /// </summary>
    public static class PediaArt
    {
        static readonly Dictionary<IntPtr, Sprite> Art = new();
        static PediaEntryIconAndHighlights _view;
        static Sprite _sprite;
        static float _until;
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
            var sp = For(entry);
            if (sp == null) { if (_view != null && view != null && _view.Pointer == view.Pointer) _view = null; return; }
            _view = view; _sprite = sp; _until = Time.unscaledTime + 5f;
            Apply();
        }

        static void Apply()
        {
            var img = _view?._primaryIcon;
            if (img == null) return;
            if (img.sprite == null || img.sprite.Pointer != _sprite.Pointer) img.sprite = _sprite;
            img.preserveAspect = true;
            if (!img.enabled) img.enabled = true;
            var c = img.color;
            if (c.a < 1f) img.color = new Color(c.r, c.g, c.b, 1f);
        }

        public static void Tick()
        {
            if (_view == null) return;
            try
            {
                if (Time.unscaledTime > _until || !_view.isActiveAndEnabled || For(_view._currentEntry) == null) { _view = null; return; }
                Apply();
            }
            catch (Exception e)
            {
                _view = null;
                if (!_err) { _err = true; ClassicSlimesMod.Log.Warning("Pedia art: " + e.Message); }
            }
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
