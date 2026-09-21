# Traversal target-framework policy

## Status and authority

This document is the focused owner of the host-neutral target-framework policy
used by traversal operations. The policy correction and its production
adoption are tracked by
[#7423](https://github.com/richlander/dotnet-inspect/issues/7423).

The earlier Workspace-default design and its first retention step landed
through #7352 and #7375. This owner narrows that value to traversal semantics,
renames it so selection consumers cannot mistake it for their policy, and
changes the product default from `net11.0` to `net12.0`.

## Claim

> One traversal operation carries one validated canonical target framework.
> The product default is `net12.0`. The same target governs every
> target-sensitive selection across the traversal unless the operation was
> constructed with an explicit configured target.

The policy is:

```text
TraversalTargetFrameworkPolicy
  TargetFramework: canonical NuGet target-framework identity
  Source: ProductDefault | Configured
```

`TargetFramework` is never absent. Omitted configuration constructs
`ProductDefault(net12.0)`. Explicit configuration constructs `Configured` only
after the existing canonical NuGet framework parser accepts the value.
Malformed or padded input is a construction failure; it does not become the
product default.

The immutable, resource-free policy may be retained in a reusable
`WorkspacePlan`. Every independently constructed Workspace receives the same
policy value, and registration replacement preserves the exact instance.

## Traversal and selection are different policies

A consumer declares whether its operation is traversal or package-local
selection before constructing its request.

- Traversal asks which target governs a connected graph or relationship walk.
  Call graphs and package dependency traversal use this policy.
- Package-local selection asks which one package slice should represent an
  isolated package question. Its default is PackageHouse's owner-issued
  `HighestAvailable` selection, not `net12.0`.

Package Info is a selection consumer. It reports the selected package slice and
does not consume this traversal target merely because a Workspace retains one.
An operation that needs both policies carries both typed decisions; neither
value is inferred from the other.

## Governing-target invariant

The traversal target is operation construction intent, not participant
provenance. A source participant's declared or selected framework never
replaces it on a later edge. A compatible selected destination framework also
does not become the next target.

For example, a traversal governed by `net12.0` may select a package's `net8.0`
assets. The result retains both values. The next package is still selected
relative to `net12.0`.

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
- Workspace lifetime, definitions, and serialization; and
- CLI and Browser/Wasm gestures and presentation.

## Conventional basis and deliberate divergence

NuGet restore uses one project target framework to select assets throughout a
restore graph. A free-standing inspection traversal has no project target, so
it uses one explicit product or configured target as the analogous governing
input.

`net12.0` is a product constant, not the executing SDK version, the newest
installed Platform, or the highest framework found in a package. The deliberate
inspection-only divergence is that an explicitly selected root remains fixed
even when the traversal target would select another root asset. Results retain
that mixed provenance and do not describe it as one restored project.

## Motivating real assets

Cross-TFM package evidence shows why source-selected frameworks cannot govern
later traversal:

- `NodaTime@3.2.2` exposes materially more API in `net8.0` than in
  `netstandard2.0`.
- `Polly.Core@8.8.0` has four declarations in its `netstandard2.0` dependency
  group and none in its `net8.0` group.
- `Microsoft.Extensions.Telemetry@8.0.0` has different dependency declarations
  in its `net6.0` and `net8.0` groups.

These packages require physical source selection, source dependency evidence,
and the graph-wide traversal target to remain separate typed facts.

## Adoption map

Issue #7423 is the end-to-end tracker:

1. This slice defines `TraversalTargetFrameworkPolicy`, adopts it in
   `WorkspacePlan`, and removes the ambiguous Workspace-default naming.
2. Package Dependency Traversal consumes one governing target and retires
   per-manifest default selection as its ordinary mode.
3. Call-graph operations declare traversal semantics and consume the same
   governing-target contract.
4. The CLI configures and preserves the host-neutral policy.
5. Browser/Wasm configures and preserves the same host-neutral policy. Inspect
   Web's package call-graph page exposes that choice independently from its
   package-local TFM selector and defaults it to `ProductDefault(net12.0)`.

PackageHouse selection, Package Info measurements, all-library aggregation, and
host aggregate navigation are separate #7423 slices. They do not adopt this
policy merely because they inspect packages.

## Required gates

| Property | Release gate |
| --- | --- |
| Omitted configuration produces exactly `ProductDefault(net12.0)`; configured values canonicalize; malformed and padded values fail. | `WorkspacePlanTests.EmptyPlanIsReusableWithoutSharingLiveIdentity`, `ExplicitTraversalTargetPolicyIsCanonicalReusableConstructionIntent`, and `PlanConstructionRejectsTheWholeInvalidSet` |
| Reusing a construction plan preserves the exact policy in independent Workspaces and registration replacement. | `WorkspacePlanTests.EmptyPlanIsReusableWithoutSharingLiveIdentity` and `ReplacementChangesOneLivePlanWithoutMutatingSharedData` |
| One traversal target governs every destination edge without substitution from selected asset frameworks. | `Traversal_TargetPolicyIsStructuralCurrency`; `Traversal_RealizedPollyContextsPreservePackageSelectionUnderDefaultTarget`. |
| Package-local selection remains independent and defaults to `HighestAvailable`. | Existing `PackageCompileAssetSelectorTests` and `PackageHouse` contract tests owned by package asset-selection correspondence. |
| CLI and Browser/Wasm retain equal configured traversal policy. | Unverified until host adoption lands. |

## Non-goals

- Redefining NuGet compatibility or package asset selection.
- Selecting a package's highest framework for traversal.
- Applying `net12.0` to Package Info or another selection operation.
- Selecting an already realized source participant's dependency group.
- Guessing a project target from an inspected assembly.
- Defining Workspace wire formats or host configuration syntax.
