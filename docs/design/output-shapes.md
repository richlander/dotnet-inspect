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

## Three flag families

A flag can contribute in one of three ways:

- **Shape selectors** narrow the requested shape (`-S`, `--fields`/`--columns`,
  `--count`, `-n 1`).
- **Presentation modifiers** change how a selected payload is rendered without
  changing the shape (`--bare`, `--markdown`, `--json`, `--table`, `--tsv`,
  `--jsonl`, `--plaintext`, `--no-headers`).
- **URL-shape modifiers** change only the form of GitHub URLs emitted as data
  (`--raw`, `--blob`). They are orthogonal to the output-shape ladder.

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

### Presentation modifiers (render the chosen shape)

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | the whole Document as one JSON object |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / `--rows` | with `--rows`, `-n` trims to N **data rows per table**, across Markdown, TSV, and JSONL |
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
resolved source location can be useful as a human fact sheet, a row, a scalar, a
URL, a path, or a source-line payload.

The default stays evidence-oriented and renders the singleton location as
vertical facts:

```bash
dotnet-inspect library My.dll --il-offset 0x06000002+0x1
```

```md
## IL Offset

| Field | Value |
| ----- | ----- |
| Method | My.Type.Method |
| Token | 0x6000002 |
| IL Offset | 0x1 |
| File | /_/src/Foo.cs |
| Line | 42 |
| Url | https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42 |
```

The same resolved location then projects cleanly:

```bash
# Scalar
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "IL Offset" --fields Line --value
# 42

# URL vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "IL Offset" --urls
# https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42

# Path vector (one row)
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "IL Offset" --paths
# /_/src/Foo.cs

# Printable payload: the raw resolved source line
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "IL Offset" --print
#         return JsonSerializer.Serialize(value, options);

# Singleton count
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "IL Offset" --count
# 1
```

This keeps the concerns separate: the default fact section shows all
symbolication evidence, `--urls` returns the anchored source location, `--paths`
returns the PDB document path, and `--print` returns the raw payload at the
location rather than a decorated snippet.

## Design discipline for future flags

The stable vocabulary is:

- `--count` is a shape-reduction selector: it collapses a selected table/vector to a
  single scalar count.
- `--bare` is a presentation modifier: it strips the surrounding framing from an
  already-selected payload.
- `--raw` / `--blob` are URL-shape modifiers: they control the form of emitted
  GitHub links, not the shape of the payload itself.
- `--plaintext` remains distinct from `--bare`; if it stays in the product, it is
  a whole-document plain-text rendering mode rather than a bare-payload mode.

New flags should fit one of those buckets rather than blending concepts.
