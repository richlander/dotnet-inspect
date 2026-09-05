# Incremental package-prefix candidates

## Owner and consumer

This document owns demand-driven prefix-search page production in NuGetFetch.
The claim is narrow: an admitted page can be consumed before later pages are
requested, without changing candidate order, source identity, or search bounds.
[Browser package sources](browser-package-sources.md) continues to own source
result construction, transport, and request deadlines.

The production consumer is `PackageProfileQuery`, shared by Inspect Web Package
Query and CLI `find --package-prefix`. End-to-end tracker
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816) records the broader
responsiveness work. This source-production slice has three adoption steps,
landing together:

1. Add ordered, pull-driven prefix pages to the source contract and Gallery.
2. Have `PackageProfileQuery` evaluate each page's exact manifests before asking
   for another page, replacing its full-prefix materialization barrier.
3. Exercise that query through both existing production hosts. The Browser
   already supplies match credit; the CLI retains its materialized presentation
   and shared operation context.

No host-specific search implementation or new rendering path is introduced.
The materialized source API remains useful to callers requiring one aggregate;
it shares pagination machinery with the page API rather than maintaining a
second search algorithm. Local and custom clients may provide their existing
bounded aggregate as one page. Unsupported prefix search remains unsupported.

## Page contract

`SearchByPrefixPagesAsync` returns an ordered async sequence of the existing
`PackageSourceOperationResult<PackageSearchResult>` values. A successful value
contains only that page's admitted candidates, not the accumulated prefix.
Empty filtered pages are valid: they do not prove source exhaustion.

- Enumeration starts work only on demand. Once a page returns, no later page
  is requested until the consumer advances.
- Candidates retain source relevance order. Gallery admits only literal,
  case-insensitive prefix matches and retains the first occurrence of each
  case-insensitive package ID across pages.
- Normal sequence completion establishes the end of the observed search.
  A final successful page carries any requested-count, source-page, or
  client-page truncation reason. Intermediate pages carry `None`; that value
  alone is not an exhaustiveness claim.
- A failed page terminates the sequence. Earlier admitted pages remain valid
  partial evidence, but neither the failure nor early consumer disposal proves
  exhaustion. Materialized callers retain their all-or-failure behavior.
- Caller cancellation remains cancellation, and disposal requests no more
  work. Source outcomes retain their existing factory-issued identity and
  immutable snapshots.

Gallery still requests 100 raw rows per page, advances by the raw response
count, and applies its existing 3,000 maximum skip and 100-page client ceiling.
Metadata byte limits, repeat-page rejection, candidate limits, and exact
manifest validation remain in force. One retained source page is permitted;
this does not promise one network row per Browser credit or avoid scanning
nonmatching candidates to establish the next match.

## Work and deadline ownership

The page stream's standalone operation ceiling measures cumulative active
source work: requests, retries, body processing, filtering, and result
construction. Time while the consumer holds an already returned page is not
source work. Resumption receives only the remaining budget.

A caller-supplied `NuGetOperationContext` instead retains its unchanged,
caller-owned wall-clock ceiling across the entire composition. The source
never pauses or replaces it. Per-request and body bounds are unchanged.
The Browser's enclosing active-work clock separately covers manifest and
package-content work under the existing Package Query adopter contract.

The convention is pull-based `IAsyncEnumerable<T>` composition, as used by the
existing package-profile and Package Query streams. This avoids a background
producer, parallel manifest work, or an independently buffered queue. The
[NuGet Search protocol][search-protocol] supplies `skip`/`take`, not true prefix
matching; the
existing `SearchService` client-side filter and duplicate handling remain the
behavioral reference.

[search-protocol]: https://github.com/NuGet/docs.microsoft.com-nuget/blob/9864ac481a47dbfd4b4d71254974ecc7c33221c2/docs/api/search-query-service-resource.md

## Evidence and non-claims

`PackagePrefixSearchTests` gates demand, order, filtering, limits, late failures,
cancellation, idle-time exclusion, cumulative active budget, and caller-context
ownership. `PackageProfileQueryTests` gates manifest work before later search
pages, disposal, and partial-result accounting. `PackageQueryTests` gates the
consumer's existing limits and failure projection. These gates run in Release.

The pathological case is a useful first page followed by a blocked or failed
second page: the first manifest result must already be observable, and the
second page must not start without further demand. A neighboring empty-filtered
page must not incorrectly end the search.

This slice does not claim a fixed first-row latency, reduce NuGet round-trip
time, parallelize manifests, virtualize the DOM, or move Wasm into a Worker.
The production measurements in #5816 explain the motivation, not a deterministic
latency guarantee.
