#!/usr/bin/env bash
# Windows（Git Bash）で tools/check.sh を動かす。Mono は入れず、Unity に同梱の Mono を使う。
#
# 使い方: bash tools/check-win.sh
#   Unity の場所が違うときは KN_UNITY_MONO に MonoBleedingEdge のフォルダを指定する。
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
mono_home="${KN_UNITY_MONO:-}"
if [ -z "$mono_home" ]; then
  for candidate in "/c/Program Files/Unity/Hub/Editor"/2022.3.*/Editor/Data/MonoBleedingEdge; do
    [ -x "$candidate/bin/mono.exe" ] && mono_home="$candidate"
  done
fi
if [ -z "$mono_home" ] || [ ! -x "$mono_home/bin/mono.exe" ]; then
  echo "Unity 2022.3 の Mono が見つかりません。KN_UNITY_MONO に MonoBleedingEdge のフォルダを指定してください。" >&2
  exit 1
fi

# python3 が Microsoft Store の入口になっている環境があるので、本物の Python を探す。
python=""
for candidate in py python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c "import sys" >/dev/null 2>&1; then
    python="$(command -v "$candidate")"; break
  fi
done
if [ -z "$python" ]; then
  for candidate in "$LOCALAPPDATA"/Programs/Python/Python3*/python.exe; do
    [ -x "$candidate" ] && python="$candidate"
  done
fi
if [ -z "$python" ]; then
  echo "Python 3 が見つかりません。" >&2
  exit 1
fi

shims="$(mktemp -d)"
trap 'rm -rf "$shims"' EXIT
printf '#!/usr/bin/env bash\nexec "%s/bin/mono.exe" "%s/lib/mono/4.5/mcs.exe" "$@"\n' "$mono_home" "$mono_home" > "$shims/mcs"
printf '#!/usr/bin/env bash\nexec "%s/bin/mono.exe" "$@"\n' "$mono_home" > "$shims/mono"
printf '#!/usr/bin/env bash\nexec "%s" "$@"\n' "$python" > "$shims/python3"
chmod +x "$shims"/*

# bash からは shims、Python の subprocess からは Unity の mcs.bat / mono.exe が見つかるようにする。
PATH="$shims:$PATH:$mono_home/bin" exec bash "$here/check.sh"
