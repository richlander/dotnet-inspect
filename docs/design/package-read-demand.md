# Package read demand

## Status, owner, and claim

This document is the normative owner for **how much of a package archive a
realization asks a ranged read for**. It is a slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day.

The claim has three parts:

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
- The budget is the [size cut](package-cache-policy.md#size-first), 1 MB.

A read of a named asset fetches the whole block that contains it, as one
request covering the block from its first entry's local header to the next
block's first local header. Blocks tile the folder without gaps or overlap,
so two reads never fetch the same bytes, and a block already held by the
[entry cache](package-cache-policy.md#the-entry-cache) costs no request. The
entry cache still stores entries. A block is a read-planning unit, and it is
present when all of its entries are.

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

### Per-command demand

| Command | Demand | Access at this head |
| --- | --- | --- |
| `find` member search, `implements`, `extensions`, `depends`, with one `--package ID@VERSION` and `--tfm` | `Surface` | ranged, size first |
| `type`, `member`, `library` | `SurfaceAndImplementation` | complete |
| `graph` | `SurfaceAndImplementation` | complete |
| `package` | the whole archive | complete |

Only the first row adopts ranged access in this slice. The others keep their
current complete acquisition until they adopt ranged access (see
[Adoption](#adoption)).

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Surface search of an archive whose surface and implementation folders are both large | only the surface folder is read | `ConfiguredPayloadAcquisitionTests.SearchCommand_RangedRead_RealAvaloniaArchive`: `Avalonia` 12.1.2, 22 of 121 entries, every span inside `ref/net10.0`, under 2.5 MB |
| 2. A folder whose assemblies have documentation files | the whole folder, documentation included | `PackageRangedRealizationTests`: `PCLStorage` 1.0.2 for net45 reads its two assemblies and two documentation files |
| 3. A surface-only Root | no implementation role; admission succeeds | case 1's gate, whose search admits a Root realized from the surface folder alone |
| 4. The same search twice from a credential-free HTTP feed | the second makes no package request and returns the same output | `ConfiguredPayloadAcquisitionTests.SearchCommand_RangedRead_TransfersOnlyTheSelectedAssembly` |
| 5. A surface search, then focused commands and `package` on the same package | each command's output equals the baseline build's | `eng/measure-package-read-demand.sh`, a preserved probe as design evidence; it also reproduces the measurements above |
| 6. A named implementation assembly | the surface folders and only the block that contains the named assembly | `PackageRangedRealizationTests`, real asset `Avalonia` 12.1.2 |
| 7. A second realization naming a neighbour in the same block | no request | `PackageRangedRealizationTests`, entry cache |
| 8. An entry larger than the budget | one block holding that entry alone | contract suite, block planner |
| 9. A name that selects no implementation asset | a visible realization failure | contract suite |

## Adoption

1. This document, `PackageAssetDemand`, the folder unit, and the
   exact-package search Root realized with `Surface`.
2. Named implementation demand and aligned blocks in the House and the
   acquisition step, with gates 6 to 9. No command sets names yet.
3. `type`, `member`, and `library` adopt ranged access with
   `SurfaceAndImplementation`, naming the assemblies that define what they
   inspect, and reusing the folders a surface search cached. Moving these
   commands onto the House is separate work; this step only sets their demand.
4. `graph` adopts ranged access with `SurfaceAndImplementation`.
5. Runtime packs are realized with named implementation demand once the
   [package-backed platform source](package-backed-platform-realization.md)
   no longer reads every member's identity at realization. That change belongs
   to its owner.

`package` keeps complete acquisition.

## Non-claims

This document does not:

- change size first, the entry cache, or the durable identity of HTTP
  authorities, which the [package cache policy](package-cache-policy.md)
  owns;
- change which assets a realization selects, only which entries a ranged read
  of them fetches;
- change complete acquisition for any command.
