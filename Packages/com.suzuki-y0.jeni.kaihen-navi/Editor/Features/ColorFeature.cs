using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 1：髪や服の色を変えたい。
    /// lilToon は色調補正（_MainTexHSVG）を、それ以外はメインカラー（_Color）を変える。
    /// 変更前に必ずマテリアルを複製して差し替えるので、元のマテリアルは変わらない。
    /// </summary>
    internal sealed class ColorFeature : FeatureBase
    {
        private const string HsvgProperty = "_MainTexHSVG";
        private const string ColorProperty = "_Color";

        private static readonly BodyPart[] Parts = { BodyPart.Hair, BodyPart.Cloth, BodyPart.Eye, BodyPart.Skin, BodyPart.Other };

        private sealed class Entry
        {
            public Material Material;
            public List<Renderer> Renderers;
            public BodyPart Part;
        }

        // 選び直した場所や外したマテリアルは、画面を作り直しても覚えておく
        private static readonly Dictionary<Material, BodyPart> Overrides = new Dictionary<Material, BodyPart>();
        private static readonly HashSet<Material> Excluded = new HashSet<Material>();

        private readonly Dictionary<Material, Color> _baselines = new Dictionary<Material, Color>();
        private BodyPart _part = BodyPart.Hair;
        private List<Entry> _entries = new List<Entry>();
        private Slider _hue;
        private Slider _saturation;
        private Slider _brightness;

        public override string Id { get { return "color"; } }
        public override int Number { get { return 1; } }
        public override Color Accent { get { return Ui.Hex("#EC7AA8"); } }
        public override bool InTrial { get { return true; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            _baselines.Clear();
            _entries = Gather(ctx);

            var root = new VisualElement();
            root.Add(Ui.Notice(L10n.Tr("color.safe"), "ok"));

            // どこの色？
            root.Add(Ui.Heading(L10n.Tr("color.where")));
            var labels = Parts.Select(p => L10n.Tr("part." + p.ToString().ToLowerInvariant()) + "  " + _entries.Count(e => e.Part == p)).ToList();
            root.Add(Ui.Chips(labels, System.Array.IndexOf(Parts, _part), i =>
            {
                _part = Parts[i];
                UndoHelper.Separate();
                ctx.Rebuild();
            }));

            var targets = _entries.Where(e => e.Part == _part).ToList();
            if (targets.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("color.noneInPart"), "warn"));
                return root;
            }

            // 対象の一覧（チェックを外すと変えない。場所の判定が違えば選び直せる）
            var list = Ui.Box("kn-list");
            foreach (var entry in targets) list.Add(BuildRow(ctx, entry));
            root.Add(list);

            // スライダー
            root.Add(Ui.Heading(L10n.Tr("color.adjust")));
            var initial = InitialValues(targets);
            _hue = Ui.Slider(L10n.Tr("color.hue"), -180f, 180f, initial.x, v => Apply(ctx));
            _saturation = Ui.Slider(L10n.Tr("color.saturation"), 0f, 200f, initial.y, v => Apply(ctx));
            _brightness = Ui.Slider(L10n.Tr("color.brightness"), 0f, 200f, initial.z, v => Apply(ctx));
            root.Add(_hue);
            var hueBar = Ui.Box("kn-hue-bar");
            hueBar.style.backgroundImage = new StyleBackground(HueTexture());
            root.Add(hueBar);
            root.Add(_saturation);
            root.Add(_brightness);

            var buttons = Ui.Box("kn-row");
            buttons.Add(Ui.Button(L10n.Tr("color.reset"), () => ResetColors(ctx)));
            root.Add(buttons);

            if (targets.Any(e => !IsLilToon(e.Material)))
            {
                root.Add(Ui.Notice(L10n.Tr("color.nonLilToon"), "info"));
            }

            return root;
        }

        public override void OnClosed()
        {
            _baselines.Clear();
        }

        // ---------------------------------------------------------------- 一覧

        private List<Entry> Gather(FeatureContext ctx)
        {
            var body = AvatarFinder.FindBody(ctx.Avatar);
            var entries = new List<Entry>();
            foreach (var pair in MaterialCopier.Collect(ctx.Avatar))
            {
                // 色を変えられないマテリアル（色の項目がない）は出さない
                if (!IsLilToon(pair.Key) && !pair.Key.HasProperty(ColorProperty)) continue;

                BodyPart part;
                if (!Overrides.TryGetValue(pair.Key, out part))
                {
                    part = PartGuesser.Guess(pair.Value[0], pair.Key, body);
                }

                entries.Add(new Entry { Material = pair.Key, Renderers = pair.Value, Part = part });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Material.name, b.Material.name));
            return entries;
        }

        private VisualElement BuildRow(FeatureContext ctx, Entry entry)
        {
            var row = Ui.Box("kn-list__row");

            var toggle = new Toggle { value = !Excluded.Contains(entry.Material) };
            toggle.AddToClassList("kn-list__toggle");
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) Excluded.Remove(entry.Material);
                else Excluded.Add(entry.Material);
                UndoHelper.Separate();
            });
            row.Add(toggle);

            var swatch = Ui.Box("kn-swatch");
            swatch.style.backgroundColor = PreviewColor(entry.Material);
            var texture = entry.Material.HasProperty("_MainTex") ? entry.Material.mainTexture as Texture2D : null;
            if (texture != null) swatch.style.backgroundImage = new StyleBackground(texture);
            row.Add(swatch);

            var texts = Ui.Box("kn-list__texts");
            texts.Add(Ui.Text(entry.Material.name, "kn-list__title"));
            var where = string.Join(", ", entry.Renderers.Select(r => r.name).Distinct().Take(3).ToArray());
            if (entry.Renderers.Count > 3) where += " …";
            texts.Add(Ui.Text(where, "kn-list__sub"));
            row.Add(texts);

            // 場所の選び直し
            var choices = Parts.Select(p => L10n.Tr("part." + p.ToString().ToLowerInvariant())).ToList();
            var popup = new PopupField<string>(choices, System.Array.IndexOf(Parts, entry.Part));
            popup.tooltip = L10n.Tr("color.movePart");
            popup.AddToClassList("kn-list__popup");
            popup.RegisterValueChangedCallback(e =>
            {
                var index = choices.IndexOf(e.newValue);
                if (index < 0) return;
                Overrides[entry.Material] = Parts[index];
                ctx.Rebuild();
            });
            row.Add(popup);

            // クリックでシーン上の場所を示す
            row.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && entry.Renderers.Count > 0) EditorGUIUtility.PingObject(entry.Renderers[0].gameObject);
            });

            return row;
        }

        // ---------------------------------------------------------------- 色の変更

        private IEnumerable<Entry> Selected()
        {
            return _entries.Where(e => e.Part == _part && !Excluded.Contains(e.Material));
        }

        /// <summary>スライダーの初期値（色相°, 彩度%, 明度%）。</summary>
        private static Vector3 InitialValues(List<Entry> targets)
        {
            var lil = targets.FirstOrDefault(e => !Excluded.Contains(e.Material) && IsLilToon(e.Material));
            if (lil == null) return new Vector3(0f, 100f, 100f);
            var v = lil.Material.GetVector(HsvgProperty);
            return new Vector3(v.x * 360f, v.y * 100f, v.z * 100f);
        }

        private void Apply(FeatureContext ctx)
        {
            var hue = _hue.value;
            var saturation = _saturation.value;
            var brightness = _brightness.value;
            var undoName = L10n.Tr("undo.color");

            ctx.BeginEdit("color:" + _part);
            UndoHelper.Begin("color:" + _part, undoName);

            foreach (var entry in Selected().ToList())
            {
                var material = Own(ctx, entry, undoName);
                if (material == null) continue;

                Undo.RecordObject(material, undoName);
                if (IsLilToon(material))
                {
                    var v = material.GetVector(HsvgProperty);
                    material.SetVector(HsvgProperty, new Vector4(hue / 360f, saturation / 100f, brightness / 100f, v.w));
                }
                else
                {
                    Color baseline;
                    if (!_baselines.TryGetValue(material, out baseline))
                    {
                        baseline = material.GetColor(ColorProperty);
                        _baselines[material] = baseline;
                    }

                    material.SetColor(ColorProperty, Shift(baseline, hue, saturation, brightness));
                }

                EditorUtility.SetDirty(material);
            }

            UndoHelper.End();
        }

        private void ResetColors(FeatureContext ctx)
        {
            var undoName = L10n.Tr("undo.colorReset");
            ctx.BeginEdit();
            UndoHelper.Separate();
            UndoHelper.Begin("color-reset", undoName);

            foreach (var entry in Selected().ToList())
            {
                var material = entry.Material;
                if (IsLilToon(material))
                {
                    var v = material.GetVector(HsvgProperty);
                    if (Mathf.Approximately(v.x, 0f) && Mathf.Approximately(v.y, 1f) && Mathf.Approximately(v.z, 1f)) continue;
                    material = Own(ctx, entry, undoName);
                    Undo.RecordObject(material, undoName);
                    material.SetVector(HsvgProperty, new Vector4(0f, 1f, 1f, v.w));
                    EditorUtility.SetDirty(material);
                }
                else
                {
                    Color baseline;
                    if (!_baselines.TryGetValue(material, out baseline)) continue;
                    Undo.RecordObject(material, undoName);
                    material.SetColor(ColorProperty, baseline);
                    EditorUtility.SetDirty(material);
                }
            }

            UndoHelper.End();
            UndoHelper.Separate();
            _hue.SetValueWithoutNotify(0f);
            _saturation.SetValueWithoutNotify(100f);
            _brightness.SetValueWithoutNotify(100f);
        }

        /// <summary>変更してよい複製を返す。初回は複製を作り、覚えている設定も複製側へ移す。</summary>
        private static Material Own(FeatureContext ctx, Entry entry, string undoName)
        {
            var original = entry.Material;
            var owned = MaterialCopier.EnsureOwned(ctx.Avatar, original, undoName);
            if (owned == null || owned == original) return owned;

            BodyPart part;
            if (Overrides.TryGetValue(original, out part)) Overrides[owned] = part;
            entry.Material = owned;
            return owned;
        }

        private static Color Shift(Color baseline, float hueDegrees, float saturationPercent, float brightnessPercent)
        {
            float h, s, v;
            Color.RGBToHSV(baseline, out h, out s, out v);
            h = Mathf.Repeat(h + hueDegrees / 360f, 1f);
            s = Mathf.Clamp01(s * saturationPercent / 100f);
            v = v * brightnessPercent / 100f;
            var result = Color.HSVToRGB(h, s, v, true);
            result.a = baseline.a;
            return result;
        }

        private static Texture2D _hueTexture;

        /// <summary>色味スライダーの下に出す虹色の帯。</summary>
        private static Texture2D HueTexture()
        {
            if (_hueTexture != null) return _hueTexture;
            const int width = 256;
            _hueTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (var x = 0; x < width; x++)
            {
                // スライダーは -180°〜+180° なので、左端が色相 0.5（シアン）になる
                var h = Mathf.Repeat(x / (float)(width - 1) - 0.5f, 1f);
                _hueTexture.SetPixel(x, 0, Color.HSVToRGB(h, 0.75f, 1f));
            }

            _hueTexture.Apply();
            return _hueTexture;
        }

        private static bool IsLilToon(Material material)
        {
            return material != null && material.HasProperty(HsvgProperty);
        }

        private static Color PreviewColor(Material material)
        {
            if (material.HasProperty(ColorProperty))
            {
                var c = material.GetColor(ColorProperty);
                c.a = 1f;
                return c;
            }

            return Color.gray;
        }
    }
}
