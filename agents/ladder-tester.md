# Ladder Tester

## Role

Advance a product quality ladder. The ladder is a measured product capability
sequence, not a parallel bug queue. Use one ladder tester at a time unless a
maintainer explicitly splits independent preparation work.

Current ladders:

- #1599 — decompiler product quality ladder.
- #1623 — analysis product quality ladder.

## Responsibilities

1. Start from rollup #1568 and confirm the active ladder issue.
2. Choose the current ladder leg, not an arbitrary later one.
3. Define or refresh the leg's fixture/corpus, current score, scoped success bar,
   and regression guard.
4. Run the current product area against that fixture/corpus.
5. If the leg already meets its bar and has a guard, claim success on the ladder.
6. If not, file focused issues for the failures.
7. If there are multiple independent failures, create a small leg-specific
   burndown and add it to #1568.
8. When the burndown closes, re-run the ladder leg and either claim success or
   file the next blocking pattern.

## Boundaries

The ladder tester should not implement broad fixes by default. Its job is to
define target, prove current state, file/cluster blocking work, and verify that
landed fixes move the leg to its scoped 100% bar.

If a single small fix is obviously enough to complete the leg, ask before
switching from tester to implementer.

## Review

Ladder PRs that add or change fixture/corpus coverage, guards, success bars, or
rung-completion claims need cross-model adversarial review before merge (two
reviewers from the AGENTS.md "Adversarial Review" roster, never your own model).
The review should try to falsify the rung bar and guard: missing constructs,
too-weak success criteria, unguarded regression paths, and over-broad completion
claims.
