// Unity を起動せず、実際の分類・ラスタライズ処理を数値で検証する。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    internal static class InnerColorMaskTests
    {
        private static int _checks;
        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new Exception(message);
        }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.00001f; }
        private static List<float[]> Classify(List<Vector3[]> points, List<int[]> used, int mode, float depth, float height)
        {
            var result = new List<float[]>();
            InnerColorMask.Classify(points, used, mode, depth, height, (i, values) => result.Add(values));
            return result;
        }
        public static void Main()
        {
            // 別レンダラー・別マテリアルの外側の層も比較に含む。対象外の頂点は混ぜない。
            var points = new List<Vector3[]>
            {
                new[] { new Vector3(0, 0, 2), new Vector3(0, 0, 100) },
                new[] { new Vector3(0, 0, 1.5f), new Vector3(0, 0, 1.95f) },
            };
            var used = new List<int[]> { new[] { 0 }, new[] { 0, 1 } };
            var layers = Classify(points, used, 0, 0.08f, 0.5f);
            Check(Near(layers[0][0], 0) && Near(layers[0][1], 0), "Outer/unused vertices must be black");
            Check(Near(layers[1][0], 1) && Near(layers[1][1], 0), "Only deeper inner layer must be white");
            var deep = Classify(points, used, 0, 0.3f, 0.5f);
            Check(deep[1][0] < layers[1][0], "Depth threshold must narrow the colored region");

            // 球面の左右の区切り（atan2 の ±PI）をまたいでも内側を見つける。
            points.Add(new[] { new Vector3(0.00001f, 0, -2), new Vector3(-0.00001f, 0, -1.5f) });
            used.Add(new[] { 0, 1 });
            var rear = Classify(points, used, 2, 0.08f, 0.5f);
            Check(Near(rear[1][0], 0), "Rear mode must exclude front inner layers");
            Check(Near(rear[2][0], 0) && Near(rear[2][1], 1), "Rear seam must keep inner layer");

            points = new List<Vector3[]> { new[] { new Vector3(0, -2, 1), new Vector3(0, -0.5f, 1), new Vector3(0, 0.1f, 1) } };
            used = new List<int[]> { new[] { 0, 1, 2 } };
            var low = Classify(points, used, 1, 0.08f, 0.5f)[0];
            var high = Classify(points, used, 1, 0.08f, 1)[0];
            Check(Near(low[0], 1) && Near(low[1], 0) && Near(low[2], 0), "Lower mode must color only tips below cutoff");
            Check(Near(high[1], 1) && Near(high[2], 0), "Height must expand only below head center");

            const int size = 32;
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) };
            var triangles = new[] { 0, 1, 2 };
            var pixels = new float[size * size];
            InnerColorMask.Paint(pixels, size, uv, new[] { 0f, 1f, 0f }, triangles, new Vector2(1, 1), new Vector2(0, 0));
            Check(Near(pixels[3 * size + 5], 5.5f / size), "Mask must interpolate vertex values barycentrically");
            Check(Near(pixels[30 * size + 30], 0), "Triangle bounding box must not color outside triangle");
            var reversed = new float[pixels.Length];
            InnerColorMask.Paint(reversed, size, uv, new[] { 0f, 1f, 0f }, new[] { 0, 2, 1 }, new Vector2(1, 1), new Vector2(0, 0));
            Check(pixels.Zip(reversed, Near).All(x => x), "Both triangle windings must rasterize identically");
            InnerColorMask.Paint(pixels, size, uv, new[] { 1f, 1f, 1f }, triangles, new Vector2(1, 1), new Vector2(0, 0));
            InnerColorMask.Paint(pixels, size, uv, new[] { 0f, 0f, 0f }, triangles, new Vector2(1, 1), new Vector2(0, 0));
            Check(Near(pixels[3 * size + 5], 1), "Overlapping inner/outer UV must retain maximum, independent of order");

            var seamUV = new[] { new Vector2(0.9f, 0.1f), new Vector2(1.1f, 0.1f), new Vector2(0.9f, 0.9f) };
            var seam = new float[pixels.Length];
            InnerColorMask.Paint(seam, size, seamUV, new[] { 1f, 1f, 1f }, triangles, new Vector2(1, 1), new Vector2(0, 0));
            Check(seam[6 * size + 30] > 0 && seam[6 * size + 1] > 0, "UV seam triangle must paint both texture edges");
            Check(Near(seam[6 * size + 16], 0), "UV seam must not stretch across texture center");
            var transformed = new float[pixels.Length];
            InnerColorMask.Paint(transformed, size, uv, new[] { 1f, 1f, 1f }, triangles, new Vector2(-0.5f, 0.5f), new Vector2(0.8f, 0.2f));
            Check(transformed[9 * size + 20] > 0 && Near(transformed[0], 0), "MainTex negative scale and offset must transform mask UV");
            var degenerate = new float[pixels.Length];
            InnerColorMask.Paint(degenerate, size, uv, new[] { 1f, 1f, 1f }, new[] { 0, 0, 0 }, new Vector2(1, 1), new Vector2(0, 0));
            Check(degenerate.All(v => v == 0), "Degenerate UV triangles must be skipped");

            var impulse = new float[pixels.Length]; impulse[0] = 1;
            var blur = InnerColorMask.Blur(impulse, size);
            Check(Near(blur.Sum(), 1), "Blur must preserve mask energy");
            Check(blur[0] > 0 && blur[0] < 1 && blur[1] > 0 && blur[size - 1] > 0, "Blur must feather and wrap edges");
            Check(InnerColorMask.Blur(Enumerable.Repeat(1f, pixels.Length).ToArray(), size).All(v => Near(v, 1)), "Constant masks must stay constant");

            var rejected = false;
            try { InnerColorMask.Paint(new float[pixels.Length], size, new[] { new Vector2(float.NaN, 0), uv[1], uv[2] }, new[] { 1f, 1f, 1f }, triangles, new Vector2(1, 1), new Vector2(0, 0)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Non-finite UV must fail safely");
            rejected = false;
            try { InnerColorMask.Paint(new float[pixels.Length], size, uv, new[] { 1f, 1f, 1f }, triangles, new Vector2(100, 100), new Vector2(0, 0)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unbounded tiling must fail safely");
            Console.WriteLine("InnerColor mask tests passed (" + _checks + " checks).");
        }
    }
}
