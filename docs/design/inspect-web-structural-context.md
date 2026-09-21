# Inspect Web Structural Context

## Status and ownership

This document defines the focused Browser composition for [#7981](https://github.com/richlander/dotnet-inspect/issues/7981), the Browser/Wasm adoption probe in the structural-context program [#7696](https://github.com/richlander/dotnet-inspect/issues/7696).

The normative claim is:

> An explicit Library Compare Diff gesture consumes one completed, subject-owned implementation-comparison envelope and presents its Research-issued structural cohort context for the exact selected package Library and ordered version pair, without recomputing cohort membership, hiding typed non-success, or implying a quality score.

This owner defines Browser request association, explicit disclosure, bounded wire projection, and Library-level presentation. It does not own implementation comparison, profile analysis, structural pairing, cohort assignment, package target selection, package acquisition, the shared terminal envelope, navigation, or Worker lifetime.

The current `ImplementationComparisonQuery` route is command-owned and is not yet a completed host-neutral terminal. The subject-owned Implementation Diff adoption in [Diff operation and subject-section adoption](command-transition-model.md#diff-operation-and-subject-section-adoption) is therefore a prerequisite, not work silently absorbed into this Browser slice.

## Consumer and product evidence

The consumer is a person comparing two versions of one selected Gallery Package Library who wants to answer an investigation question such as:

> Which implementation changes moved in the same structural directions, and how common is that exact direction pattern in this comparison?

The Browser starts from the Library's existing Compare **Diff** frame. **Structural context** is an explicit disclosure in that frame, not a third persistent Compare mode, a default section, or a second version-target control. Opening a Library, selecting a target, or viewing API Diff never runs structural comparison automatically.

The first real asset is [`Markout@0.33.0..0.35.2`](https://www.nuget.org/packages/Markout/0.35.2), comparing its `lib/net10.0/Markout.dll` Library. The current Release source build produced:

```bash
dotnet run --project src/DotnetInspect.Cli -c Release -- \
  diff --package Markout@0.33.0..0.35.2 \
  -S "Structural Context" \
  --json
```

The result contains 1,100 complete, unambiguous structural pairs, 58 pairs with at least one nonzero structural delta, and 17 exact direction cohorts. The all-Unchanged cohort contains 1,042 pairs. `Markout.MarkoutProjection.ResolveColumns(...)` is a singleton cohort with instruction `-129`, complexity `-14`, loops `-4`, exception regions `-2`, direct calls `-27`, and allocations `-3`. In contrast, `Markout.MarkdownFormatter`'s streaming `BeginTable(...)` is in a 19-member cohort with instruction `+4` and direct calls `+2`.

Those observations establish that the Browser needs both numeric deltas and exact cohort frequency: a singleton is not an outlier or a quality defect, and equal directions do not imply equal magnitudes.

## Consumed boundaries

| Owner | Consumed contract |
| --- | --- |
| [Implementation Diff](implementation-diff.md#normal-flow-complexity-comparison) | Complete, unambiguous paired-profile eligibility; the seven signed structural dimensions; direction-only signatures; exact local cohort frequency; and the absence of a quality or outlier claim. |
| [Diff operation and subject-section adoption](command-transition-model.md#diff-operation-and-subject-section-adoption) | The future completed subject-owned implementation-comparison envelope and equal semantic baseline for equivalent operation-first and subject-section requests. |
| [Inspection envelope](inspection-envelope.md) | One complete owner-issued Content value, required Share outcome, and ordered diagnostics crossing both hosts unchanged. |
| [Inspect Web Compare Experience](inspect-web-compare-experience.md) | Library Compare Diff entry, retained Compare state, and the existing quiet result frame. |
| [Browser Diff targets](inspect-web-diff-targets.md) | Package-owned current/target version selection and the return path to Change target. |
| Browser package Workspace | Exact Gallery package coordinate, framework, selected compile asset, acquisition, and protected scope lifetime. |
| [Inspect-web operation authority](inspect-web-operation-authority.md) | Immutable request association, current-context publication, supersession, cancellation, and disposal. |
| [Inspect-web managed operation bridge](inspect-web-managed-operation-bridge.md) | Keyed managed execution, cancellation forwarding, terminal transport, and release. |
| [Inspect Web worker runtime](inspect-web-worker-runtime.md) | One ordinary Worker epoch and bounded JSON transport. |

These are dependencies, not contracts redefined by this feature. In particular, Browser never derives structural identity from a Member display label, C#/IL rendering, CLI JSON, Markout text, or a local TypeScript analysis.

## Entry, request, and association

Structural Context is available only from Library Compare Diff when all of the following are true:

- the active subject is one exact Library;
- the Library belongs to an active `nuget.org` Package model;
- the Package-owned Diff target is one resolved exact version;
- the selected Library has one acquisition-issued compile-asset identity; and
- the future subject-owned implementation-comparison operation admits that exact selected-Library plan.

The immutable Browser request retains the schema version, Package ID, current and target versions, target framework, and exact selected compile-asset ID. The same asset ID applies independently to both endpoints. Managed code must resolve that ID in each endpoint scope; it must not fall back to an assembly display name, simple name, first matching asset, a Type filter, or a Member label.

Before is the selected target version and After is the active Package version. The same version is valid and must preserve the shared operation's successful empty or all-Unchanged meaning rather than manufacturing a failure.

Changing the active Package model, current version, target, framework, selected Library, Compare lens, Workspace, or route supersedes the active request. A completion may publish only into the exact Package model and immutable request that admitted it. An unavailable target never starts a same-version comparison.

The Browser operation holds both endpoint scopes until the completed shared operation and wire projection settle. It does not open metadata readers, construct body indexes, acquire a third source, or call `ImplementationComparisonQuery` directly. The future shared terminal owns execution over the admitted endpoint populations.

## Completed content and wire projection

The completed shared terminal must preserve the normal `InspectionEnvelope<TContent>` baseline. Browser receives the complete Content JSON, Share, and diagnostics alongside its typed view projection. It neither replaces Content with cohort rows nor treats a rendered cohort table as the semantic result.

The Browser projection retains, for every eligible pair:

- the owner-issued member/physical-method correspondence used by the shared terminal;
- all seven signed deltas: instructions, normal-flow complexity, loops, aggregate exception regions, direct calls, allocations, and async state-machine presence;
- the seven corresponding decreased/unchanged/increased direction values;
- the exact local `PopulationSize` and `CohortSize`; and
- the stable `research.complexity.structural-cohort` Kind.

It also retains Content's availability, eligibility, coverage, endpoint, and diagnostic evidence. Added, removed, incomplete, ambiguous, profile-unavailable, and endpoint-failed cases never become zero-delta rows. A missing structural population remains a typed non-success or unavailable state from Content, not an empty cohort list.

The Browser may group rows using the supplied direction values, but it must preserve exact Research membership and the producer's member ordering within each group. It must not calculate a distance, weight dimensions, normalize magnitudes, infer an outlier, or sort as if a cohort frequency were a severity.

The projection is all-or-nothing. The complete envelope must first satisfy the ordinary Worker transport bounds; the Browser's additional presentation projection must then satisfy its own documented bounds. A bound failure is a typed rejected result, not a successful truncated cohort list or a baseline with omitted diagnostics. The implementation slice names and validates those concrete bounds from the active Worker contract rather than copying the Library API Diff limits by convention.

`Share.NonProjectable` remains visible as issued when the ordered endpoint comparison cannot be represented faithfully. Browser does not invent a URL, Workspace packet, or a partial replay gesture from the Package labels.

## Library presentation

Structural Context opens within Library Compare Diff as an explicit, replaceable disclosure. It does not add a persistent tab, workspace chrome, new navigation subject, or second Compare mode:

```text
Compare Markout                                      Diff | Clone
0.33.0 -> 0.35.2                            Change target

API changes
...

[Inspect structural context]

Structural context
1,100 eligible pairs  ·  58 changed pairs  ·  17 direction cohorts
Exact direction frequency only; not an outlier or quality assessment.

- instructions down · complexity down · loops down · exception regions down
  · calls down · allocations down · async unchanged       1 method
  MarkoutProjection.ResolveColumns(...)
  -129 instructions down · -14 complexity down · -4 loops down
  · -2 exception regions down · -27 direct calls down
  · -3 allocations down · 0 async unchanged

- instructions up · complexity unchanged · loops unchanged
  · exception regions unchanged · calls up · allocations unchanged
  · async unchanged                                      19 methods
  MarkoutFormatter.BeginTable(...)
  +4 instructions up · 0 complexity unchanged · 0 loops unchanged
  · 0 exception regions unchanged · +2 direct calls up
  · 0 allocations unchanged · 0 async unchanged

- unchanged in every dimension                        1,042 methods
  [Show methods]
```

The example demonstrates presentation intent, not a second fact model. The Browser derives group labels from typed directions and method detail from typed signed deltas. It does not parse the CLI's display strings or `Kind`.

The view:

- identifies the exact target and current versions;
- shows the eligible-population count, changed-pair count, and exact direction-cohort count;
- presents each direction cohort with its exact frequency;
- presents each method's seven signed deltas and directions without hiding zero dimensions;
- keeps the all-Unchanged cohort visible and available for explicit expansion;
- states that eligibility excludes added, removed, incomplete, and ambiguous pairs; and
- renders available diagnostic, unavailable, rejected, managed-failed, transport-failed, and canceled states distinctly.

The all-Unchanged cohort may begin collapsed to avoid allocating persistent space for a large routine population. Collapsing is presentation only: its count remains visible, its members stay available on explicit request, and it is never omitted from the completed Content or local population.

The view does not rank cohorts, color a singleton as risky, offer a package quality grade, compare different package pairs, or claim authored-source C# complexity. Member activation and a structural-context drill-down are follow-on Browser work; this first Library consumer does not promote a cohort row or display label into a navigation identity.

## Delivery and evidence

The first implementation must land in focused slices:

1. The Diff/Implementation owner delivers the completed subject-owned implementation-comparison envelope, preserving the existing Research structural facts and typed non-success. This is a prerequisite under [#7703](https://github.com/richlander/dotnet-inspect/issues/7703), not a Browser change.
2. Inspect Web adopts that completed terminal for the exact selected Gallery Library request defined here, with a generated, versioned facade and bounded wire projection.
3. Inspect Web presents the explicit Library Compare Diff disclosure and retires no existing API Diff, Source, Clone, or method-body capability.

The Browser implementation uses deterministic package fixtures for request association, exact asset mismatch, same-version handling, profile unavailability, ambiguous/incomplete exclusion, all-Unchanged expansion, transport bounds, cancellation, and stale completion suppression. A focused pre-merge Browser/Wasm gate uses the real `Markout@0.33.0..0.35.2` pair above and verifies:

- the 1,100-pair population, 58 changed pairs, 17 direction cohorts, and 1,042-member all-Unchanged cohort;
- the singleton `ResolveColumns(...)` deltas and the 19-member `BeginTable(...)` cohort;
- parity of complete Content, Share, and diagnostics with the shared terminal; and
- typed non-success rather than an empty successful projection when structural evidence is unavailable.

No runtime behavior is implemented by this design/probe slice. The gate plan is **unverified** until the focused shared-terminal and Browser implementation slices land.
