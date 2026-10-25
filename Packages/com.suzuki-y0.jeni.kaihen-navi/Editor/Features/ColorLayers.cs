using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Jeni.KaihenNavi
{
    /// <summary>
    /// lilToon の「2nd / 3rd カラー」の層にも、色味・あざやかさ・明るさの変更を当てる。
    /// lilToon の色調補正（_MainTexHSVG）はメインの色にしか効かないため、
    /// 層に色が乗っているマテリアル（例：Cazalis のドレスの外側）では色が変わらなかった。
    /// 層の色（_Color2nd など）はその場で変え、層のテクスチャは色を変えた複製（PNG）を作って差し替える。
    /// 元のテクスチャは書き換えない。複製の画像には元テクスチャの GUID を覚えさせ、何度変えても元から作り直す。
    /// </summary>
    internal static class ColorLayers
    {
        private static readonly string[] Layers = { "2nd", "3rd" };
        private const string SourceTag = "kaihen-navi-color-source:";

        /// <summary>層の色（_Color2nd など）を元の色から変える。元の色はマテリアルごとに覚える。</summary>
        public static void ApplyTint(Material material, float hue, float saturation, float brightness, Dictionary<string, Color> baselines)
        {
            foreach (var layer in Layers)
            {
                if (!IsUsed(material, layer)) continue;
                var property = "_Color" + layer;
                if (!material.HasProperty(property)) continue;
                // テクスチャがある層は、テクスチャの複製の方で色を変える（両方変えると二重にずれる）
                var texProperty = "_Main" + layer + "Tex";
                if (material.HasProperty(texProperty) && material.GetTexture(texProperty) != null) continue;

                var key = material.GetInstanceID() + property;
                Color baseline;
                if (!baselines.TryGetValue(key, out baseline))
                {
                    baseline = material.GetColor(property);
                    baselines[key] = baseline;
                }

                material.SetColor(property, Shift(baseline, hue, saturation, brightness));
            }
        }

        /// <summary>層のテクスチャを、色を変えた複製に差し替える（重いので、スライダーを離してから呼ぶ）。</summary>
        public static void BakeTextures(Material material, float hue, float saturation, float brightness, string undoName)
        {
            foreach (var layer in Layers)
            {
                if (!IsUsed(material, layer)) continue;
                var property = "_Main" + layer + "Tex";
                if (!material.HasProperty(property)) continue;
                var current = material.GetTexture(property) as Texture2D;
                if (current == null) continue;

                var source = SourceOf(current) ?? current;
                var unchanged = Mathf.Approximately(hue, 0f) && Mathf.Approximately(saturation, 100f) && Mathf.Approximately(brightness, 100f);
                if (unchanged)
                {
                    if (source == current) continue;
                    Undo.RecordObject(material, undoName);
                    material.SetTexture(property, source);
                    EditorUtility.SetDirty(material);
                    continue;
                }

                var path = BakedPath(material, layer);
                if (path == null) continue;
                WriteShifted(source, path, hue, saturation, brightness);
                var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (baked == null || baked == current) continue;
                Undo.RecordObject(material, undoName);
                material.SetTexture(property, baked);
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>層の色とテクスチャを元に戻す。</summary>
        public static void Reset(Material material, Dictionary<string, Color> baselines, string undoName)
        {
            foreach (var layer in Layers)
            {
                var colorProperty = "_Color" + layer;
                Color baseline;
                if (material.HasProperty(colorProperty) && baselines.TryGetValue(material.GetInstanceID() + colorProperty, out baseline))
                {
                    Undo.RecordObject(material, undoName);
                    material.SetColor(colorProperty, baseline);
                }

                var texProperty = "_Main" + layer + "Tex";
                if (!material.HasProperty(texProperty)) continue;
                var current = material.GetTexture(texProperty) as Texture2D;
                var source = current == null ? null : SourceOf(current);
                if (source == null) continue;
                Undo.RecordObject(material, undoName);
                material.SetTexture(texProperty, source);
            }

            EditorUtility.SetDirty(material);
        }

        /// <summary>このマテリアルに、色が乗った層があるか（画面の説明用）。</summary>
        public static bool HasLayers(Material material)
        {
            foreach (var layer in Layers)
            {
                if (IsUsed(material, layer)) return true;
            }

            return false;
        }

        private static bool IsUsed(Material material, string layer)
        {
            var flag = "_UseMain" + layer + "Tex";
            return material != null && material.HasProperty(flag) && material.GetFloat(flag) > 0.5f;
        }

        /// <summary>複製した画像なら、元のテクスチャを返す。</summary>
        private static Texture2D SourceOf(Texture2D texture)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            if (importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.StartsWith(SourceTag)) return null;
            var guid = importer.userData.Substring(SourceTag.Length).Trim();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static string BakedPath(Material material, string layer)
        {
            var materialPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(materialPath)) return null;
            var folder = WorkFolders.Ensure(Path.GetDirectoryName(materialPath).Replace('\\', '/') + "/Textures");
            return folder + "/" + WorkFolders.Sanitize(material.name) + "_" + layer + ".png";
        }

        private static void WriteShifted(Texture2D source, string path, float hue, float saturation, float brightness)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            var linear = sourceImporter != null && !sourceImporter.sRGBTexture;

            // 読み取り不可のテクスチャでも読めるよう、一度 RenderTexture に描いてから読む
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = copy.GetPixels();
            for (var i = 0; i < pixels.Length; i++) pixels[i] = Shift(pixels[i], hue, saturation, brightness);
            copy.SetPixels(pixels);
            copy.Apply();
            File.WriteAllBytes(path, copy.EncodeToPNG());
            Object.DestroyImmediate(copy);

            var isNew = AssetImporter.GetAtPath(path) == null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            var tag = SourceTag + AssetDatabase.AssetPathToGUID(sourcePath);
            if (isNew || importer.userData != tag)
            {
                importer.userData = tag;
                importer.sRGBTexture = !linear;
                if (sourceImporter != null)
                {
                    importer.wrapMode = sourceImporter.wrapMode;
                    importer.filterMode = sourceImporter.filterMode;
                    importer.mipmapEnabled = sourceImporter.mipmapEnabled;
                    importer.maxTextureSize = sourceImporter.maxTextureSize;
                }

                importer.SaveAndReimport();
            }
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
    }
}
