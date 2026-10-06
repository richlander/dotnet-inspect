# Repository workflow

This document owns the repository-wide contributor rules that are needed after
launch but are not specific to one subsystem: worktrees and history,
documentation ownership, engineering constraints, local change hygiene, and PR
publication. [`AGENTS.md`](../AGENTS.md) is the bounded launch constitution;
focused documents own design, evidence, testing, review, release, and
subsystem-specific contracts.

## Worktrees and history

Keep the primary checkout attached to protected `main`; never develop there or
detach its HEAD. From that checkout, fetch the live base and create one linked
worktree per PR:

```bash
git fetch origin main
git worktree add -b <branch> <repo>/.worktrees/<slug> origin/main
```

A stacked slice branches from its parent. During a GitHub outage, use the
recorded last-known base permitted by [Stacked PRs](stacked-prs.md). Put
reviewer worktrees under `.worktrees/` or an OS temporary directory, never
directly under the home directory.

Never amend. Rebase only before the first push. After publication, merge the
effective base; never rebase or force-push reviewed history except while
restacking your own slices under the stack rules. Do not include unrelated or
another contributor's changes.

Remove reviewer worktrees after review and reproduction. Remove a development
worktree after merge, or once its pushed head is unlocked, concurrent gates
pass, and required reviews are review-clean; recreate it later when needed.

## Engineering constraints

- Keep product paths SRM-only, NativeAOT-friendly, Roslyn-free, free of
  inspected-assembly loading, and cross-platform by default. Browser/Wasm is a
  design target.
- Windows Metadata (`.winmd`, including `MetadataKind.WindowsMetadata` and
  `MetadataKind.ManagedWindowsMetadata`) is unsupported. Adding support
  requires separately approved scope.
- Before introducing a dependency, API, or design that cannot run on a
  supported platform, especially single-threaded Browser/Wasm, obtain explicit
  approval. Document the supported and unsupported platforms, rationale,
  affected surface, visible degradation, and validation in the owning design
  and PR.
- Preserve layer ownership: Metadata owns metadata facts, Analysis owns IL-body
  evidence, CSharpText owns model-free textual grammar and layout, CSharp owns
  model-bound spelling and type views, Research composes evidence, and the CLI
  owns commands and presentation.
- Reuse the applicable substrate: clearing houses and services; `InertString`,
  ownership, borrowing, and snapshots; QuerySpace queries, rows, and limits;
  host-neutral APIs, `InspectionEnvelope<TContent>`, and host-specific sinks.
  Every low-level capability needs a production caller.
- Preserve Content, Share, and diagnostics. Keep failures visible and
  behavior-safe defaults intact; network, source-content, exhaustive, and
  otherwise expensive work remains explicit or capability-gated.
- Keep identifiers, provenance, local evidence, correspondence, and
  presentation separate; never infer one from display text when typed identity
  exists.
- Put independently compiled inputs under `fixtures/<owner>/`, tests and test
  infrastructure under `tests/`, and production code under `src/`; follow
  [Fixture governance](fixture-governance.md).
- Use “allow list” and “deny list,” never “whitelist” or “blacklist.”

Markout is the default host-neutral substrate for centralized multi-format
rendering. A broad information domain such as call graphs or diffs needs a
documented structured-typing and format-lowering strategy. A host-specific
renderer must say why it bypasses Markout.

Commands follow [Progressive disclosure](design/progressive-disclosure.md). A
new section enters the default `-v:m` view only when it is the command's single
high-value section.

## Threat-model boundary

Security work focuses on untrusted internet-origin data and construction-time
containment, not local or intra-repository actors unless an owning design opts
in. Do not add design or review requirements for symlinks or reparse points,
same-machine users or agents, or files mutating during inspection. nuget.org
content is immutable; local files may change between operations.

For a credible external-input threat, define the actor, input path, boundary,
containment invariant, and enforcement gate in the owning design. Prefer typed
construction-time containment such as `InertText.InertString`; centralized
entry points such as `HardenedJson` are weaker but auditable. The complete
boundary lives in the
[Untrusted data threat model](design/untrusted-data-threat-model.md).

## Documentation ownership

The root `README.md` is a product landing page of at most 120 lines.
`docs/cli-reference.md` owns detailed CLI behavior and examples;
`docs/README.md` owns acquisition and curated navigation; `docs/overview.md`
owns subsystem topology; and `docs/architecture.md` maps current code.

Ordinary feature and fix work does not edit release-managed central files:
`AGENTS.md`, root `README.md`, `docs/overview.md`, `docs/architecture.md`, or
any `SKILL.md`. Edit them only when explicitly authorized; otherwise add a
concise suggestion with the PR or stack link to the current release tracker.
Update other documentation only when its owned claim changes.

Keep product skills under `skills/` separate from contributor skills under
`.github/skills/` and `.claude/skills/`; follow
[Skill guidance](../taste/skill-guidance.md#user-facing-vs-repo-local-skills).

Some design documents retain proposals or history. Prefer current product
behavior and tests; when sources disagree, stop and resolve which owner is
authoritative.

## Building, testing, and evidence

Use production dotnet-inspect for routine inspection:

```bash
dnx dotnet-inspect -y -- <command>
```

Use the source CLI only when evidence depends on unmerged behavior. The exact
SDK, build, test, and tool-activation commands live in
[Local development](dev-environment.md). Tests are xUnit executables: run them
with `dotnet run` in Release, never `dotnet test`.

Classify new or materially expanded tests as PR-fast or
`[Trait("Speed", "Slow")]`; exhaustive and whole-assembly tests are slow.
[Test cost classification](testing-cost-classification.md) owns thresholds and
the required daily or pre-merge owner for excluded evidence.

All changed Markdown must pass `npx markdownlint-cli <files>`.

## Pull requests

Requested work may proceed through branch, commit, push, PR, and eligible
review without separate approval; merge remains separately authorized. Keep PR
summaries conclusion-first: claim, evidence, compatibility or non-action
boundary, and exact validation. Label an all-Markdown PR `documentation`.

When an agent opens or drives a PR, the repo-local `steward` skill is the
point-of-use decision layer. Invoke it at publication, resume, and every CI,
review, conflict, base-movement, merge, or close event. It applies
[Round orchestration](round-orchestration.md),
[GitHub status queries](github-status-queries.md), and
[GitHub API operations](github-api-operations.md).

Before merging a user-observable change, record it and its PR or stack link on
the current release tracker named in Steward. Outside release preparation, do
not edit `src/DotnetInspect.Cli/release-notes.md`.
