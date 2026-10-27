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

## 結論（Unity 未確認、コードのレビューと check.sh のみ）
詳細な数字と表は `bench/RESULTS.md`。

**deepseek-v4.1-flash は sol に近い成果を出せるが、今の時点では sol の代わりにそのまま任せるには少し足りない。**

確かなこと
- 3 課題とも指示書の主要項目は実装でき、`tools/check.sh` も通った。所要時間は約 10〜16 分、変更量は sol と同規模（sol の所要時間は記録なし）。
- 指示書に設計まで書いてある課題（f）は、sol とほぼ同じ設計になった。
- 履歴・Undo・保存のように、複数の仕組みが絡む課題（b、c）は細部が粗くなる傾向があった。
- ベンチの運用で分かったこと：同じリポジトリに答えがあると探して写す（b で実際に起きた）。隔離しても、パスの打ち間違い・標準入力待ち・権限拒否で作業が空になることがある（c で 3 回）。`run_ds.sh` は対策済み。

推測であること（Unity で確認が必要）
- b の脱ぎ着：MA の「OFF 側」処理が抜けており、既定オンの服がメニューをオフにしても脱げない疑い。コードを読んだ推測で、当たっていれば機能として壊れている。
- c の「戻すときに新しいバックアップを削除する」処理、f の座標変換（ルートのスケール）の扱いも、実際に動かさないと分からない。

判断
- 向いている：設計が決まっていて手順が明確な実装（f のようなもの）。
- 向いていない：履歴・Undo・保存の整合のように副作用を追う必要がある課題。レビューと Unity での確認を前提にした下書きとして使う。
- 直す手間は、sol より多くなりそう。

限界
- 各課題 1 回ずつの実行で、ばらつきは見ていない。
- ハーネスが違う（Codex CLI と opencode CLI）ので、モデルだけの差ではない。
- 判断を固めるには、b の脱ぎ着と c の履歴を Unity で確認するのが最も効く。
