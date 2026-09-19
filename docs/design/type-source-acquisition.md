# Shared type source acquisition

## Owner and claim

`DotnetInspector.Queries` owns the retained-type authored acquisition handoff.
The initial delivery is tracked by #7522; explicit CLI document adoption is
tracked by #7546, following the selection and collection prerequisites in
PRs #7549 and #7580. Member Source Locations document printing adopts the
same operation under #7679.

> Settle authored source for one independently resolved retained type through
> SourceHouse, retain its selected-document mapping and native evidence, and
> publish only after owned cleanup and query-currency checks complete.
> Ordinary Type Source retains symbols for decompiler fallback; an explicitly
> selected authored document never substitutes another document or decompilation.

This adopts the existing SourceHouse type target; it does not extend the
House's supported source-policy matrix. SourceLink owns mapping and checksum
semantics, the Library adapter owns retained-content admission, and Library
and Artifact own resource settlement. The existing
[ordinary Source policy](source-finding-producers.md#consumer-boundaries)
still prefers verified authored source and otherwise uses
`CSharpDecompilerService`. Type source is not a member declaration or a promise
that all parts of a partial type are contained in one document.

## Acquisition and publication

Type and [member acquisition](member-source-acquisition.md) share the same
internal Library/House lifetime handoff rather than duplicate acquisition,
capabilities, or retirement. One upstream Portable PDB acquisition supplies
authored settlement and optional decompilation. External companions retain
independent provenance; embedded symbols remain embedded for Library admission.

The acquired SourceLink reader closes before Library admission. SourceHouse
consumes its operation lease; the query then retires the Library before its
Artifact session. Cleanup failure prevents publication. Caller cancellation
and binding-policy invalidation retain their existing terminal precedence.
The borrowed assembly-context group remains caller-owned.

Published results retain native House or terminal Library-admission evidence.
Fallback does not erase an unsuccessful authored attempt. The projection
preserves the selected type's document scope, mapping strength, partiality,
and additional-document references. The native mapping's resolved type and
homogeneous document collection, including each browse URL, resolution method,
and checksum facts, survive unchanged;
the query does not reconstruct them from paths. It does not fabricate a
complete type declaration from a document.

`TypeSourceLimits` and `TypeSourceTimeout` bound authored settlement independently
from the existing member and member-pair settings. Defaults are the same finite
authored bounds: 512 MiB per retained assembly/PDB, three source candidate
categories, 64 MiB source bytes/characters, finite target/mapping admission, and
five minutes after upstream PDB acquisition. These are not upstream transport,
process-memory, or decompiler bounds.

## Explicit authored document

`AssemblyTypeSourceRequest.AuthoredDocument` selects one original PDB path
within the exact type's mapping. It consumes SourceHouse's
[authored type-document selection](source-house.md#authored-type-document-selection)
without redefining membership, ordering, or default selection. An unavailable
selection retains its authored attempt and native evidence; it has no
decompiler attempt. The ordinary request remains primary-authored-then-decompiled.

The CLI resolves `--print --row` against its existing default-first Source Files
rows, then supplies the exact type, retained assembly descriptor, and selected
original path to the same completed `TypeSourceInspection.ExecuteAsync`
operation used by Browser Type Source. Display URLs and projected checksums
are not acquisition authority. Only the selected document is fetched and
verified against its own PDB checksum. Source Files listing remains
metadata-only; explicit Decompiled Source remains a separate request.

Member Source Locations `--print --row` also requests a whole authored document,
not a sliced member declaration. It consumes the same exact type/document
operation, using the resolved containing type and the selected location's
original PDB document path. The existing member-location mapping and accessor
preference still choose the displayed row. Acquisition independently obtains
the selected document's mapping and checksum from retained content rather than
trusting its projected URL or checksum. Member listing remains metadata-only.
The printed row, label, URL preference, and whole-file content are preserved;
missing or rejected source is a visible failure, never a decompiled substitute.

The host supplies repository paths, local-source and adjacent-PDB permission,
and existing package-source authorization. Authoritative package or Platform
descriptor provenance continues to select PDB acquisition policy, including
forwarded definitions; an optional package fallback applies only through the
existing PDB acquisition policy. Cleanup and publication use the ordinary
shared lifetime rather than a CLI-owned House composition.

## Production adoption and retirement

`TypeSourceInspection.ExecuteAsync` is the completed host-neutral facade,
returning `InspectionEnvelope<AssemblyTypeSourceEntry>` with explicit
non-projectable Share. Browser Type Source consumes its content through the
existing browser projection and operation/cancellation bridge. Its wire shape,
source policy, viewer, and rendering substrate remain unchanged.

Retained-type acquisition followed PRs #7313, #7368, #7440, #7449, and #7502
in the adapter-first path. It retired `AssemblyContextSourceQuery`'s type-side
`PdbSourceHouse.AcquireTypeAsync` composition, not that public legacy API's
remaining callers. Member and pair acquisition retain their current policies.

The overall twelve-step plan in [SourceHouse](source-house.md#production-adoption)
and #6512 includes both CLI and Browser/Wasm adoption. The CLI document slice
retires type Source Files printing's direct verified-text acquisition;
Browser Type Source retains its existing preference/fallback behavior through
the same facade. The following one-step member-document slice (#7679) retires
Source Locations printing's direct verified-text acquisition and shares the
same thin CLI document adapter. It does not change member declaration Source,
Source Diff, the Browser wire shape, or the existing Browser consumer.
Metadata-only type/member enrichment, package/library document census, and
unused batch documentation enrichment remain outside these cutovers.
They do not claim full legacy retirement.

## Evidence

The real motivating asset is dotnet-inspect at commit
`fe85fb093d46cc9ec2215b2019839ce5a750d2d6`: its compiled
`CSharpText.MemberSlicing` assembly, matching Portable PDB, and
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs` source.
Existing compiler-produced source fixtures supply
deterministic authored, missing-source, checksum, partial-document, and
decompiler neighbors. The analogous implementation is the landed member
acquisition handoff; no third-party code or new acquisition algorithm is used.
CLI adoption additionally uses this repository's two-document
`ILInspector.SourceLink.SourceLinkService` and nuget.org
`Newtonsoft.Json@13.0.3`'s `JsonReader` and `JsonReader.Async` documents.

The following PR-fast Release gates define the delivery:

| Gate | Claim |
| --- | --- |
| `AssemblyContextSourceQueryTests`, including `TypeSourceInspection_*` | Authored preference, native House/Library evidence, symbols retained for fallback, independent finite bounds, type-document scope, and existing cancellation/currency/disposal behavior. |
| `TypeSourceInspection_Explicit*` | Exact primary/additional selection, ordinal membership, selected checksums, detached evidence, package/Platform authority and fallback coordinates, and unavailable/checksum/deadline results without decompiler substitution. |
| `LocalRepoSourceProjectionTests.TypeSourceFilesPrint_SelectsExactRepositoryDocument` | The real CLI prints the exact first or second repository document while offline. |
| `LocalRepoSourceProjectionTests.MemberSourceLocationsPrint_SelectsExactRepositoryDocument` | A member in either real partial-type document prints that exact whole file offline, under both URL preferences. |
| `SourceForwarderResolutionTests.SourceDocumentAcquisition_UsesSelectedOpener` | Type/member document printing consumes the resolved descriptor through forwarding; listing performs no source-text transport, and unavailable printing fails visibly. |
| `RenderedUrlPreferenceCommandTests.SourcePrint_EmitsPreferredUrlAndUnchangedContent` | Type/member JSON, JSONL, and JSON-array printing retain the selected URL, row/section identity, and full-file text. |
| `BrowserSourceComparisonOperationTests.TypeSourceEnvelope_PreservesBrowserPreferenceAndFallback` | The production browser projection preserves authored source, missing-source/deadline fallback, provenance, and visible limitations. |
| `BrowserTypeSourceOperationTests` | The existing keyed operation, cancellation, expected failure, and scope-release contract remains intact. |
| Published `source-comparison-production.spec.ts` fixture scenario | Generated `queryTypeSource` consumes product-discovered type identity and returns authored source or visible decompiler fallback; member and pair neighbors remain intact. |

Use the existing published source-comparison gate after publishing Inspect Web.
Only package/source transport is substituted; the harness does not construct
or repair product source results. Normal CI supplies native Browser/Wasm build
evidence. These gates establish the stated behavior only when they pass.
Existing `CommandExecutionTests.Type_SourceFiles_*` slow cases remain the
focused pre-merge and daily Deep Inspect neighbors for nuget.org row selection,
output formats, missing source, and checksum failures. Expanded
`CommandExecutionTests.Member_SourceLocations_*` and `SourceDocument_PrintRow*`
slow cases cover member rows, Platform property/accessor whole-document printing,
and visible missing-source/checksum failure.
