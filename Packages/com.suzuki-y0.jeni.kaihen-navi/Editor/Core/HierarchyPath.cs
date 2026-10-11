using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// アバターのルートからの相対パス。同名の兄弟があっても区別できるよう「名前#番号」で表す。
    /// バックアップはオブジェクト参照ではなくこのパスで保存するので、シーンを開き直しても戻せる。
    /// </summary>
    internal static class HierarchyPath
    {
        public static string Get(Transform root, Transform target)
        {
            if (target == null || root == null) return null;
            if (target == root) return "";

            var segments = new List<string>();
            var t = target;
            while (t != null && t != root)
            {
                segments.Add(Segment(t));
                t = t.parent;
            }

            if (t != root) return null; // root の子孫ではない
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        public static Transform Find(Transform root, string path)
        {
            if (root == null || path == null) return null;
            if (path.Length == 0) return root;

            var t = root;
            foreach (var segment in path.Split('/'))
            {
                t = FindChild(t, segment);
                if (t == null) return null;
            }

            return t;
        }

        private static string Segment(Transform t)
        {
            var index = 0;
            var parent = t.parent;
            if (parent != null)
            {
                for (var i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    if (sibling == t) break;
                    if (sibling.name == t.name) index++;
                }
            }

            return Escape(t.name) + "#" + index;
        }

        private static Transform FindChild(Transform parent, string segment)
        {
            var hash = segment.LastIndexOf('#');
            if (hash < 0) return null;
            var name = Unescape(segment.Substring(0, hash));
            int index;
            if (!int.TryParse(segment.Substring(hash + 1), out index)) return null;

            var seen = 0;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != name) continue;
                if (seen == index) return child;
                seen++;
            }

            return null;
        }

        private static string Escape(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (c == '%') sb.Append("%25");
                else if (c == '/') sb.Append("%2F");
                else if (c == '#') sb.Append("%23");
                else sb.Append(c);
            }

            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            return s.Replace("%2F", "/").Replace("%23", "#").Replace("%25", "%");
        }
    }
}
