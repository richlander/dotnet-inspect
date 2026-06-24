# Output Shapes

dotnet-inspect output narrows through a small ladder of **shapes**. Markout
defines the shapes and produces them; dotnet-inspect flags choose which rung you
land on. Naming the ladder gives a shared vocabulary for the output flags
(`-S`, `--fields`/`--columns`, `--tsv`/`--jsonl`, `--count`, `-n`/`--rows`,
`--bare`, …) and for deciding what a new flag should do.

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
- Tree and one-line writers render their own narrow shapes (a call graph tree, a
  one-line row) and have no verbosity dial — they either show a thing or they
  do not (see [rendering-model.md](rendering-model.md)).

## How dotnet-inspect flags select a shape

Flags are how the user (or an agent) walks the ladder. They split into two
groups: **selectors** that narrow the shape, and **format** flags that choose how
the chosen shape is rendered.

### Selectors (narrow the shape)

| Target shape | Flags |
| --- | --- |
| Document | default view; `-v:q`/`-v:m`/`-v:n`/`-v:d` (breadth presets); `-S a,b` (multiple sections) |
| Table | `-S OneSection` (a single section) |
| Vector | `--fields X` / `--columns X` (project to one column) |
| Scalar | `--count` (row count); `-n 1` (one row); `--bare` (a single content/`CodeSection` payload, e.g. redirect `Decompiled Source` to a `.cs` file) |

`-D`/`--discover` is orthogonal: it does not render the subject, it lists the
*available* shapes — the sections of the Document and the columns of a Table (see
[schema-query.md](schema-query.md)).

### Format flags (render the chosen shape)

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | the whole Document as one JSON object |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / `--rows` | with `--rows`, `-n` trims to N **data rows per table**, across Markdown, TSV, and JSONL |
| `--bare` | print only the selected payload, undecorated (the Scalar/blob rung) |

`--tsv`/`--jsonl`/`--table` render **one section at a time**, so they require a
Table-or-narrower selection; multi-section (Document) output stays in Markdown or
JSON.

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
# … or print a blob payload undecorated
member MyType Method:1 --library MyLib.dll -S "Decompiled Source" --bare > Method.cs
```

## Flag bindings still in flux

The **shape ladder above is stable**; some flag *bindings* to it are still being
designed and may change:

- **#1241** — `--bare`/`--count` should key off **shape, not row count**: a
  one-column Vector and a Scalar are different rungs, and `--bare` should apply to
  a one-column Vector, not only a single row.
- **#1219** — generalize `--bare` to text/doc sections and single-value
  selections (the full Scalar/blob rung), not just `CodeSection`s.
- **#1211** — `--raw`/`--blob` are a *URL-shape* pair (raw vs GitHub `/blob/`
  URLs), distinct from the output-shape ladder; a proposed rename would move the
  undecorated-output meaning onto `--bare` and keep `--raw`/`--blob` for URL
  shape only.

When those settle, update the flag tables here to match; keep the ladder and the
Markout mapping as the stable spine.
