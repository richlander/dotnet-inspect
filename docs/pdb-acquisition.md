# PDB acquisition

This document describes how dotnet-inspect locates and downloads PDB (Program Database) files to enable SourceLink resolution and source code navigation.

## Overview

PDBs contain debug information that maps compiled code back to source. Metadata
owns PE/PDB opening and extracts raw portable-PDB facts. The SourceLink layer
recognizes and interprets the SourceLink custom-debug-information document.

## PDB source document acquisition

After a Portable PDB maps a member or type to a checksummed source document,
`PdbSourceAcquisition` looks for the document bytes in this order:

1. The PDB-recorded local path, when local source reads are enabled.
2. Each caller-supplied local Git clone, addressed by the exact commit and
   repository-relative path in a `raw.githubusercontent.com` SourceLink URL.
3. The remote SourceLink URL.

Every successful path must match the Portable PDB checksum before its content
becomes evidence. Local-clone acquisition reads the committed Git blob rather
than the working tree, so an outage does not require a pristine checkout and
uncommitted edits cannot replace the pinned source. A missing commit, path, or
checksum match in one clone continues through the remaining clones and then to
the remote source.

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
the write and post-acquisition read paths. Local-path projection occurs before
the caller-owned PDB stream is opened, so a projection failure cannot leak that
stream;
`PdbAcquisitionServiceTests.PathlessParticipant_LocalPathFailurePrecedesOwnedStreamOpen`
gates that ownership boundary. Cached and downloaded Portable PDBs
are parsed and identity-checked before an acquired result is returned, so an
invalid entry cannot suppress later providers;
`SymbolPackageDownloaderTests.AcquirePdbAsync_InvalidCachedPdbContinuesToNextProvider`
gates the fallback.

## Error handling

When PDB acquisition fails, we report the reason:

- **"Windows PDB"**: Found a PDB but it's Windows format (unreadable)
- **"no symbols"**: No PDB found on any server (distro build, private package, etc.)
- **"embedded"**: PDB is embedded in the library (success case)
- **"msdl.microsoft.com"**: Downloaded from Microsoft symbol server (success case)

Typed SourceLink queries preserve these states as absent or failed outcomes.
Package aggregation retains the package-relative library path beside each
unavailable or failed outcome.

An HTTP 200 symbol response rejected for its size, format, or PDB identity is a
failed provider response rather than definitive absence. A symbol package that
contains no same-name PDB remains absent, and a supported later provider may
still complete acquisition. PDB-store publication failures, including cache
permission failures, remain visible failures. The Release gates
`AcquirePdbAsync_LimitedHostRejectsOversizedSymbolPackage`,
`AcquirePdbAsync_LimitedHostRejectsOversizedMsdlBeforeStore`,
`AcquirePdbAsync_MismatchedSymbolPackageIdentityRecordsFailure`, and
`SourceCorrespondencePdbAcquisition_StorePermissionFailureIsTyped` enforce
these distinctions.

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
`SourceCorrespondencePdbAcquisition_MalformedEmbeddedPdbIsFailure` gate the PDB
opening boundaries without changing the general-purpose `PdbContext` policy;
`SourceCorrespondencePdbAcquisition_MalformedSourceLinkMapIsFailure` gates the
SourceLink decode boundary.

## Related resources

- [SourceLink Exposure](sourcelink-exposure.md)
- [Portable PDB Specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
- [Symbol Server Protocol](https://github.com/dotnet/symstore)
- [SourceLink](https://github.com/dotnet/sourcelink)
