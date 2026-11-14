#if KAIHENNAVI_MA
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// アバター内の全メッシュの Anchor Override と Bounds を、Modular Avatar の MeshSettings でそろえる。
    /// ルートに 1 つだけ設定を置き、子の設定は Inherit にしてルートに従わせる。
    /// </summary>
    internal static class MeshSettingsHelper
    {
        /// <summary>レンダラーで実際に使われる Anchor Override（MA と同じ解決順）。</summary>
        internal static Transform EffectiveAnchor(Transform avatarRoot, Renderer renderer)
        {
            if (renderer == null) return null;
            var fallback = ProbeAnchorOf(renderer);
            for (var t = renderer.transform; t != null; t = t.parent)
            {
                var settings = t.GetComponent<ModularAvatarMeshSettings>();
                if (settings != null)
                {
                    var mode = settings.InheritProbeAnchor;
                    if (mode == ModularAvatarMeshSettings.InheritMode.DontSet) return fallback;
                    if (mode == ModularAvatarMeshSettings.InheritMode.Set) return Resolve(settings, settings.ProbeAnchor);
                    // SetOrInherit は、参照が空なら親の設定へ進む（MA と同じ）
                    if (mode == ModularAvatarMeshSettings.InheritMode.SetOrInherit)
                    {
                        var anchor = Resolve(settings, settings.ProbeAnchor);
                        if (anchor != null) return anchor;
                    }
                }

                if (t == avatarRoot) break;
            }

            return fallback;
        }

        /// <summary>そろえたときの Anchor と違うレンダラーの数。</summary>
        internal static int CountMismatched(VRCAvatarDescriptor avatar, out Transform target)
        {
            target = null;
            if (avatar == null) return 0;
            target = ChooseAnchor(avatar);
            var count = 0;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                // パーティクルは Anchor Override を使わない
                if (r is ParticleSystemRenderer) continue;
                // DontSet は意図的に触らないので（Unify と同じく）数えない。数えると警告が 0 にならない
                if (IsDontSetGoverned(avatar.transform, r)) continue;
                if (EffectiveAnchor(avatar.transform, r) != target) count++;
            }

            return count;
        }

        /// <summary>
        /// レンダラーの Anchor が DontSet の MeshSettings で決まっているか（EffectiveAnchor と同じ順に上へたどる）。
        /// Unify はこれらに触れない。
        /// </summary>
        private static bool IsDontSetGoverned(Transform avatarRoot, Renderer renderer)
        {
            if (renderer == null) return false;
            for (var t = renderer.transform; t != null; t = t.parent)
            {
                var settings = t.GetComponent<ModularAvatarMeshSettings>();
                if (settings != null)
                {
                    var mode = settings.InheritProbeAnchor;
                    if (mode == ModularAvatarMeshSettings.InheritMode.DontSet) return true;
                    if (mode == ModularAvatarMeshSettings.InheritMode.Set) return false;
                    if (mode == ModularAvatarMeshSettings.InheritMode.SetOrInherit && Resolve(settings, settings.ProbeAnchor) != null) return false;
                }

                if (t == avatarRoot) break;
            }

            return false;
        }

        /// <summary>
        /// そろえる先の Anchor。MA の設定が付いていない SkinnedMeshRenderer で、
        /// いちばん多く使われている probeAnchor を選ぶ。無ければ Hips（人型でなければルート）。
        /// </summary>
        internal static Transform ChooseAnchor(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            var root = avatar.transform;

            // すでにルートで決めてあれば（前にそろえた・手で置いた）、それに合わせる
            var existing = avatar.GetComponent<ModularAvatarMeshSettings>();
            if (existing != null && existing.InheritProbeAnchor == ModularAvatarMeshSettings.InheritMode.Set)
            {
                var decided = Resolve(existing, existing.ProbeAnchor);
                if (decided != null) return decided;
            }

            Transform best = null;
            var bestCount = 0;
            var counts = new Dictionary<Transform, int>();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // レンダラー自身からルート手前までに MeshSettings があれば、その設定に従うので数えない
                if (HasSettingsBelowRoot(root, smr.transform)) continue;
                var anchor = ProbeAnchorOf(smr);
                if (anchor == null) continue;
                int count;
                counts.TryGetValue(anchor, out count);
                count++;
                counts[anchor] = count;
                if (count > bestCount)
                {
                    best = anchor;
                    bestCount = count;
                }
            }

            if (best != null) return best;
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }

            return root;
        }

        /// <summary>全メッシュの Anchor と Bounds をルートの MeshSettings にそろえる。</summary>
        internal static void Unify(VRCAvatarDescriptor avatar, string undoName)
        {
            if (avatar == null) return;
            var root = avatar.transform;
            var anchor = ChooseAnchor(avatar);
            var hips = Hips(avatar);
            var rootBone = hips != null ? hips : root;

            var settings = avatar.GetComponent<ModularAvatarMeshSettings>();
            if (settings == null)
            {
                settings = Undo.AddComponent<ModularAvatarMeshSettings>(avatar.gameObject);
            }
            else
            {
                Undo.RecordObject(settings, undoName);
            }

            settings.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Set;
            settings.ProbeAnchor = Reference(anchor);
            settings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
            settings.RootBone = Reference(rootBone);
            settings.Bounds = ComputeBounds(avatar, rootBone);
            MarkChanged(settings);

            // 子の設定はルートに従わせる（DontSet は意図的なので触らない）
            foreach (var other in avatar.GetComponentsInChildren<ModularAvatarMeshSettings>(true))
            {
                if (other == settings) continue;
                var probe = other.InheritProbeAnchor;
                var bounds = other.InheritBounds;
                var changeProbe = probe == ModularAvatarMeshSettings.InheritMode.Set ||
                                  probe == ModularAvatarMeshSettings.InheritMode.SetOrInherit;
                var changeBounds = bounds == ModularAvatarMeshSettings.InheritMode.Set ||
                                   bounds == ModularAvatarMeshSettings.InheritMode.SetOrInherit;
                if (!changeProbe && !changeBounds) continue;
                Undo.RecordObject(other, undoName);
                if (changeProbe) other.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Inherit;
                if (changeBounds) other.InheritBounds = ModularAvatarMeshSettings.InheritMode.Inherit;
                MarkChanged(other);
            }
        }

        // ---------------------------------------------------------------- 部品

        /// <summary>参照先の Transform（見つからなければ null）。</summary>
        internal static Transform Resolve(Component container, AvatarObjectReference reference)
        {
            if (reference == null) return null;
            var go = reference.Get(container);
            return go == null ? null : go.transform;
        }

        /// <summary>Transform への参照を作る。パスは MA の Set が MA の形式で決める（HierarchyPath の形式とは違う）。</summary>
        internal static AvatarObjectReference Reference(Transform target)
        {
            var reference = new AvatarObjectReference();
            if (target != null) reference.Set(target.gameObject);
            return reference;
        }

        /// <summary>保存しておいたパス（HierarchyPath の形式）から参照を作り直す。</summary>
        internal static AvatarObjectReference Reference(Transform root, string path)
        {
            return Reference(path == null ? null : HierarchyPath.Find(root, path));
        }

        /// <summary>Bounds の大きさ（スタブに size が無いので max と center から求める）。</summary>
        internal static Vector3 BoundsSize(Bounds bounds)
        {
            return (bounds.max - bounds.center) * 2f;
        }

        /// <summary>人型の Hips（無ければ null）。</summary>
        private static Transform Hips(VRCAvatarDescriptor avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return null;
            return animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        /// <summary>レンダラー自身からルート手前までに、Anchor を決める MeshSettings（Set / SetOrInherit）があるか。</summary>
        private static bool HasSettingsBelowRoot(Transform root, Transform t)
        {
            for (var current = t; current != null && current != root; current = current.parent)
            {
                var settings = current.GetComponent<ModularAvatarMeshSettings>();
                if (settings != null && (settings.InheritProbeAnchor == ModularAvatarMeshSettings.InheritMode.Set ||
                    settings.InheritProbeAnchor == ModularAvatarMeshSettings.InheritMode.SetOrInherit)) return true;
            }

            return false;
        }

        /// <summary>箱の i 番目の隅（ビットで x・y・z の低い側と高い側を選ぶ）。</summary>
        private static Vector3 Corner(Vector3 lo, Vector3 hi, int i)
        {
            return new Vector3(
                (i & 1) == 0 ? lo.x : hi.x,
                (i & 2) == 0 ? lo.y : hi.y,
                (i & 4) == 0 ? lo.z : hi.z);
        }

        /// <summary>
        /// SkinnedMeshRenderer の範囲の 8 隅（ワールド）。有効でアクティブなら smr.bounds を使う。
        /// 無効・非アクティブのものは smr.bounds が原点の空箱になることがあるので、
        /// メッシュの範囲を骨（無ければ自身）の localToWorld で変換して求める。
        /// </summary>
        private static Vector3[] WorldCorners(SkinnedMeshRenderer smr)
        {
            var corners = new Vector3[8];
            if (smr.enabled && smr.gameObject.activeInHierarchy)
            {
                var b = smr.bounds;
                for (var i = 0; i < 8; i++) corners[i] = Corner(b.min, b.max, i);
                return corners;
            }

            var mesh = smr.sharedMesh.bounds;
            var matrix = (smr.rootBone != null ? smr.rootBone : smr.transform).localToWorldMatrix;
            for (var i = 0; i < 8; i++) corners[i] = matrix.MultiplyPoint3x4(Corner(mesh.min, mesh.max, i));
            return corners;
        }

        /// <summary>全 SkinnedMeshRenderer のワールド Bounds を、ルートボーンのローカルで包む。メッシュの無いものは数えない。</summary>
        private static Bounds ComputeBounds(VRCAvatarDescriptor avatar, Transform rootBone)
        {
            var has = false;
            var min = Vector3.zero;
            var max = Vector3.zero;
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                foreach (var corner in WorldCorners(smr))
                {
                    var local = rootBone.InverseTransformPoint(corner);
                    if (!has)
                    {
                        min = local;
                        max = local;
                        has = true;
                    }
                    else
                    {
                        min = new Vector3(Mathf.Min(min.x, local.x), Mathf.Min(min.y, local.y), Mathf.Min(min.z, local.z));
                        max = new Vector3(Mathf.Max(max.x, local.x), Mathf.Max(max.y, local.y), Mathf.Max(max.z, local.z));
                    }
                }
            }

            if (!has) return new Bounds(Vector3.zero, Vector3.one * 2f);
            var center = (min + max) * 0.5f;
            // 端がぎりぎりにならないよう、各軸を 10% 広げる
            var size = (max - min) * 1.1f;
            return new Bounds(center, size);
        }

        private static Transform ProbeAnchorOf(Renderer renderer)
        {
            return renderer == null ? null : renderer.probeAnchor;
        }

        private static void MarkChanged(Component component)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(component);
        }
    }

    /// <summary>MeshSettings（Anchor Override と Bounds）をバックアップに含め、「元に戻す」で戻せるようにする。</summary>
    internal sealed class MeshSettingsBackup : IBackupParticipant
    {
        [System.Serializable]
        private sealed class Entry
        {
            public string path;
            public int inheritProbeAnchor;
            public int inheritBounds;
            public string probeAnchor;
            public string rootBone;
            public Vector3 boundsCenter;
            public Vector3 boundsSize;
        }

        [System.Serializable]
        private sealed class Data
        {
            public List<Entry> entries = new List<Entry>();
        }

        public string Key { get { return "mesh-settings"; } }

        public string Capture(VRCAvatarDescriptor avatar)
        {
            var data = new Data();
            if (avatar != null)
            {
                foreach (var settings in avatar.GetComponentsInChildren<ModularAvatarMeshSettings>(true))
                {
                    var bounds = settings.Bounds;
                    data.entries.Add(new Entry
                    {
                        path = HierarchyPath.Get(avatar.transform, settings.transform),
                        inheritProbeAnchor = (int)settings.InheritProbeAnchor,
                        inheritBounds = (int)settings.InheritBounds,
                        probeAnchor = PathOf(avatar.transform, settings, settings.ProbeAnchor),
                        rootBone = PathOf(avatar.transform, settings, settings.RootBone),
                        boundsCenter = bounds.center,
                        boundsSize = MeshSettingsHelper.BoundsSize(bounds),
                    });
                }
            }

            return JsonUtility.ToJson(data);
        }

        public void Restore(VRCAvatarDescriptor avatar, string json, string undoName)
        {
            // この機能より前のバックアップ（記録なし）では何もしない。Setup Outfit が付けた設定まで消さないため。
            // 新しいバックアップは設定が 0 個でも空の一覧を記録するので、そちらは正しく消せる。
            if (avatar == null || string.IsNullOrEmpty(json)) return;
            var data = JsonUtility.FromJson<Data>(json);
            var wanted = new Dictionary<string, Entry>();
            if (data != null && data.entries != null)
            {
                foreach (var entry in data.entries)
                {
                    if (entry != null && entry.path != null) wanted[entry.path] = entry;
                }
            }

            foreach (var settings in avatar.GetComponentsInChildren<ModularAvatarMeshSettings>(true))
            {
                var path = HierarchyPath.Get(avatar.transform, settings.transform);
                Entry entry;
                if (path != null && wanted.TryGetValue(path, out entry))
                {
                    Undo.RecordObject(settings, undoName);
                    ApplyEntry(avatar.transform, settings, entry);
                    wanted.Remove(path);
                }
                else
                {
                    Undo.DestroyObjectImmediate(settings);
                }
            }

            // バックアップにあったのに今は無い設定は、その場所に作り直す
            foreach (var pair in wanted)
            {
                var t = HierarchyPath.Find(avatar.transform, pair.Key);
                if (t == null) continue;
                var settings = Undo.AddComponent<ModularAvatarMeshSettings>(t.gameObject);
                ApplyEntry(avatar.transform, settings, pair.Value);
            }
        }

        private static string PathOf(Transform root, Component container, AvatarObjectReference reference)
        {
            var target = MeshSettingsHelper.Resolve(container, reference);
            return target == null ? null : HierarchyPath.Get(root, target);
        }

        private static void ApplyEntry(Transform root, ModularAvatarMeshSettings settings, Entry entry)
        {
            settings.InheritProbeAnchor = (ModularAvatarMeshSettings.InheritMode)entry.inheritProbeAnchor;
            settings.InheritBounds = (ModularAvatarMeshSettings.InheritMode)entry.inheritBounds;
            settings.ProbeAnchor = MeshSettingsHelper.Reference(root, entry.probeAnchor);
            settings.RootBone = MeshSettingsHelper.Reference(root, entry.rootBone);
            settings.Bounds = new Bounds(entry.boundsCenter, entry.boundsSize);
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            EditorUtility.SetDirty(settings);
        }
    }
}
#endif
