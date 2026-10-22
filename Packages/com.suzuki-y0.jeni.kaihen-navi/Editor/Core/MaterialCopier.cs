using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// アバターの元マテリアルを書き換えないための複製係。
    /// 初めて変更するマテリアルを Assets/KaihenNavi/アバター名/Materials/ に複製し、
    /// アバター内でそのマテリアルを使っている場所をすべて複製に差し替える。
    /// </summary>
    internal static class MaterialCopier
    {
        /// <summary>
        /// 変更してよいマテリアルを返す。元マテリアルなら複製して差し替える（Undo 対応）。
        /// </summary>
        public static Material EnsureOwned(VRCAvatarDescriptor avatar, Material original, string undoName)
        {
            if (avatar == null || original == null) return null;
            // ほかのアバター（別バージョンなど）用の複製は共有せず、このアバター用にもう一度複製する
            if (WorkFolders.IsOwnedBy(original, avatar.gameObject)) return original;

            var folder = WorkFolders.Ensure(WorkFolders.Materials(avatar.gameObject));
            var copy = Object.Instantiate(original);
            copy.name = original.name;
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + WorkFolders.Sanitize(original.name) + ".mat");
            // 複製ファイル自体は Undo の対象にしない（参照の差し替えだけを戻せば元の見た目に戻る）
            AssetDatabase.CreateAsset(copy, path);

            Replace(avatar, original, copy, undoName);
            return copy;
        }

        /// <summary>選んだ髪専用の固定パスのコピー。全レンダラーの差し替えは呼び出し側で行う。</summary>
        internal static Material EnsureInnerColorCopy(Material original, string path, string key, string undoName)
        {
            if (!WorkFolders.IsToolPath(path)) throw new System.InvalidOperationException("Invalid InnerColor material path");
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = Object.Instantiate(original);
                copy.name = original.name + "_InnerColor";
                AssetDatabase.CreateAsset(copy, path);
                InnerColorAssets.Mark(path, key);
            }
            else
            {
                Undo.RecordObject(copy, undoName);
                // Ctrl+Z で残った未使用コピーも再利用し、変更前の値にそろえる。
                EditorUtility.CopySerialized(original, copy);
                copy.name = original.name + "_InnerColor";
            }
            return copy;
        }

        /// <summary>アバター内の original をすべて replacement に差し替える。</summary>
        public static void Replace(VRCAvatarDescriptor avatar, Material original, Material replacement, string undoName)
        {
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != original) continue;
                    mats[i] = replacement;
                    changed = true;
                }

                if (!changed) continue;
                Undo.RecordObject(r, undoName);
                r.sharedMaterials = mats;
            }
        }

        /// <summary>アバター内で使われているマテリアルと、それを使っているレンダラー。</summary>
        public static Dictionary<Material, List<Renderer>> Collect(VRCAvatarDescriptor avatar)
        {
            var result = new Dictionary<Material, List<Renderer>>();
            if (avatar == null) return result;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    List<Renderer> list;
                    if (!result.TryGetValue(m, out list))
                    {
                        list = new List<Renderer>();
                        result.Add(m, list);
                    }

                    if (!list.Contains(r)) list.Add(r);
                }
            }

            return result;
        }
    }
}
