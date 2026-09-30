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
- which evidence terminals and companion modifiers run over the base narrowing.

None of these values is reconstructed from another or from display text.

## Content query

One `PackageHouseContentQuery` carries one narrowing and one or more result
terminals. Every terminal in the query observes the exact same base narrowed
package space and retains the same owner-issued narrowing receipt. A
library-returning terminal may additionally request the selected-Library
companion closure defined below.

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

The first applicable family wins as the primary root. A later family
contributes no candidates merely because it appears later in the preference
chain. Exact entries reached through owner-issued selected-Library
correspondence are companion closure, not another selected root.

The result terminals are:

- **Nuspec.** Return the exact package manifest without acquiring archive
  payload when the authorized source supplies it directly. This terminal
  requires package-wide narrowing.
- **File list.** Return the complete validated archive entry inventory without
  expanding entry content, projected to the effective content space.
- **Files.** Return the complete validated content of one or more exact package
  entries within the base narrowed space.
- **Libraries.** Return every compatible library in the base narrowed space.
- **Best Library.** Return one compatible library selected by the rule in
  [Best Library](#best-library).
- **Whole archive.** Return the complete package payload when the product
  question genuinely requires package-wide content. This terminal requires
  package-wide narrowing.

A query may request several compatible terminals. In particular, Best Library
and File List may run together. The selected assembly and file list then
describe the same effective content space: the base narrowed TFM/root space
plus any exact selected-Library companion closure. Neither terminal repeats or
independently interprets the narrowing or closure.

One implementation slice adds a narrowing form, terminal, or companion
modifier only with a production caller. Until one lands, its place in this
vocabulary is an adoption commitment, not a supported API.

The content query does not contain an access mode, range selector, cache
instruction, source URL, or fallback preference. Network permission, source
authorization, transfer limits, cache capacity, and operation deadlines remain
host-supplied capabilities and policy.

## ZIP central-directory snapshot

Except for a nuspec request satisfied directly by the authorized source,
archive-backed content planning begins from one immutable, validated ZIP
central-directory snapshot. The archive reader owns its construction and ZIP
validation. PackageHouse retains its exact identity and composes its evidence;
it does not independently parse ZIP structures.

The snapshot is the singular basis for:

- the package-wide entry inventory;
- TFM-wide and ordered-root narrowing;
- exact-file existence and ambiguity checks;
- library candidate paths, namesake evidence, and alphabetical ordering;
- selected-Library implementation and adjacent-PDB companion paths;
- the detached File List terminal;
- entry offsets, compressed and expanded lengths, and compression facts used
  by ranged-entry planning; and
- the entry identities used to query which content the entry cache already
  holds.

PackageHouse resolves the base narrowing once against that snapshot. Library
selection consumes that candidate space. PackageHouse then derives any
requested selected-Library companion closure from owner-issued correspondence
and the same snapshot. A terminal cannot rescan the archive, construct a
second path inventory, resolve root preference independently, or widen a
companion to its entire root.

Namesake evidence and alphabetical ordering require only directory paths.
Namespace selection visits candidate assemblies from the narrowed space in
that deterministic order and stops at the first exact match. If namespace
evidence for an earlier candidate cannot be completed, the query fails visibly
because the House cannot prove that a later candidate is the first match.

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
- is complete for the query's effective content space: the base narrowed
  package space plus exact selected-Library companion entries;
- carries no stream, payload generation, cache handle, or source authority;
- identifies which exact files a later request may name when that later
  query's narrowing or companion closure admits them; and
- does not imply that any listed entry content was materialized.

PackageHouse may already need the archive directory to plan a ranged
acquisition. It publishes that directory as file-list evidence only when the
query asks for it, and projects it through the exact narrowing and companion
receipts before publication. An internal planning read does not silently
enlarge the product result.

A selected file or library entry is always complete. PackageHouse returns the
whole validated ZIP entry or a typed non-success; it never returns a partial
assembly and labels it acquired. The file list explains what else the package
contains and can be requested next. The settlement and transfer receipt, not
the file list, establish that the returned entry completed validation.

## Selected-Library companion closure

Libraries and Best Library may request the existing
`ImplementationPortablePdb` selected-Library companion demand. This is a
modifier on a library-returning terminal, not a general PDB terminal.

The base narrowing selects each primary Library candidate. The package
asset-selection and correspondence owners may associate that Library with an
implementation assembly outside the winning primary root. PackageHouse
preserves that correspondence and derives the implementation assembly's
same-directory, same-file-name-stem `.pdb` path from the central-directory
snapshot.

The effective content space then adds only:

- the exact owner-issued implementation assembly when it is not already in the
  base narrowed space; and
- its exact adjacent Portable PDB entry when the directory lists it.

This closure does not select the implementation entry's root family, add its
neighbors, or make a later preference-chain family contribute candidates. A
simultaneous File List contains the complete base narrowed inventory plus these
exact closure entries, not the complete companion root.

The implementation assembly remains the ranged-read block anchor under
[package read demand](package-read-demand.md#selected-library-companion-demand).
The PDB is an optional exact entry and never another anchor. An absent,
directory-only, unreadable, or limit-omitted PDB does not fail an otherwise
valid Library result; the typed omission remains visible. A Library without an
implementation counterpart does not invent one or a PDB.

The companion demand is package-local and never dispatches a `.snupkg` or
symbol-server request. PackageHouse may satisfy it from retained package
content, the entry cache, or an authorized package-source transfer. A valid
cached package entry avoids that transfer. If the central-directory snapshot
does not list the companion, PackageHouse settles typed absence without trying
another symbol provider.

PackageHouse does not open the PDB, validate Portable PDB format or identity,
inspect embedded PDB content, or consult `.snupkg` or symbol-server sources.
The [PDB acquisition owner](../pdb-acquisition.md#pdb-location-strategy)
may perform those later operations only when a separate downstream source or
PDB query requests them. That operation consumes embedded or supplied
package-local content and valid PDB-store entries before an applicable remote
route. A valid cache hit skips that route. Invalid, mismatched, or unreadable
cached content remains visible and may continue to another authorized provider
unless the operation is cache-only or offline. The downstream operation
preserves its independent source authority, limits, and identity checks.

This boundary has distinct authorities:

- PackageHouse owns package-local companion delivery and its omission receipt.
- The selected Library's owner-issued assembly reference and Metadata identity
  checks authorize assembly/PDB correspondence.
- `PdbAcquisitionService` owns matching external-PDB acquisition mechanics.
- Hosts own capability construction, network permission, stores, limits,
  cancellation, and offline/cache-only policy.
- Product source composition owns whether a source request invokes that
  capability and how authored and decompiled candidates are ordered.

The last authority is transitional today:
`AssemblyContextSourceQuery` and legacy CLI wrappers still orchestrate parts of
the embedded, adjacent, and external sequence. The target
[SourceHouse](source-house.md), tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512), consumes
PackageHouse-supplied Library and PDB content first, owns PDB-use policy and
authored-source candidate ordering, and invokes PDB acquisition only through
an explicit host-authorized capability. This design requires that typed
handoff but does not redefine SourceHouse or PDB-acquisition policy.

## Best Library

`GetBestLibrary` is the only single-library selection demand. There is no
separate namesake-library request.

The terminal carries an optional namespace. Its target context and ordered
root-family preference come only from the shared narrowing query. It consumes
compatible library candidates from that narrowed space and namespace facts
from the Metadata owner. PackageHouse composes those owner-issued facts; it
does not parse target frameworks, rank asset compatibility, resolve the root
preference from paths, or decode Metadata itself.

Selection is deterministic:

1. Order compatible libraries alphabetically.
2. When a namespace is supplied, visit libraries in that order and select the
   first whose owner-issued namespace inventory contains that exact namespace.
3. If the namespace is absent from every library, or no namespace was supplied,
   select the first compatible library in that order.

Alphabetical order compares the assembly file-name stem from the validated
package path using ordinal case-insensitive ordering, then compares normalized
package path using ordinal case-insensitive ordering. It does not require the
assembly manifest name, which may differ from the file name. The path tie-break
makes distinct libraries with the same file-name stem deterministic.

A package namesake is a pure name fact: `System.Text.Json.dll` is namesake
evidence for package `System.Text.Json`. It is derived from the package ID and
assembly file name without opening or decoding the assembly. The selection
receipt may disclose that fact, but namesake status does not create another
demand or override the namespace-then-alphabetical rule.

Missing compatible libraries is a typed no-match. Namespace absence is not a
failure because the alphabetical fallback is part of the request. Metadata
decode failure is visible and cannot be treated as namespace absence; the
House cannot select a fallback on incomplete namespace evidence.

## House-owned acquisition planning

PackageHouse chooses one execution plan from the semantic query and
owner-issued capabilities and evidence. Planning may consider:

- an already settled manifest, directory, complete payload, or selected entry;
- complete-payload and entry-cache state;
- the source's manifest and range capabilities;
- advertised archive length and the package-cache size cut;
- the exact narrowing, terminal set, files, libraries, optional namespace, and
  selected-Library companion demand;
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
- the requested selected-Library companion demand and exact companion-closure
  receipt when present;
- the source decision and authority;
- the requested target and root-family preference chain, plus the selected
  applicable root family when present;
- owner-issued asset and namespace evidence;
- selected files or libraries and their actual package paths;
- optional complete detached file-list evidence for the effective content
  space;
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

The counted stack has six slices:

1. Lock this focused PackageHouse semantic-demand and planning contract.
2. Put existing nuspec, file-list, exact-file, selected-asset,
   selected-Library PDB companion, and whole-archive behavior behind semantic
   queries. Migrate one current production route while preserving output.
3. Add reusable narrowing, Libraries, and Best Library by composing the
   existing asset-selection and Metadata owners. Preserve real multi-library
   package evidence.
4. Adopt Best Library and composable file-list evidence in Inspect Web Package
   Query, beginning with assembly-semantic evaluation.
5. Adopt the same demands in `find` and shared Workspace/declaration loading,
   removing their split acquisition behavior.
6. Migrate remaining commands and hosts, then delete
   `PackagePayloadAccess` and caller-owned range/complete fallback logic.

Implementation slices target their predecessor and land bottom-up. A demand
arm lands only with a production caller. The first implementation slice may
lock with one adopter under the bounded first-adopter exception in
[design scope](../design-scope.md#stage-implementation-after-locking-the-design);
later adopters remain focused owner-specific slices.

The production demo is a Package Query over `System.Text.Json`: one TFM plus
`ref || lib` narrowing returns the selected complete `System.Text.Json.dll`
entry and the complete detached file list for that same effective content
space, without the Browser choosing an acquisition mode. When the companion
modifier is present, a listed implementation PDB appears as one exact closure
entry; absence is a typed omission and does not start symbol acquisition. The
neighboring multi-library case uses `Avalonia`, an arbitrary ordered root
chain, exact namespace selection, and deterministic alphabetical fallback for
an absent namespace.

## Pathological cases and gates

All implementation gates run in Release.

| Case | Required outcome |
| --- | --- |
| Nuspec with direct manifest capability | No archive payload acquisition; the exact manifest and source receipt settle. |
| Package-wide file list | Every admitted path appears once; no expanded entry content is opened. |
| TFM-wide file list | Every path in the owner-issued target scope appears once; unrelated target paths do not appear. |
| Ordered root preference | The first applicable family is selected from an arbitrary-length chain; a later family cannot contribute candidates or whole-root inventory. Exact owner-issued companion closure does not select that family. |
| Shared directory evidence | File List, exact-file admission, library candidates, companion closure, and range spans derive from one validated snapshot plus narrowing and closure receipts. |
| Exact files | Every explicitly requested file lies in the base narrowed space and is complete and validated; an outside, missing, or ambiguous path fails visibly. |
| File list plus selected library | One narrowing receipt and any exact closure receipt govern the complete effective file list and selected Library content. |
| Reference primary plus implementation PDB | The reference root remains primary; only the owner-issued implementation assembly and listed adjacent PDB enter companion closure. The rest of the implementation root remains absent. |
| Missing or unmaterialized package-local PDB | The Library remains usable and the exact typed omission is visible; PackageHouse does not consult another symbol source. |
| Cached package-local PDB | PackageHouse returns the valid retained or entry-cached companion without a package-source request. |
| Later remote symbol acquisition | It occurs only for a separate authorized downstream PDB/source query after embedded, supplied package-local, and applicable valid store content do not answer it. |
| SourceHouse adoption | Supplied PackageHouse PDB content is consumed before an explicitly authorized external-PDB capability; hosts do not recreate candidate order. |
| Namespace in several libraries | The first exact namespace match in file-name-stem and package-path order wins. |
| Namespace absent | The alphabetically first compatible library wins. |
| Namesake evidence | Package-ID/file-name equality is reported without opening the assembly and does not alter selection order. |
| Namespace evidence failure | Failure remains visible; it cannot become alphabetical fallback. |
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
- make PackageHouse a ZIP parser or permit terminal-specific archive scans;
- make PackageHouse a Portable PDB decoder, identity validator, `.snupkg`
  client, or symbol-server client;
- make PackageHouse own source-candidate ordering or PDB-use policy;
- require every demand to use ranged acquisition;
- promise that a file list means every listed entry is materialized;
- permit different terminals in one query to resolve different base narrowings
  or companion closures;
- return partial assembly entries;
- define command syntax, output shape, rendering, or presentation;
- migrate all callers in one PR; or
- change Platform-shaped package acquisition or local-file inspection.
