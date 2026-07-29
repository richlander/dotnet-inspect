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

## Flag families

Three families walk the shape ladder, and a fourth sits before it. A flag in one
of the ladder families contributes in one of three ways:

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
exist to be selected. The IL coordinate is the family's one implemented
currency; `--heap` (see
[metadata-table-projection.md](metadata-table-projection.md)) is designed to be
the second.

The family is counted in currencies, not flags, because one currency can have
more than one spelling. The IL coordinate has two: `--il-offset` takes a single
coordinate, and `--il-offsets` takes a file of them for batch reporting. They
are mutually exclusive (`--il-offset cannot be combined with --il-offsets`) and
carry the same currency, so they are one member of this family rather than two.

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
selection and filtering:

| Rendered rows | `--print` | `--print --row N\|first\|last` |
| ---: | --- | --- |
| 0 | Error: the selected section has no rows. | Error. |
| 1 | Print the one payload. | Print that row by its number, `first`, or `last`; any other number is an error. |
| More than 1 | Guidance error requiring `--row`. | Print exactly the selected row. |

`--print` resolves exactly one payload. There is no fan-out gesture: printing
more than one document at a time is not currently expressible.

Numeric `--row N` addresses a row by its position in the rendered section,
counting from 1. Sections do not print a row-number column, so N is the number
the reader arrives at by counting rows top to bottom — which is precisely why it
has to be stable: it is not a position within a filtered subsequence, and
printability does not renumber anything. A row that declares no payload still
occupies its number, and selecting it reports that it has no document rather
than silently sliding to a neighbour. `first` and `last` are the endpoints of
the rendered sequence, so when a projection skips rows they resolve to the
first and last numbers actually present rather than to `1` and the row count.
Structured output makes the number explicit — `--jsonl` and `--json` emit it as
`row` — and error messages name the addressable numbers, so a projection with
gaps stays navigable.

This is the one rule that makes the ordinal trustworthy. Numbering by position
in a filtered list is wrong in the worst way available: it returns a real row,
so nothing looks broken, and the reader has no way to recover the sequence being
indexed. Addressing by rendered position can only ever hit the intended row or
report a miss.

Because `--print` is exactly-one, failing to acquire the selected row's payload
is an error, not an omission: it reports the failure and exits non-zero rather
than rendering an empty or short success. This covers acquisition for the
selected row; whether a section producer declares a row at all is that
producer's concern.

`-n N` and `--tail` are rendered-line windows applied after
row cardinality is resolved and the payload is fetched. They do not
select rows:

```text
--print -n 1
  multi-row selection -> error; does not choose the first row

--print --row 2 -n 20
  select row 2 -> fetch one payload -> render its first 20 lines
```

`--rows <spec>` switches to per-table data-row windows and carries its own
count, so three concerns stay on three flags: `--rows` sets the unit,
its value sets the count or the rows, and `--head`/`--tail` set the direction.

- `--rows 6` keeps the first six data rows; `--rows 6 --tail` keeps the last six.
- `--rows 2..10` keeps the rows numbered 2 through 10 inclusive — nine rows.
- `--rows 2+10` keeps ten rows starting at row 2.
- `--rows 10..` keeps row 10 through the last row.

A count and a range are different kinds, not two spellings of one: a count
anchors to an end and a range does not, so `--rows 2..10 --tail` is rejected
rather than silently resolved. Bare `--rows` is an error — it once meant
"interpret `-n` as rows", which put the count on a different flag than the unit.

Both row-window forms are incompatible with `--print`;
`--row N|first|last` is the explicit row selector. The CLI implements
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

### A payload projection is never silently dropped

`--print`, `--value`, `--urls`, `--paths`, and `--count` reshape the payload, so
a render path that ignores one answers a question the caller did not ask while
still exiting 0. That failure is invisible to exit-code checks and to tests that
only cover the unprojected path.

Every accepted payload projection must therefore end in one of two outcomes: the
payload is projected, or the command reports why it cannot be and exits non-zero.
Rendering the full unprojected shape is not a third option. The requirement is
enforced structurally rather than per command — the request is recorded from the
parse result and the projection writers report which projection they honored, so
a route that drops one fails loudly instead of shipping the wrong payload.

The whole-surface type listing (`type` with no type name — the Classes, Structs,
Interfaces, Enums, and Delegates sections) is a name table that exposes no
printable payload, so `--print`/`--value`/`--urls`/`--paths` there is rejected up
front rather than dumping the full surface and then tripping this audit. Inspect
a single type (for example `type <Name>`) to project a member payload. `--count`
is the one payload projection the surface does honor.

Writers report *which* flag they honored rather than merely acknowledging one.
A writer can be reached for more than one reason — the print writer also serves
`--bare` — so an untyped signal would let it satisfy an unrelated request and let
that drop escape.

The projections are mutually exclusive. Two of them cannot both shape one
payload, so a combination is rejected before the command runs rather than
resolved by discarding one.

### Presentation modifiers (render the chosen shape)

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | render the selected shape as JSON: the whole Document when no narrower shape is selected, otherwise the projected payload (`--print`, `--value`, `--urls`, `--paths`). A column projection (`--fields`/`--columns`) does **not** compose with `--json`: JSON renders the whole document and has no column-slicing facility, so the combination is rejected rather than silently dropped — use `--tsv`/`--jsonl`/`--table` to project columns, or add `--value`/`--print` to project a payload (`--fields` then picks which column feeds it). |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / numeric shorthand such as `-20` | keep the first N rendered output lines |
| `-n N --tail` | keep the last N rendered output lines |
| `--rows N` | keep the first N **data rows per table**, across Markdown, TSV, and JSONL |
| `--rows N --tail` | keep the last N **data rows per table** |
| `--rows N..M` / `--rows N+K` / `--rows N..` | keep the **rows those numbers name**, inclusive; absolute, so no direction applies |
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
## Context: Source Location

| Field | Value |
| ----- | ----- |
| Method | My.Type.Method |
| Token | 0x6000002 |
| IL Offset | 0x1 |
| File | /_/src/Foo.cs |
| Line | 42 |
| Url | https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42 |

## Context: Member

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

## Context: Instruction

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

## Context: Exception

| Region | Context | Clause | Try Range | Handler Range | Caught Type |
| ------ | ------- | ------ | --------- | ------------- | ----------- |
| 1 | try | catch | IL_0010..IL_0045 | IL_0045..IL_0070 | System.TimeoutException |

## Context: Callsite

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
## Context: Return Address

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
# Context: Source Location, Context: Member, Context: Instruction, Context: Exception,
# Context: Callsite, and Context: Return Address are omitted.

dotnet-inspect library My.dll --il-offset 0x06000002+0x1 -D
# Context: Source Location
# Context: Member
# Context: Instruction
# Context: Exception (only when applicable)
# Context: Callsite (only when applicable)
# Context: Return Address (only when applicable)
```

The source-location section then projects cleanly:

```bash
# Scalar
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --fields Line --value
# 42

# URL vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --urls
# https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42

# Path vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --paths
# /_/src/Foo.cs

# Printable payload: the raw resolved source line
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --print
#         return JsonSerializer.Serialize(value, options);

# Singleton count
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --count
# 1
```

This keeps the concerns separate: the default fact section shows all
symbolication evidence, `Context: Member` shows the owning metadata context,
`Context: Instruction` shows the exact IL operation, `Context: Exception` shows
active exception-handling regions, `Context: Callsite` shows the call-like
operation at the coordinate, `Context: Return Address` points back to the prior
call, `--urls` returns the anchored source location, `--paths` returns the PDB
document path, and `--print` returns the raw payload at the location rather than
a decorated snippet.

## Design discipline for future flags

The stable vocabulary is:

- `--count` is a shape-reduction selector: it collapses a selected table/vector to a
  single scalar count.
- `--print` is an exactly-one row-payload projection: it never chooses the first
  of multiple rows implicitly, does not make rows without a payload printable,
  and does not evaluate new addresses.
- `--head` / `--tail` name a direction, not a count. Outside `--rows` they
  choose which end of the rendered lines `-n N` keeps; they do not select rows
  or constrain payload acquisition.
- `--rows` makes the window a first/last or absolute data-row window, but those
  windows remain presentation limits rather than row selectors.
- `--row` addresses a rendered row by its position in the section, counting from
  1. Any future selector that takes an ordinal joins this rule: the number a
  reader arrives at by counting rows is the number that can be addressed, and no
  filter may renumber it.
- `--bare` is a presentation modifier: it strips the surrounding framing from an
  already-selected payload.
- `--raw` / `--blob` are URL-shape modifiers: they control the form of emitted
  GitHub links, not the shape of the payload itself.
- `--plaintext` remains distinct from `--bare`; if it stays in the product, it is
  a whole-document plain-text rendering mode rather than a bare-payload mode.
- `--il-offset` / `--il-offsets` are coordinate carriers: they supply an input
  that has no other expression and gate the sections it makes meaningful. They
  do not narrow a shape, and a flag qualifies for this family only if its input
  is a new currency. Both spell the same currency, so they are one member;
  `--heap` is the designed second.

New flags should fit one of those buckets rather than blending concepts.
