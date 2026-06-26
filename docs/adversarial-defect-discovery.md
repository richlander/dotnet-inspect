# Adversarial Defect Discovery

The **Adversarial Defect Discovery** role searches for high-confidence defects
before they become broad burndown work. It is not a general implementation role
and not a PR reviewer by default: the job is to falsify an existing behavior
claim, file concrete issues with done signals, and hand clusters to the curator.

Use this role when:

- a recent raise, signal, or analysis surface looks too permissive;
- a burndown family is nearly complete and needs near-miss pressure;
- the rollup needs fresh measured rows but the next lane is unclear;
- a broad feature needs adversarial issue discovery before runners start work.

## Output

Each discovered defect should become one durable issue with:

- the product area and surface affected;
- the exact false claim being falsified;
- a minimal repro shape, preferably ordinary source before synthetic IR;
- the observed bad output or misleading signal;
- the first fix shape;
- the done signal, including positive canaries that must keep working.

If several issues share a theme, ask the curator to add them to an existing
burndown or create a new themed burndown. Do not create a catch-all list for
unrelated leftovers.

## General workflow

1. Pick one focus area and one claim to falsify.
2. Check #1568 and active burndowns so you do not duplicate an existing row.
3. Build the smallest positive/negative shape that differs by one discriminator.
4. Prefer real importer/product paths over synthetic-only proof when a real shape
   exists.
5. File one issue per independently fixable defect.
6. Stop when the issue is concrete enough for a burndown runner to claim without
   rediscovering the taxonomy.

Do not broaden a discovery pass into fixes unless explicitly asked. If you do fix
while discovering, follow the burndown runner rules for claiming, syncing
`origin/main`, validation, and adversarial review.

## Focus area: decompiler

Decompiler adversarial discovery tries to falsify raised C# or fidelity claims.
Good targets are recent or broad passes, printer/type materialization, pass-local
identity predicates, sidecar fact coverage, and any `Full` output that may be
invalid, misleading, or semantically different from the IL.

Check:

- over-raises from name-only, namespace-only, or shape-only evidence;
- dropped side effects, trapping evaluations, `leave`/branch targets, or EH
  regions;
- printer output that changes checked/unchecked, signedness, precedence, casts,
  volatility, pointer semantics, or spellability;
- lookalike framework/compiler patterns that should stay lowered or `Partial`;
- sidecar `MissingDiscriminator` text that suggests a near miss is not covered.

Use the relevant evidence boss from `docs/decompiler-correctness-pipeline.md`.
Small pass-level discoveries usually need a focused fixture and
`dotnet run --project src/ILInspector.Decompiler.Tests -c Release`. Risky or
corpus-shaped discoveries should include the harness command or artifact that
proved the issue.

## Focus area: analysis library

Analysis adversarial discovery tries to falsify whole-assembly signal and graph
claims in `ILInspector.Analysis`. Good targets are identity normalization,
framework API predicates, generated-code detection, direct-call matching, and
signals used by `Top Leverage`, `Performance Triage`, `Call Graph`,
`Caller Graph`, and `Analysis Diff`.

Check:

- simple-name or suffix-only matches that should require declaring type,
  assembly, inheritance, or signature identity;
- constructed generic calls that fail to link back to open definitions;
- nested type display/key collisions;
- user-defined lookalikes of framework APIs such as reflection, `Unsafe`,
  `BitConverter`, copy APIs, or exception types;
- generated-looking user code that suppresses or reranks real user methods;
- misleading diff rows caused by identity drift rather than real signal changes.

Keep `ILInspector.Analysis` standalone and SRM-direct. It intentionally does not
share the decompiler's type model. For defects in this area, the default
validation is `dotnet run --project src/ILInspector.Analysis.Tests -c Release`;
include a CLI before/after only when the rendered surface changes.

## Relationship to curator and runners

Adversarial discovery feeds the curator. The curator owns the rollup (#1568),
clusters related discoveries into burndowns, retires completed lists, and assigns
next actions. Burndown runners then claim individual rows and drive them to PRs.

Discovery issues should be runner-ready: narrow, named, reproducible, and scoped
to one done signal. If the best result is "no defect found," post the negative
result only when it changes the map: for example, it closes a suspected gap,
sharpens a sidecar discriminator, or prevents a duplicate row.
