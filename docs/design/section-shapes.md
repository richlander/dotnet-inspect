# Section shapes

## Status

This document is the normative owner for section shape typing and format
lowering, tracked by
[#9303](https://github.com/richlander/dotnet-inspect/issues/9303). It
establishes the contract before the `package`, `library`, `type`, and `member`
owners adopt it through focused follow-on work, in that order.

Current product spellings and renderings remain current behavior until their
owning command adopts this contract. Examples marked **target** describe the
settled direction; they do not claim that the behavior is already executable.

The operator approved the representation-selection and JSON Document target on
2026-10-20. [Section representations](#section-representations) supersedes the
earlier exactly-one-presentation-shape rule and bare-array JSON lowerings below.
The semantic Table, Hierarchy, and Text definitions remain applicable. Existing
command-specific adoption records describe earlier shape adoption, not
conformance to this newer representation contract.

[Relationship Section Naming](relationship-section-naming.md) is the merged
precedent: it names a relationship section by its semantic result shape. This
document applies the same discipline to presentation. A section's shape, not
the command that hosts it and not a renderer flag, decides how the section can
be shown.

## Normative owner and claim

Section Shapes owns one claim:

> After a section's semantic result is known, its owner declares the
> representations it offers. Each representation presents that same result;
> the consumer chooses one without changing the declared rows, ordering,
> selection, or Count. The CLI prefers Hierarchy, then Table, then Document;
> JSON consumes the structured Document.

This owner defines the three shapes, the native and permitted lowerings of
each, the owner-issued properties that give a result its context, the
invariants every lowering keeps, and the boundary between content and host
presentation. The third shape is named Text rather than Document because
Document already names a content kind in
[Host-observable content kinds](host-observable-content-kinds.md) and a
multi-section rendered shape in [Output shapes](output-shapes.md); a
Text-shaped section may well carry a content-kind Document, and the three
classifications are independent axes of one section. A Document representation
is a structured composition, not a synonym for a Text payload or rendered
Markdown. This document does not own section identity, categories, selection,
verbosity, cardinality, discovery surfaces, format spellings, or any
command's data.

## Section representations

### One result, several representations

A section may expose Hierarchy, Table, and Document representations in any
supported combination. These are alternative presentations of one owner-issued
result, not separate inspections or permission to acquire additional content.
The semantic definitions below still distinguish rows, parent relationships,
and text payloads. An owner may expose a Table alongside a Hierarchy, or a
Document alongside either, without changing that semantic grain.

A Table contains one ordered row population with one column schema. A
Hierarchy adds owner-issued parent relationships over that population; context
nodes do not become rows. A Document carries the payload and the structured
summary or context needed to interpret it. It may also carry an owner's text
payload, hierarchy, completeness evidence, or scoped failures. It need not be
table-centric.

For a table-centric section, the Document contains the same typed, selected,
ordered rows that feed the Table representation. The Document can retain
additional row facts; tabular display spelling need not be the JSON schema.
Identity, population, and meaning must agree, not the rendered bytes.

Summary values are owner-issued structured facts, not sentences parsed back
into fields. Their scope must be defined: totals over the complete result must
not be mistaken for totals over a selected row window. Row selection does not
silently recompute a complete-result summary. Each owner defines the summary's
scope and any selection context needed to interpret it; this contract does not
impose a universal summary schema.

### Consumer selection

For one effective section with no explicit format intent, the CLI chooses the
first available representation in this order:

1. Hierarchy, rendered as a tree.
2. Table, rendered as TSV rows.
3. Document, rendered according to its content; a lone text payload retains
   its undecorated native spelling.

Section selection, including which section is the command's default, remains
with its existing owner. A multi-section selection uses a representation that
can carry the complete selection under the existing composition rules; the
preference order does not authorize concatenating incompatible row schemas or
discarding selected sections. A heterogeneous composition uses Document.

Explicit output intent takes precedence:

| Request | Representation and rendering |
| --- | --- |
| `--tsv`, `--table`, `--jsonl` | Table rows only, including an admitted flat Table representation of a Hierarchy |
| `--json` | Structured Document, serialized with native value types |
| `--markdown` | Authored Document rendering when offered; otherwise a Table rendered as a Markdown table or a Hierarchy rendered as a Markdown-framed tree |
| Explicit tree or diagram format | The corresponding admitted Hierarchy lowering |

A row stream has one schema and no document-summary preamble or trailer.
Changing to a tabular formatter over an entire Document is not Table
selection. Genuine field-entry rows of a scalar Table remain rows; the rule
removes incidental Document context, not deliberately selected scalar content.
Failures remain visible under the producer's outcome and host diagnostic
contracts; they are not hidden to make the row stream look successful.

An unsupported explicit representation fails visibly before acquisition where
capability admission can determine that failure. The host does not silently
substitute another representation. Merely preferring a richer representation
does not authorize additional expensive or network work.

### JSON Document

`--json` serializes the structured Document, never rendered Markdown, TSV, or
string-valued formatter callbacks. A typical table-centric Document is
conceptually:

```json
{
  "summary": {
    "breaking": 1,
    "additive": 2
  },
  "rows": [
    { "change": "added", "type": "Example.Widget" }
  ]
}
```

This example illustrates composition, not prescribed property names, a complete
population, or a universal wire schema. Each owner retains its typed contract.
Hierarchy- and text-centric Documents retain their corresponding payloads
rather than manufacturing table rows. Selecting one section does not discard
its Document wrapper or interpretation context merely because one table
remains. An empty successful row population remains distinguishable from an
incomplete or unavailable result.

Supported section, row, field, and column projection operates on the Document's
declared typed content before serialization. It does not change `--json` to a
display-value dialect; an unsupported projection fails rather than being
ignored or silently selecting that dialect. Existing display-only projections
require explicit owner adoption rather than inferred mappings from labels.

This is a content-level composition, not a second service envelope.
`InspectionEnvelope<TContent>` retains its existing Content, Share, and
diagnostics boundary. Ordinary `--json` emits the Document content; an admitted
`--envelope` request emits the service envelope. Owner-specific non-available
outcomes retain their typed failure contracts rather than becoming empty
Documents.

### Motivation and adoption boundary

The concrete witness is repository source at
`4d86f7d84`, particularly
[`DiffTableView` and `DiffDetailedChangesView`](https://github.com/richlander/dotnet-inspect/blob/4d86f7d84/src/DotnetInspect.Cli/Views/DiffViews.cs#L13-L65):
both combine Versions and Summary fields with Changes rows.
[`LibraryApiDiffOutput`](https://github.com/richlander/dotnet-inspect/blob/4d86f7d84/src/DotnetInspect.Cli/Output/LibraryApiDiffOutput.cs#L113-L160)
passes those whole views to the tabular serializer.
[`CommandLine_ApiAnalysisMemberScopePreservesBreakingGenericConstraint`](https://github.com/richlander/dotnet-inspect/blob/4d86f7d84/tests/DotnetInspect.Cli.Tests/DiffCommandTests.cs#L3256-L3295)
even requires `Summary\t1 breaking` in `-S Changes --tsv` output. Complexity
Context and Structural Context use the same whole-view pattern.

That behavior motivates distinct Table and Document representations, not
deleting summaries from all formats. CLI row output is the immediate consumer;
JSON and Markdown consume the richer Document. Browser/Wasm consumers can
choose among the same owner-issued representations without adopting the CLI
preference order.

Implementation remains staged by owner. Diff is the motivating first adoption;
other command owners follow independently. Existing shape declarations,
public JSON schemas, and display-lowered JSON paths are not migrated by this
documentation change. Each implementation adoption must record its public
compatibility change and retire its superseded whole-Document tabular path.

The initial end-to-end plan is tracked by
[#10385](https://github.com/richlander/dotnet-inspect/issues/10385), with three
planned slices:

1. This representation-pattern specification, with no runtime adoption.
2. Shared Diff representations and CLI adoption, including retirement of
   whole-Document tabular serialization and superseded JSON dialect routing.
3. Browser/Wasm Diff adoption of the shared representations under its own
   presentation owner, retiring any superseded equivalent host-only projection.

Each implementation slice gets a focused issue before it begins. Three is the
initial plan's total, not a claim that every command has migrated. Other command
owners require separately tracked adoptions.

## Why shape belongs on the section

The section model was first made coherent for Markdown and then widened one
format at a time. Each widening put a rendering decision somewhere other than
the section: a command branch, a mode flag, or a renderer. The result is that
the same data shows up differently depending on which door the user came
through, and that machine formats inherit choices made for prose.

One real package shows the cost. Every observation below is current behavior
for `package System.Text.Json` at version 10.0.12.

- The bare Package Tree is a hierarchy of the selected compile Libraries, yet
  its title line lists every content directory in the package. The
  parenthetical names the coordinates of what is displayed, so `analyzers` and
  `buildTransitive` do not belong there when only `lib` children are shown.
  The list is assembled by command-specific printer code, so every command
  that wants a context line writes its own.
- The same Tree's JSON rows carry a shell-quoted `selector` that tells a
  person how to run the tool. That is host navigation, not package data. It
  belongs to the host's presentation, or, as a portable packet, to the
  envelope's Share slot.
- `-S "Package files"` is a flat Path/Size table, while `--layout` renders the
  same file inventory as a directory tree. One inventory, two unrelated
  presentations, chosen by which flag the user knew.
- `Package README file` is a one-row table that names a file. The user who
  selected it wanted the file. Showing it requires a second gesture, `--print`,
  that `type T -S Source` already proves unnecessary for a text payload.

Each of these is a presentation decision that should follow from what the
section is. A Hierarchy is shown as a tree. A Text is shown as its payload. A
Table is shown as rows. Markdown remains the composition format for a
multi-section answer, and JSON remains the complete structured one, but neither
is the native shape of a single selected section.

## Examples

The spellings below are **target** behavior over the motivating package.

Selecting nothing opens the command's default section. For `package` that is
the Package Tree, a Hierarchy, so the native rendering is a tree. Its title
prints the properties the owner issued for the displayed children: source,
target, and asset root.

```console
$ dotnet-inspect package System.Text.Json
System.Text.Json 10.0.12 (NuGet; net10.0; lib)
└─ System.Text.Json.dll
```

Selecting one Hierarchy renders its tree. The package file inventory is one
hierarchy whose rows are files and whose directory nodes are context.

```console
$ dotnet-inspect package System.Text.Json -S Files
System.Text.Json 10.0.12 (NuGet)
├─ Icon.png
├─ PACKAGE.md
├─ System.Text.Json.nuspec
├─ THIRD-PARTY-NOTICES.TXT
├─ analyzers/dotnet/roslyn3.11/cs
│  ├─ System.Text.Json.SourceGeneration.dll
│  └─ ...
├─ buildTransitive/...
└─ lib
   ├─ net10.0/System.Text.Json.dll
   └─ ...
```

Selecting one Table renders its rows. A row stream is the native format; a
Markdown table is how the same rows appear inside a multi-section document.

```console
$ dotnet-inspect package System.Text.Json -S "Target Frameworks"
tfm
net10.0
net9.0
net8.0
netstandard2.0
net462
```

Selecting one Text renders its payload.

```console
$ dotnet-inspect package System.Text.Json -S README
## About

Provides high-performance and low-allocating types that serialize objects to
JavaScript Object Notation (JSON) text ...
```

Selecting several sections composes a Markdown document. Inside it, a
Hierarchy appears as its tree, a Table as a Markdown table, and a Text as
its fact row; rendered text shows a Text's body only when the Text is
the whole selection. JSON composes the same selection as structured content,
with every Text's content included.

```console
$ dotnet-inspect package System.Text.Json -S "Target Frameworks" -S README
# System.Text.Json

## Target Frameworks

| TFM |
| --- |
| net10.0 |
...

## README

| Path | Size |
| --- | --- |
| PACKAGE.md | 8582 |
```

An explicit format always wins over the native one, within the shape's
permitted lowerings: `-S Files --tsv` streams the file rows flat, and
`-S "Target Frameworks" --json` carries the rows as an array value inside the
JSON root that [Projected JSON](projected-json.md) owns. An explicit format a
shape cannot carry fails before acquisition and names the shape.

## The three shapes

A semantic shape is a statement about the result, not about a renderer. The
definitions below constrain its offered representations; they do not restrict
the section to one representation.

The earlier per-shape JSON lowerings below describe payloads within the
structured Document, not permission to omit that Document.
[Projected JSON](projected-json.md) records the display-lowered compatibility
path. Its dialect routing remains current behavior only until an owner adopts
the Document representation contract above.

### Table

A Table is an ordered set of rows sharing one column schema. Its row unit is
whatever the owner declares — a target framework, a dependency, a type, a
finding — and nothing in the presentation adds or removes rows.

The native Table rendering is the row stream: TSV in the CLI. Permitted lowerings are
the pretty table, JSONL, a JSON value that is an array of row objects, the
Markdown table under the section heading whether the Table is selected alone
or composed, and the Count and projection steps of the
[output-shape ladder](output-shapes.md#the-shape-ladder). Under the representation
contract, that JSON array is the Table payload within a Document.

A scalar record such as `Package Info` or single-Library `Library Info` is a
Table whose row unit is a field entry. [Section
cardinality](section-cardinality.md) classifies it scalar, so it exposes
neither Rows nor Count; its tabular lowering is the two-column field listing
and its JSON lowering keeps the object form.

### Hierarchy

A Hierarchy is a Table whose rows also carry an owner-issued parent. The rows
are the result; the parent chain is how the owner explains them. A directory
is context for the files beneath it, a package is context for its Libraries, a
root occurrence is context for the dependency occurrences it reaches.

The native format is the tree. Permitted lowerings are the flat row stream,
pretty table, and JSONL rows, each carrying the parent as a column; a nested
JSON value in which each row's children are an array; and Markdown, which
renders the tree under the section heading whether the Hierarchy is selected
alone or composed. A Hierarchy has no Markdown-table form. A tree node that is
only context is never a row: Count, `-n`, `--rows`, and every flat lowering
see the same rows the tree shows as leaves or as rows of their own.

Mermaid is a permitted Hierarchy lowering, never the native one. It draws
the nodes and owner-issued parent edges the tree shows, in the same order, so
it adds no node, edge, or row. [Host-neutral hierarchy
projection](host-neutral-hierarchy-projection.md) owns the shared Tree and
Mermaid lowerings of one owner-issued hierarchy, and [Relationship Section
Naming](relationship-section-naming.md#projections-do-not-rename-results)
already holds that a Mermaid rendering of `Dependency Hierarchy` does not
become `Dependency Graph`. The diagram does not change the shape: a result
whose identity is the union of nodes and typed edges — several parents,
cycles, or edge kinds — is a Graph, not a Hierarchy, and the Graph shape,
with its own diagram formats, remains with its Graph owners.

### Text

A Text is one text payload with the scalar facts that identify it: path,
size, provenance, and whatever else the owner issues. A README, a nuspec, a
skill, a decompiled or authored source body, and a diff are Texts.

A section is a Text only when its owner guarantees at most one payload for
the selected subject: the best README, the root manifest, the selected
member's source. The guarantee comes from the owner's selection rule, not from
the hope that a file matcher yields one hit. A package's nuspec Text is the
root manifest, which the package format places exactly once; other files with
the same extension are ordinary rows of the file inventory. When a selection
rule can yield several files, as license files and skill documents can, the
section is a Table whose row unit is a Text fact row. Each row names a
payload the command can open through its existing path selection, and the
opened payload is a Text.

The native format is the text itself, undecorated. Permitted lowerings are a
JSON value that is an object carrying the facts and the content, complete
whether the Text is selected alone or composed; Markdown, which frames the
body under the section heading when the Text is the whole selection and
shows the fact row when it is composed with other sections; and the row
formats, whose row unit depends on whether the owner declares an inventory, as
the next paragraph states. Rendered text therefore shows the body only for a
whole selection, while structured output always carries it.

A Text's **fact row** is a summary presentation, not an inventory. It is
how a Text participates in a composed Markdown document without flooding
it, and how Texts and Text-row Tables selected together remain one
homogeneous family listing: a package's nuspec and README Texts and its
license and skill Tables all lower to Path/Size rows. Row selection never
applies to a fact row.

A Text whose owner declares no inventory is scalar under
[Section cardinality](section-cardinality.md): it has no Count, `-n` and
`--rows` do not apply to it, and its row formats show its fact row. A Text
whose owner declares a line inventory, as
[Source document cardinality](source-document-cardinality.md) does for type and
member Source, exposes that inventory as its rows in every row format: TSV,
pretty table, and JSONL emit lines, and Count, `-n`, and `--rows` observe those
lines as that owner specifies. Such a Text still contributes its fact row
to a composed Markdown document and to a family listing, where no row
selection is in effect.

A large Text is never clipped silently. Each Text owner supplies its
own completeness behavior: type and member Source continue through the ordered
`Lines` inventory owned by
[Source document cardinality](source-document-cardinality.md); other owners
mark the page incomplete and keep a complete transfer gesture available.

## Properties

Every section result may carry an ordered set of owner-issued **properties**:
short named values that give the result its context. Source, target framework,
asset root, provenance, and version are properties. They are neither content
rows nor the fields of a scalar record. They describe the circumstances under
which the rows, hierarchy, or document was produced, and they are the only
material a renderer may place in a title or heading beside the subject's
identity.

Properties are issued, not inferred. The owner decides which properties a
result carries and in what order, and the renderer prints what it was given.
A renderer never computes a property from the subject, the command, or the
content; a command that wants a context line issues properties instead of
writing a printer. One renderer therefore serves every command, and a new
section gains a context line by issuing properties rather than by changing
presentation code.

Where properties appear follows from the format:

- a tree prints them once, after the subject identity on the title line, in
  issued order;
- a Markdown composition prints subject-level properties after the subject
  identity on the document title, and a section's properties after the
  section name on that section's heading;
- a native Text prints its payload only, because the payload is the whole
  output and a title would decorate it; its properties appear when the same
  Text is rendered inside a composition; and
- a row stream has no place for them and omits them, because a header row
  describes columns, not context.

The Document representation retains owner-issued interpretation context under
its typed contract; this document prescribes no universal property container
or machine keys. The earlier display-lowered compatibility path in
[Projected JSON](projected-json.md) has no per-section property association and
continues to omit these properties until its owner adopts the new contract.
Row streams continue to omit them.

The subject's identity itself — the package, library, type, or member being
inspected — is not a property. Identity is owned by the subject's resolution;
properties qualify what was displayed about it.

## Invariants every lowering keeps

**Lowerings present; they do not author.** A lowering may elide, group, nest,
indent, or fence what the section issued. It may not add content. Replay
commands, selectors, and other host navigation belong to the host's own
presentation; a portable replay currency belongs to the `InspectionEnvelope`
Share slot, which carries a packet or URL, not shell text. Neither belongs in a
content row or field. A consumer reading the content must not be able to tell
which host produced it.

**Titles and headings print issued properties and nothing else.** Where a
format has a title, it carries the subject identity and the subject-level
properties; where it has section headings, each carries the section name and
that section's properties. Facts about the subject that the user did not
select belong in their own sections, not in a title or heading, and no renderer
adds a value the owner did not issue.

**Count is shape-invariant.** For an inventory section, Count, `-n`, and
`--rows` observe the same rows in every permitted lowering that carries rows.
Tree context nodes, Markdown headings, fences, and a Text's fact row are
presentation, not rows, and a summary presentation carries no row selection.

**Explicit intent wins.** When the user names a format, the shape's permitted
lowerings decide only whether the request is admissible. An inadmissible pair
fails before acquisition and names the shape and the formats it supports. The
host never substitutes a different lowering or a different section.

**Document composition can carry every semantic shape.** JSON requires an
offered Document representation for each selected section. A Table-only
section therefore rejects `--json`; the host does not manufacture an unoffered
Document. Markdown uses the offered Document or its declared Table/Hierarchy
fallbacks. Row-stream formats require either one section or a family whose
members lower to one homogeneous row schema; the
[section model](section-model.md#output-shapes) owns that heterogeneity rule
and this document adds only that a Text's fact row counts as its row
schema for the purpose.

**Representations are discoverable.** The semantic shape and offered
representations are owner-issued declarations, exposed through the same
discovery surface as the section's name and categories. Its supported-format
list derives from those declarations. The
[schema query](schema-query.md) design owns the discovery surface; [Output
shapes](output-shapes.md#structural-format-capabilities) owns how a host
evaluates a complete selection against those capabilities.

## Owner map

| Concern | Owner | Boundary |
| --- | --- | --- |
| Semantic shapes, offered representations, selection policy, properties, lowering invariants | This document | Defines representation alternatives and what every lowering preserves |
| Section identity, categories, selection, verbosity, heterogeneity | [Section Model](section-model.md) and [Section Pipeline](section-pipeline.md) | Apply authored shape without deriving it from spelling or renderer |
| Scalar versus inventory, Rows and Count | [Section Cardinality](section-cardinality.md) | Decides whether a section has rows at all |
| Shape ladder, projection, complete-selection capability evaluation | [Output Shapes](output-shapes.md) | Narrows a selected shape and evaluates a selection; supported modes derive from the declared shape |
| Format spellings, admission, defaults, destination | [CLI Output Format and Destination](cli-output-format-and-destination.md) | Spells formats; admits or rejects a command-format pair using the shape's lowerings |
| Relationship section names, Hierarchy versus Graph | [Relationship Section Naming](relationship-section-naming.md) | Names the result; this document presents it |
| Streaming Tree and Mermaid lowering of an owner-issued hierarchy | [Host-neutral hierarchy projection](host-neutral-hierarchy-projection.md) | Lowers without a second retained tree; this document admits the lowerings |
| Typed JSON representability | [Projected JSON](projected-json.md) | Decides when a lowered JSON shape is representable |
| Text completeness | [Source Document Cardinality](source-document-cardinality.md) for type and member Source; each other Text owner for its own payloads | Owns `Lines`, exact Count, and continuation for Source; other owners supply their own incomplete-page and complete-transfer behavior |
| Discovery surface | [Schema Query](schema-query.md) | Exposes shape beside name and kind |
| Each command's sections | The command owner | Classifies its sections and chooses its names and defaults |
| Rendering | Markout and host writers | Produce the lowering without changing the content |

This document transfers one claim from Output Shapes: a section's
supported-format capabilities are derived from its declared shape rather than
declared independently. It transfers no evidence, query, result, or host
ownership.

## Pathological cases

Conforming adoptions preserve these outcomes:

1. **One-row hierarchy.** A package with one compile Library still renders a
   tree with a root and one leaf. Shape does not collapse on cardinality.
2. **Absent document.** A package without a README makes the README section
   ineffective. It does not render an empty Text or an empty fact row, and
   a multi-section composition omits it under the ordinary section model.
3. **Flattened hierarchy.** `-S Files --tsv` and `-S Files --count` observe
   exactly the file rows the tree shows; directory nodes contribute no rows.
4. **Heterogeneous row request.** `-S README -S "Target Frameworks" --tsv` is
   rejected with the two shapes named; the same selection without a format
   composes Markdown.
5. **Homogeneous document family.** `-S @Files --tsv` streams Path/Size rows
   because the nuspec and README Texts lower to their fact rows and the
   license and skill Tables already have that row schema.
6. **Explicit tabular on a hierarchy.** `-S Files --table` is admissible and
   flat; the parent column makes the flattening lossless.
7. **Inadmissible explicit format.** `-S "Target Frameworks" --tree` fails
   before acquisition, naming Table and its permitted lowerings. It does not
   quietly render a table.
8. **Large text.** A Text whose body exceeds the presentation budget
   is marked incomplete on its first page and names how to obtain the rest
   under its owner's contract: `Lines` continuation for type and member
   Source, which may not know the exact remainder before exhaustion, or a
   complete transfer for a package README. It never ends as though complete.
9. **Host text in content.** A JSON content row containing a replay command or
   a shell-quoted selector is a defect, whichever host produced it.
10. **Renderer-computed context.** A title value that no owner issued — a
    directory list assembled by the printer, a count derived while rendering —
    is a defect even when it happens to be true.

## Analogous implementations

`kubectl get` and the GitHub CLI default to tabular rows and switch to
structured output only on an explicit flag; the GitHub CLI also narrows its
default to plain tab-separated rows when standard output is not a terminal.
`tree` and `dotnet nuget why` render rooted explanations as trees and offer no
table because their result is the ancestry. `cat` and `git show` print a
single text payload undecorated. None of these tools treats a human-readable
multi-section report as the native form of a single data question.

The assumption that transfers is that a tool used by both people and programs
should pick its default from what the answer is, not from what reads best in a
report. The terminal-versus-pipe narrowing is evidence for the CLI format owner
to weigh; it is not adopted here.

## Production adoption

Adoption is staged by command owner. Each adoption classifies its sections,
chooses section names under the existing naming rules, decides its default
section, and migrates any public JSON schema deliberately. The naming rule
that a section does not repeat the coordinate its route already established
is owned by [Relationship Section
Naming](relationship-section-naming.md#canonical-grammar) and applies here
unchanged.

1. **Package owner.** The first adoption classifies the Package Tree and the
   file inventory as Hierarchies, `Target Frameworks` and `Dependencies` as
   Tables, `Package Info` as a scalar record, the root manifest and the best
   README as Texts, and the license and skill sections as Tables of
   Text fact rows. Other `.nuspec` paths remain rows of the file inventory.
   The Tree issues source, target, and asset root as properties and retires
   its command-specific title printer. Its target spellings are `Files`,
   `Nuspec`, `README`, `Licenses`, and `Skills`, with the whole-package
   listing outside the `@Files` door as today. Replay selectors leave the
   JSON content.
2. **Library owner.** Classifies the `library` sections, including the Type
   inventory and `Library Info`, under the same three shapes (#9511). Every
   section is a Table except `Reference Hierarchy`, a Hierarchy of rooted
   reference occurrences whose Mermaid format is its explicit diagram
   lowering, and `Dependency Structure`, a Graph of namespace nodes and edges
   that keeps its diagram formats. The library catalog has
   no Text. Its field-set records — single-Library `Library Info`, the
   SourceLink availability and integrity records, `Symbols`, and
   `Metadata: Image` — are scalar Tables; every other section is an
   inventory. The coordinate-scoped `Context:` sections that locate one
   thing — `Source Location`, `Member`, `Instruction`, `Callsite`, and
   `Return Address` — are one-row Tables whose row unit is the located
   coordinate, as a member `Signature` is. `Context: Exception`,
   `Allocation`, `Safety`, and `Cost` list the regions or facts at the
   coordinate, one row each, and `Metadata: Heap` rows are heap entries.
   A lone selected Table streams its rows and `Reference Hierarchy` renders
   its tree when no format is named; `Dependency Structure`, declaring no
   shape, keeps its rendering.
3. **Type owner.** Classifies the `type` sections, including the member tree
   and the `Source` family. The default Type overview is a Hierarchy: the
   Type, its categories as context, and its MemberGroups as rows, each
   carrying its exact Member Count.
   [Type and member inspection documents](type-member-inspection-documents.md#hierarchy-projection)
   owns that topology. Its native lowering is the tree and Mermaid is its
   explicit diagram lowering. It is the command's unselected overview rather
   than a cataloged section, so the CLI adoption that streams it (#9478)
   carries its shape declaration.
4. **Member owner.** Classifies the `member` sections, including overload
   inventories, call relationships, and source and decompiler Texts.

   The type and member owners' Texts lower as follows (#9456). `Source` keeps
   its `Lines` inventory. Every other Text with one bare payload — API
   declarations, decompiled, annotated, PDB, and diffed source, the overlays,
   and IL — shows in its row formats a fact row of section, line count, and
   character count; diffed source keeps its owner's richer row form, the
   comparison metadata and summary. Its JSON value is those facts plus the
   complete payload, except where an owner's dedicated JSON lowering already
   carries the complete content: the API declarations' inspection envelope,
   `Source`'s document, the finding census, and the annotated source
   document keep theirs. A Text without a bare payload keeps its composed and dedicated
   lowerings. **Divergence:** a type or member Text composed with other
   sections shows its body in Markdown rather than its fact row, because
   reading two code views side by side (`-S "Decompiled Source,IL"`) is the
   owners' primary composed use; the operator approved this divergence on
   2026-10-06.

Each adoption changes only its owning command and is tracked by its own
focused issue. This document authorizes no cross-command sweep, and it does
not change Browser/Wasm presentation; the Browser consumes the same declared
shape through the shared discovery and envelope contracts and chooses its
widgets under its own presentation owners.

## Evidence and gates

This specification is documentation-only. `markdownlint` and
`git diff --check` gate its Markdown and patch integrity.

Runtime conformance remains **unverified** until each adoption adds Release
gates covering, for its command:

- Table output with exactly one schema and no Document-summary rows, including
  an empty primary population;
- agreement between Document payload rows and Table rows after the same
  selection, with declared summary scope preserved;
- JSON Document serialization with native types, retained context, and
  projection that never switches to display-lowered JSON;
- default representation preference and explicit Markdown Document preference
  with Table and Hierarchy fallbacks;
- the declared shape of every section in discovery output;
- the native lowering for each shape when exactly one section is selected
  and no format is named;
- explicit-format precedence and pre-acquisition rejection of an inadmissible
  shape-format pair, naming the shape;
- Count, `-n`, and `--rows` invariance across the tree, flat, and structured
  lowerings of each Hierarchy;
- for each Text, its fact row in composed Markdown and family listings,
  its framed body in lone Markdown output, its complete JSON value alone or
  composed, its bare body natively, and in row formats either its fact row
  (no declared inventory) or its owner-declared inventory rows;
- the absence of host replay text from structured content;
- title and heading text composed only from the subject identity and issued
  properties, a bare payload for a native Text, and no properties in row
  streams; and
- the one-row hierarchy, absent document, heterogeneous request, and
  homogeneous family cases above.

No TLA+ model is required. The contract has no concurrent or stateful
protocol; its correctness is expressed by authored classification and
observable lowering gates.

## Non-goals

This document does not:

- rename current runtime sections or change current defaults before adoption;
- define a Graph shape, which remains with its Graph owners;
- define format spellings, the environment default, or terminal detection;
- define JSON schemas for any command;
- define Browser/Wasm widgets or interaction;
- change section identity, categories, verbosity presets, or cost classes;
- define discovery output columns;
- fix the vocabulary of property names, which each owner issues;
- impose a universal schema for Document context properties; or
- authorize one implementation sweep across commands.
