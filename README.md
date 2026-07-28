# ContextOS

Persistent engineering context for AI coding agents. ContextOS is an MCP
server that gives Claude Code memory across sessions. Store decisions, notes,
and gotchas once; the agent already knows them next time you open a project.

Apache 2.0. Single binary. No cloud, no auth, no Postgres.

---

## How it works

When Claude Code opens a workspace, ContextOS injects a context summary into
the MCP `initialize` handshake. The agent receives your recent commits, open
todos, and top decisions before any user message. No tool call required.

Three MCP tools are always available:

- **remember** -- store a memory (decision, note, gotcha, or todo).
- **recall** -- search memories by hybrid semantic and keyword search.
- **context** -- assemble a full workspace summary on demand.

Storage is local SQLite, one file per workspace under `~/.contextos/`.

---

## Install

**macOS / Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/aftabkh4n/contextos/main/install.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/aftabkh4n/contextos/main/install.ps1 | iex
```

The script downloads the right binary for your platform, extracts it, adds it to PATH, registers with Claude Code, and runs a selftest.

<details>
<summary>Manual install (advanced)</summary>

> **v0.1.0 is the first release.** If downloading from "latest" returns a 404,
> the release has not been published yet. Check the
> [Releases tab](https://github.com/aftabkh4n/contextos/releases).

### macOS (Apple Silicon)

These commands are safe to run repeatedly. If you've installed ContextOS before, this will cleanly upgrade you to the latest version.

```sh
# Stop any running instance and remove existing registration (no-ops on a fresh machine)
pkill -f contextos || true
claude mcp remove contextos 2>/dev/null || true

# Extract to a permanent location (no sudo required)
mkdir -p "$HOME/.local/share/contextos/osx-arm64"
curl -fsSL https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz \
  | tar xz -C "$HOME/.local/share/contextos"

# Verify
"$HOME/.local/share/contextos/osx-arm64/contextos" --version

# Register with Claude Code (full path, works without any PATH changes)
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/osx-arm64/contextos"
```

### macOS (Intel)

```sh
pkill -f contextos || true
claude mcp remove contextos 2>/dev/null || true

mkdir -p "$HOME/.local/share/contextos/osx-x64"
curl -fsSL https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-x64.tar.gz \
  | tar xz -C "$HOME/.local/share/contextos"
"$HOME/.local/share/contextos/osx-x64/contextos" --version
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/osx-x64/contextos"
```

### Linux x64

```sh
pkill -f contextos || true
claude mcp remove contextos 2>/dev/null || true

mkdir -p "$HOME/.local/share/contextos/linux-x64"
curl -fsSL https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-linux-x64.tar.gz \
  | tar xz -C "$HOME/.local/share/contextos"
"$HOME/.local/share/contextos/linux-x64/contextos" --version
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/linux-x64/contextos"
```

### Windows (PowerShell)

```powershell
# Stop any running ContextOS (released by Claude Code MCP sessions)
Get-Process -Name contextos -ErrorAction SilentlyContinue | Stop-Process -Force
& claude mcp remove contextos

# Download and extract to a permanent location (no admin required)
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" `
  -OutFile "$env:TEMP\contextos-install.zip"
Expand-Archive "$env:TEMP\contextos-install.zip" -DestinationPath "$env:LOCALAPPDATA\Programs\contextos" -Force
$ProgressPreference = 'Continue'

# Verify it runs
& "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe" --version

# Register with Claude Code (full path, works without any PATH changes)
claude mcp add --scope user contextos -- "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe"
```

Optional: add to PATH so you can type `contextos` at the command line:

```powershell
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$env:LOCALAPPDATA\Programs\contextos\win-x64", "User")
# Restart your PowerShell session for the change to take effect.
```

### .NET tool (requires .NET 10)

```sh
dotnet tool install -g ContextOS
```

The .NET tool package does not bundle the ONNX model or native runtime libs.
Configure Ollama or OpenAI as the embeddings provider before starting the
server. See [docs/CONFIG.md](docs/CONFIG.md) for provider setup, then:

```sh
claude mcp add --scope user contextos -- contextos
```

</details>

---

## Verify the registration

Run `claude mcp list` — `contextos` should appear without a "failed" status.
Open a Claude Code session and run `/mcp` to confirm all three tools appear:
`remember`, `recall`, `context`.

---

## First five minutes

After registering, open Claude Code in any git repo with a few commits. Ask:

> "What was I working on?"

The agent will describe the recent commit history and any stored memories
without calling any tool. That is auto-hydration working.

Then try storing a memory:

> "Use contextos remember to store: We chose SQLite over Postgres to keep
> the install zero-dependency. Type: decision, importance: 0.8."

Close Claude Code, reopen it in the same directory, and ask again. The
decision comes back automatically.

---

## Technical summary

ContextOS listens on stdio and speaks the MCP protocol. On the `initialize`
handshake it assembles a context blob (recent commits, top memories by
importance and recency, open todos) and returns it in `serverInfo.instructions`.
The client passes this to the model as a system instruction before any user
message lands.

Search uses a hybrid pipeline: vector cosine similarity over 384-dim embeddings
(all-MiniLM-L6-v2, runs locally via ONNX Runtime) merged with SQLite FTS5
full-text search. The two ranked lists are merged via Reciprocal Rank Fusion
and reranked by recency and stored importance. All of this runs in-process with
no network calls for the default ONNX provider.

State lives in `~/.contextos/{workspace_id}.db`, one SQLite file per workspace.
The workspace ID is derived from the absolute path of the nearest `.git` directory.
No data leaves the machine unless you configure the OpenAI embeddings provider.

---

## Scope

v1 is a local, single-user tool. Out of scope: web UI, auth, Postgres, team
sync, cloud features, PR ingestion, architecture graphs, memory decay policies.
See [PROJECT.md](PROJECT.md) for the full spec.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

Apache 2.0. See [LICENSE](LICENSE).
