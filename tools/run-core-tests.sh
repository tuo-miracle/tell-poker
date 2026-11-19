#!/usr/bin/env bash
# 不經 Unity，用 Mono + NUnitLite 編譯並執行 Poker.Core / Poker.AI 的測試（數秒完成，可與 Unity 並行）。
# 用法：tools/run-core-tests.sh [NUnitLite 參數，例如 --where "class =~ SidePot"]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
T="$ROOT/tools/core-tests"
PKG="$T/packages"
if [ ! -f "$PKG/NUnitLite.3.14.0/lib/net45/nunitlite.dll" ]; then
  nuget install NUnitLite -Version 3.14.0 -OutputDirectory "$PKG" > /dev/null
fi
OUT="$T/bin"; mkdir -p "$OUT"
cp "$PKG/NUnit.3.14.0/lib/net45/nunit.framework.dll" "$PKG/NUnitLite.3.14.0/lib/net45/nunitlite.dll" "$OUT/"
SRC=$(find "$ROOT/Assets/_Project/Scripts/Poker.Core" "$ROOT/Assets/_Project/Scripts/Poker.AI" "$ROOT/Assets/_Project/Scripts/Poker.Gestures" "$ROOT/Assets/_Project/Tests/Core" "$ROOT/Assets/_Project/Tests/Gestures" -name "*.cs" 2>/dev/null)
csc -nologo -warn:0 -langversion:9 -optimize+ -out:"$OUT/CoreTests.exe" -r:"$OUT/nunit.framework.dll" -r:"$OUT/nunitlite.dll" -r:System.Numerics.dll -r:System.Numerics.Vectors.dll \
  "$T/Main.cs" $SRC
cd "$OUT" && mono CoreTests.exe --noresult --labels=Off "$@"
