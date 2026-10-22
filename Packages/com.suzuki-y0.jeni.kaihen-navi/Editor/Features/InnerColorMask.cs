using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Jeni.KaihenNavi
{
    /// <summary>焼いた髪の位置 → 頂点の内側度 → UV マスク。元の Mesh/Texture は変更しない。</summary>
    internal static class InnerColorMask
    {
        private static double Radius(Vector3 p) { return Math.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z); }
        private static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); }
        private static float Smooth(float value) { var t = Clamp01(value); return t * t * (3 - 2 * t); }

        // UI の 1〜40 を、開け具合のしきい値 0.08〜0.40 に対応させる。
        // 大きくするほど外から見えやすい場所まで含む。64 本のレイでも境目が段にならないようにする。
        internal static float FromOpenness(float openness, float range)
        {
            var threshold = 0.08f + Clamp01((range - 0.01f) / 0.39f) * 0.32f;
            return Smooth((threshold - Clamp01(openness)) / 0.06f + 0.5f);
        }

        /// <param name="used">色を変える枠で使う頂点。他の枠には色をつけない。</param>
        /// <param name="openness">ワールド座標の髪・体に対して測定済みの開け具合。mode 1 では不要。</param>
        internal static void Classify(List<Vector3[]> positions, List<int[]> used, List<float[]> openness,
            int mode, float range, float height, Action<int, float[]> receive)
        {
            var lowest = 0f;
            for (var s = 0; s < positions.Count; s++)
                foreach (var i in used[s]) lowest = Math.Min(lowest, positions[s][i].y);
            var cutoff = lowest * (1 - Clamp01(height));
            var heightFeather = Math.Max(0.00001f, -lowest * 0.05f);
            for (var s = 0; s < positions.Count; s++)
            {
                var values = new float[positions[s].Length];
                foreach (var i in used[s])
                {
                    var p = positions[s][i];
                    if (mode == 1)
                    {
                        // 上端は必ず頭の中心以下。0=毛先だけ、1=頭の中心まで。
                        values[i] = p.y >= 0 ? 0 : Smooth((cutoff - p.y) / heightFeather);
                        continue;
                    }
                    values[i] = FromOpenness(openness[s][i], range);
                    // アバター前方は +Z。後頭部側のみ、中心の近くはなだらかにする。
                    if (mode == 2)
                    {
                        var radius = Radius(p);
                        values[i] *= p.z >= 0 || radius < 0.000001 ? 0 : Smooth((float)(-p.z / radius) / 0.15f);
                    }
                }
                receive(s, values);
            }
        }

        internal static void Paint(float[] pixels, int size, Vector2[] uv, float[] values, int[] triangles, Vector2 scale, Vector2 offset)
        {
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var ia = triangles[t]; var ib = triangles[t + 1]; var ic = triangles[t + 2];
                var a = TransformUV(uv[ia], scale, offset);
                var b = TransformUV(uv[ib], scale, offset);
                var c = TransformUV(uv[ic], scale, offset);
                if (!Finite(a) || !Finite(b) || !Finite(c)) throw new InvalidOperationException(L10n.Tr("innerColor.invalidUV"));
                // UV を頂点ごとに Repeat すると継ぎ目をまたぐ三角形が伸びるので、三角形全体をタイルごとに描く。
                var originX = (float)Math.Floor(a.x); var originY = (float)Math.Floor(a.y);
                a = new Vector2(a.x - originX, a.y - originY);
                b = new Vector2(b.x - originX, b.y - originY);
                c = new Vector2(c.x - originX, c.y - originY);
                var minX = Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)));
                var maxX = Math.Floor(Math.Max(a.x, Math.Max(b.x, c.x)));
                var minY = Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y)));
                var maxY = Math.Floor(Math.Max(a.y, Math.Max(b.y, c.y)));
                if ((maxX - minX + 1) * (maxY - minY + 1) > 64)
                    throw new InvalidOperationException(L10n.Tr("innerColor.wideUV"));
                for (var y = (int)minY; y <= maxY; y++)
                    for (var x = (int)minX; x <= maxX; x++)
                        Triangle(pixels, size, new Vector2(a.x - x, a.y - y), new Vector2(b.x - x, b.y - y),
                            new Vector2(c.x - x, c.y - y), values[ia], values[ib], values[ic]);
            }
        }
        private static Vector2 TransformUV(Vector2 uv, Vector2 scale, Vector2 offset)
        {
            return new Vector2(uv.x * scale.x + offset.x, uv.y * scale.y + offset.y);
        }
        private static bool Finite(Vector2 uv)
        {
            return !float.IsNaN(uv.x) && !float.IsNaN(uv.y) && !float.IsInfinity(uv.x) && !float.IsInfinity(uv.y);
        }
        private static float Cross(float ax, float ay, float bx, float by) { return ax * by - ay * bx; }
        private static void Triangle(float[] pixels, int size, Vector2 a, Vector2 b, Vector2 c, float va, float vb, float vc)
        {
            var area = Cross(b.x - a.x, b.y - a.y, c.x - a.x, c.y - a.y);
            if (Math.Abs(area) < 0.00000001f) return;
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) * size));
            var maxX = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x)) * size));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y)) * size));
            var maxY = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.y, Math.Max(b.y, c.y)) * size));
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var px = (x + 0.5f) / size; var py = (y + 0.5f) / size;
                    var wa = Cross(b.x - px, b.y - py, c.x - px, c.y - py) / area;
                    var wb = Cross(c.x - px, c.y - py, a.x - px, a.y - py) / area;
                    var wc = 1 - wa - wb;
                    if (wa < -0.00001f || wb < -0.00001f || wc < -0.00001f) continue;
                    var value = Clamp01(wa * va + wb * vb + wc * vc);
                    var index = y * size + x;
                    pixels[index] = Math.Max(pixels[index], value);
                }
        }

        internal static float[] Blur(float[] source, int size)
        {
            var temp = new float[source.Length];
            var result = new float[source.Length];
            var weights = new[] { 1f, 4f, 6f, 4f, 1f };
            // 半径2pxのガウスぼかし。Repeat の継ぎ目にも同じぼかしをかける。
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    for (var d = -2; d <= 2; d++)
                        temp[y * size + x] += source[y * size + (x + d + size) % size] * weights[d + 2] / 16;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    for (var d = -2; d <= 2; d++)
                        result[y * size + x] += temp[((y + d + size) % size) * size + x] * weights[d + 2] / 16;
            return result;
        }

        internal static Texture2D Save(string path, string key, float[] pixels, int size)
        {
            if (!WorkFolders.IsToolPath(path)) throw new InvalidOperationException(L10n.Tr("innerColor.importFailed"));
            var blurred = Blur(pixels, size);
            var colors = new Color32[blurred.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                var value = (byte)Math.Round(Clamp01(blurred[i]) * 255);
                colors[i] = new Color32(value, value, value, 255); // ブレンドマスクは R チャンネル。
            }
            WorkFolders.Ensure(Path.GetDirectoryName(path).Replace('\\', '/'));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(colors);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException(L10n.Tr("innerColor.importFailed"));
            importer.userData = InnerColorAssets.Tag + key;
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result == null) throw new InvalidOperationException(L10n.Tr("innerColor.importFailed"));
            return result;
        }
    }
}
