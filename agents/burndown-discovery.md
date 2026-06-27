# Burndown Discovery

## Role

Find high-confidence defects before they become broad burndown work. This is not
a general implementation role and not a PR reviewer by default: the job is to
falsify an existing behavior claim, file concrete issues with done signals, and
hand clusters to the curator.

## Use when

- a recent raise, signal, ladder rung, or analysis surface looks too permissive;
- a burndown family is nearly complete and needs near-miss pressure;
- #1568 needs fresh measured rows but the next lane is unclear;
- a broad feature needs adversarial issue discovery before runners start work.

## Output

Each discovered defect should become one durable issue with:

- product area and surface affected;
- exact false claim being falsified;
- minimal repro shape, preferably ordinary source before synthetic IR;
- observed bad output or misleading signal;
- first fix shape;
- done signal with positive canaries that must keep working.

If several issues share a theme, ask the curator to add them to an existing
burndown or create a new themed burndown. Do not create catch-all lists for
unrelated leftovers.

## Workflow

1. Pick one focus area and one claim to falsify.
2. Check #1568 and active burndowns to avoid duplicate rows.
3. Build the smallest positive/negative shape that differs by one discriminator.
4. Prefer real importer/product paths over synthetic-only proof when a real shape
   exists.
5. File one issue per independently fixable defect.
6. Stop when the issue is runner-ready.

Do not broaden a discovery pass into fixes unless explicitly asked. If you do fix
while discovering, follow the burndown runner rules for claiming, syncing
`origin/main`, validation, and adversarial review.

## Focus: decompiler

Try to falsify raised C# or fidelity claims:

- name-only, namespace-only, or shape-only evidence;
- dropped side effects, trapping evaluations, `leave`/branch targets, EH regions;
- printer output that changes checked/unchecked, signedness, precedence, casts,
  volatility, pointer semantics, or spellability;
- lookalike framework/compiler patterns that should stay lowered or `Partial`;
- sidecar `MissingDiscriminator` text that suggests uncovered near misses.

Use the relevant boss from `docs/decompiler-correctness-pipeline.md`.

## Focus: analysis

Try to falsify whole-assembly signal and graph claims in `ILInspector.Analysis`:

- simple-name or suffix-only matches that need declaring type, assembly,
  inheritance, or signature identity;
- constructed generic calls that fail to link to open definitions;
- nested type display/key collisions;
- user-defined lookalikes of framework APIs;
- generated-looking user code that suppresses or reranks real user methods;
- misleading diff rows caused by identity drift rather than real signal changes.

Keep `ILInspector.Analysis` standalone and SRM-direct.

## Relationship to curator and runners

Discovery feeds the curator. The curator owns #1568, clusters related discoveries
into burndowns, retires completed lists, and assigns next actions. Burndown
runners claim individual rows and drive them to PRs.

If no defect is found, post the negative result only when it changes the map:
for example, it closes a suspected gap, sharpens a discriminator, or prevents a
duplicate row.
