using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
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
        public static readonly Dictionary<IntPtr, RadiantSlimePediaEntry> RadiantBySlime = new();
        public static readonly Dictionary<IntPtr, Sprite> RadiantCardSprites = new();
        static readonly Dictionary<IntPtr, PediaEntry> RadiantScreenEntry = new();
        static readonly HashSet<IntPtr> LoggedRadCard = new();
        // every icon view that currently shows one of our entries (the details page has several of them, e.g. the
        // default and the slime icon highlights; only one is visible) - re-applied every frame while it is active
        static readonly Dictionary<IntPtr, PediaEntryIconAndHighlights> Views = new();
        static readonly List<IntPtr> Drop = new();
        static readonly HashSet<IntPtr> Logged = new();
        // diet highlight of slimes without a food group (quicksilver: energy, glitch: nothing); SR2 shows "Fruit" + no icon
        sealed class DietInfo { public LocalizedString Text; public Func<Sprite> Find; public Sprite Icon; }
        static readonly Dictionary<IntPtr, DietInfo> Diets = new();
        static readonly Dictionary<string, Sprite> GameSprites = new();
        static readonly Dictionary<IntPtr, Sprite> Applied = new();                  // image -> sprite we last wrote
        static readonly Dictionary<IntPtr, Sprite> Original = new();                 // image -> sprite it had before we first changed it
        static readonly Dictionary<IntPtr, (Vector2 cell, Vector2 spacing)> OrigGridLayout = new();
        static readonly Dictionary<IntPtr, (Vector2 pos, float stepX, float stepY, int cols)> OrigManualGrid = new();
        static bool _err;

        public static void Register(IdentifiableType ident, Sprite art)
        {
            if (ident == null || art == null) return;
            Art[ident.Pointer] = art;
        }

        public static void RegisterPlort(IdentifiableType slime, Sprite art)
        {
            if (slime == null || art == null) return;
            PlortArt[slime.Pointer] = art;
        }

        public static void ClearSession()
        {
            Views.Clear();
            Applied.Clear();
            Original.Clear();
            GameSprites.Clear();
            Logged.Clear();
            RadiantScreenEntry.Clear();
            LoggedRadCard.Clear();
            OrigGridLayout.Clear();
            OrigManualGrid.Clear();
        }

        public static void RegisterRadiant(IdentifiableType slime, RadiantSlimePediaEntry entry, Sprite cardArt)
        {
            if (slime == null || entry == null) return;
            RadiantBySlime[slime.Pointer] = entry;
            if (cardArt != null) RadiantCardSprites[entry.Pointer] = cardArt;
        }

        internal static void EnsureRadiantScreen(PediaRadiantSlimeScreen scr)
        {
            if (scr == null) return;
            var rt = scr._category?.TryCast<PediaRuntimeCategory>();
            var items = rt?._items;
            RadiantLooks.EnsureRadiantEntriesInRuntimeList(items);
            if (items == null || scr._buttons == null || scr._buttons.Length == 0) return;

            if (scr._buttons.Length < items.Count)
            {
                int origLen = scr._buttons.Length;
                var tpl = scr._buttons[origLen - 1];
                var parent = scr._buttons[0].transform.parent?.TryCast<RectTransform>();
                if (tpl != null && parent != null)
                {
                    var list = new List<PediaEntryIconButton>(items.Count);
                    for (int i = 0; i < origLen; i++) list.Add(scr._buttons[i]);
                    for (int i = origLen; i < items.Count; i++)
                    {
                        var cloneGo = UnityEngine.Object.Instantiate(tpl.gameObject, parent, false);
                        cloneGo.name = tpl.gameObject.name + "_Classic_" + i;
                        cloneGo.SetActive(true);
                        var nb = cloneGo.GetComponent<PediaEntryIconButton>();
                        list.Add(nb);
                    }
                    scr._buttons = Gc.Array(list);

                    var glg = parent.GetComponent<GridLayoutGroup>();
                    if (glg != null)
                    {
                        if (!OrigGridLayout.TryGetValue(glg.Pointer, out var orig))
                        {
                            orig = (glg.cellSize, glg.spacing);
                            OrigGridLayout[glg.Pointer] = orig;
                        }
                        int cols = glg.constraint == GridLayoutGroup.Constraint.FixedColumnCount && glg.constraintCount > 0
                            ? glg.constraintCount : 6;
                        int rows = (items.Count + cols - 1) / cols;
                        int origRows = (origLen + cols - 1) / cols;
                        if (rows > origRows)
                        {
                            glg.cellSize = orig.cell * 0.82f;
                            glg.spacing = orig.spacing * 0.78f;
                        }
                    }
                    else
                    {
                        if (!OrigManualGrid.TryGetValue(parent.Pointer, out var mg))
                        {
                            var pos0 = list[0]?.GetComponent<RectTransform>()?.anchoredPosition ?? Vector2.zero;
                            float x0 = pos0.x, y0 = pos0.y, stepX = 0f, stepY = 0f;
                            int cols = 1;
                            for (int i = 1; i < origLen; i++)
                            {
                                var rtB = list[i]?.GetComponent<RectTransform>();
                                if (rtB == null) continue;
                                var p = rtB.anchoredPosition;
                                if (Mathf.Abs(p.y - y0) < 4f)
                                {
                                    cols++;
                                    if (stepX == 0f && Mathf.Abs(p.x - x0) > 4f) stepX = p.x - x0;
                                }
                                else if (stepY == 0f)
                                {
                                    stepY = p.y - y0;
                                }
                            }
                            mg = (pos0, stepX, stepY, cols);
                            OrigManualGrid[parent.Pointer] = mg;
                        }
                        if (mg.cols > 1 && mg.stepX != 0f && mg.stepY != 0f)
                        {
                            int rows = (items.Count + mg.cols - 1) / mg.cols;
                            int origRows = (origLen + mg.cols - 1) / mg.cols;
                            float scale = rows > origRows ? Mathf.Clamp((float)origRows / rows, 0.80f, 0.92f) : 1f;
                            float useStepY = mg.stepY * scale;
                            for (int i = 0; i < list.Count; i++)
                            {
                                var rtB = list[i]?.GetComponent<RectTransform>();
                                if (rtB == null) continue;
                                int r = i / mg.cols, c = i % mg.cols;
                                rtB.anchoredPosition = new Vector2(mg.pos.x + c * mg.stepX, mg.pos.y + r * useStepY);
                                if (rows > origRows) rtB.localScale = Vector3.one * scale;
                            }
                        }
                    }

                    for (int i = origLen; i < list.Count; i++)
                    {
                        var nb = list[i];
                        if (nb == null) continue;
                        try { nb.BindItem(i, items[i]); }
                        catch { try { nb.SetData(items[i]); } catch { } }
                        try { scr._SetCategory_b__22_0(nb, items[i]); } catch { }
                        try { nb.SetState(false, false); } catch { }
                    }
                    try { UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parent); } catch { }
                    if (Diag.Enabled) ClassicSlimesMod.Log.Msg($"[pedia-rad] expanded Radiant grid buttons {origLen} -> {list.Count} (glg={glg != null})");
                }
            }

            if (scr._buttons != null)
            {
                for (int i = 0; i < scr._buttons.Length; i++)
                {
                    var b = scr._buttons[i];
                    if (b == null) continue;
                    if (b.PointerSelected == null)
                        b.PointerSelected = new Action<PediaEntryIconButton>(btn => scr.OnPointerSelectedButton(btn));
                    if (b.PointerDeselected == null)
                        b.PointerDeselected = new Action<PediaEntryIconButton>(btn => scr.OnPointerDeselectedButton(btn));
                    if (b.PointerClicked == null)
                        b.PointerClicked = new Action<PediaEntryIconButton>(btn => scr.OnPointerClickedButton(btn));
                }
            }

            if (scr._gridNavigation != null)
            {
                int navCount = Mathf.Min(items.Count, scr._buttons.Length);
                int cols = Mathf.Max(1, scr._gridNavigation.NumColumns);
                int rows = (navCount + cols - 1) / cols;
                if (scr._gridNavigation.NumRows < rows || scr._gridNavigation._totalCount != navCount)
                {
                    int sel = scr._gridNavigation.SelectedIndex;
                    scr._gridNavigation.Resize(rows, cols);
                    scr._gridNavigation.SetItemCount(navCount);
                    if (sel >= 0 && sel < navCount) scr._gridNavigation.SetSelectedIndex(sel);
                }
            }
            if (scr._hoverIndex >= scr._buttons.Length) scr._hoverIndex = -1;
            if (scr._previousSelectedIndex >= scr._buttons.Length) scr._previousSelectedIndex = -1;
        }

        internal static void ShownRadiant(PediaRadiantSlimeScreen scr, PediaEntry entry)
        {
            if (scr == null) return;
            RadiantScreenEntry[scr.Pointer] = entry;
            ApplyRadiantCard(scr, entry);
        }

        internal static void TickRadiantScreen(PediaRadiantSlimeScreen scr)
        {
            if (scr == null || !RadiantScreenEntry.TryGetValue(scr.Pointer, out var entry)) return;
            if (entry != null && RadiantCardSprites.ContainsKey(entry.Pointer)) ApplyRadiantCard(scr, entry);
        }

        static void ApplyRadiantCard(PediaRadiantSlimeScreen scr, PediaEntry entry)
        {
            if (scr?._cardImages == null) return;
            bool ours = false;
            Sprite sp = null;
            if (entry != null && RadiantCardSprites.TryGetValue(entry.Pointer, out sp) && sp != null)
            {
                try { ours = SceneContext.Instance?.PediaDirector?.IsUnlocked(entry) == true; } catch { }
            }
            if (Diag.Enabled && entry != null && RadiantCardSprites.ContainsKey(entry.Pointer) && LoggedRadCard.Add(entry.Pointer))
            {
                var names = new List<string>();
                foreach (var im in scr._cardImages)
                    if (im != null) names.Add($"{im.name}({(im.sprite != null ? im.sprite.name : "-")})");
                ClassicSlimesMod.Log.Msg($"[pedia-rad] card {entry.name} unlocked={ours} cardImages=[{string.Join(", ", names)}]");
            }
            if (!ours)
            {
                foreach (var im in scr._cardImages)
                {
                    if (im == null || !Applied.TryGetValue(im.Pointer, out var prevOurs)) continue;
                    Applied.Remove(im.Pointer);
                    try
                    {
                        if (im.overrideSprite != null && im.overrideSprite.Pointer == prevOurs.Pointer) im.overrideSprite = null;
                        if (Original.TryGetValue(im.Pointer, out var orig) && im.sprite != null && im.sprite.Pointer == prevOurs.Pointer)
                            if ((object)orig == null || orig != null) im.sprite = orig;
                    }
                    catch { }
                }
                return;
            }
            foreach (var im in scr._cardImages)
            {
                if (im == null) continue;
                if (im.sprite == null || im.sprite.Pointer != sp.Pointer || im.overrideSprite == null || im.overrideSprite.Pointer != sp.Pointer)
                {
                    Keep(im);
                    im.sprite = sp;
                    im.overrideSprite = sp;
                    Applied[im.Pointer] = sp;
                }
                im.preserveAspect = true;
                if (!im.enabled) im.enabled = true;
                var c = im.color;
                bool isShadow = im.name != null && im.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isShadow && c.a < 1f) im.color = new Color(c.r, c.g, c.b, 1f);
            }
        }

        public static void RegisterPersistence(SaveReferenceTranslation srt = null)
        {
            try
            {
                srt ??= GameContext.Instance?.AutoSaveDirector?._saveReferenceTranslation;
                var lookup = srt?._pediaEntryLookup;
                if (lookup == null) return;
                int added = 0;
                foreach (var pe in Resources.FindObjectsOfTypeAll<PediaEntry>())
                {
                    if (pe == null) continue;
                    try
                    {
                        string pid = pe.PersistenceId;
                        if (!string.IsNullOrEmpty(pid) && !lookup.ContainsKey(pid)) { lookup[pid] = pe; added++; }
                    }
                    catch { }
                    try
                    {
                        string nm = pe.name;
                        if (!string.IsNullOrEmpty(nm) && !lookup.ContainsKey(nm)) { lookup[nm] = pe; added++; }
                        if (!string.IsNullOrEmpty(nm) && !nm.Contains('.') && nm.EndsWith("_slime", StringComparison.Ordinal))
                        {
                            string k = "Pink." + nm;
                            if (!lookup.ContainsKey(k)) { lookup[k] = pe; added++; }
                        }
                        if (!string.IsNullOrEmpty(nm) && !nm.Contains('.') && (nm.EndsWith("_veggie", StringComparison.Ordinal) || nm.EndsWith("_fruit", StringComparison.Ordinal)))
                        {
                            string pfx = nm.EndsWith("_veggie", StringComparison.Ordinal) ? "CarrotVeggie." : "PogoFruit.";
                            string k = pfx + nm;
                            if (!lookup.ContainsKey(k)) { lookup[k] = pe; added++; }
                        }
                    }
                    catch { }
                }
                if (added > 0 && Diag.Enabled) ClassicSlimesMod.Log.Msg($"[pedia] registered {added} persistence key(s) in SaveReferenceTranslation");
            }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("Pedia persistence register: " + e.Message); }
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
            if (d.Icon == null && full && d.Find != null) d.Icon = d.Find();
            var ic = h._categoryIcon;
            // the game sets the category icons through .sprite (no override), so it restores them on the next entry
            if (d.Icon != null && ic != null && (ic.sprite == null || ic.sprite.Pointer != d.Icon.Pointer)) { Keep(ic); ic.sprite = d.Icon; Applied[ic.Pointer] = d.Icon; ic.preserveAspect = true; }
            if (full && d.Text != null && h._valueString != null) { h._valueString.StringReference = d.Text; h._valueString.RefreshString(); }
        }

        static void Keep(Image img)
        {
            if (img == null || Original.ContainsKey(img.Pointer) || Applied.ContainsKey(img.Pointer)) return;
            Original[img.Pointer] = img.sprite;
        }

        /// <summary>Restores any images on this view that we previously overwrote before showing a different entry.</summary>
        static void Clean(PediaEntryIconAndHighlights view)
        {
            void One(Image img)
            {
                if (img == null || !Applied.TryGetValue(img.Pointer, out var ours)) return;
                Applied.Remove(img.Pointer);
                try
                {
                    var ov = img.overrideSprite;
                    if (ov != null && ours != null && ov.Pointer == ours.Pointer) img.overrideSprite = null;
                    if (Original.TryGetValue(img.Pointer, out var orig) && img.sprite != null && ours != null && img.sprite.Pointer == ours.Pointer)
                    {
                        if ((object)orig == null || orig != null) img.sprite = orig;
                    }
                }
                catch { }
            }
            One(view._primaryIcon); One(view._secondaryIcon);
            var ds = DietSlot(view);
            if (ds != null) One(ds._categoryIcon);
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
            Clean(view);
            var sp = For(entry);
            if (sp == null) { Views.Remove(view.Pointer); return; }
            Views[view.Pointer] = view;
            Apply(view, sp, PlortFor(entry));
            var diet = DietFor(entry);
            if (diet != null) ApplyDiet(view, diet, true);
        }

        static void Apply(PediaEntryIconAndHighlights view, Sprite sp, Sprite plort)
        {
            var sec = plort != null ? view._secondaryIcon : null;
            if (sec != null && (sec.sprite == null || sec.sprite.Pointer != plort.Pointer)) { Keep(sec); sec.sprite = plort; Applied[sec.Pointer] = plort; sec.preserveAspect = true; }
            if (sec != null && sec.overrideSprite != null && sec.overrideSprite.Pointer != plort.Pointer) { sec.overrideSprite = plort; Applied[sec.Pointer] = plort; }
            var img = view._primaryIcon;
            if (img == null) return;
            // without full art the game shows the identifiable icon through overrideSprite, which wins over sprite
            var ov = img.overrideSprite;
            if (img.sprite == null || img.sprite.Pointer != sp.Pointer || ov == null || ov.Pointer != sp.Pointer)
            {
                Keep(img);
                img.sprite = sp;
                img.overrideSprite = sp;
                Applied[img.Pointer] = sp;
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

    [HarmonyPatch]
    static class PediaSaveLoadPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in typeof(AutoSaveDirector).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                if (m.Name == "Load") yield return m;
        }

        static void Prefix(AutoSaveDirector __instance)
        {
            try { if (__instance != null) PediaArt.RegisterPersistence(__instance._saveReferenceTranslation); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(SaveReferenceTranslation), nameof(SaveReferenceTranslation.OnPediaEntryListChanged))]
    static class PediaEntryListChangedPatch
    {
        static void Postfix(SaveReferenceTranslation __instance) => PediaArt.RegisterPersistence(__instance);
    }

    [HarmonyPatch(typeof(GameModelPushHelpers), nameof(GameModelPushHelpers.PushPedia))]
    static class PushPediaPersistencePatch
    {
        static void Prefix() => PediaArt.RegisterPersistence();
    }

    [HarmonyPatch(typeof(PediaRadiantSlimeScreen))]
    static class PediaRadiantScreenPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PediaRadiantSlimeScreen.BeforeFadeIn))]
        static void BeforeFadeInPrefix(PediaRadiantSlimeScreen __instance)
        {
            try { PediaArt.EnsureRadiantScreen(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("PediaRadiant BeforeFadeIn: " + e.Message); }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(PediaRadiantSlimeScreen.ViewEntry))]
        static void ViewEntryPrefix(PediaRadiantSlimeScreen __instance)
        {
            try { PediaArt.EnsureRadiantScreen(__instance); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("PediaRadiant ViewEntry prefix: " + e.Message); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(PediaRadiantSlimeScreen.ViewEntry))]
        static void ViewEntryPostfix(PediaRadiantSlimeScreen __instance, PediaEntry entry)
        {
            try { PediaArt.ShownRadiant(__instance, entry); }
            catch (Exception e) { ClassicSlimesMod.Log.Warning("PediaRadiant ViewEntry: " + e.Message); }
        }

        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        static void UpdatePrefix(PediaRadiantSlimeScreen __instance)
        {
            try { PediaArt.EnsureRadiantScreen(__instance); }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Update")]
        static void UpdatePostfix(PediaRadiantSlimeScreen __instance)
        {
            try { PediaArt.TickRadiantScreen(__instance); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector), nameof(Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector.UnlockRadiantPediaEntry))]
    static class UnlockRadiantPediaPatch
    {
        static void Postfix(IdentifiableType radiantBaseType)
        {
            try
            {
                if (radiantBaseType == null || !PediaArt.RadiantBySlime.TryGetValue(radiantBaseType.Pointer, out var re) || re == null) return;
                var pd = SceneContext.Instance?.PediaDirector;
                if (pd != null && !pd.IsUnlocked(re)) pd.Unlock(re, true);
            }
            catch { }
        }
    }
}
