# Capability catalog search

## Status

This document is the normative design for **Capability Catalog Search**,
tracked by
[#8424](https://github.com/richlander/dotnet-inspect/issues/8424).

The operation is proposed. It depends on the explicit available-capability
graph defined by
[Inspection Capability Composition](inspection-capability-composition.md).
Its first production adoption will let CLI and Browser users search for
`literal`, discover the Package Query `library-literal` facet, resolve its
exact Resource Explanation path, and reach the existing production query.

## Owner and exact claim

**Capability Catalog Search** owns this exact claim:

> Given one non-empty bounded search text and one settled composed
> available-capability graph, project a bounded set of canonical search terms
> from each available resource, rank matching resources with the repository's
> existing string-similarity model, and return one deterministic bounded
> Document that preserves each result's exact resource path, owning route, and
> available production bindings without acquisition or producer execution.

This owner defines:

- the host-neutral search request and result Document;
- which composed owner-issued fields participate in search;
- canonical term segmentation and comparison normalization;
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
remains the semantic drill-down when the agent already has one canonical
resource path.

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
An agent starts with the concept `literal`, not with prior knowledge of Package
Query:

```console
dotnet-inspect catalog literal
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

The agent then resolves the returned exact path:

```console
dotnet-inspect explain <library-literal-resource-path>
```

and can execute the established production scenario:

```console
dotnet-inspect package query Microsoft.Azure.SignalR \
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
ranked resource identities, paths, routes, and production bindings. Browser
interaction design may differ, but it may not privately rank another catalog
or reconstruct capability from UI labels.

## Basis and authority map

| Owner | Contract consumed by catalog search |
| --- | --- |
| [Inspection Capability Composition](inspection-capability-composition.md) | The deterministic available-capability graph, owner-issued resource facts, routes, and production bindings |
| [Resource Explanation](resource-explanation.md) | Canonical product-resource paths and exact-path explanation |
| [`StringDistance`](../../src/ILInspector.MetadataPrimitives/StringDistance.cs) | Levenshtein edit distance and normalized similarity |
| [Inspection Envelope](inspection-envelope.md) | Completed host-neutral Content, Share, and diagnostics |
| [Host-observable Content Kinds](host-observable-content-kinds.md) | Document and complete-empty semantics |
| [Output Shapes](output-shapes.md) | Host lowering and structured output boundaries |
| CLI and Browser focused owners | Gesture parsing, interaction, presentation, and navigation |

`ILInspector.MetadataPrimitives.StringDistance.Similarity` is the normative
similarity algorithm. Capability Catalog Search does not introduce another
distance function, fuzzy library, phonetic model, stemming model, synonym
catalog, or learned ranker.

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
available-capability population, reports the total match count, and returns
the highest-ranked prefix. A host may map its ordinary row-limit gesture to
this value, but it may not clip a larger hidden result and describe the
remaining population as unknown.

The first operation accepts one search text. Boolean expressions, field
filters, globs, regular expressions, semantic embeddings, natural-language
plans, and pagination are outside this contract.

## Search population

The input population is the settled **available-capability** projection from
Inspection Capability Composition. A resource is searchable only when the
composed graph proves that it is reachable from an executable registered
route and at least one production consumer binding.

This excludes:

- proposed or partially registered resources with no production binding;
- legacy paths that do not participate in the modern composed graph;
- adoption-gap records used for engineering census;
- parser commands and options that are not owner-issued product resources;
- CLR types, source-code symbol names, and reflection-discovered values; and
- rendered section, help, Markdown, JSON, or Browser text.

The complete composed graph may separately expose adoption gaps to engineering
tools. User-facing catalog search does not mix unavailable plans into a result
that promises a next production gesture.

## Canonical search-term projection

Each searchable resource projects terms from the following composed fields:

1. its stable owner-issued identity;
2. its canonical Resource Explanation path;
3. owner-issued canonical keys, including a Query Space facet key;
4. its owner-issued name and summary; and
5. the stable identities and owner-issued names of directly related routes
   and available production bindings.

The search adapter selects these fields explicitly for each adopted descriptor
family. It does not use reflection, property enumeration, serializer metadata,
display-object inspection, or rendered text.

Each selected field contributes:

- its complete non-empty value; and
- non-empty segments split on ASCII whitespace or `.`, `/`, `:`, `-`, and
  `_`.

Segmentation is a search projection, not a new identity. It never replaces the
complete owner-issued value in results or Resource Explanation. The fixed
separator set makes `literal` an exact search term projected from the
canonical key `library-literal` without inventing a synonym or copying that
facet into a search-only inventory.

Terms are de-duplicated per resource with
`StringComparer.OrdinalIgnoreCase`. The projection retains the strongest
provenance and then prefers the complete-field form when equal text comes from
more than one field.

Search-term provenance is ordered from strongest to weakest:

1. owner identity or canonical key;
2. canonical resource path;
3. owner-issued resource name;
4. owner-issued summary; and
5. related route or production-binding identity or name.

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

| Search | Candidate term | Score and outcome |
| --- | --- | --- |
| `literal` | segment `literal` from `library-literal` | `1.0`, included |
| `litteral` | segment `literal` from `library-literal` | `0.875`, included |
| `library-literal` | complete canonical key `library-literal` | `1.0`, included |
| `literal` | an unrelated long summary containing no close term | below threshold, excluded |

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
  Rank
  Similarity
  MatchedTerm
  MatchSource
  IsSegment
  ResourceIdentity
  ResourceKind
  ResourceName
  CanonicalKeys[]
  ResourcePath
  OwningRoutes[]
  ProductionBindings[]
```

`CandidateResourceCount` is the complete available-capability population
evaluated by this request. `MatchCount` is the number at or above the
threshold before `MaximumResults`. `ReturnedCount` is the result-array length,
and `IsTruncated` is true exactly when `MatchCount > ReturnedCount`.

`CanonicalKeys` retains owner-issued keys applicable to the resource, including
`library-literal` for the production witness. Each route and
production-binding reference retains its typed composed identity. The CLI may
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

Hosts render a visible "No matching installed capabilities" outcome. They do
not fall back to parser help, perform network search, lower the threshold
silently, or turn an invalid request into this complete-empty result.

## Host-neutral operation

The owner exposes one completed operation:

```text
CapabilityCatalogSearch.Search(
  InspectionCapabilityCatalog catalog,
  CapabilityCatalogSearchRequest request)
  -> InspectionEnvelope<CapabilityCatalogSearchDocument>
```

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
`InspectionEnvelope<CapabilityCatalogSearchDocument>`. The target state uses
an available Share URL that preserves the search text and result bound for the
Browser capability-search route. Diagnostics remain ordered cross-host
diagnostics; a host does not translate an operation failure into an empty
result.

## Exact explanation handoff

Every returned row carries the canonical `ResourcePath` issued by Resource
Explanation. Selecting Explain passes that exact path unchanged:

```text
catalog search result ResourcePath
  -> ResourceExplanationCatalog.Resolve(exactPath)
```

Catalog search does not ask Resource Explanation to resolve the original
search text. `explain literal`, wildcard resource paths, prefix resolution,
and similarity fallback inside Resource Explanation remain invalid.

The handoff gate must prove that every returned path resolves to the same
typed resource identity represented by the search row. A dangling path, a
path that resolves to another identity, or a result synthesized without an
explainable resource prevents catalog construction or operation completion.

## CLI and Browser adoption

The proposed CLI gesture is:

```console
dotnet-inspect catalog <text>
```

`catalog` is a product-contract orientation command. It takes no package,
Library, Type, Member, project, or Workspace subject. Default Markdown is a
compact ranked table. Structured output serializes the same
`CapabilityCatalogSearchDocument`; it does not expose a CLI-only search DTO.

The first CLI adoption supports the ordinary output-format controls and the
semantic result limit. Section selection, `--where`, acquisition capabilities,
source-content controls, and inspection verbosity are inapplicable and are
rejected rather than ignored.

The Browser exposes a capability-search entry point using the same request and
inspection envelope. Search results can navigate to exact explanation and to
available Browser bindings. The CLI and Browser may arrange controls
differently, but equivalent request values over the same catalog generation
must receive equal Content, Share, and diagnostics.

The root shipped skill eventually needs only the durable workflow:

```text
search installed capability catalog
  -> explain an exact result
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

Cancellation is not required for the first synchronous bounded operation. If
later catalog scale requires cancellation, the operation must distinguish
cancelled or incomplete evaluation from a complete empty result and must not
publish a partially ranked prefix as complete.

## Evidence and gates

All gates below are planned until implementation lands.

| Claim | Gate |
| --- | --- |
| `literal` discovers the `library-literal` query facet through its canonical key segment | Host-neutral search test over the composed Package Query capability graph |
| A close misspelling uses the existing similarity model | Search test asserting `litteral` ranks `library-literal` with the `StringDistance` score |
| Ranking is independent of registration order | Permutation test over equivalent composed catalogs |
| Equal scores use provenance, complete-versus-segment, and path tie-breakers | Focused ordering tests with close neighboring resources |
| Search evaluates the complete available population before applying the result bound | Result-count and truncation test |
| No match is complete empty rather than failure | Host-neutral zero-match Document test |
| Invalid input does not become successful empty Content | Request-validation tests |
| Every result path resolves to the same resource identity | Search-to-Resource-Explanation handoff test |
| Search invokes no producer or acquisition path | Throwing producer/acquisition seam test over a settled catalog |
| CLI and Browser consume equal Content for the same request and catalog | Cross-host contract test |
| The real agent path reaches the production literal query | CLI end-to-end test using `literal`, exact explanation, and `Microsoft.Azure.SignalR@1.33.1` at `net8.0` |

No timing or allocation-performance claim is made. The existing
MetadataPrimitives project boundary owns the reusable similarity
implementation; this adoption adds no new matching algorithm.

## Adoption sequence

1. Complete the #8417 Package Query capability registration, including the
   query facet's canonical Resource Explanation path and real CLI and Browser
   bindings.
2. Add the host-neutral search-term projection, request, result Document,
   similarity ranking, and exact-path handoff over the settled composed graph.
3. Adopt `dotnet-inspect catalog <text>` with Markdown and structured output.
4. Add the Browser/Wasm capability-search entry point over the same envelope.
5. Replace detailed capability inventory in the shipped router skill with the
   search, explain, discover, and execute workflow after both production hosts
   are available.

Each slice lands with its own production consumer. A search implementation
over a synthetic test-only registry does not complete this design.

## Alternatives not selected

### Exact substring-only search

Substring search makes `literal` easy but gives misspellings and neighboring
terms no useful orientation. It would also introduce a second matching model
despite the repository already owning normalized edit-distance similarity.

### Resource Explanation similarity fallback

Letting `explain` guess from non-path text combines orientation with exact
resolution and makes automation ambiguous. Search returns candidates;
Resource Explanation continues to resolve exactly one canonical path.

### Search-only aliases or keywords

A manually curated alias inventory would drift from producer capabilities and
repeat the maintenance failure this architecture is intended to remove. The
first contract derives terms only from explicit composed owner facts. A future
synonym owner requires a separately demonstrated need and design.

### Embeddings or learned semantic ranking

Remote or model-dependent ranking is not version-matched, deterministic,
resource-free, or necessary for the motivating scenario. Installed
edit-distance similarity over explicit product contracts is sufficient.

### Parser-help or rendered-output indexing

Those surfaces mix presentation, compatibility, and implementation detail.
They cannot prove that a result is an executable modern capability or supply a
typed route and exact resource path.
