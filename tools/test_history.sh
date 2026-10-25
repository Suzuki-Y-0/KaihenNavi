#!/usr/bin/env bash
# 履歴の保存・分類と、体型の部位選択に副作用がないことを Unity なしで確認する。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$here")"
mkdir -p "$here/stubs/build"
mapfile -t sources < <(find "$repo/Packages/com.suzuki-y0.jeni.kaihen-navi/Editor" -name '*.cs' | sort)
mcs -nologo -target:exe -langversion:7 -define:KAIHENNAVI_MA,KAIHENNAVI_FULL -warnaserror+ -nowarn:0618 -r:System.Core.dll \
    -out:"$here/stubs/build/HistoryTests.exe" \
    "$here/stubs/UnityStubs.cs" "$here/stubs/VrcAndMaStubs.cs" "${sources[@]}" "$here/tests/HistoryTests.cs"
cd "$repo"
mono "$here/stubs/build/HistoryTests.exe"
