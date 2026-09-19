# Package Info Tool Measurements

## Scope

This design owns one narrow Package Info profile: measurements for a retained
payload whose nuspec declares the `DotnetTool` or `DotnetToolRidPackage`
package type. PackageHouse continues to own compile inventory and compile-slice
measurements. This profile does not reinterpret a compile realization or make
tool layout part of the PackageHouse contract.

The production consumer is CLI Package Info. It projects the tool result
through the same `InspectionEnvelope<PackageInfoMeasurements>` used for compile
packages, so Browser/Wasm can adopt one portable shape instead of rebuilding
tool measurements in TypeScript.

## Contract

The profile consumes the exact `AcquiredPackageSourcePayload` retained by the
existing configured-source acquisition. It performs no second acquisition and
retains the payload's coordinate and content-generation identity in its
resource-free evidence.

Only an exact, case-insensitive `DotnetTool` or `DotnetToolRidPackage` nuspec
package-type declaration read from the measured payload authorizes this
profile. The bounded manifest read validates the declared identity against the
payload coordinate and binds the evidence to that payload's content
generation. A wrapper declaration or a directory that merely resembles a tool
package does not authorize another payload.

Declaration authentication accepts only the established nuspec namespace
grammar: a namespace-free package root, a recognized nuspec schema root with
matching metadata, or a namespace-free root with recognized schema-namespaced
metadata. Foreign roots, incompatible namespace pairs, and multiple compatible
metadata elements do not authorize the profile.

Tool target-framework slices are the TFM-like second path segments under
`tools/<tfm>/`. The default selection is the highest available TFM. An explicit
target uses the same compatible-framework selection helper as package compile
selection and reports the actual selected TFM separately from the request.

One selected tool Library is one DLL entry under the selected
`tools/<tfm>/` tree, except:

- culture-qualified satellite assemblies; and
- DLLs below a standard nested `runtimes/<rid>/native/` subtree.

Package Size remains the compressed retained archive length. Selected-TFM Size
is the sum of declared uncompressed lengths for every selected tool Library.
The selected TFM, available TFM inventory, and selected folder inventory are
lowered through `InertString` containment in the shared envelope. The folder
inventory contains the distinct top-level package folders that carry the
selected TFM.

An available tool TFM is discovered from any well-formed entry below
`tools/<tfm>/`, independently of whether the slice contains a Library. A
selected slice with no Libraries is a typed selected-empty result with zero
bytes and zero Libraries.

No tool slices, no applicable slice, ambiguous case-colliding entries, missing
entry-manifest capability, missing or invalid selected entry lengths, archive
unavailability, and size overflow remain typed visible outcomes. They do not
become zero-shaped success.

## Boundaries

- Direct local-file and offline legacy extraction continue to expose only
  their established compressed package size because they do not retain an
  acquired source payload.
- Declared tool layouts without a TFM-like `tools/<tfm>/` segment do not
  manufacture a selected TFM.
- The profile classifies package entries by the declared tool layout and DLL
  naming contract; it does not open inspected assemblies or infer managed
  metadata.
- Dependency traversal treats only `DotnetTool` as a wrapper declaration.
  `DotnetToolRidPackage` is a direct payload and uses its fetched nuspec without
  acquiring the archive for wrapper resolution.
- Package-index persistence versions the broadened tool classification so warm
  entries cannot retain the older `DotnetToolRidPackage`-as-Library result.
- Aggregate package Type and Member subjects remain later #7423 slices.

The motivating production package is `dotnet-inspect.any@0.25.0`, whose
selected `tools/net10.0/any/` slice contains many Libraries and has no compile
slice from which a representative assembly could be chosen.

## Required gates

| Claim | Release gate |
| --- | --- |
| Aggregate selection | `PackageInfoEnvelopeMeasuresDeclaredToolPayload` |
| Compatible explicit target | `DeclaredToolMeasurementUsesApplicableExplicitFramework` |
| Empty selected slice | `DeclaredToolMeasurementPreservesSelectedEmptySlice` |
| Declaration and generation binding | `ToolDeclarationAuthenticatesSupportedPayloadType`, `ToolDeclarationRejectsMismatchedManifestCoordinate`, `DeclaredToolMeasurementRejectsForeignDeclarationGeneration`, and `PackageCommand_WrapperDeclarationDoesNotAuthorizePayloadMeasurements` |
| Nuspec namespace grammar | `ToolDeclarationAcceptsSchemaMetadataUnderNamespaceFreeRoot` and `ToolDeclarationRejectsForeignNuspecNamespace` |
| Structural failures | `DeclaredToolMeasurementRejectsCaseCollidingEntries`, `MalformedToolPathsDoNotCreateSlices`, and `DeclaredToolMeasurementRequiresEntryManifest` |
| Closed no-slice JSON shape | `NoToolSlicesJsonRejectsNonemptyFrameworkInventory` |
| CLI `all`, one-download-per-invocation, and cold/warm adoption | `PackageCommand_DeclaredToolUsesAggregateToolMeasurementsColdAndWarm` |
| CLI JSON outcomes | `PackageCommand_DeclaredToolJsonRetainsMeasuredAndNoApplicableOutcomes` |
| Selected TFM containment | `PackageInfoEnvelopeMeasuresDeclaredToolPayload` and `PackageCommand_DeclaredToolSelectedFrameworkIsContainedAcrossOutputs` |
| Direct RID dependency routing | `BuildPackageDependencyTreeAsync_RidToolPackageUsesNuspec` |
| Shape alone is insufficient | `PackageCommand_UndeclaredToolShapeDoesNotAuthorizeToolMeasurements` |
