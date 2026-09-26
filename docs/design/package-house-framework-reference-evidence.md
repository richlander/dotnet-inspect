# PackageHouse framework-reference evidence

## Status and authority

This document specifies the PackageHouse-owned projection that associates one
package's nuspec `<frameworkReferences>` declarations with one exact acquired
compile settlement. Implementation is tracked by
[#8504](https://github.com/richlander/dotnet-inspect/issues/8504) as a
prerequisite of
[#8466](https://github.com/richlander/dotnet-inspect/issues/8466).
It is a focused adoption slice of the eleven-step PackageHouse migration in
[#6426](https://github.com/richlander/dotnet-inspect/issues/6426).

[PackageHouse](package-house.md) owns the request, settlement, acquisition
receipt, compile realization, and package-content lifetime. The focused
[package-manifest framework-reference facts](https://github.com/richlander/dotnet-inspect/issues/8513)
effort owns bounded raw nuspec projection. This owner composes those facts; it
does not redefine manifest parsing, platform correspondence, assembly-route
precedence, Workspace replacement, or host presentation.

The legacy
[Realized Package Dependency Context](realized-package-dependency-context.md)
does not gain framework-reference evidence. Consumers are moving to
PackageHouse requests, settlements, and receipts; no new behavior depends on
the legacy Root reacquisition path.

The logical owner may span package projects as allowed by PackageHouse.
[#6432](https://github.com/richlander/dotnet-inspect/issues/6432) owns the
physical `DotnetInspector.Queries`/`DotnetInspector.PackageQueries`
disposition. Placement must not make Queries the owner of package acquisition
or create a second package-policy path.

## Claim

> A PackageHouse consumer can obtain resource-free framework-reference
> evidence projected from the exact package generation acquired by one compile
> settlement and selected under that settlement's owner-issued target policy.

The evidence retains the exact `PackageHouseResult`,
`PackageHouseAcquisitionReceipt`, and
`PackageHouseRealizationReceipt.Compile`. Equal package coordinates, target
text, independently repeated selections, `PackageRootBinding`, or reconstructed
host state cannot substitute for that association.

Framework-reference groups and compile assets are separate candidate
populations. The House target policy supplies their common selection basis,
but neither selected group is inferred from the other.

## Consumer and production path

The motivating consumer is Browser/Wasm package workspace construction for
`Microsoft.Azure.SignalR@1.33.1`. The CLI and Browser are moving to the same
PackageHouse-based package APIs:

```text
PackageHouseRequest
  -> PackageHouseSettlement.Acquired
     -> PackageHouseFrameworkReferenceProjection
        -> package framework-reference evidence
           -> Assembly Reference Resolution Ladder
              -> immutable Workspace replacement
```

Hosts form a typed House request and consume the shared projection. They do not
parse nuspec XML, choose a framework-reference group, reconstruct a settlement
from package coordinates, or route an AssemblyRef directly from a framework
name.

The projection is analogous to
`PackageHouseCompileSliceMeasurementProjection`: it runs while an acquired
settlement keeps the exact payload alive, then returns only resource-free
evidence associated with the settlement's receipts.

## Input and construction

The projection accepts one `PackageHouseSettlement.Acquired` whose result
retains a `PackageHouseRealizationReceipt.Compile`. Construction obtains all
authority from that settlement:

- `PackageHouseResult.Request` supplies the exact demand, operation, target
  context, and request association;
- `PackageHouseAcquisitionReceipt` supplies the exact candidate, authority,
  producer, and `PackageContentGenerationIdentity`;
- `PackageHouseRealizationReceipt.Compile` supplies the exact compile
  selection policy and outcome; and
- `settlement.Payload.Content` supplies the live package generation from which
  the root nuspec is read.

The projection verifies the ordinary House correspondence already required
between those values. It accepts no separately acquired manifest, caller-made
package coordinate, target string, compile selection, or framework-reference
group.

The package-manifest owner selects the one root nuspec entry and projects its
bounded facts from that exact content. Its successful framework-reference arm
contains every declared group in source order, including valid empty groups.
Its failed arm remains failure. This projection never retries against a feed,
another cache entry, or another package generation.

## Explicit read demand

A complete payload already contains the root nuspec. A ranged PackageHouse
realization may otherwise materialize only selected assembly entries.
Framework-reference evidence therefore requires an explicit House request
demand, `PackageHouseEvidenceDemand.FrameworkReferences`, that adds the root
nuspec to ranged-read planning.

The demand:

- is valid only for a `Realize` operation with compile asset selection;
- authorizes bounded reading and projection of the root nuspec;
- does not authorize another package-source operation or a complete archive
  fallback merely because projection was requested; and
- remains visible on the exact `PackageHouseRequest` retained by the result.

If the package directory has no unique root nuspec, the entry was not
materialized under the request, or bounded reading fails, the projection
returns typed non-success. It does not silently perform a second acquisition.

This demand is additive package evidence, not an asset role. It does not change
compile or implementation selection and is not represented as another
`PackageAssetDemand` value.

## Target selection

Framework-reference selection uses the House request and compile realization,
not a legacy `RootRequest`.

For `PackageHouseTargetContext.Exact(requestedFramework)`, the requested
framework is the selection target. The framework-reference group owner applies
NuGet nearest-framework reduction over its own group population. A compatible
group is therefore valid under the same explicit-target policy that
PackageHouse already applies to compile assets. The selected framework-reference
group and selected compile slice may differ because their candidate
populations differ.

For `PackageHouseTargetContext.OwnerDefault`, or the existing equivalent
request with no target context, the compile realization's selected target
framework becomes the framework-reference selection target. This preserves one
owner-issued package variant without independently ranking external-platform
declarations. If the compile realization selected no target framework, the
framework-reference outcome is `TargetUnavailable`.

The selected compile target is used only to complete owner-default intent. It
does not replace an explicit requested framework. For example, an explicit
`net12.0` request may select a `net8.0` compile slice while independently
selecting a `net10.0` framework-reference group when NuGet compatibility makes
both choices nearest in their respective populations.

Equal target-framework group occurrences retain source order. When the
manifest owner reports case-insensitive duplicate framework-reference names,
the selected evidence retains its deterministic NuGet-compatible identity and
source spelling.

## Result algebra

Conceptually:

```text
PackageHouseFrameworkReferenceOutcome
  NotRequested(Association)
  Selected(Evidence)
  NoFrameworkReferenceGroups(Association)
  NoMatchingTargetFramework(Association)
  TargetUnavailable(Association)
  ManifestUnavailable(Association, Reason)
  HouseFailed(Association)
  Failed(Association, Failure)

PackageHouseFrameworkReferenceAssociation
  Result: exact PackageHouseResult
  Acquisition: exact PackageHouseAcquisitionReceipt
  Realization: exact PackageHouseRealizationReceipt.Compile
  Generation: exact PackageContentGenerationIdentity
  TargetBasis:
    Exact(string)
    CompileSelection(string)
    Unavailable

PackageHouseFrameworkReferenceEvidence
  Association: PackageHouseFrameworkReferenceAssociation
  Groups: complete ordered package-manifest groups
  SelectedOccurrence: exact source occurrence
  SelectedTargetFramework: canonical NuGet framework identity
  References: selected ordered framework-reference identities and spellings
```

`Selected` includes an explicit empty group. That state is not
`NoFrameworkReferenceGroups` and does not fall through to another target's
non-empty group.

`NoMatchingTargetFramework` means a real selection target existed but no
manifest group was compatible. `TargetUnavailable` means owner-default compile
realization produced no selected target. Neither is a manifest failure.

`NotRequested` means the exact House request did not demand framework-reference
evidence; calling the shared projection cannot retroactively broaden that
request. `HouseFailed` preserves a terminal House failure after acquisition,
including an operation timeout. Neither arm opens the manifest.

`ManifestUnavailable` covers absence or unreadability of the exact root
manifest under the House request. `Failed` preserves a manifest-projection or
selection failure. Neither becomes an empty selected group.

The PackageHouse settlement remains independently usable when framework
projection fails. A consumer whose operation requires external assembly
resolution must treat every outcome except `Selected` as unable to authorize a
framework route. Other consumers may continue to use the unchanged House
result.

Projection order is deterministic:

1. validate the exact acquired compile-settlement association;
2. return `NotRequested` when the request contains no framework-evidence
   demand;
3. return `HouseFailed` for a terminal House failure;
4. select and read the exact root nuspec under the request's bounds;
5. preserve manifest projection non-success;
6. return `NoFrameworkReferenceGroups` before target selection when the
   manifest declares no groups;
7. derive the requested or selected-compile target basis; and
8. select the nearest framework-reference group or return the corresponding
   target/no-match outcome.

`NoMatch` or `Rejected` compile realization does not by itself erase valid
framework-reference declarations. Framework-only or otherwise asset-empty
packages may still carry the declaration that explains their external
framework supply. The projection retains the complete House result so route
composition can apply its own source-admission requirements without rewriting
that result.

## Association and lifetime

Reference equality with the exact House result and receipts is the
construction-time association. The result's acquisition receipt and the live
payload must name the same `PackageContentGenerationIdentity`; the compile
realization must be the result's exact realization.

The completed projection retains no payload, package content, stream, path,
Workspace, source lease, callback, opener, or credential. It remains valid
historical evidence after the acquired settlement and payload retire, but it
cannot reopen them or transfer to another settlement.

Re-executing an equal logical request produces another House request,
settlement, and projection. Equal coordinates or target text do not make the
old projection evidence for the new execution. PackageHouse and its existing
receipts decide whether a retained generation is reused or replaced.

## Route handoff and non-claims

The selected names are package-authored framework-reference evidence only.
`Microsoft.AspNetCore.App` does not by itself prove:

- an `aspnetcore` platform family;
- a targeting or runtime pack;
- a Platform Library;
- any assembly membership;
- an applicable platform route; or
- route precedence over a retained package dependency.

The [Assembly Reference Resolution
Ladder](assembly-reference-resolution-ladder.md) consumes this evidence only
as framework-family eligibility. Exact platform correspondence, package
pruning, and the requested AssemblyRef's membership in one Platform Library
remain separate owner-issued inputs. The ladder applies package pruning before
platform applicability.

The nuspec `<frameworkAssemblies>` element is a legacy .NET Framework assembly
declaration and is not shared-framework evidence.

## Motivating and pathological cases

`Microsoft.Azure.SignalR@1.33.1` declares:

- a `net8.0` framework-reference group containing
  `Microsoft.AspNetCore.App`; and
- an explicit empty `.NETStandard2.0` framework-reference group.

An exact-target House request for `net8.0` therefore projects the non-empty
group. An exact-target request for `netstandard2.0` projects the explicit empty
group and does not inherit `net8.0`.

The overlapping-package case remains the route-pathological case. If the same
inspection also retains a `System.Text.Json` package edge, framework evidence
cannot override it. PackageHouse pruning must first determine whether the
package edge remains package-owned or delegates to Platform; only delegated or
absent package ownership permits the ladder to consider exact Platform Library
membership. Package versions and assembly versions are never compared.

## Conventional basis and deliberate choices

NuGet's
[`NuspecUtility.GetFrameworkReferenceGroups`](https://github.com/NuGet/NuGet.Client/blob/eb4a66f19ad6cbc62fda8be20b7bdc8c72746efd/src/NuGet.Core/NuGet.Packaging/Core/NuspecUtility.cs)
parses target-specific groups and compares framework-reference names
ordinal-ignore-case. Restore's
[`LockFileUtils.AddFrameworkReferences`](https://github.com/NuGet/NuGet.Client/blob/eb4a66f19ad6cbc62fda8be20b7bdc8c72746efd/src/NuGet.Core/NuGet.Commands/RestoreCommand/Utility/LockFileUtils.cs)
uses `GetNearest(projectFramework)` to choose a group.

The explicit-target House path follows that nearest-framework behavior. The
owner-default path has no project framework, so it deliberately uses the
compile realization's selected target rather than independently choosing the
highest framework-reference group. This keeps the external-platform
declaration attached to the package variant PackageHouse actually realized.

The projection preserves the selected occurrence and exact House receipts
because dotnet-inspect detaches evidence from live package content and composes
it later with platform and Workspace evidence.

## Required gates

| Property | Required Release gate |
| --- | --- |
| The projection accepts only an acquired compile settlement and retains its exact result, acquisition receipt, realization receipt, and generation. | `FrameworkReferences_RequireExactHouseCompileSettlement` |
| The projection reads the root nuspec from the settlement's exact payload and rejects separately supplied facts or another generation. | `FrameworkReferences_ProjectExactHousePayload` |
| A ranged request for framework evidence materializes the bounded root nuspec without broadening to a complete archive read. | `FrameworkReferences_RangedHouseReadIncludesOnlyDemandedManifest` |
| `Microsoft.Azure.SignalR@1.33.1` selects `Microsoft.AspNetCore.App` for explicit `net8.0` and the explicit empty group for `netstandard2.0`. | `MicrosoftAzureSignalR_HouseFrameworkReferencesFollowTargetContext` |
| Explicit-target selection applies NuGet compatibility independently over framework-reference groups. | `FrameworkReferences_ExplicitHouseTargetUsesNearestGroup` |
| Owner-default selection uses the compile realization's selected target and returns `TargetUnavailable` when no target was selected. | `FrameworkReferences_OwnerDefaultUsesSelectedCompileTarget` |
| Not requested, selected empty, no groups, no match, target unavailable, manifest unavailable, House failure, and projection failure remain distinct. | `FrameworkReferences_PreserveClosedHouseProjectionOutcomes` |
| Framework-reference identity is ordinal-ignore-case while source spelling, group order, and selected occurrence remain visible. | `FrameworkReferences_PreserveNuGetIdentityAndOccurrence` |
| Projection failure leaves the exact House settlement usable but cannot authorize a framework route. | `FrameworkReferences_FailureDoesNotRewriteHouseResult` |
| A compile `NoMatch` caused by no local assets does not erase valid framework-reference evidence. | `FrameworkOnlyPackage_PreservesHouseFrameworkReferences` |
| Framework evidence grants no platform family, Library, assembly, pruning, or precedence claim. | `FrameworkReferences_RemainPackageEvidenceOnly` |

The real-package gate pins exact package content for deterministic execution.
Synthetic fixtures cover malformed, missing, duplicate, selected-empty,
owner-default, compatible, and ranged-read boundaries.

## Adoption and retirement

Framework-route adoption under #8466 and the shared ladder tracker #6288 has
seven owner-separated slices:

1. #8506 locks the Assembly Reference Resolution Ladder's association contract
   (complete).
2. #8513 supplies bounded package-manifest framework-reference groups reusable
   by PackageHouse.
3. #8504 adds the PackageHouse request demand and exact-settlement projection
   specified here.
4. #8503 supplies exact platform package-to-Library correspondence.
5. the ladder composes framework eligibility, correspondence, and
   pruning-before-platform applicability.
6. Workspace publishes the selected platform closure as an immutable
   replacement generation.
7. Browser/Wasm and CLI resolution consumers adopt the shared PackageHouse
   continuation.

Within #6426's eleven-step PackageHouse plan, this work extends normalized
package evidence in step 5, participates in shared Workspace realization in
step 8, and reaches CLI and Browser/Wasm through steps 9 and 10. It does not
create another adoption plan beside that tracker.

The production migration replaces legacy package acquisition and
`RealizedPackageDependencyContext` consumers rather than teaching those paths
new framework behavior. Temporary adapters may carry existing Roots during
migration, but they do not own, reconstruct, or persist the new evidence.

## Non-goals

- Extending `RealizedPackageDependencyContext` or
  `PackageRootReacquisitionRequest`.
- Making `PackageRootBinding` the framework-evidence association.
- Parsing or validating raw nuspec XML in a host.
- Merging framework-reference and package-dependency groups.
- Mapping framework names to platform families, packs, Libraries, or
  assemblies.
- Applying package pruning or choosing route precedence.
- Publishing or mutating a Workspace.
- Supporting legacy `<frameworkAssemblies>`.
