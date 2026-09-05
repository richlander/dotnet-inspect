# NuGet Gallery discovery

## Status and purpose

Focused design for [#5920](https://github.com/richlander/dotnet-inspect/issues/5920).
NuGet Gallery discovery in `NuGetFetch` is the sole normative owner.
[Delivery tracker #5919](https://github.com/richlander/dotnet-inspect/issues/5919)
records eight milestones from this design through both CLI and browser adoption.

**Typed request/catalog implemented; transport and delegation remain unverified.**
[Milestone 2, #5934](https://github.com/richlander/dotnet-inspect/issues/5934),
adds `NuGetGalleryDiscoveryRequest`, `NuGetGalleryPackageType`, and
`NuGetGalleryDiscoveryCatalog` in NuGetFetch. These express inert source intent;
they do not authorize access or advertise executable Gallery discovery.
Existing Gallery keyword/prefix search, typed source results, deadlines, and
the semantic row-selection language remain unchanged. The discovery transport,
general Source Delegation protocol, and product-row/host adoption are still
future work. Closed design issues are not implementation evidence.

The user goal is inexpensive discovery: find popular packages, tools, or
templates without already knowing a package name. Gallery capabilities should
serve that goal directly, rather than being limited to the portable feed API.
The added machinery is limited to typed source intent, a small discoverable
selector catalog, and completion evidence needed to preserve semantic limits.

## Authority and scope

This owner defines:

- Gallery-specific text and termless discovery, package-type selection, and
  source order;
- discovery and admission of Gallery search selectors and available orders;
- provider request/response interpretation, bounded acquisition, and typed
  package discovery observations; and
- Gallery-specific capability and evidence construction when adopting the
  existing Source Delegation protocol.

The exact claim is:

> One host-neutral Gallery discovery operation binds an authorized source and
> typed search intent to an explicitly bounded, ordered package-metadata input
> and honest scope/completion evidence. Row selection preserves that named
> input; a provider page-size shortcut cannot replace semantic selection.

This does not own generic feed behavior, source authorization or identity,
row-schema binding, row predicates, selection-stage meaning, Count, CLI
grammar, browser interaction, archive inspection, or product-demo registration.
It introduces one source capability, not a universal query language or a new
source-delegation protocol.

### Consumed contracts and layering

| Owner | Boundary consumed here |
| --- | --- |
| [NuGetFetch source identity](browser-package-sources.md#nugetfetch-typed-source-result-identity) and [operation deadlines](browser-package-sources.md#timeout-ownership) | Existing source association, factory-bound provenance, contained failures, cancellation, and operation ceiling. |
| [Package source model](package-source-model.md) | Host-authorized source eligibility and package-level result adoption; producer equality does not grant authority. |
| [Semantic row selection](semantic-row-selection.md) | Typed declaration language and complete-sequence reference meaning, without importing Sections or CLI concepts into NuGetFetch. |
| [Source delegation](source-delegation.md) | Caller-formed candidate, pure support decision, acceptance, closed result algebra, completion requirements, and residual boundary. |
| [Row query and ordering](row-query-order.md) | Resolved field/order meaning and source-closed declarations; this source cannot declare another owner's operation source-closed. |
| [Section-row shaping](section-row-shaping.md) | Declared package-row binding, plan partition, residual execution, and terminal Rows/Count meaning. |
| [CLI row selection](cli-row-selection.md) and [Package Query experience](package-query-experience.md) | Production consumers of shared typed intent/results, not owners of Gallery HTTP or search interpretation. |

NuGetFetch owns provider functionality. The row language is an orthogonal leaf,
as established by [Inspection layers](inspection-layers.md#the-layers).
Product row adapters retain schema/projection concerns above NuGetFetch; the
CLI and browser lower gestures into that shared composition. The adoption
tracker, not this document, assigns their implementation work.

## Gallery input and discovery domain

A request names one authorized Gallery source, optional search text, zero or
one package-type selector, a source order, prerelease policy, and bounded
response capacity K. The caller's semantic selection and the operation's other
resource ceilings are separate inputs.

Missing or whitespace-only text means **browse**, not invalid input and not a
request for a fabricated wildcard. Nonempty text uses Gallery search semantics;
it is encoded as one provider parameter, not interpreted as CLI text, a row
predicate, or a literal package-ID prefix. Existing literal-prefix inspection
remains a separate supported operation.

The discovery population is the Gallery's searchable listed package IDs under
that text, type, and version policy, including the provider's browse eligibility
rules. It is not every package ever uploaded or every version of each package.
One result identifies one package ID and its provider-selected eligible
version. Stable-only is the default; SemVer 2 packages remain eligible.
Package-type selection applies to the provider-selected version under the same
prerelease policy, not to an arbitrary historical version.

The initial **row input is one bounded Gallery response**, not the whole
discovery population. Its capacity K is part of the declared source input,
chosen before row planning and preserved with the result. It requests offset
zero and up to K rows, with positive K within the adapter's advertised maximum
of 1,000.
Changing only a semantic selection such as `Head(10)` must not silently change
K. The source cannot shrink K to ten as an optimization.

This restriction matters because Gallery selects page membership using indexed
download counts, then re-sorts that page using auxiliary lifetime counts.
The first ten of a 200-candidate response need not equal a ten-candidate
response. Each is useful Gallery discovery; neither certifies the globally
most-downloaded ten packages.

The initial source orders are:

| Order | Meaning |
| --- | --- |
| Most downloaded | Gallery's download-ranked candidate selection, then descending package-level lifetime downloads within that response, retaining provider tie order. Not global top-N, recent activity, or unique users. |
| Relevance | The Gallery's relevance order for this query, including its provider ranking policy. Not a locally reproducible numeric score. |

An omitted source order resolves to Most downloaded for browse and Relevance
for nonempty text. An explicit order wins in either case. Selection never
changes that resolved source order.

These are source-input orders: they define the incoming bounded sequence.
They are not aliases for L2 `--order-by` or semantic `Top`. A caller that
retains this incoming order can select its head; an additional row-order
operation still occupies its normal place in the caller's plan.

## Search-facet registry

Use one small, immutable, source-owned catalog in NuGetFetch. A search facet is
a typed selector over the Gallery discovery domain, not an arbitrary callback
and not a predicate over downloaded package contents.

A descriptor exposes a stable identity, display label and explanation, typed
value domain, cardinality, and availability. Lookup uses identity rather than
display text. Discovery is inert and does not perform source work. Selection
returns validated typed intent or a visible unsupported/invalid selection;
unknown selectors and invalid values are never dropped.

The initial facet is **package type**, with zero-or-one cardinality.
`DotnetTool`, `Template`, and `Dependency` are initial discoverable suggestions.
Other valid NuGet package-type names can be supplied as typed values.
All types means no selector, not a provider type named `all`. Dependency
packages are not labelled as guaranteed libraries: they may be metapackages.
Multiple package types do not implicitly become an OR query.

The same owner exposes the two source-order descriptors, separately from
filter facets. Text and prerelease policy retain their request roles. This
does not create a generalized registry for every search widget or operator.

The existing `PackageQuery.Facets` remains authoritative for manifest/content
evaluation. Its any-tool facet, for example, currently evaluates manifest
evidence; the Gallery package-type selector instead consumes index evidence.
Matching labels do not make those contracts or their evidence interchangeable.
A future composition may explicitly relate owner-issued descriptors, but it
must not move a residual manifest/content predicate into source input to evade
the delegation barrier.

Both hosts discover the same catalog. CLI aliases and UI labels may project it,
but neither host maintains a second provider-parameter or predicate inventory.
Additional facets earn admission individually with a provider contract and
gate; framework, verified-owner, dependency, and archive-content predicates are
not implied by this initial registry.

### Typed request/catalog adoption

`NuGetGalleryDiscoveryRequest` requires a Gallery source descriptor and explicit
capacity; it grants no source eligibility. It normalizes absent/whitespace-only
text to browse and resolves the default or explicit order at construction.
Package-type values use the existing NuGet package-ID grammar, as the Gallery
provider does, with lowercase value identity. Invalid construction or unknown
catalog identities throw argument exceptions, consistent with existing
NuGetFetch coordinate/configuration APIs, rather than returning altered intent.

The catalog exposes the typed package-type domain, optional-single cardinality,
source kind, suggestions, and separate source-order descriptors. Opaque IDs
use ordinal matching; labels and provider wire strings are not lookup aliases.
The entries describe source intent, not a transport capability advertisement.
The focused Release gate is `GalleryDiscoveryRequestAndCatalogTests`.

## Provider contract and typed results

The existing built-in Gallery transport is the authority for its endpoints.
It does not turn an arbitrary feed into a Gallery or infer authority from a
matching hostname.

The initial adapter adopts the Gallery `/search/query` service with
`packageType` and `sortBy=totalDownloads-desc` or `sortBy=relevance`.
The public V3 `/query` service supports package-type filtering, but does not
accept a sort parameter. A request for Most downloaded cannot silently switch
to relevance, to V3 search, or to a locally sorted relevance-limited subset.

Source observations preserve the selected package ID/version, description,
available display metadata, package-level lifetime downloads, producer
provenance, actual source order, and the applicable source selector evidence.
The Gallery response's `PackageRegistration.DownloadCount` is the lifetime
count; the row's outer `DownloadCount` is version-specific and must not replace
it. Optional fields remain unavailable when absent. A missing or malformed
field required to support a selected order or completion claim is a visible
source-contract failure, not zero, an omitted row, or an empty success.

Selected package type can be reported as evidence of the provider-applied
selector. It is not a claim that the response included an exhaustive list of
that package's types, or that its archive has been inspected.

The typed result retains the full source-input association, including K, and
completion evidence with its ordered rows. It reuses the existing source-result
identity and containment contracts rather than introducing caller-policing
tokens. Source-reported `totalHits` is an **estimate** of the discovery
population, not an L2 Count. It cannot prove population exhaustion, including
when it equals the returned row count or is zero. A missing or unusable
estimate remains unavailable without making otherwise valid rows unavailable.
This Gallery response has its own wire shape, including
`PackageRegistration.Id`, `Version`, and `NormalizedVersion`. It is not the
V3 search DTO; its count interpretation does not change generic feed parsing,
which deliberately tolerates sources without useful `totalHits`.

Basic discovery acquires search metadata only. Manifest, registration, archive,
and assembly enrichment are separate capability-bearing work. The enforcing
future gate is `GalleryDiscoveryUsesSearchMetadataOnly`; the property is
unverified until that gate runs against the product adapter.

## Source delegation and semantic limits

The initial adoption supports **acquisition-only row handoff** for one declared
bounded Gallery response input. The delegated operation prefix is empty;
semantic `Head`, predicates, ordering, and other stages stay in the caller's
exact residual. NuGetFetch consumes the owner-formed candidate and does not
parse `-n`, invent a row plan, or imply that using the row language authorizes
selection pushdown.

Acceptance, failure, and publication follow
[Source Delegation's effect protocol](source-delegation.md#effect-protocol).
The Source Delegation implementation and L2's declared finite-input binding are
prerequisites. This source design does not implement or alter their contracts.
Nonempty delegated prefixes, whole-population completion requirements, and
upstream exact Count decline before source work. A caller may use another
separately supported strategy after decline or report unsupported; it must not
quietly reinterpret the input.

Publication is atomic after one complete successful provider response. The
adapter admits its full ordered row sequence, without silently dropping
malformed rows or repeated package IDs, and preserves the accepted source
input. The named finite input is precisely the rows in that response, whose
membership and order belong to Gallery; K is its maximum requested capacity,
not a promise that K rows exist or will be returned.

The matching evidence proves that this **finite response input** was fully
acquired and admitted. It can establish logical exhaustion only of that named
input when the caller's completion requirement accepts that scope. Neither
transport EOF nor a response containing K rows establishes exhaustion or a
Head witness over the discovery population. The candidate must name the finite
input before execution; the source cannot relabel an incomplete population
request after receiving a short response.

A valid short or empty response can therefore be a complete finite input,
while remaining inconclusive about the wider population. Malformed/truncated
transport, decode failure, byte/time limits, or cancellation do not become
complete empty inputs. Accepted failures retain the existing source/delegation
failure outcomes and never silently switch plan or source.

There is no immutable-corpus, cross-page, or refresh-repeatability claim.
Future selection delegation needs both the operation owner's source-closed
declaration and a provider basis that proves equivalence over the same input.
The one-response restriction alone does not make download ranking
prefix-stable, and approximate `totalHits` cannot supply the missing proof.

### Residual predicates are a real boundary

Consider a 200-candidate tools input followed by a manifest predicate and
`Head(10)`. The caller evaluates that exact residual over the acquired input.
It may produce fewer than ten matches because this finite input contains fewer,
not because the Gallery has no further matching tools. Product adoption must
make the bounded scope visible; it cannot describe those rows as an exhausted
global top ten.

Fetching only ten candidates before evaluating the predicate changes both the
input and the plan. Likewise, `Head(100) -> Top(10, downloads)` does not
authorize changing the source order or capacity. Registry discovery grants no
permission to reorder either plan. These barriers leave useful local selection
available without promising unsupported global search semantics.

## Failures, resources, and platform

Inherited NuGetFetch source association, HTTP policy, bounded metadata decoding,
retry, cancellation, and shared operation deadlines apply. The external input
is Gallery response content entering that established construction boundary;
remote strings and failures retain the existing contained source-result form.
There is no new local-actor or inspected-code-execution threat model.

Provider page and response limits are operational ceilings, distinct from the
semantic row count. The declared response capacity makes the initial source
scope explicit; additional operational limits cannot truncate it into success.
Expected source failure and insufficient completion evidence remain visible
through the existing source/delegation outcomes. Failed execution does not
become unsupported planning or success-shaped empty rows.

The new endpoint must remain usable through the existing injected HTTP
capability on CLI and Browser/Wasm. CORS is a concrete browser-adoption
requirement, not a reason to add a browser-only search implementation. Current
provider observations support that path; they are not a permanent availability
guarantee or a platform exception.

## Consumer composition and demo

This section is a non-normative adoption map, not host or row-owner design.

```text
CLI gestures / browser controls
  -> shared product row intent and source selection
  -> L2 binding, order resolution, and delegation candidate + residual
  -> NuGetFetch Gallery operation using the row language
  -> typed rows + source completion evidence
  -> L2 residual / selected package rows
  -> CLI Markout output / browser query interaction
```

The CLI should consume its existing semantic `-n` lowering, not forward the
token to HTTP. The browser should express the equivalent typed selection.
Both should disclose the bounded Gallery input independently of the selected
row count. The shared product binding owns its default capacity; the example
below uses 200, not a new CLI flag or a mandatory source default.
CLI text/Markdown/JSON lowering uses the existing Markout/Sections path.
Browser interactive controls and cards remain host-specific rendering over
typed rows; browser adoption must name that lowering boundary rather than
copying Gallery behavior into TypeScript.

Mockup, not shipped syntax or a new CLI grammar:

```text
Gallery browse: .NET Tool, Most downloaded
Source input:  up to 200 Gallery candidates
CLI selection: -n 10
Browser:       Search [ optional text ] Type [ .NET tools ]
               Sort [ Most downloads ]  [ Search ]

Package                              Lifetime downloads
Cake.Tool                            170,466,408
dotnet-ef                            123,908,981
GitVersion.Tool                      122,595,411
...                                  ...
10 packages shown from a bounded Gallery input
Gallery estimates about 8,868 matching package IDs
```

The package values illustrate the observed ten-candidate request, not a claim
that every capacity returns the same prefix. The neighboring case uses
nonempty text and Relevance without changing the row pipeline. A generic V3
feed lacking Gallery browse/order capability remains a feed, not an automatic
substitute. A content-enriched query still exposes
its acquisition cost and semantic-selection boundary.

[#5919](https://github.com/richlander/dotnet-inspect/issues/5919) owns the
eight-milestone sequence and retirement of the Gallery page's required-prefix
assumption. Exact package lookup, literal prefix profiling, generic feeds, and
existing content facets stay supported. No host behavior changes in this PR.

Demand-windowed streaming is separately tracked by
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816).
This design neither changes its backpressure/worker contracts nor treats the
current Source Delegation protocol as permitting incremental success
publication. Query-demo registration and URL sharing remain focused follow-ons.

## Evidence and required gates

### Provider and convention evidence

The [public NuGet search contract][v3-search] documents termless search,
package-type filtering, package-ID grouping, prerelease policy, and response
download counts. The Gallery implementation at
[`bc5a59e3cf5d4d357e40615639b78615f97b2cc0`][gallery-controller] separately
accepts sorting on `/search/query`, not `/query`.
[Its parameter mapping][gallery-parameters] recognizes
`totalDownloads-desc`; [its search builder][gallery-order] selects candidates
using indexed download counts and creation-time tie order.
[Its response builder][gallery-response] then re-sorts only the selected page
using auxiliary lifetime counts. That is why capacity is part of input meaning.
The search builder limits `take` to 1,000; out-of-range values fall back to the
provider's default rather than honoring a larger request.

A fixed-state counterexample preserves this boundary as reproducible design
evidence: A has indexed/auxiliary counts 200/200; B has 100/300. `take=1`
selects A, while the response for `take=2` orders B before A. Thus changing K
to implement `Head(1)` changes the answer without any concurrent index update.

[The Gallery wrapper][gallery-wrapper] reads only the first Azure result page
and forwards its `TotalCount`. Azure's [IncludeTotalCount contract][azure-count]
explicitly describes that value as approximate. A short page whose approximate
total happens to equal its length cannot prove population exhaustion.

This is the deliberate provider-specific extension of the existing Gallery
source, not an extension silently imposed on generic V3. Gallery browse is the
behavioral analogue; the row reference evaluator, not Gallery implementation
details, remains the selection-equivalence oracle. No provider code is copied.

On 2026-09-04 a macOS HTTP observation of ten stable tools used 6,742 compressed
response bytes in 0.355 seconds; V3 relevance search used 17,047 bytes in 0.367
seconds. These are single observations, not latency thresholds or a speedup
claim. The sorted response produced the first three package counts shown in
the mockup. Reproduce the sorted request with:

```sh
curl --compressed --get \
  'https://azuresearch-usnc.nuget.org/search/query' \
  --data-urlencode 'packageType=DotnetTool' \
  --data-urlencode 'sortBy=totalDownloads-desc' \
  --data-urlencode 'prerelease=false' \
  --data-urlencode 'semVerLevel=2.0.0' \
  --data-urlencode 'take=10'
```

A subsequent GET with `Origin: https://dotnet-inspect.net` returned
`Access-Control-Allow-Origin: *` on 2026-09-05 UTC. Browser adoption still
requires a real browser run against the product path.

### Release gates

These gates are required before their respective implementation claims ship.
Except for the request/catalog gate, they remain unimplemented and unverified.

| Gate | Outcome established |
| --- | --- |
| `GalleryDiscoveryRequestAndCatalogTests` | Implemented in `src/NuGetFetch.Tests`: termless/type-filtered intent, explicit/default orders, prerelease intent, custom valid package types, invalid/unknown selections, bounded-input identity, and immutable descriptor discovery retain their declared meaning. Provider execution remains a later gate. |
| `GalleryDiscoveryUsesSearchMetadataOnly` | The product adapter returns basic browse rows with only search transport; no per-package enrichment is requested. |
| `GalleryDiscoveryProviderProjection` | Lifetime versus version downloads, optional missing fields, required-field failures, source association, provider ordering, and selector evidence are preserved. |
| `GalleryDiscoveryFiniteInputSelection` | Product acquisition preserves K and the whole provider response; the shared adopter's residual matches `RowSelectionExecutor` over that exact finite input, including ties and zero/fewer/exactly/more-than-N rows. The fixed index/auxiliary divergence detects replacing K with N. |
| `GalleryDiscoveryCompletionEvidence` | Evidence binds complete acquisition to the predeclared finite input, never to population exhaustion. Short/empty responses and approximate totals retain that distinction; malformed/duplicate rows, truncated transport, cancellation, and resource failures do not become complete inputs. |
| `GalleryDiscoveryDelegationBoundaries` | Nonempty delegated prefixes, whole-population completion, out-of-range capacity, and upstream Count decline before source work; accepted failures do not fall back. Local predicate/Head composition retains the declared candidate scope. |

The adoption consumes the Source Delegation and RowSelection owners' own gates;
these tests do not replace their effect-protocol or reference-semantics evidence.
The L2 and host milestones separately gate binding and CLI/browser equivalence.
A live provider probe detects observed provider drift but does not prove future
provider honesty; deterministic fixtures enforce adapter behavior in CI.

No new scheduling protocol is defined here. Atomic one-response adoption uses
the existing delegation contract; concurrent execution or incremental
publication would require separate owner work and corresponding model evidence.

[v3-search]: https://learn.microsoft.com/en-us/nuget/api/search-query-service-resource
[gallery-controller]: https://github.com/NuGet/NuGetGallery/blob/bc5a59e3cf5d4d357e40615639b78615f97b2cc0/src/NuGet.Services.SearchService.Core/Controllers/SearchController.cs
[gallery-parameters]: https://github.com/NuGet/NuGetGallery/blob/bc5a59e3cf5d4d357e40615639b78615f97b2cc0/src/NuGet.Services.AzureSearch/SearchService/Models/ParameterUtilities.cs
[gallery-order]: https://github.com/NuGet/NuGetGallery/blob/bc5a59e3cf5d4d357e40615639b78615f97b2cc0/src/NuGet.Services.AzureSearch/SearchService/SearchParametersBuilder.cs
[gallery-response]: https://github.com/NuGet/NuGetGallery/blob/bc5a59e3cf5d4d357e40615639b78615f97b2cc0/src/NuGet.Services.AzureSearch/SearchService/SearchResponseBuilder.cs#L330-L353
[gallery-wrapper]: https://github.com/NuGet/NuGetGallery/blob/bc5a59e3cf5d4d357e40615639b78615f97b2cc0/src/NuGet.Services.AzureSearch/Wrappers/SearchClientWrapper.cs#L40-L61
[azure-count]: https://learn.microsoft.com/en-us/dotnet/api/azure.search.documents.searchoptions.includetotalcount?view=azure-dotnet
