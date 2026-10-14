#!/usr/bin/env bash
set -euo pipefail

PLUGIN_DATA="${CLAUDE_PLUGIN_DATA}"
BIN_DIR="$PLUGIN_DATA/bin"
STAGING_DIR="$PLUGIN_DATA/bin.staging"
OLD_DIR="$PLUGIN_DATA/bin.old"
VERSION_FILE="$BIN_DIR/.version"

# plugin.json is the single source for the binary version.
VERSION="$(grep -m1 '"version"' "${CLAUDE_PLUGIN_ROOT}/.claude-plugin/plugin.json" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
if [ -z "$VERSION" ]; then
  echo "ContextOS: could not read version from plugin.json" >&2
  exit 1
fi
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
    BINARY_NAME="contextos"
    ;;
  Linux)
    RID="linux-x64"
    BINARY_NAME="contextos"
    ;;
  *)
    RID="win-x64"
    BINARY_NAME="contextos.exe"
    ;;
esac

BINARY="$BIN_DIR/$BINARY_NAME"

if [ -f "$BINARY" ] && [ -f "$VERSION_FILE" ] && [ "$(cat "$VERSION_FILE")" = "$VERSION" ]; then
  exit 0
fi

ARCHIVE="contextos-${RID}"
if [ "$RID" = "win-x64" ]; then
  ARCHIVE="${ARCHIVE}.zip"
else
  ARCHIVE="${ARCHIVE}.tar.gz"
fi

# Download and verify into a staging folder so the old binary keeps working
# until the new one is known good. Claude Code caches a failed server start
# for 15 minutes, so BIN_DIR must never be empty during an upgrade.
rm -rf "${STAGING_DIR:?}"
mkdir -p "$STAGING_DIR"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

fail() {
  rm -rf "${STAGING_DIR:?}"
  echo "ContextOS: $1. Keeping the existing binary." >&2
  exit 0
}

echo "ContextOS: downloading binary $VERSION for $RID..." >&2
curl -fsSL --show-error "$BASE_URL/$ARCHIVE" -o "$TMP_DIR/$ARCHIVE" \
  || fail "download of $BASE_URL/$ARCHIVE failed"

# Drop the top-level RID folder. Binary and Models/ must sit side by side
# (OnnxMiniLmProvider looks for ./Models next to the exe).
if [ "$RID" = "win-x64" ]; then
  unzip -q "$TMP_DIR/$ARCHIVE" -d "$TMP_DIR" || fail "extracting $ARCHIVE failed"
  cp -R "$TMP_DIR/$RID/." "$STAGING_DIR/" || fail "copying $ARCHIVE contents failed"
else
  tar xzf "$TMP_DIR/$ARCHIVE" -C "$STAGING_DIR" --strip-components=1 || fail "extracting $ARCHIVE failed"
  chmod +x "$STAGING_DIR/$BINARY_NAME"
fi

"$STAGING_DIR/$BINARY_NAME" --version >/dev/null 2>&1 \
  || fail "downloaded binary $VERSION did not run (--version failed)"

if [ "$RID" = "win-x64" ]; then
  taskkill //F //IM contextos.exe >/dev/null 2>&1 || true
fi

# On Windows a just-run exe can stay locked for a moment (antivirus scan,
# process teardown), so retry the renames before giving up.
move_with_retry() {
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    mv "$1" "$2" 2>/dev/null && return 0
    sleep 1
  done
  return 1
}

rm -rf "${OLD_DIR:?}"
if [ -d "$BIN_DIR" ]; then
  move_with_retry "$BIN_DIR" "$OLD_DIR" || fail "could not move the old binary aside"
fi
if ! move_with_retry "$STAGING_DIR" "$BIN_DIR"; then
  [ -d "$OLD_DIR" ] && mv "$OLD_DIR" "$BIN_DIR"
  fail "could not move the new binary into place"
fi
rm -rf "${OLD_DIR:?}"

# Written last so a failed swap re-runs on the next start.
printf '%s\n' "$VERSION" > "$VERSION_FILE"

echo "ContextOS: binary $VERSION installed at $BINARY" >&2
