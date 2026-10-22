using UnityEngine;

namespace Jeni.KaihenNavi
{
    // 台帳の対応だけを Undo に載せる。シーンや購入アセットには保存しない。
    internal sealed class KonoAssetLedgerLinkUndo : ScriptableObject
    {
        public string itemId;
        public bool manual;
    }
}
