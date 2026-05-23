# Contributing to ContextOS

---

## Development environment

Requirements:
- .NET 10 SDK
- Git

```sh
git clone https://github.com/aftabkh4n/contextos
cd contextos
bash scripts/fetch-model.sh   # downloads the ONNX model (~22 MB)
dotnet build
dotnet test
```

`fetch-model.sh` writes two files into `src/ContextOS.Embeddings/Models/`:
`all-MiniLM-L6-v2.onnx` and `vocab.txt`. The server will not start without them.

To run the server locally and test it with Claude Code:

```sh
claude mcp add --scope user contextos -- dotnet run \
  --project /path/to/contextos/src/ContextOS.Mcp \
  --no-build
```

See [examples/claude-code-config/README.md](examples/claude-code-config/README.md)
for the file lock workaround that applies when rebuilding while a Claude Code
session is active.

---

## Scope

Before working on a feature, check [PROJECT.md](PROJECT.md) section 2 ("What
we are NOT building in v1"). If what you want to add is on that list, open an
issue first. Do not add Postgres, web UI, auth, team sync, cloud features,
PR/issue ingestion, cross-encoder reranking, or memory decay policies.

If a design choice is not covered in PROJECT.md, pick the simplest option
that does not close off future options, note it in DECISIONS.md (see format
below), and move on.

---

## Commit convention

Use conventional commits. One concern per commit.

| Prefix | Use for |
|--------|---------|
| `feat:` | new behavior visible to users or callers |
| `fix:` | bug fix |
| `test:` | adding or fixing tests |
| `refactor:` | code change with no behavior change |
| `docs:` | documentation only |
| `chore:` | build, CI, dependency updates |

The commit message body should explain why, not what. The diff already shows
what changed.

---

## Code style

- Target framework: `net10.0`.
- Nullable enabled, warnings as errors.
- Async all the way. No `.Result` or `.Wait()`.
- Records for DTOs, classes for services.
- File-scoped namespaces.
- No `var` for primitives; `var` is fine where the type is obvious from the
  right-hand side.
- No comments unless the "why" is non-obvious. Never explain what the code
  does; well-named identifiers do that.

---

## Tests

Use xUnit. Write tests for retrieval logic (RRF, scoring), workspace detection,
hydration assembly, and any parsing or transformation. Do not write tests
purely for coverage; write them where a regression would be hard to notice.

Run the full suite before opening a PR:

```sh
dotnet test
```

---

## Manual e2e test before release

Before tagging a release, run the checklist in
[tests/manual/day-7-e2e.md](tests/manual/day-7-e2e.md). All four scenarios
must pass. Document any failures before proceeding.

---

## DECISIONS.md format

When you make a non-obvious architectural choice, add a row to DECISIONS.md
(newest first). Keep it to one table row. The rationale column should explain
why, not what.

```
| 2026-MM-DD | Short description of the decision | One sentence explaining why this choice was made over the alternative. |
```

---

## Author preferences for docs and commit messages

These apply to all documentation, README files, and commit message bodies:

- Simple, natural English. No marketing voice.
- No em dashes. Use commas, periods, or parentheses instead.
- No AI-sounding words: "leverage", "robust", "seamless", "delve", "tapestry",
  "in today's fast-paced world". Plain words.
- Quantifiable claims where relevant. If you cannot quantify it, do not claim it.
- Read each sentence aloud mentally. If it sounds like a press release, rewrite it.
