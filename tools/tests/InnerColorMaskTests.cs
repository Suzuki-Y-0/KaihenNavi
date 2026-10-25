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
        private static void CheckVisibility()
        {
            const int size = 8;
            var votes = new Color[size * size]; var scope = new Color[votes.Length];
            // 同じ三角形の中でも、テクセルごとの見え方で色が分かれること。
            votes[0] = new Color(64, 20, 64, 20); // 外側。
            votes[1] = new Color(0, 0, 64, 20); // どちらからも隠れている。
            votes[2] = new Color(32, 0, 64, 20); // 全体では見えるが、後ろからは隠れる。
            votes[3] = new Color(0, 0, 64, 20); // 隠れているが頭より上・前。
            scope[0] = scope[1] = scope[2] = new Color(0.8f, 1, 1, 1);
            scope[3] = new Color(0, 0, 1, 1);
            var map = new InnerColorVisibility.Map { votes = votes, scope = scope };
            var inner = map.Mask(0, 0.08f, 0.5f, size);
            Check(Near(inner[0], 0) && Near(inner[1], 1), "Neighboring visible and hidden texels must stay distinct");
            var rear = map.Mask(2, 0.08f, 0.5f, size);
            Check(Near(inner[2], 0) && Near(rear[2], 1), "Rear mode must use rear camera votes rather than global visibility");
            Check(Near(rear[3], 0), "Rear mode must exclude front texels even when hidden");
            var lower = map.Mask(1, 0.08f, 0.5f, size);
            Check(Near(lower[0], 0) && Near(lower[1], 1) && Near(lower[3], 0), "Lower mode must combine height with visibility");
            Check(Near(map.Mask(1, 0.08f, 0, size)[1], 0), "Minimum height must not color above the tip");
            Check(Near(InnerColorMask.LowerWeight(0, 1), 0) && Near(InnerColorMask.LowerWeight(0.5f, 1), 1),
                "Height must be tested after position interpolation and stop at the head center");
            Check(votes[0].r == 64 && scope[1].r == 0.8f, "Adjustments must not modify cached measurements");

            var pixels = new float[size * size]; var coverage = new bool[pixels.Length];
            pixels[0] = 1; coverage[0] = true; coverage[1] = true; // 隣の島の外側は黒のまま。
            var padded = InnerColorMask.PadIslands(pixels, coverage, size, 1);
            Check(Near(padded[1], 0) && Near(padded[0], 1), "Padding must never replace covered outer texels");
            Check(padded[size - 1] > 0 && padded[(size - 1) * size] > 0, "Island padding must wrap across Repeat seams");
            Check(Near(padded[3 * size + 3], 0), "Padding radius must not grow into distant islands");
            Check(Near(pixels[size - 1], 0) && !coverage[size - 1], "Padding must not modify its inputs");
            Check(InnerColorMask.PadIslands(new float[pixels.Length], coverage, size, 3).All(v => v == 0), "Black islands must remain black");

            var directions = InnerColorVisibility.Directions();
            Check(directions.Length == 64 && directions.All(d => d.y > -0.2f && d.y < 1), "GPU directions must omit the bottom and include upper views");
            Check(directions.Count(d => d.y > 0) > 40 && directions.Any(d => d.z < -0.9f) && directions.Any(d => d.z > 0.9f),
                "GPU directions must cover both front and rear, with most samples above the horizon");
            Check(directions.All(d => Near(d.x * d.x + d.y * d.y + d.z * d.z, 1)), "GPU directions must have unit length");

            var material = new Material((Shader)null);
            material.SetTexture("_MainTex", null);
            material.SetTextureScale("_MainTex", new Vector2(1, 1)); material.SetTextureOffset("_MainTex", new Vector2(0, 0));
            material.SetColor("_Color", new Color(1, 1, 1, 1)); material.renderQueue = 2450; material.SetFloat("_Cutoff", 0.5f);
            var geometry = Geometry(1);
            var source = new InnerColorVisibility.Source { geometry = geometry, materials = new[] { material }, selected = new[] { true } };
            var sources = new List<InnerColorVisibility.Source> { source };
            Func<string> key = () => InnerColorVisibility.CacheKey(sources, new Vector3(0, 1, 0), Quaternion.identity, 512);
            var initial = key();
            Check(initial == key(), "Equal GPU inputs must reuse visibility");
            geometry.uv[0] = new Vector2(0.1f, 0);
            Check(initial != key(), "UV edits must invalidate GPU visibility"); geometry.uv[0] = new Vector2(0, 0);
            material.SetTextureScale("_MainTex", new Vector2(-1, 1));
            Check(initial != key(), "Texture tiling must invalidate GPU visibility"); material.SetTextureScale("_MainTex", new Vector2(1, 1));
            material.SetTextureOffset("_MainTex", new Vector2(0.1f, 0));
            Check(initial != key(), "Texture offset must invalidate GPU visibility"); material.SetTextureOffset("_MainTex", new Vector2(0, 0));
            material.SetFloat("_Cutoff", 0.4f);
            Check(initial != key(), "Alpha cutoff must invalidate GPU visibility"); material.SetFloat("_Cutoff", 0.5f);
            material.SetColor("_Color", new Color(1, 1, 1, 0.8f));
            Check(initial != key(), "Material alpha must invalidate GPU visibility"); material.SetColor("_Color", new Color(1, 1, 1, 1));
            material.SetColor("_Color2nd", new Color(1, 0, 0, 1));
            Check(initial == key(), "Changing the inner color must reuse GPU visibility");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            material.SetTexture("_MainTex", texture);
            var textured = key(); texture.wrapMode = TextureWrapMode.Clamp;
            Check(textured != key(), "Texture wrapping must invalidate GPU visibility"); texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            Check(textured != key(), "Texture filtering must invalidate GPU visibility");
            material.SetTexture("_MainTex", null);
            source.selected[0] = false;
            Check(initial != key(), "Selected submeshes must be part of the GPU cache key"); source.selected[0] = true;
            Check(initial != InnerColorVisibility.CacheKey(sources, new Vector3(0, 1, 0), Quaternion.identity, 1024), "Quality must invalidate GPU texel measurements");
            Check(initial != InnerColorVisibility.CacheKey(sources, new Vector3(0, 2, 0), Quaternion.identity, 512), "Head position must invalidate GPU measurements");
            var rotated = new Quaternion { x = 0, y = 1, z = 0, w = 0 };
            Check(initial != InnerColorVisibility.CacheKey(sources, new Vector3(0, 1, 0), rotated, 512), "Avatar direction must invalidate GPU measurements");
            Check(new InnerColorVisibility().Get(sources, new Vector3(0, 1, 0), Quaternion.identity, 512) == null,
                "Missing GPU shader must return control to the ray fallback");
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
            CheckVisibility();
            Console.WriteLine("InnerColor mask tests passed (" + _checks + " checks).");
            InnerColorAssetTests.Run();
        }
    }
}
