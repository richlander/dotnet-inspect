# Shared member source acquisition

## Owner and claim

`DotnetInspector.Queries` owns the retained-member authored acquisition handoff.

> Settle authored source for one independently resolved retained member through
> SourceHouse, preserve its typed attempt and provenance together with symbols
> needed by the caller, and publish only after owned cleanup and query-currency
> checks complete.

This is an internal composition boundary shared by ordinary member Source,
same-member authored/decompiled comparison, and selected-member source pairs.
It does not own their selection or comparison policies.
[Ordinary Source](source-finding-producers.md#consumer-boundaries) prefers
complete verified authored source and otherwise attempts decompilation.
[Same-member comparison](member-source-comparison-query.md) attempts both.
[Member pairs](member-source-pair-query.md) compare verified authored declarations
without decompilation. Those contracts remain unchanged.

Supporting owners supply exact Metadata identity, upstream PDB acquisition,
the assembly-context Library adapter, Library/Artifact lifetime, SourceHouse
authored settlement, and authorized source-byte capabilities. This handoff
does not redefine those owners, add transport, or implement House-owned
decompiler fallback.

## Acquisition and publication

One acquired Portable PDB is used for authored settlement and, when the
caller needs decompilation, retained as detached bytes for that producer.
Embedded symbols remain embedded for Library admission; external symbols
retain independent companion provenance. A second symbol acquisition is not
needed to run the caller's fallback or comparison.

The acquired SourceLink reader closes before Library admission. The House
settles its transferred operation lease, then the query retires the Library
owner before its Artifact session. Failed cleanup prevents publication;
cancellation propagates and binding-policy invalidation remains a query
failure. No borrowed context group is closed by this operation.

Every published authored attempt retains its native House outcome or terminal
Library-admission result when that stage was reached. A successful decompiler
fallback does not erase a failed authored attempt. A House deadline or finite
bound stays distinguishable from lexical complexity, invalid coordinates,
missing source, and checksum failure.

`MemberSourceLimits` and `MemberSourceTimeout` configure ordinary member Source
and same-member comparison. Their defaults match the existing member-pair
authored settlement bounds: 512 MiB per retained image, three candidate
categories, 64 MiB source bytes/characters, finite target/mapping admission, and
five minutes after upstream PDB acquisition. They bound authored acceptance and
work, not upstream transport, total memory, or the separate decompiler.
`MemberSourcePairLimits` and `MemberSourcePairTimeout` remain independent.
Type Source does not consume either member setting. Its
[type acquisition](type-source-acquisition.md) uses the same internal lifetime
handoff with independent type bounds and a type-document projection.

## Production adoption and retirement

`MemberSourceInspection` in Sections is the completed host-neutral facade.
It returns `InspectionEnvelope<AssemblyMemberSourceEntry>` for ordinary Source
and `InspectionEnvelope<AssemblyMemberSourceComparisonEntry>` for explicit
same-member comparison. Browser member Source and the explicit CLI
[Source section](cli-source-section.md) consume the first operation;
CLI Source Diff consumes the second. The existing pair facade consumes the
same authored acquisition internally. Hosts retain their permissions,
resolution, cancellation gestures, and presentation rather than independently
composing a House.

The immediate adapter-first adoption path now has five deliveries:

1. Retained assembly Library adapter, #7313.
2. Authored SourceHouse settlement, #7368.
3. Acquired Portable PDB companion adapter, #7440.
4. Shared selected-member pairs for CLI and Browser/Wasm, #7449.
5. This shared member acquisition and both existing production callers.

This is a further partial adoption within the twelve-step #6512 migration,
not completion of its full source-policy matrix. It retires ordinary member
queries' `PdbSourceHouse.AcquireMemberAsync` composition and shares the pair's
previously private adapter/House composition. Broader CLI enrichment, House-owned fallback, and full legacy retirement remain
separate. Type Source adopts the shared handoff in the following
[type acquisition delivery](type-source-acquisition.md).
The user approved this bounded member-only slice; both production hosts adopt
in this delivery.

The focused implementation is tracked by #7497.

Rendering remains unchanged: CLI uses the existing typed comparison and Markout
Source Diff lowering; Browser/Wasm projects the ordinary typed result into its
existing Source wire DTO and viewer. The browser-specific lowering is appropriate
to that existing facade, not a new rendering substrate. Native settlement
evidence remains in the managed envelope rather than being serialized wholesale.

## Evidence

The motivating real asset is dotnet-inspect at commit
`4fad2104d203f5fdac4d81ca9eef04eae4b653a7`, specifically the compiled
`CSharpText.MemberSlicing` library, matching Portable PDB, and
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs`. The member-pair adoption
already demonstrates verified authored extraction for `ExtractMemberText`;
this delivery exercises the ordinary envelope with the same real declaration.
The existing compiler-produced Counter and EmbeddedSource fixtures provide
deterministic neighboring cases requiring decompiler fallback.
CLI adoption also exposed duplicate physical/accessor projections of explicit
interface methods in House target lookup. This is an implementation correction
to the existing exact-target contract, not a new target policy:
`System.Data.DataView`'s `IBindingListView.Filter` accessors preserve that real
Platform regression.

The conventional baseline is the existing pair adapter/House handoff and
ordinary query fallback, not a new acquisition algorithm. No third-party code
is transferred. Shared composition is justified by preserving identical
ownership and failure handling across three query consumers.

PR-fast Release outcome gates:

| Gate | Owned evidence |
| --- | --- |
| `AssemblyContextSourceQueryTests`, including `MemberSourceInspection_*` and `MemberSourceComparisonInspection_*` | Authored preference; fallback with retained failure/symbols; detached native evidence; independent pair/member limits; same-PDB comparison; existing cancellation, invalidation, failed disposal, and unavailable-source outcomes. |
| `AuthoredSourceHouseTests.RealPlatformExplicitAccessor_RecognizesOnePhysicalTarget` | Repeated physical/accessor projections of a real Platform method do not reject its exact target. Existing wrong-target cases continue enforcing identity. |
| `CommandExecutionTests.Member_SourceDiff_*` | Production CLI comparison, including ordinary and explicit accessors and co-selected source sections. |
| `BrowserSourceComparisonOperationTests.MemberSourceEnvelope_PreservesBrowserPreferenceAndFallback` | Browser lowering retains authored preference and visible missing-source/deadline fallback with acquired symbols. |
| `source-comparison-production.spec.ts`: `cataloged Source-only, exact, moved, and unavailable declarations preserve evidence` | Published generated `queryMemberSource` returns authored text and visible decompiler fallback; neighboring pair operations retain their existing results. |

Run the published-browser gate with
`bash eng/test-inspect-web-source-comparison-gate.sh` after publishing Inspect Web.
The gate discovers the exact targets through the package facade and substitutes
only package/source transport, not product outcomes. Its JSON artifact records
ordinary authored and fallback output alongside the source-pair evidence.
