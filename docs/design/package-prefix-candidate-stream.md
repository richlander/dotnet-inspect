# Incremental package-prefix candidates

## Owner and consumer

This document owns demand-driven prefix-search page production in NuGetFetch.
The claim is narrow: an admitted page can be consumed before later pages are
requested, without changing candidate order, source identity, or search bounds.
[Browser package sources](browser-package-sources.md) continues to own source
result construction, transport, and request deadlines.

Gallery's page stream uses a prefix-candidate projection: it decodes the
top-level search metadata consumed by `PackageSearchMatch` but does not decode,
validate, retain, or snapshot the response's version-history collection.
Ordinary materialized search APIs retain their complete `SearchResult`
projection because callers may consume that history.

The production consumer is `PackageProfileQuery`, used by CLI
`find --package-prefix` and the shared Package Query prefix path. End-to-end tracker
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816) records the broader
responsiveness work. Website package-ID/prefix adoption is tracked in
[#6070](https://github.com/richlander/dotnet-inspect/issues/6070).
The source-to-host path has three adoption steps:

1. Add ordered, pull-driven prefix pages to the source contract and Gallery.
2. Have `PackageProfileQuery` evaluate each page's exact manifests before asking
   for another page, replacing its full-prefix materialization barrier.
3. Adopt that query through the website's explicit prefix input in #6070,
   alongside its exact-ID input. The Browser already supplies match credit; the
   CLI retains its materialized presentation and shared operation context. The
   first two steps land in this source slice; the Browser adoption is a focused
   successor, not a current website claim.

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
- Gallery prefix pages preserve top-level ID, latest version, description,
  downloads, verification, and owners. `SearchResult.Versions` is absent on
  those page-stream matches. Invalid top-level package identities and malformed
  JSON remain failures; semantically invalid entries inside the syntactically
  valid, unconsumed `versions` property do not invalidate a prefix candidate.

Gallery candidate pages initially request 20 raw rows. When a completed raw
page admits fewer than half as many new literal-prefix matches as raw rows, the
next request doubles monotonically through 40 and 80 to the existing 100-row
ceiling. Dense prefixes therefore retain a 20-row acquisition window, while
sparse prefixes recover the established scan throughput. Materialized prefix
search retains 100-row requests because its caller has already requested one
aggregate rather than demand-driven pages.

The Gallery candidate projection uses a 128 KB `System.Text.Json` read buffer.
This setting belongs only to the prefix projection context; materialized search
retains the serializer default. The larger prefix buffer reduces Browser/Wasm
stream crossings without changing the response, decoded fields, validation, or
candidate ordering.

Pagination advances by the raw response count and retains the existing 3,000
maximum skip and 100-page client ceiling. Metadata byte limits, repeat-page
rejection, candidate limits, and exact manifest validation remain in force.
One retained source page is permitted; this does not promise one network row
per Browser credit or avoid scanning nonmatching candidates to establish the
next match.

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

`PackagePrefixSearchTests` gates demand, order, filtering, adaptive request
sizes, limits, late failures, cancellation, idle-time exclusion, cumulative
active budget, caller-context ownership, top-level metadata preservation,
version-history omission, and top-level identity failure.
`PackageProfileQueryTests` gates manifest work before later search pages,
disposal, and partial-result accounting. `PackageQueryTests` gates the
consumer's existing limits and failure projection. These gates run in Release.

The pathological case is a useful first page followed by a blocked or failed
second page: the first manifest result must already be observable, and the
second page must not start without further demand. A neighboring empty-filtered
page must not incorrectly end the search.

The retained performance witness is `AWSSDK.*`. The #6569 prototype measured
that a 100-row response carried 125,094 version entries and about 16.7 MB of
decompressed JSON, while Package Query consumed none of that history. The
production projection transfers only the prototype's top-level-field selection;
it continues to use the existing source client, request containment, result
factory, and page contract.

The adaptive-page prototype at `724774adf` ran the same published Browser
artifact and NativeAOT comparator three times for twelve prefixes on both
fernie and merritt. The first request was 20 rows in all 72 Browser samples,
and all samples paused at the initial 20-match credit. `AWSSDK.*` row-20
latency fell from 7.920 to 2.468 seconds on fernie and from 6.120 to 1.672
seconds on merritt; descendant-process RSS growth fell from about 75 to 31 MB
and from about 78 to 40 MB respectively. The sparse `Grpc.*` witness grew to
100-row requests and completed source exhaustion within 0.4 seconds of the
fixed-100 baseline on both hosts.

The phase-timing prototype at `cb016eeb9` then compared the serializer-default
and 128 KB prefix buffers with five samples for `System.*`, `Azure.*`,
`AWSSDK.*`, and `Grpc.*` on each host. Both artifacts used the same supported
Search Query Service response and the same prefix projection. For the 3.55 MB
decoded `AWSSDK.*` first page, the larger buffer reduced managed stream reads
from 272 to 82 on fernie and 85 on merritt. Median stream-read time fell from
513 to 85 ms and from 897 to 68 ms; row-20 latency fell from 2.347 to 1.839
seconds and from 1.701 to 0.820 seconds. A 256 KB probe produced no further
row-20 improvement on fernie, so 128 KB is the smallest measured plateau.
Five alternating clean-production pairs then compared `origin/main` with
candidate `e080cdb21`. `AWSSDK.*` median row-20 latency fell from 2.900 to
2.668 seconds on a heavily loaded fernie and from 1.672 to 0.816 seconds on
merritt. The candidate was faster in every fernie pair; one merritt candidate
request was a network outlier, while the other four improved by 51-53%.

This slice does not claim a fixed first-row latency, reduce NuGet round-trip
time, yield individual candidates before one raw page completes, isolate
decompression within stream-read time, parallelize manifests, virtualize the
DOM, or change the Worker credit protocol. The production measurements in
issue #5816 explain the motivation and observed result, not a deterministic
latency guarantee.
