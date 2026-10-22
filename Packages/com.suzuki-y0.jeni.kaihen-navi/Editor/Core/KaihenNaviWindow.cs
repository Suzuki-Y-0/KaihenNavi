using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        private ulong _panelHierarchyHash;
        private readonly Dictionary<FeatureBase, VisualElement> _cards = new Dictionary<FeatureBase, VisualElement>();

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

        // ①の二択（新しく作る／前に作ったものを直す）
        private readonly AvatarChooser _chooser = new AvatarChooser();
        private bool _choosing;
        private VisualElement _step2Header;

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
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += QueueScan;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            _backup.Changed += RefreshFooter;
        }

        private void OnDisable()
        {
            L10n.Changed -= RebuildAll;
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

            root.Add(BuildHeader());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("kn-body");
            root.Add(scroll);

            scroll.Add(Ui.StepHeader("1", L10n.Tr("step.avatar")));
            _avatarArea = Ui.Box("kn-section");
            scroll.Add(_avatarArea);

            _step2Header = Ui.StepHeader("2", L10n.Tr("step.feature"));
            scroll.Add(_step2Header);
            _cardsArea = Ui.Box("kn-section", "kn-cards");
            scroll.Add(_cardsArea);

            _panelArea = Ui.Box("kn-section", "kn-panel");
            scroll.Add(_panelArea);

            // ③の中身が短い項目に切り替えたとき、スクロール位置が巻き戻って①②まで動いて見えないよう、
            // ③は少なくとも画面の高さぶんの場所を取っておく
            var panel = _panelArea;
            scroll.RegisterCallback<GeometryChangedEvent>(e =>
            {
                var height = scroll.contentViewport.layout.height;
                if (height > 0) panel.style.minHeight = height;
            });

            root.Add(BuildFooter());

            if (_autoSave != null) _autoSave.Pause();
            _autoSave = root.schedule.Execute(AutoSaveTick).Every(1000);

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
            _avatarStatus = null;
            _saveLabel = null;
            _checkArea = null;

            // アバターが決まるまでは二択を出し、②③は隠す
            var choosing = _avatar == null || _choosing;
            SetEditingVisible(!choosing);
            if (choosing)
            {
                _chooser.Picked = OnAvatarPicked;
                _chooser.Changed = BuildAvatarArea;
                _chooser.Cancel = _avatar == null ? (System.Action)null : () =>
                {
                    _choosing = false;
                    BuildAvatarArea();
                };
                _avatarArea.Add(_chooser.Build());
                return;
            }

            var current = Ui.Box("kn-row", "kn-current");
            var texts = Ui.Box("kn-grow");
            texts.Add(Ui.Text(L10n.Tr("current.label"), "kn-hint"));
            texts.Add(Ui.Text(_avatar.name, "kn-current__name"));
            current.Add(texts);
            current.Add(Ui.Button(L10n.Tr("current.change"), () =>
            {
                SaveNow();
                _choosing = true;
                _chooser.CurrentMode = AvatarChooser.Mode.Start;
                BuildAvatarArea();
            }, "kn-button--small", "kn-button--ghost"));
            _avatarArea.Add(current);

            _avatarStatus = Ui.Box("kn-avatar-status");
            _avatarArea.Add(_avatarStatus);
            RefreshAvatarStatus();

            _checkArea = Ui.Box("kn-upload-check");
            _avatarArea.Add(_checkArea);
            RefreshUploadCheck();
        }

        private void SetEditingVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_step2Header != null) _step2Header.style.display = display;
            if (_cardsArea != null) _cardsArea.style.display = display;
            if (_panelArea != null) _panelArea.style.display = display;
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

        /// <summary>①の下に、自分用アバターの状態（作る案内・保存の状態）を出す。</summary>
        private void RefreshAvatarStatus()
        {
            if (_avatarStatus == null) return;
            _avatarStatus.Clear();
            _saveLabel = null;
            if (_avatar == null) return;

            switch (MyAvatar.KindOf(_avatar))
            {
                case AvatarKind.Mine:
                {
                    var path = MyAvatar.PrefabPath(_avatar);
                    var row = Ui.Box("kn-mine");
                    var texts = Ui.Box("kn-mine__texts");
                    _saveLabel = Ui.Text("", "kn-mine__sub");
                    texts.Add(_saveLabel);
                    row.Add(texts);
                    row.Add(Ui.Button(L10n.Tr("mine.saveNow"), () =>
                    {
                        _savedGroup = Undo.GetCurrentGroup();
                        SaveNow(true);
                    }, "kn-button--small"));
                    row.Add(Ui.Button(L10n.Tr("common.where"), () =>
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }, "kn-button--small", "kn-button--ghost"));
                    _avatarStatus.Add(row);
                    UpdateSaveLabel();
                    _avatarStatus.Add(BuildUploadTarget());
                    break;
                }

                case AvatarKind.Original:
                {
                    if (Postponed.Contains(_avatarId))
                    {
                        var later = Ui.Box("kn-row", "kn-mine-later");
                        later.Add(Ui.Text(L10n.Tr("mine.notYet"), "kn-hint"));
                        later.Add(Ui.Button(L10n.Tr("mine.create"), CreateMine, "kn-button--small"));
                        _avatarStatus.Add(later);
                        break;
                    }

                    _avatarStatus.Add(Ui.Notice(L10n.Tr("mine.suggest"), "warn"));
                    var buttons = Ui.Box("kn-row");
                    buttons.Add(Ui.Button(L10n.Tr("mine.create"), CreateMine, "kn-button--primary"));
                    buttons.Add(Ui.Button(L10n.Tr("mine.later"), () =>
                    {
                        Postponed.Add(_avatarId);
                        RefreshAvatarStatus();
                    }, "kn-button--ghost"));
                    _avatarStatus.Add(buttons);
                    break;
                }

                default:
                {
                    _avatarStatus.Add(Ui.Notice(L10n.Tr("mine.unlinked"), "warn"));
                    _avatarStatus.Add(Ui.Button(L10n.Tr("mine.createUnlinked"), CreateMine, "kn-button--primary"));
                    break;
                }
            }
        }

        /// <summary>アップロード先（新しく出る／前のアバターを更新する）と、上書き事故の警告。</summary>
        private VisualElement BuildUploadTarget()
        {
            var box = Ui.Box("kn-upload");
            var id = MyAvatar.BlueprintId(_avatar.gameObject);
            var row = Ui.Box("kn-row");
            row.Add(Ui.Text(id.Length == 0 ? L10n.Tr("upload.new") : L10n.Tr("upload.update", id), "kn-hint", "kn-grow"));
            if (id.Length > 0)
            {
                row.Add(Ui.Button(L10n.Tr("upload.makeNew"), () =>
                {
                    if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("upload.makeNewConfirm"),
                            L10n.Tr("upload.makeNew"), L10n.Tr("dialog.cancel"))) return;
                    MyAvatar.ClearBlueprintId(_avatar.gameObject);
                    SaveNow();
                    RefreshAvatarStatus();
                }, "kn-button--small", "kn-button--ghost"));
            }

            box.Add(row);

            var same = MyAvatar.SameUploadTarget(_avatar);
            if (same.Count > 0)
            {
                box.Add(Ui.Notice(L10n.Tr("upload.conflict", string.Join("、", same.Select(p => p.name).ToArray())), "warn"));
            }

            return box;
        }

        // ---------------------------------------------------------------- アップロード前のチェック

        /// <summary>
        /// SDK がアップロードを止めるモデルの読み込み設定（Read/Write など）を先に見つけて、①の下に出す。
        /// 直すと配布物の読み込み設定（.meta）が変わるので、必ず確認を出す。
        /// </summary>
        private void RefreshUploadCheck()
        {
            if (_checkArea == null) return;
            _checkArea.Clear();
            if (_avatar == null) return;

            var issues = UploadCheck.Find(_avatar);
            if (issues.Count == 0) return;

            _checkArea.Add(Ui.Notice(L10n.Tr("check.attention", issues.Count), "warn"));
            _checkArea.Add(Ui.Text(UploadCheck.Describe(issues), "kn-hint"));
            var fixableIssues = issues.Where(i => i.CanFix).ToList();
            if (fixableIssues.Count == 0) return;
            var row = Ui.Box("kn-row");
            row.Add(Ui.Button(L10n.Tr("check.fix"), () =>
            {
                if (!EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.confirm", UploadCheck.Describe(fixableIssues)),
                        L10n.Tr("check.fix"), L10n.Tr("dialog.cancel"))) return;
                var fixedCount = UploadCheck.Fix(fixableIssues);
                RefreshUploadCheck();
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("check.fixed", fixedCount), L10n.Tr("dialog.ok"));
            }, "kn-button--small", "kn-button--primary"));
            _checkArea.Add(row);
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
            BuildPanel();
            RefreshFooter();
        }

        private void UpdateSaveLabel()
        {
            if (_saveLabel == null) return;
            _saveLabel.text = _lastSavedAt == default(System.DateTime)
                ? L10n.Tr("mine.autosave")
                : L10n.Tr(_lastSaveManual ? "mine.savedManually" : "mine.savedAt", _lastSavedAt.ToString("HH:mm:ss"));
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

        private void BuildCards()
        {
            _cardsArea.Clear();
            _cards.Clear();
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
                _cards[f] = card;
            }
        }

        /// <summary>カードは作り直さず、選択中の印だけ付け替える（作り直すと一瞬見た目が変わるため）。</summary>
        private void UpdateCardSelection()
        {
            foreach (var pair in _cards)
            {
                pair.Value.EnableInClassList("kn-card--selected", pair.Key == _selected);
            }
        }

        private void BuildPanel()
        {
            // 手動更新や機能切り替えでも比較の基準を更新し、同じ変更で二度作り直さない。
            _panelHierarchyHash = HierarchyHash();
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
            UpdateCardSelection();
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
