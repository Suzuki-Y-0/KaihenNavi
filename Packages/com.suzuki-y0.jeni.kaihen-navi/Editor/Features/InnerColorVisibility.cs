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
    /// <summary>頭を囲む正射影の深度を使い、頂点補間をせずに UV の各画素で見え方を測る。</summary>
    internal sealed class InnerColorVisibility
    {
        internal sealed class Source
        {
            public InnerColorOpenness.Geometry geometry;
            public Material[] materials;
            public bool[] selected;
        }

        internal sealed class Map
        {
            public Color[] votes, scope;

            internal float[] Mask(int mode, float range, float height, int size)
            {
                var result = new float[votes.Length];
                var coverage = new bool[result.Length];
                for (var i = 0; i < result.Length; i++)
                {
                    if (scope[i].b < 0.5f || votes[i].b < 0.5f) continue;
                    coverage[i] = true;
                    var seen = mode == 2 ? votes[i].g / Math.Max(0.00001f, votes[i].a) : votes[i].r / votes[i].b;
                    // 毛先は高さを主に使い、外からはっきり見える表面だけを除く。
                    var value = InnerColorMask.FromOpenness(seen, mode == 1 ? 0.4f : range);
                    if (mode == 1) value *= InnerColorMask.LowerWeight(scope[i].r, height);
                    if (mode == 2) value *= scope[i].g;
                    result[i] = value;
                }
                return InnerColorMask.PadIslands(result, coverage, size, 3);
            }
        }

        private sealed class Draw
        {
            internal Mesh mesh;
            internal Material probe;
            internal int submesh;
        }

        private string _key;
        private Dictionary<Material, Map> _maps;
        internal void Clear() { _key = null; _maps = null; }

        /// <summary>色や範囲だけの変更では再撮影しない。失敗時には完成済みのキャッシュを残す。</summary>
        internal Dictionary<Material, Map> Get(List<Source> sources, Vector3 head, Quaternion rotation, int size)
        {
            if (EditorPrefs.GetBool("KaihenNavi.InnerColor.DisableGpu", false)) return null;
            var shader = Shader.Find("Hidden/KaihenNavi/InnerColorVisibility");
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
                !SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat) || SystemInfo.maxTextureSize < size * 2)
                return null;
            var key = CacheKey(sources, head, rotation, size);
            if (_maps != null && _key == key) return _maps;
            var maps = Measure(shader, sources, head, rotation, size);
            _key = key;
            _maps = maps;
            return maps;
        }

        internal static string CacheKey(List<Source> sources, Vector3 head, Quaternion rotation, int size)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var hash = SHA256.Create())
            {
                writer.Write(InnerColorOpenness.CacheKey(0, sources.Select(s => s.geometry).ToList()));
                writer.Write(size);
                Write(writer, head);
                writer.Write(rotation.x); writer.Write(rotation.y); writer.Write(rotation.z); writer.Write(rotation.w);
                foreach (var source in sources)
                {
                    // レイ版と違い、UV の編集や MainTex の透明部分・繰り返しも結果を変える。
                    writer.Write(source.geometry.uv.Length);
                    foreach (var uv in source.geometry.uv) { writer.Write(uv.x); writer.Write(uv.y); }
                    writer.Write(source.materials.Length);
                    for (var i = 0; i < source.materials.Length; i++)
                    {
                        var m = source.materials[i];
                        writer.Write(source.selected[i]);
                        writer.Write(m == null ? 0 : m.GetInstanceID());
                        if (m == null) continue;
                        var texture = MainTexture(m);
                        writer.Write(texture == null ? 0 : texture.GetInstanceID());
                        writer.Write(texture == null ? "" : texture.imageContentsHash.ToString());
                        writer.Write(texture == null ? 0 : (int)texture.wrapMode);
                        writer.Write(texture == null ? 0 : (int)texture.filterMode);
                        var st = TextureST(m);
                        writer.Write(st.x); writer.Write(st.y); writer.Write(st.z); writer.Write(st.w);
                        writer.Write(Cutoff(m)); writer.Write(Alpha(m));
                    }
                }
                writer.Flush(); stream.Position = 0;
                return Convert.ToBase64String(hash.ComputeHash(stream));
            }
        }

        private static void Write(BinaryWriter writer, Vector3 p)
        { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }

        internal static Vector3[] Directions()
        {
            var result = new Vector3[64];
            var angle = Math.PI * (3 - Math.Sqrt(5));
            for (var i = 0; i < result.Length; i++)
            {
                // 横・上からを多くし、少し下からも測る。真下から頭越しに見える票は入れない。
                var y = 1 - 1.2 * (i + 0.5) / result.Length;
                var radius = Math.Sqrt(1 - y * y);
                result[i] = new Vector3((float)(Math.Cos(angle * i) * radius), (float)y, (float)(Math.Sin(angle * i) * radius));
            }
            return result;
        }

        private static string TextureProperty(Material m)
        { return m.HasProperty("_MainTex") ? "_MainTex" : m.HasProperty("_BaseMap") ? "_BaseMap" : null; }
        private static Texture MainTexture(Material m)
        { var property = TextureProperty(m); return property == null ? null : m.GetTexture(property); }
        private static Vector4 TextureST(Material m)
        {
            var property = TextureProperty(m);
            if (property == null) return new Vector4(1, 1, 0, 0);
            var scale = m.GetTextureScale(property); var offset = m.GetTextureOffset(property);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
        private static float Alpha(Material m)
        {
            return m.HasProperty("_Color") ? m.GetColor("_Color").a :
                m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").a : 1;
        }
        private static float Cutoff(Material m)
        {
            // 不透明な素体のテクスチャには、未使用のアルファが入っている商品もある。
            if (m.renderQueue < 2450) return -1;
            return m.HasProperty("_Cutoff") ? Math.Max(0.001f, m.GetFloat("_Cutoff")) : 0.1f;
        }
        private static Material Probe(Shader shader, Material source, List<Object> cleanup)
        {
            var probe = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            cleanup.Add(probe);
            probe.SetTexture("_MainTex", MainTexture(source) ?? Texture2D.whiteTexture);
            probe.SetVector("_MainTex_ST", TextureST(source));
            probe.SetFloat("_KNAlpha", Alpha(source));
            probe.SetFloat("_KNCutoff", Cutoff(source));
            return probe;
        }

        private static RenderTexture Target(int size, int depth, List<Object> cleanup)
        {
            var target = new RenderTexture(size, size, depth, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1 };
            cleanup.Add(target);
            if (!target.Create()) throw new InvalidOperationException("InnerColor: RenderTexture could not be created");
            return target;
        }
        private static Color[] Read(RenderTexture target, int size, Texture2D reader)
        {
            RenderTexture.active = target;
            reader.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            return reader.GetPixels();
        }

        private static Dictionary<Material, Map> Measure(Shader shader, List<Source> sources, Vector3 head, Quaternion rotation, int size)
        {
            var cleanup = new List<Object>();
            var previous = RenderTexture.active;
            var buffer = new CommandBuffer { name = "KaihenNavi InnerColor visibility" };
            try
            {
                EditorUtility.DisplayProgressBar(L10n.Tr("feature.innerColor.title"), L10n.Tr("innerColor.measuring"), 0);
                var cameraDraws = new List<Draw>();
                var uvDraws = new Dictionary<Material, Draw>();
                var selected = sources.Where(s => s.selected.Any(v => v)).ToList();
                var points = selected.SelectMany(s => s.geometry.used.Select(i => s.geometry.positions[i])).ToList();
                var radius = Math.Max(0.05f, points.Max(p => Vector3.Distance(p, head))) * 1.05f;
                var allRadius = Math.Max(radius, sources.SelectMany(s => s.geometry.positions).Max(p => Vector3.Distance(p, head)));
                var distance = allRadius * 2 + 1;
                var lowest = points.Min(p => (Quaternion.Inverse(rotation) * (p - head)).y);
                var headSpace = Matrix4x4.TRS(head, rotation, Vector3.one).inverse;
                var uvProjection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, 1, 0, 1, -1, 1), true);
                foreach (var source in sources)
                {
                    var g = source.geometry;
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    cleanup.Add(mesh);
                    mesh.vertices = g.positions;
                    mesh.uv = g.uv.Length == g.positions.Length ? g.uv : new Vector2[g.positions.Length];
                    mesh.subMeshCount = g.triangles.Length;
                    for (var i = 0; i < g.triangles.Length; i++)
                    {
                        mesh.SetTriangles(g.triangles[i], i);
                        if (i >= source.materials.Length || source.materials[i] == null || g.triangles[i].Length == 0) continue;
                        cameraDraws.Add(new Draw { mesh = mesh, submesh = i, probe = Probe(shader, source.materials[i], cleanup) });
                    }
                    mesh.RecalculateBounds();
                }
                foreach (var material in selected.SelectMany(s => s.materials.Where((m, i) => s.selected[i])).Distinct())
                {
                    var mesh = UVGeometry(sources, material);
                    cleanup.Add(mesh);
                    var probe = Probe(shader, material, cleanup);
                    probe.SetMatrix("_KNUVProjection", uvProjection);
                    probe.SetMatrix("_KNHeadSpace", headSpace);
                    probe.SetFloat("_KNLowest", lowest);
                    // 0.1mm だけ自己遮蔽の誤差を許す。深度の距離で調べ、別の房への膨張はしない。
                    probe.SetFloat("_KNEpsilon", 0.0001f);
                    probe.SetFloat("_KNCameraSize", size * 2);
                    uvDraws.Add(material, new Draw { mesh = mesh, probe = probe });
                }
                var camera = Target(size * 2, 24, cleanup);
                var viewVotes = Target(size, 0, cleanup);
                var accumulator = uvDraws.Keys.ToDictionary(m => m, m => Target(size, 0, cleanup));
                var add = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                cleanup.Add(add);
                foreach (var target in accumulator.Values)
                {
                    buffer.SetRenderTarget(target); buffer.ClearRenderTarget(false, true, Color.clear);
                }
                Graphics.ExecuteCommandBuffer(buffer); buffer.Clear();
                var directions = Directions();
                for (var i = 0; i < directions.Length; i++)
                {
                    EditorUtility.DisplayProgressBar(L10n.Tr("feature.innerColor.title"), L10n.Tr("innerColor.measuring"), (float)i / directions.Length);
                    var direction = rotation * directions[i];
                    var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
                        Matrix4x4.TRS(head + direction * distance, Quaternion.LookRotation(-direction, rotation * Vector3.up), Vector3.one).inverse;
                    var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-radius, radius, -radius, radius,
                        distance - allRadius - 0.1f, distance + allRadius + 0.1f), true);
                    var vp = projection * view;
                    buffer.SetRenderTarget(camera);
                    buffer.ClearRenderTarget(true, true, new Color(0, 0, distance + allRadius + 1, 0));
                    foreach (var draw in cameraDraws)
                    {
                        draw.probe.SetMatrix("_KNVP", vp); draw.probe.SetMatrix("_KNView", view);
                        buffer.DrawMesh(draw.mesh, Matrix4x4.identity, draw.probe, draw.submesh, 0);
                    }
                    foreach (var pair in uvDraws)
                    {
                        var draw = pair.Value;
                        draw.probe.SetMatrix("_KNVP", vp); draw.probe.SetMatrix("_KNView", view);
                        draw.probe.SetTexture("_KNViewTexture", camera);
                        draw.probe.SetFloat("_KNRear", Math.Max(0, -directions[i].z));
                        draw.probe.SetVector("_KNDir", new Vector4(direction.x, direction.y, direction.z, 0f));
                        buffer.SetRenderTarget(viewVotes); buffer.ClearRenderTarget(false, true, Color.clear);
                        buffer.DrawMesh(draw.mesh, Matrix4x4.identity, draw.probe, 0, 1);
                        buffer.Blit(viewVotes, accumulator[pair.Key], add, 3);
                    }
                    Graphics.ExecuteCommandBuffer(buffer); buffer.Clear();
                }
                var result = new Dictionary<Material, Map>();
                var reader = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                cleanup.Add(reader);
                foreach (var pair in uvDraws)
                {
                    var votes = Read(accumulator[pair.Key], size, reader);
                    buffer.SetRenderTarget(viewVotes); buffer.ClearRenderTarget(false, true, Color.clear);
                    buffer.DrawMesh(pair.Value.mesh, Matrix4x4.identity, pair.Value.probe, 0, 2);
                    Graphics.ExecuteCommandBuffer(buffer); buffer.Clear();
                    var scope = Read(viewVotes, size, reader);
                    // 非対応 GPU やコンパイル失敗を、全面「内側」の結果として保存しない。
                    if (!votes.Any(v => v.b > 0.5f) || !scope.Any(v => v.b > 0.5f))
                        throw new InvalidOperationException("InnerColor: GPU measurement returned no UV coverage");
                    result.Add(pair.Key, new Map { votes = votes, scope = scope });
                }
                if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("InnerColor: visibility shader could not be compiled");
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                buffer.Release();
                foreach (var obj in cleanup)
                {
                    var target = obj as RenderTexture;
                    if (target != null) target.Release();
                    Object.DestroyImmediate(obj);
                }
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>各三角形をタイル単位で複製する。頂点ごとの frac は継ぎ目を横切って伸びるので使わない。</summary>
        private static Mesh UVGeometry(List<Source> sources, Material material)
        {
            var positions = new List<Vector3>(); var uv = new List<Vector2>(); var sourceUV = new List<Vector2>(); var normals = new List<Vector3>();
            foreach (var source in sources)
                for (var slot = 0; slot < source.materials.Length; slot++)
                {
                    if (!source.selected[slot] || source.materials[slot] != material || slot >= source.geometry.triangles.Length) continue;
                    var g = source.geometry; var triangles = g.triangles[slot]; var st = TextureST(material);
                    for (var t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        var corners = new Vector2[3];
                        for (var j = 0; j < 3; j++)
                        {
                            var value = g.uv[triangles[t + j]];
                            corners[j] = new Vector2(value.x * st.x + st.z, value.y * st.y + st.w);
                        }
                        if (corners.Any(p => float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)))
                            throw new InvalidOperationException(L10n.Tr("innerColor.invalidUV"));
                        var minX = Math.Floor(corners.Min(p => p.x)); var maxX = Math.Floor(corners.Max(p => p.x));
                        var minY = Math.Floor(corners.Min(p => p.y)); var maxY = Math.Floor(corners.Max(p => p.y));
                        if ((maxX - minX + 1) * (maxY - minY + 1) > 64 || Math.Abs(minX) > 1000000 || Math.Abs(minY) > 1000000)
                            throw new InvalidOperationException(L10n.Tr("innerColor.wideUV"));
                        // 三角形の表の向き（ワールド）。面がカメラを向いている方向だけを数えるために使う。
                        var p0 = g.positions[triangles[t]]; var p1 = g.positions[triangles[t + 1]]; var p2 = g.positions[triangles[t + 2]];
                        var faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                        for (var y = (int)minY; y <= maxY; y++)
                            for (var x = (int)minX; x <= maxX; x++)
                                for (var j = 0; j < 3; j++)
                                {
                                    positions.Add(g.positions[triangles[t + j]]);
                                    normals.Add(faceNormal);
                                    uv.Add(new Vector2(corners[j].x - x, corners[j].y - y));
                                    // Clamp や Mirror の透明部分は、タイル移動前の座標で読む。
                                    sourceUV.Add(corners[j]);
                                }
                    }
                }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.vertices = positions.ToArray(); mesh.uv = uv.ToArray(); mesh.uv2 = sourceUV.ToArray(); mesh.normals = normals.ToArray();
                mesh.triangles = Enumerable.Range(0, positions.Count).ToArray(); mesh.RecalculateBounds();
                return mesh;
            }
            catch { Object.DestroyImmediate(mesh); throw; }
        }
    }
}
