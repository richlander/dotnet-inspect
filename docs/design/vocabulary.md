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
dotnet-inspect vocabulary -S "C# Style Choices" --format json
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
`VocabularyCatalog` authoritative for names, field labels, stable field IDs,
and row order. Each runtime section carries its summary as an ordinary
Markout paragraph because unwrapped child sections lower their content rather
than their `DescriptionProperty` metadata. `VocabularyCommandTests` gates these
formats in Release, including
`Command_DefaultRendersTheSelfDescribingSectionIndex`,
`Command_PlainTextUsesThePlainTextFormatter`, and
`Command_JsonlUsesProjectedRuntimeColumns`.

Plain unprojected `--format json` is an approved CLI-host exception to ordinary
Markout lowering. Its typed input is the selected owner-issued
`VocabularySection` sequence plus the catalog schema version, and its lowering
boundary is `VocabularyWireDocument` through the generated
`VocabularyWireJsonContext` or `VocabularyWireCompactJsonContext`. This path
preserves the established schema-versioned document containing section
metadata, field schemas, operators, accepted-command identities, and typed
value cells; the lowered Markout table shape cannot represent that contract
without discarding schema or changing typed values to display strings. The
exception is limited to unprojected CLI `--format json`; every human, tabular, stream,
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

`DotnetInspector.Vocabulary` composes existing owner catalogs; it does not
reclassify their values:

- `ApiAccessibility` owns accessibility identity, order, defaults, and
  classification.
- `StyleOptionCatalog` owns C# style tiers, selectable choices, conflicts,
  endorsement, and byte-divergence properties.
- `BodyShapeSearch.SupportedKinds` owns searchable body-kind identity and order;
  `AnnotatedSourceNodeKinds` owns their display labels.

CLI and browser/WASM consume the same `VocabularyCatalog` and `VocabularyJson`
projection. Hosts may select a section for a purpose-specific control, but they
do not restate its values, labels, order, defaults, or selection semantics.

Static vocabulary answers "what may I ask?" Target-aware facets remain query
results: they add availability, counts, or rejection reasons for one inspected
target while retaining the static value IDs.

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
