# Agent Instructions

## Repository map

Read this file first, then use the docs it points to:

- `README.md`: human and agent entrypoint for capabilities, commands, and common examples.
- `docs/overview.md`: minimum system/architecture context for this repo.
- `docs/architecture.md`: deeper architecture and command model details.
- `docs/design/`: focused design notes for rendering, sections, schemas, version resolution, and related systems.
- `taste/skill-guidance.md`: examples and rules for maintaining `skills/dotnet-inspect/SKILL.md`.
- `skills/dotnet-inspect/SKILL.md`: embedded agent skill printed by `dotnet-inspect skill`; keep it workflow-focused, current, and ideally under 100 lines.

Keep this file as a resolver plus essential repo workflow rules. Put detailed architecture and taste guidance in docs instead of expanding `AGENTS.md`.

## Current priorities

For decompiler raise, scorecard, ledger, adversarial fixture, or predicate work,
read `docs/decompiler-quality.md` first, then
`docs/design/decompiler-substrate.md`.
Use `docs/decompiler-correctness-pipeline.md` to choose the right test/harness
"boss" for a decompiler PR and to report the expected evidence.

The current decompiler priority is high-value hardening:

- Treat a near-full scorecard as a signal to do more adversarial passes over
  recent or broad raises.
- When the obvious breadth-hunt queue dries up, switch to stabilization,
  curation, measured validity/corpus bugs, or one explicitly scoped large climb;
  do not keep opening tiny overlapping raise PRs for motion.
- Sharpen `Partial` ledger rows instead of adding easy scorecard rows.
- Run curator passes when uncoordinated agents land raises: keep scorecard
  entries positive-only, sidecar coverage current, and adversarial fixtures
  clearly negative.
- Use `docs/decompiler-burndown-curator.md` for burndown queue hygiene: stale
  rows, merged PR state, merge conflicts, CI breaks, rebaseline triggers, and
  safe subagent delegation. Claimed burndown rows are hot-start work: drive to a
  PR, explicit blocker, or pivot issue.
- Use PR-intent-informed adversarial reviewer passes for recent or broad raises:
  reconstruct the raise claim from the PR and current code, then falsify the
  discriminator with near-miss negative fixtures.
- Use stepper semantic audits for ECMA/pipeline-contract concerns: walk specimens
  through `--dump --steps --diff --cfg --facts --remarks` to find the first
  illegal intermediate rewrite.
- Keep the product decompiler path SRM-only, NativeAOT-friendly, Roslyn-free,
  and free of inspected-assembly loading.
- Use **decompiler substrate** for shared pass-evidence layers and
  **identity predicates** for exact rewrite gates; avoid **fact substrate**.

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

Build the main project:

```bash
dotnet build src/dotnet-inspect -c Release
```

**IMPORTANT: Tests use xunit v3 with `OutputType Exe`. You MUST use `dotnet run`, NOT `dotnet test`. Using `dotnet test` will silently produce no output.**

```bash
dotnet run --project src/dotnet-inspect.Tests -c Release
dotnet run --project src/ILInspector.Analysis.Tests -c Release
dotnet run --project src/ILInspector.Decompiler.Tests -c Release
dotnet run --project src/DotnetInspector.Services.Tests -c Release
dotnet run --project tests/ILInspector.Metadata.Tests -c Release
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

For decompiler-affecting PRs, follow this evidence and review contract:

- Documentation-only PRs that do not claim new measured behavior may stop at
  markdownlint; state that the change is docs-only.
- Include the tool-generated quality-diff card. Paste harness output; do not
  hand-construct aggregate tables.
- For risky behavior changes (raise/structuring/printer semantics), include a
  per-method delta artifact and changed-method fidelity result, or state
  explicitly that changed methods are not currently checkable.
- Add targeted improved examples and still-flat near misses for behavior changes.
- Do not paste raw `--dump --steps` walls into PR bodies; link drill-down
  artifacts when needed.
- Request adversarial review from another model family at the end, or earlier if
  progress slows. Current default pairing: GPT-5.5 should request Opus 4.8, and
  Opus 4.8 should request GPT-5.5. Other models should pick the strongest
  available reviewer from a different model family.
- It is fine to open the PR before the final adversarial review.
- Always post a PR comment summarizing the adversarial review result and any
  follow-up changes or explicit non-actions.

See `docs/decompiler-quality.md` and `tools/DecompilerHarness/README.md` for the
broader workflow and command details.

Some tests in `dotnet-inspect.Tests` require `ilasm`/`ildasm` and will skip if not installed.

`DotnetInspector.ILRoundtrip.Tests` requires the vendored managed ILAssembler
(orphan branch `vendor/ilassembler`); run `eng/restore-ilassembler.sh` once to
materialize it at `external/ILAssembler`. Edits under `external/ILAssembler`
commit directly to the vendor branch — see its README for the fork policy.

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

After fetching, rebasing, merging `origin/main`, or resolving conflicts from
main, re-read `AGENTS.md` and any task-relevant docs it points to before
continuing. If instructions changed, treat the refreshed instructions as
authoritative and adjust PR evidence/status accordingly.

Create feature branches with descriptive names, e.g.:
- `feature/issue-3-assembly-references`
- `fix/null-reference-in-parser`

## Markdown Linting

All markdown files must pass `markdownlint` before committing. When there are lint errors, run the auto-fixer first:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

Run `markdownlint` on all changed markdown files as part of preparing a PR.
