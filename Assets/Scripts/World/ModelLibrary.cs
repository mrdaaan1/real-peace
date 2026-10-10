using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Модели из Blender в формате RPM1 (Tools/Blender/rp_kit.py): части с иерархией (корпус → башня → пушка, катки),
    /// у каждой части — сетка с подсетками по именам материалов. Файлы лежат в Resources/Models/*.bytes.
    /// Сетки разбираются один раз и делятся между всеми экземплярами.
    /// </summary>
    public static class ModelLibrary
    {
        public class Part
        {
            public string Name;
            public int Parent;
            public Vector3 Pivot;
            public Mesh Mesh;
            public string[] Materials;
            public bool HasMesh;
        }

        public class Model
        {
            public readonly List<Part> Parts = new List<Part>();
        }

        static readonly Dictionary<string, Model> cache = new Dictionary<string, Model>();

        public static Model Load(string name)
        {
            // Сетки могут быть выгружены при смене сцены — тогда читаем файл заново
            if (cache.TryGetValue(name, out var m) && (m == null || m.Parts.TrueForAll(p => !p.HasMesh || p.Mesh != null))) return m;
            var asset = Resources.Load<TextAsset>("Models/" + name);
            if (asset == null)
            {
                cache[name] = null;
                return null;
            }
            try
            {
                m = Parse(asset.bytes, name);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Real Peace] Модель " + name + " не прочиталась: " + e.Message);
                m = null;
            }
            cache[name] = m;
            return m;
        }

        static Model Parse(byte[] gz, string name)
        {
            using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            input.CopyTo(ms);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RPM1") throw new IOException("не RPM1");
            int count = r.ReadInt32();
            var model = new Model();
            for (int i = 0; i < count; i++)
            {
                var part = new Part
                {
                    Name = Str(r),
                    Parent = r.ReadInt32(),
                    Pivot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                };
                int nv = r.ReadInt32();
                var verts = new Vector3[nv];
                var norms = new Vector3[nv];
                for (int k = 0; k < nv; k++) verts[k] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int k = 0; k < nv; k++) norms[k] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                int subs = r.ReadInt32();
                part.Materials = new string[subs];
                var tris = new List<int[]>();
                for (int s = 0; s < subs; s++)
                {
                    part.Materials[s] = Str(r);
                    int ni = r.ReadInt32();
                    var idx = new int[ni];
                    for (int k = 0; k < ni; k++) idx[k] = r.ReadInt32();
                    tris.Add(idx);
                }
                if (nv > 0)
                {
                    var mesh = new Mesh { name = name + "_" + part.Name, indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    mesh.vertices = verts;
                    mesh.normals = norms;
                    mesh.subMeshCount = subs;
                    for (int s = 0; s < subs; s++) mesh.SetTriangles(tris[s], s);
                    mesh.RecalculateBounds();
                    part.Mesh = mesh;
                    part.HasMesh = true;
                }
                model.Parts.Add(part);
            }
            return model;
        }

        static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));

        /// <summary>Собрать одну часть модели с её потомками (например, одно оружие из набора).</summary>
        public static Dictionary<string, Transform> BuildPart(Model model, string rootName, Transform parent, System.Func<string, string, Material> material)
        {
            var result = new Dictionary<string, Transform>();
            var made = new Dictionary<int, Transform>();
            for (int i = 0; i < model.Parts.Count; i++)
            {
                var p = model.Parts[i];
                Transform parentT;
                if (p.Name == rootName) parentT = parent;
                else if (p.Parent >= 0 && made.TryGetValue(p.Parent, out var pt)) parentT = pt;
                else continue;
                var go = new GameObject(p.Name);
                var t = go.transform;
                t.SetParent(parentT, false);
                t.localPosition = p.Name == rootName ? Vector3.zero : p.Pivot;
                if (p.Mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                    var mats = new Material[p.Materials.Length];
                    for (int k = 0; k < mats.Length; k++) mats[k] = material(p.Name, p.Materials[k]);
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                made[i] = t;
                result[p.Name] = t;
            }
            return result;
        }

        /// <summary>Собрать экземпляр: объекты частей с сетками (материал — по имени части и слота). Вернёт словарь «имя части → Transform».</summary>
        public static Dictionary<string, Transform> Build(Model model, Transform parent, System.Func<string, string, Material> material)
        {
            var result = new Dictionary<string, Transform>();
            var list = new List<Transform>();
            foreach (var p in model.Parts)
            {
                var go = new GameObject(p.Name);
                var t = go.transform;
                t.SetParent(p.Parent >= 0 ? list[p.Parent] : parent, false);
                t.localPosition = p.Pivot;
                if (p.Mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    var mats = new Material[p.Materials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material(p.Name, p.Materials[i]);
                    mr.sharedMaterials = mats;
                }
                list.Add(t);
                result[p.Name] = t;
            }
            return result;
        }
    }
}
