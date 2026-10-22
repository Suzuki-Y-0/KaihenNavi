#!/usr/bin/env bash
# 数値処理のみの回帰テスト。Unity API の見た目・Undo の動作確認は Unity 上で行う。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$here")"
mkdir -p "$here/stubs/build"
mapfile -t sources < <(find "$repo/Packages/com.suzuki-y0.jeni.kaihen-navi/Editor" -name '*.cs' | sort)
mcs -nologo -target:exe -langversion:7 -warnaserror+ -nowarn:0618 -r:System.Core.dll \
    -out:"$here/stubs/build/InnerColorMaskTests.exe" \
    "$here/stubs/UnityStubs.cs" "$here/stubs/VrcAndMaStubs.cs" \
    "${sources[@]}" "$here/tests/InnerColorMaskTests.cs"
mono "$here/stubs/build/InnerColorMaskTests.exe"
