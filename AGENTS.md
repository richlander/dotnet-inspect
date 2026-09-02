# Agent instructions

## Start here

`dotnet-inspect` is a general .NET inspection tool spanning packages, restored
projects, platform libraries, metadata, APIs, dependencies, source provenance,
analysis, Findings, implementation diffs, and decompilation.

Read this file before doing work, then read only the entry documents below that
are relevant to your change.

This file is the source of truth for repository-wide engineering and workflow
rules. Detailed design, subsystem mechanics, version requirements, and
historical context belong with their owning code, workflow, or focused
documentation.

### How work runs on this repo

These practices serve one purpose: build robust, capable features that provide
foundational capabilities or compelling user experiences. The result should be
recognizable as conventionally sound, delightfully new or unique, or both.

[`docs/development-practices.md`](docs/development-practices.md) owns the full
development model and rationale. The binding summary:

- **Start from convention and best practice.** Name and justify any deliberate
  divergence, whether stricter or looser, and document its scope.
- **Prefer the simplest sufficient design.** Add complexity only when robust
  reliability or correctness requires it, or when it enables a compelling
  user-observable experience.
- **Design first and state the basis.** Name one normative owner and exact
  claim, then supporting designs, models, constraints, and evidence by role.
- **Start capabilities from named consumers.** Every new capability or
  substrate identifies its consumer in the specification and issue, links an
  overall end-to-end tracker, and may land its consumer in a later slice.
  Shared substrate must benefit and plan enablement through both the CLI and
  browser/Wasm hosts; a single-consumer or single-host substrate requires
  explicit user approval from the start.
- **Keep hosts thin.** Put reusable concepts and algorithms in host-neutral
  code. Duplicated host logic triggers a review for a shared abstraction that
  would also benefit another future host.
- **Choose rendering strategy deliberately.** Use Markout as the default
  host-neutral substrate for centralized, multi-format rendering, and call out
  host-specific rendering that bypasses it. Broad information domains such as
  call graphs and diffs require a documented structured-typing and format-
  lowering strategy, whether it uses Markout or another approach.
- **Demonstrate the pathological case.** Build boundary and failure fixtures;
  run contract-defining cases in CI and preserve valuable non-CI probes as
  reproducible design evidence.
- **Survey analogous implementations.** Use their behavior, omissions, and
  boundaries as evidence, not authority; transfer code or architecture only
  when license, provenance, assumptions, and architectural fit all transfer.
- **Bias toward progress and low carrying cost.** Land independently coherent
  slices; never present unfinished behavior as supported or preserve CLI flags
  solely for compatibility. Shipped product skills must match current behavior.
- **Lead with a demo.** Every PR demonstrates the scenario (a mockup for
  docs-only PRs) without fitting the implementation only to that example.
- **Treat critical review feedback as a design question first.** Ask whether
  the owning design addresses it before repairing code; keep paired design
  work moving quickly when the contract needs clarification.
- **Use extraordinary pre-work for complicated features.** Corpus evidence,
  an established oracle, a TLA+ model, or a closely developed specification
  should bound the contract before implementation.
- **Hot-start requested work through PR and review.** Agents may branch,
  commit, push, open the PR, and dispatch eligible rounds without separate
  approval; merge remains separately authorized.
- **Use the Markdown fast path.** For Markdown-only PRs at non-boundary rounds,
  `markdownlint` replaces `ci-required` as the pre-review and per-round gate.
- **Use bounded adversarial review to find design and implementation gaps.**
  Every non-trivial change gets two seats; repeated findings are evidence to
  revisit design, and six rounds ends the current review block.
- **Keep security work inside the repository threat model.** Focus on
  untrusted internet-origin data and construction-time containment, not local
  or intra-repository actors unless an owning design explicitly opts in.

> A change spanning Markout and this repo is rare and uses a separate
> co-development loop: read
> [`docs/markout-co-development.md`](docs/markout-co-development.md) before
> touching either repository.

## Session resume

The transcript survives a resumed session; repository and PR state may not.
Follow [Resume a session](docs/agent-session-state.md#resume-a-session) to
confirm the worktree and PR, refresh the effective base, restore window
identity, and classify the session. Handle conflicts, failed gates, or moved
bases first, and do not revisit decisions already settled in the transcript.

## Making your work findable

This section is tmux-specific and applies only inside a tmux pane — check
`[ -n "$TMUX" ]` first; outside tmux there is no window to name or option to
attach state to, so skip it entirely. Each window must identify its PR,
current state, and any decision it needs. Full tmux mechanics and rationale
live in [Agent session state](docs/agent-session-state.md); this section
states the binding rules.

- **Rename the window** to `pr<number>` (or `i<number>` before a PR exists),
  always targeting `"${TMUX_PANE:?}"`, and keep it stable except for a
  `-blocked` or `-conflict` suffix.
- **Announce PR identity** — the literal token `PR #<number>` or `PR <number>`,
  plus branch or expected head — at the start of work, after every resume, and
  at every round start. Round completions use the
  [round report](docs/round-orchestration.md#the-round-report).
- **Separate status from approval prompts.** Emit supporting status or analysis
  as normal visible output first; only after it appears in the session log may
  you open an approval prompt containing just the concise decision question and
  answer labels — never the report, checkpoint, or evidence itself.
- **Publish `@agent` and `@agent_state`** after every state change, each as its
  own single command (never inside `if`/`&&`/a loop). `@agent_state` carries
  `head` and `pr`/`issue`, plus `round`, `reviews`, `blocked`, `waiting`, and
  `rec` (`continue`, `wait`, `merge`, `split`, `approve`, `stop`); clear both
  when the window no longer owns the work. `blocked` names an issue/PR a
  person can act on; `waiting` names tool-evaluable predicates (`check:<name>`,
  `checks`, `merge`, `review`).
- **Signal `HELP`** with a persistent state plus one best-effort
  `tmux display-message` nudge when blocked on a human decision; clear it once
  the decision arrives.

### Keep the review-clean label current

`review-clean` is live, advisory state — it records that reviews are clean as
of a head SHA, not that the PR is mergeable right now. Reconcile it after every
resume and whenever the head or review result changes; never infer merge
readiness from its presence (see [Forming a candidate](#forming-a-candidate)).

- **Add it** when every required review at the current head is review-clean,
  recording the reviewed head SHA in the same comment or update.
- **Base movement alone does not remove it.** Classify the landed range per
  [Clean reviews are not spent by main
  moving](#clean-reviews-are-not-spent-by-main-moving); a no-interaction
  classification keeps the label on the unchanged reviewed head.
- **Remove it and expire recorded merge authorization** before a new round,
  author change, conflict recovery, restack, base-ref retarget, unresolved
  finding, or draft transition — anything that spends the clean reviews.

## User-directed workflow adjustments

The user may adjust a sequencing gate for a specific task or PR. Follow that
direction, record its scope and evidentiary consequence, and preserve every
other requirement. An adjustment does not make failed validation successful,
make an unmergeable PR ready, or transfer fixed-head evidence to a new head.
The standing adjustments and their exact evidence requirements live in
[User-directed workflow adjustments](docs/round-orchestration.md#user-directed-workflow-adjustments).

## Before changing files

- Keep the primary checkout attached to protected `main`; never develop or
  detach HEAD there.
- From the primary checkout, run `git fetch origin main`, then create a
  descriptive branch and linked worktree:
  `git worktree add -b <branch> <repo>/.worktrees/<slug> origin/main`. A
  stacked slice branches from its parent; during a GitHub outage, use the
  recorded last-known base allowed by
  [the stack rules](#stacked-prs-for-multi-slice-issues).
- Use one development worktree per PR and one temporary worktree per reviewer,
  under `.worktrees/` or (for reviewers) an OS temporary directory — never
  directly under the home directory.
- For an open PR, apply [Canonical round flow](#canonical-round-flow) before
  other work. Conflict recovery has first priority.
- Never amend. Rebase only before the first push. After publication, merge the
  effective base; never rebase or force-push reviewed history except when
  restacking your own slices under the stack rules.
- After integrating or resolving conflicts, re-read this file and the relevant
  focused docs. Do not include unrelated or another contributor's changes.
- Remove reviewer worktrees after review and reproduction finish. Remove a
  development worktree after merge, or once the pushed head is unlocked, all
  concurrent gates pass, and every required review is review-clean — recreate
  it later if needed.

## Task-specific guidance

Read the relevant entry before working in that area. This table covers the
highest-value entry points; the full index — every design doc, contributor
workflow doc, and PR template — lives in [`docs/README.md`](docs/README.md).

| Area | Read first |
| --- | --- |
| User-visible capabilities, commands, or examples | `README.md` |
| Core workspace, query, cache, or safety architecture | `docs/inspection-space.md` |
| A change crossing subsystem ownership boundaries | `docs/overview.md` |
| Implementation structure | the relevant section of `docs/architecture.md` |
| Layering and consumer boundaries | `docs/design/inspection-layers.md` |
| Command defaults and disclosure | `docs/design/progressive-disclosure.md` |
| Output data shapes and style | `docs/design/output-shapes.md`, `docs/design/style-guide.md` |
| Metadata and API inspection | `docs/design/assembly-inspection-query.md` |
| PDB and source acquisition | `docs/pdb-acquisition.md` |
| Security and untrusted input | `docs/design/untrusted-data-threat-model.md` |
| Decompiler raising, structuring, typing, or printer behavior | `docs/decompiler-correctness-pipeline.md`, then `docs/decompiler-raise-discipline.md`; use `docs/templates/decompiler-pr.md` for the PR body |
| Everything else — design docs, contributor workflow, PR templates, skills | `docs/README.md` |

Some files under `docs/design/` record proposals or design history. Prefer
current product behavior and tests over design history. When current sources
disagree, stop and resolve which owner is authoritative rather than silently
choosing one.

Keep user-facing product skills (`skills/`, shipped in the binary) separate
from repo-local contributor skills (`.github/skills/`, `.claude/skills/`); do
not select a product skill merely because an agent is maintaining this
repository. See
[User-facing vs. repo-local skills](taste/skill-guidance.md#user-facing-vs-repo-local-skills)
for the registration mechanics.

For routine development, use production dotnet-inspect
(`dnx dotnet-inspect -y -- <command>`) — normally current and much faster to
start than `dotnet run --project src/dotnet-inspect -c Release -- <command>`,
which is required only when evidence depends on an unmerged change. Full
rationale:
[`docs/dev-environment.md`](docs/dev-environment.md#which-dotnet-inspect-to-run).

## Design scope and composition

Full mechanics, the composition-document rules, TLA+ modeling guidance, and the
over-broad-design recovery procedure live in
[`docs/design-scope.md`](docs/design-scope.md). The binding rules:

- Default every design effort to one named architectural owner. A focused design
  may reference adjacent owner-issued types but must not redefine another
  owner's contract; beyond the single-claim transfer exception, cross-owner
  normative changes need focused efforts joined by a thin composition map.
- State boundaries and contracts as simply as possible. Never translate current
  or planned implementation into prose; code implements the contract.
- When product correctness joins facts across components, model the same
  owner-issued join currency — version, generation, identity, receipt, handle,
  or composite key — and preserve the association, freshness, and replacement
  semantics that make the product join sound. The model may abstract the
  currency's concrete representation.
- Let TLA+ module dependencies mirror product dependencies: consume stable
  owner-issued definitions and behaviors through named instances instead of
  copying them, and recheck the imported properties in each composition. A
  bounded result for one instance is evidence, not a proof transferred to
  another. Put contract-defining configurations in
  `eng/tla-expected-exit-codes.txt` so CI enforces their exact semantic
  verdict; see
  [TLA+ methodology](docs/tla-plus-methodology.md#compose-models-along-product-boundaries).
- A **broad design** normatively specifies multiple independently owned
  components (outside that one exception) or sweeps an end-to-end lifecycle.
  Do not start or broaden into one without the user's explicit request or
  approval; a large issue, cross-cutting motivation, or reviewer suggestion is
  not approval.
- If review keeps discovering new component-internal contracts, stop and apply
  the [scope-violation recovery transition](#recovery-transitions).
- Lock a new cross-cutting pattern as its own focused design document — defining
  only the pattern's contract, not other owners' internals — then have each
  owner adopt it one at a time rather than one PR sweeping every owner; see
  [Stage implementation after locking the design](docs/design-scope.md#stage-implementation-after-locking-the-design).

## Repository-wide engineering constraints

- Keep product paths SRM-only, NativeAOT-friendly, Roslyn-free, and free of
  inspected-assembly loading.
- Preserve layer ownership. Metadata owns metadata facts, Analysis owns IL-body
  evidence, CSharpText owns model-free textual grammars and layout, CSharp owns
  model-bound C# spelling and type views, Research composes evidence, and the CLI
  owns command and presentation concerns.
- Reuse existing typed models, Finding contracts, section schemas, serializers,
  and resolution services before adding parallel abstractions.
- Preserve behavior-safe defaults and progressive disclosure. Network,
  source-content, exhaustive, or otherwise expensive work must remain explicit
  or capability-gated.
- Keep failure visible. Do not turn decode, acquisition, analysis, or rendering
  failures into success-shaped empty output.
- Treat identifiers, provenance, local evidence, correspondence, and
  presentation as separate concerns. Do not infer one from display text when a
  typed identity exists.
- Use inclusive terminology: "allow list"/"deny list", never
  "whitelist"/"blacklist" (match casing and word form, e.g. `allowList`,
  "deny-listed").

### Keep design and adversarial review within scope

Unless an owning design explicitly opts in, do not add design requirements or
adversarial-review findings for symlinks/reparse points, same-machine users or
agents, or files mutating during inspection. Existing explicit controls remain
governed by their owning designs.
nuget.org content is immutable; local files may change freely between
operations rather than provide stable snapshots. The primary threat is
untrusted internet data; the tool never executes inspected code. Additional
trust-boundary and containment guidance:
[`docs/design/untrusted-data-threat-model.md`](docs/design/untrusted-data-threat-model.md#trust-boundaries).
For a credible external-input threat, first define its actor, input path,
boundary, containment invariant, and enforcement gate in the owning design.
Prefer typed construction-time containment such as `InertText.InertString`;
when that shape is unavailable, a centralized entry point such as
`HardenedJson` is weaker but still auditable.

### Platform compatibility

- Treat cross-platform operation as the default requirement for product
  libraries and reusable feature paths. Browser/Wasm compatibility is a design target.
- Windows Metadata (`.winmd`, including `MetadataKind.WindowsMetadata` and
  `MetadataKind.ManagedWindowsMetadata`) is not a supported input format.
  Adding WinMD support requires separately approved project scope; do not add
  compatibility paths incidentally while changing ordinary ECMA-335 inspection.
- Before introducing a dependency, API, or design that cannot run on a
  supported platform -- especially single-threaded Browser/Wasm -- stop and
  obtain explicit user approval for that specific exception.
- Document every approved exception in the owning design or architecture
  document and in the PR. Name the supported and unsupported platforms, the
  rationale, the affected surface, the visible failure or degradation mode, and
  the validation used for supported hosts. Do not let a broad catch, silent
  fallback, or generic diagnostic stand in for that documentation.

### Output contract

Commands follow the verbosity and section-selection model owned by
[`docs/design/progressive-disclosure.md`](docs/design/progressive-disclosure.md).
The binding rule for new work: a new section must not enter the default
`-v:m` view unless it is the command's single high-value section.

## Building and testing

Use the SDK selected by repository configuration and CI; inspect the current
selection (`command -v dotnet`, `dotnet --version`) before installing one or
changing `PATH`. If `dotnet` is centrally installed, stop and ask before
replacing or shadowing it. Follow `README.md#repository-development-sdk`.

Build the normal graph with `dotnet build dotnet-inspect.slnx -c Release`.

Tests are xUnit executables. **Use `dotnet run`, not `dotnet test`**;
`dotnet test` silently executes no tests here. Always use Release because
compiler-generated IL shapes differ in Debug.

| Area | Command |
| --- | --- |
| CLI and product output | `dotnet run --project src/dotnet-inspect.Tests -c Release` |
| Artifact contracts | `dotnet run --project src/DotnetInspector.Artifacts.Tests -c Release` |
| Analysis | `dotnet run --project src/ILInspector.Analysis.Tests -c Release` |
| Decompiler | `dotnet run --project src/ILInspector.Decompiler.Tests -c Release` |
| C# text | `dotnet run --project tests/CSharpText.Tests -c Release` |
| Inspection queries | `dotnet run --project src/DotnetInspector.Queries.Tests -c Release` |
| Shared services | `dotnet run --project src/DotnetInspector.Services.Tests -c Release` |
| Metadata and SourceLink | `dotnet run --project tests/ILInspector.Metadata.Tests -c Release` |
| Metadata rendering and `mdi` | `dotnet run --project tests/DotnetInspector.MetadataRendering.Tests -c Release` |

A .NET correctness gate must run in Release. Do not use
`[Conditional("DEBUG")]`; use a runtime opt-in such as `IrInvariants`. The host
contract lives in `docs/decompiler-correctness-pipeline.md`.

Test-tool activation (`ilasm`/`ildasm`/`mdv`), the IL round-trip commands, and
the `IsPackable`/`VersionPrefix` release rules live in
[`docs/dev-environment.md`](docs/dev-environment.md#test-tooling-activation).

## Evidence and validation

Match evidence to the claim and use the smallest existing check that proves it.
Detailed practices — matching evidence to claim types, the style-oracle
consultation procedure, and the harness/product boundary — live in
[`docs/evidence-and-validation.md`](docs/evidence-and-validation.md). Two rules
are load-bearing everywhere:

- **Asserted properties name their gate.** A safety, soundness, or faithfulness
  claim must name its enforcing gate or say `unverified`. A gate counts only
  when it runs in the suite's Release configuration; use runtime opt-ins, not
  `[Conditional("DEBUG")]`.
- **Absence-claim coverage is a user choice.** Before proceeding, propose full,
  partial, or no gate coverage and get the user's selection. An analyzer or
  NativeAOT evidence for NativeAOT-prohibited behavior may be a gate; the
  [evidence guide](docs/evidence-and-validation.md#absence-claims-choose-their-coverage)
  owns the detailed coverage and residual rules.
- **Harnesses don't manufacture the evidence they check.** They own
  orchestration, fixtures, oracles, and reporting, but must exercise
  product-owned artifact construction — never construct, normalize, or repair
  C# that is later compiled as product evidence. If a test needs that
  compensation, stop and fix the product gap instead.

### Markdown

All changed Markdown must pass `markdownlint` before commit (fixer:
`npx markdownlint-cli --fix <file>`; check: `npx markdownlint-cli <file>`).

## Adversarial review

Review is a locked-head feedback loop: freeze and push one exact head, review
that head, reconcile the feedback publicly, make any resulting fixes, and freeze
the replacement head. These are the binding invariants; the rest of this
section and [round orchestration](docs/round-orchestration.md) explain them.

1. **One frozen head per round.** The lock begins at the push and ends only
   when the round closes (reconciled *and* green) or recovery supersedes the
   attempt. Do not edit a locked head; fixes belong to the next cycle.
2. **A candidate includes its effective base.** Integrate twice before pushing
   — once before fixing, once after — because the fix window is long enough for
   `main` to move.
3. **Base movement alone never invalidates a pushed candidate**, and never
   justifies another round.
4. **A round that pushes a fix is not review-clean.** Only the replacement head
   can earn that.
5. **Never claim merge readiness from label state alone.** Confirm current-head
   CI and GitHub's live mergeability immediately before every merge attempt.
6. **A round closes only when reconciled and its applicable gates are green.**
   For a non-Markdown-only PR, known-red `ci-required` blocks; pending status follows
   [Bounded status waiting](docs/round-orchestration.md#bounded-status-waiting).
   At non-boundary rounds, a Markdown-only PR's gate is pre-commit
   `markdownlint`; do not wait for CI before review. A gate failure requiring an
   author change restarts the *same* round.
7. **Six rounds, then stop** and ask for another block.
8. **Never merge without explicit user authorization** for that specific PR.
   A recorded exact-head merge authorization satisfies this rule; see the
   [user-directed workflow adjustments](docs/round-orchestration.md#user-directed-workflow-adjustments).

### Canonical round flow

Full round-cycle steps, the eligibility table, and the `review-clean`
definition live in
[Candidate lifecycle](docs/round-orchestration.md#candidate-lifecycle). The
essentials: integrate the effective base, make the change, run the focused
gate, integrate again, push to lock the head, satisfy the eligibility row,
dispatch reviewers, reconcile publicly, and close only when reconciliation and
the applicable gates are green.

### Recovery transitions

Applied without waiting for CI; full conditions live in
[Candidate lifecycle](docs/round-orchestration.md#candidate-lifecycle).

- **Conflict:** supersede, integrate, resolve, push immediately, and restart
  the same round — or take the exact-head trivial-interaction waiver when
  eligible.
- **Scope violation:** keep the locked head unchanged while the user chooses
  split, abandonment, or an approved broad exception (see
  [Recovering from an over-broad design](docs/design-scope.md#recovering-from-an-over-broad-design)).
- **Failure requiring an author change:** supersede, push the fix, satisfy the
  failed-gate row, and restart the same round.
- **Cancelled or evidenced transient failure:** keep the lock and retry the
  unchanged head with concrete transient evidence; otherwise treat it as an
  author change.

A superseded attempt spends no round and gets no completion report; carry every
returned finding forward.

### Forming a candidate

Spend review only on a pushed, settled head formed by the canonical cycle.
Record the exact head and effective base. If a conflict, author change,
finding, restack, or base-ref retarget changes the candidate, form a replacement
through the cycle unless the user approves the exact-head waiver below. While a
candidate is locked, do not push or integrate other than for recovery; a
non-mutating fetch is allowed for resume and carry-forward analysis. Before
merge, confirm live GitHub readiness — see [Merge preflight](docs/round-orchestration.md#merge-preflight).

### Clean reviews are not spent by main moving

When a `main`-targeting PR (or the bottom open stack slice) has a review-clean
head, or a head with a pending/approved trivial-interaction waiver, and
an agent observes that `origin/main` moved while the PR remains open, assess the
landed range before an agent-driven merge or mutation — do not integrate
blindly and do not start another round by default. An upper stack slice follows
its parent instead: parent movement is a restack requiring review at the new
head.

After a non-mutating fetch, classify the landed range into exactly one
outcome, act on it, and report the classification and action as normal session
output before changing labels or dispatching reviewers; re-classify only when
the landed range itself changes, not on every poll. Merging still needs a live
readiness check and explicit user authorization.
The analysis is a point-in-time decision aid, not an exact-base lock: later base
movement does not trigger branch integration or CI chasing; exact-base
revalidation needs a merge queue, not repeated branch updates.
Full detection, classification, and action procedure:
[Carry-forward after clean reviews](docs/round-orchestration.md#carry-forward-after-clean-reviews).
The four outcomes: **no interaction** (keep the reviewed or waived head
unchanged, preserve its state and merge authorization, and start no new CI run
or other gate — the common case), **trivial interaction** (if still open,
expire authorization, disable any armed auto-merge first, remove
`review-clean`, integrate, run affected gates, and offer the exact-head
re-review waiver), **significant interaction, no conflict** (if still open,
expire authorization, disable any armed auto-merge first, remove
`review-clean`, integrate, re-run validation and CI, and re-dispatch reviewers
as a normal round), and **merge conflict requiring semantic resolution**
(expire authorization, disable any armed auto-merge first, and recover under
[Recovery transitions](#recovery-transitions)).

### How many reviewers, and from which models

| Tier | Requirement |
| --- | --- |
| Trivial | No review. State why the change is trivial. |
| Everything else | **GPT-5.6 Sol**, always, plus one other roster reviewer (Claude Opus or Gemini Pro). |

When uncertain, use the standard round. Second-seat selection by prior clean
count lives in
[Reviewer roster](docs/round-orchestration.md#reviewer-roster). A MAI-Code
quick read on unsettled work is neither tier: it gets no isolated worktree or
fixed head and satisfies no review tier — label its findings as early
feedback, since the settled PR still requires its full round.

### Running the round

Start every reviewer prompt with the complete canonical
[adversarial-review prompt](docs/adversarial-review-prompt.md); do not omit,
paraphrase, reorder, or precede it with domain instructions. Append the same
self-contained candidate instructions for every seat, directly or with the
optional [fill-in template](docs/templates/adversarial-review-prompt.md). Follow
[running a round](docs/round-orchestration.md#running-a-round) for mechanics
and reporting.

### Keep review proportional to the contract

The prompt's finding-admission and trust-boundary rules are binding. A
reviewer concern outside them is a scope proposal, not a landing requirement,
unless the operator explicitly approves it.

### Stop after six rounds

Review blocks hot-start. Rounds 1-6 begin automatically, and every fix-producing
replacement within an authorized block dispatches without asking, setting
`HELP`, or waiting for user input. Approval is required only before rounds 7,
13, 19, and so on; each approval authorizes at most six more rounds.

At a block boundary, conflict recovery may push immediately unless an immutable
split decision hold is active; reviewer dispatch waits for approval. Before
asking, acquire fresh green current-head `ci-required` and definite positive
mergeability under the 60-minute status budget; if it expires, publish its
report and stop without asking.

Round 12 and every later six-round boundary presume splitting into focused
successors unless a strong, user-approved reason keeps the PR intact. Full
checkpoint and split mechanics:
[Block boundaries and splitting](docs/round-orchestration.md#block-boundaries-and-splitting).

## Lead with the demo

Validation proves correctness; a demo shows value. Post the intended demo early
enough to change the implementation.

For a network-accessible inspect-web demo, follow
[`docs/runbooks/inspect-web-demo-hosting.md`](docs/runbooks/inspect-web-demo-hosting.md).
A local HTTP listener or successful `curl` is not a user-visible demo.

A useful demo:

- shows a real canonical invocation and its real output;
- includes before and after for a fix;
- says what to notice; and
- still works for a neighboring scenario, proving the implementation was not
  fitted to the sample.

Put it under `## Demo` above validation in the PR body.

## PR and CI discipline

- Keep concurrent agents modest and avoid unnecessary churn in central files.
  Label a Markdown-only PR (every changed file is `*.md`) `documentation`.
- Use REST endpoints via `gh api`, not `gh pr edit`, for PR/issue metadata
  changes; see [GitHub API operations](docs/github-api-operations.md) for the
  exact commands and the `-F`/`-f` distinction that matters for PR bodies.
- For non-Markdown-only PRs, run the focused gate, push promptly, and start
  eligible local suites and CI concurrently. Reviewer dispatch waits for green
  `ci-required` unless parallel review is approved or conflict recovery applies.
  Query GitHub status only when the round cadence requires it; follow
  [GitHub status queries](docs/github-status-queries.md)'s bounded waiting
  instead of polling. If an hour passes without an authored change while an
  independent gate hasn't started, fix the sequencing or record the blocker.
- `ci-required` is this repository's aggregate merge gate
  (`.github/workflows/ci.yml`): it passes only when the aggregate itself
  concludes `success`, and a missing aggregate is not green. Never require a
  path-gated job directly, and do not broaden CI without measured need.
- Keep PR summaries conclusion-first: claim, evidence, compatibility or
  non-action boundary, and exact validation.
- `review-clean` is advisory, not a merge-eligibility claim (see
  [Keep the review-clean label current](#keep-the-review-clean-label-current)).
  Confirm current-head CI and GitHub's live mergeability for every agent-driven
  merge attempt or readiness statement.
- Never merge without explicit authorization for that PR. A clean review,
  green CI, or readiness comment is not authorization. A recorded merge
  authorization applies only to its exact head/base ref and valid evidence.

### Stacked PRs for multi-slice issues

When an issue is too large for one coherent PR, prefer a **stack** — a sequence
of PRs targeting their predecessors — over one unreviewable PR or parallel PRs
that race in the same files. `docs/stacked-prs.md` owns the mechanics.

- Each slice must land independently with one claim and its own evidence, and
  name its slice position, parent PR, and remaining work. Fold in any slice
  that depends on later work for correctness.
- Give each slice its own branch and worktree, branched from and targeted at
  its parent; only the bottom open slice uses `main`. During a GitHub outage,
  branch from the recorded last-known base or parent, then update and
  validate bottom-up on recovery before pushing.
- Merge bottom-up and confirm each retargeted diff still shows only its slice.
- Restacking your own slices is the exception to the no-force-push rule. Use
  `--force-with-lease` and post a `range-diff` proving only the base changed.
- Apply review depth and the canonical eligibility table per slice and
  stack-wide. Every upper-slice restack and every other moved head needs a
  review-clean round — the sole exception is a bottom open slice with a
  user-approved exact-head trivial-interaction waiver; restacking never
  retires findings.
- Stop when another slice would exist only to continue the stack.
