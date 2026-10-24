using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// かいへんナビのメインウィンドウ。
    /// アバター帯・機能ナビゲーション・操作ページを持つ Editor 専用の画面。
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
        private VisualElement _navigation;
        private VisualElement _workspace;
        private VisualElement _chooserArea;
        private VisualElement _chooserScroll;
        private VisualElement _footer;
        private VisualElement _compactArea;
        private PopupField<string> _featurePopup;
        private List<FeatureBase> _navigationFeatures;
        private List<string> _featureChoices;
        private VisualElement _pageHeader;
        private VisualElement _avatarActions;
        private VisualElement _avatarAdvice;
        private bool _compact;
        private VisualElement _panelArea;
        private Label _backupLabel;
        private Button _restoreButton;
        private Button _historyButton;
        private bool _scanQueued;
        private ulong _panelHierarchyHash;
        private readonly Dictionary<FeatureBase, VisualElement> _navigationItems = new Dictionary<FeatureBase, VisualElement>();

        // 自分用アバターの案内と自動保存
        private VisualElement _avatarStatus;
        private Label _saveLabel;
        private VisualElement _checkArea;
        private IVisualElementScheduledItem _autoSave;
        private int _seenGroup = -1;
        private double _seenAt;
        private int _savedGroup = -1;
        private System.DateTime _lastSavedAt;
        private bool _lastSaveManual;
        private const double AutoSaveDelay = 1.5;

        // アバター選択の二択（新しく作る／前に作ったものを直す）
        private readonly AvatarChooser _chooser = new AvatarChooser();
        private bool _choosing;

        // 「あとで」を押したアバター（エディタを開いている間だけ覚える）
        private static readonly HashSet<string> Postponed = new HashSet<string>();

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
            KonoAssetLink.Changed += QueueKonoRefresh;
            ItemLedger.Changed += QueueKonoRefresh;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += QueueScan;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            _backup.Changed += RefreshFooter;
        }

        private void OnDisable()
        {
            L10n.Changed -= RebuildAll;
            KonoAssetLink.Changed -= QueueKonoRefresh;
            ItemLedger.Changed -= QueueKonoRefresh;
            EditorApplication.delayCall -= RefreshKono;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= QueueScan;
            EditorApplication.delayCall -= RunQueuedScan;
            _scanQueued = false;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            _backup.Changed -= RefreshFooter;
            SaveNow();
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
        }

        private void QueueKonoRefresh()
        {
            EditorApplication.delayCall -= RefreshKono;
            EditorApplication.delayCall += RefreshKono;
        }

        private void RefreshKono() { RebuildAll(); }

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
            FontLoader.Apply(root, L10n.Current);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePaths.StyleSheet);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            _avatarArea = Ui.Box("kn-avatar-area");
            root.Add(_avatarArea);

            var chooserScroll = new ScrollView(ScrollViewMode.Vertical);
            chooserScroll.AddToClassList("kn-chooser-scroll");
            _chooserScroll = chooserScroll;
            _chooserArea = Ui.Box("kn-chooser");
            chooserScroll.Add(_chooserArea);
            root.Add(chooserScroll);

            _workspace = Ui.Box("kn-workspace");
            var navigation = new ScrollView(ScrollViewMode.Vertical);
            navigation.AddToClassList("kn-navigation");
            _navigation = navigation;
            _workspace.Add(navigation);
            var page = Ui.Box("kn-page");
            _compactArea = Ui.Box("kn-compact");
            page.Add(_compactArea);
            _pageHeader = Ui.Box("kn-page-header");
            page.Add(_pageHeader);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("kn-page-scroll");
            _panelArea = Ui.Box("kn-panel");
            scroll.Add(_panelArea);
            page.Add(scroll);
            _workspace.Add(page);
            root.Add(_workspace);
            _footer = BuildFooter();
            root.Add(_footer);

            // root は RebuildAll でも再利用するため、コールバックを重複登録しない。
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            ApplyResponsiveLayout(root.layout.width);

            if (_autoSave != null) _autoSave.Pause();
            _autoSave = root.schedule.Execute(AutoSaveTick).Every(1000);

            ScanAvatars();
            if (_selected == null && !string.IsNullOrEmpty(_featureId))
            {
                _selected = _features.FirstOrDefault(f => f.Id == _featureId);
            }

            BuildAvatarArea();
            BuildNavigation();
            BuildPanel();
            RefreshFooter();
        }

        private VisualElement BuildLanguage()
        {
            var language = new DropdownField(L10n.DisplayNames.ToList(), L10n.CurrentIndex);
            language.AddToClassList("kn-language");
            language.tooltip = L10n.Tr("nav.language");
            language.RegisterValueChangedCallback(e =>
            {
                var index = System.Array.IndexOf(L10n.DisplayNames, e.newValue);
                if (index >= 0) EditorApplication.delayCall += () => L10n.Current = L10n.Codes[index];
            });
            return language;
        }

        private void OnGeometryChanged(GeometryChangedEvent e)
        {
            ApplyResponsiveLayout(e.newRect.width);
        }

        private void ApplyResponsiveLayout(float width)
        {
            var changed = _compact != (width < 560);
            _compact = width < 560;
            rootVisualElement.EnableInClassList("kn-compact-window", _compact);
            if (_navigation != null) _navigation.style.display = _compact ? DisplayStyle.None : DisplayStyle.Flex;
            if (_compactArea != null) _compactArea.style.display = _compact ? DisplayStyle.Flex : DisplayStyle.None;
            if (changed && _selected == null && _panelArea != null) BuildPanel();
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
            _restoreButton = Ui.Button(L10n.Tr("footer.restore"), RestoreLatest, "kn-button--ghost");
            footer.Add(_restoreButton);
            return footer;
        }

        private void BuildAvatarArea()
        {
            _avatarArea.Clear();
            _chooserArea.Clear();
            _avatarStatus = null;
            _avatarActions = null;
            _avatarAdvice = null;
            _saveLabel = null;
            _checkArea = null;

            var choosing = _avatar == null || _choosing;
            _workspace.style.display = choosing ? DisplayStyle.None : DisplayStyle.Flex;
            _chooserScroll.style.display = choosing ? DisplayStyle.Flex : DisplayStyle.None;
            _footer.style.display = choosing ? DisplayStyle.None : DisplayStyle.Flex;
            if (choosing)
            {
                var header = Ui.Box("kn-header");
                header.Add(Ui.Text(L10n.Tr("app.name"), "kn-header__title"));
                header.Add(BuildLanguage());
                _avatarArea.Add(header);
                _chooserArea.Add(Ui.Text(L10n.Tr("step.avatar"), "kn-page-title"));
                _chooser.Picked = OnAvatarPicked;
                _chooser.Changed = BuildAvatarArea;
                _chooser.Cancel = _avatar == null ? (System.Action)null : () =>
                {
                    _choosing = false;
                    BuildAvatarArea();
                };
                _chooserArea.Add(_chooser.Build());
                return;
            }

            var current = Ui.Box("kn-current");
            var texts = Ui.Box("kn-current__texts");
            texts.Add(Ui.Text(L10n.Tr("current.label"), "kn-hint"));
            texts.Add(Ui.Text(_avatar.name, "kn-current__name"));
            _avatarStatus = Ui.Box("kn-status-chips");
            texts.Add(_avatarStatus);
            current.Add(texts);
            _avatarActions = Ui.Box("kn-avatar-actions");
            current.Add(_avatarActions);
            _avatarArea.Add(current);
            _avatarAdvice = Ui.Box("kn-avatar-advice");
            _avatarArea.Add(_avatarAdvice);
            RefreshAvatarStatus();
            _checkArea = Ui.Box("kn-upload-check");
            _avatarArea.Add(_checkArea);
            RefreshUploadCheck();
        }

        private void OnAvatarPicked(VRCAvatarDescriptor avatar)
        {
            _choosing = false;
            ScanAvatars();
            if (avatar != _avatar) SelectAvatar(avatar);
            BuildAvatarArea();
            BuildPanel();
            RefreshFooter();
        }

        // ---------------------------------------------------------------- 自分用アバター

        /// <summary>アバター帯の状態チップと操作を更新する。</summary>
        private void RefreshAvatarStatus()
        {
            if (_avatarStatus == null || _avatarActions == null) return;
            _avatarStatus.Clear();
            _avatarActions.Clear();
            _avatarAdvice.Clear();
            _saveLabel = null;
            if (_avatar == null) return;

            _avatarStatus.Add(Ui.Text(L10n.Tr(Edition.IsTrial ? "app.trial" : "nav.full"), "kn-status-chip"));
            _avatarActions.Add(Ui.Button(L10n.Tr("nav.changeAvatar"), () =>
            {
                SaveNow();
                _choosing = true;
                _chooser.CurrentMode = AvatarChooser.Mode.Start;
                BuildAvatarArea();
            }, "kn-button--small", "kn-button--ghost"));

            var kind = MyAvatar.KindOf(_avatar);
            if (kind == AvatarKind.Mine)
            {
                _saveLabel = Ui.Text("", "kn-status-chip");
                _avatarStatus.Add(_saveLabel);
                UpdateSaveLabel();
                _avatarActions.Add(Ui.Button(L10n.Tr("mine.saveNow"), () =>
                {
                    _savedGroup = Undo.GetCurrentGroup();
                    SaveNow(true);
                }, "kn-button--small"));
                var path = MyAvatar.PrefabPath(_avatar);
                _avatarActions.Add(Ui.Button(L10n.Tr("common.where"), () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }, "kn-button--small", "kn-button--ghost"));
            }
            else
            {
                var postponed = kind == AvatarKind.Original && Postponed.Contains(_avatarId);
                var message = kind == AvatarKind.Original ? (postponed ? "mine.notYet" : "mine.suggest") : "mine.unlinked";
                var chip = Ui.Text(L10n.Tr("nav.sceneOnly"), "kn-status-chip");
                chip.tooltip = L10n.Tr(message);
                _avatarStatus.Add(chip);
                // 詳しい説明は折りたたみ、通常の帯の高さを保つ。
                var advice = new Foldout { text = L10n.Tr("nav.saveAdvice"), value = false };
                advice.Add(Ui.Notice(L10n.Tr(message), "info"));
                var buttons = Ui.Box("kn-row");
                buttons.Add(Ui.Button(L10n.Tr(kind == AvatarKind.Original ? "mine.create" : "mine.createUnlinked"), CreateMine, "kn-button--small"));
                if (kind == AvatarKind.Original && !postponed)
                    buttons.Add(Ui.Button(L10n.Tr("mine.later"), () =>
                    {
                        Postponed.Add(_avatarId);
                        RefreshAvatarStatus();
                    }, "kn-button--small", "kn-button--ghost"));
                advice.Add(buttons);
                _avatarAdvice.Add(advice);
            }

            var id = MyAvatar.BlueprintId(_avatar.gameObject);
            var upload = Ui.Text(L10n.Tr(id.Length == 0 ? "nav.uploadNew" : "nav.uploadUpdate"), "kn-status-chip");
            upload.tooltip = id.Length == 0 ? L10n.Tr("upload.new") : L10n.Tr("upload.update", id);
            _avatarStatus.Add(upload);
            if (id.Length > 0)
            {
                _avatarActions.Add(Ui.Button(L10n.Tr("upload.makeNew"), () =>
                {
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("upload.makeNewConfirm"),
                            L10n.Tr("upload.makeNew"), L10n.Tr("dialog.cancel"))) return;
                    MyAvatar.ClearBlueprintId(_avatar.gameObject);
                    SaveNow();
                    RefreshAvatarStatus();
                    RefreshUploadCheck();
                }, "kn-button--small", "kn-button--ghost"));
            }
            _avatarActions.Add(BuildLanguage());
        }

        // ---------------------------------------------------------------- アップロード前のチェック

        /// <summary>
        /// SDK がアップロードを止めるモデルの読み込み設定（Read/Write など）を先に見つけて、アバター帯の直下に出す。
        /// 直すと配布物の読み込み設定（.meta）が変わるので、必ず確認を出す。
        /// </summary>
        private void RefreshUploadCheck()
        {
            if (_checkArea == null) return;
            _checkArea.Clear();
            if (_avatar == null) return;

            var issues = UploadCheck.Find(_avatar);
            var same = MyAvatar.SameUploadTarget(_avatar);
            if (same.Count > 0)
                _checkArea.Add(Ui.Notice(L10n.Tr("upload.conflict", string.Join("、", same.Select(p => p.name).ToArray())), "warn"));
            if (issues.Count == 0) return;

            var notice = Ui.Notice(L10n.Tr("check.attention", issues.Count), "warn");
            notice.tooltip = UploadCheck.Describe(issues);
            _checkArea.Add(notice);
            var fixableIssues = issues.Where(i => i.CanFix).ToList();
            if (fixableIssues.Count == 0) return;
            notice.Add(Ui.Button(L10n.Tr("check.fix"), () =>
            {
                if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.confirm", UploadCheck.Describe(fixableIssues)),
                        L10n.Tr("check.fix"), L10n.Tr("dialog.cancel"))) return;
                var fixedCount = UploadCheck.Fix(fixableIssues);
                RefreshUploadCheck();
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.fixed", fixedCount), L10n.Tr("dialog.ok"));
            }, "kn-button--small", "kn-button--ghost"));
        }

        private void CreateMine()
        {
            if (_avatar == null) return;
            if (_selected != null) _selected.OnClosed();
            UndoHelper.Separate();

            var path = MyAvatar.Create(_avatar, _backup);
            if (path == null)
            {
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("mine.failed"), L10n.Tr("dialog.ok"));
                return;
            }

            _avatarId = BackupService.AvatarId(_avatar);
            _lastSavedAt = System.DateTime.Now;
            _savedGroup = Undo.GetCurrentGroup();
            EditorUtility.DisplayDialog(L10n.Tr("app.name"),
                L10n.Tr("mine.created", System.IO.Path.GetFileNameWithoutExtension(path)), L10n.Tr("dialog.ok"));

            RefreshAvatarStatus();
            RefreshUploadCheck();
            BuildPanel();
            RefreshFooter();
        }

        private void UpdateSaveLabel()
        {
            if (_saveLabel == null) return;
            _saveLabel.text = _lastSavedAt == default(System.DateTime)
                ? L10n.Tr("nav.autosave")
                : L10n.Tr(_lastSaveManual ? "nav.savedManually" : "nav.savedAt", _lastSavedAt.ToString("HH:mm:ss"));
        }

        /// <summary>
        /// 操作が 1.5 秒止まったら、自分用アバターに保存する。
        /// Undo のグループ番号を「何か変わった」目印にしているので、何も触っていない間は保存しない。
        /// </summary>
        private void AutoSaveTick()
        {
            if (_avatar == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var group = Undo.GetCurrentGroup();
            var now = EditorApplication.timeSinceStartup;
            if (group != _seenGroup)
            {
                _seenGroup = group;
                _seenAt = now;
                return;
            }

            if (group == _savedGroup || now - _seenAt < AutoSaveDelay) return;
            _savedGroup = group;
            SaveNow();
        }

        /// <summary>今すぐ保存する（ウィンドウを閉じる・シーンを閉じる・Play の前など）。</summary>
        /// <param name="manual">「今すぐ保存」ボタンから呼んだとき true（表示を「保存しました」にする）。</param>
        private void SaveNow(bool manual = false)
        {
            if (_avatar == null) return;
            MyAvatar.SaveMaterials(_avatar);
            if (!MyAvatar.Save(_avatar)) return;
            _lastSavedAt = System.DateTime.Now;
            _lastSaveManual = manual;
            UpdateSaveLabel();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) SaveNow();
        }

        private void OnSceneClosing(UnityEngine.SceneManagement.Scene scene, bool removingScene)
        {
            SaveNow();
        }

        private void BuildNavigation()
        {
            _navigation.Clear();
            _navigationItems.Clear();
            _compactArea.Clear();
            _navigationFeatures = new List<FeatureBase>();
            foreach (var group in _features.GroupBy(f => f.Group))
            {
                _navigation.Add(Ui.Text(L10n.Tr("nav.group." + group.Key), "kn-nav-group"));
                foreach (var feature in group)
                {
                    var f = feature;
                    _navigationFeatures.Add(f);
                    var item = new Button(() => SelectFeature(f));
                    item.AddToClassList("kn-nav-item");
                    item.tooltip = f.Description;
                    item.Add(Ui.NavigationIcon(f.Group));
                    item.Add(Ui.Text(f.Title, "kn-nav-item__title"));
                    // Unity のアイコン取得に依存せず、製品版の表示を常に保証する。
                    if (f.Locked) item.Add(Ui.Text(L10n.Tr("card.full"), "kn-nav-item__lock"));
                    _navigation.Add(item);
                    _navigationItems[f] = item;
                }
            }

            _featureChoices = new List<string> { L10n.Tr("nav.chooseFeature") };
            _featureChoices.AddRange(_navigationFeatures.Select(f => L10n.Tr("nav.group." + f.Group) + " / " + f.Title +
                (f.Locked ? " (" + L10n.Tr("card.full") + ")" : "")));
            _featurePopup = new PopupField<string>(_featureChoices, 0);
            _featurePopup.AddToClassList("kn-feature-popup");
            _featurePopup.tooltip = L10n.Tr("nav.chooseFeature");
            _featurePopup.RegisterValueChangedCallback(e =>
            {
                var index = _featureChoices.IndexOf(e.newValue) - 1;
                if (index >= 0) SelectFeature(_navigationFeatures[index]);
                else UpdateNavigationSelection();
            });
            _compactArea.Add(_featurePopup);
            UpdateNavigationSelection();
        }

        private void UpdateNavigationSelection()
        {
            foreach (var pair in _navigationItems)
                pair.Value.EnableInClassList("kn-nav-item--selected", pair.Key == _selected);
            if (_featurePopup != null)
                _featurePopup.SetValueWithoutNotify(_featureChoices[_navigationFeatures.IndexOf(_selected) + 1]);
        }

        private void BuildPanel()
        {
            // 手動更新や機能切り替えでも比較の基準を更新し、同じ変更で二度作り直さない。
            _panelHierarchyHash = HierarchyHash();
            _panelArea.Clear();
            _pageHeader.Clear();
            _panelArea.EnableInClassList("kn-panel--empty", _selected == null);
            if (_selected == null)
            {
                _panelArea.Add(Ui.Text(L10n.Tr(_compact ? "nav.emptyCompact" : "nav.empty"), "kn-empty"));
                return;
            }

            var header = Ui.Box("kn-page-header__row");
            var titles = Ui.Box("kn-page-header__texts");
            titles.Add(Ui.Text(_selected.Title, "kn-page-title"));
            titles.Add(Ui.Text(_selected.Description, "kn-page-description"));
            header.Add(titles);
            var refresh = Ui.Button(L10n.Tr("panel.refresh"), () => BuildPanel(), "kn-button--small", "kn-button--ghost");
            refresh.tooltip = L10n.Tr("panel.refreshTip");
            header.Add(refresh);
            _pageHeader.Add(header);

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
                SelectAvatar = a =>
                {
                    ScanAvatars();
                    SelectAvatar(a);
                    // 一覧に新しいアバターを加え、選んだ状態で作り直す
                    BuildAvatarArea();
                },
                OpenFeature = id =>
                {
                    var feature = _features.FirstOrDefault(f => f.Id == id);
                    if (feature != null) EditorApplication.delayCall += () => SelectFeature(feature);
                },
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
            SaveNow();
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _avatar = avatar;
            _avatarId = avatar == null ? null : BackupService.AvatarId(avatar);
            _lastSavedAt = default(System.DateTime);
            RefreshAvatarStatus();
            BuildPanel();
            RefreshFooter();
        }

        private void SelectFeature(FeatureBase feature)
        {
            if (feature == _selected) return;
            SaveNow();
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _selected = feature;
            _featureId = feature.Id;
            UpdateNavigationSelection();
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


            if (next != _avatar)
            {
                _backup.EndSession();
                _avatar = next;
            }

            _avatarId = _avatar == null ? null : BackupService.AvatarId(_avatar);
        }

        /// <summary>
        /// 非表示も含めた構成だけを見る。位置・回転・大きさ・表示状態やコンポーネントの値は含めない。
        /// 識別子も含め、同じ名前のオブジェクトに入れ替わった場合も候補の参照を取り直す。
        /// </summary>
        private ulong HierarchyHash()
        {
            if (_avatar == null || _selected == null || !_selected.RefreshOnHierarchyChange) return 0;
            unchecked
            {
                var hash = 14695981039346656037UL;
                var objects = _avatar.GetComponentsInChildren<Transform>(true);
                foreach (var t in objects)
                {
                    hash = (hash ^ (ulong)t.GetInstanceID()) * 1099511628211UL;
                    hash = (hash ^ (ulong)(t.parent == null ? 0 : t.parent.GetInstanceID())) * 1099511628211UL;
                    hash = (hash ^ (ulong)t.childCount) * 1099511628211UL;
                    foreach (var c in t.name) hash = (hash ^ c) * 1099511628211UL;
                    hash = (hash ^ 0xFFFFUL) * 1099511628211UL;
                }
                return (hash ^ (ulong)objects.Length) * 1099511628211UL;
            }
        }

        private void QueueScan()
        {
            if (_scanQueued) return;
            _scanQueued = true;
            // 同じフレームの変更をまとめ、入力イベントの処理が終わってから作り直す。
            EditorApplication.delayCall += RunQueuedScan;
        }

        private void RunQueuedScan()
        {
            _scanQueued = false;
            if (this == null || _avatarArea == null) return;

            var before = _avatars.ToList();
            var current = _avatar;
            ScanAvatars();

            if (!before.SequenceEqual(_avatars) || current != _avatar) BuildAvatarArea();
            if (current != _avatar)
            {
                BuildPanel();
                RefreshFooter();
            }
            else if (_avatar != null && _selected != null && _selected.RefreshOnHierarchyChange &&
                     HierarchyHash() != _panelHierarchyHash)
            {
                // ツール自身のスライダーなどで通知されても、構成が同じなら入力を保つ。
                BuildPanel();
            }

            // アクセや服を足したときも、アップロードを止める設定がないか見直す
            RefreshUploadCheck();
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
