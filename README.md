# ContextOS

ContextOS is an MCP server that gives Claude Code, Cursor, and other agents memory that survives across sessions, so developers stop re-explaining their codebase every morning.

**Status: under construction**

---

## What it does

A single-binary MCP server written in .NET 10. It stores engineering memories (decisions, notes, gotchas, todos) in a local SQLite database and retrieves them by hybrid semantic and keyword search. On session start it auto-injects relevant context so the agent already knows where you left off.

Three MCP tools: `remember`, `recall`, `context`.

## Local development setup

**Prerequisites — do these once before running tests.**

### 1. Download the embeddings model

The default provider is `all-MiniLM-L6-v2` running via ONNX Runtime (~22 MB). It is not checked into git.

```
bash scripts/fetch-model.sh
```

This writes two files into `src/ContextOS.Embeddings/Models/`:
- `all-MiniLM-L6-v2.onnx`
- `vocab.txt`

The server will not start without these files. If you skip this step, you will see:

```
ContextOS cannot start: no functional embeddings provider.
The configured provider is: onnx
...
```

### 2. Build and test

```
dotnet test
```

All 25 tests should pass, including the end-to-end MCP integration tests that spawn the server as a subprocess.

### Full setup from scratch

```sh
git clone https://github.com/bro1o1/contextos
cd contextos
bash scripts/fetch-model.sh   # required — downloads the ONNX model
dotnet test
```

## License

Apache 2.0. See [LICENSE](LICENSE).
