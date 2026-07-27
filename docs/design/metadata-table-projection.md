# Raw metadata-table projection

> Design note for
> [#3282](https://github.com/richlander/dotnet-inspect/issues/3282): a
> first-class capability to project the raw ECMA-335 metadata tables of an
> assembly — enumerate a table, emit each row with its columns, and expose every
> coded index / handle as a resolvable reference to the row it points at. This is
> the ground-truth substrate the rest of the tool is cooked from, and it belongs
> in the Metadata layer as a reusable projection rather than a bespoke reader
> wired to a single consumer.

## Problem

`dotnet-inspect` has many *typed* projections of an assembly — `ApiSurface`,
Findings, C# type views, call graphs — each of which cooks a curated, meaningful
fact out of the metadata. What it does not have is a projection of the metadata
*as metadata*: the ECMA-335 tables themselves, row by row, column by column,
with the coded indices that stitch the tables into a graph.

Raw metadata-table viewing is a staple diagnostic in this space (SRM's own
`mdv`/`MetadataVisualizer`, ildasm's metadata view, ILSpy's Metadata tab). For
`dotnet-inspect` it earns its keep in three ways:

- **Diagnostics / evidence.** When a typed projection looks wrong, dropping to
  the raw row is the fastest way to tell whether the defect is in the metadata
  itself or in the higher-level cooking. This directly serves the repo's *keep
  failure visible* principle: the raw row is the independent oracle a suspect
  typed fact can be checked against.
- **Reuse across features.** One projection can back a CLI `metadata` table
  lens, raw-table version diffs (complementing the existing API version-diff
  capability), and forensic/corpus inspection.
- **Low marginal cost.** SRM already exposes table enumeration and typed row
  structs, and `ILTokenResolver` already performs token resolution. The new work
  is mainly coded-index/heap decoding and a stable output shape — not a new
  reader.

## The two projections — the layering crux

This is the load-bearing idea in the whole design, so it comes first.

The ECMA-335 bytes — read faithfully by `System.Reflection.Metadata` (SRM) — are
the common ground truth. Two projections rise from that substrate along
**orthogonal axes**, and each is simultaneously lossless and lossy, but in
*opposite dimensions*:

| Projection | Structural dimension | Semantic dimension |
| --- | --- | --- |
| **Raw table projection** (this note) | **lossless over the logical table/heap graph SRM exposes** — every table, row, column, coded index, token, and heap reference (scoped below) | **lossy** — computes no derived meaning |
| **Typed extractors** (`ApiSurfaceExtractor`, Findings, C# views) | **lossy** — discards which rows, which tokens, the table topology | **lossless** *within the scope they curate* — API surface, spellable signatures, hierarchy, nullability, correspondences |

Because each is lossless exactly where the other is lossy, **neither is
reconstructible from the other**, at least not efficiently:

- You **cannot build a metadata explorer on `ApiSurfaceExtractor`.** The
  explorer needs the physical graph — every row and every handle-to-handle edge
  — and the extractor has already thrown that away to get at meaning.
- You **cannot build `dotnet-inspect`'s features on the table projection.** The
  features need semantics the raw projection never computed; re-deriving them
  from raw rows means re-implementing all the cooking on top of a
  presentation-shaped intermediate — both slower and an inversion of ownership.

"Lossless" here is scoped to the *logical* table-and-heap graph SRM exposes — it
is not a byte-exact image. PE/COFF headers, the `#~` vs `#-` metadata-stream
choice, physical stream/row layout, exact heap byte offsets, and unreferenced or
duplicated heap entries are out of scope, and bulk blob bytes are a bounded
preview until explicitly requested (see [Scope](#scope) and [Safety](#safety)).
The property the argument rests on holds at that logical-graph level: the
projection drops no *table-graph* fact a typed extractor would otherwise need.

The asymmetry does not make derivation across the two *impossible* — typed facts
can, in the ordinary sense, be cooked from raw rows. What it does is make each
direction **inefficient and an inversion of ownership**. So the topology below is
a deliberate architectural boundary, not an arbitrary one: the lossless/lossy
asymmetry is what makes that boundary cheap to keep and costly to violate.

### Sibling, not stack

The issue phrases the constraint as "a projection consumed by higher features,
**never a dependency of them**." The key word is *directional*. The projection
absolutely has dependents; what it must never become is a dependency **of the
typed extractors**. The projection and the extractors are **siblings**, each
reading the same SRM substrate directly — not a stack in which extractors sit on
top of the projection.

The inversion this rules out:

```text
WRONG — inverted ownership
  ApiSurfaceExtractor / Findings   (typed facts)
        │ derives its facts from
        ▼
  RawMetadataTableProjection        (raw decoded rows)
        │
        ▼
  SRM MetadataReader
```

i.e. implementing the API surface by first building raw `MethodDef`/`TypeDef`
rows and then cooking typed facts out of those rows.

The intended topology:

```text
RIGHT — sibling consumers of a shared floor
  metadata lens / raw-table diff / explorer   (depend on the projection)
        │
        ▼
  RawMetadataTableProjection ──┐        ApiSurfaceExtractor / Findings
        │ reuses               │              │ read SRM directly
        ▼                      ▼              ▼
   ILTokenResolver        SRM MetadataReader (+ MetadataPrimitives)
```

Both the projection and the extractors depend *downward* on the same shared
floor (SRM, `ILInspector.MetadataPrimitives`, `ILTokenResolver`). What the
projection must never do is become an *upward* input that typed facts are
re-derived from.

Why it matters beyond tidiness: the projection is a *diagnostic,
presentation-oriented* view — its decode and display choices are for humans. If
typed extractors depended on it, those display choices would become semantically
load-bearing, and — worse — the projection would lose its entire diagnostic
value. The point of the raw view is to **falsify** a suspect typed projection; if
both share the same code path, a bug hides identically in both and the raw view
can no longer contradict the cooked one.

Analogy: `SELECT * FROM sys.tables` is a diagnostic lens over the same storage
the query planner reads. You do not build the planner by parsing the catalog
dump — both read the storage engine directly. The dump has consumers (DBAs,
tools); it is not a dependency *of the planner*.

## Scope

Given an assembly image, enumerate the ECMA-335 metadata tables (`Module`,
`TypeRef`, `TypeDef`, `Field`, `MethodDef`, `Param`, `MemberRef`, `Constant`,
`CustomAttribute`, `StandAloneSig`, `MethodImpl`, `TypeSpec`, `Assembly`,
`AssemblyRef`, `ExportedType`, `GenericParam`, `MethodSpec`, …) and produce, per
table, its rows with:

1. **Each column value in raw form**, plus a friendly decode where cheap (flag
   enums, a name/namespace pulled from the string heap, well-known GUIDs). The
   friendly decode is strictly **additive**: it is a convenience column *beside*
   the raw value, never a replacement for it. Replacing the raw value would
   forfeit the lossless-over-the-graph property that is the projection's whole
   reason to exist.
2. **Handle-typed columns surfaced as resolvable references** (target table + row
   index / token) so a consumer can follow a coded index to the row it points
   at. This is the feature that turns a flat dump into the navigable graph the
   explorer needs.
3. **Large heaps (string / blob / user-string / guid) surfaced lazily or
   optionally, not dumped by default.** A blob column shows its handle and a
   bounded, escaped preview; the full heap is an explicit opt-in.

Resolution reuses `ILTokenResolver`; the projection does **not** fork a second
decoder (see [Non-goals](#non-goals)).

## Structured model

The projection's product is a typed model, not text. Markdown (and TSV / JSONL /
JSON) are *renderings* of the model; the model is what an explorer, a diff, or a
row query consume. Sketch (names indicative, not final):

```text
MetadataTableView
  TableIndex   Index          // ECMA-335 table id (e.g. TypeDef = 0x02)
  string       Name
  int          RowCount
  IReadOnlyList<MetadataColumn> Columns
  IReadOnlyList<MetadataRow>    Rows

MetadataRow
  int          RowId          // 1-based row number within the table
  int          Token          // full metadata token for the row
  IReadOnlyList<MetadataCell> Cells

MetadataCell
  MetadataColumn Column
  MetadataValue  Value        // discriminated: Raw | HeapRef | HandleRef | HandleRange | Flags

HandleRef                     // the crux: a resolvable edge, not display text
  TableIndex   TargetTable
  int          TargetRowId
  int          Token
  string?      Display        // optional convenience label (via ILTokenResolver)

HandleRange                   // a list/run column: TypeDef.FieldList, MethodList, …
  TableIndex   TargetTable
  int          StartRowId     // inclusive
  int          EndRowId       // exclusive; end derived from the next owner's start
```

A `HandleRef` is a *typed edge*, decoded from a coded index or entity handle. The
`Display` label is convenience only; a consumer navigating handle-to-handle keys
off `TargetTable` + `TargetRowId` (or `Token`), never off display text. This is
the same discipline the repo applies everywhere: identity and correspondence are
separate concerns from presentation, and one is never inferred from the other.

Two ECMA-335 shapes need first-class modeling, and are why the sketch is not just
a flat `HandleRef`:

- **Multi-target coded indices.** Many columns are coded indices whose target may
  be one of several tables (for example `TypeDefOrRef`, `HasCustomAttribute`,
  `MemberRefParent`). The column *schema* (`MetadataColumn`) declares the
  candidate target-table set; each row's `HandleRef.TargetTable` names the one
  concrete table it resolved to.
- **List / range columns.** Some columns encode a contiguous *run* of target rows
  rather than a single edge — `TypeDef.FieldList` / `MethodList`, `PropertyMap`,
  `EventMap`. These are modeled as `HandleRange`, whose end is derived from the
  next owner row's start (the standard ECMA "runs to the next owner" rule). The
  first version must handle ranges: the `TypeDef → Field` / `Method`
  relationships are core structure, not an edge case.

Values that cross the inspection-session boundary are immutable tokens and
shapes, never live SRM handles or reader-backed spans — consistent with the
session-lifetime rule in
[assembly-inspection-query.md](assembly-inspection-query.md).

## Surface — the `metadata` table lens

The projection is exposed as its own table lens so it composes with the existing
verbosity model and can be selected explicitly. It rides the `library` command
(the assembly-oriented surface; note the deprecated `package X --metadata` alias
already redirects there):

```bash
# enumerate a single table
dotnet-inspect library My.dll --table TypeDef

# a single row with its columns and resolvable handle refs
dotnet-inspect library My.dll --table MethodDef --row 3

# structured, for tooling
dotnet-inspect library My.dll --table TypeRef --jsonl
```

This obeys the shape ladder in [output-shapes.md](output-shapes.md): a table is a
**Table**, one column is a **Vector**, `--count` / one row is a **Scalar**. Each
metadata table is one section; a `HandleRef` cell renders as its token +
optional label in Markdown and as structured fields in JSON/JSONL.

Progressive-disclosure discipline (see
[progressive-disclosure.md](progressive-disclosure.md) and
[section-model.md](section-model.md)): raw tables are **not** in the default
`-v:m` view. They are reached by explicit selection (`--table <Name>` or
`-S`), following the same focused-flag-promotes-sections pattern that
`library --il-offset` uses to add its coordinate-scoped sections. Heap dumps are
a further explicit opt-in on top of that.

## Safety

Raw table projection reads untrusted metadata and can amplify a tiny artifact
into unbounded work or output, so it inherits the existing contracts rather than
inventing new ones:

- **Bounded traversal.** Row enumeration, handle resolution, and text/heap
  projection are budgeted per
  [bounded-metadata-traversal.md](bounded-metadata-traversal.md): a bounded
  number of rows visited, edges followed, and characters projected. Exceeding a
  budget yields a **typed rejection**, never a plausible truncated value or a
  success-shaped empty table.
- **Untrusted input.** SRM-only, parse-never-load, NativeAOT-friendly,
  Roslyn-free, per
  [untrusted-data-threat-model.md](untrusted-data-threat-model.md). Heap and
  name text is rendered as **data** — escaped so it cannot inject terminal
  control sequences or break structured output. A malformed coded index resolves
  to a visible failure marker, not a fabricated target.
- **Heaps are opt-in.** The string/blob/user-string/guid heaps are the largest
  amplification surface, so they are never dumped by default; a bounded, escaped
  preview stands in until explicitly requested.

## Prior art: `mdv` / `MetadataVisualizer`

`dotnet/metadata-tools` ships `mdv` and the `Microsoft.Metadata.Visualizer`
library — the closest existing tool to this feature. It is MIT-licensed
(vendorable with a `THIRD-PARTY-NOTICES.TXT` entry), but the recommendation is
**inspire, do not vendor**. License is not the blocker; architecture is:

- **Roslyn-dependent.** Its project references `Microsoft.CodeAnalysis.Debugging`
  and `Microsoft.CodeAnalysis.PooledObjects`, and its source uses
  `Roslyn.Utilities`. That violates the Roslyn-free product-path constraint.
- **No structured model.** It goes `MetadataReader → TextWriter`, building
  `TableBuilder` rows of *already-formatted* `string[]`. Handle targets are baked
  into display text, so it cannot back the navigable explorer without re-parsing
  strings — the exact layer inversion this note forbids.
- **Forks a decoder.** It ships its own `SignatureVisualizer` (an ilasm-syntax
  `ISignatureTypeProvider`) and `ILVisualizer`. We reuse `ILTokenResolver`.
- **Scope bloat.** A large share of its ~113 KB core is Edit-and-Continue /
  multi-generation `MetadataAggregator` delta machinery that is out of scope and
  a NativeAOT / maintenance risk. It is also `#nullable disable`, older style.

What is worth mining as a **reference** (not copied):

- Its per-table read methods are an excellent worked ECMA-335 crib — the exact
  column set per table, which columns are coded indices vs heap refs vs flags,
  and the cheap friendly decodes (flag enums, language / hash-algorithm GUIDs,
  custom-debug-info kinds). Follow it while emitting our *own* typed rows.
- Heap discipline (`NoHeapReferences` / `ShortenBlobs` options, lazy heap dumps)
  matches the opt-in-heaps rule above.
- Its `BlobKind` / `StringKind` tagging — labeling a heap entry by its
  referencing context (what a blob *is*) — is a nice cheap-decode idea.

### `mdv` as a future consumer, not just a reference

In the fullness of time, an `mdv`-style text dump could be *rebuilt on top of*
this projection. That is the sanctioned direction: such a dump is a
**presentation consumer** (like the lens, the diff, and the explorer), never a
dependency of the typed extractors, so it does not disturb the sibling topology.

`mdv`'s output decomposes cleanly by where each part would come from:

| `mdv` output | Rebuilt from |
| --- | --- |
| Per-table row/column dump; coded-index / handle rendering | **This projection**, via a fixed-width text formatter — free, and navigable |
| Signature / blob **content** decode | Projection structure + the existing `SignatureDecoder` / `GuardedSignatureText` / `ILTokenResolver` as cell decoders |
| Heap dumps + `BlobKind` / `StringKind` tagging | The projection's opt-in **heap enumeration** plus reverse-reference tagging (walk blob-typed columns) |
| GUID → language / hash, custom-debug-info kinds | The projection's **additive friendly-decode** slot |
| PE / COFF headers, debug directory, R2R | A **sibling** PE-header projection — not metadata-table facts (out of scope here) |
| IL disassembly of method bodies | A **sibling** IL projection (Instructions / Analysis layer) |
| EnC / multi-generation deltas | A **consumer** over per-generation projections + the `EncLog` / `EncMap` tables — a separate feature, today a non-goal |

So the metadata-table and heap domain — the majority of `mdv`'s bytes — is a
*formatter over this model*; the rest is peer projections composed alongside it.

This is the renderer-over-model inversion made concrete, and a good future
litmus test for the design: because the projection is **lossless over the
metadata table/heap graph**, an `mdv`-style dump is a pure (lossy) rendering of
it, and text becomes one formatter among many (text, JSON, explorer). If a
faithful `mdv` text formatter can be produced from the model, the model is rich
enough for the table domain.

The honest caveat is the line between *functionally equivalent* and
*byte-identical*. Reproducing `mdv`'s information is easy; reproducing its exact
text needs a few **physical** details this scope defers — heap offsets, per-table
byte sizes (its table title prints `size: rowCount × rowSize`), and exact
enumeration order. All are cheap and SRM-available
(`GetTableRowCount`, `GetTableRowSize`, `MetadataTokens.GetHeapOffset`), so they
are a knob the model can expose, not a wall.

## Implemented: the `mdi` tool and the shared renderer

The first presentation consumer of the projection is **`mdi`** (metadata
inspector), a standalone tool that renders the tables the way `mdv` does:

- **`src/mdi`** — a `PackAsTool` / `PublishAot` command
  (`ToolCommandName=mdi`) whose System.CommandLine front-end maps flags onto
  `MetadataProjectionOptions` and delegates all output to the renderer below.
  Surface: `mdi <assembly> [--table|-t <Names>] [--format|-f md|tsv|jsonl]
  [--max-rows|-n N] [--start-row|-s N] [--max-bytes N] [--max-chars N]`. Missing
  files, native images, and unreadable metadata surface as visible errors, never
  success-shaped empty output.
- **`src/DotnetInspector.MetadataRendering`** — a small reusable library holding
  `MetadataProjectionRenderer` (projection → Markout tables). It lives in the
  product-side `DotnetInspector.*` family, **not** in `ILInspector.Metadata`,
  which stays presentation-free: the renderer is a sibling of the Metadata layer
  that consumes the projection, so a future `dotnet-inspect metadata` lens reuses
  the same code the tool uses today.

Renderer contract. Rendering is a deliberately **lossy** human/inspection view;
the projection model stays the lossless source of truth (for example for the
oracle below). Three invariants hold regardless: every cell renders from exactly
one `MetadataValue` case; a `Malformed` cell keeps a visible `!malformed:`
marker; and a bounded preview is suffixed with `…` so it is never mistaken for a
whole value. A leading row-id column lets a reader cross-reference a resolved
handle target (rendered `TypeRef[5] (System.Object)`) back to its row.

Formats differ only in how a row's table is identified. Markdown introduces each
table with a `## <Name> (rows)` heading over a pipe table; TSV and JSONL carry a
leading `Table` column so every row self-identifies, keeping those outputs pure
machine-readable streams (one `WriteTable` block per table).

The `mdv` oracle is a follow-up increment: because it diffs against the
projection **model** (not `mdi`'s rendered text), the renderer is free to be
human-friendly without weakening the oracle. Supported-table coverage is kept in
step with the tables `mdv` dumps, so every supported table's physical row count
is cross-validated against `mdv` for free on the product assembly.

## Implemented: random access for a browser host

The projection's other planned consumer is the wasm **Metadata Explorer** (issue
#3341), which browses tables interactively rather than dumping them. That host
imposes three constraints a batch dump does not, all met without changing the
model:

- **No filesystem.** `MetadataTableProjector.Project(PEReader, options)` is the
  entry point; a browser host constructs `new PEReader(new MemoryStream(bytes))`
  and never supplies a path. `AssemblyInspectionSession` stays the desktop-shaped
  convenience over the same projector.
- **No whole-table materialization.** `MetadataProjectionOptions.StartRowId`
  pairs with `MaxRowsPerTable` to form a **row window**, so a table of 5,000 rows
  is browsed a page at a time. A window changes coverage, never content: rows
  keep their absolute `RowId`, `MetadataTableView.RowCount` stays the physical
  count, and any partial coverage is marked by `Truncation`. A window past the
  end of a populated table yields that table with zero rows rather than dropping
  it, so paging cannot make a table look absent.
- **No dead-end handles.** A `HandleRef` is the click-through primitive, and its
  target frequently lies outside the current window.
  `MetadataTableProjector.ProjectRow(peReader, table, rowId)` reads that one row
  on demand and returns it inside its table's view, so the caller also gets the
  column schema and the table's real size. It is a one-row window over the same
  reader, not a second row path, so malformed-row containment and every budget
  behave identically.

Because a window can start anywhere, presentation must **name** the window
rather than only its size: `mdi` renders `showing rows 4–6 of 366`, and its
machine formats report the same range on stderr. Reporting `3 of 366` alone
would read as the first three rows.

Allocation shape — windowed or lazy materialization, and `ReadOnlySpan<T>` views
over the current `ImmutableArray<T>` — remains open and **measurement-gated**;
the browser's memory ceiling is the real constraint, and no benchmark exists yet.

## Layer placement

The projection lives in the **Metadata layer** (`ILInspector.Metadata`), beside
the typed extractors, reading SRM through the canonical
`AssemblyImage.Open` / `AssemblyInspectionSession` entry points. It reuses
`ILTokenResolver` for handle-to-text and the shared SRM primitives in
`ILInspector.MetadataPrimitives`; the CLI owns the `metadata` lens presentation
via the existing section pipeline and `OutputFormatter`.

## Non-goals

- **Not a dependency of the typed extractors.** `ApiSurfaceExtractor`, Findings
  producers, and C# views continue to read SRM directly and own their typed
  facts. They must not be re-derived from raw rows (see
  [the layering crux](#the-two-projections--the-layering-crux)).
- **Not a second decoder.** Handle/token resolution reuses `ILTokenResolver`; no
  parallel signature or IL decoder is introduced.
- **No Edit-and-Continue / delta support.** Single-generation images only. The
  multi-generation machinery is out of scope.
- **Read-only.** No metadata writing, editing, or round-tripping.
- **The explorer is a downstream consumer, not this deliverable.** This note
  tracks the *projection* — the general capability. The interactive
  handle-to-handle explorer is one consumer that the resolvable-handle links make
  possible; it is scoped separately.

## Open questions

- **`HandleRef` shape.** Exact fields and whether `Display` is always populated
  or lazily resolved. Decide before code, since the explorer's value rests on it.
- **Lens home.** A focused `--table <Name>` flag on `library`, a `-S` section
  group ("Metadata Tables"), or a dedicated `metadata` command. Leaning toward a
  `library` flag to reuse assembly acquisition and the section pipeline.
- **Heap surfacing flags.** The exact opt-in gesture(s) for dumping string / blob
  / user-string / guid heaps, and their bounded-preview defaults.
- **Table selection grammar.** Whether tables are addressed by ECMA name
  (`TypeDef`), by index (`0x02`), or both.
