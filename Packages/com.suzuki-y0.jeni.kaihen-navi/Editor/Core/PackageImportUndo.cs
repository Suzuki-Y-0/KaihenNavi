using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// Unity 標準の取り込みにはファイルの Undo がないため、追加ファイルと台帳を Library に控える。
    /// 元アセットへの上書きは事前に拒否し、Ctrl+Z / やり直しで追加分だけを戻す。
    /// </summary>
    [InitializeOnLoad]
    internal static class PackageImportUndo
    {
        private const string PendingKey = "KaihenNavi.PackageImportUndo.Pending";
        [Serializable] internal sealed class FileState { public string path; public bool before; public bool after; }
        [Serializable] internal sealed class Snapshot
        {
            public List<FileState> files = new List<FileState>();
            public List<string> directories = new List<string>();
        }
        private sealed class ArchiveEntry { public string path; public string assetHash; public string metaHash; public string guid; }

        static PackageImportUndo()
        {
            AssetDatabase.importPackageCompleted += name =>
            {
                SessionState.SetString(PendingKey + ".finished", "true");
                EditorApplication.delayCall += () => Complete(name);
            };
            // スクリプトの取り込みで、完了通知と遅延処理の間にドメインリロードされた場合も続ける。
            if (SessionState.GetString(PendingKey + ".finished", "") == "true")
                EditorApplication.delayCall += () => Complete("");
            AssetDatabase.importPackageCancelled += name => Cancel();
            AssetDatabase.importPackageFailed += (name, error) => Cancel();
            Undo.undoRedoPerformed += Restore;
        }

        public static bool Begin(string package)
        {
            if (SessionState.GetString(PendingKey, "").Length > 0) return false;
            try
            {
                var entries = ReadArchive(package);
                var snapshot = new Snapshot();
                foreach (var entry in entries)
                {
                    ValidatePath(entry.path);
                    ValidatePath(entry.path + ".meta");
                    // 同名の元アセットを置き換える取り込みは実行しない。
                    var existingPath = string.IsNullOrEmpty(entry.guid) ? null : AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(existingPath) && existingPath != entry.path)
                        throw new IOException("Existing GUID at another path: " + existingPath);
                    CheckExisting(entry.path, entry.assetHash);
                    CheckExisting(entry.path + ".meta", entry.metaHash);
                    Add(snapshot, entry.path);
                    Add(snapshot, entry.path + ".meta");
                }
                Add(snapshot, ItemLedger.LedgerFile);
                Add(snapshot, ItemLedger.LedgerFile + ".meta");
                Add(snapshot, WorkFolders.Root + "/README.md");
                Add(snapshot, WorkFolders.Root + "/README.md.meta");
                var directory = Path.Combine("Library", "KaihenNavi", "Imports", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                for (var i = 0; i < snapshot.files.Count; i++)
                {
                    var file = snapshot.files[i];
                    ValidatePath(file.path);
                    file.before = File.Exists(file.path);
                    if (file.before && IsManaged(file.path)) File.Copy(file.path, Path.Combine(directory, i + ".before"));
                }
                File.WriteAllText(Path.Combine(directory, "state.json"), JsonUtility.ToJson(snapshot), Encoding.UTF8);
                SessionState.SetString(PendingKey, directory);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KaihenNavi] " + e.Message);
                EditorUtility.DisplayDialog(L10n.Tr("app.name"), L10n.Tr("library.importBlocked"), L10n.Tr("dialog.ok"));
                return false;
            }
        }

        private static void CheckExisting(string path, string expected)
        {
            if (!File.Exists(path)) return;
            using (var stream = File.OpenRead(path))
                if (expected == null || Hash(stream) != expected) throw new IOException("Existing asset: " + path);
        }

        private static void Add(Snapshot snapshot, string path)
        {
            if (!snapshot.files.Any(f => f.path == path)) snapshot.files.Add(new FileState { path = path });
            for (var parent = Path.GetDirectoryName(path).Replace('\\', '/'); parent != "Assets" && parent.Length > 0;
                parent = Path.GetDirectoryName(parent).Replace('\\', '/'))
            {
                if (!Directory.Exists(parent) && !snapshot.directories.Contains(parent)) snapshot.directories.Add(parent);
                var meta = parent + ".meta";
                if (!snapshot.files.Any(f => f.path == meta)) snapshot.files.Add(new FileState { path = meta });
            }
        }

        private static void Complete(string name)
        {
            var folder = SessionState.GetString(PendingKey, "");
            if (folder.Length == 0) return;
            // 台帳の Completed が先に走り、生成した items.json も同じ1手で戻す。
            try
            {
                var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path.Combine(folder, "state.json")));
                for (var i = 0; i < snapshot.files.Count; i++)
                {
                    var file = snapshot.files[i];
                    file.after = File.Exists(file.path);
                    if (file.after && (!file.before || IsManaged(file.path))) File.Copy(file.path, Path.Combine(folder, i + ".after"));
                }
                File.WriteAllText(Path.Combine(folder, "state.json"), JsonUtility.ToJson(snapshot), Encoding.UTF8);
                var changed = snapshot.files.Where((f, i) => f.before != f.after ||
                    (f.before && IsManaged(f.path) && !File.ReadAllBytes(Path.Combine(folder, i + ".before"))
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(folder, i + ".after"))))).Any();
                if (!changed) { Directory.Delete(folder, true); return; }
                var state = ScriptableObject.CreateInstance<PackageImportState>();
                state.hideFlags = HideFlags.HideAndDontSave;
                state.folder = folder;
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(L10n.Tr("library.import"));
                Undo.RecordObject(state, L10n.Tr("library.import"));
                state.applied = true;
                Undo.FlushUndoRecordObjects();
                SessionState.SetString(folder, "applied");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception e) { Debug.LogError("[KaihenNavi] Import Undo: " + e.Message); }
            finally { SessionState.EraseString(PendingKey); SessionState.EraseString(PendingKey + ".finished"); }
        }

        public static void Cancel()
        {
            // 部分取り込み後の失敗も、追加されたファイルを取り消せる記録として残す。
            Complete("");
        }

        private static void Restore()
        {
            var changed = false;
            foreach (var state in Resources.FindObjectsOfTypeAll<PackageImportState>())
            {
                if (string.IsNullOrEmpty(state.folder)) continue;
                if (SessionState.GetString(state.folder, "applied") == (state.applied ? "applied" : "undone")) continue;
                try
                {
                    var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path.Combine(state.folder, "state.json")));
                    Apply(snapshot, state.folder, state.applied);
                    SessionState.SetString(state.folder, state.applied ? "applied" : "undone");
                    changed = true;
                }
                catch (Exception e) { Debug.LogError("[KaihenNavi] Import Undo: " + e.Message); }
            }
            if (changed)
            {
                AssetDatabase.Refresh();
                ItemLedger.Invalidate();
            }
        }

        internal static void Apply(Snapshot snapshot, string folder, bool after)
        {
            if (after)
                foreach (var directory in snapshot.directories)
                {
                    ValidatePath(directory);
                    Directory.CreateDirectory(directory);
                }
            for (var i = 0; i < snapshot.files.Count; i++)
            {
                var file = snapshot.files[i];
                ValidatePath(file.path);
                // 取り込み前からあった配布ファイルには、Undo / Redo 時にも書き込まない。
                if (file.before && !IsManaged(file.path)) continue;
                if (after ? file.after : file.before)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.path));
                    File.Copy(Path.Combine(folder, i + (after ? ".after" : ".before")), file.path, true);
                }
                else if (File.Exists(file.path)) File.Delete(file.path);
            }
            if (!after)
                foreach (var directory in snapshot.directories.OrderByDescending(p => p.Length))
                {
                    ValidatePath(directory);
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
        }

        private static bool IsManaged(string path)
        {
            return path == ItemLedger.LedgerFile || path == ItemLedger.LedgerFile + ".meta" ||
                path == WorkFolders.Root + "/README.md" || path == WorkFolders.Root + "/README.md.meta";
        }

        internal static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Contains('\\') || path.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                throw new InvalidDataException("Invalid package path.");
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked package path.");
        }

        /// <summary>gzip/tar の pathname と内容のハッシュだけを読む。外部アーカイブは展開・変更しない。</summary>
        private static List<ArchiveEntry> ReadArchive(string path)
        {
            var entries = new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal);
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                var header = new byte[512];
                var buffer = new byte[8192];
                while (true)
                {
                    ReadExactly(gzip, header, header.Length);
                    if (header.All(b => b == 0)) break;
                    var name = Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0');
                    var size = Convert.ToInt64(Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' '), 8);
                    var parts = name.Split('/');
                    ArchiveEntry entry = null;
                    if (parts.Length == 2)
                    {
                        if (!entries.TryGetValue(parts[0], out entry)) entries[parts[0]] = entry = new ArchiveEntry();
                    }
                    using (var hash = SHA256.Create())
                    using (var pathname = new MemoryStream())
                    {
                        var left = size;
                        while (left > 0)
                        {
                            var count = (int)Math.Min(buffer.Length, left);
                            ReadExactly(gzip, buffer, count);
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                            if (entry != null && (parts[1] == "pathname" || parts[1] == "asset.meta"))
                            {
                                if (size > 4 * 1024 * 1024) throw new InvalidDataException("Long package path.");
                                pathname.Write(buffer, 0, count);
                            }
                            left -= count;
                        }
                        hash.TransformFinalBlock(buffer, 0, 0);
                        if (entry != null)
                        {
                            if (parts[1] == "pathname") entry.path = Encoding.UTF8.GetString(pathname.ToArray()).TrimEnd('\0', '\n', '\r');
                            if (parts[1] == "asset") entry.assetHash = Convert.ToBase64String(hash.Hash);
                            if (parts[1] == "asset.meta")
                            {
                                entry.metaHash = Convert.ToBase64String(hash.Hash);
                                var match = Regex.Match(Encoding.UTF8.GetString(pathname.ToArray()), @"(?m)^guid: ([a-fA-F0-9]{32})\r?$");
                                if (match.Success) entry.guid = match.Groups[1].Value;
                            }
                        }
                    }
                    var padding = (int)((512 - size % 512) % 512);
                    ReadExactly(gzip, header, padding);
                }
            }
            var result = entries.Values.Where(e => e.path != null).ToList();
            if (result.Count == 0) throw new InvalidDataException("Empty package.");
            return result;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static string Hash(Stream stream)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream));
        }
    }
}
