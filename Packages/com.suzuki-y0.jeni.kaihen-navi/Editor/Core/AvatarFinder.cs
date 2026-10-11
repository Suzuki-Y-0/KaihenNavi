using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>シーン内のアバター（VRC Avatar Descriptor）を集める。</summary>
    internal static class AvatarFinder
    {
        public static List<VRCAvatarDescriptor> FindInOpenScenes()
        {
            var result = new List<VRCAvatarDescriptor>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    result.AddRange(root.GetComponentsInChildren<VRCAvatarDescriptor>(true));
                }
            }

            return result;
        }

        /// <summary>素体（顔や体の本体メッシュ）を推定する。色変えで「肌」、脱ぎ着で「除外」に使う。</summary>
        public static SkinnedMeshRenderer FindBody(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            if (avatar.VisemeSkinnedMesh != null) return avatar.VisemeSkinnedMesh;

            SkinnedMeshRenderer best = null;
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var name = smr.name.ToLowerInvariant();
                if (name == "body") return smr;
                if (best == null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 20) best = smr;
            }

            return best;
        }

        /// <summary>Humanoid のボーン一覧。</summary>
        public static HashSet<Transform> HumanoidBones(VRCAvatarDescriptor avatar)
        {
            var set = new HashSet<Transform>();
            if (avatar == null) return set;
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return set;

            for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) set.Add(bone);
            }

            return set;
        }
    }
}
