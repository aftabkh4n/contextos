# Manual end-to-end test checklist

Run this before any release to confirm the three MCP tools work with a real
client. This is the same sequence run on Days 5 and 6 that produced the
DECISIONS.md entries for those dates.

Estimated time: 10-15 minutes.

## Setup

Use either a release binary or a source build.

**Release binary (preferred for pre-release testing):**
- [ ] Downloaded and placed the binary at a permanent path.
- [ ] `contextos --version` prints the expected version.
- [ ] `contextos --selftest` reports dimension 384 without errors.
- [ ] Server registered with Claude Code:
  ```sh
  claude mcp add --scope user contextos -- /path/to/contextos
  ```

**Source build:**
- [ ] `bash scripts/fetch-model.sh` completed without errors.
- [ ] `dotnet build` completed without errors in the repo root.
- [ ] Server registered with Claude Code:
  ```sh
  claude mcp add --scope user contextos -- dotnet run \
    --project /path/to/contextos/src/ContextOS.Mcp \
    --no-build
  ```

**Both paths:**
- [ ] `claude mcp list` shows `contextos` with status OK (not failed).
- [ ] `/mcp` inside a Claude Code session shows all three tools:
  `remember`, `recall`, `context`.

## Scenario 1: remember

**Prompt to paste into Claude Code:**

> Use the contextos remember tool to store the following decision: "We chose
> SQLite over Postgres because we want zero-dependency local installs."
> Set type to "decision" and importance to 0.8.

**What success looks like:**

- Claude invokes `remember` with the correct arguments.
- The tool returns a memory ID (a UUID) and a short confirmation string.
- Claude echoes the ID back to you.
- No error in the Claude Code sidebar next to the `contextos` server.

**Where to look if it fails:**

- If the tool errors: check `~/.contextos/` exists and is writable.
- If Claude doesn't invoke the tool: check `/mcp` and confirm the tool appears.
- If the model isn't loaded: look for "no functional embeddings provider" in
  the output; run `fetch-model.sh`.

---

## Scenario 2: recall

Run this after Scenario 1 so there is at least one memory to retrieve.

**Prompt to paste into Claude Code:**

> Use the contextos recall tool to find anything about "SQLite" or "database
> choice". Return up to 5 results.

**What success looks like:**

- Claude invokes `recall` with `query: "SQLite database choice"`.
- The tool returns an array containing the memory stored in Scenario 1.
- The `content` field matches what was stored verbatim.
- The `score` field is greater than 0.

**Where to look if it fails:**

- If results are empty but the memory was stored: the embedding may not have
  been written. Check that `fetch-model.sh` ran and the `.onnx` file is present
  in `src/ContextOS.Embeddings/Models/`.
- If the tool errors with a SQLite message: the DB file may be locked. Exit
  any other process that has the workspace DB open.

---

## Scenario 3: context

**Prompt to paste into Claude Code:**

> Use the contextos context tool with scope "current" to summarize what I'm
> working on in this workspace.

**What success looks like:**

- Claude invokes `context` with `scope: "current"`.
- The tool returns a markdown block that includes:
  - The workspace name or root path.
  - The current git branch.
  - At least one recent commit (if the repo has commits).
  - The memory stored in Scenario 1 under a "recent decisions" heading.
- Only memories from the current workspace appear (workspace isolation).
- Claude reformats the markdown naturally in its reply.

**Where to look if it fails:**

- If git info is missing: confirm the test is run inside a git repo. The
  server walks up from cwd to find `.git`.
- If no memories appear in the context block: check that `scope` was passed
  correctly. `scope=current` covers the last 7 days.
- If memories from another workspace appear: this is a workspace isolation
  failure; open an issue with the workspace IDs from the two DB files in
  `~/.contextos/`.

---

---

## Scenario 4: Auto-hydration on session start

This verifies that the agent receives workspace context automatically on
`initialize`, without the user calling any tool.

**Prerequisites:**

- The workspace at `D:/Projects/contextos-test` (or another workspace
  with stored memories and a git history) is registered with Claude Code.
- At least one memory has been stored in that workspace (run Scenario 1
  first if needed).

**Steps:**

1. Exit any running Claude Code session connected to this workspace.
2. Rebuild: `dotnet build src/ContextOS.Mcp`.
3. Open Claude Code in the workspace. Let it fully start (the MCP server
   starts as a subprocess).
4. Without calling any tool, ask the agent a vague question such as:
   "What was I working on?"

**What success looks like:**

- The agent answers using the injected context, for example describing the
  kafka outbox decision or the current git branch.
- The agent does NOT call `recall` or `context` to find the answer. The
  information was injected on `initialize`.
- The response references concrete details that exist in the stored
  memories or recent commits, not generic filler.

**Where to look if it fails:**

- If the agent says it has no context: check that `serverInfo.instructions`
  appears in the initialize response. Run the integration test
  `AutoHydrationTests.Initialize_InstructionsContainsStoredMemoryContent`
  to isolate the issue.
- If instructions are empty or contain only the friendly empty-workspace
  message: confirm the workspace DB has memories (`recall "anything"`).
- If the agent calls `recall` before answering: the client may not be
  passing `serverInfo.instructions` to the model. This is a client-side
  behaviour and outside ContextOS's control.

---

## After all four scenarios

- [ ] All three tools invoked without errors
- [ ] Memory stored in Scenario 1 was returned in Scenario 2
- [ ] Context block in Scenario 3 showed the correct workspace and the stored memory
- [ ] Auto-hydration in Scenario 4 surfaced context without a tool call
- [ ] No server crashes visible in the Claude Code sidebar
- [ ] `~/.contextos/` contains exactly one `.db` file for this workspace

---

## Pre-release checklist

Run this in addition to the four scenarios above before tagging any release.

### Binary verification

- [ ] Download the release binary for your platform from the draft release
      (or build with `scripts/publish-all.sh`).
- [ ] `contextos --version` prints the expected version string.
- [ ] `contextos --selftest` reports dimension 384 (ONNX) without errors.

### Registration

- [ ] `claude mcp add --scope user contextos -- /path/to/binary` succeeds.
- [ ] `claude mcp list` shows `contextos` with no "failed" status.
- [ ] `/mcp` inside Claude Code shows all three tools.

### Fresh-shell smoke test

Open a **new** terminal or PowerShell window with no prior contextos session
active. Follow the README Install section word for word from the top. The goal
is to catch any step that requires knowledge not stated in the docs.

- [ ] `claude mcp remove contextos` first (if already registered), so the test
      starts clean.
- [ ] Download and extract completed without errors or extra steps.
- [ ] `--version` ran using the full extracted path (no PATH changes needed).
- [ ] `claude mcp add` completed using the full extracted path.
- [ ] `claude mcp list` shows `contextos` without requiring a shell restart.
- [ ] `/mcp` inside Claude Code shows all three tools in a fresh session.
- [ ] "First five minutes" section: auto-hydration answered "What was I working
      on?" without calling any tool.
- [ ] Every command in the README ran exactly as written, with no workarounds.

### .nupkg check

- [ ] `dotnet pack src/ContextOS.Mcp -c Release` produces a `.nupkg` under 5 MB.
- [ ] `dotnet tool install -g ContextOS --add-source dist/nupkg` installs without errors.
- [ ] `contextos --version` works after tool install.
- [ ] `contextos --selftest` fails with a clear provider-not-configured message
      (expected: the tool package has no bundled model).

### Final sign-off

- [ ] All four e2e scenarios passed.
- [ ] All pre-release checklist items passed.
- [ ] DECISIONS.md is up to date with any choices made during the release prep.
- [ ] README and INSTALL.md accurately describe the version being released.
