# Product Vocabulary

Product vocabularies are the stable values that product-owned queries accept:
API accessibility facets, C# style tiers and choices, and C# body kinds. Both
hosts present them through
[Resource Explanation](resource-explanation.md#value-vocabulary-resources).
There is no separate vocabulary command, document, or wire format.

**Status.** This is the target set by
[#9250](https://github.com/richlander/dotnet-inspect/issues/9250) step 8.
In the CLI today, `explain vocabularies`, `explain vocabularies/<id>`, and
`explain vocabularies/<id>/values/<value>` explain every vocabulary and value
by reading the composed snapshot generically. The step's remaining slices, in
order, are:

1. The Inspect Web export.
2. The [retirement](#retirement) of the `vocabulary` command. Until it lands,
   the command still runs, and its document projection and CLI section
   descriptors still name each vocabulary, so adding a vocabulary also takes a
   projection section and a CLI section descriptor.

Statements below about the export and retired parts describe that target.

## Product surface

A vocabulary, its values, and the inputs that accept them are explainable
resources:

```bash
dotnet-inspect explain vocabularies
dotnet-inspect explain vocabularies/csharp.body-kinds
dotnet-inspect explain vocabularies/csharp.body-kinds --depth 1
dotnet-inspect explain vocabularies/csharp.body-kinds/values/objectcreationexpression
dotnet-inspect explain vocabularies/csharp.style-choices --depth 1 --json
```

- `vocabularies` lists every composed vocabulary in index order.
- `vocabularies/<id>` explains one vocabulary: its identity, name, summary,
  value count, accepted query inputs, maps, and defaults. Its values are an
  ordered relationship, so every value is reachable.
- `vocabularies/<id>/values/<value>` explains one value: its exact owner
  identity, display label, summary, and owner-issued map values. A term-map
  value is a typed link to the target value's resource.
- `--depth 1` on a vocabulary returns the vocabulary and all of its values in
  one Document, which is the bulk listing the retired command provided.
- `vocabularies/<id>/values` is not a resource. It resolves as unknown with
  suggestions, so depth 1 from a vocabulary reaches its values directly.

A value's path segment is its exact identity in ASCII lower case with `:`
replaced by `.`, under
[Value-vocabulary resources](resource-explanation.md#value-vocabulary-resources).
The path segment is the only path spelling. The exact identity, which queries
accept, is the value's identity fact, so the vocabulary's depth-1 listing
shows both.

Inspect Web requests the same explanation. A catalog-facade export resolves a
`vocabularies` path against the Browser-composed snapshot and returns the same
`ResourceExplanationDocument` Content the CLI produces for the same path and
depth. Both hosts use one host-neutral set of traversal limits, so equal
snapshots yield equal Content. An unknown path, a path outside `vocabularies`,
or an invalid path or depth is a typed non-success result, never an empty Document.
Inspect Web's existing snapshot export for the Settings style picker is
unchanged.

A CLI query key whose values come from one vocabulary links to that
vocabulary's resource. `-Q` presents the link, for example
`explain vocabularies/csharp.body-kinds` for the Body Shapes `Kind` key.

### Retirement

The `dotnet-inspect vocabulary` command retires, with the parts only it uses:

- the CLI command, its options, section descriptors (`VocabularySections`),
  and views;
- the `@Vocabulary` selection category;
- the Product Vocabulary document (`VocabularyDocument` and
  `ProductVocabularyProjection`) and its schema-versioned JSON wire projection
  (`VocabularyJson` and the `VocabularyWire*` types).

Each map's per-field query operators retire with the document. Only the
document's JSON and the explanation `fields` fact read them, and no
predicate enforces them. The operators a query input accepts belong to that
input and are presented by `-Q`.

`vocabulary` stays a reserved command name. Invoking it fails with an error
that names `explain vocabularies`. The retirement is a disclosed CLI breaking
change. Explanation output is Markdown, plain text, or JSON, so every other
option of the command retires too, including table, TSV, and JSONL output;
`--columns`, `--fields`, and `--no-headers`; `-n`, `--head`, `--tail`,
`--rows`, `--lines`, and `--tail-lines`; `--count`; `-D`, `--schema`, and
`--tree`; and selecting several vocabularies with a glob or an `@` category
`-S`. The release notes list them. The information
stays reachable. The retirement lands only after values are explainable
resources and Inspect Web can request the explanation, so no information or
host loses access in between. The retirement slice also retargets every
remaining pointer to the command or the `@Vocabulary` category: the Body
Shapes `Kind` error hint, the `DiscoveryDocumentFactory` category arm, the
design and reference documents that describe the command, and shipped skill
guidance, which is proposed on the release tracker.

What stays is the substrate: owner declarations, host composition, the
`ProductVocabularyPin` digest, and Inspect Web's snapshot export for Settings.
[JSON Schema Vocabulary Bindings](json-schema-vocabulary-bindings.md), which
have no product host consumer yet, reference an exact Vocabulary Mappings
snapshot and never the retired document.

Static vocabulary answers "what may I ask?" Target-aware facets remain query
results: they add availability, counts, or rejection reasons for one inspected
target while retaining the static value IDs.

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
states. In the target, no component owns the complete list of product
vocabularies; until the retirement lands, the document projection and the
CLI section descriptors also name them.

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
which names its accepted query inputs.

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
(`ProductVocabularyComposition`) and the inspection wrapper live in
`DotnetInspector.Sections`, beside Resource Explanation's
`ResourceExplanationCatalog.CreateVocabularies`, which explains a composed
snapshot generically. Each host holds its contribution list, and in the target
no assembly between the owners and the hosts restates their values or names
their sections. After the retirement, adding a vocabulary takes an owner
declaration and a contribution in each host that ships it; explanation, the
Browser export, and the snapshot pin pick it up without another list.

## Vocabulary Mappings adoption

[Vocabulary Mappings](vocabulary-mappings.md) generalizes stable terms and
named scalar or term-reference maps across hosts. Product Vocabulary is its
first adopter. This owner continues to define the query values, accepted
inputs, and their product presentation; the mapping pattern defines only
how those owner-issued facts become an immutable, exactly identified snapshot.

The first adoption preserved the former CLI structured document as a
compatibility projection; that projection retires with the `vocabulary`
command. The adoption additionally authenticates
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

## Current vocabularies

| Vocabulary | Stable ID | Values |
| ---------- | --------- | ------ |
| Accessibility | `api.accessibility` | API accessibility facet IDs |
| C# Style Tiers | `csharp.style-tiers` | Style fidelity/presentation tiers |
| C# Style Choices | `csharp.style-choices` | Selectable rendering choice IDs |
| C# Body Kinds | `csharp.body-kinds` | Exact rendered body-syntax kinds |

Each is explainable at `vocabularies/<stable-id>`. The composition's own
`vocabulary.sections` index lists them and their accepting inputs; it is the
input to the `vocabularies` collection rather than a member of it.

The library `Body Shapes` section consumes body-kind IDs through
`--where "Kind=<ID>"` and auto-selects that section when no explicit `-S`
selection is present. Repeated Performance Triage predicates compose at library
scope by selecting typed source MethodDef identities before decompilation.
Exact type and member scoping are also available. The former standalone
`body-shape` command was removed without a compatibility alias after these
scoped queries reached parity.
