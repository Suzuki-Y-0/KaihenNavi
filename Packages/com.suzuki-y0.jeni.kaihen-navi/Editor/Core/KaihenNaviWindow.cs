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
        private VisualElement _avatarThumbnail;
        private Texture2D _thumbnailImage;
        private string _thumbnailInitial;
        private string _checkSignature;
        private VisualElement _navigation;
        private VisualElement _navigationList;
        private VisualElement _navigationFooter;
        private Button _uploadNavigationItem;
        private Label _uploadBadge;
        private VisualElement _saveDot;
        private List<UploadCheck.Issue> _uploadIssues = new List<UploadCheck.Issue>();
        private List<GameObject> _sameUploadTargets = new List<GameObject>();
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
        private VisualElement _historyOverlay;
        private bool _undoPerformed;
        private readonly Dictionary<FeatureBase, Label> _navigationBadges = new Dictionary<FeatureBase, Label>();
        private readonly Dictionary<FeatureBase, VisualElement> _navigationDone = new Dictionary<FeatureBase, VisualElement>();
        private bool _scanQueued;
        private ulong _panelHierarchyHash;
        private ulong _navigationHierarchyHash;
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
            FontLoader.Release(rootVisualElement);
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
        }

        private void QueueKonoRefresh()
        {
            EditorApplication.delayCall -= RefreshKono;
            EditorApplication.delayCall += RefreshKono;
        }

        private void RefreshKono()
        {
            if (_avatarArea == null) return;
            if (_avatar == null || _choosing) BuildAvatarArea();
            else if (_selected != null && (_selected.Id == "outfit" || _selected.Id == "transform")) BuildPanel(false);
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
            CloseHistory();
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
            _navigation = Ui.Box("kn-navigation");
            var navigation = new ScrollView(ScrollViewMode.Vertical);
            navigation.AddToClassList("kn-navigation-scroll");
            _navigationList = navigation;
            _navigation.Add(navigation);
            _navigationFooter = Ui.Box("kn-navigation-footer");
            _navigation.Add(_navigationFooter);
            _workspace.Add(_navigation);
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
            _saveDot = null;
            _avatarThumbnail = null;
            _restoreButton = null;
            _historyButton = null;
            _checkArea = null;
            _checkSignature = null;
            _thumbnailImage = null;
            _thumbnailInitial = null;

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
            _avatarThumbnail = Ui.Box("kn-current__thumbnail");
            RefreshAvatarThumbnail();
            current.Add(_avatarThumbnail);
            var texts = Ui.Box("kn-current__texts");
            texts.Add(Ui.Text(_avatar.name, "kn-current__name"));
            _avatarStatus = Ui.Box("kn-current__meta");
            texts.Add(_avatarStatus);
            _avatarAdvice = Ui.Box("kn-avatar-advice");
            texts.Add(_avatarAdvice);
            current.Add(texts);
            _avatarActions = Ui.Box("kn-avatar-actions");
            current.Add(_avatarActions);
            _avatarArea.Add(current);
            RefreshAvatarStatus();
            _checkArea = Ui.Box("kn-upload-check");
            _avatarArea.Add(_checkArea);
            RefreshUploadCheck();
        }

        /// <summary>撮影・画像の差し替え後も、上部のサムネイルを最新にする。</summary>
        private void OnProjectChange()
        {
            if (_avatarArea == null) return;
            RefreshAvatarThumbnail();
            RefreshUploadCheck();
        }

        private void RefreshAvatarThumbnail()
        {
            if (_avatarThumbnail == null || _avatar == null) return;
            var shot = AvatarChooser.LoadThumbnail(MyAvatar.PrefabPath(_avatar));
            var initial = string.IsNullOrEmpty(_avatar.name) ? "?" :
                System.Globalization.StringInfo.GetNextTextElement(_avatar.name);
            if (_thumbnailImage == shot && _thumbnailInitial == initial) return;
            _thumbnailImage = shot;
            _thumbnailInitial = initial;
            _avatarThumbnail.Clear();
            _avatarThumbnail.style.backgroundImage = new StyleBackground(shot);
            if (shot == null) _avatarThumbnail.Add(Ui.Text(initial, "kn-current__initial"));
        }

        private void OnAvatarPicked(VRCAvatarDescriptor avatar)
        {
            _choosing = false;
            ScanAvatars();
            if (avatar != _avatar) SelectAvatar(avatar);
            BuildAvatarArea();
            BuildNavigation();
            BuildPanel();
            RefreshFooter();
        }

        // ---------------------------------------------------------------- 自分用アバター

        /// <summary>アバター帯の保存状態と操作を更新する。</summary>
        private void RefreshAvatarStatus()
        {
            if (_avatarStatus == null || _avatarActions == null) return;
            _avatarStatus.Clear();
            _avatarActions.Clear();
            _avatarAdvice.Clear();
            if (_avatar == null) return;

            var kind = MyAvatar.KindOf(_avatar);
            _saveDot = Ui.Box("kn-save-dot");
            _avatarStatus.Add(_saveDot);
            _saveLabel = Ui.Text("", "kn-meta-text");
            _avatarStatus.Add(_saveLabel);
            UpdateSaveLabel();
            _avatarStatus.Add(Ui.Text("·", "kn-meta-separator"));
            var id = MyAvatar.BlueprintId(_avatar.gameObject);
            var upload = Ui.Text(L10n.Tr(id.Length == 0 ? "nav.uploadNew" : "nav.uploadUpdate"), "kn-meta-text");
            upload.tooltip = id.Length == 0 ? L10n.Tr("upload.new") : L10n.Tr("upload.update", id);
            _avatarStatus.Add(upload);
            if (Edition.IsTrial)
            {
                _avatarStatus.Add(Ui.Text("·", "kn-meta-separator"));
                _avatarStatus.Add(Ui.Text(L10n.Tr("app.trial"), "kn-meta-text"));
            }

            _restoreButton = Ui.IconButton("undo", L10n.Tr("footer.restore"), RestoreLatest);
            _avatarActions.Add(_restoreButton);
            _historyButton = Ui.IconButton("history", L10n.Tr("footer.history"), ShowHistoryMenu);
            _avatarActions.Add(_historyButton);
            var save = Ui.Button(L10n.Tr("nav.save"), () => SaveNow(true), "kn-button--small");
            save.tooltip = L10n.Tr("mine.saveNow");
            save.SetEnabled(kind == AvatarKind.Mine);
            _avatarActions.Add(save);
            _avatarActions.Add(Ui.IconButton("more", L10n.Tr("nav.more"), ShowAvatarMenu));

            if (kind != AvatarKind.Mine)
            {
                var postponed = kind == AvatarKind.Original && Postponed.Contains(_avatarId);
                var message = kind == AvatarKind.Original ? (postponed ? "mine.notYet" : "mine.suggest") : "mine.unlinked";
                var advice = Ui.Text(L10n.Tr("nav.sceneAdvice"), "kn-avatar-advice__text");
                advice.tooltip = L10n.Tr(message);
                _avatarAdvice.Add(advice);
            }
            RefreshFooter();
        }

        /// <summary>帯の補助操作と言語をメニューにまとめる。</summary>
        private void ShowAvatarMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(L10n.Tr("nav.changeAvatar")), false, () =>
            {
                SaveNow();
                _choosing = true;
                _chooser.CurrentMode = AvatarChooser.Mode.Start;
                BuildAvatarArea();
            });
            var path = MyAvatar.PrefabPath(_avatar);
            if (!string.IsNullOrEmpty(path))
                menu.AddItem(new GUIContent(L10n.Tr("nav.openLocation")), false, () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                });
            else menu.AddDisabledItem(new GUIContent(L10n.Tr("nav.openLocation")));

            var kind = MyAvatar.KindOf(_avatar);
            if (kind != AvatarKind.Mine)
            {
                menu.AddItem(new GUIContent(L10n.Tr(kind == AvatarKind.Original ? "mine.create" : "mine.createUnlinked")), false, CreateMine);
                if (kind == AvatarKind.Original && !Postponed.Contains(_avatarId))
                    menu.AddItem(new GUIContent(L10n.Tr("mine.later")), false, () =>
                    {
                        Postponed.Add(_avatarId);
                        RefreshAvatarStatus();
                    });
            }
            if (MyAvatar.BlueprintId(_avatar.gameObject).Length > 0)
                menu.AddItem(new GUIContent(L10n.Tr("upload.makeNew")), false, () =>
                {
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("upload.makeNewConfirm"),
                            L10n.Tr("upload.makeNew"), L10n.Tr("dialog.cancel"))) return;
                    MyAvatar.ClearBlueprintId(_avatar.gameObject);
                    SaveNow();
                    RefreshAvatarStatus();
                    RefreshUploadCheck();
                });
            menu.AddSeparator("");
            for (var i = 0; i < L10n.Codes.Length; i++)
            {
                var code = L10n.Codes[i];
                menu.AddItem(new GUIContent(L10n.Tr("nav.language") + "/" + L10n.DisplayNames[i]), L10n.Current == code,
                    () => EditorApplication.delayCall += () => L10n.Current = code);
            }
            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent(L10n.Tr(Edition.IsTrial ? "app.trial" : "nav.full")));
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- アップロード前のチェック

        /// <summary>問題はナビの件数と確認ページに集約し、細い幅では上部にも案内する。</summary>
        private void RefreshUploadCheck()
        {
            _uploadIssues = UploadCheck.Find(_avatar);
            _sameUploadTargets = _avatar == null ? new List<GameObject>() : MyAvatar.SameUploadTarget(_avatar);
            var count = _uploadIssues.Count + (_sameUploadTargets.Count > 0 ? 1 : 0);
            if (_uploadBadge != null)
            {
                _uploadBadge.text = count.ToString();
                _uploadBadge.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            var signature = string.Join("|", _uploadIssues.Select(i => i.ModelPath + ":" + i.CanFix + ":" +
                string.Join(",", i.Kinds.OrderBy(k => k).Select(k => k.ToString()).ToArray())).Concat(
                    _sameUploadTargets.Select(t => t.GetInstanceID() + ":" + t.name)).ToArray());
            var changed = signature != _checkSignature;
            _checkSignature = signature;
            if (_checkArea != null && changed)
            {
                _checkArea.Clear();
                if (count > 0)
                {
                    var notice = Ui.Notice(L10n.Tr("check.attention", count), "warn");
                    notice.Add(Ui.Button(L10n.Tr("nav.uploadCheck"), SelectUploadCheck, "kn-button--small", "kn-button--ghost"));
                    _checkArea.Add(notice);
                }
            }
            if (changed && _featureId == "uploadCheck" && _panelArea != null) BuildPanel(false);
        }

        /// <summary>確認ページ。修正は従来どおり、対象を列挙した確認ダイアログの後にだけ実行する。</summary>
        private void BuildUploadCheckPage(bool rebuildHeader)
        {
            if (rebuildHeader)
            {
                var header = Ui.Box("kn-page-header__row");
                var titles = Ui.Box("kn-page-header__texts");
                titles.Add(Ui.Text(L10n.Tr("nav.uploadCheck"), "kn-page-title"));
                titles.Add(Ui.Text(L10n.Tr("check.description"), "kn-page-description"));
                header.Add(titles);
                var refresh = Ui.Button(L10n.Tr("panel.refresh"), RefreshUploadCheck, "kn-button--small", "kn-button--ghost");
                refresh.tooltip = L10n.Tr("panel.refreshTip");
                header.Add(refresh);
                _pageHeader.Add(header);
            }
            if (_avatar == null) return;
            if (_uploadIssues.Count == 0 && _sameUploadTargets.Count == 0)
            {
                var success = Ui.Box("kn-check-success");
                success.Add(Ui.NavigationIcon("check"));
                success.Add(Ui.Text(L10n.Tr("check.none"), "kn-check-success__text"));
                _panelArea.Add(success);
                return;
            }
            if (_sameUploadTargets.Count > 0)
                _panelArea.Add(Ui.Notice(L10n.Tr("upload.conflict", string.Join("、", _sameUploadTargets.Select(p => p.name).ToArray())), "warn"));
            var list = Ui.Box("kn-list");
            foreach (var issue in _uploadIssues)
            {
                var row = Ui.Box("kn-list__row", "kn-check-row");
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(System.IO.Path.GetFileName(issue.ModelPath), "kn-list__title"));
                texts.Add(Ui.Text(UploadCheck.DescribeKinds(issue), "kn-list__sub"));
                texts.Add(Ui.Text(issue.ModelPath, "kn-list__sub"));
                row.Add(texts);
                list.Add(row);
            }
            _panelArea.Add(list);
            var fixable = _uploadIssues.Where(i => i.CanFix).ToList();
            if (fixable.Count == 0) return;
            _panelArea.Add(Ui.Button(L10n.Tr("check.fixAll"), () =>
            {
                if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.confirm", UploadCheck.Describe(fixable)),
                        L10n.Tr("check.fix"), L10n.Tr("dialog.cancel"))) return;
                var fixedCount = UploadCheck.Fix(fixable);
                RefreshUploadCheck();
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.fixed", fixedCount), L10n.Tr("dialog.ok"));
            }, "kn-button--primary"));
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
            var sceneOnly = _avatar == null || MyAvatar.KindOf(_avatar) != AvatarKind.Mine;
            var dirty = !sceneOnly && (MyAvatar.HasUnsaved(_avatar) || HasUnsavedMaterials());
            _saveDot.EnableInClassList("kn-save-dot--ok", !sceneOnly && !dirty);
            _saveDot.EnableInClassList("kn-save-dot--dirty", dirty);
            _saveLabel.text = sceneOnly ? L10n.Tr("nav.sceneOnly") : dirty ? L10n.Tr("nav.unsaved") :
                _lastSavedAt == default(System.DateTime) ? L10n.Tr("nav.saved") : L10n.Tr("nav.savedAt", _lastSavedAt.ToString("HH:mm"));
        }

        /// <summary>Prefab の変更に含まれない、自分用マテリアルの未保存状態も表示に反映する。</summary>
        private bool HasUnsavedMaterials()
        {
            if (_avatar == null) return false;
            foreach (var renderer in _avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && EditorUtility.IsDirty(material) && WorkFolders.IsOwnedBy(material, _avatar.gameObject)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 操作が 1.5 秒止まったら、自分用アバターに保存する。
        /// Undo のグループ番号を「何か変わった」目印にしているので、何も触っていない間は保存しない。
        /// </summary>
        private void AutoSaveTick()
        {
            if (_avatar == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            UpdateSaveLabel();
            var group = Undo.GetCurrentGroup();
            var now = EditorApplication.timeSinceStartup;
            if (group != _seenGroup)
            {
                _seenGroup = group;
                _seenAt = now;
                return;
            }

            if (group == _savedGroup || now - _seenAt < AutoSaveDelay) return;
            SaveNow();
        }

        /// <summary>今すぐ保存する（ウィンドウを閉じる・シーンを閉じる・Play の前など）。</summary>
        /// <param name="manual">「保存」ボタンから呼んだとき true（変更がなくても保存時刻を更新する）。</param>
        private void SaveNow(bool manual = false)
        {
            if (_avatar == null) return;
            _backup.CompletePending();
            _backup.SaveHistory(_avatar);
            var materialsChanged = HasUnsavedMaterials();
            MyAvatar.SaveMaterials(_avatar);
            var saved = MyAvatar.Save(_avatar);
            _savedGroup = Undo.GetCurrentGroup();
            if (saved || materialsChanged || manual) _lastSavedAt = System.DateTime.Now;
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
            _navigationHierarchyHash = HierarchyHash(true);
            _navigationList.Clear();
            _navigationFooter.Clear();
            _navigationItems.Clear();
            _navigationBadges.Clear();
            _navigationDone.Clear();
            _compactArea.Clear();
            _navigationFeatures = new List<FeatureBase>();
            foreach (var group in _features.GroupBy(f => f.Group))
            {
                _navigationList.Add(Ui.Text(L10n.Tr("nav.group." + group.Key), "kn-nav-group"));
                foreach (var feature in group)
                {
                    var f = feature;
                    _navigationFeatures.Add(f);
                    var item = new Button(() => SelectFeature(f));
                    item.AddToClassList("kn-nav-item");
                    item.tooltip = f.Title + "\n" + f.Description;
                    item.Add(Ui.Box("kn-nav-item__indicator"));
                    item.Add(Ui.NavigationIcon(f.Id));
                    item.Add(Ui.Text(f.NavTitle, "kn-nav-item__title"));
                    var badge = Ui.Text("", "kn-nav-badge");
                    item.Add(badge);
                    _navigationBadges[f] = badge;
                    var done = Ui.NavigationIcon("check");
                    done.AddToClassList("kn-nav-done");
                    item.Add(done);
                    _navigationDone[f] = done;
                    // Unity のアイコン取得に依存せず、製品版の表示を常に保証する。
                    if (f.Locked) item.Add(Ui.Text(L10n.Tr("card.full"), "kn-nav-item__lock"));
                    _navigationList.Add(item);
                    _navigationItems[f] = item;
                }
            }

            _uploadNavigationItem = new Button(SelectUploadCheck);
            _uploadNavigationItem.AddToClassList("kn-nav-item");
            _uploadNavigationItem.Add(Ui.Box("kn-nav-item__indicator"));
            _uploadNavigationItem.Add(Ui.NavigationIcon("uploadCheck"));
            _uploadNavigationItem.Add(Ui.Text(L10n.Tr("nav.uploadCheck"), "kn-nav-item__title"));
            _uploadBadge = Ui.Text("", "kn-nav-badge", "kn-nav-badge--warn");
            _uploadNavigationItem.Add(_uploadBadge);
            _navigationFooter.Add(_uploadNavigationItem);

            _featureChoices = new List<string> { L10n.Tr("nav.chooseFeature") };
            _featureChoices.AddRange(_navigationFeatures.Select(f => L10n.Tr("nav.group." + f.Group) + " / " + f.NavTitle +
                (f.Locked ? " (" + L10n.Tr("card.full") + ")" : "")));
            _featureChoices.Add(L10n.Tr("nav.uploadCheck"));
            _featurePopup = new PopupField<string>(_featureChoices, 0);
            _featurePopup.AddToClassList("kn-feature-popup");
            _featurePopup.tooltip = L10n.Tr("nav.chooseFeature");
            _featurePopup.RegisterValueChangedCallback(e =>
            {
                var index = _featureChoices.IndexOf(e.newValue) - 1;
                if (index == _navigationFeatures.Count) SelectUploadCheck();
                else if (index >= 0) SelectFeature(_navigationFeatures[index]);
                else UpdateNavigationSelection();
            });
            _compactArea.Add(_featurePopup);
            UpdateNavigationSelection();
            RefreshNavigationStatus();
            RefreshUploadCheck();
        }

        /// <summary>変わらないナビの文字とフォントを保ち、件数と完了の印だけ更新する。</summary>
        private void RefreshNavigationStatus()
        {
            _navigationHierarchyHash = HierarchyHash(true);
            foreach (var pair in _navigationBadges)
            {
                var text = pair.Key.NavBadge(_avatar);
                pair.Value.text = text ?? "";
                pair.Value.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
                _navigationDone[pair.Key].style.display = pair.Key.IsDone(_avatar) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateNavigationSelection()
        {
            foreach (var pair in _navigationItems)
                pair.Value.EnableInClassList("kn-nav-item--selected", pair.Key == _selected);
            if (_uploadNavigationItem != null)
                _uploadNavigationItem.EnableInClassList("kn-nav-item--selected", _featureId == "uploadCheck");
            if (_featurePopup != null)
                _featurePopup.SetValueWithoutNotify(_featureChoices[_featureId == "uploadCheck" ?
                    _featureChoices.Count - 1 : _navigationFeatures.IndexOf(_selected) + 1]);
        }

        private void BuildPanel(bool rebuildHeader = true)
        {
            // 手動更新や機能切り替えでも比較の基準を更新し、同じ変更で二度作り直さない。
            _panelHierarchyHash = HierarchyHash();
            _panelArea.Clear();
            if (rebuildHeader) _pageHeader.Clear();
            _panelArea.EnableInClassList("kn-panel--empty", _selected == null && _featureId != "uploadCheck");
            if (_featureId == "uploadCheck")
            {
                BuildUploadCheckPage(rebuildHeader);
                return;
            }
            if (_selected == null)
            {
                _panelArea.Add(Ui.Text(L10n.Tr(_compact ? "nav.emptyCompact" : "nav.empty"), "kn-empty"));
                return;
            }

            if (rebuildHeader)
            {
                var header = Ui.Box("kn-page-header__row");
                var titles = Ui.Box("kn-page-header__texts");
                titles.Add(Ui.Text(_selected.Title, "kn-page-title"));
                titles.Add(Ui.Text(_selected.Description, "kn-page-description"));
                header.Add(titles);
                var refresh = Ui.Button(L10n.Tr("panel.refresh"), () => BuildPanel(), "kn-button--small", "kn-button--ghost");
                refresh.tooltip = L10n.Tr("panel.refreshTip");
                header.Add(refresh);
                _pageHeader.Add(header);
            }

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
                Rebuild = () =>
                {
                    RefreshNavigationStatus();
                    BuildPanel(false);
                },
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
            var list = _backup.List(_avatar);
            var count = list.Select(s => s.SessionTicks).Distinct().Count();
            _backupLabel.text = count == 0 ? L10n.Tr("footer.backups", 0) :
                L10n.Tr("footer.status", count, list[0].CreatedAt.ToString("HH:mm"));
            if (_restoreButton != null) _restoreButton.SetEnabled(list.Count > 0 || _backup.HasLiveUndo(_avatar));
            if (_historyButton != null) _historyButton.SetEnabled(_avatar != null);
        }

        // ---------------------------------------------------------------- 選択

        private void SelectAvatar(VRCAvatarDescriptor avatar)
        {
            if (avatar == _avatar) return;
            CloseHistory();
            _backup.CompletePending();
            SaveNow();
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _avatar = avatar;
            _avatarId = avatar == null ? null : BackupService.AvatarId(avatar);
            _lastSavedAt = default(System.DateTime);
            _seenGroup = Undo.GetCurrentGroup();
            _seenAt = EditorApplication.timeSinceStartup;
            _savedGroup = MyAvatar.HasUnsaved(_avatar) || HasUnsavedMaterials() ? -1 : _seenGroup;
            RefreshAvatarStatus();
            BuildNavigation();
            BuildPanel();
            RefreshFooter();
        }

        private void SelectFeature(FeatureBase feature)
        {
            SelectPage(feature, feature.Id);
        }

        private void SelectUploadCheck()
        {
            SelectPage(null, "uploadCheck");
        }

        private void SelectPage(FeatureBase feature, string id)
        {
            if (_featureId == id) return;
            CloseHistory();
            _backup.CompletePending();
            SaveNow();
            if (_selected != null) _selected.OnClosed();
            _backup.EndSession();
            UndoHelper.Separate();
            _selected = feature;
            _featureId = id;
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
        private ulong HierarchyHash(bool forNavigation = false)
        {
            if (_avatar == null || (!forNavigation && (_selected == null || !_selected.RefreshOnHierarchyChange))) return 0;
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
                BuildPanel(false);
            }

            // 構成が変わっても、左ナビと見出しを作り直さない。
            if (HierarchyHash(true) != _navigationHierarchyHash || current != _avatar) RefreshNavigationStatus();
            RefreshUploadCheck();
        }

        // ---------------------------------------------------------------- 元に戻す

        private void RestoreLatest()
        {
            if (_avatar == null) return;
            CloseHistory();
            _backup.CompletePending();
            // このウィンドウで記録した手順は Unity と同じ直前の 1 手。
            // 再起動後など、Undo との対応が分からない履歴は最新のバックアップ 1 件で戻す。
            _undoPerformed = false;
            if (_backup.HasLiveUndo(_avatar)) Undo.PerformUndo();
            if (!_undoPerformed)
            {
                var latest = _backup.List(_avatar).FirstOrDefault();
                if (latest != null) RestoreFromHistory(latest);
            }
        }

        /// <summary>履歴はウィンドウ内に重ねる。日付の / がメニュー階層になる問題も避ける。</summary>
        private void ShowHistoryMenu()
        {
            if (_avatar == null) return;
            if (_historyOverlay != null) { CloseHistory(); return; }
            _backup.CompletePending();
            _historyOverlay = Ui.Box("kn-history-overlay");
            var dismiss = Ui.Button("", CloseHistory, "kn-history-dismiss");
            dismiss.tooltip = L10n.Tr("history.close");
            _historyOverlay.Add(dismiss);
            var panel = Ui.Box("kn-history");
            var header = Ui.Box("kn-row", "kn-history__header");
            header.Add(Ui.Heading(L10n.Tr("footer.history")));
            header.Add(Ui.Button(L10n.Tr("history.close"), CloseHistory, "kn-button--small", "kn-button--ghost"));
            panel.Add(header);
            panel.Add(Ui.Text(L10n.Tr("history.hint"), "kn-hint"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("kn-history__scroll");
            var list = _backup.List(_avatar);
            foreach (var session in list.GroupBy(item => item.SessionTicks))
            {
                var entries = session.OrderByDescending(item => item.createdTicks).ToList();
                var latest = entries[0];
                var section = Ui.Box("kn-history__session");
                var title = Ui.Button("", null, "kn-history__summary");
                title.Add(Ui.Text(FeatureTitle(latest.featureId), "kn-list__title"));
                title.Add(Ui.Text(L10n.Tr("history.session", latest.CreatedAt.ToString("M/d HH:mm:ss"), entries.Count), "kn-list__sub"));
                var details = Ui.Box("kn-history__steps");
                details.style.display = DisplayStyle.None;
                var expanded = false;
                title.clicked += () =>
                {
                    expanded = !expanded;
                    details.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    title.EnableInClassList("kn-history__summary--open", expanded);
                    if (expanded && details.childCount == 0)
                        foreach (var entry in entries) details.Add(BuildHistoryStep(entry));
                };
                section.Add(title);
                section.Add(details);
                scroll.Add(section);
            }
            if (list.Count == 0) scroll.Add(Ui.Text(L10n.Tr("history.empty"), "kn-hint"));
            panel.Add(scroll);
            _historyOverlay.Add(panel);
            _historyOverlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                CloseHistory();
                e.StopPropagation();
            });
            rootVisualElement.Add(_historyOverlay);
            panel.focusable = true;
            panel.Focus();
        }

        private VisualElement BuildHistoryStep(BackupSnapshot snapshot)
        {
            var row = Ui.Box("kn-list__row", "kn-history__step");
            var texts = Ui.Box("kn-list__texts");
            texts.Add(Ui.Text(OperationTitle(snapshot), "kn-list__title"));
            texts.Add(Ui.Text(snapshot.CreatedAt.ToString("HH:mm:ss"), "kn-list__sub"));
            row.Add(texts);
            row.Add(Ui.Button(L10n.Tr("history.restoreHere"), () => RestoreFromHistory(snapshot), "kn-button--small"));
            return row;
        }

        private void CloseHistory()
        {
            if (_historyOverlay == null) return;
            _historyOverlay.RemoveFromHierarchy();
            _historyOverlay = null;
        }

        private string OperationTitle(BackupSnapshot snapshot)
        {
            if (!string.IsNullOrEmpty(snapshot.operationKey)) return L10n.Tr(snapshot.operationKey);
            // 古いバックアップや MA などの英語名は、初心者にも読める機能名に置き換える。
            var name = snapshot.operationName;
            if (!string.IsNullOrEmpty(name) && name.Any(c => c >= '\u3040' && c <= '\u9fff')) return name;
            return L10n.Tr("history.operation", FeatureTitle(snapshot.featureId));
        }

        private void RestoreFromHistory(BackupSnapshot snapshot)
        {
            if (_avatar == null || snapshot == null || snapshot.hiddenFromHistory) return;
            if (!EditorUtility.DisplayDialog(
                    L10n.Tr("restore.title"),
                    L10n.Tr("history.confirm", snapshot.CreatedAt.ToString("M/d HH:mm:ss"), OperationTitle(snapshot)),
                    L10n.Tr("history.restoreHere"), L10n.Tr("dialog.cancel"))) return;
            CloseHistory();
            if (_selected != null) _selected.OnClosed();
            _backup.Restore(_avatar, snapshot);
            _savedGroup = -1;
            _seenAt = EditorApplication.timeSinceStartup;
            RefreshAfterUndo();
        }

        private string FeatureTitle(string id)
        {
            var feature = _features.FirstOrDefault(f => f.Id == id);
            return feature == null ? L10n.Tr("history.unknownFeature") : feature.NavTitle;
        }

        private void OnUndoRedo()
        {
            _undoPerformed = true;
            _backup.OnUndoRedo();
            // Undo 後はグループ番号が同じでも、戻した状態を次の自動保存に含める。
            _savedGroup = -1;
            _seenAt = EditorApplication.timeSinceStartup;
            CloseHistory();
            RefreshAfterUndo();
        }

        private void RefreshAfterUndo()
        {
            if (_panelArea == null) return;
            UndoHelper.Separate();
            RefreshNavigationStatus();
            if (_selected == null || !_selected.RefreshValues()) BuildPanel(false);
            RefreshUploadCheck();
            UpdateSaveLabel();
            RefreshFooter();
        }
    }
}
