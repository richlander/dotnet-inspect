# Inspect Web Library API Diff

## Status and ownership

This document defines the focused Browser composition for
[#6423](https://github.com/richlander/dotnet-inspect/issues/6423), within the
Compare experience tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).

The normative claim is:

> Explicitly opening Compare for one selected Gallery Package Library uses
> that retained Package model's effective Diff target to publish one
> request-associated, complete public-API changed-Type inventory from the
> shared Library API diff document, preserving exact endpoint and Type
> identity and every typed non-success outcome.

This document owns only the Browser request, operation association, bounded
wire projection, and Library-root presentation. It does not own package
version ordering, package acquisition, API comparison, compatibility
classification, the portable result, general Compare interaction, navigation,
or Worker lifetime.

The issue's original selected-Type master/detail wording is superseded by the
[Inspect Web Compare Experience](inspect-web-compare-experience.md). Library
Compare is a flat changed-Type inventory. Type and Member are later
drill-down lists, and Member is the first detail boundary.

## Consumer and basis

The consumer is a person inspecting one Library from a Gallery Package who
wants to see how its complete public API differs from the Package-owned
baseline without leaving the Library context or acquiring Source.

This is the final adopter in the delivery path defined by
[Library API Diff Presentation](library-api-diff-presentation.md):

1. selected-Library API comparison;
2. Package Diff target intent;
3. portable Library-root presentation contract;
4. presentation adapter; and
5. this Browser composition.

The production host is Inspect Web. The CLI already consumes the shared query
and presentation substrate through its own presentation path. This Browser
slice does not create a host-neutral abstraction waiting for a future adopter.

## Consumed boundaries

| Owner | Consumed contract |
| --- | --- |
| [Compare experience](inspect-web-compare-experience.md) | Package owns targets; Library Diff is one quiet flat changed-Type inventory. |
| [Diff targets](inspect-web-diff-targets.md) | Previous/exact target intent and authoritative Gallery version ordering. |
| [Selected-Library query](../inspection-space.md#selected-library-api-comparison) | Independently projected Before and After endpoints and Metadata-owned API comparison. |
| [Library API Diff Presentation](library-api-diff-presentation.md) | Complete Library-root document, exact Type identities, aggregate counts, and typed Available/Unavailable/Rejected outcomes. |
| Browser package Workspace | Exact Gallery package, framework, compile-asset identity, acquisition, and protected scope lifetime. |
| [Operation authority](inspect-web-operation-authority.md) | Current-context publication, supersession, cancellation, disposal, and quiescence. |
| [Managed operation bridge](inspect-web-managed-operation-bridge.md) | Keyed managed execution, cancellation forwarding, and release. |
| [Worker runtime](inspect-web-worker-runtime.md) | One ordinary Worker epoch and bounded JSON transport. |

These are dependencies, not additional normative owners. In particular, the
Browser operation does not reconstruct a Compare Registry entry or use display
text as an authorization token. The visible Compare lens is admitted only for
the supported Gallery Library context; the managed query then consumes the
exact selected package and compile-asset identities.

## Entry and target resolution

Compare is offered as a Library lens only when all of these facts hold:

- the active subject is one exact Library;
- the owning Package source is `nuget.org`; and
- the Package is not a Platform/runtime pseudo-package.

Entering Compare is the explicit query trigger. Merely selecting a Package or
Library, changing target settings, or listing versions does not run a
comparison.

The active retained Package model owns one Diff target:

- an exact target resolves directly to its retained exact version, even if a
  later version-list request is unavailable; or
- the previous target resolves only when the authoritative version inventory
  supplies `previousVersion`.

An unresolved previous target remains a visible target state. Loading,
version-list failure, an unavailable predecessor, and a supplied
`previousVersionUnavailableReason` stay distinct. None starts the managed
operation or becomes a same-version comparison.

The exact target may equal the current version. That is a valid neighboring
case and produces a successful empty comparison when the selected Library is
unchanged.

**Change target** returns to Package Overview's Comparison targets area. The
Library surface does not duplicate Package target controls.

## Request and result association

One immutable request contains:

- schema version;
- package ID;
- current package version;
- resolved target version;
- target framework; and
- the exact acquisition-issued compile-asset ID of the selected Library.

The selected asset ID applies independently at both package versions. Managed
code resolves that exact ID in each acquired scope. It never falls back to
assembly display name, simple name, or the first matching asset.

The request context additionally retains the active Package object identity.
A replacement Package model with equal displayed coordinates is a different
context and supersedes prior work.

Changing the Package model, current version, framework, selected Library,
effective target, lens, Workspace, or route cancels or replaces the active
operation. Page operation authority associates terminal publication with the
immutable request. Managed completion after replacement is consumed but
cannot publish into the new context.

The managed result echoes the accepted request. A schema mismatch, missing
required success value, contradictory result arm, or request mismatch is a
transport contract failure, not a feature non-success. Expected acquisition,
projection, presentation, and cancellation outcomes retain their typed result
arms.

## Managed composition

The Metadata facade owns the operation because the result is API/metadata
projection. No eighth facade is introduced.

For each endpoint, managed code:

1. opens the requested Gallery package and framework through
   `BrowserPackageWorkspace`;
2. resolves the exact compile asset;
3. projects `ApiSurfaceScope.Public` with the fixed
   `BrowserApiSurfacePolicy.Limits`; and
4. passes both projections to `AssemblyContextApiComparisonQuery` and
   `LibraryApiDiffPresentationAdapter`.

Before is the target version and After is the current version. Each scope is
released after the shared comparison and wire projection complete.

The Browser wire result retains:

- endpoint package, version, framework, exact asset, assembly identity, scope,
  completeness, and bounded issues;
- aggregate changed-Type, changed-member, and compatibility counts;
- every producer-ordered changed Type;
- exact nullable Before and After Type identities; and
- type-definition and compact compatibility counts.

The wire projection is all-or-nothing. It admits at most 10,000 changed Types
and a conservative retained Type-text budget below the ordinary Worker's
8,388,608-character JSON limit. Exceeding either bound produces typed
`Rejected`; it never returns a truncated successful inventory.

## Library presentation

The Library surface uses one quiet Compare frame:

```text
Compare Example.Library                              Diff
1.0.0 -> 2.0.0                             Change target

3 changed Types  ·  1 breaking  ·  4 additive

Changed   Example.Widget             2 members  1 breaking
Added     Example.WidgetOptions       3 additive
Removed   Example.LegacyWidget        1 breaking
```

The frame renders:

- the exact effective target and current version;
- aggregate owner-issued counts;
- one row for every changed Type in producer order;
- added, removed, or changed state;
- Type-definition change status when present;
- changed-member count; and
- compact breaking, additive, and potentially-breaking counts.

Rows retain exact Before and After Type identifiers in the DOM projection.
This first Library adopter does not make them interactive because Type Compare
is not yet delivered. It does not fake sticky drill-down by mutating Type and
lens state independently, and it does not open an in-place detail region.
The later Type Compare slice consumes the product-owned atomic descendant
subject-and-lens activation from
[#6490](https://github.com/richlander/dotnet-inspect/issues/6490).

A successful empty comparison remains visibly successful and names both
versions. It is not an unavailable placeholder.

Loading, target unavailable, endpoint unavailable, presentation rejected,
managed failed, transport failed, and canceled are distinct states within the
same frame. Endpoint issue details remain available in non-success
presentation. No state substitutes an empty Type list for failure.

The Browser lowers the typed document directly to DOM. It does not parse CLI
Markdown, Mermaid, Markout text, diagnostic messages, or display labels to
recover identity. Source acquisition and annotated diffs remain on-demand
later work.

## Demo and gates

The published Browser demo uses the deterministic `LibraryApiDiff.V1` and
`LibraryApiDiff.V2` Gallery fixtures:

1. inspect the V2 package and its exact fixture Library;
2. keep the default previous-release Diff target;
3. open Library Compare;
4. show the complete changed-Type inventory and aggregate compatibility
   counts;
5. select the same version as a neighboring target and show a successful empty
   comparison; and
6. show a missing or incomplete target as unavailable rather than equal.

| Gate | Adoption evidence |
| --- | --- |
| Release `BrowserLibraryApiDiffOperationTests` | Real V1-to-V2 and same-version results, exact asset mismatch, typed non-success, bounds, cancellation, and generated JSON shape. |
| Release `ProductionFacadeContextTests` and `generate-inspect-web-engine-facade.sh --check` | Existing Metadata facade exports and compiler-derived TypeScript transport. |
| Node Library API Diff tests | Target resolution, request association, complete row rendering, exact identities, non-success, and stale completion suppression. |
| Node ordinary Worker tests | Closed operation catalog, argument forwarding, cancellation forwarding, and bounded result transport. |
| Published Firefox package-adoption gate | Real Gallery fixture acquisition through the generated facade and WebAssembly engine. |

## Delivery and non-claims

This slice lands independently of retained Workspace Definition restoration
and the heavy-inspection multi-part-document work in
[#6980](https://github.com/richlander/dotnet-inspect/issues/6980). It consumes
the existing portable Library document and request-associated live Package
context.

It does not add Type or Member Compare, selected-Type detail inside Library,
Source comparison, Clone execution, platform/local package comparison,
portable comparison settings, a second live Workspace, a new Worker, a new
facade, a new matching algorithm, or a generalized comparison session.
