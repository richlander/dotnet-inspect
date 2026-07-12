# Agent Instructions

## Repository map

Read this file first, then use the docs it points to:

- `README.md`: human and agent entrypoint for capabilities, commands, and common examples.
- `docs/overview.md`: minimum system/architecture context for this repo.
- `docs/architecture.md`: deeper architecture and command model details.
- `docs/design/`: focused design notes for rendering, sections, schemas, version resolution, and related systems.
- `taste/skill-guidance.md`: examples and rules for maintaining `skills/dotnet-inspect/SKILL.md`.
- `skills/dotnet-inspect/SKILL.md`: the base agent skill printed by `dotnet-inspect skill`; a tight router (kept at/under 50 lines) that ends with a generated list of focused skills.
- `skills/<scenario>/SKILL.md`: focused scenario sub-skills printed by `dotnet-inspect skill <name>` (e.g. `query`, `compatibility`, `signals`). Register each in `SkillCommand.Skills`; its one-line description comes from the YAML frontmatter `description:` (the single source of truth).

Keep this file as a resolver plus essential repo workflow rules. Put detailed architecture and taste guidance in docs instead of expanding `AGENTS.md`.

## Product constraints

Keep the product path SRM-only, NativeAOT-friendly, Roslyn-free, and free of
inspected-assembly loading. This applies to every command, not just the
decompiler.

## Decompiler and analysis work

dotnet-inspect is a general assembly and package inspection tool; the decompiler
and analysis paths are one workstream among several, not the whole repo. That
work does carry its own deep discipline — when doing decompiler raise,
adversarial-fixture, or predicate work, start with these docs and follow them
over any summary here:

- `docs/decompiler-quality.md` and `docs/decompiler-correctness-pipeline.md` —
  overall quality workflow and the correctness-pipeline stages/evidence (use the
  latter to name the highest relevant proof "boss").
- `docs/decompiler-raise-discipline.md` — evidence, typing, scoping, and
  annotation rules for raise/typing/emission changes. Non-negotiable there:
  render-text A/B against an explicit merge-base ref, no claimed win before the
  A/B lands, and a sibling-rule grep after every rule fix.
- `docs/design/decompiler-substrate.md` — read before adding or changing shared
  rewrite-gate predicates. Use **decompiler substrate** for shared pass-evidence
  layers and **identity predicates** for exact gates; avoid **fact substrate**.

Prefer high-value hardening (measured correctness/validity bugs, adversarial
passes over recent or broad raises) over easy changes made just for motion. Use
PR-intent-informed adversarial reviews for recent or broad raises: reconstruct
the raise claim, then falsify the discriminator with near-miss negative fixtures.
For ECMA/pipeline-contract concerns, run a stepper semantic audit
(`--dump --steps --diff --cfg --facts --remarks`) to find the first illegal
intermediate rewrite.

## File-Based Apps

Do NOT use `dotnet-script`, `dotnet script`, `dotnet-fsi`, or `.csx` files. Always use file-based apps (new in .NET 10). Always prefer file-based apps over Python, unless a specific Python library is needed.

Run with `dotnet run /tmp/check.cs`. Write throwaway scripts to `/tmp/`.

Reference: <https://raw.githubusercontent.com/dotnet/docs/refs/heads/main/docs/core/sdk/file-based-apps.md>

### File-based app with project reference

```csharp
#:project ../src/MyLib/MyLib.csproj

using MyLib.Domain;

var items = await MyService.LoadAsync();
Console.WriteLine($"Found {items.Count} items");
```

## Building and Testing

Repository development tracks .NET 11 daily SDKs so the repo can follow
compiler-produced shapes (which matters most for decompiler work) before monthly
previews. Published tool users are not affected by this repo-development
requirement.

Before installing an SDK or changing PATH, inspect the current `dotnet`:

```bash
command -v dotnet
dotnet --version
```

If `dotnet` already resolves to a dotnetup-managed .NET 11 daily SDK, use normal
`dotnet` commands for this repo. Do not wrap those commands in `dotnetup dotnet`
unless you need to force an isolated dotnetup install.

If `dotnet` is centrally installed (for example under `/usr/bin`,
`/usr/local/share/dotnet`, `/snap`, or `C:\Program Files\dotnet`), stop and ask
for guidance before installing an additional user-level SDK or changing shell
configuration. Do not remove, shadow, or replace a centrally managed install
unless the user explicitly approves it.

Use `dotnetup` for non-invasive local SDK acquisition:

```bash
curl -fsSL --retry 3 https://aka.ms/dotnetup/get-dotnetup.sh -o /tmp/get-dotnetup.sh
bash /tmp/get-dotnetup.sh --install-dir "$HOME/.local/bin"
dotnetup sdk install 11.0-daily --interactive false
```

When the default `dotnet` for commands run from this repository is not the
dotnetup-managed .NET 11 daily SDK, and the user has approved a user-level
dotnetup install, run repo commands through dotnetup so the nightly SDK is
selected only for that command:

```bash
dotnetup dotnet build dotnet-inspect.slnx -c Release
dotnetup dotnet run --project src/ILInspector.Decompiler.Tests -c Release
```

For a temporary shell/process override, evaluate dotnetup's supported
environment script before running repo commands:

```bash
eval "$(dotnetup print-env-script --shell bash)"
dotnet build dotnet-inspect.slnx -c Release
```

That affects only the current shell process and its children. Do not write this
line to startup files such as `.bashrc`, `.profile`, or `.zshrc` unless the user
explicitly asks for a persistent shell change.

Verify the selected `dotnet --version` reports the dotnetup-managed daily SDK
before building.

Build the normal product/test/fixture graph:

```bash
dotnet build dotnet-inspect.slnx -c Release
```

Pack and publish flows remain separate and continue to build/pack
`src/dotnet-inspect` directly. `DotnetInspector.ILRoundtrip.Tests` is not in the
default solution because it requires the vendored ILAssembler; run
`eng/restore-ilassembler.sh` before its targeted test command.

**IMPORTANT: Tests use xunit v3 with `OutputType Exe`. You MUST use `dotnet run`, NOT `dotnet test`. Using `dotnet test` will silently produce no output.**

```bash
dotnet run --project src/dotnet-inspect.Tests -c Release
dotnet run --project src/ILInspector.Analysis.Tests -c Release
dotnet run --project src/ILInspector.Decompiler.Tests -c Release
dotnet run --project src/DotnetInspector.Services.Tests -c Release
dotnet run --project tests/ILInspector.Metadata.Tests -c Release
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release -- -trait- "Speed=Slow"
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
```

For decompiler PRs, start with the
[Stage 0 entry-gate checklist](docs/decompiler-correctness-pipeline.md#entry-gate-checklist-stage-0):
build, focused xUnit executable tests, IR invariant checks, and markdownlint for
changed Markdown.

For decompiler work, expensive checks are a local-agent responsibility, not
something to defer to every PR CI run. Run the relevant heavy checks locally when
your change can affect structuring, fidelity, validity, or corpus behavior:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --quality-diff-card \
  --compile-cap 25 \
  --corpus-fidelity-cap 3 \
  --max-examples 3
```

For raise/printer-affecting changes, add the render-text A/B (pass
`--workers N` — parallelism is not the default):

```bash
git worktree add /tmp/ab-base --detach "$(git merge-base origin/main HEAD)"
dotnet run --project /tmp/ab-base/tools/DecompilerHarness -c Release -- \
  "${assemblies[@]}" --workers 20 --emit-render-ab /tmp/base.renderab
dotnet run --project tools/DecompilerHarness -c Release -- \
  "${assemblies[@]}" --workers 20 --render-ab /tmp/base.renderab
```

Classify every changed method; see `docs/decompiler-raise-discipline.md`.

For decompiler-affecting PRs, follow this evidence and review contract:

- Use `docs/templates/decompiler-pr.md` for general decompiler PR bodies,
  `docs/templates/decompiler-compile-back-harness-pr.md` for DecompilerHarness /
  ReturnToSender / compile-back coverage PRs, and
  `docs/templates/decompiler-burndown-fix-pr.md` for focused invalid-`Full`
  fixes. Keep the human summary terse and conclusion-first.
- Include the tool-generated quality-diff card; do not hand-construct or re-key
  metric tables. Use the matching corpus script/baseline pair documented in
  `tools/DecompilerHarness/README.md`. If a card has capped changed rows, link
  `docs/decompiler-corpus-delta-repro.md` rather than pasting dump walls.
- For risky behavior changes (raise/structuring/printer semantics), include
  targeted improved examples and still-flat near misses, plus changed-method
  fidelity evidence when the changed population is checkable. If not checkable,
  say that explicitly.
- Synthetic IR fixtures are useful for unreachable near misses, but identity or
  lowering-shape gates should also include a real importer/compiled-fixture
  canary when one exists.
- Request adversarial review per the [Adversarial Review](#adversarial-review)
  policy (two reviewers from a different model family than your own). It is fine
  to open the PR before reviews finish.
- Always post a PR comment or body update summarizing adversarial review results
  and any follow-up changes or explicit non-actions. Include links or commit refs
  for resolved guidance; state why no resolution commit applies for dismissed
  guidance.
- Documentation-only PRs that do not claim new measured behavior may stop at
  markdownlint; state that the change is docs-only.

See `docs/decompiler-quality.md` and `tools/DecompilerHarness/README.md` for the
broader workflow and command details.

Some tests in `dotnet-inspect.Tests` require `ilasm`/`ildasm` and will skip if not installed.

`DotnetInspector.ILRoundtrip.Tests` requires the vendored managed ILAssembler
(orphan branch `vendor/ilassembler`); run `eng/restore-ilassembler.sh` once to
materialize it at `external/ILAssembler`. Use `-- -trait- "Speed=Slow"` for the
fast PR subset; run the unfiltered command for the full assembly-wide sweep.
Edits under `external/ILAssembler` commit directly to the vendor branch — see its
README for the fork policy.

## Output Verbosity Contract

Commands that render sections should follow this verbosity model:

- `-v:q`: compact fields only; include high-value fields only.
- `-v:m`: one section only, plus an optional text line; include the high-value section only. The section must include all high-value fields and may include other fields.
- `-v:n`: multiple sections are allowed; include all sections that are not network-bound.
- `-v:d`: include all sections.

New sections must not appear in the default `-v:m` view unless they are the command's single high-value section. Focused flags such as `--audit` may explicitly select their section and promote verbosity as needed.

## Git Commits

Never amend commits. Always create new commits instead of using `git commit --amend`.

## Branching

The `main` branch is protected. All work must be done on a feature branch.

Development should always happen in a worktree. Both of the following workflows
are equally valid — neither is preferred or discouraged, so pick whichever suits
you:

- Create a fresh worktree per PR and destroy it when the PR is done.
- Re-use the same worktree across PRs.

Reusing a worktree across PRs is perfectly fine. Whichever you choose, the rule
is the same: use worktrees, and start every new feature even with `origin/main`
(fetch and base off the latest `origin/main`).

Before opening a PR, fetch `origin/main` and update the feature branch with the
latest main using either merge or rebase. Resolve conflicts locally and rerun
relevant checks so the PR does not start from a stale base.

When all merge-blocking validation, CI, and required review are complete, post a
clear PR comment that says the PR is `Ready to merge`. If you continue running
extra tests or review after that point, label them as non-blocking follow-up work
so maintainers do not mistake the PR for still being in progress.

After fetching, rebasing, merging `origin/main`, or resolving conflicts from
main, re-read `AGENTS.md` and any task-relevant docs it points to before
continuing. If instructions changed, treat the refreshed instructions as
authoritative and adjust PR evidence/status accordingly.

Create feature branches with descriptive names, e.g.:
- `feature/issue-3-assembly-references`
- `fix/null-reference-in-parser`

## Adversarial Review

Adversarial review is a **general requirement across the repo**, not a
decompiler-only one. Any PR with non-trivial behavior changes — new
heuristics or shapes, or subtle correctness, security, or compatibility risk, in
the decompiler, analysis, or any other area — must request adversarial review
from **two** different models, chosen from:

- Claude Opus 4.8
- GPT-5.5
- Gemini Pro (e.g. Gemini 3.1 Pro)
- the MAI Flash family (e.g. MAI-Code-1-Flash)

This list is the single source of truth for the reviewer roster; other scenario
docs reference this section rather than restating the models.

Rules:

- **Do not review with your own model.** If you are running as one of the models
  above, pick the two reviewers from the *other* entries. The only exception is a
  single-model agent (e.g. Claude Code or Codex) that cannot delegate to another
  model — it may use its own model, but should still use two independent review
  passes where possible.
- Give each reviewer the same self-contained, adversarial prompt (the PR's diff,
  the design intent, and concrete attack points) so their findings are
  comparable. Require evidence from real runs, not theorizing.
- **Isolate each reviewer's workspace.** Give every review agent its own
  checkout — a dedicated worktree or copy of the PR head — never a shared one.
  Reviewers routinely write scratch repro files, add probe tests, or run
  `git reset` to compare against a base; if two reviewers share a tree, one's
  uncommitted edits contaminate the other's view and produce phantom findings
  (e.g. a stray brace from a probe test read as a "PR compile error" that is not
  in the PR). Instruct reviewers to keep scratch files under `/tmp` and to avoid
  `git add -A`, `git commit`, and `git reset` in the review tree. Before acting
  on any blocking finding, reproduce it against a clean checkout of the PR head —
  `git status` should be empty — so a contaminated workspace cannot be mistaken
  for a real defect.
- It is fine to open the PR before the reviews finish.
- **Reconcile and surface the results on the PR**: post a PR review/comment
  summarizing both reviews (attributed by model), where they agreed or diverged,
  which findings you verified vs dismissed, and the follow-up changes or explicit
  non-actions. Include references or links to the commit(s) that resolved
  actionable review guidance; for dismissed or non-actioned guidance, state why
  no resolution commit applies. Do not merely summarize reviews back to the
  requester — they must be visible on the PR.
- Simple/docs-only PRs do not need this; state that the change is low-risk.
- If the work is very targeted (e.g. a one-line fix, a small localized
  refactor, or a mechanical change with an obvious, contained blast radius), it
  is fine to say so and ask whether the two-reviewer requirement really applies
  before spending it. The answer is usually yes, so default to running it unless
  a maintainer waives it.

## PR Strategy and CI Cost

GitHub Actions cost scales with PR volume, not PR size: every PR pays fixed
overhead (checkout, `setup-dotnet`, restore, the `changes` job), and many small
PRs from many concurrent agents also saturate the runner pool, queue jobs, and
raise merge contention on central files. Prefer **fewer, larger PRs** and keep
the number of **concurrent agents** modest.

To keep larger PRs from failing in CI, treat CI as a confirmation gate, not a
discovery tool:

- Run the relevant local checks first (build, focused tests, decompiler
  harness, `markdownlint`) so CI rarely surfaces something you could have caught
  locally. See [Building and Testing](#building-and-testing).
- Minimize central-file churn (e.g. `LoweringCoverage`, `CSharpPrinter`) to
  reduce conflicts when several PRs land together.
- Rebase on the latest `origin/main` before opening the PR.

CI is intentionally lean so this stays cheap. Do not re-expand it without a
reason:

- `test` runs on PRs only; push-to-main is not re-tested (the PR validates the
  merge commit; Deep Inspect and publish provide opt-in/full safety nets).
- `pack` runs only on PRs that change build/packaging config.
- Release packages are built at publish time in `release.yml`, so CI never
  produces release artifacts.

## Markdown Linting

All markdown files must pass `markdownlint` before committing. When there are lint errors, run the auto-fixer first:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

Run `markdownlint` on all changed markdown files as part of preparing a PR.
