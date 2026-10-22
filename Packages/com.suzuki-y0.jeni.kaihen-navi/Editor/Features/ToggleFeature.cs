using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#if KAIHENNAVI_MA
using nadena.dev.modular_avatar.core;
#endif

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 2：服やアクセを脱ぎ着できるようにしたい。
    /// Modular Avatar があれば、MA のコンポーネントで脱ぎ着メニューを非破壊で作る
    /// （アバターの Animator やメニューのアセットは書き換えない）。
    /// MA が無い場合は、その場で表示・非表示を切り替えるだけにして、MA の導入を案内する。
    /// </summary>
    internal sealed class ToggleFeature : FeatureBase
    {
        public const string MenuObjectName = "KaihenNavi_Menu";
        private const string ParameterPrefix = "KaihenNavi/";
        private const string ModularAvatarUrl = "https://modular-avatar.nadena.dev/ja/";

        private sealed class Candidate
        {
            public GameObject Target;
            public int Depth;
            public string Label;
            public bool Picked;
            public bool IsOther;
            public bool InMenu = false;
        }

        private static bool _othersExpanded;

        // チェックと名前は画面を作り直しても覚えておく
        private static readonly Dictionary<GameObject, bool> Picks = new Dictionary<GameObject, bool>();
        private static readonly Dictionary<GameObject, string> Labels = new Dictionary<GameObject, string>();

#if KAIHENNAVI_MA
        // チェックを覚えたときのメニューの中身。メニューが作り直し・削除・「元に戻す」で変わったら、覚えたチェックは捨てて実際のメニューに合わせる
        private static string _picksMenuSignature = "";
#endif

        public override string Id { get { return "toggle"; } }
        public override int Number { get { return 2; } }
        public override Color Accent { get { return Ui.Hex("#6CC08B"); } }
        public override bool InTrial { get { return true; } }

        public override bool RefreshOnHierarchyChange { get { return true; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
#if KAIHENNAVI_MA
            SyncPicksWithMenu(ctx.Avatar);
#endif
            var candidates = FindCandidates(ctx.Avatar);

#if KAIHENNAVI_MA
            BuildWithModularAvatar(ctx, root, candidates);
#else
            BuildWithoutModularAvatar(ctx, root, candidates);
#endif
            return root;
        }

        // ---------------------------------------------------------------- 候補

        /// <summary>脱ぎ着の候補（メッシュと、メッシュを複数まとめた衣装フォルダ）。</summary>
        private static List<Candidate> FindCandidates(VRCAvatarDescriptor avatar)
        {
            var result = new List<Candidate>();
            var body = AvatarFinder.FindBody(avatar);
            var bones = AvatarFinder.HumanoidBones(avatar);
            var menuRoot = avatar.transform.Find(MenuObjectName);

            Walk(avatar.transform, avatar.transform, 0, body, bones, menuRoot, result);

            // 小物のメッシュ名は「L」「R.001」のように分かりにくいことが多いので、
            // 小物のまとまり（体の骨やアバター直下に付いている親）にメッシュが 1 つだけなら、その名前を表示名にする
            foreach (var c in result)
            {
                // まとまりを切り替えると中の髪・体も消えるので、子のメッシュも判定する。
                var group = AccessoryRoot(avatar.transform, c.Target.transform, bones);
                // 服・小物の言葉が名前にあれば服を優先する（Cloth_HairRibbon など）
                c.IsOther = !PartGuesser.LooksLikeCloth(c.Target.name) &&
                    (IsHairName(c.Target.name) ||
                     c.Target.GetComponentsInChildren<Renderer>(true)
                         .Any(r => IsOtherRenderer(r, body, avatar.VisemeSkinnedMesh)));
                if (Labels.ContainsKey(c.Target)) continue;
                if (group == c.Target.transform) continue;
                var meshes = group.GetComponentsInChildren<Renderer>(true)
                    .Count(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r != body);
                if (meshes == 1) c.Label = group.name;
            }

            return result;
        }

        private static bool IsHairName(string name)
        {
            var lower = name.ToLowerInvariant();
            return new[] { "hair", "kami", "bang", "fringe", "ponytail", "twintail", "髪", "アホ毛" }
                .Any(lower.Contains);
        }

        private static bool IsOtherRenderer(Renderer renderer, Renderer body, Renderer face)
        {
            if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) return false;
            // 名前や服のマテリアルより、素体・顔への直接の参照を優先する。
            if (renderer == body || renderer == face) return true;
            // マテリアル名は服と素体で共有されていることが多いので、メッシュの名前だけで判定する
            if (PartGuesser.LooksLikeCloth(renderer.name)) return false;
            return IsOtherPart(PartGuesser.Guess(renderer, null, body));
        }

        private static bool IsOtherPart(BodyPart part)
        {
            return part == BodyPart.Hair || part == BodyPart.Skin ||
                part == BodyPart.Eye || part == BodyPart.Other;
        }

        /// <summary>同じ候補を振り分けるだけにして、チェックや名前の変更を共有する。</summary>
        private static VisualElement BuildGroupedList(List<Candidate> candidates,
            System.Func<List<Candidate>, VisualElement> buildList)
        {
            var groups = new VisualElement();
            groups.Add(Ui.Heading(L10n.Tr("toggle.clothes")));
            groups.Add(buildList(candidates.Where(c => !c.IsOther).ToList()));
            var others = candidates.Where(c => c.IsOther).ToList();
            var foldout = new Foldout
            {
                text = L10n.Tr("toggle.others", others.Count),
                value = _othersExpanded,
            };
            foldout.RegisterValueChangedCallback(e =>
            {
                // 子のチェック変更も伝わるため、折りたたみ自身の変更だけを覚える。
                if (e.target == foldout) _othersExpanded = e.newValue;
            });
            foldout.Add(buildList(others));
            groups.Add(foldout);
            return groups;
        }

        /// <summary>体の骨かアバター直下に付いている、いちばん上の親。</summary>
        private static Transform AccessoryRoot(Transform avatarRoot, Transform t, HashSet<Transform> bones)
        {
            while (t.parent != null && t.parent != avatarRoot && !bones.Contains(t.parent)) t = t.parent;
            return t;
        }

        private static void Walk(Transform avatarRoot, Transform parent, int depth, Renderer body,
            HashSet<Transform> bones, Transform menuRoot, List<Candidate> result)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var t = parent.GetChild(i);
                if (t == menuRoot) continue;

                var childDepth = depth;
                var renderer = t.GetComponent<Renderer>();
                var isMesh = renderer is SkinnedMeshRenderer || renderer is MeshRenderer;

                if (isMesh)
                {
                    Add(result, t.gameObject, depth);
                    childDepth = depth + 1;
                }
                else if (!isMesh && parent == avatarRoot && IsOutfitFolder(t, body, bones))
                {
                    Add(result, t.gameObject, depth);
                    childDepth = depth + 1;
                }

                Walk(avatarRoot, t, childDepth, body, bones, menuRoot, result);
            }
        }

        /// <summary>アバター直下にある「メッシュを 2 つ以上含み、素体や体の骨を含まない」フォルダ。</summary>
        private static bool IsOutfitFolder(Transform t, Renderer body, HashSet<Transform> bones)
        {
            var meshes = t.GetComponentsInChildren<Renderer>(true)
                .Count(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r != body);
            if (meshes < 2) return false;
            if (body != null && body.transform.IsChildOf(t)) return false;
            return !t.GetComponentsInChildren<Transform>(true).Any(bones.Contains);
        }

        private static void Add(List<Candidate> result, GameObject target, int depth)
        {
            bool picked;
            string label;
            result.Add(new Candidate
            {
                Target = target,
                Depth = depth,
                Picked = Picks.TryGetValue(target, out picked) && picked,
                Label = Labels.TryGetValue(target, out label) ? label : target.name,
            });
        }

        private static VisualElement BuildList(List<Candidate> candidates, bool editableLabel, System.Action onPickChanged = null)
        {
            var list = Ui.Box("kn-list");
            foreach (var candidate in candidates)
            {
                var c = candidate;
                var row = Ui.Box("kn-list__row");
                row.style.paddingLeft = 6 + c.Depth * 16;

                var toggle = new Toggle { value = c.Picked };
                toggle.AddToClassList("kn-list__toggle");
                toggle.RegisterValueChangedCallback(e =>
                {
                    c.Picked = e.newValue;
                    Picks[c.Target] = e.newValue;
                    if (onPickChanged != null) onPickChanged();
                });
                row.Add(toggle);

                var texts = Ui.Box("kn-list__texts");
                if (editableLabel)
                {
                    var field = new TextField { value = c.Label };
                    field.AddToClassList("kn-list__field");
                    field.tooltip = L10n.Tr("toggle.labelTip");
                    field.RegisterValueChangedCallback(e =>
                    {
                        c.Label = e.newValue;
                        Labels[c.Target] = e.newValue;
                    });
                    texts.Add(field);
                }
                else
                {
                    texts.Add(Ui.Text(c.Target.name, "kn-list__title"));
                }

                var state = c.Target.activeSelf ? L10n.Tr("toggle.stateVisible") : L10n.Tr("toggle.stateHidden");
                if (c.InMenu) state += "  ・  " + L10n.Tr("toggle.inMenu");
                // 表示名を変えたものは、元のオブジェクト名も添えて見分けられるようにする
                if (editableLabel && c.Label != c.Target.name) state += "  ・  " + c.Target.name;
                texts.Add(Ui.Text(state, "kn-list__sub"));
                row.Add(texts);

                var ping = Ui.Button(L10n.Tr("common.where"), () =>
                {
                    EditorGUIUtility.PingObject(c.Target);
                    Selection.activeGameObject = c.Target;
                }, "kn-button--small", "kn-button--ghost");
                row.Add(ping);

                list.Add(row);
            }

            return list;
        }

        /// <summary>パラメータ名（OSC などで困らないよう英数字だけにする）。</summary>
        private static string ParameterName(string label, HashSet<string> used)
        {
            var safe = new string(label.Where(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')).ToArray());
            if (safe.Length == 0) safe = "Item";
            var name = ParameterPrefix + safe;
            var n = 2;
            while (used.Contains(name)) name = ParameterPrefix + safe + "_" + n++;
            used.Add(name);
            return name;
        }

        private static int AvatarParameterCost(VRCAvatarDescriptor avatar)
        {
            var parameters = avatar.expressionParameters;
            return parameters == null ? 0 : parameters.CalcTotalCost();
        }

#if KAIHENNAVI_MA
        // ---------------------------------------------------------------- MA あり

        private sealed class ExistingToggle
        {
            public GameObject Item;
            public GameObject Target;
            public bool IsDefault;
        }

        private static void BuildWithModularAvatar(FeatureContext ctx, VisualElement root, List<Candidate> candidates)
        {
            var existing = ReadExisting(ctx.Avatar);
            foreach (var e in existing)
            {
                var candidate = candidates.FirstOrDefault(c => c.Target == e.Target);
                if (candidate == null) continue;
                candidate.InMenu = true;
                if (!Picks.ContainsKey(e.Target)) candidate.Picked = true;
                if (!Labels.ContainsKey(e.Target)) candidate.Label = e.Item.name;
            }

            root.Add(Ui.Notice(L10n.Tr("toggle.intro"), "info"));

            if (candidates.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("toggle.none"), "warn"));
                return;
            }

            // 同期パラメータの消費量（1 項目 1 bit）。チェックを変えるたびに数え直す
            var baseCost = AvatarParameterCost(ctx.Avatar);
            var max = VRCExpressionParameters.MAX_PARAMETER_COST;
            var bits = Ui.Text("", "kn-bits");
            var over = Ui.Notice(L10n.Tr("toggle.bitsOver"), "warn");
            System.Action updateBits = () =>
            {
                var picked = candidates.Count(c => c.Picked);
                bits.text = L10n.Tr("toggle.bits", baseCost, picked, max);
                over.style.display = baseCost + picked > max ? DisplayStyle.Flex : DisplayStyle.None;
            };

            root.Add(Ui.Heading(L10n.Tr("toggle.pick")));
            root.Add(BuildGroupedList(candidates, group => BuildList(group, true, updateBits)));
            root.Add(bits);
            root.Add(over);
            updateBits();

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(
                existing.Count > 0 ? L10n.Tr("toggle.rebuild") : L10n.Tr("toggle.create"),
                () =>
                {
                    var targets = candidates.Where(c => c.Picked).ToList();
                    if (targets.Count == 0)
                    {
                        EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("toggle.nothingPicked"), L10n.Tr("dialog.ok"));
                        return;
                    }

                    CreateMenu(ctx, targets);
                    EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("toggle.done", targets.Count), L10n.Tr("dialog.ok"));
                    ctx.Rebuild();
                },
                "kn-button--primary"));

            if (existing.Count > 0)
            {
                buttons.Add(Ui.Button(L10n.Tr("toggle.remove"), () =>
                {
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("toggle.removeConfirm"),
                            L10n.Tr("toggle.remove"), L10n.Tr("dialog.cancel"))) return;
                    ctx.BeginEdit();
                    UndoHelper.Separate();
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(L10n.Tr("undo.toggleRemove"));
                    RemoveMenu(ctx.Avatar, L10n.Tr("undo.toggleRemove"));
                    ctx.Rebuild();
                }));
            }

            root.Add(buttons);
            root.Add(Ui.Notice(L10n.Tr("toggle.howToUse"), "ok"));
        }

        private static void SyncPicksWithMenu(VRCAvatarDescriptor avatar)
        {
            var signature = string.Join("|", ReadExisting(avatar)
                .Select(e => e.Target.GetInstanceID() + ":" + e.Item.name).ToArray());
            if (signature == _picksMenuSignature) return;
            Picks.Clear();
            Labels.Clear();
            _picksMenuSignature = signature;
        }

        private static List<ExistingToggle> ReadExisting(VRCAvatarDescriptor avatar)
        {
            var result = new List<ExistingToggle>();
            var menuRoot = avatar.transform.Find(MenuObjectName);
            if (menuRoot == null) return result;

            foreach (var item in menuRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.transform == menuRoot) continue;
                var toggle = item.GetComponent<ModularAvatarObjectToggle>();
                if (toggle == null || toggle.Objects == null) continue;
                foreach (var obj in toggle.Objects)
                {
                    if (obj.Object == null) continue;
                    var target = obj.Object.Get(toggle);
                    if (target == null) continue;
                    result.Add(new ExistingToggle { Item = item.gameObject, Target = target, IsDefault = item.isDefault });
                }
            }

            return result;
        }

        /// <summary>前回作ったメニューを消し、対象の表示状態を作る前に戻す。</summary>
        private static void RemoveMenu(VRCAvatarDescriptor avatar, string undoName)
        {
            foreach (var e in ReadExisting(avatar))
            {
                if (e.Target.activeSelf == e.IsDefault) continue;
                Undo.RecordObject(e.Target, undoName);
                e.Target.SetActive(e.IsDefault);
            }

            var menuRoot = avatar.transform.Find(MenuObjectName);
            if (menuRoot != null) Undo.DestroyObjectImmediate(menuRoot.gameObject);
        }

        private static void CreateMenu(FeatureContext ctx, List<Candidate> targets)
        {
            var avatar = ctx.Avatar;
            var undoName = L10n.Tr("undo.toggle");

            ctx.BeginEdit();
            UndoHelper.Separate();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            RemoveMenu(avatar, undoName);

            var used = new HashSet<string>();
            var data = new MenuData { menuName = L10n.Tr("toggle.menuName") };
            foreach (var c in targets)
            {
                var label = string.IsNullOrEmpty(c.Label) ? c.Target.name : c.Label;
                data.items.Add(new MenuItemData
                {
                    label = label,
                    parameter = ParameterName(label, used),
                    targetPath = HierarchyPath.Get(avatar.transform, c.Target.transform),
                    // 今見えているものは最初から ON（着ている状態）にする
                    isDefault = c.Target.activeSelf,
                });
            }

            var menuRoot = BuildMenu(avatar, data, undoName);

            // メニューが ON のときだけ表示する。初期状態は isDefault で決まる
            foreach (var c in targets)
            {
                if (!c.Target.activeSelf) continue;
                Undo.RecordObject(c.Target, undoName);
                c.Target.SetActive(false);
            }

            ctx.NoteCreated(menuRoot);
            Undo.CollapseUndoOperations(group);
            EditorGUIUtility.PingObject(menuRoot);
        }

        /// <summary>
        /// MA のコンポーネントでメニューを組み立てる。対象の表示状態は変えない。
        /// バックアップからの復元でも使う。
        /// </summary>
        internal static GameObject BuildMenu(VRCAvatarDescriptor avatar, MenuData data, string undoName)
        {
            // 親：Expressions メニューに「脱ぎ着」サブメニューを追加する
            var menuRoot = new GameObject(MenuObjectName);
            menuRoot.transform.SetParent(avatar.transform, false);
            Undo.RegisterCreatedObjectUndo(menuRoot, undoName);
            Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot);
            var submenu = Undo.AddComponent<ModularAvatarMenuItem>(menuRoot);
            submenu.Control = new VRCExpressionsMenu.Control
            {
                name = data.menuName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            };
            submenu.MenuSource = SubmenuSource.Children;

            foreach (var spec in data.items)
            {
                var target = HierarchyPath.Find(avatar.transform, spec.targetPath);
                if (target == null) continue;

                var itemObject = new GameObject(spec.label);
                itemObject.transform.SetParent(menuRoot.transform, false);
                Undo.RegisterCreatedObjectUndo(itemObject, undoName);

                var item = Undo.AddComponent<ModularAvatarMenuItem>(itemObject);
                item.Control = new VRCExpressionsMenu.Control
                {
                    name = spec.label,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = spec.parameter },
                    value = 1f,
                };
                item.isSynced = true;
                item.isSaved = true;
                item.isDefault = spec.isDefault;

                var reference = new AvatarObjectReference();
                reference.Set(target.gameObject);
                var toggle = Undo.AddComponent<ModularAvatarObjectToggle>(itemObject);
                toggle.Objects = new List<ToggledObject>
                {
                    new ToggledObject { Object = reference, Active = true },
                };
            }

            return menuRoot;
        }

        /// <summary>今あるメニューを MenuData にする（バックアップ用）。メニューが無ければ null。</summary>
        internal static MenuData ReadMenu(VRCAvatarDescriptor avatar)
        {
            var menuRoot = avatar.transform.Find(MenuObjectName);
            if (menuRoot == null) return null;

            var data = new MenuData();
            var rootItem = menuRoot.GetComponent<ModularAvatarMenuItem>();
            if (rootItem != null && rootItem.Control != null) data.menuName = rootItem.Control.name;

            foreach (var item in menuRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.transform == menuRoot) continue;
                var toggle = item.GetComponent<ModularAvatarObjectToggle>();
                if (toggle == null || toggle.Objects == null || toggle.Objects.Count == 0) continue;
                var reference = toggle.Objects[0].Object;
                var target = reference == null ? null : reference.Get(toggle);
                if (target == null) continue;

                data.items.Add(new MenuItemData
                {
                    label = item.gameObject.name,
                    parameter = item.Control != null && item.Control.parameter != null ? item.Control.parameter.name : "",
                    targetPath = HierarchyPath.Get(avatar.transform, target.transform),
                    isDefault = item.isDefault,
                });
            }

            return data;
        }
#else
        // ---------------------------------------------------------------- MA なし

        private static void BuildWithoutModularAvatar(FeatureContext ctx, VisualElement root, List<Candidate> candidates)
        {
            root.Add(Ui.Notice(L10n.Tr("toggle.needMa"), "warn"));
            root.Add(Ui.Button(L10n.Tr("toggle.openMa"), () => Application.OpenURL(ModularAvatarUrl), "kn-button--primary"));

            root.Add(Ui.Heading(L10n.Tr("toggle.quickTitle")));
            root.Add(Ui.Notice(L10n.Tr("toggle.quickNote"), "info"));
            if (candidates.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("toggle.none"), "warn"));
                return;
            }

            root.Add(BuildGroupedList(candidates, group => BuildVisibilityList(ctx, group)));
        }

        private static VisualElement BuildVisibilityList(FeatureContext ctx, List<Candidate> candidates)
        {
            var list = Ui.Box("kn-list");
            foreach (var candidate in candidates)
            {
                var c = candidate;
                var row = Ui.Box("kn-list__row");
                row.style.paddingLeft = 6 + c.Depth * 16;
                var toggle = new Toggle { value = c.Target.activeSelf };
                toggle.AddToClassList("kn-list__toggle");
                toggle.RegisterValueChangedCallback(e =>
                {
                    var undoName = L10n.Tr("undo.visibility");
                    ctx.BeginEdit();
                    UndoHelper.Separate();
                    Undo.RecordObject(c.Target, undoName);
                    c.Target.SetActive(e.newValue);
                });
                row.Add(toggle);
                row.Add(Ui.Text(c.Target.name, "kn-list__title"));
                list.Add(row);
            }

            return list;
        }
#endif
    }

    /// <summary>脱ぎ着メニューの中身（バックアップに JSON で保存する）。</summary>
    [System.Serializable]
    internal sealed class MenuData
    {
        public string menuName = "";
        public List<MenuItemData> items = new List<MenuItemData>();
    }

    [System.Serializable]
    internal sealed class MenuItemData
    {
        public string label = "";
        public string parameter = "";
        public string targetPath = "";
        public bool isDefault = false;
    }

    /// <summary>
    /// 脱ぎ着メニューをバックアップに含める。メニューを作り直したあとで「元に戻す」と、前のメニューが復元される。
    /// </summary>
    internal sealed class ToggleMenuBackup : IBackupParticipant
    {
        public string Key { get { return "toggle-menu"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
#if KAIHENNAVI_MA
            var data = ToggleFeature.ReadMenu(avatar);
            return data == null ? null : JsonUtility.ToJson(data);
#else
            return null;
#endif
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
#if KAIHENNAVI_MA
            if (string.IsNullOrEmpty(json)) return;
            if (avatar.transform.Find(ToggleFeature.MenuObjectName) != null) return; // まだ残っていればそのまま
            var data = JsonUtility.FromJson<MenuData>(json);
            if (data != null) ToggleFeature.BuildMenu(avatar, data, undoName);
#endif
        }
    }
}
