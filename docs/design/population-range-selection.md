# Population range selection

## Status, owner, and claim

Status: **proposed; not implemented**. This focused pattern is tracked with
[#6987](https://github.com/richlander/dotnet-inspect/issues/6987), within
[Compare delivery #5083](https://github.com/richlander/dotnet-inspect/issues/5083).
It owns the selection obligation introduced by a population-creating range:

> Ranges that create a population need an explicit consumer selector.
> Ranges that filter an already selected population do not need another one.
> A reduction such as Count can be that consumer.

This records the user's 2026-09-14 direction. It defines neither a universal
range grammar nor a new selection algorithm. Population discovery, identity,
ordering, completeness, acquisition, and row reduction retain their owners.
The command or query adopting this pattern declares the supported consumers
and the population each consumes.

## Construction versus filtering

A **population-creating range** supplies the subjects on which an operation
could act, but does not choose the operation. An adopted request must select
one admitted consumer before executing that population request. Omission or
conflicting operation selectors produces a visible input error, not an implicit
default, endpoint comparison, history scan, or count.

A **population-filtering range** narrows an operation's already declared input
or result population. It inherits that operation and does not require another
consumer selector. It does not satisfy a missing consumer on a separate
population-creating range in the same request.

Classification follows the typed request's role, not punctuation alone.
The same `A..B` spelling can construct version subjects or filter declared
rows without making those populations interchangeable.

The declaration of an admitted consumer identifies its input population,
result unit, and permitted work. An operation selector can authorize
inspection under that operation's contract. A metadata-only reduction cannot
authorize payload inspection merely because its result is small.

## Count is a consumer, not a guessed operation

Count-only consumes the declared source population and returns its exact
selected cardinality. It does not silently choose a comparison first.
With an explicit operation selected, Count instead reduces that operation's
declared result rows. The command declaration fixes this distinction before
execution; success, failure, result size, and rendering cannot change it.

Supported filters and semantic row stages precede the reduction. Count uses
the existing [section-row shaping](section-row-shaping.md#count-semantics)
contract, including strict window failures and source-evidence requirements.
It does not report a work ceiling or a partially observed population as an
exact count, or turn an unavailable population into zero.

An adopter must state the unit plainly: versions, comparison rows, or another
owner-issued cohort. Multiple selected cohorts retain their separate counts;
this pattern does not add a cross-cohort total.

## Precedent and deliberate change

Existing CLI behavior provides complementary evidence:

- `--rows A..B` filters an already declared row population.
- `package P@A..B --versions` explicitly selects a metadata-only version view.
- `type T --package P@A..B --at last` selects one evaluation from a range.
- `match A B` and `match A --similar` explicitly distinguish pairwise and
  population-based work.

These precedents are not a claim that this rule is already implemented
everywhere. In particular, current Diff interprets a source range as an
endpoint comparison without another selector. Its first adoption intentionally
removes that default; other command owners are not migrated by this document.

## First adoption and evidence

[Diff History inspection](diff-history.md#explicit-range-consumers) is the
first adopter. Its specification locks alongside this pattern under the
[bounded first-adopter exception](../design-scope.md#stage-implementation-after-locking-the-design).
That owner defines the CLI selectors, shared results, Browser consumption,
and no-compat retirement; this pattern does not duplicate their contracts.

The counted production path is the adopter's four steps: specification, shared
queries/results, CLI cutover, and Browser adoption. The real motivating asset is
`Markout@0.33.0..0.35.2`, both as a version-count question and as a History
inspection of `Markout.MarkoutWriterOptions`. Other owners adopt separately;
no repository-wide migration or new temporal population resolver is implied.

The adopter's Release gates must distinguish a missing consumer, count-only
source rows, Count after an explicit operation, and filtering without an extra
consumer. They must preserve discovery failures and show that metadata-only
counts do not acquire package payloads. Those new gates are **unverified**
in this specification-only slice.
