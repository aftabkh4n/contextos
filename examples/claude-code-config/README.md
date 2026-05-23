# ContextOS with Claude Code

This guide shows how to register the ContextOS MCP server with Claude Code
so that `remember`, `recall`, and `context` are available in every session.

---

## Quick setup (release binary)

This is the recommended path. No .NET SDK required.

1. **Download and extract the binary** for your platform. See
   [docs/INSTALL.md](../../docs/INSTALL.md) for per-platform instructions.
   The short version:

   ```sh
   # macOS / Linux
   mkdir -p "$HOME/.local/share/contextos"
   curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz \
     | tar xz --strip-components=1 -C "$HOME/.local/share/contextos"
   # (replace osx-arm64 with osx-x64 or linux-x64 as appropriate)
   ```

   ```powershell
   # Windows
   Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" -OutFile contextos.zip
   Expand-Archive contextos.zip -DestinationPath "$env:LOCALAPPDATA\Programs\contextos" -Force
   ```

2. **Register with Claude Code.**

   ```sh
   # macOS / Linux
   claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/contextos"
   ```

   ```powershell
   # Windows
   claude mcp add --scope user contextos -- "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe"
   ```

   The `--scope user` flag makes the server available in every project,
   not just the current directory.

3. **Verify.**

   ```sh
   claude mcp list
   ```

   You should see `contextos` in the list. Open a Claude Code session and
   run `/mcp` to confirm all three tools appear: `remember`, `recall`,
   `context`.

<!-- Screenshot: /mcp output showing the three tools -->

---

## Development setup (from source)

For contributors only. Requires .NET 10 SDK.

```sh
git clone https://github.com/aftabkh4n/contextos
cd contextos
bash scripts/fetch-model.sh   # downloads the ONNX model (~22 MB)
dotnet build
```

Register from source:

```sh
claude mcp add --scope user contextos -- dotnet run \
  --project /path/to/contextos/src/ContextOS.Mcp \
  --no-build
```

Replace `/path/to/contextos` with the absolute path to the repo clone.
Use forward slashes even on Windows
(e.g. `C:/Users/yourname/repos/contextos`).

### File lock workaround

Claude Code keeps the server process running. If you try to rebuild while
a session is active, `dotnet build` will fail with MSB3027 (files locked).

Before rebuilding:

1. Exit the Claude Code session that has contextos registered, OR
2. Remove the server temporarily:
   ```sh
   claude mcp remove contextos
   ```
   After building, re-add it with the command above.

---

## What to try first

Once the server is connected, paste one of these prompts into Claude Code:

- "What was I working on?" (tests auto-hydration; works without any stored memories if the workspace has git commits)
- "Use contextos remember to store: [a decision you just made]. Type: decision."
- "Use contextos recall to find anything about [a topic in your codebase]."
- "Use contextos context with scope current to summarize this workspace."

---

## Common issues

**Server shows as failed in `claude mcp list`**
Check the log at `~/.contextos/logs/contextos-<date>.log`. The most likely
cause is the binary not being found at the registered path, or (for .NET tool
installs) a missing embeddings provider.

**Three tools don't show up in `/mcp`**
Claude Code re-handshakes with MCP servers at startup. Exit Claude Code
completely and reopen it.

**File lock errors when running `dotnet build`**
See the file lock workaround section above.

For more issues, see [docs/TROUBLESHOOTING.md](../../docs/TROUBLESHOOTING.md).
