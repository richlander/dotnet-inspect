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
> Serial Type Source retains one admitted Library across authored and
> SourceHouse decompilation operations, with a fresh lease for each. The
> opt-in latency hedge may instead admit independent immutable Libraries so
> authored acquisition and decompilation have independent settlement
> lifetimes. An explicitly selected authored document never substitutes
> another document or decompilation.

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
capabilities, decompilation, or retirement. One upstream Portable PDB
acquisition supplies authored settlement and optional decompilation. External
companions retain independent provenance; embedded symbols remain embedded for
Library admission.

The acquired SourceLink reader closes before Library admission. SourceHouse
consumes its operation lease; the query then retires the Library before its
Artifact session. Cleanup failure prevents publication. Caller cancellation
and binding-policy invalidation retain their existing terminal precedence.
The borrowed assembly-context group remains caller-owned.

Published results retain the projected PDB/authored inspection and any native
authored and decompilation House outcomes or terminal Library-admission
evidence. Fallback does not erase an unsuccessful PDB/authored attempt. When
hedged PDB preparation settles unavailable, the query may skip SourceHouse
admission; the typed PDB outcome remains the evidence and the authored House
outcome is absent. The projection preserves the selected type's document scope,
mapping strength, partiality, and additional-document references. The native
mapping's resolved type and homogeneous document collection, including each
browse URL, resolution method, and checksum facts, survive unchanged; the
query does not reconstruct them from paths. It does not fabricate a complete
type declaration from a document.

`TypeSourceLimits` and `TypeSourceTimeout` bound authored settlement independently
from the existing member and member-pair settings. Defaults are the same finite
authored bounds: 512 MiB per retained assembly/PDB, three source candidate
categories, 64 MiB source bytes/characters, finite target/mapping admission, and
five minutes after upstream PDB acquisition. `TypeDecompilationLimits`
independently bounds the fallback's detached assembly/PDB snapshots and exact
target surface; `MaxDecompilerBodyProjections` bounds its native body work.
These are not upstream transport or process-memory bounds.

## Latency hedge implementations

The serial `TypeSourceInspection.ExecuteAsync` operation and latency-sensitive
operations remain separate rather than sharing one algorithm that infers
execution capability from host identity or processor count. Each composes
existing PDB acquisition, authored SourceHouse, and decompiled SourceHouse
contracts without changing their internal policies. Explicit authored-document
requests remain serial.

`TypeSourceInspection.ExecuteWithPdbLatencyHedgeAsync` is the conservative
single-thread-compatible operation:

1. Start Portable PDB acquisition immediately. Completion means any successful
   external PDB has been published to the operation-scoped `IPdbStore`; it does
   not mean authored source is available.
2. Wait the configured Portable PDB preference window. If the PDB settles
   successfully, complete the existing serial authored-first path: await
   authored source under its independent timeout and limits, then decompile
   with the prepared companion only when authored source is unavailable.
3. If the PDB is unavailable inside the window, decompile immediately without
   a supplied companion. If the window elapses first, decompile without a
   supplied companion while PDB acquisition remains live.
4. After a PDB-window expiry, publish available decompilation without an
   additional authored-source grace period. If decompilation is unavailable,
   preserve the only remaining source path by awaiting PDB and authored
   settlement under their existing independent bounds.
5. Cancel and await unfinished PDB work before publication. Cleanup, caller
   cancellation, and binding-policy invalidation retain terminal precedence.

The PDB-only operation returns `TypeSourceLatencyHedgeEvidence`: whether the
PDB was ready when decompilation began, whether decompilation ran and observed
a PDB, and which authored/decompiled selection path published. It also
distinguishes terminal authored and decompilation Library admissions. A
PDB-only timeout is represented by
`PdbTypeSourceOutcome.PortablePdbPreferenceWindowElapsed`, not as PDB
acquisition failure or authored-source unavailability. This is execution
evidence for deterministic gates and host timing work, not a rendering section
or a claim that one timing sample establishes a universal policy.

## PDB diagnostics and Debug evidence

Every completed `TypeSourceInspection` envelope includes at most one
result-relevant Portable PDB diagnostic. The stable codes distinguish:

- `type-source.portable-pdb.available`: a matching Portable PDB participated in
  the operation;
- `type-source.portable-pdb.unavailable`: no matching Portable PDB was
  available and Type Source continued without one;
- `type-source.portable-pdb.preference-window-elapsed`: hedged acquisition did
  not settle inside the configured preference window, so decompilation
  continued without it; and
- `type-source.portable-pdb.acquisition-failed`: PDB acquisition failed and the
  remaining providers determined the result.

An operation that rejects the subject or fails before attempting PDB work
emits no PDB diagnostic. These diagnostics describe the settled result; they do
not contain request logs, timing samples, cache internals, or exception traces.
Matching-PDB availability is retained independently from a later House or
Library terminal outcome so those downstream failures cannot change the PDB
disposition.

`TypeSourceInspection.ExecuteWithPdbLatencyHedgeAndEvidenceAsync` composes the
same baseline envelope with `TypeSourcePdbAcquisitionEvidence`. The evidence
records PDB disposition and contribution to authored source and decompilation,
the hedge selection, whether decompilation started, and the
operation-scoped external acquisition document owned by
[PDB acquisition](../pdb-acquisition.md#acquisition-evidence). The ordinary
operation does not construct that document.

Inspect Web's ordinary Type Source result now carries the baseline Share and
diagnostics beside the adapted source. Its Debug-only
`QueryTypeSourceEvidence` export uses
`EvidenceInspectionBuilder<TContent, TEvidence>` to select exactly one ordinary
or evidence-enabled operation; Release builds cannot request the evidence
path. Browser-owned evidence records project the host-neutral model into the
facade wire contract rather than registering product-owned query or package
types in the Browser serializer context. The production worker validates and
preserves the added baseline fields.
CLI Type Source remains serial and consumes the baseline envelope; a future
multi-core hedge may adopt the evidence entry point when it has a production
consumer.

The PDB-only operation does not use `Task.Run`. Network transport may progress
while synchronous decompilation owns a single-threaded Browser/Wasm worker, but
managed continuations are observed only when decompilation yields or returns.
The earlier staged authored/decompilation hedge was retired because it did not
provide literal first-completion publication and materially regressed prompt
authored source on Browser/Wasm. A distinct multi-core first-completion
implementation may schedule decompilation independently only when the host
explicitly supplies that capability; it must include loser cancellation and
retirement in the completed-operation contract. That follow-up, including
possible cooperatively asynchronous type decompilation, is tracked with the
cross-host measurements in #8082.

The earlier exploratory delivery kept existing CLI and Browser product
behavior unchanged while proving the staged operation in the desktop
query-test executable. Controlled before/after measurements then showed that
the staged operation substantially regressed prompt authored-source paths on
single-threaded Wasm,
while the PDB-only operation preserved them and still removed long stalled-PDB
waits. Browser Type Source therefore adopts the PDB-only operation with a
one-second PDB preference window. The published single-threaded Browser/Wasm
gate measures the motivating package while deliberately withholding its exact
MSDL PDB response; the request must publish decompilation before that response
is released. The full CLI and Wasm matrices, exact commits, harness method, and
cancellation observations are recorded in #8082. CLI Type Source remains
serial until a separate multi-core implementation and production consumer are
defined. Browser cache lifetime remains separately owned by the Browser host
and PDB acquisition design.

The motivating production asset is
`System.Text.Json@11.0.0-preview.7.26381.103`,
`lib/netstandard2.0/System.Text.Json.dll`, type
`System.HexConverter+Casing`. Its external Portable PDB is available from MSDL,
and that PDB carries the SourceLink document
`src/runtime/src/libraries/Common/src/System/HexConverter.cs` at dotnet/dotnet
commit `e2c1e00b3d0f96afb892fb261d5921565b400246`. Current exact-type mapping does
not associate that document with the nested enum, so this asset exercises PDB
preference and decompiled fallback rather than successful authored publication.
The desktop gates use the compiler-produced source fixtures to control each
scheduling boundary deterministically; the Browser gate preserves this real
package and symbol request as the published timing scenario.

## Explicit authored document

`AssemblyTypeSourceRequest.AuthoredDocument` selects one original PDB path
within the exact type's mapping. It consumes SourceHouse's
[authored type-document selection](source-house.md#authored-type-document-selection)
without redefining membership, ordering, or default selection. An unavailable
selection retains its authored attempt and native evidence; it has no
decompiler attempt. The ordinary request remains primary-authored-then-decompiled.

The CLI resolves `--print --row` against its existing default-first Source Files
rows, then supplies the exact type, retained assembly descriptor, and selected
original path to the completed serial `TypeSourceInspection.ExecuteAsync`
operation. The Browser hedge routes any explicit-document request to that same
serial query path rather than racing decompilation. Display URLs and projected
checksums are not acquisition authority. Only the selected document is fetched
and verified against its own PDB checksum. Source Files listing remains
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

`TypeSourceInspection.ExecuteAsync` is the completed serial host-neutral facade,
returning `InspectionEnvelope<AssemblyTypeSourceEntry>` with explicit
non-projectable Share and result-relevant PDB diagnostics.
The explicit CLI [Source section](cli-source-section.md)
consumes this ordinary authored-first operation, retaining its document scope
and failed-attempt evidence. Browser Type Source instead consumes
`ExecuteWithPdbLatencyHedgeAsync` through the existing browser projection and
operation/cancellation bridge. The source case now preserves the shared
envelope's Share and diagnostics beside the existing browser source value; its
source policy, viewer, and rendering substrate remain unchanged.
`TypeSourceInspection.DecompileAsync` is the adjacent completed
decompiled-only facade, returning
`InspectionEnvelope<AssemblyTypeDecompilationEntry>` after exact Library and
Artifact retirement. It accepts explicit supplied or adjacent Portable PDB
content from the host but performs no authored-source acquisition and does not
change this document's authored-first contract. Requests created from an
`ApiType` retain its exact metadata identity and printer options, not its
listing member collection. SourceHouse resolves and composes the complete exact
type independently, so Browser fallback and ordinary CLI full-type source do
not vary with default or `--all` listing accessibility.

Retained-type acquisition followed PRs #7313, #7368, #7440, #7449, and #7502
in the adapter-first path. It retired `AssemblyContextSourceQuery`'s type-side
`PdbSourceHouse.AcquireTypeAsync` composition, not that public legacy API's
remaining callers. #7953 retires the shared query's direct
`CSharpDecompilerService.ProduceType` fallback in favor of exact-type
SourceHouse settlement and adopts that result in Browser Type Source. #7963
adopts the decompiled-only facade for ordinary CLI whole-type Decompiled Source
and retires that host's direct `MemberBodyProducer.Project` call. Member and
pair acquisition retain their current policies.

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
| `AssemblyContextSourceQueryTests`, including `TypeSourceInspection_*` | Authored preference, native authored/decompilation House and Library evidence, one retained Library with fresh leases, independent finite bounds, type-document scope, and existing cancellation/currency/disposal behavior. |
| `TypeSourcePdbLatencyHedge_*` | Deterministic scheduling with an injected clock: the PDB-only operation preserves the serial authored path after prompt PDB settlement, publishes no-PDB decompilation after PDB-window expiry, preserves late authored source when decompilation is unavailable, and aligns typed PDB diagnostics with the evidence envelope. |
| `TypeDecompilationInspection_*` | Decompiled-only exact type identity, complete-type behavior despite a filtered request model, supplied/no-PDB input, native incomplete status, terminal Library admission, binding currency, settled operation leases, detached envelopes, and no authored or network requests. |
| `TypeSourceInspection_Explicit*` | Exact primary/additional selection, ordinal membership, selected checksums, detached evidence, package/Platform authority and fallback coordinates, and unavailable/checksum/deadline results without decompiler substitution. |
| CLI `Type_DecompiledSource_*`, `TypeWholeTypeDecompilerAcquisition_*`, and bodyless memory-safety cases | Ordinary whole-type SourceHouse adoption preserves complete source across default and `--all`, selected suppliers, symbol names, exact diagnostics, enum/bodyless distinctions, Markout/bare rendering, and lazy non-source paths; neighboring listing and exact-member cases retain their independent accessibility and target boundaries. |
| `LocalRepoSourceProjectionTests.TypeSourceFilesPrint_SelectsExactRepositoryDocument` | The real CLI prints the exact first or second repository document while offline. |
| `LocalRepoSourceProjectionTests.MemberSourceLocationsPrint_SelectsExactRepositoryDocument` | A member in either real partial-type document prints that exact whole file offline, under both URL preferences. |
| `SourceForwarderResolutionTests.SourceDocumentAcquisition_UsesSelectedOpener` | Type/member document printing consumes the resolved descriptor through forwarding; listing performs no source-text transport, and unavailable printing fails visibly. |
| `RenderedUrlPreferenceCommandTests.SourcePrint_EmitsPreferredUrlAndUnchangedContent` | Type/member JSON, JSONL, and JSON-array printing retain the selected URL, row/section identity, and full-file text. |
| `BrowserSourceComparisonOperationTests.TypeSourcePdbHedgeEnvelope_PreservesBrowserPreferenceAndFallback` | The production browser selects PDB-only scheduling and preserves authored source, SourceHouse missing-source/deadline fallback, provenance, and visible limitations without changing the wire shape. |
| `BrowserTypeSourceOperationTests` | The keyed operation, cancellation, expected failure, scope-release, baseline Share/diagnostic transport, and Debug evidence serialization contracts remain intact. |
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
