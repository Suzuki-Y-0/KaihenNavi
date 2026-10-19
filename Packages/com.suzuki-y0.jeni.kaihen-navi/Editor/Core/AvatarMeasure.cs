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
            var found = false;
            var top = 0f;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(false))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var y = r.bounds.max.y;
                if (!found || y > top) top = y;
                found = true;
            }

            var h = found ? top - avatar.transform.position.y : 0f;
            if (h > 0.1f) return h;

            // メッシュが見つからないときは視点の高さから見積もる
            return Mathf.Max(0.5f, avatar.ViewPosition.y / 0.92f);
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
