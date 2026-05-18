# ContextOS

ContextOS is an MCP server that gives Claude Code, Cursor, and other agents memory that survives across sessions, so developers stop re-explaining their codebase every morning.

**Status: under construction**

---

## What it does

A single-binary MCP server written in .NET 10. It stores engineering memories (decisions, notes, gotchas, todos) in a local SQLite database and retrieves them by hybrid semantic and keyword search. On session start it auto-injects relevant context so the agent already knows where you left off.

Three MCP tools: `remember`, `recall`, `context`.

## Local development setup

1. Clone the repo.
2. Run `bash scripts/fetch-model.sh` once to download the all-MiniLM-L6-v2 ONNX model (~22 MB) into `src/ContextOS.Embeddings/Models/`. The model is excluded from git.
3. Run `dotnet test`. All tests should pass.

```
git clone https://github.com/bro1o1/contextos
cd contextos
bash scripts/fetch-model.sh
dotnet test
```

## License

Apache 2.0. See [LICENSE](LICENSE).
