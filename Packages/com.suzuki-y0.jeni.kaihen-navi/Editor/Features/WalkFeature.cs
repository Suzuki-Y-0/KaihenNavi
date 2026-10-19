using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 13：動きを変えたい（製品版）。歩き方（Base）・座り方（Sitting）・エモートや AFK（Action）を入れ替える。
    /// アバターの設定（Base 層のコントローラー）は直接書き換えず、MA Merge Animator を「置き換え（Replace）」モードで
    /// 置いたオブジェクト（KaihenNavi_Walk）を 1 つ足す。アップロードのときだけ MA が差し替えるので非破壊で、
    /// このオブジェクトを消せば元の歩き方に戻る。
    /// 置き場所は「このアバターだけ」（改変アバターの中）か「みんな」（改変のベースの中）を選べる。
    /// </summary>
    internal sealed class WalkFeature : FeatureBase
    {
        public const string WalkObjectName = "KaihenNavi_Walk";

#if KAIHENNAVI_MA
        // 入れ替えられる動き。FX（表情・メニューの切り替え）や Gesture（手の形）は丸ごと消えて困るので出さない
        private static readonly VRCAvatarDescriptor.AnimLayerType[] Layers =
        {
            VRCAvatarDescriptor.AnimLayerType.Base,
            VRCAvatarDescriptor.AnimLayerType.Sitting,
            VRCAvatarDescriptor.AnimLayerType.Action,
        };

        private static readonly string[] ObjectNames = { WalkObjectName, "KaihenNavi_Sit", "KaihenNavi_Action" };
        private static readonly string[] LayerKeys = { "walk.layerWalk", "walk.layerSit", "walk.layerAction" };
        private static int _layer;

        private static VRCAvatarDescriptor.AnimLayerType CurrentLayer { get { return Layers[_layer]; } }
        private static string CurrentObjectName { get { return ObjectNames[_layer]; } }
#endif

#if KAIHENNAVI_MA
        private static int _scope;   // 0 = このアバターだけ, 1 = みんな（ベース）
#endif

        public override string Id { get { return "walk"; } }
        public override int Number { get { return 13; } }
        public override Color Accent { get { return Ui.Hex("#7BC8A4"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("walk.intro"), "info"));
#if KAIHENNAVI_MA
            var avatar = ctx.Avatar;
            var basePrefab = BaseOf(avatar);

            // どの動き？
            root.Add(Ui.Heading(L10n.Tr("walk.which")));
            root.Add(Ui.Chips(LayerKeys.Select(k => L10n.Tr(k)).ToList(), _layer, i =>
            {
                _layer = i;
                UndoHelper.Separate();
                ctx.Rebuild();
            }));
            root.Add(Ui.Text(L10n.Tr(LayerKeys[_layer] + "Hint"), "kn-hint"));

            // いまの歩き方
            root.Add(Ui.Heading(L10n.Tr("walk.current", L10n.Tr(LayerKeys[_layer]))));
            var replacers = Replacers(avatar.gameObject);
            if (replacers.Count == 0)
            {
                root.Add(Ui.Text(L10n.Tr("walk.currentOriginal"), "kn-readout"));
            }
            else
            {
                foreach (var r in replacers)
                {
                    var name = r.animator != null ? r.animator.name : "-";
                    string where;
                    if (basePrefab != null && IsFromBase(r.gameObject, basePrefab))
                    {
                        // ベースの歩き方を、このアバターだけ別のものに変えているか
                        var shared = FindWalk(basePrefab.transform);
                        where = shared != null && shared.animator != r.animator ? L10n.Tr("walk.fromBaseChanged") : L10n.Tr("walk.fromBase");
                    }
                    else
                    {
                        where = L10n.Tr("walk.fromThis");
                    }

                    root.Add(Ui.Text(L10n.Tr("walk.currentIs", name, where), "kn-readout"));
                }
            }

            // 置き換えが 2 つあると MA がアップロードを止めるので、直し方を出す
            if (replacers.Count > 1)
            {
                root.Add(Ui.Notice(L10n.Tr("walk.conflict"), "warn"));
                root.Add(Ui.Button(L10n.Tr("walk.fixConflict"), () => FixConflict(ctx, basePrefab), "kn-button--primary"));
            }

            // どこに効かせる？
            root.Add(Ui.Heading(L10n.Tr("walk.scope")));
            root.Add(Ui.Chips(new List<string> { L10n.Tr("walk.scopeThis"), L10n.Tr("walk.scopeAll") }, _scope, i =>
            {
                _scope = i;
                ctx.Rebuild();
            }));

            if (_scope == 1 && basePrefab == null)
            {
                root.Add(Ui.Notice(L10n.Tr("walk.noBase"), "warn"));
                return root;
            }

            if (_scope == 1)
            {
                var children = MyAvatar.ChildrenOf(basePrefab);
                root.Add(Ui.Text(L10n.Tr("walk.scopeAllHint", children.Count, string.Join(", ", children.Select(c => c.name).Take(5).ToArray())), "kn-hint"));
            }

            // 歩き方をえらぶ（コントローラーか、MA 用の Prefab）
            root.Add(Ui.Heading(L10n.Tr("walk.pick")));
            root.Add(PickZone(ctx, basePrefab));

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("walk.reset"), () => ResetWalk(ctx, basePrefab), "kn-button--ghost"));
            root.Add(buttons);
            root.Add(Ui.Text(L10n.Tr("walk.tip"), "kn-hint"));
#else
            root.Add(Ui.Notice(L10n.Tr("walk.needMa"), "warn"));
#endif
            return root;
        }

#if KAIHENNAVI_MA
        // ---------------------------------------------------------------- 調べる

        /// <summary>Base 層を置き換える Merge Animator（隠してあっても MA は使うので、隠れているものも数える）。</summary>
        private static List<ModularAvatarMergeAnimator> Replacers(GameObject root)
        {
            return root.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Where(m => m.layerType == CurrentLayer && m.mergeAnimatorMode == MergeAnimatorMode.Replace)
                .ToList();
        }

        private static GameObject BaseOf(VRCAvatarDescriptor avatar)
        {
            var path = MyAvatar.PrefabPath(avatar);
            if (!MyAvatar.IsMinePath(path)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab == null ? null : MyAvatar.ParentBaseOf(prefab);
        }

        /// <summary>このオブジェクトがベースから来たものか（改変アバターで足したものではないか）。</summary>
        private static bool IsFromBase(GameObject go, GameObject basePrefab)
        {
            var basePath = AssetDatabase.GetAssetPath(basePrefab);
            Object current = go;
            for (var i = 0; i < 16 && current != null; i++)
            {
                current = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (current != null && AssetDatabase.GetAssetPath(current) == basePath) return true;
            }

            return false;
        }

        private static ModularAvatarMergeAnimator FindWalk(Transform root)
        {
            var t = root.Find(CurrentObjectName);
            return t == null ? null : t.GetComponent<ModularAvatarMergeAnimator>();
        }

        // ---------------------------------------------------------------- えらぶ

        private VisualElement PickZone(FeatureContext ctx, GameObject basePrefab)
        {
            var zone = Ui.Box("kn-drop");
            zone.Add(Ui.Text(L10n.Tr("walk.dropHint"), "kn-drop__text"));

            zone.RegisterCallback<DragUpdatedEvent>(e =>
            {
                DragAndDrop.visualMode = FromDrag() != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                zone.AddToClassList("kn-drop--over");
            });
            zone.RegisterCallback<DragLeaveEvent>(e => zone.RemoveFromClassList("kn-drop--over"));
            zone.RegisterCallback<DragPerformEvent>(e =>
            {
                zone.RemoveFromClassList("kn-drop--over");
                var picked = FromDrag();
                if (picked == null) return;
                DragAndDrop.AcceptDrag();
                Use(ctx, basePrefab, picked);
            });

            zone.Add(ItemPicker.PickRow(typeof(RuntimeAnimatorController), picked => Use(ctx, basePrefab, picked)));
            return zone;
        }

        private static Object FromDrag()
        {
            foreach (var o in DragAndDrop.objectReferences)
            {
                if (o is RuntimeAnimatorController) return o;
                var go = o as GameObject;
                if (go != null && AssetDatabase.GetAssetPath(go).EndsWith(".prefab")) return go;
            }

            return null;
        }

        private void Use(FeatureContext ctx, GameObject basePrefab, Object picked)
        {
            var controller = picked as RuntimeAnimatorController;
            if (controller != null)
            {
                if (_scope == 1) SetOnBase(ctx, basePrefab, controller);
                else SetOnAvatar(ctx, basePrefab, controller);
                return;
            }

            var prefab = picked as GameObject;
            if (prefab == null) return;

            // MA 用に作られた歩き方の Prefab（GoGo Loco など）は、中に入れるだけ
            if (prefab.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Length == 0)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.notWalk", prefab.name), L10n.Tr("dialog.ok"));
                return;
            }

            if (_scope == 1) AddPrefabToBase(ctx, basePrefab, prefab);
            else AddPrefabToAvatar(ctx, prefab);
        }

        // ---------------------------------------------------------------- このアバターだけ

        private static void SetOnAvatar(FeatureContext ctx, GameObject basePrefab, RuntimeAnimatorController controller)
        {
            var avatar = ctx.Avatar;
            var undoName = L10n.Tr("undo.walk");
            var walk = FindWalk(avatar.transform);

            // 自分で入れた別の歩き方ギミックがあれば、重なってアップロードできなくなるので止める
            var others = Replacers(avatar.gameObject).Where(r => r != walk).ToList();
            if (others.Count > 0)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.otherGimmick", others[0].gameObject.name), L10n.Tr("dialog.ok"));
                return;
            }

            ctx.BeginEdit();
            UndoHelper.Separate();
            if (walk != null)
            {
                // ベースから来たものなら、このアバターだけの上書きになる（ベースは変わらない）
                Undo.RecordObject(walk, undoName);
                walk.animator = controller;
                EditorUtility.SetDirty(walk);
            }
            else
            {
                var go = new GameObject(CurrentObjectName);
                Undo.RegisterCreatedObjectUndo(go, undoName);
                go.transform.SetParent(avatar.transform, false);
                ctx.NoteCreated(go);
                Configure(Undo.AddComponent<ModularAvatarMergeAnimator>(go), controller);
            }

            UndoHelper.Separate();
            EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.doneThis", controller.name), L10n.Tr("dialog.ok"));
            ctx.Rebuild();
        }

        private static void AddPrefabToAvatar(FeatureContext ctx, GameObject prefab)
        {
            var avatar = ctx.Avatar;
            if (Replacers(avatar.gameObject).Count > 0 && Replacers(prefab).Count > 0)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.resetFirst"), L10n.Tr("dialog.ok"));
                return;
            }

            var undoName = L10n.Tr("undo.walk");
            ctx.BeginEdit();
            UndoHelper.Separate();
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) return;
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            Undo.SetTransformParent(instance.transform, avatar.transform, undoName);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            ctx.NoteCreated(instance);
            UndoHelper.Separate();
            EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.doneThis", prefab.name), L10n.Tr("dialog.ok"));
            ctx.Rebuild();
        }

        // ---------------------------------------------------------------- みんな（ベース）

        /// <summary>ベースの中身を開いて変える。Ctrl+Z では戻せないので、先に確認する。</summary>
        private static bool EditBase(GameObject basePrefab, string confirmKey, string itemName, System.Func<GameObject, bool> edit)
        {
            var children = MyAvatar.ChildrenOf(basePrefab);
            if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr(confirmKey, itemName, children.Count), L10n.Tr("dialog.ok"), L10n.Tr("dialog.cancel")))
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(basePrefab);
            if (!MyAvatar.IsBasePath(path)) return false;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(contents)) return false;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return true;
        }

        private static void SetOnBase(FeatureContext ctx, GameObject basePrefab, RuntimeAnimatorController controller)
        {
            var conflicts = OwnWalkChildren(basePrefab);
            var ok = EditBase(basePrefab, "walk.confirmAll", controller.name, contents =>
            {
                if (Replacers(contents).Any(r => r.gameObject.name != CurrentObjectName))
                {
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.resetFirst"), L10n.Tr("dialog.ok"));
                    return false;
                }

                var walk = FindWalk(contents.transform);
                if (walk == null)
                {
                    var go = new GameObject(CurrentObjectName);
                    go.transform.SetParent(contents.transform, false);
                    walk = go.AddComponent<ModularAvatarMergeAnimator>();
                }

                Configure(walk, controller);
                return true;
            });
            if (!ok) return;

            var message = L10n.Tr("walk.doneAll", controller.name);
            if (conflicts.Count > 0) message += "\n\n" + L10n.Tr("walk.childConflicts", string.Join(", ", conflicts.Select(c => c.name).ToArray()));
            EditorUtility.DisplayDialog(L10n.Tr("app.name"), message, L10n.Tr("dialog.ok"));
            ctx.Rebuild();
        }

        private static void AddPrefabToBase(FeatureContext ctx, GameObject basePrefab, GameObject prefab)
        {
            var ok = EditBase(basePrefab, "walk.confirmAll", prefab.name, contents =>
            {
                if (Replacers(contents).Count > 0)
                {
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.resetFirst"), L10n.Tr("dialog.ok"));
                    return false;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab, contents.transform) as GameObject;
                return instance != null;
            });
            if (!ok) return;

            EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("walk.doneAll", prefab.name), L10n.Tr("dialog.ok"));
            ctx.Rebuild();
        }

        /// <summary>ベースから作った改変アバターのうち、自分で歩き方を入れているもの（ベースにも入れると重なる）。</summary>
        private static List<GameObject> OwnWalkChildren(GameObject basePrefab)
        {
            return MyAvatar.ChildrenOf(basePrefab)
                .Where(child => Replacers(child).Any(r => !IsFromBase(r.gameObject, basePrefab)))
                .ToList();
        }

        // ---------------------------------------------------------------- 元に戻す・重なりを直す

        private static void ResetWalk(FeatureContext ctx, GameObject basePrefab)
        {
            if (_scope == 1)
            {
                var ok = EditBase(basePrefab, "walk.confirmResetAll", "", contents =>
                {
                    var found = Replacers(contents).Select(r => r.gameObject).Where(g => g != contents).Distinct().ToList();
                    if (found.Count == 0) return false;
                    foreach (var g in found)
                    {
                        // ベースの中に入れた Prefab ごと外す（入れ子の中の部品だけを消すことはできないため、いちばん外側を消す）
                        var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(g);
                        Object.DestroyImmediate(outer != null && outer != contents ? outer : g);
                    }

                    return true;
                });
                if (ok) ctx.Rebuild();
                return;
            }

            var avatar = ctx.Avatar;
            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.SetCurrentGroupName(L10n.Tr("undo.walk"));
            var revertInherited = false;
            foreach (var r in Replacers(avatar.gameObject))
            {
                if (r == null) continue;
                if (basePrefab != null && IsFromBase(r.gameObject, basePrefab))
                {
                    // ベースから来たものは消せないので、このアバターでの上書きだけを取り消す（下でまとめて）
                    revertInherited = true;
                    continue;
                }

                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                var target = outer != null && outer != avatar.gameObject && outer.transform.IsChildOf(avatar.transform) ? outer : r.gameObject;
                if (target == avatar.gameObject) continue;
                Undo.DestroyObjectImmediate(target);
            }

            UndoHelper.Separate();
            if (revertInherited) RevertInheritedWalk(avatar, basePrefab);
            ctx.Rebuild();
        }

        /// <summary>
        /// ベースから来た歩き方への、このアバターでの上書きを取り消す。上書きは改変アバターのファイルに保存されているので、
        /// いったん保存してからファイルの中で取り消す（シーンのアバターも自動で追いつく）。
        /// </summary>
        private static void RevertInheritedWalk(VRCAvatarDescriptor avatar, GameObject basePrefab)
        {
            var scene = Replacers(avatar.gameObject).FirstOrDefault(r => IsFromBase(r.gameObject, basePrefab));
            if (scene != null) PrefabUtility.RevertObjectOverride(scene, InteractionMode.UserAction);
            MyAvatar.Save(avatar);

            var path = MyAvatar.PrefabPath(avatar);
            if (!MyAvatar.IsMinePath(path)) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var inFile = Replacers(contents).FirstOrDefault(r => IsFromBase(r.gameObject, basePrefab));
                if (inFile == null) return;
                PrefabUtility.RevertObjectOverride(inFile, InteractionMode.AutomatedAction);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>このアバターで足した歩き方を外し、ベースの歩き方をこのアバター用に書き換えて使う（重ならないように）。</summary>
        private static void FixConflict(FeatureContext ctx, GameObject basePrefab)
        {
            var avatar = ctx.Avatar;
            var replacers = Replacers(avatar.gameObject);
            var inherited = basePrefab == null ? null : replacers.FirstOrDefault(r => IsFromBase(r.gameObject, basePrefab));
            var own = replacers.Where(r => r != inherited).ToList();
            if (own.Count == 0) return;

            var undoName = L10n.Tr("undo.walk");
            ctx.BeginEdit();
            UndoHelper.Separate();
            var keep = own[0].animator;
            // ベースに歩き方が無ければ、自分で入れたもののうち最初の 1 つは残す
            if (inherited == null) own.RemoveAt(0);
            foreach (var r in own)
            {
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                var target = outer != null && outer != avatar.gameObject && outer.transform.IsChildOf(avatar.transform) ? outer : r.gameObject;
                Undo.DestroyObjectImmediate(target);
            }

            if (inherited != null && keep != null)
            {
                Undo.RecordObject(inherited, undoName);
                inherited.animator = keep;
                EditorUtility.SetDirty(inherited);
            }

            UndoHelper.Separate();
            ctx.Rebuild();
        }

        private static void Configure(ModularAvatarMergeAnimator merge, RuntimeAnimatorController controller)
        {
            merge.animator = controller;
            merge.layerType = CurrentLayer;
            merge.mergeAnimatorMode = MergeAnimatorMode.Replace;
            // 歩き方のアニメーションはアバターの根元からのパスで書かれているので、そのまま使う
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = false;
            merge.deleteAttachedAnimator = false;
            EditorUtility.SetDirty(merge);
        }
#endif
    }
}
