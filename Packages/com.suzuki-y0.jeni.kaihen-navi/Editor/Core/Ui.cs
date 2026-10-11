using System;
using System.Collections.Generic;
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

        /// <summary>手順の見出し（「① アバターをえらぶ」など）。</summary>
        public static VisualElement StepHeader(string number, string title)
        {
            var row = Box("kn-step-header");
            row.Add(Text(number, "kn-step-number"));
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

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.gray;
        }
    }
}
