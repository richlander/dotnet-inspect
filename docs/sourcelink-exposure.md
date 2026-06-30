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
| Which source files map to this target? | `Source Files` sections on `library` / `package` / `type` |
| Where do these member signatures live in source? | A dedicated member `Source Locations` section for file/URL/line when a verified PDB is available |
| What is the source for this exact member or IL offset? | selected `member` source sections, or `library -S "IL Offset:<token>+<offset>"` for MethodDef token + IL offset point queries |

The command model should prefer sections over new flags. SourceLink URL listings
are document sections, not standalone verbs. Point queries, such as method-token
plus IL offset symbolication, use parameterized sections; `--il-offset` remains a
compatibility alias for selecting `IL Offset`.

## Current product surfaces

### Library

`library` is the natural home for assembly-level SourceLink evidence.

| Section | Purpose | Network posture |
| --- | --- | --- |
| `Symbols` | PDB format/location, SourceLink presence, symbol server, builder hints | may acquire one missing PDB when authorized |
| `Signals` | summary evidence including SourceLink/provenance signals | opt-in; may acquire one missing PDB |
| `Source Files` | type-to-SourceLink URL rows for the selected library | opt-in; may acquire one missing PDB |
| `SourceLink Availability` | per-source-file reachability via HTTP HEAD | opt-in; one request per source file |
| `SourceLink Missing Files` | files not reachable or embedded | opt-in; derived from availability pass |
| `SourceLink Integrity` | downloads source bodies and checks PDB checksums | opt-in; slowest and exits non-zero on mismatch |

### Package

`package` owns package-level aggregation. It should not force users to pivot to
another command merely to continue a package inspection.

| Section | Purpose |
| --- | --- |
| `Signals` | package and dependency evidence, plus binary/source provenance summaries |
| `Source Files` | SourceLink URL rows aggregated from package libraries, with library provenance |

The package `Source Files` section defaults to the same library selection rules
as other package-library views: compatible/highest TFM unless a TFM selector says
otherwise. This keeps the section scoped and avoids exploding output by default.

### Type and member

Selected member output already exposes source evidence:

| Section | Purpose |
| --- | --- |
| `Original Source` | SourceLink-backed original source text for one selected overload |
| `Source Locations` | file/URL/line rows for a member group or selected signature |
| `Decompiled Source` | readable C# reconstructed from IL |
| `Annotated Source` | C# plus hidden-fact comments and IL evidence |
| `IL` | raw IL disassembly |

Single-type `Source Files` is the natural section home for type-to-URL rows when
a user is already in a `type` flow.

Member source locations should not be added to `Member Index`. That section must
stay focused on the query pattern: terse selectors, stable selectors, and
canonical signatures. SourceLink file/URL/line belongs in a separate `Source*`
section, currently described as `Source Locations`.

`Source Locations` should exist on both member-group and selected-signature
views so the experience progressively narrows. A member-group view can show one
row per overload with enough selector/signature context to identify the row. A
selected-signature view can show the same file/URL/line evidence for one
signature without fetching the full `Original Source` body.

### Removed source command

The former `source` command has been folded into host-command sections and point
queries. Issue [#1163](https://github.com/richlander/dotnet-inspect/issues/1163)
records the removal path: source inventories became `Source Files` sections,
source-body retrieval follows selected-member `Original Source` / package
content patterns, availability checks live in `SourceLink Integrity` and
`SourceLink Availability`, URL shape is selected with `--blob`, and IL offset
symbolication is now `library -S "IL Offset:<token>+<offset>"` (or the
compatibility alias `--il-offset`).

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
sections such as `Decompiled Source`, `Annotated Source`, `Original Source`, and
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

### Identity is mandatory

Portable PDB metadata is row-aligned with the exact assembly build. A PDB from
another TFM or build can contain the same source files and still map method rows
to the wrong documents. Therefore:

- External portable PDBs must match the assembly CodeView GUID before use.
- Symbol-package cache keys include PDB identity, not just package/version/file
  name.
- If identity does not match, treat the PDB as unavailable for that assembly.
- SourceLink Integrity cannot replace identity checking: content hashes can
  verify source files while method-row mappings are still wrong.

This is a fail-closed rule: missing SourceLink is better than wrong SourceLink.

## Network and performance policy

dotnet-inspect should stay fast and local by default. SourceLink is allowed to
use the network only when the selected section justifies it.

| Work | When allowed |
| --- | --- |
| Acquire one missing PDB | explicit SourceLink/source/provenance section, or detailed library provenance where already documented |
| HEAD every source URL | explicit `SourceLink Availability` / `SourceLink Missing Files` |
| Download every source body | explicit `SourceLink Integrity` |
| Fetch one original member source body | explicit selected-member `Original Source` / `@Source` |
| Resolve member file/line locations | explicit member `Source Locations` section; may acquire one missing PDB but should not fetch source bodies |

The section pipeline encodes this with capabilities:

- `MayDownloadPdb`: a section may acquire symbols.
- `MayAuditSources`: a section may issue per-file HEAD requests.
- `MayFetchSources`: a section may download source bodies.

Detailed verbosity can authorize some lighter provenance enrichment, but it must
not silently authorize bulk SourceLink URL checks or source-body downloads. Bulk
work remains explicit.

## Caching

Caching is required because SourceLink can otherwise turn one command into many
network requests.

- Version/package metadata caches prevent repeated NuGet lookups.
- PDB caches avoid repeated symbol downloads.
- Symbol-package PDB caches are identity-keyed to avoid multi-TFM collisions.
- Effective-section caches may summarize what sections are renderable, but must
  be invalidated when section semantics change.

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
