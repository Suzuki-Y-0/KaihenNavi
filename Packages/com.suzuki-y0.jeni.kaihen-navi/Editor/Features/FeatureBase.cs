using System;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 「やりたいこと」1 つぶんの機能。ナビに表示され、選ぶと操作エリアに Build() の中身が出る。
    /// 新しい機能はこのクラスを継承して FeatureCatalog に登録する。
    /// </summary>
    internal abstract class FeatureBase
    {
        /// <summary>英小文字の識別子。言語ファイルのキー（feature.ID.title など）にも使う。</summary>
        public abstract string Id { get; }

        /// <summary>ナビのグループキー。表示名は nav.group.KEY で翻訳する。</summary>
        public virtual string Group
        {
            get
            {
                switch (Id)
                {
                    case "outfit": case "transform": case "toggle": case "skin": return "outfit";
                    case "color": case "innerColor": case "light": case "pink": return "appearance";
                    case "body": case "view": case "walk": return "body";
                    default: return "finish";
                }
            }
        }

        /// <summary>企画書の機能番号（1〜10）。</summary>
        public abstract int Number { get; }

        /// <summary>カードのアイコンに出す 1 文字。言語ファイルの feature.ID.icon で上書きできる。</summary>
        public virtual string Icon { get { return L10n.Tr("feature." + Id + ".icon"); } }

        public abstract Color Accent { get; }

        /// <summary>体験版でも使えるか。</summary>
        public abstract bool InTrial { get; }

        /// <summary>実装済みか（未実装は「近日追加」と表示する）。</summary>
        public virtual bool Implemented { get { return true; } }

        /// <summary>アバター内のオブジェクト構成が変わったとき、候補を取り直す必要があるか。</summary>
        public virtual bool RefreshOnHierarchyChange { get { return false; } }

        public string Title { get { return L10n.Tr("feature." + Id + ".title"); } }
        public string Description { get { return L10n.Tr("feature." + Id + ".desc"); } }

        /// <summary>ナビでは短い名前を使い、未定義ならページの見出しを使う。</summary>
        public string NavTitle
        {
            get
            {
                var key = "feature." + Id + ".nav";
                var title = L10n.Tr(key);
                return title == key ? Title : title;
            }
        }

        /// <summary>ナビの右端に表示する件数。ナビ再構築時にだけ呼ばれる。</summary>
        public virtual string NavBadge(VRCAvatarDescriptor avatar) => null;

        /// <summary>完了済みならナビの右端に緑のチェックを出す。</summary>
        public virtual bool IsDone(VRCAvatarDescriptor avatar) => false;

        /// <summary>体験版のため鍵がかかっているか。</summary>
        public bool Locked { get { return Edition.IsTrial && !InTrial; } }

        /// <summary>操作エリアの中身を作る。</summary>
        public abstract VisualElement Build(FeatureContext ctx);

        /// <summary>別の機能やアバターに切り替わるとき。</summary>
        public virtual void OnClosed()
        {
        }
    }

    /// <summary>機能に渡す、いま選んでいるアバターなどの情報。</summary>
    internal sealed class FeatureContext
    {
        public VRCAvatarDescriptor Avatar;
        public BackupService Backup;
        public string FeatureId;

        /// <summary>Undo/Redo 後の保存待ちを、新しい編集で解除する。</summary>
        public Action Editing;

        /// <summary>操作エリアを作り直す（一覧の中身が変わったときなど）。</summary>
        public Action Rebuild;

        /// <summary>①で選ぶアバターを切り替える（シーンに新しく出したときなど）。</summary>
        public Action<VRCAvatarDescriptor> SelectAvatar;

        /// <summary>別の「やりたいこと」に切り替える（id を指定）。</summary>
        public Action<string> OpenFeature;

        /// <summary>何かを変える直前に呼ぶ。最初の 1 回だけ自動バックアップを取る。</summary>
        public void BeginEdit()
        {
            if (Editing != null) Editing();
            Backup.BeginEdit(Avatar, FeatureId);
        }

        /// <summary>保存による Prefab の変更も含めて、編集を Ctrl+Z 1 回分にまとめる。</summary>
        public void CompleteEdit(int group)
        {
            // RecordObject の差分を確定してから Prefab 側の Undo を登録する。
            UnityEditor.Undo.FlushUndoRecordObjects();
            try
            {
                MyAvatar.Save(Avatar);
            }
            finally
            {
                UnityEditor.Undo.CollapseUndoOperations(group);
            }
        }

        public void NoteCreated(GameObject created)
        {
            Backup.NoteCreated(Avatar, created);
        }
    }

    /// <summary>製品版で追加する予定の機能（カードだけ表示する）。</summary>
    internal sealed class PlannedFeature : FeatureBase
    {
        private readonly string _id;
        private readonly int _number;
        private readonly Color _accent;

        public PlannedFeature(string id, int number, string accentHex)
        {
            _id = id;
            _number = number;
            _accent = Ui.Hex(accentHex);
        }

        public override string Id { get { return _id; } }
        public override int Number { get { return _number; } }
        public override Color Accent { get { return _accent; } }
        public override bool InTrial { get { return false; } }
        public override bool Implemented { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("planned.notice"), "info"));
            return root;
        }
    }
}
