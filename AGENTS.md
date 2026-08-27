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

## Session resume

The transcript survives a resumed session; repository and PR state may not.
Before continuing:

1. Confirm the worktree, branch, and head from git. Fetch the effective base and
   re-check the PR per [Canonical round flow](#canonical-round-flow). Do not pull
   or rebase a pushed branch to catch up.
2. Rename the window and re-announce the PR as described below.
3. State which case applies:

- **Mid-stream:** continue, but handle conflicts, failed gates, or moved bases
  first. Do not revisit decisions already settled in the transcript.
- **Waiting on the user:** restate the full question and options, then wait.
- **Task complete:** state what landed and what proves it, then propose the next
  task without starting it.
- **Unclear:** explain what the transcript claims and what git shows, then wait.

## Making your work findable

Each window must identify its PR, current state, and any decision it needs.

### Name the window for identity

```sh
tmux rename-window -t "${TMUX_PANE:?}" pr<number>
```

Always target `"${TMUX_PANE:?}"`; a bare command renames another window, and an
empty variable silently targets the current one. Rename
the window, never the shared session. Use `pr<number>`, or `i<number>` before a
PR exists. Keep the name stable except for these temporary suffixes:

| Suffix | Meaning |
| --- | --- |
| `-blocked` | waiting on a human decision |
| `-conflict` | in conflict recovery |

### Announce PR identity in your output

At the start of work, after every resume, and at every round start, include the
literal token `PR #<number>` or `PR <number>`, plus the branch or expected head
when relevant. Round completions must use the report in
[`docs/round-orchestration.md`](docs/round-orchestration.md#the-round-report).

### Separate status from approval prompts

When a decision needs supporting status or analysis, emit that material first
as normal visible assistant output. Only after it appears in the session log
may you open the interactive approval prompt.

The prompt contains only the concise decision question and short answer labels.
Do not repeat or move the round report, architectural checkpoint, carry-forward
analysis, evidence, or recommendation into the prompt. The prompt is a control,
not the record.

### Keep the review-clean label current

`review-clean` is live state, not a historical milestone, and it is advisory,
not authoritative — it records that reviews are clean as of a head SHA, not
that the PR is mergeable right now. Reconcile it after every resume and
whenever the head or review result changes. Never infer merge readiness from
its presence: before every merge attempt, re-check current-head CI and
GitHub's live mergeability regardless of the label (see
[Forming a candidate](#forming-a-candidate)).

- **Add it** when every required review at the current head is review-clean.
  Record the reviewed head SHA in the same comment or update that adds the
  label.
- **Base movement on its own does not remove it.** Follow [Clean reviews are
  not spent by main moving](#clean-reviews-are-not-spent-by-main-moving) to
  classify the landed range; a no-interaction classification keeps the label
  through the integration.
- **Remove it** before a new round, author change, conflict recovery, restack,
  unresolved finding, or draft transition — anything that spends the clean
  reviews or reopens the head to a fresh finding.

### Publish your state where tooling can read it

Update both window-scoped options whenever state changes:

```sh
tmux set -w -t "${TMUX_PANE:?}" @agent "round 6 on pr4405, waiting on CI"
tmux set -w -t "${TMUX_PANE:?}" @agent_state "pr=4463 head=595e5d4b round=6 reviews=1/2 blocked=4597,4611 rec=wait"

# Clear when this window no longer owns the PR.
tmux set -w -t "${TMUX_PANE:?}" -u @agent
tmux set -w -t "${TMUX_PANE:?}" -u @agent_state
```

**Publish state as single, separate commands.** Never wrap them in `if`, `&&`,
or a `for` loop. Publishing is the one thing that must never stop to ask
permission: an approval prompt on it blocks the agent on the very act of
reporting that it is blocked, and semi-autonomous work stops dead. A bare
`tmux …` matches an approval rule for `tmux`; `if [ … ]; then tmux … && tmux …;
fi` does not match it, because the command being judged is now the compound.
That difference has stalled real work.

`${TMUX_PANE:?}` is what keeps the target safe without a guard clause. If the
variable is empty the shell fails the command outright and nothing is written —
which matters, because `tmux set -w -t ""` does not error: it silently applies
to whichever window is *current*, which is somebody else's.

Always target `"${TMUX_PANE:?}"`. The state must include
`head` and either `pr` or, before a PR exists, `issue`; add `round`, `reviews`,
`blocked`, `waiting`, and `rec` when applicable. Values contain no spaces. `rec`
is `continue`, `wait`, `merge`, `split`, `approve`, or `stop`. Clear both
options when the window no longer owns the work.

`blocked` and `waiting` are both things you are waiting on, split by **who can
act on them**:

- **`blocked`** takes issue or PR numbers only — things a person can open and
  prioritise, and that the next agent hitting the same wall can find instead of
  re-investigating it. If a flake blocks you and no issue exists, file one and
  cite it.
- **`waiting`** takes one or more comma-separated predicates a tool can evaluate
  against your `head`: `check:<name>`, `checks`, `merge`, or `review`. The wait
  ends only when every listed predicate clears. Use it when nothing is wrong
  and nothing is openable — a check that has not reported yet is not a defect
  and does not deserve an issue.

`rec=wait` is coherent when either is populated. `blocked=ci` is the specific
error this split exists to remove: it names nothing a person can open and
nothing a tool can evaluate, so it reads as a wait on nothing.

### Signal when you need a person

When blocked on a human decision, set a persistent `HELP` state and send one
best-effort nudge:

```sh
tmux set -w -t "${TMUX_PANE:?}" @agent "HELP: integrate main into pr4405, or close it?"
tmux display-message -d 10000 -t "${TMUX_PANE:?}" \
  "HELP pr4405 in w#{window_index}: integrate main, or close it?"
```

Send the nudge once, then stop and wait; the flag is not an answer. Clear `HELP`
as soon as the decision arrives. Use ordinary state for progress and completion.

## User-directed workflow adjustments

The user may adjust a sequencing gate for a specific task or PR. Follow that
direction, record its scope and evidentiary consequence, and preserve every
other requirement. An adjustment does not make failed validation successful,
make an unmergeable PR ready, or transfer fixed-head evidence to a new head.

### Standing adjustments

- **Review in parallel with CI:** requires the user's approval for that PR. A CI
  failure requiring an author change still supersedes the attempt, and all
  findings carry forward.
- **Auto-merge on the final push:** once every required review is review-clean,
  the user may authorize auto-merge for the intended final head; the agent may
  ask. If the head moves after arming, disarm, review the new head, and ask
  again.
- **"CI is ready":** the user's statement that CI has no failures and the PR is
  mergeable. Trust it without re-checking and move to the next task, such as
  dispatching the next round's reviewers.
- **Authorizing the next round before CI completes:** the agent does not need
  to check CI status first; proceed with the authorized round.

## Before changing files

- Keep the primary checkout attached to protected `main`; never develop or
  detach HEAD there.
- From the primary checkout, run `git fetch origin main`, then create a
  descriptive branch and linked worktree:

  ```sh
  git worktree add -b <branch> <repo>/.worktrees/<slug> origin/main
  ```

  A stacked slice branches from its parent. During a GitHub outage, use the
  recorded last-known base allowed by [the stack rules](#stacked-prs-for-multi-slice-issues).
- Use one development worktree per PR and one temporary worktree per reviewer.
  Put them under `.worktrees/` or, for reviewers, an OS temporary directory;
  never directly under the home directory.
- For an open PR, apply [Canonical round flow](#canonical-round-flow) before
  other work. Conflict recovery has first priority.
- Never amend. Rebase only before the first push. After publication, merge the
  effective base; never rebase or force-push reviewed history except when
  restacking your own slices under the stack rules.
- After integrating or resolving conflicts, re-read this file and the relevant
  focused docs. Do not include unrelated or another contributor's changes.
- Remove reviewer worktrees after review and reproduction finish. Remove a
  development worktree after merge, or after the exact pushed head is unlocked,
  all concurrent gates pass, and every required review is review-clean. Recreate
  it later if needed.

## Task-specific guidance

| Area | Read first |
| --- | --- |
| User-visible capabilities, commands, or examples | `README.md` |
| Core workspace, query, cache, or safety architecture | `docs/inspection-space.md` |
| A change crossing subsystem ownership boundaries | `docs/overview.md` |
| Implementation structure | the relevant section of `docs/architecture.md` |
| Layering and consumer boundaries | `docs/design/inspection-layers.md` |
| Artifact acquisition and workspace composition | `docs/design/artifact-acquisition-and-workspaces.md` |
| Platform composition, overlays, and core-library entitlement | `docs/design/platform-composition-and-overlays.md` |
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
| TypeScript facade generation for `[JSExport]` | `docs/design/ts-jsexport.md` |
| IL round-trip tests | `tests/DotnetInspector.ILRoundtrip.Tests/README.md` |
| Decompiler raising, structuring, typing, or printer behavior | `docs/decompiler-correctness-pipeline.md`, then `docs/decompiler-raise-discipline.md` |
| Classic async state-machine reconstruction | `docs/design/classic-async-reconstruction.md` |
| Decompiler harness-only behavior | `docs/decompiler-correctness-pipeline.md`, then the owning harness README |
| Skills | `taste/skill-guidance.md` |
| Stacked PRs and restacking | `docs/stacked-prs.md` |
| Running a review round, or checking PR status | `docs/round-orchestration.md` |
| Hosting a network-accessible inspect-web demo | `docs/runbooks/inspect-web-demo-hosting.md` |
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

Keep user-facing product skills and repository-maintainer skills separate:

- `skills/` contains user-facing guidance shipped in the dotnet-inspect binary.
  Use these skills when consuming the published tool or when a product change
  needs its user-facing commands, examples, and expectations reviewed. Do not
  select them merely because an agent is maintaining this repository; they are
  product artifacts, not contributor runbooks.
  When adding a focused product skill, register it in `SkillCommand.Skills` and
  add an `EmbeddedResource` line for it in
  `src/dotnet-inspect/dotnet-inspect.csproj`; the embeds are enumerated per
  skill. `FocusedSkillFilesRegistryAndEmbeddedResourcesAgree` keeps the skill
  directories, runtime registry, and embedded resources equal. Its YAML
  frontmatter `description:` is the single source of truth for the generated
  skill listing.
- `.github/skills/` and `.claude/skills/` contain repo-local guidance for
  contributors and agents. Use the matching repo-local skill for release, CI,
  corpus maintenance, and other repository operations. Do not register or
  embed these skills in the product, and keep repository operations out of the
  user-facing `skills/` tree.

### Which dotnet-inspect to run

For routine repository development and investigation, use the latest production
dotnet-inspect:

```bash
dnx dotnet-inspect -y -- <command>
```

The production tool is normally current and its Native AOT executable starts
much faster than `dotnet run`. Prefer it for inspecting packages, platform
libraries, local artifacts, and existing product behavior while developing.

Use the source version primarily to test behavior from the current worktree:

```bash
dotnet run --project src/dotnet-inspect -c Release -- <command>
```

The source command is required when the evidence depends on an unmerged change,
when reproducing or validating a source-only fix, or when checking output that
the production release does not yet contain. Do not cite the production tool as
evidence for worktree behavior, and do not pay the source-build startup cost for
routine development queries that the production tool can answer.

## Design scope and composition

Default every design effort to exactly one architectural owner and name its
owning document. A focused owner is either an independently owned architecture
unit whose authority was already stated in
[the overview](docs/overview.md) or an existing focused owning document before
the effort began, or exactly one new unit established by the effort. A
new-owner effort adds the unit's authority entry to the overview, creates or
names its focused owning document, and declares its responsibility, immediate
boundaries, and non-claims. It may introduce a new responsibility or transfer
one cohesive responsibility from one existing owner when that transfer is the
effort's single claim and the donor's other authority is unchanged. The donor's
relinquishment of that one responsibility and corrections that only remove
stale statements assigning it to the donor are part of the transfer; those
edits may not change any other owner contract. Any other normative donor change
is a separate effort. A new owner may not aggregate responsibilities from
multiple owners or create an umbrella owner to evade the broad-design gate. A
project boundary alone neither creates nor erases a component boundary. Every
focused issue and PR names the owner and owning document. For this rule, each
such owner is one component.

A focused design may specify its owner's immediate typed input and output
obligations. It may reference an adjacent component's owner-issued types and
state the preconditions it consumes and the results it returns, but it must not
redefine that component's construction, validation, identity, lifetime, or
failure semantics. Except for the bounded one-donor transfer above, if closing
the claim requires normative changes in two owners, use two focused efforts and
connect them with a thin composition map.

A composition document may name sequencing and typed handoffs, but must
reference owner contracts rather than restating participating components'
internal inventories or policies. When another component needs prerequisite
work, file or record that residual and handle it as an independently reviewable
effort or stack slice. Do not expand the current design merely to make the whole
end-to-end system appear closed. PR coherence does not justify combining
independently owned component designs.

A **broad design** sweeps an end-to-end lifecycle such as acquisition,
analysis, publication, and presentation or, outside the bounded one-donor
transfer above, normatively specifies multiple independently owned components.
Do not start one or broaden a focused effort into one unless the user explicitly
requests or approves that scope. A large issue, cross-cutting motivation,
general request to redesign a subsystem, or reviewer suggestion is not
approval. Before requesting approval, present the component map, explain why
focused designs cannot close independently, and name the intended claims and
non-claims.

### Reviewing focused designs

Review a focused design against its named owner, owning document, immediate
typed boundaries, and declared non-claims. If repeated review keeps discovering
new component-internal contracts or manually synchronized cross-component
inventories, stop and apply the scope-violation recovery transition. Adding
more prose, stages, gates, or receipts to a sweeping document is not evidence
that it closes.

### Recovering from an over-broad design

If you discover that current work violates this guidance, stop broadening,
repairing, or reviewing the design in place and apply the
[scope-violation recovery transition](#recovery-transitions). Keep a locked
candidate unchanged while discussing the violation with the user. Name the
components whose ownership has been combined, explain the closure or review
evidence that exposed the problem, and propose component-sized replacements in
priority order, including their owners, owning documents, immediate boundaries,
dependencies and parallel work, claims, and non-claims.

After that discussion, preserve significant design problems found in other
components as focused issues rather than dropping them or absorbing them into
the current design. Each issue names the owning component and document,
concrete evidence and consequence, why the problem is outside the current
claim, and any boundary or sequencing dependency. Filing the issue preserves
the finding; it does not approve a solution or expand the current effort.

Present three explicit outcomes, recommend one, and ask the user to choose:

- **Split into focused successors.** Supersede the broad candidate and re-derive
  each successor's normative contract in its owning document; do not copy or
  mechanically move an unclosed contract. Close the broad effort, or replace it
  with one named focused successor when the user explicitly chooses that use.
- **Abandon.** Supersede and close the current effort without committing to
  successors. Preserve useful analysis only as explicitly non-normative source
  material and retain any already-filed focused issues as independent records.
- **Approve a broad exception.** Record the user-approved scope and preserve
  every other requirement in this section.

Do not silently narrow the work or infer approval to continue broadly. Until the
decision, do not dispatch another review or describe the design as ready.

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

Use the SDK selected by repository configuration and CI. Before installing an
SDK or changing `PATH`, inspect the current selection:

```bash
command -v dotnet
dotnet --version
```

If `dotnet` is centrally installed, stop and ask before replacing or shadowing
it. Follow `README.md#repository-development-sdk`; do not change shell startup
files unless asked.

Build the normal graph with:

```bash
dotnet build dotnet-inspect.slnx -c Release
```

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

The CLI and decompiler suites skip `ilasm`/`ildasm` checks when those tools are
missing; metadata tests do the same for `mdv`. Activate all three before relying
on a clean run:

```bash
source eng/activate-iltools.sh --mdv
```

Source the wrapper; do not assemble `PATH` by hand. CI restores the same pinned
tools and fails its lane if acquisition fails.

The IL round-trip project has separate dependency restore and fast/full test
commands; follow `tests/DotnetInspector.ILRoundtrip.Tests/README.md`.
`ILInspector.Decompiler.Tests` composes `Speed` and `Area` traits and offers a
`--gate <preset>` flag (`--gate list` prints the table); the taxonomy and the
per-change targeting advice live in `docs/decompiler-correctness-pipeline.md`.

Only tool projects set `IsPackable=true`; `IsTool` also makes them available to
solution-level publish. Internal library APIs are not external compatibility
surfaces. Changing `VersionPrefix` is a coordinated package-and-site release:
follow `docs/release-workflow.md`, publish dotnet-inspect and
`https://dotnet-inspect.net` from the same commit, and update the shipped
`README.md` and skills.

### Package acquisition when nuget.org is disabled

If a machine-level proxy lacks an exact pinned version and restore reports
`NU1603`, do not edit machine configuration or commit a clearing
`nuget.config`. Override sources for one restore:

```bash
dotnet restore dotnet-inspect.slnx -s https://api.nuget.org/v3/index.json
```

Prefer `--source` to `--add-source`; the package cache then satisfies later
restores. Repeat after clearing the cache or changing to an uncached pin. Tool
acquisition accepts the same override:

```bash
dotnet tool install -g dotnet-inspect --source https://api.nuget.org/v3/index.json
dnx dotnet-inspect --source https://api.nuget.org/v3/index.json
```

### File-based apps

For throwaway probes, use .NET file-based apps under `/tmp/` unless a specific
Python library is required. Do not use `.csx`, `dotnet-script`, `dotnet script`,
or `dotnet-fsi`.

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

A safety, soundness, or faithfulness claim must name its enforcing gate or say
`unverified`. Prefer deriving the gate's expected set from the declaration so
both missing and stale entries fail. For wiring properties, add one named
non-vacuity test that fails when the wiring is removed. A gate counts only when
it runs in the suite's Release configuration; use runtime opt-ins, not
`[Conditional("DEBUG")]`.

### Harness boundary

Harnesses own orchestration, fixtures, independent oracles, comparison, and
reporting. They may parse source or diagnostics to measure evidence, but must
exercise product-owned artifact construction. Do not construct, normalize,
repair, or rewrite C# that is later compiled as product evidence, and do not add
fallbacks or shape recognition that compensate for missing product behavior.
If a test requires that compensation, stop, file the product gap, and fix it or
mark the harness work blocked.

Decompiler raising, typing, structuring, fidelity, or printer changes have
additional evidence requirements. Follow the decompiler docs and PR templates
rather than duplicating their evolving commands and gates here.

### Markdown

All changed Markdown must pass `markdownlint` before commit. Run the fixer first
when needed:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

## Adversarial review

Review is a locked-head feedback loop: freeze and push one exact head, review
that head, reconcile the feedback publicly, make any resulting fixes, and freeze
the replacement head. Everything below serves that loop.

These are the binding invariants. The rest of this section explains them;
driving the loop is
[round orchestration](docs/round-orchestration.md).

1. **One frozen head per round.** The lock begins at the push and ends two ways
   only: the round closes — reconciled *and* green — or recovery supersedes the
   attempt. Do not edit a head while it is held; fixes belong to the next cycle.
2. **A candidate includes its effective base.** Integrate twice before pushing
   — once before fixing, once after — because the fix window is long enough for
   `main` to move.
3. **Base movement alone never invalidates a pushed candidate**, and never
   justifies another round.
4. **A round that pushes a fix is not review-clean.** Only the replacement head
   can earn that.
5. **Never claim merge readiness from label state alone.** Confirm current-head
   CI and GitHub's live mergeability immediately before every merge attempt,
   even when `review-clean` is present.
6. **A round closes only when reconciled and green.** Both: the feedback is
   publicly reconciled, and every required current-head check and post-push gate
   has succeeded. For a documentation-only PR, the pre-commit `markdownlint`
   result is the per-round green gate; `ci-required` may remain pending until
   final merge readiness. Until the applicable gate is green the round number
   does not advance — a check that goes red first makes the next push a
   failed-gate restart at the *same* number, not the next round.
7. **Six rounds, then stop** and ask for another block.
8. **Never merge without explicit user authorization** for that specific PR.
   Auto-merge armed at the user's direction is that authorization; see
   [Standing adjustments](#standing-adjustments).

### Canonical round flow

This section owns candidate formation, eligibility, locking, supersession, and
recovery. `docs/round-orchestration.md` owns the mechanics.

#### The round cycle

Steps 1-5 run unlocked. The push at step 6 locks the head until step 10 or a
recovery transition supersedes it.

1. Integrate the effective base.
2. Make the initial or review-driven change.
3. Run the focused gate.
4. Integrate the effective base again.
5. Re-run focused gates for anything the integrated range can affect.
6. Push and record the candidate head and effective base; the lock begins.
7. Satisfy the applicable eligibility row below.
8. Dispatch every required reviewer at the exact candidate head.
9. Reconcile all feedback publicly.
10. Close only when reconciliation and every current-head gate are green. The
    lock ends, the round number is spent, and the visible
    [round report](docs/round-orchestration.md#the-round-report) is required.

Both integrations happen before the push. Base movement after the push does not
reopen the locked candidate.

| Attempt | Required before reviewer dispatch | May remain pending |
| --- | --- | --- |
| First attempt at round 1 | Pushed settled head, recorded effective base, focused gate | CI and mergeability |
| Ordinary subsequent round | First-attempt requirements, zero conflicts, green current-head `ci-required` | Nothing required |
| Conflict-recovery attempt | Resolution head pushed, round number authorized | Post-push local gates, CI, mergeability |
| Failed-gate restart | Required fix pushed, zero conflicts, green current-head `ci-required` | Nothing required |

Documentation-only candidates do not wait for CI before review. Read the
applicable attempt row with only two substitutions: `markdownlint` must pass
before commit and replaces any requirement for green current-head
`ci-required`, and `ci-required` may remain pending. Every other requirement
still applies, including the settled push, recorded effective base, zero
conflicts where required, and round authorization. A documentation-only round
may close after reconciliation and the local lint gate; `ci-required` remains
mandatory for final merge readiness. The user may authorize review in parallel
with CI for other changes.

#### Review-clean, and what it gates

A review is **review-clean** when public reconciliation leaves no finding
unresolved and the head did not move in response. A justified dismissal counts
only when recorded publicly. A fix-producing round can complete but is not
review-clean; only the replacement head can earn that status. A review-clean
round ends adversarial review. The report classification `clean` is narrower
and mandatory when every required reviewer returned no findings and the locked
head stayed unchanged. Use `converging`, `neutral`, or `diverging` only when at
least one reviewer returned a finding.

#### Recovery transitions

- **Conflict:** supersede the attempt, integrate and resolve, push immediately,
  and restart the same round without waiting for CI. The six-round boundary
  still applies.
- **Scope violation:** keep the locked head unchanged while the user chooses
  split, abandonment, or an explicitly approved broad exception. Split or
  abandonment supersedes the attempt without spending the round; reconcile
  returned findings publicly, then close the broad effort or replace it with a
  user-selected focused successor. A replacement head follows the
  author-change transition at the same round. A broad exception may resume the
  unchanged attempt after its scope is recorded; any required head change
  follows the author-change transition.
- **Failure requiring an author change:** supersede the attempt, push the fix,
  satisfy the failed-gate row, and restart the same round.
- **Cancelled or evidenced transient failure:** keep the lock and retry the
  unchanged head. Repeat only with concrete transient evidence; otherwise treat
  it as requiring an author change.

If final-gate `ci-required` reports any conclusion other than success after a
documentation-only round closes, it does not reopen or renumber that completed
round. Retry at the unchanged head only with concrete evidence that the result
is transient and requires no author change. Otherwise remove `review-clean`,
make the required fix, and form a candidate at the next round number,
respecting the next six-round authorization boundary.

Never close with a required check red. A superseded attempt spends no round and
gets no completion report. Let its reviewers finish or have cancellation
acknowledged, carry every returned finding forward, and reconcile each one
before the restarted round closes.

### Forming a candidate

Spend review only on a pushed, settled head formed by the canonical cycle.
Record the exact head and effective base. If a conflict, author change, finding,
or restack moves the head, form a replacement through the cycle again.

While a candidate is locked, do not push or integrate. Recovery is the only
mutation exception; a non-mutating fetch is allowed to re-establish state after
a resume and for the carry-forward analysis below.

Before merge, re-read GitHub state and confirm the expected head, non-draft
status, positive mergeability, and successful current-head `ci-required`.
`BLOCKED`, `DRAFT`, `UNKNOWN`, a missing check, or a check from another head is
not ready. Follow
[status discovery](docs/round-orchestration.md#status-discovery).

For stacks, every open layer must meet its applicable eligibility row. A
known-red or conflicted parent blocks upper slices; a pending parent does not
block a first or conflict-recovery attempt. Apply the remaining stack rules
below.

### Clean reviews are not spent by main moving

When a `main`-targeting PR has a review-clean current head and `origin/main`
moves, assess the landed range before doing anything else; do not integrate
blindly and do not start another round by default. This also applies to the
bottom open stack slice. An upper slice follows its parent, so parent movement
is a restack and requires review at the new head.

After a non-mutating fetch, classify the exact landed range into exactly one
outcome and act on it directly — the classification drives the response, not a
per-movement approval prompt:

- **No interaction.** The range does not touch files, contracts, or behavior
  this change touches. Keep `review-clean`, integrate the exact analyzed tip by
  SHA, and update the recorded head SHA — skip re-running validation, CI, and
  review. This is the common case on a fast-moving `main` and is what ends the
  poll-and-rerun loop: repeated non-interacting movement never demands another
  gate. Merging itself still needs a live readiness check and explicit user
  authorization (invariants 5 and 8 under [Adversarial
  review](#adversarial-review)); base movement alone does not grant it.
- **Significant interaction, no conflict.** The range touches related files,
  contracts, or behavior but merges cleanly. Remove `review-clean`, integrate
  the tip, re-run the applicable validation and current-head CI, and
  re-dispatch the required reviewers at the new head as a normal round; the
  prior clean reviews do not carry forward.
- **Merge conflict.** Remove `review-clean` and treat it as an author change:
  integrate, resolve the conflict, rebuild and re-test, and re-dispatch the
  required reviewers at the new head, following the ordinary [conflict recovery
  transition](#recovery-transitions).

Report the classification and the action taken as normal session output before
changing labels or dispatching reviewers. Re-classify only when the landed
range itself changes — a later, distinct base movement — not on every poll of
an already-classified range. Follow the full
[carry-forward procedure](docs/round-orchestration.md#carry-forward-after-clean-reviews).

### A quick read is not a round

Use MAI-Code for early feedback on unsettled work. A quick read gets no isolated
worktree or fixed head and satisfies no review tier. Label its findings as early
feedback; the settled PR still requires its full round.

### How many reviewers, and from which models

| Tier | Requirement |
| --- | --- |
| Trivial | No review. State why the change is trivial. |
| Everything else | **GPT-5.6 Sol**, always, plus one other roster reviewer (Claude Opus or Gemini Pro). |

When uncertain, use the standard round. Pick the second seat from the prior
round's clean count:

- **No prior round, or 0/2 clean:** prefer GPT-5.6 Sol again for the second
  seat.
- **1/2 clean:** keep GPT-5.6 Sol fixed, rule out last round's second seat,
  then prefer a different family than the author (the author's family only as
  a fallback) at that model's highest available quality.

Record the choice and its reasoning on the PR.

If a roster model is unavailable, substitute another model for that seat,
report the substitution on the PR, and proceed without approval — a
substituted seat still counts as filled. One round evaluates one settled head
with all required reviewers.

### Running the round

Give every reviewer the same self-contained prompt and a separate worktree.
Review the whole head unless the user narrows scope. Reproduce findings before
acting on them, wait for all locked-head reviews, and reconcile publicly.

Word the prompt as a description of the property under test, not as an attack
brief. A prompt that reads as an exploit tutorial can trip a model's content
filter, and it fails silently: the reviewer returns nothing and looks broken.
Suspect the prompt before the model when a reviewer returns empty. Follow
[running a round](docs/round-orchestration.md#running-a-round) for mechanics and
reporting.

### Keep review proportional to the contract

Review the invariant the design promises, not arbitrary misuse outside the
threat model. A surviving mutation justifies a gate only when it exposes a
plausible regression of promised behavior. Prefer outcome-level tests and
simple, auditable enforcement over fixture seams or abstractions hardened
against callers the contract excludes.

A reviewer concern that materially expands functionality is a scope proposal,
not automatically a landing requirement. Do not accept it without explicit
operator approval. The default is to reject it as out of scope or record it as
follow-up work; significant scope growth is normally not part of landing the
current PR.

### Stop after six rounds

Rounds 1-6 are the initial authorized block. Within an authorized block, a
fix-producing round that requires replacement review continues automatically
to the next round. Report `Recommendation: continue` and begin the next
candidate cycle; do not ask for approval, set `HELP`, or wait for user input.

Approval is required only before rounds 7, 13, 19, and so on. Each approval
allows at most six more rounds; stop sooner when review converges. At a block
boundary, conflict recovery may resolve and push immediately, but reviewers
still wait for approval, unless an immutable split decision hold is active.

Before requesting another block, answer:

1. **What changed?** Summarize product, architecture, and test improvements,
   findings retired, and confidence gained. Separate durable progress from
   churn.
2. **Are reviews converging?** Cite clean counts and repeated versus new finding
   categories. State why dual-clean is or is not likely in the next block.
3. **Are the foundations sound?** Classify remaining findings as architectural,
   coverage gaps, contract expansion, or harness-only concerns.
4. **Should implementation pause for a docs-only design PR?** Recommend it when
   contracts, ownership, or architecture need direct repository-owner
   engagement.
5. **If design work was skipped last block, why skip it again?** The prior
   decision is not standing authorization; identify the new evidence that makes
   implementation rounds the better investment.

When a PR reaches round 12, split the remaining work into focused successors
unless the checkpoint establishes a strong reason to keep the PR intact and
the user explicitly approves that exception. The strong reason must explain
why the remaining claims cannot become independently reviewable successors,
why the reviews are still converging, and why continuing the same PR is safer
than splitting it. Reviewer familiarity, sunk cost, or the inconvenience of
restacking are not strong reasons. Sprawling changes accumulated across review
comments are themselves a sign that the remaining work should be split into
focused successors. The same presumption and burden apply at every 6-round
boundary after round 12.

After round 12 or a later six-round boundary closes, the split recommendation
puts the completed head in an immutable decision hold while the user decides.
This is not a round lock; do not mutate the head or dispatch another round
during the hold, including for conflict recovery. If approved, publicly assign
every current change, claim, and finding — including resolved or dismissed
findings and their resulting changes or rationale — to a focused successor, or
explicitly record why an item is being dropped. Close the current PR as
superseded without merging it, and open the successors from their effective
base. Each successor starts at round 1; reviews, round counts, and authorization
blocks do not carry forward.

At round 12 and every 6-round boundary after (18, 24, and so on), also answer:

1. **Would a design doc better define the design space?** Foundational APIs
   weigh heavily toward yes.
2. **Can hardening move to followups?** State whether deferring remaining
   hardening to followup work would unlock this PR's value for other agent
   work sooner.

State the proposed remedy and end with one recommendation: split into focused
successors, approve the next implementation block under the strong-reason
exception, switch to a docs-only design PR, or stop. If consecutive rounds only
strengthen the harness while the product goes unchallenged, report that count
and recommend splitting or stopping rather than continuing by reflex.

Publish the complete checkpoint as normal session output before opening the
approval prompt. The prompt asks only which recommended action to authorize; it
must not contain the checkpoint itself.

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
- Label a documentation-only PR (no product code, test, or build changes)
  `documentation` when opening it.
- When passing a file's content to `gh api` (for example a PR body), use
  `-F key=@path` (typed `--field`, which expands `@path`), not `-f key=@path`
  (raw `--raw-field`, which sends the literal string `@path`). This is a `gh`
  flag distinction, not platform-specific. Verify with
  `gh pr view <n> --json body -q .body` after creating or editing a PR body
  this way.
- Avoid high-level `gh` commands when they fail by querying deprecated GraphQL
  fields. In particular, `gh pr edit` may hit the removed Projects (classic)
  fields even when changing unrelated metadata. Do not retry it; use the
  operation-specific REST endpoint through `gh api` instead. Use
  `PATCH repos/{owner}/{repo}/pulls/<number>` for PR title, body, or base
  changes; the issue labels POST and per-label DELETE endpoints for label
  additions and removals; the issue assignees POST and DELETE endpoints for
  assignee additions and removals; and
  `PATCH repos/{owner}/{repo}/issues/<number>` for milestone changes. Do not
  replace complete label or assignee arrays to perform an add or remove.
  Verify the resulting metadata after the REST update.
- Treat CI as confirmation: run the focused local gate, then push promptly.
  Run eligible local suites, CI, and review concurrently.
- Use [status discovery](docs/round-orchestration.md#status-discovery): REST by
  default, GraphQL only when breadth is worth its shared quota, and scheduled
  checks rather than polling.
- A settled candidate should spend wall-clock time in parallel. If an hour
  passes without an authored change while an independent gate has not started,
  fix the sequencing or record the blocker.
- `ci-required` is the only merge-gating check. It passes when all jobs that ran
  succeeded or skipped; a missing aggregate is not green. Never require a
  path-gated job directly, and do not broaden CI without measured need.
- Keep PR summaries conclusion-first: claim, evidence, compatibility or
  non-action boundary, and exact validation.
- `review-clean` records clean reviews as of a head SHA; it is advisory, not a
  merge-eligibility claim. Confirm current-head CI and GitHub's live
  mergeability at the moment of any merge attempt or readiness statement — do
  not infer either from the label or from a prior check.
- Never merge without explicit authorization for that PR. A clean review, green
  CI, readiness comment, or request to prepare a PR is not authorization.
  User-directed auto-merge authorizes only the reviewed head.

### Stacked PRs for multi-slice issues

When an issue is too large for one coherent PR, prefer a **stack** — a sequence
of PRs targeting their predecessors — over one unreviewable PR or parallel PRs
that race in the same files. `docs/stacked-prs.md` owns the mechanics.

- Each slice must land independently with one claim and its own evidence. Fold
  in any slice that depends on later work for correctness.
- In every PR, name the slice position, parent PR, and remaining work.
- Give each slice its own branch and worktree, branched from and targeted at its
  parent. Only the bottom open slice uses `main`.
- During a GitHub outage, branch from the recorded last-known base or parent.
  On recovery, update and validate bottom-up before pushing.
- Merge bottom-up and confirm each retargeted diff still shows only its slice.
- Restacking your own slices is the exception to the no-force-push rule. Use
  `--force-with-lease` and post a `range-diff` proving only the base changed.
- Apply review depth and the canonical eligibility table per slice and
  stack-wide. Every moved head needs a review-clean round; restacking never
  retires findings.
- Stop when another slice would exist only to continue the stack.
