using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ClassicSlimes
{
    /// <summary>
    /// SlimeFace.ExpressionFaces is an IL2CPP array of the value type SlimeExpressionFace, which Il2CppInterop
    /// mis-exposes as a reference array. These helpers read/write the elements through raw memory.
    /// </summary>
    public static unsafe class Faces
    {
        static bool _init;
        static int _elem, _offEx, _offEyes, _offMouth;

        static void Init()
        {
            if (_init) return;
            var cls = Il2CppClassPointerStore<SlimeExpressionFace>.NativeClassPtr;
            uint align = 0;
            _elem = (int)IL2CPP.il2cpp_class_value_size(cls, ref align);
            int hdr = 2 * IntPtr.Size; // value-type field offsets include the boxed object header
            _offEx = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "SlimeExpression")) - hdr;
            _offEyes = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "Eyes")) - hdr;
            _offMouth = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "Mouth")) - hdr;
            _init = true;
            ClassicSlimesMod.Log.Msg($"[faces] elem={_elem} ex={_offEx} eyes={_offEyes} mouth={_offMouth}");
        }

        static IntPtr Arr(SlimeFace f)
        {
            var a = f.ExpressionFaces;
            return a == null ? IntPtr.Zero : a.Pointer;
        }

        static int Len(IntPtr arr) => arr == IntPtr.Zero ? 0 : (int)*(long*)(arr + 3 * IntPtr.Size);
        static IntPtr Elem(IntPtr arr, int i) => arr + 4 * IntPtr.Size + i * _elem;

        static Material M(IntPtr p) => p == IntPtr.Zero ? null : new Material(p);

        public static List<(SlimeFace.SlimeExpression, Material, Material)> Read(SlimeFace f)
        {
            Init();
            var r = new List<(SlimeFace.SlimeExpression, Material, Material)>();
            var arr = Arr(f);
            int n = Len(arr);
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                r.Add(((SlimeFace.SlimeExpression)(*(int*)(e + _offEx)), M(*(IntPtr*)(e + _offEyes)), M(*(IntPtr*)(e + _offMouth))));
            }
            return r;
        }

        static void Set(IntPtr arr, IntPtr addr, Material m) =>
            IL2CPP.il2cpp_gc_wbarrier_set_field(arr, addr, m == null ? IntPtr.Zero : m.Pointer);

        /// <summary>Clones a face and remaps every eye/mouth material (null func or null result keeps the original).
        /// Results are cached per source material so shared materials stay shared.</summary>
        public static SlimeFace Clone(SlimeFace src, string name, Func<Material, Material> eyes, Func<Material, Material> mouth)
        {
            Init();
            if (src == null) return null;
            var f = Object.Instantiate(src);
            f.name = name;
            f.hideFlags |= HideFlags.HideAndDontSave;
            var arr = Arr(f);
            int n = Len(arr);
            var ce = new Dictionary<IntPtr, Material>();
            var cm = new Dictionary<IntPtr, Material>();
            var srcE = new IntPtr[n];
            var srcM = new IntPtr[n];
            for (int i = 0; i < n; i++) { var e = Elem(arr, i); srcE[i] = *(IntPtr*)(e + _offEyes); srcM[i] = *(IntPtr*)(e + _offMouth); }
            int srcNullE = 0, srcNullM = 0;
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if (*(IntPtr*)(e + _offEyes) == IntPtr.Zero) srcNullE++;
                if (*(IntPtr*)(e + _offMouth) == IntPtr.Zero) srcNullM++;
                Remap(arr, e + _offEyes, eyes, ce);
                Remap(arr, e + _offMouth, mouth, cm);
            }
            // a null slot renders with Unity's magenta error material: fill gaps with the HAPPY (default) eyes / mouth,
            // falling back to the first real ones
            IntPtr firstE = IntPtr.Zero, firstM = IntPtr.Zero;
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if ((SlimeFace.SlimeExpression)(*(int*)(e + _offEx)) != SlimeFace.SlimeExpression.HAPPY) continue;
                firstE = *(IntPtr*)(e + _offEyes);
                firstM = *(IntPtr*)(e + _offMouth);
            }
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if (firstE == IntPtr.Zero) firstE = *(IntPtr*)(e + _offEyes);
                if (firstM == IntPtr.Zero) firstM = *(IntPtr*)(e + _offMouth);
            }
            // source materials of the fillers (for the self-heal below)
            IntPtr fillSrcE = IntPtr.Zero, fillSrcM = IntPtr.Zero;
            for (int i = 0; i < n; i++)
                if ((SlimeFace.SlimeExpression)(*(int*)(Elem(arr, i) + _offEx)) == SlimeFace.SlimeExpression.HAPPY) { fillSrcE = srcE[i]; fillSrcM = srcM[i]; }
            for (int i = 0; i < n; i++) { if (fillSrcE == IntPtr.Zero) fillSrcE = srcE[i]; if (fillSrcM == IntPtr.Zero) fillSrcM = srcM[i]; }
            // stock faces leave the mouth of a few expressions empty on purpose: SR2 then keeps animating an *instance*
            // of the current mouth and destroys it afterwards. Filling those slots with our shared mouth made the game
            // destroy the shared material (magenta mouths), so empty mouth slots stay empty.
            for (int i = 0; i < n; i++) { if (srcE[i] == IntPtr.Zero) srcE[i] = fillSrcE; }
            int filled = 0;
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if (*(IntPtr*)(e + _offEyes) == IntPtr.Zero && firstE != IntPtr.Zero) { IL2CPP.il2cpp_gc_wbarrier_set_field(arr, e + _offEyes, firstE); filled++; }

            }
            ClassicSlimesMod.Log.Msg($"[faces] {name}: {n} expressions, source null eyes/mouths {srcNullE}/{srcNullM}, filled {filled}, new mats {ce.Count}/{cm.Count}");
            f.UpdateCache();
            _regs.Add(new Reg { Face = f, Name = name, Eyes = eyes, Mouth = mouth, SrcE = srcE, SrcM = srcM, CE = ce, CM = cm });
            return f;
        }

        // ------------------------------------------------------------------ self-heal
        // Something in the game occasionally destroys cloned face materials (seen with mouthHappy clones); a destroyed
        // slot renders with the magenta error material. Every 2 s the registered faces are checked and dead slots are
        // re-created from their source material.
        sealed class Reg
        {
            public SlimeFace Face; public string Name;
            public Func<Material, Material> Eyes, Mouth;
            public IntPtr[] SrcE, SrcM;
            public Dictionary<IntPtr, Material> CE, CM;
        }
        static readonly List<Reg> _regs = new();
        /// <summary>Native pointers of the eye/mouth materials made for our faces (see DestroyGuard).</summary>
        public static readonly HashSet<IntPtr> Protected = new();
        public static int Healed;
        static readonly HashSet<string> _healLogged = new();

        /// <summary>Number of destroyed materials among the ones made for the registered faces (diagnostics).</summary>
        public static int DeadCount()
        {
            int d = 0;
            foreach (var r in _regs)
            {
                foreach (var m in r.CE.Values) if ((object)m != null && m == null) d++;
                foreach (var m in r.CM.Values) if ((object)m != null && m == null) d++;
            }
            return d;
        }

        public static bool IsOurs(SlimeFace f)
        {
            if (f == null) return false;
            var p = f.Pointer;
            foreach (var r in _regs) if (r.Face != null && r.Face.Pointer == p) return true;
            return false;
        }

        static bool Alive(IntPtr p)
        {
            if (p == IntPtr.Zero) return false;
            var m = new Material(p);
            return m != null;
        }

        public static void Tick()
        {
            foreach (var r in _regs)
            {
                if (r.Face == null) continue;
                var arr = Arr(r.Face);
                int n = Math.Min(Len(arr), r.SrcE.Length);
                bool changed = false;
                for (int i = 0; i < n; i++)
                {
                    var e = Elem(arr, i);
                    changed |= Heal(arr, e + _offEyes, r.SrcE[i], r.Eyes, r.CE, r.Name + ".eyes");
                    changed |= Heal(arr, e + _offMouth, r.SrcM[i], r.Mouth, r.CM, r.Name + ".mouth");
                }
                if (changed) r.Face.UpdateCache();
            }
        }

        static bool Heal(IntPtr arr, IntPtr addr, IntPtr src, Func<Material, Material> fn, Dictionary<IntPtr, Material> cache, string what)
        {
            var cur = *(IntPtr*)addr;
            if (Alive(cur)) return false;
            Material m = null;
            if (src != IntPtr.Zero)
            {
                if (cache.TryGetValue(src, out var c) && c != null) m = c;
                else if (Alive(src))
                {
                    var sm = new Material(src);
                    try { m = fn != null ? fn(sm) : sm; } catch (Exception ex) { ClassicSlimesMod.Log.Warning("[faces] heal: " + ex.Message); }
                    if (m != null && fn != null) { cache[src] = m; Mat.Track(m); Protected.Add(m.Pointer); }
                }
            }
            if (m == null) return false;
            Set(arr, addr, m);
            Healed++;
            if (_healLogged.Add(what + m.name)) ClassicSlimesMod.Log.Msg($"[faces] healed {what} slot ({(cur == IntPtr.Zero ? "empty" : "destroyed material")}) -> {m.name}");
            return true;
        }

        static void Remap(IntPtr arr, IntPtr addr, Func<Material, Material> fn, Dictionary<IntPtr, Material> cache)
        {
            if (fn == null) return;
            var p = *(IntPtr*)addr;
            if (p == IntPtr.Zero) return;
            if (!cache.TryGetValue(p, out var nm))
            {
                try { nm = fn(new Material(p)); } catch (Exception ex) { ClassicSlimesMod.Log.Warning("[faces] remap: " + ex.Message); nm = null; }
                cache[p] = nm;
            }
            if (nm != null) { Mat.Track(nm); Protected.Add(nm.Pointer); Set(arr, addr, nm); }
        }
    }
}
