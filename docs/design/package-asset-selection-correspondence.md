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

Available slices are the case-insensitive union of candidate asset frameworks,
the legacy `net0` slice formed by direct `lib/*.dll` entries, and explicit
`ref/<tfm>/_._` groups. The `net0` spelling and direct-entry boundary match
NuGet's `ManagedCodeConventions` for `lib/{assembly}`. Every real framework
slice outranks it, and an explicit recognized target selects it only after no
real compatible slice applies. Nested `lib/` entries do not enter that legacy
slice.

Each `PackageCompileAssetSlice` binds one framework identity to all of its
`ref` and `lib` candidates plus the presence of an explicit empty reference
group. `CandidateAssets` retains the same complete candidates as a
deterministic flat compatibility projection, including nested candidates and
candidates whose implementation correspondence is later rejected. Satellite
resource assemblies remain excluded. `ExplicitEmptyTargetFrameworks` is the
corresponding flat marker projection. Selected and non-selected slices
therefore remain visible in selected, selected-empty, no-match, and invalid
results.

Compile roles are framework-local. A marker suppresses library fallback only
when its exact slice is selected and that slice has no real reference asset. A
real reference asset in the selected slice wins over a co-located marker; a
marker in a lower compatible slice cannot suppress a selected higher slice.
Compile and implementation slices are reduced independently, so a selected
empty compile slice may retain compatible lower implementation assets.
`PackageAssetSelector` remains the sole implementation-universe and RID
overlay owner.

One package-local selected compile projection contains zero assets for an
explicit empty slice or every reference/library fallback asset in the selected
slice, including nested candidates. It may therefore contain one or many
Libraries; compile selection does not collapse that projection to a namesake or
representative Library. The legacy `DefaultAsset` convenience remains outside
this package-local projection claim until its current consumers adopt aggregate
Navigation or its
[First Library](inspection-subject-navigation.md#initial-aggregate-and-package)
request. `DefaultAsset` matches file stems in selection order, while First
Library matches owner-issued assembly names in Library order.

## Comparison counterparts

An ordered package-version comparison retains the exact selected current
compile asset. Its baseline selects a compile slice independently for the
requested target using the existing compatibility policy. Within that selected
set, `FindComparisonAsset` first resolves the exact asset ID; otherwise it
issues a counterpart only when exactly one selected asset has the same
framework-relative path. For example, `lib/net11.0/System.Text.Json.dll`
corresponds to `lib/net10.0/System.Text.Json.dll` when those are the independently
selected slices. Reference and library roots may differ between versions.

Only the framework/root prefixes are removed by the existing package path
projection; nested relative directories remain part of identity. This operation
does not choose an unselected slice, change framework compatibility, match an
assembly display name, or select the first candidate. Missing, unsupported, and
ambiguous counterparts return no asset. The consumer retains both issued exact
asset identities as comparison provenance.

## Receipts

`PackageAssetSelector.Evaluate` returns `PackageAssetSelectionReceipt` with:

- the `PackageContentGenerationIdentity` read from the input content;
- the exact requested target-framework string;
- the exact optional requested runtime identifier; and
- the existing `PackageAssetSelection` outcome.

The policy-taking `PackageCompileAssetSelector.Evaluate` overload returns
`PackageCompileAssetSelectionReceipt` with the same generation and request
evidence plus the exact package ID, `HighestAvailable`, `ExplicitTarget`, or
`ExactTarget` policy source, nested-candidate eligibility, and
`PackageCompileAssetSelection` outcome. Requested and selected target
frameworks remain separate.

The existing exact-only selector convenience remains direct-only: it issues
`ExactTarget`, selects direct assets from an exactly named available slice, and
retains nested candidates only in `AvailableSlices`. PackageHouse may also
issue `ExactTarget`, but its target context and receipt must agree separately
on nested-candidate eligibility. Caller-authored exact-target realization
retains the direct-only behavior. Frozen owner-selected realization accepts
only the named framework while retaining owner-default eligibility, including
nested compile assets. A standalone selector receipt therefore cannot satisfy
House correspondence merely by naming the same policy and target.

The receipts are resource-free. They do not retain `IPackageContent`, streams,
filesystem paths outside the existing package-relative asset outcomes, package
source clients, or leases.

The existing non-policy `Select` and `Evaluate` overloads retain exact-target
behavior and issue `ExactTarget` when a target is present. An absent target
retains the existing highest-available behavior. This keeps later Query and
host adoption in their assigned slices rather than changing them implicitly
through a lower-owner prerequisite.

`WebGrease@1.6.0` is the motivating real package: it carries
`lib/WebGrease.dll` without a framework-specific compile slice. NuGet restore
selects that asset for a .NET 11 project with its legacy compatibility warning.
The CLI canary is:

```text
dotnet-inspect library --package WebGrease@1.6.0 --tfm net11.0 \
  -S "Library Info"
```

Focused deterministic archives reproduce the direct root, nested root, real
framework, and unrelated-role boundaries without making PR tests depend on
nuget.org availability.

## Composition and adoption

The immediate consumer is the focused PackageHouse package-local compile
contract in #7423. PackageHouse maps absent, compatible explicit,
caller-authored exact, and frozen owner-selected target contexts to the
selector policy and candidate-eligibility facts above and preserves the
returned receipt unchanged. The aggregate recommendation and exact/namesake
narrowing contract remains owned by Navigation in #7318.

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
| Direct root Libraries form the NuGet-compatible legacy slice | `RootLibraries_FormLegacyNet0CompileSlice` |
| Real framework slices outrank the legacy slice | `RealTargetFrameworkSlice_OutranksLegacyNet0` |
| An explicit recognized target uses the legacy slice only as the final fallback | `ExplicitTarget_UsesLegacyNet0AsFinalFallback` and `InvalidOrExactTarget_DoesNotApplyLegacyNet0Fallback` |
| Exact policy alone cannot satisfy PackageHouse correspondence | `ExactTargetConveniencePathDoesNotApplyPackageLocalFallback`, `CompileReceiptRejectsExactTargetOutsideHousePolicy`, `CompileReceiptRejectsDifferentCandidateEligibility`, and `FrozenTargetCompileRealizePreservesNestedOwnerEligibility` |
| Policy and target cardinality must agree | `PackageLocalPolicyRejectsAnInconsistentTarget` |
| Marker-only and highest-marker packages remain selected-empty inventory | `EmptyReferenceGroup_AloneIsASelectedEmptySlice` and `HighestAvailable_ChoosesHigherEmptySliceOverLowerLibrarySlice` |
| Marker suppression is framework-local and real references win | `EmptyReferenceGroup_InALowerSliceDoesNotSuppressSelectedHigherSlice` and `EmptyReferenceGroup_LosesToRealReferenceAssetsAtTheSelectedFramework` |
| Compile and implementation slices reduce separately | `CompatibleImplementation_ReducesCompileAndImplementationSlicesSeparately` |
| Invalid implementation selection retains complete candidate inventory | `Selection_CaseCollidingImplementationPathsAreRejected` |
| Selected implementation assets retain RID qualification | `RidSpecificImplementation_DoesNotReplaceLibraryCompileFallback` |
| PackageHouse preserves owner-default inventory and many Library handoffs | `OwnerDefaultCompileRealizePreservesHighestAvailableInventory` |
| PackageHouse preserves selected-empty, no-match, and exact receipt evidence | `ExactCompileRealizePreservesExplicitEmptyGroup` and `ExactCompileRealizePreservesNoMatchWithPayload` |
| Exact root-Library demand retains complete central-directory inventory and materializes only the selected body | `EndpointScope_RootNet0DemandNarrowsFromCentralDirectory` |
| CLI root-Library consumers use the selected legacy population and preserve real-slice precedence | `LibraryCommand_RootLibUsesLegacyNet0CompilePopulation` and `LibraryCommand_RealTfmSliceOutranksRootLegacy` |
| Frozen Package Root evidence preserves immutable empty-slice inventory | `PackageRootSelectionIdentity_SelectionSequencesAreImmutable` |
| A null runtime framework remains a typed invalid outcome | `Evaluate_PreservesNullTargetAsTypedInvalidSelection` |
| Receipt instance type graphs retain no content or disposable resource | `SelectionReceiptsAreResourceFree` |
| Existing runtime selection remains the evaluation projection | Existing `PackageAssetSelectorTests` |

## Non-claims

The receipts do not prove package coordinate, source, producer, authority,
operation, dependency edge, or platform correspondence. Those associations
require their respective owner-issued evidence at a composition boundary. The
compile inventory does not select an initial UI subject, define aggregate
structural identity, merge separately requested framework slices, or decide
whether a package with no compile slice may expose one RID-specific
implementation population.
