using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Jeni.KaihenNavi
{
    /// <summary>画面部品を作る小さなヘルパー。見た目は UI/KaihenNavi.uss の kn- クラスで決める。</summary>
    internal static class Ui
    {
        public static VisualElement Box(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        /// <summary>セクション見出し。既存の呼び出しとの互換性のため number は受け取るが表示しない。</summary>
        public static VisualElement StepHeader(string number, string title)
        {
            var row = Box("kn-step-header");
            row.Add(Text(title, "kn-step-title"));
            return row;
        }

        /// <summary>小見出し。</summary>
        public static Label Heading(string text)
        {
            return Text(text, "kn-heading");
        }

        /// <summary>お知らせの帯。kind は info / ok / warn。</summary>
        public static VisualElement Notice(string text, string kind = "info")
        {
            var box = Box("kn-notice", "kn-notice--" + kind);
            box.tooltip = text;
            var content = EditorGUIUtility.IconContent(kind == "warn" ? "console.warnicon.sml" :
                kind == "ok" ? "TestPassed" : "console.infoicon.sml");
            var texture = content == null ? null : content.image as Texture2D;
            if (texture != null) box.Add(Image(texture, "kn-notice__icon"));
            box.Add(Text(text, "kn-notice__text"));
            return box;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("kn-button");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        /// <summary>場所チップの列。selected が変わったら onSelect が呼ばれる。</summary>
        public static VisualElement Chips(IList<string> labels, int selected, Action<int> onSelect)
        {
            var row = Box("kn-chips");
            for (var i = 0; i < labels.Count; i++)
            {
                var index = i;
                var chip = new Button(() => onSelect(index)) { text = labels[i] };
                chip.AddToClassList("kn-chip");
                if (i == 0) chip.AddToClassList("kn-chip--first");
                if (i == labels.Count - 1) chip.AddToClassList("kn-chip--last");
                if (i == selected) chip.AddToClassList("kn-chip--selected");
                row.Add(chip);
            }

            return row;
        }

        /// <summary>スライダー。数値入力欄付き。onChange はドラッグ中も呼ばれる。</summary>
        public static Slider Slider(string label, float min, float max, float value, Action<float> onChange)
        {
            var slider = new Slider(label, min, max) { showInputField = true };
            slider.AddToClassList("kn-slider");
            slider.SetValueWithoutNotify(value);
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            return slider;
        }

        /// <summary>大きめの矢印ボタン。</summary>
        public static Button Arrow(string glyph, string caption, Action onClick)
        {
            var b = new Button(onClick);
            b.AddToClassList("kn-arrow");
            b.Add(Text(glyph, "kn-arrow__glyph"));
            b.Add(Text(caption, "kn-arrow__caption"));
            return b;
        }

        /// <summary>
        /// アセットのサムネイルを背景に出す箱。サムネイルは作られるまで少しかかるので、出来るまで何度か見に行く。
        /// </summary>
        public static VisualElement Thumbnail(UnityEngine.Object asset, params string[] classes)
        {
            var box = Box(classes);
            SetThumbnail(box, asset, 0);
            return box;
        }

        /// <summary>画像をそのまま背景に貼った箱。</summary>
        public static VisualElement Image(Texture2D texture, params string[] classes)
        {
            var box = Box(classes);
            if (texture != null) box.style.backgroundImage = new StyleBackground(texture);
            return box;
        }

        private static void SetThumbnail(VisualElement box, UnityEngine.Object asset, int tries)
        {
            if (asset == null) return;
            var texture = UnityEditor.AssetPreview.GetAssetPreview(asset);
            if (texture != null)
            {
                box.style.backgroundImage = new StyleBackground(texture);
                return;
            }

            if (tries == 0) box.style.backgroundImage = new StyleBackground(UnityEditor.AssetPreview.GetMiniThumbnail(asset));
            if (tries < 20) box.schedule.Execute(() => SetThumbnail(box, asset, tries + 1)).StartingIn(250);
        }

        /// <summary>小さなモノクロの線画。外部画像や文字フォントに依存しない。</summary>
        public static VisualElement NavigationIcon(string group)
        {
            var icon = Box("kn-nav-icon");
            icon.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = EditorGUIUtility.isProSkin ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.38f, 0.38f, 0.38f);
                painter.lineWidth = 1.2f;
                if (group == "outfit")
                {
                    Stroke(painter, new Vector2(6, 4), new Vector2(8, 2), new Vector2(10, 4), new Vector2(8, 7),
                        new Vector2(2, 12), new Vector2(14, 12), new Vector2(8, 7));
                }
                else if (group == "appearance")
                {
                    Stroke(painter, new Vector2(2, 4), new Vector2(14, 4));
                    Stroke(painter, new Vector2(2, 8), new Vector2(14, 8));
                    Stroke(painter, new Vector2(2, 12), new Vector2(14, 12));
                    Stroke(painter, new Vector2(5, 2), new Vector2(5, 6));
                    Stroke(painter, new Vector2(11, 6), new Vector2(11, 10));
                    Stroke(painter, new Vector2(7, 10), new Vector2(7, 14));
                }
                else if (group == "body")
                {
                    Stroke(painter, new Vector2(6, 2), new Vector2(10, 2), new Vector2(10, 5), new Vector2(6, 5), new Vector2(6, 2));
                    Stroke(painter, new Vector2(2, 7), new Vector2(14, 7));
                    Stroke(painter, new Vector2(8, 5), new Vector2(8, 10), new Vector2(4, 14));
                    Stroke(painter, new Vector2(8, 10), new Vector2(12, 14));
                }
                else
                {
                    Stroke(painter, new Vector2(3, 2), new Vector2(13, 2), new Vector2(13, 14), new Vector2(3, 14), new Vector2(3, 2));
                    Stroke(painter, new Vector2(5, 8), new Vector2(7, 10), new Vector2(11, 6));
                }
            };
            return icon;
        }

        private static void Stroke(Painter2D painter, params Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.Stroke();
        }

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.gray;
        }
    }
}
