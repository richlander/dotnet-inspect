# Finding Adoption

A Finding producer is not adopted when a consumer merely calls it. Adoption is
complete when the consumer retains the producer's outcome semantics, replaces
the bespoke path that the producer supersedes, and exposes the producer's
native cases without disguising them as legacy classifications.

This document is the consumer-side companion to
[Finding Producer Design](finding-producers.md). The observation and transition
vocabulary comes from [Finding Nomenclature](finding-nomenclature.md), while
identity, order, and provenance follow
[Finding Coordinates](finding-coordinates.md).

## 1. Cross-validate dual representations

**Rule.** A migration may temporarily retain a legacy inventory for display or
compatibility, but the Finding census must not be decorative. Build both
representations from the same acquisition and cross-validate every consumed
legacy item against a Finding observation using producer-owned identity and the
domain fields needed to disambiguate collisions. A count comparison is not
enough. Divergence is a producer or adapter bug and must fail loudly.

Delete the legacy representation after this check has run at the required
scale. The cross-validation period is what proves that the Finding census can
become the sole source; it is not a reason to preserve two permanent lanes.

**Model.** [PR #2662](https://github.com/richlander/dotnet-inspect/pull/2662)
uses `ExtensionsCommand.ProjectExtensions` to require correspondence between
each scanner member and Metadata's extension-member census by anchor, kind,
canonical extended type, return type, and assembly. Presentation takes the
member kind from the validated observation. See
[`ExtensionsCommand`](../../src/dotnet-inspect/Commands/ExtensionsCommand.cs).

## 2. Retain acquisition outcomes

**Rule.** Preserve `Complete([])`, `Absent`, and `Failed` as distinct states
until the presentation boundary. Do not turn a failed acquisition into an empty
collection, a missing section, or a verbose-only log entry. Surface failure
unconditionally with its subject, descriptor, and reason.

For multi-source operations, scope failure to the source that failed. Healthy
assemblies or packages must still contribute results; one bad source must not
erase the rest.

**Models.**

- [PR #2628](https://github.com/richlander/dotnet-inspect/pull/2628) retains
  failed library inspections and projects them through the `Inspection
  Failures` section instead of making failure look empty. See
  [`LibraryInspection`](../../src/dotnet-inspect/Models/LibraryInspection.cs).
- [PR #2662](https://github.com/richlander/dotnet-inspect/pull/2662) creates one
  extension-member inspection per assembly, writes failed inspections to
  standard error, excludes only the failed census, and keeps successful
  assemblies in the result.
- [PR #2699](https://github.com/richlander/dotnet-inspect/pull/2699) scopes
  failure per version cell: one throwing evaluation becomes that cell's `Failed`
  while later cells are still evaluated, applying the same one-bad-source
  principle at N-address scale. See
  [`TimelineCommand`](../../src/dotnet-inspect/Commands/TimelineCommand.cs).

## 3. Replace instead of accreting

**Rule.** Delete the bespoke producer, adapter, comparison, or presentation lane
that the adopted Finding path replaces in the same change. A temporary
dual-representation check is allowed under rule 1; two independently active
sources of truth are not.

Downstream summaries may derive compatibility fields from the native Finding
comparison, but they must not rerun a parallel matcher or count-only diff.

**Models.**

- [PR #2638](https://github.com/richlander/dotnet-inspect/pull/2638) derives the
  allocation count, hotness, magnitude, and Research projection from
  `FindingComparison<AllocationOccurrence>` and removes the separate count-only
  allocation row path. See
  [`ResearchDiff`](../../src/ILInspector.Research/ResearchDiff.cs).
- [PR #2640](https://github.com/richlander/dotnet-inspect/pull/2640) replaces
  the project `Grounding` lens with `Skills`; it does not leave both selectors
  and row models active. See
  [`ProjectCommand`](../../src/dotnet-inspect/Commands/ProjectCommand.cs).

## 4. Consume envelopes fail-visibly

**Rule.** Handle every operation-outcome case explicitly. If a failed
comparison is reachable, render a visible degraded or failure row. If the
consumer can prove failure unreachable because both producer inputs are total
`Complete` censuses, throw and state that invariant next to the exhaustive
case. Never manufacture an empty match or silently omit a known change because
its preferred evidence projection is unavailable.

**Models.**

- [PR #2643](https://github.com/richlander/dotnet-inspect/pull/2643) established
  that consumers of total Analysis censuses throw on
  `FindingComparison<T>.Failed` and record why that case is unreachable. The
  adopted consumer keeps that invariant in
  [`UnsafetyFindingDiff`](../../src/ILInspector.Research/UnsafetyFindingDiff.cs).
- [PR #2641](https://github.com/richlander/dotnet-inspect/pull/2641) emits a
  fallback `ImplementationDiffRow` from the change detail or descriptor when a
  known change has no unified evidence lines, rather than rendering it as
  nothing. See
  [`DiffOutputFormatter`](../../src/dotnet-inspect/Output/DiffOutputFormatter.cs).

## 5. Confirmation lenses report native cases

**Rule.** A focused Finding confirmation surface reports the producer's native
`PairFinding<T>` case verbatim. It must not relabel `Present`, `Added`,
`Removed`, or `Changed` as compatibility, severity, or triage classes, and it
must reject filters that would create that costume.

Validate the lens, descriptor, and target shape before package, platform, or
assembly acquisition. Invalid confirmation requests should fail before doing
expensive work.

**Models.** [PR #2642](https://github.com/richlander/dotnet-inspect/pull/2642)
introduced API Finding transitions, and
[PR #2661](https://github.com/richlander/dotnet-inspect/pull/2661) generalized
the lens to allocation transitions.
[PR #2671](https://github.com/richlander/dotnet-inspect/pull/2671) applies the
same contract to direct call sites. `DiffCommand` validates the focused target
before acquisition, rejects classification filters, and renders
`PairFinding.{pair.Kind}` directly. See
[`DiffCommand`](../../src/dotnet-inspect/Commands/DiffCommand.cs).
[PR #2697](https://github.com/richlander/dotnet-inspect/pull/2697) proves the
marginal cost of the next descriptor: `analysis.unsafety` reuses the generic
`BuildAnalysisFindingTransitions<T>` plus `RetainedFindingComparisonSet` for one
row-mapper and one delegation, without a parallel matcher. See
[`DiffCommand`](../../src/dotnet-inspect/Commands/DiffCommand.cs) and
[`RetainedFindingComparisonSet`](../../src/ILInspector.Research/ResearchChanges.cs).
[PR #2699](https://github.com/richlander/dotnet-inspect/pull/2699) renders the
same native cases in timeline transition rows as `pair.Kind`, without a triage
relabel.

## 6. Carry sensor and docket integrity

**Rule.** Adoption quality gates must prove that the same subjects were
measured. Compare sampled identity sets, not only counts or caps: equal sample
sizes can hide membership changes and make a quality delta incomparable.

When a known-difference docket accepts a non-exact result, add a checkability
pin for the docket entry. The pin must require that the subject is still
evaluated and remains in a result class the gate can inspect. Otherwise a
regression can escape by falling into an excluded failure bucket.

**Models.**

- [PR #2649](https://github.com/richlander/dotnet-inspect/pull/2649) uses
  `CorpusSensor.HaveSameMethodSample` to compare checked method identity sets
  before reporting sampled quality deltas. See
  [`CorpusSensor`](../../tools/DecompilerHarness/CorpusSensor.cs).
- [PR #2651](https://github.com/richlander/dotnet-inspect/pull/2651) adds
  `PointerStoreUsesOriginalAddress_StaysCompileBackCheckable` in both fidelity
  views. The docket may accept `Exact` or `OpcodeDiff`, but not disappearance,
  `RecompileFail`, or `ContextFail`. See
  [`FidelityGateTests`](../../src/ILInspector.Decompiler.Tests/FidelityGateTests.cs)
  and
  [`LoweredFidelityGateTests`](../../src/ILInspector.Decompiler.Tests/LoweredFidelityGateTests.cs).

## 7. Correlate through the census and identity tiers

**Rule.** An N-address consumer routes whole-census state through
`FindingCensusCorrelation<T>` and exact-identity tracks through `Correlate(key)`.
It must not build a private per-cell state model, and it must not merge the two
vocabularies: `Complete` is a census inspection state, not a fifth exact-identity
correlation state (see [Finding Nomenclature](finding-nomenclature.md)).

`Unevaluated` is a presentation join of the address space against the evaluated
cells; it is never fabricated as an inspection outcome. Failure topology is
per-cell: one failed evaluation becomes that cell's `Failed` and must not abort
or discard the other paid evaluations.

**Model.** [PR #2699](https://github.com/richlander/dotnet-inspect/pull/2699)
correlates package version vectors through `FindingCensusCorrelation<T>` and
`Correlate(key)`, joins unevaluated addresses only at presentation, and scopes
failure to the cell. Pins:
`CellException_BecomesFailureAndLaterCellsStillEvaluate`,
`EmptyOwnedCensus_PreservesSubjectAvailabilityTransitions`, and
`ProbeOrder_DoesNotChangeTimelineOrder` in
[`TimelineCommandTests`](../../src/dotnet-inspect.Tests/TimelineCommandTests.cs).
See [`FindingCensusCorrelation`](../../src/ILInspector.Findings/FindingCorrelation.cs).

## 8. Equality is not correspondence

**Rule.** Correspondence is always `FindingKey`-driven. .NET value equality
answers only whether two already-materialized values have the same content; it
is not a matching channel. A consumer that dedupes, caches, or set-compares
findings must not reach for `.Equals` as a stand-in for correspondence. A
collection-bearing payload that promises value equality must define its semantic
equality explicitly, or supply an explicit comparer.

**Model.** [PR #2701](https://github.com/richlander/dotnet-inspect/pull/2701)
gives Finding collection records sequence- and set-aware value equality while
keeping matching key-driven. The load-bearing pin is
`FindingPayloadEquality_IsProducerOwnedButMatchingRemainsKeyDriven`: equal keys
still match while unequal payload content reports unequal, and that is correct.
See [Finding Value Equality](finding-value-equality.md) and
[`FindingValueEquality`](../../src/ILInspector.Findings/FindingValueEquality.cs).

## 9. Retain provenance without promoting judgments

**Rule.** A downstream judgment may retain the native Finding that supports it
without turning the judgment itself into a Finding. Preserve the producer
descriptor and identity separately from version-local coordinates, then keep
matching with the producer rather than inventing correspondence for the
judgment.

**Model.** Performance Triage keeps ranking and fix guidance as downstream
judgments. Exact rows retain the `analysis.allocation` or
`analysis.call-site` descriptor and identity fingerprint together with the
version-local MethodDef token, IL offset, occurrence ordinal, operation, and
operand token. `Provenance` distinguishes `exact`, `aggregate`, and `unmatched`
rows rather than relying on empty fields as an implicit signal. `Candidate` is
therefore useful for a runtime/static join within one build, while
`diff --finding` and `timeline --finding` remain the cross-version
correspondence paths. Aggregate judgments such as `allocation-hotspot`
deliberately have no exact source Finding.

`Resource Triage` follows the same rule. Its pool-churn impact and cleanup
direction are downstream judgments over exact
`analysis.resource-lifecycle` observations; rows retain the native descriptor,
candidate identity, resolved boundaries, and acquisition/boundary IL offsets.
The harness measures all actionability classes from that same inspection, while
the product section selects the untrusted-actionable subset.

## Adoption review

Before calling a consumer migration complete, verify:

- the Finding census is either the sole source or is cross-validated against
  every retained legacy item;
- absence, successful emptiness, and failure remain distinguishable;
- failures are visible and isolated to their source;
- the superseded lane is deleted;
- native transition cases reach confirmation surfaces unchanged;
- N-address consumers correlate through the census and identity tiers, with
  `Unevaluated` only as a presentation join and per-cell failure scoping;
- matching remains key-driven, payload equality is content-only, and
  collection-bearing payloads declare their equality semantics;
- downstream judgments retain native provenance without claiming a new
  correspondence model;
- sensors compare the same identities and docket entries remain checkable.
