using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.UI.Pedia;
using UnityEngine;
using UnityEngine.Localization;
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
        static readonly Dictionary<IntPtr, Sprite> PlortArt = new();   // small secondary picture (the plort) on the slime page
        // every icon view that currently shows one of our entries (the details page has several of them, e.g. the
        // default and the slime icon highlights; only one is visible) - re-applied every frame while it is active
        static readonly Dictionary<IntPtr, PediaEntryIconAndHighlights> Views = new();
        static readonly List<IntPtr> Drop = new();
        static readonly HashSet<IntPtr> Logged = new();
        // diet highlight of slimes without a food group (quicksilver: energy, glitch: nothing); SR2 shows "Fruit" + no icon
        sealed class DietInfo { public LocalizedString Text; public Func<Sprite> Find; public Sprite Icon; }
        static readonly Dictionary<IntPtr, DietInfo> Diets = new();
        static readonly Dictionary<string, Sprite> GameSprites = new();
        static readonly HashSet<IntPtr> Ours = new();                    // every sprite this class puts into the pedia
        static readonly Dictionary<IntPtr, Sprite> Original = new();     // image -> sprite it had before we first changed it
        static bool _err;

        public static void Register(IdentifiableType ident, Sprite art)
        {
            if (ident == null || art == null) return;
            Art[ident.Pointer] = art; Ours.Add(art.Pointer);
        }

        public static void RegisterPlort(IdentifiableType slime, Sprite art)
        {
            if (slime == null || art == null) return;
            PlortArt[slime.Pointer] = art; Ours.Add(art.Pointer);
        }

        public static void RegisterDiet(IdentifiableType slime, LocalizedString text, Func<Sprite> icon)
        {
            if (slime == null) return;
            Diets[slime.Pointer] = new DietInfo { Text = text, Find = icon };
        }

        /// <summary>A loaded game sprite by name (cached once found).</summary>
        public static Sprite GameSprite(string name)
        {
            if (GameSprites.TryGetValue(name, out var s) && s != null) return s;
            foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sp != null && sp.name == name) { GameSprites[name] = sp; return sp; }
            return null;
        }

        static DietInfo DietFor(PediaEntry entry)
        {
            var id = entry?.TryCast<IdentifiablePediaEntry>()?._identifiableType;
            return id != null && Diets.TryGetValue(id.Pointer, out var d) ? d : null;
        }

        static string KeyOf(UnityEngine.Localization.Components.LocalizeStringEvent ev)
        {
            try { return ev?.StringReference?.TableEntryReference.Key; } catch { return null; }
        }

        static PediaEntryHighlightDisplay DietSlot(PediaEntryIconAndHighlights view)
        {
            var hs = view._highlights;
            if (hs == null || hs.Length == 0) return null;
            // diet comes first (diet, favourite); prefer the highlight whose label key names the diet
            for (int i = 0; i < hs.Length; i++)
            {
                var k = KeyOf(hs[i]?._categoryString);
                if (k != null && k.IndexOf("iet", StringComparison.OrdinalIgnoreCase) >= 0) return hs[i];
            }
            return hs[0];
        }

        static void ApplyDiet(PediaEntryIconAndHighlights view, DietInfo d, bool full)
        {
            var h = DietSlot(view);
            if (h == null) return;
            if (d.Icon == null && full && d.Find != null) { d.Icon = d.Find(); if (d.Icon != null) Ours.Add(d.Icon.Pointer); }
            var ic = h._categoryIcon;
            // the game sets the category icons through .sprite (no override), so it restores them on the next entry
            if (d.Icon != null && ic != null && (ic.sprite == null || ic.sprite.Pointer != d.Icon.Pointer)) { Keep(ic); ic.sprite = d.Icon; ic.preserveAspect = true; }
            if (full && d.Text != null && h._valueString != null) { h._valueString.StringReference = d.Text; h._valueString.RefreshString(); }
        }

        static void Keep(Image img)
        {
            if (img != null && !Original.ContainsKey(img.Pointer) && (img.sprite == null || !Ours.Contains(img.sprite.Pointer))) Original[img.Pointer] = img.sprite;
        }

        /// <summary>A vanilla entry is shown in a view we changed before: drop our sprites the game did not replace itself.</summary>
        static void Clean(PediaEntryIconAndHighlights view)
        {
            void One(Image img)
            {
                if (img == null) return;
                var act = img.overrideSprite;   // active sprite: override, else sprite
                if (act == null || !Ours.Contains(act.Pointer)) return;
                img.overrideSprite = null;
                if (img.sprite != null && Ours.Contains(img.sprite.Pointer) && Original.TryGetValue(img.Pointer, out var o)) img.sprite = o;
            }
            One(view._primaryIcon); One(view._secondaryIcon);
            var hs = view._highlights;
            if (hs != null) for (int i = 0; i < hs.Length; i++) One(hs[i]?._categoryIcon);
        }

        static Sprite PlortFor(PediaEntry entry)
        {
            var id = entry?.TryCast<IdentifiablePediaEntry>()?._identifiableType;
            return id != null && PlortArt.TryGetValue(id.Pointer, out var sp) ? sp : null;
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
            if (sp == null) { Views.Remove(view.Pointer); Clean(view); return; }
            Views[view.Pointer] = view;
            Apply(view, sp, PlortFor(entry));
            var diet = DietFor(entry);
            if (diet != null) ApplyDiet(view, diet, true);
        }

        static void Apply(PediaEntryIconAndHighlights view, Sprite sp, Sprite plort)
        {
            var sec = plort != null ? view._secondaryIcon : null;
            if (sec != null && (sec.sprite == null || sec.sprite.Pointer != plort.Pointer)) { Keep(sec); sec.sprite = plort; sec.preserveAspect = true; }
            if (sec != null && sec.overrideSprite != null && sec.overrideSprite.Pointer != plort.Pointer) sec.overrideSprite = plort;
            var img = view._primaryIcon;
            if (img == null) return;
            // without full art the game shows the identifiable icon through overrideSprite, which wins over sprite
            var ov = img.overrideSprite;
            if (img.sprite == null || img.sprite.Pointer != sp.Pointer || ov == null || ov.Pointer != sp.Pointer)
            {
                Keep(img);
                img.sprite = sp;
                img.overrideSprite = sp;
                if (Logged.Add(sp.Pointer)) ClassicSlimesMod.Log.Msg($"Pedia art: {sp.name} -> {view.name}");
            }
            img.preserveAspect = true;
            if (!img.enabled) img.enabled = true;
            var c = img.color;
            if (c.a < 1f) img.color = new Color(c.r, c.g, c.b, 1f);
        }

        /// <summary>Dev: every active image of the open slimepedia (path, sprite, size) -> pediaimgs.txt.</summary>
        public static string Dump()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var r in new[] { 0 })
            {
                foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
                {
                    if (img == null || !img.isActiveAndEnabled) continue;
                    var pth = DiagX.Path(img.transform);
                    if (pth.IndexOf("edia", StringComparison.Ordinal) < 0 && (img.sprite == null || img.sprite.name.IndexOf("osaic", StringComparison.Ordinal) < 0)) continue;
                    var rt = img.rectTransform; n++;
                    sb.AppendLine($"IMG {DiagX.Path(img.transform)} sprite={(img.sprite != null ? img.sprite.name : "-")} shown={(img.overrideSprite != null ? img.overrideSprite.name : "-")} size={rt.rect.width:0}x{rt.rect.height:0} a={img.color.a:0.00} type={img.GetIl2CppType().Name}");
                }
                foreach (var ri in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (ri == null || !ri.isActiveAndEnabled) continue;
                    var rt = ri.rectTransform; n++;
                    sb.AppendLine($"RAW {DiagX.Path(ri.transform)} tex={(ri.texture != null ? ri.texture.name : "-")} size={rt.rect.width:0}x{rt.rect.height:0}");
                }
                foreach (var v in Resources.FindObjectsOfTypeAll<PediaEntryIconAndHighlights>())
                {
                    if (v == null) continue;
                    var p = v._primaryIcon; var s2 = v._secondaryIcon;
                    sb.AppendLine($"VIEW {DiagX.Path(v.transform)} active={v.isActiveAndEnabled} entry={(v._currentEntry != null ? v._currentEntry.name : "-")} primary={(p != null ? DiagX.Path(p.transform) + " sprite=" + (p.sprite != null ? p.sprite.name : "-") + " on=" + p.isActiveAndEnabled : "-")} secondary={(s2 != null ? DiagX.Path(s2.transform) + " sprite=" + (s2.sprite != null ? s2.sprite.name : "-") : "-")}");
                    var hs = v._highlights;
                    if (hs != null)
                        for (int i = 0; i < hs.Length; i++)
                        {
                            var h = hs[i]; if (h == null) continue;
                            var hi = h._categoryIcon;
                            sb.AppendLine($"  HL{i} label={KeyOf(h._categoryString)} value={KeyOf(h._valueString)} icon={(hi != null && hi.overrideSprite != null ? hi.overrideSprite.name : "-")}");
                        }
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Diag.Dir, "pediaimgs.txt"), sb.ToString());
            return $"pediaimgs: {n} visible images -> pediaimgs.txt";
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
                    if (!v.isActiveAndEnabled) continue;
                    Apply(v, sp, PlortFor(v._currentEntry));
                    var diet = DietFor(v._currentEntry);
                    if (diet != null) ApplyDiet(v, diet, false);
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
