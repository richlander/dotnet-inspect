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
The selected folder inventory contains the distinct top-level package folders
that carry the selected TFM and is lowered through `InertString` containment in
the shared envelope.

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
| Structural failures | `DeclaredToolMeasurementRejectsCaseCollidingEntries`, `MalformedToolPathsDoNotCreateSlices`, and `DeclaredToolMeasurementRequiresEntryManifest` |
| Closed no-slice JSON shape | `NoToolSlicesJsonRejectsNonemptyFrameworkInventory` |
| CLI `all` normalization and one-download adoption | `PackageCommand_DeclaredToolUsesAggregateToolMeasurements` |
| Shape alone is insufficient | `PackageCommand_UndeclaredToolShapeDoesNotAuthorizeToolMeasurements` |
