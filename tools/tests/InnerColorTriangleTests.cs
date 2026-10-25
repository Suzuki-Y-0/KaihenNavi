// 面の法線・重心・頭の中心・範囲・高さを、実際の分類コードで数値検証する。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    internal static class InnerColorTriangleTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-5f; }
        private static Vector3[] Face(Vector3 p, Vector3 n)
        {
            n = n.normalized;
            var u = Vector3.Cross(n, Math.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward).normalized * 0.03f;
            var v = Vector3.Cross(n, u);
            return new[] { p + u, p + v, p - u - v };
        }
        private static InnerColorTriangles.Split Classify(Vector3[] positions, float threshold = 0.15f, float height = 0)
        {
            return InnerColorTriangles.Classify(positions,
                new[] { Enumerable.Range(0, positions.Length).ToArray() }, new[] { true },
                Vector3.zero, Vector3.up, threshold, height);
        }
        private static void Throws(Action action, string message)
        {
            var threw = false;
            try { action(); } catch (ArgumentException) { threw = true; }
            Check(threw, message);
        }
        public static void Main()
        {
            var center = InnerColorTriangles.HeadCenter(new Vector3(2, 3, 4), Vector3.up, 1.5f);
            Check(Near(center.x, 2) && Near(center.y, 3.06f) && Near(center.z, 4), "Head center must be 4% of avatar height above Head");
            var tiltedCenter = InnerColorTriangles.HeadCenter(Vector3.zero, new Vector3(3, 0, 0), 2f);
            Check(Near(tiltedCenter.x, 0.08f), "Head center must follow normalized avatar up, not world Y");
            Check(Near(InnerColorTriangles.Threshold(56.25f), 0.15f), "Default threshold must be 0.15");
            Check(Near(InnerColorTriangles.Threshold(0), 0.6f) && Near(InnerColorTriangles.Threshold(100), -0.2f), "Slider endpoints must map to specified thresholds");
            Check(Near(InnerColorTriangles.Threshold(-50), 0.6f) && Near(InnerColorTriangles.Threshold(150), -0.2f), "Range must clamp at slider endpoints");
            var inward = Face(new Vector3(0, 0, 1), Vector3.back);
            var outward = Face(new Vector3(0, 0, 1), Vector3.forward);
            Check(Classify(inward).inner.Length == 3 && Classify(outward).inner.Length == 0, "Triangle winding must distinguish inner and outer faces sharing the same texture");
            Check(Classify(inward.Select(p => new Vector3(p.x * 0.001f, p.y * 0.001f, p.z)).ToArray()).inner.Length == 3,
                "Tiny hair triangles must not lose their normal through Unity normalized's small-vector cutoff");
            var borderline = Face(new Vector3(0, 0, 1), new Vector3((float)Math.Sqrt(0.99), 0, -0.1f));
            Check(Classify(borderline, 0.15f).inner.Length == 0 && Classify(borderline, 0.05f).inner.Length == 3,
                "Faces must cross the actual dot-product threshold as range increases");
            var two = Classify(inward.Concat(outward).ToArray());
            Check(two.inner.SequenceEqual(new[] { 0, 1, 2 }) && two.outer[0].SequenceEqual(new[] { 3, 4, 5 }), "Each triangle must appear exactly once with unchanged indices and winding");
            var side = Face(new Vector3(0, 0, 1), new Vector3(1, 0, 0));
            Check(Classify(side, 0).inner.Length == 0 && Classify(side, -0.01f).inner.Length == 3, "Threshold comparison must be strictly greater than");
            Check(Classify(Face(new Vector3(0, -10, 1), Vector3.back), 0.2f).inner.Length == 3,
                "Vertical head direction must be weakened by 70% before normalization");
            var top = Face(new Vector3(0, 0.5f, 1), Vector3.back);
            Check(Classify(top, 0.15f, 0).inner.Length == 0 && Classify(top, 0.15f, 0.1f).inner.Length == 0,
                "Faces above the head center are never inner (ahoge tips)");
            var belowTop = Face(new Vector3(0, -0.5f, 1), Vector3.back);
            Check(Classify(belowTop, 0.15f, 0).inner.Length == 3, "Inward faces below the head center stay inner");
            var tips = Face(new Vector3(0, -2, 1), Vector3.back);
            var middle = Face(new Vector3(0, -0.5f, 1), Vector3.back);
            var levels = tips.Concat(middle).Concat(top).ToArray();
            var all = Classify(levels);
            var half = Classify(levels, 0.15f, 0.5f);
            // top（頭の中心より上）は常に内側にしない
            Check(all.inner.Length == 6 && half.inner.SequenceEqual(new[] { 0, 1, 2 }), "Height must select centroids below lowest selected vertex times its coefficient");
            Check(Classify(levels, 0.15f, 1).inner.Length == 0, "Maximum height must stop below the lowest vertex");
            var selected = InnerColorTriangles.Classify(levels,
                new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 } }, new[] { false, true, false },
                Vector3.zero, Vector3.up, 0.15f, 0.5f);
            Check(selected.inner.Length == 3 && selected.outer[0].SequenceEqual(new[] { 0, 1, 2 }) && selected.outer[2].SequenceEqual(new[] { 6, 7, 8 }),
                "Unselected materials must not affect the lowest point and must keep every triangle");
            var degenerate = Classify(new[] { Vector3.one, Vector3.one, Vector3.one }, -0.2f);
            Check(degenerate.inner.Length == 0 && degenerate.outer[0].Length == 3, "Degenerate faces must remain in the original submesh");
            Check(Classify(Face(Vector3.zero, Vector3.back), -0.2f).inner.Length == 0, "A centroid at the head center must not produce an arbitrary classification");
            Check(Classify(new Vector3[0]).inner.Length == 0, "Empty submeshes must be safe");
            var baseline = Classify(levels, 0.15f, 0.5f);
            // 90 度の回転・平行移動・一様拡大をワールド座標に適用する。
            Func<Vector3, Vector3> rotate = p => new Vector3(-p.y, p.x, p.z);
            var offset = new Vector3(4, 7, -2);
            var moved = InnerColorTriangles.Classify(levels.Select(p => rotate(p) * 3 + offset).ToArray(),
                new[] { Enumerable.Range(0, levels.Length).ToArray() }, new[] { true }, offset, rotate(Vector3.up), 0.15f, 0.5f);
            Check(moved.inner.SequenceEqual(baseline.inner), "World-space classification must follow rotated, translated and scaled avatars");
            var oldPositions = levels.ToArray(); var oldIndices = Enumerable.Range(0, levels.Length).ToArray();
            var previousCount = 0;
            for (var range = 0; range <= 100; range++)
            {
                var split = Classify(levels, InnerColorTriangles.Threshold(range));
                Check(split.inner.Length >= previousCount, "Increasing range must never remove selected faces");
                previousCount = split.inner.Length;
                Check(split.inner.Concat(split.outer[0]).OrderBy(i => i).SequenceEqual(oldIndices), "Splitting must conserve the original index set");
            }
            for (var height = 1; height <= 100; height++)
            {
                Check(Classify(levels, 0.15f, height / 100f).inner.Length <= Classify(levels, 0.15f, (height - 1) / 100f).inner.Length,
                    "Increasing height must only narrow the selected area");
            }
            Check(levels.SequenceEqual(oldPositions), "Classification must never modify source vertices");
            Throws(() => InnerColorTriangles.Classify(inward, new[] { new[] { 0, 1 } }, new[] { true }, Vector3.zero, Vector3.up, 0.15f, 0), "Reject incomplete triangles");
            Throws(() => InnerColorTriangles.Classify(inward, new[] { new[] { 0, 1, 3 } }, new[] { true }, Vector3.zero, Vector3.up, 0.15f, 0), "Reject invalid indices before any asset mutation");
            Throws(() => InnerColorTriangles.Classify(inward, new[] { new[] { 0, 1, 2 } }, new bool[0], Vector3.zero, Vector3.up, 0.15f, 0), "Reject mismatched selections");
            Throws(() => InnerColorTriangles.Classify(inward, new[] { new[] { 0, 1, 2 } }, new[] { true }, Vector3.zero, Vector3.zero, 0.15f, 0), "Reject invalid avatar up");
            Console.WriteLine("InnerColor triangle tests passed (" + checks + " checks).");
            InnerColorAssetTests.Run();
        }
    }
}
