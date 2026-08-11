# Changelog

## v0.1.3 -- 2026-08-11

### Fixes
- Git integration now fails gracefully when LibGit2Sharp's native library is
  missing (dotnet tool install path). The server starts normally; remember,
  recall, and context work without git context.
- NuGet push in CI now derives the package version from the git tag instead
  of the hardcoded value in Directory.Build.props.
- Error message when ONNX model is missing now explains the three remediation
  options instead of pointing to a repo-local script.

### Known limitation
The dotnet tool package does not bundle the ONNX model or LibGit2Sharp's
native binaries. Platform binaries from the releases page include both and
require no extra setup.

---

## v0.1.0 -- 2026-05-22

First public release.

### What works
- Three MCP tools: remember, recall, context
- Auto-hydration on session start (the wedge)
- Hybrid search: vector + BM25, RRF, recency * importance scoring
- Three embeddings providers: bundled ONNX (default), Ollama, OpenAI
- Self-contained binaries for Windows, Linux, macOS (x64 + arm64)
- .NET tool path: dotnet tool install -g ContextOS
- Tested end-to-end with Claude Code

### Known limitations
- Cursor support is not manually verified for v0.1.0
- sqlite-vec not included; BLOB-only cosine scan is the v1 strategy
- No team or cloud sync (workspace-local SQLite only)
- No git auto-ingestion (manual remember only)

See PROJECT.md for the full v1 scope and v2 roadmap.
