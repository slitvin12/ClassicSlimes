using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>Loads assets embedded in the mod DLL (extracted from Slime Rancher 1).</summary>
    public static class Res
    {
        static readonly Dictionary<string, Mesh> _meshes = new();
        static readonly Dictionary<string, AudioClip> _clips = new();
        static readonly Dictionary<string, Sprite> _sprites = new();
        static readonly Dictionary<string, Texture2D> _texs = new();
        public static readonly Dictionary<string, (Vector3[] v, int[] t, Vector2[] uv)> Data = new();

        /// <summary>PNG texture (sRGB colour by default, linear for masks/normal maps).</summary>
        public static Texture2D Tex(string name, bool linear = false, bool repeat = true)
        {
            string key = name + (linear ? "|L" : "|S") + (repeat ? "|R" : "|C");
            if (_texs.TryGetValue(key, out var t) && t != null) return t;
            var bytes = Bytes(name + ".png");
            if (bytes == null) { ClassicSlimesMod.Log.Warning("Missing texture " + name); return null; }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            Il2CppImageConversionManager.LoadImage(t, bytes, true);
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.name = "ClassicSlimes_" + name;
            t.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(t);
            _texs[key] = t;
            return t;
        }

        public static byte[] Bytes(string file)
        {
            var asm = typeof(Res).Assembly;
            using var s = asm.GetManifestResourceStream("ClassicSlimes." + file);
            if (s == null) return null;
            var buf = new byte[s.Length];
            int read = 0;
            while (read < buf.Length) { int n = s.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            return buf;
        }

        public static Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out var sp) && sp != null) return sp;
            var bytes = Bytes(name + ".png");
            if (bytes == null) { ClassicSlimesMod.Log.Warning("Missing sprite " + name); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Il2CppImageConversionManager.LoadImage(tex, bytes, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = name;
            tex.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(tex);
            sp = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sp.name = name;
            sp.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(sp);
            _sprites[name] = sp;
            return sp;
        }

        /// <summary>CSM1 mesh format written by tools/export_assets.py.</summary>
        public static Mesh Mesh(string name)
        {
            if (_meshes.TryGetValue(name, out var m) && m != null) return m;
            var bytes = Bytes(name + ".csm");
            if (bytes == null) { ClassicSlimesMod.Log.Warning("Missing mesh " + name); return null; }
            try
            {
                using var br = new BinaryReader(new MemoryStream(bytes));
                if (bytes.Length < 16 || Encoding.ASCII.GetString(br.ReadBytes(4)) != "CSM1") throw new Exception("bad csm " + name);
                int nv = br.ReadInt32(), ni = br.ReadInt32(), flags = br.ReadInt32();
                if (nv <= 0 || ni <= 0) throw new Exception($"invalid csm counts ({nv}/{ni}) in {name}");
                var v = new Vector3[nv];
                for (int i = 0; i < nv; i++) v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                Vector3[] nrm = null; Vector2[] uv = null; BoneWeight[] bw = null;
                if ((flags & 1) != 0) { nrm = new Vector3[nv]; for (int i = 0; i < nv; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); }
                if ((flags & 2) != 0) { uv = new Vector2[nv]; for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle()); }
                if ((flags & 4) != 0)
                {
                    bw = new BoneWeight[nv];
                    for (int i = 0; i < nv; i++)
                    {
                        var w = new BoneWeight();
                        w.boneIndex0 = br.ReadInt32(); w.boneIndex1 = br.ReadInt32(); w.boneIndex2 = br.ReadInt32(); w.boneIndex3 = br.ReadInt32();
                        w.weight0 = br.ReadSingle(); w.weight1 = br.ReadSingle(); w.weight2 = br.ReadSingle(); w.weight3 = br.ReadSingle();
                        bw[i] = w;
                    }
                }
                var tris = new int[ni];
                for (int i = 0; i < ni; i++) tris[i] = br.ReadInt32();
                int nbp = br.ReadInt32();
                if (nbp < 0) throw new Exception("bad csm bindposes " + name);
                var bp = new Matrix4x4[nbp];
                for (int b = 0; b < nbp; b++)
                {
                    var mx = new Matrix4x4();
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) mx[r, c] = br.ReadSingle();
                    bp[b] = mx;
                }
                m = new Mesh();
                m.name = name;
                m.vertices = v;
                if (nrm != null) m.normals = nrm;
                if (uv != null) m.uv = uv;
                m.triangles = tris;
                if (bw != null) m.boneWeights = bw;
                if (nbp > 0) m.bindposes = bp;
                if (nrm == null) m.RecalculateNormals();
                m.RecalculateBounds();
                if (uv != null) m.RecalculateTangents();
                m.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(m);
                _meshes[name] = m;
                Data[name] = (v, tris, uv);
                return m;
            }
            catch (Exception e)
            {
                ClassicSlimesMod.Log.Warning($"Mesh {name} failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Raw mesh data (for re-skinning onto SR2 bones).</summary>
        public static Mesh MeshCopy(string name)
        {
            var src = Mesh(name);
            if (src == null) return null;
            var m = UnityEngine.Object.Instantiate(src);
            m.name = name + "_copy";
            m.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(m);
            return m;
        }

        /// <summary>16-bit PCM WAV -> AudioClip.</summary>
        public static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            var bytes = Bytes(name + ".wav");
            if (bytes == null) { ClassicSlimesMod.Log.Warning("Missing clip " + name); return null; }
            try
            {
                if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                    throw new Exception("not a RIFF/WAVE file");
                int audioFmt = 0, channels = 1, rate = 44100, bits = 16, dataOff = -1, dataLen = 0;
                int p = 12;
                while (p + 8 <= bytes.Length)
                {
                    string id = Encoding.ASCII.GetString(bytes, p, 4);
                    int len = BitConverter.ToInt32(bytes, p + 4);
                    if (len < 0 || p + 8L + len > bytes.Length) throw new Exception($"bad wav chunk {id} len={len}");
                    if (id == "fmt " && len >= 16)
                    {
                        audioFmt = BitConverter.ToInt16(bytes, p + 8);
                        channels = BitConverter.ToInt16(bytes, p + 10);
                        rate = BitConverter.ToInt32(bytes, p + 12);
                        bits = BitConverter.ToInt16(bytes, p + 22);
                    }
                    else if (id == "data") { dataOff = p + 8; dataLen = len; break; }
                    p += 8 + len + (len & 1);
                }
                if (dataOff < 0 || audioFmt != 1 || bits != 16 || channels < 1 || rate < 1)
                    throw new Exception($"unsupported wav (fmt={audioFmt} bits={bits} ch={channels} rate={rate})");
                int samples = dataLen / 2;
                int frames = samples / channels;
                if (frames <= 0) throw new Exception("empty wav");
                var data = new float[samples];
                for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(bytes, dataOff + i * 2) / 32768f;
                c = AudioClip.Create(name, frames, channels, rate, false);
                c.SetData(new Il2CppStructArray<float>(data), 0);
                c.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(c);
                _clips[name] = c;
                return c;
            }
            catch (Exception e)
            {
                ClassicSlimesMod.Log.Warning($"Clip {name} failed: {e.Message}");
                return null;
            }
        }
    }
}
