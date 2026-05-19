# CLAUDE.md — Operating instructions for Claude Code

You are helping build **ContextOS**. Before doing anything in this repo, read `PROJECT.md` in full. It is the source of truth for scope, architecture, and what is in v1.

## Working agreements

1. **Read `PROJECT.md` before every non-trivial change.** If a request seems to fall outside the v1 scope listed there, stop and ask the user.

2. **Stay inside the architecture.** Do not introduce new top-level projects without confirming. Do not add Postgres, web UI, or auth. Do not modify BlazorMemory from this repo — it is a dependency.

3. **Small, reviewable commits.** One concern per commit. Conventional commit messages: `feat:`, `fix:`, `chore:`, `test:`, `docs:`, `refactor:`.

4. **Tests for non-trivial logic.** Especially the retrieval pipeline (RRF, scoring), workspace detection, and hydration assembly. Use xUnit. Aim for tests that would catch real regressions, not coverage for its own sake.

5. **No premature abstraction.** Build for the concrete v1 cases. The embeddings provider is the only abstraction we need up front because we already know we have three implementations. Everything else: write the concrete thing first, abstract when a second caller appears.

6. **No silent failures.** Log errors with context. The MCP server must never crash silently when a tool call fails — return a structured error to the client.

7. **Cross-platform from day one.** Paths via `Path.Combine`, line endings via `Environment.NewLine`. Test on Linux/macOS/Windows where possible.

## Style and conventions

- Target framework: `net10.0`.
- Nullable enabled, treat warnings as errors.
- Async all the way down. No `.Result` or `.Wait()`.
- Records for DTOs, classes for services.
- File-scoped namespaces.
- xmldoc on public APIs.
- No `var` for primitives; `var` is fine where the type is obvious from the right-hand side.

## Author preferences (carry into all docs, READMEs, commit messages)

These are the user's stated preferences across past projects and apply here too:

- Simple, natural English. No marketing voice.
- No em dashes (—). Use commas, periods, or parentheses instead.
- No AI-sounding buzzwords ("leverage", "robust", "seamless", "delve", "tapestry"). Plain words.
- Quantifiable claims where relevant. If you can't quantify, don't claim.
- ATS-friendly is not relevant here, but the same plainness applies.

## When stuck

If a design choice is not covered in `PROJECT.md`:
1. Pick the simplest option that does not paint us into a corner.
2. Note the decision in a `DECISIONS.md` file with one sentence of rationale.
3. Move on. Don't ask the user for every small choice.

If a choice is large enough to change scope or architecture, ask first.

## Development workflow

### File lock workaround

ContextOS.Mcp is invoked as a subprocess by any Claude Code session that
has it registered. While that session is alive, ContextOS.Mcp's DLLs are
locked and `dotnet build` will fail with MSB3027 errors.

Before rebuilding during development:
  1. Exit the Claude Code session that has contextos registered, OR
  2. Run `claude mcp remove contextos` to temporarily unregister

After rebuilding, restore with:
  `claude mcp add --scope user contextos -- dotnet run --project /path/to/contextos/src/ContextOS.Mcp --no-build`

For local dev only. Production users won't see this because they invoke
the published binary, not `dotnet run`.

## Definition of done for any task

- Compiles with no warnings.
- Tests pass.
- New behavior has at least one test.
- README updated if user-facing.
- Commit message is conventional and explains the *why*.