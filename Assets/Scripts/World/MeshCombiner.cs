using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealPeace
{
    /// <summary>
    /// Склеивает тысячи мелких деталей неподвижного мира (деревья, изгороди, избы) в несколько больших сеток —
    /// по одной на материал в каждом квадрате карты. Так кадр рисуется сотней вызовов, а не десятью тысячами.
    /// </summary>
    public static class MeshCombiner
    {
        public static void Combine(Transform root, float chunkSize = 100f)
        {
            var groups = new Dictionary<(Material, int, int), List<CombineInstance>>();
            var counts = new Dictionary<(Material, int, int), int>();
            var toDestroy = new List<GameObject>();
            var toRoot = root.worldToLocalMatrix;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                var p = mf.transform.position;
                var key = (mr.sharedMaterial, Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));
                if (!groups.TryGetValue(key, out var list)) { list = new List<CombineInstance>(); groups[key] = list; counts[key] = 0; }
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toRoot * mf.transform.localToWorldMatrix });
                counts[key] += mf.sharedMesh.vertexCount;
                toDestroy.Add(mf.gameObject);
            }

            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = "Combined", indexFormat = counts[kv.Key] > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                var go = new GameObject("Combined_" + kv.Key.Item1.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key.Item1;
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            // Удаляем исходные детали, но не трогаем объекты с коллайдерами (они нужны для линии видимости)
            foreach (var go in toDestroy)
            {
                if (go == null) continue;
                if (go.GetComponent<Collider>() != null)
                {
                    Object.Destroy(go.GetComponent<MeshRenderer>());
                    Object.Destroy(go.GetComponent<MeshFilter>());
                }
                else Object.Destroy(go);
            }
        }
    }
}
