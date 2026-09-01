# Output shapes

dotnet-inspect output narrows through a small ladder of **shapes**. Product
producers define the rows and capabilities; Markout renders those shapes after
dotnet-inspect flags choose which rung you land on. Naming the ladder gives a
shared vocabulary for the output flags
(`-S`, `--fields`/`--columns`, `--tsv`/`--jsonl`, `--count`, `-n`/`--rows`,
`--print`, `--bare`, …) and for deciding what a new flag should
do.

The item-limit, projection-role, typed-L2 result, and multi-item print passages
describe the approved
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) target, not
released behavior. [Item and line selection composition](item-and-line-limits.md) records its
implementation status and required gates.

Related docs:

- [Output composition model](output-composition.md) — section selection, filtering, and writer capabilities
- [Projected JSON output](projected-json.md) — typed versus lowered JSON, representability, and atomic failure
- [Rendering model](rendering-model.md) — verbosity vs mode-switch flags
- [Schema query](schema-query.md) — `-D` discovery of sections and columns
- [Command model](command-model.md) — command surface and shared options
- [Item and line selection composition](item-and-line-limits.md) — the composition
  map sequencing item limit and row projection participants
- [Semantic row selection](semantic-row-selection.md) — typed Head, Tail,
  Window, and Top stage behavior over one or more named row sequences
- [Section-row shaping](section-row-shaping.md) — typed declared-row-set
  binding, projection roles, and terminal Count semantics
- [The package query CLI](package-query-cli.md) — a facet-matched package
  corpus row applying this ladder's "declared row unit" discipline, and the
  source of the item-limit design

## The shape ladder

Each shape is a narrowing of the one above it. You start at a Document and
descend to a Scalar by selecting a section, then columns, then collapsing.

| Shape | What it is | Example |
| --- | --- | --- |
| **Document** | many sections | a full `library` / `type` report |
| **Table** | one section: columns × rows | the `Top Leverage` section |
| **Vector** | one column: many rows of a single field | just the `Member` column |
| **Scalar** | a single value, or a text/doc blob | `1234`, a README, a `///` summary |

That descent describes one declared row-set outcome. Count reduces each
declared row set independently: exactly one outcome reaches Scalar, while
multiple exact outcomes reassemble as one ordered count Table. Count never
collapses independent row sets into one request-wide scalar.

- **Document → Table.** A Document is a sequence of sections. Selecting one
  section leaves a single Table (or other single-section payload).
- **Table → Vector.** A Table is columns × rows. Cell-projecting it to one
  column leaves a Vector — many rows of a single field. A field-set membership
  projection instead changes which field-entry rows reach this ladder.
- **Vector → Scalar.** Within one declared row set, collapsing a Vector (count
  it, or take one row) yields a Scalar. A Scalar is also the natural shape of a
  non-tabular payload: one count, a single field value, or a
  text/documentation blob (a README, a decompiled `.cs` body, an XML-doc `///`
  comment).

Most sections are Tables, but a section can also be a key-value field set, a
list, a code/text blob, a tree, or a graph. Those are still "one section" — the
Table rung — and each declared row set can collapse to a Scalar the same way.
For a call graph, the declared row unit is a directed edge: `--count` counts
relationships, `-n` limits them, and `--rows` selects an absolute range of the
same ordered relationships whether the graph is rendered as a Markdown edge
table, standalone tree, standalone Mermaid diagram, or tabular stream. Tree
nodes are presentation context, not additional rows.
`graph integrations` uses the same row contract: one row is one directed
logical relationship. Its package groups and finer member/type nodes are
presentation context, while `--count`, `-n`, and `--rows` count, limit, or
select logical edges consistently across Markdown, tree, Mermaid, tabular, and
structured output. Isolated explicit packages remain node/group context in
graph and JSON views, but never become empty data rows in the default Markdown
edge table.
`OutputModes_UseTheSameWindowedLogicalEdges` gates the same selected logical
edges across the non-count output modes. The CLI does not compose `--count`
with `-n`, `--rows`, `--top`, or rendered-line gestures. It counts the complete
logical cohort after command-owned membership and predicate selection but
before those CLI windows.

The `graph integrations --json` failure array preserves both presentation and
typed addressing: each failure carries its rendered target plus
`target_kind`/`target_id`, and Integration failures retain structured producer,
kind, assembly-reference, acquisition-failure, and exception fields. Opaque
workspace registration handles are deliberately not stringified; the graph
target and typed reference evidence remain the identities a consumer can
interpret outside the owning workspace.
Failure-targeted nodes and groups remain in the JSON document as diagnostic
context even when they are not endpoints of the selected edge window; they are
not additional relationship rows. Structured diagnostic text crosses the same
lossless inert containment boundary as human-readable failures.
`VisibleGraphFailure_PreservesOutputAndNonzeroExit` gates target
resolvability, and `StructuredFailureText_IsInertAfterJsonParsing` gates
containment after a JSON consumer decodes the value.

Integration graph edge rows carry `source`, `source_assembly`, `source_group`,
`relationship`, `target`, `target_assembly`, `target_group`, `occurrences`,
and `evidence`. Assembly and group fields preserve endpoint identity within a
multi-assembly package and package ownership across package contexts;
plain-text and graph node labels carry the same context. JSON nodes also carry
assembly identity, and failure target labels retain it. JSON and JSONL keep
occurrence counts numeric and absent values null; JSON edges carry projected
evidence rather than exposing
document-local occurrence ids without the occurrence collection that owns
them. `ProductionShapedEndpoints_RetainPackageOwnership`,
`AcquiredEndpoints_RetainAssemblyWithinOnePackage`,
`AcquiredFailureTargets_RetainAssemblyWithinOnePackage`, and
`OutputModes_UseTheSameWindowedLogicalEdges` gate these contracts.

## Flag families

Four families walk the shape ladder, and a fifth sits before it. A flag in one
of the ladder families contributes in one of four ways:

- **Shape selectors** narrow the requested data or shape (`-S`,
  `--fields`/`--columns`, `--count`). Under the target
  [section-row-shaping contract](section-row-shaping.md#projection-kinds), L2
  resolves field/column intent as membership or cell projection before a
  renderer sees it.
- **Item/range selectors** narrow the rows without changing the shape rung
  (`--where`, `--order-by`, `-n`, `--top`, `--rows`).
- **Presentation modifiers** change how a selected payload is rendered without
  changing the shape (`--bare`, `--markdown`, `--json`, `--table`, `--tsv`,
  `--jsonl`, `--plaintext`, `--no-headers`, and graph-supported `--tree` or
  `--mermaid`).
- **URL-shape modifiers** change only the form of GitHub URLs emitted as data
  (`--raw`, `--blob`). They are orthogonal to the output-shape ladder.

`library --package ... --tfm all` selects multiple independent inspections. Its
full output therefore requires a document format: Markdown or JSON.
Single-table, stream, plain-text, tree, unary projection, and single-row-set
`--print` output fail closed rather than selecting one inspection or combining
independent row sets. Count remains valid because it preserves the producer's
declared aggregate or independent row-set scopes.

For unreduced output, shape cardinality is evaluated after both section and
subject selection. `--table`, `--tsv`, and `--jsonl` require exactly one table
shape; `--tree` requires exactly one tree shape; standalone `--mermaid`
requires exactly one graph shape. Selecting one section with `--tfm all` still
produces one shape per inspection, so it does not satisfy any unreduced
single-shape contract.

Count does not apply that eligibility test to its contributing inputs. It first
consumes the already-bound typed reduction result, then evaluates format
eligibility against the resulting Scalar or one count Table as defined below.

### Coordinate carriers sit before the ladder

A fourth kind of flag does not walk the ladder at all: it *supplies an input the
command has no other way to express*, and in doing so changes which sections
exist to be selected. The family has two currencies: the IL coordinate, and the
heap coordinate `--heap` carries (see
[metadata-table-projection.md](metadata-table-projection.md)).

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

The current product supplies raw field/column names to
`MarkoutWriterOptions.Projection`, which applies both table-column projection
and field-set inclusion during serialization. The target
[section-row-shaping contract](section-row-shaping.md#projection-kinds) moves
the membership-versus-cell decision into L2; after that adoption, two Markout
knobs handle remaining cell narrowing and formatting:

- **Projection** (`MarkoutWriterOptions.Projection`) applies an already-resolved
  cell projection — the Table → Vector step. It does not implement field-set
  membership projection.
- **Table mode** (`MarkoutWriterOptions.TableMode`) picks how tables render:
  Markdown (default), `MarkoutTableMode.Tsv`, or `MarkoutTableMode.Jsonl`.

Formatters decide presentation, not content:

- **`MarkdownFormatter`** — the rich, multi-section, verbosity-aware Document
  format. The canonical shape; everything else is a projection or reduction of
  it.
- **`TableFormatter`** — a single-section tabular renderer (pretty table, `--tsv`,
  `--jsonl`). Because it renders one section at a time, its output is always a
  single Table (or Vector).
- Tree, Mermaid, and table writers render their own narrow shapes (a call graph
  tree or diagram, a table row) and have no verbosity dial — they either show a
  thing or they do not (see [rendering-model.md](rendering-model.md)).

The current `CountProjectionFormatter` establishes cardinality by intercepting
structured Markout rows without writing them. Under the target
[section-row-shaping contract](section-row-shaping.md#result-binding-and-failure),
formatters instead consume typed L2 Row-outcomes, Count, or failure results and
do not establish cardinality. Rendered Markdown is never parsed back into rows.
Producers outside Markout, such as metadata tables, expose the same declared
logical rows to L2 that their renderers consume.

An incomplete comparison is not narrowed into a clean result. Diff document
formats include typed inspection-failure rows. Single-shape diff formats
(`--table`, `--tsv`, `--jsonl`, and `--name-only`) cannot append a second
failure table, so they emit an explicit incomplete-comparison diagnostic and
exit nonzero.

## How dotnet-inspect flags select a shape

Flags are how the user (or an agent) walks the ladder. The important distinction
is that a shape selector changes what data is requested, while a presentation
modifier changes how a selected payload is rendered.

### Shape selectors (narrow the shape)

| Target shape | Flags |
| --- | --- |
| Document | default view; `-v:q`/`-v:m`/`-v:n`/`-v:d` (breadth presets); `-S a,b` (multiple sections) |
| Table | `-S OneSection` (a single section) |
| Vector | `--fields X` / `--columns X` when resolved as a one-column cell projection |
| Scalar or count Table | Count reduction: one declared row-set outcome becomes a Scalar; multiple outcomes become an ordered count Table |

### Count results

[Section-row shaping](section-row-shaping.md#count-semantics) owns which row
sets participate, what Count observes, when its evidence is exact, and whether
L2 binds a successful Count or failure result. This document begins with that
already-bound typed result and owns only its place on the shape ladder and its
presentation.

- A successful Count result containing one exact declared-row-set entry
  produces a culture-invariant decimal scalar. Markdown, plain text, pretty
  table, and TSV emit the same bare value; JSON emits one number; and JSONL
  emits one numeric record.
- A successful Count result containing multiple exact declared-row-set entries
  produces ordered row-set/count rows. Markdown, table, and plain text render
  those rows as their native table form; TSV emits two columns; JSONL emits one
  object per row; JSON emits an array of objects. JSON and JSONL counts are
  numbers rather than numeric strings.
- Standalone Mermaid rejects every Count result because neither a scalar nor a
  count map is a graph.
- An already-bound failure result produces no Scalar or count Table. Failure
  presentation belongs to the consuming output owner and is not encoded as a
  numeric value.

The multi-row-set reduction is itself one table, so table, TSV, and JSONL
formats accept a request that resolves to multiple row sets under `--count`.
Their ordinary one-input-table restriction evaluates the already-bound
post-reduction shape and therefore accepts this one count-result table without
inspecting how many declared row sets contributed entries.

Target adoption must add the non-vacuous Release gate
`TypedCountResultsRenderByShape`. It feeds already-bound typed results directly
to the output layer and requires:

- one exact entry to exercise Markdown, plain-text, pretty-table, TSV, JSON,
  JSONL, and Mermaid paths, rendering the specified bare numeric value in the
  first four, one JSON number, one numeric JSONL record, and the Mermaid
  rejection;
- multiple entries to preserve identity, order, and numeric counts as a native
  Markdown, plain-text, and pretty-table result, two-column TSV, JSON array,
  and object-per-row JSONL result, while the separately exercised Mermaid path
  rejects and the ordinary single-input-table restriction does not reject the
  count result;
- a bound failure to exercise every Markdown, plain-text, pretty-table, TSV,
  JSON, JSONL, and Mermaid route, each using its owner-defined failure
  presentation with no Scalar or count Table payload; and
- fixtures to prove that no tested output path reconstructs cardinality from
  rendered or intercepted rows.

For multiple package subjects, `Package Info` and package-file sections retain
their producer-declared cross-package survey row sets. Other sections preserve
the aggregate or per-package scope declared before shaping. L2 does not infer a
merge from labels or presentation.

Trees and graphs do not acquire row semantics from whichever presentation a
formatter happens to choose. A producer that supports counting such a shape
must declare and count its product-owned lowering, as the dependency commands
do for graph nodes.

`-D`/`--discover` is orthogonal: it does not render the subject, it lists the
*available* shapes — the sections of the Document and the columns of a Table (see
[schema-query.md](schema-query.md)).

### Printable payload projections

Under this shape contract, normal `--print` is a batch projection over the
selected rows. Every selected row is projected to its
declared printable payload:

| Selected rows | `--print` | `--print --row N\|first\|last` |
| ---: | --- | --- |
| 0 | Error: the selected section has no rows. | Error. |
| 1 | Print one framed or structured result. | Print one framed or structured result for the addressed row; any other number is an error. |
| More than 1 | Print one framed or structured result per selected row. | Print one framed or structured result for the addressed row. |

`--where` filters rows; item-mode `-n`, `--rows`, and `--top` then narrow them
before projection. `--row` is the mutually exclusive exactly-one alternative to
the item/range windows; line-mode `-n` remains available under `--lines`.
`--paths` and `--urls` project the same selected rows without acquiring their
content.

Numeric `--row N` addresses a row by its position after filtering and effective
ordering, but before item/range windows or payload projection. Sections do not
print a row-number column, so N is the number the reader arrives at by counting
the unwindowed ordered rows top to bottom. Later windows and printability do not
renumber anything. A row that declares no payload still occupies its number,
and selecting it reports that it has no document rather than silently sliding
to a neighbour. For projections that omit inapplicable rows, such as `--value`,
`--urls`, and `--paths`, `first` and `last` remain the endpoints actually
emitted by that projection, retaining their original numeric addresses.
`--print` has no such gaps because every selected row emits a success or
failure. Structured output makes the number explicit — `--jsonl` and `--json`
emit it as `row` — and error messages name the available addresses, so a
projection with gaps stays navigable.

This is the one rule that makes the ordinal trustworthy. Renumbering after a
payload projection or printability check is wrong in the worst way available:
it returns a real row, so nothing looks broken, and the reader has no way to
recover the sequence being indexed. Addressing the pre-projection ordered row
can only ever hit the intended row or report a miss.

A row set that declares no printable capability rejects `--print` once during
preflight rather than emitting one failure per row. Per-row failures apply to a
print-capable row set after that preflight, including heterogeneous rows that do
not individually carry a payload.

After successful preflight, every selected print row in normal framed or
structured output emits a visible success or failure result. A heterogeneous
row that does not declare a printable payload, or whose payload cannot be
acquired, is not omitted. Other rows continue, and any failure makes the command
exit non-zero. Normal text frames every result with typed row identity; JSONL
and JSON-array output retain that identity in one complete object per row.
Plain `--json` retains its unary one-object contract and rejects multiple
selected rows. Unary `--bare` and unstructured `--out` report acquisition or
transformation failures as diagnostics with no payload envelope.

A printed document is the document the package shipped. Markdown conventions --
YAML frontmatter scoping through `--frontmatter`/`--body`, and rewriting GitHub
`blob` links to `raw` so the target is fetchable -- apply only to Markdown. A
document's kind comes from its extension, except for the package README, whose
kind comes from its role: the manifest declared it as the readme and NuGet
renders it as Markdown, so an extensionless or unconventionally named README is
still Markdown. That role follows the manifest declaration, not the file the
README section displays. A package that ships `README.md` and also declares a
different file has declared both readmes, and the declared one keeps its kind
even though the section shows the conventional name. The role answers only where
the extension is silent: a manifest can declare anything, and
`<readme>logo.png</readme>` is malformed but shippable, so a declaration never
overrides a name that says what the document is. Any dot in the file name counts
as saying something: `logo.png` names a suffix, `logo.png.` names one with a
stray dot after it, and `.png` spells one as a hidden basename. Telling a hidden
suffix from a hidden word like `.README` would take a list of known suffixes that
goes stale and still guesses wrong at the edges, so the tie goes to the
conservative reading -- refusing a scope on `.README` is loud and leaves the
document readable, while handing a declared PNG to the link rewriter returns a
corrupted file and exit 0.
Applied to anything else they are corruption rather than presentation: the link
rewriter matches bare URLs anywhere in the text, so a URL inside an XML element
or an MSBuild comment is rewritten and the printed manifest silently stops
matching the one the feed serves. Asking for a Markdown scope on a document that
is not Markdown is refused, because both other answers -- the whole document, or
an empty one -- report success for a question that was never answered. The
refusal belongs to the request, not to one flag, so `--content` refuses it on
the same terms as `--print`.

The refusal covers the whole request rather than skipping the documents it does
not apply to. A selection that matches Markdown and non-Markdown alike --
`--path "*" --frontmatter` -- is one request, and answering part of it while
dropping the rest reports success for files that were never scoped. The refusal
names the first such document so the selection can be narrowed, for example with
`--path "*.md"`.

This request-level scope preflight runs after filters and item, range, or
single-row selection establish the selected documents, but before payload
acquisition or output. It inspects only selected rows, so an unselected
non-Markdown row does not reject the request. If any selected row is not
Markdown, one preflight rejection preempts the per-row batch failure model; the
requested transformation itself is invalid rather than one row's payload being
missing or unavailable.

Normal `--print` stdout is a framed, visually encoded projection, even for one
row. Unary `--bare` removes the frame but remains terminal-safe rather than an
exact byte-transfer contract. A caller printing a manifest in order to hash or
diff it uses unary `--out`, which preserves the package bytes exactly,
including any byte order mark.

`-n N` and bare `-N` are semantic item windows applied independently to each
declared row set after filtering and ordering. `--head` names the first-N
direction explicitly, and `--tail` selects the last N items. Non-row sections
remain unchanged:

```text
--print -n 1
  select the first declared row -> emit its framed print success or failure

--print --rows 2..5 -n 20 --lines
  select rows 2 through 5 -> fetch each payload -> render its first 20 lines
```

`--rows` carries only absolute row ranges:

- `--rows 2..10` keeps the rows numbered 2 through 10 inclusive — nine rows.
- `--rows 2+10` keeps ten rows starting at row 2.
- `--rows 10..` keeps row 10 through the last row.

Count-form `--rows 6` and `--rows 6 --tail` retire in favor of `-n 6` and
`-n 6 --tail`. A range may intersect an `-n` or `--top` result without
renumbering stable row addresses.

In `package --all-libraries`, singular sections retain one table per library
for windowing even when a row format flattens them with provenance; aggregate
sections window the rolled-up table once. The paired
`PackageCommand_AllLibraries_RowFormats_WindowPerLibraryLikeMarkdownCount` and
`PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection`
tests gate both scopes and their count/row-format parity.
`PackageCommand_AllLibraries_RowFormats_TailWindowMatchesMarkdownRows`,
`PackageCommand_AllLibraries_AggregateRowFormats_WindowSameRowsAsMarkdown`,
and `PackageCommand_AllLibraries_OpportunityRowFormat_WindowSameRowAsMarkdown`
gate selected-row identity at the window boundary.

A count and a range are different kinds, not two spellings of one: a count
anchors to an end and a range does not. Bare `--rows 2..10 --tail` is rejected.
`-n 20 --tail --rows 90..95` is valid because `--tail` belongs to the item
count; `--rows 2..10 --print -n 20 --lines --tail` is valid because it belongs
to the independent line window.

`--lines` changes the unit carried by `-n` from items to rendered lines. For an
ordinary report it windows the report; for multi-item `--print` it windows each
payload independently, excluding separators. `--tail-lines` is sugar for
`--lines --tail`. A single `-n` cannot carry both an item count and a line
count; use `--rows 1..M --print -n N --lines` when both dimensions are needed.

Printability is a row capability, not a property implied by Table or Vector
shape. Multi-item `--print` may not:

- reinterpret an address row as the artifact at that address;
- evaluate an unevaluated address;
- acquire content that the selected row did not declare.

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

### Lens modes project their own payload

A few flags select a *lens* rather than a section of the normal document:
`package --versions`, `--layout`, `--tfms`, and `--content`, along with
`library --il-offsets` and the `-D`/`--discover` listing. Each renders a
payload it computes itself and returns before the section pipeline, so the
section-selection vocabulary does not describe what the caller is looking at.

The lens payload is still a payload, so the two-outcome rule above applies
unchanged. Because the lens owns the shape, its answers are fixed:

- `--count` counts the lens payload — versions, target frameworks, package
  files, IL offsets, discovered artifacts — not the lines used to render it. A
  layout count is a count of files, even though the rendered tree also shows the
  directories that contain them.
- `--content` yields one structured row per matched file despite rendering
  text, so its count is the number of files matched.
- A `--content` path that matches nothing in a package still renders a
  per-package placeholder — `(absent)` in the block render, a `found:false` row
  in `--jsonl` — and that placeholder is **not** counted. The count answers *how
  many files did I get content for*, so counting placeholders would report
  matches that did not happen. The placeholder is presentation, which is why
  `--skip-empty` removes it and `--bare` never emits it: under `--skip-empty` the
  rendered rows and the count agree exactly. This is the one place the count is
  deliberately smaller than the default render's row total.
- An opaque lens payload refuses `--print`, `--value`, `--urls`, and `--paths`
  with the reason rather than inferring structure from rendered text. A lens
  that declares rows and their capabilities composes with ordinary projections:
  for example, version rows may expose URLs. A version row set that declares no
  printable capability rejects `--print` once during preflight.
- `-S`/`--select` is refused when the caller typed it, rather than ignored. A
  lens and a section selection are competing answers to *what am I looking at*,
  and silently honoring the lens hides that the selection did nothing.

There is deliberately no lens for printing documents. A flag that names a
particular document is a second answer to *which documents*, competing with the
section and row selectors, and the two can disagree — which is exactly how a
lens that printed the package README came to print the XML manifest through the
README's Markdown pipeline. Printable documents are therefore reached only by
selecting the section that lists them, narrowing its rows, and applying
`--print`.

#### Payload stdout is visually encoded; exact export is explicit

Everything a rendered surface shows is *contained*: untrusted metadata names,
attribute text, doc text, and nuspec fragments have their line terminators
folded and their rendering hazards (VT, ANSI escapes, bidi overrides, LS/PS)
rewritten as visible `\uXXXX`, so they cannot escape a table cell, a code
fence, a tree gutter, or a diagnostic line (issue #3319).

Printing documents (`-S "Package README file" --print`) and `--content`
visually encode rendering hazards on stdout. Exact payload transfer is an
explicit unary file operation: add `--out <path>` to a selection that resolves
one payload. An unscoped file export preserves the package bytes exactly,
including encoding, byte order mark, and line endings, except for package skill
documents: skills are agent instructions, so every route, including
`project -S Skills --print`, `package -S "Package skill files" --print`,
`--content`, and a package README declaration, classifies through a
`TextPolicy.Prose` `InertString` and carries one containment-selected value
through stdout, structured output, and `--out`. The raw scoped skill is
classified before link normalization; concerning text becomes the standard
placeholder, safe text retains its full presented spelling, and exact package
bytes are not retained. A Markdown scope exports projected text.
Terminal-facing output never emits a live control or bidi scalar from package
content. Multi-item
`--print --out` and multi-file or multi-package `--content --out` are refused
unless a structured JSON shape owns the destination; global selection
cardinality is resolved before any selected payload is read, and a unique exact
payload is read from the same retained package acquisition that supplied its
selection metadata. Narrow it with row
or path selectors for exact transfer.
Unstructured exact `--out` rejects line windows because clipping would no
longer be exact. Every refused export is decided before opening its destination:
an absent path stays absent, and an existing file remains byte-for-byte
unchanged.

Every command that exposes `--print` also exposes and wires unary `--bare` and
`--out`; this makes the payload-only and exact-destination paths properties of
the projection rather than accidents of its parent command. Structured
multi-item `--out` is a different mode: after atomic preflight it may publish
complete result records incrementally, including typed row failures.

Tool-authored companion sections still use the stream split: for example,
`package X -S "Package README file" --print --info` writes the framed, encoded
document to stdout and the `# Info` table to stderr.

Two consequences define the boundary:

- `--jsonl` preserves ordinary payloads as JSON string values. The wire format
  escapes control characters as required by JSON; parsing reconstructs the
  original ordinary payload. A package skill document that requires containment
  is omitted before serialization, so parsing returns
  `[Text omitted: required containment]`.
- `--content` and target `--print` write framing to stdout. `--content`
  delimits each matched file with a
  `------------ <package> :: <path> ------------` banner; `--print` uses its
  row-identity and line-metadata frame. Every frame field is contained.
  `--print` additionally prefixes each terminal-safe payload line with a
  tool-owned `|` followed by one space, so payload text cannot forge a sibling
  frame.

These are gated by `PayloadLensContainmentTests`, which runs the built CLI over
a package whose README carries bidi, ESC, and LS hazards and asserts encoded
stdout, contained stderr, parsed JSON payload fidelity, and exact `--out`
export. `PackageContentOutput_ContainsNoLiveControlsOnStdoutAndPreservesExplicitFileExport`
gates both framed and `--bare` single-file content export with a UTF-16 payload
that has no trailing newline. The target
`MultiPrintFrameFieldsAreContained` gate applies the same adversarial coverage
to every `--print` frame field, and `MultiPrintPayloadCannotForgeFrames` covers
frame-shaped payload lines and line-ending edge cases. Package skill output is
gated separately by `SkillDocuments_OmitPayloadsThatRequireContainment` and
`SkillDocuments_OutputAliasesWritePackageAndProjectPayloads`.

Discovery (`-D`/`--discover`) is a lens for the projections above but not for
`-S`, which legitimately narrows what discovery reports. Its own `--count` must
come from the discovered rows; the surrounding command's document count is a
different payload that happens to be a plausible-looking number.

`-S` here means an explicit selection. Some options are sugar that synthesize a
selection internally, and a synthesized one must not be mistaken for a request
the caller made.

### Presentation modifiers (render the chosen shape)

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | render the selected shape as JSON: the whole Document when no narrower shape is selected, otherwise the projected payload (`--print`, `--value`, `--urls`, `--paths`). Accepted lenses and payload projections claim their own output first. Plain document `--json` keeps the pre-lowered typed document; an otherwise-unclaimed, non-empty `--fields`/`--columns` request names lowered vocabulary and opts into the lowered display view (#3494), with the same machine table keys as `--jsonl` and with semantic item/range windows and `--compact` preserved. `find` and `vocabulary` currently wire lowered document paths, while discovery owns projected JSON under its lens contract; unadopted projection-capable routes reject unsupported combinations before typed JSON serialization. Complete structured values under item and line limits remain unverified; [Projected JSON output](projected-json.md) owns routing, representability, diagnostics, compatibility, and its adoption gates. |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / numeric shorthand such as `-20` | keep the first N declared items per row set |
| `-n N --head` | keep the first N declared items with the default direction explicit |
| `-n N --tail` | keep the last N declared items per row set |
| `--rows N..M` / `--rows N+K` / `--rows N..` | keep the **rows those stable numbers name**, inclusive; absolute, so no item direction applies |
| `-n N --lines` | keep the first N lines of the rendered report, or of each multi-print payload |
| `-n N --lines --head` | keep the first N lines with the default direction explicit |
| `-n N --tail-lines` | keep the last N lines; sugar for `--lines --tail` |
| `--bare` | render the selected payload without document decoration; multi-item print rejects it because framing carries row identity |
| `--plaintext` | render a whole-document plain-text view; distinct from `--bare` |

`--tsv`/`--jsonl`/`--table` render **one section at a time**, so they require a
Table-or-narrower selection; multi-section (Document) output stays in Markdown or
JSON. `--print` likewise requires exactly one declared row set, though it may
project every selected row in that set.

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

# Printable payload: the visually encoded resolved source line
dotnet-inspect library My.dll --il-offset 0x06000002+0x1 \
  -S "Context: Source Location" --print --bare
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
document path, and `--print --bare` returns the visually encoded payload at the
location without the normal frame or gutter. Use `--print --out <path>` instead
for exact payload export.

## Design discipline for future flags

The stable vocabulary is:

- `--count` is a terminal shape reduction over the complete logical cohort
  surviving command-owned membership and predicate selection. The CLI rejects
  item, absolute-range, ranked, row-address, direction, and rendered-line
  gestures with `--count`; one declared row set collapses to a Scalar and
  multiple sets produce an ordered count Table.
- `-n N` / bare `-N` select the first N declared items per row set after
  filtering and ordering. `--head` names that direction explicitly and
  `--tail` reverses it when the producer can establish a truthful suffix.
- `--rows` selects absolute stable row ranges and carries no count-only form.
- Normal `--print` projects every selected row to one framed or structured
  success/failure result. Unary `--bare` and unstructured `--out` carry no
  result envelope. None of these modes invents printability or evaluates new
  addresses.
- `--lines` changes the `-n` unit to rendered lines. For multi-item print the
  line window applies independently to each payload.
- `--head` / `--tail` name a direction, not a count. They require and modify an
  active item or line `-n` window; they never modify an absolute row range or
  ranking.
- `--row` addresses a rendered row by its position in the section, counting from
  1. Any future selector that takes an ordinal joins this rule: the number a
  reader arrives at by counting rows is the number that can be addressed, and no
  later item/range window or projection may renumber it.
- `--bare` is a presentation modifier: for one selected payload, it strips the
  surrounding frame and payload gutter.
- `--raw` / `--blob` are URL-shape modifiers: they control the form of emitted
  GitHub links, not the shape of the payload itself.
- `--plaintext` remains distinct from `--bare`; if it stays in the product, it is
  a whole-document plain-text rendering mode rather than a bare-payload mode.
- `--il-offset` / `--il-offsets` / `--heap` are coordinate carriers: they supply
  an input that has no other expression and gate the sections it makes
  meaningful. They do not narrow a shape, and a flag qualifies for this family
  only if its input is a new currency. The first two spell the same currency, so
  they are one member; `--heap` is the second.

New flags should fit one of those buckets rather than blending concepts.
