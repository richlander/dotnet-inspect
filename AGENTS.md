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

### Nightshift is opt-in

`NIGHTSHIFT.md`, the `nightshift` skills, and the
`nightshift`/`turnstile`/`octoshift` tools describe a separate multi-agent
operating model with its own vocabulary and its own stricter gates. **They apply
only when you have been explicitly told that you are working in Nightshift mode
for this session.** Otherwise they are inapplicable: follow this file, and do
not adopt Nightshift roles, orders, gates, or tooling merely because you noticed
those documents exist.

## Before changing files

- `main` is protected. Keep the primary repository checkout attached to
  `main`; never detach its HEAD or develop in it.
- Before starting a change, run `git fetch origin main` from the primary
  checkout, then create a descriptive branch and linked worktree with
  `git worktree add -b <branch> <path> origin/main`. Make all edits, builds,
  tests, and commits in the worktree, not the primary checkout. A slice in a
  stack branches from its parent slice's branch instead — see
  [Stacked PRs for multi-slice issues](#stacked-prs-for-multi-slice-issues).
- Use one development worktree per PR, plus temporary worktrees for independent
  reviews. Do not reuse a worktree across unrelated changes.
- Never amend commits; create follow-up commits.
- Integrate `origin/main` into the feature branch before **every** review round,
  not only the first — see [Adversarial review](#adversarial-review). Rebase
  only before the branch's first push. Once a branch is public or under review,
  merge `origin/main`; never amend, rebase, or force-push reviewed history. A
  slice in a stack is the standing exception: restacking rebases and
  force-pushes a public branch by design — see
  [Stacked PRs for multi-slice issues](#stacked-prs-for-multi-slice-issues) for
  the discipline that replaces this rule there.
- After updating from main or resolving conflicts, re-read `AGENTS.md` and
  task-relevant docs before continuing.
- Do not mix unrelated changes into one commit or sweep another contributor's
  working-tree changes into your work.
- Treat worktrees as temporary. Confirm the exact reviewed head is pushed, then
  `git worktree remove <path>` as soon as every required fixed-head review is
  clean — or after merge, for a change that needs no adversarial review. Do not
  retain inactive worktrees in case more work appears; recreate one for the
  branch if follow-up work is needed.

## Task-specific guidance

| Area | Read first |
| --- | --- |
| User-visible capabilities, commands, or examples | `README.md` |
| A change crossing subsystem ownership boundaries | `docs/overview.md` |
| Implementation structure | the relevant section of `docs/architecture.md` |
| Layering and consumer boundaries | `docs/design/inspection-layers.md` |
| Command defaults and disclosure | `docs/design/progressive-disclosure.md` |
| Output data shapes | `docs/design/output-shapes.md` |
| Output style | `docs/design/style-guide.md` |
| Sections and selection | `docs/design/section-model.md` |
| Metadata and API inspection | `docs/design/assembly-inspection-query.md` |
| Type, member, or API identity | `docs/design/type-member-api-representation.md` |
| PDB and source acquisition | `docs/pdb-acquisition.md` |
| Source Finding producers | `docs/design/source-finding-producers.md` |
| Package resolution and caches | `docs/design/version-resolution.md` |
| Security and untrusted input | `docs/design/untrusted-data-threat-model.md` |
| Analysis, Findings, and Research | `docs/design/finding-adoption.md` |
| Call-graph projection | `docs/design/call-graph-projection.md` |
| Shared IL/control-flow substrate | `docs/design/instruction-substrate.md`, plus the consuming subsystem's docs |
| IL round-trip tests | `tests/DotnetInspector.ILRoundtrip.Tests/README.md` |
| Decompiler behavior or harnesses | `docs/decompiler-correctness-pipeline.md` |
| Skills | `taste/skill-guidance.md` |
| Stacked PRs and restacking | `docs/stacked-prs.md` |
| Release and publishing | `docs/release-workflow.md` |

PR templates:

| Change | Template |
| --- | --- |
| Raising, structuring, validity, fidelity, or corpus behavior | `docs/templates/decompiler-pr.md` |
| Focused invalid-`Full` or burndown row fix | `docs/templates/decompiler-burndown-fix-pr.md` |
| Compile-back harness, fidelity skeleton, or ReturnToSender coverage | `docs/templates/decompiler-compile-back-harness-pr.md` |

Some files under `docs/design/` record proposals or design history. Prefer
current product behavior and tests over design history. When current sources
disagree, stop and resolve which owner is authoritative rather than silently
choosing one.

When adding a focused skill, register it in `SkillCommand.Skills` **and** add an
`EmbeddedResource` line for it in `src/dotnet-inspect/dotnet-inspect.csproj`;
the embeds are enumerated per skill, and no test compares them against the
`skills/` directory, so a skill missing from either list ships as nothing with a
green suite. Its YAML frontmatter `description:` is the single source of truth
for the generated skill listing.

## Repository-wide engineering constraints

- Keep product paths SRM-only, NativeAOT-friendly, Roslyn-free, and free of
  inspected-assembly loading.
- Preserve layer ownership. Metadata owns metadata facts, Analysis owns IL-body
  evidence, CSharp owns C# spelling and type views, Research composes evidence,
  and the CLI owns command and presentation concerns.
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

### Output contract

Commands that render sections follow this verbosity model:

- `-v:q`: compact fields only; include high-value fields only.
- `-v:m`: one section, plus an optional text line. Include all high-value
  fields in that section.
- `-v:n`: multiple sections are allowed; include all sections that are not
  network-bound.
- `-v:d`: all sections.

New sections must not enter the default `-v:m` view unless they are the
command's single high-value section. Focused flags may explicitly select a
section and promote verbosity as needed. Keep alternate lenses, section
selection, row queries, and rendering formats orthogonal; follow the current
progressive-disclosure and output-shape docs for detailed behavior.

### Terminology

Prefer inclusive terminology in code, identifiers, comments, output, and docs.
These substitutions are required, not stylistic:

- Write "allow list" instead of "whitelist".
- Write "deny list" instead of "blacklist".

Match the surrounding casing and word form when substituting (for example
`allowList`/`AllowList` for an identifier, "deny-listed" for an adjective).

## Building and testing

Use the SDK and toolchain selected by current repository configuration and CI.
Version requirements belong with those owners, not in this file. Before
installing an SDK or changing `PATH`, inspect the current selection:

```bash
command -v dotnet
dotnet --version
```

If `dotnet` is centrally installed (for example under `/usr/bin`,
`/usr/local/share/dotnet`, `/snap`, or `C:\Program Files\dotnet`), stop and ask
before installing, replacing, or shadowing it. Follow
`README.md#repository-development-sdk` for the current SDK acquisition
workflow. Do not modify shell startup files unless explicitly requested.

Build the normal product, test, and fixture graph with:

```bash
dotnet build dotnet-inspect.slnx -c Release
```

Tests use xUnit executable projects. **Use `dotnet run`, not `dotnet test`**;
`dotnet test` silently executes no tests here.

| Area | Command |
| --- | --- |
| CLI and product output | `dotnet run --project src/dotnet-inspect.Tests -c Release` |
| Analysis | `dotnet run --project src/ILInspector.Analysis.Tests -c Release` |
| Decompiler | `dotnet run --project src/ILInspector.Decompiler.Tests -c Release` |
| Shared services | `dotnet run --project src/DotnetInspector.Services.Tests -c Release` |
| Metadata | `dotnet run --project tests/ILInspector.Metadata.Tests -c Release` |
| Metadata rendering and `mdi` | `dotnet run --project tests/DotnetInspector.MetadataRendering.Tests -c Release` |

Run the suite in **Release** for input fidelity, not speed: the optimized IL a
Release build of the compilers emits is what ships and what the decompiler
corpus consumes, so a Debug run would validate the decompiler against IL shapes
users never see. A correctness check therefore must not hide behind
`[Conditional("DEBUG")]` — such a call is stripped from the Release test
assembly and asserts nothing. Make it a runtime opt-in that the test host arms
instead; the IR invariant check (`IrInvariants`, on by default in every host but
the shipped CLI) is the worked example, and
`docs/decompiler-correctness-pipeline.md` owns its host contract, its structural
and semantic levels, and what to do when a fixture trips one.

Some tests use external tools as independent oracles and **skip** when those
tools are absent: `ilasm`/`ildasm` (CLI and decompiler suites) and `mdv`
(the metadata projection oracle). A machine without them reports a green run
that proved less than it appears to, so restore them before trusting a clean
result:

```bash
source eng/activate-iltools.sh --mdv
```

`eng/restore-iltools.sh` does the acquisition and prints the directories;
`eng/activate-iltools.sh` is the sourceable wrapper that puts them on PATH.
Source the wrapper rather than assembling PATH by hand. A child process cannot
change its parent's PATH, so the assembly has to happen in your shell, and
every way of getting it wrong is silent -- a masked exit status, a lost
trailing newline, or empty output prepending an empty PATH entry, which means
the current directory. Each leaves a plausible PATH with no oracles on it. The
wrapper is the one tested copy of that logic; `IlToolsActivationTests` in
`src/dotnet-inspect.Tests` is its gate, and also fails if this documentation
goes back to hand-rolling the assembly.

The script pins the `ilasm`/`ildasm` version for CI and local runs alike;
`ci.yml`, `deep-inspect.yml`, and `release.yml` invoke `eng/restore-iltools.sh`
directly, appending its output to `$GITHUB_PATH` so the runner does the joining.
Only `ci.yml` passes `--mdv`, because it is the only workflow that runs the
metadata oracle suite. Those workflow steps are `continue-on-error`, so an
acquisition failure in CI degrades to skips rather than a red run -- check the
step's log before reading a green decompiler, IL-diff, or metadata leg as proof.

The IL round-trip project has separate dependency restore and fast/full test
commands; follow `tests/DotnetInspector.ILRoundtrip.Tests/README.md`.
`ILInspector.Decompiler.Tests` composes `Speed` and `Area` traits and offers a
`--gate <preset>` flag (`--gate list` prints the table); the taxonomy and the
per-change targeting advice live in `docs/decompiler-correctness-pipeline.md`.

Only `src/dotnet-inspect` and `src/runfaster` are packable, and internal
libraries carry no versioning story or API-stability commitment: treat their
public surface as an internal design constraint, not an external compatibility
surface. `docs/release-workflow.md` owns the packaging mechanics.

Changing `VersionPrefix` in `src/dotnet-inspect/dotnet-inspect.csproj` is a
release, and `README.md` (packed as the package readme) and the shipped
`SKILL.md` files (embedded in the binary) ship with it. Consult both before the
version moves and update whatever the release changed; the checklist is in
`docs/release-workflow.md`.

### File-based apps

Do not use `dotnet-script`, `dotnet script`, `dotnet-fsi`, or `.csx` files.
Prefer .NET file-based apps for throwaway probes unless a specific Python
library is needed. Write probes under `/tmp/` and run them with:

```bash
dotnet run /tmp/check.cs
```

## Evidence and validation

Match evidence to the claim and use the smallest existing check that proves it:

- Start with focused tests for the changed subsystem; expand only when the
  change crosses boundaries or focused results expose broader risk.
- For compiler-, metadata-, or IL-shape claims, include a compiled fixture or
  real artifact canary when practical. Synthetic fixtures are appropriate for
  unreachable states and seam isolation, but not as the only proof of a
  compiler-produced shape.
- Pair every new discriminator or heuristic with close negative cases. Preserve
  candidate identity, provenance, local semantics, and default output unless
  the change explicitly intends otherwise.
- For output changes, exercise the affected Markdown and structured modes,
  schema/query fields, ordering, and verbosity behavior.
- For any taste- or style-oriented raise or rendering change, consult **both**
  facets of the dotnet/runtime style oracle before landing it and record what
  each says — the **declared** facet (`dotnet/runtime`'s `.editorconfig` and
  enabled analyzers; quote the `dotnet_style_*`/`csharp_style_*` key or state it
  is silent) and the **revealed** facet (the dominant form in `dotnet/runtime`
  source, with `path/file.cs:line` witnesses). Cite the facet a claim rests on,
  never infer one facet from the other, and never assert "oracle approved"
  uncited; a knowing divergence is legitimate only when the consultation
  happened and is recorded. See
  [`docs/decompiler-taste.md`](docs/decompiler-taste.md#consulting-both-facets-is-required).
- For corpus or performance claims, record the pinned input, command, baseline,
  and result. Static analysis proves structural evidence, not runtime heat,
  frequency, bytes, or impact; use a benchmark or profiler for runtime claims.
- Documentation-only changes that make no measured behavior claim require
  Markdown validation, not product builds or tests.
- A doc comment or README that asserts a safety, soundness, or faithfulness
  property must name the gate that enforces it, or explicitly mark the
  property as unverified.

### Asserted properties name their gate

"Unverified" is an acceptable answer; an unmarked, ungated claim is not. A
green suite plus a confident comment reads exactly like a verified property,
and a reviewer can only tell them apart by tampering with the code to see
whether anything notices. Naming the gate moves that cost to the author, where
it is a one-line answer.

Prefer making the declaration *drive* the enforcement set over restating it, so
that stale and missing entries both fail — `ByteNeutralityGateTests` derives its
coverage set from the style catalog
(`StyleOptionCatalog.Options.Where(o => !o.ByteDivergent)`) and asserts set
equality against the specimens; `SpanAttributionTests` asserts set equality
between the body-intrinsic error allowlist and the pin for the current
`MethodologyVersion`. When the property depends on wiring rather than on a set,
write one named non-vacuity test that fails if the wiring dies, and say in its
doc comment that it is that test —
`IrInvariantCheckTests.PipelineRunner_UnderTestHost_ThrowsWhenAPassCorruptsTheTree`
is the example.

A gate only counts if it runs in the configuration the suite uses. The suite
runs Release for fixture fidelity (see [Building and
testing](#building-and-testing)), so a `[Conditional("DEBUG")]` check asserts
nothing. Make such a check a runtime opt-in that the test host arms; do not
switch the suite to Debug.

### Harness boundary

Test harnesses own orchestration, fixtures, independent oracles, comparison,
and reporting. When behavior belongs to the product, a harness must exercise
the product-owned capability rather than reconstructing or replacing it.

Do not add harness-side adaptive mechanisms, fallback resolvers, special-case
shape recognition, or normalization that compensates for missing, incomplete,
or incorrect product behavior. Such compensation hides the product gap and
makes the harness a second implementation.

If a test cannot express its claim without covering for the product, stop and
ask for guidance. File an issue against the missing product capability and
either fix that capability first or record the harness work as blocked; do not
make the harness substitute for the product.

Decompiler raising, typing, structuring, fidelity, or printer changes have
additional evidence requirements. Follow the decompiler docs and PR templates
rather than duplicating their evolving commands and gates here.

### Markdown

All changed Markdown must pass `markdownlint`. Run the fixer first when needed:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

## Adversarial review

### Do not start a round until the branch is settled

**A review round does not begin until the PR is stable, free of merge conflicts,
and green on every check that runs for it — and for a stacked PR, until every
layer is.** This is a gate, not a preference: hold the round until that state
clears.

Adversarial review is the scarcest resource in this workflow — several models, a
self-contained prompt, isolated worktrees, real runs. A branch whose head is
unpushed or still moving, whose base is stale, whose CI is red, or whose PR
reports a conflict has no single answer to "what am I reviewing?", so every
finding it produces is provisional and every clean result is worthless. Reach
this state before the first round, and reach it again before every subsequent
round:

- **The head is pushed, named, and settled.** Reviewers get an exact base and
  head, not a branch that moves under them. Finish your own edits first.
- **`origin/main` is integrated.** Fetch and merge it, resolve any conflicts,
  and re-run the validation the change claims; the resulting head is what you
  hand out. Reviewing a stale head spends the review on code that is not what
  will merge, and defers conflict resolution to *after* the reviews are clean —
  where the resolution is itself unreviewed.
- **The PR is mergeable and green.** Use `gh pr view <n> --json
  mergeable,mergeStateStatus` for conflicts and `gh pr checks <n> --required`
  for the gating runs; when the repository marks no check required, `--required`
  reports none and exits non-zero, so fall back to plain `gh pr checks <n>`.
  Exit `0` means nothing failed and nothing is outstanding; exit `8` means
  checks are still running, which is not green — wait with `--watch`. A
  `skipping` result is terminal and does not block: a path-filtered job that
  skipped will never become a pass, so waiting on it waits forever. It is also
  not evidence of anything. Never cite a skipped job as proof your change was
  validated, and if a change should have triggered a job that skipped, treat the
  path filter as the bug.
- **Every PR in a stack meets all of the above**, not only the slice under
  review — a red or conflicted parent is a red or conflicted base for everything
  above it. A slice rebases onto its parent, never onto `main`: only the stack's
  bottom open slice takes `origin/main` as its base, and rebasing an upper slice
  onto `main` pulls in work its parent has not landed and makes the slice's diff
  report its parent's changes as its own.
- **Every slice in a stack must report CI.** Stack branches use the `feature/`
  prefix, so a child PR targeting its parent branch schedules the same CI as a
  bottom slice targeting `main`. If `gh pr checks` reports no checks for a
  stack slice, the slice is not green: verify the branch naming and workflow
  scheduling before review.

Do not integrate main under a reviewer mid-read. When integration is what moved
the head, say so on the PR and name the merge commit, so the re-review reads as
a confirmation rather than a second full pass.

### A quick read is not a round

The gate above forbids spending a *round* on an unsettled branch. It does not
forbid getting early signal. When you want a fast read on a design or an
in-progress implementation ahead of a later adversarial review, **use
MAI-Code** — that is what it is for here: cheap enough to run on a branch that
is still moving, and useful well before there is anything to gate.

Keep the two distinct. A quick read gets no isolated worktree, no fixed head,
and **satisfies no tier** — a PR that had one still owes its full review once
the branch settles. When you cite its findings, say which it was.

### How many reviewers, and from which models

**How much review a PR needs is a function of its triviality and risk alone —
never the kind of change it makes.** If you are unsure which tier applies,
escalate: default to more review, not less.

| Tier | Requirement |
| --- | --- |
| Trivial | No review. State why the change is trivial. |
| Medium risk | One reviewer, always **GPT** at the highest available version and quality level. |
| Higher risk — typical for a substantial feature | Two reviewers from two different model families. |

Higher risk means subtle correctness, security, or compatibility risk, or a
large or uncertain blast radius.

Adversarial-review roster — this list is the single source of truth, and
scenario docs should reference it rather than restating it:

- Claude Opus
- Gemini Pro
- GPT

**Always use the highest version a model offers** — if both Opus 4.8 and Opus 5
are available, use Opus 5. For GPT, also select the highest available quality
level. In the two-reviewer tier, do not review with your own model when another
listed family is available.

These tiers assume a harness — such as the GitHub Copilot CLI — that can
delegate to any family in the roster. A harness exposing only its own vendor's
models changes how the reviewers are obtained, never the bar. For the
medium-risk tier, use GPT when the harness offers it; otherwise request a GPT
review from the user. For the two-reviewer tier, use an available roster family
and **request the other, different-family reviewer from the user**, not marking
the PR ready until both reviews arrive.

A **round** evaluates one settled head with every reviewer required by its tier.
Two reviewers in the same round count as one round, not two.

### Running the round

Give each reviewer the same self-contained prompt: exact base and head, design
intent, relevant diff, concrete attack points, and required real-run evidence.
Isolate every reviewer in a separate linked review worktree; never detach the
primary checkout for review. Require scratch work under `/tmp/` and prohibit
`git reset`, `git add`, and commits in review trees. Before acting on a blocking
finding, reproduce it on a clean exact-head review worktree.

After addressing findings, re-review the fixed exact head. Reconcile the reviews
publicly on the PR: attribute findings, state what was verified or dismissed, and
link resolution commits or explain explicit non-actions. Do not mark the PR ready
until every required fixed-head review is clean.

### Keep review proportional to the contract

Review the invariant the design actually promises. Unless the threat model
explicitly includes hostile in-process callers, require the invariant for
well-behaved code that follows the design — not for arbitrary code that bypasses
or misuses its abstractions.

Prefer simple, auditable enforcement over making every abstraction a fortress
against rogue callers. `InertString` is the model: code that uses the type
properly gets its invariant, while bypasses and misuse are deliberately easy to
find with a targeted search. A reviewer should report such a caller so it can be
fixed, but should not demand bend-over-backwards features in the type merely to
make misuse impossible. Escalate to stronger enforcement only when the stated
contract or threat model requires it.

### Stop after six rounds

Do not begin a seventh review round without explicit user approval, and get
fresh approval for every additional round. Before requesting approval, present
an analysis of why six rounds did not converge. In particular, determine
whether the repeated findings expose an architectural problem, missing test
coverage, or reviewers expanding the contract beyond the intended threat
model. State the proposed architectural or test remedy, or explain why the
remaining concern should be dismissed, before spending another round.

## PR and CI discipline

- Prefer fewer coherent PRs over many small PRs that each pay fixed CI cost and
  increase merge contention. That is an argument against splitting one coherent
  change, not against sequencing a genuinely multi-slice one — see
  [Stacked PRs for multi-slice issues](#stacked-prs-for-multi-slice-issues).
- Keep concurrent agents modest and avoid unnecessary churn in central files.
- Treat CI as confirmation, not discovery: run relevant local checks first.
- `ci-required` is the only check that may gate merges, and the one the `main`
  ruleset is meant to require: an aggregate that fails if any job in `ci.yml`
  failed or was cancelled. It passes `skipped`, because most jobs are
  path-gated, so a green `ci-required` means "nothing that ran went wrong", not
  "everything ran". Never require a path-gated job directly — a required check
  that does not run blocks the merge forever.
- Do not broaden CI without a measured need. The PR `test` job validates the
  merge path; `pack` is path-gated; release artifacts are built by
  `release.yml`.
- Keep PR summaries conclusion-first. Include the behavioral claim, evidence,
  compatibility or non-action boundary, and exact validation appropriate to
  the change.
- When all merge-blocking validation, CI, and required review are complete, post
  a PR comment that says `Ready to merge`. Label later work as non-blocking
  follow-up so readiness remains unambiguous.

### Stacked PRs for multi-slice issues

When an issue is too large for one coherent PR, prefer a **stack** — a sequence
of PRs, each targeting its predecessor's branch — over a single PR that grows
until it is unreviewable, and over parallel PRs that race in the same files.
`docs/stacked-prs.md` owns the mechanics; the rules that bind are:

- **Every slice lands on its own**, carrying one behavioral claim and its own
  evidence. If a slice is only defensible once the next one lands, fold it in.
- **Name the stack in every PR**: the slice's position, its parent PR, and the
  enumerated residual, which is the non-action boundary the PR-summary rule
  already requires.
- **Name every slice branch `feature/<name>`** so child PRs targeting it run CI.
- **One branch and one worktree per slice**, branched from the parent slice, and
  targeted at the parent branch (`gh pr create --base <parent-branch>`).
- **Merge bottom-up, one at a time**, then confirm the next PR retargeted and
  still shows only its own slice.
- **Restacking rebases and force-pushes a public branch by design** — the
  standing exception to the never-force-push rule, and it cascades to every
  slice above. Use `--force-with-lease`, restack only your own slices, and post
  a `range-diff` proving the restack changed the base and nothing else.
- **Review depth is per-slice, by that slice's own risk**, not the stack's size.
- **Green and mergeable is checked stack-wide, before any slice's round** — see
  [Adversarial review](#adversarial-review).
- **A moved head — including one moved by a restack — needs a clean round at
  the new head**, and a restack never retires an open finding.
- **Stop stacking when a slice would exist only to continue the stack.** CI cost
  is per PR; three coherent slices beat ten mechanical ones.
