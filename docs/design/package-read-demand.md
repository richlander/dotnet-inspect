# Package read demand

## Status, owner, and claim

This document is the normative owner for **which package entries a resolved
semantic-content narrowing selects when PackageHouse plans a ranged read**. It
is a slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day.

The [PackageHouse semantic content-demand
contract](package-house-semantic-content-demand.md) owns the reusable
package/TFM/root-family narrowing and whether cache, manifest,
archive-directory, ranged, or complete acquisition satisfies a query. This
document owns only entry expansion after the House resolves that narrowing
against one validated ZIP central-directory snapshot and selects ranged
execution. The current direct selected-Library PDB companion remains an
existing request path; the semantic content-query successor instead uses File
List evidence followed by an exact Files request. Commands and hosts do not
select an access mode.

The claim has five parts:

- **Asset demand.** A House request carries an asset demand. `Surface` asks
  for the compile surface only: the reference or compile assets the
  realization selects. `SurfaceAndImplementation`, the default, also asks for
  the implementation assets. A surface-only realization carries no
  implementation role and no role correspondence. It is never upgraded in
  place: a consumer that needs the implementation asks for a realization with
  that demand.
- **The folder is the unit of a ranged read.** A ranged selection is expanded
  from each selected asset to every direct entry of that asset's folder: the
  folder's other assemblies and their documentation files, but not its
  subfolders, such as satellite resource folders. A command that inspects one
  assembly of a folder usually inspects its neighbors and their
  documentation next, and a whole folder is cached as a whole.
- **Named implementation reads aligned blocks.** A consumer may name the
  implementation assemblies it needs. Those are read in fixed, entry-aligned
  blocks of about 1 MB, derived from the archive alone, instead of whole
  folders. A large implementation folder, such as a runtime pack's 172
  assemblies, then costs the blocks an inspection actually names.
- **Selected-Library companions are exact.** A compile realization that
  returns selected Library handoffs may request the package-local Portable
  PDB beside each selected implementation assembly. A listed companion is
  added as one exact entry; an absent companion does not fail realization.
- **Files are named directly.** A file demand names exact entries or folders.
  A ranged `Acquire` may carry it, and ranged content serves the House's pull
  reads. Hosts that need one exact path call `PackageFileAcquisition`, which
  owns the demand, House execution, cache-aware acquisition, manifest
  resolution, and generation-bound read.

This document transfers one claim from the
[package source model](package-source-model.md#ranged-payload-realization):
which entries the House selects for a ranged read. It consumes, and does not
redefine, the House request and its realization
([PackageHouse](package-house.md)), role correspondence
([package asset-selection correspondence](package-asset-selection-correspondence.md)),
the ranged read ([package archive range access](package-archive-range-access.md)),
and size first and the entry cache
([package cache policy](package-cache-policy.md)).

## Basis

Commands differ in what they read:

- **Broad surface.** `find` member search, `implements`, and `extensions`
  scan every public type and member of a package's compile surface. They never
  decompile or read method bodies, so the reference assemblies answer them.
  `depends` reads assembly references, which the surface carries too.
- **Focused depth.** `type`, `member`, and `library` inspect one type or
  library, including its implementation: IL, decompiled source, and
  source-link data. `graph` also reads implementation assemblies.
- **The whole package.** `package` reports the archive's content, so it
  needs the complete archive.

Before this slice the exact-package search Root read both the compile and the
implementation assets. For `Avalonia` 12.1.2 targeting net10.0 that is the
`ref/net10.0` and `lib/net10.0` folders, eight requests and 3.9 MB, for a
search that reads only the first folder.

Measured 2026-09-24 against nuget.org, native linux-x64 builds, `find
.InvalidateMeasure --package Avalonia@12.1.2 --tfm net10.0`, three cold runs
and three warm runs each:

| Build | Cold | Warm | Requests, cold | Requests, warm | Bytes received, cold | Cache on disk |
| --- | --- | --- | --- | --- | --- | --- |
| 0.26.0 | 0.92–1.00 s | 0.43–0.51 s | 1 | 0 | 10.2 MB | 46.2 MB |
| ranged read (#8415) | 0.58–0.85 s | 0.49–0.57 s | 8 | 8 | 3.9 MB | none |
| this slice | 0.57–0.59 s | 0.25–0.26 s | 5 | 0 | 2.3 MB | 8.3 MB |

The five cold requests are the size probe, the tail read, and three spans for
the `ref/net10.0` folder, which the archive interleaves with other folders'
documentation files.

`find .ToString --package ID@VERSION --tfm net10.0` on eight more packages,
the same day, one cold and one warm run each, returned the same rows as
0.26.0 for every package. Every warm run made no request:

| Package | Archive | Bytes received, cold: 0.26.0 | Bytes received, cold: this slice | Cache on disk: 0.26.0 | Cache on disk: this slice |
| --- | --- | --- | --- | --- | --- |
| `Dapper` 2.1.89 | 0.6 MB | 0.6 MB | 0.6 MB | 2.3 MB | 2.3 MB |
| `Serilog` 4.4.0 | 0.8 MB | 0.8 MB | 0.8 MB | 4.4 MB | 4.4 MB |
| `Humanizer.Core` 3.0.10 | 1.9 MB | 1.9 MB | 0.5 MB | 7.1 MB | 1.2 MB |
| `AWSSDK.Core` 4.0.102.6 | 2.3 MB | 2.3 MB | 0.7 MB | 11.6 MB | 2.4 MB |
| `Newtonsoft.Json` 13.0.4 | 2.5 MB | 2.5 MB | 0.4 MB | 13.2 MB | 1.5 MB |
| `Azure.Storage.Blobs` 12.29.2 | 2.6 MB | 2.6 MB | 0.7 MB | 16.5 MB | 3.5 MB |
| `SkiaSharp` 4.152.1 | 9.9 MB | 9.9 MB | 0.2 MB | 38.9 MB | 0.4 MB |
| `Microsoft.CodeAnalysis.CSharp` 5.9.0 | 12.2 MB | 12.2 MB | 4.4 MB | 52.4 MB | 13.6 MB |

Five interleaved cold runs per build on this fast link (about 40–60 MB/s):

| Package | 0.26.0 | This slice |
| --- | --- | --- |
| `Serilog`, under the cut | 0.18–0.21 s | 0.17–0.21 s |
| `Newtonsoft.Json` | 0.26–0.54 s | 0.22–0.33 s |
| `AWSSDK.Core` | 0.27–0.40 s | 0.25–0.31 s |
| `SkiaSharp` | 0.42–0.85 s | 0.22–0.29 s |
| `Microsoft.CodeAnalysis.CSharp` | 0.72–2.42 s | 0.51–0.61 s |
| `Avalonia` | 0.65–1.06 s | 0.55–0.69 s |

## Contract

### Asset demand

`PackageHouseRequest` carries a `PackageAssetDemand`. A ranged read of a
compile realization selects the realization's compile assets for `Surface`,
and its compile and implementation assets for `SurfaceAndImplementation`. A
runtime realization selects its runtime universe as before. The complete
archive, when a read takes the complete fetch, is unaffected: it holds every
entry whatever the demand.

The package Root records the demand it was realized with. A surface-only Root
prepares no implementation role and forms no role correspondence, so no
consumer can observe a missing implementation as a correspondence failure.
Deriving a Root with another demand keeps its selection and changes only the
demand; it never reuses a surface-only payload to answer an implementation
role.

### The folder unit

After the realization selects its assets, the House adds every entry whose
parent folder is a selected asset's folder. It adds nothing from other
folders and nothing from subfolders. The realization receipt is still
evaluated over the materialized content, so it names only entries that were
read.

A cached directory tells a later read which entries its demand needs and
which the entry cache already holds, without a request
([package cache policy](package-cache-policy.md#the-entry-cache)). Because
reads are whole folders, a folder is either cached as a whole or read as a
whole.

### Named implementation and aligned blocks

A House request with `SurfaceAndImplementation` demand may name the
implementation assemblies it needs, by file name. With no names, every
selected implementation asset is read, as above. With names, the realization
selects only the implementation assets with those names. The surface is
unchanged and is still read as whole folders. The package Root prepares an
implementation role, and forms a role correspondence, only for the named
assets. A name that selects no implementation asset is a visible realization
failure, never an empty success.

Named implementation assets are read in **aligned blocks**, not folders.
Blocks are fixed by the archive alone:

- The entries of each implementation folder are taken in archive order: the
  order of their local-header offsets in the central directory.
- Walking from the folder's first entry, a block closes when adding the next
  whole entry would take the block past the budget. No entry is ever split,
  so an entry larger than the budget is a block by itself.
- An entry's size is its span in the archive: from its local header to the
  next local header of its folder, or, for the folder's last entry, to the
  next local header in the archive. Entries of another folder interleaved
  between two of the folder's entries count toward the earlier one, so no
  block's request is longer than the budget, unless the block is one entry
  above it.
- The budget is the [size cut](package-cache-policy.md#size-first), 1 MB.

A read of a named asset fetches the whole block that contains it, as one
request from the block's first local header to the end of its last entry.
Every archive entry whose local header lies between those two, whatever its
folder, lies wholly inside the request, up to the next local header. It is
read with the block, materialized, and kept in the entry cache like any read
entry, so a block's request never carries bytes that are discarded, and a
later read that selects one of those entries does not fetch it again. The
only bytes read and not kept are the read slack of at most 1 KB past the
last entry, which may hold the start of the next entry; no partial entry can
occur at the start, which is always a local header. Blocks tile the folder
without gaps or overlap, and a later read requests only the runs of a
block's entries that the entry cache does not hold, so two reads never fetch
the same entry, and a block already held by the
[entry cache](package-cache-policy.md#the-entry-cache) costs no request. The
entry cache still stores entries. A block is a read-planning unit, and it is
present when all of its entries are.

A named asset whose folder is already read whole as the surface, as in a
package with no `ref/` folder, gets no block. Its own folder is being read
anyway, so its block would add only other folders' interleaved entries: a
cost the unnamed read does not pay.

Covered entries of other folders pass every check the reader applies to any
entry: declared lengths, compression method, and the payload's expanded-byte
bound. A covered entry that fails one fails the block's read visibly, as it
would fail a read that selected it. Only a malformed or hostile archive has
such an entry.

On `Avalonia` 12.1.2 for net10.0, whose `lib/net10.0` folder the archive
interleaves with other folders, naming `Avalonia.Dialogs.dll` reads its
0.75 MB block of 15 entries in one request, and keeps the 14 `lib/net8.0`
entries that request covers. The whole realization, surface
included, takes 6 requests and 3.0 MB, where reading the whole
implementation folder takes 10 requests and 4.7 MB.

Following a reference into another assembly, such as a runtime facade that
forwards a type to `System.Private.CoreLib`, is a new realization naming that
assembly. Each realization is an ordinary House operation. Nothing reads
after settlement, and ranged content stays immutable.

Measured on `Microsoft.NETCore.App.Runtime.linux-x64` 10.0.0 (39.9 MB, 172
managed assemblies in one contiguous 28.3 MB run), a 1 MB budget gives 25
blocks:

| Named assembly | Its own size | Its block |
| --- | --- | --- |
| `System.Private.CoreLib` | 6.70 MB | 6.70 MB |
| `System.Text.Json` | 0.84 MB | 0.84 MB |
| `System.Linq` | 0.32 MB | 0.83 MB |
| `System.Collections` | 0.14 MB | 0.94 MB |
| `System.Runtime` (a facade) | 0.02 MB | 0.86 MB, the facade block |

Counting assemblies instead of bytes was measured and rejected. With
8-assembly blocks, `System.Linq` reads 2.67 MB and `System.Collections`
1.74 MB, because a large neighbour shares the block.

### Selected-Library companion demand

`PackageHouseRequest` may carry an optional selected-Library companion
demand. It belongs only to a compile `Realize` request that asks for
`SurfaceAndImplementation` assets and `SelectedLibraries` handoffs. The first
companion is `ImplementationPortablePdb`: for each selected implementation
assembly, the House derives the same-directory, same-stem `.pdb` path.

This section describes the current direct realization handoff. The semantic
content-query successor does not request this companion with Best Library.
Instead, Best Library plus TFM-wide File List downloads one DLL and reports
typed directory evidence for the owner-issued implementation DLL and adjacent
PDB. A later host-neutral PDB operation may request those exact entries through
the Files terminal or skip to an external provider. That settlement owner is
tracked by
[#9002](https://github.com/richlander/dotnet-inspect/issues/9002). Until that
successor and its production composition land, current callers continue to
receive the existing companion handoff.

If the package directory lists that path, the ranged selection adds it as an
exact entry. It does not make the PDB a block anchor and does not widen the
implementation assembly's aligned block. If the directory does not list the
path, realization continues without it. Complete acquisition is unchanged
because it already retains the whole archive.

The demand changes neither compile asset selection nor API-to-implementation
correspondence. PackageHouse does not validate Portable PDB format or
identity and does not consult symbol servers; the selected-Library consumer
owns those later decisions. A request without companion demand preserves the
existing ranged plan exactly. An unnamed implementation realization already
reads the implementation folder whole, so a listed adjacent PDB is already
retained without an additional exact entry.

### Exact file demand

A consumer that needs package files rather than assets names them directly:
`PackageFileDemand` is a set of exact entry paths and folder prefixes.
Paths are validated as entry names are: relative, `/`-separated, and without
an empty, `.`, or `..` segment, a root, `\`, or `:`. They are compared
without regard to case. A file demand needs no asset realization, so a
ranged `Acquire` operation may carry one; only an `Acquire` operation does.
Without a file demand, ranged access still requires a `Realize`
operation.

A file demand fetches:

- the package's root folder, whole, which holds the `.nuspec` and common
  package-level files;
- the folder of each named entry, whole, under the folder unit above: its
  direct entries, not its subfolders; and
- each named folder with every entry beneath it, subfolders included.

A named entry or folder the archive's directory does not list is a visible
failure: the House returns `NoMatch` with a selection-stage failure naming
it, on the ranged and the complete path alike, never an empty success.
[Size first](package-cache-policy.md#size-first) and the
[entry cache](package-cache-policy.md#the-entry-cache) apply unchanged: an
archive under the cut is acquired complete, and a warm file read makes
no request.

`PackageFileAcquisition` is the host-neutral exact-file operation. Its request
contains one pinned package coordinate, one safe relative file path, and one
House operation. Its plan contains the host-provided authority-scoped store,
limits, transfer policy, diagnostics, and ranged size cut. The operation:

1. creates the exact file demand and House request;
2. executes cache-first, size-first ranged acquisition;
3. resolves one case-insensitive manifest entry while preserving its actual
   path and rejecting missing or ambiguous matches; and
4. returns a typed acquired file whose `OpenRead()` is bound to that House
   settlement and content generation.

Hosts bind source authorization, stores, deadlines, cancellation, and output
projection. They do not independently assemble House demand, execute
acquisition, resolve the entry, or open an arbitrary settlement path.

Ranged content supports the House's
[pull-based payload reads](package-house.md#pull-based-acquired-payload-reads)
for its materialized entries, whose bytes the archive reader has already
checked against the directory's declared length and CRC. Opening an entry
that was not read stays a visible refusal. The capability is host-neutral, so
a Browser/Wasm host that adopts ranged access needs no further substrate.

Two production hosts use the exact-file operation for their document
vocabularies:

- **The CLI exact-file content route.** `package
  ID@VERSION --content` with one literal root `README.md` or
  `skills/**/SKILL.md` path
  calls the desktop `AcquireFileAsync` adapter. It uses the
  authority-scoped store the search Root uses, so size first, the entry
  cache, and durable HTTP identity apply. The directory lists every entry, so
  the .NET tool-wrapper check reads the ranged directory as it reads the
  complete archive; a possible wrapper takes the complete package-content
  path, which follows the redirect, and says so in verbose output. Separator,
  raw, JSONL, and Skill file projections detach content up to the 16 MiB
  document limit; exact README file output copies the acquired file directly
  with a bounded buffer.
  Offline, the command keeps the local package cache path, as the search Root's
  offline branch does, so it does not yet answer from the authority-scoped
  store or the entry cache. For `Newtonsoft.Json` 13.0.4 (2.5 MB), a cold
  README content request is the size probe, the directory tail, and one span:
  6 of 24 entries.
- **The Browser/Wasm viewer**, which validates that the requested path is a
  root `README.md`, root `PACKAGE.md`, or Markdown beneath `skills/`, then
  calls `PackageFileAcquisition`. Size first keeps small archives on complete
  acquisition; larger archives read the root and selected file's direct folder
  by range. The
  Browser package-entry store retains the directory and selected entries in
  Cache Storage, so a warm read after store recreation makes no package request
  ([cache policy adoption step 5](package-cache-policy.md#adoption)).

### Current demand adoption

| Command | Semantic demand |
| --- | --- |
| `find` member search, `implements`, `extensions`, `depends`, with one `--package ID@VERSION` and `--tfm` | `Surface` |
| `library address` with one exact package Library and non-JSON output | `SurfaceAndImplementation`, one named implementation, and a listed adjacent Portable PDB when source location is selected |
| `library address --json` | `SurfaceAndImplementation` plus the established complete Library JSON metadata |
| `type`, `member`, and other `library` operations | `SurfaceAndImplementation` |
| `graph` | `SurfaceAndImplementation` |
| `package` | Whole archive |
| `package ID@VERSION --content` of a root `README.md` or `skills/**/SKILL.md` | One exact file |
| `diff --history`, Metadata cells (API findings) | `Surface` |
| `diff --history`, Analysis cells (IL-body findings) | `SurfaceAndImplementation` |

At this head, callers still carry transitional ranged or complete access
choices. [#8994](https://github.com/richlander/dotnet-inspect/issues/8994)
removes those choices one adopter at a time. The table records semantic need,
not transfer policy; new callers must not copy the transitional access split.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Surface search of an archive whose surface and implementation folders are both large | only the surface folder is read | `ConfiguredPayloadAcquisitionTests.SearchCommand_RangedRead_RealAvaloniaArchive`: `Avalonia` 12.1.2, 22 of 121 entries, every span inside `ref/net10.0`, under 2.5 MB |
| 2. A folder whose assemblies have documentation files | the whole folder, documentation included | `PackageRangedRealizationTests`: `PCLStorage` 1.0.2 for net45 reads its two assemblies and two documentation files |
| 3. A surface-only Root | no implementation role; admission succeeds | case 1's gate, whose search admits a Root realized from the surface folder alone |
| 4. The same search twice from a credential-free HTTP feed | the second makes no package request and returns the same output | `ConfiguredPayloadAcquisitionTests.SearchCommand_RangedRead_TransfersOnlyTheSelectedAssembly` |
| 5. A surface search, then focused commands and `package` on the same package | each command's output equals the baseline build's | `eng/measure-package-read-demand.sh`, a preserved probe as design evidence; it also reproduces the measurements above |
| 6. A named implementation assembly | the surface folders and only the block that contains the named assembly | `PackageRangedRealizationTests.NamedImplementation_RealAvalonia_ReadsTheSurfaceAndOnlyTheNamedBlock`, real asset `Avalonia` 12.1.2: the block is one entry span, the other-folder entries inside it are materialized and cached, a later read that selects one makes no request for it, and roles realize over what was read |
| 7. A second realization naming a neighbour in the same block | no request | `PackageRangedRealizationTests.NamedImplementation_NeighbourInACachedBlock_MakesNoRequest`, entry cache |
| 8. An entry larger than the budget | one block holding that entry alone | `PackageEntryBlocksTests`: blocks tile the folder without gap or overlap, whatever the input order |
| 9. A name that selects no implementation asset | a visible realization failure | `PackageRangedRealizationTests.NamedImplementation_NameSelectingNothing_FailsVisibly` (House `NoMatch`) and `PackageRootAcquisitionTests.AssetDemand_NamedRootRealizesOnlyItsNames` (Root `PackageImplementationNameException`) |
| 10. A named asset in a folder already read whole as the surface | no block and no extra request: the named read equals the unnamed read | `PackageRangedRealizationTests.NamedImplementation_FolderAlreadyReadAsSurface_AddsNoBlock`, a boundary fixture with interleaved `lib/net8.0` and `lib/net10.0` folders and no `ref/` |
| 11. A root `README.md` content request from an archive above the cut | size probe, tail, and one span for the root folder; every CLI projection matches the complete-acquisition path, file output is byte-identical and bounded-streamed, detached output above 16 MiB fails visibly; Browser/Wasm incrementally decodes the same ranged entry and a warm read makes no request | `ConfiguredPayloadAcquisitionTests.PackageCommand_ReadmeContent_RealNewtonsoftArchive_ReadsTheRootFolderByRange` and `BrowserEngineBoundaryTests.PackageDocument_RealNewtonsoftReadmeUsesRangeAndWarmEntryCache`, real asset `Newtonsoft.Json` 13.0.4; `PackageCommand_ReadmeExport_CompleteFallbackWritesTheSameBytes`; `PackageFileContentInspectionTests.EntryAboveDetachedLimitFailsBeforeOpeningContent` |
| 12. A `skills/<name>/SKILL.md` content request | the root folder and that exact file's direct folder only; sibling and reference subfolders are not fetched | `ConfiguredPayloadAcquisitionTests.PackageCommand_SkillExport_ReadsTheRootAndExactSkillFolderOnly`, a boundary fixture modeled on the skill layout of `CrestApps.AgentSkills.Mcp.OrchardCore` 1.2.0 and padded above the cut, because that package is under it |
| 13. The same content request twice from a credential-free HTTP feed | the second makes no package request | case 11's gate: the second request reads the entry cache, and its transfer receipt has no request |
| 14. A named entry the directory does not list | a visible failure | `PackageRangedRealizationTests.PackageFileAcquisition_ColdWarmAndMissing_AreOrchestrated` (typed exact-file result), `FileDemand_UnlistedName_FailsVisibly` (House `NoMatch`, ranged and complete), and `ConfiguredPayloadAcquisitionTests.PackageCommand_SkillExport_MissingSkillFailsVisibly` |
| 15. A pull read of a ranged entry that was not read | a visible refusal | `PackageRangedRealizationTests.FileDemand_PullReadOfAnUnreadEntry_IsAVisibleRefusal`, real asset `PCLStorage` 1.0.2: the first read, not the open, raises `PackageEntryNotMaterializedException` |
| 16. A Metadata history over a large package with a `ref/` folder | each version cell reads only its surface folder; findings identical to the complete path | `ConfiguredPayloadAcquisitionTests.DiffHistory_MetadataCells_ReadOnlyTheSurfaceFolderByRange`, real assets `Avalonia` 11.3.14 and 12.1.2 for net8.0: every span starts in `ref/net8.0` and crosses other entries only within the 64 KiB merge gap, and the spans read every entry of that folder |
| 17. An Analysis history | each version cell reads its surface and implementation folders only; findings identical to the complete path | `ConfiguredPayloadAcquisitionTests.DiffHistory_AnalysisCells_ReadTheSurfaceAndImplementationFoldersByRange`, the same assets, `analysis.allocation` on `Button.OnClick`: `ref/net8.0` and `lib/net8.0` |
| 18. The same history twice from a credential-free HTTP feed | the second makes no package request | case 16's gate; in Debug hosts, `DiffHistoryEvidenceEnvelope_RangedCellsRecordTheirReads` shows each cold cell's size probe, tail, and entry spans, and each warm cell's `EntryCache` path with no request |
| 19. A named implementation Library requests its listed adjacent Portable PDB | the implementation DLL remains a block anchor, the PDB is retained as an exact entry, and its neighboring entry is not materialized | `PackageRangedRealizationTests.RangedSelectedLibraryPortablePdbDemandRetainsExactCompanion`, using the real `System.Text.Json` implementation assembly in a boundary archive |
| 20. The requested adjacent Portable PDB is absent | realization and the selected Library handoff still settle without a PDB entry | `PackageRangedRealizationTests.RangedSelectedLibraryPortablePdbDemandAllowsAbsentCompanion` |
| 21. A selected reference Library has no implementation counterpart | companion demand invents neither an implementation asset nor a PDB; the reference-only handoff settles | `PackageRangedRealizationTests.RangedSelectedLibraryPortablePdbDemandDoesNotInventImplementation` |
| 22. CLI Address selects an exact Library from a pinned or latest configured package | both version policies use PackageHouse and render the same Member context | `ConfiguredPayloadAcquisitionTests.LibraryAddressCommand_ConfiguredPackageUsesHouseSelection` |
| 23. CLI Address selects an exact Library from a local archive and requests source location | the exact archive is admitted by its embedded identity and the adjacent Portable PDB supplies source evidence | `CommandExecutionTests.LibraryAddressCommand_PackageSourceLocationUsesAdjacentPortablePdb` |
| 24. A local archive lacks embedded package identity | exact-source admission fails visibly before Address inspection | `CommandExecutionTests.LibraryAddressCommand_LocalArchiveRequiresEmbeddedPackageIdentity` |

## Adoption

1. This document, `PackageAssetDemand`, the folder unit, and the
   exact-package search Root realized with `Surface`.
2. Named implementation demand and aligned blocks in the House and the
   acquisition step, with gates 6 to 10.
3. `library address` declares
   `SurfaceAndImplementation`, one named implementation, selected Library
   handoffs, and source-sensitive companion demand for non-JSON output.
4. `type`, `member`, and the remaining `library` operations name the
   assemblies that define what they inspect.
5. `graph` declares `SurfaceAndImplementation`.
6. Runtime packs are realized with named implementation demand once the
   [package-backed platform source](package-backed-platform-realization.md)
   no longer reads every member's identity at realization. That change belongs
   to its owner.
7. File demand and pull reads over ranged content, composed by
   `PackageFileAcquisition`. Exact `package --content` requests for a root
   `README.md` or `skills/**/SKILL.md` path call its desktop adapter, including
   separator, raw, JSONL, and `--out` projections. The Browser/Wasm document
   viewer calls the same operation for root `README.md`, root `PACKAGE.md`, and
   `skills/**/*.md`. Other Inspect Web package operations retain their existing
   acquisition paths.
8. `diff --history` realizes each version cell with semantic asset demand:
   Metadata
   cells with `Surface`, whose package Root prepares no implementation role,
   and Analysis cells with `SurfaceAndImplementation`. A history over the
   versions of a large package permits the House to select only each version's
   surface folder, or its surface and implementation folders, and a repeated
   history may read nothing it already holds.

[#8994](https://github.com/richlander/dotnet-inspect/issues/8994) moves access
planning into PackageHouse after these demand semantics are preserved.

## Non-claims

This document does not:

- change size first, the entry cache, or the durable identity of HTTP
  authorities, which the [package cache policy](package-cache-policy.md)
  owns;
- change which assets a realization selects, only which entries a House-planned
  ranged read of them fetches; or
- decide whether a semantic demand uses ranged or complete acquisition.
