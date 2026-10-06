# Traversal target-framework policy

## Status and authority

This document is the focused owner of the host-neutral target-framework policy
used by traversal operations. The policy correction and its production
adoption are tracked by
[#7423](https://github.com/richlander/dotnet-inspect/issues/7423).

The earlier Workspace-default design and its first retention step landed
through #7352 and #7375. This owner narrows that value to traversal semantics
so selection consumers cannot mistake it for their policy. This revision
corrects both the product default and the target's final authority:
`WorkspacePlan.TraversalTargetPolicy` supplies the target before any package
or Platform is realized, and its product default is `net11.0`.

## Claim

> One Workspace carries one validated canonical traversal target from
> construction. Every traversal admitted by that Workspace uses the same
> target whether its Package scope is empty or populated and whether a
> Platform has been realized. Any Platform evidence composed into the
> traversal must match that target; it cannot create or replace it.

The policy is:

```text
WorkspacePlan.TraversalTargetPolicy
  TargetFramework: canonical NuGet target-framework identity
  Source:
    ProductDefault(net11.0)
    Configured

Product-default .NET release line
  Traversal target: net11.0
  Product Workspace .NET version: exact 11.0.*

ReleaseLine(ProductDefaultTargetFramework)
  == ReleaseLine(ProductDefaultWorkspace.DotNetVersion)

Traversal operation
  Target: exact retained Workspace policy
  Platform evidence:
    Absent
    Matching(exact slot identity and generation)
```

The exact public type names may change during adoption. These distinctions may
not:

- **Default product Workspace.** The product-curated plan retains
  `ProductDefault(net11.0)` and registers the .NET Runtime Ecosystem. A host
  with package-source authority can realize its matching .NET 11 Platform from
  nuget.org.
- **Product-default correspondence.** `ProductDefaultTargetFramework` and the
  product-default Workspace .NET version derive from one product-owned release
  line. `net11.0` may pair only with an exact `11.0.*` Platform version.
  Servicing patch, feature band, and prerelease status may vary within that
  release line; another major or minor version cannot be relabeled as the
  default Workspace Platform.
- **Empty Workspace.** The neutral empty plan also retains
  `ProductDefault(net11.0)`. Package loading and traversal remain valid before
  any Platform registration or realization. The equal default acts only as
  aligned target intent; it is not an implicit registration, acquisition
  authorization, or Platform receipt.
- **Configured Workspace.** A configured target such as `net10.0` or
  `net12.0` is fixed at construction. Every traversal uses that target, and
  any Platform later composed with it must match. A future target unavailable
  through the product's default package source may still use explicitly
  authorized evidence such as a local daily-build layout.
- **One target currency.** A traversal never carries an independent target
  beside the Workspace policy. A Platform receipt contributes membership,
  identity, generation, pruning, and binding evidence only after target
  correspondence is established.

`TargetFramework` is never absent on an admitted traversal. Configured text is
accepted only after the existing canonical NuGet framework parser validates
it. Malformed or padded input is a construction failure; it does not become
the product fallback.

The product default release line is singular configuration currency, not two
equal literals maintained by Traversal and Workspace. Changing the product
default changes both projections together. If the authorized Platform sources
cannot settle an exact version in that release line, product-default Platform
realization remains visibly unavailable; it does not select another release
line or change `ProductDefaultTargetFramework`.

The Workspace-plan owner retains the policy as reusable construction data. The
Platform realization owner supplies optional matching evidence, including its
canonical framework, exact family composition, identity, and generation. This
owner consumes those values but does not define plan retention, Platform-slot
realization, replacement, family composition, or acquisition. A mismatched
Platform receipt is typed non-success rather than permission to retarget the
Workspace. Platform-dependent evidence admitted against an earlier slot
generation cannot silently continue against its replacement.

## Current implementation status

`ProductDotNetReleaseLine` owns the singular product-default `net11.0`
currency. `WorkspacePlan` constructs `ProductDefault(net11.0)` from it or
retains a caller-supplied `Configured` policy and passes that value to
Traversal. The Platform catalog generator consumes the same release-line
value, and the Browser consumes the generated catalog default rather than
maintaining another default literal. Product gates require the default and
empty plans, the curated Browser Workspace, and the shipped catalog's exact
`11.0.*` target to correspond.

Platform realization and Platform-dependent traversal composition must still
consume and validate the Workspace target rather than selecting or issuing a
second one.

## Traversal and selection are different policies

A consumer declares whether its operation is traversal or package-local
selection before constructing its request.

- Traversal uses the Workspace's retained policy for a connected graph or
  relationship walk. Platform participation is an independent composition
  choice and does not determine whether the target exists.
- Package-local selection asks which one package slice should represent an
  isolated package question. Its default is PackageHouse's owner-issued
  `HighestAvailable` selection, not `net11.0`.

Package Info is a selection consumer. It reports the selected package slice and
does not consume the Workspace traversal target merely because the package is
loaded into a Workspace. An operation that needs both policies carries both
typed decisions; package-local selection is never inferred from the traversal
target.

## Governing-target invariant

The traversal target is owner-issued operation context, not participant
provenance. A source participant's declared or selected framework never
replaces it on a later edge. A compatible selected destination framework and a
realized Platform target also do not become the next target.

For example, a default Workspace targets `net11.0` before it loads a package.
It may traverse a package whose compatible selected assets are `net8.0`. The
result retains both values. The next package is still selected relative to
`net11.0`, with or without a realized Platform.

Explicitly chosen root subjects remain the subjects the user selected. The
traversal target governs newly reached participants; it does not silently
replace a root selected under another framework or turn a mixed inspection
graph into a restore claim.

This owner supplies the target only. Adjacent owners retain:

- framework compatibility, nearest-match, ambiguity, and no-match behavior;
- package compile and runtime slice selection;
- dependency-group selection and source-participant association;
- dependency and call-graph expansion, completion, and failures;
- restored-project target authority;
- Workspace lifetime, Platform-slot realization, definitions, and
  serialization; and
- CLI and Browser/Wasm gestures and presentation.

## Conventional basis and deliberate divergence

NuGet restore uses one project target framework to select assets throughout a
restore graph. A free-standing Workspace likewise establishes one target
before selecting roots or traversing their relationships. Platform pruning,
Platform assembly binding, and package destination selection may then compose
against that same target when their evidence is available.

`net11.0` is the product default because it matches the current
ecosystem-provided .NET Platform that package-backed acquisition can realize.
It is not the executing SDK version, the newest installed Platform, or the
highest framework found in the first package. `net12.0` is not the default
while the product cannot realize a corresponding default Platform from
nuget.org. That does not prohibit direct `net12.0` package or Library
Selection, or an explicitly configured `net12.0` Workspace backed by an
authorized local Platform layout. The deliberate inspection-only divergence
is that an explicitly selected root remains fixed even when the traversal
target would select another root asset. Results retain that mixed provenance
and do not describe it as one restored project.

## Motivating real assets

Cross-TFM package evidence shows why source-selected frameworks cannot govern
later traversal:

- `NodaTime@3.2.2` exposes materially more API in `net8.0` than in
  `netstandard2.0`.
- `Polly.Core@8.8.0` has four declarations in its `netstandard2.0` dependency
  group and none in its `net8.0` group.
- `Microsoft.Extensions.Telemetry@8.0.0` has different dependency declarations
  in its `net6.0` and `net8.0` groups.
- `Microsoft.Azure.SignalR@1.33.1/net8.0` references ASP.NET Core assemblies
  whose Platform binding must correspond to the Workspace target rather than
  the root package's selected framework.

These packages require physical source selection, source dependency evidence,
and the graph-wide traversal target to remain separate typed facts.

## Pathological cases

| Workspace and operation state | Required traversal behavior |
| --- | --- |
| Default product Workspace before Platform realization | Use `ProductDefault(net11.0)` for package traversal; the .NET Runtime registration may later realize only a matching Platform. |
| Product-default Workspace selects exact .NET `11.0.*` | Accept release-line correspondence with `ProductDefault(net11.0)` while retaining the exact Platform version independently. |
| Product-default Workspace observes only .NET 10 or .NET 12 candidates | Leave Platform realization visibly unavailable; do not retarget Traversal or relabel another release line as the default. |
| Empty Workspace with one loaded package | Use `ProductDefault(net11.0)` from construction; do not choose the package's highest TFM or imply Platform registration. |
| Configured `net10.0` Workspace | Use `net10.0` for every traversal edge; admit only matching Platform evidence. |
| Default or empty `net11.0` Workspace directly selects a `net12.0` package or Library | Load the exact root without changing the Workspace target; do not traverse or bind .NET 12 Platform APIs as though matching Platform evidence existed. |
| Configured `net12.0` Workspace without matching Platform evidence | Use `net12.0` for package traversal, while .NET 12 Platform pruning and API binding remain absent or visibly unavailable. |
| Configured `net12.0` Workspace with an authorized matching local layout | Admit that Platform evidence and use `net12.0` consistently for package traversal, pruning, and Platform API binding. |
| First package exposes only `net6.0` or `netstandard2.0` | Retain its selected source provenance while the traversal target remains the Workspace target. |
| Platform realization is absent or unavailable | Continue package traversal under the Workspace target; Platform pruning or binding remains absent or visibly unavailable under its owning contract. |
| Platform receipt targets another framework | Reject the composition; do not retarget the Workspace or traversal. |
| Matching slot generation changes after Platform-dependent work is admitted | Reject or cancel that stale composition; do not join old Platform evidence to the replacement. |

## Adoption map

Issue #7423 is the end-to-end tracker. The original policy type and its
Package Traversal consumption have landed; correction proceeds in focused
slices:

1. Lock this Workspace-target authority and three-scenario contract.
2. Define one product-default .NET release-line value, derive
   `ProductDefaultTargetFramework(net11.0)` and product Workspace Platform
   selection from it, and preserve the immutable policy retained by every
   `WorkspacePlan`.
3. Make default and configured Platform realization consume the Workspace
   target and reject a non-corresponding result.
4. Keep Package Dependency Traversal and call-graph admission valid without a
   realized Platform; every destination edge continues to use the retained
   Workspace policy.
5. Compose Platform pruning and assembly-reference binding only from a
   matching slot receipt and generation; no consumer reconstructs a target
   from Platform or package display text.
6. Adopt equal target formation and correspondence checks in CLI and
   Browser/Wasm while keeping package-local TFM selection independent.

PackageHouse selection, Package Info measurements, all-library aggregation, and
host aggregate navigation are separate #7423 slices. They do not adopt this
policy merely because they inspect packages.

## Required gates

| Property | Release gate |
| --- | --- |
| The default product Workspace and neutral empty Workspace both retain `ProductDefault(net11.0)`. | `WorkspacePlanTests.EmptyPlanIsReusableWithoutSharingLiveIdentity` and `BrowserProductHomeDemosTests.ProductDefaultWorkspaceAndShippedPlatformCatalogShareReleaseLine`. |
| `ProductDefaultTargetFramework` and the product-default Workspace .NET version always share one release line; `net11.0` accepts exact `11.0.*` and rejects .NET 10/12 candidates without retargeting. | Default/catalog correspondence is gated by `BrowserProductHomeDemosTests.ProductDefaultWorkspaceAndShippedPlatformCatalogShareReleaseLine`; `parsePlatformIndex` and its TypeScript tests reject a default without a matching shipped target, while `BrowserPlatformCatalogTests` gate exact-version release-line matching. Realization-time correspondence remains unverified until adoption slice 3 lands. |
| An empty Workspace can load a package and traverse it under `net11.0` without Platform realization. | Unverified until adoption slice 4 lands. |
| A configured `net10.0` Workspace uses `net10.0` for every destination edge and matching Platform composition. | Edge stability is covered by `Traversal_TargetPolicyIsStructuralCurrency`; Workspace/Platform correspondence is unverified until adoption slices 3-5 land. |
| Direct `net12.0` Selection under a default `net11.0` Workspace does not retarget traversal or manufacture .NET 12 Platform evidence. | Unverified until adoption slices 4-6 land. |
| A configured `net12.0` Workspace composes an authorized matching local Platform layout, while the same Workspace without that evidence cannot traverse .NET 12 Platform APIs. | Unverified until adoption slices 3-6 land. |
| Platform absence does not prevent package traversal or imply Platform evidence. | Unverified until adoption slice 4 lands. |
| A mismatched Platform receipt cannot replace the Workspace target. | Unverified until adoption slices 3 and 5 land. |
| Matching-slot replacement invalidates or cancels Platform-dependent work admitted against an earlier generation. | Unverified until adoption slice 5 lands. |
| One Workspace-issued traversal target governs every destination edge without substitution from selected asset frameworks. | Existing `Traversal_TargetPolicyIsStructuralCurrency` and `Traversal_RealizedPollyContextsPreservePackageSelectionUnderDefaultTarget` gate edge stability; full Workspace admission is unverified until adoption. |
| Package-local selection remains independent and defaults to `HighestAvailable`. | Existing `PackageCompileAssetSelectorTests` and `PackageHouse` contract tests owned by package asset-selection correspondence. |
| CLI and Browser/Wasm form equal policies from equivalent Workspace plans. | Unverified until host adoption lands. |

## Non-goals

- Redefining NuGet compatibility or package asset selection.
- Selecting a package's highest framework for traversal.
- Implicitly registering or acquiring a Platform for an empty Workspace.
- Prohibiting direct Selection or explicit Workspace configuration for a
  future target merely because the product default cannot acquire its
  Platform.
- Applying `net11.0` to Package Info or another selection operation.
- Selecting an already realized source participant's dependency group.
- Guessing a project target from an inspected assembly.
- Defining Platform-slot membership, family composition, or acquisition.
- Defining Workspace wire formats or host configuration syntax.
