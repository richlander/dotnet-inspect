# Package asset-selection correspondence

## Status

Implemented. The receipt correspondence originated as a focused prerequisite
for the PackageHouse resource-free contract in #6433. Issue #7431 extends the
same owner with complete compile-slice inventory and package-local selection
policy evidence for the aggregate-first sequence in #7318.

## Authority and exact claim

The runtime selector owns one implementation universe, and the compile
selector owns one package-local compile projection:

> For one package-content generation, runtime selection issues exactly one
> target-and-optional-RID implementation universe or typed non-success and
> retains every applicable RID population, while compile selection issues the
> complete available-slice inventory and one selected compile projection or
> typed non-success. Their resource-free receipts bind those results to the
> exact generation and explicit or owner-default request that produced them.

An absent target selects `HighestAvailable`. An explicit target selects its
exact slice when available, otherwise the highest compatible slice using
`TfmResolver`'s version-aware framework and fallback ranks. Unrecognized target
spellings fail closed unless an available slice matches the spelling exactly.
When an owner-default compile selection feeds runtime-population choice, its
already-selected effective framework bounds runtime eligibility; the original
omitted request remains separate Root correspondence.
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

Direct `lib/*.dll` assets form one framework-agnostic slice whose receipt
identity is `any`; nested paths do not join that slice. A recognized explicit
target may select `any` only when no framework-qualified slice satisfies the
request, and `HighestAvailable` ranks every framework-qualified slice ahead of
`any`. An unrecognized target still fails closed unless an available
framework-qualified slice matches its spelling exactly. The implementation
universe applies the same `any` fallback, so a selected root `lib` asset is its
own implementation counterpart.

For an exact runtime request, neutral and RID-specific implementation groups
are reduced independently. When the exact RID has a compatible managed runtime
group, that whole group supplies the implementation universe; neutral `lib`
assets do not join it. Neutral `lib` is selected only when no compatible group
exists for the exact RID. An owner-default runtime request considers only
structurally eligible
`runtimes/<rid>/lib/<tfm>/*.dll` candidates, excludes `native` and every
unrelated role, and issues one implementation-only universe:

- an omitted RID is implied only when exactly one eligible RID occurs;
- an explicit RID selects only that RID and never merges another;
- an omitted target selects the highest available runtime framework;
- an explicit target uses the ordinary compatibility and platform rules; and
- more than one eligible RID is a typed ambiguity that requires an explicit
  RID.

Runtime folders and target slices are selected from the package directory
without opening candidate assemblies. Managed-image admission remains the
consumer's responsibility, so a native DLL incorrectly placed under
`runtimes/<rid>/lib/<tfm>` can produce no admitted Library but cannot make
another RID or role join the population. `SelectPlatformPack` remains separate:
its broader pinned-platform candidate rule, including managed images outside
`lib`, does not define a Package Library population.

`EvaluateSingleRuntime` additionally retains every successfully reducible
runtime population applicable to the exact target request. Each
`PackageRuntimeAssetPopulation` carries its canonical RID, selected target
framework, package-relative population path, and ordered assets. The inventory
is ordered by canonical RID using ordinal comparison, excludes native-only and
unrelated roles, and remains complete when the selected outcome is ambiguous
or an explicit RID narrows the effective universe. This lower owner does not
apply the product-default RID or reorder the inventory around it.

Compile roles are framework-local. A marker suppresses library fallback only
when its exact slice is selected and that slice has no real reference asset. A
real reference asset in the selected slice wins over a co-located marker; a
marker in a lower compatible slice cannot suppress a selected higher slice.
Compile and implementation slices are reduced independently, so a selected
empty compile slice may retain compatible lower implementation assets.
`PackageAssetSelector` remains the sole implementation-universe and RID
population owner.

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

The framework-agnostic slice is grounded by published packages that carry
managed assemblies directly under `lib/`: WebGrease 1.6.0, Antlr 3.5.0.2, and
EO.WebBrowser 26.3.18. Focused selector fixtures preserve that layout without
retaining package binaries.

The owner-default runtime rule is grounded by
Microsoft.NETCore.App.Runtime.linux-x64 10.0.12, which carries 172 Libraries in
one `runtimes/linux-x64/lib/net10.0` directory, and
runtime.unix.System.Private.Uri 4.3.0, which carries an explicit empty compile
group plus one Library in
`runtimes/unix/lib/netstandard1.0`. Focused fixtures preserve both layouts and
the multiple-RID refusal without retaining package binaries.

Exact-RID precedence is grounded by an SDK restore oracle with neutral
`lib/net10.0` and RID-specific `runtimes/linux-x64/lib/net8.0` groups. A
`net10.0/linux-x64` restore selects the compatible RID-specific `net8.0`
implementation group. A same-framework oracle with an extra neutral companion
also selects only the RID-specific group, confirming that NuGet does not merge
neutral and RID-specific managed populations per assembly.

The layouts are reproducible with:

```text
dnx dotnet-inspect -y -- package Microsoft.NETCore.App.Runtime.linux-x64@10.0.12 --path --tsv
dnx dotnet-inspect -y -- package runtime.unix.System.Private.Uri@4.3.0 --path --tsv
```

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
- the complete ordinally ordered applicable runtime-population inventory for
  `EvaluateSingleRuntime`, empty for ordinary exact-RID or neutral-fallback
  evaluation;
  and
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

## Composition and adoption

The immediate consumer is the focused PackageHouse package-local compile
contract in #7423. PackageHouse maps absent, compatible explicit,
caller-authored exact, and frozen owner-selected target contexts to the
selector policy and candidate-eligibility facts above and preserves the
returned receipt unchanged. The aggregate recommendation and exact/namesake
narrowing contract remains owned by Navigation in #7318.

PackageHouse may compare a selector receipt's generation and exact request
arguments with its acquisition and target-context receipts. It preserves the
runtime-population inventory unchanged, including through named
implementation narrowing, and does not construct selector correspondence
around an independently supplied selection outcome.

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
| Exact policy alone cannot satisfy PackageHouse correspondence | `ExactTargetConveniencePathDoesNotApplyPackageLocalFallback`, `CompileReceiptRejectsExactTargetOutsideHousePolicy`, `CompileReceiptRejectsDifferentCandidateEligibility`, and `FrozenTargetCompileRealizePreservesNestedOwnerEligibility` |
| Policy and target cardinality must agree | `PackageLocalPolicyRejectsAnInconsistentTarget` |
| Marker-only and highest-marker packages remain selected-empty inventory | `EmptyReferenceGroup_AloneIsASelectedEmptySlice` and `HighestAvailable_ChoosesHigherEmptySliceOverLowerLibrarySlice` |
| Marker suppression is framework-local and real references win | `EmptyReferenceGroup_InALowerSliceDoesNotSuppressSelectedHigherSlice` and `EmptyReferenceGroup_LosesToRealReferenceAssetsAtTheSelectedFramework` |
| Compile and implementation slices reduce separately | `CompatibleImplementation_ReducesCompileAndImplementationSlicesSeparately` |
| Compatible implementation reconstruction preserves nested compile candidates | `CompatibleImplementation_PreservesNestedCompileAssets` and `PackageRootBinding_OwnerDefaultPreservesImplementationUniverseTarget` |
| Direct root `lib` assets form the framework-agnostic fallback and retain implementation correspondence | `RootLibraryAssets_FormFrameworkAgnosticCompileSlice`, `ExactTargetRootFallbackPreservesRootImplementationCounterpart`, `Select_RootLibraryIsFrameworkAgnosticFallback`, `Select_FrameworkSpecificFolderOutranksRootLibrary`, and `LibraryInventory_RootLibraryUsesAnySliceAndSameImplementationEntry` |
| The framework-agnostic fallback does not admit unrecognized target spellings | `Select_RootLibraryDoesNotMakeAnUnrecognizedTargetApplicable` |
| Owner-default runtime selection implies one RID and one target slice without mixing neutral, native, or unrelated assets | `SelectSingleRuntime_InfersOneRidAndHighestRuntimeSlice`, `SelectSingleRuntime_ExplicitTargetUsesOneCompatibleRuntimeSlice`, `RuntimeOwnerDefaultRealizeInfersSingleRidAndHighestTarget`, and `RuntimeExactTargetWithoutRidInfersSingleRid` |
| Owner-default runtime selection never merges RIDs or case-colliding assets; an explicit RID selects only itself | `SelectSingleRuntime_RequiresAnExplicitRidForMultipleRids`, `SelectSingleRuntime_ReportsCaseCollidingAssetsAsAmbiguous`, `SelectSingleRuntime_ExplicitRidDoesNotMergeOtherRids`, and `RuntimeOwnerDefaultDoesNotMergeMultipleRids` |
| Runtime evaluation retains every applicable RID population in stable ordinal order without shrinking for an explicit RID | `EvaluateSingleRuntime_RetainsCompleteOrderedPopulationInventory`, `MultipleRuntimeIdentifiersRemainTypedNonSuccess`, and `RuntimeOwnerDefaultDoesNotMergeMultipleRids` |
| PackageHouse preserves the complete runtime-population inventory through named implementation narrowing | `RuntimeExactTargetWithoutRidInfersSingleRid` |
| Owner-default runtime inventory is bounded by the selected effective framework while the original request remains omitted | `RuntimePopulationSelectionExcludesRuntimeAboveOwnerDefaultFramework` |
| Native-only RID folders do not participate in runtime Library inference | `SelectSingleRuntime_IgnoresNativeOnlyRids` |
| Invalid implementation selection retains complete candidate inventory | `Selection_CaseCollidingImplementationPathsAreRejected` |
| An applicable exact-RID group wholly replaces neutral implementation fallback, including when it targets a lower compatible framework | `Select_RuntimePopulationReplacesTheNeutralImplementationGroup`, `Select_CompatibleRuntimePopulationOutranksNewerNeutralGroup`, `RuntimePopulationSelectionUsesCompatibleRuntimeGroupOverNewerNeutralFallback`, and `RidSpecificImplementation_ReplacesWholeNeutralImplementationGroup` |
| An incompatible exact-RID group leaves neutral implementation fallback available | `Select_IncompatibleRuntimePopulationFallsBackToNeutralGroup` |
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
