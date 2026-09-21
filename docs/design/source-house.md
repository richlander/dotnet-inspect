# SourceHouse composition

## Status and approved scope

This document is the normative owner for the host-neutral `SourceHouse`
composition boundary. It is tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512).

The user approved `SourceHouse` as the product-facing source settlement
concept. It composes `SourceLinkService` and `CSharpDecompilerService` over one
exact resource-free `LibraryReference`, one selected assembly
`LibraryContentReference`, and a transferred matching `LibraryOperationLease`.
Source selection and PDB use remain independent consumer choices: requesting
decompiled source does not forbid acquiring or using a PDB, and requesting
best-available source does not implicitly authorize network or filesystem
access.

This is an explicitly approved broad source-composition design. It establishes
one new owner and transfers the cohesive product source-settlement
responsibility currently divided between `PdbSourceHouse` and
`AssemblyContextSourceQuery`. It specifies the SourceHouse request, input
obligations, producer boundaries, candidate settlement, result, and receipt.
It does not redefine PackageHouse, PlatformHouse, Workspace, artifact, PDB,
SourceLink interpretation, source-fetch transport, or decompiler internals.
Their adoption remains separately reviewed through the tracker.

The first production consumer is the shared selected-member source pair for
CLI and Browser/Wasm, adopted under #7448. Shared
[member acquisition](member-source-acquisition.md) now also supplies browser
member Source and CLI Source Diff through `MemberSourceInspection` under #7497.
Shared [type acquisition](type-source-acquisition.md) supplies Browser Type
Source through `TypeSourceInspection` under #7522, CLI type-document printing
under #7546, and member Source Locations document printing under #7679. CLI
ordinary PDB Source adopts the shared member operation under #7819, and
ordinary selected-member Decompiled Source adopts decompiled-only settlement
under #7918. Exact-type decompilation and Browser Type Source fallback adopt
the same settlement under #7953. Ordinary CLI whole-type Decompiled Source
adopts the decompiled-only type operation under #7963. Broader CLI enrichment
and the full source-policy contract remain later adoption.
The tracker contains 12 ordered steps from this specification through both
host adoptions and retirement of the current duplicated composition.

The current production orchestrator is `AssemblyContextSourceQuery`, which
resolves an exact member or type and applies authored-first fallback.
Type/member authored acquisition, selected-member pairs, and exact member/type
decompilation use SourceHouse. Browser Type Source consumes the completed
shared query result with authored-first preference; its explicit authored
document requests never decompile. `PdbSourceHouse` retains broader enrichment
ordering. SourceLinkService owns checksum verification and decoding.
Query-owned fallback ordering remains migration evidence, not the target
public House policy boundary.

### Authored settlement delivery

The adapter-first implementation begins with an explicitly named
authored-only, supplied-PDB operation, tracked by
[#7356](https://github.com/richlander/dotnet-inspect/issues/7356).
It does not expose an incomplete
best-available default, decompiled demand, or external PDB-acquisition option.
Its supported input and lifetime are the exact Library, selected assembly
content, target and transferred operation lease defined below.

The implemented entry point is `SourceHouse.ExecuteAuthoredAsync`. Its
detached request evidence explicitly records `AuthoredOnly` and
`LibraryCompanionOrEmbeddedOnly`; those names describe this supported
operation, not the full future policy matrix below.

This operation uses an applicable supplied companion or embedded Portable PDB.
SourceHouse composes owner-issued document and member/type mapping evidence
with explicit host-authorized source-content capabilities. It attempts local,
then repository, then remote candidates, preserving declared order within
each category. Missing capabilities do not grant ambient filesystem or
network access. A rejected, unavailable or failed candidate remains visible
beside any later success; exhausting candidates must not flatten a failure
into authoritative absence.

The House verifies candidate bytes through SourceLinkService and uses
CSharpText for member slicing. An authored member requires a vouched
declaration; a type result retains its selected-document scope, mapping
strength and possible partiality rather than claiming a complete declaration.
Embedded PDB interpretation does not by itself promise embedded source-text
retrieval.
Type settlement retains the producer's resolved type identity and complete
document collection, including browse URL, resolution method, and checksum facts, rather than
replacing that evidence with a path-only reconstruction.

The operation's finite bounds cover detached assembly and PDB bytes,
SourceLink mapping/document work, candidate attempts, source bytes/text and
deadline. A boundary that prevents authoritative settlement returns
`Incomplete`, not empty or partial success. Host capabilities receive the
applicable bound; the House also checks returned content before interpreting
it. These are logical operation bounds, not a process-RSS ceiling. The current
SourceLink document and target-mapping APIs materialize their result before
the House can inspect its count. The corresponding limits bound acceptance
and retained House evidence, not producer enumeration or peak allocation;
assembly/PDB and SourceLink-map byte bounds still apply before that work.
Source-byte and decoded-character charges accumulate across attempted
candidates; slicing does not replace the cost of decoding a whole document.
Embedded-PDB expansion uses the stricter House and SourceLink limit, with
expanded or limit-rejected declared bytes retained as work evidence.

SourceHouse finishes reader-backed mapping before asynchronous source
retrieval. Results retain detached mapping and attempt evidence, never the
operation plan, capability, producer reader or transferred lease. Lease
settlement is recorded only after disposal; cancellation remains cancellation
after owned cleanup.

Deadline expiry must cooperatively stop an outstanding source read and settle
`Incomplete` after its owned cleanup. Checking the clock only after retrieval
returns is insufficient: a stalled, cancellation-aware source must not require
unrelated caller cancellation to release the operation. Caller cancellation
remains distinct from deadline expiry.
Absence of a supplied/embedded PDB, a correlated source document, or available
source content does not exempt completed work from the deadline. Expiry before
absence settlement returns `Incomplete` with its retained evidence. Empty
source-capability plans follow the same rule.

An unreadable SourceLink map or rejection of the selected document's mapping
remains producer failure evidence when it prevents authorized remote-source
resolution. Unrelated usable entries do not clear that document's rejection,
and unrelated rejected entries do not invalidate a resolved document.
If no permitted candidate succeeds, the relevant failure cannot become
authoritative `Unavailable`. Independently usable local
or repository source may still succeed, retaining the map's diagnostic; an
optional URL-map failure does not invalidate otherwise authoritative local-only
settlement.

This is the settlement-core portion of step 5. The public `PdbSourceHouse`
retirement obligation remains open until shared source-query adoption replaces
its callers. The [six-delivery adapter-first path](type-source-acquisition.md#production-adoption-and-retirement)
and overall twelve-step plan below retain both CLI and Browser/Wasm consumers. The member-source-pair
cutover in #7448 supplies the first shared completed
`InspectionEnvelope<TContent>` adoption, extended to member Source and same-member comparison in #7497
and Browser Type Source in #7522. These deliveries do not claim full source-policy retirement.

The member cutover also corrects exact-target lookup for explicit-interface
accessors: their physical and property/event projections can repeat the same
method token and anchor without introducing a second target. The PR-fast
`RealPlatformExplicitAccessor_RecognizesOnePhysicalTarget` cases retain the
real `System.Data.DataView` getter/setter regression; existing CLI Source Diff
accessor cases gate authored/decompiled comparison behavior.

The motivating real repository input for this delivery is
[`richlander/dotnet-inspect`](https://github.com/richlander/dotnet-inspect):
inspect its compiled `CSharpText.MemberSlicing` assembly, matching Portable PDB
and actual `MemberTextSlicer.cs` source. This permits an offline,
pathless-content success case with real method/type mappings, alongside
checksum rejection and lease-retirement cases. The Platform `System.Text.Json`
scenario below remains the broader production-adoption motivation.

#### Authored type-document selection

An authored type request settles its primary document by default. The shared
consumer selector preserves the former SourceLink preference: a filename
matching the resolved metadata leaf name plus `.cs`, case-insensitively;
otherwise the shortest filename, with discovery order breaking ties. This
heuristic is presentation/acquisition policy, not a producer fact. It does not
reorder SourceLink's native document collection. `TypeSourceDocumentSelectionTests`
gates the unchanged preference, ties, and selection from reordered real
`SourceLinkService` evidence.

An explicit selection names one exact original PDB document path in that type's
producer-issued document collection. Selection is ordinal, not a URL,
basename, case-insensitive match, or a path to open directly. The exact TypeDef
must exist before its document membership is considered. Missing membership
settles unavailable without reading source content; another type's document
in the same PDB is not a substitute.

The selected document supplies the candidate path, URL, and checksum. A
secondary document retains `AdditionalTypeDocument` scope rather than
masquerading as the primary document. The complete native type mapping retains
every document and its discovery order; selection does not rewrite that
mapping or claim a complete type declaration. The existing additional-document
projection lists documents other than the conventional default, even when one
of them is explicitly selected. The request and
settlement receipt retain the explicit selector, including when settlement
fails or hits a bound. Existing authored-only policy, authorized capabilities,
bounds, and cleanup obligations apply unchanged.

The focused prerequisite is #7544. Its real motivating asset is this
repository's partial `SourceLinkService`, including
`SourceLinkService.SourceContent.cs`. PR-fast Release
`AuthoredSourceHouseTests` cases gate explicit primary/additional selection,
ordinal membership, rejection of unrelated documents, selected-document
checksum evidence, and operation settlement.

The immediate adoption path has three focused slices: this House selection
capability (#7544), the homogeneous SourceLink collection and mechanical
consumer migration (#7577), then a shared completed CLI type-document operation
and production caller cutover (#7546). The latter must preserve metadata-only
Source Files listing, explicit authored/decompiled demand, and partial-document
row selection through `InspectionEnvelope<T>`, with the existing Browser Type
Source operation as its neighbor. This is a prerequisite within the existing
twelve-step plan, not a claim that CLI adoption or legacy retirement is complete.
No rendering changes are introduced here: the later CLI cutover retains its
Markout sections and printable-document lowering.

#### Authored member parts

An explicit member-parts demand settles the verified document together with
the [CSharpText-issued member parts](member-text-parts.md). Their coordinates
address that same decoded document, not a normalized declaration or a second
fetch. The selected member text includes its attached documentation and
attributes; the ordinary declaration-text demand keeps its existing behavior.

SourceHouse preserves the original member mapping, selected document, checksum
verification, attempts, bounds, and lease-settlement evidence alongside the
document and parts. Lexical uncertainty remains unavailable, and lexical
failure remains a failed attempt. Another source candidate may be attempted
under the existing policy; decompilation is not an authored-parts substitute.

The result is verified, PDB-correlated source. Neither checksum verification
nor lexical ranges prove exact Metadata-to-physical-declaration authorship.
The stronger
[physical-declaration correspondence](source-house-physical-declaration-correspondence.md)
tracked by #6584 and parsed documentation in #6583 remain separate.

This is slice 2 of the three-delivery plan in #7718: CSharpText parts,
SourceHouse settlement with the shared completed inspection handoff, then CLI
and Browser/Wasm production adoption. The shared query forwards the requested
form and preserves the native document/parts result in its envelope. Existing
ordinary Source can still fall back when the consumer explicitly permits it;
an authored-only parts request cannot. Hosts do not rediscover lexical bounds.

The motivating asset is `richlander/dotnet-inspect` at
`bffd209a896d0380193e8d5f0f3a8beac3770d0f`: its compiled
`CSharpText.MemberSlicing` library, matching PDB, and XML-documented
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs` `ExtractMemberText` declaration.
PR-fast Release `AuthoredSourceHouseTests` and
`AssemblyContextSourceQueryTests` member-parts cases gate original-text/span
association, checksum rejection, native evidence, explicit fallback policy,
and unchanged ordinary member acquisition. The lexical contract and its
boundary gates remain owned by CSharpText.

#### CLI ordinary PDB Source adoption

The focused CLI consumer slice is #7819. For an exact ordinal-selected member,
the CLI lowers a property or event to the selected accessor MethodDef, creates
the ordinary declaration-text `AssemblyMemberSourceRequest`, disables
decompiled fallback, and consumes the completed
`InspectionEnvelope<AssemblyMemberSourceEntry>` from
`MemberSourceInspection.ExecuteAsync`. The selected assembly participant and
MethodDef token retain the same forwarded or implementation assembly identity.
Both accessor ordinals continue to render the whole authored property.

This host route authorizes source-content capabilities only when PDB Source or
Source Diff is explicitly selected. Decompiled and analysis sections may still
acquire and reuse a PDB, but do not authorize source text. The CLI projects only
`AssemblyMemberSource.Pdb` as PDB Source and retains the House-issued
`PdbMemberSourceInspection` outcome for bodyless, unmapped, lexical-complexity,
invalid-coordinate, acquisition, and checksum failures. Source Diff continues
to use `MemberSourceInspection.CompareAsync`; Source Locations whole-document
printing and authored member parts are unchanged.

The cutover retires the CLI-private `ResolveMethodSourceAsync` acquisition,
local/repository/network source selection, checksum verification, and
declaration slicing pipeline. `AuthoredSourceDocumentPrinter.CreateContext`
remains the CLI adapter for exact assembly participation, dependency binding,
PDB stores, source capabilities, package fallback, and logging. The separate
on-disk PDB acquisition path remains for analysis and decompiler reuse because
the completed member envelope intentionally exposes no disk path. That path
opens the selected assembly path first for embedded or adjacent PDB evidence
and opens the selected supplier only when external PDB acquisition is needed,
preserving supplier authority without duplicating the ordinary metadata open.

The real repository gate uses
`richlander/dotnet-inspect@9e5c35b3bd269a1a99d287cbc59e80fc2d6c1d5b`,
its compiled `CSharpText.MemberSlicing` assembly and Portable PDB, and
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs`
`ExtractMemberText` declaration. The real-platform parity gate uses
`System.Text.Json.JsonSerializerOptions.MaxDepth` getter and setter ordinals.
Focused Release CLI cases gate exact Markdown preservation, whole-property
accessor parity, local source without SourceLink, checksum-mismatch visibility,
bodyless co-selection, unchanged Source Diff comparison, explicit PDB Source
and Source Diff authored-content authorization, and a cold-process Detailed
member request that performs no authored retrieval. Selected-supplier
decompiler cases with an adjacent PDB gate the path-first acquisition boundary
and unchanged metadata-open count. Shared query Release cases continue to gate
the detached House outcome and finite bounds.

#### CLI implementation-diff PDB Source adoption

The focused general-batch consumer slice is #7857. When `diff --pdb-source`
does not resolve to the exact selected-member pair operation, the CLI retains
the Research comparison subject as the outer association and uses Research's
typed old/new implementation-profile evidence to recover each endpoint's exact
logical owner MethodDef from `OldProfile.Method` and `NewProfile.Method`.
Complexity contributors in `OldEvidenceMethod` and `NewEvidenceMethod`,
including generated local-function bodies, remain evidence for that logical
owner and never become authored-source targets. The CLI then creates an
authored-only
`AssemblyMemberSourceRequest` from the Metadata/API-issued type, member anchor,
and token and consumes the completed
`InspectionEnvelope<AssemblyMemberSourceEntry>` from
`MemberSourceInspection.ExecuteAsync`. The CLI does not parse the Research
display label or compare it textually with the API anchor.

One workspace, source-query context, PDB store, and source-content cache are
reused across each endpoint batch. Each independently bound participant uses
its own assembly-context group, preserving that participant's binding-policy
snapshot rather than combining unrelated policy identities. The ordinary
include-all API projection supplies requests for authored methods and
accessors. If a selected token is absent only because it is compiler-generated,
the CLI lazily projects the compiler-generated API surface and SourceHouse
repeats its bounded target-surface check with compiler-generated rows enabled.
Ordinary targets retain the original bounded surface, and decompiled fallback
remains disabled for this PDB Source lane.

The completed House result is projected back into the existing
`FindingInspection<string>` comparison input with the Research subject rebound
as the presentation association. Complete findings retain their descriptor,
key, payload, ordinal, and detail. Absent, failed, rejected, and incomplete
states remain typed; existing user-visible text for no PDB mapping and for a
PDB range that does not identify one declaration is preserved. Indexing
failures remain distinct from genuine endpoint absence. Resolved package and
platform `AssemblySetEntry` provenance wins over requested version-range text.

Research currently gives the two fixture methods
`NestedGenericOuter<T>.Inner<TInner>.M` and
`NestedGenericOuter<T1,T2>.Inner<TInner>.M` the same subject identity even
though each endpoint retains two distinct MethodDefs. The CLI logs that
association ambiguity and projects it as a failed endpoint inspection instead
of choosing a token or reporting subject absence. Before this diagnostic
correction, the `net11.0` `DiffFixtures.V1` to `DiffFixtures.V2` gate emitted
`change=unavailable` with `old: The member is unavailable in the old endpoint.;
new: The member is unavailable in the new endpoint.` The corrected row emits
`change=failed`, identifies old MethodDefs `0x06000052` and `0x06000054` plus
new MethodDefs `0x06000050` and `0x06000052`, and states that they share
Research subject `M~00afb421db`. Changing that Research-owned identity contract
remains outside this SourceHouse consumer slice.

The motivating production gate compares
`System.Text.Json@9.0.0..10.0.0`
`JsonSerializerOptions` with the general Implementation Diff selection. It
preserves all 97 PDB Source rows byte-for-byte, including accessor, non-public,
nested, and compiler-generated local-function targets plus the five unavailable
rows. Focused Release CLI cases gate changed source, a missing endpoint,
accessor and non-public bodies, a normal logical owner with multiple generated
local-function contributors, generated local-function targets, explicit
implementations, removed and bodyless methods, disabled PDB Source retrieval,
and the diagnosed nested-generic ambiguity. The exact selected-member pair
route and its neighboring output remain unchanged.

The multi-assembly production case compares
`Avalonia@11.3.14..12.1.2` with type filter `Avalonia.Build.Tasks.*`.
`SelectedSourceDiffTests.GeneralSourceBatch_InspectsIndependentlyBoundAssemblies`
gates authored edits from two independently bound fixture assemblies, with
their endpoint order reversed, through the production batch and output
projection. This is a focused PR-fast case, not an exhaustive assembly sweep.

#### Shared exact-target decompilation settlement

The focused #7885 delivery settles decompiled C# for one exact MethodDef
member target through SourceHouse and adopts that operation in
`MemberSourceInspection`. The focused #7953 delivery extends the same operation
to one exact type and adopts it for ordinary shared type fallback and Browser
Type Source. SourceHouse resolves the Metadata-issued type, or type plus member
anchor and MethodDef token, against its own detached selected-assembly
snapshot, then invokes `CSharpDecompilerService.ProduceType` or
`CSharpDecompilerService.ProduceMember` with the consumer's printer options,
finite body-projection limit, explicit binding policy, and either the selected
Library companion or embedded PDB contribution or no PDB. It performs no
ambient path, adjacent-file, or network discovery. An authored-document
selector is not a decompilation target.

An exact type target always composes the complete selected type from
SourceHouse's own bounded detached metadata model, including non-public
members. A caller's API listing projection is not decompilation input and
cannot truncate full-type source. Exact-member settlement remains independently
narrow: it composes only the selected MethodDef or accessor target.

The decompiled result remains a native `CSharpDecompilationAttempt`, including
its status, text, imports, fidelity, diagnostics, method-addressed body
projections, supplied/consulted symbol evidence, and work charge. The House
adds exact request correspondence, selected Library content, detached PDB
contribution evidence, and terminal operation-lease settlement without
relabeling the result as authored source.

The focused #7918 delivery exposes that work through
`MemberSourceInspection.DecompileAsync`. The operation admits the exact
selected participant as a Library, invokes no authored-source acquisition or
comparison, and preserves terminal Library admission as typed unavailable
evidence. A host may explicitly supply an already-authorized adjacent or
acquired Portable PDB; otherwise the House may use the admitted assembly's
embedded PDB and performs no ambient discovery. The query publishes its
completed envelope only after Library and artifact retirement, then revalidates
caller cancellation and the participant binding-policy version.

The focused #7963 delivery exposes the exact-type counterpart through
`TypeSourceInspection.DecompileAsync`. It uses the same typed admission,
operation lease, native attempt, terminal Library evidence, retirement order,
and post-retirement cancellation and binding-currency checks without invoking
the authored-first `TypeSourceInspection.ExecuteAsync` operation.
`AssemblyTypeSourceRequest.From(ApiType)` retains exact type identity and
printer options, not the caller's listing member collection. Browser fallback
and ordinary CLI full-type decompilation therefore share the same complete-type
settlement regardless of default or `--all` listing accessibility. The CLI
does not rediscover members from display text or filter completed C#.

CLI ordinary selected-member Decompiled Source consumes the exact contributing
`CSharpBodyProjection`, not the aggregate composed-member text. The native
projection retains the MethodDef address, body result, contribution status, and
selected property-accessor declaration evidence needed by the existing CLI
formatter. Aggregate failed or incomplete status remains authoritative even
when a body projection exists. Fidelity Causes, Applied Taste, and Research
views still use their independently requested direct IR paths; co-selection
does not make those paths the producer of the displayed ordinary Decompiled
Source.

The assembly-context adapter keeps one admitted Library alive while a shared
member or ordinary type operation performs its authorized authored and
decompiled work. Each House operation receives a fresh lease; the adapter
retires the Library and then its Artifact session before publication, followed
by final caller-cancellation and binding-currency checks. Authored-only member
and explicit authored-document type requests never invoke decompilation.
Authored failure, absence, checksum rejection, deadline, or finite authored
bounds do not suppress an independently authorized decompiled fallback, while
an invalid owner-issued PDB companion remains a visible Library admission
failure rather than a successful no-PDB retry.

CLI same-member Source Diff consumes this path through
`MemberSourceInspection.CompareAsync`. Browser ordinary member Source consumes
it through `MemberSourceInspection.ExecuteAsync`, with the existing
`queryMemberSource` worker and `loadMemberSource` TypeScript call site.
CLI ordinary selected-member Decompiled Source consumes it through
`MemberSourceInspection.DecompileAsync`. Browser ordinary Type Source consumes
the type path through `TypeSourceInspection.ExecuteAsync`, retaining its
source-failure visibility and viewer. Its existing source value is wrapped in
the code-view union used by [Type API Declaration Inspection](type-api-declarations.md);
that separate declaration arm does not change SourceHouse settlement. CLI ordinary
whole-type Decompiled Source consumes the decompiled-only path through
`TypeSourceInspection.DecompileAsync`; it renders only an available native
attempt, treats failed or incomplete settlement as a visible command failure,
and passes true absence to the selected renderer without fabricating text.
The CLI's [native source default](rendering-model.md#native-type-and-source-defaults)
does not change that shared settlement. This intentionally
changes the earlier CLI behavior that omitted non-public members by default.
The [Type API Declaration Inspection](type-api-declarations.md) view,
`type ... -S "API Declarations" [--all] [--markdown]`, is a separate
metadata declaration contract, not SourceHouse implementation-source settlement.
Implementation Diff's C# lane and
cross-version authored member pairs remain separate consumers.

## Authority and exact claim

**SourceHouse Composition** owns:

> Given one exact type or member target in one selected assembly
> `LibraryContentReference`, its owner-issued `LibraryReference`, ownership of
> one matching `LibraryOperationLease`, one consumer-selected source-result
> demand, one independent PDB-access policy, one host-authorized source
> operation plan, and finite work, compose SourceLink-authored source and C#
> decompilation into one typed settlement that preserves the request, input
> correspondence, PDB contribution, producer attempts, selected source,
> provenance, failures, completion, and resource-free lease-settlement evidence
> without reconstructing authority from paths or display text.

The owner defines:

- the product-facing `SourceHouse` facade;
- exact type and member source requests;
- the request's exact selected assembly content reference and its required
  Library membership and assembly role;
- source-result demand and its best-available default;
- the independent PDB-access policy;
- the host-authorized source operation plan;
- the House-facing contribution contracts for `SourceLinkService` and
  `CSharpDecompilerService`;
- complete selected-type decompilation independent of API listing
  accessibility, beside narrow exact-member decompilation;
- authored-source candidate ordering over owner-issued local, repository, and
  remote capabilities;
- producer ordering, short-circuiting, and fallback;
- reuse of supplied or acquired PDB content across producer attempts;
- consumption and terminal settlement of the transferred Library operation
  lease;
- the source result envelope and settlement receipt;
- visible rejected, unavailable, failed, and incomplete outcomes; and
- the rule that product source selection is not reconstructed in hosts,
  Workspace, PackageHouse, PlatformHouse, or presentation.

It does not define:

- package, platform, project, direct-library, or Workspace realization;
- Library construction, content roles, correspondence, ownership, operation
  lease issuance, retirement, or scoped-borrowing mechanics;
- assembly, artifact, Portable PDB, or source-document identity;
- substantive assembly-to-PDB correspondence validation;
- package or symbol source authority;
- PDB discovery, acquisition, parsing, or storage algorithms;
- SourceLink map grammar, document correlation, provenance interpretation,
  checksum semantics, or source decoding;
- C# decompiler import, raising, structuring, typing, naming, formatting, or
  diagnostic behavior;
- source-byte transport, redirect policy, authorization, or caching;
- XML documentation, source-comment extraction, or documentation settlement;
- comparison of authored and decompiled source;
- Workspace admission, replacement, revisions, or binding policy;
- CLI flags, browser interaction, rendering, or serialized transport; or
- host credentials, ambient filesystem policy, network policy, cancellation
  gesture, or display policy.

Those owners issue typed content, capabilities, and outcomes. SourceHouse
composes them and preserves their evidence.

## Why source needs a House

Source production crosses two independently useful producers:

```text
LibraryReference + selected assembly content + transferred operation lease
  -> optional PDB contribution
  -> SourceLink-authored source
  -> PDB-enriched or PDB-free C# decompilation
  -> consumer-selected settlement
```

Neither producer should decide the product result:

- `SourceLinkService` can produce authenticated authored source but cannot
  decide whether decompilation is an acceptable fallback;
- `CSharpDecompilerService` can produce C# with or without PDB evidence but
  cannot decide whether available authored source should supersede it;
- PackageHouse and PlatformHouse can supply assembly and companion artifacts
  but do not own source semantics;
- Workspace can retain those artifacts but does not own source preference;
  and
- hosts choose permissions and presentation but should not reproduce
  producer ordering, PDB reuse, or failure interpretation.

SourceHouse is therefore a clearing house over one exact selected assembly in
one already-realized Library. It does not discover the Library or choose its
API or implementation assembly role. Its value is the stable association of
consumer policy, shared content, authored-source candidate settlement,
independent producer evidence, and one visible result.

This scope is smaller than PackageHouse and PlatformHouse because the input
subject is already exact. It still earns the `House` role because it is the
sole product boundary that composes independent producers under explicit
policy and settles one source result for multiple hosts.

## Relationship to composed services

The target dependency direction is:

```text
SourceHouse
  +-- SourceLinkService
  +-- CSharpDecompilerService
  +-- SourceFetch and optional PDB-acquisition capabilities
```

Neither service references SourceHouse or the other service.

### `SourceLinkService`

`SourceLinkService` is the PDB and SourceLink interpretation service.
SourceHouse supplies:

- detached input for the request's selected assembly, obtained through a
  synchronous Library snapshot;
- optional detached Portable PDB input with its exact content reference and
  provenance;
- one exact member or type target;
- bounded work and cancellation.

The service returns typed PDB and SourceLink evidence: PDB applicability,
document correlation, SourceLink provenance and mapping, checksum identity,
and the mapping's exact or inferred strength. Given candidate bytes, it also
applies the owner-issued checksum and decoding contract and returns the
corresponding authored-source observation.

SourceLinkService does not perform network or repository acquisition and does
not choose product source candidates. SourceHouse applies the
local/repository/remote candidate order over explicit capabilities, invokes
`SourceFetch` when authorized, and asks SourceLinkService to interpret and
validate each candidate. This preserves the existing family boundary:
`ILInspector.SourceLink` interprets PDB-associated program evidence, while the
`DotnetInspector` House owns product policy and transport composition.

The current `PdbSourceHouse` combines those roles. Its product candidate
settlement transfers to SourceHouse; its PDB and SourceLink interpretation
uses owner-issued SourceLink service operations. That migration is a separate
tracked implementation step rather than an expansion of the
`ILInspector.SourceLink` family.

### `CSharpDecompilerService`

`CSharpDecompilerService` is the reconstructed-source producer. SourceHouse
supplies:

- detached input for the same request-selected assembly from the same Library
  operation;
- the same exact member or type target;
- the applicable binding context;
- optional PDB content selected under the request's PDB policy;
- decompiler rendering options; and
- bounded work and cancellation.

The service returns a typed decompilation attempt retaining output,
diagnostics, completeness, and failure evidence.

The service does not acquire a PDB. It can use PDB evidence supplied by
SourceHouse, but decompilation remains available when policy or availability
provides none. It must not discover or open an embedded, adjacent, or ambient
PDB contrary to the explicit PDB contribution supplied for the request. The
existing `MemberBodyProducer` is the implementation basis; introducing
`CSharpDecompilerService` is a separate focused owner step governed by the
[C# decompiler service](csharp-decompiler-service.md) contract.

### `SourceFetch`

`SourceFetch` remains the source-byte transport capability invoked by
SourceHouse for a SourceLinkService-issued remote candidate. SourceHouse
carries the host authorization that makes that capability available, but it
does not interpret URLs, redirects, HTTP status, byte limits, or storage
outcomes.

The request-selected assembly and its corresponding PDB supplied by the exact
`LibraryReference` eliminate assembly and symbol acquisition. They do not imply
that the mapped source-document bytes are already present. Authored-source
production may still use an authorized source-content store, local repository,
or network fetch.

## Library input and operation ownership

The primary SourceHouse inputs are one exact resource-free `LibraryReference`,
one exact assembly `LibraryContentReference` selected in the source request,
and ownership of one matching `LibraryOperationLease`. PackageHouse,
PlatformHouse, and direct-library or Workspace adapters produce the shared
Library shape; the caller chooses the exact assembly content and the host or
orchestrator obtains operation authority from its `LibraryContentOwner` and
transfers that authority into SourceHouse.

The reference supplies:

- one exact logical library and physical assembly correspondence;
- exact API and optional implementation assembly content references and their
  roles;
- optional companion Portable PDB content associated with that assembly;
- any embedded Portable PDB carried inside the supplied assembly content;
- owner-issued assembly/PDB correspondence or the evidence needed by the PDB
  owner to validate that correspondence;
- source-specific provenance records without flattening them to labels;
- Artifact-registration correspondence; and
- any already-retained source content that an authorized lower service can
  address by typed identity.

The resource-free reference carries no stream, callback, opener, or other live
authority. The transferred lease authorizes synchronous snapshots of exact
content references in that Library. Before producer work, SourceHouse validates
that the request-selected content belongs to the exact `LibraryReference`, is
an API or implementation assembly role, and is accessible through the
transferred lease. It materializes a detached or independently owned value
before a snapshot callback returns; no borrow, view, or owner-backed span
crosses `await`.

The reference is not a path bundle. A local path may be source-specific
provenance or an adapter capability, but SourceHouse cannot require it, derive
another path from it, or reopen content that the Library already supplies.

PackageHouse is the preferred package path. A package realization can provide
the selected assembly and an available companion PDB in one retained
Library. SourceHouse consumes its exact references and transferred lease
without reopening the package, resolving its coordinate again, probing an
output directory, or reacquiring the supplied PDB.

PlatformHouse supplies the same Library shape while retaining its reference and
implementation view correspondence. A direct-library adapter registers
explicitly authorized local inputs into the same content form before invoking
SourceHouse. SourceHouse does not branch on which source owner produced the
Library.

`LibraryReference`, `LibraryContentOwner`, `LibraryOperationLease`, content
roles, correspondence preservation, retirement, and scoped snapshots are owned
by [Library ownership and borrowing](library-ownership-and-borrowing.md).
SourceHouse consumes that contract; it does not define a source-ready wrapper
or substitute lease.

## Exact source target

Every request names one exact assembly `LibraryContentReference` in the exact
`LibraryReference` and addresses one target in that selected content:

- an exact metadata type definition identity; or
- an exact method definition identity plus its declaring-type and member
  correspondence.

Display names, source file names, source URLs, package labels, assembly
identity, and metadata token text are not substitutes for the typed content
reference and target. A content reference outside the Library or without an
assembly role is rejected. Resolution against the selected assembly must
produce one exact target or a typed non-success before either source producer
runs.

Type and member requests may carry decompiler options. Those options affect
only reconstructed source. Authored source remains the verified producer text.

## Source-result demand

Source-result demand is closed:

- **BestAvailable** requests authored source first and decompiled source only
  when authored source is unavailable. This is the default.
- **AuthoredOnly** requests only PDB-correlated authored source and never
  invokes the decompiler.
- **DecompiledOnly** requests decompiled source and never selects authored
  source as the result.

`DecompiledOnly` constrains the selected output, not the evidence available to
the decompiler. It may acquire and use a PDB when the independent PDB policy
authorizes that work.

Best-available is an ordering contract, not a universal quality claim.
Authored source is preferred because it carries checksum-verified provenance
to producer text. Decompiled source remains a distinct reconstructed
representation with its own diagnostics and fidelity boundaries.

An operation that needs both authored and decompiled attempts for comparison
is not `BestAvailable`. It is a separate explicit comparison demand or query
because it deliberately defeats short-circuiting and incurs both producers'
work. Comparison is outside this design's first contract.

## PDB-access policy

PDB access is independent from source-result demand. The initial closed policy
is:

- **None** does not extract, open, acquire, or supply PDB content to either
  producer.
- **SuppliedOnly** may use companion PDB content associated with the
  request-selected assembly or extract an embedded PDB from that assembly
  content, but does not acquire an external replacement.
- **AllowAcquisition** uses supplied PDB content first and, when no applicable
  PDB is available, permits one attempt through the host-authorized PDB
  acquisition capability.

`SuppliedOnly` is the content-first default. It makes PackageHouse,
PlatformHouse, and direct-library realization responsible for supplying
content they already possess while preventing SourceHouse from silently
widening the operation.

Extracting an embedded PDB from supplied assembly bytes is interpretation of
supplied content, not external acquisition. The resulting contribution is
recorded distinctly from a supplied companion PDB. `None` still forbids that
extraction and requires `CSharpDecompilerService` to run without symbols.

`AllowAcquisition` is permission to attempt acquisition, not a promise that a
PDB exists and not a requirement that the whole source operation fail without
one. For `DecompiledOnly`, SourceHouse attempts authorized PDB acquisition so
the decompiler can use the result; failure remains visible beside a possible
PDB-free decompilation success. For `BestAvailable` or `AuthoredOnly`, the same
PDB attempt can support authored-source production.

`AuthoredOnly` with `None` has no legal producer in the initial contract and
is rejected before work. Other combinations remain valid:

| Source demand | PDB policy | Behavior |
| --- | --- | --- |
| BestAvailable | None | Decompile without PDB |
| BestAvailable | SuppliedOnly | Try authored source from supplied companion or embedded PDB, then decompile with the same PDB |
| BestAvailable | AllowAcquisition | Use supplied or embedded PDB, or attempt one acquisition, then apply authored-first fallback |
| AuthoredOnly | SuppliedOnly | Use supplied companion or embedded PDB and return authored source or its typed non-success |
| AuthoredOnly | AllowAcquisition | Use or acquire a PDB, then return authored source or its typed non-success |
| DecompiledOnly | None | Decompile without PDB |
| DecompiledOnly | SuppliedOnly | Decompile with supplied companion or embedded PDB when present |
| DecompiledOnly | AllowAcquisition | Use or acquire a PDB, then decompile |

The House never treats the absence of a host capability as permission to find
an ambient filesystem or network substitute.

If `AllowAcquisition` has no authorized PDB capability, the PDB contribution
records that limitation and settlement continues wherever the source demand
permits PDB-free decompilation. It does not reject the whole request.

## Host-authorized source operation plan

The host supplies an immutable plan containing only capabilities authorized
for this operation:

- optional PDB acquisition;
- optional source-content retrieval and content-store access;
- optional local-source and repository-source access;
- assembly binding policy required by decompilation;
- source and decompiler work limits;
- byte, document, candidate, and deadline limits where applicable;
- operation identity and policy generation; and
- caller cancellation.

The plan is capability, not evidence. Supplying a symbol client does not prove
that a PDB exists. Supplying source fetch does not prove that a SourceLink URL
is authorized or available. Supplying repository paths does not prove a
matching revision or checksum.

Credentials and host-native clients do not enter the resource-free settlement
receipt. The receipt records capability identity and the owner-issued outcomes
needed to explain the operation.

## Settlement

SourceHouse preserves this semantic order:

1. accept the exact request, selected assembly `LibraryContentReference`,
   `LibraryReference`, transferred matching `LibraryOperationLease`, and policy
   generation;
2. validate the selected assembly's exact Library membership and role, then
   resolve the exact target against its content through a synchronous Library
   snapshot;
3. apply the PDB policy, always preferring applicable supplied companion or
   embedded content over acquisition;
4. when authored source is permitted, ask `SourceLinkService` for typed
   mapping evidence, settle authorized source-document candidates, and ask the
   service to validate and decode candidate bytes;
5. select an available authored-source result immediately for `BestAvailable`
   or `AuthoredOnly`;
6. when decompiled source is permitted and not already short-circuited, ask
   `CSharpDecompilerService` using the same assembly and applicable PDB
   content; and
7. materialize one terminal result retaining every attempted contribution,
   settle the transferred Library lease, and return only resource-free
   evidence.

The ordering does not require one implementation method. It requires that:

- supplied assembly or PDB content is not reacquired;
- SourceHouse never chooses between API and implementation assembly roles;
- no Library borrow, callback view, or owner-backed span crosses `await`;
- the transferred Library lease is settled on success, rejection, failure,
  cancellation, and incomplete completion;
- owner retirement after lease issuance does not invalidate the operation;
- one acquired PDB is reused across both producers;
- `BestAvailable` does not decompile after an authored-source result meets the
  requested source unit;
- `AuthoredOnly` never invokes the decompiler;
- `DecompiledOnly` never selects authored source but may use PDB evidence;
- no producer runs without request and host authorization;
- producer failure does not become absence;
- successful fallback retains the earlier unsuccessful attempt; and
- Library or lease mismatch, absent selected-content membership, non-assembly
  role, rejected correspondence, or borrow failure cannot publish a successful
  source result.

## Result and receipt

Every SourceHouse result preserves:

- the exact source request and target;
- the exact `LibraryReference`, request-selected assembly
  `LibraryContentReference`, and companion content references used;
- the selected source-result demand;
- the PDB-access policy and operation-plan identity;
- one PDB contribution: not requested, supplied, acquired, unavailable,
  rejected, failed, or incomplete;
- one authored-source attempt when requested;
- one decompiled-source attempt when requested;
- the selected provider and source text when available;
- producer-specific provenance and diagnostics;
- operation completion and charged work; and
- resource-free evidence that the transferred Library lease settled.

The terminal result family is closed:

- **Available** carries one selected authored or decompiled source result and
  every preceding attempt relevant to that selection.
- **Unavailable** means every producer permitted by the request completed
  authoritatively without usable source.
- **Rejected** means the request itself is contradictory, the exact target is
  invalid, or an owner-issued input correspondence claim is invalid.
- **Failed** preserves an operational or decode failure that prevents the
  requested settlement.
- **Incomplete** means a finite work, byte, candidate, or deadline boundary
  prevented authoritative settlement.

Caller cancellation remains cancellation rather than a source result.

An unsuccessful PDB or authored-source attempt is not necessarily terminal.
For `BestAvailable` and `DecompiledOnly`, complete decompiled source may still
produce `Available`; the result retains the PDB and authored-source limitation
beside it. `AuthoredOnly` cannot convert that limitation into decompiled
success.

Text alone is not the result contract. A caller can distinguish authored from
decompiled source, identify which PDB contribution was used, inspect why an
earlier attempt failed, and retain the exact input correspondence without
parsing a label or diagnostic.

Every result contains detached or independently owned values. It retains no
Library lease, callback, borrow, span, stream, opener, or path-reopening
authority.

Candidate rejection is contribution-local. An unauthorized source URL,
checksum-rejected source document, unusable optional PDB, or unavailable PDB
capability remains in its producer contribution and may permit another
authorized candidate or decompiled fallback. It becomes request-wide
`Rejected` only when the request is contradictory or a required owner-issued
input correspondence is invalid.

A supplied PDB candidate that does not match the request-selected assembly is
disqualified and retained as a rejected contribution. With
`AllowAcquisition`, the House may attempt an authorized replacement; with
`SuppliedOnly`, it proceeds as though no usable PDB contribution exists. If the
`LibraryReference` carries owner-issued correspondence that claims the
mismatched PDB is applicable to that assembly, the broken claim rejects the
input rather than becoming an ordinary candidate miss.

## Content and lifetime

SourceHouse is content-first:

- assembly and PDB inputs use exact resource-free content references;
- one transferred Library operation lease governs the complete async
  settlement;
- content is borrowed only through synchronous snapshots, and values needed
  after a callback are detached before it returns;
- the House and both services operate without requiring filesystem paths;
- supplied content remains tied to its artifact generation and provenance;
- producer readers and borrowed views do not outlive their synchronous
  snapshots;
- materialized source text does not retain live metadata or PDB readers; and
- success, rejection, failure, cancellation, and incomplete completion settle
  the transferred lease and every acquired resource.

The House does not mutate the supplied `LibraryReference` when it acquires a
PDB. Its result records detached contribution evidence. Publishing acquired
content into a reusable Artifact or a new Workspace-owned Library is a separate
owner operation.

## Failure boundaries

The following distinctions remain visible:

- no exact target versus a producer that could not produce source;
- no PDB supplied versus PDB acquisition disallowed;
- PDB unavailable versus PDB acquisition failed;
- PDB identity mismatch versus no SourceLink map;
- no correlated document versus mapped document absence;
- source-content failure versus checksum rejection;
- decompiler unsupported input versus decompiler operational failure;
- one successful fallback with an earlier limitation versus both producers
  unavailable; and
- incomplete evidence versus authoritative absence.

SourceHouse does not catch arbitrary failures and return empty source. Each
service retains its own typed detail; the House classifies only the effect on
the requested settlement.

## Pathological cases

### Supplied PDB has no source document

PackageHouse supplies an assembly and matching Portable PDB. The PDB has useful
local names but no source document correlated with the requested body.
`BestAvailable` retains the authored-source absence and passes the same PDB
content to `CSharpDecompilerService`. It does not reacquire another PDB merely
because source mapping was absent.

An available authored type result may be a primary document selected from
method correlation or filename inference, and a type may be partial across
documents. Availability therefore preserves the producer's source-unit scope,
mapping strength, and partiality; it does not claim that one document contains
the complete physical declaration. Best-available short-circuits on the
requested source unit, not on an invented stronger correspondence.

### Decompiled-only source with authorized PDB acquisition

The `LibraryReference` names only assembly content. The consumer requests
`DecompiledOnly` with `AllowAcquisition`. SourceHouse attempts one authorized
PDB acquisition, supplies any resulting PDB to the decompiler, and never
selects authored source. PDB acquisition failure remains visible beside a
possible PDB-free decompilation success.

### Authored source fails integrity

The PDB maps the target to a source document, but every supplied or acquired
document candidate fails the PDB checksum. `BestAvailable` may return
decompiled source while preserving the integrity failure.
`AuthoredOnly` returns the authored-source non-success and does not invoke the
decompiler.

### Library retirement and exact authority

If Library retirement begins before operation-lease issuance, the Library owner
returns its visible `OwnerRetiring` or `OwnerReleased` outcome and SourceHouse
does not start. If retirement begins after issuance, the transferred lease
remains usable while the owner drains it; SourceHouse completes or fails
normally and settles the lease on its terminal path.

A lease for another exact `LibraryReference`, a moved or settled lease, or a
content reference outside the Library is rejected. SourceHouse does not reopen
a path, reacquire a same-named package, or use a newer Workspace participant to
manufacture correspondence.

The concrete case is the .NET 11 Platform `System.Text.Json` Library. Its
runtime assembly and matching Portable PDB belong to one exact Platform
`LibraryReference`; the source request selects the runtime assembly's exact
`ImplementationAssembly` content reference, and supplied PDB selection follows
its recorded companion correspondence. A separately realized NuGet package may
expose the same assembly identity but remains another Library. A lease for
either Library cannot borrow the other's assembly or PDB.

### Browser/Wasm has no paths

PackageHouse supplies an in-memory Library owner and resource-free assembly and
PDB references. The host transfers a matching operation lease to SourceHouse,
which invokes the same services and settlement as the CLI without a filesystem
path. Authored source may use an authorized in-memory content store or network
fetch; decompilation consumes detached content snapshots.

### Assembly content contains an embedded PDB

With `SuppliedOnly` or `AllowAcquisition`, the PDB owner may extract the
embedded PDB from the supplied assembly content and issue an embedded
contribution without external acquisition. With `None`, neither SourceHouse
nor `CSharpDecompilerService` opens that embedded PDB. This preserves the
consumer's PDB choice even when symbol content is physically colocated with
the assembly.

## Analogous implementation evidence

The analogues inform producer separation and policy shape; they are not
architectural authorities for this repository.

| Implementation | Observed behavior | SourceHouse lesson |
| --- | --- | --- |
| [Roslyn Metadata-as-Source provider ordering](https://github.com/dotnet/roslyn/blob/a44ae6bcfdd8b8c4ac920f033fd02894983c79db/src/Features/Core/Portable/MetadataAsSource/MetadataAsSourceFileService.cs#L103-L117) | PDB source is attempted before the terminal decompilation provider. | Keep authored-source preference in a composition owner rather than either producer. |
| [Roslyn Metadata-as-Source options](https://github.com/dotnet/roslyn/blob/a44ae6bcfdd8b8c4ac920f033fd02894983c79db/src/Features/Core/Portable/MetadataAsSource/MetadataAsSourceOptions.cs#L17-L36) | Decompilation and symbol/SourceLink behaviors are separately configurable. | Keep source-result demand separate from acquisition authorization. |
| [ILSpy debug-information loading](https://github.com/icsharpcode/ILSpy/blob/7777f573c54ef0155725d3a56f0222edd79af35f/ICSharpCode.ILSpyX/PdbProvider/DebugInfoUtils.cs#L35-L123) and [C# decompilation](https://github.com/icsharpcode/ILSpy/blob/7777f573c54ef0155725d3a56f0222edd79af35f/ILSpy/Languages/CSharpLanguage.cs#L322-L367) | Optional PDB information enriches decompilation rather than changing the output producer. | `DecompiledOnly` must not imply PDB-free execution. |
| [PerfView SourceLink authorization](https://github.com/microsoft/perfview/blob/da37ec113a0545ed20b3a7fcfce006706c166cc1/src/TraceEvent/Symbols/SymbolReader.cs#L2773-L2815) | Remote source retrieval is gated by caller-provided authorization. | Output preference does not confer network permission. |

Roslyn's source loader also has a documented remote-checksum bypass. That is
not precedent for SourceHouse: dotnet-inspect retains its existing requirement
that every successful PDB-mapped source document satisfy the Portable PDB
checksum contract.

## Production adoption

[#6512](https://github.com/richlander/dotnet-inspect/issues/6512) is the
end-to-end tracker. Its current total is 12 steps:

1. lock this focused SourceHouse structure and input contract;
2. reconcile SourceHouse input and lifetime with the shared Library ownership
   and borrowing contract;
3. introduce `CSharpDecompilerService`;
4. expose content-backed PDB and SourceLink interpretation through
   `SourceLinkService` without moving transport or product policy into it;
5. implement SourceHouse authored-source candidate settlement and retire the
   public `PdbSourceHouse` composition;
6. implement SourceHouse decompiler composition, fallback, and receipts;
7. adopt the shared Library contract in PackageHouse;
8. adopt it in PlatformHouse;
9. adopt it for direct-library and Workspace assembly-context inputs;
10. delegate `AssemblyContextSourceQuery` to SourceHouse and retire its fallback
   policy;
11. adopt SourceHouse in Inspect Web Browser/Wasm; and
12. adopt SourceHouse in the CLI, retire the remaining legacy composition, and
    close the source portion of #6335.

Each step changes one owner. Steps 2-9 establish the reusable path; steps 10-12
migrate production consumers and retire the alternative architecture. A change
to the count or host coverage requires an explicit tracker update.

The user-approved adapter-first sequence brings the assembly-context portion
of step 9 ahead of step 5 under
[#7312](https://github.com/richlander/dotnet-inspect/issues/7312).
The [Workspace-owned adapter](assembly-context-library-adapter.md) supplies
Library input for later House adoption; it does not complete step 9's broader
adoption or the source-query and host migrations. The twelve steps and both
production hosts remain in scope.

The immediate delivery path is the merged adapter in
[#7313](https://github.com/richlander/dotnet-inspect/pull/7313), the authored
settlement core in #7356, supplied-PDB adapter admission in
[#7439](https://github.com/richlander/dotnet-inspect/issues/7439), and the shared
member-source-pair cutover in
[#7448](https://github.com/richlander/dotnet-inspect/issues/7448) through its existing completed
`InspectionEnvelope<TContent>` for CLI and Browser/Wasm. These four deliveries
reach the first production consumers without combining the adapter's companion
contract with query adoption. Shared member Source/comparison (#7497) and type
acquisition (#7522) extend that path to six deliveries. Each retires only the
composition it replaces; House-owned fallback, acquisition/decompiler modes,
broader CLI enrichment, and remaining callers stay tracked by the twelve steps
above. CLI ordinary PDB Source (#7819) consumes that completed member operation
with authored-only declaration demand and retires its duplicated acquisition,
verification, and slicing path without changing Source Diff or PDB-assisted
decompilation. Exact-member decompilation (#7885 and #7918) and exact-type
decompilation with Browser fallback adoption (#7953) use the same Library
handoff and native producer attempt. Ordinary CLI whole-type decompilation
adopts the completed decompiled-only type envelope under #7963 and retires its
direct `MemberBodyProducer.Project` composition.

Step 2 is the design correction tracked by
[#6934](https://github.com/richlander/dotnet-inspect/issues/6934). SourceHouse
implementation remains staged behind the Library contract floor, concrete
owner, and producer adoption in #6621; this document does not claim those
implementation steps are complete.

## Evidence plan

### Implemented authored-delivery gate

Run `dotnet run --project tests/DotnetInspector.SourceHouse.Tests -c Release`.
The suite runs in the ordinary CI contracts shard. The calibrated native-mapping
deadline regression is tagged `Speed=Slow` under the repository's isolated-time
threshold and remains included in this focused pre-merge gate; the other cases
are PR-fast. It covers both a mapped partial type and an unmapped enum in the
real SourceLinkService assembly, retaining document work to distinguish
post-mapping expiry from earlier stops.

| Property | Named cases |
| --- | --- |
| Exact real member, ordered candidates, checksum gate and detached receipt | `RealRepositoryMember_OrdersCapabilitiesAndReturnsExactSlice`, `ChecksumRejectionExhaustion_IsUnavailableWithAttemptEvidence` |
| Primary-document scope and real multi-file type partiality | `RealRepositoryPartialType_ReturnsPrimaryAndAdditionalDocuments` |
| Embedded PDB use and bounded expansion accounting | `EmbeddedPdb_ReturnsSourceAndChargesExpandedBytes`, `EmbeddedPdb_UsesStricterHouseLimitAndChargesDeclaredBytes` |
| Correspondence rejection versus producer uncertainty | `ForeignLease_IsRejectedAndSettled`, `MismatchedClaimedPdb_RejectsOwnerCorrespondence`, `ExactTargetMismatch_IsRejected`, `TargetMissingUnderInspectionFailure_IsFailedNotRejected` |
| Candidate-local failure and retained incomplete evidence | `CapabilityFailures_AreRetainedAndLaterCandidateCanSucceed`, `FiniteBoundary_ReturnsIncomplete`, `CandidateAttemptBoundary_PreservesMappingAndEarlierAttempt`, `SourceByteBoundary_PreservesRejectedAttemptAndObservedBytes`, `DeadlineAfterCapability_RecordsCompletedAttemptAndWork` |
| Cooperative deadline settlement, empty capabilities after mapping, final checksum rejection, exception parity and long finite deadlines | `DeadlineDuringCapability_CancelsSuppliedTokenAndReturnsIncomplete`, `DeadlineDuringMappingWithoutCapabilities_IsIncomplete`, `DeadlineDuringFinalChecksumRejection_IsIncomplete`, `LateRecognizedCapabilityExceptionAfterDeadline_IsIncomplete`, `DeadlineBeyondSingleTimerRange_CanCompleteNormally` |
| SourceLink-map failure relevance and successful independent fallback | `UnusableSourceLinkMap_RemoteExhaustionIsFailed`, `UnusableSourceLinkMap_IndependentRepositorySourceCanSucceed`, `UnusableSourceLinkMap_LocalOnlyAbsenceRemainsUnavailable`, `PartiallyUsableSourceLinkMap_PreservesDocumentFailure` |
| Transferred ownership and detached outcomes | `NullRequest_StillSettlesTransferredLease`, `CancellationDuringCapability_SettlesLease`, `UnexpectedCapabilityException_PropagatesAfterSettlement`, `OwnerAndArtifactRetirement_DrainIssuedOperation`, `PublicOutcomeClosureRetainsNoLiveAuthority` |

These cases gate the authored-only delivery, not production-host parity,
external PDB acquisition or decompiled fallback.

### Implemented exact-target decompilation gate

Run
`dotnet run --project tests/DotnetInspector.SourceHouse.Tests -c Release -- --filter-method '*Decompilation*'`.
The PR-fast cases cover exact member and nested generic type identity,
no/supplied/embedded PDB contribution, invalid supplied PDB preservation,
finite assembly and body-projection bounds, target rejection, native
body-address evidence, fresh operation leases, and cancellation settlement.

Run
`dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- --filter-class DotnetInspector.Queries.Tests.AssemblyContextSourceQueryTests`.
The focused type-source and decompiled-only cases cover authored-first
short-circuiting, ordinary fallback through the House outcome,
explicit-document non-substitution, supplied/no-PDB decompiled-only operation,
native incomplete status, terminal Library admission, binding-currency
rejection, authored deadline and source-bound fallback, existing cancellation
and disposal outcomes, and the unchanged completed envelope used by Browser
Type Source.

Run the focused `DotnetInspect.Cli.Tests` whole-type Decompiled Source and
`TypeWholeTypeDecompilerAcquisition_*` cases. They cover the production CLI's
selected supplier, explicit/adjacent symbol input, generic and enum listings,
bare and ordinary Markout output, memory-safety diagnostics, terminal selected
input failure, absent completed-inspection rejection, complete-type parity
across default and `--all`, and lazy ordinary and discovery paths. The real
`System.Text.Json@10.0.5` `JsonNamingPolicy` baseline gates inclusion of its
protected instance constructor and static constructor in both modes. A focused
fixture separately proves that ordinary member listings still apply
accessibility while exact-member source remains one selected method.

### Remaining full-composition evidence

The implementation slices must provide Release gates for:

- every valid source-demand and PDB-policy combination;
- rejection of `AuthoredOnly` with `None`;
- supplied assembly and PDB content requiring no path or reacquisition;
- embedded-PDB extraction under `SuppliedOnly` and suppression under `None`;
- one acquired PDB being reused by authored-source and decompiler attempts;
- `DecompiledOnly` acquiring and using a PDB when consumer policy authorizes
  it;
- best-available short-circuit after authored-source success;
- authored-only never invoking the decompiler;
- successful decompiled fallback retaining PDB and authored-source failure;
- contribution-local PDB or source rejection permitting valid fallback while
  invalid owner-issued input correspondence rejects the whole request;
- an issued Library lease remaining usable when owner retirement begins, while
  issuance after retirement fails visibly;
- no borrowed content crossing `await`, and no live Library authority entering
  a result or receipt;
- authored type results retaining mapping strength, source-unit scope, and
  partiality without claiming a complete declaration;
- checksum-rejected source never becoming available;
- exact target, selected assembly content, Library membership and role, lease
  settlement, and result correspondence;
- finite-work incomplete outcomes;
- Library-lease and acquired-resource settlement on success, rejection,
  failure, cancellation, and incomplete completion;
- equivalent CLI and Browser/Wasm settlement for the same typed request; and
- dependency-policy enforcement that keeps SourceLink and Decompiler
  independent beneath SourceHouse.

Beyond the named authored-delivery gate above, these full-composition claims
remain unverified until their Release gates land in the corresponding
implementation slices.
