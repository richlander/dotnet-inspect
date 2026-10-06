# Product Vocabulary

`dotnet-inspect vocabulary` exposes the stable values accepted by product-owned
queries. It is an inspection document, not a help-text command or an enum dump:
sections are vocabularies, and section rows are legal values.

## Data all the way down

Vocabulary uses the ordinary output model:

```bash
dotnet-inspect vocabulary
dotnet-inspect vocabulary -D
dotnet-inspect vocabulary -S Accessibility
dotnet-inspect vocabulary -S "C# Style Choices" --json
dotnet-inspect vocabulary -S "C# Body Kinds"
dotnet-inspect vocabulary -S Accessibility -n 2 --tail
dotnet-inspect vocabulary -S "C#*" --count
```

- Bare `vocabulary` renders a compact `Vocabulary Sections` index with the
  section name, summary, and value count. The index lists the value
  vocabularies, not itself.
- `-D` discovers sections and fields.
- `-S` selects the values to materialize by exact section name, stable section
  ID, or glob.
- `--columns` and `--fields` project values. `-n` selects Head rows by default
  and Tail rows with `--tail`; `--rows` accepts one-based inclusive `N..M`,
  `N..`, and `..M` windows. These gestures compose in argument order and apply
  independently to every selected vocabulary section through the shared
  semantic row-selection path. Discovery retains its existing structural-row
  window behavior.
- `--count` collapses each selected row set after projection and semantic row
  selection.
- Markdown, plain text, table, TSV, JSONL, and JSON use the same section and row identities.

`VocabularyCommandTests.CommandLine_HeadTailAndBareLimitUseSemanticRows`,
`CommandLine_ComposesSemanticStagesInArgumentOrder`,
`Command_MultiSectionStrictWindowFailsWithoutPartialOutput`, and
`CommandLine_MultiSectionCountObservesSemanticWindow` gate the CLI grammar,
ordered execution, all-or-failure behavior, and terminal count composition in
Release. Predicate, baseline-order, and Top adoption remain with the shared
row-query and CLI owners tracked by #5162, #5414, and #6489; vocabulary does
not implement a command-local substitute.

Markdown, plain text, table, TSV, JSONL, and projected JSON lower one typed
`VocabularyView` through `MarkoutSerializer` and
`VocabularyViewContext`. Runtime-named sections and runtime-column tables keep
the composed snapshot authoritative for names, field labels, stable field IDs,
and row order; that snapshot is the host-composed snapshot described under
[Ownership](#ownership). Each runtime section carries its summary as an ordinary
Markout paragraph because unwrapped child sections lower their content rather
than their `DescriptionProperty` metadata. `VocabularyCommandTests` gates these
formats in Release, including
`Command_DefaultRendersTheSelfDescribingSectionIndex`,
`Command_PlainTextUsesThePlainTextFormatter`, and
`Command_JsonlUsesProjectedRuntimeColumns`.

Plain unprojected `--json` is an approved CLI-host exception to ordinary
Markout lowering. Its typed input is the selected owner-issued
`VocabularySection` sequence plus the catalog schema version, and its lowering
boundary is `VocabularyWireDocument` through the generated
`VocabularyWireJsonContext` or `VocabularyWireCompactJsonContext`. This path
preserves the established schema-versioned document containing section
metadata, field schemas, operators, accepted-command identities, and typed
value cells; the lowered Markout table shape cannot represent that contract
without discarding schema or changing typed values to display strings. The
exception is limited to unprojected CLI `--json`; every human, tabular, stream,
and projected-JSON path uses the typed Markout view. The Release gates are
`JsonSerialization_PreservesWireShapeAcrossIndentationModes`,
`Command_JsonCarriesTypedSchemaAndValues`, and
`Command_PartialMachineKeyProjectionKeepsSectionIdentityAcrossFormats`.

The structured document carries a schema version. Every section declares its
stable ID, accepted query inputs, field schema, legal operators, and typed
values. A stable value ID can therefore flow from discovery or a website picker
back into a typed query without parsing labels.

The catalog is intentionally flat. Its small section corpus does not warrant
categories, category-first discovery, or category selectors. Exact names and
globs provide the complete multi-section selection model. Schema version 2
removes the former `categories` member from structured vocabulary sections.

## Ownership

Each vocabulary is declared by the owner of its terms as one immutable
`VocabularyDefinition` beside that owner's catalog. Because a vocabulary
identity carries its catalog identity and the product catalog name is a
product concept, an owner declares through a deterministic factory over the
host's `VocabularyCatalogIdentity` (`StyleOptionVocabularies`,
`BodyShapeVocabulary`, `ApiAccessibilityVocabulary`); the vocabulary's stable
identity, display label, maps, terms, and order are the owner's, and equal
inputs yield equal declarations. A host composes the declarations it ships into
one exactly identified snapshot, under the tier-1 composition rule the
[QuerySpace library boundary](query-space-library.md#two-assemblies-and-two-participation-tiers)
states. No component owns the complete list of product vocabularies; the
Product Vocabulary document schema and the CLI's section descriptors, described
below, each name the sections they present.

The term owners, and therefore the declaring owners, are:

- `ApiAccessibility` in `DotnetInspector.Queries` owns accessibility identity,
  order, defaults, and classification.
- `StyleOptionCatalog` in `ILInspector.Decompiler` owns C# style tiers,
  selectable choices, conflicts, endorsement, and byte-divergence properties.
- `BodyShapeSearch.SupportedKinds` in `ILInspector.Decompiler` owns searchable
  body-kind identity and order; `AnnotatedSourceNodeKinds` owns their display
  labels.

The declaration type lives in the `QuerySpace.Primitives` floor under the
[QuerySpace library boundary](query-space-library.md#two-assemblies-and-two-participation-tiers),
so an `ILInspector` owner can declare without referencing any product
assembly. A Query Space facet that is bounded by a vocabulary names it by
identity on its facet descriptor, through the opaque value-vocabulary identity
that [Query Space Composition](query-space-composition.md) already defines; no
facet sets one today. The Body Shapes `Kind` predicate that accepts
`csharp.body-kinds` is a CLI section query key, not a Query Space facet, so it
has no facet descriptor. Its CLI query-key descriptor carries the link
instead: `SectionQueryKey.ValueVocabulary` names the vocabulary and its
canonical explanation path, derived from the owner's identity constant and
`ResourceExplanationCatalog.VocabularyPath`, and `-Q` presents
`explain vocabularies/csharp.body-kinds` for the key. Each composed
vocabulary is explainable at `vocabularies/<id>` under
[Resource Explanation](resource-explanation.md#value-vocabulary-resources),
which names its accepted query inputs. The product vocabulary document, its
sections, fields, operators, rows, and wire projection are declared section
schemas owned by `DotnetInspector.Sections`.

Each host composes the vocabularies it ships. The CLI and Inspect Web each
pass their own list of owner declarations, with the product query inputs that
accept each one, to `ProductVocabularyComposition` in
`DotnetInspector.Sections`. That composition adds only the product catalog
identity and the `vocabulary.sections` index, whose `accepted_by` values name
product query inputs and therefore stay with the host rather than with a term
owner. It rejects a declaration under another catalog and a contribution with
no accepting input. Hosts may select a section for a purpose-specific control,
but they do not restate its values, labels, order, defaults, or selection
semantics. Equal contribution lists yield one snapshot identity, so drift
between the hosts is detectable by identity. The gate is one pinned digest,
`ProductVocabularyPin` in `tests/DotnetInspect.Web.Tests`, which the CLI suite
compiles as a linked file. The CLI suite
(`ProductVocabularySnapshotTests`) and the Inspect Web suite
(`BrowserVocabularyCompositionTests`) each assert their own host's composed
snapshot against that one value. No test project references both hosts, so the
shared pin stands in for a direct comparison: changing the pin for one host's
list fails the other host's suite until that host composes the same
snapshot. The pin lives in the Inspect Web suite because CI selects jobs by
changed path: a change under `tests/DotnetInspect.Web.Tests` runs both the
Inspect Web and CLI lanes, while a CLI-only change does not run the Inspect
Web lane.

The declaration types live in `QuerySpace.Primitives`. The composition
(`ProductVocabularyComposition`), the document projection
(`ProductVocabularyProjection`), the inspection wrapper, and the document and
wire types live in `DotnetInspector.Sections`. Each host holds its
contribution list, and no assembly between the owners and the hosts restates
their values. Two fixed section lists remain, both naming sections rather than
restating values:

- **Document schema.** `ProductVocabularyProjection` names each section it
  projects, with that section's fields and operators, and fails visibly when a
  composed snapshot lacks one. A vocabulary that a host contributes without a
  document section appears only as an index row.
- **CLI section descriptors.** `VocabularySections` in the CLI host declares
  one section descriptor per document section, with its selection category.
  `-S` resolves only sections that have a descriptor, so an unlisted section is
  reported as unresolved.

Adding a vocabulary therefore takes an owner declaration, a contribution in
each host that ships it, a document section, and, for the CLI, a section
descriptor. Whether the document keeps a per-section schema or projects every
composed vocabulary generically is decided with the explanation adoption in
[#9250](https://github.com/richlander/dotnet-inspect/issues/9250) steps 7
and 8.

Static vocabulary answers "what may I ask?" Target-aware facets remain query
results: they add availability, counts, or rejection reasons for one inspected
target while retaining the static value IDs.

## Vocabulary Mappings adoption

[Vocabulary Mappings](vocabulary-mappings.md) generalizes stable terms and
named scalar or term-reference maps across hosts. Product Vocabulary is its
first adopter. This owner continues to define the query values, fields,
operators, accepted inputs, and CLI behavior; the mapping pattern defines only
how those owner-issued facts become an immutable, exactly identified snapshot.

The first adoption preserves the existing CLI structured contract as a
compatibility projection. It additionally authenticates
`csharp.style-choices.tier` as a complete, exactly-one term map to
`csharp.style-tiers`, allowing Inspect Web to group choices without treating an
ordinary string field as an implicit foreign key. Other string-valued fields
remain scalar until their owners publish a target vocabulary.

Inspect Web still knows that its Settings feature consumes
`csharp.style-choices`, `csharp.style-tiers`, and their relevant map identities.
The general API makes those contracts resolvable and their contents
discoverable; it does not infer which product feature should use them.

Implementation and host migration remain tracked by
[#8593](https://github.com/richlander/dotnet-inspect/issues/8593).

## Current sections

| Section | Stable ID | Values |
| ------- | --------- | ------ |
| Vocabulary Sections | `vocabulary.sections` | Available vocabulary sections |
| Accessibility | `api.accessibility` | API accessibility facet IDs |
| C# Style Tiers | `csharp.style-tiers` | Style fidelity/presentation tiers |
| C# Style Choices | `csharp.style-choices` | Selectable rendering choice IDs |
| C# Body Kinds | `csharp.body-kinds` | Exact rendered body-syntax kinds |

The library `Body Shapes` section consumes body-kind IDs through
`--where "Kind=<ID>"` and auto-selects that section when no explicit `-S`
selection is present. Repeated Performance Triage predicates compose at library
scope by selecting typed source MethodDef identities before decompilation.
Exact type and member scoping are also available. The former standalone
`body-shape` command was removed without a compatibility alias after these
scoped queries reached parity.
