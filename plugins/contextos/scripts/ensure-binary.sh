#!/usr/bin/env bash
set -euo pipefail

PLUGIN_DATA="${CLAUDE_PLUGIN_DATA}"
BIN_DIR="$PLUGIN_DATA/bin"
BINARY="$BIN_DIR/contextos"
VERSION="0.2.1"
BASE_URL="https://github.com/aftabkh4n/contextos/releases/download/v${VERSION}"

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Darwin)
    case "$ARCH" in
      arm64) RID="osx-arm64" ;;
      x86_64) RID="osx-x64" ;;
      *) echo "ContextOS: unsupported macOS architecture: $ARCH" >&2; exit 1 ;;
    esac
    ;;
  Linux)
    RID="linux-x64"
    ;;
  *)
    BINARY="$BIN_DIR/contextos.exe"
    RID="win-x64"
    ;;
esac

if [ -f "$BINARY" ]; then
  exit 0
fi

mkdir -p "$BIN_DIR"

ARCHIVE="contextos-${RID}"
if [ "$RID" = "win-x64" ]; then
  ARCHIVE="${ARCHIVE}.zip"
else
  ARCHIVE="${ARCHIVE}.tar.gz"
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "ContextOS: downloading binary for $RID..." >&2
curl -fsSL --show-error "$BASE_URL/$ARCHIVE" -o "$TMP_DIR/$ARCHIVE"

# Extract directly into BIN_DIR, dropping the top-level RID folder.
# Binary and Models/ must sit side by side (OnnxMiniLmProvider looks for ./Models next to the exe).
if [ "$RID" = "win-x64" ]; then
  unzip -q "$TMP_DIR/$ARCHIVE" -d "$TMP_DIR"
  cp -R "$TMP_DIR/$RID/." "$BIN_DIR/"
else
  tar xzf "$TMP_DIR/$ARCHIVE" -C "$BIN_DIR" --strip-components=1
  chmod +x "$BINARY"
fi

echo "ContextOS: binary installed at $BINARY" >&2
