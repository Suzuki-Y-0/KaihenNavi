# かいへんナビ（Kaihen Navi）

VRChat 初心者向けの Unity エディタ拡張です。Unity の標準機能を「やりたいこと」単位のかんたんな画面で包み、ヒエラルキーやインスペクターを触らずに見た目を改変できるようにします。

- 「髪の色を変えたい」「服を脱ぎ着させたい」など、やりたいことから選ぶ
- 操作のたびに自動でバックアップし、いつでも「元に戻す」で戻せる
- アバターの元アセットは直接書き換えない（必ず複製してから変更）
- ツールのコードは Editor 専用。アップロードされるアバターに独自スクリプトは残らない

企画・引き継ぎ資料: [かいへんナビ 企画・引き継ぎ資料](https://claude.ai/code/artifact/9b4542aa-308b-43b6-ac15-3ce195893895)

## 動作環境

| 項目 | 内容 |
| --- | --- |
| Unity | 2022.3.22f1（VRChat 推奨環境） |
| VRChat SDK | Avatars 3.5.0 以降 |
| lilToon | 任意（あれば色調補正を使って自然に色を変える） |
| Modular Avatar | 任意・1.10.0 以降（あれば脱ぎ着メニューを非破壊で生成） |

## 導入（開発中）

`Packages/com.suzuki-y0.jeni.kaihen-navi` フォルダを、VCC で作った Unity プロジェクトの `Packages` フォルダにコピーします。メニューの **Tools > かいへんナビ** から開きます。

配布時は [jeni-tools-vpm](https://github.com/szyey/jeni-tools-vpm) の VPM リポジトリと unitypackage の 2 形式にする予定です。

## 機能

| # | やりたいこと | 裏で使う Unity 標準機能 | 範囲 | 状態 |
| --- | --- | --- | --- | --- |
| 1 | 髪や服の色を変えたい | マテリアル自動複製＋色変更 | 体験版 | 実装済み |
| 2 | 服やアクセを脱ぎ着できるようにしたい | オブジェクト ON/OFF＋メニュー自動生成 | 体験版 | 実装済み |
| 3 | 服からはみ出る肌を隠したい | 素体の BlendShape をスライダー調整 | 製品版 | 未着手 |
| 4 | 体型を変えたい | BlendShape＋ボーンスケール | 製品版 | 未着手 |
| 5 | 身長を変えたい | 全体スケール＋View Position 自動追従 | 製品版 | 未着手 |
| 6 | アクセの位置や大きさを直したい | Transform を大きな矢印＋スライダーで操作 | 体験版 | 実装済み |
| 7 | 視点の高さを直したい | View Position を目の位置に自動合わせ | 製品版 | 未着手 |
| 8 | ピンク色を直したい | 不足シェーダーを検出し導入手順を案内 | 製品版 | 未着手 |
| 9 | 明るさや影を整えたい | lilToon の明るさ関連項目だけ表示 | 製品版 | 未着手 |
| 10 | きれいなサムネイルにしたい | アップロード用カメラの位置合わせ＋背景 | 製品版 | 未着手 |
| 15 | アイテムの置き場所をまとめたい | 取り込み直後に用途を聞き、AssetDatabase.MoveAsset（GUID 維持）で `Assets/<アバター>_kaihen/<用途>/<アイテム>/` へ移す。Ctrl+Z で戻る | 体験版 | 実装済み（Unity 未確認） |

## 構成

```
Packages/com.suzuki-y0.jeni.kaihen-navi/
  package.json
  Editor/
    Jeni.KaihenNavi.Editor.asmdef   Editor 専用アセンブリ（MA があれば KAIHENNAVI_MA を定義）
    Core/                           ウィンドウ、アバター検出、バックアップ、部位判定、翻訳
    Features/                       やりたいこと 1 つにつき 1 クラス
    UI/KaihenNavi.uss               見た目
    Localization/                   ja / en / zh-Hans / ko の文言
tools/
  check.sh                          Unity なしで C# の構文と型の整合を確認する（Mono + スタブ）
  stubs/                            Unity / VRChat SDK / MA の API スタブ（確認用。配布物には含めない）
```

## 改変データの置き場所

改変プロジェクトの標準の形（AvatarPrefabMaterialExporter の出力と同じ）に置きます。アバターの元アセットは変更しません。

```
Assets/<アバター>_kaihen/
  <用途>/<アイテム>/                振り分けたアイテム
  Variant/
    <アバター>_Variant_Base.prefab   改変のベース（元アバターのプレハブバリアント。無ければ作る）
    <アバター>_<改変名>/              改変アバター 1 体につき 1 フォルダ（Exporter や手で作ったフォルダも選べる）
      <アバター>_<改変名>.prefab      改変アバター（ベースのプレハブバリアント）
      Materials/<元の名前>_<改変名>.mat  複製マテリアル
      Textures/ Meshes/ Menus/ …     ツールが作ったもの
  KaihenNavi/                        内部データ（Backups/<改変フォルダ名>/、Ledger/items.json、README.md）
```

`<アバター>_kaihen` はインポート振り分けと同じ決め方です（元アバターの Prefab が入っているルート → `<アバター>_kaihen` → 振り分けのルート → 無ければ作る）。前の版の `Assets/KaihenNavi/{Avatars,Bases,Ledger}` もそのまま開けます。「アイテムの置き場所」のページの「Variant へ移す」で新しい形へ移せます（GUID は変わらず、Ctrl+Z で戻ります）。

「アイテムの置き場所」（仕上げ）を ON にしておくと、unitypackage を取り込んだ直後に小さな窓で用途を聞き、取り込んだアイテムのフォルダを `Assets/<アバター>_kaihen/<用途>/<アイテム名>/` へ移します（ファイルの中身と GUID は変えず、場所だけ）。用途の一覧と ON/OFF は PC ごと、振り分け先のフォルダはプロジェクトごと（UserSettings）に覚えます。あとからは Project のフォルダを右クリック →「かいへんナビ」→「アイテムを振り分ける…」でも振り分けられます。

## 開発メモ

- 製品版のビルドはスクリプト定義シンボル `KAIHENNAVI_FULL` を付けると、鍵付き表示が外れます。
- lilToon の色調補正は `_MainTexHSVG`（色相 -0.5〜0.5、彩度 0〜2、明度 0〜2、ガンマ）を使います。lilToon 以外は `_Color` のみ変更します。
- 脱ぎ着メニューは MA の `ModularAvatarMenuInstaller` / `ModularAvatarMenuItem` / `ModularAvatarObjectToggle` で生成します。
