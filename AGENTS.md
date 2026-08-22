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

Your process can be replaced without your work being finished — a machine
reboot, a lost terminal, a session resumed from disk. This section covers that.
It is distinct from a round restart, which
[Canonical round flow](#canonical-round-flow) governs.

Your transcript comes back intact, and no conversation was missed: nothing
happened between your last turn and this one, so there is nothing to catch up on
and no new direction waiting to be found. What may have moved is machine state
outside your process — CI runs asynchronously and other work merges — so a plan
formed before you stopped can describe a world that no longer exists.

### First, re-establish the world

- **Position comes from git, not from the transcript.** Confirm the worktree,
  branch, and head. Fetch, and determine whether the effective base moved while
  you were gone. Do not pull or rebase a pushed branch to "catch up"; reconcile
  it the way this file already requires.
- **Re-check PR state** per [Canonical round flow](#canonical-round-flow).
- **Re-announce yourself.** A resumed window has lost whatever it had on
  screen, so nothing identifies it. Rename it and restate your PR per
  [Making your work findable](#making-your-work-findable).

### Then act on where you stopped

Exactly one of these applies. Say which, in one line, before doing anything
else.

- **Mid-stream — continue.** Pick the work back up. The re-check above takes
  precedence: a conflict, a failed gate, or a moved base supersedes your
  restored plan and is handled first. Conflict recovery remains the first
  priority. If nothing changed, do not re-litigate decisions already made in
  the transcript; carry on from them.
- **Waiting on the user — restate the request.** Never assume the question was
  seen or answered while you were gone. Restate it in full, including the
  context needed to answer it and the options you were choosing between; a
  pointer to an earlier message is not a restatement, because the user may be
  looking at a fresh window with none of that history on screen. Then wait.
- **Task complete — report and propose.** State what landed and what proves it.
  Then either propose the next piece of work, with a reason it is the right
  next thing, or ask for a task. Propose; do not start. Inventing scope after a
  resume is how a finished PR grows changes nobody asked for.

If you cannot tell which of the three applies, that is the fourth case: say so,
summarize what the transcript claims and what git shows, and wait rather than
guessing.

## Making your work findable

Work runs in many concurrent agent windows across several machines. Whoever is
watching must be able to tell, without attaching to any of them, which PR each
window is on and which one needs a person. Three conventions carry that. Use
them.

### Name the window for identity

`tmux rename-window pr<number>` — not the session. A tmux session is shared by
every window on that host, so renaming it identifies nothing; `rename-window`
sets the same per-window name that `C-b ,` sets. Without a PR yet, use the
issue: `i<number>`.

Keep the name short and stable. The status bar truncates, and a truncated name
reads as a corrupted one. Do not encode changing state in it — your terminal
title already carries that, updates itself, and costs nothing.

The one exception is a state a person must act on. Append a single token then,
and remove it when it clears:

| suffix | means |
| --- | --- |
| `-blocked` | waiting on a human decision |
| `-conflict` | in conflict recovery |

`pr4405-conflict` is worth the eight characters. `pr4405-round-6-of-adversarial-review` is not.

### Announce PR identity in your output

State which PR you are on, in your visible output, in a form a reader and a
script can both parse. Either pattern below is sufficient and both is fine; what
matters is that the literal token `PR #<number>` or `PR <number>` appears, and
the branch name where it is relevant.

Beginning or continuing work:

> Continue PR #4405 readiness for frozen expected head `595e5d4b…` on branch
> `browser-platform-workspace` after conflict recovery.

Completing a round:

> Round 6 is complete for PR 4463.
> - Review models GPT-5.6 Sol and Claude Opus 5 were used for adversarial review.
> - Review feedback is: converging.
> - Round start / end / duration.
>
> Fix description: …

Restate it after every resume and at the start of every round, not once at the
beginning. A window that has scrolled past its only mention of the PR is a
window nobody can identify.

### Signal when you need a person

Whenever you stop and wait on a human decision, say so out of band as well as on
screen. This is the standing convention during normal work, not an option:

```sh
tmux display-message -d 10000 'PR #4405 needs a decision'
```

Send it once, when you become blocked — not on a timer, and not again while
waiting on the same question. Keep it to one short line naming the PR and what
is needed; the message takes over the status line for as long as it shows.

**It is best effort and will often go unseen.** Nobody may be attached; the
person may be in another window, on another machine, or asleep. So it is a
nudge, never a handoff: a sent notification is not a delivered question and
never an answered one. Stop at your prompt and wait exactly as you would have
without it, and restate the request in full when resumed.

Signal only for being blocked. Progress, completion, and resuming are not
signals — they belong in your output, where they can be read whenever someone
looks.

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

### Standing adjustments

Two are common enough to name. Both still need the user's word for the specific
PR; naming them means treating them as expected requests rather than exceptions
to be argued about.

**Review in parallel with CI.** The eligibility table makes an ordinary
subsequent round wait for green current-head `ci-required`. When the user
directs it, dispatch that round while CI runs. Sequencing changes, nothing else:
a CI failure needing an author change still supersedes the attempt under
[Recovery transitions](#recovery-transitions), and a superseded round's findings
still carry forward.

**Auto-merge on the final push.** Once every required review is review-clean and
a push is intended to be the last, the user may direct that auto-merge be armed,
letting GitHub merge when the required checks pass. **The agent may ask for
this** — it is the one merge-related request to raise on its own initiative, and
asking is not merging.

Arming auto-merge authorizes the merge of the reviewed head; it is not a
standing grant for the branch. GitHub keeps it armed across later pushes, so
anything pushed afterward merges unreviewed once checks pass. If the head moves
after arming — a review finding, a conflict resolution, a restack — disarm it,
review the new head, and ask again.

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
| Artifact acquisition and workspace composition | `docs/design/artifact-acquisition-and-workspaces.md` |
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
| Running a review round, or checking PR status | `docs/round-orchestration.md` |
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
| CLI and product output | `dotnet run --project src/dotnet-inspect.Tests -c Release` |
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
5. **Do not post `Ready to merge` until every required review at the current
   head is review-clean.**
6. **A round closes only when reconciled and green.** Both: the feedback is
   publicly reconciled, and every required current-head check and post-push gate
   has succeeded. Until then the round number does not advance — a check that
   goes red first makes the next push a failed-gate restart at the *same*
   number, not the next round.
7. **Six rounds, then stop** and ask for another block.
8. **Never merge without explicit user authorization** for that specific PR.
   Auto-merge armed at the user's direction is that authorization; see
   [Standing adjustments](#standing-adjustments).

### Canonical round flow

This section is the sole source of truth for candidate formation, round
eligibility, head locking, supersession, and recovery. Other sections add
reviewer, stack, or readiness detail without redefining these transitions.

#### The round cycle

Steps 1-5 run with no lock held. The lock begins at the push, and ends at step
10 unless a [recovery transition](#recovery-transitions) supersedes the attempt
first.

1. **Integrate** the effective base, so the work is written against current
   `main` rather than against history.
2. **Fix** — the review-driven changes, or the initial authoring for round 1.
3. **Validate** the fix with the focused gate.
4. **Integrate again.** Fixing takes real time, and `main` moves during it.
5. **Validate again**, enough to prove the integration did not break the fix.
   Scope it by the rerun rule under [Evidence and
   validation](#evidence-and-validation): focused gates for whatever the landed
   range can interact with, not the broad suite again.
6. **Push.** That head is the candidate, and the lock begins here.
7. **Confirm zero conflicts and green current-head `ci-required`** — unless the
   round's row below leaves them pending, or the user authorized reviewing in
   parallel with CI. A conflict or a failed check here does not mean waiting
   longer; take the matching [recovery transition](#recovery-transitions).
8. **Review**: dispatch every required reviewer at that exact head.
9. **Reconcile** the feedback publicly.
10. **Close the round** once it is also green (invariant 6). The lock ends here,
    and only here is the round number spent. Emit the round report as your
    visible response — required, format in [the round
    report](docs/round-orchestration.md#the-round-report) — and if the
    reconciliation produced fixes, the next round begins at step 1.

**Two integrations per round, both before the push.** The first makes the work
current; the second closes the window the fix itself opened, which can be an
hour wide and several merges deep. After the push, base movement does not
reopen the candidate — that is invariant 3, and it is what stops the cycle from
running forever.

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

The user may direct that a round run in parallel with CI, waiving the green
`ci-required` requirement in the rows above; see [Standing
adjustments](#standing-adjustments).

The head lock ends when the round closes: reconciled, current-head
zero-conflict evidence confirmed, and every required current-head check and
concurrent local gate succeeded. A superseded attempt is the other exit — it
releases the lock through recovery, spends no round number, and never reaches
closure. The fixed replacement is a new candidate and its review is a new round.

#### Review-clean, and what it gates

A fixed-head review is **review-clean** when its public reconciliation leaves no
finding unresolved **and the reviewed head did not move in response to that
round**. A justified dismissal counts as a resolution only when the reason is
recorded publicly.

Three consequences follow, and they are the ones most often missed:

- A round that pushes a fix is *complete* but not review-clean. Only the
  replacement head can earn that status, which means a fix-producing round
  always implies at least one more round.
- Merge readiness requires a review-clean review **at the current head**. An
  author who wants to stop while the last round pushed a fix is asking for a
  waiver, not making a judgment call. Ask for it explicitly. An approved
  carry-forward integration is the one move that *transfers* review-clean status
  to a head no reviewer saw; nothing else does.
- A review-clean round ends adversarial review. Do not move the head merely to
  buy another pass.

The report classification `clean` is narrower than review-clean: use it only
when the reviewers returned no findings at all.

#### Recovery transitions

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
disposition each one publicly. Supersession never retires a finding.

### Forming a candidate

Adversarial review is scarce, but serial wall-clock time is also a cost. Spend
review only on a named frozen head with focused local evidence, then accept the
bounded risk that later CI may supersede it. A branch whose head is unpushed or
still moving, whose candidate was formed without integrating its effective base,
or whose PR has a known failure or conflict has no single answer to "what am I
reviewing?"

Form and freeze one candidate before the first round, and again before every
subsequent round:

- **The head is pushed, named, and settled.** Reviewers get an exact base and
  head, not a branch that moves under them. Finish your own edits first, and do
  not push again until both reviewers have returned and their feedback has been
  reconciled. A confirmed merge conflict or failing required gate that requires
  an author change is the exception: it supersedes the incomplete attempt and
  releases the lock. Conflict recovery pushes immediately; failed-gate recovery
  pushes the fix and waits for the ordinary subsequent-round status gate.
- **The candidate includes its effective base.** Integrate twice, per [the round
  cycle](#the-round-cycle): once before fixing, once after, recording the tip
  you finally integrated. That head is the candidate — keep it fixed through
  push, CI, and review. Do **not** refetch once it is pushed; that restarts the
  cycle without making the review more useful.
- **Re-integrate whenever the head moves.** When a conflict, an author change,
  or a review finding ends a candidate, the replacement is formed by running the
  cycle again. A multi-round PR therefore picks up `main` on each round that
  produced a fix — not never, and not continuously while a head is frozen.
- **Before merge, the PR is mergeable and green.** That means four things at
  once: the returned head is the pushed head, the PR is not a draft,
  mergeability is positive, and the current head's `ci-required` completed with
  a `SUCCESS` conclusion. A `BLOCKED` or `DRAFT` merge state is an independent
  readiness blocker — clear it before posting `Ready to merge`. One status check
  answers all of it; see [status
  discovery](docs/round-orchestration.md#status-discovery) for the REST default,
  when GraphQL is worth a point, the traps each result carries, and the polling
  cadence. The first attempt at round 1 and conflict-recovery rounds do not wait
  for this result; a failed-gate restart, an ordinary subsequent round, and merge
  readiness do.
- **Every PR in a stack meets the applicable conditions**, not only the slice
  under review. A known-conflicted or known-red parent blocks review of
  everything above it. A pending parent does not block a slice's first or
  conflict-recovery round, provided each layer has a settled pushed head and
  passed focused local evidence. A conflict-recovery round is scoped to the
  recovered slice; do not review an upper slice until its own conflicts are
  recovered. Before any ordinary subsequent round, a current-head aggregate
  check for every open layer must confirm zero conflicts and green
  `ci-required`. A slice rebases onto its parent, never onto `main`: only the
  bottom open slice takes `origin/main` as its base, and rebasing an upper slice
  onto `main` pulls in work its parent has not landed and makes the slice's diff
  report its parent's changes as its own. `ci.yml` applies no base-branch
  filter, so every non-documentation slice schedules the same CI wherever it
  targets; a non-documentation slice reporting *no* checks is therefore not
  green. Re-query after the registration window, following the status-discovery
  cadence, and verify the current head; if no matching workflow run appears,
  that is a scheduling bug to investigate — a PR that triggers no workflow
  leaves `ci-required` nothing to block on and displays as MERGEABLE and CLEAN.

Once the candidate is pushed, do not fetch or integrate the base while CI or
review is in progress. After a review-clean result, a non-mutating fetch is
permitted solely to inspect the landed range for the carry-forward decision
below.

### Clean reviews are not spent by main moving

For a PR that targets `main` — including the bottom open slice of a stack —
when its required review is review-clean at the current head and `origin/main`
has since moved, **stop and ask.** Do not integrate, and do not open another
round on your own initiative.

**This path does not apply to an upper stack slice.** Its effective base is its
parent branch, so `origin/main` moving is not base movement for it, and the
carry-forward procedure would compare against the parent instead. When a parent
does move, that is a restack, and a restack requires a review-clean round at the
resulting head.

Absent an actual conflict, a round that produces no review-driven fix and then
integrates newer `main` to create another round is a failed round: the review
produced no change and the integration discarded the value of the locked-head
result.

The user may then approve carrying the clean reviews forward across a
non-interacting base integration, without another round. **The integrated head
inherits the review-clean status**, and is merge-ready on that basis — that
transfer is the whole point, and without it the integration would strand the PR
at a head no review covers. It rests on the approved analysis, not on the merge
being mechanical: the reviews carry because the landed range was shown not to
interact and the user accepted that finding.

Carrying forward is the sole default path that integrates the base when no
conflict, review-driven fix, author change, current-head merge-path failure,
required cascading restack, or explicit user workflow adjustment has ended the
candidate.

**Carry forward only a non-interacting range.** If the analyzed range cannot
affect the change and the user approves, integrate it and carry the clean
reviews without another round; if the live tip moves before you integrate,
analyze the additional range and obtain renewed approval. A decline keeps the
reviewed head and leaves the PR blocked there. **If it can affect the
change**, carry-forward is unavailable: keep the reviewed head, say the PR is
not merge-ready, and ask whether to adjust the workflow to integrate,
re-validate, and re-review the replacement head, or to leave the PR blocked.
Approving that adjustment buys a **re-review**, never a carried one. Declining
it leaves the PR blocked at the reviewed head. Do not re-ask either decline.

**Repeat it whenever the base moves again**; a carried-forward head is
review-clean, and each pass needs its own analysis and its own approval. **A
failure in the post-integration validation or CI ends the candidate**: it is a
current-head merge-path failure, so the reviews do not carry, the fix is an
author change, and the replacement head owes a normal round. Carry-forward
transfers a clean result across an integration; it does not survive that
integration going wrong.

The procedure, and the analysis to bring to the user, are in
[carry-forward after clean reviews](docs/round-orchestration.md#carry-forward-after-clean-reviews).

Evaluate eligibility from the *latest* review-clean result: an earlier finding
that was fixed and then reviewed clean does not disqualify it. Carry-forward
does not apply, and the head must be reviewed normally, when a finding remains
unresolved, or when the head moved after that result because of an author
change, conflict resolution, or a restack.

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
Two reviewers in the same round count as one round, not two.

### Running the round

A round starts when its reviewers are dispatched. It ends when all current and
carried feedback is publicly reconciled, every resulting fix is committed and
pushed, current-head zero-conflict evidence is confirmed, and every required
current-head check and post-push local gate for that round has completed
successfully.

Every reviewer gets the same self-contained prompt and its own isolated
worktree; findings are reproduced before they are acted on and reconciled
publicly on the PR. Address actionable findings only after the locked-head
reviews finish. See
[running a round](docs/round-orchestration.md#running-a-round) for dispatch,
reconciliation, and the required round report.

Review the whole head. An author may not declare a subsystem out of scope for a
round — including a test harness the previous rounds have already hardened —
without explicit user approval, because a round narrowed by the author is not
evidence about the head.

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

Before requesting each block, present an analysis of why the prior rounds did
not converge. Classify the repeated findings as one of:

- an architectural problem in the change;
- missing test coverage;
- reviewers expanding the contract beyond the intended threat model; or
- **findings confined to the change's own test harness** while the product diff
  goes unchallenged.

State the proposed architectural or test remedy, or explain why the remaining
concern should be dismissed, before spending another block.

The fourth case deserves its own judgment, because it looks like convergence and
behaves like a ratchet. When successive rounds find only new ways to strengthen
a test generator, each finding is real and each fix is cheap, so the loop can run
indefinitely on a product diff nobody has disputed. Say so plainly when you see
it: report how many consecutive rounds produced no product finding, and
recommend either a final round or stopping. Stopping still needs the user's
waiver under invariant 5 — but asking for one, with that evidence, is the
correct move rather than opening another round by reflex.

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
- Check PR state through [status
  discovery](docs/round-orchestration.md#status-discovery) — REST by default,
  GraphQL when breadth pays for the point, and a scheduled check rather than a
  watch loop. That applies to any PR, not only one under review.
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
  is not merge authorization. Arming auto-merge at the user's direction is, for
  the reviewed head only — see [Standing
  adjustments](#standing-adjustments). Asking whether to arm it is always
  permitted.
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
