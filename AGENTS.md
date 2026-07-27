# Agent instructions

## Start here

`dotnet-inspect` is a general .NET inspection tool spanning packages, restored
projects, platform libraries, metadata, APIs, dependencies, source provenance,
analysis, Findings, implementation diffs, and decompilation.

Read this file before doing work. Then read only the task-specific entry
documents relevant to the change:

- Read `README.md` when changing user-visible capabilities, commands, or
  examples.
- Read `docs/overview.md` when a change crosses subsystem ownership boundaries.
- Read the relevant section of `docs/architecture.md` only when implementation
  structure matters to the task.
- Follow the task-specific entry points below and then only the links relevant
  to the change.

This file is the source of truth for repository-wide engineering and workflow
rules. Detailed design, subsystem mechanics, version requirements, and
historical context belong with their owning code, workflow, or focused
documentation.

## Before changing files

- `main` is protected. Keep the primary repository checkout attached to
  `main`; never detach its HEAD or develop in it.
- Before starting a change, run `git fetch origin main` from the primary
  checkout, then create a descriptive branch and linked worktree with
  `git worktree add -b <branch> <path> origin/main`. Make all edits, builds,
  tests, and commits in the worktree, not the primary checkout.
- Use one development worktree per PR, plus temporary worktrees for independent
  reviews. Do not reuse a worktree across unrelated changes.
- Never amend commits; create follow-up commits.
- Before requesting review, fetch `origin/main` and incorporate it into the
  feature branch. Rebase only before the branch's first push. Once a branch is
  public or under review, merge `origin/main`; never amend, rebase, or
  force-push reviewed history.
- After updating from main or resolving conflicts, re-read `AGENTS.md` and
  task-relevant docs before continuing.
- Do not mix unrelated changes into one commit or sweep another contributor's
  working-tree changes into your work.
- Treat worktrees as temporary. For a PR requiring adversarial review, confirm
  the exact reviewed head is pushed, then remove the development and review
  worktrees with `git worktree remove <path>` as soon as every required
  fixed-head review is clean. For a change that does not require adversarial review,
  remove its development worktree after merge. Do not retain inactive
  worktrees in case more work appears; recreate one for the branch if follow-up
  work is needed.

## Task-specific guidance

| Area | Read first |
| --- | --- |
| Command defaults and disclosure | `docs/design/progressive-disclosure.md` |
| Output data shapes | `docs/design/output-shapes.md` |
| Output style | `docs/design/style-guide.md` |
| Sections and selection | `docs/design/section-model.md` |
| Metadata and API inspection | `docs/design/assembly-inspection-query.md` |
| PDB and source acquisition | `docs/pdb-acquisition.md` |
| Source Finding producers | `docs/design/source-finding-producers.md` |
| Package resolution and caches | `docs/design/version-resolution.md` |
| Security and untrusted input | `docs/design/untrusted-data-threat-model.md` |
| Analysis, Findings, and Research | `docs/design/finding-adoption.md` |
| Call-graph Mermaid projection | `docs/design/call-graph-mermaid-projection.md` |
| Shared IL/control-flow substrate | `docs/design/instruction-substrate.md`, plus the consuming subsystem's docs |
| IL round-trip tests | `tests/DotnetInspector.ILRoundtrip.Tests/README.md` |
| Decompiler behavior or harnesses | `docs/decompiler-correctness-pipeline.md` |
| Skills | `taste/skill-guidance.md` |
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

When adding a focused skill, register it in `SkillCommand.Skills`. Its YAML
frontmatter `description:` is the single source of truth for the generated
skill listing.

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
- For corpus or performance claims, record the pinned input, command, baseline,
  and result. Static analysis proves structural evidence, not runtime heat,
  frequency, bytes, or impact; use a benchmark or profiler for runtime claims.
- Documentation-only changes that make no measured behavior claim require
  Markdown validation, not product builds or tests.

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

## File-based apps

Do not use `dotnet-script`, `dotnet script`, `dotnet-fsi`, or `.csx` files.
Prefer .NET file-based apps for throwaway probes unless a specific Python
library is needed. Write probes under `/tmp/` and run them with:

```bash
dotnet run /tmp/check.cs
```

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

Run the suite in **Release** for input fidelity, not speed: the optimized IL a
Release build of the compilers emits is what ships and what the decompiler
corpus consumes, so a Debug run would validate the decompiler against IL shapes
users never see. Because the suite runs Release, correctness checks must not
hide behind `[Conditional("DEBUG")]` — such a call is stripped from the Release
test assembly and asserts nothing. The IR invariant check
(`IrNode.CheckInvariant`) is instead a runtime flag (`IrInvariants.Enabled`,
env var `DOTNET_INSPECT_IR_INVARIANTS`) that is **on by default**, so any host
that runs the pipeline — test suite, harness, sweep, benchmark — validates it
after every pass in the same build users run. The shipped CLI is the one
sanctioned opt-out (`IrInvariants.DisableForShippedTool()` in
`src/dotnet-inspect/Program.cs`), so the tool pays nothing on the decompile hot
path; `IrInvariantsHostContractTests` pins that call site so a new host cannot
quietly decline validation. An explicit `DOTNET_INSPECT_IR_INVARIANTS` value
outranks the opt-out in both directions.

The invariant check is **leveled**, because the two levels need different
inputs to be sound:

- **Structural** invariants (parent/child back-pointer consistency, tree
  shape) hold on *any* well-formed `IrNode` graph, including the deliberately
  minimal `IrFunction`s that hand-built pass-unit fixtures construct. These are
  the default level every host gets (`IrInvariants.Enabled`).
- **Semantic** invariants (e.g. local-slot indices within the enclosing
  function/lambda's `Locals`) hold on *real importer output* but not on minimal
  fixtures, which routinely reference slots without populating `Locals`. These
  are a separate opt-in (`IrInvariants.CheckSemantics`,
  `DOTNET_INSPECT_IR_INVARIANTS=full`) so they run over the corpus (harness
  `--gaps`, Speed=Slow gates), where the input is well-formed, without
  false-positiving the minimal-fixture suite. `CheckInvariant(includeSemantics:
  true)` threads the level explicitly for hermetic per-test coverage.

Some CLI tests require `ilasm`/`ildasm` and skip when those tools are absent.
The IL round-trip project has separate dependency restore and fast/full test
commands; follow `tests/DotnetInspector.ILRoundtrip.Tests/README.md`.

`ILInspector.Decompiler.Tests` carries two orthogonal `[Trait]` dimensions you
can compose with xUnit's `-trait`/`-trait-` filters:

- `Speed` (`Slow` marks the expensive corpus/fidelity/compile-back gates). Drop
  them with `-trait- "Speed=Slow"`.
- `Area` groups a functional slice — `RoundTrip`, `Fidelity`, `Corpus`,
  `Validity`, and `Pass` — so you can run one area's tests (including its slow
  gates) without every other area's slow gates.

```bash
# every RoundTrip test, fast and slow (includes its slow compile-back gates):
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait "Area=RoundTrip"
# narrow to the fast RoundTrip tests only:
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait "Area=RoundTrip" -trait- "Speed=Slow"
```

The `Area` taxonomy and how classes map to it live with the decompiler test
docket in `docs/decompiler-correctness-pipeline.md`.

The executable also accepts a discoverable `--gate <preset>` flag that expands
to these trait filters (e.g. `--gate fast`, `--gate no-corpus`); run
`--gate list` for the table.

Pack and publish flows remain separate and build `src/dotnet-inspect`
directly.

## Output contract

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

When all merge-blocking validation, CI, and required review are complete, post
a PR comment that says `Ready to merge`. Label later work as non-blocking
follow-up so readiness remains unambiguous.

## Adversarial review

**These instructions assume a harness — such as the GitHub Copilot CLI — that can
delegate a review to any model family in the roster below.** The multi-model tiers
depend on that ability. Most harnesses do not expose multiple model families; a
harness that only exposes its own vendor's models handles review differently (see
*Single-vendor harnesses* below).

**How much review a PR needs is a function of its triviality and risk alone —
never the kind of change it makes.** Place the PR on that spectrum and match the
review depth to it:

- **Trivial** — no review. State why the change is trivial.
- **More than trivial, but not high risk** — a single review, always with
  **MAI-Code**.
- **High risk** (subtle correctness, security, or compatibility risk, or a large
  or uncertain blast radius) — the default for any substantial change — two
  reviews from two different models.

If you are unsure which tier a PR falls in, escalate: default to more review, not
less.

Reviewer roster:

- Claude Opus
- Gemini Pro
- GPT
- MAI-Code

This list is the single source of truth for the reviewer roster; scenario docs
should reference it rather than restating it. **Always use the highest version a
model offers** — e.g. if both Opus 4.8 and Opus 5 are available, use Opus 5.

For a two-model review, do not review with your own model when another listed
family is available.

**Single-vendor harnesses.** The tiers above set how many reviews and which models
a PR requires; a harness's capabilities change only *how* those reviews are
obtained, not the bar. Most harnesses expose only their own vendor's models — for
example Claude Code or Codex. For the **single-review tier**, such a harness just
reviews with its own model (for example an Opus subagent under Claude Code); that
one review satisfies the tier — no MAI-Code or other cross-model review is
additionally required. The cross-model requirement applies only to the **two-model
tier**: there the harness reviews with its own model (independent passes on the
fixed head), then **requests a second, different-family review from the user**, and
does **not** mark the PR ready until that different-model review is obtained.

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

## PR and CI discipline

- Prefer fewer coherent PRs over many small PRs that each pay fixed CI cost and
  increase merge contention.
- Keep concurrent agents modest and avoid unnecessary churn in central files.
- Treat CI as confirmation, not discovery: run relevant local checks first.
- Do not broaden CI without a measured need. The PR `test` job validates the
  merge path; `pack` is path-gated; release artifacts are built by
  `release.yml`.
- Keep PR summaries conclusion-first. Include the behavioral claim, evidence,
  compatibility or non-action boundary, and exact validation appropriate to
  the change.

## Markdown

All changed Markdown must pass `markdownlint`. Run the fixer first when needed:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```
