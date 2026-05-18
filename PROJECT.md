# ContextOS — Project Specification

**Tagline:** Persistent engineering context for AI coding agents.

**One-liner:** ContextOS is an MCP server that gives Claude Code, Cursor, and other agents memory that survives across sessions, so developers stop re-explaining their codebase every morning.

**License:** Apache 2.0

---

## 1. What we are building

A single-binary MCP server, written in .NET 10, that:

1. Stores engineering memories (decisions, notes, gotchas, todos) in a local SQLite database, one file per workspace.
2. Retrieves them by hybrid semantic + keyword search.
3. Auto-injects relevant context on session start, so the agent already knows where the developer left off.

Three MCP tools: `remember`, `recall`, `context`. Plus auto-hydration on the MCP `initialize` handshake — this is the differentiator.

## 2. What we are NOT building in v1

These are explicitly out of scope. Do not add them even if they seem easy.

- Web UI or dashboard
- Auth or multi-user
- Postgres support (SQLite only)
- Team sync or cloud features
- Automatic git ingestion (manual `remember` only for now)
- PR or issue ingestion
- Architecture graph
- Kubernetes diagnostics
- Timeline tool
- Cross-encoder reranking
- Memory decay policies
- Multi-agent collaboration

If you find yourself building one of these, stop and confirm with the user first.

## 3. Architecture

One .NET 10 process. One SQLite file per workspace. Three internal layers.

```
AI Client (Claude Code / Cursor)
        |
        | MCP over stdio
        v
ContextOS.Mcp (entry point)
        |
        +-- ContextOS.Core (domain)
        +-- ContextOS.Storage (SQLite + sqlite-vec)
        +-- ContextOS.Retrieval (hybrid search)
        +-- ContextOS.Embeddings (provider seam)
        +-- ContextOS.Git (libgit2sharp wrapper)
                |
                v
        BlazorMemory (existing library, used as engine)
```

**BlazorMemory** is an existing NuGet package owned by the same author. ContextOS depends on it as a library for storage, embedding, and search primitives. Do not modify BlazorMemory in this repo — treat it as a stable dependency.

## 4. Solution structure

```
/src
  /ContextOS.Mcp           entry point, MCP protocol handlers
  /ContextOS.Core          domain: Memory, Workspace, Hydration
  /ContextOS.Storage       SQLite + sqlite-vec, schema, migrations
  /ContextOS.Retrieval     hybrid search (vector + FTS5), RRF, scoring
  /ContextOS.Embeddings    provider seam (ONNX default, Ollama, OpenAI)
  /ContextOS.Git           libgit2sharp wrapper, workspace detection
/tests
  /ContextOS.Tests
/docs
/examples
  /claude-code-config
  /cursor-config
README.md
LICENSE
CLAUDE.md
PROJECT.md
```

Dependency direction: `Mcp -> Core -> {Storage, Retrieval, Git}`, `Retrieval -> Embeddings`, `Storage -> BlazorMemory`. No cycles.

## 5. Storage schema (SQLite)

```sql
CREATE TABLE workspaces (
  id          TEXT PRIMARY KEY,
  root_path   TEXT NOT NULL UNIQUE,
  name        TEXT NOT NULL,
  repo_url    TEXT,
  created_at  INTEGER NOT NULL
);

CREATE TABLE memories (
  id            TEXT PRIMARY KEY,
  workspace_id  TEXT NOT NULL REFERENCES workspaces(id),
  type          TEXT NOT NULL,
  content       TEXT NOT NULL,
  source        TEXT,
  tags          TEXT,
  importance    REAL NOT NULL DEFAULT 0.5,
  created_at    INTEGER NOT NULL,
  archived_at   INTEGER
);

CREATE INDEX idx_memories_workspace_created ON memories(workspace_id, created_at DESC);
CREATE INDEX idx_memories_type ON memories(workspace_id, type);

CREATE VIRTUAL TABLE memories_fts USING fts5(
  content, tags, content=memories, content_rowid=rowid
);

CREATE VIRTUAL TABLE memories_vec USING vec0(
  embedding float[384]
);

CREATE TABLE hydration_log (
  workspace_id  TEXT NOT NULL,
  session_id    TEXT NOT NULL,
  hydrated_at   INTEGER NOT NULL,
  context_hash  TEXT NOT NULL,
  PRIMARY KEY (workspace_id, session_id)
);
```

Database location: `~/.contextos/{workspace_id}.db` where `workspace_id` is the first 16 chars of SHA1 of the absolute repo root path.

## 6. MCP tools

### `remember`

Store a memory verbatim. No LLM extraction, no summarization.

Input:
- `content` (string, required)
- `type` (string, enum: note, decision, gotcha, todo. Default: note)
- `tags` (array of strings, optional)
- `importance` (number, 0 to 1, default 0.5)

Returns: the created memory ID and a short confirmation.

### `recall`

Hybrid search: vector + FTS5, merged via Reciprocal Rank Fusion, reranked by recency and importance.

Input:
- `query` (string, required)
- `k` (integer, default 5, max 20)
- `types` (array of strings, optional filter)

Returns: array of `{id, type, content, tags, created_at, score}`.

### `context`

Assemble the current engineering context for this workspace.

Input:
- `scope` (string, enum: current, week, all. Default: current)

Returns: a markdown block containing branch, recent commits, recent decisions, open todos.

### Auto-hydration (MCP `initialize` handler)

When a client connects, the server responds with `serverInfo.instructions` containing a context blob assembled from:
- Workspace name and current git branch
- Last 10 commits (sha, message, age)
- Top 5 memories by `importance * recency` from last 30 days
- All memories tagged "active" or with type "todo"

The blob must be under 2 KB. Log to `hydration_log` so we never double-inject for the same session.

## 7. Retrieval pipeline

1. Embed query via current provider.
2. Vector top-20 from `memories_vec`.
3. FTS5 top-20 from `memories_fts`.
4. Merge via Reciprocal Rank Fusion, constant = 60.
5. Rerank: `final_score = rrf_score * exp(-age_days / 30) * (0.5 + importance)`.
6. Return top-k.

No cross-encoder reranking. Measure first.

## 8. Embeddings

Provider seam:

```csharp
public interface IEmbeddingsProvider
{
    string Name { get; }
    int Dimension { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
```

Default: `OnnxMiniLmProvider` using `all-MiniLM-L6-v2`, 384-dim, CPU, bundled with the binary.

Opt-in via `~/.contextos/config.json`:
- `OllamaProvider` — `http://localhost:11434`
- `OpenAiProvider` — `text-embedding-3-small`, needs API key

Switching providers mid-workspace is not supported in v1. Refuse the change and tell the user to reindex.

## 9. Auto-hydration flow

On MCP `initialize`:

1. Walk up from cwd to find `.git`. If found, workspace root = that dir. Else workspace root = cwd.
2. Compute `workspace_id = SHA1(absolute_root_path)[:16]`.
3. Open or create `~/.contextos/{workspace_id}.db`. Run migrations.
4. Probe git: current branch, last 10 commits, uncommitted file count.
5. Query store: top 5 memories by `importance * recency` from last 30 days, plus all `todo` and `active`-tagged memories.
6. Assemble markdown context blob under 2 KB.
7. Return in `initialize` response under `serverInfo.instructions`.
8. Insert row into `hydration_log`.

## 10. Configuration

`~/.contextos/config.json`:

```json
{
  "embeddings": {
    "provider": "onnx",
    "ollamaUrl": "http://localhost:11434",
    "ollamaModel": "nomic-embed-text",
    "openAiModel": "text-embedding-3-small",
    "openAiApiKey": null
  },
  "hydration": {
    "enabled": true,
    "maxBytes": 2048
  }
}
```

Sensible defaults if the file is absent.

## 11. CLI surface

Two commands only:

- `contextos serve` — start the MCP server on stdio. This is what Claude Code/Cursor invoke.
- `contextos init` — print MCP config snippets for the user to paste into their client config.

No more CLI in v1.

## 12. Distribution

- Self-contained single-file publish per platform: `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`.
- GitHub Release with all four binaries attached.
- Also publish as a .NET tool: `dotnet tool install -g ContextOS`.
- Install snippet in README for each platform.

## 13. Success metric for v1

One real developer who is not the author uses ContextOS in Claude Code for a week and says "I would notice if it stopped working."

If we hit that within 30 days of launch, v1 worked.