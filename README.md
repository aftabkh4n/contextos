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

### macOS (Apple Silicon)

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz | tar xz
sudo mv osx-arm64/contextos /usr/local/bin/contextos
contextos --version
```

### macOS (Intel)

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-x64.tar.gz | tar xz
sudo mv osx-x64/contextos /usr/local/bin/contextos
contextos --version
```

### Linux x64

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-linux-x64.tar.gz | tar xz
sudo mv linux-x64/contextos /usr/local/bin/contextos
contextos --version
```

### Windows (PowerShell)

```powershell
Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" -OutFile contextos.zip
Expand-Archive contextos.zip -DestinationPath .
# Move win-x64\contextos.exe somewhere on your PATH, e.g.:
Move-Item win-x64\contextos.exe C:\tools\contextos.exe
contextos --version
```

### .NET tool (requires .NET 10)

```sh
dotnet tool install -g ContextOS
```

Note: the .NET tool package does not bundle the ONNX model or native runtime
libs. You must configure Ollama or OpenAI as the embeddings provider before
the server will start. See [docs/CONFIG.md](docs/CONFIG.md).

---

## Register with Claude Code

Run this once after installing. The `--scope user` flag makes the server
available in every project, not just the current directory.

```sh
# macOS / Linux
claude mcp add --scope user contextos -- /usr/local/bin/contextos

# Windows
claude mcp add --scope user contextos -- C:\tools\contextos.exe
```

Verify: `claude mcp list` should show `contextos`. Then open a Claude Code
session and run `/mcp` to confirm the three tools appear.

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
