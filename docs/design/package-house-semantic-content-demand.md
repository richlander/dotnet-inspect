# PackageHouse semantic content demand

## Status, owner, and exact claim

This document is the normative owner for the semantic package content a
`PackageHouse` caller requests and for the House-owned choice of how that
content is acquired. It is tracked by
[#8994](https://github.com/richlander/dotnet-inspect/issues/8994) under the
[PackageHouse composition](package-house.md) and the package-read program in
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386).

The operator approved one product-facing rule:

> A caller declares what package evidence it needs. PackageHouse alone chooses
> whether cache, manifest service, archive directory, ranged entries, or a
> complete archive satisfies that demand.

Commands, Workspace loaders, and hosts do not choose ranged or complete
payload access. They do not retry a ranged result through a complete path or
reconstruct archive selectors. PackageHouse preserves the semantic query
through planning, execution, typed fallback, settlement, and receipts.

The semantic content query is separate from the existing package identity or
version demand. One request therefore retains both:

- which package is requested; and
- one reusable narrowing over that package; and
- which evidence terminals run over the base narrowing.

None of these values is reconstructed from another or from display text.

## Content query

One `PackageHouseContentQuery` carries one narrowing and one or more result
terminals. Every terminal in the query observes the exact same base narrowed
package space and retains the same owner-issued narrowing receipt.

The narrowing forms are:

- **Package-wide.** Every admitted package entry is in scope.
- **TFM-wide.** Every admitted entry associated with one requested target
  framework is in scope. A runtime target also carries its RID.
- **TFM and ordered root preference.** The query carries one target framework
  and an arbitrary non-empty ordered list of distinct package library root
  families: `ref`, `lib`, or `runtimes`. PackageHouse selects the first
  applicable family in request order. Examples include `lib`, `ref || lib`,
  `lib || runtimes`, and `ref || lib || runtimes`; the vocabulary is not
  limited to two choices.

The package asset-selection owner determines whether a root family is
applicable for the target context. PackageHouse preserves that owner-issued
decision and does not infer applicability from folder existence or rendered
paths. A `runtimes` choice requires the target's RID.

TFM-wide narrowing is the complete direct contents of the folders containing
the owner-selected compile surface and its corresponding implementation
assets. A RID-specific implementation selected by the asset owner replaces the
non-RID implementation folder in that target view. PackageHouse does not
approximate this scope with a TFM path-prefix filter.

The first applicable family wins as the primary root. A later family
contributes no candidates or inventory merely because it appears later in the
preference chain.

The result terminals are:

- **Nuspec.** Return the exact package manifest without acquiring archive
  payload when the authorized source supplies it directly. This terminal
  requires package-wide narrowing.
- **File list.** Return the complete validated archive file-entry inventory
  without expanding entry content, projected to the base narrowed package space.
  Package-wide requests may opt into logical directory evidence, including
  explicit empty directory entries, from the same validated generation.
  Directory evidence is unavailable (null, not an empty population) when an
  older retained content implementation cannot supply its admitted snapshot;
  callers may use their established complete-inspection path.
- **Files.** Return the complete validated content of one or more exact package
  entries within the base narrowed space.
- **Libraries.** Return every compatible library in the base narrowed space.
- **Library and inventory for target.**
  `GetLibraryAndInventoryForTarget` requires TFM-wide narrowing and returns one
  policy-selected Library plus the complete logical Library inventory for that
  target.
- **Library inventory for target.**
  `GetLibraryInventoryForTarget` requires TFM-wide narrowing and returns the
  same complete logical Library inventory without expanding any Library
  content.
- **Whole archive.** Return the complete package payload when the product
  question genuinely requires package-wide content. This terminal requires
  package-wide narrowing.

A query may request several compatible terminals. In particular,
`GetLibraryAndInventoryForTarget` may run with File List when a caller also
needs the raw package-entry inventory. Both observe the same TFM-wide
narrowing; neither repeats or independently interprets it.

One implementation slice adds a narrowing form or terminal only with a
production caller. `GetLibraryAndInventoryForTarget` is supported by package
member Source Locations, which consumes its exact implementation and PDB
references through later retained Files queries.
`GetLibraryInventoryForTarget` is supported by Portable PDB settlement for an
already-realized Browser/Wasm package assembly; it binds the exact provenance
path to an inventory row before requesting only that row's PDB reference.

Package-wide and TFM-wide Files and File List are implemented. Their results
retain the exact acquired generation and, for TFM-wide narrowing, the existing
compile/implementation asset-selection receipt. A File List can issue a later
Files query only from entries in that exact narrowed inventory; the query
retains the owner-issued narrowing declaration and typed entries rather than
reconstructing a path from display text. Before acquiring entry bodies, the
later request resolves that narrowing against its current validated directory;
a receipt from an older generation cannot admit bodies in the new generation.

The content query does not contain an access mode, range selector, cache
backend, source URL, or fallback preference. Network permission, source
authorization, transfer limits, cache capacity, and operation deadlines remain
host-supplied capabilities and policy.

## ZIP central-directory snapshot

Except for a nuspec request satisfied directly by the authorized source,
archive-backed content planning begins from one immutable, validated ZIP
central-directory snapshot. The archive reader owns its construction and ZIP
validation. PackageHouse retains its exact identity and composes its evidence;
it does not independently parse ZIP structures.

Portable archive admission completes before PackageHouse narrows the snapshot
or resolves a terminal. Complete, ranged, and cached-directory paths apply the
same entry-path, portable-destination collision, directory-shape, entry-count,
expanded-byte, and unique-directory rules. An archive rejected by those rules
does not become a missing or ambiguous Files result merely because one
requested path could be compared first.

Missing and ambiguous Files outcomes apply within an admitted owner-issued
snapshot. The terminal retains those typed outcomes for any owner-issued
inventory that can preserve distinct entry identities which compare equal
under exact-reference matching; portable ZIP admission currently rejects
case-colliding destinations before that resolution boundary.

The snapshot is the singular basis for:

- the package-wide entry inventory;
- TFM-wide and ordered-root narrowing;
- exact-file existence and ambiguity checks;
- library candidate paths, namesake evidence, and alphabetical ordering;
- logical TFM Library inventory, implementation correspondence, and
  adjacent-PDB entry evidence;
- the detached File List terminal;
- entry offsets, compressed and expanded lengths, and compression facts used
  by ranged-entry planning; and
- the entry identities used to query which content the entry cache already
  holds.

PackageHouse resolves the base narrowing once against that snapshot. A ranged
plan may carry that resolution into the result only when the directory view and
successful payload share one generation identity; source fallback or complete
fallback otherwise resolves once against the successful payload's validated
archive inventory. Library inventory and selection consume that candidate
space. PackageHouse joins owner-issued compile/implementation correspondence
with the same snapshot to issue exact later file references and adjacent-PDB
entry evidence. A terminal
cannot rescan the archive, construct a second path inventory, or resolve root
preference independently.

Namesake evidence and alphabetical ordering require only directory paths.
The later namespace-selection slice must acquire candidate assemblies from
this same narrowed generation and fail visibly when an earlier candidate's
Metadata evidence cannot be completed.

A complete archive transfer does not bypass the snapshot contract. Its
directory is validated into the same evidence shape before terminals execute.
A warm operation may consume an owner-issued cached snapshot. The semantic
result does not disclose whether the snapshot came from a directory-range read,
a complete payload, or the directory cache; the transfer receipt records that
execution path.

## File-list evidence

A query may ask for the complete detached file list together with another
terminal. File-list evidence is therefore composable rather than a mutually
exclusive payload mode.

The file list:

- preserves every validated package entry path and declared expanded length;
- is complete for the query's base narrowed package space;
- carries no stream, payload generation, cache handle, or source authority;
- identifies which exact files a later request may name under a compatible
  narrowing; and
- does not imply that any listed entry content was materialized.

PackageHouse may already need the archive directory to plan a ranged
acquisition. It publishes that directory as file-list evidence only when the
query asks for it, and projects it through the exact narrowing receipt before
publication. An internal planning read does not silently enlarge the product
result.

A path or file name may nominate one package entry for downstream assembly
binding. It cannot establish a canonical assembly identity, prove package
ownership, or turn a missing file-name match into an assembly-identity result.
Those decisions require decoded Metadata and remain with the external-supplier
composition owner.

A consumer evaluating several assembly references against one PackageHouse
generation reuses the owner-issued directory and narrowing evidence. It does
not rescan the archive or construct a competing package-entry inventory.
PackageHouse may provide an efficient lookup over its existing snapshot as an
implementation of this contract; that does not create an AssemblyRef-specific
terminal.

A selected file or library entry is always complete. PackageHouse returns the
whole validated ZIP entry or a typed non-success; it never returns a partial
assembly and labels it acquired. The file list explains what else the package
contains and can be requested next. The settlement and transfer receipt, not
the file list, establish that the returned entry completed validation.

## Library inventory terminals

`GetLibraryAndInventoryForTarget` is one composite terminal over TFM-wide
narrowing. It does not accept package-wide or TFM-plus-root narrowing. A
runtime identifier, when present in the target context, remains part of the
owner-issued target.

`GetLibraryInventoryForTarget` has the same narrowing and inventory semantics,
but returns no selected-Library handoff and expands no compile or
implementation entry. Its result is directory-derived evidence only. A later
Files query may expand exact references issued by that inventory.

`GetLibraryAndInventoryForTarget` returns:

- one selected Library whose required assembly content is complete and
  validated;
- one complete logical Library inventory for the TFM; and
- one selection receipt linking the selected Library to its exact inventory
  row.

The inventory is the same product concept the website presents for an active
package framework: one logical row per owner-issued compile Library, including
Libraries with no public Types. It is not a list of every physical DLL entry
or every package TFM.

Each detached inventory row preserves:

- the Library identity and compile role;
- its exact compile package-entry reference;
- its root, TFM, optional RID, and asset-selection receipts;
- its owner-issued implementation correspondence and exact implementation
  package-entry reference when distinct;
- adjacent implementation-PDB entry evidence: **Listed**, **Absent**, or
  **Not applicable**; and
- the exact PDB package-entry reference when Listed.

Listed proves only that the validated package directory contains the named
entry. It does not prove Portable PDB format, identity, readability, or
applicability to the implementation assembly. Absent means the TFM-wide
inventory contains the applicable implementation assembly but no
same-directory, same-file-name-stem `.pdb`. Not applicable means the logical
Library has no implementation correspondence for package-local symbols.

Inventory rows carry resource-free package-entry references, not streams,
content generations, or cache handles. A later Files query may present one or
more of those exact references to acquire additional compile, implementation,
or PDB content without reconstructing paths from display text.

The selection policy is part of this operation, not its name. The current
production-supported policy is **alphabetical**. Namespace-first selection
requires a same-operation Metadata evidence stage that can acquire candidate
assemblies from the validated package generation; it remains a later focused
adoption and is not exposed by this slice. PackageHouse does not parse target
frameworks, rank asset compatibility, or decode Metadata itself.

Selection is deterministic:

1. Order compatible libraries alphabetically.
2. Select the first compatible library in that order.

Alphabetical order compares the assembly file-name stem from the validated
package path using ordinal case-insensitive ordering, then compares normalized
package path using ordinal case-insensitive ordering. It does not require the
assembly manifest name, which may differ from the file name. The path tie-break
makes distinct libraries with the same file-name stem deterministic.

A package namesake is a pure name fact: `System.Text.Json.dll` is namesake
evidence for package `System.Text.Json`. It is derived from the package ID and
assembly file name without opening or decoding the assembly. The selection
receipt may disclose that fact, but namesake status does not create another
demand or override the alphabetical rule.

Missing compatible libraries, including an explicit empty compile group, is a
typed no-match.

Directory evidence selects the alphabetical first Library and PackageHouse
materializes only that assembly. Inventory construction does not materialize
the other Libraries, implementation assemblies, or PDB entries.

PackageHouse does not open a PDB, validate Portable PDB format or identity,
inspect embedded PDB content, or consult `.snupkg` or symbol-server sources.
The host-neutral PDB settlement tracked by
[#9002](https://github.com/richlander/dotnet-inspect/issues/9002) may consume an
inventory row, request its exact implementation/PDB references through Files,
or skip to an external provider. It owns verified-store reuse, provider
ordering, negative acquisition observations, and PDB admission.

The current [SourceHouse](source-house.md) contract, tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512), still
defines supplied companion or embedded PDB input and a separately authorized
external-acquisition capability. `AssemblyContextSourceQuery` and legacy CLI
wrappers still orchestrate parts of that sequence. #9002 must explicitly
reconcile that PDB-input contract before SourceHouse consumes Library inventory
evidence. Existing direct PackageHouse companion delivery remains transitional
until one production consumer adopts the new path.

## Compile inventory realization

A package Overview needs the complete package directory, compile selection,
archive and selected-slice measurements, and package-authored manifest facts.
It does not require selected Library content. A compile inventory request
combines package-wide File List with compile realization and Package-only
handoff. It preserves the selector-issued receipt, including owner-default
selection, explicit empty groups, no applicable target, and invalid
correspondence. It issues no Library handoff.

The House settles that inventory before any later Library-content request.
Under ranged execution it expands the root manifest and tool settings needed
by Package children, but no DLL, PDB, icon, or documentation body. The acquired
package retains its validated complete directory, including declared lengths,
so the existing compile-slice measurement and Package Root adapters can consume
it. That inventory Root must be realized with Library-content demand before
entering an assembly inspection. Complete acquisition below the size cut or
when ranges are unavailable preserves the same inventory result.

The motivating asset is `Avalonia@12.1.3`, observed on 2026-10-07: a
10,157,510-byte archive with 121 entries and separate reference and
implementation folders. The website Summary currently obtains its Library rows
from compile selection but transfers those folders before displaying them.
`Microsoft.CodeAnalysis.CSharp@5.9.0` provides a large single-Library control;
`Dapper@2.1.66` exercises the complete-transfer size cut. The actual Summary
export is measured through `eng/measure-inspect-web-package-summary.cs`, with
complete JSON equality against the base and cold/warm measurements.

The website is the first production adopter, under website download-flow
tracker [#9678](https://github.com/richlander/dotnet-inspect/issues/9678)
and focused issue [#9754](https://github.com/richlander/dotnet-inspect/issues/9754). The CLI's inventory-only
Package Info selection is the next adoption boundary; its complete-content
sections retain their existing demand until that selection reaches acquisition.
Release gates are `CompileInventoryDemandIsDeclaredBeforeAcquisition`,
`CompileInventory_UsesHousePlanningAndPreservesMeasurementReceipts`,
`PackageInventory_RangePreservesSelectionWithoutLibraryBodies`,
`PackageInventory_RangePreservesEmptyAndNoMatch`, and the existing
`QueryPackageSummary_ToolPayloadPublishesExactManagedLibraries` and
`PackageSummary_PreservesProductDefaultAlongsideCompleteLibraryInventory`. This slice consumes existing selection, measurement,
Root, and QuerySpace child-row contracts rather than changing them.

## Exact compile Library realization

The website Summary-to-Library flow declares an exact compile Library selector
before acquisition. PackageHouse resolves that selector against the same
owner-issued compile selection used by inventory. Surface demand materializes
only the selected compile entry; implementation demand also materializes its
corresponding implementation in archive-aligned chunks. Missing or ambiguous
selectors preserve their typed outcomes and never authorize sibling Library
content. The full directory and compile receipt remain authoritative for
identity, compatible targets, and owner-default targets.

Workspace realization consumes that same selector and prepares only its
surface and requested implementation participants. It preserves reference API
semantics and implementation-preferred Library documents. Hosts do not infer
correspondence or choose range versus complete access. Complete acquisition
below the shared size cut preserves the same selected participants.

`Avalonia@12.1.3` and `Microsoft.CodeAnalysis.CSharp@5.9.0` motivate this boundary:
on 2026-10-09 the inventory-only candidate made Summary faster but left
substantial acquisition work on the subsequent Library operation. Earlier
Roslyn measurements requested `net8.0` and returned PackageMismatch, so they
are not evidence of a successful Library API sequence. Valid measurements
use the browser-selected `netstandard2.0` target and explicitly retain its
existing API projection-limit outcome. The production browser API and
Library enablements exports must adopt the exact shared demand before this
candidate is ready. The CLI exact-Library caller is the subsequent shared
adoption boundary under #9754. Before/after evidence uses the existing
`eng/measure-inspect-web-library-open.cs` sequence and complete result parity;
synthetic multi-Library fixtures enforce that sibling bodies stay unread.
The gates are `ExactCompileLibraryDemand_RequiresDeclaredRolesAndPackageOnlyHandoff`,
`ExactCompileLibrary_PreservesInventoryAndNarrowsParticipants`, and
`ExactLibraryDemand_NarrowsTransferAndWorkspace`, plus existing tool-package
API and Library document gates.

### Joint exact Library inspection demand

For one exact Library selector, the host-neutral
`PackageLibraryInspectionDemandPlanner` receives the complete set of PublicApi
and ImplementationFacts acquisition requirements before source work starts.
It uses QuerySpace producer-capability planning to retain one association per
requirement and choose one source provision. SurfaceAndImplementation covers
Surface acquisition for that same selector; it does not replace the API result
with implementation declarations. API-only demand selects Surface. Empty or
invalid requirement sets fail before acquisition, and a plan cannot combine
selectors or authorize sibling Libraries.

The website Overview is the first adopter: when API and Enablements are both
needed, the API export declares both requirements and the Enablements export
declares ImplementationFacts. Both lower the accepted plan to the existing
exact Library scope demand and may join the same source operation. Independent
result publication, lease ownership, and waiter cancellation remain unchanged.
A failed shared acquisition cannot make a surface-only API unavailable merely
because implementation content was requested: the API retries its independent
Surface acquisition for a settled acquisition failure, while cancellation does
not start another operation. No timed batching or live reader retention is
introduced.

`Microsoft.CodeAnalysis.CSharp@5.9.0` and `Newtonsoft.Json@13.0.4` motivate this
adoption: the diagnostics below show two retained copies of their same selected
DLL when Overview uses separate demands. `Avalonia@12.1.3` exercises distinct
reference and implementation participants; `Dapper@2.1.66` retains the shared
complete-download size-cut behavior. Durable gates must verify standalone
Surface demand, one acquisition for a concurrent Overview, sibling exclusion,
independent outcomes when implementation acquisition fails, and complete
API/Enablements result parity. The CLI exact-Library adoption remains the
subsequent host under #9754 and uses this same resource-free demand API.

`PackageLibraryInspectionDemandPlanningTests` gates the structural requirement
associations and covering provision. `ExactLibraryDemand_NarrowsTransferAndWorkspace`
gates standalone Surface demand through the shared planner.
`OverviewDemand_SharesAcquisitionAndPreservesWaiterCancellation` and
`OverviewDemand_ImplementationFailurePreservesIndependentApi` gate successful
sharing, cancellation, sibling exclusion, and independent acquisition failure.
`library-overview-demand.test.ts` checks the website's companion declaration.
QuerySpace producer capabilities supplies plan validation; existing Root,
Workspace, cache, and range contracts retain their respective ownership.

### Adopted shared-demand measurements

The adopted implementation was measured on 2026-10-09 against effective base
`479f3536e153b5f90e5cc78d07a0920b33c9d664`, candidate
`2b538c3f95759f9647059d5c11ee91a72b950ae4`. Both use the candidate's
`eng/measure-inspect-web-library-open.cs` probe; the baseline's production
implementation is unchanged. The candidate defines
`WEB_PACKAGE_ENTRY_CACHE;WEB_LIBRARY_SHARED_DEMAND`; the baseline defines only
`WEB_PACKAGE_ENTRY_CACHE`. The same bounded entry-persistence adapter is
configured in both. This models the website's acquisition branch, not
JavaScript Cache Storage I/O. The candidate passes the known companion demand
through the production API export and shared QuerySpace planner.

The host is Linux x64, AMD Ryzen 9 9900X, SDK
`11.0.100-rc.1.26425.128`, Release NativeAOT. Builds finished before timing.
Each pinned scenario has one discarded warm-up pair followed by seven
alternating before/after pairs. Each invocation starts a fresh process and
executes cold then warm Summary plus concurrent API and Enablements, consuming
all outputs. CDN state is uncontrolled. Tail is the largest of seven samples
(nearest-rank p95), not a high-confidence population percentile.

| Scenario | Cold median / tail before (ms) | Cold median / tail after (ms) | Warm median / tail before (ms) | Warm median / tail after (ms) |
| --- | --- | --- | --- | --- |
| Avalonia 12.1.3 / net10.0 / default facade | 405.5 / 424.4 | 226.6 / 336.7 | 0.5 / 0.5 | 0.6 / 0.6 |
| Avalonia 12.1.3 / net10.0 / Avalonia.Base | 505.4 / 538.0 | 428.5 / 483.4 | 162.3 / 165.3 | 146.4 / 147.3 |
| Microsoft.CodeAnalysis.CSharp 5.9.0 / netstandard2.0 | 462.4 / 515.1 | 390.2 / 477.0 | 53.5 / 54.3 | 52.3 / 54.5 |
| Newtonsoft.Json 13.0.4 / net6.0 | 230.3 / 311.7 | 235.4 / 290.9 | 19.5 / 21.2 | 18.7 / 23.2 |
| Dapper 2.1.66 / net8.0 | 128.4 / 154.0 | 119.0 / 141.0 | 9.9 / 11.0 | 9.3 / 11.4 |

These operation times include serialization. Whole-process wall time includes
startup, both cold and warm operations, and output collection:

| Scenario | Process median / tail before (ms) | Process median / tail after (ms) | Retained package bytes before / after |
| --- | --- | --- | --- |
| Avalonia default facade | 422.0 / 439.8 | 239.9 / 350.0 | 16,503,751 / 10,133 |
| Avalonia.Base | 683.0 / 715.5 | 589.7 / 645.7 | 16,503,751 / 4,711,829 |
| Roslyn | 532.4 / 582.4 | 455.8 / 542.7 | 14,031,739 / 7,556,994 |
| Newtonsoft.Json | 263.5 / 346.4 | 263.1 / 324.4 | 1,444,030 / 725,781 |
| Dapper | 148.4 / 178.8 | 139.5 / 165.3 | 437,579 / 437,579 |

Retained bytes are the cache's logical image accounting, not transferred
bytes or process RSS. Every pair has identical Summary bytes and identical
API and Enablements SHA-256 hashes. Each request selects one exact Library.
All APIs are available and complete except Roslyn, which retains the same
explicit RetainedTextCharacters projection-limit outcome on both sides.

Publish the two binaries from the respective worktrees:

```bash
dotnet publish eng/measure-inspect-web-library-open.cs -c Release \
  -p:IsPublishable=true -p:DefineConstants=WEB_PACKAGE_ENTRY_CACHE \
  -o /tmp/web-overview-shared-before
dotnet publish eng/measure-inspect-web-library-open.cs -c Release \
  -p:IsPublishable=true \
  -p:DefineConstants=WEB_PACKAGE_ENTRY_CACHE%3BWEB_LIBRARY_SHARED_DEMAND \
  -o /tmp/web-overview-shared-after
```

Run each apphost with the coordinate and `overview`; add
`compile:ref/net10.0/Avalonia.Base.dll` as the fifth argument for the explicit
Avalonia.Base scenario. `WEB_LIBRARY_OPEN_RESULT_DIR` retains full output for
parity checking. Native apphost SHA-256 identities are:

- Before: `7df62cea542a48cb1d084f356911fee2299298740faf6fcbf2ff2eb7d5ed05c0`.
- After: `514226849e62ee4416ffe352761fab13060358712aa8057246f054e81c0628e1`.

Raw measurements and full outputs are retained locally under
`/tmp/web-overview-shared-native-perf` and
`/tmp/web-overview-shared-native-base-perf`. Newtonsoft's cold operation
median differs by 5.1 ms, within the observed network timing spread; that
difference does not establish a regression. Its complete-process median is
unchanged. These measurements do not establish a Newtonsoft latency win. Its later missing-entry acquisition still reads a
fresh central directory before the DLL span; eliminating that round trip
requires a separate source-owned representation/snapshot design under the
range and cache owners, preserving operation deadlines and source authority.

### Browser host corroboration

The same base and candidate were published as Release Browser/Wasm sites,
with the same opt-in benchmark bridge exposing their existing production
Worker operations. `browser/benchmark-library-overview.ts` drives Firefox,
fresh contexts and actual Cache Storage, one discarded pair then seven
alternating pairs per scenario. It requests Summary, then concurrent exact
Library API and Enablements, and consumes the JSON results. These browser
numbers diagnose the production host; they do not replace the NativeAOT
performance gate. Page painting is not measured.

| Scenario | Cold median / tail before (ms) | Cold median / tail after (ms) | Warm median / tail before (ms) | Warm median / tail after (ms) |
| --- | --- | --- | --- | --- |
| Avalonia default facade | 1069 / 1134 | 723 / 762 | 11 / 12 | 13 / 14 |
| Avalonia.Base | 3390 / 3473 | 3145 / 3258 | 1934 / 1975 | 1933 / 1974 |
| Roslyn | 1937 / 1994 | 1875 / 2180 | 657 / 676 | 679 / 693 |
| Newtonsoft.Json | 1244 / 1292 | 1293 / 1428 | 348 / 358 | 352 / 370 |
| Dapper | 859 / 874 | 880 / 896 | 184 / 191 | 183 / 185 |

| Scenario | Cold observed wall median / tail before (ms) | Cold observed wall median / tail after (ms) | Startup median before / after (ms) | Cold archive requests before / after |
| --- | --- | --- | --- | --- |
| Avalonia default facade | 1075.1 / 1153.8 | 729.3 / 768.4 | 119.8 / 122.6 | 10 / 6 |
| Avalonia.Base | 3404.2 / 3487.4 | 3152.1 / 3264.7 | 120.6 / 130.7 | 10 / 6 |
| Roslyn | 1945.5 / 2001.9 | 1881.8 / 2189.4 | 122.8 / 121.4 | 5 / 5 |
| Newtonsoft.Json | 1252.4 / 1298.6 | 1307.5 / 1434.7 | 122.7 / 122.4 | 4 / 5 |
| Dapper | 873.2 / 885.8 | 887.5 / 929.5 | 121.4 / 123.9 | 1 / 1 |

Observed wall includes Worker result transfer and output consumption; startup
is measured separately. Warm archive requests are zero in every scenario.
All 140 retained cold/warm measurements preserve full JSON hash parity and the
same API completeness or explicit Roslyn projection-limit outcome. The bridge
reports a null commit identity in these local publications, so artifact hashes
identify the publications instead:

- Package interop assembly before:
  `f5420a12c6e2c30e4c86c9a934f20ff42f0bcbde70c439a9cdd05dd6cd0c5943`.
- Package interop assembly after:
  `caa32ef2af7dbc5d556078a5ec427201ca1c4fd25ea59d968274d679c7f3b812`.
- Worker entry before:
  `3942e3f1633003d2e2fa49765a1a228c7ddd3b417983c7dd52f56048fd090548`.
- Worker entry after:
  `835beb7f3bcf6875968683db85888006cc78ae1bcfc9d5a8a08505e155a582f5`.

The measured Newtonsoft request trace confirms the cost: the baseline reads
`bytes=2419169-2484725` once, while the candidate reads that directory tail
once for Summary and again before the DLL. The candidate DLL range is smaller
(`1162439-1438309`, versus `1162439-1490735`), but one extra serial round trip
outweighs that saving. Both use modern ranged infrastructure. Warm Roslyn and
cold Dapper also show browser slowdowns; the candidate is not a universal
latency improvement.

Run the published sites with `scripts/serve-package-adoption-gate.ts`, then:

```bash
node browser/benchmark-library-overview.ts \
  http://127.0.0.1:4193/index.html http://127.0.0.1:4194/index.html \
  /tmp/web-overview-shared-browser-perf 7
```

Full results, request traces, startup and wall times remain under
`/tmp/web-overview-shared-browser-perf`. The next focused acquisition slice
belongs to the shared range and cache owners: preserve resource-free validated
directory evidence across operations, reopen with fresh operation context,
and bind reuse to the exact authorized source and representation. It must not
retain a reader with an expired deadline, trust a cache key alone, or bypass
representation-change handling for mutable feeds.

### Network receipt audit

The 2026-10-09 follow-up checks the owner-issued `PackageTransferReceipt`
carried by the retained House acquisition evidence, rather than inferring
consumed bytes from browser request headers. The same production export
sequence runs in a Release CoreCLR diagnostic probe with entry persistence
configured identically on base and candidate. These are configuration-neutral
receipts; the probe's timings are not performance evidence. The probe observes
retained receipts after Summary and Overview and counts each receipt object
once, so a cached operation cannot count its earlier acquisition again.

[The retained receipt payloads](../evidence/web-library-overview-network-2026-10-09.json)
record purpose, range, outcome, consumed body bytes, origin and authority.
The website's baseline export responses do not deliver a Debug enriched
evidence envelope; this diagnostic reads the House evidence already retained
by the operation. Browser evidence delivery remains a separate adoption.

| Scenario | Cold requests before / after | Cold consumed bytes before / after | Candidate Summary bytes | Candidate later Library bytes |
| --- | --- | --- | --- | --- |
| Avalonia default facade | 10 / 6 | 4,681,109 / 138,904 | 67,449 | 71,455 |
| Avalonia.Base | 10 / 6 | 4,681,109 / 1,786,944 | 67,449 | 1,719,495 |
| Roslyn | 5 / 5 | 4,674,023 / 2,914,735 | 67,557 | 2,847,178 |
| Newtonsoft.Json | 4 / 5 | 395,672 / 408,803 | 67,375 | 341,428 |
| Dapper | 1 / 1 | 437,579 / 437,579 | 437,579 | 0 |

Activity matches the current acquisition and cache contracts:

- For packages above the size cut, candidate Summary performs one abandoned
  SizeProbe (zero consumed body bytes), one DirectoryTail and one manifest
  EntrySpan. It acquires no Library bodies.
- Later API and Enablements share one acquisition: one fresh DirectoryTail,
  then one exact DLL EntrySpan for Roslyn/Newtonsoft or two corresponding
  reference/implementation spans for Avalonia. There is no second size probe
  and no second API-versus-Enablements transfer.
- Dapper is below the size cut: one complete download, then Cache receipts
  with zero requests. This is the intended size policy, not a range fallback.
- Every ranged request completes; there is no RangedThenDownload fallback,
  failed request, truncation or unexpected authority. The sole authority is
  nuget.org. The warm browser trace independently confirms zero archive
  requests for all scenarios.

For Newtonsoft, exact DLL acquisition saves 52,426 entry-span bytes, but the
extra 65,557-byte directory tail adds 13,131 bytes overall (3.3%) and one serial
request. Initial Summary transfer falls by 83.0%. This is a measured
acquisition tradeoff; the 5.1 ms NativeAOT difference alone is not a demonstrated
latency regression. A source-owned reusable directory snapshot targets the
known extra request, while preserving the existing cache validation contract.

### Acquisition performance investigation

The 2026-10-09 continuation compares candidate `08a1138` with a diagnostic
publication that changes only the API acquisition demand to the same
SurfaceAndImplementation demand already declared by concurrent Enablements.
This diagnostic is not the production API contract: an API-only request must
retain Surface demand. Any adoption must declare the requests known together
through host-neutral QuerySpace planning before acquisition, preserving each
independent result. Existing shared-operation lifetime and cancellation remain
a supporting constraint, rather than a new browser batching mechanism.

Seven alternating cold/warm pairs follow one discarded warm-up pair per asset.
The harness is `eng/measure-inspect-web-library-open.cs`, Linux x64 Release
NativeAOT, SDK `11.0.100-rc.1.26425.128`. Builds finish before measurement.
Cold means a fresh process with empty host state; CDN state is uncontrolled.
Full Summary bytes and API/Enablements hashes match in every cold/warm pair.
Roslyn retains the same RetainedTextCharacters projection limit in both cases;
the other API results are available and complete. Avalonia uses its default
namesake facade, rather than the larger `Avalonia.Base` Library.

| Package and selected target | Current cold median (ms) | Shared-demand diagnostic (ms) | Current / diagnostic resident bytes |
| --- | --- | --- | --- |
| Avalonia 12.1.3 / net10.0 | 286.3 | 279.6 | 14,229 / 10,133 |
| Microsoft.CodeAnalysis.CSharp 5.9.0 / netstandard2.0 | 465.4 | 412.1 | 15,111,338 / 7,556,994 |
| Newtonsoft.Json 13.0.4 / net6.0 | 328.8 | 305.2 | 1,449,149 / 725,781 |
| Dapper 2.1.66 / net8.0 | 120.7 | 120.4 | 437,579 / 437,579 |

The duplicate retained DLL disappears for Roslyn and Newtonsoft, but this
change alone does not establish the intended end-to-end improvement over the
pre-inventory baseline. The native harness also does not configure the
website's Cache Storage entry persistence. Its later missing-entry acquisition
therefore repeats the size probe and directory read instead of consuming a
cached directory. Browser-mode entry persistence must be isolated separately
before attributing that native overhead to the website. The shared range and
cache owners retain archive-validation and operation-lifetime authority.

The separate entry-store diagnostic keeps production demand unchanged and
configures a bounded in-memory implementation of the browser entry-persistence
interface. This isolates the existing acquisition branch; it does not measure
JavaScript, base64 interop, Cache Storage I/O, Wasm execution, or page paint.
Seven alternating pairs with identical complete outputs produced:

| Package | Entry persistence absent: cold median (ms) | Entry persistence configured: cold median (ms) |
| --- | --- | --- |
| Avalonia 12.1.3 | 272.2 | 205.0 |
| Microsoft.CodeAnalysis.CSharp 5.9.0 | 462.8 | 409.8 |
| Newtonsoft.Json 13.0.4 | 310.3 | 248.6 |
| Dapper 2.1.66 | 133.9 | 128.7 |

These results establish a measurement limitation and the cost of repeating the
size probe. They do not establish a missing production browser cache: the
website already configures entry persistence during host initialization.
Missing-entry reads continue to validate the fresh directory as required by
Package cache policy; this investigation changes no validation contract.

The final diagnostic comparison configures the same entry-persistence adapter
on baseline `e562f035` and on `08a1138` with shared Overview acquisition demand.
It uses seven alternating pairs after one discarded warm-up pair and the same
result-parity checks. It is an acquisition-path experiment, not a production
browser performance gate.

| Package / selected Library | Baseline cold median (ms) | Combined diagnostic cold median (ms) | Baseline / diagnostic largest cold sample (ms) |
| --- | --- | --- | --- |
| Avalonia 12.1.3 / default facade | 392.3 | 184.5 | 504.8 / 210.7 |
| Avalonia 12.1.3 / Avalonia.Base | 511.1 | 453.9 | 566.6 / 482.7 |
| Microsoft.CodeAnalysis.CSharp 5.9.0 | 455.2 | 358.4 | 515.4 / 404.8 |
| Newtonsoft.Json 13.0.4 | 206.3 | 227.0 | 243.3 / 305.1 |
| Dapper 2.1.66 | 126.2 | 125.2 | 136.4 / 134.7 |

Avalonia.Base uses the exact
`compile:ref/net10.0/Avalonia.Base.dll` selector; both API outcomes are available
and complete. Its warm median is 170.0 / 155.8 ms. The remaining default-Library
warm medians are 0.4 / 0.5 ms for Avalonia, 55.2 / 55.6 for Roslyn, 17.2 / 18.9
for Newtonsoft, and 9.0 / 9.3 for Dapper. Roslyn's unchanged projection-limit
outcome remains a bounded-result control.

The corrected comparison still does not close the performance claim:
Newtonsoft regresses even with entry reuse and one concurrent acquisition.
Its baseline Summary median is 181.3 ms followed by an already resident API;
the diagnostic Summary median is 156.2 ms, followed by approximately 75 ms of
Library work. The cached missing-entry path uses modern ranged acquisition,
but still reads a fresh directory before its DLL transfer. Eliminating that
round trip requires a separately owned range/cache proof; retaining a live
reader would incorrectly retain its expired operation deadline and credential
lifetime. This semantic-demand effort does not authorize such a shortcut.
Production adoption must also preserve Surface-only demand for standalone API
requests, collapse only declared compatible requirements through QuerySpace,
and pass actual browser measurements before claiming the intended E2E gain.

## House-owned acquisition planning

PackageHouse chooses one execution plan from the semantic query and
owner-issued capabilities and evidence. Planning may consider:

- an already settled manifest, directory, complete payload, or selected entry;
- complete-payload and entry-cache state;
- the source's manifest and range capabilities;
- advertised archive length and the package-cache size cut;
- the exact narrowing, terminal set, files, libraries, and optional namespace;
- transfer, archive, expanded-entry, request-count, and operation limits; and
- whether a complete transfer is required to preserve the semantic result.

These are planning inputs, not caller gestures. A caller cannot force a range
request, prevent a semantically required complete transfer, or request a cache
miss.

The plan may use:

- manifest-only source work;
- an already cached result;
- archive-directory work with no expanded entries;
- ranged acquisition of selected complete entries;
- a complete archive transfer; or
- ranged work followed by a typed complete-transfer fallback.

Fallback changes the transfer path, not the semantic result. Cache, ranged,
ranged-then-complete, and complete execution of the same query must return
equivalent content evidence or corresponding typed non-success. The
[package transfer receipt](package-transfer-receipt.md) records which path
occurred and why.

Size-first policy remains owned by
[package cache policy](package-cache-policy.md). Entry selection for ranged
execution remains owned by
[package read demand](package-read-demand.md). PackageHouse owns choosing and
settling the plan that composes those owner-issued policies.

## Result and receipts

One settlement preserves:

- the exact package identity or version demand;
- the exact semantic content query;
- the exact admitted central-directory snapshot identity when the query
  requires archive evidence;
- one narrowing receipt shared by every terminal result;
- the source decision and authority;
- the requested target and root-family preference chain, plus the selected
  applicable root family when present;
- owner-issued asset and namespace evidence;
- selected files or libraries and their actual package paths;
- optional complete detached file-list evidence for the base narrowed space;
- optional complete logical TFM Library inventory, exact later file
  references, and package-local PDB entry evidence;
- the acquisition and transfer receipts;
- typed fallback, no-match, unavailability, and failure evidence; and
- any live acquired content generation owned by the settlement.

The result does not infer completion from a path or file name. Every returned
file and library corresponds to one validated complete entry in the acquired
generation. A detached result may carry entry bytes or another owner-approved
detached representation, but never a live stream or borrowed generation.

## Analogous implementation evidence

NuGet.Client separates several semantic package questions:

- protocol resources can acquire a nuspec without first exposing a package
  archive;
- `PackageReaderBase.GetFiles()` inventories archive entries;
- `GetStream(path)` opens one named entry; and
- `GetLibItems()` and `GetReferenceItems()` expose framework-grouped package
  assets.

See the authoritative
[`PackageReaderBase`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageReaderBase.cs)
and
[`FindPackageByIdResource`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Resources/FindPackageByIdResource.cs)
sources.

Those APIs support the separation between manifest, inventory, exact entry,
and library questions. They do not supply this design's product-level
settlement: callers still compose framework matching, archive access, and
transport decisions. PackageHouse deliberately centralizes that composition
while continuing to consume NuGet-owned framework and source facts.

## Production adoption and retirement

The counted PackageHouse stack has seven slices:

1. Lock this focused PackageHouse semantic-demand and planning contract.
2. Put existing nuspec, file-list, exact-file, selected-asset,
   selected-Library PDB companion, and whole-archive behavior behind semantic
   queries while preserving the current direct PDB-companion handoff. Migrate
   one current production route.
3. Add reusable narrowing, Libraries, and
   `GetLibraryAndInventoryForTarget` by composing the existing asset-selection,
   correspondence, and Metadata owners. Preserve real multi-library package
   evidence.
4. After #9002 locks and implements the independent PDB settlement, add
   Library-inventory package-symbol consumption, compose later exact Files
   acquisition, and migrate one current PDB consumer. Retire direct companion
   delivery only for that adopted route.
5. Adopt `GetLibraryAndInventoryForTarget` in Inspect Web Package Query and
   the website Library list. Assembly-semantic candidate acquisition already
   enters PackageHouse through #9815; semantic content narrowing remains in
   this step.
6. Adopt the same demands in `find` and shared Workspace/declaration loading,
   removing their split acquisition behavior.
7. Migrate remaining commands and hosts, then delete
   `PackagePayloadAccess` and caller-owned range/complete fallback logic.

Package Query `skill` consumes package-wide File List evidence. Its
`tool-format` and `references` predicates reuse that same inventory to issue
one later exact Files request for only selected settings and managed-library
entries. Package Query owns predicate breadth, limits, parsing, and matching;
PackageHouse owns the shared snapshot, generation correspondence, exact-entry
admission, physical acquisition, and receipts.

Implementation slices target their predecessor and land bottom-up. A demand
arm lands only with a production caller. The first implementation slice may
lock with one adopter under the bounded first-adopter exception in
[design scope](../design-scope.md#stage-implementation-after-locking-the-design);
later adopters remain focused owner-specific slices.

The Package-first external `AssemblyRef` supplier composition tracked by
[#8466](https://github.com/richlander/dotnet-inspect/issues/8466) is another
focused consumer. It may reuse TFM-narrowed directory evidence to nominate
exact package entries and Files to acquire them. #8466 retains ownership of
cross-package reachability, Package-tier ordering, Metadata identity binding,
Package-first precedence, Platform fallback, and its closed external binding
result. It neither adds an AssemblyRef terminal here nor constructs a second
file-name index.

The production demo is a Package Query over `System.Text.Json`:
`GetLibraryAndInventoryForTarget` returns one complete
`System.Text.Json.dll` and the complete logical Library inventory for the TFM
without downloading a PDB. Each Library row records whether its applicable
implementation PDB is Listed, Absent, or Not applicable and supplies exact
references for later Files requests. A later PDB operation may request the
listed implementation/PDB files or skip to an external provider. The
neighboring multi-library case uses deterministic alphabetical selection.

The production consumer demo is member Source Locations over
`NodaTime@3.3.5`. Its single selected `lib/net8.0/NodaTime.dll` Library row
issues `lib/net8.0/NodaTime.pdb`; Portable PDB settlement requests only that
entry, validates its complete identity in Metadata, and loads the admitted
content into the existing SourceLink context.

## Pathological cases and gates

All implementation gates run in Release.

| Case | Required outcome |
| --- | --- |
| Nuspec with direct manifest capability | No archive payload acquisition; the exact manifest and source receipt settle. |
| Package-wide file list | Every admitted path appears once; no expanded entry content is opened. |
| TFM-wide file list | Every path in the owner-issued target scope appears once; unrelated target paths do not appear. |
| Ordered root preference | The first applicable family is selected from an arbitrary-length chain; a later family cannot contribute candidates or inventory. |
| Shared directory evidence | File List, exact-file admission, logical Library inventory, package-symbol evidence, and range spans derive from one validated snapshot plus owner-issued narrowing and correspondence. |
| Exact files | Ranged execution expands only the exact referenced entries; complete fallback may transfer the archive but publishes only those entries. Every reference lies in the base narrowed space and is complete and validated; an outside, missing, or ambiguous reference fails visibly. |
| `GetLibraryAndInventoryForTarget` without namespace | PackageHouse returns exactly one selected DLL, no PDB content, and one complete logical inventory whose selection receipt identifies that row. |
| `GetLibraryInventoryForTarget` | PackageHouse returns one complete logical inventory and materializes no DLL or PDB entry; a later Files query may request only an exact issued reference. |
| Reference primary plus listed implementation PDB | The selected reference DLL remains the only downloaded entry; its inventory row identifies the owner-issued implementation DLL and adjacent PDB for a later exact Files request. |
| TFM-wide package-local PDB absence | The applicable inventory row proves the adjacent implementation PDB is absent without downloading package content or consulting a symbol provider. |
| Library without implementation correspondence | Its inventory row reports Not applicable and invents neither an implementation DLL nor a PDB reference. |
| Root-narrowed composite request | `GetLibraryAndInventoryForTarget` rejects TFM-plus-root narrowing rather than publishing an incomplete logical inventory. |
| Raw File List | The result remains a physical entry inventory and does not acquire Library or PDB semantics merely because matching paths are present. |
| Several AssemblyRefs against one package generation | The consumer reuses owner-issued directory/narrowing evidence and exact Files references; Metadata validates each candidate without another archive scan or package-entry inventory. |
| Later package-symbol acquisition | The PDB operation may request the exact listed implementation/PDB entries through Files, then validates identity outside PackageHouse. |
| Later remote symbol acquisition | The PDB operation may skip the package candidate or use an external provider when package evidence is absent or disfavored by its separately owned policy. |
| Namesake evidence | Package-ID/file-name equality is reported without opening the assembly and does not alter selection order. |
| Range ignored | Complete fallback preserves the semantic result and records the typed transfer path. |
| Archive below the size cut | Complete acquisition may satisfy the demand without changing its result. |
| Cache, ranged, and complete paths | Results are equivalent apart from transfer receipts and cache-dependent evidence. |
| CLI and Browser/Wasm | Equivalent authorized requests use the same semantic query and House planning contract. |

The first design slice changes no executable behavior, so Markdown validation
is its enforcing gate. Each implementation slice names the focused Release
tests and real packages that enforce the cases it adopts.

## Non-claims

This design does not:

- define package ID, version, target framework, RID, namespace, package-path,
  or package-root grammar;
- define version selection, source authorization, framework compatibility,
  asset selection, namespace decoding, archive validation, cache policy, or
  transfer receipt issuance;
- make PackageHouse a Metadata decoder or infer namespace facts from file
  names;
- make PackageHouse issue canonical assembly identity, `PackageOwned`,
  `NameOwnedNoMatch`, or another external-supplier binding result from package
  paths, namesake evidence, File List, or Library inventory;
- make PackageHouse a ZIP parser or permit terminal-specific archive scans;
- make PackageHouse a Portable PDB decoder, identity validator, `.snupkg`
  client, or symbol-server client;
- make PackageHouse own source-candidate ordering or PDB-use policy;
- download or retain a PDB merely because
  `GetLibraryAndInventoryForTarget` or File List was requested;
- infer logical Library correspondence or package-local PDB absence from raw
  File List paths;
- require every demand to use ranged acquisition;
- promise that a file list means every listed entry is materialized;
- permit different terminals in one query to resolve different base
  narrowings;
- return partial assembly entries;
- define command syntax, output shape, rendering, or presentation;
- migrate all callers in one PR; or
- change Platform-shaped package acquisition or local-file inspection.
