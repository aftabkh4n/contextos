# Changelog

## v0.2.3 -- 2026-10-14

### Fixed
- Plugin upgrades killed running ContextOS servers. On Windows,
  ensure-binary.sh ran `taskkill` on every contextos.exe before
  swapping the binary, so upgrading in one Claude Code window
  killed the server in every other window. Each version now
  installs into its own folder (bin/<version>) and plugin.json
  points at that folder. No binary is ever replaced and no process
  is killed. Old version folders are deleted at a later session
  start, keeping the newest older one for windows still running it.
- After an upgrade, the session start hook tells Claude that the
  server may need a `/mcp` Reconnect, since Claude Code starts the
  server while the new version is still downloading.

---

## v0.2.2 -- 2026-10-14

### Fixed
- Claude explored the repo (git log, file listings, session
  history) to answer "what was I working on?" even when the
  answer was already in the hydration blob. The "answer from
  context" rule now sits in the always-loaded hydration blob
  (serverInfo.instructions) instead of only in SKILL.md, which
  loads on demand.
- Plugin upgrade race: ensure-binary.sh deleted the old binary
  before downloading the new one (about 118 MB). Claude Code
  tried to start the server meanwhile, found nothing, and cached
  the failure for 15 minutes. The script now downloads into
  bin.staging, checks that `--version` exits 0, then swaps
  folders. A failed download or check keeps the old binary.
  The binary version is now read from plugin.json.

---

## v0.2.1 -- 2026-10-12

### Fixed
- Decay data loss on v0.2.0 upgrade: the V3 migration added
  last_recalled_at as NULL for existing rows, so the first
  v0.2.0 startup archived every memory older than decay_days
  (90 by default). V3 now backfills last_recalled_at. A new
  V4 repair migration restores rows that the broken decay pass
  already archived (archived_at set, last_recalled_at NULL),
  logging the restored count.

### Changed
- Auto-hydration blob is now capped at 1000 bytes (was 2 KB)
  with a one-line framing. Empty sections are omitted; commit
  messages truncate to 50 chars. Empty workspace returns a
  single-line directive instead of a two-sentence paragraph.
- Tool descriptions in tools/list reduced to one sentence per
  tool and one short phrase per parameter.
- recall results now return only id, type, content, score (and
  workspace for scope="global"). Content is truncated to 300
  chars with "..." when longer. null fields are omitted.
- The ContextOS skill (SKILL.md) is now under 150 words and
  tells the agent to answer "what was I working on?" from the
  already-injected context without running tools.

---

## v0.2.0 -- 2026-09-07

### Added
- Memory decay: memories auto-archive after decay_days of
  inactivity (default 90 days). Per-row override via
  decay_days=0 pins a memory permanently. Configurable
  via memory.decayDays in config.json.
- Skill memory type: new `remember_skill` MCP tool stores
  reusable procedures with name, steps, and outcome.
  Skills appear in context and hydration blobs. Skills
  never decay (pinned at creation).
- Cross-workspace search: `recall` now accepts
  scope="global" to search across all workspace DBs
  in ~/.contextos/. Results include workspace name.
  Corrupt or locked DBs are skipped gracefully.

### Changed
- Context tool now includes a Skills section between
  Active tasks and Recent decisions.
- Auto-hydration blob includes top-3 skills.
- AddMemoryAsync accepts decayDays parameter for
  per-memory pin at creation time.

### Fixed
- In-memory SQLite connections correctly excluded from
  global search workspace scan.

---

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
