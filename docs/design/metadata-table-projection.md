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

**Status: implemented.** The commands and outputs below are the shipping
surface.

Each metadata table is **one section**, and the tables together form a section
category, `@Metadata`, registered the same way `@Performance` is:

```csharp
.AddCategory(SectionCategoryNames.Metadata, MetadataSectionNames.All)
```

The lens therefore adds **no new shape flags**. Metadata tables introduce no new
currency — they are sections, addressed by name — so they are reached with the
existing selection vocabulary rather than a focused flag. It rides the `library`
command (the assembly-oriented surface; note the deprecated `package X
--metadata` alias already redirects there):

```bash
# Document: every projected table
dotnet-inspect library My.dll -S @Metadata

# Table: one metadata table
dotnet-inspect library My.dll -S "Metadata: TypeRef"

# Vector: one column of that table
dotnet-inspect library My.dll -S "Metadata: TypeRef" --columns Name --tsv

# Scalar: collapse to a row count
dotnet-inspect library My.dll -S "Metadata: TypeDef" --count

# a bounded window into a large table
dotnet-inspect library My.dll -S "Metadata: MethodDef" --rows 20

# structured, for tooling
dotnet-inspect library My.dll -S "Metadata: TypeRef" --jsonl
```

This obeys the shape ladder in [output-shapes.md](output-shapes.md): a table is a
**Table**, one column is a **Vector**, and `--count` is a **Scalar**. A
`HandleRef` cell renders as its token + optional label in Markdown and as
structured fields in JSON/JSONL.

Because `--count` reduces *each* selected section to a count, the category
selection doubles as the table-stream overview, with no separate rendering path:

```bash
dotnet-inspect library My.dll -S @Metadata --count
```

```md
| Section | Count |
| ------- | ----- |
| Metadata: TypeRef | 309 |
| Metadata: TypeDef | 118 |
| Metadata: MethodDef | 1204 |
```

The parts of the image that are not rows of a table — stream sizes, the
table-present bitmask, the metadata version string — are not expressible as row
counts, so they remain a single `Metadata: Image` section.

Progressive-disclosure discipline (see
[progressive-disclosure.md](progressive-disclosure.md) and
[section-model.md](section-model.md)): raw tables are **not** in the default
`-v:m` view, nor in any other verbosity view, nor in `-S @All`. Three separate
mechanisms are involved, and they do different jobs:

- Each metadata table descriptor sets `ExplicitOnly => true`. That is a render
  gate — `SectionPipeline.IsRequested` returns `false` for an `ExplicitOnly`
  entry unless it is explicitly included, so no verbosity level auto-selects it.
  This is per-section configuration, the same as the `@Performance` sections and
  the `--il-offset` coordinate sections.
- Each also declares `SectionCost.Unbounded`, which no verbosity budget admits —
  not even `-v:d`. This is a **second, independently sufficient** gate:
  measured by mutation, removing either one alone still leaves raw tables
  suppressed, and only removing both makes them render.
  `MetadataLens_NoVerbosity_RendersAnyMetadataSection` is the gate that proves
  the property; note that because the two are redundant it would not catch the
  loss of one alone.
- The `@Metadata` category is the *selection and discovery* affordance. It lets
  `-S @Metadata` name the whole group and gives `-D` something to list; it does
  not by itself suppress anything.

Heap **addressing** is the one place this lens does introduce a new currency: a
heap coordinate such as `#Strings:0x1a4` is not a section name, so it needs a
carrier. `--heap` is that carrier, and it behaves like `--il-offset` — it makes a
coordinate-scoped section available and discoverable only when present (see the
coordinate-carrier family in [output-shapes.md](output-shapes.md)):

```bash
# what this heap holds — just a section
dotnet-inspect library My.dll -S "Metadata: #Strings"

# one address — a coordinate
dotnet-inspect library My.dll --heap "#Strings:0x1a4"
```

**Status: implemented**
([#3467](https://github.com/richlander/dotnet-inspect/issues/3467)). Both
spellings of a heap name are accepted (`#Strings` and `String`), and an address
is decimal unless it carries an explicit `0x`. Hex is never *inferred*: a bare
`1a4` is rejected rather than read as `0x1a4`, because guessing would silently
address a different entry than the one a dump printed.

### Heaps are not tables

A heap listing cannot be what a table listing is, and the difference is a
property of ECMA-335 rather than of this projection. A table has a row count and
fixed-width rows, so "row 40 000" is arithmetic. `#Strings` and `#Blob` are
length-prefixed byte soup with no index, and `System.Reflection.Metadata`
exposes no walker for them — only random access from an address a table cell
already holds. So each heap section is listed by the strongest honest means
available, and `MetadataHeapCoverage` makes *which one* part of the answer
rather than a footnote:

| Heap | Coverage | What is listed |
| --- | --- | --- |
| `#GUID` | `Complete` | Every entry. Records are a fixed 16 bytes, so the count is `size / 16` and each is read by index. |
| `#Strings`, `#Blob` | `ReferencedOnly` | The distinct values projected table rows point at, address-ordered. An entry no row references is invisible. |
| `#US` | `NotEnumerable` | Nothing. No table column points into `#US` — its references are `ldstr` operands in method bodies — so there is no reference set to list. |

Every listing renders its coverage as a caveat, so a referenced-values listing
never reads as a walk of the heap and `#US`'s empty table never reads as an
empty heap. `#US` keeps its section rather than being hidden: the section is how
a caller learns the heap exists, how large it is, and that `--heap "#US:<addr>"`
still reads any address in it.

Reference scanning deliberately ignores a `--tables` filter. An entry is
referenced by the *image*, not by whichever subset of tables the caller happens
to be looking at, so honoring the filter would drop entries and undercount
references while looking complete. A row window is different — it is a stated
bound — so a short window is honored and reported as `RowsTruncated`.

Deep paging into the middle of a table needs no metadata-specific gesture. It is
a general row-selection concern, and `--rows` now carries it
([#3364](https://github.com/richlander/dotnet-inspect/issues/3364)): a row range
such as `--rows 40000..40099` addresses an arbitrary window in any section,
metadata tables included. This lens therefore adds nothing for paging and
inherits that behavior. Random access from a host that is not the CLI stays a
library concern, served by `ProjectRow` and the row window.

## Safety

Raw table projection reads untrusted metadata and can amplify a tiny artifact
into unbounded work or output. This section aligns with and is an application
of [untrusted-data-threat-model.md](untrusted-data-threat-model.md), and is the
worked example that document points at.

### Inherited contracts

- **Bounded traversal.** Row enumeration, handle resolution, and text/heap
  projection are budgeted per
  [bounded-metadata-traversal.md](bounded-metadata-traversal.md): a bounded
  number of rows visited, edges followed, and characters projected. Today a
  budget produces a **successful projection carrying an explicit `Truncation`
  marker**, not a typed rejection — visible, but not yet the rejection model
  that contract describes. Gated by
  `MetadataTableProjectionTests.RowBudget_TruncatesExplicitly_NeverSilently`,
  `StringBudget_ProjectsBoundedPreviewWithExplicitTruncation`, and
  `BlobBudget_ProjectsBoundedHexPreviewWithExplicitTruncation`. Those three
  gate that truncation is *explicit*; the one that gates the bound itself is
  `StringPreview_NeverExceedsCharBudgetEvenWhenEscaped`, because
  `StringBudget_…` asserts only that the full length is reported and the
  preview is non-empty.
- **Parse, never load.** SRM-only, NativeAOT-friendly, Roslyn-free. A malformed
  coded index resolves to a visible failure marker, not a fabricated target.
  This is an architectural constraint with no single gate naming it; treat the
  property as unverified here.
- **Heaps are opt-in.** The string/blob/user-string/guid heaps are the largest
  amplification surface, so they are never dumped by default. Gated by
  `MetadataLensTests.MetadataLens_NoVerbosity_RendersAnyHeapListing`, which
  covers `-v:q` and `-v:d` — the ends of the ladder, not `-v:m` or `-v:n`. The
  middle of the ladder is unverified.

### Two orthogonal axes

Safety here is two independent decisions, and conflating them into one flag is
a design error. One decides whether hostile input is *tolerated*; the other
decides how artifact text is *spelled*.

**Trust** — what happens when a concerning pattern is found:

| Flag | Behavior |
| --- | --- |
| *(default)* | abort at the first one |
| `--survey` | keep going and report each site — offset and pattern kind, never content — up to the traversal budget, which truncates explicitly like any other budget |
| `--dangerously-skip-checks` | keep going and render the values anyway |

**Rendering** — how artifact text is spelled once something is printed:

| Flag | Behavior |
| --- | --- |
| *(default)* | visually encoded: control characters re-spelled into an inert form |
| `--dangerously-print-raw-text` | artifact text passed through without visual encoding; the output format's own structural escaping still applies, so JSON stays parseable |

The axes are independent, and that is the whole design. Visual encoding is the
default on **every** artifact-text path, including under
`--dangerously-skip-checks` — which is exactly what makes that flag defensible.
It means "do not refuse," not "attack my terminal." Reaching a live `ESC` on a
terminal therefore takes **both** flags: one to stop refusing, one to stop
encoding. Two separately named mistakes.

`--survey` is the mode that keeps a hostile image inspectable without handing
over its bytes: it reports *where* and *what kind*, never *what*. This is the
shape `grep` has had for decades — `Binary file X matches` by default, content
only under `-a`/`--text`.

### Visual encoding, not neutralization

The rendering axis does not remove or replace dangerous characters; it
**visually encodes** them so the sink cannot interpret them. The term and the
contract are borrowed from BSD [`vis(3)`](https://man.netbsd.org/vis.3), which
encodes arbitrary input into graphic characters only and pairs every encoder
with a decoder (`unvis`) so the transform is unique and invertible.

**What is encoded** is defined by Unicode general category, not by a list.
Lists drift, and the drift is invisible: a list written against terminal
escapes will not contain the character that attacks a *different* sink.

| Category | Contains | Sink it attacks |
| --- | --- | --- |
| `Cc` | C0, `DEL`, C1 | terminal control sequences |
| `Cf` ⊇ `Bidi_Control` | bidi overrides, isolates, marks | visual reordering — Trojan Source |
| `Cs` | *unpaired* surrogates only | UTF-8 conversion |
| `Zl`, `Zp` | `U+2028`, `U+2029` | line-oriented and JS-adjacent consumers |

The bidi characters are the ones a hand-written list always misses, and not one
of them is anywhere near C1, so a rule stated as "C0, `DEL`, and C1" excludes
every one. Two sets get conflated here, so name them separately:

- `rustc` made **nine** code points a deny-by-default error after Trojan Source
  (CVE-2021-42574): the embeddings and overrides `U+202A`–`U+202E` and the
  isolates `U+2066`–`U+2069`. That is the whole of its `TEXT_FLOW_CONTROL_CHARS`.
- Unicode's **`Bidi_Control`** property is those nine plus the three marks
  `U+200E` LRM, `U+200F` RLM, and `U+061C` ALM — twelve. This is the set
  `ApiOutputFormatter.IsBidiControl` already implements.

**How far past bidi to go is an open decision, and not one this document
settles.** `Cf` is `Bidi_Control` plus the invisible formatting characters:
`U+200C` ZWNJ, `U+200D` ZWJ, `U+2060` WORD JOINER, `U+00AD` SOFT HYPHEN,
`U+FEFF`. This repository has already decided against the wider set once,
deliberately, with the reason recorded in `ApiOutputFormatter.cs`:

> Deliberately narrower than the Cf category — a zero-width joiner or a BOM
> does not reorder its neighbors, and legitimate identifiers may contain format
> characters, so escaping all of Cf would corrupt ordinary names.

That holds on its own terms, and C# backs it: the language admits `Cf` in
`identifier_part_character`, so a format character inside a type name can be
entirely legitimate. The counter-argument is that these are two different
attacks. `Bidi_Control` *reorders*; ZWJ and its neighbours are *invisible*, and
an invisible character is exactly how two distinct package or type identities
come to render identically. For a tool whose product is "what is actually in
this field", showing that difference is arguably the job.

Both readings are defensible, they disagree, and one of them is already
shipped. Resolve it before implementing — do not let this document silently
overrule `ApiOutputFormatter`. The measured cost of choosing the wider set:
Persian `می‌خواهم`, Devanagari `क्‍ष`, and emoji ZWJ sequences all render with
visible escapes.

Counts, for whichever set is chosen: `Cc` 65, `Bidi_Control` 12, all of `Cf`
43, `Cs` 2,048, `Zl` and `Zp` one each — 2,158 BMP code points for the widest
reading. CJK, combining diacritics, and precomposed ligatures such as `U+FB01`
are untouched under either.

**Encode by scalar, not by UTF-16 code unit.** Every non-BMP character — every
emoji, every rare CJK ideograph — is stored in a .NET `string` as *two* `Cs`
code units. A loop over `char` calling `GetUnicodeCategory` per unit therefore
encodes all of them, turning 😀 into `\uD83D\uDE00`. Only an **unpaired**
surrogate is a hazard. Enumerate `Rune`s and encode a surrogate only when it
has no partner; the gate below must include a paired-surrogate case, or this
bug ships looking correct.

**How it is spelled** is `vis(3)`'s: introduce with a backslash, and put
standard **caret notation** — `cat -v`'s and `less`'s convention, dating to the
PDP-6 up-arrow the 1967 ASCII revision replaced with `^` — inside it.

| Input | Spelling | Source |
| --- | --- | --- |
| C0 (`U+0000`–`U+001F`) | `\^` + (code point + `0x40`); `ESC` is `\^[` | `vis(3)` + caret notation |
| `DEL` (`U+007F`) | `\^?` | `vis(3)` + caret notation |
| every other encoded code point | `\u` plus four hex digits; `U+202E` is `\u202E` | `vis(3)` shape, C# spelling |
| literal `\` | `\\` | `vis(3)` |

The `\uXXXX` form diverges from `vis(3)`'s meta notation deliberately: meta
notation describes a *byte* with its high bit set, and our input is a .NET
`string` of code points. `\uXXXX` is C#'s own spelling, and it is already the
spelling the rest of this repository uses for these characters.

**Do not introduce with the caret**, which is the obvious simplification and is
wrong. Caret notation alone is not invertible, and the collision is not exotic:
`U+001E` (RS) is `0x1E + 0x40 = 0x5E`, which *is* `^`, so RS spells `^^` — the
same as any escape of a literal `^`. `cat -v` lives with this because it never
claims to be reversible; `vis(3)` avoids it by introducing with a character
outside the caret image. That is the whole reason for the backslash, and it is
worth writing down because the caret-introduced version looks correct and
survives casual inspection.

**The exempt set is per-sink, and only ever shrinks the C0 part.** A metadata
name has no business containing `CR`, `LF`, or `TAB`, so a field sink encodes
them. Prose — a package description — is legitimately multi-line, so a prose
sink exempts those three and nothing else. `vis(3)` parameterizes exactly this
(`VIS_NL`, `VIS_TAB`, `VIS_SAFE`). No sink may exempt `Bidi_Control`: there is
no rendering context in which an artifact needs to reorder the reader's screen.

The properties that matter are `vis(3)`'s: the output is **inert**, **lossless**
(nothing is dropped, so the view still answers "what is actually in this
field"), and **invertible** (a decoder recovers the original exactly).
Neutralization has none of the three.

"Inert" is scoped, and the scope matters: it means no *terminal* interprets the
text as control, and no *bidi algorithm* reorders it. It does **not** mean the
text is safe to drop into a structured format. A `|` still breaks a Markdown
table cell, a backtick still opens a span, and a `"` still terminates a JSON
string — none of those is in any encoded category, and none should be, because
they are ordinary characters that the *serializer* is responsible for escaping
for its own grammar. Visual encoding and structural escaping are separate
obligations that compose; neither substitutes for the other, and structural
escaping stays mandatory in every mode, including raw.

Those three are an asserted property, so the encoding ships with the gate that
proves them: a decoder, plus a round-trip over every single code unit in
`U+0000`–`U+FFFF`, plus a round-trip-and-injectivity sweep over strings built
from the characters that can collide — `\`, `^`, `u`, `?`, `@`, `[`, hex
digits, and a representative of each encoded category — plus a paired-surrogate
case, so that the scalar-versus-code-unit bug above fails the gate instead of
shipping. The category rule needs its own gate, asserting membership for the
nine `rustc` code points and the twelve `Bidi_Control` code points by name
rather than by category lookup, so that a future narrowing of the rule fails
rather than silently stops covering them. Encode-without-a-decoder is not this
pattern, and an invertibility claim with no decoder in the test is not
evidence: a caret-introduced spelling passes every casual inspection and fails
this sweep on `U+001E`.

Because the encoding is inert, it needs no opt-in. It is the default on every
artifact-text path, with exactly one named opt-out
(`--dangerously-print-raw-text`) rather than a per-call-site choice. Gating it
behind an opt-in would buy no safety and would recreate the inheritance failure
described below — a path that forgets to ask for it. Nothing passes a flag to
make `System.Text.Json` escape `\u001b`.

### Failure messages carry no artifact bytes

A rejection names the **user-supplied** input — the path or coordinate the
caller passed — the rule that fired, and the location. It does not quote the
offending value. The rejected value is by construction the most hostile string
in the image, and an error path that echoes it re-opens on `stderr` exactly the
channel the check just closed.

### Status

The bounded-traversal budgets, parse-never-load architecture, and opt-in heaps
are implemented, with the gates and the one unverified property named above.
The trust and rendering axes, `--survey`, the visual-encoding spelling and its
decoder, and the failure-message rule are the **target model** and are not yet
implemented; today the projector neutralizes control characters unconditionally
and continues. See the threat model's open work.

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
  [--max-rows|-n N] [--start-row|-s N] [--references|-r Table:RowId]
  [--max-references N] [--overview|-i] [--heap Heap:Address]
  [--max-bytes N] [--max-chars N]`. Missing
  files, native images, and unreadable metadata surface as visible errors, never
  success-shaped empty output. `--references`, `--overview`, and `--heap` each
  select a different view and are rejected in combination rather than silently
  ranked.
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

Containment is inherited, not reimplemented — but only along paths that
actually go through the projection. `mdi` performs no escaping of its own, and
each projected view hands the renderer text the projection has already made
safe. That is what makes `mdi` the reference example of consuming the
projection: a consumer should render `MetadataValue` cases and add nothing,
rather than defend itself.

The cost of that arrangement is that the escaping lives in exactly one place —
the projector — so any text that reaches output *around* the projector is never
escaped at all. Nothing at the call site reveals it: rendering an already-safe
string and rendering a raw one are the same line of code.

That is not hypothetical. The metadata root's version stamp is an
artifact-derived counted string, and `MetadataImageInspector` reported it
straight into the image overview without projecting it, so a hostile assembly
could emit a live `ESC` in Markdown and TSV from both `mdi --overview` and the
CLI's `Metadata: Image` section. #3518 fixed it by routing the value through
the projector's escaper.

That fix was right, but the defect argues for more than itself. Containment
applied by *calling a function* is containment you can forget, and `string` is
the type of both a contained and an uncontained value, so neither a reviewer
nor the compiler can tell which one a given variable holds. The durable fix is
for artifact-derived text to be its own type that a renderer cannot accept as a
raw `string` — the shape `HardenedJson` already uses, where the guarantee comes
from choosing the type rather than from remembering the call. Auditing then
becomes a search for a type rather than an argument about coverage.

That is also why the property needs its own gate rather than a comment.
`MdiContainmentTests` splices a payload spanning all three control ranges the
projector recognizes — a live `ESC [ 3 1 m` sequence, `BEL`, `DEL`, and a C1
control — into a real `#Strings` entry *and* into the version stamp, then renders
the patched assembly through every view and format. The three views that carry
artifact text — table, heap, and overview — each assert both that no raw control
character survives and that the neutralized form of every control is present, so
they cannot pass by rendering nothing, and the multi-range payload means
narrowing containment to `ESC` alone fails rather than passes. Coverage is driven
from `MetadataTableFormat` itself, so a new format is gated on arrival. The
`--references` view renders only coordinates and counts, so it carries no
artifact text and is asserted against raw controls alone, as a regression net
rather than a payload-carrying case; the file says so.

That payload covers only what the projector recognizes today, which is `Cc`.
It contains no `Cf` character, so the gate would not notice a bidi override
reaching output. Widening the payload is part of adopting the category rule.

Those tests gate the *current* neutralizing behavior. Adopting the target model
changes what they assert — a control character in a name becomes a rejection
rather than a rendered substitution — but not that the property is gated.

The `mdv` oracle is a follow-up increment: because it diffs against the
projection **model** (not `mdi`'s rendered text), the renderer is free to be
human-friendly without weakening the oracle. Supported-table coverage is kept in
step with the tables `mdv` dumps, so every supported table's physical row count
is cross-validated against `mdv` for free on the product assembly.

## Implemented: random access for a browser host

The projection's other planned consumer is the wasm **Metadata Explorer**
(issue #3341), which browses tables interactively rather than dumping them.
That host imposes three constraints a batch dump does not, all met without
changing the model:

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

## Implemented: reverse references

Forward navigation is only half of browsing. A reader looking at `Field[1]` asks
"what declares this?", and looking at `TypeDef[5]` asks "what points here?".
Neither question is answerable by following the projection's edges, because
those edges only run one way.

`MetadataTableProjector.FindReferences(peReader, targetTable, targetRowId,
maxReferences)` inverts them, returning a `MetadataRowReferenceSet`: the
`MetadataRowLocation` of every row pointing at the target, with the pointing
column's index, its name, and whether the edge was a `Handle` or a `Range`.

The `Range` case is the load-bearing one. ECMA-335 does not give an owned row a
back-pointer to its owner: a `Field` is owned by whichever `TypeDef.FieldList`
run covers it, and a `Param` by whichever `MethodDef.ParamList` run covers it.
Reverse search over list columns is therefore how ownership is resolved at all —
not an extra convenience on top of handle search.

Two design points are deliberate and worth stating, because both look like
oversights:

- **`options.Tables` is not honored.** Like `ProjectRow`, the search is a query
  over the whole projection, not a projection of a selection. Narrowing the scan
  per call could report "nothing points here" while a pointer sat in an
  unsearched table, which is worse than a slower answer.
- **The projection's table coverage is itself a blind spot.** The scan cannot
  be wider than the projection, and the projection models a subset of ECMA-335's
  tables. A real assembly populates tables outside that subset — `NestedClass`,
  `MethodSemantics`, `InterfaceImpl`, `Property` and friends — so an edge living
  in one of them is invisible to the search. A nested type's declaring type is
  exactly such an edge. `UnscannedTables` names the populated tables the scan
  did not read in full, so the gap is disclosed rather than answered as an
  absence. Empty tables are excluded: they cannot hide a reference.
- **`UnscannedTables` is derived from the traversal, not declared.** The scan
  records a table only after examining every row the image says it has, and the
  blind spot is the populated tables missing from that record. Computing it from
  the list of tables the scan *intends* to visit would let the two drift, and a
  blind spot that under-reports is the whole failure this exists to prevent.
  Entering a table is deliberately not enough: a loop that stops part-way leaves
  rows unread, and an edge onto the target could sit in any of them. That also
  makes the budget interaction mostly fall out for free — a scan the budget
  stops leaves every table after it unsearched, and all of them are reported.

  Reaching the end of the row loop is *also* not enough, because the budget is
  checked inside the **column** loop: truncation on a table's final row leaves
  that row entered and abandoned part-way through its columns while the row
  counter still passes the row count, so the counter alone cannot tell a
  completed scan from one stopped on the last row.

  `Truncated` is too blunt to separate them, though, because of the boundary
  case: if the budget trips on the **last column of the last row**, every cell
  the table has was examined and the table genuinely was searched in full, even
  though the scan ended inside it. Reporting it unscanned there would be a false
  blind spot — it would tell the reader an unexamined cell could hide an edge
  when no cell went unexamined. So the scan tracks the precise fact instead:
  whether anything in this table was left unlooked-at, which is the columns
  after the stop on that row, or any row after it.

  That also keeps the malformed-cell blind spot sound. A stop before the last
  column leaves the remaining columns unchecked for malformed edges, so the row
  might belong in `UnreadableRows` without the scan knowing — but that is
  exactly the case where the table is reported unscanned, so the gap is still
  disclosed. When the stop is on the last column, every column was checked and
  the row's status is fully determined.
- **`UnscannedTables` covers unexamined cells, not just unread rows.** A table
  lands there for three different reasons — never entered, entered and stopped
  between rows, or entered and stopped between columns of its final row — and
  only the first two leave a whole row unread. The caveat is therefore worded
  around a cell the scan never examined, which is true of all three.
- **A table with an unreadable row is not an `UnscannedTables` entry.** The two
  blind spots partition the space rather than overlapping: `UnreadableRows` is
  for rows the scan **read but could not decode**, `UnscannedTables` for cells it
  **never examined**. A row whose read threw, or whose edge column decoded as
  `Malformed`, is named individually in `UnreadableRows` and forces `IsComplete`
  false, which is strictly more precise than implicating its whole table. Folding
  it into `UnscannedTables` would also print a false statement, since every row
  of that table was in fact read.
- **Signature blobs are not searched, and that limit is not detectable.** The
  scan matches `Handle` and `HandleRange` columns. A `TypeDefOrRef` coded token
  spelled inside a signature blob lives in a `Heap` column, which is correctly
  not an edge column, on a row of a table the scan reads in full. So no blind
  spot fires: the row is read, the table is searched, and the edge is simply not
  looked for. Among the tables modelled today those columns are
  `Field.Signature`, `MethodDef.Signature`, `MemberRef.Signature`,
  `TypeSpec.Signature`, `StandAloneSig.Signature` and
  `MethodSpec.Instantiation`. Unmodelled tables hold signature blobs too —
  `Property.Type` is one — but those tables are already disclosed by
  `UnscannedTables`, so modelling one later moves its signature column into this
  list rather than out of any disclosure. Decoding blobs is future work (see the
  reverse-reference tagging note above); until then the limit is disclosed
  **unconditionally** by the renderer rather than by a per-scan signal, because
  there is no per-scan signal to give.

  The caveat deliberately covers two unlike things. A signature blob spells a
  reference as a TypeDefOrRef coded **token**, which is a genuine missed
  row-to-row edge. A `CustomAttribute.Value` blob spells a `System.Type`
  argument as a serialized type **name** (ECMA-335 II.23.3) — `[My(typeof(Alpha))]`
  stores the bytes `0100 05 "Alpha" 0000`, with no token anywhere — so it is not
  a row-to-row edge at all and is out of scope for a search defined over tokens.
  A reader asking "what references this type?" is not served by that
  distinction, though, so the caveat names both rather than resting on it.

  The risk runs backwards here, which is why the unconditional caveat is not
  redundant. `IsComplete` is true exactly when every populated table happens to
  be modelled — that is, on small, simple assemblies, which are precisely the
  images where a blob edge is the *only* remaining way to miss a reference. A
  caveat conditioned on incompleteness would therefore go quiet exactly where it
  is most needed.
- **Blind spots are reported, not folded in.** `Truncated` marks a scan the
  result budget stopped, `UnreadableRows` lists rows whose edges could not be
  fully determined, `UnscannedTables` lists the populated tables the scan did not
  read in full, and `TargetExists` marks a target row id past the end of its
  table. `IsComplete` is true only when the scan hit none of the blind spots it
  can detect — and today that means it is **false for essentially every real
  assembly**, because the table-coverage blind spot always fires. That is the
  honest reading: until the projection covers every table, the search has not
  covered the whole image. `IsComplete` describes the scan, not the image: it
  cannot account for the signature-blob limit above, which no scan can detect.
  Callers that only want to know whether the scan itself finished should read
  `Truncated`. Unlike `MetadataTableTruncation`, `Truncated` carries no total: a
  stopped scan never learns how many references it did not reach.
- **A row id past the end of its table is answered, not rejected.** Asking what
  points at a row that does not exist is a well-formed question — a dangling
  edge points at exactly the rows that are not there, so the search must stay
  askable. What it must not do is answer "nothing points at this row", which
  claims the image was searched and came back clean. `TargetExists` records the
  distinction and takes `IsComplete` down with it, so an absent row reads as a
  question that could not be answered rather than as an answer.

`UnreadableRows` is subtler than "the row failed to read". The cell readers
**contain** a decode failure as a `Malformed` cell rather than throwing, so a
row holding a broken handle or list column reads back successfully and would
otherwise pass as fully searched — a missed reference that reads as an absent
one. A `Malformed` cell in an edge column therefore marks its row a blind spot.
The **column's declared kind decides, not the cell**: a `Malformed` heap,
scalar, or flags cell was never an edge and cannot hide a reference, so it is
not counted. A row stays a blind spot only once, and after its good edges have
been collected, so one broken column never costs the caller the edges that row
does have.

The search reuses the projection's single row-reading path rather than a faster
private one, so a `HandleRef` or `HandleRange` means the same thing in a search
result as in a rendered table. Cost is therefore proportional to the whole image
per query; whether that needs an index is a measurement question, filed with the
allocation work above rather than guessed at here.

The renderer and `mdi` carry the blind spots into every format:
`mdi --references TypeDef:5` prints them inline under the Markdown table, and
the TSV/JSONL streams report them on stderr, since a pure row stream cannot
distinguish a complete scan from a stopped one.

## Implemented: the image overview and heap random access

The projection covers *tables*. A metadata browser also shows the container —
heap sizes, stream and header facts, and which tables exist at all — and needs
to read a heap value the tables never point at. That is issue #3341's gap 5, and
it is net-new surface rather than a change to the projection.

`MetadataImageInspector.Describe(PEReader)` returns a `MetadataImageOverview`:
the metadata root's version, kind, offset, size and whether it carries an
assembly manifest; one `MetadataHeapSummary` per heap; one
`MetadataTableSummary` per ECMA-335 table; and `MetadataImageHeaders` for the
PE/CLI facts. It returns `null` for an image with no metadata, the same "not
applicable" signal `ProjectRow` uses.

Two decisions in that model are load-bearing.

**Row counts cover every table, not just the projected ones.** The overview
reports what is physically present and marks each table with `IsProjected`, so a
table with rows the projection does not model — `NestedClass`,
`MethodSemantics`, `Property` on a typical assembly — is visible as a coverage
gap. Listing only the projected tables would make an unmodelled table
indistinguishable from an empty one, which is the same success-shaped-absence
failure the reverse search avoids.

**Heap addressing is part of the model.** ECMA-335 addresses the String, Blob,
and UserString heaps by byte offset but the GUID heap by 1-based index into a
vector of 16-byte values, and SRM's `GetHeapOffset` follows suit, returning an
index for a GUID handle. `MetadataHeapAddressing` states which convention a heap
uses and `MaxAddress` applies it, so a caller cannot silently read a GUID
address as a byte offset.

`MetadataTableProjector.ReadHeapValue(peReader, heap, address, options)` is the
heap counterpart of `ProjectRow`. The address is exactly what
`MetadataValue.HeapReference.Offset` publishes, so a projected cell's offset
round-trips, and the result is the same `MetadataValue` shape a projected cell
carries — one renderer serves both. Address zero is every heap's nil value; an
address past the end yields `Malformed` rather than an empty value. Because the
tables never reference the `#US` heap, this is also the only way to browse the
user strings that IL points at.

What is deliberately *not* here: heap **enumeration**. SRM exposes no public way
to walk every entry of a heap, so `mdi`'s surface is overview plus random access,
and says so rather than faking a walk by scanning bytes. The `library` heap
sections go one step further without crossing that line — they list what the
projection can *prove* is in a heap, and name their coverage (see
[Heaps are not tables](#heaps-are-not-tables)).

Both facets hang off `AssemblyInspectionSession` (`MetadataImage()`,
`MetadataHeapValue(...)`), render through `MetadataProjectionRenderer`, and are
reachable from `mdi --overview` and `mdi --heap Heap:Address`. The overview
lists only tables that carry rows; the number omitted and any unmodelled table
with rows are reported as caveats — inline in Markdown, on stderr for the
machine formats.

## Measured: allocation shape

Issue #3341 lists eager row materialization as the memory axis to watch and
explicitly gates any change on a real measurement. The measurement now exists,
and it closes the gap **without** an allocation redesign.

`eng/measure-metadata-projection-allocation.cs` is a file-based app that reports
two different quantities, because they answer different questions: *allocated*
is total churn while projecting (a throughput and GC-pressure signal), while
*retained* is the **incremental managed heap** still reachable from the finished
projection after a forced collection.

Be precise about what *retained* does and does not include. It is a
`GC.GetTotalMemory` delta, so it counts managed objects only, and the input image
is allocated before the baseline is taken. A host holding one projection also
pays for two things this column deliberately excludes, because they are properties
of the input rather than of the projection:

| Also live, not in the `retained` column | Size |
| --- | --- |
| input image bytes (the host must hold these regardless) | 15.3 MB |
| SRM's native metadata block (unmanaged, invisible to the GC) | 3.3 MB |

So the browser-side arithmetic for one window is `15.3 + 3.3 + 0.1 MB`, not
`0.1 MB`. The `retained` column answers "what does *projecting* cost on top of
having the file open", which is the question that decides whether to change the
projection's representation.

Pinned input `Microsoft.NETCore.App/10.0.9/System.Private.CoreLib.dll`
(16,017,232 bytes), workstation GC, .NET 11.0.0. Reproduce with:

```bash
dotnet run eng/measure-metadata-projection-allocation.cs
```

| Scenario | Allocated | Retained | Retained bytes observed | Rows | Cells |
| --- | --- | --- | --- | --- | --- |
| control: `PEReader` only, no rows | 0.0 MB | 0.0 MB | 3,720 | — | — |
| full (`MaxRowsPerTable = int.MaxValue`) | 222.8–222.9 MB | 74.7 MB | 78,288,240–78,291,336 | 182,719 | 670,982 |
| `mdi` default (`MaxRowsPerTable = 4096`) | 81.4–82.3 MB | 19.8 MB | 20,754,744–20,757,840 | 45,501 | 145,332 |
| window 1000 | 17.4–17.6 MB | 5.0 MB | 5,205,704 | 12,002 | 38,014 |
| window 100 | 1.8 MB | 0.5 MB | 523,968 | 1,202 | 3,814 |
| window 100, `MethodDef` only | 0.1 MB | 0.1 MB | 63,760 | 100 | 600 |
| window 100 @ `MethodDef` row 40000 | 0.1 MB | 0.1 MB | 60,880 | 100 | 600 |

The probe prints raw bytes as well as megabytes so these claims can be checked
rather than inferred from a rounded figure, and the columns have genuinely
different reproducibility, which is why they are written differently:

- **Rows and cells** are exactly reproducible.
- **Retained** is exact for the windowed scenarios. The two large scenarios vary
  by a few KB across runs and machines, so they carry observed ranges; a single
  byte count for them would be false precision.
- **Allocated** is the noisy column. The `mdi` default was observed at
  85,322,584, 86,306,824, and 86,310,024 bytes by three different observers,
  each of whom saw a *stable* value within their own session.

The cause of the allocated spread was **not** identified, and the obvious
hypotheses were tested and failed. Disabling tiered compilation, tiered PGO,
QuickJit, and ReadyToRun, varying call-counting delays, adding post-warm-up
delays, and repeating the warm-up all left the value unchanged within a given
session. Treat allocated as an order-of-magnitude GC-pressure signal, not a
measurement, and retained as the number to reason about.

Deep paging is measured **inside a single large table** on purpose: an
all-tables window at a high start row looks cheap for the wrong reason, since
only the two tables that reach that depth contribute any rows at all. Windowing
into `MethodDef` at row 40,000 costs no more than at row 1 — in fact slightly
less (60,880 vs 63,760 bytes), since those rows carry shorter names — so cost
tracks the size of the window, not its distance into the table.

The control row is load-bearing, and a second experiment makes it stronger.
The probe projects inside a scope, lets the projection become unreachable while
the `PEReader` stays live, and re-measures; a `WeakReference` confirms the
projection really was collected. With the projection reachable, 74.7 MB. With it
unreachable and the reader still open, **0.0 MB** (3,720 bytes).

Read that as a measurement, not a proof of a universal property, and be precise
about what it can and cannot see. A delta cannot distinguish the projection from
some *other* root, and it is blind in one specific direction: the probe warms up
**before** taking the baseline, so any process-wide static cache populated during
warm-up is already inside the baseline and stays invisible when the projection
drops. A reviewer demonstrated exactly this — a 10 MB static cache filled during
warm-up left the post-drop delta at 24 bytes.

So the experiment establishes something narrower but still useful: for this input
and runtime, nothing row-proportional that was allocated **after the baseline**
survived dropping the projection. What that covers follows from how the probe is
built rather than from any claim about SRM. The warm-up uses a separate
`PEReader`, so lazy state held *per reader instance* is created after the
baseline and would be visible; a process-wide static cache populated during
warm-up would not be. Which of those SRM actually uses was not verified here, and
settling it would need source inspection or a cold-baseline run.

### What the numbers say

A full projection retains **74.7 MB of managed heap for a 15.3 MB image**, at
117 bytes per cell. Adding the image and the unmanaged metadata block, a host
holding a full CoreLib projection is carrying roughly 93 MB. #3333 measured
1.6 GB as fatal in a browser tab, so this does not on its own prove a full
projection is undeliverable — but it scales with the assembly, it is paid before
anything is rendered, and it buys rows nobody asked for.

Because an explorer never needs them. The row window landed for exactly this
reason, and it is **150x cheaper**: paging 100 rows across all tables retains
0.5 MB, and the realistic explorer interaction — one table, one window — retains
**0.1 MB**, whether that window is at the start of the table or 40,000 rows in.

The scope of that conclusion is the measured workload: a windowed, table-selected
projection. It says the explorer does not need a representation change to be
built. It does not say a compact or lazy representation could not help a
different workload, and the full and `mdi`-default rows show workloads that are
genuinely expensive today.

### Negative results worth keeping

**`ReadOnlySpan<T>` specifically is the wrong tool for this problem.** It is
worth stating plainly because it was the intuitive candidate. A span is a
`ref struct`: it cannot be stored in a reference-type field, cannot live in an
`ImmutableArray`, and cannot cross an `await` or `yield`. It *can* be returned
when it is backed by longer-lived memory — which is why a span-returning
accessor appears below as still open — but it cannot itself be the thing the
caller holds. The projection is a *retained object graph handed to a caller that
holds it*, precisely the shape a span cannot express. Spans help transient decode
paths that borrow and discard.

That argument is about `ReadOnlySpan<T>` and does not generalize to borrowing or
laziness as such. These remain open and are simply not needed yet:

- `ReadOnlyMemory<T>` **can** be stored in a field and retained, and could back
  cell text without per-cell string objects.
- Struct cells would remove one object header per cell — the 44 MB that is not
  string inventory is spread across 670,982 cell objects and their rows.
- Formatting display text lazily from the `MetadataReader`, or a flyweight over
  it, would trade retention for recomputation.
- A span-returning *accessor* over compact backing storage is compatible with a
  retained model, unlike a span-shaped *cell*.

**Eager display text accounts for 41% of the retained graph, and is still not
worth changing for the explorer.** Attribution of the 74.7 MB, counted by
**object identity** with per-object x64 string sizing — `Align8(22 + 2 * Length)`
charged for each string separately, since rounding a summed character count once
at the end understates the total. Counting by property instead would double-count
badly: `String`, `UserString`, and `Guid` cells pass the *same instance* as both
`Text` and `Preview` (136,299 cells do this), and only `Blob` cells hold a
distinct preview.

| Component | Distinct string objects | Chars | Size |
| --- | --- | --- | --- |
| `Handle.Display` | 79,870 | 5,455,583 | 12.3 MB |
| heap `Text`/`Preview` | 239,289 | 3,347,232 | 12.1 MB |
| `Flags.Decoded` | 53,106 | 1,645,153 | 4.4 MB |
| `Scalar.Display` | 42,453 | 423,938 | 1.9 MB |
| total | | | **30.7 MB** |

Two caveats keep this honest. Identity counting charges a string the runtime had
already cached — small integers and enum names — as though the projection owned
it, and whether such a string is shared depends on what ran earlier in the
process, so totals drift slightly between runs (32,216,712 and 32,217,048 bytes
observed). The rounded 30.7 MB is reproducible; the exact byte count is not. And
this measures only the string inventory: the remaining ~44 MB is the non-string
remainder — cell and row objects, their fields, and backing arrays — which was
not decomposed further.

Two cheap wins are visible and were deliberately **not** taken:
`Scalar`/`Flags` strings collapse to 40,357 distinct values, and the 14,217
`Nil` cells are stateless and identical so could be a singleton.

They were left alone because of where they pay off, and that boundary should be
stated honestly rather than dismissed. `mdi` defaults to all tables at 4,096 rows
each, so `mdi <assembly>` **is** the 19.8 MB row — a real consumer, not a
hypothetical one, and interning would measurably help it. The wins are declined
for now because they are unnecessary for the explorer this work is for, where the
same optimizations are a rounding error on 0.1 MB, and because interning adds
caching state and lifetime questions to the projector. If `mdi`'s default path
becomes a memory complaint, this table says to start with `Scalar`/`Flags`
interning and the `Nil` singleton, and the probe says how to prove it moved.

## Implemented: hex table selection

`-S "Metadata: 0x02"` and `-S "Metadata: TypeDef"` are the same selector. The
motivation is mechanical: this tool's own output prints hex tokens, so a reader
following a `0x02000015` reference already has the table index in hand and
should not have to translate `0x02` to `TypeDef` before asking for the table.

The alias rewrites the **input selector**; it does not register a second
section. That is the whole design, and it is what makes the two spellings *one*
section rather than two that happen to render alike. A hex alias registered in
the catalog would print its own heading, sort independently in the section
order, count separately under `--count`, and appear as a second entry under
`-D`. Rewriting at the boundary means everything downstream — the orderer, the
heading, `--count`, the document schema, the effective-section cache key — only
ever sees the canonical name, so those failure modes are not merely untested but
unreachable.

`MetadataSectionNames.TryGetTable` therefore stays canonical-only. Teaching it
hex as well would put alias resolution in two places, and would make
`IsMetadataSection` claim a name that selection resolution — which matches
against the canonical catalog — would still reject.

Three rules follow from treating the hex form as an address rather than a name:

- **Hex carries its `0x`.** A bare `02` is a table *name* position, and
  inferring a radix would let one spelling mean two things. This matches the
  `--heap` address rule.
- **Only projected tables resolve.** The alias table is derived from
  `MetadataTableProjector.ProjectedTables`, the same array the canonical names
  come from, so a table the projection does not cover cannot become selectable
  by its index. The rejection names the projected tables in both spellings,
  because a caller who pasted an index is thinking in hex.
- **A metadata token is not a table.** `0x02000015` addresses a *row*; its high
  byte is the table. Width is checked **textually** — a table index is one byte,
  hence one or two hex digits — because a numeric range check alone accepts an
  eight-digit token whose value happens to fit, so `0x00000001` (a Module row
  token) would resolve as table `0x01`, TypeRef.

A bad index fails the run even beside a selector that does match — a deliberate
divergence from the unknown-*name* rule, which tolerates a miss when something
else matched. That tolerance exists for names that may exist in one inspected
assembly and not another; a hex index outside the projection is not that, since
no image can ever supply it. Tolerating it would silently drop a selector the
caller definitely got wrong.

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
- **Row addressing.** `--row` currently selects the Nth row that survived an
  invisible printability filter, so it neither names `MethodDef[3]` nor matches
  the row a reader counted in the output; `--where` carries row addressing
  today. The rework in
  [#3364](https://github.com/richlander/dotnet-inspect/issues/3364) has since
  landed: numbering now addresses the row a reader counts, and `--rows` takes a
  value of `N`, `N..M` (inclusive), `N+K` (start plus count), or `N..`, with
  `--head`/`--tail` reduced to a direction. This lens inherits that addressing.

Resolved:

- **Lens home.** A `@Metadata` section category, not a focused flag and not a
  dedicated command. Metadata tables introduce no new currency, so section
  selection already addresses them. This also avoids a collision: `--table` is
  already taken as a presentation modifier ("render as a pretty table").
- **Heap surfacing flags.** Per-heap listings are ordinary sections
  (`Metadata: #Strings`). Reading a specific address is a coordinate, so it gets
  a carrier: `--heap "#Strings:0x1a4"`.
- **What a heap listing contains.** Not a byte scan, and not nothing. Each heap
  is listed by the strongest honest means it admits — complete for `#GUID`,
  referenced-values-only for `#Strings` and `#Blob`, nothing at all for `#US` —
  and the listing states which, so a partial view is never mistaken for a whole
  one. See [Heaps are not tables](#heaps-are-not-tables).
- **Table selection grammar.** Both, since output already prints hex tokens and
  users will paste them: `TypeDef` and `0x02` address the same table. See
  [Hex table selection](#implemented-hex-table-selection).
