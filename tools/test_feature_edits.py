#!/usr/bin/env python3
"""脱ぎ着の状態復元と保存の Undo API 選択を確認する。Unity の Undo 自体や MA のプレビューは再現しない。"""
from pathlib import Path
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parent.parent
source = r'''
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;
using Jeni.KaihenNavi;

internal static class FeatureEditChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private static int count;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        count++;
    }
    private static void Set(object owner, string name, object value)
    {
        owner.GetType().GetField(name, Private).SetValue(owner, value);
    }
    private static object Call(string name, params object[] args)
    {
        return typeof(ToggleFeature).GetMethod(name, Static).Invoke(null, args);
    }
    private static GameObject Child(GameObject parent, string name, bool active)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.SetActive(active);
        return go;
    }
    private static IList Candidates(params GameObject[] targets)
    {
        var type = typeof(ToggleFeature).GetNestedType("Candidate", BindingFlags.NonPublic);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        foreach (var target in targets)
        {
            var candidate = Activator.CreateInstance(type, true);
            type.GetField("Target").SetValue(candidate, target);
            type.GetField("Label").SetValue(candidate, target.name);
            list.Add(candidate);
        }
        return list;
    }
    // メニュー整理：実際に差し替えたメニューを覚えて戻す。分からないときは取り出したメニューを消さない。
    private static void MenuTidyChecks()
    {
        PrefabUtility.RootForTests = null;
        var paths = new Dictionary<UnityEngine.Object, string>();
        var assets = new Dictionary<string, UnityEngine.Object>();
        AssetDatabase.PathForTests = o => o != null && paths.ContainsKey(o) ? paths[o] : "";
        AssetDatabase.LoadForTests = p => p != null && assets.ContainsKey(p) ? assets[p] : null;
        AssetDatabase.GuidForTests = p => "guid-" + p;
        AssetDatabase.PathFromGuidForTests = g => g.StartsWith("guid-") ? g.Substring(5) : "";
        AssetDatabase.CreateForTests = (o, p) => { paths[o] = p; assets[p] = o; };
        Func<string, VRCExpressionsMenu> menu = p =>
        {
            var m = (VRCExpressionsMenu)Activator.CreateInstance(typeof(VRCExpressionsMenu));
            m.controls.Add(new VRCExpressionsMenu.Control { name = "A", type = VRCExpressionsMenu.Control.ControlType.Toggle });
            paths[m] = p; assets[p] = m;
            return m;
        };
        var deep = menu("Assets/Base/Deep.asset");
        var variantMenu = menu("Assets/Booth/Variant.asset");
        var go = new GameObject("MenuAvatar");
        var avatar = go.AddComponent<VRCAvatarDescriptor>();
        avatar.expressionsMenu = variantMenu;

        var root = MenuFeature.BuildMenu(avatar, new MenuEdits { extracted = true, originalMenu = paths[variantMenu] }, "menu");
        Check(root != null && !ReferenceEquals(avatar.expressionsMenu, variantMenu), "menu extracted into MA items");
        Check(root.transform.Cast<Transform>().Any(t => t.name.StartsWith(MenuFeature.OriginalMarkerPrefix) && t.gameObject.tag == "EditorOnly"),
            "replaced menu remembered in EditorOnly marker");
        Check(ReferenceEquals(MenuFeature.OriginalMenu(avatar), variantMenu), "remembered menu wins over prefab source");
        Check(MenuFeature.ReadEdits(avatar).items.All(e => !e.key.StartsWith(MenuFeature.OriginalMarkerPrefix)), "marker is not a menu item");
        Check(MenuFeature.RemoveMenu(avatar, "menu") && ReferenceEquals(avatar.expressionsMenu, variantMenu) &&
            go.transform.Find(MenuFeature.RootName) == null, "remove restores the replaced menu");

        // 古い版で取り出したもの（目印なし）：元を 1 段ずつたどり、作業フォルダの外の最初のメニューを使う
        root = MenuFeature.BuildMenu(avatar, new MenuEdits { extracted = true, originalMenu = paths[variantMenu] }, "menu");
        foreach (var t in root.transform.Cast<Transform>().Where(t => t.name.StartsWith(MenuFeature.OriginalMarkerPrefix)).ToList()) t.SetParent(null, false);
        var mine = new GameObject("Mine").AddComponent<VRCAvatarDescriptor>();
        mine.expressionsMenu = avatar.expressionsMenu;
        var variant = new GameObject("Variant").AddComponent<VRCAvatarDescriptor>();
        variant.expressionsMenu = variantMenu;
        var baseAvatar = new GameObject("Base").AddComponent<VRCAvatarDescriptor>();
        baseAvatar.expressionsMenu = deep;
        // スタブの Object は bool への変換を持つので、参照で比べる
        PrefabUtility.SourceForTests = o => ReferenceEquals(o, avatar) ? mine : ReferenceEquals(o, mine) ? variant : ReferenceEquals(o, variant) ? (UnityEngine.Object)baseAvatar : null;
        Check(ReferenceEquals(MenuFeature.OriginalMenu(avatar), variantMenu), "legacy fallback stops at first menu outside work folders");
        PrefabUtility.SourceForTests = null;

        // 元のメニューが分からないときは消さない
        Check(!MenuFeature.RemoveMenu(avatar, "menu") && go.transform.Find(MenuFeature.RootName) != null &&
            avatar.expressionsMenu != null && !ReferenceEquals(avatar.expressionsMenu, variantMenu), "unresolved original keeps extracted menu");
        Check(MenuFeature.RemoveMenu(avatar, "menu", paths[variantMenu]) && ReferenceEquals(avatar.expressionsMenu, variantMenu),
            "backup path used as last resort");

        // 「その他」を使わない（隠した）状態も、バックアップから戻せる
        var tidy = new MenuEdits { extracted = true, originalMenu = paths[variantMenu] };
        tidy.items.Add(new MenuEdit { key = "0", other = true, order = 0 });
        tidy.items.Add(new MenuEdit { key = MenuFeature.OtherName, label = "Misc", hidden = true, order = -1 });
        root = MenuFeature.BuildMenu(avatar, tidy, "menu");
        var holder = root.transform.Find(MenuFeature.HiddenName);
        Check(root.transform.Find(MenuFeature.OtherName) == null && holder != null && holder.Find(MenuFeature.OtherName) != null &&
            holder.Find(MenuFeature.OtherName).GetComponent<ModularAvatarMenuItem>().label == "Misc", "hidden others restored hidden");
        var others = MenuFeature.ReadEdits(avatar).items.FirstOrDefault(e => e.key == MenuFeature.OtherName);
        Check(others != null && others.hidden && others.label == "Misc", "others hidden state captured");
        var oldTidy = new MenuEdits { extracted = true, originalMenu = paths[variantMenu] };
        oldTidy.items.Add(new MenuEdit { key = "0", other = true, order = 0 });
        root = MenuFeature.BuildMenu(avatar, oldTidy, "menu");
        Check(root.transform.Find(MenuFeature.OtherName) != null, "old backups without others entry keep others visible");

        AssetDatabase.PathForTests = null;
        AssetDatabase.LoadForTests = null;
        AssetDatabase.GuidForTests = null;
        AssetDatabase.PathFromGuidForTests = null;
        AssetDatabase.CreateForTests = null;
    }

    public static void Main()
    {
        ScriptableObject.CreateForTests = t => (ScriptableObject)System.Activator.CreateInstance(t);
        var root = new GameObject("Avatar");
        var avatar = root.AddComponent<VRCAvatarDescriptor>();
        var visible = Child(root, "Visible", true);
        var hidden = Child(root, "Hidden", false);
        var backup = new BackupService();
        Set(backup, "_current", new BackupSnapshot());
        Set(backup, "_sessionKey", BackupService.AvatarId(avatar) + "|toggle");
        var ctx = new FeatureContext { Avatar = avatar, FeatureId = "toggle", Backup = backup };
        Call("CreateMenu", ctx, Candidates(visible, hidden));
        Check(visible.activeSelf && !hidden.activeSelf, "create preserves both activeSelf values");
        var data = ToggleFeature.ReadMenu(avatar);
        Check(data.items.Count == 2 && data.items[0].isDefault && !data.items[1].isDefault, "initial menu defaults follow visibility");
        Check(data.items.All(i => i.hasOriginalActive) && data.items[0].originalActive && !data.items[1].originalActive,
            "original visibility persists in menu data");
        var menu = root.transform.Find(ToggleFeature.MenuObjectName);
        var off = menu.GetComponent<ModularAvatarObjectToggle>();
        Check(off.Objects.Count == 2 && off.Objects.All(o => !o.Active), "explicit MA off state for visible objects");
        Check(menu.GetComponentsInChildren<ModularAvatarObjectToggle>(true).Skip(1).All(t => t.Objects.All(o => o.Active)),
            "menu items override off state when enabled");
        Check(menu.GetComponentsInChildren<Transform>(true).Where(t => t.name == "KaihenNavi_OriginalVisibility")
            .All(t => t.gameObject.tag == "EditorOnly"), "visibility markers are EditorOnly");

        // 既定値やシーンの表示を後から変えても、削除では作成前へ戻す。
        visible.SetActive(false);
        hidden.SetActive(true);
        Call("CreateMenu", ctx, Candidates(visible, hidden));
        data = ToggleFeature.ReadMenu(avatar);
        Check(!visible.activeSelf && hidden.activeSelf, "rebuild preserves current scene visibility");
        Check(data.items[0].isDefault && !data.items[1].isDefault, "rebuild preserves menu defaults");
        Check(data.items[0].originalActive && !data.items[1].originalActive, "rebuild retains first original state");
        // チェックを外した項目も削除として復元する。
        Call("CreateMenu", ctx, Candidates(visible));
        Check(!hidden.activeSelf && !visible.activeSelf, "removed rebuild target restored; retained target untouched");
        menu = root.transform.Find(ToggleFeature.MenuObjectName);
        menu.GetComponentsInChildren<ModularAvatarMenuItem>(true).Last().isDefault = false;
        var recorded = new List<UnityEngine.Object>();
        Undo.RecordForTests = o => recorded.Add(o);
        Call("RemoveMenu", avatar, "remove", true);
        Check(visible.activeSelf && !hidden.activeSelf, "remove restores original independent of default");
        Check(recorded.Contains(visible), "restored visibility is registered for Undo");
        Check(root.transform.Find(ToggleFeature.MenuObjectName) == null, "whole menu removed");

        // 元の表示を持たない古いバックアップとの互換性。
        var legacy = new MenuData();
        legacy.items.Add(new MenuItemData { label = "Legacy", targetPath = HierarchyPath.Get(root.transform, visible.transform), isDefault = true });
        ToggleFeature.BuildMenu(avatar, legacy, "restore");
        Check(ToggleFeature.ReadMenu(avatar).items[0].originalActive, "legacy backup uses old default as fallback");
        visible.SetActive(false);
        Call("RemoveMenu", avatar, "remove", true);
        Check(visible.activeSelf, "legacy hidden object recovered");
        legacy.items[0].targetPath = "Missing#0";
        ToggleFeature.BuildMenu(avatar, legacy, "restore");
        Call("RemoveMenu", avatar, "remove", true);
        Check(root.transform.Find(ToggleFeature.MenuObjectName) == null, "menu with missing targets can be removed");

        var applied = new List<InteractionMode>();
        var collapsed = new List<int>();
        PrefabUtility.RootForTests = root;
        PrefabUtility.PathForTests = WorkFolders.AvatarsRoot + "/Test/Avatar.prefab";
        PrefabUtility.OverridesForTests = true;
        PrefabUtility.ApplyForTests = (go, mode) => applied.Add(mode);
        Undo.CollapseForTests = group => collapsed.Add(group);
        Undo.IncrementCurrentGroup();
        var editGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("test edit");
        ctx.CompleteEdit(editGroup);
        Check(applied.SequenceEqual(new[] { InteractionMode.UserAction }), "save records Prefab Undo");
        Check(collapsed.Last() == editGroup && Undo.GetCurrentGroupName() == "test edit", "save collapses into original edit");
        applied.Clear();
        PrefabUtility.PathForTests = "Assets/Purchased/Original.prefab";
        Check(!MyAvatar.Save(avatar) && applied.Count == 0, "purchased Prefab is never saved");
        PrefabUtility.PathForTests = WorkFolders.AvatarsRoot + "/Test/Avatar.prefab";
        var window = new KaihenNaviWindow();
        Set(window, "_avatar", avatar);
        Set(window, "_afterUndoRedo", true);
        typeof(KaihenNaviWindow).GetMethod("SaveNow", Private).Invoke(window, new object[] { false });
        Check(applied.Count == 0, "lifecycle save after Undo does not clear Redo");
        typeof(KaihenNaviWindow).GetMethod("AutoSaveTick", Private).Invoke(window, null);
        Check(applied.Count == 0, "timer after Undo does not clear Redo");
        typeof(KaihenNaviWindow).GetMethod("OnRecordedModifications", Private).Invoke(window,
            new object[] { new[] { new UndoPropertyModification { currentValue = new PropertyModification { target = visible } } } });
        Check(!(bool)typeof(KaihenNaviWindow).GetField("_afterUndoRedo", Private).GetValue(window), "actual editing resumes auto save");
        var accessory = Child(root, "Accessory", true);
        Child(accessory, "Mesh", true);
        Set(backup, "_sessionKey", BackupService.AvatarId(avatar) + "|transform");
        ctx.FeatureId = "transform";
        var destroyed = new List<UnityEngine.Object>();
        Undo.DestroyForTests = o => destroyed.Add(o);
        ctx.Rebuild = () => { };
        typeof(TransformFeature).GetMethod("Remove", Static).Invoke(null, new object[] { ctx, accessory });
        Check(destroyed.Contains(accessory) && accessory.transform.parent == null, "remove accessory uses Undo on whole root");
        Check(visible.transform.parent == root.transform && hidden.transform.parent == root.transform,
            "remove leaves other avatar parts intact");
        Check(applied.Last() == InteractionMode.UserAction, "accessory removal saves with Undo");
        applied.Clear();
        var slider = (Slider)typeof(TransformFeature).GetMethod("EditSlider", Static).Invoke(null, new object[]
        {
            ctx, visible.transform, "position", -10f, 10f, 0f, "move",
            (Action<float>)(v => visible.transform.localPosition = new Vector3(v, 0f, 0f))
        });
        slider.SendValueForTests(1f);
        var dragGroup = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        slider.SendValueForTests(2f);
        Check(TransformFeature.IsAdjusting && applied.Count == 0, "drag waits to save until release");
        TransformFeature.FinishAdjustment();
        Check(!TransformFeature.IsAdjusting && applied.Count == 1 && collapsed.Last() == dragGroup,
            "whole drag and save collapse into first group");
        slider.SendValueForTests(3f);
        applied.Clear();
        TransformFeature.CancelAdjustment();
        TransformFeature.FinishAdjustment();
        Check(applied.Count == 0 && !TransformFeature.IsAdjusting, "Undo cancels pending slider save");
        MyAvatar.Save(avatar, false);
        Check(applied.Last() == InteractionMode.AutomatedAction, "Undo replay does not register new history");
        MenuTidyChecks();
        Console.WriteLine("Feature edit checks passed (" + count + "). Unity Undo, Prefab imports and MA preview require Unity.");
    }
}
'''
with tempfile.TemporaryDirectory(prefix="kaihennavi-edits-") as temporary:
    temp = Path(temporary)
    harness = temp / "Checks.cs"
    harness.write_text(source, encoding="utf-8")
    executable = temp / "Checks.exe"
    sources = sorted((repo / "Packages/com.suzuki-y0.jeni.kaihen-navi/Editor").rglob("*.cs"))
    subprocess.run([shutil.which("mcs") or "mcs", "-nologo", "-langversion:7", "-define:KAIHENNAVI_MA", "-r:System.Core.dll",
                    "-out:" + str(executable), str(repo / "tools/stubs/UnityStubs.cs"),
                    str(repo / "tools/stubs/VrcAndMaStubs.cs"), *map(str, sources), str(harness)], check=True)
    subprocess.run([shutil.which("mono") or "mono", str(executable)], cwd=temp, check=True)
