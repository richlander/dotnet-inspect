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
- **Empty Workspace.** The neutral empty plan also retains
  `ProductDefault(net11.0)`. Package loading and traversal remain valid before
  any Platform registration or realization. The equal default acts only as
  aligned target intent; it is not an implicit registration, acquisition
  authorization, or Platform receipt.
- **Configured Workspace.** A configured target such as `net10.0` is fixed at
  construction. Every traversal uses `net10.0`, and any Platform later
  composed with it must be a matching .NET 10 realization.
- **One target currency.** A traversal never carries an independent target
  beside the Workspace policy. A Platform receipt contributes membership,
  identity, generation, pruning, and binding evidence only after target
  correspondence is established.

`TargetFramework` is never absent on an admitted traversal. Configured text is
accepted only after the existing canonical NuGet framework parser validates
it. Malformed or padded input is a construction failure; it does not become
the product fallback.

The Workspace-plan owner retains the policy as reusable construction data. The
Platform realization owner supplies optional matching evidence, including its
canonical framework, exact family composition, identity, and generation. This
owner consumes those values but does not define plan retention, Platform-slot
realization, replacement, family composition, or acquisition. A mismatched
Platform receipt is typed non-success rather than permission to retarget the
Workspace. Platform-dependent evidence admitted against an earlier slot
generation cannot silently continue against its replacement.

## Current implementation gap

The current implementation already constructs `ProductDefault` or
`Configured` in `WorkspacePlan` and passes that retained value to Traversal.
That authority flow is correct and is required for empty-Workspace traversal.
However, `ProductDefaultTargetFramework` is currently `net12.0`; it must be
`net11.0`. Platform realization and Platform-dependent traversal composition
must also consume and validate the same Workspace target rather than selecting
or issuing a second one. This documentation change does not present those
corrections as shipped.

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
nuget.org. The deliberate inspection-only divergence is that an explicitly
selected root remains fixed even when the traversal target would select
another root asset. Results retain that mixed provenance and do not describe
it as one restored project.

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
| Empty Workspace with one loaded package | Use `ProductDefault(net11.0)` from construction; do not choose the package's highest TFM or imply Platform registration. |
| Configured `net10.0` Workspace | Use `net10.0` for every traversal edge; admit only matching Platform evidence. |
| First package exposes only `net6.0` or `netstandard2.0` | Retain its selected source provenance while the traversal target remains the Workspace target. |
| Platform realization is absent or unavailable | Continue package traversal under the Workspace target; Platform pruning or binding remains absent or visibly unavailable under its owning contract. |
| Platform receipt targets another framework | Reject the composition; do not retarget the Workspace or traversal. |
| Matching slot generation changes after Platform-dependent work is admitted | Reject or cancel that stale composition; do not join old Platform evidence to the replacement. |

## Adoption map

Issue #7423 is the end-to-end tracker. The original policy type and its
Package Traversal consumption have landed; correction proceeds in focused
slices:

1. Lock this Workspace-target authority and three-scenario contract.
2. Change `ProductDefaultTargetFramework` to `net11.0` while preserving the
   immutable policy retained by every `WorkspacePlan`.
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
| The default product Workspace and neutral empty Workspace both retain `ProductDefault(net11.0)`. | The plan-retention shape is covered by existing `WorkspacePlanTests`; the corrected constant and both construction paths are unverified until adoption slice 2 lands. |
| An empty Workspace can load a package and traverse it under `net11.0` without Platform realization. | Unverified until adoption slice 4 lands. |
| A configured `net10.0` Workspace uses `net10.0` for every destination edge and matching Platform composition. | Edge stability is covered by `Traversal_TargetPolicyIsStructuralCurrency`; Workspace/Platform correspondence is unverified until adoption slices 3-5 land. |
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
- Applying `net11.0` to Package Info or another selection operation.
- Selecting an already realized source participant's dependency group.
- Guessing a project target from an inspected assembly.
- Defining Platform-slot membership, family composition, or acquisition.
- Defining Workspace wire formats or host configuration syntax.
