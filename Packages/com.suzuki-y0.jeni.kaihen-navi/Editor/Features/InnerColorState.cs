using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>Editor 内だけで使う復元用台帳。アバターにはコンポーネントを追加しない。</summary>
    internal sealed class InnerColorState : ScriptableObject
    {
        [Serializable]
        internal sealed class Entry
        {
            public string materialPath;
            // 初回変更前のマテリアルを保存した、不変の複製。元アセットではない。
            public string originalPath;
        }

        [Serializable]
        internal sealed class Data
        {
            public List<Entry> entries = new List<Entry>();
        }

        public Data data = new Data();

        public static InnerColorState Load(VRCAvatarDescriptor avatar, bool create = false)
        {
            var path = WorkFolders.ForAvatar(avatar.gameObject) + "/InnerColorState.asset";
            var state = AssetDatabase.LoadAssetAtPath<InnerColorState>(path);
            if (state != null || !create) return state;
            WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject));
            state = CreateInstance<InnerColorState>();
            AssetDatabase.CreateAsset(state, path);
            return state;
        }
    }

    internal sealed class InnerColorBackup : IBackupParticipant
    {
        public string Key { get { return "inner-color"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
            var state = InnerColorState.Load(avatar);
            return state == null ? null : JsonUtility.ToJson(state.data);
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
            var state = InnerColorState.Load(avatar, !string.IsNullOrEmpty(json));
            if (state == null) return;
            Undo.RecordObject(state, undoName);
            state.data = string.IsNullOrEmpty(json) ? new InnerColorState.Data() :
                JsonUtility.FromJson<InnerColorState.Data>(json) ?? new InnerColorState.Data();
            EditorUtility.SetDirty(state);
            // 見た目は、この後 BackupService が保存済みのマテリアルから戻す。
        }
    }
}
