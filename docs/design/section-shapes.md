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

[Relationship Section Naming](relationship-section-naming.md) is the merged
precedent: it names a relationship section by its semantic result shape. This
document applies the same discipline to presentation. A section's shape, not
the command that hosts it and not a renderer flag, decides how the section can
be shown.

## Normative owner and claim

Section Shapes owns one claim:

> After a section's semantic result is known, the section declares exactly one
> shape — Table, Hierarchy, or Document — and that shape decides which output
> formats can present the section, which format is native when the user
> selects that one section and names no format, and what each supported
> format preserves when it lowers the section.

This owner defines the three shapes, the native and permitted lowerings of
each, the owner-issued properties that give a result its context, the
invariants every lowering keeps, and the boundary between content and host
presentation. It does not own section identity, categories, selection,
verbosity, cardinality, discovery surfaces, format spellings, or any
command's data.

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
section is. A Hierarchy is shown as a tree. A Document is shown as its text. A
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

Selecting one Document renders its text.

```console
$ dotnet-inspect package System.Text.Json -S README
## About

Provides high-performance and low-allocating types that serialize objects to
JavaScript Object Notation (JSON) text ...
```

Selecting several sections composes a Markdown document. Inside it, a
Hierarchy appears as its tree, a Table as a Markdown table, and a Document as
its fact row; rendered text shows a Document's body only when the Document is
the whole selection. JSON composes the same selection as structured content,
with every Document's content included.

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

A shape is a statement about the result, not about a renderer. The owner
classifies the section once, from the semantic result it issues, and every
host and format follows that classification.

Each shape's JSON lowering below describes the section's JSON value. Whether
that value is the root, how it sits inside a document or envelope, and whether
a request routes to typed or lowered JSON are owned by
[Projected JSON](projected-json.md); this document does not change that
routing.

### Table

A Table is an ordered set of rows sharing one column schema. Its row unit is
whatever the owner declares — a target framework, a dependency, a type, a
finding — and nothing in the presentation adds or removes rows.

The native format is the row stream: TSV in the CLI. Permitted lowerings are
the pretty table, JSONL, a JSON value that is an array of row objects, the
Markdown table under the section heading whether the Table is selected alone
or composed, and the Count and projection steps of the
[output-shape ladder](output-shapes.md#the-shape-ladder).

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

Mermaid is not a Hierarchy lowering. A result whose identity is the union of
nodes and typed edges is a Graph, not a Hierarchy;
[Relationship Section Naming](relationship-section-naming.md) owns that
distinction, and the Graph shape, with its diagram formats, remains with its
Graph owners.

### Document

A Document is one text payload with the scalar facts that identify it: path,
size, provenance, and whatever else the owner issues. A README, a nuspec, a
skill, a decompiled or authored source body, and a diff are Documents.

A section is a Document only when its owner guarantees at most one payload for
the selected subject: the best README, the root manifest, the selected
member's source. The guarantee comes from the owner's selection rule, not from
the hope that a file matcher yields one hit. A package's nuspec Document is the
root manifest, which the package format places exactly once; other files with
the same extension are ordinary rows of the file inventory. When a selection
rule can yield several files, as license files and skill documents can, the
section is a Table whose row unit is a Document fact row. Each row names a
payload the command can open through its existing path selection, and the
opened payload is a Document.

The native format is the text itself, undecorated. Permitted lowerings are a
JSON value that is an object carrying the facts and the content, complete
whether the Document is selected alone or composed; Markdown, which frames the
body under the section heading when the Document is the whole selection and
shows the fact row when it is composed with other sections; and the row
formats, whose row unit depends on whether the owner declares an inventory, as
the next paragraph states. Rendered text therefore shows the body only for a
whole selection, while structured output always carries it.

A Document's **fact row** is a summary presentation, not an inventory. It is
how a Document participates in a composed Markdown document without flooding
it, and how Documents and Document-row Tables selected together remain one
homogeneous family listing: a package's nuspec and README Documents and its
license and skill Tables all lower to Path/Size rows. Row selection never
applies to a fact row.

A Document whose owner declares no inventory is scalar under
[Section cardinality](section-cardinality.md): it has no Count, `-n` and
`--rows` do not apply to it, and its row formats show its fact row. A Document
whose owner declares a line inventory, as
[Source document cardinality](source-document-cardinality.md) does for type and
member Source, exposes that inventory as its rows in every row format: TSV,
pretty table, and JSONL emit lines, and Count, `-n`, and `--rows` observe those
lines as that owner specifies. Such a Document still contributes its fact row
to a composed Markdown document and to a family listing, where no row
selection is in effect.

A large Document is never clipped silently. Each Document owner supplies its
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
- a native Document prints its payload only, because the payload is the whole
  output and a title would decorate it; its properties appear when the same
  Document is rendered inside a composition; and
- a row stream has no place for them and omits them, because a header row
  describes columns, not context.

Structured formats do not carry properties under this document.
[Projected JSON](projected-json.md) gives each section one value and owns the
root namespace, so a per-section property association is a Projected JSON
design question. Until that owner defines one, structured output omits
properties, and no adoption invents a carriage of its own.

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
Tree context nodes, Markdown headings, fences, and a Document's fact row are
presentation, not rows, and a summary presentation carries no row selection.

**Explicit intent wins.** When the user names a format, the shape's permitted
lowerings decide only whether the request is admissible. An inadmissible pair
fails before acquisition and names the shape and the formats it supports. The
host never substitutes a different lowering or a different section.

**Composition formats carry every shape.** Markdown and JSON can present any
selection of sections. Row-stream formats require either one section or a
family whose members lower to one homogeneous row schema; the
[section model](section-model.md#output-shapes) owns that heterogeneity rule
and this document adds only that a Document's fact row counts as its row
schema for the purpose.

**Shape is discoverable.** The shape is an owner-issued property of the
section, exposed through the same discovery surface as its name and
categories, and the section's supported-format list derives from it. The
[schema query](schema-query.md) design owns the discovery surface; [Output
shapes](output-shapes.md#structural-format-capabilities) owns how a host
evaluates a complete selection against those capabilities.

## Owner map

| Concern | Owner | Boundary |
| --- | --- | --- |
| Shape classification, native and permitted lowerings, properties, lowering invariants | This document | Defines the three shapes, the properties contract, and what every lowering preserves |
| Section identity, categories, selection, verbosity, heterogeneity | [Section Model](section-model.md) and [Section Pipeline](section-pipeline.md) | Apply authored shape without deriving it from spelling or renderer |
| Scalar versus inventory, Rows and Count | [Section Cardinality](section-cardinality.md) | Decides whether a section has rows at all |
| Shape ladder, projection, complete-selection capability evaluation | [Output Shapes](output-shapes.md) | Narrows a selected shape and evaluates a selection; supported modes derive from the declared shape |
| Format spellings, admission, defaults, destination | [CLI Output Format and Destination](cli-output-format-and-destination.md) | Spells formats; admits or rejects a command-format pair using the shape's lowerings |
| Relationship section names, Hierarchy versus Graph | [Relationship Section Naming](relationship-section-naming.md) | Names the result; this document presents it |
| Typed JSON representability | [Projected JSON](projected-json.md) | Decides when a lowered JSON shape is representable |
| Document completeness | [Source Document Cardinality](source-document-cardinality.md) for type and member Source; each other Document owner for its own payloads | Owns `Lines`, exact Count, and continuation for Source; other owners supply their own incomplete-page and complete-transfer behavior |
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
   ineffective. It does not render an empty Document or an empty fact row, and
   a multi-section composition omits it under the ordinary section model.
3. **Flattened hierarchy.** `-S Files --tsv` and `-S Files --count` observe
   exactly the file rows the tree shows; directory nodes contribute no rows.
4. **Heterogeneous row request.** `-S README -S "Target Frameworks" --tsv` is
   rejected with the two shapes named; the same selection without a format
   composes Markdown.
5. **Homogeneous document family.** `-S @Files --tsv` streams Path/Size rows
   because the nuspec and README Documents lower to their fact rows and the
   license and skill Tables already have that row schema.
6. **Explicit tabular on a hierarchy.** `-S Files --table` is admissible and
   flat; the parent column makes the flattening lossless.
7. **Inadmissible explicit format.** `-S "Target Frameworks" --tree` fails
   before acquisition, naming Table and its permitted lowerings. It does not
   quietly render a table.
8. **Large document.** A Document whose body exceeds the presentation budget
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
   README as Documents, and the license and skill sections as Tables of
   Document fact rows. Other `.nuspec` paths remain rows of the file inventory.
   The Tree issues source, target, and asset root as properties and retires
   its command-specific title printer. Its target spellings are `Files`,
   `Nuspec`, `README`, `Licenses`, and `Skills`, with the whole-package
   listing outside the `@Files` door as today. Replay selectors leave the
   JSON content.
2. **Library owner.** Classifies the `library` sections, including the Type
   inventory and `Library Info`, under the same three shapes.
3. **Type owner.** Classifies the `type` sections, including the member tree
   and the `Source` family.
4. **Member owner.** Classifies the `member` sections, including overload
   inventories, call relationships, and source and decompiler Documents.

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

- the declared shape of every section in discovery output;
- the native lowering for each shape when exactly one section is selected
  and no format is named;
- explicit-format precedence and pre-acquisition rejection of an inadmissible
  shape-format pair, naming the shape;
- Count, `-n`, and `--rows` invariance across the tree, flat, and structured
  lowerings of each Hierarchy;
- for each Document, its fact row in composed Markdown and family listings,
  its framed body in lone Markdown output, its complete JSON value alone or
  composed, its bare body natively, and in row formats either its fact row
  (no declared inventory) or its owner-declared inventory rows;
- the absence of host replay text from structured content;
- title and heading text composed only from the subject identity and issued
  properties, a bare payload for a native Document, and no properties in row
  streams or structured output; and
- the one-row hierarchy, absent document, heterogeneous request, and
  homogeneous family cases above.

No TLA+ model is required. The contract has no concurrent or stateful
protocol; its correctness is expressed by authored classification and
observable lowering gates.

## Non-goals

This document does not:

- rename current runtime sections or change current defaults before adoption;
- define a fourth shape, or a Graph shape, which remains with its Graph owners;
- define format spellings, the environment default, or terminal detection;
- define JSON schemas for any command;
- define Browser/Wasm widgets or interaction;
- change section identity, categories, verbosity presets, or cost classes;
- define discovery output columns;
- fix the vocabulary of property names, which each owner issues;
- define how structured formats carry properties, which Projected JSON owns; or
- authorize one implementation sweep across commands.
