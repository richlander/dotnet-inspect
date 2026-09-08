# NuGet Catalog acquisition

## Status and authority

Focused source-capability design for
[#6294](https://github.com/richlander/dotnet-inspect/issues/6294), implementing
step 2 of the eight-step
[ecosystem change-report tracker](https://github.com/richlander/dotnet-inspect/issues/6124).

**NuGet Catalog acquisition in `NuGetFetch` is the sole normative owner.**
Its claim is:

> One authorized NuGet V3 source can incrementally acquire a bounded Catalog
> interval while preserving source-issued event identity, the observed Catalog
> horizon, explicit completion, and typed failure. It never promotes a partial
> read into complete coverage.

The named consumer is the shared ecosystem change-report query, followed by
CLI and browser/Wasm adoption in steps 4, 6, and 7 of #6124. This capability
adds no host surface. It retires no existing source path, so no migration or
retirement plan applies. Rendering is not applicable: the output is typed
source evidence, and shared Presentation remains a later Markout owner.

## Scope and consumed contracts

This owner defines:

- discovery and selection of advertised `Catalog/3.0.0` resources;
- explicit interval validation and bounded index/page acquisition;
- typed Catalog event, progress, horizon, completion, and failure evidence;
- chronological page and event processing; and
- the exact point at which a bounded interval may be called covered.

It does not define ecosystem membership, package-ID prefix filtering, security
evidence, report predicates or row limits, current package inventory, saved
cursors, caching, leaf enrichment, query event-stream semantics, command
grammar, browser placement, or rendering.

| Owner | Boundary consumed here |
| --- | --- |
| [NuGetFetch source-result identity](browser-package-sources.md#nugetfetch-typed-source-result-identity) | Caller-authorized source association, factory-issued provenance, contained failures, and custom-client validation. |
| [NuGetFetch operation deadlines](browser-package-sources.md#timeout-ownership) | One monotonic operation ceiling, nested request deadlines, caller cancellation, and source-safe timeout identity. |
| [NuGet API selection](nuget.md#scenario-selection) | Catalog is the time-indexed source for exhaustive recent activity; Search and Registration retain their distinct scenarios. |
| [Untrusted-data threat model](untrusted-data-threat-model.md#feed-discovered-package-resources-use-a-guarded-destination-policy) | Guarded advertised destinations, source-relative credentials, bounded response bodies, duplicate-property rejection, and visible malformed metadata. |
| [Ecosystem change report](ecosystem-change-report.md) | Future consumer of source-issued events and coverage; report scope, selection, security meaning, and presentation remain above this owner. |

The implementation inherits the existing platform contract and transport
mechanisms. Desktop and browser transports retain their existing destination
policies; in particular, browser/Wasm does not gain cross-origin feed authority
through this capability.

## Request and resource bounds

`NuGetCatalogRequest` names one explicit UTC interval
`(FromExclusive, ThroughInclusive]`. The end must be later than the start, and
the interval may not exceed 42 days. The source owner does not resolve a
default reference time: the report query owns the rolling six-week default and
passes its resolved bounds here.

The request contains no package scope, prefix, prerelease choice, security
selector, semantic row count, or presentation option. Every in-window Catalog
event is source evidence for later consumers. Repeated coordinates remain
repeated events.

Catalog-specific `NuGetFetchOptions` bound one operation independently of the
existing per-response metadata limit:

| Bound | Default | Meaning |
| --- | ---: | --- |
| Catalog pages | 512 | Maximum page documents acquired after the index |
| HTTP attempts | 1,024 | Actual sends across service-index, Catalog-index, page, redirect, authentication, and retry traffic |
| Aggregate decoded metadata | 512 MiB | Bytes read across every admitted Catalog document and retry attempt |

The existing 16 MiB per-response ceiling, request deadline, metadata-body
deadline, and 120-second default operation deadline still apply. A response
that exceeds its own ceiling, malformed JSON, and transport failure remain
typed source failures.
Reaching an operation-wide Catalog bound between complete documents is an
explicit partial completion, not an empty success or an assertion that the
window was exhausted after the Catalog horizon has been captured. A bound
reached before that point is a typed source failure because no coverage
evidence exists to attach to a partial completion. If an aggregate byte bound
is crossed while reading a document, that incomplete document is rejected and
no events from it are published.

The defaults are grounded in the preserved 42-day experiment, which consumed
210 pages, 212 requests, and about 184 MiB of decoded metadata. They bound the
approved scenario without turning those observations into provider promises.

## Discovery and advertised authority

The client reads the selected source's service index and discovers
`Catalog/3.0.0`; it never substitutes the nuget.org Catalog for another source.
The resource type may use the service-index string-or-array shape. Usable
equivalent endpoints are retained in service-index order under a small fixed
cap, matching existing V3 resource discovery. An unusable supported resource
is a visible invalid response when no usable equivalent remains; a source that
does not advertise Catalog returns `Unsupported`.

Every Catalog index, page, and retained leaf URL is taken from an advertised
document and normalized through the existing source request projection. URLs
are not synthesized from page numbers, timestamps, package IDs, or versions.
Credentials follow the existing same-origin rule, and the configured transport
enforces desktop and browser destination policy for every request.

Equivalent Catalog endpoints may fail over only before an endpoint publishes
an acquired page. One interval never combines pages from independent Catalog
roots, because their horizons and event identity need not describe one log.

## Bounded interval traversal

The [NuGet Catalog resource specification][catalog-spec] is normative for wire
semantics. The
[`NuGet.Protocol.Catalog` bounded-range implementation][catalog-bounds] is
supporting analogous evidence for its documented “upper range plus one page”
algorithm; it is not an imported implementation.

Acquisition performs these steps:

1. Fetch the service index and one selected Catalog index under the operation
   budgets.
2. Validate the index horizon and page descriptors, then sort page descriptors
   by `commitTimeStamp` and a stable advertised-URL tie-breaker. Index array
   position has no ordering meaning.
3. Select pages whose maximum commit timestamp is later than the exclusive
   lower bound.
4. Continue through every selected page whose maximum commit timestamp equals
   the first maximum later than the effective upper bound. A page timestamp is
   its maximum, not its minimum, so each tied crossing page can still contain
   in-window events.
5. Parse each selected page atomically, sort its items by commit timestamp and
   stable leaf-URL tie-breaker, and publish only items whose timestamps are
   greater than the lower bound and less than or equal to the effective upper
   bound.

The index `commitTimeStamp` is the observed horizon for this attempt. The
effective upper bound is the earlier of that horizon and the requested end. If
the source horizon trails the requested end, acquisition may still publish the
covered prefix, but terminal completion is `SourceHorizonReached`, not
`WindowExhausted`.

The analogous NuGet implementation stops after one crossing page. This owner
deliberately extends that algorithm through every descriptor tied at the
crossing maximum: the wire contract makes index order undefined and exposes no
page minimum that would prove another tied page contains no in-window event.
The ordinary acquisition bounds remain the stopping authority.

The active page can grow after the index was fetched. Events later than the
captured index horizon are excluded even if the subsequent page response
contains them. This binds every emitted event and terminal coverage claim to
one observed horizon without claiming a stable filesystem-like snapshot.
The index horizon must match its latest page descriptor; an inconsistent root
cannot establish coverage. A page response older than its descriptor is
retried as a stale active-page observation and fails visibly if it remains
stale.

Top-level and page `count` values are not used to invent events or coverage.
The acquired arrays are authoritative for that response. No page-size
assumption is made: a single commit can produce a page much larger than a
provider's nominal target.

## Typed incremental result

`INuGetCatalogPackageSourceClient` is a capability implemented by the V3
source client. It exposes a backpressured `IAsyncEnumerable` of factory-issued
`PackageSourceOperationResult<NuGetCatalogPage>`. The general package-source
interface remains unchanged; consumers opt into Catalog only when the source
implements this capability.

Each successful page value retains:

- the exact source-result identity and request;
- immutable typed events from one acquired page;
- the captured Catalog horizon;
- cumulative pages, HTTP attempts, decoded bytes, and in-window event count;
- and an optional terminal `NuGetCatalogCompletion`.

The last successful value carries exactly one terminal completion:

| Completion | Meaning |
| --- | --- |
| `WindowExhausted` | The source horizon covers the requested end and every selected page was admitted. |
| `SourceHorizonReached` | Every page through the captured horizon was admitted, but that horizon predates the requested end. |
| `PageLimitReached` | The configured page bound stopped acquisition before coverage was established. |
| `RequestLimitReached` | The configured attempt bound stopped acquisition before coverage was established. |
| `DecodedByteLimitReached` | The aggregate decoded-byte bound stopped acquisition before another complete document could be admitted. |

An empty interval still produces one terminal value. A typed source failure
ends the sequence after any already-published pages; caller cancellation
propagates as cancellation. No internally created deadline or network resource
remains live while the consumer processes a yielded page. A caller-owned
`NuGetOperationContext` retains its caller-defined lifetime across the stream.
Catalog acquisition inherits the operation-context owner's token-identity
rules rather than defining a second cancellation contract.

`NuGetCatalogEvent` retains the source package ID and version, their normalized
package coordinate, advertised leaf URL, Catalog commit ID, commit timestamp,
and a closed Details/Delete kind.
Both kinds are activity observations. Details is not renamed to publish,
release, relist, metadata change, or vulnerability update. Repeated events for
one coordinate are not deduplicated.

Catalog commits are strictly ordered by timestamp, but item order within one
commit is undefined. The stable leaf-URL tie-breaker gives deterministic
delivery without claiming causality. This capability does not persist or
promise a resumable cursor. In particular, an intermediate page is not proof
that every item sharing its last commit timestamp has been processed.

## Failure and admission

Service indexes, Catalog indexes, and pages are untrusted JSON. Required
members, timestamp values, package coordinates, supported event kinds, and
advertised URLs are validated before a page is published. Duplicate object
properties are rejected at every depth so two readers cannot bind different
values. Unknown optional properties are ignored.

A page is admitted atomically. Malformed or over-bound metadata never produces
some events from that page. Previously yielded pages remain observable, while
the terminal source result remains failed rather than being rewritten as
`WindowExhausted`.

Only `nuget:PackageDetails` and `nuget:PackageDelete` page items are supported.
Their page observations do not require leaf acquisition. The leaf URL is
retained as source-issued identity and possible future enrichment authority;
this slice does not fetch or interpret leaf documents.

## Evidence and non-claims

The focused Release suite must gate:

- explicit request bounds and the absence of implicit filtering;
- advertised resource discovery and no hard-coded Catalog fallback;
- unordered index/page arrays and the crossing-page upper boundary;
- exclusive lower and inclusive upper timestamps;
- captured-horizon filtering when the active page grows;
- inconsistent index horizons and retry of stale active-page responses;
- `WindowExhausted` versus `SourceHorizonReached` and each acquisition bound;
- backpressure, page-atomic publication, partial failure, and caller
  cancellation;
- repeated coordinates, deterministic same-commit delivery, and distinct
  Details/Delete evidence;
- source identity, normalized advertised URLs, credential scope, malformed
  URLs, duplicate properties, and per-response/aggregate body limits; and
- original coordinate spelling beside normalized package identity; and
- no leaf request for page-event acquisition.

The six-week live experiment remains performance evidence, not a normal CI
dependency. This slice makes no report latency, security completeness,
ecosystem membership, inventory, cache, cursor, CLI, or browser presentation
claim.

[catalog-spec]: https://learn.microsoft.com/nuget/api/catalog-resource
[catalog-bounds]: https://github.com/NuGet/NuGetGallery/blob/main/src/NuGet.Protocol.Catalog/Models/ModelExtensions.cs
