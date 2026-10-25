#!/usr/bin/env python3
"""脱ぎ着の状態復元と保存の Undo API 選択を確認する。Unity の Undo 自体や MA のプレビューは再現しない。"""
from pathlib import Path
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
    subprocess.run(["mcs", "-nologo", "-langversion:7", "-define:KAIHENNAVI_MA", "-r:System.Core.dll",
                    "-out:" + str(executable), str(repo / "tools/stubs/UnityStubs.cs"),
                    str(repo / "tools/stubs/VrcAndMaStubs.cs"), *map(str, sources), str(harness)], check=True)
    subprocess.run(["mono", str(executable)], cwd=temp, check=True)
