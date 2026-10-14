# ContextOS Claude Code Plugin

Gives Claude Code persistent memory across sessions.

## Install

```
/plugin marketplace add aftabkh4n/contextos
/plugin install contextos@contextos
```

The plugin downloads the right binary for your platform on first use.

After updating, if contextos shows as failed in `/mcp`, choose Reconnect.
This happens once per update while the new version downloads.

## What it does

- Stores decisions, todos, notes, and skills in a local SQLite database
- Injects workspace context at session start (auto-hydration)
- Searches memory with hybrid BM25 + vector search
- Supports cross-workspace search with `scope="global"`

## Platform support

The SessionStart hook is a bash script, so it runs on macOS, Linux,
and Windows under Git Bash or WSL. Native Windows PowerShell support
is tracked separately. For now, Windows users without bash should use
the manual install below.

## Manual install (alternative)

If you prefer not to use the plugin system:

macOS / Linux:
```
curl -fsSL https://raw.githubusercontent.com/aftabkh4n/contextos/main/install.sh | bash
```

Windows (PowerShell):
```
irm https://raw.githubusercontent.com/aftabkh4n/contextos/main/install.ps1 | iex
```

## Source

https://github.com/aftabkh4n/contextos
