# かいへんナビ 引き継ぎメモ（2026-10-11 時点）

次の作業者（人でも AI でも）が、これだけ読めば続きから始められるようにまとめたもの。

## 1. これは何か

VRChat 初心者向けの Unity エディタ拡張「かいへんナビ」（仮称）。BOOTH で販売予定。
Unity 標準機能を「やりたいこと」単位のかんたんな画面で包み、ヒエラルキーやインスペクターを触らずに見た目を改変できるようにする。

- 企画・引き継ぎ資料（市場調査、競合、機能一覧、画面仕様、技術設計、販売戦略）:
  https://claude.ai/code/artifact/9b4542aa-308b-43b6-ac15-3ce195893895
- リポジトリ: https://github.com/Suzuki-Y-0/KaihenNavi （Private、main ブランチ）
- 対象: BOOTH でアバターを買ったが Unity 操作が難しい一般ユーザー
- 価格案: 無料体験版（3 機能）＋製品版 1,500〜2,500 円

## 2. いまの状態

体験版の 3 機能と土台は実装済み。**Unity 上ではまだ一度も動かしていない**（作業環境に Unity が無かったため）。
Mono と自作スタブで「MA なし／MA あり／製品版」の 3 通りのコンパイルは通っている（`tools/check.sh`）。

| 項目 | 状態 |
| --- | --- |
| パッケージ雛形（package.json、Editor 専用 asmdef、固定 GUID の .meta） | 済 |
| EditorWindow（Tools > かいへんナビ） | 済・Unity 未確認 |
| 自動バックアップ／元に戻す／履歴 | 済・Unity 未確認 |
| 機能1 色変え | 済・Unity 未確認 |
| 機能2 脱ぎ着メニュー生成（MA） | 済・Unity 未確認 |
| 機能6 位置と大きさ | 済・Unity 未確認 |
| 日英中韓の言語ファイル | 済 |
| 製品版の残り 7 機能 | 未着手（カードだけ鍵付きで表示） |
| VPM 配布・unitypackage・紹介動画・説明書 | 未着手 |

## 3. 最優先でやること：Unity での動作確認

1. Unity 2022.3.22f1 の VCC（または ALCOM）プロジェクトを用意（VRChat SDK Avatars 3.5 以降）。lilToon と Modular Avatar 1.10 以降も入れる
2. リポジトリの `Packages/com.suzuki-y0.jeni.kaihen-navi` をプロジェクトの `Packages/` にコピー
3. Console の赤エラーをすべて潰す（下の「4. 未確認の前提」が怪しい順）
4. Tools > かいへんナビ を開き、人気アバター 5 体ほどで次を確認
   - 色変え: 髪チップでスライダーを動かす → `Assets/KaihenNavi/<アバター名>/Materials/` に複製ができ、元マテリアルは無変更
   - 脱ぎ着: 服にチェック → メニューを作る → `KaihenNavi_Menu` が生成される。Play モードや Gesture Manager でメニューから切り替えられる
   - 位置と大きさ: 頭のアクセを選び矢印で動く
   - フッターの「元に戻す」で完全に戻る。「メニューを作り直す → 元に戻す」で前のメニューが復元される
   - ピンク色や Missing があるアバターでも落ちない
5. MA なしのプロジェクトでもコンパイルが通り、導入案内が出ることを確認

## 4. 未確認の前提（Unity で最初に疑う所）

- MA の API: `ModularAvatarMenuItem` の `Control`（VRCExpressionsMenu.Control）、`MenuSource`、`isSynced`、`isSaved`、`isDefault`。`ModularAvatarObjectToggle.Objects`（`List<ToggledObject>`、`ToggledObject.Object` / `.Active`）。`AvatarObjectReference.Set()` / `Get()`。最新 main のソースで確認したが、1.10 時点で全部あるかは未確認
- MA の asmdef 名 `nadena.dev.modular-avatar.core`、パッケージ名 `nadena.dev.modular-avatar`（asmdef の versionDefines で `KAIHENNAVI_MA` を定義）
- `PopupField<T>` の名前空間（2022.3 では UnityEditor.UIElements の想定。両方 using 済み）
- MA の「Object Toggle の初期状態プレビュー」がシーンに出るか。出ないと、脱ぎ着メニュー作成後に服がシーン上で消えて見える（対象を非表示にし、メニュー既定 ON で表示する方式のため）
- 矢印（↑↓←→）や日本語がエディタのフォントで表示されるか
- lilToon 色調補正 `_MainTexHSVG`（既定 (0,1,1,1)、色相 -0.5〜0.5）。lilToon のシェーダーソースで確認済み

## 5. 設計の要点

- **元アセットは絶対に書き換えない**: マテリアルは初回変更時に `Assets/KaihenNavi/<アバター名>/Materials/` へ複製し、アバター内の参照をすべて差し替える（`MaterialCopier`）
- **自動バックアップ**: 「やりたいこと」を選んで最初に何か変える直前に 1 件保存（`BackupService.BeginEdit`）。同じ機能を続けて触っている間は追加しない。保存先は `Assets/KaihenNavi/<アバター名>/Backups/*.asset`、最大 20 件
  - 保存内容: 全 Transform（位置・回転・大きさ・表示）、全レンダラーのマテリアル参照、ツールの複製マテリアルの中身（サブアセットに複製）、BlendShape、View Position、脱ぎ着メニューの定義（`IBackupParticipant` で JSON）
  - オブジェクトは参照ではなく「名前#同名内の番号」のパスで記録（`HierarchyPath`）
  - 元に戻すと、そのバックアップ以降に作られたオブジェクトを消してから状態を戻し、使ったバックアップは削除。Unity の Ctrl+Z でも取り消せる
- **Editor 専用**: アップロードされるアバターに独自スクリプトは残さない。脱ぎ着は MA 標準コンポーネントのみ使用
- **Undo**: スライダーのドラッグ中の変更は `UndoHelper` で Ctrl+Z 1 回分にまとめる
- **体験版／製品版**: スクリプト定義シンボル `KAIHENNAVI_FULL` があると鍵が外れる（`Edition.IsTrial`）。`Edition.BoothUrl` は仮の `https://booth.pm/`
- **言語**: `Editor/Localization/{ja,en,zh-Hans,ko}.json`（キーと文言だけの平らな JSON）。文言を足したら 4 ファイルともキーをそろえる（`tools/check.sh` が検査）

## 6. ファイル構成

```
Packages/com.suzuki-y0.jeni.kaihen-navi/
  package.json / CHANGELOG.md
  Editor/
    Jeni.KaihenNavi.Editor.asmdef
    Core/  KaihenNaviWindow（画面）, BackupService / BackupSnapshot（バックアップ）,
           MaterialCopier, PartGuesser（髪・服・目・肌の判定）, AvatarFinder, HierarchyPath,
           WorkFolders, UndoHelper, L10n, Ui, PackagePaths, Edition
    Features/  FeatureBase, FeatureCatalog（カードの並び）, ColorFeature, ToggleFeature, TransformFeature
    UI/KaihenNavi.uss
    Localization/*.json
tools/
  check.sh      Unity なしのコンパイル確認（apt install mono-mcs が必要）
  gen_meta.py   新しいファイルを足したら実行して .meta を作る（GUID はパスから固定生成）
  stubs/        確認用の API スタブ（配布物には含めない）
```

新しい機能は `FeatureBase` を継承し、`FeatureCatalog` の `PlannedFeature` を置き換える。何かを変える直前に `ctx.BeginEdit()`、オブジェクトを作ったら `ctx.NoteCreated()` を呼ぶ。

## 7. 残りのタスク（資料の順）

1. Unity での動作確認と修正（上記）
2. 紹介動画（30 秒程度）と説明書、BOOTH で無料体験版を公開
3. 配布: [jeni-tools-vpm](https://github.com/Suzuki-Y-0/jeni-tools-vpm)（ID `com.suzuki-y0.jeni`）に追加するか検討＋unitypackage。ただし有料版の配布方法は別途決める
4. 反応を見て製品版の 7 機能を追加（3 肌隠し、4 体型、5 身長、7 視点、8 ピンク修正、9 明るさ、10 サムネイル）
5. 知恵袋や X の質問を集め、機能の優先順位を見直す

## 8. 競合メモ（2026-10-11 に BOOTH を再検索）

資料作成後に近いものが出ている。統合（3 機能を 1 画面で）と自動バックアップで差別化する方針は維持。

- いめちぇん！（無料、lilToon 専用の色変え、複製してから変更）: https://booth.pm/ja/items/8788853 … 色変え単体ではほぼ競合
- MO-DERA（無料＋支援版、手順を案内するアシスト。自動では変更しない）: https://booth.pm/ja/items/8966216
- DESIREVO WARDROBE（800 円、衣装・マテリアル・メニュー設定）: https://booth.pm/ja/items/8854279
- AvatarOutfitMenuBuilder（無料、衣装切替メニュー自動生成）: https://booth.pm/ja/items/8961880
- Avatar Doctor（500 円〜、初心者向け診断）: https://booth.pm/ja/items/8797619

## 9. 作業環境メモ

- クラウドのセッションからは GitHub に新規リポジトリを作れない（既存リポジトリへの push は可）
- リモート URL は大文字小文字込みで `https://github.com/Suzuki-Y-0/KaihenNavi.git`
- コミット末尾に Co-Authored-By などの署名行を付けている
