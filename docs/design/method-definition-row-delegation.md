# Method-definition row delegation

## Status

Draft focused design. It owns one step of the existing
[item and line selection composition](item-and-line-limits.md#composition):
*source owner may perform semantics-preserving execution*, for sources whose
units are method definitions. The composition, the row-selection language, and
the delegation protocol are unchanged. Everything this design needs from other
owners is listed under [Residuals](#residuals) as separate work.

Motivating evidence is in the
[performance record](../evidence/producer-planning-performance-2026-09.md#row-windows-skipping-work):
on dense populations, stopping a window's source early measured 7× to 5,600×
faster than building every row and then selecting, with no penalty when
nothing can be skipped.

Every property below is **unverified** until its gate lands.

## Example

Consider a dense method listing asked with `-n 6`, such as the experiment's
public-methods population over Newtonsoft.Json: 1,509 rows with signature
text. No library section of exactly that shape exists yet; choosing a real
first adopter is a residual. Today such a section would build all 1,509 rows
and then row selection would keep the first six. Under this design the
method-definition source receives `Head(6)` as a row-handoff candidate. It tests the predicate method by
method, projects only selected methods, and stops after the sixth, before
reading further metadata. It returns six rows with the evidence *reached 6*.
Row selection accepts them as the exact prefix.

`--rows 4..6` today becomes the same handoff with `Head(6)`, plus the residual
`Window(4..6)` in the reference evaluator. That is exact but projects rows one
to three needlessly. Skipping their projection needs a residual decision by
another owner; see [Residuals](#residuals).

## Owner and exact claim

The method-definition source owns this claim:

> Given a delegation candidate from its caller, a method-definition source
> executes the accepted prefix as phases over one cursor, in the order its
> open query declares. It returns the rows that the reference evaluator would
> produce for that prefix, with completion evidence that states whether each
> phase's condition was met or the source was exhausted. It stops before
> reading metadata that the accepted prefix does not need.

This owner defines:

- which accepted operations it executes, and how each lowers to phases;
- the order-compatibility precondition for stopping early; and
- how phase endings map to completion evidence.

This owner does not define:

- what Head, Tail, Window, or Top mean, or when a strict window fails, which
  [semantic row selection](semantic-row-selection.md) owns;
- delegation candidates, acceptance, source-closed declarations, the effect
  protocol, or evidence validation, which
  [source delegation](source-delegation.md) owns;
- predicate evaluation or effective order, which
  [row query and ordering](row-query-order.md) owns;
- producers, open queries, phases, or kernels, which
  [Producer Planning](producer-planning.md) and
  [open and closed queries](open-and-closed-queries.md) own; or
- which sections adopt, or their row order, which the section owners decide.

## Accepted operations

The source accepts only what the delegation protocol allows it:
source-closed operations, and row-handoff candidates whose residual the caller
completes.

| Request | Candidate | Phases in the source | Evidence |
| --- | --- | --- | --- |
| `-n N` | Row handoff of the exact first N rows | Rows until N | Met at N, or exhausted with fewer |
| Count | Exact count, once declared source-closed | Count until never | Exhausted |
| `--rows A..B` | Row handoff of the first B rows; residual Window | Rows until B | Met at B, or exhausted with fewer |
| `--tail N` | Row handoff of the last N rows | Buffer(N) until never | Exhausted |

A source declines any candidate it cannot execute exactly. Declining is not a
failure; the caller falls back to the reference path.

## Order compatibility

A source can stop early only when the accepted prefix's order is the order it
traverses. Method definitions traverse in metadata order: types in TypeDef
order, then each type's methods. When a section declares any other order, the
source must visit every selected unit, and it can at most keep a bounded top-N
by the sort key and project only the survivors. Whether a section keeps its
order or declares metadata order is the section owner's decision, and it
decides whether that section can skip work.

## Projection

An open query that serves row selection separates its predicate from its
projection. Units the source only tests, counts, or skips pay for the
predicate. Only rows that are returned pay for projection, which is most of a
row's cost.

## Evidence

Every phase ends in one of two recorded ways, and each maps to the evidence
the delegation protocol already defines:

- **Condition met.** The phase reached its N. This is the requirement witness
  for `Head(N)`.
- **Exhausted.** The source ended first. This proves logical exhaustion, so a
  shorter result is exact.

An interruption, such as a budget, a cancellation, or a failed unit, is an
incomplete stop and is never reported as either.

## Residuals

Other owners' work, each independently reviewable:

1. **Source delegation and semantic row selection:** a way to execute
   Window(A..B) without projecting rows one to A - 1. Either semantic row
   selection declares Window source-closed and proves it, including the strict
   failure when A does not exist, or delegation gains a handoff form that
   carries skipped positions without their rows.
2. **Semantic row selection:** a source-closed declaration for Count after
   Head, proven by `SourceClosedDeclarationsMatchOwnerContracts`. It is needed
   before the source may answer "rows A to B of N" in one pass.
3. **Producer Planning:** open queries with a separate predicate and
   projection, and a phase kernel with Discard, Count, Rows, and Buffer
   processors. Both exist only in the experiment branch today.
4. **Section owners:** choose the first adopting section. A good first adopter
   is dense, its rows cost real projection work, and its declared order is
   metadata order. Async Methods is sparse and sorted by kind, declaring type,
   and name, so it would show little. Its owner could still adopt it for the
   type scope and scope guards it would exercise.
5. **QuerySpace:** merging closings from several consumers of one open query,
   tracked by [#8574](https://github.com/richlander/dotnet-inspect/issues/8574).

## Gates

- **Equivalence.** For every accepted candidate, the returned rows and evidence
  match the semantic row selection reference evaluator over the complete
  sequence. This covers windows that start past the end, fewer rows than N,
  and empty populations, and it runs in Release.
- **Contract harness.** The source passes the
  [source delegation contract harness](../../tests/DotnetInspector.SourceDelegation.Tests/)
  for the candidates it accepts.
- **Stopping.** A gate observes that a met Head reads no metadata beyond the
  Nth selected unit, through units visited in the receipt.
