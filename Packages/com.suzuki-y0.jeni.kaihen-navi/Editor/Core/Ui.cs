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

        /// <summary>ツールチップ付きの線画アイコンボタン。</summary>
        public static Button IconButton(string id, string tooltip, Action onClick)
        {
            var button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("kn-icon-button");
            button.Add(NavigationIcon(id));
            return button;
        }

        /// <summary>24×24 の線画を 16px に縮める。色はテーマと選択状態を USS から受け取る。</summary>
        public static VisualElement NavigationIcon(string id)
        {
            var icon = Box("kn-nav-icon");
            icon.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = icon.resolvedStyle.color;
                painter.lineWidth = 1.6f * 2f / 3f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                switch (id)
                {
                    case "outfit":
                        Stroke(painter, 8, 3, 4, 6, 6, 10, 8, 9, 8, 21, 16, 21, 16, 9, 18, 10, 20, 6, 16, 3);
                        Curve(painter, 16, 3, 16, 7, 8, 7, 8, 3);
                        break;
                    case "transform":
                        Circle(painter, 12, 14, 6);
                        Stroke(painter, 9, 4, 12, 8, 15, 4);
                        break;
                    case "toggle":
                        Curve(painter, 7, 8, 1.7f, 8, 1.7f, 16, 7, 16);
                        Stroke(painter, 7, 16, 17, 16);
                        Curve(painter, 17, 16, 22.3f, 16, 22.3f, 8, 17, 8);
                        Stroke(painter, 17, 8, 7, 8);
                        Circle(painter, 16, 12, 2.5f);
                        break;
                    case "skin":
                        Stroke(painter, 12, 3, 19, 7, 19, 12);
                        Curve(painter, 19, 12, 19, 17, 16, 20, 12, 21);
                        Curve(painter, 12, 21, 8, 20, 5, 17, 5, 12);
                        Stroke(painter, 5, 12, 5, 7, 12, 3);
                        break;
                    case "color":
                        painter.BeginPath();
                        painter.MoveTo(Point(12, 3));
                        painter.BezierCurveTo(Point(0, 3), Point(0, 21), Point(12, 21));
                        painter.BezierCurveTo(Point(16, 21), Point(12, 18), Point(14, 16));
                        painter.BezierCurveTo(Point(16, 14), Point(21, 17), Point(21, 12));
                        painter.BezierCurveTo(Point(21, 7), Point(17, 3), Point(12, 3));
                        painter.Stroke();
                        Circle(painter, 8, 10, 1); Circle(painter, 12, 7, 1); Circle(painter, 16, 10, 1);
                        break;
                    case "innerColor":
                        Curve(painter, 5, 20, 5, -1, 19, -1, 19, 20);
                        Curve(painter, 10, 9, 9, 13, 9, 17, 10, 20);
                        break;
                    case "light":
                        Circle(painter, 12, 12, 4);
                        Stroke(painter, 12, 2, 12, 4); Stroke(painter, 12, 20, 12, 22);
                        Stroke(painter, 2, 12, 4, 12); Stroke(painter, 20, 12, 22, 12);
                        Stroke(painter, 5, 5, 6.5f, 6.5f); Stroke(painter, 17.5f, 17.5f, 19, 19);
                        Stroke(painter, 5, 19, 6.5f, 17.5f); Stroke(painter, 17.5f, 6.5f, 19, 5);
                        break;
                    case "pink":
                        Stroke(painter, 4, 20, 14, 10); Stroke(painter, 14, 4, 14, 7);
                        Stroke(painter, 18, 6, 16, 8); Stroke(painter, 20, 10, 17, 10);
                        break;
                    case "body":
                        Circle(painter, 12, 5, 2);
                        Stroke(painter, 12, 7, 12, 14, 9, 21);
                        Stroke(painter, 8, 10, 16, 10); Stroke(painter, 12, 14, 15, 21);
                        break;
                    case "view":
                        Curve(painter, 2, 12, 7, 3, 17, 3, 22, 12);
                        Curve(painter, 22, 12, 17, 21, 7, 21, 2, 12);
                        Circle(painter, 12, 12, 3);
                        break;
                    case "walk":
                        Circle(painter, 14, 4, 2);
                        Stroke(painter, 8, 21, 11, 14, 14, 17, 16, 21);
                        Stroke(painter, 11, 14, 13, 8, 17, 11, 21, 11);
                        Stroke(painter, 13, 8, 9, 9, 6, 13);
                        break;
                    case "menu":
                        for (var y = 6; y <= 18; y += 6)
                        {
                            Circle(painter, 4, y, 0.7f);
                            Stroke(painter, 9, y, 21, y);
                        }
                        break;
                    case "thumbnail":
                        Stroke(painter, 3, 7, 7, 7, 9, 4, 15, 4, 17, 7, 21, 7, 21, 20, 3, 20, 3, 7);
                        Circle(painter, 12, 13, 4);
                        break;
                    case "uploadCheck":
                        Stroke(painter, 12, 16, 12, 4); Stroke(painter, 7, 9, 12, 4, 17, 9);
                        Stroke(painter, 4, 16, 4, 20, 20, 20, 20, 16);
                        break;
                    case "undo":
                        Stroke(painter, 9, 14, 4, 9, 9, 4);
                        Stroke(painter, 4, 9, 14, 9);
                        Curve(painter, 14, 9, 22, 9, 22, 21, 14, 21);
                        Stroke(painter, 14, 21, 11, 21);
                        break;
                    case "history":
                        Circle(painter, 12, 12, 9);
                        Stroke(painter, 12, 7, 12, 12, 15, 14);
                        break;
                    case "more":
                        Circle(painter, 5, 12, 1); Circle(painter, 12, 12, 1); Circle(painter, 19, 12, 1);
                        break;
                    default:
                        Stroke(painter, 5, 12, 10, 17, 19, 7);
                        break;
                }
            };
            return icon;
        }

        private static Vector2 Point(float x, float y)
        {
            return new Vector2(x * 2f / 3f, y * 2f / 3f);
        }

        private static void Stroke(Painter2D painter, params float[] points)
        {
            painter.BeginPath();
            painter.MoveTo(Point(points[0], points[1]));
            for (var i = 2; i < points.Length; i += 2) painter.LineTo(Point(points[i], points[i + 1]));
            painter.Stroke();
        }

        private static void Curve(Painter2D painter, float x, float y, float c1x, float c1y, float c2x, float c2y, float ex, float ey)
        {
            painter.BeginPath();
            painter.MoveTo(Point(x, y));
            painter.BezierCurveTo(Point(c1x, c1y), Point(c2x, c2y), Point(ex, ey));
            painter.Stroke();
        }

        private static void Circle(Painter2D painter, float x, float y, float radius)
        {
            var c = radius * 0.5522848f;
            painter.BeginPath();
            painter.MoveTo(Point(x + radius, y));
            painter.BezierCurveTo(Point(x + radius, y + c), Point(x + c, y + radius), Point(x, y + radius));
            painter.BezierCurveTo(Point(x - c, y + radius), Point(x - radius, y + c), Point(x - radius, y));
            painter.BezierCurveTo(Point(x - radius, y - c), Point(x - c, y - radius), Point(x, y - radius));
            painter.BezierCurveTo(Point(x + c, y - radius), Point(x + radius, y - c), Point(x + radius, y));
            painter.ClosePath();
            painter.Stroke();
        }

        /// <summary>USS の実線を使わず、4px の線と 4px の空白で角丸のドロップ枠を描く。</summary>
        public static void DashedBorder(VisualElement element)
        {
            element.generateVisualContent += context =>
            {
                // contentRect ではなく外周を使い、内側の余白を保つ。
                var width = element.layout.width - 1f;
                var height = element.layout.height - 1f;
                if (width < 12f || height < 12f) return;
                var radius = 6f;
                var horizontal = width - radius * 2f;
                var vertical = height - radius * 2f;
                var arc = Mathf.PI * radius * 0.5f;
                var perimeter = (horizontal + vertical) * 2f + arc * 4f;
                var painter = context.painter2D;
                painter.lineWidth = 1f;
                painter.strokeColor = element.resolvedStyle.color;
                for (var offset = 0f; offset < perimeter; offset += 8f)
                {
                    painter.BeginPath();
                    painter.MoveTo(BorderPoint(offset, width, height, radius));
                    // 角の短い円弧も折れずにつながるよう 1px ごとにたどる。
                    for (var d = 1f; d <= 4f && offset + d <= perimeter; d += 1f)
                        painter.LineTo(BorderPoint(offset + d, width, height, radius));
                    painter.Stroke();
                }
            };
        }

        private static Vector2 BorderPoint(float distance, float width, float height, float radius)
        {
            var horizontal = width - radius * 2f;
            var vertical = height - radius * 2f;
            var arc = Mathf.PI * radius * 0.5f;
            for (var side = 0; side < 4; side++)
            {
                var length = side % 2 == 0 ? horizontal : vertical;
                if (distance <= length)
                {
                    if (side == 0) return new Vector2(radius + distance + 0.5f, 0.5f);
                    if (side == 1) return new Vector2(width + 0.5f, radius + distance + 0.5f);
                    if (side == 2) return new Vector2(width - radius - distance + 0.5f, height + 0.5f);
                    return new Vector2(0.5f, height - radius - distance + 0.5f);
                }
                distance -= length;
                if (distance <= arc)
                {
                    var angle = -Mathf.PI * 0.5f + side * Mathf.PI * 0.5f + distance / radius;
                    var cx = side == 0 || side == 1 ? width - radius : radius;
                    var cy = side == 1 || side == 2 ? height - radius : radius;
                    return new Vector2(cx + Mathf.Cos(angle) * radius + 0.5f, cy + Mathf.Sin(angle) * radius + 0.5f);
                }
                distance -= arc;
            }
            return new Vector2(radius + 0.5f, 0.5f);
        }

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.gray;
        }
    }
}
