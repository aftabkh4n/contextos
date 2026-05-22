#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

RIDS=(win-x64 linux-x64 osx-arm64 osx-x64)
OUTDIR="dist"
rm -rf "$OUTDIR"
mkdir -p "$OUTDIR"

for rid in "${RIDS[@]}"; do
  echo "Publishing $rid..."
  dotnet publish src/ContextOS.Mcp -c Release -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTDIR/$rid"

  case "$rid" in
    win-*)
      (cd "$OUTDIR" && zip -r "contextos-$rid.zip" "$rid")
      ;;
    *)
      (cd "$OUTDIR" && tar czf "contextos-$rid.tar.gz" "$rid")
      ;;
  esac
done

ls -lh "$OUTDIR"/*.zip "$OUTDIR"/*.tar.gz 2>/dev/null || ls -lh "$OUTDIR"
