# NuGet Package Structure and Asset Roles

A NuGet package is a ZIP archive whose paths conventionally describe how its
contents participate in build and runtime use. This document defines the
package assembly roles that `dotnet-inspect` consumes. It is not a complete
NuGet restore specification.

## Assembly asset roles

A **compile asset** is a package assembly selected as a compile-time reference
for a target framework. It describes the API surface available to code that
references the package.

An **implementation asset** is a package assembly selected to provide the
implementation for a target framework and optional runtime identifier (RID).
`dotnet-inspect` uses implementation assets for implementation-oriented
inspection such as IL, decompilation, and source provenance; it never executes
an inspected assembly.

The role belongs to a selection, not to the `.dll` extension alone. One
`lib/<tfm>/` assembly can serve both roles when it is selected as the compile
fallback. A DLL elsewhere in the archive is not a compile asset merely because
it is managed code.

| Package path | Role in `dotnet-inspect` |
| --- | --- |
| `ref/<tfm>/*.dll` | Preferred compile assets when real assemblies exist at the exactly selected target framework. These are commonly reference assemblies and may be metadata-only. |
| `lib/<tfm>/*.dll` | Runtime-neutral implementation assets and the compile fallback when the exactly selected target framework has no real `ref` assembly group and no compatible empty group suppresses fallback. |
| `runtimes/<rid>/lib/<tfm>/*.dll` | RID-specific implementation assets. For an exact RID request, an asset here replaces a neutral `lib` asset at the same relative path. |
| `runtimes/<rid>/native/*` | Native runtime assets; not managed compile assets. |
| `build/`, `buildTransitive/`, `buildMultiTargeting/` | MSBuild props and targets; build logic, not compile assets. |
| `analyzers/`, `contentFiles/`, `tools/` | Analyzer, project-content, and tool payloads; not compile assets. |
| `.nuspec`, readme, icon, license, project, and source files | Package metadata or auxiliary content; not compile assets. |

The table is intentionally about roles consumed by this product. NuGet defines
additional asset classes and broader restore behavior.

## Selection example

Consider this package:

```text
Example.nuspec
ref/net8.0/Example.dll
lib/net8.0/Example.dll
lib/net8.0/Companion.dll
runtimes/linux-x64/lib/net8.0/Example.dll
buildTransitive/Example.targets
```

For `net8.0` without a RID, `Example.dll` under `ref/` is the compile asset.
The two assemblies under `lib/` are implementation assets. The build target is
not an assembly asset.

For `net8.0` and `linux-x64`, the compile selection is unchanged. The
RID-specific `Example.dll` replaces the neutral implementation with the same
relative path, while the neutral `Companion.dll` remains in the implementation
set.

Compile and implementation groups are selected independently. A `ref/` group
does not need to contain every implementation assembly, and an implementation
assembly is not automatically exposed to package consumers at compile time.

## Explicitly empty compile groups

The exact file name `_._` is NuGet's empty-group marker. Real reference assets
and empty markers intentionally use different framework matching rules. Only
real `ref` assets at the exactly selected target framework can supply the
compile surface. If none exist there, the nearest compatible
`ref/<tfm>/_._` group states that the package deliberately contributes no
compile assembly for that target:

```text
ref/net8.0/_._
lib/net8.0/Example.dll
```

For a `net8.0` compile selection, the marker suppresses the normal `lib/`
fallback. `Example.dll` may still be selected independently as an
implementation asset, but the package has no compile surface for that target.
This is an explicit outcome, not missing package data.

A real reference-assembly group at the exactly selected target wins over a
compatible empty group. A compatible real `ref` group at another target
framework is not a compile candidate. A library empty group such as
`lib/net8.0/_._` says nothing about compile assets. Files such as
`ref/net8.0/_` and `ref/net8.0/_._.dll` are not empty-group markers.

## Product boundaries

Package Root realization remains valid when compile selection is empty or
fails. The Root retains package identity, documents, dependencies, and the
typed selection outcome without inventing an assembly surface. See
[Package Root realization](design/artifact-acquisition-and-workspaces.md#package-root-realization).

The package adapter owns TFM and RID selection. Workspace and query layers
consume its typed compile and implementation roles without reinterpreting
package paths. See
[Package-role planning and cleanup](design/inspection-layers.md#package-role-planning-and-cleanup-boundary).

Current behavior is gated by:

- `PackageCompileAssetSelectorTests.InMemorySelection_PrefersReferenceAssetsAndPackageNamedDefault`;
- `PackageCompileAssetSelectorTests.InMemorySelection_FallsBackToLibraryAssetsAtHighestTfm`;
- `PackageCompileAssetSelectorTests.EmptyReferenceGroup_AtTheSelectedFramework_SuppressesLibraryFallback`;
- `PackageCompileAssetSelectorTests.EmptyReferenceGroup_NearestCompatibleGroupSuppressesLibraryFallback`;
- `PackageCompileAssetSelectorTests.EmptyReferenceGroup_LosesToRealReferenceAssetsAtTheSelectedFramework`;
- `PackageCompileAssetSelectorTests.RidSpecificImplementation_DoesNotReplaceLibraryCompileFallback`;
- `PackageAssetSelectorTests.Select_PrefersTheRuntimeSpecificAssetForTheRequestedRid`;
- `PackageAssetSelectorTests.Select_WithoutARid_UsesOnlyRuntimeNeutralAssets`;
- `PackageAssemblyContextRealizationTests.RidSpecificImplementation_UsesSeparateNeutralCompileRole`;
- `BrowserEngineBoundaryTests.RidSpecificPackage_SeparatesCompileAndImplementationAssets`.

For the upstream role semantics, NuGet's
[asset-selection guidance](https://learn.microsoft.com/nuget/create-packages/native-files-in-net-packages#understanding-nuget-package-asset-selection)
defines compile assets as `ref/<tfm>` falling back to neutral `lib/<tfm>`,
runtime assets as `runtimes/<rid>/lib/<tfm>` falling back to `lib/<tfm>`, and
states that compile-time assemblies cannot vary by RID. NuGet.Client encodes
the same separation in
[`ManagedCodeConventions`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Packaging/ContentModel/ManagedCodeConventions.cs#L517-L553)
and applies compile and runtime selection separately in
[`LockFileUtils`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Commands/RestoreCommand/Utility/LockFileUtils.cs#L220-L240).
