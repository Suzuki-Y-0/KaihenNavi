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
        backup.CompletePending();
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
        Assets.Add("legacy.asset", legacy);
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
