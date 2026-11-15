// 髪の色合わせ（HairColorMatch）の計算を、数値で確かめる。画像の読み書きとマテリアルの差し替えは Unity が必要。
using System;
using System.Linq;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    internal static class HairColorMatchTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private static bool Near(float a, float b, float tolerance = 0.02f) { return Math.Abs(a - b) < tolerance; }
        private static bool Near(Color a, Color b, float tolerance = 0.02f)
        {
            return Near(a.r, b.r, tolerance) && Near(a.g, b.g, tolerance) && Near(a.b, b.b, tolerance);
        }

        /// <summary>暗い色から明るい色へのグラデーション（髪の画像の代わり）。</summary>
        private static Color[] Gradient(Color dark, Color bright, int count)
        {
            var pixels = new Color[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)(count - 1);
                pixels[i] = new Color(dark.r + (bright.r - dark.r) * t, dark.g + (bright.g - dark.g) * t, dark.b + (bright.b - dark.b) * t, 1f);
            }
            return pixels;
        }
        private static void Throws(Action action, string message)
        {
            var threw = false;
            try { action(); } catch (ArgumentException) { threw = true; }
            Check(threw, message);
        }
        private static float Lum(Color c) { return HairColorMatch.Luminance(c.r, c.g, c.b); }

        public static void Main()
        {
            var pink = Gradient(new Color(0.55f, 0.30f, 0.40f), new Color(1f, 0.90f, 0.95f), 4000);
            var navy = Gradient(new Color(0.02f, 0.03f, 0.10f), new Color(0.30f, 0.35f, 0.60f), 3000);

            // ---- 色点
            var source = HairColorMatch.Analyze(pink, null);
            var target = HairColorMatch.Analyze(navy, null);
            Check(source.Length == 7 && target.Length == 7, "Analyze must return 7 color points");
            for (var k = 1; k < 7; k++)
                Check(source[k].Luminance >= source[k - 1].Luminance, "Color points must be ordered dark to bright");
            Check(Near(target[0].ToColor(), navy[60], 0.01f) && Near(target[6].ToColor(), navy[2940], 0.01f),
                "End points must be the mean of the darkest / brightest 4%");
            Check(HairColorMatch.Analyze(new Color[8], null) == null, "Too few pixels must return null");
            Check(HairColorMatch.Analyze(Enumerable.Repeat(new Color(1, 0, 0, 0), 100).ToArray(), null) == null, "Transparent pixels must be ignored");

            // 透明な所と mask の外は色点に入らない
            var mixed = navy.Concat(Enumerable.Repeat(new Color(1f, 1f, 1f, 1f), 3000)).Concat(Enumerable.Repeat(new Color(0f, 1f, 0f, 0f), 500)).ToArray();
            var mask = Enumerable.Range(0, mixed.Length).Select(i => i < 3000).ToArray();
            var masked = HairColorMatch.Analyze(mixed, mask);
            for (var k = 0; k < 7; k++) Check(Near(masked[k].ToColor(), target[k].ToColor(), 0.001f), "Masked-out background must not change the color points");
            var unmasked = HairColorMatch.Analyze(mixed, null);
            Check(unmasked[6].Luminance > 0.99f, "Without a mask the white background is picked up (this is why the UV mask exists)");

            // ---- 色を合わせる
            var lut = HairColorMatch.BuildLut(source, target);
            var result = pink.Select(c => HairColorMatch.Apply(c, lut, 1f)).ToArray();
            var matched = HairColorMatch.Analyze(result, null);
            for (var k = 0; k < 7; k++)
                Check(Near(matched[k].ToColor(), target[k].ToColor()), "Matched image must have the sample's color points (point " + k + ")");
            for (var i = 1; i < result.Length; i++)
                Check(Lum(result[i]) >= Lum(result[i - 1]) - 0.002f, "Brightness order must be kept so strand shading survives");
            Check(result.All(c => c.r >= 0 && c.r <= 1 && c.g >= 0 && c.g <= 1 && c.b >= 0 && c.b <= 1), "Result must stay within 0..1");
            Check(result[2000].b > result[2000].r, "Pink must become bluish like the sample");

            // 強さと透明度
            var half = new Color(0.8f, 0.6f, 0.7f, 0.25f);
            var none = HairColorMatch.Apply(half, lut, 0f);
            Check(Near(none, half, 0.0001f) && none.a == 0.25f, "Strength 0 must leave the pixel unchanged");
            var full = HairColorMatch.Apply(half, lut, 1f);
            var mid = HairColorMatch.Apply(half, lut, 0.5f);
            Check(Near(mid.r, (half.r + full.r) / 2, 0.001f) && Near(mid.b, (half.b + full.b) / 2, 0.001f), "Strength must blend linearly");
            Check(full.a == 0.25f && mid.a == 0.25f, "Alpha must never change");

            // 見本より暗い・明るい画素（色点の外）でも破綻しない
            var black = HairColorMatch.Apply(new Color(0, 0, 0, 1), lut, 1f);
            Check(Near(black, new Color(0, 0, 0), 0.001f), "Black must stay black");
            var white = HairColorMatch.Apply(new Color(1, 1, 1, 1), lut, 1f);
            Check(Lum(white) >= target[6].Luminance - 0.001f, "Brighter-than-range pixels must not get darker than the brightest point");

            // 同じ画像どうしなら、ほぼ変わらない
            var self = HairColorMatch.BuildLut(source, source);
            Check(pink.All(c => Near(HairColorMatch.Apply(c, self, 1f), c, 0.03f)), "Matching an image to itself must be close to a no-op");

            // 単色の見本でも濃淡が残る
            var flatTarget = HairColorMatch.Analyze(Enumerable.Repeat(new Color(0.2f, 0.4f, 0.8f, 1f), 500).ToArray(), null);
            var flatLut = HairColorMatch.BuildLut(source, flatTarget);
            var darkOut = HairColorMatch.Apply(pink[200], flatLut, 1f);
            var brightOut = HairColorMatch.Apply(pink[3800], flatLut, 1f);
            Check(Lum(brightOut) > Lum(darkOut) + 0.1f, "A flat sample must not erase shading");
            Check(Near(HairColorMatch.Apply(pink[2000], flatLut, 1f), new Color(0.2f, 0.4f, 0.8f), 0.03f), "A flat sample must give its color at mid brightness");

            // 単色の変える側でも、見本のまん中の色になる
            var flatSource = HairColorMatch.Analyze(Enumerable.Repeat(new Color(0.5f, 0.5f, 0.5f, 1f), 500).ToArray(), null);
            var fromFlat = HairColorMatch.Apply(new Color(0.5f, 0.5f, 0.5f, 1f), HairColorMatch.BuildLut(flatSource, target), 1f);
            Check(Near(fromFlat, target[3].ToColor(), 0.01f), "A flat source must take the sample's middle color");

            // ---- UV の範囲
            var uvMask = new bool[16 * 16];
            HairColorMatch.MarkUv(new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) }, new[] { 0, 1, 2 }, 16, 16, uvMask);
            Check(uvMask[0] && uvMask[1 * 16 + 1] && uvMask[2 * 16 + 4], "Pixels inside the UV triangle must be marked");
            Check(!uvMask[15 * 16 + 15] && !uvMask[7 * 16 + 7] && !uvMask[12], "Pixels outside the UV triangle must stay unmarked");
            Check(uvMask.Count(m => m) > 20 && uvMask.Count(m => m) < 45, "About 1/8 of the image must be marked");
            var reversed = new bool[16 * 16];
            HairColorMatch.MarkUv(new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) }, new[] { 0, 2, 1 }, 16, 16, reversed);
            Check(reversed.SequenceEqual(uvMask), "Triangle winding must not matter");
            var tiled = new bool[16 * 16];
            HairColorMatch.MarkUv(new[] { new Vector2(3f, -2f), new Vector2(3.5f, -2f), new Vector2(3f, -1.5f) }, new[] { 0, 1, 2 }, 16, 16, tiled);
            Check(tiled.SequenceEqual(uvMask), "UVs outside 0..1 must wrap back into the image");
            var tiny = new bool[16 * 16];
            HairColorMatch.MarkUv(new[] { new Vector2(0.51f, 0.51f), new Vector2(0.511f, 0.51f), new Vector2(0.51f, 0.511f) }, new[] { 0, 1, 2 }, 16, 16, tiny);
            Check(tiny[8 * 16 + 8] && tiny.Count(m => m) == 1, "A sub-pixel triangle must still mark its pixel");
            HairColorMatch.MarkUv(new[] { new Vector2(0, 0) }, new[] { 0, 5, 9 }, 16, 16, tiny);
            Check(tiny.Count(m => m) == 1, "Invalid indices must be skipped");

            // ---- 見本の見た目（lilToon の色調補正と色）
            var look = HairColorMatch.ApplyLook(target, new Color(1, 1, 1, 1), new Vector4(0f, 1f, 1f, 1f), true);
            for (var k = 0; k < 7; k++) Check(Near(look[k].ToColor(), target[k].ToColor(), 0.001f), "Neutral tone correction must not change the points");
            var red = new[] { new HairColorMatch.Point(0.8f, 0.2f, 0.2f) };
            var shifted = HairColorMatch.ApplyLook(red, new Color(1, 1, 1, 1), new Vector4(1f / 3f, 1f, 1f, 1f), true)[0];
            Check(Near(shifted.ToColor(), new Color(0.2f, 0.8f, 0.2f), 0.001f), "Hue +120 degrees must turn red into green");
            var grey = HairColorMatch.ApplyLook(red, new Color(1, 1, 1, 1), new Vector4(0f, 0f, 0.5f, 1f), true)[0];
            Check(Near(grey.ToColor(), new Color(0.4f, 0.4f, 0.4f), 0.001f), "Saturation 0 and value 0.5 must give half-bright grey");
            var tinted = HairColorMatch.ApplyLook(red, new Color(0.5f, 1f, 0f, 1f), new Vector4(0f, 1f, 1f, 1f), false)[0];
            Check(Near(tinted.ToColor(), new Color(0.4f, 0.2f, 0f), 0.001f), "Material color must multiply the points");
            float h, s, v, r, g, b;
            foreach (var c in new[] { new Color(0.1f, 0.5f, 0.9f), new Color(0.9f, 0.5f, 0.1f), new Color(0.3f, 0.3f, 0.3f), new Color(0.2f, 0.9f, 0.4f), new Color(0.7f, 0.1f, 0.6f) })
            {
                HairColorMatch.RgbToHsv(c.r, c.g, c.b, out h, out s, out v);
                HairColorMatch.HsvToRgb(h, s, v, out r, out g, out b);
                Check(Near(new Color(r, g, b), c, 0.0001f), "RGB -> HSV -> RGB must round-trip");
            }

            // ---- 画素ごとの見た目（ApplyLook と同じ計算）
            var px = new Color(0.8f, 0.2f, 0.2f, 0.5f);
            var lookPx = HairColorMatch.Look(px, new Color(1f, 1f, 1f, 0.5f), new Vector4(1f / 3f, 1f, 1f, 1f), true);
            Check(Near(lookPx, new Color(0.2f, 0.8f, 0.2f), 0.001f) && Near(lookPx.a, 0.25f, 0.0001f), "Per-pixel look must match point look and multiply alpha");

            // ---- 層を重ねる
            var baseColors = new[] { new Color(0.2f, 0.4f, 0.6f, 1f), new Color(0.2f, 0.4f, 0.6f, 0.3f) };
            var layerColors = new[] { new Color(1f, 0.5f, 0f, 1f), new Color(1f, 0.5f, 0f, 1f) };
            Func<float[], Color, int, bool, Color[]> comp = (m, tint, mode, lin) =>
            {
                var copy = baseColors.ToArray();
                HairColorMatch.Composite(copy, layerColors, m, tint, mode, lin);
                return copy;
            };
            var opaque = new Color(1f, 1f, 1f, 1f);
            Check(comp(new[] { 0f, 0f }, opaque, 0, false).SequenceEqual(baseColors), "Mask 0 must leave colors unchanged");
            var normal = comp(null, opaque, 0, false);
            Check(Near(normal[0], layerColors[0], 0.0001f) && Near(normal[1].a, 0.3f, 0.0001f), "Normal blend at full mask replaces color and keeps base alpha");
            var halfMask = comp(new[] { 0.5f, 0.5f }, opaque, 0, false);
            Check(Near(halfMask[0], new Color(0.6f, 0.45f, 0.3f), 0.0001f), "Normal blend must lerp by mask");
            var tintedLayer = comp(null, new Color(1f, 0.5f, 1f, 0.5f), 0, false);
            Check(Near(tintedLayer[0], new Color(0.6f, 0.325f, 0.3f), 0.0001f), "Layer color must tint the layer and its alpha must scale the blend");
            Check(Near(comp(null, opaque, 1, false)[0], new Color(1f, 0.9f, 0.6f), 0.0001f), "Add blend");
            Check(Near(comp(null, opaque, 2, false)[0], new Color(1f, 0.7f, 0.6f), 0.0001f), "Screen blend");
            Check(Near(comp(null, opaque, 3, false)[0], new Color(0.2f, 0.2f, 0f), 0.0001f), "Multiply blend");
            var noLayer = baseColors.ToArray();
            HairColorMatch.Composite(noLayer, null, null, new Color(1f, 0f, 0f, 1f), 0, false);
            Check(Near(noLayer[0], new Color(1f, 0f, 0f), 0.0001f), "A layer without an image must use the plain layer color");
            var linMix = comp(new[] { 0.5f, 0.5f }, opaque, 0, true)[0];
            Check(linMix.r > 0.6f && linMix.r < 0.75f, "Linear blending must mix in linear space (brighter midpoint than gamma)");
            Check(Near(comp(null, opaque, 0, true)[0], layerColors[0], 0.001f), "Linear round-trip must be exact at full mask");
            for (var x = 0f; x <= 1f; x += 0.05f) Check(Near(HairColorMatch.ToGamma(HairColorMatch.ToLinear(x)), x, 0.0001f), "sRGB conversion must round-trip");
            Throws(() => HairColorMatch.Composite(new Color[2], new Color[3], null, opaque, 0, false), "Reject mismatched layer size");

            Console.WriteLine("Hair color match tests passed (" + checks + " checks).");
        }
    }
}
