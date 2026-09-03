# Local folder package source

This document is the normative owner for bounded local-folder package-source
operations in `NuGetFetch`. It defines how one owner-issued
`LocalPackageSourceIdentity` becomes a source client that recognizes general
NuGet folder-feed layouts and produces source-bound search, version, manifest,
payload, and failure outcomes.

This is the second focused slice of
[#3759](https://github.com/richlander/dotnet-inspect/issues/3759). Canonical
path identity is already owned by
[Local package source identity](local-package-source-identity.md). Package
authority, local-before-HTTP ordering, cross-source composition, and cache
authorization remain the separate adoption successor
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400).

The contract is implemented and verified by the Release gates in
[Implementation evidence](#implementation-evidence).

## Boundary

The owner consumes these typed inputs:

| Input | Owning contract | Use here |
| --- | --- | --- |
| `LocalPackageSourceIdentity` | [Local package source identity](local-package-source-identity.md) | Name exactly one lexical local root without parsing source text again. |
| `PackageSourceAssociation` and the bound source-result factory | [NuGetFetch typed source-result identity](browser-package-sources.md#nugetfetch-typed-source-result-identity) | Construct every observation, result, payload, and failure with one producer, association, transport kind, and private issuer. |
| `NuGetOperationContext` | [Operation-context handoff](browser-package-sources.md#operation-context-handoff) | Apply the original caller cancellation and one monotonic operation ceiling to all filesystem and archive work. |
| Validated package IDs, versions, search arguments, and finite source limits | Existing `IPackageSourceClient` operations and this owner | Select an operation, bound its work, and form exact normalized coordinates. |
| A host local-filesystem capability | This owner | List directories, open seekable files, observe length, and transfer a readable stream when those operations are available. |

It returns only existing NuGetFetch source-result shapes:

- search matches or complete version observations;
- one bounded exact manifest;
- one caller-owned package payload stream;
- an explicit unsupported-symbol outcome; or
- a typed source failure or caller cancellation.

Every settled value retains the source-result identity bound to the client.
The transport kind is `LocalFolder`. The owner neither creates nor interprets
configured package authority.

The owner also defines the local source's archive-observation boundary. It may
inspect a package archive far enough to establish its coordinate, bounded
manifest, and safe stream handoff. It does not authorize the archive for a
package store. `DotnetInspector.Packages` remains responsible for complete
payload validation and publication under its own limits. Revalidating there is
intentional: source evidence and store admission are different claims at
different data boundaries. As with HTTP source clients, a successful
`PackageSourcePayload` means that source bytes were acquired for the requested
coordinate; it does not claim that package-store admission has succeeded.

## Source and producer identity

Client construction consumes an existing `LocalPackageSourceIdentity` object.
It does not accept a path string, `Uri`, declaring base, or configuration
alias. It must not trim separators, call `Path.GetFullPath`, parse `file://`,
fold path case, resolve links, or derive another persistent spelling.

NuGetFetch projects that exact identity through its owner-controlled
source-result factory. Two local clients have equal producer identity exactly
when their `LocalPackageSourceIdentity` values are equal. Local producer keys
occupy the source-result owner's versioned local namespace and derive their
payload from the identity owner's persistent value. The key remains opaque to
consumers. Producer display is an inert rendering of the canonical path, not a
path parser, configured authority, or key source.

The caller supplies the `PackageSourceAssociation`. A local client cannot
derive that association from its root, producer, alias, or display. Every
success and failure uses the client's one bound result factory; no local result
constructor accepts identity components independently.

## Recognized layouts

Recognition is operation-scoped. The client does not permanently label a
mutable root as V2 or V3, and it does not choose one layout by filesystem
enumeration order. Each operation observes all relevant shapes within one
ledger and then settles.

The general V2-style layout admits package archives in exactly two places:

- `<root>/<file>.nupkg`; and
- `<root>/<one-child-directory>/<file>.nupkg`.

Traversal goes no deeper for V2. Symbol archives ending in
`.symbols.nupkg` or `.snupkg` are not package candidates. Extension matching is
ordinal case-insensitive rather than inherited from host wildcard behavior.
The embedded root nuspec supplies the authoritative ID and version. Once read,
the file name must begin with that ID plus `.` and end in a NuGet version
spelling that normalizes to the embedded version, followed by `.nupkg`.
Version and exact-coordinate operations may therefore prefilter V2 file names
by the requested ID plus `.` before opening an archive; search must inspect
every admitted V2 candidate.

The general V3-style layout admits:

```text
<root>/<normalized-lowercase-id>/<normalized-lowercase-version>/
    <normalized-lowercase-id>.<normalized-lowercase-version>.nupkg
```

The ID and version directory names and archive file name use their canonical
normalized lowercase spellings. A `.sha512`, `.nupkg.metadata`, or external
nuspec sidecar is neither required nor authoritative. The embedded root nuspec
must match the path coordinate.

Packages-config repositories, project-template unzipped repositories, symbol
repositories, arbitrary recursive archives, global-packages folders, and
loose assemblies are not recognized layouts.

A root may contain both recognized layouts. All admitted entries participate
in one operation. Distinct physical archives whose embedded identities
normalize to the same coordinate are a duplicate, including a V2/V3 collision;
the client never selects one by layout precedence, spelling, timestamp, or
enumeration order.

## Capability contract

Capabilities describe what the client and its host can perform, not whether a
mutable root currently contains a matching package.

| Capability | Host requirements | V2-style operation | V3-style operation |
| --- | --- | --- | --- |
| `Search` | Bounded listing, seekable open, length, and bounded archive reads | Inspect every admitted root and immediate-child archive. | Inspect every admitted normalized ID/version archive. |
| `VersionEnumeration` | Bounded listing, seekable open, length, and bounded archive reads | Inspect admitted archives and retain the requested ID. | Inspect the requested normalized ID directory and validate admitted archives. |
| `Manifest` | Bounded listing, seekable open, length, and bounded archive reads | Locate the unique requested coordinate and copy its embedded nuspec. | Locate the unique requested coordinate and copy its embedded nuspec. |
| `PackagePayload` | Manifest requirements plus transfer of the same open readable stream | Validate the unique requested coordinate, rewind, and transfer its archive stream. | Validate the unique requested coordinate, rewind, and transfer its archive stream. |
| `SymbolPayload` | None | Unsupported. | Unsupported. |

Complete duplicate detection and feed-shape validation require bounded listing,
including for exact operations. A host that can open only a derived V3 path
therefore does not advertise exact local capabilities. This deliberately
trades NuGet.Client's direct-path optimization for one deterministic contract
across mixed and mutable roots.

The capability flags remain independent. For example, a host that can list and
read bounded files but cannot transfer a stable caller-owned stream may expose
search, version, and manifest capabilities without `PackagePayload`. A host
with no local filesystem exposes `None`.

An advertised capability may still settle as `NotFound`, `InvalidResponse`,
`ResponseRejected`, `Transport`, `Timeout`, or caller cancellation for one
operation. Unsupported means that the client-host pair lacks the operation,
not that a package or root is absent.

## One bounded observation ledger

Each operation creates one fresh ledger. It charges work before retaining or
opening an entry and applies the strictest relevant finite limits:

| Dimension | Initial default |
| --- | ---: |
| Directory entries observed across the operation | 16,384 |
| Candidate archives admitted across the operation | 4,096 |
| Entries declared by one archive central directory | 50,000 |
| Bytes in one archive central directory | 16 MiB |
| Bytes in one embedded manifest | 1 MiB |
| Aggregate embedded-manifest bytes read by one operation | 64 MiB |
| Length of one package archive | 500,000,000 bytes |

The owner may expose validated positive finite options for these dimensions.
Changing a default is source-client policy, not a change to layout identity.
No requested search result count, directory page size, archive-advertised
length, or downstream store limit replaces the ledger.

Directory work is bounded by depth as well as count. The owner observes only
the root, one child level for V2 archives and V3 ID directories, and one
additional V3 version level. It never follows an archive entry as a filesystem
path and never recursively walks an unrecognized directory.

A physical listing may arrive in arbitrary order. The operation materializes
at most its finite directory-entry bound, validates names, then orders retained
entries ordinally before archive inspection. If the host reports that more
entries exist than the bound can hold, the operation fails
`ResponseRejected`; it does not publish a filesystem-order-dependent subset.

Archive observation preflights the end-of-central-directory records and the
declared central-directory extent before constructing an object model that
materializes entries. It rejects an excessive or inconsistent declaration
first. The operation then finds exactly one root nuspec directly from the
bounded central-directory records, where root means that the entry name
contains no `/` or `\`. It checks the matching local header, independently
expands the stored or deflated bytes, and verifies the exact declared expanded
length and CRC under the compressed and expanded 1 MiB limits, aggregate
manifest budget, and operation ceiling. XML parsing prohibits DTDs and
external resolution.

This is a source-coordinate admission, not full package-content validation.
Non-manifest entries are not extracted or assigned store paths. A successful
payload stream still passes through the package owner's complete archive
validation before publication. "Malformed archive" at this boundary means
unsafe or inconsistent ZIP structure, a missing or ambiguous root nuspec, or
invalid coordinate metadata. Defects in an otherwise unobserved payload entry
remain visible at package-store admission or later stream consumption.

Every loop, chunked read, archive record, manifest decode, and transition
between host calls observes caller cancellation and the remaining operation
ceiling. Local work creates no HTTP request deadline. A library-owned expiry
settles the operation with the existing `Timeout` failure kind. An expiry
while consuming a returned payload stream carries the existing
`PackageSourceTimeout` detail with kind `Operation` and the configured
duration. A host syscall that has already entered the operating system may not
be preemptible; cancellation or expiry wins immediately when control returns.
This contract does not claim stronger kernel preemption.

## Search

`SearchAsync` and `SearchByPrefixAsync` inspect the complete bounded local
observation before applying the requested result count. Empty search text
matches every admitted package ID. Keyword search uses case-insensitive
matching against package ID, tags, and description; prefix search applies a
case-insensitive package-ID prefix.

Prerelease filtering occurs before grouping. Each matching normalized package
ID contributes its highest admitted version and carries a
`KeywordSearch` observation with `PackageListingState.NotApplicable`. Results
are ordered by normalized package ID. A requested count that omits otherwise
complete matches returns `RequestedLimit`; it is not source incompleteness.

A safety-ledger exhaustion fails `ResponseRejected`. It does not become
`ClientPageLimit`, because an arbitrary filesystem prefix is not deterministic
or sufficient evidence for a safe partial result. A malformed, unreadable,
duplicate, or changed admitted candidate likewise fails the operation rather
than disappearing from an apparently complete search.

## Version enumeration

`GetVersionsAsync` validates every admitted candidate needed to establish the
requested normalized ID, rejects duplicates, sorts versions by NuGet semantic
version order, and returns `CompleteVersionEnumeration` observations.
Listing state is `NotApplicable`, and the result does not claim authoritative
listed/unlisted state: `HasAuthoritativeListingState` is false.

An existing empty root or an existing root with no matching package produces a
successful empty version result. A safety-ledger exhaustion cannot be
represented by `PackageVersionResult` without falsely claiming complete
enumeration, so it fails `ResponseRejected`.

## Exact manifest and payload

Exact operations normalize the caller's ID and version through
`PackageSourceCoordinate`. They inspect the bounded root, admit the unique
matching archive, and validate its embedded coordinate before success.

`GetManifestAsync` returns an immutable bounded copy of the exact embedded
nuspec bytes. It does not return an external V3 nuspec or retain an archive
reader.

`GetPackageAsync` opens one seekable archive stream, validates the embedded
coordinate through that same stream, rewinds it, and transfers exclusive
ownership to the caller. It does not validate one path and reopen another.
`AdvertisedLength` is the length observed on the transferred stream.
This success authenticates the source coordinate and stream handoff only.
Full package validity and publication remain downstream package-owner results.

If the existing root contains no matching coordinate, both operations return
`NotFound`. If more than one physical archive supplies the coordinate, both
fail `InvalidResponse`. For a live, non-expired context,
`TryGetSymbolsAsync` always fails `Unsupported` with the requested coordinate
and local source identity.

## Failure settlement

Failures remain source outcomes rather than exceptions except for argument
validation and caller cancellation already defined by `IPackageSourceClient`.

| Condition | Source settlement |
| --- | --- |
| Host has no required local capability | `Unsupported` |
| Source root is absent or ceases to be a directory | `Transport` |
| Existing root has no exact requested coordinate | `NotFound` |
| Existing root has no search or version matches | Successful empty result |
| Entry or archive cannot be listed, opened, sought, or read | `Transport` |
| Candidate archive, central directory, nuspec, coordinate, or XML is malformed or inconsistent | `InvalidResponse` |
| Two physical archives normalize to one coordinate | `InvalidResponse` |
| A finite directory, candidate, archive, or byte bound is exceeded | `ResponseRejected` |
| Owner-issued operation ceiling expires | `Timeout` |
| Caller cancellation wins | `OperationCanceledException` with the original caller token |
| Returned payload later cannot be read or disposed | Source-bound `PackageSourceStreamException` |

Failure messages are fixed, source-safe descriptions. They do not retain child
entry names, discovered paths, host exception messages, archive content, or
nuspec text. The producer display may expose the canonical root under the
source-result owner's existing inert-display contract.

An authentication failure is impossible for this owner. Local failures never
fall through to an HTTP client and are not recast as package absence.
Argument validation, caller cancellation, and an already-expired operation
context retain the existing source-client precedence before capability
settlement.

The downstream package-source aggregate must preserve the distinction already
owned by the [package source model](package-source-model.md): a transport
failure from an absent root is incomplete source evidence, while a successful
empty result from an existing root is authoritative source-relative absence.

## Mutation and stream lifetime

Separate operations observe the root independently. Creating, removing, or
replacing packages between completed operations is allowed and does not
invalidate either result. The client owns no long-lived folder snapshot,
archive cache, or layout classification.

Within one operation, an entry that disappears or contradicts already observed
length or stability evidence settles as `Transport`, unless the bytes actually
read establish a more specific `InvalidResponse`. It is not silently changed
to `NotFound`. Hosts may expose an opaque change token or handle generation;
the owner compares such evidence when available but does not derive identity
from it.

The contract does not attempt to defeat a trusted same-machine actor that
replaces bytes while preserving every host-observable attribute. The bytes
read through one admitted open stream are the operation's evidence. Symlink,
junction, mount, and reparse-point policy remains the host's ordinary
filesystem policy; this owner neither resolves them into a new source identity
nor treats them as hostile.

Manifest success owns a copied immutable value. Payload success transfers the
same open stream used for coordinate validation. The caller owns that stream
and must keep the supplied `NuGetOperationContext` alive until consumption or
disposal finishes. Reads and disposal preserve producer and transport identity
under the existing source-stream failure contract. Client disposal does not
dispose a stream already transferred to a caller.

## Host and platform boundary

The local source engine depends on capabilities rather than `DirectoryInfo`,
`FileInfo`, ambient working-directory state, or HTTP fallback. Its host
boundary can:

- report whether the canonical root is an available directory;
- list one directory with explicit overflow evidence;
- open one root-contained entry as a seekable readable stream;
- report finite length and optional stability evidence; and
- transfer that stream when the host can preserve caller ownership.

The first physical-filesystem adapter may remain internal. A future
Browser/Wasm host can supply equivalent capabilities without changing source,
producer, layout, or result identity. Defining a public browser filesystem
registration API is a separate host-composition concern.

When no adapter exists, client construction may still bind the canonical
source-result identity, but capabilities are `None` and every operation settles
`Unsupported` after caller-cancellation and operation-expiry precedence. This
is the portable default for Browser/Wasm and cannot construct or contact an
HTTP transport.

The implementation remains SRM-only, NativeAOT-friendly, Roslyn-free, and free
of inspected-assembly loading. Package manifest XML and ZIP observation are
data parsing; no inspected assembly is loaded.

## Convention and deliberate differences

This contract adopts NuGet's documented general local-feed shapes: root or
immediate-child V2 archives and normalized hierarchical V3 archives. Evidence
was surveyed at NuGet.Client commit
[`14240937a33fdf1daf2ef9adea9d83202cb8ccc0`](https://github.com/NuGet/NuGet.Client/commit/14240937a33fdf1daf2ef9adea9d83202cb8ccc0).
NuGet.Client source and tests are Apache-2.0 licensed; these links provide
behavioral evidence only, and no code is transferred.

The analogous NuGet.Client V2 enumerator checks root packages and exactly one
child level in
[`LocalFolderUtility.GetNupkgsFromFlatFolderChunked`](https://github.com/NuGet/NuGet.Client/blob/14240937a33fdf1daf2ef9adea9d83202cb8ccc0/src/NuGet.Core/NuGet.Protocol/Utility/LocalFolderUtility.cs#L853-L897).
Its V3 coordinate paths use normalized lowercase ID and version spellings in
[`VersionFolderPathResolver`](https://github.com/NuGet/NuGet.Client/blob/14240937a33fdf1daf2ef9adea9d83202cb8ccc0/src/NuGet.Core/NuGet.Packaging/VersionFolderPathResolver.cs#L139-L209).

The local source deliberately differs where NuGet.Client's behavior cannot
satisfy this owner's reliability contract:

- NuGet.Client's V3 finder requires external nuspec and hash sidecars in
  [`GetPackageV3`](https://github.com/NuGet/NuGet.Client/blob/14240937a33fdf1daf2ef9adea9d83202cb8ccc0/src/NuGet.Core/NuGet.Protocol/Utility/LocalFolderUtility.cs#L663-L696);
  this owner treats the archive and its embedded nuspec as authoritative.
- NuGet.Client search reads every package before applying `skip` and `take` in
  [`LocalPackageSearchResource`](https://github.com/NuGet/NuGet.Client/blob/14240937a33fdf1daf2ef9adea9d83202cb8ccc0/src/NuGet.Core/NuGet.Protocol/LocalRepositories/LocalPackageSearchResource.cs#L35-L87);
  this owner applies independent directory, candidate, archive, byte, and
  operation bounds.
- NuGet.Client's safe enumeration catches non-cancellation exceptions and
  returns an empty list in
  [`LocalFolderUtility`](https://github.com/NuGet/NuGet.Client/blob/14240937a33fdf1daf2ef9adea9d83202cb8ccc0/src/NuGet.Core/NuGet.Protocol/Utility/LocalFolderUtility.cs#L1134-L1198);
  this owner keeps unreadable roots and entries visible.
- NuGet.Client exact V2 selection and deduplication can depend on filesystem
  order. This owner orders observations and rejects duplicate normalized
  coordinates.
- NuGet.Client has no operation deadline or portable filesystem capability
  boundary for local feeds. This owner consumes `NuGetOperationContext` and
  exposes unsupported-host behavior without HTTP fallback.

NuGet.Client's packages-config and unzipped-template resource types are
specialized repositories, not evidence that those layouts belong in this
general source.

## Implementation evidence

Implementation is verified by these named Release gates:

- `LocalFolderSource_ConsumesCanonicalIdentityWithoutReparsing` proves path and
  `file://` equivalents share one producer, case-distinct Unix roots remain
  distinct, and no declaring base or ambient current directory is consulted.
- `LocalFolderSource_AllSettlementsUseBoundResultFactory` derives every
  operation success and failure shape and proves exact producer, association,
  transport, capability, coordinate, and issuer propagation.
- `LocalFolderSource_V2FlatAndImmediateChildCapabilities` and
  `LocalFolderSource_V3HierarchicalCapabilities` exercise search, complete
  version enumeration, exact manifest, exact payload, and close-negative
  unsupported layouts.
- `LocalFolderSource_MixedLayoutsRejectDuplicateCoordinates` proves normalized
  V2/V2 and V2/V3 collisions fail independently of creation and enumeration
  order.
- `LocalFolderSource_DirectoryBoundRejectsWithoutPartialAuthority` proves an
  over-limit search or version operation cannot publish a successful complete
  or filesystem-order-dependent partial result.
- `LocalFolderSource_ArchivePreflightBoundsMaterialization` proves excessive
  central-directory count and size are rejected before archive-entry object
  materialization; hidden expanded bytes, unsupported manifest methods and
  flags, and per-manifest and remaining-aggregate byte cases prove the
  remaining archive ledger.
- `LocalFolderSource_ExactOperationsValidateEmbeddedCoordinate` proves a
  renamed archive, malformed nuspec, missing nuspec, and multiple root nuspecs
  cannot produce manifest or payload success.
- `LocalFolderSource_AbsentUnreadableAndChangedRootsRemainDistinct` proves
  existing-package absence is `NotFound`, an absent or unreadable root is
  `Transport`, and disappearance after observation is not rewritten as
  absence.
- `LocalFolderSource_ContextBoundsEveryOperation` proves caller cancellation
  identity, terminal operation timeout, and checks during directory, archive,
  manifest, payload, and transferred-stream cleanup work. It also proves
  pre-call and racing per-read cancellation precedence, reversible EOF and seek
  reactivation, and source-bound reads released by concurrent disposal.
- `LocalFolderSource_PayloadTransfersValidatedStreamOwnership` proves the
  returned stream is the validated stream, remains caller-owned after client
  disposal, and translates later read and disposal failures with exact source
  producer and transport identity.
- `LocalFolderSource_HostUnavailableCannotConstructHttp` proves Browser/Wasm
  and an explicit unavailable-host fixture expose no capabilities, preserve
  local source identity, and invoke no HTTP factory or handler.
- The NuGetFetch `browser-wasm` build remains the platform compilation gate.

Purpose-built fixtures cover V2 flat, V2 immediate-child, V3 hierarchical,
mixed, malformed, duplicate, bounded, mutating, and unavailable-host cases.
Tests use tiny overridden limits rather than allocating production-scale
directories or archives.

## Non-claims

This owner does not define:

- config- or command-relative path resolution, `file://` parsing, host path
  equality, persistent local identity, or symlink identity;
- configured aliases, package-source mapping, configured package authority, or
  `PackageSourceAssociation` issuance;
- local-before-HTTP ordering, route precedence, cross-source candidate
  aggregation, package selection, or enrichment;
- package-store authorization, full archive extraction validation, cache
  publication, global-packages-folder semantics, or `.nupkg.metadata`;
- HTTP, Gallery, V3 service-index, authentication, retry, or redirect behavior;
- specialized packages-config, global-packages, unzipped-template, recursive,
  loose-file, or symbol-source layouts;
- a public browser filesystem registration API; or
- CLI, browser settings, diagnostics, or package-profile presentation.

The [package source model](package-source-model.md) and
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400) consume this
owner's source outcomes. They must not infer local source authority or package
precedence from producer display, transport kind, filesystem path text, or
success alone.
