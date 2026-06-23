# SourceLink Exposure

SourceLink is not a first-class artifact like a package manifest or assembly
metadata table. It is source provenance hidden behind debug symbols, so its CLI
home is less obvious. This document records where dotnet-inspect should expose
SourceLink, how those surfaces depend on PDBs, and how network work is bounded
so the tool stays fast by default.

This follows the section-bias direction from
[issue #1163](https://github.com/richlander/dotnet-inspect/issues/1163):
SourceLink inventories belong on `package`, `library`, and `type` sections; the
standalone `source` command is transitional compatibility, not the long-term
home for new SourceLink surfaces.

## Model

SourceLink answers three related questions:

| Question | Best home |
| --- | --- |
| Does this binary have trustworthy source provenance? | `library` / `package` `Signals`, `Symbols`, and `SourceLink *` sections |
| Which source files map to this target? | `Source Files` sections on `library` / `package` / `type` |
| What is the source for this exact member or IL offset? | selected `member` source sections, or a narrow point query / parameterized section while the `source` command remains |

The command model should prefer sections over new flags. SourceLink URL listings
are document sections, not standalone verbs. Point queries, such as method-token
plus IL offset symbolication, are the exception because they do not naturally
produce a section-shaped inventory.

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
| `Decompiled Source` | readable C# reconstructed from IL |
| `Annotated Source` | C# plus hidden-fact comments and IL evidence |
| `IL` | raw IL disassembly |

Single-type `Source Files` is the natural section home for type-to-URL rows when
a user is already in a `type` flow. The standalone `source` command remains for
type URL lookup compatibility and point symbolication.

### Source command

`source` is legacy / transitional. Most of its output is section-shaped and
should migrate into `type`, `library`, and `package`. Issue #1163 records the
long-term removal path: source inventories become `Source Files` sections,
source-body retrieval follows existing content retrieval patterns, availability
checks fold into `SourceLink Integrity`, URL shape is a format concern, and IL
offset symbolication becomes the remaining narrow point-query/parameterized
section rather than a broad command.

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
9. Do not expand `source` with new section-shaped capabilities; move those to
   their package/library/type/member homes.
