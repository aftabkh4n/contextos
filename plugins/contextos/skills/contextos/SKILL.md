---
name: contextos
description: Persistent memory for this workspace. Store decisions, todos, gotchas, and reusable skills; recall by hybrid search.
---

# ContextOS

ContextOS has already injected the current workspace context into your
instructions. Use that first; do not re-fetch what you already have.

## Tools

- `remember`: store a `decision`, `note`, `todo`, `gotcha`, or `skill`.
  Set `importance` 0.0–1.0 and comma-separated `tags`.
- `remember_skill`: store a reusable procedure (`name`, `steps`,
  `outcome`). Pinned, never decays.
- `recall`: hybrid search over stored memories. `scope="global"`
  searches every workspace.
- `context`: assemble a markdown summary. `scope`: `current`, `week`,
  or `all`.

Store context as it happens. Do not wait to be asked.

## Answering "what was I working on?"

Use only the injected ContextOS context. Do not run git, list files,
search the repo, or call tools. If the context says nothing is stored,
reply in one sentence that nothing is recorded.

Memories auto-archive after 90 days of inactivity. Skills never decay.
