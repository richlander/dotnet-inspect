# Source Finding Producers

Metadata owns the local, SRM-only observations available from PE and portable
PDB data. Network source acquisition, checksum verdicts, decompiler source
correspondence, and old/new product interpretation remain separate consumers.

## Producer inventory

| Producer | Input | Output |
| -------- | ----- | ------ |
| `InspectSourceDocuments` | A `SourceLinkService` with a loaded portable PDB, or an already-extracted `IEnumerable<SourceDocument>` | `FindingInspection<SourceDocumentObservation>` with one identity-set Finding per PDB document |
| `InspectMemberSources` | A loaded PE/PDB pair, or extracted `IEnumerable<MemberSourceInfo>`; an optional metadata-token query narrows the census | `FindingInspection<MemberSourceObservation>` with one Finding per member/document relationship |
| `InspectCompilationOptions` | Portable-PDB module `CompilationOptions` custom debug information, or extracted option rows | `FindingInspection<CompilationOptionInfo>` with one Finding per option |
| `InspectCompilationReferences` | Portable-PDB module `CompilationMetadataReferences` custom debug information, or extracted reference rows | `FindingInspection<CompilationReferenceInfo>` with one Finding per compiler reference |

The service overloads return `Absent` when no portable PDB is loaded and
`Failed` when the PDB cannot produce a complete census. The enumerable overloads
are total over an already-acquired inventory, so an empty input is
`Complete([])`.

`InspectSourceDocuments` accepts an optional `SourceDocumentQuery`. Its
`PathContains` value performs an ordinal-ignore-case substring match over the
canonical source path. A null query returns every PDB document; a query with no
matches returns `Complete([])`.

The document producer does not restrict file extensions: every portable-PDB
document is observable. Existing SourceLink availability and integrity sections
retain their narrower C#, Visual Basic, and F# population when folding the
census, preserving their established product metrics.

## Identities and coordinates

Source-document identity is the canonical path recovered from the SourceLink
wildcard mapping when available. The original PDB path, document row, resolved
URL, storage kind, and expected checksum remain typed payload data. Document-row
renumbering does not make a comparison `Changed`.

Member-source identity is the canonical `MemberAnchor` signature. Metadata token
and document row are same-version coordinates; canonical document path and line
range are the compared relationship. This lets consumers locate the exact PDB
method without using overload ordinals.

Compilation-option identity is the option name. Compilation-reference identity
is its normalized reference name; aliases, image kind, embedding, timestamp,
image size, MVID, and any currently-reserved flag bits are changeable payload
facets.

All four families use identity-set matching and leave `Finding.Ordinal` null.

## Consumer boundaries

`SourceAuditService` and `SourceIntegrityService` consume the source-document
census. Their reachability and checksum statuses are operation results and
presentation folds, not additional Metadata Findings.

`MemberSourceLocationCollector` consumes member-source Findings by metadata
token. The decompiler SourceLink oracle can later consume the same mappings,
then join a complete source acquisition with the selected member observation.

Compilation options and references describe available rebuild context. They do
not claim that the context is complete enough to reproduce the original build.

## Migration and API retirement

The Findings initially sit over the existing PDB extraction substrate. Adoption
does not require deleting the SRM readers that authoritatively produce document,
sequence-point, option, and reference rows.

Once all consumers use the censuses, these convenience APIs can be removed or
made internal:

- `SourceLinkService.GetTrackedFiles` and `GetEmbeddedFiles`; acquisition can
  consume `SourceDocumentObservation` payloads instead.
- `PdbContext.EnumerateSourceDocuments`; it remains an internal producer input.
- the overload-ordinal
  `SourceLinkService.ResolveMethodSource(type, method, overload)` and matching
  `PdbContext` method; member-source Findings replace that correspondence.
- legacy consumer-owned source inventory adapters that only duplicate one of
  the producer payloads.

These remain product APIs because they answer different questions:

- `SourceLinkService` and `PdbContext` PDB lifecycle/acquisition;
- low-level document and sequence-point extraction inside Metadata;
- `SourceFileCollector`'s public-type-to-source presentation;
- `LibraryInspection` accessibility and integrity counters, which are folds over
  acquisition results rather than source inventories.
