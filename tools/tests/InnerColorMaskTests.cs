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
        private static List<float[]> Classify(List<Vector3[]> points, List<int[]> used, List<float[]> openness,
            int mode, float range, float height)
        {
            var result = new List<float[]>();
            InnerColorMask.Classify(points, used, openness, mode, range, height, (i, values) => result.Add(values));
            return result;
        }
        private static InnerColorOpenness.Geometry Geometry(int id)
        {
            return new InnerColorOpenness.Geometry
            {
                rendererId = id,
                positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                normals = new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) },
                uv = new Vector2[3], triangles = new[] { new[] { 0, 1, 2 } }, used = new[] { 0, 1, 2 },
            };
        }
        public static void Main()
        {
            var points = new List<Vector3[]>
            {
                new[] { new Vector3(0, 0, 2), new Vector3(0, 0, 100) },
                new[] { new Vector3(0, -1, 1.5f), new Vector3(0, -2, 1.95f) },
                new[] { new Vector3(0, 0, -2), new Vector3(0, -1, -1.5f), new Vector3(1, 0, -0.05f) },
            };
            var used = new List<int[]> { new[] { 0 }, new[] { 0, 1 }, new[] { 0, 1, 2 } };
            var openness = new List<float[]> { new[] { 0.6f, 0f }, new[] { 0.05f, 0.3f }, new[] { 0.6f, 0.05f, 0.05f } };
            var layers = Classify(points, used, openness, 0, 0.08f, 0.5f);
            Check(Near(layers[0][0], 0) && Near(layers[0][1], 0), "Visible/unused vertices must be black");
            Check(Near(layers[1][0], 1) && Near(layers[1][1], 0), "Hidden vertices must be white, moderately open ones excluded by default");
            var wide = Classify(points, used, openness, 0, 0.4f, 0.5f);
            Check(wide[1][1] > layers[1][1] && Near(wide[1][1], 1), "Larger range must expand the colored region");
            Check(Near(wide[0][0], 0), "An exposed surface must stay uncolored even at maximum range");
            var rear = Classify(points, used, openness, 2, 0.08f, 0.5f);
            Check(Near(rear[1][0], 0), "Rear mode must exclude front hidden vertices");
            Check(Near(rear[2][0], 0) && Near(rear[2][1], 1), "Rear mode must keep hidden rear vertices only");
            Check(rear[2][2] > 0 && rear[2][2] < layers[2][2], "Rear weighting must feather at the side");
            points[1][0] = new Vector3(3, -10, 2);
            Check(Near(Classify(points, used, openness, 0, 0.08f, 0.5f)[1][0], layers[1][0]),
                "Long diagonal hair must depend on openness, not distance from the head");
            Check(Near(InnerColorMask.FromOpenness(0.08f, 0.01f), 0.5f), "Minimum range must center its feather at 0.08 openness");
            Check(Near(InnerColorMask.FromOpenness(0.4f, 0.4f), 0.5f), "Maximum range must center its feather at 0.4 openness");
            for (var step = 0; step < 100; step++)
            {
                var o = step / 100f;
                var value = InnerColorMask.FromOpenness(o, 0.08f);
                Check(value >= 0 && value <= 1 && value >= InnerColorMask.FromOpenness(o + 0.01f, 0.08f), "Increasing openness must reduce bounded color values");
                Check(value <= InnerColorMask.FromOpenness(o, 0.2f), "Increasing range must never shrink color");
            }
            Check(InnerColorMask.FromOpenness(0.13f, 0.08f) > InnerColorMask.FromOpenness(0.14f, 0.08f) &&
                InnerColorMask.FromOpenness(0.14f, 0.08f) > 0, "The openness boundary must be smooth");

            // mode 1 はレイのデータを渡さず従来の高さ計算だけで動く。
            points = new List<Vector3[]> { new[] { new Vector3(0, -2, 1), new Vector3(0, -0.5f, 1), new Vector3(0, 0.1f, 1) } };
            used = new List<int[]> { new[] { 0, 1, 2 } };
            var low = Classify(points, used, null, 1, 0.08f, 0.5f)[0];
            var high = Classify(points, used, null, 1, 0.08f, 1)[0];
            Check(Near(low[0], 1) && Near(low[1], 0) && Near(low[2], 0), "Lower mode must color only tips below cutoff");
            Check(Near(high[1], 1) && Near(high[2], 0), "Height must expand only below head center");

            var geometry = new List<InnerColorOpenness.Geometry> { Geometry(1), Geometry(2) };
            geometry[1].used = new int[0]; // 選んでいない髪・体もキーに含める。
            var key = InnerColorOpenness.CacheKey(10, geometry);
            Check(key == InnerColorOpenness.CacheKey(10, geometry), "Equal inputs must give equal cache keys");
            Check(key != InnerColorOpenness.CacheKey(11, geometry), "Changing avatar must invalidate cache");
            geometry[0].rendererId++;
            Check(key != InnerColorOpenness.CacheKey(10, geometry), "Changing selected renderer must invalidate cache");
            geometry[0].rendererId--;
            geometry[0].used = new[] { 0 };
            Check(key != InnerColorOpenness.CacheKey(10, geometry), "Changing selected vertices must invalidate cache");
            geometry[0].used = new[] { 0, 1, 2 };
            geometry[1].positions[0] = new Vector3(0.00001f, 0, 0);
            Check(key != InnerColorOpenness.CacheKey(10, geometry), "Body/unselected hair pose or BlendShape changes must invalidate cache even below sample rounding");
            geometry[1].positions[0] = new Vector3(0, 0, 0);
            geometry[0].normals[0] = new Vector3(1, 0, 0);
            Check(key != InnerColorOpenness.CacheKey(10, geometry), "Changing normals must invalidate cache");
            geometry[0].normals[0] = new Vector3(0, 0, 1);
            geometry[1].triangles[0] = new[] { 0, 2, 1 };
            Check(key != InnerColorOpenness.CacheKey(10, geometry), "Changing occluder topology must invalidate cache");
            geometry[1].triangles[0] = new[] { 0, 1, 2 };
            geometry[0].uv[0] = new Vector2(0.5f, 0.5f);
            Check(key == InnerColorOpenness.CacheKey(10, geometry), "UV-only edits must reuse openness");
            geometry.RemoveAt(1);
            var changedKey = InnerColorOpenness.CacheKey(10, geometry);
            Check(key != changedKey, "Adding/removing body or unselected hair must invalidate cache");
            var cache = new InnerColorOpenness.Cache();
            var calls = 0;
            Func<List<float[]>> measure = () => { calls++; return new List<float[]> { new[] { 0.1f } }; };
            var first = cache.Get(key, measure);
            Check(ReferenceEquals(first, cache.Get(key, measure)) && calls == 1, "Repeated slider/color/mode/quality edits must reuse the measured array");
            cache.Get(changedKey, measure);
            Check(calls == 2, "Changed key must measure again");
            var failed = false;
            try { cache.Get(key, () => { throw new InvalidOperationException(); }); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && ReferenceEquals(cache.Get(changedKey, measure), cache.Get(changedKey, measure)) && calls == 2,
                "Failed measurements must not replace a complete cache entry");
            cache.Clear(); cache.Get(changedKey, measure);
            Check(calls == 3, "Clearing cache must release old measurement");

            var normal = new Vector3(0, 0, 1);
            var sample = InnerColorOpenness.SampleKey(new Vector3(0, 0, 0), normal);
            Check(sample == InnerColorOpenness.SampleKey(new Vector3(0.0005f, 0, 0), normal), "Nearby vertices must share a sample");
            Check(sample != InnerColorOpenness.SampleKey(new Vector3(0.003f, 0, 0), normal), "Distant vertices must not share a sample");
            Check(sample != InnerColorOpenness.SampleKey(new Vector3(0, 0, 0), new Vector3(1, 0, 0)), "Different surface directions must keep distinct samples");
            Check(sample == InnerColorOpenness.SampleKey(new Vector3(0, 0, 0), new Vector3(0, 0, -1)), "Reversed normals must share a two-sided sample");
            var directions = InnerColorOpenness.Directions();
            Check(directions.Length == 64, "Use 64 fixed directions");
            var sumX = 0f; var sumY = 0f; var sumZ = 0f;
            for (var i = 0; i < directions.Length; i++)
            {
                var d = directions[i]; var repeated = InnerColorOpenness.Directions()[i];
                Check(Near(d.x * d.x + d.y * d.y + d.z * d.z, 1), "Ray directions must be unit length");
                Check(Near(d.x, repeated.x) && Near(d.y, repeated.y) && Near(d.z, repeated.z), "Ray sequence must be deterministic");
                sumX += d.x; sumY += d.y; sumZ += d.z;
            }
            Check(Math.Abs(sumX) < 0.1f && Math.Abs(sumY) < 0.1f && Math.Abs(sumZ) < 0.1f, "Directions must cover the sphere evenly");
            foreach (var name in new[] { "Hair_ACC", "HairRibbon", "hair_pin", "HAIR_CLIP", "HairEarring", "髪飾り", "髪リボン", "髪ピン", "Cloth_Hair" })
                Check(InnerColorFeature.DefaultExcluded(name), "Accessories must be initially unchecked: " + name);
            foreach (var name in new[] { "Hair", "HairBack", "LongHair", "前髪", "後ろ髪" })
                Check(!InnerColorFeature.DefaultExcluded(name), "Ordinary hair must be initially selected: " + name);

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
