using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    /// <summary>現在の髪・体だけを遮蔽物として、外へ抜ける方向の割合を測る。Editor 内のみ。</summary>
    internal static class InnerColorOpenness
    {
        internal sealed class Geometry
        {
            public int rendererId;
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector2[] uv;
            public int[][] triangles;
            // 空配列は遮るだけのメッシュ。マテリアルの複製でキーが変わらないよう、参照は含めない。
            public int[] used = new int[0];
        }

        internal sealed class Cache
        {
            private string _key;
            private List<float[]> _values;
            internal List<float[]> Get(string key, Func<List<float[]>> measure)
            {
                if (_values != null && _key == key) return _values;
                var values = measure(); // 途中で失敗した値は保存しない。
                _key = key;
                _values = values;
                return values;
            }
            internal void Clear() { _key = null; _values = null; }
        }

        internal static Geometry Bake(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // useScale=false のローカル座標を、一度だけワールド座標へ変換する。
                renderer.BakeMesh(mesh, false);
                if (mesh.normals.Length != mesh.vertexCount) mesh.RecalculateNormals();
                var data = new Geometry
                {
                    rendererId = renderer.GetInstanceID(), positions = mesh.vertices, normals = mesh.normals,
                    uv = mesh.uv, triangles = new int[mesh.subMeshCount][],
                };
                var normalMatrix = renderer.transform.localToWorldMatrix.inverse.transpose;
                for (var i = 0; i < data.positions.Length; i++)
                {
                    data.positions[i] = renderer.transform.TransformPoint(data.positions[i]);
                    data.normals[i] = normalMatrix.MultiplyVector(data.normals[i]).normalized;
                }
                for (var i = 0; i < data.triangles.Length; i++) data.triangles[i] = mesh.GetTriangles(i);
                return data;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // 形状・姿勢・選択が同じなら同じキー。色、範囲、モード、画質、マテリアルの参照は含めない。
        // 焼いた頂点を比較するので、ボーン・BlendShape・拡縮・元メッシュの編集も検出する。
        internal static string CacheKey(int avatarId, List<Geometry> geometry)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var hash = SHA256.Create())
            {
                writer.Write(avatarId);
                writer.Write(geometry.Count);
                foreach (var g in geometry)
                {
                    writer.Write(g.rendererId);
                    Write(writer, g.positions);
                    Write(writer, g.normals);
                    writer.Write(g.triangles.Length);
                    foreach (var triangles in g.triangles) Write(writer, triangles);
                    Write(writer, g.used);
                }
                writer.Flush();
                stream.Position = 0;
                return Convert.ToBase64String(hash.ComputeHash(stream));
            }
        }
        private static void Write(BinaryWriter writer, Vector3[] values)
        {
            writer.Write(values.Length);
            foreach (var p in values) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
        }
        private static void Write(BinaryWriter writer, int[] values)
        {
            writer.Write(values.Length);
            foreach (var value in values) writer.Write(value);
        }

        internal static Vector3[] Directions()
        {
            const int count = 64;
            var result = new Vector3[count];
            var angle = Math.PI * (3 - Math.Sqrt(5));
            for (var i = 0; i < count; i++)
            {
                var y = 1 - 2 * (i + 0.5) / count;
                var radius = Math.Sqrt(1 - y * y);
                result[i] = new Vector3((float)(Math.Cos(angle * i) * radius), (float)y, (float)(Math.Sin(angle * i) * radius));
            }
            return result;
        }

        internal static string SampleKey(Vector3 p, Vector3 normal)
        {
            // 位置は 2mm。折り返しのある髪を混ぜないよう、法線の向きも約4度ずつ区切る。
            // 両面を測るので、逆向きの法線は同じキーでよい。
            var sign = normal.x != 0 ? Math.Sign(normal.x) : normal.y != 0 ? Math.Sign(normal.y) : Math.Sign(normal.z);
            return Math.Round(p.x / 0.002) + ":" + Math.Round(p.y / 0.002) + ":" + Math.Round(p.z / 0.002) + ":" +
                Math.Round(normal.x * sign * 16) + ":" + Math.Round(normal.y * sign * 16) + ":" + Math.Round(normal.z * sign * 16);
        }

        internal static List<float[]> Measure(List<Geometry> geometry)
        {
            var objects = new List<GameObject>();
            var meshes = new List<Mesh>();
            var colliders = new HashSet<Collider>();
            var backfaces = Physics.queriesHitBackfaces;
            try
            {
                EditorUtility.DisplayProgressBar(L10n.Tr("feature.innerColor.title"), L10n.Tr("innerColor.measuring"), 0);
                Physics.queriesHitBackfaces = true;
                // Ignore Raycast レイヤーを明示して検索する。他のシーンの物は結果から除外する。
                foreach (var g in geometry)
                {
                    if (g.positions.Length == 0 || !g.triangles.Any(t => t.Length > 0)) continue;
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    meshes.Add(mesh);
                    mesh.vertices = g.positions;
                    mesh.triangles = g.triangles.SelectMany(t => t).ToArray();
                    mesh.RecalculateBounds();
                    var go = new GameObject("KaihenNavi InnerColor probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                    objects.Add(go);
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    colliders.Add(collider);
                }
                Physics.SyncTransforms();
                // 全遮蔽物の対角線より長い距離。頂点がどこにあっても反対側を越える。
                var all = geometry.SelectMany(g => g.positions).ToList();
                var min = new Vector3(all.Min(p => p.x), all.Min(p => p.y), all.Min(p => p.z));
                var max = new Vector3(all.Max(p => p.x), all.Max(p => p.y), all.Max(p => p.z));
                var distance = Math.Max(1f, Vector3.Distance(min, max) * 2 + 0.01f);
                var directions = Directions();
                var samples = new Dictionary<string, float>();
                var hits = new RaycastHit[32];
                var total = geometry.Sum(g => g.used.Length);
                var done = 0;
                var result = new List<float[]>();
                foreach (var g in geometry)
                {
                    var values = new float[g.positions.Length];
                    foreach (var i in g.used)
                    {
                        if (done % 128 == 0)
                            EditorUtility.DisplayProgressBar(L10n.Tr("feature.innerColor.title"), L10n.Tr("innerColor.measuring"), (float)done / Math.Max(1, total));
                        done++;
                        var key = SampleKey(g.positions[i], g.normals[i]);
                        float value;
                        if (!samples.TryGetValue(key, out value))
                        {
                            // 0.3mm ずらし、表と裏それぞれの開け具合の大きい方を使う。
                            var front = Open(g.positions[i] + g.normals[i] * 0.0003f, directions, distance, colliders, ref hits);
                            var back = Open(g.positions[i] - g.normals[i] * 0.0003f, directions, distance, colliders, ref hits);
                            value = Math.Max(front, back);
                            samples.Add(key, value);
                        }
                        values[i] = value;
                    }
                    result.Add(values);
                }
                return result;
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
                foreach (var go in objects) Object.DestroyImmediate(go);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                EditorUtility.ClearProgressBar();
            }
        }

        private static float Open(Vector3 origin, Vector3[] directions, float distance, HashSet<Collider> colliders, ref RaycastHit[] hits)
        {
            var escaped = 0;
            foreach (var direction in directions)
            {
                RaycastHit first;
                if (!Physics.Raycast(origin, direction, out first, distance, 1 << 2, QueryTriggerInteraction.Ignore))
                { escaped++; continue; }
                if (colliders.Contains(first.collider)) continue;
                // 同レイヤーの既存 Collider で結果が変わらないよう、一時 Collider だけを数える。
                // NonAlloc の順序は未定義。満杯なら配列を広げて取り直し、遮蔽物の取りこぼしを防ぐ。
                int count;
                while ((count = Physics.RaycastNonAlloc(origin, direction, hits, distance, 1 << 2, QueryTriggerInteraction.Ignore)) == hits.Length)
                    Array.Resize(ref hits, hits.Length * 2);
                var blocked = false;
                for (var i = 0; i < count; i++)
                    if (colliders.Contains(hits[i].collider)) { blocked = true; break; }
                if (!blocked) escaped++;
            }
            return (float)escaped / directions.Length;
        }
    }
}
