# Output shapes

dotnet-inspect output narrows through a small ladder of **shapes**. Markout
defines the shapes and produces them; dotnet-inspect flags choose which rung you
land on. Naming the ladder gives a shared vocabulary for the output flags
(`-S`, `--fields`/`--columns`, `--tsv`/`--jsonl`, `--count`, `-n`/`--rows`,
`--print`, `--bare`, …) and for deciding what a new flag should
do.

Related docs:

- [Output composition model](output-composition.md) — section selection, filtering, and writer capabilities
- [Rendering model](rendering-model.md) — verbosity vs mode-switch flags
- [Schema query](schema-query.md) — `-D` discovery of sections and columns
- [Command model](command-model.md) — command surface and shared options

## The shape ladder

Each shape is a narrowing of the one above it. You start at a Document and
descend to a Scalar by selecting a section, then columns, then collapsing.

| Shape | What it is | Example |
| --- | --- | --- |
| **Document** | many sections | a full `library` / `type` report |
| **Table** | one section: columns × rows | the `Top Leverage` section |
| **Vector** | one column: many rows of a single field | just the `Member` column |
| **Scalar** | a single value, or a text/doc blob | `1234`, a README, a `///` summary |

- **Document → Table.** A Document is a sequence of sections. Selecting one
  section leaves a single Table (or other single-section payload).
- **Table → Vector.** A Table is columns × rows. Projecting to one column
  leaves a Vector — many rows of a single field.
- **Vector → Scalar.** Collapsing a Vector (count it, or take one row) yields a
  Scalar. A Scalar is also the natural shape of a non-tabular payload: a count,
  a single field value, or a text/documentation blob (a README, a decompiled
  `.cs` body, an XML-doc `///` comment).

Most sections are Tables, but a section can also be a key-value field set, a
list, a code/text blob, or a tree (for example a call graph). Those are still
"one section" — the Table rung — and they collapse to Scalars the same way.

## Three flag families

A flag can contribute in one of three ways:

- **Shape selectors** narrow the requested shape (`-S`, `--fields`/`--columns`,
  `--count`, `-n 1`).
- **Presentation modifiers** change how a selected payload is rendered without
  changing the shape (`--bare`, `--markdown`, `--json`, `--table`, `--tsv`,
  `--jsonl`, `--plaintext`, `--no-headers`).
- **URL-shape modifiers** change only the form of GitHub URLs emitted as data
  (`--raw`, `--blob`). They are orthogonal to the output-shape ladder.

### Coordinate carriers sit before the ladder

A fourth kind of flag does not walk the ladder at all: it *supplies an input the
command has no other way to express*, and in doing so changes which sections
exist to be selected. `--il-offset` and `--heap` are the members of this family.

A coordinate carrier is the right shape for a flag only when the input is a
genuinely new currency — a value that is not a section name, a column name, or a
row. An IL coordinate (`0x06000002+0x1`) and a heap address (`#Strings:0x1a4`)
qualify; a table name does not, because a table is already a section and `-S`
already addresses sections.

Carriers behave consistently:

- The sections they enable are **discoverable only when the carrier is present**,
  so `-D` reflects the carrier (see the IL-offset case study below).
- Absent the carrier, requesting a coordinate-scoped section is an error that
  names the missing carrier, for example
  `IL coordinate sections require --il-offset`.
- Once the carrier resolves, its sections are ordinary sections: they obey `-S`,
  `--columns`, `--count`, and the rest of the ladder like any other.

Prefer a section, a category, or `--where` before reaching for a new carrier.
The bar is a new currency, not merely a new thing to look at.

## How Markout produces the shapes

Markout serializes a view object into a **Document** of **Sections** and renders
it with a chosen **formatter**. The shapes map onto Markout concepts directly:

| Shape | Markout construct |
| --- | --- |
| Document | the serialized view: an ordered set of `[MarkoutSection]` members |
| Table | one section rendered as a table (`WriteTable`: headers + rows), a field set, a `WriteList`, a `CodeSection`, or a tree |
| Vector | a table projected to one column, or a single-column `WriteList` |
| Scalar | a single cell, a `CodeSection` payload, or a row count |

Two Markout knobs do the narrowing and the formatting:

- **Projection** (`MarkoutWriterOptions.Projection`) selects which columns/fields
  a section emits — the Table → Vector step.
- **Table mode** (`MarkoutWriterOptions.TableMode`) picks how tables render:
  Markdown (default), `MarkoutTableMode.Tsv`, or `MarkoutTableMode.Jsonl`.

Formatters decide presentation, not content:

- **`MarkdownFormatter`** — the rich, multi-section, verbosity-aware Document
  format. The canonical shape; everything else is a projection or reduction of
  it.
- **`TableFormatter`** — a single-section tabular renderer (pretty table, `--tsv`,
  `--jsonl`). Because it renders one section at a time, its output is always a
  single Table (or Vector).
- Tree and table writers render their own narrow shapes (a call graph tree, a
  table row) and have no verbosity dial — they either show a thing or they
  do not (see [rendering-model.md](rendering-model.md)).

## How dotnet-inspect flags select a shape

Flags are how the user (or an agent) walks the ladder. The important distinction
is that a shape selector changes what data is requested, while a presentation
modifier changes how a selected payload is rendered.

### Shape selectors (narrow the shape)

| Target shape | Flags |
| --- | --- |
| Document | default view; `-v:q`/`-v:m`/`-v:n`/`-v:d` (breadth presets); `-S a,b` (multiple sections) |
| Table | `-S OneSection` (a single section) |
| Vector | `--fields X` / `--columns X` (project to one column) |
| Scalar | `--count` (row count); `-n 1` (one row) |

`-D`/`--discover` is orthogonal: it does not render the subject, it lists the
*available* shapes — the sections of the Document and the columns of a Table (see
[schema-query.md](schema-query.md)).

### Printable payload projections

`--print` projects a selected row's declared printable payload. It is unary, but
it does not mean "take the first row." Cardinality is resolved after section
selection, filtering, and printable-capability filtering:

| Printable rows | `--print` | `--print --row N\|first\|last` |
| ---: | --- | --- |
| 0 | Error: the selected shape is not printable. | Error. |
| 1 | Print the one payload. | Print row `1`, `first`, or `last`; any other index is an error. |
| More than 1 | Guidance error requiring `--row`. | Print exactly the selected printable row. |

`--print` resolves exactly one payload. There is no fan-out gesture: printing
more than one document at a time is not currently expressible.

Numeric `--row N` is one-based and counts printable rows, not every row in the
selected table. `first` and `last` are stable aliases for the endpoints of that
printable-row sequence.

Because `--print` is exactly-one, failing to acquire the selected row's payload
is an error, not an omission: it reports the failure and exits non-zero rather
than rendering an empty or short success. This covers acquisition for the
selected row; whether a section producer declares a row at all is that
producer's concern.

`-n N` / `--head N` and `--tail N` are rendered-line windows applied after
printable-row cardinality is resolved and the payload is fetched. They do not
select rows:

```text
--print --head 1
  multi-row selection -> error; does not choose the first row

--print --row 2 --head 20
  select printable row 2 -> fetch one payload -> render its first 20 lines
```

`--rows` changes head/tail from rendered-line windows into per-table data-row
windows:

- `--rows --head N` keeps the first N data rows;
- `--rows --tail N` keeps the last N data rows.

Both row-window forms are incompatible with `--print`;
`--row N|first|last` is the explicit printable-row selector. The CLI implements
both head and tail data-row windows symmetrically.

This policy deliberately rejects implicit-first behavior. Row order may change
with filtering, producer evolution, or package versions, and choosing the first
row could silently fetch the wrong document. It also rejects implicit fan-out:
one `--print` authorizes exactly one declared payload fetch.

Printability is a row capability, not a property implied by Table or Vector
shape. `--print` may not:

- reinterpret an address row as the artifact at that address;
- evaluate an unevaluated address;
- change operation arity or primary-subject acquisition cardinality.

A version-address Vector is therefore not printable merely because each row
could name a package. The explicit transition to that package artifact remains
`package Package@version`. Likewise, printing a timeline may use only declared
payloads on already evaluated rows; it cannot probe missing cells.

### Presentation modifiers (render the chosen shape)

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | the whole Document as one JSON object |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / `--head N` / numeric shorthand such as `-20` | keep the first N rendered output lines unless `--rows` is active |
| `--tail N` | keep the last N rendered output lines unless `--rows` is active |
| `--rows --head N` | keep the first N **data rows per table**, across Markdown, TSV, and JSONL |
| `--rows --tail N` | keep the last N **data rows per table**, across Markdown, TSV, and JSONL |
| `--bare` | render the selected payload without document decoration; it changes presentation only, not the selected shape |
| `--plaintext` | render a whole-document plain-text view; distinct from `--bare` |

`--tsv`/`--jsonl`/`--table` render **one section at a time**, so they require a
Table-or-narrower selection; multi-section (Document) output stays in Markdown or
JSON.

### URL-shape modifiers (orthogonal to the ladder)

| Flag | Effect |
| --- | --- |
| `--raw` | emit GitHub URLs as raw/fetchable URLs (default) |
| `--blob` | emit GitHub URLs as browser-friendly `/blob/` URLs |

These flags are orthogonal to the output-shape ladder. They change the form of
GitHub URLs that the tool emits as data (source links, sample links, link rows),
but they do not change the selected shape or the framing around the payload.
The safe default direction is `blob → raw`; the reverse is a browser-oriented
mode and should not be applied to user-authored README/markdown content unless a
separate opt-in path is introduced.

### Walking the ladder — one example

```bash
# Document: the whole assembly report
library MyLib.dll

# Table: one section
library MyLib.dll -S "Top Leverage"

# Vector: one column of that table
library MyLib.dll -S "Top Leverage" --fields Member --tsv

# Scalar: collapse the table to a count …
library MyLib.dll -S "Top Leverage" --count
# … or render a blob payload without decoration
member MyType Method:1 --library MyLib.dll -S "Decompiled Source" --bare > Method.cs
```

### Case study: IL offset as a shape catalogue

`library --il-offset` is a compact example of the shape ladder because one
resolved coordinate can expose multiple sibling sections. The source-location
section is useful as a human fact sheet, a row, a scalar, a URL, a path, or a
source-line payload; the member-context section projects the same coordinate to
the owning type and method; the instruction-context section projects it to the
exact IL instruction; the exception-context section appears when the coordinate
falls inside protected exception-handling regions; callsite and return-address
sections explain call-like operations and stack-frame return addresses.

The default stays evidence-oriented and renders all applicable coordinate-scoped
sections:

```bash
dotnet-inspect library My.dll --il-offset 0x06000002+0x1
```

```md
## Source Location

| Field | Value |
| ----- | ----- |
| Method | My.Type.Method |
| Token | 0x6000002 |
| IL Offset | 0x1 |
| File | /_/src/Foo.cs |
| Line | 42 |
| Url | https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42 |

## Member Context

| Field | Value |
| ----- | ----- |
| Assembly | My.Assembly |
| Type | My.Type |
| Type Kind | class |
| Member | My.Type.DoWork |
| Signature | int DoWork(int value) |
| Member Kind | method |
| Visibility | public |
| Static | No |
| Async | State machine |
| Metadata Token | 0x6000002 |
| IL Offset | 0x1 |

## Instruction Context

| Field | Value |
| ----- | ----- |
| IL Offset | 0x1 |
| Boundary | Exact |
| Opcode | callvirt |
| Operand Kind | Method |
| Operand | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Next Offset | 0x6 |
| Length | 5 |
| Block | 0 |
| Terminates Block | No |
| Falls Through | Yes |

## Exception Context

| Region | Context | Clause | Try Range | Handler Range | Caught Type |
| ------ | ------- | ------ | --------- | ------------- | ----------- |
| 1 | try | catch | IL_0010..IL_0045 | IL_0045..IL_0070 | System.TimeoutException |

## Callsite Context

| Field | Value |
| ----- | ----- |
| Call Offset | IL_0001 |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Return Address | IL_0006 |
```

If the coordinate is the return address after the call, the applicable section
changes:

```md
## Return Address Context

| Field | Value |
| ----- | ----- |
| IL Offset | IL_0006 |
| Call Offset | IL_0001 |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
```

Coordinate-scoped sections are discoverable only when the coordinate carrier is
present:

```bash
dotnet-inspect library My.dll -D
# Source Location, Member Context, Instruction Context, Exception Context,
# Callsite Context, and Return Address Context are omitted.

dotnet-inspect library My.dll --il-offset 0x06000002+0x1 -D
# Source Location
# Member Context
# Instruction Context
# Exception Context (only when applicable)
# Callsite Context (only when applicable)
# Return Address Context (only when applicable)
```

The source-location section then projects cleanly:

```bash
# Scalar
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Source Location" --fields Line --value
# 42

# URL vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Source Location" --urls
# https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42

# Path vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Source Location" --paths
# /_/src/Foo.cs

# Printable payload: the raw resolved source line
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Source Location" --print
#         return JsonSerializer.Serialize(value, options);

# Singleton count
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Source Location" --count
# 1
```

This keeps the concerns separate: the default fact section shows all
symbolication evidence, `Member Context` shows the owning metadata context,
`Instruction Context` shows the exact IL operation, `Exception Context` shows
active exception-handling regions, `Callsite Context` shows the call-like
operation at the coordinate, `Return Address Context` points back to the prior
call, `--urls` returns the anchored source location, `--paths` returns the PDB
document path, and `--print` returns the raw payload at the location rather than
a decorated snippet.

## Design discipline for future flags

The stable vocabulary is:

- `--count` is a shape-reduction selector: it collapses a selected table/vector to a
  single scalar count.
- `--print` is an exactly-one row-payload projection: it never chooses the first
  of multiple printable rows implicitly, does not make non-printable rows
  printable, and does not evaluate new addresses.
- `--head` / `--tail` are post-projection line windows: they do not select rows
  or constrain payload acquisition.
- `--rows` promotes head/tail to first/last data-row windows, but those windows
  remain presentation limits rather than printable-row selectors.
- `--bare` is a presentation modifier: it strips the surrounding framing from an
  already-selected payload.
- `--raw` / `--blob` are URL-shape modifiers: they control the form of emitted
  GitHub links, not the shape of the payload itself.
- `--plaintext` remains distinct from `--bare`; if it stays in the product, it is
  a whole-document plain-text rendering mode rather than a bare-payload mode.
- `--il-offset` / `--heap` are coordinate carriers: they supply an input that has
  no other expression and gate the sections it makes meaningful. They do not
  narrow a shape, and a flag qualifies only if its input is a new currency.

New flags should fit one of those buckets rather than blending concepts.
