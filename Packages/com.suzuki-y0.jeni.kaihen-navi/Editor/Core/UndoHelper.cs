using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// スライダーを動かしている間の細かい変更を、Ctrl+Z 1 回で戻せる 1 つの操作にまとめる。
    /// </summary>
    internal static class UndoHelper
    {
        private const double MergeSeconds = 0.6;

        private static string _lastKey;
        private static double _lastTime;
        private static int _group = -1;

        /// <summary>変更の直前に呼ぶ。同じ key の変更が続いている間は同じグループになる。</summary>
        public static void Begin(string key, string undoName)
        {
            var now = EditorApplication.timeSinceStartup;
            if (_lastKey != key || now - _lastTime > MergeSeconds || _group < 0)
            {
                Undo.IncrementCurrentGroup();
                _group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
            }

            _lastKey = key;
            _lastTime = now;
        }

        /// <summary>変更の直後に呼ぶ。</summary>
        public static void End()
        {
            if (_group >= 0) Undo.CollapseUndoOperations(_group);
            _lastTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>まとめずに、1 回の操作として区切る。</summary>
        public static void Separate()
        {
            _lastKey = null;
            _group = -1;
        }

        public static void Record(Object target, string undoName)
        {
            if (target == null) return;
            Undo.RecordObject(target, undoName);
        }

        public static void MarkDirty(Object target)
        {
            if (target == null) return;
            EditorUtility.SetDirty(target);
        }
    }
}
