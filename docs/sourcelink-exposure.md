# SourceLink Exposure

SourceLink is not a first-class artifact like a package manifest or assembly
metadata table. It is source provenance hidden behind debug symbols, so its CLI
home is less obvious. This document records where dotnet-inspect should expose
SourceLink, how those surfaces depend on PDBs, and how network work is bounded
so the tool stays fast by default.

This follows the section-bias direction from
[issue #1163](https://github.com/richlander/dotnet-inspect/issues/1163):
SourceLink inventories belong on `package`, `library`, `type`, and dedicated
member source sections. The standalone `source` command has been removed.

## Model

SourceLink answers three related questions:

| Question | Best home |
| --- | --- |
| Does this binary have trustworthy source provenance? | `library` / `package` `Signals`, `Symbols`, and `SourceLink *` sections |
| Which source files map to this target? | `SourceLink: Files` (`library` / `package`) / `Source Files` (`type`) |
| Where do these member signatures live in source? | A dedicated member `Source Locations` section for file/URL/line when a verified PDB is available |
| What is the source for this exact member or IL offset? | selected `member` source sections, or `library --il-offset <token>+<offset>` for MethodDef token + IL offset point queries |

The command model should prefer sections over new flags. SourceLink URL listings
are document sections, not standalone verbs. Point queries, such as method-token
plus IL offset symbolication, use flags to supply query input while section
selection controls rendering.

## Current product surfaces

### Library

`library` is the natural home for assembly-level SourceLink evidence.

| Section | Purpose | Network posture |
| --- | --- | --- |
| `Symbols` | PDB format/location, SourceLink presence, symbol server, builder hints | may acquire one missing PDB when authorized |
| `Signals` | summary evidence including SourceLink map presence and usability | opt-in; may acquire one missing PDB |
| `SourceLink: Diagnostics` | local map parse errors and rejected document mappings | network-free |
| `SourceLink: Files` | type-to-SourceLink URL rows for the selected library | opt-in; may acquire one missing PDB |
| `SourceLink: Availability` | per-source-file reachability via HTTP HEAD | opt-in; one request per source file |
| `SourceLink: Missing Files` | compiler source paths that are neither reachable nor embedded | opt-in; derived from availability pass |
| `SourceLink: Integrity` | downloads source bodies and checks PDB checksums | opt-in; slowest and exits non-zero on mismatch |

Plain `library -D` may open an embedded PDB because it is already part of the
named carrier, but it caps decompression at 64 MiB and does not probe an
adjacent PDB or acquire symbols. This lets discovery advertise the
`@SourceLink` door for embedded maps without performing network work.
`LibraryCommand_Discover_AdvertisesEmbeddedSourceLinkDoor` gates the positive
carrier case, `LibraryCommand_Discover_BoundsEmbeddedPdbExpansion` gates the
decompression bound, and
`LibraryPipeline_SourceLinkFamily_NotDiscoverableWithoutSourceLink` gates the
close negative.

### Package

`package` owns package-level aggregation. It should not force users to pivot to
another command merely to continue a package inspection.

| Section | Purpose |
| --- | --- |
| `Signals` | package and dependency evidence, plus binary/source provenance summaries |
| `Audit: Findings` | network-free findings from decoded SourceLink map text and text-bearing package files |
| `SourceLink: Files` | SourceLink URL rows aggregated from package libraries, with library provenance |
| `SourceLink: Availability` | aggregate reachability and embedded-source coverage across selected package libraries |
| `SourceLink: Missing Files` | unreachable source paths plus unavailable/failed library rows, with package-library provenance |
| `SourceLink: Integrity` | aggregate checksum verification across selected package libraries |

The package SourceLink sections default to the same library selection rules as
other package-library views: compatible/highest TFM unless a TFM selector says
otherwise. This keeps the sections scoped and avoids multiplying work across
every asset group by default.

The package and library availability, missing-file, and integrity sections bind
to the same typed queries. Package owns only asset selection, aggregation, and
library provenance. `SourceLink: Missing Files` is a second view of the
availability result, not a second network pass. All four package sections are
rooted in `@SourceLink`; the legacy `Source Files` spelling still resolves for
the file listing. `type` still spells its equivalent `Source Files`.

Effective discovery never executes the unbounded availability or integrity
queries merely to list these package sections.

`Audit: Findings` opens only package-local portable PDBs and embedded PDBs from
package-managed `.dll` and `.exe` files. It consumes the SourceLink owner's
decoded mapping inventory, so JSON escape sequences are audited as their
semantic Unicode values without creating a second map parser. A standalone PDB
is inspected as authored package content without claiming assembly identity;
identity remains mandatory for method/document correspondence. The audit also
emits a review-oriented finding for every literal `../` in a decoded document
key or URL. That finding is intentionally suspiciousness, not a maliciousness
verdict. Embedded-PDB inflation and SourceLink map byte/mapping materialization
are caller-bounded before allocation, including a shared decompression budget
across package carriers. Query hosts likewise apply their symbol-acquisition
per-PDB and aggregate expansion limits before a query-owned embedded PDB is
opened. The audit does not acquire PDBs or contact SourceLink URLs.

### Type and member

Selected member output already exposes source evidence:

| Section | Purpose |
| --- | --- |
| `PDB Source` | Portable-PDB-selected, checksum-verified source text acquired locally or through SourceLink for one selected overload |
| `Source Locations` | file/URL/line rows for a member group or selected signature |
| `Decompiled Source` | readable C# reconstructed from IL |
| `Annotated Source` | C# plus hidden-fact comments and IL evidence |
| `IL` | raw IL disassembly |

`PDB Source` means that the returned bytes match the Portable PDB's document
checksum and were selected through its source coordinates. Neither the checksum
nor SourceLink origin attribution independently proves that those bytes were
the physical syntax tree that produced a MethodDef.

Single-type `Source Files` is the natural section home for type-to-URL rows when
a user is already in a `type` flow. Printing a source body from `Source Files`
or `Source Locations` requires a usable portable-PDB checksum. A missing or
unsupported checksum is a visible failure rather than permission to render
unverified network content.

Member source locations should not be added to `Member Index`. That section must
stay focused on the query pattern: terse selectors, stable selectors, and
canonical signatures. SourceLink file/URL/line belongs in a separate `Source*`
section, currently described as `Source Locations`.

`Source Locations` should exist on both member-group and selected-signature
views so the experience progressively narrows. A member-group view can show one
row per overload with enough selector/signature context to identify the row. A
selected-signature view can show the same file/URL/line evidence for one
signature without fetching the full `PDB Source` body.

### Removed source command

The former `source` command has been folded into host-command sections and point
queries. Issue [#1163](https://github.com/richlander/dotnet-inspect/issues/1163)
records the removal path: source inventories became `Source Files` sections,
source-body retrieval follows selected-member `PDB Source` / package
content patterns, availability checks live in `SourceLink: Integrity` and
`SourceLink: Availability`, URL shape is selected with `--blob`, and IL offset
symbolication is now `library --il-offset <token>+<offset>`, which supplies the
value for the `Context: Source Location` section.

Sample URLs are less direct: they should be URL rows from real package or
documentation metadata rather than calculated links, because some sample URL
schemes are odd and some application firewalls block model-constructed URLs.
Printing a table of sample names and URLs is the useful default; fetching or
printing sample source bodies is overkill and should be avoided or removed.

### Adjacent member documentation and safety sections

Not every source-adjacent member feature is SourceLink. Member signatures should
also have sections for documentation and safety evidence:

| Section | Purpose |
| --- | --- |
| `Documentation` | the full XML documentation comment (`///`) block for a selected member signature |
| `Signals` | signature-level evidence such as visibility, safety documentation presence, and safety classification |
| `Samples` | sample name/description plus URL rows when package/docs metadata can provide trustworthy links |

`Signals` can report visibility (`private`, `protected`, `internal`, `public`)
and a safety row such as `safe`, `unsafe boundary`, or `unsafe`. Safety comments
should come from the documentation block, not SourceLink URL inference.

`--bare` is the right presentation modifier when a caller wants section content
without a heading, table, or Markdown decoration. It supports type/member code
sections such as `Decompiled Source`, `Annotated Source`, `PDB Source`, and
`IL`, one-column SourceLink URL output such as `Source Locations`, and package
README/content payloads. It does not change the selected shape; it simply strips
framing from an already-selected payload. `--count` remains the reduction that
collapses a selected section to a single row count, and `--raw`/`--blob` remain
the URL-shape pair for emitted GitHub links.

## PDB dependency

SourceLink requires a readable portable PDB. The assembly alone is not enough.
PDB acquisition proceeds in this order:

1. Embedded portable PDB in the assembly.
2. Adjacent standalone `.pdb`.
3. NuGet symbol package (`.snupkg`) for package assemblies.
4. Symbol servers, keyed by the assembly's CodeView PDB identity.

Windows PDBs are detected but not read by this tool. If no matching portable PDB
is available, SourceLink sections should degrade to absent/empty evidence rather
than guessing.

Map presence is not map usability. A malformed custom-debug-information payload,
an empty map, or a map whose entries are all rejected remains visibly present
but unusable. `Signals` carries that summary; `SourceLink: Diagnostics` carries
the parse reason and rejected keys. Non-normalized authored document keys remain
in `Non-normalized Paths`, alongside CodeView PDB paths, so the deterministic
path signal evaluates both inputs.

### Identity is mandatory

Portable PDB metadata is row-aligned with the exact assembly build. A PDB from
another TFM or build can contain the same source files and still map method rows
to the wrong documents. Therefore:

- External portable PDBs must match the assembly CodeView GUID before use.
- Symbol-package cache keys include PDB identity, not just package/version/file
  name.
- If identity does not match, treat the PDB as unavailable for that assembly.
- SourceLink: Integrity cannot replace identity checking: content hashes can
  verify source files while method-row mappings are still wrong.

This is a fail-closed rule: missing SourceLink is better than wrong SourceLink.

### Discovery-time cache-only probe

Library `-D` discovery lists the SourceLink section family only when a local PDB
(embedded, adjacent, or already in the symbol cache) exposes a SourceLink
document — determined **network-free**. `LibraryMetadataService.ProbeLocalSourceLinkAsync`
opens the assembly and, if no embedded/adjacent PDB is present, consults the
symbol cache **read-only** via `SymbolPackageDownloader.DownloadPdbAsync(..., cacheOnly: true)`:
each `TryLocateFrom*` helper returns after its cache-hit check and issues no
HTTP (no GET/HEAD, no `PutAsync`). Steps 3–4 above (snupkg / symbol server) are
consulted only as cache lookups here; the network download for those steps still
happens on demand when a SourceLink section is explicitly rendered. A PDB warmed
into the cache by a prior render therefore makes the family discoverable on the
next `-D`; the effective-section cache keys on this availability so warming or
clearing the PDB busts a stale catalog. See
`docs/design/section-model.md#symbol-dependent-discovery-sourcelink-family`.
The target metadata-format cutover retains this library-only persistent
compatibility catalog for package and platform routes, as described under
[`Existing library effective catalog`](design/section-model.md#existing-library-effective-catalog);
it is not an authorization-bearing outcome cache for the planned type/member
executor. A direct local-file route performs the same bounded discovery from a
fresh retained image each run without persistent catalog lookup or
publication. This direct-file target is unverified pending
`LocalAssemblyFacts_DoNotEnterACrossRunCache`; shipping `effective-v28` still
persists direct-file catalogs as recorded under
[Current mismatch](design/assembly-image-lifetime.md#current-mismatch).

At that cutover, the bounded assembly gate runs over acquisition-retained bytes
before this probe or any permitted catalog lookup, and the persistent catalog
category also bumps. The assembly debug-directory read consumes those retained
bytes rather than reopening a mutable assembly path. Portable PDB parsing after
assembly admission may construct a PDB `MetadataReader`; it is not assembly
metadata projection and remains governed by the existing embedded-PDB and
expansion budgets.

The successor catalog key replaces the predecessor `sl0`/`sl1` Boolean with
typed `LocalSymbolDiscoveryEvidence`: `None`, or an owner-minted identity for
one retained, assembly-identity-validated portable PDB. That identity includes
the PDB content digest, discovery-relevant provider/provenance dimensions, and
typed SourceLink effectiveness. The probe freezes this evidence into the
effective-catalog subject before lookup; all PDB-dependent discovery and
publication use it unchanged. Separately authorized source rendering or
concurrent cache activity may warm and validate symbols, but cannot re-key the
current catalog. An observed evidence-generation change declines publication,
and the next invocation probes and recomputes under the new evidence. Replacing
one SourceLink-bearing PDB with another therefore changes the next key even
when both report true, because PDB document paths and other facts can change
effective catalog membership. Rendering still opens and validates the current
PDB rather than reusing catalog data as source evidence.

Bare effective discovery owns one finite portable-PDB retention budget. Its
compatibility default is 64 MiB, matching the existing
`DiscoveryMaxEmbeddedPdbBytes`, and it applies uniformly to adjacent, symbol
cache, acquired, and decompressed embedded PDB bytes. The owner reserves the
selected PDB's declared length before allocation, copying, hashing, or
`MetadataReaderProvider` construction; a non-seekable source uses a bounded
copy that stops at limit plus one, and embedded content reserves its declared
decompressed length before expansion. The retained snapshot holds the
reservation through catalog lookup/production and releases it with the
operation.

An over-limit candidate returns typed `PortablePdbRetentionLimitExceeded` and
performs no catalog read or write; it is not silently treated as `None` and does
not fall through to another provider. Product effective-discovery construction
cannot select `SourceLinkReadLimits.Unlimited`. This retention budget is
unimplemented and ungated; near/over limits, every provider, the aggregate
retained-byte peak, the one digest pass, and the same single-threaded
Browser/Wasm failure are tracked by [#3478](https://github.com/richlander/dotnet-inspect/issues/3478) and are unverified.

## Network and performance policy

dotnet-inspect should stay fast and local by default. SourceLink is allowed to
use the network only when the selected section justifies it.

| Work | When allowed |
| --- | --- |
| Acquire one missing PDB | explicit SourceLink/source/provenance section, or detailed library provenance where already documented |
| HEAD every source URL | explicit `SourceLink: Availability` / `SourceLink: Missing Files` |
| Download every source body | explicit `SourceLink: Integrity` |
| Fetch one PDB-mapped member source body | explicit selected-member `PDB Source` / `@Source` |
| Resolve member file/line locations | explicit member `Source Locations` section; may acquire one missing PDB but should not fetch source bodies |

Every source-body fetch checks the final response URL after redirects. If the
requested URL has an attributable SourceLink origin, the final URL must name the
same host, repository, and revision. The response body is then used only when it
matches the portable-PDB checksum. Availability and integrity audits apply the
same final-origin rule before recording reachability or reading content.
Browser/Wasm cannot report the final URL after an automatic redirect, so
attributed SourceLink fetches fail closed on that platform; checksum-verified
URLs outside the known provenance grammars remain available. Header-first body
reads retain the untrusted-fetch timeout and enforce the download cap against
decoded bytes even when the server omits `Content-Length`. Each source body is
capped at 16 MB. Browser/Wasm fetches require streaming-response support so the
transport cannot buffer the full body before that cap is enforced.

The section pipeline lowers selected SourceLink sections to typed query demand:

- `SourceLinkDocumentsQuery` declares moderated cost and may acquire one matching
  PDB.
- `SourceAvailabilityQuery` declares unbounded cost and consumes the document
  query before issuing per-file HEAD requests.
- `SourceIntegrityQuery` declares unbounded cost and consumes the same document
  query before downloading source bodies.

Detailed verbosity can authorize some lighter provenance enrichment, but it must
not silently authorize bulk SourceLink URL checks or source-body downloads. Bulk
work remains explicit.

## Caching

Caching is required because SourceLink can otherwise turn one command into many
network requests.

- Version/package metadata caches prevent repeated NuGet lookups.
- PDB caches avoid repeated symbol downloads.
- Symbol-package PDB caches are identity-keyed to avoid multi-TFM collisions.
- Source availability and integrity queries accept an optional host cache;
  filesystem-free hosts may run without one.
- Positive availability and integrity results are cached permanently only when
  the provenance grammar establishes an immutable commit-pinned GitHub or Azure
  DevOps URL. Other availability results retain a TTL; integrity results for
  unknown hosts and moving or ambiguous selectors are not cached.
- The target bare-library effective catalog may persist successful
  package/platform section summaries under its versioned semantic key. The
  slice-5 successor keys on retained assembly content plus complete typed
  local-symbol discovery evidence, not the predecessor `sl0`/`sl1` Boolean.
  Input-admission changes bump the category before lookup so prior successful
  catalogs cannot bypass the new gate; this cutover also runs bounded
  assembly-format admission before every permitted lookup. Assembly and PDB
  digest, admission, discovery, and publication each use their owner-retained
  immutable content; bracketing hashes over a mutable path are insufficient.
  Direct local-file discovery performs neither persistent lookup nor
  publication, unverified pending
  `LocalAssemblyFacts_DoNotEnterACrossRunCache`. Planned type/member
  authorization-dependent outcomes remain operation-local and never consume
  that catalog.

Cache reuse must never bypass PDB identity validation.

## URL safety

SourceLink URLs come from package or PDB data and are not inherently trusted.
Operations that contact those URLs should use the untrusted-fetch HTTP path and
remain opt-in. Rendering URLs as rows is lower risk than fetching their bodies,
but it still depends on a verified PDB identity.

## Design rules for new SourceLink features

1. Prefer sections over new commands or flags when output is a table/list.
2. Put assembly-scoped evidence under `library`.
3. Put package-aggregate evidence under `package`.
4. Put type-scoped evidence under `type`; selected-member source belongs under
   `member`.
5. Keep point queries narrow; do not make inventories masquerade as verbs.
6. Do not add default network fan-out.
7. Verify PDB identity before trusting method/type-to-document mappings.
8. When unsure, show no SourceLink rows instead of showing possibly wrong rows.
9. Do not reintroduce a broad SourceLink inventory command; add section-shaped
   capabilities to their package/library/type/member homes.
10. Keep `Member Index` focused on query/selectors; do not add URL/file/line
    columns to it.
11. Put member file/URL/line evidence in a dedicated `Source*` section on both
    member-group and selected-signature views.
12. Prefer file/URL/line rows over fetching source bodies when the user needs a
    source pointer rather than content.
