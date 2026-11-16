using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// アバターの元マテリアルを書き換えないための複製係。
    /// 初めて変更するマテリアルを改変フォルダの Materials/ に（新構成は 〈元の名前〉_〈改変名〉.mat で）複製し、
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
            string path;
            var modification = WorkFolders.ModificationNameFor(avatar.gameObject);
            if (modification == null)
            {
                // 旧構成の改変フォルダは前と同じ名前のまま
                copy.name = original.name;
                path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + WorkFolders.Sanitize(original.name) + ".mat");
            }
            else
            {
                // 新構成は AvatarPrefabMaterialExporter と同じ 〈元の名前〉_〈改変名〉（重なれば 〈元の名前〉_2_〈改変名〉）
                string display, file;
                WorkFolders.CopyMaterialName(original.name, modification, n => System.IO.File.Exists(folder + "/" + n + ".mat"),
                    out display, out file);
                copy.name = display;
                path = folder + "/" + file + ".mat";
            }
            // 複製ファイル自体は Undo の対象にしない（参照の差し替えだけを戻せば元の見た目に戻る）
            AssetDatabase.CreateAsset(copy, path);

            Replace(avatar, original, copy, undoName);
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
