# Package asset-selection correspondence

## Status

Implemented. The receipt correspondence originated as a focused prerequisite
for the PackageHouse resource-free contract in #6433. Issue #7431 extends the
same owner with complete compile-slice inventory and package-local selection
policy evidence for the aggregate-first sequence in #7318.

## Authority and exact claim

The runtime selector retains its existing contract. The compile selector owns
this package-local result contract:

> For one package-content generation and one optional target request, compile
> selection issues the complete available-slice inventory and one selected
> compile projection or typed non-success. Its resource-free receipt binds that
> result to the exact generation, package ID, policy, target, and RID request
> that produced it.

An absent target selects `HighestAvailable`. An explicit target selects its
exact slice when available, otherwise the highest compatible slice using
`TfmResolver`'s version-aware framework and fallback ranks. Unrecognized target
spellings fail closed unless an available slice matches the spelling exactly.
Neutral and platform-qualified slices follow the same conservative platform
rule as runtime asset selection: a neutral target does not admit a
platform-specific slice, while a platform target admits its exact platform and
neutral fallback.

Available slices are the case-insensitive union of candidate asset frameworks
and explicit `ref/<tfm>/_._` groups. Each `PackageCompileAssetSlice` binds one
framework identity to all of its `ref` and `lib` candidates plus the presence
of an explicit empty reference group. `CandidateAssets` retains the same
complete candidates as a deterministic flat compatibility projection,
including nested candidates and candidates whose implementation correspondence
is later rejected. Satellite resource assemblies remain excluded.
`ExplicitEmptyTargetFrameworks` is the corresponding flat marker projection.
Selected and non-selected slices therefore remain visible in selected,
selected-empty, no-match, and invalid results.

Compile roles are framework-local. A marker suppresses library fallback only
when its exact slice is selected and that slice has no real reference asset. A
real reference asset in the selected slice wins over a co-located marker; a
marker in a lower compatible slice cannot suppress a selected higher slice.
Compile and implementation slices are reduced independently, so a selected
empty compile slice may retain compatible lower implementation assets.
`PackageAssetSelector` remains the sole implementation-universe and RID
overlay owner.

One selected compile projection contains zero assets for an explicit empty
slice or every reference/library fallback asset in the selected slice. It may
therefore contain one or many Libraries; compile selection does not collapse
that projection to a namesake or representative Library. The legacy
`DefaultAsset` convenience remains outside this package-local projection
claim until its current consumers adopt aggregate Navigation.

## Receipts

`PackageAssetSelector.Evaluate` returns `PackageAssetSelectionReceipt` with:

- the `PackageContentGenerationIdentity` read from the input content;
- the exact requested target-framework string;
- the exact optional requested runtime identifier; and
- the existing `PackageAssetSelection` outcome.

The policy-taking `PackageCompileAssetSelector.Evaluate` overload returns
`PackageCompileAssetSelectionReceipt` with the same generation and request
evidence plus the exact package ID, `HighestAvailable` or `ExplicitTarget`
policy source, and `PackageCompileAssetSelection` outcome. Requested and
selected target frameworks remain separate.

`ExactTarget` is a transitional, non-PackageHouse policy for existing
consumers that have not reached their aggregate-adoption slice. It selects only
an exactly named available slice. PackageHouse correspondence rejects that
policy, so an exact-only consumer cannot accidentally satisfy the new
package-local contract.

The receipts are resource-free. They do not retain `IPackageContent`, streams,
filesystem paths outside the existing package-relative asset outcomes, package
source clients, or leases.

The existing non-policy `Select` and `Evaluate` overloads retain exact-target
behavior and issue `ExactTarget` when a target is present. An absent target
retains the existing highest-available behavior. This keeps later Query and
host adoption in their assigned slices rather than changing them implicitly
through a lower-owner prerequisite.

## Composition and adoption

The immediate consumer is the focused PackageHouse package-local compile
contract in #7423. PackageHouse maps absent and explicit target context to the
selector policies above and preserves the returned receipt unchanged. The
aggregate recommendation and exact/namesake narrowing contract remains owned
by Navigation in #7318.

PackageHouse may compare a selector receipt's generation and exact request
arguments with its acquisition and target-context receipts. It does not
construct selector correspondence around an independently supplied selection
outcome.

This owner does not implement Workspace admission, package acquisition,
dependency traversal, CLI or browser/Wasm behavior, rendering, initial subject
recommendation, or namesake narrowing.

## Gates

The focused gates run with:

```text
dotnet run --project tests/DotnetInspector.Services.Tests -c Release -- \
  --filter-class '*PackageAssetSelectorTests' \
  '*PackageCompileAssetSelectorTests' \
  '*PackageHouseExecutionTests' \
  '*PackageHouseContractTests'

dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*PackageAssemblyContextRealizationTests'
```

| Property | Gate |
| --- | --- |
| Runtime evaluation retains generation, request, and outcome | `Evaluate_RetainsGenerationRequestAndSelection` |
| Compile evaluation retains generation, request, explicit policy, and outcome | `Evaluate_RetainsGenerationRequestAndSelection` |
| An absent target records `HighestAvailable` and selects the highest slice | `Evaluate_AbsentTargetRetainsHighestAvailablePolicy` |
| Explicit targets retain requested and compatible selected frameworks separately | `ExplicitTarget_SelectsCompatibleLowerSlice` and `ExactCompileRealizeBindsSelectionAndLibraryHandoff` |
| Existing exact-only consumers cannot satisfy PackageHouse policy | `ExactTargetConveniencePathDoesNotApplyPackageLocalFallback` and `CompileReceiptRejectsExactTargetOutsideHousePolicy` |
| Policy and target cardinality must agree | `PackageLocalPolicyRejectsAnInconsistentTarget` |
| Marker-only and highest-marker packages remain selected-empty inventory | `EmptyReferenceGroup_AloneIsASelectedEmptySlice` and `HighestAvailable_ChoosesHigherEmptySliceOverLowerLibrarySlice` |
| Marker suppression is framework-local and real references win | `EmptyReferenceGroup_InALowerSliceDoesNotSuppressSelectedHigherSlice` and `EmptyReferenceGroup_LosesToRealReferenceAssetsAtTheSelectedFramework` |
| Compile and implementation slices reduce separately | `CompatibleImplementation_ReducesCompileAndImplementationSlicesSeparately` |
| Invalid implementation selection retains complete candidate inventory | `Selection_CaseCollidingImplementationPathsAreRejected` |
| Selected implementation assets retain RID qualification | `RidSpecificImplementation_DoesNotReplaceLibraryCompileFallback` |
| PackageHouse preserves owner-default inventory and many Library handoffs | `OwnerDefaultCompileRealizePreservesHighestAvailableInventory` |
| PackageHouse preserves selected-empty, no-match, and exact receipt evidence | `ExactCompileRealizePreservesExplicitEmptyGroup` and `ExactCompileRealizePreservesNoMatchWithPayload` |
| Frozen Package Root evidence preserves immutable empty-slice inventory | `PackageRootSelectionIdentity_SelectionSequencesAreImmutable` |
| A null runtime framework remains a typed invalid outcome | `Evaluate_PreservesNullTargetAsTypedInvalidSelection` |
| Receipt instance type graphs retain no content or disposable resource | `SelectionReceiptsAreResourceFree` |
| Existing runtime selection remains the evaluation projection | Existing `PackageAssetSelectorTests` |

## Non-claims

The receipts do not prove package coordinate, source, producer, authority,
operation, dependency edge, or platform correspondence. Those associations
require their respective owner-issued evidence at a composition boundary. The
compile inventory does not select an initial UI subject, define aggregate
structural identity, or merge separately requested framework slices.
