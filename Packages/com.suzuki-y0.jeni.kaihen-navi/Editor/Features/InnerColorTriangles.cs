using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>画像の位置を使わず、三角形の向きと高さだけで髪の内側を決める。</summary>
    internal static class InnerColorTriangles
    {
        internal sealed class Split
        {
            public int[][] outer;
            public int[] inner;
        }

        internal static Vector3 HeadCenter(Vector3 head, Vector3 up, float height)
        {
            return head + up.normalized * (height * 0.04f);
        }

        /// <summary>画面では大きいほど範囲が広い。初期値 56.25 はしきい値 0.15 に対応する。</summary>
        internal static float Threshold(float range)
        {
            return 0.6f - Mathf.Clamp01(range / 100) * 0.8f;
        }

        internal static Split Classify(Vector3[] positions, int[][] triangles, bool[] selected,
            Vector3 center, Vector3 up, float threshold, float height)
        {
            if (positions == null || triangles == null || selected == null || triangles.Length != selected.Length)
                throw new ArgumentException("Invalid triangle input");
            up = up.normalized;
            if (up.sqrMagnitude < 0.5f) throw new ArgumentException("Invalid avatar up direction");
            var lowest = 0f;
            var found = false;
            for (var slot = 0; slot < triangles.Length; slot++)
            {
                if (!selected[slot]) continue;
                if (triangles[slot].Length % 3 != 0) throw new ArgumentException("Invalid triangle count");
                foreach (var index in triangles[slot])
                {
                    if (index < 0 || index >= positions.Length) throw new ArgumentException("Invalid triangle index");
                    var y = Vector3.Dot(positions[index] - center, up);
                    lowest = found ? Mathf.Min(lowest, y) : y;
                    found = true;
                }
            }
            var outer = new int[triangles.Length][];
            var inner = new List<int>();
            for (var slot = 0; slot < triangles.Length; slot++)
            {
                if (!selected[slot]) { outer[slot] = triangles[slot].ToArray(); continue; }
                var keep = new List<int>();
                var indices = triangles[slot];
                for (var i = 0; i < indices.Length; i += 3)
                {
                    var a = positions[indices[i]];
                    var b = positions[indices[i + 1]];
                    var c = positions[indices[i + 2]];
                    var p = (a + b + c) * (1f / 3f);
                    // 頂点法線ではなく、ワールド座標の辺から面の法線を求める。
                    var n = Vector3.Cross(b - a, c - a);
                    // normalized は小さい面の法線を 0 にするため、長さを明示して割る。
                    var length = (float)Math.Sqrt(n.sqrMagnitude);
                    var d = center - p;
                    d -= up * Vector3.Dot(d, up) * 0.7f;
                    var inside = length > 1e-12f && d.sqrMagnitude > 1e-12f &&
                        Vector3.Dot(n * (1f / length), d.normalized) > threshold &&
                        (height <= 0 || Vector3.Dot(p - center, up) < lowest * Mathf.Clamp01(height));
                    var target = inside ? inner : keep;
                    target.Add(indices[i]); target.Add(indices[i + 1]); target.Add(indices[i + 2]);
                }
                outer[slot] = keep.ToArray();
            }
            return new Split { outer = outer, inner = inner.ToArray() };
        }
    }
}
