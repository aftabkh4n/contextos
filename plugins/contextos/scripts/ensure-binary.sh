#!/usr/bin/env bash
set -euo pipefail

# Each version installs into its own folder (bin/<version>) and plugin.json
# points at that folder. A folder is never replaced, so nothing here can
# touch a binary another Claude Code window is running. Never kill processes.
# Every failure exits 0: a failed install must not block Claude Code.

PLUGIN_DATA="${CLAUDE_PLUGIN_DATA}"
BIN_ROOT="$PLUGIN_DATA/bin"

# plugin.json is the single source for the binary version.
VERSION="$(grep -m1 '"version"' "${CLAUDE_PLUGIN_ROOT}/.claude-plugin/plugin.json" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
if [ -z "$VERSION" ]; then
  echo "ContextOS: could not read version from plugin.json" >&2
  exit 0
fi
TARGET="$BIN_ROOT/$VERSION"
BASE_URL="https://github.com/aftabkh4n/contextos/releases/download/v${VERSION}"

# Delete every version folder except the current one and the newest older
# one (another window may still run it), plus the pre-0.2.3 flat layout.
# Errors are ignored: a folder still in use stays until a later session.
cleanup() {
  local prev
  local d
  prev="$(for d in "$BIN_ROOT"/*/; do basename "$d"; done \
    | grep -E '^[0-9]+(\.[0-9]+)+$' | { cat; echo "$VERSION"; } \
    | sort -uV | grep -B1 -xF "$VERSION" | head -n1)" || true
  local entry name
  for entry in "$BIN_ROOT"/* "$BIN_ROOT/.version"; do
    [ -e "$entry" ] || continue
    name="$(basename "$entry")"
    case "$name" in
      "$VERSION" | "$prev" | "$VERSION".staging.*) continue ;;
    esac
    rm -rf "$entry" 2>/dev/null || true
  done
  rm -rf "$PLUGIN_DATA/bin.staging" "$PLUGIN_DATA/bin.old" 2>/dev/null || true
}

if [ -f "$TARGET/.version" ] && [ "$(cat "$TARGET/.version")" = "$VERSION" ]; then
  cleanup
  exit 0
fi

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Darwin)
    case "$ARCH" in
      arm64) RID="osx-arm64" ;;
      x86_64) RID="osx-x64" ;;
      *) echo "ContextOS: unsupported macOS architecture: $ARCH" >&2; exit 0 ;;
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

ARCHIVE="contextos-${RID}"
if [ "$RID" = "win-x64" ]; then
  ARCHIVE="${ARCHIVE}.zip"
else
  ARCHIVE="${ARCHIVE}.tar.gz"
fi

# Per-process staging folder, so two windows starting at once don't
# extract into the same place.
STAGING_DIR="$TARGET.staging.$$"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR" "${STAGING_DIR:?}"' EXIT
mkdir -p "$STAGING_DIR"

fail() {
  echo "ContextOS: $1. Will retry at the next session start." >&2
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

# .version goes in before the rename, so TARGET only ever appears complete.
printf '%s\n' "$VERSION" > "$STAGING_DIR/.version"

# Another window may have finished the same install while we downloaded.
if ! [ -f "$TARGET/.version" ]; then
  # A TARGET without .version is a leftover from an interrupted install.
  rm -rf "$TARGET" 2>/dev/null || fail "could not remove incomplete $TARGET"
  mv "$STAGING_DIR" "$TARGET" || fail "could not move the new binary into $TARGET"
fi

echo "ContextOS: binary $VERSION installed at $TARGET" >&2
# SessionStart stdout is added to Claude's context.
echo "ContextOS was just updated to v$VERSION. If the contextos MCP server shows as failed this session, tell the user to run /mcp and choose Reconnect."
cleanup
