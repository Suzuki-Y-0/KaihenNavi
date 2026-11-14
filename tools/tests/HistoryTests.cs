using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jeni.KaihenNavi;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

/// <summary>Undo 本体は模倣せず、履歴の分類・保存・復元時の記録対象と、部位選択の副作用を確認する。</summary>
internal static class HistoryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
    private static readonly List<Object> Recorded = new List<Object>();

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _checks++;
    }
    private static object Get(object instance, string name) { return instance.GetType().GetField(name, Private).GetValue(instance); }
    private static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Private).SetValue(instance, value); }
    private static object Call(object instance, string name, params object[] args)
    { return instance.GetType().GetMethod(name, Private).Invoke(instance, args); }

    private sealed class Style : IStyle
    {
        public float flexGrow { get; set; }
        public StyleFontDefinition unityFontDefinition { get; set; }
        public StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleColor backgroundColor { get; set; }
        public StyleBackground backgroundImage { get; set; }
        public StyleLength paddingLeft { get; set; }
        public StyleLength minHeight { get; set; }
    }

    private sealed class BadgeFeature : FeatureBase
    {
        public string Badge;
        public bool Done;
        public override string Id { get { return "body"; } }
        public override int Number { get { return 4; } }
        public override Color Accent { get { return new Color(); } }
        public override bool InTrial { get { return true; } }
        public override string NavBadge(VRCAvatarDescriptor avatar) { return Badge; }
        public override bool IsDone(VRCAvatarDescriptor avatar) { return Done; }
        public override VisualElement Build(FeatureContext ctx) { return new VisualElement(); }
    }

    private static void Finish(BackupService backup, string name)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(name);
        // Unity では操作の記録が Undo 履歴に積まれてから、遅れて CompletePending が呼ばれる。
        Undo.RecordsForTests.Add(name);
        backup.CompletePending();
    }

    private static VRCAvatarDescriptor MakeAvatar(string name)
    {
        var go = new GameObject { name = name };
        var root = new Transform { gameObject = go, name = "Root", localScale = new Vector3(1, 1, 1) };
        go.transform = root;
        go.ComponentsForTests = type => type == typeof(Transform) ? new object[] { root } : new object[0];
        return new VRCAvatarDescriptor { name = go.name, gameObject = go, transform = root };
    }

    /// <summary>起動時の片付けで、どこからも参照されない、起動前からある焼いた画像だけを消す。</summary>
    private static void SweepBakesChecks()
    {
        var oldDirectory = Environment.CurrentDirectory;
        var temp = Path.Combine(Path.GetTempPath(), "kaihennavi-sweep-" + Guid.NewGuid().ToString("N"));
        var oldFind = AssetDatabase.FindForTests;
        var oldDelete = AssetDatabase.DeleteForTests;
        var oldDependencies = AssetDatabase.DependenciesForTests;
        var oldImporter = AssetImporter.LoadForTests;
        var oldResources = Resources.FindForTests;
        try
        {
            var folder = WorkFolders.AvatarsRoot + "/A/Textures";
            Directory.CreateDirectory(Path.Combine(temp, folder));
            Environment.CurrentDirectory = temp;
            var unused = folder + "/Body_2nd.png";
            var used = folder + "/Body_2nd 1.png";
            var fresh = folder + "/Body_2nd 2.png";
            var foreign = folder + "/Photo.png";
            foreach (var path in new[] { unused, used, fresh, foreign }) File.WriteAllBytes(path, new byte[] { 1 });
            AssetDatabase.FindForTests = (filter, folders) => filter == "t:Texture2D" ? new[] { unused, used, fresh, foreign } :
                filter == "t:Material" ? new[] { "Assets/Mat.mat" } : new string[0];
            AssetDatabase.DependenciesForTests = roots => roots.Contains("Assets/Mat.mat") ? new[] { used } : new string[0];
            AssetImporter.LoadForTests = path => new AssetImporter
                { assetPath = path, userData = path == foreign ? "" : "kaihen-navi-color-source:0123" };
            Resources.FindForTests = type => new Object[0];
            var deleted = new List<string>();
            AssetDatabase.DeleteForTests = path => deleted.Add(path);
            var start = new DateTime(2000, 1, 1);
            var removed = ColorLayers.SweepUnusedBakes(start, path => path == fresh ? start : start.AddDays(-1));
            Check(removed == 1 && deleted.SequenceEqual(new[] { unused }),
                "Startup sweep deletes only unreferenced tool-baked images written before this editor session");
        }
        finally
        {
            Environment.CurrentDirectory = oldDirectory;
            AssetDatabase.FindForTests = oldFind;
            AssetDatabase.DeleteForTests = oldDelete;
            AssetDatabase.DependenciesForTests = oldDependencies;
            AssetImporter.LoadForTests = oldImporter;
            Resources.FindForTests = oldResources;
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    public static void Main()
    {
        VisualElement.StyleForTests = () => new Style();
        Vector3.one = new Vector3(1, 1, 1);
        ScriptableObject.CreateForTests = type => (ScriptableObject)Activator.CreateInstance(type, true);
        AssetDatabase.CreateForTests = (asset, path) => Assets.Add(path, asset);
        AssetDatabase.LoadForTests = path => Assets.ContainsKey(path) ? Assets[path] :
            path != null && File.Exists(path) && path.EndsWith(".json") ? new TextAsset { text = File.ReadAllText(path) } : null;
        AssetDatabase.FindForTests = (filter, folders) => Assets.Where(p => p.Value is BackupSnapshot).Select(p => p.Key).ToArray();
        AssetDatabase.PathFromGuidForTests = path => path;
        AssetDatabase.PathForTests = asset => Assets.FirstOrDefault(p => p.Value == asset).Key;
        var deletions = 0;
        AssetDatabase.DeleteForTests = path => deletions++;
        Undo.RecordForTests = target => Recorded.Add(target);
        EditorApplication.timeSinceStartup = 3600;
        GlobalObjectId.IdForTests = o => "GlobalObjectId_V1-2-0123456789abcdef0123456789abcdef-" + o.name + "-0";
        // JSON を扱う参加機能の挙動は本物の Unity で確認する。このテストでは履歴自身に絞る。
        ((IList)typeof(BackupService).GetField("Participants", Static).GetValue(null)).Clear();
        var go = new GameObject { name = "テスト用アバター" };
        var root = new Transform { gameObject = go, name = "Root", localScale = new Vector3(1, 1, 1) };
        go.transform = root;
        go.ComponentsForTests = type => type == typeof(Transform) ? new object[] { root } : new object[0];
        var avatar = new VRCAvatarDescriptor { name = go.name, gameObject = go, transform = root };
        var backup = new BackupService();
        var englishName = FlatJson.Parse(File.ReadAllText(PackagePaths.Localization + "/en.json"))["undo.height"];
        Check(L10n.UndoKey(englishName) == "undo.height", "English Undo name must resolve to its translation key");
        backup.BeginEdit(avatar, "body", "body:height");
        Finish(backup, englishName);
        var first = backup.List(avatar).Single();
        Check(first.operationKey == "undo.height" && first.operationName == englishName, "Undo name and key are preserved");
        Check(first.featureId == "body", "The feature used when editing classifies the step");
        Check(Recorded.Contains(first), "Step visibility must participate in Undo");
        root.localScale = new Vector3(1.2f, 1.2f, 1.2f);
        backup.BeginEdit(avatar, "body");
        root.localScale = new Vector3(1.4f, 1.4f, 1.4f);
        Finish(backup, L10n.Tr("undo.body"));
        var second = backup.List(avatar)[0];
        Check(second.SessionTicks == first.SessionTicks && backup.Count(avatar) == 1, "Repeated edits belong to one task");
        Check(second.createdTicks > first.createdTicks, "Fast edits have a stable order");
        Check(first.transforms[0].localScale.x == 1f && second.transforms[0].localScale.x == 1.2f,
            "A fine step stores its own pre-edit state, not the initial session or post-edit state");
        backup.EndSession();
        backup.BeginEdit(avatar, "outfit");
        Finish(backup, L10n.Tr("undo.outfit"));
        Check(backup.Count(avatar) == 2, "Changing feature starts another task");
        backup.EndSession();
        backup.BeginEdit(avatar, "body");
        Finish(backup, L10n.Tr("undo.body"));
        Check(backup.Count(avatar) == 3, "Returning to the same feature starts a new task");
        backup.EndSession();
        Check(new BackupService().List(avatar).Count == 4, "Fine steps survive a service restart");
        var legacy = new BackupSnapshot { avatarId = BackupService.AvatarId(avatar), createdTicks = 1, featureId = "color" };
        var legacyPath = WorkFolders.AvatarsRoot + "/Legacy/Backups/legacy.asset";
        Assets.Add(legacyPath, legacy);
        // Unity ではインポートの通知（BackupAssetWatcher）で読み込みの控えが更新される
        BackupService.OnAssetsChanged(new[] { legacyPath }, new string[0], new string[0], new string[0]);
        Check(legacy.SessionTicks == legacy.createdTicks && backup.List(avatar).Contains(legacy), "Legacy backups are shown as one-step tasks");
        Recorded.Clear();
        backup.Restore(avatar, second);
        Check(!first.hiddenFromHistory && second.hiddenFromHistory && backup.List(avatar).Count == 2, "Restore hides the selected step and all later steps");
        Check(Recorded.OfType<BackupSnapshot>().Count() == 3, "Restoration records every hidden step for Ctrl+Z");
        Check(deletions == 0 && Assets.Count == 5, "Restoration must preserve assets referenced by Undo");
        foreach (var step in Recorded.OfType<BackupSnapshot>()) step.hiddenFromHistory = false;
        backup.OnUndoRedo();
        Check(backup.List(avatar).Count == 5, "Undo of a restoration exposes the same saved steps");

        UndoHelper.Separate();
        backup.BeginEdit(avatar, "body", "drag");
        UndoHelper.Begin("drag", L10n.Tr("undo.body"));
        UndoHelper.End();
        backup.CompletePending();
        var beforeMerge = Assets.Count;
        backup.BeginEdit(avatar, "body", "drag");
        UndoHelper.Begin("drag", L10n.Tr("undo.body"));
        UndoHelper.End();
        backup.CompletePending();
        Check(Assets.Count == beforeMerge, "A merged slider gesture must not add a backup for every frame");
        backup.EndSession();
        for (var i = 0; i < BackupService.MaxBackupsPerAvatar + 2; i++)
        {
            backup.BeginEdit(avatar, "body");
            Finish(backup, L10n.Tr("undo.body"));
            backup.EndSession();
        }
        Check(backup.Count(avatar) == BackupService.MaxBackupsPerAvatar, "The limit counts tasks, not fine steps");
        Check(deletions == 0, "Pruning must not delete Undo targets");

        var window = new KaihenNaviWindow();
        Set(window, "_avatar", avatar);
        Set(window, "_features", FeatureCatalog.Create());
        var nativeUndoCalls = 0;
        Set(window, "_backup", backup);
        Undo.PerformUndoForTests = () => { nativeUndoCalls++; Call(window, "OnUndoRedo"); };
        Call(window, "RestoreLatest");
        Check(nativeUndoCalls == 1, "The arrow performs exactly one native Undo");
        var restartedWindow = new KaihenNaviWindow();
        Set(restartedWindow, "_avatar", avatar);
        Set(restartedWindow, "_features", FeatureCatalog.Create());
        var beforeFallback = backup.List(avatar)[0];
        Call(restartedWindow, "RestoreLatest");
        Check(nativeUndoCalls == 1 && beforeFallback.hiddenFromHistory, "After restart the arrow restores exactly the latest backup");
        Undo.PerformUndoForTests = () => { nativeUndoCalls++; Call(restartedWindow, "OnUndoRedo"); };
        Call(restartedWindow, "RestoreLatest");
        Check(nativeUndoCalls == 2, "The next arrow can undo the restoration itself, including a backup restored after restart");

        // 上に別の操作（カメラの移動など）が積まれたら、Unity の Undo ではなくバックアップで戻す。
        var records = Undo.RecordsForTests;
        var liveBackup = new BackupService();
        var liveWindow = new KaihenNaviWindow();
        Set(liveWindow, "_avatar", avatar);
        Set(liveWindow, "_features", FeatureCatalog.Create());
        Set(liveWindow, "_backup", liveBackup);
        Undo.PerformUndoForTests = () => { nativeUndoCalls++; records.RemoveAt(records.Count - 1); Call(liveWindow, "OnUndoRedo"); };
        liveBackup.BeginEdit(avatar, "color");
        Finish(liveBackup, L10n.Tr("undo.body"));
        Check(liveBackup.HasLiveUndo(avatar), "A step just recorded by this window is the top Undo step");
        records.Add("Move Main Camera");
        Check(!liveBackup.HasLiveUndo(avatar), "Another Undo step on top must not be undone by the arrow");
        var colorStep = liveBackup.List(avatar)[0];
        var callsBefore = nativeUndoCalls;
        Call(liveWindow, "RestoreLatest");
        Check(nativeUndoCalls == callsBefore && colorStep.hiddenFromHistory, "With a foreign step on top the arrow restores the backup instead");

        // 別のアバターの復元が上にあるとき、このアバターの「元に戻す」で取り消さない。
        var other = MakeAvatar("別のアバター");
        Check(BackupService.AvatarId(other) != BackupService.AvatarId(avatar), "Test avatars have distinct identifiers");
        liveBackup.EndSession();
        liveBackup.BeginEdit(other, "body");
        Finish(liveBackup, L10n.Tr("undo.body"));
        liveBackup.EndSession();
        liveBackup.BeginEdit(avatar, "body");
        Finish(liveBackup, L10n.Tr("undo.body"));
        liveBackup.EndSession();
        liveBackup.Restore(avatar, liveBackup.List(avatar)[0]);
        records.Add(L10n.Tr("undo.restore"));
        Check(!liveBackup.HasLiveUndo(other), "A restoration of another avatar is not this avatar's Undo step");
        var otherStep = liveBackup.List(other)[0];
        Set(liveWindow, "_avatar", other);
        callsBefore = nativeUndoCalls;
        Call(liveWindow, "RestoreLatest");
        Check(nativeUndoCalls == callsBefore && otherStep.hiddenFromHistory, "The other avatar falls back to its own latest backup");
        Set(liveWindow, "_avatar", avatar);

        // 失敗して取り消した操作は履歴に残さない。
        var assetsBeforeCancel = Assets.Count;
        liveBackup.EndSession();
        liveBackup.BeginEdit(avatar, "outfit");
        liveBackup.CancelPending();
        liveBackup.CompletePending();
        Check(Assets.Count == assetsBeforeCancel, "A cancelled operation leaves no history entry");

        // 一覧は読み込みを控え、バックアップのファイルが増えた・消えたときだけ探し直す。
        var finds = 0;
        var find = AssetDatabase.FindForTests;
        AssetDatabase.FindForTests = (filter, folders) => { finds++; return find(filter, folders); };
        BackupService.InvalidateCache();
        liveBackup.List(avatar);
        liveBackup.List(avatar, true);
        liveBackup.Count(avatar);
        Check(finds == 1, "Listing reuses the loaded backups");
        var none = new string[0];
        var knownPath = AssetDatabase.GetAssetPath(liveBackup.List(avatar)[0]);
        BackupService.OnAssetsChanged(new[] { WorkFolders.AvatarsRoot + "/A/Materials/Hair.mat", knownPath }, none, none, none);
        liveBackup.List(avatar);
        Check(finds == 1, "Saving materials or a loaded backup does not reload the list");
        BackupService.OnAssetsChanged(none, new[] { knownPath }, none, none);
        liveBackup.List(avatar);
        Check(finds == 2, "Deleting a backup reloads the list");
        liveBackup.EndSession();
        liveBackup.BeginEdit(avatar, "body");
        Finish(liveBackup, L10n.Tr("undo.body"));
        Check(finds == 2 && liveBackup.List(avatar)[0].createdTicks > first.createdTicks, "A new backup joins the list without reloading");

        // 自分用アバターへのつなぎ替えで ID を付け替えるとき、隠れたバックアップも含めて Undo に記録する。
        var withHidden = liveBackup.List(avatar, true);
        Check(withHidden.Any(s => s.hiddenFromHistory), "The test has hidden backups");
        Recorded.Clear();
        liveBackup.Rekey(avatar, withHidden);
        Check(withHidden.All(Recorded.Contains), "Re-keying is part of Ctrl+Z, hidden backups included");

        // 起動前から変わっていない、履歴に出ないバックアップだけを消す。
        for (var i = 0; i < BackupService.MaxBackupsPerAvatar + 3; i++)
        {
            Assets.Add(WorkFolders.AvatarsRoot + "/Old/Backups/old" + i + ".asset",
                new BackupSnapshot { avatarId = "old-avatar", avatarName = "old", createdTicks = 100 + i });
        }
        BackupService.InvalidateCache();
        var deletedPaths = new List<string>();
        AssetDatabase.DeleteForTests = path => deletedPaths.Add(path);
        Check(BackupService.DeleteStale(BackupService.SessionStart, path => DateTime.MaxValue) == 0 && deletedPaths.Count == 0,
            "Backups written in this editor session are never deleted");
        var hiddenPaths = Assets.Where(p => p.Value is BackupSnapshot && ((BackupSnapshot)p.Value).hiddenFromHistory).Select(p => p.Key).ToList();
        var removed = BackupService.DeleteStale(BackupService.SessionStart, path => DateTime.MinValue);
        Check(removed == deletedPaths.Count && hiddenPaths.All(deletedPaths.Contains), "Hidden backups from earlier sessions are deleted");
        var oldDeleted = deletedPaths.Where(p => p.Contains("/Old/")).ToList();
        Check(oldDeleted.Count == 3 && oldDeleted.All(p => ((BackupSnapshot)Assets[p]).createdTicks < 103),
            "Only the oldest tasks beyond the limit are deleted");
        Check(deletedPaths.All(p => ((BackupSnapshot)Assets[p]).hiddenFromHistory || p.Contains("/Old/")),
            "Visible backups within the limit are kept");
        SweepBakesChecks();

        // 保存前のシーンでは、この起動中に同じシーンで取った同名のバックアップだけを同じアバターとみなす。
        var sameAvatar = typeof(BackupService).GetMethod("SameAvatar", Static);
        var unsavedId = "GlobalObjectId_V1-2-00000000000000000000000000000000-1234-0";
        avatar.gameObject.scene = new UnityEngine.SceneManagement.Scene { handle = 7 };
        var unsavedStep = new BackupSnapshot { avatarId = unsavedId, avatarName = avatar.name, sceneHandle = 7, createdTicks = DateTime.Now.Ticks };
        Func<BackupSnapshot, bool> matches = snap => (bool)sameAvatar.Invoke(null, new object[] { snap, BackupService.AvatarId(avatar), avatar });
        Check(matches(unsavedStep), "An unsaved-scene backup from this session and scene matches by name");
        unsavedStep.sceneHandle = 8;
        Check(!matches(unsavedStep), "Another scene's avatar with the same name does not match");
        unsavedStep.sceneHandle = 7;
        unsavedStep.createdTicks = BackupService.SessionStart.Ticks - 1;
        Check(!matches(unsavedStep), "A backup from an earlier editor session does not match by name");
        Check(!matches(new BackupSnapshot { avatarId = unsavedId, avatarName = avatar.name, createdTicks = DateTime.Now.Ticks }),
            "Old backups without a scene handle do not match by name alone");
        Check(matches(legacy), "Backups with the same identifier still match");
        avatar.gameObject.scene = new UnityEngine.SceneManagement.Scene();
        Check(((string)Call(window, "OperationTitle", new BackupSnapshot { featureId = "body", operationName = "Add Component" })) ==
            L10n.Tr("history.operation", L10n.Tr("feature.body.nav")), "Unknown English steps use the navigation name");

        var feature = new BadgeFeature { Badge = "2", Done = true };
        var badge = new Label();
        var done = new VisualElement();
        var navItem = new VisualElement();
        ((Dictionary<FeatureBase, Label>)Get(window, "_navigationBadges")).Add(feature, badge);
        ((Dictionary<FeatureBase, VisualElement>)Get(window, "_navigationDone")).Add(feature, done);
        ((Dictionary<FeatureBase, VisualElement>)Get(window, "_navigationItems")).Add(feature, navItem);
        Call(window, "RefreshNavigationStatus");
        Check(badge.text == "2" && done.style.display.value == DisplayStyle.Flex, "Navigation badges and done indicators update in place");
        feature.Badge = null;
        feature.Done = false;
        Call(window, "RefreshNavigationStatus");
        Check(badge.style.display.value == DisplayStyle.None && done.style.display.value == DisplayStyle.None &&
            ReferenceEquals(navItem, ((Dictionary<FeatureBase, VisualElement>)Get(window, "_navigationItems"))[feature]),
            "Removing a badge must preserve the navigation item");

        var body = new BodyFeature();
        Set(body, "_avatar", avatar);
        var canvas = new VisualElement();
        Set(body, "_canvas", canvas);
        var repaints = 0;
        canvas.RepaintForTests = () => repaints++;
        typeof(BodyFeature).GetField("_selected", Static).SetValue(null, 1);
        var rebuilds = 0;
        var ctx = new FeatureContext { Avatar = avatar, Backup = new BackupService(), FeatureId = "body", Rebuild = () => rebuilds++ };
        Call(body, "BuildPartList", ctx);
        Call(body, "BuildControls", ctx);
        var length = (Slider)Get(body, "_length");
        var width = (Slider)Get(body, "_width");
        var heading = Get(body, "_partLabel");
        var buttons = (List<Button>)Get(body, "_partButtons");
        Recorded.Clear();
        length.SetValueWithoutNotify(60);
        buttons[0].clicked();
        Check(length.lowValue == 80 && length.highValue == 120, "Whole-body selection updates slider bounds");
        Check(Recorded.Count == 0 && rebuilds == 0, "Selecting a part must not modify the avatar or rebuild the window, even when a range clamps");
        buttons[2].clicked();
        Check(length.lowValue == 50 && length.highValue == 150 && ((VisualElement)Get(body, "_widthRow")).style.display.value == DisplayStyle.Flex,
            "Non-uniform parts expose the width slider");
        Check(ReferenceEquals(length, Get(body, "_length")) && ReferenceEquals(width, Get(body, "_width")) && ReferenceEquals(heading, Get(body, "_partLabel")),
            "Part selection preserves input and heading elements");
        var points = (Dictionary<HumanBodyBones, Vector2>)Get(body, "_points");
        points[HumanBodyBones.Head] = new Vector2();
        Call(body, "OnPointerDown", ctx, new PointerDownEvent());
        Check((int)typeof(BodyFeature).GetField("_selected", Static).GetValue(null) == 1 && repaints == 3 && rebuilds == 0 && Recorded.Count == 0,
            "Selecting a silhouette point repaints locally without editing or rebuilding");
        Console.WriteLine("History and body checks passed (" + _checks + "). Native Undo, Prefab saving and rendering require Unity.");
    }
}
