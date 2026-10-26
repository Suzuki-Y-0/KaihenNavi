# ワーカー比較：gpt-6.1-sol（Codex）vs deepseek-v4.1-flash（OpenCode Go）

同じ土台・同じ指示書で DeepSeek に作業させ、sol の結果と比べる。DeepSeek の成果は main に混ぜない（bench/ds-* ブランチに置くだけ）。

## 課題（指示書は bench/specs/）
| 課題 | 土台コミット | 指示書 | sol の結果（比較の基準） | sol の使用トークン |
|---|---|---|---|---|
| b：アクセ・脱ぎ着の Undo | dc26fde | run_b.md | dd020e8（ブランチ dev/attach-undo） | 188,670 |
| c：体型ページのちらつき・履歴 | dc26fde | run_c.md | 0cb20b7（ブランチ dev/body-history） | 176,711 |
| f：インナーカラー面判定 | b883ca1 | run_f.md | 6f3d2e6（ブランチ dev/inner-faces の最初の作業） | 147,346 |
（common.md は共通ルール。指示書の頭にすでに入っている）

sol 実行条件：`codex exec -m gpt-6.1-sol -c model_reasoning_effort=high --sandbox workspace-write`、git worktree で 1 課題ずつ。

## DeepSeek 側の実行
- opencode CLI（`npm i -g opencode-ai`）、OpenCode Go（https://opencode.ai/zen/go/v1、OpenAI 互換）。
- キーはクラウド環境の「ネットワークシークレット」（opencode.ai、Authorization: Bearer）で付く。セッション内にキーは無い。
- 直接 curl する時はヘッダー `x-opencode-session: <任意のID>` が必要（無いと MissingSessionID）。
- モデル：deepseek-v4.1-flash（一覧に deepseek-v4-pro もある）。

## 測るもの
1. tools/check.sh が一発で通るか（ビルド・テスト）
2. レビューで見つかった問題の数と重さ（sol と同じ観点で）
3. Unity での同じテストスクリプトの結果（ユーザーの PC。Claude が computer use で実行）
4. Claude（監督役）が直すのにかかった手間・トークン
5. ワーカーの所要時間・トークン
注意：ハーネスが違う（Codex CLI と opencode CLI）ので、モデルだけの差ではない。
