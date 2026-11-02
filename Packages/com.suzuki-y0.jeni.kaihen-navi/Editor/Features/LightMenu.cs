#if KAIHENNAVI_MA
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 明るさスライダー（ラジアルパペット）の中身。まだ画面は無く、Modular Avatar の部品と、
    /// lilToon の _LightMinLimit を 0 → 1 に動かす AnimatorController を作るところまでを受け持つ。
    /// コントローラーはアバターのフォルダに生成し、消しても元の見た目には影響しない。
    /// </summary>
    internal static class LightMenu
    {
        internal const string ObjectName = "KaihenNavi_LightMenu";
        internal const string Parameter = "KaihenNavi/Light";
        internal const string Property = "_LightMinLimit";

        private const string LilToonMarker = "_lilToonVersion";

        /// <summary>アバター直下にあるメニュー用オブジェクト（無ければ null）。</summary>
        internal static GameObject Find(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            var t = avatar.transform.Find(ObjectName);
            return t == null ? null : t.gameObject;
        }

        /// <summary>lilToon のマテリアルを使っているレンダラーの数（パーティクルは除く）。</summary>
        internal static int CountTargets(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return 0;
            var count = 0;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (HasLilToon(r)) count++;
            }

            return count;
        }

        /// <summary>
        /// 明るさを動かすコントローラーを作り直す。対象のレンダラーごとに、
        /// _LightMinLimit を 0 秒で 0、1 秒で 1 にするカーブを 1 本のクリップに入れる。
        /// </summary>
        internal static AnimatorController BuildController(VRCAvatarDescriptor avatar, string undoName)
        {
            var folder = WorkFolders.Ensure(WorkFolders.ForAvatar(avatar.gameObject) + "/Animations");
            var path = folder + "/KaihenNavi_Light.controller";

            // 作り直すときはファイルを消さず、クリップの中身だけを入れ替える。
            // ファイルを消すと GUID が変わり、作り直しを Ctrl+Z したときに Merge Animator の参照が Missing になる。
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var existingClip = existing == null ? null :
                AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault();
            if (existing != null && existingClip != null)
            {
                Undo.RegisterCompleteObjectUndo(existingClip, undoName);
                existingClip.ClearCurves();
                FillCurves(avatar, existingClip);
                EditorUtility.SetDirty(existingClip);
                AssetDatabase.SaveAssets();
                return existing;
            }

            // 中身の欠けた生成物（このツールが作るファイル）だけは消して作り直す
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);

            var clip = new AnimationClip { name = "KaihenNavi_Light" };
            FillCurves(avatar, clip);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AssetDatabase.AddObjectToAsset(clip, controller);
            controller.AddParameter(Parameter, AnimatorControllerParameterType.Float);

            var state = controller.layers[0].stateMachine.AddState("Light");
            state.motion = clip;
            state.timeParameterActive = true;
            state.timeParameter = Parameter;
            state.writeDefaultValues = false;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// lilToon を使うレンダラーごとに、_LightMinLimit を 0 秒で 0、1 秒で 1 にするカーブを入れる。
        /// material.〜 のアニメーションは、そのレンダラーの全部のマテリアル枠のうち、同じ項目を持つものに効く。
        /// </summary>
        private static void FillCurves(VRCAvatarDescriptor avatar, AnimationClip clip)
        {
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!HasLilToon(r)) continue;

                var binding = EditorCurveBinding.FloatCurve(
                    AnimationUtility.CalculateTransformPath(r.transform, avatar.transform),
                    r.GetType(),
                    "material." + Property);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }
        }

        /// <summary>
        /// アバター直下にメニュー用オブジェクトを作り（あれば使い回し）、
        /// ラジアルパペット・パラメータ・Merge Animator をそろえる。
        /// </summary>
        internal static void Create(FeatureContext ctx, string label, float defaultValue, string undoName)
        {
            var avatar = ctx.Avatar;
            var controller = BuildController(avatar, undoName);

            var go = Find(avatar);
            if (go == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, undoName);
                go.transform.SetParent(avatar.transform, false);
                ctx.NoteCreated(go);
            }

            var installer = AddOrRecord<ModularAvatarMenuInstaller>(go, undoName);
            var item = AddOrRecord<ModularAvatarMenuItem>(go, undoName);
            var parameters = AddOrRecord<ModularAvatarParameters>(go, undoName);
            var merge = AddOrRecord<ModularAvatarMergeAnimator>(go, undoName);

            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = Parameter },
                },
            };
            item.label = label;
            item.isSynced = true;
            item.isSaved = true;

            parameters.parameters = new List<ParameterConfig>
            {
                new ParameterConfig
                {
                    nameOrPrefix = Parameter,
                    syncType = ParameterSyncType.Float,
                    defaultValue = Mathf.Clamp01(defaultValue),
                    saved = true,
                    hasExplicitDefaultValue = true,
                },
            };

            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = false;

            MarkChanged(installer);
            MarkChanged(item);
            MarkChanged(parameters);
            MarkChanged(merge);
        }

        /// <summary>メニュー用オブジェクトを消す。生成したアセットのファイルはそのまま残す。</summary>
        internal static void Remove(VRCAvatarDescriptor avatar, string undoName)
        {
            var go = Find(avatar);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }

        // ---------------------------------------------------------------- 部品

        /// <summary>コンポーネントを取り出す（無ければ足す）。前からあったものは Undo で記録してから返す。</summary>
        private static T AddOrRecord<T>(GameObject go, string undoName) where T : Component
        {
            var component = go.GetComponent<T>();
            if (component == null) return Undo.AddComponent<T>(go);
            Undo.RecordObject(component, undoName);
            return component;
        }

        private static bool HasLilToon(Renderer renderer)
        {
            if (renderer == null) return false;
            var materials = renderer.sharedMaterials;
            if (materials == null) return false;
            foreach (var material in materials)
            {
                if (material != null && material.HasProperty(LilToonMarker)) return true;
            }

            return false;
        }

        private static void MarkChanged(Component component)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(component);
        }
    }
}
#endif
