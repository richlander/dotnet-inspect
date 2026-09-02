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
dotnet-inspect vocabulary -S @Decompiler --count
```

- Bare `vocabulary` renders the `Vocabulary Sections` index.
- `-D` discovers sections, categories, and fields.
- `-S` selects the values to materialize.
- `--columns` and `--fields` project values. Released `--rows` accepts a count
  or an absolute range; the historical #4677 target proposed making it
  range-only. [Item and line limits](item-and-line-limits.md) records that
  focused CLI ownership remains pending.
  `--count` collapses each row set to its cardinality.
- Markdown, plain text, table, TSV, JSONL, and JSON use the same section and row identities.

The structured document carries a schema version. Every section declares its
stable ID, categories, accepted query inputs, field schema, legal operators, and
typed values. A stable value ID can therefore flow from discovery or a website
picker back into a typed query without parsing labels.

## Ownership

`DotnetInspector.Vocabulary` composes existing owner catalogs; it does not
reclassify their values:

- `ApiAccessibility` owns accessibility identity, order, defaults, and
  classification.
- `StyleOptionCatalog` owns C# style tiers, selectable choices, conflicts,
  endorsement, and byte-divergence properties.
- `BodyShapeSearch.SupportedKinds` owns searchable body-kind identity and order;
  `AnnotatedSourceNodeKinds` owns their display labels.

CLI and browser/WASM consume the same `VocabularyCatalog` and
`VocabularyJson` projection. Hosts may select a section for a purpose-specific
control, but they do not restate its values, labels, order, defaults, or
selection semantics.

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
