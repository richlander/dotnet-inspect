# Signals

The `Signals` section reports package and library observations. It is an evidence report, not a safety or trust verdict.

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Signals,Audit: Identifier Confusion"
dotnet-inspect library System.Text.Json -S "Signals,SourceLink: Availability,SourceLink: Missing Files"
dotnet-inspect library System.Text.Json -S "SourceLink: Integrity"
dotnet-inspect package System.Text.Json -S Signals
dotnet-inspect package System.Text.Json -S "Signals,Audit: Findings"
dotnet-inspect package System.Text.Json -S "Signals,Audit: Identifier Confusion"
dotnet-inspect package System.Text.Json -S "SourceLink: Availability,SourceLink: Missing Files"
dotnet-inspect package System.Text.Json -S "SourceLink: Integrity"
```

Cost is governed by verbosity (the cost ceiling) and explicit section
selection. `library X -S Signals` reports metadata/provenance signals and
acquires a missing library PDB to resolve SourceLink. On either `library` or
`package`, the per-source-file reachability pass (`SourceLink: Availability`
and `SourceLink: Missing Files`) is selected explicitly via `-S`; Missing Files
reuses the availability result. It does not run in a plain detailed flow because
its cost scales with source-file count. The exhaustive content check downloads
every tracked source file and compares its hash to the PDB checksum. It is
selected explicitly as `SourceLink: Integrity`, never runs in a default flow,
and exits non-zero on any checksum mismatch. Package results aggregate the
selected compatible/highest-TFM libraries and retain library provenance.

See [SourceLink Exposure](sourcelink-exposure.md) for the product surfaces,
PDB dependency, and network policy behind these sections.

## Artifact text containment

Package Signals includes an `Artifact text containment` row over the complete
typed package presentation model.

| Value | Meaning |
| ----- | ------- |
| `None` | No package-model scalar required visual containment. |
| `Required` | At least one scalar required containment; Evidence lists its Unicode category kinds. |

The reported kinds are control (`Cc`), format/bidi (`Cf`), unpaired surrogate
(`Cs`), line separator (`Zl`), and paragraph separator (`Zp`). The row never
echoes the concerning content. A literal backslash is not a concern: it may be
rewritten solely to keep the visual encoding invertible, and that mechanical
rewrite does not change the row to `Required`.

Select `Audit: Artifact Text` for the detected cases, or select `@Audit` to
include it with the other audit evidence:

```bash
dotnet-inspect package X -S "Signals,Audit: Artifact Text"
dotnet-inspect package X -S @Audit
```

The detail table has `Location` and `Concerns` columns. A location is a stable
package-model path such as `Owners[0]` or `PackageFiles[3].Path`; it is not the
artifact value. One field produces one row containing all concern kinds found
in that field. The section is explicit because package-file paths make its row
count scale with package size. Markdown and JSONL use the same rows, and neither
format includes the concerning content.

The package result is gated by
`PackageInspectionTextTests.RequiredContainment_CoversEveryPackageTextSourceIndividually`,
which derives the expected text-source set from the package model and requires
each source to contribute independently;
`Package_MultiplePackages_SignalsIncludePackageFileConcerns` gates parity
between single-package and survey-mode file-path evidence;
`PackageArtifactTextAudit_ListsLocationsAndKindsInMarkdownAndJsonl` gates the
content-free detail shapes, and
`PackageSignals_ReportsNoArtifactTextConcernForBackslashes` gates the close
negative. Library Signals does not yet report this row because the library
presentation model still unwraps containment to bare strings at individual row
properties rather than carrying `InertString` through the complete model.
Reporting a library-wide result before that migration would overstate its
coverage.

## Package findings audit

`Audit: Findings` explicitly scans text-bearing package files and SourceLink
document maps decoded by the SourceLink owner. Candidate text files include
known text extensions and names plus files under `content/`, `contentFiles/`,
`build/`, `buildTransitive/`, and `skills/`; known binary extensions are
excluded from the text pass. That pass is limited to 4 MiB per file and 32 MiB
per package, uses strict UTF-8/UTF-16/UTF-32 decoding, and reports read,
encoding, configuration, and limit failures instead of treating an incomplete
scan as clean. The local PDB pass reuses the product SourceLink parser and does
not acquire symbols or source over the network. It scans package-local portable
PDBs directly and embedded PDBs through managed `.dll` and `.exe` carriers.
Standalone PDB text is audited without claiming that it matches any package
assembly; identity remains mandatory for method/source mapping.

Retained output is capped at 4,096 findings and 2 MiB of encoded evidence.
SourceLink inspection is additionally capped at 4 MiB per decoded map, 32 MiB
of maps per package, 16,384 decoded mappings, 64 MiB per PE/PDB carrier, and
256 MiB of carriers per package. Reaching any cap adds one `scan limit` row and
makes the result `Partial`.

The detail table has exactly `Path`, `Kind`, and `Encoded Text`. A source line
produces one rendering finding containing all Unicode concern kinds on that
line. NuGet configuration also produces semantic rows for `<clear/>` and each
declared package source, even when the same line already has a text finding.
Each SourceLink mapping with concerning decoded text adds one row attributed to
its package PDB (or assembly for an embedded PDB). A decoded document key or URL
containing the literal `../` adds a separate `SourceLink parent path segment`
row. That row means “certainly take a look,” not “certainly malicious”:
legitimate mappings can contain parent references, while HTTP clients can
canonicalize them to a different repository path. Evidence is bounded around
the first rendering hazard and is always visually encoded before it reaches a
terminal.

When the scan runs, Signals adds `Audit | Findings` with `Detected`, `None`, or
`Partial`. Registry-backed package Signals also distinguish an
unlisted exact version and author-, repository-, unsigned-, and unverified
signature states. These remain observations rather than a trust verdict.

```bash
dotnet-inspect package X -S "Signals,Audit: Findings"
dotnet-inspect package X -S @Audit
```

`PackageContentAuditTests` gates bidi, OSC 52, NuGet configuration, strict
decoding, BOM, binary-file, case-distinct paths, bounded evidence and
cardinality, resource limits, and literal parent-path cases, including close
negatives.
`PackageAudit_RendersContentAndSourceLinkFindings` gates the
three-column Markdown and JSONL contract with a compiler-produced hostile
SourceLink PDB.
`PackageAudit_InspectsStandalonePackagePdbWithoutAnAssembly` and
`PackageAudit_MalformedStandaloneSourceLinkMapReportsPartial` gate the
package-local PDB census and visible incompleteness.
`PackageContentOutput_ContainsNoLiveControlsOnStdoutAndPreservesExplicitFileExport`
gates encoded stdout and byte-exact `--out` export.

## Identifier confusion

Library and package Signals include an `Identifier confusion` row. The package
scope covers the selected package ID, alternate package ID, dependency IDs,
runtime dependency IDs, and RID companion-package IDs. Library Signals covers
the selected assembly name and direct assembly-reference names. The explicit
library audit additionally resolves and inspects the transitive reference
closure; that unbounded traversal remains behind the explicit section gesture
rather than entering Signals.

| Value | Meaning |
| ----- | ------- |
| `None` | Every identifier inspected in that scope uses only ASCII characters. |
| `Detected` | At least one inspected identifier contains a non-ASCII character. Evidence reports counts and any confirmed reserved prefixes. |
| `Unavailable` | Required assembly-reference metadata could not be inspected. The command returns nonzero rather than claiming a clean identity scope. |

The detector first applies a non-ASCII filter. It then compares the leading
characters of each candidate with `System`, `Microsoft`, and `Azure`. The
stronger `reserved-prefix homoglyph` classification is emitted only when every
prefix character is either the corresponding ASCII character or maps to it
through the bounded Greek/Cyrillic homoglyph catalog. The reported similarity
is raw Levenshtein evidence, not a classification gate. This is a deliberately
high-confidence catalog, not an implementation of the complete Unicode
confusables table. Additional confirmed substitutions therefore cannot remove
an otherwise exact folded reserved-prefix match.

Select `Audit: Identifier Confusion` for the detected cases, or select
`@Audit` to include them with the other audit evidence:

```bash
dotnet-inspect library X -S "Audit: Identifier Confusion"
dotnet-inspect package X -S "Signals,Audit: Identifier Confusion"
```

For remotely acquired packages, the package audit performs bounded registry
metadata acquisition so that alternate package IDs are part of the declared
scope even when the identifier audit is selected by itself. When a valid feed
advertises no deprecation metadata resource, local package identifiers remain
authoritative and Signals discloses that the alternate-package scope was not
available from that source.

The detail table reports `Location`, `Kind`, `Concern`, `Reserved Prefix`,
`Similarity`, and `Characters`. `Characters` contains code-point evidence such
as `U+0405→S`; neither the Signal nor the audit rows repeat the identifier.
Transitive rows use the audit-scoped
`IdentifierConfusionReferenceClosure[index].Name` location rather than naming
the separately selected public reference-tree projection.
The filter is scoped to identifiers, so it does not reject ordinary non-English
prose. An ASCII backslash is not non-ASCII and does not trigger this audit or
artifact-text containment.

`IdentifierConfusionDetectorTests` gates single and multiple confirmed
homoglyphs, raw similarity evidence, generic non-ASCII cases, and ASCII close
negatives.
`DescribeCharacters_DeduplicatesRepeatedHomoglyphCodePoints` gates stable
code-point rendering when one substitution occurs more than once.
`PackageIdentifierConfusionAudit_ListsClassificationWithoutIdentifierContent`
gates the content-free Markdown and JSONL shapes, and
`PackageAudit_InspectsPackageAndDependencyIdentifierLocations` plus
`LibraryAudit_InspectsAssemblyAndReferenceNames` gate the typed identifier
scopes. `LibraryIdentifierConfusionAudit_CollectsDirectAndTransitiveReferenceNames`
gates the explicit library producer demand;
`PackageAllLibrariesIdentifierConfusionAudit_CollectsTransitiveReferences`
gates the survey-mode producer demand;
`LibraryIdentifierConfusionAudit_FullEffectiveDiscoveryIncludesTransitiveOnlyConcern`
gates full-effective discovery;
`LibrarySignals_FullEffectiveDiscoveryPropagatesReferenceFailure`
gates nonzero failure propagation when Signals effective discovery cannot
inspect direct references;
`LibraryIdentifierConfusionAudit_DoesNotRepeatDirectReferenceFromClosure`
gates direct/closure identity deduplication;
`LibraryAudit_PreservesCaseDistinctResolvedNames` gates case-distinct
direct/closure suppression, while
`LibraryIdentifierConfusionAudit_PreservesCaseDistinctUnresolvedReferences`
gates preservation of those spellings through traversal;
`LibraryIdentifierConfusionAudit_DeduplicatesDiamondClosure` gates one
projection row per resolved identity when several reference paths converge;
and
`AssemblyReferenceTreeResolutionTests.DistinctSameNameReferences_DoNotSuppressResolvableIdentity`
gates traversal of distinct typed AssemblyRefs that share a simple name;
`LibraryIdentifierConfusionAudit_FailsWhenResolvedReferenceCannotBeRead`
gates visible traversal failure for absolute and bare relative library paths.
That test also gates preservation of healthy `@Audit` sections when the
identifier-confusion member fails.
`LibraryPackageIdentifierConfusionAudit_FailsWithoutPartialDocument` gates the
same content-free hard failure for an exact package-backed library selection.
`PackageAllLibrariesIdentifierConfusionAudit_PreservesHealthyResultsOnTraversalFailure`
gates clean diagnostics, healthy partial results, and nonzero completion for
survey-mode traversal failure.
`LibraryCommand_TfmAll_PreservesHealthyIdentifierAuditResults` gates the same
per-source outcome contract across target frameworks.
`LibraryIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded`
and
`PackageAllLibrariesIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded`
gate visible root AssemblyRef decode failure without a false `None` result.
`LibraryReferenceTree_ReadFailureDiagnosticIsContentFree` gates the same
content-free failure category on the public reference-tree projection.
`PackagePipeline_IdentifierConfusionAudit_DemandsRegistrationMetadata` and
`InspectAsync_IdentifierAuditMetadataIncludesAlternatePackageId` gate the
alternate-package metadata demand, producer result, and moderated network cost.
`InspectAsync_IdentifierAuditMetadataFailureRemainsVisible` gates an
`Unavailable` result when that registry metadata cannot be established;
`FetchAllMetadataAsync_FlatContainerOnlyCompletesOptionalMetadata` and
`PackageCommand_FlatContainerOnlyPreservesLocalIdentifierDetection` gate that a
feed which does not advertise optional deprecation endpoints is complete rather
than failed;
`FetchAllMetadataAsync_SearchDeprecationMustMatchRequestedVersion` gates
version-specific authority for search deprecation metadata;
`FetchAllMetadataAsync_DoesNotCacheMismatchedSearchVersion` gates retry after
that mismatch, while
`FetchAllMetadataAsync_CachesMatchingSearchVersionWithoutDeprecation` and
`FetchAllMetadataAsync_CachesCatalogAuthorityDespiteSearchVersionMismatch`
gate authoritative absence and catalog precedence;
`FetchAllMetadataAsync_DoesNotCacheMismatchedInlineCatalogIdentity` and
`FetchAllMetadataAsync_DoesNotCacheMismatchedFetchedCatalogIdentity` gate the
same identity and retry contract for both catalog forms; and
`FetchAllMetadataAsync_IgnoresMalformedCatalogReference` gates retry after a
malformed catalog reference;
`PackageCommand_IdentifierMetadataFailureIsNonzero` gates nonzero completion
and content-free diagnostics for that failure;
`MultiPackageCount_CountsSelectedAuditRows` gates scalar counts against the
selected audit rows rather than unrelated package-info fields; and
`MultiPackageCount_PreservesSelectedSectionMap` plus
`MultiPackageCount_PreservesFixedOverviewMap` gate multi-section count maps.
`Package_MultiplePackages_FixedOverviewCountPopulatesSections` gates the
command path that supplies those fixed-overview sections, and
`LibraryPackageSignals_FullEffectiveDiscoveryWarnsOnce` gates one diagnostic
per package effective-discovery failure.
`LibraryCommand_SelectedReferences_TreeDedupUsesShallowestPath` gates
minimum-depth canonicalization under a bounded reference traversal.

## Build Audit Fields

### Deterministic

Whether the library was built with deterministic compilation, meaning the same source produces identical binaries.

| Value | Meaning |
| ----- | ------- |
| ✓ | Deterministic build - reproducible output |
| ✗ | Non-deterministic - may vary between builds |

**Why it matters:** Deterministic builds enable binary verification and reproducible builds.

### Reproducible Flag

Whether the library has the reproducible build flag set in its PE header.

| Value | Meaning |
| ----- | ------- |
| ✓ | Reproducible flag is set |
| ✗ | Flag not set |

**Why it matters:** The reproducible flag indicates the build system intended for reproducibility.

### SourceLink

Whether SourceLink metadata is present and whether its document map is usable.

| Value | Meaning |
| ----- | ------- |
| `Present` | The PDB carries a usable SourceLink map |
| `Present (partially usable)` | At least one mapping is usable and at least one was rejected |
| `Present (unusable)` | The map could not be parsed or contains no usable mappings |
| `Not found` | A checked PDB carries no SourceLink map |
| `Not checked` | No readable PDB was checked |

Select `SourceLink: Diagnostics` for parse errors and rejected mapping keys.
Select `Non-normalized Paths` for SourceLink document keys that do not use the
deterministic `/_/` prefix.

**Why it matters:** SourceLink connects compiled code to its exact source revision.

### Builder

For libraries branded as Microsoft (Company = "Microsoft Corporation"), indicates whether we could verify the build came from Microsoft.

| Value | Meaning |
| ----- | ------- |
| Microsoft | Verified via Microsoft symbol server (MSDL) |
| Unknown | Could not verify - symbols not found on Microsoft servers |
| *(not shown)* | Non-Microsoft library - see Company field instead |

**Why it matters:** Linux distributions rebuild .NET from source. These builds have Microsoft branding in metadata but aren't the official Microsoft binaries. The Builder field helps distinguish them.

**How verification works:**

- Microsoft publishes symbols to `msdl.microsoft.com`
- If we can download the PDB (matching the library's GUID) and extract SourceLink, it's verified
- If symbols aren't found, we can't verify who built it

## PDB Section Fields

### Format

The debug symbol format.

| Value | Meaning |
| ----- | ------- |
| Portable | Cross-platform Portable PDB (readable) |
| Windows | Legacy Windows PDB format (not supported) |
| Unknown | Could not determine format |

### Location

Where the PDB was found.

| Value | Meaning |
| ----- | ------- |
| Embedded | PDB embedded in the library itself |
| Standalone | PDB file next to the library |
| Symbol Package | Downloaded from symbol server or .snupkg |
| Unknown | PDB not found |

### Server

The symbol server that provided the PDB.

| Value | Meaning |
| ----- | ------- |
| msdl.microsoft.com | Microsoft symbol server |
| symbols.nuget.org | NuGet symbol server |
| nuget.org | NuGet symbol package (.snupkg) |
| *(not shown)* | Local or embedded PDB |

## Data Sources

dotnet-inspect pulls information from multiple sources:

| Data | Source |
| ---- | ------ |
| Library metadata | PE file headers and custom attributes |
| PDB / SourceLink | Embedded PDB, local .pdb files, symbol servers |
| Package metadata | NuGet.org API |
| Version verification | Microsoft symbol server (MSDL) |

## Common Scenarios

### Microsoft Official Build

```text
| SourceLink | ✓ |
| Builder | Microsoft |
| Server | msdl.microsoft.com |
```

Symbols downloaded from Microsoft, SourceLink verified.

### Distro Build (Ubuntu, Fedora, etc.)

```text
| SourceLink | ✗ (no symbols) |
| Builder | Unknown |
```

Library says "Microsoft Corporation" but symbols aren't on Microsoft servers. Rebuilt by distribution maintainers.

### NuGet Package

```text
| SourceLink | ✓ |
| Server | nuget.org |
```

Symbols from NuGet symbol package. Builder field not shown (not a Microsoft-branded library).

### Local/Development Build

```text
| SourceLink | ✗ |
| Location | Standalone |
```

PDB found locally but no SourceLink configured.
