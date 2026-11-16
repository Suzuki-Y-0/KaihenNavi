using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 旧構成（Assets/KaihenNavi/{Avatars,Bases,Ledger}）を新構成（Assets/〈アバター〉_kaihen/{Variant,KaihenNavi}）へ移す「Variant へ移す」。
    /// ・Avatars/〈名前〉/ → 〈ルート〉/Variant/〈アバター〉_〈名前〉/（Prefab も同じ名前に。中の Backups は 〈ルート〉/KaihenNavi/Backups/〈フォルダ名〉）
    /// ・Bases/〈元アバター〉/〈元アバター〉 Base.prefab → 〈ルート〉/Variant/〈アバター〉_Variant_Base.prefab
    /// ・Ledger → 〈ルート〉/KaihenNavi/Ledger、README も同じ場所へ
    /// どれも MoveAsset なので GUID は変わらず、シーンや Prefab の参照は切れない。ファイルに書いたパス（avatar.json・台帳・
    /// 取り込みの Undo の控え・履歴とインナーカラーの控えの JSON）は書き換える。Ctrl+Z 1 回で戻る（LayoutMigrationUndo）。
    /// 計画（MakePlan）とパスの書き換え（MapPath・MapJson）は System.IO だけで、Unity なしで確かめられる（tools/tests/LayoutTests.cs）。
    /// </summary>
    internal static class LayoutMigration
    {
        /// <summary>旧構成の改変アバター（Avatars/〈名前〉/ のフォルダと、その中の改変アバターの Prefab）。</summary>
        internal sealed class LegacyAvatar
        {
            public string folder;
            public string prefab;
            public string originalPath;
            public string originalName;
        }

        /// <summary>旧構成のベース（Bases/〈元アバター〉/〈元アバター〉 Base.prefab）。</summary>
        internal sealed class LegacyBase
        {
            public string prefab;
            public string originalPath;
            public string originalName;
        }

        /// <summary>移す計画。順番どおりに実行し、逆順で戻す。</summary>
        [Serializable]
        internal sealed class Plan
        {
            /// <summary>作るフォルダ（親から順）。</summary>
            public List<string> createFolders = new List<string>();
            /// <summary>移動（ファイルかフォルダ）。前の移動の結果のパスを使うことがあるので順番どおりに。</summary>
            public List<SortMove> moves = new List<SortMove>();
            /// <summary>消すファイル（ベースの avatar.json・行き先にもう README がある旧 README）。中身を控えてから消す。</summary>
            public List<string> deleteFiles = new List<string>();
            /// <summary>空になったら消すフォルダ（子から順）。</summary>
            public List<string> deleteFolders = new List<string>();
            /// <summary>avatar.json を書き換える改変アバター（from = 前の Prefab のパス、to = 移した後）。</summary>
            public List<SortMove> manifests = new List<SortMove>();
            /// <summary>README を置く内部データのフォルダ。</summary>
            public List<string> dataFolders = new List<string>();
            /// <summary>移さずに残すもの（改変アバターの入っていないフォルダなど）。</summary>
            public List<string> leftovers = new List<string>();
            public int avatars;
            public int bases;
        }

        // ---------------------------------------------------------------- 計画（System.IO だけ）

        /// <summary>
        /// 計画を作る。ルートはアバターごとに ImportSorter.ChooseAvatarRoot（振り分けと同じ決め方）。exists はディスクにあるか。
        /// </summary>
        internal static Plan MakePlan(IList<LegacyAvatar> avatars, IList<LegacyBase> bases, bool hasLedger, bool hasReadme,
            IList<string> roots, string saved, Func<string, bool> exists)
        {
            var plan = new Plan();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedRoots = new List<string>();
            Func<string, bool> used = p => exists(p) || taken.Contains(p);
            Action<string> ensure = null;
            ensure = folder =>
            {
                if (exists(folder) || plan.createFolders.Contains(folder)) return;
                var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                if (parent != "Assets") ensure(parent);
                plan.createFolders.Add(folder);
            };
            Func<string, string, string> rootFor = (originalPath, avatarName) =>
            {
                var root = ImportSorter.ChooseAvatarRoot(originalPath, avatarName, roots, saved);
                if (!usedRoots.Contains(root)) usedRoots.Add(root);
                ensure(WorkFolders.VariantFolder(root));
                return root;
            };

            foreach (var legacy in bases ?? new LegacyBase[0])
            {
                var avatarName = ImportSorter.AvatarNameFromPath(legacy.originalPath, WorkFolders.Sanitize(legacy.originalName));
                var root = rootFor(legacy.originalPath, avatarName);
                var target = UniqueFile(WorkFolders.VariantFolder(root) + "/" + WorkFolders.Sanitize(avatarName) +
                    WorkFolders.VariantBaseSuffix + ".prefab", used);
                taken.Add(target);
                plan.moves.Add(new SortMove { from = legacy.prefab, to = target });
                var folder = Path.GetDirectoryName(legacy.prefab).Replace('\\', '/');
                // 新構成のベースは avatar.json を使わない（親の Prefab で見分ける）
                if (exists(folder + "/" + WorkFolders.ManifestName)) plan.deleteFiles.Add(folder + "/" + WorkFolders.ManifestName);
                if (folder != WorkFolders.BasesRoot && !plan.deleteFolders.Contains(folder)) plan.deleteFolders.Add(folder);
                plan.bases++;
            }

            foreach (var legacy in avatars ?? new LegacyAvatar[0])
            {
                var avatarName = ImportSorter.AvatarNameFromPath(legacy.originalPath, WorkFolders.Sanitize(legacy.originalName));
                var root = rootFor(legacy.originalPath, avatarName);
                var target = UniqueFolder(WorkFolders.VariantFolder(root) + "/" +
                    WorkFolders.FolderName(avatarName, Path.GetFileName(legacy.folder)), used);
                taken.Add(target);
                var name = Path.GetFileName(target);
                // バックアップは内部データへ（フォルダごと移す前に）
                if (exists(legacy.folder + "/Backups"))
                {
                    var backups = UniqueFolder(WorkFolders.BackupsFolderFor(target), used);
                    taken.Add(backups);
                    ensure(Path.GetDirectoryName(backups).Replace('\\', '/'));
                    plan.moves.Add(new SortMove { from = legacy.folder + "/Backups", to = backups });
                }
                plan.moves.Add(new SortMove { from = legacy.folder, to = target });
                // Prefab の名前もフォルダにそろえる（〈アバター〉_〈改変名〉.prefab。AvatarPrefabMaterialExporter と同じ形）
                var prefab = target + "/" + Path.GetFileName(legacy.prefab);
                if (Path.GetFileNameWithoutExtension(legacy.prefab) == Path.GetFileName(legacy.folder) &&
                    Path.GetFileName(legacy.folder) != name && !exists(legacy.folder + "/" + name + ".prefab"))
                {
                    plan.moves.Add(new SortMove { from = prefab, to = target + "/" + name + ".prefab" });
                    prefab = target + "/" + name + ".prefab";
                }
                if (exists(legacy.folder + "/" + WorkFolders.ManifestName)) plan.manifests.Add(new SortMove { from = legacy.prefab, to = prefab });
                plan.avatars++;
            }

            // 台帳はプロジェクトで 1 つ：振り分けのルート（1 つだけか選んだもの）、無ければ今回使った最初のルート
            var dataRoot = ImportSorter.ChooseRoot(roots, saved) ?? usedRoots.FirstOrDefault();
            if (hasLedger)
            {
                var target = dataRoot == null ? null : WorkFolders.DataFolder(dataRoot) + "/Ledger";
                if (target == null || used(target)) plan.leftovers.Add(WorkFolders.Root + "/Ledger");
                else
                {
                    ensure(WorkFolders.DataFolder(dataRoot));
                    taken.Add(target);
                    plan.moves.Add(new SortMove { from = WorkFolders.Root + "/Ledger", to = target });
                }
            }
            if (hasReadme && dataRoot != null && (plan.moves.Count > 0))
            {
                var target = WorkFolders.DataFolder(dataRoot) + "/README.md";
                ensure(WorkFolders.DataFolder(dataRoot));
                if (used(target)) plan.deleteFiles.Add(WorkFolders.Root + "/README.md");
                else
                {
                    taken.Add(target);
                    plan.moves.Add(new SortMove { from = WorkFolders.Root + "/README.md", to = target });
                }
            }
            if (dataRoot != null && !usedRoots.Contains(dataRoot) && plan.moves.Count > 0) usedRoots.Add(dataRoot);
            foreach (var root in usedRoots) plan.dataFolders.Add(WorkFolders.DataFolder(root));
            // 空になった旧構成のフォルダを片付ける（残り物があれば残す）
            foreach (var folder in new[] { WorkFolders.AvatarsRoot, WorkFolders.BasesRoot, WorkFolders.Root })
                if (!plan.deleteFolders.Contains(folder)) plan.deleteFolders.Add(folder);
            return plan;
        }

        /// <summary>フォルダの空いている名前（AssetDatabase.GenerateUniqueAssetPath と同じ「名前 1」）。</summary>
        internal static string UniqueFolder(string path, Func<string, bool> used)
        {
            if (!used(path)) return path;
            for (var i = 1; ; i++)
                if (!used(path + " " + i)) return path + " " + i;
        }

        /// <summary>ファイルの空いている名前（「名前 1.prefab」）。</summary>
        internal static string UniqueFile(string path, Func<string, bool> used)
        {
            if (!used(path)) return path;
            var extension = Path.GetExtension(path);
            var stem = path.Substring(0, path.Length - extension.Length);
            for (var i = 1; ; i++)
                if (!used(stem + " " + i + extension)) return stem + " " + i + extension;
        }

        // ---------------------------------------------------------------- パスの書き換え（System.IO だけ）

        /// <summary>移動に合わせてパスを書き換える。移動を順に重ねて当てる（フォルダを移した後に中の Prefab の名前を変える、など）。</summary>
        internal static string MapPath(string path, IList<SortMove> moves, bool forward)
        {
            if (string.IsNullOrEmpty(path) || moves == null) return path;
            if (forward) for (var i = 0; i < moves.Count; i++) path = MapOne(path, moves[i].from, moves[i].to);
            else for (var i = moves.Count - 1; i >= 0; i--) path = MapOne(path, moves[i].to, moves[i].from);
            return path;
        }

        private static string MapOne(string path, string from, string to)
        {
            if (path == from) return to;
            if (path == from + ".meta") return to + ".meta";
            if (path.StartsWith(from + "/", StringComparison.Ordinal)) return to + path.Substring(from.Length);
            return path;
        }

        /// <summary>
        /// JSON（JsonUtility の出力）の中の、移したパスとその中のパスを書き換える。パスは文字列の値として "Assets/…" の形で入っている。
        /// 移動を順に重ねて当てる（MapPath と同じ）。
        /// </summary>
        internal static string MapJson(string json, IList<SortMove> moves, bool forward)
        {
            if (string.IsNullOrEmpty(json) || moves == null) return json;
            Func<string, string, string, string> one = (text, from, to) =>
                text.Replace("\"" + from + "\"", "\"" + to + "\"").Replace("\"" + from + "/", "\"" + to + "/");
            if (forward) for (var i = 0; i < moves.Count; i++) json = one(json, moves[i].from, moves[i].to);
            else for (var i = moves.Count - 1; i >= 0; i--) json = one(json, moves[i].to, moves[i].from);
            return json;
        }

        // ---------------------------------------------------------------- プロジェクトから計画を作る・実行する

        /// <summary>旧構成に移すもの（改変アバター・ベース・台帳）があるか。ページを開くたびに呼ぶので、ファイルの有無だけを見る。</summary>
        internal static bool HasLegacy()
        {
            if (!Directory.Exists(WorkFolders.Root)) return false;
            if (File.Exists(WorkFolders.Root + WorkFolders.LedgerRelative)) return true;
            return Directory.Exists(WorkFolders.AvatarsRoot) && Directory.GetFiles(WorkFolders.AvatarsRoot, "*.prefab", SearchOption.AllDirectories)
                       .Any(p => MyAvatar.IsMinePath(p.Replace('\\', '/'))) ||
                   Directory.Exists(WorkFolders.BasesRoot) && Directory.GetFiles(WorkFolders.BasesRoot, "*.prefab", SearchOption.AllDirectories).Any();
        }

        /// <summary>今のプロジェクトの旧構成から計画を作る（Prefab を読んで元アバターを調べる）。</summary>
        internal static Plan PlanForProject()
        {
            var avatars = new List<LegacyAvatar>();
            var leftovers = new List<string>();
            if (Directory.Exists(WorkFolders.AvatarsRoot))
            {
                foreach (var directory in Directory.GetDirectories(WorkFolders.AvatarsRoot).Select(d => d.Replace('\\', '/')).OrderBy(d => d, StringComparer.Ordinal))
                {
                    var prefabs = Directory.GetFiles(directory, "*.prefab").Select(p => p.Replace('\\', '/'))
                        .Where(p => MyAvatar.IsMinePath(p) && IsAvatar(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                    if (prefabs.Count == 0)
                    {
                        // 改変アバターにする前に複製したマテリアルだけのフォルダなど。シーンのアバターが旧構成のまま使う
                        leftovers.Add(directory);
                        continue;
                    }
                    var primary = prefabs.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == Path.GetFileName(directory)) ?? prefabs[0];
                    var original = MyAvatar.OriginalOf(AssetDatabase.LoadAssetAtPath<GameObject>(primary));
                    avatars.Add(new LegacyAvatar
                    {
                        folder = directory, prefab = primary,
                        originalPath = original == null ? null : AssetDatabase.GetAssetPath(original),
                        originalName = original == null ? Path.GetFileName(directory) : original.name,
                    });
                }
            }
            var bases = new List<LegacyBase>();
            if (Directory.Exists(WorkFolders.BasesRoot))
            {
                foreach (var file in Directory.GetFiles(WorkFolders.BasesRoot, "*.prefab", SearchOption.AllDirectories)
                             .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                    var original = prefab == null ? null : PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                    bases.Add(new LegacyBase
                    {
                        prefab = file,
                        originalPath = original == null ? null : AssetDatabase.GetAssetPath(original),
                        originalName = original == null ? Path.GetFileNameWithoutExtension(file).Replace(WorkFolders.BaseSuffix, "") : original.name,
                    });
                }
            }
            var plan = MakePlan(avatars, bases, File.Exists(WorkFolders.Root + WorkFolders.LedgerRelative), File.Exists(WorkFolders.Root + "/README.md"),
                ImportSorter.FindRoots(), ImportSorter.SavedRoot, p => File.Exists(p) || Directory.Exists(p));
            plan.leftovers.AddRange(leftovers);
            return plan;
        }

        private static bool IsAvatar(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && prefab.GetComponent<VRCAvatarDescriptor>() != null;
        }

        /// <summary>計画を実行する（Ctrl+Z 1 回で戻る）。失敗したら途中までを戻して理由を返す。</summary>
        internal static string Run(Plan plan)
        {
            if (plan == null || plan.moves.Count == 0) return null;
            return LayoutMigrationUndo.Run(plan, L10n.Tr("undo.layoutMigrate"));
        }

        // ---------------------------------------------------------------- 中に書いたパスの書き換え（Unity）

        /// <summary>
        /// 履歴（BackupSnapshot の機能ごとの JSON）とインナーカラーの控え（InnerColorState）に書いたパスを、移動に合わせて書き換える。
        /// どちらもオブジェクトの参照ではなくパスの文字列で持っているため（Prefab・マテリアルの参照は GUID なので書き換え不要）。
        /// 改変アバターの名前の変更（MyAvatar.Rename）でも使う。
        /// </summary>
        internal static void FollowStatePaths(IList<SortMove> moves, bool forward)
        {
            if (moves == null || moves.Count == 0) return;
            var data = WorkFolders.DataFolders();
            if (data.Length > 0)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:BackupSnapshot", data))
                {
                    var snapshot = AssetDatabase.LoadAssetAtPath<BackupSnapshot>(AssetDatabase.GUIDToAssetPath(guid));
                    if (snapshot == null || snapshot.extras == null) continue;
                    var changed = false;
                    foreach (var extra in snapshot.extras)
                    {
                        var next = MapJson(extra.json, moves, forward);
                        if (next == extra.json) continue;
                        extra.json = next;
                        changed = true;
                    }
                    if (!changed) continue;
                    EditorUtility.SetDirty(snapshot);
                    AssetDatabase.SaveAssetIfDirty(snapshot);
                }
            }
            var tool = WorkFolders.ToolFolders();
            if (tool.Length > 0)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:InnerColorState", tool))
                {
                    var state = AssetDatabase.LoadAssetAtPath<InnerColorState>(AssetDatabase.GUIDToAssetPath(guid));
                    if (state == null || state.data == null) continue;
                    var json = JsonUtility.ToJson(state.data);
                    var next = MapJson(json, moves, forward);
                    if (next == json) continue;
                    state.data = JsonUtility.FromJson<InnerColorState.Data>(next) ?? state.data;
                    state.Normalize();
                    EditorUtility.SetDirty(state);
                    AssetDatabase.SaveAssetIfDirty(state);
                }
            }
            BackupService.InvalidateCache();
        }
    }
}
