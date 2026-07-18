# Agent Instructions

## Start here

`dotnet-inspect` is a general .NET inspection tool spanning packages, restored
projects, platform libraries, metadata, APIs, dependencies, source provenance,
analysis, Findings, implementation diffs, and decompilation.

Read:

1. `README.md` for current capabilities, commands, and examples.
2. `docs/overview.md` for the minimum system and ownership model.
3. `docs/architecture.md` for command, source, and evidence architecture.
4. The task-specific docs below before changing that area.

Keep this file to repository-wide engineering and workflow rules. Detailed
design, subsystem mechanics, and historical context belong in `docs/`, tool
READMEs, and focused skills.

## Task-specific guidance

| Area | Read first |
| --- | --- |
| Commands, sections, and output | `docs/design/progressive-disclosure.md`, `docs/design/output-shapes.md`, `docs/design/style-guide.md`, `docs/design/section-model.md` |
| Metadata, source, and acquisition | `docs/design/assembly-inspection-query.md`, `docs/design/source-finding-producers.md`, `docs/pdb-acquisition.md`, `docs/design/version-resolution.md`, `docs/design/cache-concurrency.md` |
| Security and untrusted input | `docs/design/untrusted-data-threat-model.md` |
| Analysis, Findings, and Research | `docs/design/finding-nomenclature.md`, `docs/design/finding-producers.md`, `docs/design/finding-adoption.md`, `docs/design/finding-coordinates.md`, `docs/design/analysis-ux-scopes.md` |
| Shared IL/control-flow substrate | `docs/design/instruction-substrate.md`, plus the consuming subsystem's docs |
| Decompiler behavior or harnesses | `docs/decompiler-quality.md`, `docs/decompiler-correctness-pipeline.md`, `docs/decompiler-raise-discipline.md`, `docs/design/decompiler-substrate.md`, `tools/DecompilerHarness/README.md` |
| Skills | `taste/skill-guidance.md`, `skills/dotnet-inspect/SKILL.md`, and the relevant `skills/<scenario>/SKILL.md` |
| Release and publishing | `docs/release-workflow.md` |

Decompiler PR templates:

| Change | Template |
| --- | --- |
| Raising, structuring, validity, fidelity, or corpus behavior | `docs/templates/decompiler-pr.md` |
| Focused invalid-`Full` or burndown row fix | `docs/templates/decompiler-burndown-fix-pr.md` |
| Compile-back harness, fidelity skeleton, or ReturnToSender coverage | `docs/templates/decompiler-compile-back-harness-pr.md` |

Some files under `docs/design/` record proposals or design history. Prefer
current behavior in `README.md`, `docs/overview.md`, `docs/architecture.md`,
focused current docs, and tests when sources disagree.

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

A file-based app can reference a project directly:

```csharp
#:project ../src/MyLib/MyLib.csproj

using MyLib.Domain;

var items = await MyService.LoadAsync();
Console.WriteLine($"Found {items.Count} items");
```

## Building and testing

Repository development uses a .NET 11 daily SDK. Before installing an SDK or
changing `PATH`, inspect the current selection:

```bash
command -v dotnet
dotnet --version
```

If `dotnet` already resolves to a dotnetup-managed .NET 11 daily SDK, use normal
`dotnet` commands. If it is centrally installed (for example under `/usr/bin`,
`/usr/local/share/dotnet`, `/snap`, or `C:\Program Files\dotnet`), stop and ask
before installing, replacing, or shadowing it.

Use dotnetup for non-invasive user-level acquisition when approved:

```bash
curl -fsSL --retry 3 https://aka.ms/dotnetup/get-dotnetup.sh -o /tmp/get-dotnetup.sh
bash /tmp/get-dotnetup.sh --install-dir "$HOME/.local/bin"
dotnetup sdk install 11.0-daily --interactive false
```

Use `dotnetup dotnet ...` for command isolation, or evaluate its environment
script for one shell. Do not modify shell startup files unless explicitly
requested.

Build the normal product, test, and fixture graph with:

```bash
dotnet build dotnet-inspect.slnx -c Release
```

Tests use xUnit v3 executable projects. **Use `dotnet run`, not `dotnet test`**;
`dotnet test` silently executes no tests here.

| Area | Command |
| --- | --- |
| CLI and product output | `dotnet run --project src/dotnet-inspect.Tests -c Release` |
| Analysis | `dotnet run --project src/ILInspector.Analysis.Tests -c Release` |
| Decompiler | `dotnet run --project src/ILInspector.Decompiler.Tests -c Release` |
| Shared services | `dotnet run --project src/DotnetInspector.Services.Tests -c Release` |
| Metadata | `dotnet run --project tests/ILInspector.Metadata.Tests -c Release` |

Some CLI tests require `ilasm`/`ildasm` and skip when those tools are absent.
`DotnetInspector.ILRoundtrip.Tests` is outside the default solution and requires
the vendored managed ILAssembler. Run `eng/restore-ilassembler.sh` before:

```bash
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release -- \
  -trait- "Speed=Slow"
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
```

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

## Git and worktrees

- `main` is protected. Work on a descriptive feature or fix branch.
- Development must happen in a worktree. A fresh worktree per PR and a reused
  development worktree are both valid.
- Start each new change from the latest `origin/main`.
- Never amend commits; create follow-up commits.
- Before opening a PR, fetch `origin/main`, update the feature branch by merge
  or rebase, resolve conflicts locally, and rerun relevant checks.
- After updating from main or resolving conflicts, re-read `AGENTS.md` and
  task-relevant docs before continuing.
- Do not mix unrelated changes into one commit or sweep another contributor's
  working-tree changes into your work.

When all merge-blocking validation, CI, and required review are complete, post
a PR comment that says `Ready to merge`. Label later work as non-blocking
follow-up so readiness remains unambiguous.

## Adversarial review

Any PR with non-trivial behavior changes, new heuristics or shapes, or subtle
correctness, security, or compatibility risk requires adversarial review from
two different models chosen from:

- Claude Opus (for example Claude Opus 4.8)
- Gemini Pro (for example Gemini 3.1 Pro)
- GPT (for example GPT 5.6 Sol)
- MAI-Code (for example MAI-Code-1-Flash)

This list is the single source of truth for the reviewer roster; scenario docs
should reference it rather than restating it.

Do not review with your own model when another listed family is available. A
single-model agent that cannot delegate may use independent passes from its own
model.

Give both reviewers the same self-contained prompt: exact base and head, design
intent, relevant diff, concrete attack points, and required real-run evidence.
Isolate every reviewer in a separate checkout or worktree. Require scratch work
under `/tmp/` and prohibit `git reset`, `git add`, and commits in review trees.
Before acting on a blocking finding, reproduce it on a clean exact-head
checkout.

After addressing findings, re-review the fixed exact head. Reconcile both
reviews publicly on the PR: attribute findings, state what was verified or
dismissed, and link resolution commits or explain explicit non-actions. Do not
mark the PR ready until both fixed-head reviews are clean.

Simple, mechanical, or documentation-only changes do not require adversarial
review; state why the change is low risk. If the blast radius is uncertain,
default to review.

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
