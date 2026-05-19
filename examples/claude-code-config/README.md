# ContextOS with Claude Code

This guide shows how to register the ContextOS MCP server with Claude Code
so that `remember`, `recall`, and `context` are available in every session.

## Prerequisites

Complete these steps once before registering the server.

1. **Install .NET 10 SDK.** Download from
   [dot.net](https://dot.net/download).

2. **Install Claude Code.** Follow the instructions at
   [claude.ai/code](https://claude.ai/code).

3. **Clone the repo and fetch the model.**

   ```sh
   git clone https://github.com/aftabkh4n/contextos
   cd contextos
   bash scripts/fetch-model.sh
   ```

   `fetch-model.sh` downloads the ONNX embedding model (~22 MB) into
   `src/ContextOS.Embeddings/Models/`. The server will not start without it.

4. **Build once.**

   ```sh
   dotnet build
   ```

   This produces the build artifacts that the `--no-build` flag below relies on.

## Register the server

Run this command once. The `--scope user` flag makes the server available in
every directory, not just the current one.

```sh
claude mcp add --scope user contextos -- dotnet run \
  --project /path/to/contextos/src/ContextOS.Mcp \
  --no-build
```

Replace `/path/to/contextos` with the absolute path to where you cloned the
repo. Use forward slashes even on Windows
(e.g. `C:/Users/yourname/repos/contextos`).

## Verify the registration

From any directory in your terminal:

```sh
claude mcp list
```

You should see `contextos` listed. If the status column shows "failed", see
Common issues below.

Inside a Claude Code session, run `/mcp` to see the connected servers and
their tools. You should see three tools: `remember`, `recall`, and `context`.

## What to try first

Once the server is connected, paste one of these prompts into Claude Code:

- "Use the contextos remember tool to store [a decision you just made]"
- "Use the contextos recall tool to find anything about [a topic in your codebase]"
- "Use the contextos context tool to summarize what I'm working on"

Claude will invoke the tool and show you what was stored or retrieved.

## Common issues

**Server shows as failed in `claude mcp list`.**
The most common cause is a missing model file. Run `bash scripts/fetch-model.sh`
from the repo root, then retry.

**Server appears in the list but the three tools don't show up in `/mcp`.**
Claude Code re-handshakes with MCP servers at startup. Exit Claude Code
completely and reopen it.

**File lock errors when running `dotnet build`.**
Claude Code keeps the server process running. The build will fail if it tries
to overwrite a DLL that is currently loaded. Exit Claude Code first, or
temporarily remove the server with `claude mcp remove contextos`, build, then
re-add it.
