---
name: update-agents-md
description: Use before editing AGENTS.md — enforces its 120-line launch-contract boundary, protected operator templates, and focused-document ownership.
---

# Updating AGENTS.md

Use this skill before every `AGENTS.md` edit.

## Hard constraints

- **120-line cap.** Run `wc -l AGENTS.md` before and after every edit. Never
  land growth above 120 or defer extraction.
- **240-line Steward cap.** When routing or PR-lifecycle ownership changes,
  keep `.claude/skills/steward/SKILL.md` at or below 240 lines.
- **Launch contract only.** Keep the repository mission, immediate routing,
  universal non-negotiables, and protected operator templates. Detailed
  mechanics, rationale, examples, edge cases, command inventories, and
  subsystem rules belong in focused documents.
- **No second index.** `docs/README.md` owns curated navigation. AGENTS may
  link the small set of entrypoints needed to begin work but never grows a
  task catalog.
- **Protect the root README boundary.** When entrypoint guidance changes,
  confirm `README.md` remains a product landing page of at most 120 lines.
  Featured capabilities require both a runnable CLI command and matching
  `https://dotnet-inspect.net/?w=...` packet URL.
- **Protect operator templates.** Keep the theme/status reminder, post-merge
  handoff, tmux command/state block, round report, and their invocation rules
  directly in `AGENTS.md`. Do not shorten or move them to satisfy the cap.

## Ownership test

Keep a statement in AGENTS only when an unrelated session needs it before it
can identify and open the focused owner. Put these elsewhere:

- repository worktrees, history, documentation, engineering, and publication
  mechanics: `docs/repository-workflow.md`;
- development posture: `docs/development-practices.md`;
- design scope: `docs/design-scope.md`;
- evidence: `docs/evidence-and-validation.md`;
- SDK, build, and testing: `docs/dev-environment.md`;
- PR rounds and state transitions: `docs/round-orchestration.md`;
- point-of-use PR decisions: the `steward` skill;
- subsystem behavior: its owning design or implementation guide.

The focused document owns the contract. AGENTS routes to it and may retain only
the launch-critical invariant needed before acquisition.

## Workflow

1. Record the baseline with `wc -l AGENTS.md`.
2. Identify the focused owner for every changed claim.
3. Edit the owner first when behavior or policy changes.
4. Keep only the shortest launch-time rule or route in AGENTS.
5. Repair incoming links and ownership statements; never leave a focused
   document claiming a removed AGENTS section is normative.
6. Confirm every protected template remains directly usable.
7. Check the AGENTS and root README caps.
8. Run Markdown lint for every changed Markdown file.

When the cap is exceeded, move a whole concern to its focused owner. Never
compress by deleting headings, fusing unrelated prose, removing blank lines,
or shaving wording solely to save lines.

## Validation

```bash
wc -l AGENTS.md   # must be <= 120
wc -l .claude/skills/steward/SKILL.md   # must be <= 240
wc -l README.md   # must be <= 120
npx markdownlint-cli AGENTS.md <other-changed-markdown>
```
