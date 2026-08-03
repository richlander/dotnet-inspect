# Source Finding producers

> **Map:** [Type, member, and API representation](type-member-api-representation.md)
> is the entry point for choosing a type, member, or API identity shape. This
> document owns the details below.

Metadata owns local, SRM-only PE/PDB extraction: named documents, checksums,
document rows, sequence-point relationships and ranges, type/member/token
correspondence, and generic custom-debug-information blobs by GUID.
ILInspector.SourceLink owns SourceLink interpretation and source decoration.
Network acquisition, checksum verdicts, decompiler correspondence, and
old/new product interpretation remain separate consumers.

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
choice, and finalizer fact. SourceLink decorates that record with canonical
paths and URLs before `SourceLinkFindings` projects it. Token-scoped queries
resolve requested MethodDef rows directly rather than scanning the assembly
method table.

For multi-document methods, the portable PDB's
`MethodDebugInformation.Document` is primary when present. Otherwise the first
visible sequence point is primary. Presentation consumers prefer that
relationship and use document-row order only as a deterministic fallback.

Compilation-option identity is the option name. Compilation-reference identity
is its normalized reference name; aliases, image kind, embedding, timestamp,
image size, MVID, and reserved flag bits are changeable payload facets.

All four families use identity-set matching and leave `Finding.Ordinal` null.

## Layer boundary

`PdbContext` exposes POCO records and generic raw CDI access:

- `EnumeratePdbDocuments`
- `EnumerateMemberDocuments`
- `EnumerateTypeDocuments`
- `ResolveMethodDocument`
- `ResolvePdbLocation`
- `GetModuleCustomDebugInformation(Guid)`
- `GetDocumentCustomDebugInformation(row, Guid)`

It does not expose `MetadataReader` or `PEReader`, and it does not name
SourceLink GUIDs, maps, URLs, or provenance.

`SourceLinkService` owns PDB lifecycle composition above that context. A PDB
loaded after service creation advances `PdbContext.PdbVersion`; the service
then re-extracts the map and invalidates its resolver, document, provenance,
and type-index caches before the next query.

SourceLinkFetch remains the dependency-free owner of map matching and
provenance grammar. It does not open PE/PDB files and has no Metadata project
dependency.

## Consumer boundaries

`SourceAuditService` and `SourceIntegrityService` consume the source-document
census. Their reachability and checksum statuses are operation results and
presentation folds, not additional Findings.

`MemberSourceLocationCollector` consumes member-source Findings by metadata
token. `AuthoredSourceAcquisition` consumes the same token-scoped mapping and
document census, fetches exact bytes through the SSRF-hardened Services path,
verifies the portable-PDB checksum, extracts the member body, and returns a
`FindingInspection<string>`.

Compilation options and references describe available rebuild context. They do
not claim that the context is complete enough to reproduce the original build.
The authored rebuild harness reports `Recorded`, `Incomplete`, `Drift`, or
`Failed` beside—never folded into—the A-to-IL result.

Product `ImplementationDiff` consumes acquired line envelopes for its `Source`
mechanism; Research remains network-free. Decompiled `CSharp` and authored
`Source` are peers, so Source absence never changes or suppresses decompiler
evidence.
