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
            if (WorkFolders.IsOwned(original)) return original;

            var folder = WorkFolders.Ensure(WorkFolders.Materials(avatar.gameObject));
            var copy = Object.Instantiate(original);
            copy.name = original.name;
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + WorkFolders.Sanitize(original.name) + ".mat");
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
