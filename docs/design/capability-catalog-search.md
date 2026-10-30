# Capability catalog search

## Status

This document is the normative design for **Capability Catalog Search**,
tracked by
[#8424](https://github.com/richlander/dotnet-inspect/issues/8424).

The first adoption is implemented over the explicit available-capability graph
defined by
[Inspection Capability Composition](inspection-capability-composition.md).
The host-neutral operation, CLI `explain` facade, Browser/Wasm managed export,
Spotlight interaction, production `library-literal` witness, and shipped router
workflow are implemented. Portable Browser Share and reusable-reference facade
dispatch remain follow-up work.

The [structural catalog population](#structural-catalog-population) for
[#10068](https://github.com/richlander/dotnet-inspect/issues/10068) is
implemented for the CLI, which admits its seven explained structural catalogs.

[Phrase search](#phrase-search) for
[#10568](https://github.com/richlander/dotnet-inspect/issues/10568) is
implemented in the host-neutral operation, so both hosts receive it.

[#10700](https://github.com/richlander/dotnet-inspect/issues/10700) replaces
per-family search admission with registration plus host binding over one
composed capability graph. This is implemented for analyses and structural
catalogs; the host no longer supplies separate structural search entries.

## Owner and exact claim

**Capability Catalog Search** owns this exact claim:

> Given one non-empty bounded search text and one settled search population,
> derived from registration plus host binding in the composed capability
> graph, project a bounded set of canonical search terms from each available
> resource, rank matching resources with the repository's existing
> string-similarity model, and return one deterministic bounded Document that
> preserves each result's exact resource path, owning route, and available
> production bindings without acquisition or producer execution.

This owner defines:

- the host-neutral search request and result Document;
- which composed and structural owner-issued fields participate in search;
- canonical term segmentation, search-text segmentation, and comparison
  normalization;
- similarity threshold, score aggregation, tie-breaking, and result bounds;
- visible match provenance and complete-empty meaning;
- the handoff from an oriented result to exact explanation and production
  gestures; and
- equivalent CLI and Browser/Wasm consumption of the same completed
  inspection envelope.

It does not define:

- capability, resource, route, or consumer-binding identity;
- query facets, sections, commands, or operation semantics;
- canonical Resource Explanation paths or exact explanation;
- the edit-distance or normalized-similarity algorithm;
- host command parsing, Browser interaction mechanics, or rendering; or
- subject inspection, acquisition, query execution, or natural-language
  interpretation.

Those contracts remain with their existing owners. Catalog search ranks the
installed facts they issue; it does not repair, infer, or reinterpret them.

## Product goal

The shipped skill should teach a capable agent that the installed product can
describe itself. Command-local `-D` and `-Q` remain the fastest discovery
gestures when the agent already knows the relevant route. Exact `explain`
path dispatch remains the semantic drill-down when the agent already has one
canonical resource path.

Catalog search fills the orientation gap:

```text
unfamiliar task text
  -> similarity-ranked installed resources
      -> exact Resource Explanation path
          -> command-local discovery or production binding
              -> inspection
```

The operation searches product contracts, not inspected subjects. It is not a
replacement for `find`, package search, Type lookup, member lookup, section
selection, Query Space predicates, or Resource Explanation.

## Production witness

The first witness is the existing Package Query literal-string capability.
An agent starts with the value shape `https://`, not with prior knowledge of
Package Query:

```console
dotnet-inspect explain https://
```

The result includes at least:

```text
Score  Kind         Key              Route          Explain
1.000  Query facet  library-literal  Package Query  <canonical resource path>
```

The same result identifies the available CLI discovery binding:

```text
package query -Q Packages
```

The search result supplies an exact path that selects the other branch of the
same facade:

```console
dotnet-inspect explain <library-literal-resource-path>
```

and can execute the established production scenario:

```console
dotnet-inspect find --packages Microsoft.Azure.SignalR \
  --where "library-literal=https://" --tfm net8.0 \
  -S "Literal Strings"
```

The query returns complete decoded string literals, including strings that
contain surrounding text. Multiple `https://` matches inside one physical
`ldstr` produce one row, while equal strings at distinct IL coordinates remain
distinct rows. Catalog search does not execute that query or acquire
`Microsoft.Azure.SignalR`; it only makes the installed capability and its
next gestures discoverable.

The Browser uses the same host-neutral search operation and presents the same
ranked resource identities, paths, routes, and production bindings for the
catalog generation supplied by that host. Browser interaction design may
differ, but it may not privately rank another catalog or reconstruct
capability from UI labels.

## Basis and authority map

<!-- markdownlint-disable MD013 -->

| Owner | Contract consumed by catalog search |
| --- | --- |
| [Inspection Capability Composition](inspection-capability-composition.md) | The deterministic available-capability graph, owner-issued resource facts, routes, and production bindings |
| [Section model](section-model.md) | Section and category identity, names, and section descriptions |
| [Structural view registry](../../src/DotnetInspect.Cli/Planning/StructuralViewRegistry.cs) | The registered structural routes and the catalog each one projects |
| [Resource Explanation](resource-explanation.md) | Canonical product-resource paths and exact-path explanation |
| [`StringDistance`](../../src/ILInspector.MetadataPrimitives/StringDistance.cs) | Levenshtein edit distance and normalized similarity |
| [Inspection Envelope](inspection-envelope.md) | Completed host-neutral Content, Share, and diagnostics |
| [Host-observable Content Kinds](host-observable-content-kinds.md) | Document and complete-empty semantics |
| [Output Shapes](output-shapes.md) | Host lowering and structured output boundaries |
| CLI and Browser focused owners | Gesture parsing, interaction, presentation, and navigation |

<!-- markdownlint-enable MD013 -->

`ILInspector.MetadataPrimitives.StringDistance.Similarity` is the normative
similarity algorithm. Capability Catalog Search does not introduce another
distance function, fuzzy library, phonetic model, stemming model, synonym
catalog, or learned ranker.

## Complexity basis and precedent

The user-observable requirement is to orient an agent that knows a task concept
but not the responsible command, section, or query key. Exact identity lookup
alone cannot serve a misspelling such as `litteral`, while raw similarity
against only the complete key `library-literal` scores below the useful
threshold. The smallest sufficient mechanism is therefore:

1. project bounded complete values and separator-defined segments from facts
   already present in the composed catalog;
2. score those terms with the existing similarity implementation;
3. retain the maximum score and its provenance per resource;
4. for a phrase, score each search word the same way and average the words;
   and
5. return a deterministic bounded prefix with exact resource and binding
   identities.

The design transfers three repository precedents:

- `TypeMatcher.FindClosest` uses `StringDistance.Similarity`, a `0.6`
  threshold, descending score, and a bounded result count;
- section-selection suggestions compare explicit registered names and retain
  deterministic bounded candidates; and
- structural-clone name ranking gives ordinal-ignore-case equality score
  `1.0` and otherwise invariantly folds names before calling
  `StringDistance.Similarity`.

Only those mechanics transfer. Type-name base-name grammar, section prefix
selection, structural-clone eligibility, and their domain verdicts do not.
Catalog search owns its explicit term projection and returns orientation
candidates rather than a selected semantic answer.

This design replaces no existing search architecture. `find`, `-D`, `-Q`, and
exact Resource Explanation retain their current jobs, so there is no
migration or retirement plan.

## Search input

`CapabilityCatalogSearchRequest` contains:

```text
CapabilityCatalogSearchRequest
  Text
  MaximumResults
```

`Text` is trimmed at both ends. The request constructor rejects empty or
whitespace-only text; hosts surface that validation failure and do not invoke
the operation or manufacture a successful empty Document. The first
implementation admits at most 128 UTF-16 code units. This bound keeps
edit-distance work independent of unbounded host input while remaining well
above a capability identity or short task phrase.

`MaximumResults` defaults to 20 and is admitted from 1 through 100. It is a
semantic retrieval bound: the operation still evaluates the complete
search population, reports the total match count, and returns
the highest-ranked prefix. A host may map its ordinary row-limit gesture to
this value, but it may not clip a larger hidden result and describe the
remaining population as unknown.

The first operation accepts one search text. Boolean expressions, field
filters, globs, regular expressions, semantic embeddings, natural-language
plans, and pagination are outside this contract.

## Search population

A resource is searchable when it is registered in the composed capability
graph and a current-host production binding selects it, under
[Inspection Capability Composition's selection rule](inspection-capability-composition.md#host-binding).
The execution kind does not decide admission. Registered resources with no
host binding remain explainable but are not search candidates. Search has no
host-supplied per-kind population entries.

This excludes:

- proposed or partially registered resources with no production binding;
- command-local paths that do not participate in the composed graph;
- adoption-gap records used for engineering census;
- parser commands and options that are not owner-issued product resources;
- CLR types, source-code symbol names, and reflection-discovered values; and
- rendered section, help, Markdown, JSON, or Browser text.

The complete composed graph may separately expose adoption gaps to engineering
tools. User-facing catalog search does not mix unavailable plans into a result
that promises a next production gesture.

### Structural catalog population

Structural catalogs register their owner-issued Discovery declarations and
canonical paths in host-neutral product modules. A host contributes one
structural binding per view, selecting exactly the sections that view can
select on its own and categories containing one. Composition validates
registration identity, canonical paths, exact catalog references, and binding
membership before search. A catalog may be registered without bindings;
availability is a separate derived fact.

The shared catalog supplies the owning catalog root and preserves installed
catalog identities, including their historical CLI-qualified spelling. That
spelling does not imply a host-neutral route or declare a consumer. Hosts own
their gestures, such as `library -S`; the result keeps gesture and resource
name separate rather than constructing a command line.

Search reads registered Discovery resources and paths directly, without
constructing structural Resource Explanation snapshots or navigation
collections. Exact explanation builds those projections only when requested.
Both projections consume the same registered declarations. NativeAOT
base/head evidence checks preservation of complete result Content and the
existing search cost class; constructing every explanation graph merely to
search is outside this demand boundary.

From each admitted catalog, search takes the Section and Category resources.
Structural items, item collections, navigation collections, and the catalog
root are excluded. Items are column and field declarations of a section's
schema slice, not independently selectable capabilities, and roots and
collections are reached by exact path. A Section or Category is searchable
even when its producer finds no rows for a given subject, because search
describes installed capability, not subject applicability.

A host admits only the structural catalogs whose exact paths it resolves
under one root. The CLI admits the seven structural catalogs its combined
`explain` catalog resolves:

<!-- markdownlint-disable MD013 -->

| Catalog root | Structural view | Binding gesture |
| --- | --- | --- |
| `library` | `DirectLibrary` | `library -S` |
| `library` | `LibraryAddress` | `library address -S` |
| `package` | `Package` | `package -S` |
| `type` | `Type` | `type -S` |
| `member` | `MemberType` | `member -S` |
| `member-overload` | `MemberTarget` | `member -S` |
| `member-detail` | `MemberTarget` | `member -S` |
| `diff` | `Diff` | `diff -S` |

<!-- markdownlint-enable MD013 -->

The two Library views project one `library` catalog. The `LibraryAddress`
view requires a section that consumes its coordinate and selects the rest of
its schema only alongside one, so its binding covers the coordinate-scoped
`Context:` sections, `Metadata: Heap`, and the `@Context` and `@Metadata`
categories that contain them. Every other Library section and category
carries only the `library -S` binding, and `@Metadata` carries both.

Search evaluates the complete available population.
`CandidateResourceCount` counts every admitted capability and structural
resource, and the same ranking applies to both. A host that adds a structural
catalog to search and omits it from exact explanation, or the reverse, breaks
a host wiring invariant; the host's wiring test fails rather than the host
shipping a partial population.

## Canonical search-term projection

Each searchable resource projects terms from the following composed fields:

1. its stable owner-issued identity;
2. owner-issued canonical keys, including a Query Space facet key;
3. owner-issued example values accepted by a Query Space facet;
4. its canonical Resource Explanation path;
5. its owner-issued name and summary; and
6. the stable identities and owner-issued names of directly related routes
   and available production bindings.

The search adapter selects these fields explicitly for each adopted descriptor
family. It does not use reflection, property enumeration, serializer metadata,
display-object inspection, or rendered text.

For a registered analysis, the projected fields are:

1. its owner-issued analysis identity, as its owner identity, and its name;
2. its canonical Resource Explanation path, `analyses/<identity>`;
3. the owner-issued title of each Finding it issues, from its explained
   `findings` fact; and
4. the identity and name of its owning route and of the bindings that
   expose it.

An analysis has no summary. Its revision, cost, surfaces, and Finding
descriptor identities do not participate.

For a structural Section or Category, the projected fields are:

1. its owner-issued name, the exact name that selects it, as its owner
   identity;
2. its canonical Resource Explanation path;
3. its owner-issued name as its resource name;
4. for a Section, the Summary of its [section
   description](section-model.md#description) when one is declared; and
5. the identity and name of its admitted Route and Binding.

A structural resource has no canonical key or example value. A Section
description's Basis, Empty, and Boundary parts, its schema columns, and its
cardinality and shape do not participate: they describe how to read a
section's evidence, not which capability it is. An undeclared description
contributes no Summary term; search does not substitute the section name or
any other text.

This projection does not change [Search input](#search-input). A single
word is compared with each term whole, so `metrics`, `unsafe`, `leverage`,
and `families` reach the sections that own them. A phrase such as
`unsafe code` is scored word by word under [Phrase search](#phrase-search).

Each selected field contributes:

- its complete non-empty value; and
- non-empty segments split on ASCII whitespace or `.`, `/`, `:`, `-`, and
  `_`.

Segmentation is a search projection, not a new identity. It never replaces the
complete owner-issued value in results or Resource Explanation. The fixed
separator set makes `literal` an exact search term projected from the
canonical key `library-literal` without inventing a synonym or copying that
facet into a search-only inventory. The complete owner-issued example
`https://` likewise makes value-shaped intent discover the same facet without
a Browser-only URL heuristic.

Terms are de-duplicated per resource with
`StringComparer.OrdinalIgnoreCase`. The projection retains the strongest
provenance and then prefers the complete-field form when equal text comes from
more than one field.

Search-term provenance is ordered from strongest to weakest:

1. owner identity or canonical key;
2. owner-issued example value;
3. canonical resource path;
4. owner-issued resource name;
5. owner-issued summary or issued Finding title; and
6. related route or production-binding identity or name.

This order breaks equal similarity scores. It does not change the similarity
algorithm or imply that one resource kind is semantically more important than
another.

## Similarity and ranking

Comparison follows the repository's existing neutral name-similarity model:

1. If the trimmed search text and candidate term are equal under
   `StringComparison.OrdinalIgnoreCase`, the term score is `1.0`.
2. Otherwise both values are invariantly uppercased and passed to
   `StringDistance.Similarity`.

Invariant uppercasing keeps the comparison normalization aligned with
ordinal-ignore-case equality, including the existing Greek sigma behavior
documented by structural-clone name ranking. Lowercasing is not substituted.

A resource's score is the maximum term score produced by that resource.
Scores below `0.6` are excluded. The threshold matches the established default
used by `TypeMatcher.FindClosest`; it is part of this search contract rather
than a user-adjustable first-slice control.

Results use this total order:

1. similarity score descending;
2. matched-term provenance strength;
3. complete-field match before segmented-term match;
4. canonical resource path under ordinal comparison.

The selected matched term and its provenance are retained in the result. If
several terms remain equal through the first three keys, the ordinal-smallest
term is retained so output does not depend on registration or enumeration
order.

The score ranks textual proximity only. It is not a verdict that the resource
answers the user's task, and the product must not label it confidence,
correctness, relevance, or semantic compatibility.

Examples:

<!-- markdownlint-disable MD013 -->

| Search | Candidate term | Score and outcome |
| --- | --- | --- |
| `https://` | complete example value `https://` from `library-literal` | `1.0`, included |
| `literal` | segment `literal` from `library-literal` | `1.0`, included |
| `litteral` | segment `literal` from `library-literal` | `0.875`, included |
| `library-literal` | complete canonical key `library-literal` | `1.0`, included |
| `literal` | an unrelated long summary containing no close term | below threshold, excluded |
| `namespace dependency cycles` | owner-identity segment `Dependency` of Dependency Structure, plus summary words for `namespace` and `cycles` | phrase score `0.741`, included |
| `unsafe code` | owner-identity segment `Unsafe` of Unsafe Members | phrase score `0.5`, included |
| `dependency structure` | complete owner identity `Dependency Structure` | `1.0`, included |

<!-- markdownlint-enable MD013 -->

### Phrase search

A search text is a phrase when it has more than one segment, split on the
same separators as [canonical terms](#canonical-search-term-projection), and
at least one search word. Its **search words** are its distinct segments,
under `StringComparer.OrdinalIgnoreCase`, that have at least three
characters and are not function words. The function words are a fixed list
of 24 English words, such as `the`, `does`, `which`, `show`, and `find`. Any
other search text is scored whole as above.

A phrase scores each resource in this order:

1. If the complete search text equals one of the resource's terms under
   `StringComparison.OrdinalIgnoreCase`, the resource scores `1.0` with that
   term. A phrase is not otherwise compared whole: fuzzy comparison of a
   multi-word text with a multi-word term scores shared word endings, so
   `boxing allocations` would rank Source Locations first.
2. Otherwise each search word is compared with each term, as a single search
   text is, including the `0.6` threshold. A word's score is its best term
   similarity times that term's weight, or `0` when no term reaches the
   threshold.
3. A term in the resource's own heading weighs `1`: its owner identity,
   canonical key, example value, resource name, complete resource path, or a
   segment of the path's final segment. Every other term is context and
   weighs `1 / 1.5`: a summary, a related route or binding, or a path segment
   that only scopes the resource, such as `library` or `sections` in
   `library/sections/unsafe-members`. This is the H1-to-body ratio of the
   dotnet-docs-llm-tools keyword extractor: a heading names what the page
   is about.
4. The phrase score is the mean of the word scores, rounded to three
   decimals. Resources below `0.25` are excluded.
5. The retained matched term is the one with the highest weighted score,
   then stronger provenance, then a complete term before a segment, then
   ordinal order.

The phrase score then enters the same total order as a whole-text score.

The constants come from measurement, not from the ranking examples above.
In the project-analysis eval
([#8516](https://github.com/richlander/dotnet-inspect/issues/8516)), Opus 5.5
answered 26 operator-written questions. Against whole-text-only search, a
pilot with phrase search and searchable diff analyses cut ideal-caching cost
to 0.78 of base, with a 90% interval of 0.68–0.90. The heading weights
replaced three provenance tiers with one ratio. They ranked the target first
for 70 of 169 replayed agent searches, against 66, and live cost was
unchanged at 0.96 [0.89–1.03]. The
[#10568](https://github.com/richlander/dotnet-inspect/issues/10568) thread
records the runs.

## Result Document

The completed Content is:

```text
CapabilityCatalogSearchDocument
  Query
  SimilarityThreshold
  CandidateResourceCount
  MatchCount
  ReturnedCount
  IsTruncated
  Results[]

CapabilityCatalogSearchResult
  Similarity
  MatchedTerm
  MatchSource
  IsSegment
  ResourceIdentity
  StructuralIdentity
  ResourceKind
  ResourceName
  CanonicalKeys[]
  ResourcePath
  OwningRoutes[]
  ProductionBindings[]
```

`SimilarityThreshold` is the score a result must reach for this request:
`0.6` for a whole-text search and `0.25` for a phrase. Every returned result
meets it.

`CandidateResourceCount` is the complete search population
evaluated by this request. `MatchCount` is the number at or above the
threshold before `MaximumResults`. `ReturnedCount` is the result-array length,
and `IsTruncated` is true exactly when `MatchCount > ReturnedCount`.

Exactly one of `ResourceIdentity` and `StructuralIdentity` is present.
A capability-family result carries its composed
`InspectionCapabilityResourceIdentity`. An analysis result has kind
`Analysis`, the Compare route as its owning route, and the bindings that
expose it. A structural result carries its
catalog name and its typed Discovery resource identity, with `ResourceKind`
`Section` or `Category`. A structural result has one owning route, the Route
its host admitted, and the admitted Bindings whose Resources contain it, in
admission order. Their `ResourcePath` values are the catalog root path. No
structural resource is represented by a fabricated capability identity.

`CanonicalKeys` retains owner-issued keys applicable to the resource, including
`library-literal` for the production witness. It is empty for a structural
result.

This changes the serialized result shape. Plain Content JSON and `--envelope`
serialize `resource_identity` as `null` for a structural result and
`structural_identity` as `null` for a capability-family result. A host that
admits no structural catalog, as the Browser does today, emits only
capability-family results, whose existing serialized fields are unchanged.
The Browser wire projection carries no structural identity until the Browser
admits structural catalogs; until then it rejects a structural result
visibly rather than projecting it without its identity. Each
capability-family route and production-binding reference retains its typed
composed identity. The CLI may
render a copyable discovery or execution gesture from a CLI binding; the
Browser may render a navigation action from a Browser binding. Those
projections do not become search-owned free-form command text.

A complete search with no score at or above the threshold is a valid Document:

```text
CandidateResourceCount: <complete population>
MatchCount: 0
ReturnedCount: 0
IsTruncated: false
Results: []
```

A standalone capability-search host renders a visible
"No matching installed capabilities" outcome. A blended search host may omit
its transient capability group when this result is complete empty, while
retaining the other providers' results and their aggregate empty-state
behavior. Hosts do not fall back to parser help, perform network search, lower
the threshold silently, or turn an invalid request into this complete-empty
result.

## Host-neutral operation

The owner exposes one completed operation:

```text
CapabilityCatalogSearch.Search(
  InspectionCapabilityCatalog catalog,
  ResourceExplanationCatalog explanationCatalog,
  CapabilityCatalogSearchRequest request)
  -> InspectionEnvelope<CapabilityCatalogSearchDocument>
```

The catalog contains structural registrations and view bindings alongside
route registrations and route bindings. There is no separate structural
population argument. Search consumes structural paths directly from the
registered declarations; route resource paths resolve through the requested
Resource Explanation projection. Hosts may omit structural explanation
snapshots when constructing a search-only projection.

The operation is synchronous because it consumes a settled in-memory catalog
and performs no I/O. Ordinary inspection execution continues to call typed
routes directly and does not construct or search the aggregate graph.

Catalog search has no subject input and performs:

- no package, Library, project, source, symbol, or network acquisition;
- no query-plan or inspection-route execution;
- no producer delegate invocation;
- no Workspace mutation; and
- no reflection or assembly scanning.

The explicit catalog command is a cold discovery operation, so its bounded
term and result allocations do not affect ordinary route execution. Search
may cache immutable per-catalog term projections after the catalog is first
materialized. Cache identity follows the exact composed catalog instance or
generation; one deployment's index is never reused for another catalog.

The operation returns one
`InspectionEnvelope<CapabilityCatalogSearchDocument>`. The first adoption
returns `InspectionShare.NonProjectable` because the Browser does not yet own a
portable capability-search route or Workspace projection. The target state
uses an available Share URL that preserves the search text and result bound
for that route. Diagnostics remain ordered cross-host diagnostics; a host does
not translate an operation failure into an empty result.

## Exact explanation handoff

Every returned row carries the canonical `ResourcePath` issued by Resource
Explanation. Selecting Explain passes that exact path unchanged:

```text
explain facade search branch
  -> catalog search result ResourcePath
      -> explain facade exact-path branch
          -> ResourceExplanationCatalog.Resolve(exactPath)
```

The facade does not ask Resource Explanation to resolve the original search
text. An exact registered path or alias selects exact resolution; a recognized
reusable-reference shape selects its owner; an otherwise canonical
multi-segment `ResourcePath` selects exact resolution; and every remaining
non-empty bounded operand selects search.

This distinction is necessary because `ResourcePath` grammar intentionally
admits canonical single segments. Calling `ResourcePath.TryCreate("literal")`
is therefore not sufficient facade classification. An unregistered
single-segment `literal` is search text, while a registered single-segment
root or alias remains exact. `https://` is also search text because it is not a
canonical multi-segment `ResourcePath`. An unknown canonical multi-segment path
remains an exact-resolution failure and never falls through to search.

Search always returns candidates. It does not silently resolve the
highest-ranked row, including a unique row or one with similarity `1.0`.
Selecting or copying a returned path is the explicit transition to exact
explanation.

The handoff gate proves that every returned path resolves to the same typed
resource identity represented by the row. Composition validates structural
path registration and binding membership. Exact explanation consumes those
same registered declarations. CLI complete-catalog and view-scope tests prove
that the production composition advertises the resources it explains, with
only the selecting views' gestures. Search does not construct explanation
snapshots merely to repeat this wiring gate.

## Explain facade and Browser adoption

The CLI search gesture is:

```console
dotnet-inspect explain <search-text>
```

The top-level `explain` facade remains owned by
[Contextual Resource Explanation](contextual-resource-explanation.md). It
dispatches by operand syntax before invoking an operation:

<!-- markdownlint-disable MD013 -->

| Operand family | Operation |
| --- | --- |
| Exact registered path or alias, or otherwise canonical multi-segment `ResourcePath` | Exact Resource Explanation |
| Reusable inspection reference | Subject-affordance explanation |
| Any other non-empty bounded text | Capability Catalog Search |

<!-- markdownlint-enable MD013 -->

The target facade classifies in this order:

1. exact registered resource path or alias;
2. reusable-reference shape;
3. any remaining operand that `ResourcePath.TryCreate` accepts and that
   contains `/`; and
4. capability-search text.

Dispatch is never based on whether a preceding operation succeeds.
Reference-shape recognition precedes Resource Path recognition because a
reference owner may use slash-bearing syntax. An unknown canonical
multi-segment path remains an exact path failure, and an invalid
reference-shaped operand remains a reference failure. Neither becomes search
text. A noncanonical slash-bearing string such as `https://` remains search
text. The first adoption implements registered exact paths, canonical
multi-segment exact failures, and capability-search text; reusable-reference
recognition remains pending until its owner provides the classifier.

The search branch takes no package, Library, Type, Member, project, or
Workspace subject. Default Markdown is a compact ranked table. Structured
output serializes the same `CapabilityCatalogSearchDocument`; it does not
expose a CLI-only search DTO or force all three facade branches into one
universal Content type.

The CLI maps the typed Document to one Markout view for Markdown, table, TSV,
JSONL, and projected JSON. Plain unprojected Content JSON and `--envelope`
use the source-generated serializer for
`CapabilityCatalogSearchDocument`, preserving numeric similarity, booleans,
counts, arrays, identities, and relationship structure rather than
re-encoding rendered table cells. The Browser invokes the same typed operation,
then projects the completed envelope into assembly-local wire records at the
JavaScript boundary. That transport projection preserves every Content, Share,
and diagnostic value and order; it does not serialize product-owned types or
introduce another search result or ranking model.

The search branch supports shared presentation and destination controls plus
the semantic result limit. Resource Explanation traversal, including
`--depth`, reusable-reference operations, section selection, `--where`,
acquisition capabilities, source-content controls, and inspection verbosity
are inapplicable and are rejected before dispatch rather than ignored.

The Browser exposes a capability-search entry point using the same request and
host-neutral inspection envelope. Its generated facade returns the
assembly-local transport projection of that completed envelope. Spotlight
presents matching resources in one transient `Capabilities` group in its
existing `All` scope; it adds no persistent capability scope. It may route
selected exact paths directly rather than reproducing CLI operand parsing, but
its typed branch selection is equivalent. Search results can navigate to exact
explanation and to available Browser bindings. The first production
interaction follows the typed Package Query Browser binding without executing
a query. A selected Query Facet opens the exact canonical term's editor. When
the winning match provenance is an example value, Spotlight seeds the editor
with the original bounded search text; conceptual matches such as `literal`
continue to open an empty editor.
The CLI and Browser may arrange controls differently, but equivalent request
values over the same catalog generation must receive equal Content, Share, and
diagnostics before lossless host transport projection.

The root shipped skill eventually needs only the durable workflow:

```text
explain unfamiliar text
  -> select an exact result path
      -> explain the exact resource
          -> use the returned production binding
```

It does not carry a copied facet, section, route, or command inventory.

## Failure and completion

Request validation rejects:

- null, empty, or whitespace-only text;
- text longer than 128 UTF-16 code units; and
- result bounds outside 1 through 100.

Catalog construction failure remains a typed operation failure. Search does
not skip malformed resources, duplicate identities, missing paths, dangling
routes, or invalid production bindings and then claim a complete population.
Composition rejects duplicate structural catalogs, missing or duplicate
resource paths, paths outside their catalog root, bindings over unregistered
catalogs, and bindings selecting resources outside their registered catalog.
Unbound resources are registered facts, not malformed entries; search does
not advertise them.

Cancellation is not required for the first synchronous bounded operation. If
later catalog scale requires cancellation, the operation must distinguish
cancelled or incomplete evaluation from a complete empty result and must not
publish a partially ranked prefix as complete.

## Evidence and gates

<!-- markdownlint-disable MD013 -->

| Claim | Status and gate |
| --- | --- |
| `https://` discovers `library-literal` through its owner-issued example value | Implemented: host-neutral search and Browser transport tests over the composed Package Query capability graph |
| `literal` discovers the `library-literal` query facet through its canonical key segment | Implemented: host-neutral search test over the composed Package Query capability graph |
| A close misspelling uses the existing similarity model | Implemented: search test asserting `litteral` ranks `library-literal` with the `StringDistance` score |
| Search terms use owner-issued fields rather than adapter-authored explanation prose | Implemented: bounded-result regression proving generic Query Space and consumer-binding summaries do not participate |
| A host searches every composed capability family it exposes for exact explanation and production use | Implemented: CLI regression discovering both Package Query and Package File Inventory from one composed search graph |
| A host searches every structural catalog it explains | Implemented: a CLI wiring test proves every Section and Category of the complete `explain` catalog is admitted, and a CLI facade theory finds a result under each of the seven roots |
| Ranking is independent of registration order | Implemented: permutation test over equivalent composed catalogs |
| Equal scores use provenance, complete-versus-segment, and path tie-breakers | Implemented: focused ordering tests over production and bounded synthetic capability graphs |
| Search evaluates the complete available population before applying the result bound | Implemented: result-count and truncation test |
| No match is complete empty rather than failure | Implemented: host-neutral zero-match Document test |
| Invalid input does not become successful empty Content | Implemented: request-validation tests |
| Every result path resolves to the same resource identity | Implemented: search-to-Resource-Explanation handoff test |
| Search invokes no producer or acquisition path | Implemented: throwing route delegates in host-neutral ordering tests and a CLI acquisition seam test |
| The `explain` facade selects registered or canonical multi-segment paths and search text without failure fallback | Implemented: facade-level CLI matrix covering registered and unknown paths, single-segment search, misspelling, and noncanonical slash-bearing text |
| Reusable-reference shapes select their owner before Resource Path classification | Pending: no reusable-reference classifier is currently registered with the facade |
| CLI and Browser invoke the same host-neutral operation | Implemented: CLI facade tests plus Browser managed-export tests over host-composed catalogs |
| Spotlight presents capability matches without adding persistent shell chrome and activates the typed Browser binding | Implemented: TypeScript renderer/coordinator tests and a published Browser test for `https://` → `library-literal` → prefilled Package Query term editor |
| Equal requests over the same catalog generation produce equal Content, Share, and diagnostics | Guaranteed by the single host-neutral operation; a cross-host shared-generation harness remains pending |
| The real agent path reaches the production literal query | Implemented: CLI end-to-end test using `literal`, exact explanation, and `Microsoft.Azure.SignalR@1.33.1` at `net8.0` |
| Capability search has an available portable Browser Share | Pending: the first adoption reports `InspectionShare.NonProjectable` explicitly |
| `metrics`, `unsafe`, `leverage`, and `families` reach the Library sections that own them | Implemented: CLI facade theory that also follows each returned path through exact explanation |
| A structural section's Summary participates and its Basis, Empty, Boundary, and columns do not | Implemented: host-neutral term-provenance tests |
| Every structural result path resolves to the same Discovery resource in the exact-explanation catalog | Implemented: a CLI wiring test resolves every admitted path in the complete `explain` catalog to its Section or Category type, a host-neutral test resolves every candidate in the structural catalog created from the same inputs, and host-neutral rejection tests cover the entry checks |
| The structural population covers every Section and Category of each admitted catalog and no item, root, or collection | Implemented: host-neutral candidate-count test over a catalog with a column item |
| A structural result carries only bindings whose views select it; unbound resources are omitted and out-of-catalog bindings fail composition | Implemented: host-neutral binding-scope and rejection tests, and CLI theories that find `Context: Member` and `Metadata: Heap` under `library` with only the `library address -S` binding |
| Structural search acquires nothing and runs no producer | Implemented: the CLI acquisition seam test runs a structural match |
| A phrase averages each search word's best weighted term score, ignores function words and short words, and reports the phrase threshold | Implemented: host-neutral phrase tests over the structural fixture |
| A heading term outweighs a summary term, and a scoping path segment is context | Implemented: host-neutral weighting tests |
| A phrase equal to a term matches that complete term, including the separator-joined key `library-literal` | Implemented: host-neutral complete-term tests over the structural fixture and the composed Package Query graph |
| A text of several segments with no search word uses the whole-text rule | Implemented: host-neutral test reporting the `0.6` threshold |
| A phrase below `0.25` is complete empty | Implemented: host-neutral zero-match phrase test |
| The routing question "Does this library have namespace dependency cycles?" ranks Dependency Structure first | Implemented: CLI facade theory with the verbatim question and two shorter phrases |
| A host searches every analysis its bindings expose, with its owning route and binding | Implemented: the CLI test finds every analysis `diff --analysis` exposes, with only that binding and the Compare route, and follows each path through exact explanation |
| An analysis matches on its identity, path, and issued Finding titles, and a Finding-title word is context | Implemented: host-neutral term-provenance and weighting tests over Diff's registrations |
| An analysis no binding exposes is not a candidate | Implemented: host-neutral candidate-count test |
| `string literal diff` and `allocation changes` rank the analysis first | Implemented: CLI facade theory |
| `breaking changes between versions` ranks Diff `Changes` first, through its section description's Summary | Implemented: CLI facade theory |

<!-- markdownlint-enable MD013 -->

No timing or allocation-performance claim is made. The existing
MetadataPrimitives project boundary owns the reusable similarity
implementation; this adoption adds no new matching algorithm.

## Adoption sequence

1. Complete the #8417 Package Query capability registration, including the
   query facet's canonical Resource Explanation path and real CLI and Browser
   bindings. **Complete.**
2. Add the host-neutral search-term projection, request, result Document,
   similarity ranking, and exact-path handoff over the settled composed graph.
   **Complete.**
3. Extend the top-level `explain` facade with syntax-selected capability search
   for every operand that is neither an exact registered path or alias,
   reusable-reference-shaped, nor a canonical multi-segment `ResourcePath`.
   **Complete for exact paths and search text; reusable-reference dispatch is
   pending its owner-provided classifier.**
4. Add the Browser/Wasm capability-search entry point over the same envelope.
   **Complete as a generated managed export and Spotlight interaction;
   portable Share remains pending.**
5. Replace detailed capability inventory in the shipped router skill with the
   search, explain, discover, and execute workflow after both production hosts
   are available. **Complete.**
6. Admit the CLI's explained structural catalogs, Library first, as the
   structural catalog population with their section-selection bindings
   ([#10068](https://github.com/richlander/dotnet-inspect/issues/10068)).
   **Complete for the CLI.** The Browser admits structural catalogs when its
   structural explanation consumer lands.
7. Score phrases word by word with heading weights
   ([#10568](https://github.com/richlander/dotnet-inspect/issues/10568)).
   **Complete.**

8. Admit by registration plus binding over one composed graph
   ([#10700](https://github.com/richlander/dotnet-inspect/issues/10700)).
   Analyses join first. The structural population family is retired;
   structural catalogs register in the graph.
   **Complete for analyses and structural catalogs.**

Each slice lands with its own production consumer. A search implementation
over a synthetic test-only registry does not complete this design.

## Alternatives not selected

### Exact substring-only search

Substring search makes `literal` easy but gives misspellings and neighboring
terms no useful orientation. It would also introduce a second matching model
despite the repository already owning normalized edit-distance similarity.

### Exact-resolution failure fallback

Trying similarity only after a path or reusable-reference operation fails
would make misspelled automation ambiguous. The facade instead selects search
only when the operand does not have either exact operand syntax. Resource
Explanation continues to resolve exactly one canonical path, and reusable
reference explanation preserves its owner's exact invalid and unavailable
outcomes.

### Search-only aliases or keywords

A manually curated alias inventory would drift from producer capabilities and
repeat the maintenance failure this architecture is intended to remove. The
first contract derives terms only from explicit composed owner facts. A future
synonym owner requires a separately demonstrated need and design.

### Corpus term weighting

Weighting each search word by its rarity was measured offline with the
dotnet-docs-llm-tools entropy inventory, built over all text the product
ships. On its own it ranked worse: a word rare in that text but absent from
the target, such as `memory` in `unsafe code memory safety`, pulled the mean
below the threshold. It also needs a generated inventory that would drift
with the product's prose. The heading ratio needs neither.

### Embeddings or learned semantic ranking

Remote or model-dependent ranking is not version-matched, deterministic,
resource-free, or necessary for the motivating scenario. Installed
edit-distance similarity over explicit product contracts is sufficient.

### Parser-help or rendered-output indexing

Those surfaces mix presentation, compatibility, and implementation detail.
They cannot prove that a result is an executable modern capability or supply a
typed route and exact resource path.
