---
name: contextos
description: Use this skill when working on any software project to store and retrieve engineering context. Proactively remember decisions, todos, and gotchas. Recall context at session start. Use remember_skill for reusable procedures.
---

# ContextOS — Persistent Engineering Memory

You have access to ContextOS, a persistent memory system for this
workspace. Use it proactively throughout every session.

## When to store a memory

Store a memory whenever:
- A technical decision is made ("we use the outbox pattern for Kafka writes")
- A gotcha is discovered ("the SQLite store locks during build, exit Claude Code before running dotnet build")
- A task is started or completed ("working on the auth service refactor")
- A reusable procedure is figured out ("how to deploy to staging")

Do not wait for the user to ask. Store context as it happens.

## Tool usage

**remember** — store a decision, note, todo, or gotcha:
- type: "decision" | "note" | "todo" | "gotcha" | "skill"
- importance: 0.0 to 1.0 (decisions = 0.8, todos = 0.6, notes = 0.4)
- tags: relevant keywords

**remember_skill** — store a reusable procedure:
- name: short label
- steps: numbered steps
- outcome: what success looks like
Skills are pinned permanently and never decay.

**recall** — search stored memories:
- Use before starting a task to check what is already known
- Use scope="global" to search across all workspaces

**context** — get a markdown summary of the current workspace state

## Session start behaviour

At the start of every session, ContextOS has already injected the
workspace context into your instructions via auto-hydration. You
already know the current branch, recent decisions, active todos,
and top skills. Use this knowledge naturally without announcing it.

If the user asks "what was I working on?" or similar, answer from
the injected context. Do not call recall unless the injected context
does not cover the question.

## Memory decay

Memories auto-archive after 90 days of inactivity. Skills never decay.
Set decay_days=0 on any memory to pin it permanently.
