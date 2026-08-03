# Stacked PRs

When an issue is too large for one coherent PR, prefer a **stack** — a sequence
of PRs, each targeting its predecessor's branch — over a single PR that grows
until it is unreviewable, and over parallel PRs that race in the same files. The
alternative to a stack is not a smaller change; it is the same change reviewed
worse.

[AGENTS.md](../AGENTS.md#stacked-prs-for-multi-slice-issues) states the rules
that bind. This document explains the mechanics behind them: why restacking
force-pushes, what a restack must and must not change, and how a stack interacts
with the fixed-head review rule.

## Building the stack

- **Every slice lands on its own.** A slice carries one behavioral claim, its
  own evidence, and no dependency on a later slice to be correct or safe. If a
  slice is only defensible once the next one lands, it is not a slice — fold it
  into the next.
- **Name the stack in every PR.** State the slice's position, its parent PR, and
  what remains. A stack's deferrals *are* the compatibility or non-action
  boundary the PR-summary rule already requires, so declare them: a reviewer
  should read a declared residual as scope rather than as a defect. Each slice's
  residual is the next slice's opening move; keep it enumerated.
- **Name every slice branch descriptively.** No prefix is required for CI:
  `ci.yml` deliberately applies no base-branch filter, so a PR runs CI whatever
  it targets. That was not always true — an allow list of base prefixes has to
  name every prefix a base can have, and each way of getting it wrong left a
  slice mergeable with no checks at all (#3684, #3558), so the filter was
  removed rather than extended (#3706).
- **One branch and one worktree per slice**, as for any PR. Branch slice N+1
  from slice N's branch rather than `origin/main`:
  `git worktree add -b <slice-branch> <path> <parent-branch>`.
- **Target the parent branch** so the PR diff shows only its own slice:
  `gh pr create --base <parent-branch>`.
- **Stop stacking when a slice would exist only to continue the stack.** CI cost
  is per PR; three coherent slices beat ten mechanical ones.

## Landing the stack

**Merge bottom-up, one at a time.** After each merge, confirm the next PR
retargeted to `main` and that its diff is still only its own slice. When the
diff shows work already in `main`, that is the signal to restack, not a defect.

**Restacking is normal, it is usually a button, and it force-pushes.** GitHub's
*Update with rebase* rebases the slice onto its base and force-pushes the head
branch; a stacking tool's restack does the same for every slice at once. Either
way the rewrite does not stop at the slice you pressed it on — every slice above
now sits on a base that no longer exists and has to be restacked too. One
gesture, several branches rewritten, most of which you were not looking at. That
cascade is the stack's defining operational fact.

The mechanism requires it: once a parent lands by squash or rebase merge its
commits get new identities, so the child still carries the pre-merge originals.
Its PR then re-reports the parent's work — the parent's commits reappear in the
child's commit list, and the three-dot diff GitHub renders against the new base
shows the parent's files again. Merging cannot repair that; rebasing onto the
new base can.

So force-push is the norm inside a stack rather than the violation it would be
on a standalone PR. The manual equivalent of the button, when you need it:

```bash
git fetch origin main
git rebase --onto origin/main <old-parent-tip> <slice-branch>
git push --force-with-lease origin <slice-branch>
```

Always `--force-with-lease`, never bare `--force`; it declines when the remote
moved under you instead of destroying whatever arrived. Restack only your own
slices, never one another contributor has pushed to — coordinate first — and
land a parent before disturbing what sits above it rather than rewriting under a
reviewer mid-read.

Only the stack's bottom open slice takes `origin/main` as its base. Every slice
above it is based on its parent slice's branch until that parent lands, so
before changing any branch's base, check whether it is part of a stack: merging
or rebasing an upper slice onto `main` pulls in work its parent has not landed
yet and makes the slice's diff report its parent's changes as its own.

## Proving a restack changed only the base

**A restack must change the base and nothing else.** Prove that rather than
assuming it: record the pre-rebase head first, because afterwards the branch
name resolves to the *new* head, and a range built from it describes something
other than the slice you rebased.

```bash
old=$(git rev-parse <slice-branch>)          # before rebasing
git range-diff <old-parent-tip>..$old origin/main..<slice-branch>
```

Every commit reported `=` is the claim. A restack that also changes content is a
rewrite wearing maintenance clothing; say so in the PR instead of letting it
pass as routine.

## Review inside a stack

**Review depth is per-slice, by that slice's own risk**, not the stack's total
size. A long stack does not make a trivial slice risky, and a small slice in a
risky area still earns the two-reviewer tier.

**A slice's head moves for reasons other than findings** — a restack, or a
retarget after the parent lands. The fixed-head rule applies to those the same
way: a reviewed slice whose head has moved is not ready until a review is clean
at the *new* head. Because one restack can move every head above it, a single
press can invalidate several reviews at once; that is the cost of the button,
and it is paid per slice. A posted `range-diff` is what keeps each of those
re-reviews a confirmation rather than a second full pass — without one, a
reviewer cannot tell a restack from a rewrite.

**A restack does not retire a finding.** It can destroy the exact head a
reviewer was given, which makes "reproduce it on a clean exact-head review
worktree" temporarily unactionable — not moot. An open finding survives the
rewrite and is re-verified at the new head, and the burden sits with whoever
moved the head: say whether the finding still applies and at which commit, and
post the new head so review can resume. A finding that disappears because its
head did is an unresolved finding.
