using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// かいへんナビのメインウィンドウ。
    /// 上から「アバターを選ぶ → やりたいことを選ぶ → 動かす」の一方向だけで完結させる。
    /// </summary>
    internal sealed class KaihenNaviWindow : EditorWindow
    {
        private const string MenuPath = "Tools/かいへんナビ";

        [SerializeField] private string _avatarId;
        [SerializeField] private string _featureId;

        private readonly BackupService _backup = new BackupService();
        private List<FeatureBase> _features;
        private List<VRCAvatarDescriptor> _avatars = new List<VRCAvatarDescriptor>();
        private VRCAvatarDescriptor _avatar;
        private FeatureBase _selected;

        private VisualElement _avatarArea;
        private VisualElement _cardsArea;
        private VisualElement _panelArea;
        private Label _backupLabel;
        private Button _restoreButton;
        private Button _historyButton;
        private bool _scanQueued;

        [MenuItem(MenuPath, false, 0)]
        public static void Open()
        {
            var window = GetWindow<KaihenNaviWindow>();
            window.minSize = new Vector2(360, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _features = FeatureCatalog.Create();
            L10n.Changed += RebuildAll;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += QueueScan;
            _backup.Changed += RefreshFooter;
        }

        private void OnDisable()
        {
            L10n.Changed -= RebuildAll;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= QueueScan;
            _backup.Changed -= RefreshFooter;
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
        }

        private void CreateGUI()
        {
            RebuildAll();
        }

        // ---------------------------------------------------------------- 画面の組み立て

        private void RebuildAll()
        {
            titleContent = new GUIContent(L10n.Tr("app.name"));

            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("kn-root");
            root.EnableInClassList("kn-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("kn-light", !EditorGUIUtility.isProSkin);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePaths.StyleSheet);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            root.Add(BuildHeader());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("kn-body");
            root.Add(scroll);

            scroll.Add(Ui.StepHeader("1", L10n.Tr("step.avatar")));
            _avatarArea = Ui.Box("kn-section");
            scroll.Add(_avatarArea);

            scroll.Add(Ui.StepHeader("2", L10n.Tr("step.feature")));
            _cardsArea = Ui.Box("kn-section", "kn-cards");
            scroll.Add(_cardsArea);

            _panelArea = Ui.Box("kn-section", "kn-panel");
            scroll.Add(_panelArea);

            root.Add(BuildFooter());

            ScanAvatars();
            if (_selected == null && !string.IsNullOrEmpty(_featureId))
            {
                _selected = _features.FirstOrDefault(f => f.Id == _featureId);
            }

            BuildAvatarArea();
            BuildCards();
            BuildPanel();
            RefreshFooter();
        }

        private VisualElement BuildHeader()
        {
            var header = Ui.Box("kn-header");

            var titles = Ui.Box("kn-header__titles");
            titles.Add(Ui.Text(L10n.Tr("app.name"), "kn-header__title"));
            titles.Add(Ui.Text(L10n.Tr("app.easyMode"), "kn-badge"));
            if (Edition.IsTrial) titles.Add(Ui.Text(L10n.Tr("app.trial"), "kn-badge", "kn-badge--trial"));
            header.Add(titles);

            var language = new DropdownField(L10n.DisplayNames.ToList(), L10n.CurrentIndex);
            language.AddToClassList("kn-language");
            language.tooltip = "Language";
            language.RegisterValueChangedCallback(e =>
            {
                var index = System.Array.IndexOf(L10n.DisplayNames, e.newValue);
                // 画面の作り直しはイベント処理が終わってから行う
                if (index >= 0) EditorApplication.delayCall += () => L10n.Current = L10n.Codes[index];
            });
            header.Add(language);
            return header;
        }

        private VisualElement BuildFooter()
        {
            var footer = Ui.Box("kn-footer");
            var info = Ui.Box("kn-footer__info");
            _backupLabel = Ui.Text("", "kn-footer__label");
            info.Add(_backupLabel);
            footer.Add(info);

            _historyButton = Ui.Button(L10n.Tr("footer.history"), ShowHistoryMenu, "kn-button--ghost");
            footer.Add(_historyButton);
            _restoreButton = Ui.Button(L10n.Tr("footer.restore"), RestoreLatest, "kn-button--primary");
            footer.Add(_restoreButton);
            return footer;
        }

        private void BuildAvatarArea()
        {
            _avatarArea.Clear();
            if (_avatars.Count == 0)
            {
                _avatarArea.Add(Ui.Notice(L10n.Tr("avatar.none"), "warn"));
                _avatarArea.Add(Ui.Button(L10n.Tr("avatar.rescan"), () =>
                {
                    ScanAvatars();
                    BuildAvatarArea();
                    BuildPanel();
                    RefreshFooter();
                }));
                return;
            }

            var names = AvatarLabels(_avatars);
            var index = Mathf.Max(0, _avatars.IndexOf(_avatar));
            var dropdown = new DropdownField(L10n.Tr("avatar.label"), names, index);
            dropdown.AddToClassList("kn-avatar-dropdown");
            dropdown.RegisterValueChangedCallback(e =>
            {
                var i = names.IndexOf(e.newValue);
                if (i >= 0) SelectAvatar(_avatars[i]);
            });
            _avatarArea.Add(dropdown);
        }

        private void BuildCards()
        {
            _cardsArea.Clear();
            foreach (var feature in _features)
            {
                var f = feature;
                var card = new Button(() => SelectFeature(f));
                card.AddToClassList("kn-card");
                if (f == _selected) card.AddToClassList("kn-card--selected");
                if (f.Locked) card.AddToClassList("kn-card--locked");

                var icon = Ui.Text(f.Icon, "kn-card__icon");
                icon.style.backgroundColor = f.Accent;
                card.Add(icon);

                var texts = Ui.Box("kn-card__texts");
                texts.Add(Ui.Text(f.Title, "kn-card__title"));
                texts.Add(Ui.Text(f.Description, "kn-card__desc"));
                card.Add(texts);

                if (f.Locked || !f.Implemented) card.AddToClassList("kn-card--badged");
                if (f.Locked) card.Add(Ui.Text(L10n.Tr("card.full"), "kn-card__badge", "kn-card__badge--lock"));
                else if (!f.Implemented) card.Add(Ui.Text(L10n.Tr("card.soon"), "kn-card__badge"));

                _cardsArea.Add(card);
            }
        }

        private void BuildPanel()
        {
            _panelArea.Clear();
            if (_selected == null)
            {
                _panelArea.Add(Ui.StepHeader("3", L10n.Tr("step.adjust")));
                _panelArea.Add(Ui.Notice(L10n.Tr("panel.chooseFeature"), "info"));
                return;
            }

            var header = Ui.StepHeader("3", _selected.Title);
            var refresh = Ui.Button(L10n.Tr("panel.refresh"), () => BuildPanel(), "kn-button--small", "kn-button--ghost");
            refresh.tooltip = L10n.Tr("panel.refreshTip");
            header.Add(refresh);
            _panelArea.Add(header);

            if (_selected.Locked)
            {
                _panelArea.Add(Ui.Notice(L10n.Tr("panel.locked"), "info"));
                _panelArea.Add(Ui.Button(L10n.Tr("panel.aboutFull"), () => Application.OpenURL(Edition.BoothUrl), "kn-button--primary"));
                return;
            }

            if (_avatar == null)
            {
                _panelArea.Add(Ui.Notice(L10n.Tr("panel.chooseAvatar"), "warn"));
                return;
            }

            var ctx = new FeatureContext
            {
                Avatar = _avatar,
                Backup = _backup,
                FeatureId = _selected.Id,
                Rebuild = BuildPanel,
            };

            try
            {
                _panelArea.Add(_selected.Build(ctx));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                _panelArea.Add(Ui.Notice(L10n.Tr("panel.error", e.Message), "warn"));
            }
        }

        private void RefreshFooter()
        {
            if (_backupLabel == null) return;
            var count = _avatar == null ? 0 : _backup.Count(_avatar);
            _backupLabel.text = L10n.Tr("footer.backups", count);
            _restoreButton.SetEnabled(count > 0);
            _historyButton.SetEnabled(count > 0);
        }

        // ---------------------------------------------------------------- 選択

        private void SelectAvatar(VRCAvatarDescriptor avatar)
        {
            if (avatar == _avatar) return;
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _avatar = avatar;
            _avatarId = avatar == null ? null : BackupService.AvatarId(avatar);
            BuildPanel();
            RefreshFooter();
        }

        private void SelectFeature(FeatureBase feature)
        {
            if (feature == _selected) return;
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _selected = feature;
            _featureId = feature.Id;
            BuildCards();
            BuildPanel();
        }

        // ---------------------------------------------------------------- アバターの検出

        private void ScanAvatars()
        {
            _avatars = AvatarFinder.FindInOpenScenes();

            VRCAvatarDescriptor next = null;
            if (_avatar != null && _avatars.Contains(_avatar)) next = _avatar;
            if (next == null && !string.IsNullOrEmpty(_avatarId))
            {
                next = _avatars.FirstOrDefault(a => BackupService.AvatarId(a) == _avatarId);
            }

            if (next == null && _avatars.Count > 0) next = _avatars[0];

            if (next != _avatar)
            {
                _backup.EndSession();
                _avatar = next;
            }

            _avatarId = _avatar == null ? null : BackupService.AvatarId(_avatar);
        }

        private void QueueScan()
        {
            if (_scanQueued) return;
            _scanQueued = true;
            EditorApplication.delayCall += () =>
            {
                _scanQueued = false;
                if (this == null || _avatarArea == null) return;

                var before = _avatars.ToList();
                var current = _avatar;
                ScanAvatars();

                // ツール自身の操作でも hierarchyChanged は起きるので、アバターの増減があったときだけ描き直す
                if (!before.SequenceEqual(_avatars)) BuildAvatarArea();
                if (current != _avatar)
                {
                    BuildPanel();
                    RefreshFooter();
                }
            };
        }

        private static List<string> AvatarLabels(List<VRCAvatarDescriptor> avatars)
        {
            var labels = new List<string>();
            foreach (var a in avatars)
            {
                var label = a.name;
                if (!a.gameObject.activeInHierarchy) label += " " + L10n.Tr("avatar.hidden");
                var n = 2;
                var unique = label;
                while (labels.Contains(unique)) unique = label + " (" + n++ + ")";
                labels.Add(unique);
            }

            return labels;
        }

        // ---------------------------------------------------------------- 元に戻す

        private void RestoreLatest()
        {
            if (_avatar == null) return;
            var list = _backup.List(_avatar);
            if (list.Count == 0) return;

            var latest = list[0];
            if (!EditorUtility.DisplayDialog(
                    L10n.Tr("restore.title"),
                    L10n.Tr("restore.message", latest.CreatedAt.ToString("M/d HH:mm"), FeatureTitle(latest.featureId)),
                    L10n.Tr("restore.ok"),
                    L10n.Tr("dialog.cancel")))
            {
                return;
            }

            if (_selected != null) _selected.OnClosed();
            _backup.Restore(_avatar, latest);
            BuildPanel();
            RefreshFooter();
        }

        private void ShowHistoryMenu()
        {
            if (_avatar == null) return;
            var menu = new GenericMenu();
            var list = _backup.List(_avatar);
            foreach (var snapshot in list)
            {
                var s = snapshot;
                var label = L10n.Tr("history.item", s.CreatedAt.ToString("M/d HH:mm:ss"), FeatureTitle(s.featureId));
                menu.AddItem(new GUIContent(label), false, () => RestoreFromHistory(s));
            }

            if (list.Count == 0) menu.AddDisabledItem(new GUIContent(L10n.Tr("history.empty")));
            menu.ShowAsContext();
        }

        private void RestoreFromHistory(BackupSnapshot snapshot)
        {
            if (!EditorUtility.DisplayDialog(
                    L10n.Tr("restore.title"),
                    L10n.Tr("restore.historyMessage", snapshot.CreatedAt.ToString("M/d HH:mm:ss"), FeatureTitle(snapshot.featureId)),
                    L10n.Tr("restore.ok"),
                    L10n.Tr("dialog.cancel")))
            {
                return;
            }

            if (_selected != null) _selected.OnClosed();
            _backup.Restore(_avatar, snapshot);
            BuildPanel();
            RefreshFooter();
        }

        private string FeatureTitle(string id)
        {
            var feature = _features.FirstOrDefault(f => f.Id == id);
            return feature == null ? id : feature.Title;
        }

        private void OnUndoRedo()
        {
            if (_panelArea == null) return;
            UndoHelper.Separate();
            BuildPanel();
            RefreshFooter();
        }
    }
}
