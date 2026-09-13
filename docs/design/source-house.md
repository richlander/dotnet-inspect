# SourceHouse composition

## Status and approved scope

This document is the normative owner for the host-neutral `SourceHouse`
composition boundary. It is tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512).

The user approved `SourceHouse` as the product-facing source settlement
concept. It composes `SourceLinkService` and `CSharpDecompilerService` over an
owner-issued content-backed library representation. Source selection and PDB
use remain independent consumer choices: requesting decompiled source does not
forbid acquiring or using a PDB, and requesting best-available source does not
implicitly authorize network or filesystem access.

This is an explicitly approved broad source-composition design. It establishes
one new owner and transfers the cohesive product source-settlement
responsibility currently divided between `PdbSourceHouse` and
`AssemblyContextSourceQuery`. It specifies the SourceHouse request, input
obligations, producer boundaries, candidate settlement, result, and receipt.
It does not redefine PackageHouse, PlatformHouse, Workspace, artifact, PDB,
SourceLink interpretation, source-fetch transport, or decompiler internals.
Their adoption remains separately reviewed through the tracker.

The first production consumer is Inspect Web package type/member Source. The
CLI and Browser/Wasm hosts then converge on the same SourceHouse contract.
The tracker contains 12 ordered steps from this specification through both
host adoptions and retirement of the current duplicated composition.

The current implementation basis is `AssemblyContextSourceQuery`, which
resolves an exact member or type, attempts PDB-mapped source, and falls back to
`MemberBodyProducer`. `PdbSourceHouse` currently owns PDB-specific source
candidate ordering and checksum verification. These are migration evidence,
not the target public composition boundary.

## Authority and exact claim

**SourceHouse Composition** owns:

> Given one exact type or member target, one owner-issued source-ready library
> representation, one consumer-selected source-result demand, one independent
> PDB-access policy, one host-authorized source operation plan, and finite
> work, compose SourceLink-authored source and C# decompilation into one typed
> settlement that preserves the request, input correspondence, PDB
> contribution, producer attempts, selected source, provenance, failures,
> completion, and retained lifetime without reconstructing authority from
> paths or display text.

The owner defines:

- the product-facing `SourceHouse` facade;
- exact type and member source requests;
- source-result demand and its best-available default;
- the independent PDB-access policy;
- the host-authorized source operation plan;
- the House-facing contribution contracts for `SourceLinkService` and
  `CSharpDecompilerService`;
- authored-source candidate ordering over owner-issued local, repository, and
  remote capabilities;
- producer ordering, short-circuiting, and fallback;
- reuse of supplied or acquired PDB content across producer attempts;
- the source result envelope and settlement receipt;
- visible rejected, unavailable, failed, and incomplete outcomes; and
- the rule that product source selection is not reconstructed in hosts,
  Workspace, PackageHouse, PlatformHouse, or presentation.

It does not define:

- package, platform, project, direct-library, or Workspace realization;
- library, assembly, artifact, Portable PDB, or source-document identity;
- assembly-to-PDB correspondence validation;
- package or symbol source authority;
- PDB discovery, acquisition, parsing, or storage algorithms;
- SourceLink map grammar, document correlation, provenance interpretation,
  checksum semantics, or source decoding;
- C# decompiler import, raising, structuring, typing, naming, formatting, or
  diagnostic behavior;
- source-byte transport, redirect policy, authorization, or caching;
- XML documentation, source-comment extraction, or documentation settlement;
- comparison of authored and decompiled source;
- Workspace admission, replacement, revisions, leases, or binding policy;
- CLI flags, browser interaction, rendering, or serialized transport; or
- host credentials, ambient filesystem policy, network policy, cancellation
  gesture, or display policy.

Those owners issue typed content, capabilities, and outcomes. SourceHouse
composes them and preserves their evidence.

## Why source needs a House

Source production crosses two independently useful producers:

```text
source-ready library representation
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

SourceHouse is therefore a clearing house over one exact, already-realized
library target. It does not discover or select the library itself. Its value
is the stable association of consumer policy, shared content, authored-source
candidate settlement, independent producer evidence, and one visible result.

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

- the retained assembly content;
- an optional applicable Portable PDB content reference;
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

- the same retained assembly content;
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
`CSharpDecompilerService` is a separate focused owner step.

### `SourceFetch`

`SourceFetch` remains the source-byte transport capability invoked by
SourceHouse for a SourceLinkService-issued remote candidate. SourceHouse
carries the host authorization that makes that capability available, but it
does not interpret URLs, redirects, HTTP status, byte limits, or storage
outcomes.

An assembly and PDB supplied by the input representation eliminate assembly
and symbol acquisition. They do not imply that the mapped source-document
bytes are already present. Authored-source production may still use an
authorized source-content store, local repository, or network fetch.

## Source-ready library representation

The primary SourceHouse input is an owner-issued, content-backed library
representation. PackageHouse, PlatformHouse, and direct-library or Workspace
adapters can produce the same source-neutral shape.

The representation supplies:

- one exact logical library and physical assembly correspondence;
- a guarded, repeatable content reference for the selected assembly;
- optional companion Portable PDB content associated with that assembly;
- any embedded Portable PDB carried inside the supplied assembly content;
- owner-issued assembly/PDB correspondence or the evidence needed by the PDB
  owner to validate that correspondence;
- source-specific provenance records without flattening them to labels;
- artifact generation and lifetime correspondence; and
- any already-retained source content that an authorized lower service can
  address by typed identity.

The representation is not a path bundle. A local path may be source-specific
provenance or an adapter capability, but SourceHouse cannot require it, derive
another path from it, or reopen content that the representation already
supplies.

PackageHouse is the preferred package path. A package realization can provide
the selected assembly and an available companion PDB in one retained
representation. SourceHouse consumes that representation without reopening
the package, resolving its coordinate again, probing an output directory, or
reacquiring the supplied PDB.

PlatformHouse supplies the same shape while retaining its reference and
implementation view correspondence. A direct-library adapter snapshots
explicitly authorized local inputs into the same content form before invoking
SourceHouse. SourceHouse does not branch on which of these owners produced the
representation.

The concrete representation type, construction, correspondence validation,
and lifetime are owned by the focused representation step in #6512. This
document states only the input obligations SourceHouse consumes.

## Exact source target

Every request addresses one target in the supplied assembly representation:

- an exact metadata type definition identity; or
- an exact method definition identity plus its declaring-type and member
  correspondence.

Display names, source file names, source URLs, package labels, and metadata
token text are not substitutes for the typed target. Resolution against the
retained assembly must produce one exact target or a typed non-success before
either source producer runs.

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
- **SuppliedOnly** may use companion PDB content already carried by the
  library representation or extract an embedded PDB from the supplied assembly
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

1. retain the exact request, source-ready representation, and policy
   generation;
2. resolve the exact target against the supplied assembly content;
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
7. issue one terminal result retaining every attempted contribution.

The ordering does not require one implementation method. It requires that:

- supplied assembly or PDB content is not reacquired;
- one acquired PDB is reused across both producers;
- `BestAvailable` does not decompile after an authored-source result meets the
  requested source unit;
- `AuthoredOnly` never invokes the decompiler;
- `DecompiledOnly` never selects authored source but may use PDB evidence;
- no producer runs without request and host authorization;
- producer failure does not become absence;
- successful fallback retains the earlier unsuccessful attempt; and
- no result is published after input generation or binding correspondence
  becomes invalid.

## Result and receipt

Every SourceHouse result preserves:

- the exact source request and target;
- the source-ready library representation identity and generation;
- the selected source-result demand;
- the PDB-access policy and operation-plan identity;
- one PDB contribution: not requested, supplied, acquired, unavailable,
  rejected, failed, or incomplete;
- one authored-source attempt when requested;
- one decompiled-source attempt when requested;
- the selected provider and source text when available;
- producer-specific provenance and diagnostics;
- operation completion and charged work; and
- the retained content lifetime needed by any non-materialized result.

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

Candidate rejection is contribution-local. An unauthorized source URL,
checksum-rejected source document, unusable optional PDB, or unavailable PDB
capability remains in its producer contribution and may permit another
authorized candidate or decompiled fallback. It becomes request-wide
`Rejected` only when the request is contradictory or a required owner-issued
input correspondence is invalid.

A supplied PDB candidate that does not match the assembly is disqualified and
retained as a rejected contribution. With `AllowAcquisition`, the House may
attempt an authorized replacement; with `SuppliedOnly`, it proceeds as though
no usable PDB contribution exists. If the input representation claimed that
the mismatched PDB had already been validated as corresponding, the broken
owner-issued claim rejects the input rather than becoming an ordinary
candidate miss.

## Content and lifetime

SourceHouse is content-first:

- assembly and PDB inputs use guarded repeatable content references;
- the House and both services operate without requiring filesystem paths;
- supplied content remains tied to its artifact generation and provenance;
- producer readers do not outlive the operation or retained content lease;
- materialized source text does not retain live metadata or PDB readers; and
- failure or cancellation disposes every acquired resource without publishing
  a success whose evidence could not be retained.

The House does not mutate the supplied representation when it acquires a PDB.
Its result records the acquired contribution and its lifetime. Publication
into a reusable artifact or Workspace generation is a separate owner
operation.

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

The representation supplies only assembly content. The consumer requests
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

### Supplied content is stale

The source-ready representation or its lease no longer denotes the generation
captured by the request. SourceHouse rejects or fails the operation according
to the owner-issued lifetime contract. It does not reopen a path, reacquire a
same-named package, or use a newer Workspace participant to manufacture
correspondence.

### Browser/Wasm has no paths

PackageHouse supplies in-memory assembly and PDB content. SourceHouse invokes
the same services and settlement as the CLI without a filesystem path.
Authored source may use an authorized in-memory content store or network
fetch; decompilation consumes the retained content directly.

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
2. define the shared source-ready library representation;
3. introduce `CSharpDecompilerService`;
4. expose content-backed PDB and SourceLink interpretation through
   `SourceLinkService` without moving transport or product policy into it;
5. implement SourceHouse authored-source candidate settlement and retire the
   public `PdbSourceHouse` composition;
6. implement SourceHouse decompiler composition, fallback, and receipts;
7. adopt the representation in PackageHouse;
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

## Evidence plan

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
- authored type results retaining mapping strength, source-unit scope, and
  partiality without claiming a complete declaration;
- checksum-rejected source never becoming available;
- exact target, representation generation, and result correspondence;
- finite-work incomplete outcomes;
- resource disposal on success, failure, and cancellation;
- equivalent CLI and Browser/Wasm settlement for the same typed request; and
- dependency-policy enforcement that keeps SourceLink and Decompiler
  independent beneath SourceHouse.

This specification itself makes no implementation-level safety, soundness, or
faithfulness claim. Those claims remain unverified until their named Release
gates land in the corresponding implementation slices.
