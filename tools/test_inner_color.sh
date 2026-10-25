#!/usr/bin/env bash
# 数値処理とファイル管理の回帰テスト。見た目・Unity 自体の Undo は Unity 上で確認する。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$here")"
mkdir -p "$here/stubs/build"
mapfile -t sources < <(find "$repo/Packages/com.suzuki-y0.jeni.kaihen-navi/Editor" -name '*.cs' | sort)
mcs -nologo -target:exe -langversion:7 -define:INNER_COLOR_ASSET_TESTS -warnaserror+ -nowarn:0618 -r:System.Core.dll \
    -out:"$here/stubs/build/InnerColorTriangleTests.exe" \
    "$here/stubs/UnityStubs.cs" "$here/stubs/VrcAndMaStubs.cs" \
    "${sources[@]}" "$here/tests/InnerColorTriangleTests.cs" "$here/tests/InnerColorAssetTests.cs"
mono "$here/stubs/build/InnerColorTriangleTests.exe"
