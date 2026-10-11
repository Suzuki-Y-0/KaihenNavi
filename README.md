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

配布時は [jeni-tools-vpm](https://github.com/Suzuki-Y-0/jeni-tools-vpm) の VPM リポジトリと unitypackage の 2 形式にする予定です。

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

ツールが作る複製マテリアルとバックアップは、プロジェクトの `Assets/KaihenNavi/<アバター名>/` に保存されます。アバターの元アセットは変更しません。

## 開発メモ

- 製品版のビルドはスクリプト定義シンボル `KAIHENNAVI_FULL` を付けると、鍵付き表示が外れます。
- lilToon の色調補正は `_MainTexHSVG`（色相 -0.5〜0.5、彩度 0〜2、明度 0〜2、ガンマ）を使います。lilToon 以外は `_Color` のみ変更します。
- 脱ぎ着メニューは MA の `ModularAvatarMenuInstaller` / `ModularAvatarMenuItem` / `ModularAvatarObjectToggle` で生成します。
