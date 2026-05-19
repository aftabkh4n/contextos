# Decisions

Small architectural choices made during the build that aren't worth a full
ADR but are worth a note. Newest first.

| Date       | Decision                                    | Rationale                                                                                                  |
|------------|---------------------------------------------|------------------------------------------------------------------------------------------------------------|
| 2026-05-18 | Manual end-to-end test of context tool with Claude Code | context tool returned correct markdown summary scoped to the contextos-test workspace; the agent parsed it and reformatted naturally; workspace isolation verified (only memories from this workspace shown). Validates Day 6. |
| 2026-05-19 | CONTEXTOS_HOME env var overrides ~/.contextos | Needed for test isolation on Windows (SpecialFolder.UserProfile reads the registry, not USERPROFILE env var, so subprocess env override does not work). Also useful for non-standard deployments. Both config.json and workspace DBs use this base dir. |
| 2026-05-18 | Drop sqlite-vec in favor of BLOB cosine scan | Eliminates a native cross-platform dependency. At v1 scale (<10k memories) the speed difference is undetectable. Revisit if v2 workspaces exceed ~50k memories. |
| 2026-05-18 | Drop BlazorMemory as a dependency           | BlazorMemory is a paid product; depending on it would break ContextOS's "free, one-line install" wedge. Storage/Embeddings/Retrieval are small enough to implement directly in ContextOS. |
| 2026-05-18 | Use .slnx solution format instead of .sln   | .slnx is the .NET 10 default and is supported by every relevant tool (dotnet CLI, VS, Rider, GitHub Actions). No reason to revert to the legacy format. |
| 2026-05-18 | Domain types live in Core, not Storage      | Standard ports-and-adapters layering. Storage depends on Core, not the reverse. Caught and fixed on Day 2 before Retrieval was wired. |
| 2026-05-18 | First successful end-to-end test with a real MCP client (Claude Code) | remember and recall both worked correctly with hybrid retrieval returning the stored memory verbatim. Validates Days 2-5 in production. |
