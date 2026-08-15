# Source Finding producers

> **Map:** [Type, member, and API representation](type-member-api-representation.md)
> is the entry point for choosing a type, member, or API identity shape. This
> document owns the details below.

Metadata owns local, SRM-only PE/PDB extraction: named documents, checksums,
document rows, sequence-point relationships and ranges, type/member/token
correspondence, and generic custom-debug-information blobs by GUID.
ILInspector.SourceLink owns SourceLink interpretation and source decoration.
Network acquisition, checksum verdicts, decompiler correspondence, and
old/new product interpretation remain separate consumers. Typed SourceLink
queries compose the document Finding producer with shared acquisition and audit
services; they do not move network behavior into the Finding producer.

## Producer inventory

| Producer | Owner and input | Output |
| -------- | --------------- | ------ |
| `InspectSourceDocuments` | `SourceLinkFindings`; a `SourceLinkService` with a loaded portable PDB, or `IEnumerable<SourceDocument>` | `FindingInspection<SourceDocumentObservation>` with one identity-set Finding per PDB document |
| `InspectMemberSources` | `SourceLinkFindings`; a loaded PE/PDB pair through `SourceLinkService`, or `IEnumerable<MemberSourceInfo>` | `FindingInspection<MemberSourceObservation>` with one Finding per member/document relationship |
| `InspectCompilationOptions` | `MetadataFindings`; a loaded `PdbContext`, or extracted option rows | `FindingInspection<CompilationOptionInfo>` with one Finding per option |
| `InspectCompilationReferences` | `MetadataFindings`; a loaded `PdbContext`, or extracted reference rows | `FindingInspection<CompilationReferenceInfo>` with one Finding per compiler reference |

The service/context overloads return `Absent` when no portable PDB is loaded
and `Failed` when the PDB cannot produce a complete census. Enumerable
overloads are total over an already-acquired inventory, so an empty input is
`Complete([])`.

`InspectSourceDocuments` accepts an optional `SourceDocumentQuery`.
`PathContains` performs an ordinal-ignore-case substring match over the
canonical SourceLink path. A null query returns every PDB document; a query
with no matches returns `Complete([])`.

The document producer does not restrict file extensions. Existing SourceLink
availability and integrity sections retain their narrower C#, Visual Basic,
and F# population when folding the census.

## Identities and coordinates

Source-document identity is the canonical path recovered from the winning
SourceLink map entry when available. `SourceDocumentPath` uses that same match
for the canonical path and resolved URL. The original PDB path, document row,
resolved URL, storage kind, and expected checksum remain typed payload data.
Document-row renumbering does not make a comparison `Changed`.

Member-source identity is the canonical `MemberAnchor` signature. Metadata
extracts the raw member token, document row, path, range, primary-document
choice, finalizer fact, and sorted distinct start lines of every visible
sequence point in that document. SourceLink decorates that record with
canonical paths and URLs before `SourceLinkFindings` projects it; it neither
combines point lines across documents nor interprets their C# meaning.
Token-scoped queries resolve requested MethodDef rows directly rather than
scanning the assembly method table.

For multi-document methods, the portable PDB's
`MethodDebugInformation.Document` is primary when it has a visible point
relationship. Otherwise the first visible sequence point is primary.
Presentation consumers prefer that relationship and use document-row order
only as a deterministic fallback. Type-to-document correlation retains every
visible sequence-point document even when the method's root document is nil;
`MetadataSourceFindingsTests.TypeDocumentCorrelation_UsesVisibleDocumentsWhenRootIsOmitted`
gates that compiler-produced shape.

Member-source comparison treats the exact point-line set as a changeable
payload facet. Its occurrence sort key includes every compared identity and
coordinate facet, including the point set, so reversing duplicate mappings
cannot pair unlike observations and manufacture changes. This is gated by
`MetadataSourceFindingsTests.MemberSourceComparison_ReorderedDuplicateMappingsPairByComparedPayload`.

Compilation-option identity is the option name. Compilation-reference identity
is its normalized reference name; aliases, image kind, embedding, timestamp,
image size, MVID, and reserved flag bits are changeable payload facets.

All four families use identity-set matching and leave `Finding.Ordinal` null.

## Layer boundary

`PdbContext` exposes POCO records and generic raw CDI access:

- `EnumeratePdbDocuments`
- `EnumeratePdbDocumentPaths`
- `EnumerateMemberDocuments`
- `EnumerateTypeDocuments`
- `ResolveMethodDocument`
- `ResolvePdbLocation`
- `ReadModuleCustomDebugInformation(Guid)`
- `ReadDocumentCustomDebugInformation(row, Guid)`

It does not expose `MetadataReader` or `PEReader`, and it does not name
SourceLink GUIDs, maps, URLs, or provenance.

Path-only consumers use `EnumeratePdbDocumentPaths`; checksum blobs are copied
only for the full document census. A CDI read materializes its value only when
the parent and GUID identify exactly one row. Duplicate rows are reported as
ambiguous without choosing or copying a value.

`SourceLinkService` owns PDB lifecycle composition above that context. A PDB
loaded after service creation advances `PdbContext.PdbVersion`; the service
then re-extracts the map and invalidates its resolver, document, provenance,
and type-index caches before the next query.

SourceLinkFetch remains the dependency-free owner of map matching and
provenance grammar. It does not open PE/PDB files and has no Metadata project
dependency.

## Consumer boundaries

`SourceAvailabilityService` and `SourceIntegrityService` consume the
source-document census. `SourceLinkDocumentsQuery`,
`SourceAvailabilityQuery`, and `SourceIntegrityQuery` expose their composition
as host-neutral typed results with explicit absent and failed outcomes. Their
reachability and checksum statuses are operation results and presentation
folds, not additional Findings.

`MemberSourceLocationCollector` consumes member-source Findings by metadata
token. `AuthoredSourceAcquisition` consumes the same token-scoped mapping and
document census, fetches exact bytes through the SSRF-hardened Services path,
verifies the portable-PDB checksum, extracts the member body, and returns a
`FindingInspection<string>`. Its type operation resolves only the exact
`MetadataTypeDefinitionName`, verifies the primary document through the same
path, and returns the complete authored document with its typed mapping,
document, and checksum verdict. The PDB correlation retains that structured
name rather than indexing its non-injective dotted projection, and duplicate
exact identities are rejected instead of selecting the first row. It does not
use SourceLink's simple-name compatibility fallback or case-insensitive
document inference.
`MetadataSourceFindingsTests.ExactTypeSourceResolution_IsOrdinalAndDoesNotInferDocuments`
and
`MetadataSourceFindingsTests.ExactTypeIndexes_PreserveStructuredSegmentsAndRejectDuplicateIdentity`
gate that boundary. Request conversion uses `ApiType.DefinitionName` when
available; an older surface's string `MetadataName` is accepted only for an
unambiguous top-level name, because `+` cannot distinguish nesting from a
literal metadata character. This is gated by
`AssemblyContextSourceQueryTests.RequestFromLegacyApiType_RequiresUnambiguousMetadataName`.

Whole-document type output refuses more than 500,000 logical lines before
materializing the Finding census; the verified text then remains a failed
authored attempt so Decompiler fallback can run.
`AuthoredSourceAcquisitionTests.FromTypeContent_NewlineDenseSourceProducesVisibleFailedEvidence`
gates that bound. A host source-content store that reports a read or write
failure produces typed evidence and does not publish the fetched bytes to the
process-local memory cache, so an identical retry cannot silently change from
failure to authored success. The compatibility `CoreCache` adapter retains its
pre-existing best-effort persistence semantics.
`AssemblyContextSourceQueryTests.SourceStoreFailure_FallsBackRepeatablyWithoutPublishingMemoryEntry`
gates both repeatability and fallback.
Portable-PDB acquisition and validation failures follow the same composition:
the failed authored attempt remains visible while member or type decompilation
continues, and cancellation still propagates.
`AssemblyContextSourceQueryTests.PdbStoreFailure_PreservesAuthoredFailureAndFallsBackForMemberAndType`
gates external-store failure, and
`AssemblyContextSourceQueryTests.CorruptEmbeddedPdb_PreservesAuthoredFailureAndFallsBackForMemberAndType`
gates malformed embedded symbols before external acquisition begins. A PDB
that opens successfully but fails while resolving an exact type mapping follows
the same typed fallback path;
`AssemblyContextSourceQueryTests.MalformedPdbDocument_PreservesAuthoredFailureAndFallsBackForType`
gates that lazy-inspection boundary with a real PDB whose unrelated document
name is malformed. Cancellation remains exceptional but disposes an already
opened SourceLink service before it propagates;
`AssemblyContextSourceQueryTests.PdbAcquisitionCancellation_DisposesOpenedSourceLinkService`
gates that ownership boundary.

Conditional branch liveness is composed only at the member slicing boundary:
Metadata reports point lines, CSharpText reports lexical branch ranges, and the
body slicer selects a branch only when exactly one range contains point
evidence. It validates both the PDB range endpoints and every point line
against the verified physical source. Because output remains a slice of the
original authored text rather than projected text, a selected group wholly
inside that slice is omitted from a second, boundary-only projection. The
resulting declaration must remain sliceable with identical boundaries;
otherwise the slicer refuses the result rather than include a sibling from an
inactive branch.

`SourceFetcher` delegates reusable verified bytes to an
`ISourceContentStore`. Its compatibility constructor retains the desktop
`CoreCache`; content-only hosts supply `InMemorySourceContentStore`, so source
acquisition has no ambient filesystem requirement.

The same checksum evidence is carried through type, member-location, and
IL-offset projections when those views can print or derive output from source
content. Network responses are used only after the final response URL preserves
the requested URL's attributable SourceLink origin and the bytes match the
portable-PDB checksum. URLs outside the known provenance grammars carry no
repository claim but still require the checksum before their content is
rendered.

Compilation options and references describe available rebuild context. They do
not claim that the context is complete enough to reproduce the original build.
The authored rebuild harness reports `Recorded`, `Incomplete`, `Drift`, or
`Failed` beside—never folded into—the A-to-IL result.

Product `ImplementationDiff` consumes acquired line envelopes for its `Source`
mechanism; Research remains network-free. Decompiled `CSharp` and authored
`Source` are peers, so Source absence never changes or suppresses decompiler
evidence. `AssemblyContextSourceQuery` applies that rule to a selected
workspace participant: verified authored text is the preferred result;
otherwise Decompiler receives the retained assembly content and the same
binding policy as the group. A failed authored integrity attempt remains typed
beside a successful decompiled result, and neither producer succeeding yields
`AuthoredAndDecompiledUnavailable`, not empty output. The pathless authored,
fallback, integrity-failure, and neither-available cases are gated by
`AssemblyContextSourceQueryTests`.
