using System;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 髪の画像の色を、見本の髪の色に合わせる計算。
    /// 画像を明るさの順に並べて 7 つの帯に分け、帯ごとの平均色（色点）を取る。
    /// 変える側の画素は、明るさが変える側のどの帯に当たるかを調べ、見本の同じ帯の色に置き換える。
    /// 明るさの順番はそのままなので、毛の流れや影の濃淡は残り、色味だけが見本に寄る。
    /// Unity の機能を使わない計算だけにして、Unity なしでも数値を確かめられるようにしている。
    /// </summary>
    internal static class HairColorMatch
    {
        public const int PointCount = 7;
        public const int LutSize = 1024;

        /// <summary>色を調べるのに必要な最低の画素数。</summary>
        public const int MinPixels = 16;

        // 各色点が受け持つ、明るさの順位の範囲（暗い方から）。端は細く取り、いちばん暗い所・明るい所の色を拾う。
        private static readonly float[] Edges = { 0f, 0.04f, 0.15f, 0.35f, 0.65f, 0.85f, 0.96f, 1f };

        // 明るさの幅がこれより狭い画像は、単色として扱う
        private const float FlatRange = 0.02f;

        /// <summary>明るさの帯 1 つぶんの代表色。</summary>
        public struct Point
        {
            public float Luminance;
            public float R, G, B;

            public Point(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
                Luminance = HairColorMatch.Luminance(r, g, b);
            }

            public Color ToColor()
            {
                return new Color(R, G, B, 1f);
            }
        }

        public static float Luminance(float r, float g, float b)
        {
            return 0.299f * r + 0.587f * g + 0.114f * b;
        }

        /// <summary>
        /// 画像から色点を取る。mask があれば true の画素だけ、透明な画素は常に除く。
        /// 調べられる画素が少なすぎるときは null。
        /// </summary>
        public static Point[] Analyze(Color[] pixels, bool[] mask)
        {
            if (pixels == null) return null;
            if (mask != null && mask.Length != pixels.Length) throw new ArgumentException("mask");

            var count = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (IsValid(pixels, mask, i)) count++;
            }

            if (count < MinPixels) return null;

            var keys = new float[count];
            var items = new int[count];
            var n = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (!IsValid(pixels, mask, i)) continue;
                keys[n] = Luminance(pixels[i].r, pixels[i].g, pixels[i].b);
                items[n] = i;
                n++;
            }

            Array.Sort(keys, items);

            var points = new Point[PointCount];
            for (var k = 0; k < PointCount; k++)
            {
                var from = Math.Min(count - 1, (int)(Edges[k] * count));
                var to = Math.Min(count, Math.Max(from + 1, (int)(Edges[k + 1] * count)));
                double r = 0, g = 0, b = 0;
                for (var i = from; i < to; i++)
                {
                    var c = pixels[items[i]];
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }

                var size = to - from;
                points[k] = new Point((float)(r / size), (float)(g / size), (float)(b / size));
            }

            return points;
        }

        private static bool IsValid(Color[] pixels, bool[] mask, int i)
        {
            return pixels[i].a >= 0.5f && (mask == null || mask[i]);
        }

        /// <summary>
        /// 明るさ（0〜1）から見本の色を引く表を作る。source は変える側、target は見本の色点。
        /// </summary>
        public static Color[] BuildLut(Point[] source, Point[] target)
        {
            if (source == null || source.Length != PointCount) throw new ArgumentException("source");
            if (target == null || target.Length != PointCount) throw new ArgumentException("target");

            var last = PointCount - 1;
            var middle = PointCount / 2;
            var sourceFlat = source[last].Luminance - source[0].Luminance < FlatRange;

            // 見本が単色のとき、そのまま当てると濃淡が消える。変える側の濃淡の比で見本の色を明るく・暗くして補う。
            if (target[last].Luminance - target[0].Luminance < FlatRange && !sourceFlat)
            {
                var flat = target[middle];
                var baseLuminance = Math.Max(source[middle].Luminance, 0.001f);
                var spread = new Point[PointCount];
                for (var k = 0; k < PointCount; k++)
                {
                    spread[k] = Scale(flat, source[k].Luminance / baseLuminance);
                }

                target = spread;
            }

            var lut = new Color[LutSize];
            for (var i = 0; i < LutSize; i++)
            {
                var l = i / (float)(LutSize - 1);
                Point p;
                if (sourceFlat)
                {
                    // 変える側が単色なら、見本のまん中の色を、明るさの比で当てる
                    p = Scale(target[middle], l / Math.Max(source[middle].Luminance, 0.001f));
                }
                else if (l <= source[0].Luminance)
                {
                    // いちばん暗い色点より暗い所は、そのまま黒に向かって暗くする
                    p = Scale(target[0], l / Math.Max(source[0].Luminance, 0.001f));
                }
                else if (l >= source[last].Luminance)
                {
                    // いちばん明るい色点より明るい所は、最後の区間の変化をそのまま延ばす（行き過ぎないよう 2 倍まで）
                    var span = source[last].Luminance - source[last - 1].Luminance;
                    var t = span > 0.0001f ? Math.Min(2f, (l - source[last].Luminance) / span) : 0f;
                    p = Lerp(target[last - 1], target[last], 1f + t);
                }
                else
                {
                    var k = 0;
                    while (k < last - 1 && l > source[k + 1].Luminance) k++;
                    var span = source[k + 1].Luminance - source[k].Luminance;
                    var t = span > 0.0001f ? (l - source[k].Luminance) / span : 0.5f;
                    p = Lerp(target[k], target[k + 1], t);
                }

                lut[i] = new Color(Clamp01(p.R), Clamp01(p.G), Clamp01(p.B), 1f);
            }

            return lut;
        }

        /// <summary>画素 1 つを表で置き換える。strength（0〜1）で元の色と混ぜる。透明度は変えない。</summary>
        public static Color Apply(Color c, Color[] lut, float strength)
        {
            var l = Clamp01(Luminance(c.r, c.g, c.b));
            var mapped = lut[(int)(l * (lut.Length - 1) + 0.5f)];
            strength = Clamp01(strength);
            return new Color(
                c.r + (mapped.r - c.r) * strength,
                c.g + (mapped.g - c.g) * strength,
                c.b + (mapped.b - c.b) * strength,
                c.a);
        }

        /// <summary>
        /// メッシュが実際に使っている画像の範囲（UV の三角形の中）に印を付ける。
        /// 髪の画像の余白や、同じ画像に入っている別パーツの色を拾わないため。mask は幅×高さで、下の行から並ぶ。
        /// </summary>
        public static void MarkUv(Vector2[] uv, int[] triangles, int width, int height, bool[] mask)
        {
            if (uv == null || triangles == null || mask == null || width <= 0 || height <= 0) return;
            if (mask.Length != width * height) throw new ArgumentException("mask");

            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= uv.Length || b >= uv.Length || c >= uv.Length) continue;

                // 0〜1 の外にある UV（くり返し）は、三角形ごとに 0〜1 のタイルへ戻す
                var shiftX = (float)Math.Floor((uv[a].x + uv[b].x + uv[c].x) / 3f);
                var shiftY = (float)Math.Floor((uv[a].y + uv[b].y + uv[c].y) / 3f);
                var ax = (uv[a].x - shiftX) * width;
                var ay = (uv[a].y - shiftY) * height;
                var bx = (uv[b].x - shiftX) * width;
                var by = (uv[b].y - shiftY) * height;
                var cx = (uv[c].x - shiftX) * width;
                var cy = (uv[c].y - shiftY) * height;

                // 1 画素より小さい三角形も拾えるよう、重心の画素には必ず印を付ける
                Mark(mask, width, height, (int)Math.Floor((ax + bx + cx) / 3f), (int)Math.Floor((ay + by + cy) / 3f));

                var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Math.Abs(area) < 1e-12f) continue;

                var minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                var maxX = Math.Min(width - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx))));
                var minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
                var maxY = Math.Min(height - 1, (int)Math.Floor(Math.Max(ay, Math.Max(by, cy))));
                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        var w0 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) / area;
                        var w1 = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) / area;
                        var w2 = ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) / area;
                        if (w0 >= 0f && w1 >= 0f && w2 >= 0f) mask[y * width + x] = true;
                    }
                }
            }
        }

        private static void Mark(bool[] mask, int width, int height, int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            mask[y * width + x] = true;
        }

        /// <summary>
        /// 見本のマテリアルの設定（lilToon の色調補正と色）を色点に掛けて、画面で見えている色に近づける。
        /// hsvg は lilToon の _MainTexHSVG（色相のずれ・彩度の倍率・明度の倍率・ガンマ）。
        /// </summary>
        public static Point[] ApplyLook(Point[] points, Color tint, Vector4 hsvg, bool useHsvg)
        {
            if (points == null) return null;
            var result = new Point[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var c = Look(points[i].ToColor(), tint, hsvg, useHsvg);
                result[i] = new Point(c.r, c.g, c.b);
            }

            return result;
        }

        /// <summary>画素 1 つに、lilToon のメインの色の決め方（色調補正 → 色を掛ける）を当てる。透明度は色の a を掛ける。</summary>
        public static Color Look(Color c, Color tint, Vector4 hsvg, bool useHsvg)
        {
            float r = c.r, g = c.g, b = c.b;
            if (useHsvg)
            {
                var gamma = hsvg.w <= 0f ? 1f : hsvg.w;
                r = (float)Math.Pow(Math.Max(r, 0f), gamma);
                g = (float)Math.Pow(Math.Max(g, 0f), gamma);
                b = (float)Math.Pow(Math.Max(b, 0f), gamma);
                float h, s, v;
                RgbToHsv(r, g, b, out h, out s, out v);
                h += hsvg.x;
                h -= (float)Math.Floor(h);
                s = Clamp01(s * hsvg.y);
                v = Clamp01(v * hsvg.z);
                HsvToRgb(h, s, v, out r, out g, out b);
            }

            return new Color(Clamp01(r * tint.r), Clamp01(g * tint.g), Clamp01(b * tint.b), c.a * tint.a);
        }

        /// <summary>
        /// lilToon の 2nd / 3rd の層を、メインの色の上に重ねる（見本の「見えている色」を作るため）。
        /// layer は層の画像（null なら白）、mask は重ねる所のマスク（null なら全体、赤の値を使う）。
        /// blendMode は lilToon の _Main2ndTexBlendMode（0 通常・1 加算・2 スクリーン・3 乗算）。
        /// linear が true なら、色をリニアに直してから混ぜる（リニアのプロジェクトでのシェーダーの計算に合わせる）。
        /// </summary>
        public static void Composite(Color[] colors, Color[] layer, float[] mask, Color tint, int blendMode, bool linear)
        {
            if (colors == null) return;
            if (layer != null && layer.Length != colors.Length) throw new ArgumentException("layer");
            if (mask != null && mask.Length != colors.Length) throw new ArgumentException("mask");
            for (var i = 0; i < colors.Length; i++)
            {
                var src = layer == null ? new Color(1f, 1f, 1f, 1f) : layer[i];
                var a = Clamp01(src.a * tint.a * (mask == null ? 1f : mask[i]));
                if (a <= 0f) continue;
                var dst = colors[i];
                float dr = dst.r, dg = dst.g, db = dst.b;
                float sr = src.r * tint.r, sg = src.g * tint.g, sb = src.b * tint.b;
                if (linear)
                {
                    dr = ToLinear(dr); dg = ToLinear(dg); db = ToLinear(db);
                    sr = ToLinear(sr); sg = ToLinear(sg); sb = ToLinear(sb);
                }

                dr = Blend(dr, sr, a, blendMode);
                dg = Blend(dg, sg, a, blendMode);
                db = Blend(db, sb, a, blendMode);
                if (linear)
                {
                    dr = ToGamma(dr); dg = ToGamma(dg); db = ToGamma(db);
                }

                colors[i] = new Color(Clamp01(dr), Clamp01(dg), Clamp01(db), dst.a);
            }
        }

        private static float Blend(float dst, float src, float a, int mode)
        {
            switch (mode)
            {
                case 1: return dst + src * a;
                case 2: return dst + (1f - (1f - dst) * (1f - src) - dst) * a;
                case 3: return dst + (dst * src - dst) * a;
                default: return dst + (src - dst) * a;
            }
        }

        public static float ToLinear(float v)
        {
            v = Clamp01(v);
            return v <= 0.04045f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        public static float ToGamma(float v)
        {
            v = Clamp01(v);
            return v <= 0.0031308f ? v * 12.92f : 1.055f * (float)Math.Pow(v, 1f / 2.4f) - 0.055f;
        }

        public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            v = max;
            s = max <= 0f ? 0f : delta / max;
            if (delta <= 0f)
            {
                h = 0f;
                return;
            }

            if (max == r) h = (g - b) / delta;
            else if (max == g) h = 2f + (b - r) / delta;
            else h = 4f + (r - g) / delta;
            h /= 6f;
            h -= (float)Math.Floor(h);
        }

        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            h = (h - (float)Math.Floor(h)) * 6f;
            var i = (int)Math.Floor(h) % 6;
            var f = h - (float)Math.Floor(h);
            var p = v * (1f - s);
            var q = v * (1f - s * f);
            var t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        private static Point Scale(Point p, float factor)
        {
            return new Point(p.R * factor, p.G * factor, p.B * factor);
        }

        private static Point Lerp(Point a, Point b, float t)
        {
            return new Point(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
