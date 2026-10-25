#!/usr/bin/env bash
# Unity を使わずに、C# の構文と型の整合をざっと確認する。
# Unity / VRChat SDK / Modular Avatar の API はスタブ（tools/stubs）で置き換えているので、
# 実際の API との食い違いまでは分からない。最終確認は必ず Unity 上で行うこと。
#
# 使い方: tools/check.sh    （Mono の mcs が必要: apt install mono-mcs）
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$here")"
src="$repo/Packages/com.suzuki-y0.jeni.kaihen-navi/Editor"
out="$here/stubs/build"
mkdir -p "$out"

mapfile -t files < <(find "$src" -name '*.cs' | sort)

build() {
  local label="$1"; shift
  echo "== $label"
  mcs -nologo -target:library -langversion:7 -warnaserror+ -nowarn:0618 \
      -r:System.Core.dll \
      -out:"$out/KaihenNavi.$label.dll" "$@" \
      "$here/stubs/UnityStubs.cs" "$here/stubs/VrcAndMaStubs.cs" "${files[@]}"
}

build NoMA
build WithMA -define:KAIHENNAVI_MA
build Full -define:KAIHENNAVI_MA -define:KAIHENNAVI_FULL

# 言語ファイルのキーがそろっているか
python3 - "$src/Localization" <<'EOF'
import json, sys, os
folder = sys.argv[1]
base = json.load(open(os.path.join(folder, "ja.json"), encoding="utf-8"))
ok = True
for code in ["en", "zh-Hans", "ko"]:
    d = json.load(open(os.path.join(folder, code + ".json"), encoding="utf-8"))
    missing = sorted(set(base) - set(d)); extra = sorted(set(d) - set(base))
    if missing or extra:
        ok = False
        print(f"{code}: missing={missing} extra={extra}")
print("== Localization", "OK" if ok else "NG", f"({len(base)} keys)")
sys.exit(0 if ok else 1)
EOF

python3 "$here/test_konoasset.py"
bash "$here/test_history.sh"

echo "All checks passed."
