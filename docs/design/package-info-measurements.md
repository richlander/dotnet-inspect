# Package Info measurements

## Status

Implemented as the second focused slice of
[#7423](https://github.com/richlander/dotnet-inspect/issues/7423). This owner
incorporates the useful package-size distinction from
[#7415](https://github.com/richlander/dotnet-inspect/pull/7415) without its
representative-assembly rule.

## Authority and exact claim

`PackageInfoMeasurementQuery` owns this claim:

> Given one retained package-content generation, one package slice profile,
> and one optional package-local target request, issue the compressed archive
> size, complete available-TFM inventory, and one selected-TFM aggregate
> measurement or a typed non-success bound to that exact generation and
> request.

Whole-package size is the retained `.nupkg` stream length. It is a compressed
archive measurement, not the sum of package entries.

A selected-TFM measurement contains:

- the actual selected TFM;
- the uncompressed size of every measured Library entry in that slice;
- the number of selected Libraries; and
- the package-relative entries that contributed to the size.

The query never chooses one namesake, largest, first, or otherwise
representative assembly.

## Slice profiles and selection

The package manifest determines whether Package Info requests the `Compile` or
`Tool` profile. This profile is package evidence supplied to the query, not a
host-side asset-selection algorithm.

The `Compile` profile delegates selection to
`PackageCompileAssetSelector.Evaluate`. An absent target uses
`HighestAvailable`; an explicit target uses `ExplicitTarget`. The query
preserves the returned selection receipt. For every selected compile asset, the
measurement uses its implementation counterpart when one exists and otherwise
uses the selected compile entry. The selected Library count remains the
compile projection's Library count even when entry correspondence removes
duplicate measurement paths.

The `Tool` profile applies the same absent-target and explicit-target rules to
managed DLL entries under `tools/<tfm>/`. It measures every non-satellite DLL
below the selected framework folder. This profile exists because a packed
framework-dependent tool carries its Library closure under `tools/`; no one
assembly represents that package.

Both profiles retain a deterministic, case-insensitive available-TFM
inventory. An explicit target records the actual compatible slice selected by
the owner rather than echoing the requested spelling.

An explicit empty compile group is a successful selected slice with zero bytes
and zero Libraries. A package with no applicable slice, an unmatched explicit
target, an unavailable archive or entry manifest, rejected selection,
ambiguous entry paths, missing selected entry length, invalid entry length, or
size overflow remains a structural unavailable or invalid result. These states
must not become zero-valued success.

## Motivating evidence

`AWSSDK.Core@4.0.102.6` demonstrates the whole-package versus selected-slice
distinction: its 2.2 MB compressed archive selects a 1 MB `net8.0` slice.
`Microsoft.Data.SqlClient@6.1.3` demonstrates role correspondence: Package Info
measures the 908.1 KB `lib/net9.0` implementation rather than the smaller
reference facade or larger RID-qualified copies. `dotnet-inspect.any@0.25.0`
demonstrates the no-representative-assembly case: its selected `net10.0` tool
slice contains 41 Libraries.

The real-package gate uses `System.Text.Json@10.0.0`, already restored by the
Services test project, to prove the query over an immutable nuget.org archive.
Synthetic fixtures preserve the multi-Library, implementation-correspondence,
tool-closure, empty-group, and unavailable-capability boundaries without
manufacturing product evidence.

## Composition and adoption

The first production consumer is CLI Package Info. The CLI supplies retained
content, package identity, package profile, and the optional `--tfm` request,
then projects the receipt into:

- `Package Size`;
- `Selected TFM`;
- `TFM Count`;
- `Selected TFM Size`; and
- `Selected TFM Libraries`.

Request-specific selected-slice measurements are produced after package-index
cache lookup and are not persisted in that request-independent cache. Package
Info dependency-group filtering uses the actual selected TFM when selection
falls back compatibly.

The all-Libraries aggregate subject and Inspect Web presentation remain later
slices of #7423. They may consume this resource-free receipt without
reconstructing its measurement or selection decisions.

## Boundaries

This owner does not redefine NuGet compatibility, compile or runtime
asset-selection algorithms, RID selection, dependency-group selection,
package acquisition, archive admission, package caching, Workspace traversal
policy, aggregate semantic identity, or rendering mechanics.

Selected-TFM size is not installed size, restored application size, dependency
closure size, RID-slice size, memory size, or a prediction of linker output.

## Gates

```text
dotnet run --project tests/DotnetInspector.Services.Tests -c Release -- \
  --filter-class '*PackageInfoMeasurementQueryTests'

dotnet run --project tests/DotnetInspect.Cli.Tests -c Release -- \
  --filter-class '*InspectionResultTests' \
  '*PackageInspectionTextTests' \
  '*PackageInspectorMetadataSourceTests'
```

| Property | Gate |
| --- | --- |
| Receipt retains exact content generation, profile, and target request | `CompileProfile_AggregatesImplementationEntries` and `CompileProfile_ExplicitTargetReportsCompatibleSelection` |
| An immutable nuget.org package produces coherent archive and selected-slice measurements | `RealPackage_SystemTextJsonReportsArchiveAndSelectedSlice` |
| Archive size reports retained compressed bytes | `CompileProfile_AggregatesImplementationEntries` |
| Compile measurement uses every selected Library and implementation correspondence | `CompileProfile_AggregatesImplementationEntries` |
| Explicit selection reports the actual compatible TFM | `CompileProfile_ExplicitTargetReportsCompatibleSelection` |
| Explicit empty groups report zero bytes and zero Libraries | `CompileProfile_EmptyGroupIsAZeroValuedSelection` |
| Tool packages measure the complete managed closure and exclude satellites | `ToolProfile_AggregatesEveryManagedLibrary` |
| Missing entry-length capability remains unavailable | `MissingEntryManifestIsUnavailable` |
| Missing archive capability remains unavailable | `MissingArchiveIsUnavailable` |
| CLI fields and JSON use the same selected-slice facts | `PackageInfo_RendersSelectedTfmMeasurements` |
| Package Info applies aggregate measurements after inspection | `ApplyPackageInfoMeasurements_AggregatesSelectedLibraries` |
| CLI package classification selects the complete tool profile | `ApplyPackageInfoMeasurements_AggregatesToolLibraries` |
