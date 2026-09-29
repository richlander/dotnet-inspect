# Traversal target-framework policy

## Status and authority

This document is the focused owner of the host-neutral target-framework policy
used by traversal operations. The policy correction and its production
adoption are tracked by
[#7423](https://github.com/richlander/dotnet-inspect/issues/7423).

The earlier Workspace-default design and its first retention step landed
through #7352 and #7375. This owner narrows that value to traversal semantics
so selection consumers cannot mistake it for their policy. This revision
corrects the target's final authority: `net12.0` is fallback construction
intent, while an already realized Platform slot supplies the target for
Platform-aware traversal.

## Claim

> One traversal operation carries one validated canonical target framework.
> Platform-aware traversal obtains that target from the Workspace's one
> realized Platform slot. A traversal mode that intentionally does not
> traverse Platform dependencies may instead use an explicit configured
> target or the `net12.0` product fallback. The selected target governs every
> target-sensitive decision across that traversal.

The policy is:

```text
TraversalTargetFrameworkPolicy
  TargetFramework: canonical NuGet target-framework identity
  Source:
    RealizedPlatform(exact slot identity and generation)
    NoPlatformFallback(Configured | ProductDefault)
```

The exact public type names may change during adoption. These distinctions may
not:

- **Platform-aware traversal.** The one realized Platform slot is required and
  authoritative. Loading a .NET 11 Platform makes `net11.0` the traversal
  target. Loading the product-default Platform makes `net12.0` the target.
- **No-Platform traversal.** A future mode that explicitly excludes Platform
  dependencies requires an empty Platform slot. It uses one validated
  configured target when supplied and otherwise `ProductDefault(net12.0)`.
- **Unavailable Platform.** Failure to realize the required Platform slot is a
  typed non-success. Platform-aware traversal must not continue as though the
  empty slot selected the `net12.0` fallback.
- **One target currency.** A traversal never carries an independent configured
  target beside a realized Platform target. Host configuration either requests
  the Platform that will fill the slot or configures an explicit no-Platform
  operation.

`TargetFramework` is never absent on an admitted traversal. Configured text is
accepted only after the existing canonical NuGet framework parser validates
it. Malformed or padded input is a construction failure; it does not become
the product fallback.

The Workspace and Platform realization owners supply the optional slot,
including its canonical framework, exact family composition, identity, and
generation. This owner consumes that evidence only to issue the effective
traversal policy; it does not define slot realization, replacement, family
composition, or acquisition. Replacing the slot issues a new generation. An
operation admitted against an earlier slot cannot silently continue against
the replacement.

## Current implementation gap

The current implementation constructs `ProductDefault` or `Configured`
directly in `WorkspacePlan` and lets CLI and Browser callers pass that value to
Traversal. It does not bind the effective operation target to a realized
Platform slot. Until the adoption slices below land, loading a different
Platform therefore does not change Traversal's governing target. This is the
algorithmic gap this revision records; the documentation change does not
present the corrected behavior as shipped.

## Traversal and selection are different policies

A consumer declares whether its operation is Platform-aware traversal,
no-Platform traversal, or package-local selection before constructing its
request.

- Platform-aware traversal asks which realized Platform target governs a
  connected graph or relationship walk. Current call graphs and package
  dependency traversal use this mode.
- No-Platform traversal is reserved for a future operation that deliberately
  excludes Platform dependencies. No current production host exposes it.
- Package-local selection asks which one package slice should represent an
  isolated package question. Its default is PackageHouse's owner-issued
  `HighestAvailable` selection, not `net12.0`.

Package Info is a selection consumer. It reports the selected package slice and
does not consume the Platform slot's traversal target merely because a
Workspace retains one. An operation that needs both policies carries both
typed decisions; package-local selection is never inferred from the traversal
target.

## Governing-target invariant

The traversal target is owner-issued operation context, not participant
provenance. A source participant's declared or selected framework never
replaces it on a later edge. A compatible selected destination framework also
does not become the next target.

For example, a Workspace whose realized Platform slot targets `net11.0` may
traverse a package whose compatible selected assets are `net8.0`. The result
retains both values. The next package is still selected relative to
`net11.0`.

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
restore graph. A free-standing Platform-aware inspection traversal uses the
one realized Platform slot as its analogous governing context. This preserves
the ordinary expectation that Platform pruning, Platform assembly binding, and
package destination selection describe the same target.

`net12.0` remains a product constant, not the executing SDK version, the newest
installed Platform, or the highest framework found in a package. It requests
the ordinary default Platform and supplies the fallback only for an explicit
no-Platform traversal. The deliberate inspection-only divergence is that an
explicitly selected root remains fixed even when the traversal target would
select another root asset. Results retain that mixed provenance and do not
describe it as one restored project.

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
  whose canonical target identity depends on the realized Platform rather than
  the root package's selected framework.

These packages require physical source selection, source dependency evidence,
and the graph-wide traversal target to remain separate typed facts.

## Pathological cases

| Workspace and operation state | Required traversal behavior |
| --- | --- |
| Ordinary default Platform realizes at .NET 12 | Use the slot-issued `net12.0`; retain independently selected package-root frameworks. |
| User replaces the Platform slot with .NET 11 | Use `net11.0` for destination selection, pruning, and Platform binding; do not retain `net12.0` as a second target. |
| Required Platform realization is unavailable | Return typed non-success before traversal; do not reinterpret the empty slot as no-Platform mode. |
| Future operation explicitly excludes Platform dependencies and leaves the slot empty | Use one configured target or `net12.0` fallback for package traversal; perform no Platform pruning or binding. |
| Slot generation changes after operation admission | Reject or cancel stale work; do not join old traversal evidence to the replacement Platform. |

## Adoption map

Issue #7423 is the end-to-end tracker. The original policy type and its
Package Traversal consumption have landed; correction proceeds in focused
slices:

1. Lock this Platform-slot authority and no-Platform fallback contract.
2. Have Workspace realization expose one detached current Platform-slot
   target and generation. Reclassify `WorkspacePlan.TraversalTargetPolicy` as
   construction intent rather than the final effective operation target.
3. Bind Package Dependency Traversal and call-graph operation admission to the
   slot-issued policy. Missing required Platform evidence remains visible.
4. Compose Platform pruning and assembly-reference binding from the same slot
   receipt; no consumer reconstructs the target from TFM text.
5. Adopt the corrected operation formation in CLI and Browser/Wasm. A
   configured Platform target fills or replaces the slot; package-local TFM
   selection remains independent.
6. If a no-Platform traversal mode is introduced, expose it explicitly and
   use the configured or `net12.0` fallback only in that mode.

PackageHouse selection, Package Info measurements, all-library aggregation, and
host aggregate navigation are separate #7423 slices. They do not adopt this
policy merely because they inspect packages.

## Required gates

| Property | Release gate |
| --- | --- |
| The ordinary default realizes the .NET 12 Platform slot and traversal uses its `net12.0` target. | Unverified until adoption slices 2-5 land. |
| Loading a .NET 11 Platform causes Package Traversal, pruning, and Platform binding to use `net11.0`. | Unverified until adoption slices 2-5 land. |
| A missing required Platform slot produces typed non-success rather than `net12.0` fallback. | Unverified until adoption slices 2-5 land. |
| A future explicit no-Platform traversal with no configured target uses `ProductDefault(net12.0)`. | Unverified; no production no-Platform mode exists. |
| Slot replacement invalidates or cancels work admitted against an earlier generation. | Unverified until adoption slice 2 lands. |
| One slot-issued traversal target governs every destination edge without substitution from selected asset frameworks. | Existing `Traversal_TargetPolicyIsStructuralCurrency` and `Traversal_RealizedPollyContextsPreservePackageSelectionUnderDefaultTarget` gate edge stability; slot correspondence is unverified until adoption. |
| Package-local selection remains independent and defaults to `HighestAvailable`. | Existing `PackageCompileAssetSelectorTests` and `PackageHouse` contract tests owned by package asset-selection correspondence. |
| CLI and Browser/Wasm form equal effective policies from equivalent slot state. | Unverified until host adoption lands. |

## Non-goals

- Redefining NuGet compatibility or package asset selection.
- Selecting a package's highest framework for traversal.
- Applying `net12.0` to Package Info or another selection operation.
- Selecting an already realized source participant's dependency group.
- Guessing a project target from an inspected assembly.
- Defining Platform-slot membership, family composition, or acquisition.
- Adding the hypothetical no-Platform traversal mode.
- Defining Workspace wire formats or host configuration syntax.
