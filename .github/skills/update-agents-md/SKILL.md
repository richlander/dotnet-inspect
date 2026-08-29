---
name: update-agents-md
description: Use before adding, growing, or restructuring content in AGENTS.md — keeps it under its 600-line cap, free of a large table of contents, and limited to cross-cutting binding rules.
---

# Updating AGENTS.md

AGENTS.md has been rewritten many times, and the same direction keeps getting
lost: it creeps back up in size and re-accumulates content that belongs
elsewhere. This skill is the standing charter that prevents that drift. Read
it before any edit to AGENTS.md, not just during a cleanup pass.

## Hard constraints

- **600-line cap.** `wc -l AGENTS.md` must stay at or under 600, checked as
  part of every edit — not deferred to a later cleanup.
- **Offset growth in the same edit.** If a change would push the file over the
  cap, remove or move at least as many lines as you add before finishing the
  edit. Do not land growth and plan to trim later.
- **No large table of contents.** The "Task-specific guidance" pointer table
  stays at 12 rows or fewer, holding only the highest-value entries. Everything
  else is reachable through `docs/README.md`, which is the general table of
  contents for both `docs/` and `docs/design/`. Do not grow a second TOC inside
  AGENTS.md.

## What belongs in AGENTS.md vs. a doc

AGENTS.md holds binding, cross-cutting rules: things nearly every session needs
regardless of which subsystem it touches. Everything else — mechanics, worked
examples, tables of edge cases, rationale, historical context — belongs in a
focused doc under `docs/`, `docs/design/`, `docs/runbooks/`, or
`docs/templates/`, with a short pointer left in AGENTS.md.

Test before adding prose: would an agent doing unrelated work (say, a
decompiler fix) need this fact in the next 30 seconds? If yes, state the rule
in one or two sentences and stop there. If the value only shows up once an
agent is deep in a specific task, it belongs in that task's owning doc instead.

## Workflow for changing AGENTS.md

1. Run `wc -l AGENTS.md` to record the baseline before editing.
2. Write new binding policy as the shortest statement of the rule itself — not
   its rationale, mechanics, or examples.
3. Put any accompanying detail (steps, tables, worked cases) in the owning doc
   and link to it. Prefer creating a new focused doc over inflating an
   unrelated one or leaving the detail in AGENTS.md.
4. Re-run `wc -l AGENTS.md`. If still over 600, extract further content — from
   the section you just changed or from whichever existing section most
   duplicates a linked doc — until it fits.
5. Run `npx markdownlint-cli AGENTS.md` (and any doc you edited) before
   committing.

## Extraction checklist when over budget

- A bullet that restates a longer explanation already in the linked doc: keep
  the bullet, delete the restatement.
- Mechanics or tables that only matter mid-task: move to the owning doc, leave
  one linking sentence in AGENTS.md.
- A rarely invoked scenario (well under one PR in twenty): reduce to a single
  pointer sentence, as with the Markout co-development loop.

## Table of contents discipline

- Keep the "Task-specific guidance" table at 12 rows or fewer. Choose entries
  by how often an agent needs them, not by comprehensiveness.
- When a new entry would push the table past 12, either swap out a
  lower-value existing row or route the new pointer through `docs/README.md`
  instead.
- Keep `docs/README.md` current when moving content out of AGENTS.md, so the
  general reader doesn't lose the pointer.

## Validation

```bash
wc -l AGENTS.md   # must be <= 600
npx markdownlint-cli AGENTS.md
```
