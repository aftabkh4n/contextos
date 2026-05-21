# ContextOS

ContextOS is an MCP server that gives Claude Code and other AI coding agents
memory that survives across sessions. Store decisions, notes, and gotchas
once, recall them later, and get a context summary on session start so you
stop re-explaining your codebase every morning.

Apache 2.0. Built with .NET 10. Works with any MCP-compatible client.

---

## What works

Three MCP tools are implemented and tested end-to-end with Claude Code:

- **remember** -- store a memory (decision, note, gotcha, or todo) in the
  local SQLite database for this workspace.
- **recall** -- search memories by hybrid semantic and keyword search.
  Returns ranked results using Reciprocal Rank Fusion.
- **context** -- assemble a markdown summary of the current workspace:
  branch, recent commits, recent decisions, open todos. Scope can be
  `current`, `week`, or `all`.

## How it works

When you open Claude Code (or any MCP-compatible client) in a workspace,
ContextOS responds to the MCP `initialize` handshake with a context blob
in `serverInfo.instructions`. The client passes this to the model as a
system-level instruction before any user message.

The blob contains:

- Current git branch and the three most recent commits
- Open todos and memories tagged "active"
- Top recent decisions ranked by importance and recency

The agent already knows where you left off. You do not have to ask.

If the workspace has no stored memories and is not a git repo, the
instructions are a short prompt: "ContextOS connected. No memory yet for
this workspace. Use the remember tool to store decisions, todos, and notes."

## Installation

### Download a release binary (no .NET required)

Download the latest release for your platform from the
[releases page](https://github.com/aftabkh4n/contextos/releases/latest).

```sh
# macOS (Apple Silicon)
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz | tar xz
./osx-arm64/contextos --version
```

```sh
# Linux
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-linux-x64.tar.gz | tar xz
./linux-x64/contextos --version
```

```powershell
# Windows (PowerShell)
Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" -OutFile "contextos.zip"
Expand-Archive contextos.zip
.\contextos\win-x64\contextos.exe --version
```

```sh
# Or via .NET tool (if you have .NET 10 installed)
dotnet tool install -g ContextOS
```

### Register with Claude Code

After downloading, register the binary with Claude Code:

```sh
# macOS / Linux
claude mcp add --scope user contextos -- /path/to/contextos

# Windows
claude mcp add --scope user contextos -- C:\path\to\contextos.exe
```

See [examples/claude-code-config](examples/claude-code-config/README.md) for
the full setup guide, verification steps, and common issues.

## Development setup

For contributors only. End users should use the release binary above.

```sh
git clone https://github.com/aftabkh4n/contextos
cd contextos
bash scripts/fetch-model.sh   # downloads the ONNX embedding model (~22 MB)
dotnet build
dotnet test
```

Register with Claude Code from source:

```sh
claude mcp add --scope user contextos -- dotnet run \
  --project /path/to/contextos/src/ContextOS.Mcp \
  --no-build
```

Replace `/path/to/contextos` with the absolute path where you cloned the repo.
Use forward slashes even on Windows.

### Embeddings model

Three providers are supported. The default is ONNX (no setup beyond
downloading the model file). Ollama and OpenAI are also supported for
teams that prefer them.

| Provider | Setup | Dimension |
|----------|-------|-----------|
| `onnx` (default) | Run `fetch-model.sh` | 384 |
| `ollama` | `ollama serve` + `ollama pull nomic-embed-text` | 768 |
| `openai` | `OPENAI_API_KEY` env var or config | 1536 |

Configure via `~/.contextos/config.json`. See [docs/CONFIG.md](docs/CONFIG.md)
for the full reference.

The ONNX model is not checked into git. `fetch-model.sh` writes two files
into `src/ContextOS.Embeddings/Models/`:

- `all-MiniLM-L6-v2.onnx`
- `vocab.txt`

The server will not start without these. Missing files produce:

```
ContextOS cannot start: no functional embeddings provider.
The configured provider is: onnx
...
```

## License

Apache 2.0. See [LICENSE](LICENSE).
