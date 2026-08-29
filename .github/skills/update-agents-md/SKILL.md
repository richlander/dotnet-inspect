---
name: update-agents-md
description: Use before adding, growing, or restructuring content in AGENTS.md — keeps it under its 600-line cap, free of a large table of contents, and limited to cross-cutting binding rules.
---

# Updating AGENTS.md

Goal: keep AGENTS.md small, skimmable, and limited to binding cross-cutting
rules. Read this before any edit to AGENTS.md, not just during a cleanup pass.

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
4. Re-run `wc -l AGENTS.md`. If still over 600, migrate another whole section
   or subsection to its owning doc — see the extraction checklist below.
   Repeat with a full section each time; do not switch to shaving individual
   lines to close a small remaining gap.
5. Run `npx markdownlint-cli AGENTS.md` (and any doc you edited) before
   committing.

## Extraction checklist when over budget

Move content in whole, section-sized blocks, not by trimming prose word by
word. A block move is legible as a genuine reorganization; shaving a line here
and there to squeak under the cap is not — it reads as gaming the number and
tends to fuse unrelated sentences, drop headings that readers or other docs
depend on, or otherwise degrade the file it was supposed to keep skimmable.

- A whole subsection whose detail belongs in a focused doc: cut the entire
  subsection over, leave one summary paragraph plus a pointer in AGENTS.md.
- A bullet that restates a longer explanation already in the linked doc: keep
  the bullet, delete the restatement.
- A rarely invoked scenario (well under one PR in twenty): reduce to a single
  pointer sentence, as with the Markout co-development loop.

If after moving the obvious block-sized candidates the file is still over (or
barely under) 600, that is a signal to find one more whole section to
relocate — not to start merging headings into prose, deleting blank lines
between unrelated paragraphs, or rewording sentences purely to save a line.
Never remove or fold a heading to save space; if it is not clearly
disposable as a whole subsection, leave it and find a different block to
move instead.

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
