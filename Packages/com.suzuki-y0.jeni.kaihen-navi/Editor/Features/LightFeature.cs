using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// 機能 9：明るさや影を整えたい（製品版）。
    /// lilToon のマテリアルのうち、明るさと影に関係する項目だけを、わかりやすい名前のスライダーで出す。
    /// アイテム（素体・髪・服など）ごとにまとめて変えられる。変える前に必ずマテリアルを複製する。
    /// </summary>
    internal sealed class LightFeature : FeatureBase
    {
        /// <summary>出す項目。画面では実際の値 × 100（%）で見せる。</summary>
        private sealed class Setting
        {
            public string Key;
            public string Property;
            public float Max;
            public float Default;
        }

        private static readonly Setting[] Settings =
        {
            new Setting { Key = "minLight", Property = "_LightMinLimit", Max = 1f, Default = 0.05f },
            new Setting { Key = "maxLight", Property = "_LightMaxLimit", Max = 2f, Default = 1f },
            new Setting { Key = "unlit", Property = "_AsUnlit", Max = 1f, Default = 0f },
            new Setting { Key = "shadow", Property = "_ShadowStrength", Max = 1f, Default = 1f },
            new Setting { Key = "shadowBlur", Property = "_ShadowBlur", Max = 1f, Default = 0.1f },
        };

        /// <summary>おすすめ（項目名 → 値）。書いていない項目は変えない。</summary>
        private sealed class Preset
        {
            public string Key;
            public Dictionary<string, float> Values;
        }

        private static readonly Preset[] Presets =
        {
            new Preset { Key = "visible", Values = new Dictionary<string, float> { { "_LightMinLimit", 0.25f } } },
            new Preset { Key = "soft", Values = new Dictionary<string, float> { { "_ShadowStrength", 0.5f }, { "_ShadowBlur", 0.3f } } },
            new Preset { Key = "noGlare", Values = new Dictionary<string, float> { { "_LightMaxLimit", 1f }, { "_AsUnlit", 0f } } },
            new Preset { Key = "standard", Values = Settings.ToDictionary(s => s.Property, s => s.Default) },
        };

        private const string LilToonMarker = "_lilToonVersion";
        private const string UseShadowProperty = "_UseShadow";

        private sealed class Entry
        {
            public Material Material;
            public List<Renderer> Renderers;
            public GameObject Item;
        }

        private static int _itemIndex;   // 0 = ぜんぶ
        private static readonly HashSet<Material> Excluded = new HashSet<Material>();

        private List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();

        public override string Id { get { return "light"; } }
        public override int Number { get { return 9; } }
        public override Color Accent { get { return Ui.Hex("#F2C14E"); } }
        public override bool InTrial { get { return false; } }

        public override VisualElement Build(FeatureContext ctx)
        {
            var root = new VisualElement();
            _sliders.Clear();
            root.Add(Ui.Notice(L10n.Tr("light.intro"), "info"));

            var all = Gather(ctx.Avatar);
            var lil = all.Where(e => IsLilToon(e.Material)).ToList();
            if (lil.Count == 0)
            {
                root.Add(Ui.Notice(L10n.Tr("light.noLilToon"), "warn"));
                return root;
            }

            // どれを整える？（アイテムごと）
            var items = lil.Select(e => e.Item).Distinct().ToList();
            if (_itemIndex > items.Count) _itemIndex = 0;
            root.Add(Ui.Heading(L10n.Tr("light.which")));
            var labels = new List<string> { L10n.Tr("light.all") + "  " + lil.Count };
            labels.AddRange(items.Select(i => i.name + "  " + lil.Count(e => e.Item == i)));
            root.Add(Ui.Chips(labels, _itemIndex, i =>
            {
                _itemIndex = i;
                UndoHelper.Separate();
                ctx.Rebuild();
            }));

            var item = _itemIndex == 0 ? null : items[_itemIndex - 1];
            _entries = lil.Where(e => item == null || e.Item == item).ToList();

            var list = Ui.Box("kn-list");
            foreach (var entry in _entries) list.Add(BuildRow(entry));
            root.Add(list);

            var others = all.Count(e => !IsLilToon(e.Material) && (item == null || e.Item == item));
            if (others > 0) root.Add(Ui.Notice(L10n.Tr("light.othersSkipped", others), "info"));

            // おすすめ
            root.Add(Ui.Heading(L10n.Tr("light.presets")));
            var presets = Ui.Box("kn-row");
            foreach (var preset in Presets)
            {
                var p = preset;
                presets.Add(Ui.Button(L10n.Tr("light.preset." + p.Key), () => ApplyPreset(ctx, p), "kn-button--ghost"));
            }

            root.Add(presets);

            // 細かく調整する
            root.Add(Ui.Heading(L10n.Tr("light.adjust")));
            foreach (var setting in Settings)
            {
                var s = setting;
                var slider = Ui.Slider(L10n.Tr("light." + s.Key), 0f, s.Max * 100f, Current(s) * 100f, v => Apply(ctx, s, v / 100f));
                _sliders[s.Property] = slider;
                root.Add(slider);
                root.Add(Ui.Text(L10n.Tr("light." + s.Key + "Hint"), "kn-hint"));
            }

            var shadowOff = _entries.Count(e => Selected(e) && e.Material.HasProperty(UseShadowProperty) && e.Material.GetFloat(UseShadowProperty) < 0.5f);
            if (shadowOff > 0) root.Add(Ui.Notice(L10n.Tr("light.shadowOff", shadowOff), "info"));

            root.Add(Ui.Text(L10n.Tr("light.tip"), "kn-hint"));
            return root;
        }

        // ---------------------------------------------------------------- 一覧

        /// <summary>マテリアルと、それがどのアイテム（アバター直下のオブジェクト）に入っているか。</summary>
        private static List<Entry> Gather(VRCAvatarDescriptor avatar)
        {
            var result = new List<Entry>();
            foreach (var pair in MaterialCopier.Collect(avatar))
            {
                var items = pair.Value.Select(r => ItemOf(avatar, r)).Distinct().ToList();
                // 複数のアイテムで同じマテリアルを使っていたら、アイテムごとに 1 行ずつ出す（変えるのは同じマテリアル）
                foreach (var item in items)
                {
                    result.Add(new Entry
                    {
                        Material = pair.Key,
                        Renderers = pair.Value.Where(r => ItemOf(avatar, r) == item).ToList(),
                        Item = item,
                    });
                }
            }

            result.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.Item.name, b.Item.name);
                return c != 0 ? c : string.CompareOrdinal(a.Material.name, b.Material.name);
            });
            return result;
        }

        private static GameObject ItemOf(VRCAvatarDescriptor avatar, Renderer renderer)
        {
            var t = renderer.transform;
            while (t.parent != null && t.parent != avatar.transform) t = t.parent;
            return t.gameObject;
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
                UndoHelper.Separate();
            });
            row.Add(toggle);

            var swatch = Ui.Box("kn-swatch");
            var texture = entry.Material.HasProperty("_MainTex") ? entry.Material.mainTexture as Texture2D : null;
            if (texture != null) swatch.style.backgroundImage = new StyleBackground(texture);
            else if (entry.Material.HasProperty("_Color")) swatch.style.backgroundColor = entry.Material.GetColor("_Color");
            row.Add(swatch);

            var texts = Ui.Box("kn-list__texts");
            texts.Add(Ui.Text(entry.Material.name, "kn-list__title"));
            var where = string.Join(", ", entry.Renderers.Select(r => r.name).Distinct().Take(3).ToArray());
            texts.Add(Ui.Text(entry.Item.name + " / " + where, "kn-list__sub"));
            row.Add(texts);

            row.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && entry.Renderers.Count > 0) EditorGUIUtility.PingObject(entry.Renderers[0].gameObject);
            });
            return row;
        }

        private static bool Selected(Entry entry)
        {
            return !Excluded.Contains(entry.Material);
        }

        /// <summary>スライダーの最初の値（選んでいる最初のマテリアルの値）。</summary>
        private float Current(Setting setting)
        {
            var first = _entries.FirstOrDefault(e => Selected(e) && e.Material.HasProperty(setting.Property));
            return first != null ? first.Material.GetFloat(setting.Property) : setting.Default;
        }

        // ---------------------------------------------------------------- 変える

        private void Apply(FeatureContext ctx, Setting setting, float value)
        {
            var undoName = L10n.Tr("undo.light");
            ctx.BeginEdit();
            UndoHelper.Begin("light:" + setting.Property, undoName);
            Set(ctx, setting.Property, value, undoName);
            UndoHelper.End();
        }

        private void ApplyPreset(FeatureContext ctx, Preset preset)
        {
            var undoName = L10n.Tr("undo.light");
            ctx.BeginEdit();
            UndoHelper.Separate();
            UndoHelper.Begin("light-preset", undoName);
            foreach (var pair in preset.Values)
            {
                Set(ctx, pair.Key, pair.Value, undoName);
                Slider slider;
                if (_sliders.TryGetValue(pair.Key, out slider)) slider.SetValueWithoutNotify(pair.Value * 100f);
            }

            UndoHelper.End();
            UndoHelper.Separate();
        }

        private void Set(FeatureContext ctx, string property, float value, string undoName)
        {
            // 同じマテリアルが複数の行にあっても、1 回だけ変える
            var done = new HashSet<Material>();
            foreach (var entry in _entries.Where(Selected).ToList())
            {
                if (!entry.Material.HasProperty(property)) continue;
                var material = Own(ctx, entry, undoName);
                if (material == null || !done.Add(material)) continue;

                Undo.RecordObject(material, undoName);
                material.SetFloat(property, value);
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>変更してよい複製を返す（初回は複製を作る）。同じマテリアルの行もすべて複製に向け直す。</summary>
        private Material Own(FeatureContext ctx, Entry entry, string undoName)
        {
            var original = entry.Material;
            var owned = MaterialCopier.EnsureOwned(ctx.Avatar, original, undoName);
            if (owned == null || owned == original) return owned;

            foreach (var e in _entries.Where(x => x.Material == original)) e.Material = owned;
            return owned;
        }

        private static bool IsLilToon(Material material)
        {
            return material != null && material.HasProperty(LilToonMarker);
        }
    }
}
