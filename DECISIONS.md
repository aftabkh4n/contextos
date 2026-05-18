# Decisions

Small architectural choices made during the build that aren't worth a full
ADR but are worth a note. Newest first.

| Date       | Decision                                    | Rationale                                                                                                  |
|------------|---------------------------------------------|------------------------------------------------------------------------------------------------------------|
| 2026-05-18 | Drop sqlite-vec in favor of BLOB cosine scan | Eliminates a native cross-platform dependency. At v1 scale (<10k memories) the speed difference is undetectable. Revisit if v2 workspaces exceed ~50k memories. |
| 2026-05-18 | Drop BlazorMemory as a dependency           | BlazorMemory is a paid product; depending on it would break ContextOS's "free, one-line install" wedge. Storage/Embeddings/Retrieval are small enough to implement directly in ContextOS. |
| 2026-05-18 | Use .slnx solution format instead of .sln   | .slnx is the .NET 10 default and is supported by every relevant tool (dotnet CLI, VS, Rider, GitHub Actions). No reason to revert to the legacy format. |
| 2026-05-18 | Domain types live in Core, not Storage      | Standard ports-and-adapters layering. Storage depends on Core, not the reverse. Caught and fixed on Day 2 before Retrieval was wired. |
