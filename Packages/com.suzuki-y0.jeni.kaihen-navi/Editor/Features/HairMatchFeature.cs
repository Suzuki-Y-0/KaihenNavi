using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 髪の色を、見本の髪（ほかの髪・シーンで選んだもの・画像ファイル）の色に合わせる。
    /// 色を合わせた画像の複製（マテリアル名_main.png）を作って差し替えるので、元の画像とマテリアルは変わらない。
    /// 複製の置き場所と「元に戻す」は、色変えの「色をのせる」（ColorLayers）と同じ仕組みを使う。
    /// 計算は HairColorMatch。
    /// </summary>
    internal sealed class HairMatchFeature : FeatureBase
    {
        private const string MainTex = "_MainTex";
        private const string HsvgProperty = "_MainTexHSVG";
        private const string ColorProperty = "_Color";

        // lilToon の影とふちの色。髪の色に合わせて作者が決めていることが多く、元のままだと影だけ前の色味が残る
        private static readonly string[] LookColors = { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_ShadowBorderColor", "_OutlineColor" };
        // メインと同じ画像を使っていたら、いっしょに色を合わせた画像へ差し替える枠
        private static readonly string[] SameTextureSlots = { "_OutlineTex", "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex" };

        // 色を調べるときに画像を縮める大きさ（色点を取るだけなので小さくてよい）
        private const int AnalyzeSize = 256;

        private sealed class Entry
        {
            public Material Material;
            public List<Renderer> Renderers;
            public bool Hair;
        }

        /// <summary>見本。マテリアル（使っているメッシュ付き）か、画像ファイル。</summary>
        private sealed class Target
        {
            public string Label;
            public bool FromFile;
            public Material Material;
            public List<Renderer> Renderers = new List<Renderer>();
            public Texture2D Preview;
            public HairColorMatch.Point[] Points;
        }

        // 画面を作り直しても覚えておく
        private static readonly HashSet<Material> Excluded = new HashSet<Material>();
        private static bool _showAll;
        private static float _strength = 100f;
        private static bool _copyLook = true;
        // 影とふちの色を変える前の値（「元の色に戻す」用。画面を開いている間だけ覚える）
        private static readonly Dictionary<string, Color> LookBaselines = new Dictionary<string, Color>();
        private static Target _target;
        // シーンで選んだものに色の設定がいくつもあるときの候補
        private static List<Target> _candidates = new List<Target>();

        private List<Entry> _entries = new List<Entry>();

        public override string Id { get { return "hairMatch"; } }
        public override int Number { get { return 15; } }
        public override Color Accent { get { return Ui.Hex("#B98AE0"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            _entries = Gather(ctx);
            // 見本のマテリアルや画像が消えていたら、見本なしに戻す
            if (_target != null && (_target.FromFile ? _target.Preview == null : _target.Material == null)) _target = null;

            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("hairMatch.intro"), "info"));
            root.Add(Ui.Notice(L10n.Tr("hairMatch.safe"), "ok"));

            // ---- 色を変える髪
            root.Add(Ui.Heading(L10n.Tr("hairMatch.which")));
            var shown = _entries.Where(e => _showAll || e.Hair).ToList();
            if (shown.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("hairMatch.noHair"), "warn"));
            }
            else
            {
                var list = Ui.Box("kn-list");
                foreach (var entry in shown) list.Add(BuildRow(entry));
                root.Add(list);
            }

            var showAll = new Toggle(L10n.Tr("hairMatch.showAll")) { value = _showAll };
            showAll.AddToClassList("kn-field");
            showAll.RegisterValueChangedCallback(e =>
            {
                _showAll = e.newValue;
                ctx.Rebuild();
            });
            root.Add(showAll);

            // ---- 見本
            root.Add(Ui.Heading(L10n.Tr("hairMatch.target")));
            root.Add(Ui.Text(L10n.Tr("hairMatch.targetHint"), "kn-hint"));

            // アバターの中の髪からえらぶ（非表示にしている元の髪も、ここからなら選べる）
            var inAvatar = _entries.Where(e => _showAll || e.Hair).ToList();
            if (inAvatar.Count > 0)
            {
                var choices = new List<string> { L10n.Tr("hairMatch.targetNone") };
                choices.AddRange(inAvatar.Select((e, i) => (i + 1) + ". " + e.Material.name));
                var current = _target == null ? -1 : inAvatar.FindIndex(e => e.Material == _target.Material);
                var popup = new PopupField<string>(choices, current + 1) { label = L10n.Tr("hairMatch.targetChoose") };
                popup.AddToClassList("kn-field");
                popup.RegisterValueChangedCallback(e =>
                {
                    var index = choices.IndexOf(e.newValue) - 1;
                    _candidates.Clear();
                    if (index < 0) _target = null;
                    else SetTarget(ctx, FromMaterial(inAvatar[index].Material, inAvatar[index].Renderers));
                    ctx.Rebuild();
                });
                root.Add(popup);
            }

            var pick = Ui.Box("kn-row");
            pick.Add(Ui.Button(L10n.Tr("hairMatch.fromSelection"), () => PickFromSelection(ctx)));
            pick.Add(Ui.Button(L10n.Tr("hairMatch.fromFile"), () => PickFromFile(ctx)));
            root.Add(pick);

            if (_candidates.Count > 1)
            {
                root.Add(Ui.Chips(_candidates.Select(c => c.Label).ToList(), _candidates.IndexOf(_target), i =>
                {
                    SetTarget(ctx, _candidates[i]);
                    ctx.Rebuild();
                }));
            }

            if (_target != null && _target.Points != null)
            {
                var row = Ui.Box("kn-list__row");
                var swatch = Ui.Box("kn-swatch");
                if (_target.Preview != null) swatch.style.backgroundImage = new StyleBackground(_target.Preview);
                row.Add(swatch);
                var texts = Ui.Box("kn-list__texts");
                texts.Add(Ui.Text(L10n.Tr("hairMatch.targetSet", _target.Label), "kn-list__title"));
                texts.Add(Ui.Text(L10n.Tr("hairMatch.palette"), "kn-list__sub"));
                row.Add(texts);
                root.Add(row);

                // 見本から取った色（暗い方から）
                var palette = Ui.Box("kn-row");
                foreach (var point in _target.Points)
                {
                    var chip = Ui.Box("kn-swatch");
                    chip.style.backgroundColor = point.ToColor();
                    palette.Add(chip);
                }

                root.Add(palette);
            }

            // ---- 合わせる
            root.Add(Ui.Heading(L10n.Tr("hairMatch.adjust")));
            root.Add(Ui.Slider(L10n.Tr("hairMatch.strength"), 0f, 100f, _strength, v => _strength = v));
            root.Add(Ui.Text(L10n.Tr("hairMatch.strengthHint"), "kn-hint"));

            var copyLook = new Toggle(L10n.Tr("hairMatch.copyLook")) { value = _copyLook };
            copyLook.AddToClassList("kn-field");
            copyLook.RegisterValueChangedCallback(e => _copyLook = e.newValue);
            root.Add(copyLook);
            root.Add(Ui.Text(L10n.Tr("hairMatch.copyLookHint"), "kn-hint"));

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("hairMatch.apply"), () => Apply(ctx), "kn-button--primary"));
            buttons.Add(Ui.Button(L10n.Tr("hairMatch.reset"), () => Reset(ctx)));
            root.Add(buttons);

            root.Add(Ui.Text(L10n.Tr("hairMatch.fineTune"), "kn-hint"));
            var more = Ui.Box("kn-row");
            more.Add(Ui.Button(L10n.Tr("hairMatch.openColor"), () =>
            {
                if (ctx.OpenFeature != null) ctx.OpenFeature("color");
            }, "kn-button--small"));
            root.Add(more);

            return root;
        }

        // ---------------------------------------------------------------- 一覧

        private static List<Entry> Gather(FeatureContext ctx)
        {
            var body = AvatarFinder.FindBody(ctx.Avatar);
            var entries = new List<Entry>();
            foreach (var pair in MaterialCopier.Collect(ctx.Avatar))
            {
                // 色の画像が無いマテリアルは合わせられない
                if (MainTexture(pair.Key) == null) continue;
                entries.Add(new Entry
                {
                    Material = pair.Key,
                    Renderers = pair.Value,
                    Hair = PartGuesser.Guess(pair.Value[0], pair.Key, body) == BodyPart.Hair,
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Material.name, b.Material.name));
            return entries;
        }

        private static VisualElement BuildRow(Entry entry)
        {
            var row = Ui.Box("kn-list__row");

            var toggle = new Toggle { value = !Excluded.Contains(entry.Material) };
            toggle.AddToClassList("kn-list__toggle");
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) Excluded.Remove(entry.Material);
                else Excluded.Add(entry.Material);
            });
            row.Add(toggle);

            var swatch = Ui.Box("kn-swatch");
            var texture = MainTexture(entry.Material);
            if (texture != null) swatch.style.backgroundImage = new StyleBackground(texture);
            row.Add(swatch);

            var texts = Ui.Box("kn-list__texts");
            texts.Add(Ui.Text(entry.Material.name, "kn-list__title"));
            var where = string.Join(", ", entry.Renderers.Select(r => r.name).Distinct().Take(3).ToArray());
            if (entry.Renderers.Count > 3) where += " …";
            texts.Add(Ui.Text(where, "kn-list__sub"));
            row.Add(texts);

            // ダブルクリックでシーン上の場所を示す
            row.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && entry.Renderers.Count > 0) EditorGUIUtility.PingObject(entry.Renderers[0].gameObject);
            });

            return row;
        }

        // ---------------------------------------------------------------- 見本

        private void PickFromSelection(FeatureContext ctx)
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                ctx.Tell(L10n.Tr("hairMatch.selectFirst"));
                return;
            }

            // 選んだものが使っている、色の画像があるマテリアル。髪らしい名前のものを先に出す
            var found = new Dictionary<Material, List<Renderer>>();
            foreach (var r in selected.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || MainTexture(m) == null) continue;
                    List<Renderer> list;
                    if (!found.TryGetValue(m, out list))
                    {
                        list = new List<Renderer>();
                        found.Add(m, list);
                    }

                    if (!list.Contains(r)) list.Add(r);
                }
            }

            if (found.Count == 0)
            {
                ctx.Tell(L10n.Tr("hairMatch.noTexture"));
                return;
            }

            var ordered = found
                .OrderByDescending(p => PartGuesser.Guess(p.Value[0], p.Key, null) == BodyPart.Hair)
                .ThenBy(p => p.Key.name, StringComparer.Ordinal)
                .Take(6)
                .Select(p => FromMaterial(p.Key, p.Value))
                .ToList();
            _candidates = ordered;
            SetTarget(ctx, ordered[0]);
            ctx.Rebuild();
        }

        private void PickFromFile(FeatureContext ctx)
        {
            var path = EditorUtility.OpenFilePanel(L10n.Tr("hairMatch.fileTitle"), "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) return;

            Texture2D image = null;
            try
            {
                image = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!image.LoadImage(File.ReadAllBytes(path), false)) throw new IOException(path);
            }
            catch (Exception e)
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                Debug.LogException(e);
                ctx.Tell(L10n.Tr("hairMatch.fileFailed"));
                return;
            }

            // 前に読んだ見本の画像は、もう使わないので捨てる
            ReleaseFileTarget();
            _candidates.Clear();
            SetTarget(ctx, new Target { Label = Path.GetFileName(path), Preview = image, FromFile = true });
            ctx.Rebuild();
        }

        private static Target FromMaterial(Material material, List<Renderer> renderers)
        {
            return new Target
            {
                Label = material.name,
                Material = material,
                Renderers = renderers,
                Preview = MainTexture(material),
            };
        }

        /// <summary>見本を決めて、色点を取る。取れなければ見本なしにする。</summary>
        private static void SetTarget(FeatureContext ctx, Target target)
        {
            if (_target != target) ReleaseFileTarget();
            if (target.Points == null) target.Points = Analyze(target);
            if (target.Points == null)
            {
                _target = null;
                ctx.Tell(L10n.Tr("hairMatch.noTexture"));
                return;
            }

            _target = target;
        }

        private static void ReleaseFileTarget()
        {
            if (_target == null || !_target.FromFile) return;
            if (_target.Preview != null) UnityEngine.Object.DestroyImmediate(_target.Preview);
            _target = null;
        }

        private static HairColorMatch.Point[] Analyze(Target target)
        {
            if (target.Preview == null) return null;
            int width, height;
            var pixels = target.FromFile
                ? ReadLoaded(target.Preview, out width, out height)
                : ColorLayers.ReadPixels(target.Preview, AnalyzeSize, out width, out height);
            var points = AnalyzePixels(pixels, width, height, target.Material, target.Renderers);
            if (points == null || target.FromFile) return points;

            // 見本が色調補正や色で見た目を変えているなら、画面で見えている色に合わせる
            var tint = target.Material.HasProperty(ColorProperty) ? target.Material.GetColor(ColorProperty) : new Color(1f, 1f, 1f, 1f);
            var useHsvg = target.Material.HasProperty(HsvgProperty);
            var hsvg = useHsvg ? target.Material.GetVector(HsvgProperty) : new Vector4(0f, 1f, 1f, 1f);
            return HairColorMatch.ApplyLook(points, tint, hsvg, useHsvg);
        }

        /// <summary>ファイルから読んだ画像（読み取り可）の画素。大きい画像は間引いて減らす。</summary>
        private static Color[] ReadLoaded(Texture2D image, out int width, out int height)
        {
            var all = image.GetPixels();
            var step = Math.Max(1, Math.Max(image.width, image.height) / AnalyzeSize);
            width = Math.Max(1, image.width / step);
            height = Math.Max(1, image.height / step);
            if (step == 1 || all.Length != image.width * image.height) { width = image.width; height = image.height; return all; }
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) pixels[y * width + x] = all[y * step * image.width + x * step];
            }

            return pixels;
        }

        /// <summary>
        /// 画素から色点を取る。メッシュが分かれば、そのメッシュが使っている範囲だけを調べる。
        /// 範囲がうまく取れないとき（UV が無いなど）は、画像全体で調べる。
        /// </summary>
        private static HairColorMatch.Point[] AnalyzePixels(Color[] pixels, int width, int height,
            Material material, List<Renderer> renderers)
        {
            if (pixels == null || pixels.Length != width * height) return null;
            bool[] mask = null;
            if (material != null && renderers != null && renderers.Count > 0)
            {
                mask = new bool[width * height];
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mesh = MeshOf(r);
                    if (mesh == null || mesh.uv == null || mesh.uv.Length == 0) continue;
                    var materials = r.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null || materials[i] != material) continue;
                        // マテリアルがサブメッシュより多いときは、最後のサブメッシュを重ねて描く（Unity の決まり）
                        var sub = Math.Min(i, mesh.subMeshCount - 1);
                        if (sub < 0) continue;
                        HairColorMatch.MarkUv(mesh.uv, mesh.GetTriangles(sub), width, height, mask);
                    }
                }
            }

            return (mask == null ? null : HairColorMatch.Analyze(pixels, mask)) ?? HairColorMatch.Analyze(pixels, null);
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static Texture2D MainTexture(Material material)
        {
            return material != null && material.HasProperty(MainTex) ? material.GetTexture(MainTex) as Texture2D : null;
        }

        // ---------------------------------------------------------------- 色を合わせる

        private IEnumerable<Entry> Selected()
        {
            return _entries.Where(e => (_showAll || e.Hair) && !Excluded.Contains(e.Material));
        }

        private void Apply(FeatureContext ctx)
        {
            if (_target == null || _target.Points == null)
            {
                ctx.Tell(L10n.Tr("hairMatch.noTarget"));
                return;
            }

            // 見本そのものは変えない
            var targets = Selected().Where(e => e.Material != _target.Material).ToList();
            if (targets.Count == 0)
            {
                ctx.Tell(L10n.Tr("hairMatch.noneSelected"));
                return;
            }

            var undoName = L10n.Tr("undo.hairMatch");
            var strength = _strength / 100f;
            var done = 0;
            var skipped = 0;

            ctx.BeginEdit();
            UndoHelper.Separate();
            UndoHelper.Begin("hair-match", undoName);
            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(L10n.Tr("hairMatch.working"), targets[i].Material.name, i / (float)targets.Count);
                    if (Match(ctx, targets[i], strength, undoName)) done++;
                    else skipped++;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ctx.Tell(L10n.Tr("hairMatch.failed", e.Message));
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UndoHelper.End();
                UndoHelper.Separate();
            }

            ctx.Tell(skipped > 0 ? L10n.Tr("hairMatch.doneSkipped", done, skipped) : L10n.Tr("hairMatch.done", done));
            SceneView.RepaintAll();
            ctx.Rebuild();
        }

        /// <summary>マテリアル 1 つぶん。元の画像から色を合わせた複製を作り、複製したマテリアルに差し替える。</summary>
        private static bool Match(FeatureContext ctx, Entry entry, float strength, string undoName)
        {
            var original = entry.Material;
            var current = MainTexture(original);
            if (current == null) return false;

            // 前に色を合わせた（色をのせた）複製なら、その元の画像から作り直す。重ねがけで色が崩れないように
            var source = ColorLayers.SourceOf(current) ?? current;
            int width, height;
            var pixels = ColorLayers.ReadPixels(source, AnalyzeSize, out width, out height);
            var points = AnalyzePixels(pixels, width, height, original, entry.Renderers);
            if (points == null) return false;
            var lut = HairColorMatch.BuildLut(points, _target.Points);

            var material = MaterialCopier.EnsureOwned(ctx.Avatar, original, undoName);
            if (material == null) return false;
            entry.Material = material;

            var path = ColorLayers.BakedPath(material, "main");
            if (path == null) return false;
            ColorLayers.WriteConverted(source, path, c => HairColorMatch.Apply(c, lut, strength));
            var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (baked == null) return false;

            Undo.RecordObject(material, undoName);
            if (baked != current) material.SetTexture(MainTex, baked);
            if (material.HasProperty(HsvgProperty))
            {
                // 色を合わせた画像に色調補正が重なると色がずれるので、補正は無しに戻す（ガンマだけ残す）
                var v = material.GetVector(HsvgProperty);
                material.SetVector(HsvgProperty, new Vector4(0f, 1f, 1f, v.w));
            }

            // ふちや影にメインと同じ画像を使っている髪は、そこにも前の色が残るので差し替える
            foreach (var slot in SameTextureSlots)
            {
                if (!material.HasProperty(slot)) continue;
                var used = material.GetTexture(slot);
                if (used != null && (used == current || used == source) && used != baked) material.SetTexture(slot, baked);
            }

            if (_copyLook && _target.Material != null && strength > 0f)
            {
                foreach (var property in LookColors)
                {
                    if (!material.HasProperty(property) || !_target.Material.HasProperty(property)) continue;
                    var key = material.GetInstanceID() + property;
                    var before = material.GetColor(property);
                    if (!LookBaselines.ContainsKey(key)) LookBaselines[key] = before;
                    var sample = _target.Material.GetColor(property);
                    // 強さに合わせて、元の色から見本の色へ寄せる
                    var from = LookBaselines[key];
                    material.SetColor(property, new Color(
                        from.r + (sample.r - from.r) * strength,
                        from.g + (sample.g - from.g) * strength,
                        from.b + (sample.b - from.b) * strength,
                        from.a + (sample.a - from.a) * strength));
                }
            }

            EditorUtility.SetDirty(material);
            return true;
        }

        private void Reset(FeatureContext ctx)
        {
            var undoName = L10n.Tr("undo.hairMatchReset");
            ctx.BeginEdit();
            UndoHelper.Separate();
            UndoHelper.Begin("hair-match-reset", undoName);
            foreach (var entry in Selected().ToList())
            {
                var material = entry.Material;
                ColorLayers.ResetMain(material, undoName);
                foreach (var slot in SameTextureSlots)
                {
                    if (!material.HasProperty(slot)) continue;
                    var used = material.GetTexture(slot) as Texture2D;
                    var source = used == null ? null : ColorLayers.SourceOf(used);
                    if (source == null) continue;
                    Undo.RecordObject(material, undoName);
                    material.SetTexture(slot, source);
                    EditorUtility.SetDirty(material);
                }

                foreach (var property in LookColors)
                {
                    Color before;
                    var key = material.GetInstanceID() + property;
                    if (!LookBaselines.TryGetValue(key, out before) || !material.HasProperty(property)) continue;
                    Undo.RecordObject(material, undoName);
                    material.SetColor(property, before);
                    EditorUtility.SetDirty(material);
                    LookBaselines.Remove(key);
                }
            }
            UndoHelper.End();
            UndoHelper.Separate();
            ctx.Tell(L10n.Tr("hairMatch.restored"));
            SceneView.RepaintAll();
            ctx.Rebuild();
        }
    }
}
