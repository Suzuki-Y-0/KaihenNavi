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
  -d "{\"model\":\"$model\",\"messages\":[{\"role\":\"user\",\"content\":\"ok\"}],\"max_tokens\":5}")
if echo "$probe" | grep -q '"error"'; then echo "OpenCode Go に届きません: $probe"; exit 1; fi

# opencode の設定（OpenAI 互換プロバイダーとして登録）
cfg="$(mktemp -d)/opencode.json"
cat > "$cfg" <<EOF
{
  "\$schema": "https://opencode.ai/config.json",
  "provider": {
    "ocgo": {
      "npm": "@ai-sdk/openai-compatible",
      "name": "OpenCode Go",
      "options": { "baseURL": "https://opencode.ai/zen/go/v1",
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
  wt="$repo/../kaihennavi-ds-$t"
  git -C "$repo" worktree add -f -B "bench/ds-$t" "$wt" "${base[$t]}"
  start=$(date +%s)
  (cd "$wt" && opencode run -m "ocgo/$model" "$(cat "$repo/bench/specs/run_$t.md")") \
    > "$out/ds-$t.log" 2>&1 || true
  echo "elapsed_sec=$(( $(date +%s) - start ))" >> "$out/ds-$t.log"
  (cd "$wt" && bash tools/check.sh) > "$out/ds-$t.check.log" 2>&1 \
    && echo "$t: check.sh OK" || echo "$t: check.sh NG"
  (cd "$wt" && git add -A && git -c user.name=bench -c user.email=bench@local \
     commit -qm "bench: deepseek $model 課題 $t" || true)
done
echo "ログ: $out（トークン数は opencode stats または各ログ末尾を参照）"
