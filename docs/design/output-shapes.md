# Output shapes

dotnet-inspect output narrows through a small ladder of **shapes**. Product
producers define the rows and capabilities; Markout renders those shapes after
dotnet-inspect flags choose which rung you land on. Naming the ladder gives a
shared vocabulary for the output flags
(`-S`, `--fields`/`--columns`, `--tsv`/`--jsonl`, `--count`, `-n`/`--rows`,
`--print`, `--bare`, …) and for deciding what a new flag should
do.

The older item-limit and multi-item print passages are design history from the
superseded umbrella
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) proposal, not
released behavior or current implementation authority.
[Semantic row selection](semantic-row-selection.md) now owns ordered item-stage
semantics. [Item and line selection composition](item-and-line-limits.md) lists
the focused successors that will reconcile CLI, source, payload, and line
behavior with this shape contract.

Related docs:

- [Output composition model](output-composition.md) — section selection, filtering, and writer capabilities
- [Projected JSON output](projected-json.md) — typed versus lowered JSON, representability, and atomic failure
- [Rendering model](rendering-model.md) — verbosity vs mode-switch flags
- [Schema query](schema-query.md) — `-D` discovery of sections and columns
- [Command model](command-model.md) — command surface and shared options
- [Item and line selection composition](item-and-line-limits.md) — ownership
  and sequencing for the focused #4677 designs
- [Semantic row selection](semantic-row-selection.md) — ordered semantic
  selection over complete logical sequences
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

- **Document → Table.** A Document is a sequence of sections. Selecting one
  section leaves a single Table (or other single-section payload).
- **Table → Vector.** A Table is columns × rows. Projecting to one column
  leaves a Vector — many rows of a single field.
- **Vector → Scalar.** Collapsing a Vector (count it, or take one row) yields a
  Scalar. A Scalar is also the natural shape of a non-tabular payload: a count,
  a single field value, or a text/documentation blob (a README, a decompiled
  `.cs` body, an XML-doc `///` comment).

Most sections are Tables, but a section can also be a key-value field set, a
list, a code/text blob, a tree, or a graph. Those are still "one section" — the
Table rung — and they collapse to Scalars the same way. For a call graph, the
declared row unit is a directed edge. Count reduction and future semantic row
selection operate on those relationship values before they are rendered as a
Markdown edge table, standalone tree, standalone Mermaid diagram, or tabular
stream. Tree nodes are presentation context, not additional rows.
`graph integrations` uses the same row contract: one row is one directed
logical relationship. Its package groups and finer member/type nodes are
presentation context. Every format must receive the same selected logical
edges. Isolated explicit packages remain node/group context in graph and JSON
views, but never become empty data rows in the default Markdown edge table.
`OutputModes_UseTheSameWindowedLogicalEdges` gates current Markdown, table,
JSON, JSONL, and count parity over the same `--rows`-selected edges. The pending
L2 integration design must name the gate for its future common handoff.

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
`AcquiredEndpoints_RetainAssemblyWithinOnePackage`, and
`AcquiredFailureTargets_RetainAssemblyWithinOnePackage` gate endpoint
ownership. `OutputModes_UseTheSameWindowedLogicalEdges` gates numeric and null
JSONL values and omission of the document-local `edge_id`.

## Flag families

Four families walk the shape ladder, and a fifth sits before it. A flag in one
of the ladder families contributes in one of four ways:

- **Shape selectors** narrow the requested shape (`-S`, `--fields`/`--columns`,
  `--count`).
- **Row query and semantic selection** narrow rows without changing the shape
  rung. Their future CLI spellings belong to the focused L3 design.
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
independent row sets.
`--count` remains valid because it aggregates across the selected inspections.

Shape cardinality is evaluated after both section and subject selection.
`--table`, `--tsv`, and `--jsonl` require exactly one table shape; `--tree`
requires exactly one tree shape; standalone `--mermaid` requires exactly one
graph shape. Selecting one section with `--tfm all` still produces one shape
per inspection, so it does not satisfy any single-shape contract.

Released `package --all-libraries` row windowing preserves producer-declared
row-set scope. Singular sections retain one table per library even when a row
format flattens them with provenance; aggregate sections window the rolled-up
table once. The pending L2 declared-row-set integration must preserve that
topology when it adopts semantic selection.
`PackageCommand_AllLibraries_RowFormats_WindowPerLibraryLikeMarkdownCount` and
`PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection`
gate both scopes and count/row-format parity.
`PackageCommand_AllLibraries_RowFormats_TailWindowMatchesMarkdownRows`,
`PackageCommand_AllLibraries_AggregateRowFormats_WindowSameRowsAsMarkdown`, and
`PackageCommand_AllLibraries_OpportunityRowFormat_WindowSameRowAsMarkdown` gate
selected-row identity at the window boundary.

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
- Tree, Mermaid, and table writers render their own narrow shapes (a call graph
  tree or diagram, a table row) and have no verbosity dial — they either show a
  thing or they do not (see [rendering-model.md](rendering-model.md)).

Cardinality is observed at the structured row seam after section production,
command-owned filtering, and accepted column/field source selection. A
formatter can observe those rows without writing text; rendered Markdown is
never parsed back into rows. The pending L2 integration and L3 designs own
where count reduction branches relative to semantic selection. Producers
outside Markout, such as metadata tables, expose cardinality from the same
typed row builders their renderer consumes.

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
| Vector | `--fields X` / `--columns X` (project to one column) |
| Scalar | `--count` (row count) |

### Count projection

`--count` reduces structured table rows to a Scalar. The pending L2 integration
and L3 designs own where this reduction branches relative to semantic
selection and which combinations reject; this shape document does not settle
those interactions.

- Every non-empty `--fields`/`--columns` request resolves against the selected
  sections before reduction. A field-set projection that filters entries
  changes the count; selecting table columns does not create or remove rows.
  Unsupported, unmatched, or inapplicable requests reject rather than leaving
  the unprojected count unchanged.

- One selected section produces a culture-invariant decimal scalar. The scalar
  is the complete payload in every format: JSON emits a JSON number and JSONL
  emits one numeric record; text and tabular formats emit the same bare value.
- Multiple selected sections produce ordered `section`/`count` rows, including
  a zero row for a requested section that emitted no table rows. Markdown,
  table, and plain text render those rows as their native table form; TSV emits
  two columns; JSONL emits one object per row; JSON emits an array of objects.
  JSON and JSONL counts are numbers rather than numeric strings. Standalone
  Mermaid is rejected because a count map is a table, not a graph.

The multi-section reduction is itself one table, so table, TSV, and JSONL
formats accept a category or other multi-section selection under `--count`.
Their ordinary one-input-table restriction applies before reduction and does
not reject this count-result table.

For multiple package subjects, `Package Info` and package-file sections count
their existing cross-package survey rows; other sections merge each package's
structured section rows. On the released package paths, each selected row set
reports its cardinality after command-owned filtering and the current `--rows`
window. `PackageSection_Rows_WindowsTheTabularRenderAndAgreesWithCount`,
`PackageCommand_AllLibraries_RowFormats_WindowPerLibraryLikeMarkdownCount`,
and
`PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection`
gate that count/render parity. The pending L2 integration and L3 designs own
how future typed selection stages compose with count reduction.

Trees and graphs do not acquire row semantics from whichever presentation a
formatter happens to choose. A producer that supports counting such a shape
must declare and count its product-owned lowering, as the dependency commands
do for graph nodes.

`-D`/`--discover` is orthogonal: it does not render the subject, it lists the
*available* shapes — the sections of the Document and the columns of a Table (see
[schema-query.md](schema-query.md)).

### Printable payload projections

Printability is a producer-declared row capability, not a property implied by
Table or Vector shape. Semantic item selection completes before printable,
path, or URL projection, and projection code carries producer-owned identity
rather than reconstructing it from rendered position.

The focused payload design listed in
[Item and line selection composition](item-and-line-limits.md) owns future
print cardinality, framing, structured results, failures, line selection,
acquisition, and destination publication. This shape document chooses none of
those future behaviors or their CLI spellings.

Released Markdown scoping remains command-owned until an explicit migration.
`--frontmatter`/`--yaml-header` and `--body` apply only to Markdown documents,
and Markdown link rewriting changes GitHub `blob` targets to fetchable `raw`
targets. `Package_ReadmeFrontmatter_PrintsOnlyYamlHeader`,
`Package_ReadmeBody_PrintsContentAfterYamlHeader`, and
`Package_Readme_DefaultNormalizesGithubBlobLinksToRaw` gate those
transformations. A package README's kind comes from its manifest role when its
file name has no dot; otherwise the name decides. Any dot counts as naming a
kind, including in `logo.png.`, `.png`, or `.README`, so a manifest role never
makes those names Markdown.
`Package_ExtensionlessReadme_IsStillTreatedAsMarkdown`,
`Package_DeclaredReadme_KeepsItsRoleWhenTheConventionalNameAlsoExists`, and
`Package_DeclaredNonMarkdownReadme_IsStillNotMarkdown` gate those rules.

The released paths do not share one preflight order. `--print` validates
Markdown scope over its full declared file family before resolving `--row`,
then reads only the chosen payload. Non-unary `--content` reads every
path-matched payload before applying `--rows` and validating the visible rows.
Unary `--content --bare` and `--content --out` apply `--rows` and resolve one
visible row before reading that payload. These ordering details are current
implementation behavior but unverified.

When Markdown-scope validation reaches a non-Markdown document, the current
request rejects and names it rather than silently returning the whole document
or dropping that row. `Package_NuspecPrint_RefusesMarkdownScopes` gates the
single-row `--print` refusal. Mixed-selection atomicity remains unverified. The
focused payload design owns a coherent future preflight contract; it must not
infer that contract from these differing released paths.

A projection may consume only capabilities and payloads declared on already
selected rows. It does not reinterpret an address as an artifact, evaluate an
unevaluated row, or acquire content for an unselected row. Exact compatibility
and failure behavior remain with the focused payload owner.

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
  for example, version rows may expose URLs. The pending payload design owns
  print-capability preflight.
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
including encoding, byte order mark, and line endings; a Markdown scope exports
that projected text. Terminal-facing output never emits a live control or bidi
scalar from package content.

The pending focused payload design owns future cardinality, structured output,
line-selection compatibility, preflight, and destination-publication behavior.
It must preserve terminal containment and ensure that any rejected export is
decided before its destination is mutated.

Tool-authored companion sections still use the stream split: for example,
`package X -S "Package README file" --print --info` writes the encoded document
directly to stdout and the `# Info` table to stderr.

Two consequences define the boundary:

- `--jsonl` preserves the payload as a JSON string value. The wire format
  escapes control characters as required by JSON; parsing the JSON reconstructs
  the original value.
- `--content` delimits each matched file with a
  `------------ <package> :: <path> ------------` banner. Current unary
  `--print` writes its selected payload directly, without a tool-authored frame
  or line-prefix gutter. The pending payload design owns any future multi-row
  framing and its containment contract.

These are gated by `PayloadLensContainmentTests`, which runs the built CLI over
a package whose README carries bidi, ESC, and LS hazards and asserts encoded
stdout, contained stderr, parsed JSON payload fidelity, and exact `--out`
export. `PackageContentOutput_ContainsNoLiveControlsOnStdoutAndPreservesExplicitFileExport`
gates both framed and `--bare` single-file content export with a UTF-16 payload
that has no trailing newline. Future multi-row containment gates belong to the
pending payload design.

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
| `--json` | render the selected shape as JSON: the whole Document when no narrower shape is selected, otherwise the projected payload (`--print`, `--value`, `--urls`, `--paths`). Accepted lenses and payload projections claim their own output first. Plain document `--json` keeps the pre-lowered typed document; an otherwise-unclaimed, non-empty `--fields`/`--columns` request names lowered vocabulary and opts into the lowered display view (#3494), with the same machine table keys as `--jsonl` and with semantic item selection and `--compact` preserved. `find` and `vocabulary` currently wire lowered document paths, while discovery owns projected JSON under its lens contract; unadopted projection-capable routes reject unsupported combinations before typed JSON serialization. Complete structured values under future line selection remain unverified and belong to the pending payload design in [Item and line selection composition](item-and-line-limits.md). See [Projected JSON output](projected-json.md) for routing, representability, diagnostics, and compatibility. |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| Item-selection gestures | select logical values before this presentation layer; exact CLI spellings are pending |
| Rendered-line gestures | select report or payload lines after text projection; exact CLI spellings are pending |
| `--bare` | render a selected payload without document decoration; future multi-item interaction is pending |
| `--plaintext` | render a whole-document plain-text view; distinct from `--bare` |

`--tsv`/`--jsonl`/`--table` render **one section at a time**, so they require a
Table-or-narrower selection; multi-section (Document) output stays in Markdown or
JSON. The pending payload design owns future `--print` row-set cardinality.

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
location. Use `--print --out <path>` instead for exact payload export.

## Design discipline for future flags

The stable shape vocabulary is:

- `--count` is a shape-reduction selector: it collapses a table/vector to a
  single scalar count. Its interaction with future semantic selection is
  pending.
- Semantic item selection consumes declared row values before projection.
  Its future CLI spellings are owned by the focused L3 design, not this shape
  document.
- `--print` is a payload projection. The pending focused payload design owns
  future multi-row framing, structured results, and unary alternatives.
- Rendered-line selection is a presentation operation over report or payload
  text. It does not select rows.
- `--row` is the released exactly-one address gesture. The pending L2
  integration and L3 designs own its relationship to stage-local positions and
  semantic selection.
- `--bare` is a presentation modifier. Current unary `--print` emits the
  selected payload without document decoration; the pending payload design owns
  its future multi-row meaning.
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
