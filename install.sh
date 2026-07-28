#!/usr/bin/env bash
set -euo pipefail

# Detect OS and architecture
OS="$(uname -s)"
ARCH="$(uname -m)"
RID=""

case "$OS" in
  Darwin)
    case "$ARCH" in
      arm64)  RID="osx-arm64" ;;
      x86_64) RID="osx-x64"   ;;
      *)
        echo "Unsupported platform. Download manually from https://github.com/aftabkh4n/contextos/releases" >&2
        exit 1
        ;;
    esac
    ;;
  Linux)
    case "$ARCH" in
      x86_64) RID="linux-x64" ;;
      *)
        echo "Unsupported platform. Download manually from https://github.com/aftabkh4n/contextos/releases" >&2
        exit 1
        ;;
    esac
    ;;
  *)
    echo "Unsupported platform. Download manually from https://github.com/aftabkh4n/contextos/releases" >&2
    exit 1
    ;;
esac

INSTALL_DIR="$HOME/.local/share/contextos"
BIN_DIR="$INSTALL_DIR/$RID"
BIN="$BIN_DIR/contextos"
URL="https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-${RID}.tar.gz"

echo "Installing ContextOS ($RID)..."
mkdir -p "$INSTALL_DIR"

# Download
echo "Downloading $URL..."
TMPFILE="$(mktemp)"
if ! curl -fsSL --show-error -o "$TMPFILE" "$URL"; then
  echo "Download failed: $URL" >&2
  rm -f "$TMPFILE"
  exit 1
fi

# Extract and clean up
echo "Extracting..."
tar xzf "$TMPFILE" -C "$INSTALL_DIR"
rm -f "$TMPFILE"

chmod +x "$BIN"

# Add to PATH if not already present
case ":${PATH}:" in
  *":${BIN_DIR}:"*)
    echo "$BIN_DIR is already in PATH."
    ;;
  *)
    ADDED_TO_PATH=false
    if [ -f "$HOME/.bashrc" ]; then
      printf "\nexport PATH=\"%s:\$PATH\"\n" "$BIN_DIR" >> "$HOME/.bashrc"
      ADDED_TO_PATH=true
    fi
    if [ -f "$HOME/.zshrc" ]; then
      printf "\nexport PATH=\"%s:\$PATH\"\n" "$BIN_DIR" >> "$HOME/.zshrc"
      ADDED_TO_PATH=true
    fi
    if [ "$ADDED_TO_PATH" = "true" ]; then
      echo "Added to PATH in ~/.bashrc and ~/.zshrc."
      echo "Restart your terminal or run: source ~/.bashrc (or ~/.zshrc)"
    fi
    ;;
esac

# Register with Claude Code
if command -v claude >/dev/null 2>&1; then
  claude mcp remove contextos 2>/dev/null || true
  claude mcp add --scope user contextos -- "$BIN"
  echo "Registered with Claude Code."
else
  echo "Claude Code CLI not found. After installing it, run:"
  echo "  claude mcp add --scope user contextos -- $BIN"
fi

# Selftest (non-fatal)
if "$BIN" --selftest; then
  echo "ContextOS OK"
else
  echo "Selftest failed. Check logs at ~/.contextos/logs/ or run: contextos --selftest"
fi

echo ""
echo "ContextOS installed successfully."
echo ""
echo "Next step: open Claude Code in any git repo and ask:"
echo "  'What was I working on?'"
echo ""
echo "Docs: https://github.com/aftabkh4n/contextos"
