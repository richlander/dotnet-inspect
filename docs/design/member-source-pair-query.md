# Selected member source pair query

## Owner and claim

`DotnetInspector.Queries` owns this explicit two-endpoint operation.

> Resolve each requested metadata member in its own retained image, acquire
> each endpoint's PDB source without decompilation, and compare only complete
> verified declarations while retaining both endpoint associations and
> non-success outcomes.

This implements the bounded composition permitted by
[Research authored-source comparison](implementation-diff.md#research-authored-source-comparison).
Research supplies comparison policy, existing source services supply verified
member acquisition, and Metadata supplies exact member identity. This query
does not redefine those owners or establish Research correspondence, subject
absence, admission, or producer completion.

## Request and result

The request names an exact metadata type and member anchor independently for
each requested endpoint, two participants with their owning context groups,
and explicit source capabilities. At least one endpoint is requested. The
same-member convenience request supplies the same logical anchor to both
endpoints; callers that already own correspondence may instead supply
different exact anchors or leave one endpoint unrequested. Each requested
endpoint resolves only its own anchor in its own retained image. A physical
token from one image is never reused to resolve the other.

An unrequested endpoint is retained as `Unrequested`; it is not
query-established positive member absence. A requested missing or ambiguous
MethodDef is `NotFound`, also not positive member absence. Unsupported
non-method targets have no source comparison.

Each endpoint retains its acquisition registration, assembly identity, and
provenance. A resolved endpoint also retains its actual exact member request
and typed PDB source attempt. Unresolved, rejected, and failed inspection
outcomes remain explicit. PDB unavailability or acquisition failure does not
suppress the other endpoint's attempt.

The pair is `Compared` only when both endpoints were requested and have
complete verified member source. It retains the native
`FindingComparison<string>` and both endpoint records, including an exact
comparison with no edit rows. Otherwise it is `Unavailable`, preserving
unrequested and acquisition outcomes, or `Failed` when query validation or
comparison itself failed. An unavailable comparison may contain a failed
acquisition attempt; that attempt is not rewritten as missing source or
success.

No empty text, decompiled fallback, or generic one-sided line addition/removal
stands in for unavailable source. Exactness concerns the supplied declaration
text under the existing text-line semantics, not source authenticity, member
correspondence, C# equivalence, or IL equivalence.

## Execution boundary

The operation reuses exact-member lookup and PDB acquisition from
`AssemblyContextSourceQuery`, then supplies the exact retained implementation
and optional acquired companion through the
[assembly-context Library adapter](assembly-context-library-adapter.md).
`SourceHouse.ExecuteAuthoredAsync` owns authored candidate settlement and
declaration extraction. The query projects the settled declaration into its
existing typed PDB-source inspection and retains the native `HouseOutcome`
beside it. A terminal adapter result remains `LibraryFailure`; neither result
is turned into positive member absence.

Existing local, repository, and remote byte providers remain acquisition
authorities. Their capability adapters preserve checksum-gated reads and cache
admission; exposing the existing `SourceFetch` typed byte API avoids
re-encoding text or adding another transport. Local/repository helper
non-success remains a candidate miss under those providers' existing
contracts. SourceHouse still owns ordering, final checksum verification,
decoding, slicing, and settlement. The query does not invoke the ordinary
source query's decompiled fallback or same-member comparison.

An optional retained assembly path is not implicit permission to probe the
filesystem. `AllowAdjacentPdbReads` explicitly permits a matching PDB beside
that path; it defaults to false independently of `AllowLocalSourceReads`.
The CLI enables both. Embedded PDBs retain precedence, sidecars use Metadata's
existing identity-checked stream loader, and supplied acquisition byte limits
also bound a sidecar before loading. A missing sidecar permits the existing
symbol-acquisition path; read failures remain failed acquisition evidence.
Pathless participants do not gain a filesystem probe from this capability.

Each input's binding-policy version is captured independently. Existing
retained-image, cancellation, and source-disposal rules apply to both
acquisitions; both versions are revalidated before pair publication.
Cancellation propagates without publishing a partial pair. Invalidated query
evidence or failed owned cleanup cannot yield `Compared`. The query borrows
the groups and source capabilities; it does not acquire authority to close
host-owned groups or resources.

The acquired PDB reader closes before Library admission. The query transfers
one Library operation lease to SourceHouse and then retires the Library owner
before the adjacent Artifact session, including on cancellation and failure.
House receipt evidence records lease settlement; the pair publishes only after
the query's remaining owners settle. A primary exception remains primary when
cleanup also fails, with cleanup evidence attached.

`MemberSourcePairLimits` and `MemberSourcePairTimeout` on the existing source
context make the House plan explicit and caller-adjustable. Defaults inherit the
512 MiB retained-image ceiling for assembly and PDB snapshots, allow three
candidate categories, 64 MiB source bytes/characters, and five minutes of
settlement after upstream PDB acquisition. Target and mapping admission have
finite bounds as declared by that plan. Existing source providers retain their
own read limits; the House limit bounds acceptance, not all upstream I/O or
process memory. Adapter capture uses the larger image allowance and combined
retention uses their sum; the House enforces each role's stricter snapshot
allowance. A limit or deadline produces retained `Incomplete` evidence and a
failed host-facing source attempt, never a complete or empty comparison.
The public outcome distinguishes `SourceDeadlineExceeded` from
`SourceLimitExceeded`; neither is lexical source complexity. Producer slicing
failures preserve `SourceTooComplex`, `InvalidSequencePointCoordinates`, and
`SourceExtractionFailed`, without deriving a classification from diagnostic
prose.
Ordinary source/decompiler queries do not consume these member-pair settings.

The query is `InspectionCost.Moderated` and requires explicit source intent.
Acquisition is sequential. Its result retains evidence, not metadata readers
or content-opening capabilities.

## Consumers and limits

The immediate adopter is CLI `diff --pdb-source` with one explicitly selected
method represented by an exact MethodDef anchor and one assembly on each side.
Accessor selections that cannot retain that anchor stay on the existing
enrichment path rather than being promoted to their owning declaration.
The shared query also supplies the
[browser two-version Source facade](inspect-web-source-comparison.md).
That facade accepts the complete logical anchor for each requested endpoint so the
Member Diff Explore adopter can preserve relation-issued anchors and
one-sidedness. Its `Unrequested` endpoint remains request state; the caller's
relation evidence, not this query, owns any positive absence claim.
The former Source Diff dialog is retired; this adoption serves the published
generated facade, not a restored UI.
Existing broader CLI enrichment
is not claimed migrated or removed by this bounded cutover.

`MemberSourcePairInspection` in `DotnetInspector.Sections` now owns the final
shared handoff for that bounded pair query. It returns
`InspectionEnvelope<AssemblyMemberSourcePairResult>` after both borrowed
assembly contexts have produced one detached result. CLI `diff --pdb-source`
and the browser two-version Source operation both consume that envelope while
retaining host-owned endpoint resolution, source authorization, operation
lifetime, and presentation. Authored settlement now uses SourceHouse under
[#7448](https://github.com/richlander/dotnet-inspect/issues/7448), delivery four
of the immediate adapter-first path: assembly adapter #7313, authored
House #7368, companion handoff #7440, and this shared production cutover.
Both hosts adopt in this delivery without another host-specific composition.
The member-pair route no longer invokes `PdbSourceHouse.AcquireMemberAsync`.
Existing acquisition and local-byte helpers, ordinary type queries,
decompiler fallback, and broader CLI enrichment remain; their migration and
retirement stay under the twelve-step
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512) plan.

The pair now shares its query-owned authored handoff with
[ordinary member acquisition](member-source-acquisition.md) under #7497.
Member queries adopt authored settlement without moving their decompiler
policy into the House; pair-specific bounds and authored-only behavior remain
unchanged.

The single delivery ledger is
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706):
S1 contract alignment (landed), S2 this query, S3 CLI adoption, S4 browser
facade, S5 browser view/state, and S6 scoped retirement in
[#6250](https://github.com/richlander/dotnet-inspect/issues/6250). Six
milestones total;
CLI adoption uses S1-S3 and browser adoption S1/S2/S4/S5. S2 and S3 travel
together in [#5970](https://github.com/richlander/dotnet-inspect/issues/5970)
rather than leaving another unconsumed substrate. Browser ownership
remains under [#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

S6 removes the now-uncalled Research member-plus-PDB wrapper and its
Source-specific member-result scaffolding. It deliberately retains broad CLI
assembly enrichment, typed unavailable and failed Source rows, ordinary
single-version Source, and same-member PDB-versus-decompiled comparison.

CLI rendering consumes the typed pair alongside unchanged native C#/IL
results and lowers into its existing Markout Implementation Diff rows.
Retained line moves remain visible with their old and new declaration-relative
line numbers, including when no line content changed or moves coexist with
content edits. Only an exact pair receives an unchanged Source row. The browser
facade consumes the same pair as structured native line relations;
its feature design owns the projection and historical DOM lowering. No browser
transport or interaction contract is defined here.

## Outcome gates

The query and CLI Release gates cover compiler-produced Source-only changes,
equal source, unavailable and failed PDB source, missing targets, distinct
per-endpoint anchors, each one-sided request, same-token different-image
association, cancellation, and binding invalidation while the other endpoint
is acquired. Source-only execution does not invoke local producers. Ordinary
PDB-first/decompiled-fallback behavior is retained.

```bash
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*AssemblyContextSourceQueryTests'
dotnet run --project tests/DotnetInspect.Cli.Tests -c Release -- \
  --filter-class '*SelectedSourceDiffTests' --filter-class '*DiffCommandTests'
```

The query gate also covers explicit sidecar permission, pathless inputs,
acquisition byte limits, and owned PDB cleanup failure. The CLI gate exercises
text and JSON output, selected-document composition, native-lane preservation,
and resolved package coordinates through symbol acquisition. Compiler-produced
two-line moves, alone and alongside a content edit, gate retained move evidence;
swapping isolated lines is not a substitute for that boundary case.

Fixtures supply real compiler-produced assemblies, PDBs, and source. Product
lookup, checksum verification, extraction, and comparison produce the evidence;
tests do not fabricate successful source endpoints.

The motivating real repository asset is dotnet-inspect's compiled
`CSharpText.MemberSlicing`, its matching external PDB, and actual
`MemberTextSlicer.cs`: `SourcePair_RealRepositoryMemberUsesAuthoredHouse`
compares exact declarations through both independent endpoint adapters,
including requests with different methods and one unrequested side.
The versioned Counter fixtures preserve source-only edits and neighboring
unchanged/moved declarations. `SourcePair_SourceHouseByteBoundIsVisibleAndExact`
gates one byte below and exactly at the larger source document's length;
`SourcePair_ExpiredHouseDeadlineIsNotMissingSource` gates visible expiry.
Both bound cases assert the public outcome. The real-repository source/PDB
mutation case `SourcePair_ProducerSlicingFailuresRemainDistinct` supplies
checksum-matching token-dense and truncated documents and gates retained
producer failure classifications; product code still performs verification
and slicing.
The detached-envelope, selected CLI, and browser comparison gates assert native
House evidence, including external-companion and embedded-PDB paths.
These focused outcome cases are PR-fast.
