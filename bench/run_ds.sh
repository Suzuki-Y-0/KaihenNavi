#!/usr/bin/env bash
# DeepSeek（deepseek-v4.1-flash / OpenCode Go）で課題 b・c・f を 1 つずつ実行する。
# 前提：OpenCode Go のキーがネットワークシークレットで付く環境（BENCH.md 参照）。
# 使い方: bash bench/run_ds.sh [b c f]
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
out="$repo/bench/results"; mkdir -p "$out"
model="${DS_MODEL:-deepseek-v4.1-flash}"

# 疎通確認（キーが付かない環境ではここで止まる）
probe=$(curl -sS https://opencode.ai/zen/go/v1/chat/completions \
  -H "Content-Type: application/json" -H "x-opencode-session: bench-probe" \
  ${OPENCODE_API_KEY:+-H "Authorization: Bearer $OPENCODE_API_KEY"} \
  -d "{\"model\":\"$model\",\"messages\":[{\"role\":\"user\",\"content\":\"ok\"}],\"max_tokens\":5}")
if echo "$probe" | grep -q '"error"'; then echo "OpenCode Go に届きません: $probe"; exit 1; fi

# opencode の設定（OpenAI 互換プロバイダーとして登録）
cfg="$(mktemp -d)/opencode.json"
keyopt=""; [ -n "${OPENCODE_API_KEY:-}" ] && keyopt='"apiKey": "{env:OPENCODE_API_KEY}", '
cat > "$cfg" <<EOF
{
  "\$schema": "https://opencode.ai/config.json",
  "provider": {
    "ocgo": {
      "npm": "@ai-sdk/openai-compatible",
      "name": "OpenCode Go",
      "options": { $keyopt"baseURL": "https://opencode.ai/zen/go/v1",
                   "headers": { "x-opencode-session": "kaihennavi-bench" } },
      "models": { "$model": {} }
    }
  },
  "permission": { "edit": "allow", "bash": "allow" }
}
EOF
export OPENCODE_CONFIG="$cfg"

declare -A base=( [b]=dc26fde [c]=dc26fde [f]=b883ca1 )
tasks=("${@:-b c f}"); [ $# -eq 0 ] && tasks=(b c f)

for t in "${tasks[@]}"; do
  # 隔離：土台コミット 1 つだけ（履歴なし）の別リポジトリで作業させる。
  # 同じリポジトリの worktree だと git log --all で sol の答えが見えてしまう（実際に b で写された）。
  wt="$(mktemp -d /tmp/knbench-XXXXXX)/kaihennavi-ds-$t"
  git init -q "$wt"
  git -C "$wt" -c protocol.version=2 fetch -q --depth=1 --no-tags "file://$repo" "$(git -C "$repo" rev-parse "${base[$t]}")"
  git -C "$wt" checkout -q -B "bench/ds-$t" FETCH_HEAD
  start=$(date +%s)
  (cd "$wt" && timeout "${DS_TIMEOUT:-2400}" opencode run -m "ocgo/$model" "$(cat "$repo/bench/specs/run_$t.md")" < /dev/null) \
    > "$out/ds-$t.log" 2>&1 || true
  echo "elapsed_sec=$(( $(date +%s) - start ))" >> "$out/ds-$t.log"
  (cd "$wt" && bash tools/check.sh) > "$out/ds-$t.check.log" 2>&1 \
    && echo "$t: check.sh OK" || echo "$t: check.sh NG"
  (cd "$wt" && git add -A && git -c user.name=bench -c user.email=bench@local \
     commit -qm "bench: deepseek $model 課題 $t" || true)
  # 成果は本体リポジトリのブランチとして取り込む（衝突を避けるため既存は退避してから）
  git -C "$repo" fetch -q --no-tags "$wt" "+bench/ds-$t:bench/ds-$t"
  echo "$t: 作業場所 $wt"
done
echo "ログ: $out（トークン数は opencode stats または各ログ末尾を参照）"
