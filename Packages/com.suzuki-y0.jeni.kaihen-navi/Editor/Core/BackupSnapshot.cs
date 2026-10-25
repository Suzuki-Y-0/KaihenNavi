using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 操作前のアバターの状態。Assets/KaihenNavi/アバター名/Backups/ に .asset として保存する。
    /// オブジェクトはアバターのルートからの相対パス（HierarchyPath）で記録する。
    /// </summary>
    public sealed class BackupSnapshot : ScriptableObject
    {
        [Serializable]
        public sealed class TransformState
        {
            public string path;
            public bool active;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        [Serializable]
        public sealed class RendererState
        {
            public string path;
            public Material[] materials;
            /// <summary>旧バックアップでは false。面を分けた後もメッシュは直接書き換えない。</summary>
            public bool hasMesh;
            public Mesh mesh;
            public int rendererIndex;
        }

        /// <summary>ツールが作った複製マテリアルの中身。複製は直接書き換えるため、値ごと保存しておく。</summary>
        [Serializable]
        public sealed class MaterialState
        {
            public Material live;
            public Material saved;
        }

        [Serializable]
        public sealed class BlendShapeState
        {
            public string path;
            public float[] weights;
        }

        /// <summary>機能ごとの追加データ（IBackupParticipant が JSON で読み書きする）。</summary>
        [Serializable]
        public sealed class ExtraState
        {
            public string key;
            public string json;
        }

        /// <summary>アバターの識別子（GlobalObjectId）。同名アバターの取り違えを防ぐ。</summary>
        public string avatarId;
        public string avatarName;
        /// <summary>作成日時（DateTime.Ticks）。並べ替えに使う。</summary>
        public long createdTicks;
        /// <summary>どの「やりたいこと」の前に取ったか（FeatureBase.Id）。</summary>
        public string featureId;
        /// <summary>同じ機能を続けて操作したまとまりの識別子。旧バックアップでは createdTicks を使う。</summary>
        public long sessionTicks;
        /// <summary>Undo グループ名と翻訳キー。未知の名前は画面で機能名に置き換える。</summary>
        public string operationName;
        public string operationKey;
        /// <summary>Undo・Redo と同じグループで更新する履歴の表示状態。復元後もファイルは消さない。</summary>
        public bool hiddenFromHistory;

        public long SessionTicks { get { return sessionTicks == 0 ? createdTicks : sessionTicks; } }

        public Vector3 viewPosition;
        public List<TransformState> transforms = new List<TransformState>();
        public List<RendererState> renderers = new List<RendererState>();
        public List<MaterialState> materials = new List<MaterialState>();
        public List<BlendShapeState> blendShapes = new List<BlendShapeState>();
        public List<ExtraState> extras = new List<ExtraState>();

        /// <summary>このバックアップの後にツールが作ったオブジェクト。元に戻すときに削除する。</summary>
        public List<string> createdPaths = new List<string>();

        public DateTime CreatedAt
        {
            get { return new DateTime(createdTicks, DateTimeKind.Local); }
        }
    }
}
