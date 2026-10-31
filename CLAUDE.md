# かいへんナビ（KaihenNavi）

VRChat 改変の初心者向け Unity 2022.3 エディタ拡張。パッケージは `Packages/com.suzuki-y0.jeni.kaihen-navi`。

**作業を始める前に `HANDOFF.md` の 0 章を読むこと。**（いまの状態・残り・Unity での確かめ方）

## 決まり
- ユーザーとは日本語で話す。コードのコメントも日本語（既存のスタイル：C# 7.3 相当、var、日本語 XML コメント）。
- アバター・服・アクセの元アセットは絶対に直接書き換えない。必ず複製してから変更する。
- コードは Editor 専用。アップロードされるアバターに独自スクリプトを残さない（MA などの既存コンポーネントは可）。
- 新しいファイルを足したら `python3 tools/gen_meta.py` で .meta を作る。
- 文言を足したら `Editor/Localization/{ja,en,zh-Hans,ko}.json` の 4 つともキーをそろえる。
- 変更したら `bash tools/check.sh` を通す。最後は Unity で確かめる。
- テストで作ったプレハブ・ファイル・一時スクリプトは、終わったら消す。
- コードは GitHub（https://github.com/Suzuki-Y-0/KaihenNavi）が正。作業はブランチを切り、main へのマージはユーザーに確認してから。
