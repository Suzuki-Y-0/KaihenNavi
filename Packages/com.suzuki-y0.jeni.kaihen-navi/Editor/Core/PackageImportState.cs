using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>取り込んだファイルの状態だけを覚える Editor 専用の Undo。アバターには付けない。</summary>
    internal sealed class PackageImportState : ScriptableObject
    {
        public string folder;
        public bool applied;
    }

}
