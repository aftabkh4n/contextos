# Changelog

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
