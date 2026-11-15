using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// 焼くたびに空いている名前の新しい画像を作り、差し替えを Undo に載せる（同じ画像を書き換えると Ctrl+Z・履歴で色が戻らない）。
    /// 前の画像は、どのマテリアル・履歴からも参照されなくなったものだけ消す。
    /// </summary>
    internal static class ColorLayers
    {
        private static readonly string[] Layers = { "2nd", "3rd" };
        private const string SourceTag = "kaihen-navi-color-source:";
        private const string BaselineTag = "kaihen-navi-color-baseline:";
        private static readonly string[] BaselineProperties = { "_Color", "_Color2nd", "_Color3rd" };

        /// <summary>複製を作った元のマテリアルの色。画面を作り直しても、ここからずらす（ずれが重ならない）。</summary>
        [System.Serializable]
        private sealed class Baselines
        {
            public List<string> names = new List<string>();
            public List<Color> colors = new List<Color>();
        }

        /// <summary>
        /// 複製（このツールのマテリアル）に、元の色を覚えさせる（.meta の userData）。既に覚えていれば何もしない。
        /// from には複製の元を渡す。元が分からない複製（明るさで作ったものなど）は、初めて色を変える前の自分を渡す。
        /// </summary>
        public static void RecordBaselines(Material target, Material from)
        {
            if (target == null || from == null || !WorkFolders.IsOwned(target)) return;
            var path = AssetDatabase.GetAssetPath(target);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || !string.IsNullOrEmpty(importer.userData)) return;
            var record = new Baselines();
            foreach (var property in BaselineProperties)
            {
                if (!from.HasProperty(property)) continue;
                record.names.Add(property);
                record.colors.Add(from.GetColor(property));
            }

            // 読み込み直しで未保存の変更を失わないよう、先に保存する
            AssetDatabase.SaveAssetIfDirty(target);
            importer.userData = BaselineTag + JsonUtility.ToJson(record);
            importer.SaveAndReimport();
        }

        /// <summary>複製に覚えさせた元の色。</summary>
        public static bool TryBaseline(Material material, string property, out Color color)
        {
            color = default(Color);
            if (material == null) return false;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material));
            var data = importer == null ? null : importer.userData;
            if (string.IsNullOrEmpty(data) || !data.StartsWith(BaselineTag)) return false;
            Baselines record;
            try
            {
                record = JsonUtility.FromJson<Baselines>(data.Substring(BaselineTag.Length));
            }
            catch (System.ArgumentException)
            {
                return false;
            }

            if (record == null || record.names == null || record.colors == null) return false;
            var index = record.names.IndexOf(property);
            if (index < 0 || index >= record.colors.Count) return false;
            color = record.colors[index];
            return true;
        }

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
                    // 作り直し・再読み込みの後も、今のずれた色ではなく元の色からずらす
                    if (!TryBaseline(material, property, out baseline)) baseline = material.GetColor(property);
                    baselines[key] = baseline;
                }

                material.SetColor(property, Shift(baseline, hue, saturation, brightness));
            }
        }

        /// <summary>層のテクスチャを、色を変えた複製に差し替える（重いので、スライダーを離してから呼ぶ）。</summary>
        public static void BakeTextures(Material material, float hue, float saturation, float brightness, string undoName)
        {
            var stale = new HashSet<string>();
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
                    CollectStale(material, layer, current, stale);
                    continue;
                }

                var path = NewBakedPath(material, layer);
                if (path == null) continue;
                WriteShifted(source, path, hue, saturation, brightness);
                var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (baked == null || baked == current) continue;
                Undo.RecordObject(material, undoName);
                material.SetTexture(property, baked);
                EditorUtility.SetDirty(material);
                CollectStale(material, layer, current, stale);
            }

            DeleteUnused(material, stale);
        }

        /// <summary>
        /// 白・黒・灰色の服にも色をのせる（明るさの濃淡は残して、色味とあざやかさを指定の値にする）。
        /// 色調補正（色味をずらす）では、色の無い所はずらしても色が出ないため。メインの画像の複製を作って差し替える。
        /// </summary>
        public static void BakeColorize(Material material, float hueDegrees, float saturation, float brightness, string undoName)
        {
            var hue = Mathf.Repeat(hueDegrees / 360f, 1f);
            var sat = Mathf.Clamp01(saturation / 100f * 0.6f);
            BakeMain(material, c =>
            {
                var l = Mathf.Clamp01((0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * brightness / 100f);
                var result = Hsl(hue, sat, l);
                result.a = c.a;
                return result;
            }, undoName);
        }

        /// <summary>
        /// メインの画像を、元の画像の画素を convert で変えた複製に差し替える（色をのせる・髪の色合わせで使う）。
        /// 何度変えても元の画像から作り直す。差し替えた画像を返す（変えられなかったら null）。
        /// </summary>
        internal static Texture2D BakeMain(Material material, System.Func<Color, Color> convert, string undoName)
        {
            const string property = "_MainTex";
            if (!material.HasProperty(property)) return null;
            var current = material.GetTexture(property) as Texture2D;
            if (current == null) return null;
            var source = SourceOf(current) ?? current;
            var path = NewBakedPath(material, "main");
            if (path == null) return null;
            WriteConverted(source, path, convert);
            var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (baked == null || baked == current) return baked;
            Undo.RecordObject(material, undoName);
            material.SetTexture(property, baked);
            EditorUtility.SetDirty(material);
            var stale = new HashSet<string>();
            CollectStale(material, "main", current, stale);
            DeleteUnused(material, stale);
            return baked;
        }

        /// <summary>色をのせるのをやめて、元のメインの画像に戻す。</summary>
        public static void ResetMain(Material material, string undoName)
        {
            const string property = "_MainTex";
            if (!material.HasProperty(property)) return;
            var current = material.GetTexture(property) as Texture2D;
            var source = current == null ? null : SourceOf(current);
            if (source == null) return;
            Undo.RecordObject(material, undoName);
            material.SetTexture(property, source);
            EditorUtility.SetDirty(material);
        }

        private static Color Hsl(float h, float s, float l)
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            return new Color(HueToRgb(p, q, h + 1f / 3f), HueToRgb(p, q, h), HueToRgb(p, q, h - 1f / 3f));
        }

        private static float HueToRgb(float p, float q, float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        /// <summary>層の色とテクスチャを元に戻す。</summary>
        public static void Reset(Material material, Dictionary<string, Color> baselines, string undoName)
        {
            foreach (var layer in Layers)
            {
                var colorProperty = "_Color" + layer;
                Color baseline;
                if (material.HasProperty(colorProperty) && (baselines.TryGetValue(material.GetInstanceID() + colorProperty, out baseline) ||
                    TryBaseline(material, colorProperty, out baseline)))
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
        internal static Texture2D SourceOf(Texture2D texture)
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
            // 複製は元と同じ名前のままなので、名前で付けると別の「Body」（体と服）が同じ画像を上書きし合う。
            // 複製係が重ならないように付けたファイル名で分ける。旧名の画像が付いていても、作り直すと新しい名前へ移る。
            return folder + "/" + WorkFolders.Sanitize(Path.GetFileNameWithoutExtension(materialPath)) + "_" + layer + ".png";
        }

        /// <summary>前の画像は Ctrl+Z・履歴が指しているかもしれないので書き換えず、空いている名前に焼く。</summary>
        private static string NewBakedPath(Material material, string layer)
        {
            var path = BakedPath(material, layer);
            return path == null ? null : AssetDatabase.GenerateUniqueAssetPath(path);
        }

        /// <summary>差し替えで外れた画像と、同じマテリアル・層の焼いた画像を、片付けの候補にする。</summary>
        private static void CollectStale(Material material, string layer, Texture2D replaced, HashSet<string> stale)
        {
            // 旧名（マテリアルの名前で付けた画像）も、外れたものは候補にする。参照が残っていれば消さない。
            if (replaced != null && SourceOf(replaced) != null) stale.Add(AssetDatabase.GetAssetPath(replaced));
            var path = BakedPath(material, layer);
            if (path == null) return;
            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            var pattern = "^" + Regex.Escape(Path.GetFileNameWithoutExtension(path)) + @"(?: \d+)?\.png$";
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.png"))
            {
                var candidate = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(candidate);
                if (Regex.IsMatch(Path.GetFileName(candidate), pattern) && importer != null &&
                    (importer.userData ?? "").StartsWith(SourceTag)) stale.Add(candidate);
            }
        }

        /// <summary>
        /// どこからも参照されない焼いた画像を消す。開いているマテリアル（未保存の履歴を含む）、プロジェクトのマテリアル、
        /// 履歴（バックアップ）を参照元に数える。Ctrl+Z で戻れる前の状態は、その操作の前の履歴が指しているので残る。
        /// </summary>
        private static void DeleteUnused(Material material, HashSet<string> stale)
        {
            stale.RemoveWhere(p => string.IsNullOrEmpty(p) || !WorkFolders.IsToolPath(p) || !File.Exists(p));
            // この起動のあいだに書いた画像は Ctrl+Z で戻る先になり得る（スライダーを続けて動かした手順は
            // 履歴では 1 件にまとまるので、履歴の参照だけでは守れない）。消すのは次の起動時の片付けに任せる
            stale.RemoveWhere(p => File.GetLastWriteTime(p) >= BackupService.SessionStart);
            if (stale.Count == 0) return;
            // ディスク上の参照を数える前に、差し替えたマテリアルを保存する
            AssetDatabase.SaveAssetIfDirty(material);
            DeleteUnreferenced(stale, "t:BackupSnapshot t:Prefab", WorkFolders.Root);
        }

        /// <summary>
        /// 起動時の片付け：ツールのフォルダにある焼いた画像のうち、どこからも参照されないものを消す。
        /// 焼いた画像は、同じマテリアル・層を焼き直したときにしか片付かず、それを指していた履歴は起動時に消えるので、ここで拾う。
        /// Undo が指し得るのはこの起動後に書いたファイルだけなので、起動前から変わっていない画像に限る。
        /// 起動時（BackupMaintenance で古い履歴を消した後）だけ呼ぶ。
        /// </summary>
        /// <returns>消した数。</returns>
        internal static int SweepUnusedBakes(System.DateTime sessionStart, System.Func<string, System.DateTime> lastWrite)
        {
            if (!AssetDatabase.IsValidFolder(WorkFolders.Root)) return 0;
            var stale = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { WorkFolders.Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!WorkFolders.IsToolPath(path) || !File.Exists(path) || lastWrite(path) >= sessionStart) continue;
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && (importer.userData ?? "").StartsWith(SourceTag)) stale.Add(path);
            }

            if (stale.Count == 0) return 0;
            // 起動時はまとめて 1 回なので、ツール外のプレハブ・シーン・アニメーションも参照元に数える（消しすぎないように）
            return DeleteUnreferenced(stale, "t:BackupSnapshot t:Prefab t:Scene t:AnimationClip", "Assets");
        }

        /// <summary>stale のうち、開いているマテリアル・プロジェクトのマテリアル・folder の中の filter に当たるもののどれからも参照されないものを消す。</summary>
        /// <returns>消した数。</returns>
        private static int DeleteUnreferenced(HashSet<string> stale, string filter, string folder)
        {
            var used = new HashSet<string>();
            foreach (var loaded in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (loaded == null) continue;
                foreach (var name in loaded.GetTexturePropertyNames())
                {
                    var texture = loaded.GetTexture(name);
                    if (texture != null) used.Add(AssetDatabase.GetAssetPath(texture));
                }
            }

            var roots = AssetDatabase.FindAssets("t:Material", new[] { "Assets" })
                .Concat(AssetDatabase.FindAssets(filter, new[] { folder }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && !stale.Contains(p)).Distinct().ToArray();
            foreach (var path in AssetDatabase.GetDependencies(roots, false)) used.Add(path);
            var deleted = 0;
            foreach (var path in stale.OrderBy(p => p, System.StringComparer.Ordinal))
                if (!used.Contains(path) && AssetDatabase.DeleteAsset(path)) deleted++;
            return deleted;
        }

        private static void WriteShifted(Texture2D source, string path, float hue, float saturation, float brightness)
        {
            WriteConverted(source, path, c => Shift(c, hue, saturation, brightness));
        }

        /// <summary>
        /// 画像の画素を読む（読み取り不可のテクスチャでも読める）。maxSize より大きい画像は縮めて読む。
        /// 色を調べるだけの用途向け。並びは下の行から。
        /// </summary>
        internal static Color[] ReadPixels(Texture2D source, int maxSize, out int width, out int height)
        {
            var scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(source.width, source.height));
            width = Mathf.Max(1, (int)(source.width * scale));
            height = Mathf.Max(1, (int)(source.height * scale));
            return ReadPixelsAt(source, width, height);
        }

        /// <summary>画像を指定の大きさに引き伸ばして読む（別の大きさの画像どうしを画素ごとに重ねるため）。</summary>
        internal static Color[] ReadPixelsAt(Texture source, int width, int height)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as TextureImporter;
            var linear = importer != null && !importer.sRGBTexture;

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        /// <summary>画像が sRGB（色の画像）として読み込まれているか。</summary>
        internal static bool IsSrgb(Texture texture)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            return importer == null || importer.sRGBTexture;
        }

        internal static void WriteConverted(Texture2D source, string path, System.Func<Color, Color> convert)
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
            for (var i = 0; i < pixels.Length; i++) pixels[i] = convert(pixels[i]);
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
