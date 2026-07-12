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

- [PR #2643](https://github.com/richlander/dotnet-inspect/pull/2643) makes
  `BodySignalDiff.AddComparisonRows` throw on `FindingComparison<T>.Failed` and
  records why its total Analysis inputs make that case unreachable. See
  [`BodySignalDiff`](../../src/ILInspector.Analysis/BodySignalDiff.cs).
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

## Adoption review

Before calling a consumer migration complete, verify:

- the Finding census is either the sole source or is cross-validated against
  every retained legacy item;
- absence, successful emptiness, and failure remain distinguishable;
- failures are visible and isolated to their source;
- the superseded lane is deleted;
- native transition cases reach confirmation surfaces unchanged;
- sensors compare the same identities and docket entries remain checkable.
