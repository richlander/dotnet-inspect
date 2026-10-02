# PDB acquisition

This document describes how dotnet-inspect locates and downloads PDB (Program Database) files to enable SourceLink resolution and source code navigation.

## Overview

PDBs contain debug information that maps compiled code back to source. Metadata
owns PE/PDB opening and extracts raw portable-PDB facts. The SourceLink layer
recognizes and interprets the SourceLink custom-debug-information document.

## Host-neutral Portable PDB settlement

### Status, owner, and claim

Status: **target design** for
[#9002](https://github.com/richlander/dotnet-inspect/issues/9002).

The PDB acquisition owner defines one host-neutral Portable PDB settlement
operation:

> Given one exact owner-issued managed assembly reference, its selected
> assembly content, and explicit host capabilities and policy, return matching
> validated Portable PDB content or a typed non-success together with complete
> candidate, provider, store, and work receipts.

The settlement owns candidate ordering, verified-store reuse, package-local
candidate consumption, external-provider attempts, matching-PDB admission,
provider-scoped negative observations, fallback, and the final result. A cache
is an optional capability and optimization; every successful operation returns
repeatable content plus typed identity and provenance rather than requiring a
later component to rediscover cache state.

This is one focused owner. It consumes but does not redefine:

- the assembly owner's exact reference, selected content, generation, and
  provenance;
- PackageHouse logical Library-inventory rows and exact Files delivery;
- Metadata Portable PDB format and PE/PDB identity validation;
- host authorization, network, store, limit, deadline, cancellation, and
  offline capabilities;
- SourceHouse source demand, authored/decompiled ordering, and source
  settlement; or
- PDB Source Provenance classification over an already bound PE/PDB pair.

The settlement returns PDB evidence. It does not fetch SourceLink documents,
interpret repository mappings, choose authored versus decompiled source,
decompile code, classify source provenance, or render host output.

### First production scenario

The first adopter is the desktop CLI's existing focused member Source
Locations operation:

```bash
dotnet-inspect member JsonSerializer \
  --platform System.Text.Json \
  Serialize:1 \
  -S "Source Locations" \
  --json
```

This command requires matching Portable PDB sequence-point evidence but does
not fetch source text unless the user separately requests source parts or
printing. It therefore demonstrates settlement without conflating PDB
acquisition with SourceLink document acquisition. The selected
`System.Text.Json` platform assembly supplies typed platform provenance, so the
initial external-provider policy may select MSDL without inferring publisher
ownership from an assembly or package name.

The neighboring package scenario is:

```bash
dotnet-inspect member JsonConvert \
  --package Newtonsoft.Json \
  SerializeObject:1 \
  -S "Source Locations" \
  --json
```

It exercises exact package provenance, an optional PackageHouse-issued
package-local candidate, NuGet.org producer authorization, and external
symbol-package or symbol-server fallback. Package-local consumption begins
only after `GetLibraryAndInventoryForTarget` is implemented and its exact
Library row is supplied; settlement never inventories a package implicitly.

Browser/Wasm adopts the same host-neutral operation in the next host slice
with explicit in-memory or browser-owned stores and fetch capabilities. The
CLI-first slice must not place provider policy, identity validation, cache
keys, or fallback logic in the command.

### Request and exact binding

One request binds:

```text
PortablePdbSettlementRequest
  AssemblyReferenceIdentity
  AssemblyContentGeneration
  SelectedAssemblyContent
  PortablePdbIdentity
  AssemblyProvenance
  OptionalPackageLibraryInventoryRow
  ProviderPolicy
  Capabilities
  LimitsAndDeadline
```

This is a conceptual contract shape, not a frozen CLR type.

The owner-issued assembly reference and selected content are authoritative.
The settlement does not reconstruct identity from a display path, assembly
simple name, package ID, or PDB filename. The Portable PDB request identity
comes from the selected assembly's Portable CodeView record and includes the
complete Portable PDB content identity: GUID plus stamp. The PDB filename is
untrusted inert routing evidence, not identity.

The request retains the association between assembly reference, selected
content generation, and Portable PDB identity. A result for another assembly,
generation, or CodeView identity cannot satisfy the request even when its
filename, MVID, package path, or display name matches.

An assembly with no applicable Portable CodeView identity has no standalone
Portable PDB request. Applicable embedded Portable PDB content may still settle
through the Metadata-owned embedded binding. Windows PDB identity is a typed
unsupported outcome, not authority to search for or accept a Portable PDB.

### Candidate classes and order

The settlement evaluates only policy-authorized candidate classes in this
order:

1. applicable embedded Portable PDB content issued from the selected assembly;
2. verified positive-store content for the exact Portable PDB identity;
3. an optional PackageHouse-issued package-local candidate;
4. external providers selected by typed platform, package-source, publisher,
   and assembly provenance.

A later candidate may succeed after an earlier candidate is absent, rejected,
or failed. The final result retains every attempted, skipped, negative-hit,
rejected, and failed candidate in order. Cancellation and deadline expiry stop
new work after owned cleanup and remain distinct from unavailability.

Embedded content is first because its PE containment establishes the strongest
available correspondence without network or external package work. Positive
store reuse precedes acquisition because the store contains content previously
validated for the exact identity. Package-local evidence precedes remote
symbol providers when the caller supplies an applicable owner-issued Library
row and policy enables that candidate.

The operation does not probe an adjacent filesystem PDB merely because the
selected assembly has a local path. A caller that wants local companion
content supplies it as an explicit candidate capability with its provenance;
ambient same-directory discovery is not part of the host-neutral contract.

### Package-local candidate

PackageHouse owns whether a logical target Library has an applicable
implementation assembly and adjacent PDB entry. The settlement consumes one
owner-issued inventory row from the same selected target:

- **Listed** supplies exact implementation-assembly and PDB entry references.
- **Absent** proves package-local absence only for that row's applicable
  implementation location and PackageHouse generation.
- **Not applicable** means the logical Library has no implementation
  correspondence from which a package-local PDB candidate can be issued.

The row is evidence, not content. When policy selects a Listed candidate, the
settlement asks PackageHouse for exact Files using those references. A selected
reference Library requires both the implementation assembly and PDB so
Metadata can validate their correspondence. A selected implementation Library
requires only the PDB when the request's selected assembly content is already
the row's exact implementation content.

PackageHouse content that is missing, ambiguous, incomplete, rejected,
bounded, or failed remains a typed candidate outcome. It does not become
package-local absence. Raw File List paths cannot substitute for the logical
Library row, establish implementation correspondence, or prove PDB absence.

Without a supplied Library row, the settlement makes no package-local presence
or absence claim. It may continue to policy-authorized external providers; it
does not silently issue a PackageHouse inventory query merely because symbols
were requested.

### Provider policy

Provider ordering consumes typed evidence:

- typed platform provenance may select platform symbol policy;
- exact package producer and source authorization may select `.snupkg` and
  NuGet symbol policy;
- owner-issued publisher evidence may select publisher-specific policy; and
- explicitly configured symbol providers retain their own identities and
  coordinates.

Package ID, assembly name, company metadata, filename, namespace, or display
text cannot establish publisher identity. In particular, `Microsoft.`,
`System.`, and `Azure.` name prefixes are not authority for Microsoft-specific
provider ordering.

The initial CLI policy is:

1. for a platform assembly, embedded and verified-store candidates followed by
   MSDL;
2. for a NuGet.org-produced package, embedded, verified store, applicable
   package-local evidence, `.snupkg`, the NuGet symbol server, then any
   explicitly configured fallback provider;
3. for another package producer, only package-local and external providers
   explicitly authorized for that producer; and
4. in offline or cache-only mode, embedded and verified-store content plus
   already supplied package content, with no remote request.

Microsoft-owned-package specialization remains unavailable until an
owner-issued publisher identity exists. The current package-ID-prefix
heuristic is compatibility behavior to retire, not a source of normative
policy.

### Positive verified-PDB store

The host may supply a positive store for content already admitted against an
exact Portable PDB identity. The store:

- supports filesystem and pathless hosts;
- returns a fresh readable stream or detached immutable content;
- never makes a local path part of the semantic result;
- distinguishes missing content from read, validation, publication, and
  read-back failure; and
- retains or can reproduce the content's supplying provenance for the
  settlement receipt.

Downloaded or package-local bytes are parsed and identity-validated before
publication. Settlement succeeds only after the store can reproduce the
published content, unless the host explicitly supplies a detached
operation-owned result store whose successful publication itself transfers
repeatable ownership. A publication or read-back failure is visible and may
permit a later provider; it is never reported as symbol absence.

The store key is derived from the complete Portable PDB identity, not the
remote provider's protocol lookup key. Provider-specific coordinates and
provenance remain receipt evidence rather than weakening content identity.

### Provider-scoped negative observations

The host may supply a separate negative-observation store. One observation is
keyed by:

```text
PortablePdbIdentity
ProviderIdentity
ProviderCoordinates
```

It records the authoritative absence evidence, observation time, and expiry.
A hit suppresses only the same provider route and records that no request was
made. It cannot suppress a newly configured provider, changed coordinates, a
PackageHouse generation, or a different PDB identity.

Only definitive provider absence may be retained. For the initial HTTP
providers this means an exact HTTP 404 from that route. Authentication or
authorization denial, throttling, timeout, cancellation, transport failure,
offline or cache-only policy, operation bounds, malformed or mismatched
content, unsupported Windows PDB content, and positive-store failure are not
absence.

An aggregate "no symbols anywhere" observation is invalid. Expiry is host
policy carried by the observation capability; settlement reports the applied
expiry but does not silently extend it. The existing process-global persistent
miss cache, including cached HTTP 403 behavior, is compatibility substrate to
retire from adopted routes.

### Result and receipts

The result is one of:

- **Acquired** — matching repeatable Portable PDB content, exact binding
  receipt, supplying candidate/provider provenance, positive-store
  publication/reuse evidence, and complete ordered work receipts;
- **Unavailable** — every authorized candidate established absence or was not
  applicable, with no failure that prevents that conclusion;
- **Incomplete** — a configured bound or deadline prevented completion;
- **Canceled** — caller cancellation stopped settlement after owned cleanup;
  or
- **Failed** — validation, provider, store, authorization, or operational
  failure prevented an authoritative unavailable result.

An Acquired result retains:

- the exact assembly reference and content generation;
- the complete Portable PDB identity;
- embedded, store, package-local, symbol-package, or symbol-server provenance;
- package generation and exact entry references when PackageHouse supplied the
  candidate;
- provider identity and coordinates when an external provider supplied it;
- whether network work occurred;
- positive-store reuse/publication evidence; and
- the ordered candidate and provider attempts.

Each candidate receipt identifies its class, applicability, policy decision,
attempt outcome, work performed, and typed detail. External-provider receipts
retain credential-redacted inert coordinates, request count, status when
available, bytes read, and elapsed duration. Negative hits retain the original
absence evidence and record zero requests. Receipts describe the operation;
they do not expose an acquisition plan or host capability.

Unavailable requires that no retained failure can explain the lack of content.
A rejected same-identity response, store failure, authorization failure, or
malformed package-local candidate therefore cannot be flattened into
Unavailable merely because later providers were absent.

### Limits, containment, and platform contract

The operation is SRM-only, NativeAOT-friendly, and free of inspected-assembly
loading. Untrusted PE debug metadata, package bytes, PDB bytes, symbol-package
bytes, provider responses, and URLs cross their existing typed admission
boundaries before use.

Limits cover provider attempts, downloaded and expanded symbol-package bytes,
Portable PDB bytes, retained receipt count, and deadline. A limit that prevents
candidate classification settles Incomplete. Validation and positive-store
publication happen before Acquired is visible.

The same request/result contract supports desktop, Browser/Wasm, and other
pathless hosts. Desktop may use filesystem-backed stores; Browser/Wasm uses
host-owned pathless capabilities. No result or policy branch requires a local
path, ambient NuGet configuration, process-global cache, blocking wait, or
multi-threading.

### Analogous implementation evidence

Microsoft.SymbolStore and `dotnet-symbol` use configured cache and symbol
server providers addressed by symbol keys:

- <https://github.com/dotnet/diagnostics/blob/main/src/Tools/dotnet-symbol/README.md>
- <https://github.com/dotnet/symstore>

The transferable ideas are an ordered provider chain, explicit server/cache
configuration, and identity-keyed lookup. Their debugger-oriented file output,
ambient symbol-path configuration, and support for native modules and Windows
PDBs do not transfer to this Portable-PDB-only, host-neutral product boundary.

The .NET symbol guidance establishes Portable PDB as the cross-platform managed
symbol format and System.Reflection.Metadata as its reader:

- <https://learn.microsoft.com/dotnet/core/diagnostics/symbols>
- <https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md>

This design is stricter at the product boundary: bytes become evidence only
after exact PE/PDB identity validation, and provider absence remains distinct
from rejection or operational failure.

NuGet symbol packages and public symbol servers demonstrate that package and
assembly provenance select different acquisition coordinates. They do not
provide the logical Library correspondence or package-local PDB-absence
evidence owned by PackageHouse, and no standard NuGet V3 resource discovers
symbol packages for an arbitrary custom producer.

### Production adoption and retirement

The counted adoption has six focused slices:

1. Lock this request, result, candidate, policy, positive-store,
   negative-observation, and receipt contract.
2. Adapt `PdbAcquisitionService` and `SymbolPackageDownloader` behind the
   settlement and migrate CLI member Source Locations for platform
   `System.Text.Json`. Preserve SourceHouse's supplied-PDB input while deleting
   duplicate CLI provider orchestration for that route.
3. After PackageHouse implements
   `GetLibraryAndInventoryForTarget`, compose one supplied Library row with
   exact Files acquisition and adopt the package CLI Source Locations scenario.
4. Adopt the same host-neutral settlement in Browser/Wasm member/type source
   with explicit pathless stores and fetch capabilities.
5. Let SourceHouse consume the settlement capability, then retire duplicated
   `AssemblyContextSourceQuery` and `PdbSourceHouse` candidate orchestration
   route by route.
6. Adopt the settlement in non-source consumers, including PDB Source
   Provenance, decompilation, analysis, and diagnostics, then remove the
   compatibility acquisition APIs and process-global symbol miss cache.

Each adoption changes one consumer owner at a time. The design slice defines
the shared settlement only; it does not simultaneously change PackageHouse,
SourceHouse, Metadata, Query, CLI, or Browser internals. The first-adopter
implementation may pair the settlement with the one bounded CLI adoption
allowed by the design-scope rules.

Each implementation adoption publishes exact-base/head NativeAOT evidence for
the production command it changes. The CLI-first slice measures the
`System.Text.Json` Source Locations command with an empty operation-owned store
and with a verified warm store, keeping base and head on one accepted
performance host. Network-inclusive cold acquisition is reported as
observational unless a controlled provider makes the compared work
deterministic; it cannot substitute for the warm-store comparison.

### Contract evidence

Implementation must gate at least:

- platform `System.Text.Json` Source Locations acquiring a matching Portable
  PDB through the CLI-first settlement path;
- warm verified-store reuse with no network request;
- Browser/Wasm-equivalent pathless store behavior before that host adopts;
- rejection of matching GUID with a different Portable PDB stamp;
- package Listed, Absent, and Not applicable evidence without implicit
  inventory work;
- a selected reference Library acquiring only its exact implementation DLL and
  PDB;
- exact HTTP 404 negative reuse for one unchanged provider route;
- changed provider coordinates and newly authorized providers bypassing the
  old negative observation;
- HTTP 403, timeout, cancellation, malformed content, identity mismatch,
  limits, and store failures remaining non-absence;
- a later provider succeeding after an earlier rejection or failure; and
- every Acquired result reopening repeatable matching content after the
  acquisition operation has completed.

The platform CLI scenario and a real NuGet package with published Portable PDB
evidence are the production fixtures. Synthetic PDB identity and provider
responses remain appropriate for exact mismatch, failure, limit, and negative
observation boundaries.

## PDB source document acquisition

After a Portable PDB maps a member or type to a checksummed source document,
the source composition looks for document bytes in this order:

1. The PDB-recorded local path, when local source reads are enabled.
2. Each caller-supplied local Git clone, addressed by the revision selector and
   repository-relative path in a `raw.githubusercontent.com` SourceLink URL.
3. The remote SourceLink URL.

For an exact TypeDef with no method-correlated document, such as a bodyless
interface, SourceLink may infer matching PDB documents by filename. The exact
metadata type is resolved before that inference, and the resulting mapping
retains `Inferred` rather than presenting the filename relationship as a
sequence-point correlation.

`PdbSourceHouse` is the clearing house for this PDB-provenance-based source
scenario: it composes the candidate origins, fetch policy, checksum
verification, source decoding, and typed failure outcomes into one settled
result. It intentionally does not include decompiler-generated source.
`AssemblyContextSourceQuery` owns that higher Queries-layer fallback.
The [selected-member source pair](design/member-source-pair-query.md),
[shared member acquisition](design/member-source-acquisition.md), and
[shared type acquisition](design/type-source-acquisition.md) use SourceHouse
for authored settlement, while preserving these acquisition providers and their
authorization and checksum-gated admission. Type/member callers retain the
acquired PDB for their existing fallback or explicit member comparison.

The target [SourceHouse composition](design/source-house.md), tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512), replaces
that product composition with one content-first House over
`SourceLinkService`, `CSharpDecompilerService`, and authorized acquisition
capabilities. Product candidate ordering moves from `PdbSourceHouse` to
SourceHouse, while this document continues to own PDB acquisition, SourceLink
interpretation, checksum semantics, and authored-source evidence.

Every successful path must satisfy the shared Portable PDB checksum verifier
(exact or accepted line-ending-normalized correspondence) before its content
becomes evidence. Local-clone acquisition reads the addressed Git blob rather
than the working-tree file. A missing revision, path, or checksum match in one
clone continues through the remaining clones and then to the remote source.
Remote acquisition follows HTTP redirects. A final successful response becomes
PDB source only when its bytes satisfy the document checksum; an unsuccessful
response or transport failure remains a typed acquisition failure, after which
`AssemblyContextSourceQuery` uses decompiled source when available.
[SourceFetch evidence admission](design/source-fetch.md) owns the remote
candidate order, host authorization, bounded retrieval, validation-before-use,
content-store publication, and typed transport outcomes. This PDB owner
supplies the checksum predicate and retains the settled source meaning.

[Local repository source acquisition](design/local-repository-source-acquisition.md)
owns that adapter's locator interpretation, byte admission, optional-lookup
decline, and execution limits. This document retains acquisition ordering and
remote outcome policy. A local result proves correspondence with the supplied
PDB, not repository authenticity or globally immutable SourceLink identity.
The adapter's focused evidence section distinguishes existing gates from
ungated working-tree-divergence and execution-limit claims.

Resolution and retrieval preserve document-specific failure state. A rejected
SourceLink entry does not shadow a valid entry that resolves the same document,
but when no valid entry resolves the document, a rejected conformant key that
matches that document is a mapping failure. An unrelated usable entry in the
same map does not turn that failure into absence. Once a URL resolves, HTTP 404
is definitive document absence; transport failures, other unsuccessful HTTP
responses, unauthorized initial destinations, oversized responses, checksum
mismatches, and storage failures remain acquisition failures. This boundary
cannot distinguish a deliberately concealed private GitHub document that
returns HTTP 404 from a missing public document; both are absence.

At the reusable service boundary, callers supply optional fully qualified
repository paths to member or type acquisition. The desktop CLI exposes those
paths through repeated `--repo <fully-qualified-clone-path>` options for
`member` PDB Source, printable `type` Source Files, printable member Source
Locations, and implementation-diff PDB source.

Browser/Wasm hosts do not supply filesystem clone paths and continue through
their host-authorized source fetcher.
`ServiceLocalClone_SatisfiesMemberAndTypeSourceWithoutRemoteFetch` is the
non-vacuity gate that member, type, and printable-projection service acquisition
use a verified local clone with PDB-recorded path reads disabled and without
dispatching the simulated unavailable remote source.
`TypeSourceFilesPrint_AcceptsRepoAtCliBoundaryWhileOffline` separately gates the
CLI parser and dispatch boundary using the product's embedded PDB while all
network access is disabled.

## Content-backed SourceLink producer

`SourceLinkService` owns interpretation of supplied PDB and source content.
Its content-backed boundary preserves Metadata's assembly/PDB correspondence,
SourceLink mapping and provenance, and the existing checksum and BOM-decoding
semantics. It does not select an acquisition candidate or decide whether
decompilation is an acceptable fallback.

The focused implementation is
[#7197](https://github.com/richlander/dotnet-inspect/issues/7197), the SourceLink
producer step of
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512), within project
4 of [#7177](https://github.com/richlander/dotnet-inspect/issues/7177).
SourceHouse supplies the eventual composition requirements; Metadata supplies
PE/PDB extraction and identity validation. Neither is redefined here.

A caller can open an existing pathless `ResolvedAssemblyReference` with
`OpenMetadataOnly`, supplying `SourceLinkReadLimits`, then transfer already
acquired PDB content with `LoadPdbFromStream`. Metadata consumes that stream
and retains its existing identity, read-failure, and cleanup behavior.
`PdbLoadStatus` distinguishes a loaded PDB, identity mismatch, unsupported
format, Windows PDB, malformed or truncated content, and suppressed read
failure without requiring consumers to parse diagnostic text. Loading updates
SourceLink's cached map and document state before the next query.
This path does not activate embedded or adjacent PDB discovery; callers that
want embedded symbols continue to select the existing bounded embedded-PDB
operation explicitly.

`PdbContext.GetPortablePdbImage` copies the currently loaded Portable PDB into
independent immutable content, or returns null when none is loaded. Consumers
can supply that content to another producer after disposing the acquisition
context, without reopening `PortablePdbPath`. The accessor requires a live
context; `PortablePdbSnapshotTests` gates byte preservation, post-disposal use
of the returned image, absent/rejected PDBs, and use after context disposal.

`VerifyChecksum` applies the existing SHA-1/SHA-256 and accepted bytewise CR/LF
normalization rules to supplied bytes. `VerifySourceContent` then returns the
detached `VerifiedSourceTextResult`, retaining `Exact`, `LineEndingNormalized`,
`Unavailable`, `Unsupported`, or `Mismatch`. Only the first two yield text.
`DecodeSourceText` retains BOM-aware UTF-8/UTF-16/UTF-32 decoding with UTF-8 as
the default; decoding alone is not checksum evidence. Normalized verification
still returns the supplied text, not a rewritten source document.

The production adoption path has three steps in this slice: expose these
operations at SourceLink; migrate `PdbSourceHouse` and source-integrity/local-
repository consumers; and retain the existing CLI and Browser/Wasm source
query paths over those consumers. The old House checksum/decoder entry points
and Services-owned checksum/result types are retired, not duplicated.
Acquisition ordering and fallback stay in their current compositions until
SourceHouse adoption. Library lease consumption and the independent decompiler
producer remain separately tracked.

The motivating real repository is dotnet-inspect itself: its compiled
Portable PDB maps an exact member to checksum-verified repository source,
including when the assembly and PDB are supplied from memory. PR-fast
`SourceLinkContentProducerTests` gates supplied-content mapping, stream
settlement, mismatch/read-failure boundaries, retained map limits, and checksum
and decoding outcomes. `PdbSourceHouseTests` and `VerifiedLocalSourceReadTests`
gate the migrated composition; `AssemblyContextSourceQueryTests` and existing
browser source-operation cases cover shared production callers.

This producer adds no host result schema or rendering mode. Existing source
queries retain their typed output and host-owned lowering; completed host
inspection envelopes remain the responsibility of their composition boundary.

## PDB formats

### Portable PDB

- Cross-platform format introduced with .NET Core
- Magic header: `BSJB` (first 4 bytes)
- Can contain a raw SourceLink custom-debug-information blob
- Identified in PE files by CodeView entry with `MinorVersion == 0x504d` ("PM" for Portable Metadata)

> **Trivia**: BSJB are initials from the original CLR team: Brian, Susan, Jason, and Bill. Bill was the metadata developer—of course, management goes first and the developer goes last. This follows the tradition of `MZ` (Mark Zbikowski) in DOS/PE headers found in the same binaries.

### Windows PDB

- Legacy format, Windows-only tooling
- Magic header: `Microsoft C/C++ MSF 7.00`
- Cannot be read by System.Reflection.Metadata
- Still used for native image PDBs (`.ni.pdb`) in Windows R2R builds

## PDB location strategy

The tool searches for PDBs in this order:

`PdbAcquisitionService` owns this reusable acquisition algorithm. The typed
`SourceLinkDocumentsQuery` invokes it through a host-supplied `SourceLinkService`
and HTTP client, so library and package hosts do not duplicate symbol lookup.
`SymbolPackageDownloader.AcquirePdbAsync` returns an
`AcquiredPortablePdb` content reference backed by the host's `IPdbStore`;
filesystem stores expose an optional local path, while browser/Wasm hosts use
an in-memory store, an explicit `IPackageSourceAuthorization`, and open the same
acquired bytes as a stream. The legacy `DownloadPdbAsync` path result is only
the desktop compatibility projection.

`PdbAcquisitionService` can pair that content with a
`ResolvedAssemblyReference` that has no path. It derives the symbol-package PDB
name from the CodeView record, uses the assembly identity only as a validated
fallback, and asks Metadata to validate the Portable PDB identity against the
already-open assembly image. That comparison uses the complete Portable PDB
content id (GUID plus stamp), not the symbol-server GUID alone. The
explicit-capability descriptor overload requires both its `IPdbStore` and
`IPackageSourceAuthorization`; the legacy desktop descriptor overload remains
path-bound and cannot make a pathless participant silently select the desktop
filesystem or ambient NuGet policy. `AssemblyContextSourceQuery` consumes this
content-shaped symbol capability for a selected group participant. Its query
context requires the store and source authorization explicitly; an in-memory
store lets browser/Wasm hosts acquire and validate the same PDB bytes without a
path. `AssemblyContextSourceQueryTests.PathlessMember_AcquiresVerifiedPdbSource`
gates the end-to-end query path.
`PdbIdentityTests.LoadPdbFromStream_RejectsMatchingGuidWithDifferentStamp`,
`PdbIdentityTests.PortablePdbIdentity_WindowsCodeViewCannotAuthorizePortablePdb`,
and
`PdbAcquisitionServiceTests.PathlessParticipant_AcquiresMatchingPdbThroughInMemoryStore`
gate those claims.

When an API or member selection came from a resolved assembly, the descriptor
for the `ApiType` that supplied that selection is authoritative for PDB
acquisition. The CLI retains that descriptor by selected object identity rather
than reconstructing it from `SourceAssemblyPath`. A package descriptor supplies
its own package ID and exact version, and a platform descriptor selects platform
symbol policy. Explicit caller package coordinates are only a fallback for
project, local, or explicitly designated descriptors that do not encode a
backing package. They never override package or platform provenance. This lets a
forwarding facade and the assembly that supplies its selected member use
different symbol coordinates without attributing the member to the facade.
`SourceForwarderResolutionTests.ApiServices_RetainsSelectedForwarderDescriptor`,
`SourceForwarderResolutionTests.ApiServices_RetainsRootPackageDescriptor`,
`SourceForwarderResolutionTests.TypeSourceFiles_ForwardedPlatformDescriptorSelectsPlatformPolicy`,
`SourceForwarderResolutionTests.TypeSourceFiles_ProjectDescriptorUsesPackageFallback`,
`PdbAcquisitionServiceTests.SelectedPackageDescriptor_OverridesCallerPackageFallback`,
`PdbAcquisitionServiceTests.SelectedPlatformDescriptor_IgnoresCallerPackageFallback`,
and
`PdbAcquisitionServiceTests.SelectedLocalOrProjectDescriptor_UsesCallerPackageFallback`
gate that handoff and precedence contract.

This contract does not define type-forwarder resolution, API-to-runtime
MethodDef correspondence, package-feed authorization, SourceLink map semantics,
or presentation. Those remain owned by their existing components.

Descriptor-backed PDB contexts own the stream they open. If debug-directory or
embedded-PDB inspection fails during construction, the incomplete context
releases that stream before propagating the failure.
`AssemblyContextSourceQueryTests.PdbContextOpenFailure_DisposesAuthoritativeStream`
gates that construction boundary. PE and portable-PDB readers leave their
streams open so `PdbContext` remains the sole owner. A fully prefetched portable
PDB releases its store stream immediately; any release failure is retained for
the strict query-disposal boundary rather than masked or ignored.
`PdbIdentityTests.LoadPdbFromStream_AcceptsMatchingContentWithoutAPath`
gates immediate release and continued prefetched-metadata access.
`PdbContextDescriptorTests.DescriptorOpenPrimaryFailure_IsNotMaskedByCleanupFailure`
and
`AssemblyContextSourceQueryTests.PdbLoadPrimaryFailure_IsNotMaskedByCleanupFailure`
gate those ownership boundaries.
The compatibility `PdbContext.Dispose` path retains its best-effort cleanup
behavior. Strict query ownership uses `DisposeWithFailure`, which attempts
every owned resource and reports the first cleanup failure; source queries
therefore cannot publish PDB-source success after PDB disposal failed.
`AssemblyContextSourceQueryTests.PdbDisposalFailure_PreventsPdbSourceSuccess`
gates cancellation and operational failure for member and type queries;
`AssemblyContextSourceQueryTests.NonStandardPdbDisposalFailure_IsTyped`
gates host-specific non-fatal exceptions outside the common I/O types. A
cleanup failure while an acquisition failure is already propagating does not
replace that primary failure;
`AssemblyContextSourceQueryTests.PdbLoadPrimaryFailure_IsNotMaskedByCleanupFailure`
gates the member and type cancellation and fatal-exception paths both before
and during portable-PDB provider construction.

### 1. Embedded PDB

Check if the library has an embedded PDB (stored inside the PE file itself). This is the most reliable option as no external lookup is needed.

### 2. Standalone PDB

Look for a `.pdb` file next to the library with the same base name. Common when debugging locally.

### 3. Symbol package (.snupkg)

The current implementation downloads a NuGet package's corresponding `.snupkg`
from:

- `https://globalcdn.nuget.org/symbol-packages/{id}.{version}.snupkg`
- `https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.snupkg`

That lookup is not yet source-conformant for packages acquired from another
feed. Under the target
[package source model](design/package-source-model.md#enrichment-is-a-separate-capability),
these known routes are available only when NuGet.org produced the package, and
the derived PDB remains tied to that producer. NuGet V3 defines no standard
symbol-package download resource for custom or local feeds, so `.snupkg`
acquisition from those producers is unsupported until an explicit endpoint
contract exists. This migration is tracked by
[#3738](https://github.com/richlander/dotnet-inspect/issues/3738).

### 4. Symbol servers

Query symbol servers using the CodeView GUID and age:

- **NuGet**: `https://symbols.nuget.org/download/symbols/{pdbname}/{key}/{pdbname}`
- **MSDL**: `https://msdl.microsoft.com/download/symbols/{pdbname}/{key}/{pdbname}`

The symbol key format differs by PDB type:

- Portable PDB: `{GUID}FFFFFFFF`
- Windows PDB: `{GUID}{age:x}`

## CodeView debug directory

The PE file's debug directory contains CodeView entries that provide:

- **Path**: Original PDB filename (e.g., `System.Text.Json.pdb`)
- **GUID**: Unique identifier for this build
- **Stamp**: Final 4 bytes of the Portable PDB content identity
- **Age**: Build counter (always 1 for Portable PDBs)
- **MinorVersion**: `0x504d` indicates Portable PDB format

### Multiple CodeView entries

**Important**: Some libraries have multiple CodeView entries. Windows ReadyToRun (R2R) assemblies typically have two:

1. **Native Image PDB** (`.ni.pdb`) - Windows PDB format, different GUID
2. **Original PDB** - Portable PDB format, original GUID

We iterate through all CodeView entries and **prefer the Portable PDB entry** (identified by `MinorVersion == 0x504d`). This ensures we use the correct GUID when querying symbol servers.

Example from a Windows R2R build:

```text
CodeView Entry 1: System.Text.Json.ni.pdb (MinorVersion: 0x0000, Windows PDB)
CodeView Entry 2: System.Text.Json.pdb    (MinorVersion: 0x504d, Portable PDB) ← use this
```

`PdbContext` exposes the selected CodeView identity and raw PDB records without
exposing `PEReader` or `MetadataReader`. `ILInspector.SourceLink` uses those
typed APIs for map extraction, URL decoration, and provenance.
`PdbResourceLimitException.Kind` distinguishes PE debug-directory, CodeView
record, and embedded Portable PDB limits so callers do not infer the bounded
resource from diagnostic text.

### Document identity is not declaration provenance

A Portable PDB document names content but does not identify the physical syntax
tree that produced a MethodDef. Its document row contains a name and may carry
language and checksum metadata; sequence points contain mapped destination
documents and positions. When a recognized checksum algorithm and checksum are
present, they can validate candidate bytes against the recorded value. Neither
record preserves the pre-mapping source document or identifies a `#line`
transition.

This distinction prevents PDB method spans from authorizing a local C# body. A
`#line` directive in one compilation input can map its MethodDef into another
input's document row, reusing that document's real checksum. `#pragma checksum`
can similarly give a mapped external document a caller-selected checksum. The
originating file need not be among the source paths supplied to an inspector,
and embedded source or a compiler-reported source-file count does not associate
an individual MethodDef with its physical syntax tree.

ReturnToSender has no PDB-authoritative local-source path. Its raw source
indexes remain non-authoritative, and
`TryIsolateRecompileFailure_DeclinesRawSourceIndex` gates that raw-index
behavior. No dedicated gate asserts the broader absence of a PDB attribution
path; it is an architectural non-action boundary. #3835 remains blocked on an
independent build manifest that certifies the complete physical source set and
permits an assembly-wide line-mapping check, or on a stronger per-method
provenance contract outside the Portable PDB format.

## Microsoft vs third-party libraries

### Microsoft platform libraries

- Built by Microsoft from dotnet/runtime
- Published to MSDL symbol server
- SourceLink URLs point to `raw.githubusercontent.com/dotnet/runtime/...`

### Distro builds (Canonical, Red Hat, etc.)

- Rebuilt from source by Linux distributions
- SourceLink typically disabled during rebuild
- Same metadata (Company: "Microsoft Corporation") but no symbols on MSDL
- Detected by: symbols not found on any server

### Third-party NuGet packages

- May publish `.snupkg` to NuGet.org
- May publish to NuGet symbol server
- Quality varies by publisher

## Caching

Downloaded PDBs are cached locally to avoid repeated downloads:

- **Symbol packages**: `~/.dotnet-inspect/symbols/{package}/{version}/{filename}.pdb`
- **Symbol server**:
  `~/.dotnet-inspect/symbols/servers/{server-host}/{pdbname}/{key}/{pdbname}`

The store may instead be in-memory, in which case the same keys have no
filesystem projection. Symbol-server entries are scoped by provider host, so a
warm hit reports the same server that supplied the content. Portable PDB store
keys use the full content identity (GUID plus stamp), even though the remote
symbol-server request retains its protocol-defined `GUID + FFFFFFFF` lookup
key. A reference to one acquired payload therefore remains repeatable if
another PDB shares its GUID but has a different stamp.
`SymbolPackageDownloaderTests.AcquiredPortablePdb_DifferentStampsRemainRepeatable`
gates that invariant. Package-associated PDB entries remain NuGet.org-specific
and package/version-keyed; extending them to custom producers requires
source-scoped provenance and is part of
[#3738](https://github.com/richlander/dotnet-inspect/issues/3738).
`SymbolPackageDownloaderTests.AcquirePdbAsync_MsdlCachePreservesProvider` gates
provider preservation when the supplying server is not the first one probed.

Filesystem PDB publication writes a unique sibling staging file and atomically
replaces the final entry only after the complete payload is closed. Readers
therefore observe the previous complete PDB or the replacement, never a
truncated in-progress write.
`PdbStoreTests.FileSystemPdbStore_FailedReplacementPreservesPublishedContent`
gates that publication invariant.

The host-neutral downloader overload pairs an explicit store with explicit
package-source authorization and disables the filesystem negative-result cache
by default.
`SymbolPackageDownloaderTests.AcquirePdbAsync_ExplicitStore_DoesNotUseAmbientCaches`
gates both defaults, and
`PdbAcquisitionServiceTests.DescriptorAcquisition_RequiresExplicitHostCapabilities`
and
`PdbAcquisitionServiceTests.PathlessParticipant_DesktopOverloadDoesNotAcquire`
gate the descriptor API shape and compatibility overload. Store read/write
failures remain visible rather than being reported as symbol unavailability;
`SymbolPackageDownloaderTests.AcquirePdbAsync_StoreFailureIsVisible` and
`PdbAcquisitionServiceTests.PathlessParticipant_StoreReadFailureIsVisible` gate
the write and post-acquisition read paths. A cache read failure is carried
across providers, cleared by a later successful acquisition, and returned as a
typed store failure only if no provider succeeds.
`SymbolPackageDownloaderTests.AcquirePdbAsync_CachedReadFailureContinuesToNextProvider`
and
`SymbolPackageDownloaderTests.AcquirePdbAsync_CachedReadFailureRecordsFinalStoreFailure`
gate those outcomes.
`SymbolPackageDownloaderTests.AcquirePdbAsync_StoreWriteFailureContinuesToNextProvider`
and
`PdbAcquisitionServiceTests.PathlessParticipant_StoreWriteFailureIsVisible`
apply the same fallback and typed-boundary contract to publication failures.
Local-path projection occurs before the caller-owned PDB stream is opened, so a
projection failure cannot leak that stream;
`PdbAcquisitionServiceTests.PathlessParticipant_LocalPathFailurePrecedesOwnedStreamOpen`
gates that ownership boundary. Cached and downloaded Portable PDBs
are parsed and identity-checked before an acquired result is returned, so an
invalid entry cannot suppress later providers;
`SymbolPackageDownloaderTests.AcquirePdbAsync_InvalidCachedPdbContinuesToNextProvider`
gates the fallback.

Library inspection treats symbols as optional enrichment: a typed PDB-store
failure remains visible in Signals without suppressing metadata or other
library sections. It does not catch unrelated acquisition failures.
`CommandExecutionTests.LibraryCommand_InvalidCachedPdbPreservesLibraryInspection`
gates that command boundary.

## Acquisition evidence

`SymbolPackageDownloader.AcquirePdbAsync` accepts an optional
`PortablePdbAcquisitionEvidenceCollector`. The ordinary path supplies no
collector and does not allocate attempt records or capture elapsed timing.
Evidence-enabled callers receive one immutable
`PortablePdbAcquisitionEvidenceDocument` after the acquisition task has
settled.

The document records the external-acquisition outcome, selected symbol server,
cache origin, Windows-PDB detection, PDB-store failure, and the ordered network
attempts made by that acquisition. Each network attempt identifies the MSDL,
symbol-package, or symbol-server route and records:

- a credential-redacted inert URL;
- the number of HTTP requests, including retries;
- the terminal transport outcome and HTTP status when available;
- bytes read across attempts; and
- monotonic elapsed duration.

Canceled and failed attempts retain the latest response status and cumulative
body-byte count observed before settlement, including when a later retry is
canceled before receiving headers, rather than reporting the operation as if no
response arrived. Windows-PDB and store-failure observations from an earlier
provider likewise remain visible when acquisition is canceled in a later
provider.

`Acquired` is emitted only after the downloaded or cached content has passed
Portable PDB format and identity validation and has been retained by the
configured store. `Unavailable` means no route produced retained matching
content; `AcquisitionFailure` distinguishes a failed external provider from
definitive absence, while `WindowsPdbDetected` and `StoreFailure` preserve the
corresponding validation and persistence distinctions. The acquisition service
returns that distinction from its explicit-capability descriptor overload, and
the evidence document settles as `Failed`. Callers that use PDBs as optional
enrichment may ignore the returned provider failure; Type Source projects it
through its existing typed acquisition-failure path. A retained PDB that cannot
be reopened by the acquisition service likewise settles as `Failed` with
`StoreFailure = ReadFailed` while preserving its route and cache origin. A cache
hit records `FromCache` and does not fabricate a network attempt.

This operation-scoped evidence is captured directly in the downloader rather
than reconstructed from process-global `NetworkTelemetry` subscriptions.
`NetworkTelemetry` remains appropriate for request-start logging, aggregate
counts, and policy observation, but it does not own response, retry, body,
validation, or store settlement and can include concurrent unrelated work.
`SymbolPackageDownloaderTests.AcquirePdbAsync_InMemoryStoreSupportsRepeatedReads`
gates network acquisition evidence and the no-network cache-hit document;
`HttpRetryHelperTests.HeaderFirstBodyRead_TimesOutAndRetriesAStalledBody`
gates retry-count accounting.

## Error handling

When PDB acquisition fails, we report the reason:

- **"Windows PDB"**: Found a PDB but it's Windows format (unreadable)
- **"no symbols"**: No PDB found on any server (distro build, private package, etc.)
- **"embedded"**: PDB is embedded in the library (success case)
- **"msdl.microsoft.com"**: Downloaded from Microsoft symbol server (success case)

Typed SourceLink queries preserve these states as absent or failed outcomes.
Package aggregation retains the package-relative library path beside each
unavailable or failed outcome.

An HTTP 200 response from an exact-identity symbol-server URL that is rejected
for its size, format, or PDB identity is a failed provider response rather than
definitive absence. A symbol package is instead an identity inventory: valid
Portable PDBs for sibling assemblies or target frameworks are a definitive miss
for the requested identity, while malformed same-name entries remain failure.
A supported later provider may still complete acquisition. Downloaded PDB bytes
are parsed and identity-checked before cache publication; a legacy cached entry
that fails those checks yields a typed store failure when no later provider
succeeds. PDB-store publication and read-back failures, including cache
permission failures, remain visible store failures and are not attributed to a
remote feed. The Release gates
`AcquirePdbAsync_LimitedHostRejectsOversizedSymbolPackage`,
`AcquirePdbAsync_LimitedHostRejectsOversizedMsdlBeforeStore`,
`AcquirePdbAsync_SymbolPackageWithSiblingIdentitiesRemainsAbsence`,
`AcquirePdbAsync_InvalidSymbolPackageCandidateRecordsFailure`,
`AcquirePdbAsync_RejectedDownloadIsNotPublished`,
`AcquirePdbAsync_InvalidCachedPdbContinuesToNextProvider`,
`AcquirePdbAsync_InvalidCachedPdbRecordsFailure`,
`AcquirePdbAsync_CachedReadFailureContinuesToNextProvider`,
`AcquirePdbAsync_CachedReadFailureRecordsFinalStoreFailure`,
`AcquirePdbAsync_StoreWriteFailureContinuesToNextProvider`,
`AcquirePdbAsync_UnretainedDownloadRecordsFailure`,
`AcquirePdbAsync_ReadbackStoreFailureIsVisible`,
`AcquirePdbAsync_UnretainedDownloadContinuesToNextProvider`,
`AcquirePdbAsync_CancellationPreservesPriorProviderStoreFailure`, and
`PdbAcquisitionServiceTests.PathlessParticipant_ProviderFailureIsVisible`
enforce these distinctions. Store-permission projection remains gated by
`SourceCorrespondencePdbAcquisition_StorePermissionFailureIsTyped`. Type Source
additionally gates the ordinary warning with
`TypeSourcePdbLatencyHedge_FailedProviderReportsAcquisitionFailure`.

The persistent symbol-miss cache records HTTP 404 absence only. A cached HTTP
403 retains failure evidence, while other operational statuses are not replayed
as absence. Legacy `.miss` entries whose payload is not exactly HTTP 404 are
ignored so the provider is retried.
`DownloadPdbAsync_CachePreservesAbsenceAndFailure` and
`DownloadPdbAsync_LegacyOperationalMissIsRetried` gate those distinctions. The
source-correspondence and authored-rebuild harness lanes reject an adjacent
standalone PDB when the assembly has no Portable CodeView identity, and project
malformed embedded-PDB opening or a present unusable SourceLink map as typed
failure after verified local and repository alternatives are exhausted.
`SourceCorrespondencePdbAcquisition_RejectsUnverifiedStandalonePdb` and
`AuthoredSourceHarvest_RejectsUnverifiedStandalonePdbWithoutTerminating`
gate the failure boundary across the census and corpus-harvest consumers, and
`SourceCorrespondencePdbAcquisition_MalformedEmbeddedPdbIsFailure` gate the PDB
opening boundaries without changing the general-purpose `PdbContext` policy;
`SourceCorrespondencePdbAcquisition_MalformedSourceLinkMapIsFailure` gates the
whole-map decode boundary,
`SourceCorrespondencePdbAcquisition_RejectedDocumentMappingIsFailure` gates a
rejected mapping for one requested document in a partially usable map, and
`SourceCorrespondencePdbAcquisition_RemoteNotFoundIsAbsent` gates definitive
remote document absence.

## Related resources

- [SourceLink Exposure](sourcelink-exposure.md)
- [Portable PDB Specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
- [Symbol Server Protocol](https://github.com/dotnet/symstore)
- [SourceLink](https://github.com/dotnet/sourcelink)
