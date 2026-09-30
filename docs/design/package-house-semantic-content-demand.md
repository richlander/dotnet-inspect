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

The first applicable family wins as the primary root. A later family
contributes no candidates or inventory merely because it appears later in the
preference chain.

The result terminals are:

- **Nuspec.** Return the exact package manifest without acquiring archive
  payload when the authorized source supplies it directly. This terminal
  requires package-wide narrowing.
- **File list.** Return the complete validated archive entry inventory without
  expanding entry content, projected to the base narrowed package space.
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
describe the same base narrowed TFM/root space. Neither terminal repeats or
independently interprets the narrowing.

One implementation slice adds a narrowing form or terminal only with a
production caller. Until one lands, its place in this vocabulary is an
adoption commitment, not a supported API.

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

The snapshot is the singular basis for:

- the package-wide entry inventory;
- TFM-wide and ordered-root narrowing;
- exact-file existence and ambiguity checks;
- library candidate paths, namesake evidence, and alphabetical ordering;
- selected-Library implementation and adjacent-PDB candidate evidence;
- the detached File List terminal;
- entry offsets, compressed and expanded lengths, and compression facts used
  by ranged-entry planning; and
- the entry identities used to query which content the entry cache already
  holds.

PackageHouse resolves the base narrowing once against that snapshot. Library
selection consumes that candidate space. When File List accompanies a
library-returning terminal, PackageHouse derives any selected-Library
package-symbol evidence from owner-issued implementation correspondence and the
same snapshot. A terminal cannot rescan the archive, construct a second path
inventory, or resolve root preference independently.

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
- is complete for the query's base narrowed package space;
- carries no stream, payload generation, cache handle, or source authority;
- identifies which exact files a later request may name under a compatible
  narrowing; and
- does not imply that any listed entry content was materialized.

When File List accompanies Libraries or Best Library, it also carries typed
package-symbol evidence for each selected Library. That evidence binds the
owner-issued selected Library and implementation correspondence to the same
directory snapshot and reports one of:

- **Listed.** The narrowed inventory contains the exact applicable
  implementation assembly and its same-directory, same-file-name-stem `.pdb`.
- **Absent.** The narrowed inventory completely covers the applicable
  implementation location and contains no such PDB.
- **Not covered.** The narrowing does not cover the applicable implementation
  location, so the file list cannot claim package-local PDB absence.
- **Not applicable.** The selected Library has no implementation
  correspondence for which package-local symbols can be requested.

Listed proves that the package directory contains the named entry. It does not
prove Portable PDB format, identity, readability, or applicability to the
assembly.

A TFM-wide File List is the ordinary complete package-symbol discovery view
for one target. A runtime target also carries its RID. Package-wide inventory
is complete but broader than needed. A TFM-plus-root File List proves only its
winning root; for example, `ref` inventory cannot claim that an implementation
PDB is absent from `lib` or `runtimes`.

PackageHouse may already need the archive directory to plan a ranged
acquisition. It publishes that directory as file-list evidence only when the
query asks for it, and projects it through the exact narrowing receipt before
publication. An internal planning read does not silently enlarge the product
result or become package-symbol evidence.

A selected file or library entry is always complete. PackageHouse returns the
whole validated ZIP entry or a typed non-success; it never returns a partial
assembly and labels it acquired. The file list explains what else the package
contains and can be requested next. The settlement and transfer receipt, not
the file list, establish that the returned entry completed validation.

## Package-local PDB evidence

Best Library without a namespace plus File List uses directory evidence to
select one Library and materializes only that selected entry. Namespace
selection may materialize the candidate assemblies required to prove the first
exact match, as described above. In either form, PackageHouse does not download
a Portable PDB or an implementation assembly merely because the file list
reports a package-local symbol candidate.

The base narrowing selects the primary Library. The package
asset-selection and correspondence owners may associate that Library with an
implementation assembly outside the winning primary root. PackageHouse
preserves that correspondence in the package-symbol evidence. If the selected
Library is a reference assembly, Listed evidence may therefore identify an
implementation DLL and adjacent PDB that are both still directory-only. If the
selected Library is already the implementation assembly, a later request needs
only the listed PDB entry.

PackageHouse does not open the PDB, validate Portable PDB format or identity,
inspect embedded PDB content, or consult `.snupkg` or symbol-server sources.
The [PDB acquisition owner](../pdb-acquisition.md#pdb-location-strategy)
may perform those later operations only when a separate downstream source or
PDB query requests them.

That downstream PDB operation receives the authoritative selected-assembly
reference and optional package-symbol evidence. It may request the exact listed
implementation DLL and PDB through a PackageHouse Files query or skip directly
to an applicable external provider. Exact package files are complete and
validated as ZIP entries; the PDB owner then validates Portable PDB format and
assembly identity before publishing bytes to the verified PDB store.

If the original request omitted File List, the downstream operation has no
package-local presence or absence claim. The caller may issue a later File List
query or permit the PDB operation to skip to external providers. PackageHouse
does not perform hidden inventory or PDB acquisition merely because another
component asks for symbols.

The separately focused PDB-settlement owner, tracked by
[#9002](https://github.com/richlander/dotnet-inspect/issues/9002), defines
verified-store reuse, external-provider ordering, and negative acquisition
observations. That contract must keep definitive provider absence distinct
from operational or policy failure. This document does not define when Listed
package evidence wins over an external provider, or the observation key,
expiry, or provider retry algorithm.

This boundary has distinct authorities:

- PackageHouse owns complete narrowed File List evidence, selected-Library
  package-symbol evidence, and later exact-file delivery.
- The selected Library's owner-issued assembly reference is authoritative for
  which assembly needs symbols; Metadata identity checks admit any PDB before
  use.
- The host-neutral PDB settlement service owns embedded, verified-store,
  package-companion, and external-provider composition. Existing
  `PdbAcquisitionService` is its external-acquisition substrate.
- Hosts own capability construction, network permission, stores, limits,
  cancellation, and offline/cache-only policy.
- Product source composition owns whether a source request invokes that
  capability and how authored and decompiled candidates are ordered.

The current [SourceHouse](source-house.md) contract, tracked by
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512), still
defines supplied companion or embedded PDB input and a separately authorized
external-acquisition capability. `AssemblyContextSourceQuery` and legacy CLI
wrappers still orchestrate parts of that sequence. #9002 must explicitly
reconcile and transfer that PDB-input contract before SourceHouse consumes the
new settlement. Existing direct PackageHouse companion delivery therefore
remains transitional until the independent PDB settlement and one production
consumer adopt File List package-symbol evidence. Non-source consumers,
including PDB Source Provenance and decompilation or analysis, may then consume
the same PDB settlement without routing through SourceHouse.

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
- optional complete detached file-list evidence for the base narrowed space,
  including selected-Library package-symbol evidence when a library terminal
  accompanies it;
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
3. Add reusable narrowing, Libraries, and Best Library by composing the
   existing asset-selection and Metadata owners. Preserve real multi-library
   package evidence.
4. After #9002 locks and implements the independent PDB settlement, add
   selected-Library package-symbol evidence to Best Library plus TFM-wide File
   List, compose later exact Files acquisition, and migrate one current PDB
   consumer. Retire direct companion delivery only for that adopted route.
5. Adopt Best Library and composable file-list evidence in Inspect Web Package
   Query, beginning with assembly-semantic evaluation.
6. Adopt the same demands in `find` and shared Workspace/declaration loading,
   removing their split acquisition behavior.
7. Migrate remaining commands and hosts, then delete
   `PackagePayloadAccess` and caller-owned range/complete fallback logic.

Implementation slices target their predecessor and land bottom-up. A demand
arm lands only with a production caller. The first implementation slice may
lock with one adopter under the bounded first-adopter exception in
[design scope](../design-scope.md#stage-implementation-after-locking-the-design);
later adopters remain focused owner-specific slices.

The production demo is a Package Query over `System.Text.Json`: Best Library
without a namespace plus a TFM-wide File List returns one complete
`System.Text.Json.dll` and the complete detached target inventory without
downloading a PDB. The file list records whether a package-local
implementation/PDB candidate is Listed, Absent, Not covered, or Not
applicable. A later PDB operation may request the exact listed files or skip to
an external provider. The neighboring multi-library case uses `Avalonia`, an
arbitrary ordered root chain, exact namespace selection, and deterministic
alphabetical fallback for an absent namespace.

## Pathological cases and gates

All implementation gates run in Release.

| Case | Required outcome |
| --- | --- |
| Nuspec with direct manifest capability | No archive payload acquisition; the exact manifest and source receipt settle. |
| Package-wide file list | Every admitted path appears once; no expanded entry content is opened. |
| TFM-wide file list | Every path in the owner-issued target scope appears once; unrelated target paths do not appear. |
| Ordered root preference | The first applicable family is selected from an arbitrary-length chain; a later family cannot contribute candidates or inventory. |
| Shared directory evidence | File List, exact-file admission, library candidates, package-symbol evidence, and range spans derive from one validated snapshot plus owner-issued narrowing and correspondence. |
| Exact files | Every explicitly requested file lies in the base narrowed space and is complete and validated; an outside, missing, or ambiguous path fails visibly. |
| Best Library without namespace plus TFM-wide File List | PackageHouse returns exactly one selected DLL, no PDB content, complete target inventory, and typed package-symbol evidence for the selected Library. |
| Reference primary plus listed implementation PDB | The selected reference DLL remains the only downloaded entry; File List evidence identifies the owner-issued implementation DLL and adjacent PDB for a later exact Files request. |
| TFM-wide package-local PDB absence | Complete target inventory proves the applicable adjacent PDB is absent without downloading package content or consulting a symbol provider. |
| Root-narrowed File List | Inventory outside the winning root is Not covered; PackageHouse cannot report package-local PDB absence there. |
| No File List | The Library remains usable, no PDB is downloaded, and the result carries no package-local PDB presence or absence claim. |
| Later package-symbol acquisition | The PDB operation may request the exact listed implementation/PDB entries through Files, then validates identity outside PackageHouse. |
| Later remote symbol acquisition | The PDB operation may skip the package candidate or use an external provider when package evidence is absent, not covered, or disfavored by its separately owned policy. |
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
- download or retain a PDB merely because Best Library or File List was
  requested;
- infer package-local PDB absence from a File List whose narrowing does not
  cover the applicable implementation location;
- require every demand to use ranged acquisition;
- promise that a file list means every listed entry is materialized;
- permit different terminals in one query to resolve different base
  narrowings;
- return partial assembly entries;
- define command syntax, output shape, rendering, or presentation;
- migrate all callers in one PR; or
- change Platform-shaped package acquisition or local-file inspection.
