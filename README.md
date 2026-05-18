# ContextOS

ContextOS is an MCP server that gives Claude Code, Cursor, and other agents memory that survives across sessions, so developers stop re-explaining their codebase every morning.

**Status: under construction**

---

## What it does

A single-binary MCP server written in .NET 10. It stores engineering memories (decisions, notes, gotchas, todos) in a local SQLite database and retrieves them by hybrid semantic and keyword search. On session start it auto-injects relevant context so the agent already knows where you left off.

Three MCP tools: `remember`, `recall`, `context`.

## License

Apache 2.0. See [LICENSE](LICENSE).
