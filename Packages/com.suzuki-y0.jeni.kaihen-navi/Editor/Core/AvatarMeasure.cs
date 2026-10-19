using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>アバターの寸法（目の位置・身長など）を測る。視点合わせや体型・身長の変更で使う。</summary>
    internal static class AvatarMeasure
    {
        /// <summary>目のボーンが無いときに、頭のボーンから目までの高さとみなす割合（身長に対して）。</summary>
        private const float HeadToEyeRatio = 0.045f;

        /// <summary>目の中心から顔の表面までのおおよその距離（身長に対して）。視点は目玉の中心より少し前に置く。</summary>
        private const float EyeToSurfaceRatio = 0.01f;

        public static Animator HumanAnimator(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            var animator = avatar.GetComponent<Animator>();
            return animator != null && animator.isHuman ? animator : null;
        }

        public static Transform Bone(VRCAvatarDescriptor avatar, HumanBodyBones bone)
        {
            var animator = HumanAnimator(avatar);
            return animator == null ? null : animator.GetBoneTransform(bone);
        }

        /// <summary>足元から頭のてっぺんまでのおおよその高さ（m）。メッシュの大きさで測る。</summary>
        public static float Height(VRCAvatarDescriptor avatar)
        {
            // 何度も呼ばれる（サムネイルの撮り直しなど）ので、少しの間は覚えておく
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (avatar.GetInstanceID() == _cachedId && now - _cachedAt < 2.0) return _cachedHeight;

            var h = MeasureTop(avatar) - avatar.transform.position.y;
            // メッシュが見つからないときは視点の高さから見積もる
            if (h <= 0.1f) h = Mathf.Max(0.5f, avatar.ViewPosition.y / 0.92f);

            _cachedId = avatar.GetInstanceID();
            _cachedAt = now;
            _cachedHeight = h;
            return h;
        }

        /// <summary>着せた服（MA の Merge Armature が入っている、アバター直下のもの）。</summary>
        private static List<Transform> OutfitRoots(VRCAvatarDescriptor avatar)
        {
            var result = new List<Transform>();
#if KAIHENNAVI_MA
            foreach (Transform child in avatar.transform)
            {
                if (child.GetComponentInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeArmature>(true) != null) result.Add(child);
            }
#endif
            return result;
        }

        /// <summary>覚えている身長を捨てる（身長を変えたあとに測り直すため）。</summary>
        public static void Forget()
        {
            _cachedId = 0;
        }

        private static int _cachedId;
        private static double _cachedAt;
        private static float _cachedHeight;

        /// <summary>
        /// いちばん上の頂点の高さ。レンダラーの外枠（bounds）は実際より大きめなことが多いので、
        /// スキンメッシュは今のポーズで頂点を計算して測る。
        /// </summary>
        private static float MeasureTop(VRCAvatarDescriptor avatar)
        {
            var found = false;
            var top = 0f;
            var baked = new Mesh();
            var outfits = OutfitRoots(avatar);
            try
            {
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(false))
                {
                    // 着せた服は、アップロードのときに体の骨へ合わせられるまで元の大きさのまま置かれているので測らない
                    if (outfits.Any(o => r.transform.IsChildOf(o))) continue;

                    float y;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null)
                    {
                        if (smr.sharedMesh == null) continue;
                        smr.BakeMesh(baked, true);
                        var vertices = baked.vertices;
                        if (vertices.Length == 0) continue;
                        // useScale = true で焼いた頂点はスケール込みなので、位置と回転だけ足す
                        var origin = smr.transform.position;
                        var rotation = smr.transform.rotation;
                        y = float.MinValue;
                        foreach (var v in vertices) y = Mathf.Max(y, (origin + rotation * v).y);
                    }
                    else if (r is MeshRenderer)
                    {
                        y = r.bounds.max.y;
                    }
                    else
                    {
                        continue;
                    }

                    if (!found || y > top) top = y;
                    found = true;
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }

            return found ? top : avatar.transform.position.y;
        }

        /// <summary>目の中心（ワールド座標）。目のボーンが無ければ頭のボーンから見積もる。見つからなければ null。</summary>
        public static Vector3? EyeCenter(VRCAvatarDescriptor avatar)
        {
            var left = Bone(avatar, HumanBodyBones.LeftEye);
            var right = Bone(avatar, HumanBodyBones.RightEye);
            if (left != null && right != null) return (left.position + right.position) * 0.5f;

            var head = Bone(avatar, HumanBodyBones.Head);
            if (head == null) return null;
            return head.position + avatar.transform.up * (Height(avatar) * HeadToEyeRatio);
        }

        /// <summary>おすすめの視点（Avatar Descriptor の View Position と同じ、アバターから見た座標）。</summary>
        public static Vector3? SuggestedViewPosition(VRCAvatarDescriptor avatar)
        {
            var eyes = EyeCenter(avatar);
            if (!eyes.HasValue) return null;

            var world = eyes.Value + avatar.transform.forward * (Height(avatar) * EyeToSurfaceRatio);
            var local = avatar.transform.InverseTransformPoint(world);
            local.x = 0f; // 左右は真ん中にそろえる
            return local;
        }
    }
}
