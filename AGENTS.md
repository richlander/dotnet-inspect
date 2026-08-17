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

### Markout changes use the co-development loop

When a change needs new or altered Markout behavior, read
[`docs/markout-co-development.md`](docs/markout-co-development.md) before
changing either repository. Point dotnet-inspect at the exact Markout source
branch and validate it as a real consumer before the Markout PR merges; that
consumer proof is part of getting Markout to quality, not a post-release check.
Keep the peer-checkout `ProjectReference` edits local and unpushed. After
Markout lands and releases, restore `PackageReference` and only then raise the
dotnet-inspect PR.

## User-directed workflow adjustments

The workflow gates in this file establish the default safe sequencing. A user
may explicitly adjust a process gate for a specific task or PR in the interests
of speed, including directing work that normally waits on another step to run
in parallel. Follow that direction rather than refusing solely because the
default is described as a gate, and preserve every requirement the user did not
adjust.

An adjustment changes sequencing, not evidence. Record the scope of the
adjustment and any consequence for what the result proves. Work tied to an
exact head remains valid only for that head; if parallel validation or a later
change moves it, apply the fixed-head rules to the new head. A user-directed
adjustment does not turn failed validation into success or make an unmergeable
change ready to merge.

## Before changing files

- `main` is protected. Keep the primary repository checkout attached to
  `main`; never detach its HEAD or develop in it.
- Before starting a change, run `git fetch origin main` from the primary
  checkout, then create a descriptive branch and linked worktree with
  `git worktree add -b <branch> <repo>/.worktrees/<slug> origin/main`. Make all
  edits, builds, tests, and commits in the worktree, not the primary checkout.
  Do not create worktrees as direct children of the user's home directory. A
  slice in a stack branches from its parent slice's branch instead. If a GitHub
  outage prevents the fetch, the outage exception under [Stacked PRs for
  multi-slice issues](#stacked-prs-for-multi-slice-issues) permits a new slice
  to use its recorded last-known local base or parent until service recovers.
- Use one development worktree per PR, plus temporary worktrees for independent
  reviews. Development worktrees belong under the primary checkout's
  `.worktrees/` directory. Reviewer worktrees belong there or under an
  operating-system temporary directory; they are also prohibited at the root
  of the user's home directory. Do not reuse a worktree across unrelated
  changes.
- Never amend commits; create follow-up commits.
- For an open PR, the candidate, lock, CI, conflict, failed-gate, base-movement,
  and round-restart rules have one source of truth: [Canonical round
  flow](#canonical-round-flow). **Conflict recovery remains the first
  priority**; apply that flow before tests, reviews, restacks, or unrelated
  follow-up work.
- Rebase only before the branch's first push. Once a branch is public or under
  review, merge `origin/main`; never amend, rebase, or force-push reviewed
  history. A slice in a stack is the standing exception: restacking rebases and
  force-pushes a public branch by design — see
  [Stacked PRs for multi-slice issues](#stacked-prs-for-multi-slice-issues) for
  the discipline that replaces this rule there.
- After updating from main or resolving conflicts, re-read `AGENTS.md` and
  task-relevant docs before continuing.
- Do not mix unrelated changes into one commit or sweep another contributor's
  working-tree changes into your work.
- Treat worktrees as temporary. Remove a reviewer worktree after that reviewer
  has returned or its cancellation is acknowledged and any required
  reproduction there is complete. Remove the development worktree only after
  the exact reviewed head is pushed, its head lock has ended with all required
  concurrent gates successful, and every required fixed-head review at that
  head is review-clean — or after merge, for a change that needs no adversarial
  review. Do not retain inactive worktrees in case more work appears; recreate
  one for the branch if follow-up work is needed.

## Task-specific guidance

| Area | Read first |
| --- | --- |
| User-visible capabilities, commands, or examples | `README.md` |
| Core workspace, query, cache, or safety architecture | `docs/inspection-space.md` |
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
| Inspection graphs and characteristics | `docs/design/inspection-graph-document.md`, plus the contributing relationship producer's docs |
| Inspection-graph modes | `docs/design/inspection-graph-modes.md` |
| Call-graph projection | `docs/design/call-graph-projection.md` |
| Shared IL/control-flow substrate | `docs/design/instruction-substrate.md`, plus the consuming subsystem's docs |
| IL round-trip tests | `tests/DotnetInspector.ILRoundtrip.Tests/README.md` |
| Decompiler raising, structuring, typing, or printer behavior | `docs/decompiler-correctness-pipeline.md`, then `docs/decompiler-raise-discipline.md` |
| Decompiler harness-only behavior | `docs/decompiler-correctness-pipeline.md`, then the owning harness README |
| Skills | `taste/skill-guidance.md` |
| Stacked PRs and restacking | `docs/stacked-prs.md` |
| Release and publishing | `docs/release-workflow.md` |
| Changes spanning Markout and this repo | `docs/markout-co-development.md` |

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
the embeds are enumerated per skill.
`FocusedSkillFilesRegistryAndEmbeddedResourcesAgree` keeps the skill
directories, runtime registry, and embedded resources equal. Its YAML
frontmatter `description:` is the single source of truth for the generated
skill listing.

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

### Platform compatibility

- Treat cross-platform operation as the default requirement for product
  libraries and reusable feature paths. Browser/Wasm compatibility is a design target.
- Before introducing a dependency, API, or design that cannot run on a
  supported platform -- especially single-threaded Browser/Wasm -- stop and
  obtain explicit user approval for that specific exception.
- Document every approved exception in the owning design or architecture
  document and in the PR. Name the supported and unsupported platforms, the
  rationale, the affected surface, the visible failure or degradation mode, and
  the validation used for supported hosts. Do not let a broad catch, silent
  fallback, or generic diagnostic stand in for that documentation.

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
| CLI, sections, and product output | `dotnet run --project src/dotnet-inspect.Tests -c Release` |
| Analysis | `dotnet run --project src/ILInspector.Analysis.Tests -c Release` |
| Decompiler | `dotnet run --project src/ILInspector.Decompiler.Tests -c Release` |
| C# text | `dotnet run --project tests/CSharpText.Tests -c Release` |
| Inspection queries | `dotnet run --project src/DotnetInspector.Queries.Tests -c Release` |
| Shared services | `dotnet run --project src/DotnetInspector.Services.Tests -c Release` |
| Metadata and SourceLink | `dotnet run --project tests/ILInspector.Metadata.Tests -c Release` |
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
`ci.yml` and `deep-inspect.yml` invoke `eng/restore-iltools.sh` directly,
appending its output to `$GITHUB_PATH` so the runner does the joining. Only
`ci.yml` passes `--mdv`, because it is the only workflow that runs the metadata
oracle suite. Each install step is `continue-on-error` so that a feed outage
does not cost every other result in the lane, but a terminal
`Check ilasm/ildasm[/mdv] result` step fails the lane if acquisition failed:
losing oracle coverage is red, not a quietly shorter skip list. Deep Inspect
cannot certify that commit, so `release.yml` rejects the run before building
packages. `IlToolsActivationTests.SlowWorkflows_FailAfterOracleRestoreFailure`
gates the Deep Inspect wiring.

The IL round-trip project has separate dependency restore and fast/full test
commands; follow `tests/DotnetInspector.ILRoundtrip.Tests/README.md`.
`ILInspector.Decompiler.Tests` composes `Speed` and `Area` traits and offers a
`--gate <preset>` flag (`--gate list` prints the table); the taxonomy and the
per-change targeting advice live in `docs/decompiler-correctness-pipeline.md`.

Only tool projects explicitly set `IsPackable=true`, and `IsTool` makes those
same projects available to solution-level `dotnet publish`. Internal libraries
carry no versioning story or API-stability commitment: treat their public
surface as an internal design constraint, not an external compatibility
surface. Packability and publishability control SDK commands; release workflow
membership remains owned by `docs/release-workflow.md`.

Changing `VersionPrefix` in `src/dotnet-inspect/dotnet-inspect.csproj` is a
release, and `README.md` (packed as the package readme) and the shipped
`SKILL.md` files (embedded in the binary) ship with it. Consult both before the
version moves and update whatever the release changed; the checklist is in
`docs/release-workflow.md`.

### Package acquisition when nuget.org is disabled

Some machine-level NuGet configurations disable nuget.org in favor of a
company-imposed proxy feed. When that proxy does not mirror a pinned package
version -- the co-developed `Markout` pins in `Directory.Packages.props` are
the common case -- restore fails with `NU1603` ("was not found ... resolved
instead") even though nuget.org serves the exact pin.

Do not edit the machine-level NuGet config, and do not commit a repository
`nuget.config` that starts with `<clear/>`: clearing the inherited sources on
such a machine has previously left it with no usable feed at all. Instead,
override the source list for a single restore. `--source`/`-s` replaces the
configured feeds for that one invocation only and downloads the pinned
versions into the global package cache (`~/.nuget/packages`):

```bash
dotnet restore dotnet-inspect.slnx -s https://api.nuget.org/v3/index.json
```

Subsequent restores resolve exact centrally-pinned versions from that cache
without consulting any feed, so plain `dotnet build` works afterward. The fix
is per-machine and must be repeated after `dotnet nuget locals all --clear` or
when a pin moves to a version the proxy still lacks.

Prefer `--source` over `--add-source` for this recovery: a restore given both
nuget.org and the proxy has been observed to still fail with `NU1603` when the
proxy answers with a different version of the same package.

Acquiring the shipped tool accepts the same override, verified for both forms:

```bash
dotnet tool install -g dotnet-inspect --source https://api.nuget.org/v3/index.json
dnx dotnet-inspect --source https://api.nuget.org/v3/index.json
```

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
- Do not serialize independent evidence. After the focused pre-push gate is
  green, start broader local suites, current-head CI, and eligible fixed-head
  review concurrently. Eligibility includes the per-round CI and conflict
  rules under [Adversarial review](#adversarial-review). A long suite is not a
  reason to delay an independent gate.
- Run broad local suites once per authored head, not once per elapsed base
  update. After a conflict-free base-only merge, inspect the integrated range
  and rerun the focused gates for files, contracts, and behavior that can
  interact with the branch. Let current-head CI provide the broad merge-path
  confirmation. Rerun an otherwise non-interacting broad suite only when its
  result is itself a claimed artifact, the integrated base changed its
  prerequisites, or prior evidence exposed a reason.
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

Harnesses may parse source or diagnostics to observe and measure independent
evidence. They must not use that parsed representation to construct, normalize,
repair, or rewrite C# that the harness later compiles as product evidence. The
product must own that artifact construction and expose typed identities, ranges,
or replacement operations so the harness never becomes a second C# producer.

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

### Canonical round flow

This section is the sole source of truth for candidate formation, round
eligibility, head locking, supersession, and recovery. Other sections add
reviewer, stack, or readiness detail without redefining these transitions.

| Attempt | Required before reviewer dispatch | May remain pending |
| --- | --- | --- |
| First attempt at round 1 | Pushed settled head, recorded effective base, focused gate | CI and mergeability |
| Ordinary subsequent round | First-attempt requirements, zero conflicts, green current-head `ci-required` | Nothing required |
| Conflict-recovery attempt | Resolution head pushed, round number authorized | Post-push local gates, CI, mergeability |
| Failed-gate restart | Required fix pushed, zero conflicts, green current-head `ci-required` | Nothing required |

Documentation-only ordinary candidates use Markdown linting as their focused
gate. A documentation conflict-recovery attempt instead lints after the
resolution push; it may start review immediately, but cannot reconcile or
complete until lint succeeds.

Review is a locked-head feedback loop: freeze and push one exact head, review
that head, reconcile the feedback, make any resulting fixes, and freeze the
replacement head. Do not edit a head while its head lock is held. The lock ends
when both reviewers have returned, their feedback has been reconciled for
action, current-head zero-conflict evidence is confirmed, and every required
current-head check and local gate allowed to run concurrently has succeeded.
The fixed replacement is a new candidate and its review is a new round.

A fixed-head review is **review-clean** when its public reconciliation leaves no
finding unresolved **and the reviewed head did not move in response to that
round**. A justified dismissal counts as a resolution only when the reason is
recorded publicly. A round that pushes a fix is complete but is not
review-clean; only the replacement head can earn that status. The report
classification `clean` is narrower: use it only when the reviewers returned no
findings.

Apply one transition when the locked head becomes invalid:

- **Conflict:** supersede the attempt, release the lock, integrate and resolve
  the effective base, push immediately, and restart the same numbered round
  without waiting for CI. The six-round approval boundary still applies.
- **Failure requiring an author change:** supersede the attempt, release the
  lock, push the fix, satisfy the failed-gate restart row, and restart the same
  numbered round.
- **Cancelled or evidenced transient failure:** keep the unchanged head and its
  lock, re-run the failed check, and continue if it passes. After another
  failure, repeat only with concrete transient evidence or classify it as
  requiring an author change. Never continue or complete while a required check
  remains red.

A superseded attempt consumes no round number and receives no completion
report. Let its reviewers finish or cancel them explicitly. Before completing
the restarted round, wait for every superseded reviewer to finish or have its
cancellation acknowledged, carry forward every returned finding, and
disposition each one publicly.

Absent an actual conflict, a round that produces no review-driven fix and then
integrates newer `main` to create another round on the agent's own initiative
is a failed round: the review produced no change and the integration discarded
the value of the locked-head result. A review-clean round ends adversarial
review; do not move the head merely to buy another pass. The sole default
base-only integration after clean reviews is the explicitly approved
carry-forward path in
[Clean reviews are not spent by main
moving](#clean-reviews-are-not-spent-by-main-moving), which preserves the clean
reviews and does not open another round. An explicit user workflow adjustment
may instead authorize interacting-base integration followed by validation and
a new review round.

Adversarial review is scarce, but serial wall-clock time is also a cost. Spend
review only on a named frozen head with focused local evidence, then accept the
bounded risk that later CI may supersede it. A branch whose head is unpushed or
still moving, whose candidate was formed without integrating its effective
base, or whose PR has a known failure or conflict has no single answer to "what
am I reviewing?" Form and freeze one candidate before the first round, and do
so again before every subsequent round:

- **The head is pushed, named, and settled.** Reviewers get an exact base and
  head, not a branch that moves under them. Finish your own edits first, and do
  not push again until both reviewers have returned and their feedback has been
  reconciled. A confirmed merge conflict or failing required gate that requires
  an author change is the exception: it supersedes the incomplete attempt and
  releases the lock. Conflict recovery pushes immediately; failed-gate recovery
  pushes the fix and waits for the ordinary subsequent-round status gate.
- **The candidate includes its effective base.** Immediately before focused
  validation, fetch and integrate the effective base and record the integrated
  tip. The resulting head is the candidate: keep it fixed through broader
  validation, push, CI, and review. Do **not** refetch or integrate the base
  merely because it advances during those steps; doing so creates an
  integrate-validate-integrate loop without making the review more useful.
  Base movement alone does not invalidate a candidate. It remains eligible
  while its exact pushed head has no known failure or conflict. If it becomes
  conflicting, or an author change or review finding moves the head, end that
  candidate and integrate the then-current effective base while forming the
  replacement. Once the reviews are clean, see
  [Clean reviews are not spent by main
  moving](#clean-reviews-are-not-spent-by-main-moving).
- **Before merge, the PR is mergeable and green** — two questions, one
  consolidated status query. The first attempt at round 1 and conflict-recovery
  rounds do not wait for this result, but a failed-gate restart, an ordinary
  subsequent round, and merge readiness do. Use a single
  `gh api graphql` request that returns the PR's
  `headRefOid`, `baseRefOid`, `baseRef { target { oid } }`, `isDraft`,
  `mergeable`, `mergeStateStatus`, `statusCheckRollup` state and contexts with
  `pageInfo`, and the query's `rateLimit` cost, remaining quota, and reset time.
  Request enough contexts for the normal check matrix; if
  `pageInfo.hasNextPage` is true and `ci-required` is absent, fetch the
  remaining context pages before concluding that it is missing. Confirm that
  `headRefOid` is the pushed head, `isDraft` is false, `mergeable` is
  `MERGEABLE`, and the current head's `ci-required` check run completed
  successfully. Treat `mergeStateStatus` values `BLOCKED` and `DRAFT` as
  independent readiness blockers: identify and clear the blocker before
  posting `Ready to merge`. A `CONFLICTING` mergeability result blocks, and
  `UNKNOWN` means GitHub has not finished computing the merge. When the exact
  head's `ci-required` is already
  `SUCCESS` and mergeability is the only unknown, immediately make one REST
  `GET /repos/{owner}/{repo}/pulls/{number}` request for `head.sha`,
  `mergeable`, and `mergeable_state`. That endpoint triggers GitHub's
  mergeability computation and often returns the definite answer while GraphQL
  still says `UNKNOWN`. Accept it only when `head.sha` is the expected head:
  `mergeable: true` satisfies the mergeability half of the gate, while
  `mergeable: false` blocks. A null REST result is still computing; yield for
  five minutes with small random jitter, then re-run the consolidated GraphQL
  query and the REST fallback if it remains `UNKNOWN`. Continue that
  five-minute self-recovery until GitHub returns a definite result; do not ask
  the user to report CI or mergeability.

  Do not infer mergeability from green CI: #4032 reported successful checks
  while GitHub reported `CONFLICTING`/`DIRTY`. Do not read `mergeStateStatus` as
  check state either: it is a composite, and it reports `CLEAN` for a PR with
  no checks at all (#3706). A missing `ci-required` is likewise inconclusive:
  the aggregate may not have registered yet. Inspect all returned contexts; no
  PR is green until its current-head `ci-required` has completed with a
  `SUCCESS` conclusion. A subordinate check run with status `COMPLETED` and
  conclusion `SKIPPED` does not block, but it is also not evidence: never cite
  a skipped job as validation, and if a change should have triggered a job
  that skipped, the path filter is the bug.

  Status discovery must conserve the shared GitHub API budget. After every
  push, schedule one status check for five minutes later; do not hold a
  synchronous shell or agent turn open with `sleep`. The first check verifies
  the expected head and detects merge conflicts early:

  - If `mergeable` is `CONFLICTING`, integrate the effective base, resolve the
    conflict, push the replacement head, start its conflict-recovery review
    round immediately if its round number is authorized, and schedule a new
    five-minute check. Do not wait for CI before starting an authorized
    conflict-recovery round.
  - If `mergeable` is `UNKNOWN`, it does not satisfy the zero-conflict gate. If
    current-head `ci-required` is already green, use the REST fallback above;
    a null result follows its five-minute recovery cadence. If `ci-required` is
    pending or missing, schedule the documentation-only follow-up for at least
    10 minutes plus small random jitter after the five-minute check, or the
    non-documentation follow-up for the expected 35-minute completion point. If
    both mergeability and CI remain unresolved at that follow-up, continue with
    at least 10 minutes plus small random jitter between aggregate queries.
    Switch to the five-minute REST-backed recovery cadence once `ci-required`
    is green and mergeability is the only unknown.
  - If `mergeable` is `MERGEABLE` and the PR is documentation-only, use that
    five-minute result as the expected CI completion check. Do not schedule a
    longer planned wait; documentation CI should be complete by then. If it is
    unexpectedly pending or `ci-required` is missing, wait at least 10 minutes
    plus small random jitter before querying again.
  - If `mergeable` is `MERGEABLE` and the PR is not documentation-only, expect
    CI to take about 35 minutes from the push. Schedule the next status check
    for about 30 minutes after the five-minute conflict check. If CI is still
    pending or `ci-required` is missing, wait at least 10 minutes plus small
    random jitter before querying again.

  An ordinary subsequent round cannot start until one of these checks confirms
  both zero merge conflicts and green current-head `ci-required`. Apply the
  short REST-backed recovery above when mergeability is the only unknown.
  Yield the session or schedule a delayed wake-up between checks. Do not use
  `gh run watch`, `gh pr checks --watch`, or a polling loop for long-running PR
  checks.

  Every status check must re-query the PR aggregate and compare its current
  `headRefOid`; a run or check identifier is pinned to one commit and cannot
  detect a later push. Retain the expected head SHA locally, and reuse returned
  identifiers only for one-off detail or log queries after the aggregate has
  confirmed that head. Separate discovery calls are prohibited; additional
  calls are only for required context pagination or one-off details after the
  aggregate has confirmed the head. If the query reports low remaining quota,
  yield until its reported reset time rather than sleeping or continuing to
  query. These intervals are minimums, not targets: wait longer when no
  decision depends on an immediate result.
- **Before merge, every PR in a stack meets the applicable conditions above**,
  not only the slice under review. A known-conflicted or known-red parent blocks
  review of everything above it. A pending parent does not block a slice's
  first or conflict-recovery round, provided each layer has a settled pushed
  head and passed focused local evidence. A conflict-recovery round is scoped
  to the recovered slice and may start while that slice's post-push local
  validation and affected descendant restacks are pending; do not review an
  upper slice until its own conflicts are recovered. Before any ordinary
  subsequent round, a current-head aggregate check for every open stack layer
  must confirm zero merge conflicts and green `ci-required`. A slice rebases
  onto its parent, never onto `main`: only the stack's bottom open slice takes
  `origin/main` as its base, and rebasing an upper slice onto `main` pulls in
  work its parent has not landed and makes the slice's diff report its parent's
  changes as its own. `ci.yml` applies no base-branch filter, so every
  non-documentation slice schedules the same CI wherever it targets; a
  non-documentation slice reporting *no* checks is therefore not green.
  Re-query after the registration window, following the status-discovery
  cadence above, and verify the current head; if no matching workflow run
  appears, that is a scheduling bug to investigate, since a PR that triggers no
  workflow leaves `ci-required` nothing to block on and displays as MERGEABLE
  and CLEAN (#3706).

Do not fetch or integrate the base after the candidate is formed while
validation, CI, or review is in progress. After a review-clean result, the
consolidated status query's `baseRef.target.oid` may reveal that the effective
base moved; `baseRefOid` identifies the base commit recorded for the PR and is
not the live branch tip. At that point a non-mutating fetch is permitted solely
to inspect the exact landed range for the carry-forward decision below. Do not
integrate unless that decision or another candidate-ending trigger authorizes
it. When an author change, review fix, or conflict requires a replacement
candidate, say so on the PR and name the base tip and merge commit, so the next
review reads as a confirmation rather than an unexplained second full pass. Do
not form a replacement candidate from base movement alone unless the user
approved the clean-review carry-forward path, explicitly adjusted the workflow
to integrate and re-review an interacting range, or the slice requires a
cascading restack onto its moved parent.

### Clean reviews are not spent by main moving

For a PR that targets `main` — including the bottom slice of a stack — when its
required fixed-head review is review-clean at the current head and `origin/main`
has since moved, **stop and ask.** Do not integrate main, and do not open
another round on your own initiative. Evaluate this from the latest
review-clean result: an earlier finding that was fixed and then reviewed clean
does not disqualify it. If a finding remains unresolved, or the head changed
after that review-clean result because of an author change, conflict resolution,
or restack, the exception does not apply: resolve or restack, integrate the
effective base, and review the new head normally.

Detect movement by comparing the candidate's recorded base tip with the live
tip in `baseRef.target.oid` from the consolidated status query. If they differ
after the review-clean result, record that live tip, fetch the effective base
without integrating it, and analyze the exact old-to-new range.

Ask with an analysis of what actually landed: which commits touch files this
change touches, which behavior this change relies on that they alter, and any
conflict a textual merge would resolve silently but wrongly. Say plainly when
the answer is that nothing in the range interacts with this change — that is the
common case and it is the most useful thing you can report.

If the range is non-interacting, the user may direct you to integrate the
approved base tip, re-run the claimed validation and current-head CI, and carry
the clean reviews forward without another round. Integrate that exact analyzed
tip by SHA, not a moving branch ref. If the live base tip changes before
integration, analyze the additional range and obtain renewed approval before
carrying the reviews forward. Record the reviewed head, old and approved new
main tips, the non-interaction analysis, and the user's decision on the PR.

If the range can affect the change, report that the carry-forward path is not
available and keep the reviewed head. The PR is not merge-ready in that state:
do not post `Ready to merge`. Do not integrate merely because the base moved,
and do not open another round unless an actual conflict, review-driven fix,
author change, or explicit user workflow adjustment creates a replacement
candidate. Ask the user whether to make a workflow adjustment that integrates
the base and re-validates and re-reviews the replacement head, or to leave the
PR blocked.

The carry-forward continuation is the sole exception that carries clean reviews
across a base integration without opening another round. It does not authorize
carrying reviews across author changes, conflict-resolution changes, or a
restack that occurred after the recorded reviewed head. It is also the sole
default path that permits integrating the base when no actual conflict,
review-driven fix, author change, current-head merge-path failure, or explicit
user workflow adjustment has ended the candidate and no required cascading
restack has moved its effective base.

This is the one place the settled-branch rule yields, and it has to, or the
budget is unbounded: on a busy `main`, a round takes longer than the interval
between commits, so integrate-and-re-review by reflex never converges. A pair of
clean reviews is a result. Unrelated commits landing behind it do not retract
it.

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

The review gate has one threshold: trivial changes may skip review; everything
else gets the standard round. Risk scales how deeply the reviewers attack the
change, not how the round is staffed. If you are unsure whether a change is
trivial, escalate: default to review, not none.

| Tier | Requirement |
| --- | --- |
| Trivial | No review. State why the change is trivial. |
| Everything else | **GPT-5.6 Sol**, always, plus one other roster reviewer. |

Adversarial-review roster — this list is the single source of truth, and
scenario docs should reference it rather than restating it:

- **GPT-5.6 Sol** — the fixed seat, in every round
- Claude Opus
- Gemini Pro

**Strongly prefer a second seat from a different model family than the one that
authored the change.** Two families fail differently, and an author reviewing
its own work brings the same blind spot that produced the bug — the second seat
exists for the perspective the first cannot have. Reuse of your own model is
permitted rather than blocking, because the fixed seat already guarantees one
independent perspective, but treat it as the fallback when no other roster
reviewer is available, and say on the PR which case applied.

**Use the highest version and quality level a model offers** in the second seat
— given both Opus 4.8 and Opus 5, use Opus 5. The GPT-5.6 Sol seat is a
deliberate pin rather than a "highest available" slot; when it should move, move
it here.

These tiers assume a harness — such as the GitHub Copilot CLI — that can
delegate to the roster. A harness with only some roster models changes how the
round is obtained, never the bar: run the roster reviewer it has and request
every missing seat from the user. An out-of-roster model may provide a quick
read but does not fill a seat.

A **round** evaluates one settled head with every reviewer its tier requires.
Two reviewers in the same round count as one round, not two. The round remains
locked until both reviewers return and their feedback is reconciled.

### Running the round

Give each reviewer the same self-contained prompt: exact base and head, design
intent, relevant diff, concrete attack points, and required real-run evidence.
Isolate every reviewer in a separate linked review worktree under the primary
checkout's `.worktrees/` directory or an operating-system temporary directory;
never place it at the root of the user's home directory or detach the primary
checkout for review. Require scratch work under `/tmp/` and prohibit `git
reset`, `git add`, and commits in review trees. Before acting on a blocking
finding, reproduce it on a clean exact-head review worktree.

Reconcile the reviews publicly on the PR: attribute findings, state what was
verified or dismissed, and link resolution commits or explain explicit
non-actions. Address actionable findings only after the locked-head reviews
finish. If fixes move the head, push the replacement candidate, wait for its
zero-conflict and green-CI gate, and re-review it as the next numbered round.
Do not mark the PR ready until every required fixed-head review at the current
head is review-clean.

A round starts when its reviewers are dispatched. It ends when all current and
carried feedback is publicly reconciled, every resulting fix is committed and
pushed, current-head zero-conflict evidence is confirmed, and every required
current-head check and post-push local gate for that round has completed
successfully. A no-fix round with publicly justified dismissals can therefore
end review-clean; a round that pushes fixes is complete but not review-clean,
and its replacement head still requires the next numbered review.

Superseded attempts follow [Canonical round flow](#canonical-round-flow);
supersession never retires a finding.

After every completed round and before starting the next one, emit this report
as the assistant's visible user-facing response in the terminal, filling every
field and choosing exactly one feedback classification. Do not emit it through
a shell command such as `printf`, leave it only in tool output, collapse it
behind a tool-call summary, or replace it with a shorter completion summary:

```text
Round <n> is complete for PR <number>.
- Review models <model-a> and <model-b> were used for adversarial review.
- Review feedback is: [converging, diverging, neutral, clean].
- Round start: <datetime>.
- Round end: <datetime>.
- Round duration: <hours:minutes>

Fix description: <prose description of changes made in response to the round>.
```

Use `Fix description` to state the concrete review-driven changes. For a clean
classification, say that no findings or fixes were produced and that the locked
head remained unchanged. For a no-fix round with dismissed findings, use
`converging`, `diverging`, or `neutral` and explain the dismissals in the public
reconciliation. Do not integrate the base after a review-clean result except
through the approved carry-forward path or when a conflict, author change,
current-head merge-path failure, required cascading restack, or explicit user
workflow adjustment ends the candidate. The same report may also be posted on
the PR; the public reconciliation may include more detail when the findings or
fixes warrant it.

### Keep review proportional to the contract

Review the invariant the design actually promises. Unless the threat model
explicitly includes hostile in-process callers, require the invariant for
well-behaved code that follows the design — not for arbitrary code that bypasses
or misuses its abstractions.

Mutation testing is evidence, not an admission rule. A mutation surviving the
suite does not by itself justify another gate: require a plausible regression
of promised behavior that existing contract-level coverage misses. Prefer one
outcome-level test over tests coupled to every branch or call site, and do not
add fixture seams solely to make each intentional-looking weakening
independently red.

Prefer simple, auditable enforcement over making every abstraction a fortress
against rogue callers. `InertString` is the model: code that uses the type
properly gets its invariant, while bypasses and misuse are deliberately easy to
find with a targeted search. A reviewer should report such a caller so it can be
fixed, but should not demand bend-over-backwards features in the type merely to
make misuse impossible. Escalate to stronger enforcement only when the stated
contract or threat model requires it.

### Stop after six rounds

Do not begin a seventh review round without explicit user approval. Each
approval authorizes one new block of up to six rounds: rounds 7-12, then 13-18,
and so on. Stop as soon as review converges; approval is a ceiling, not a
requirement to spend the full block.

Conflict recovery does not waive this approval boundary. Resolve and push a
conflict immediately so CI starts, but if its review would begin a new
unauthorized block, request approval before dispatching reviewers. Once
approved, start that conflict-recovery round without waiting for CI.

Before requesting each six-round block, present an analysis of why the prior
six rounds did not converge. In particular, determine whether the repeated
findings expose an architectural problem, missing test coverage, or reviewers
expanding the contract beyond the intended threat model. State the proposed
architectural or test remedy, or explain why the remaining concern should be
dismissed, before spending another block.

## PR and CI discipline

- Prefer fewer coherent PRs over many small PRs that each pay fixed CI cost and
  increase merge contention. That is an argument against splitting one coherent
  change, not against sequencing a genuinely multi-slice one — see
  [Stacked PRs for multi-slice issues](#stacked-prs-for-multi-slice-issues).
- Keep concurrent agents modest and avoid unnecessary churn in central files.
- Treat CI as confirmation, not discovery: run the smallest relevant local gate
  first, then push the frozen candidate promptly. Run broader local validation,
  CI, and eligible fixed-head review concurrently, subject to the per-round CI
  and conflict gates above.
- A settled candidate should spend wall-clock time in parallel. If an hour
  passes without an authored change while an eligible independent gate has not
  started, stop and correct the sequencing or record the concrete blocker. Do
  not respond by refreshing the base or rerunning a broad suite that already
  proved the unchanged authored head.
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
- Agents are not authorized to merge pull requests unless the user explicitly
  directs them to merge that specific PR. A clean review, green CI, mergeable
  state, `Ready to merge` comment, or general request to prepare or finish a PR
  is not merge authorization.
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
- **Name every slice branch descriptively.** No prefix is required for CI:
  `ci.yml` applies no base-branch filter, so a PR runs CI whatever it targets.
- **One branch and one worktree per slice**, branched from the parent slice, and
  targeted at the parent branch (`gh pr create --base <parent-branch>`).
- **During a GitHub outage, use stacked branches for new coherent slices** so
  local work can continue without pretending remote evidence exists. Branch
  each new slice from its recorded last-known local base or parent slice, create
  its worktree under `.worktrees/`, record that base SHA, and keep its commits
  isolated. When GitHub recovers, fetch the effective base, update the bottom
  slice, cascade required restacks and focused validation through the stack,
  then push and open it bottom-up. Run each slice's required CI, status, and
  review gates before treating it as ready.
- **Merge bottom-up, one at a time**, then confirm the next PR retargeted and
  still shows only its own slice.
- **Restacking rebases and force-pushes a public branch by design** — the
  standing exception to the never-force-push rule, and it cascades to every
  slice above. Use `--force-with-lease`, restack only your own slices, and post
  a `range-diff` proving the restack changed the base and nothing else.
- **Review depth is per-slice, by that slice's own risk**, not the stack's size.
- **Apply the canonical eligibility table stack-wide.** Before an ordinary
  subsequent round, every open layer must have a settled pushed head, focused
  evidence, zero conflicts, and green current-head `ci-required`. A first
  attempt may retain pending CI. A conflict-recovery attempt is scoped to the
  recovered slice and may retain the pending work allowed by its table row;
  upper-slice review remains blocked until that slice is conflict-free. Merge
  readiness still requires every layer to be green and mergeable.
- **A moved head — including one moved by a restack — needs a review-clean round
  at the new head**, and a restack never retires an open finding.
- **Stop stacking when a slice would exist only to continue the stack.** CI cost
  is per PR; three coherent slices beat ten mechanical ones.
