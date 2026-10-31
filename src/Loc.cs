using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CottonLibrary;
using Il2Cpp;
using UnityEngine.Localization;

namespace ClassicSlimes
{
    public static class Loc
    {
        static HashSet<string> _keys;
        static Dictionary<string, LocalizedString> _largo;

        static void LoadKeys()
        {
            _keys = new HashSet<string>(StringComparer.Ordinal);
            var b = Res.Bytes("localized.csv");
            if (b == null) return;
            using var r = new StringReader(Encoding.UTF8.GetString(b));
            string line; bool first = true;
            while ((line = r.ReadLine()) != null)
            {
                if (first) { first = false; continue; }
                int i = line.IndexOf(',');
                if (i > 0) _keys.Add(line.Substring(0, i).Trim().Trim('"').TrimStart('\uFEFF'));
            }
        }

        public static bool Has(string key)
        {
            if (_keys == null) LoadKeys();
            return _keys.Contains(key);
        }

        /// <summary>
        /// Register every largo name up-front (called from SaveDirectorLoaded together with the other strings).
        /// Adding new Cotton strings later, while Cotton's LocalizationRepair coroutine is enumerating them,
        /// throws "Collection was modified" and kills that coroutine for the whole session.
        /// </summary>
        public static void PreRegisterLargos()
        {
            if (_keys == null) LoadKeys();
            _largo = new Dictionary<string, LocalizedString>(StringComparer.Ordinal);
            foreach (var k in _keys)
                if (k.EndsWith("_largo", StringComparison.Ordinal))
                    _largo[k] = Library.CreateLocalizedString(k);
        }

        public static void NameLargo(SlimeDefinition d)
        {
            if (_largo == null || d.BaseSlimes == null || d.BaseSlimes.Length != 2) return;
            string a = d.BaseSlimes[0].name.ToLowerInvariant(), b = d.BaseSlimes[1].name.ToLowerInvariant();
            if (_largo.TryGetValue($"l.{a}_{b}_largo", out var s) || _largo.TryGetValue($"l.{b}_{a}_largo", out s))
                d.localizedName = s;
        }
    }
}
