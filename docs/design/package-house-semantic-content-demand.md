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
reconstruct archive selectors. PackageHouse preserves the semantic demand
through planning, execution, typed fallback, settlement, and receipts.

The semantic demand is separate from the existing package identity or version
demand. One request therefore retains both:

- which package is requested; and
- which package evidence is required.

Neither value is reconstructed from the other or from display text.

## Content demands

The PackageHouse content vocabulary is:

- **Nuspec.** Return the exact package manifest without acquiring archive
  payload when the authorized source supplies it directly.
- **File list.** Return the complete validated archive entry inventory without
  expanding entry content.
- **Files.** Return the complete validated content of one or more exact package
  entries.
- **Libraries.** Return every compatible library selected for one target
  context and requested package root family: `ref`, `lib`, or `runtimes`.
  Runtime selection carries the required RID.
- **Best Library.** Return one compatible library selected by the rule in
  [Best Library](#best-library).
- **Whole archive.** Return the complete package payload when the product
  question genuinely requires package-wide content.

One implementation slice adds a demand arm only with a production caller.
Until an arm lands, its place in this vocabulary is an adoption commitment,
not a supported API.

The content demand does not contain an access mode, range selector, cache
instruction, source URL, or fallback preference. Network permission, source
authorization, transfer limits, cache capacity, and operation deadlines remain
host-supplied capabilities and policy.

## File-list evidence

A request may ask for the complete detached file list together with another
content demand. File-list evidence is therefore composable rather than a
mutually exclusive payload mode.

The file list:

- preserves every validated package entry path and declared expanded length;
- is complete for the admitted archive directory;
- carries no stream, payload generation, cache handle, or source authority;
- identifies which exact files a later request may name; and
- does not imply that any listed entry content was materialized.

PackageHouse may already need the archive directory to plan a ranged
acquisition. It publishes that directory as file-list evidence only when the
request asks for it. An internal planning read does not silently enlarge the
product result.

A selected file or library entry is always complete. PackageHouse returns the
whole validated ZIP entry or a typed non-success; it never returns a partial
assembly and labels it acquired. The file list explains what else the package
contains and can be requested next. The settlement and transfer receipt, not
the file list, establish that the returned entry completed validation.

## Best Library

`GetBestLibrary` is the only single-library selection demand. There is no
separate namesake-library request.

The demand carries a target context, requested package root family, and an
optional namespace. It consumes compatible library candidates from the
package asset-selection owner and namespace facts from the Metadata owner.
PackageHouse composes those owner-issued facts; it does not parse target
frameworks, rank asset compatibility, or decode Metadata itself.

Selection is deterministic:

1. When a namespace is supplied, retain compatible libraries whose owner-issued
   namespace inventory contains that exact namespace.
2. If one or more libraries remain, select the alphabetically first library.
3. If no library contains the namespace, select the alphabetically first
   compatible library.

Alphabetical order compares assembly simple name using ordinal
case-insensitive ordering, then normalized package path using ordinal
case-insensitive ordering. The path tie-break makes distinct libraries with
the same simple name deterministic.

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

PackageHouse chooses one execution plan from the semantic demand and
owner-issued capabilities and evidence. Planning may consider:

- an already settled manifest, directory, complete payload, or selected entry;
- complete-payload and entry-cache state;
- the source's manifest and range capabilities;
- advertised archive length and the package-cache size cut;
- the exact file, library, target, root-family, and optional namespace demand;
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
ranged-then-complete, and complete execution of the same demand must return
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
- the exact semantic content demand;
- the source decision and authority;
- the selected target and package root family when applicable;
- owner-issued asset and namespace evidence;
- selected files or libraries and their actual package paths;
- optional complete detached file-list evidence;
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
2. Put existing nuspec, file-list, exact-file, selected-asset, and
   whole-archive behavior behind semantic demands. Migrate one current
   production route while preserving output.
3. Add Libraries and Best Library by composing the existing asset-selection
   and Metadata owners. Preserve real multi-library package evidence.
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

The production demo is a Package Query over `System.Text.Json`: PackageHouse
returns the selected complete `System.Text.Json.dll` entry and the complete
detached package file list without the Browser choosing an acquisition mode.
The neighboring multi-library case uses `Avalonia` and proves exact namespace
selection plus deterministic alphabetical fallback for an absent namespace.

## Pathological cases and gates

All implementation gates run in Release.

| Case | Required outcome |
| --- | --- |
| Nuspec with direct manifest capability | No archive payload acquisition; the exact manifest and source receipt settle. |
| File list | Every admitted path appears once; no expanded entry content is opened. |
| Exact files | Every returned entry is complete and validated; a missing or ambiguous path fails visibly. |
| File list plus selected library | One settlement carries the complete directory and one complete selected assembly entry. |
| Namespace in several libraries | The alphabetically first exact namespace match wins. |
| Namespace absent | The alphabetically first compatible library wins. |
| Namesake evidence | Package-ID/file-name equality is reported without opening the assembly and does not alter selection order. |
| Namespace evidence failure | Failure remains visible; it cannot become alphabetical fallback. |
| Range ignored | Complete fallback preserves the semantic result and records the typed transfer path. |
| Archive below the size cut | Complete acquisition may satisfy the demand without changing its result. |
| Cache, ranged, and complete paths | Results are equivalent apart from transfer receipts and cache-dependent evidence. |
| CLI and Browser/Wasm | Equivalent authorized requests use the same semantic demand and House planning contract. |

The first design slice changes no executable behavior, so Markdown validation
is its enforcing gate. Each implementation slice names the focused Release
tests and real packages that enforce the cases it adopts.

## Non-claims

This design does not:

- define package ID, version, target framework, RID, namespace, or package-path
  grammar;
- define version selection, source authorization, framework compatibility,
  asset selection, namespace decoding, archive validation, cache policy, or
  transfer receipt issuance;
- make PackageHouse a Metadata decoder or infer namespace facts from file
  names;
- require every demand to use ranged acquisition;
- promise that a file list means every listed entry is materialized;
- return partial assembly entries;
- define command syntax, output shape, rendering, or presentation;
- migrate all callers in one PR; or
- change Platform-shaped package acquisition or local-file inspection.
