# NuGet Catalog package-receipt evidence

## Status and authority

This document is the normative owner for enriching one NuGet Catalog Package
Details event with the source-issued time when that package version was first
received. Its claim is deliberately narrow:

> Given one factory-issued Package Details event, acquire its advertised
> Catalog leaf and return the source-issued package-receipt timestamp while
> preserving exact event and source-result identity.

The immediate consumer is the
[ecosystem change report](ecosystem-change-report.md), whose tracked production
path in [#6124](https://github.com/richlander/dotnet-inspect/issues/6124)
continues through shared query and rendering, CLI, and browser/Wasm adoption.
That report remains the owner of joining receipt evidence to advisory evidence
and classifying a security release.

This owner does not define ecosystem membership, advisory interpretation,
security-release meaning, report filtering or ordering, Registration metadata,
presentation, caching, or a maintained package inventory.

| Owner | Boundary consumed here |
| --- | --- |
| [NuGet Catalog acquisition](nuget-catalog-acquisition.md) | Factory-issued Details events, retained advertised leaf URLs, exact coordinate and commit identity, and source-result provenance. |
| [NuGetFetch source and deadline contracts](browser-package-sources.md#nugetfetch-typed-source-result-identity) | Caller-authorized source association, typed failure, guarded transport, credential scope, request and operation deadlines, and cancellation. |
| [Untrusted-data threat model](untrusted-data-threat-model.md#feed-discovered-package-resources-use-a-guarded-destination-policy) | Guarded advertised destinations, bounded response bodies, duplicate-property rejection, and construction-time validation. |
| [NuGet Catalog specification][catalog-spec] | Package Details leaf fields and the meanings of `created`, `published`, and Catalog commit identity. |

## Evidence contract

`NuGetCatalogPackageReceipt` retains the exact factory-issued
`NuGetCatalogEvent`, its `PackageSourceResultIdentity`, one UTC
`ReceivedAt` timestamp, and the closed basis that supplied that timestamp.

The preferred basis is the Package Details leaf's `created` field. The Catalog
specification defines it as when the package was first received by the package
source and names `published` as its fallback property. When `created` is absent,
the result therefore uses the required `published` field and records
`PublishedFallback` rather than presenting the two fields as equivalent.

This is source-receipt evidence. It is not publisher intent, semantic-version
meaning, a Catalog commit time, an advisory time, or proof of why the package
was released. In particular, `published` is the time when a package was last
listed and is a year-1900 sentinel for an unlisted package on nuget.org.
Consumers retain the basis and decide whether fallback evidence is suitable
for their own claim.

Enrichment does not rename the input event. The event remains **snapshot
observed**; a later reflow, relist, metadata update, or vulnerability update can
produce another Details event carrying the same package-receipt time.

## Acquisition and admission

The Catalog capability accepts only a non-null Details event issued by that
same runtime client. A Delete event or an event issued by another client is a
caller contract violation rejected before network work.

The client fetches only the event's retained, advertised leaf URL. It does not
synthesize a leaf URL, rescan the Catalog, or query Registration. Credentials
follow the existing same-origin rule relative to the configured service index;
the configured desktop or browser transport applies destination policy to the
leaf request.

One complete leaf is admitted atomically only when:

- the root is an object whose `@type` includes `PackageDetails`;
- `id` and normalized `version` equal the event coordinate;
- `catalog:commitId` and `catalog:commitTimeStamp` equal the event;
- required `published` and optional `created` values are complete ISO 8601
  timestamps with an explicit UTC designator or numeric offset; and
- no object at any depth contains duplicate properties.

Accepted timestamps are normalized to UTC. Unknown optional properties are
ignored. Malformed text, timestamp, coordinate, kind, or correspondence is a
typed invalid response; it never produces partial evidence.

## Bounds, failure, and portability

One enrichment is one logical NuGet operation. It inherits the source's
existing retry policy, per-response metadata-byte limit, request deadline,
operation deadline, caller cancellation, destination policy, and
authentication handling. The eventual report owns the finite number of events
it chooses to enrich; this owner does not add a hidden multi-event batch or
independent work budget.

A missing retained leaf is an invalid source response rather than proof that
the package coordinate is absent. Authentication, timeout, response rejection,
transport failure, and invalid metadata remain distinct typed failures carrying
the event coordinate and source-result identity. Caller cancellation
propagates as cancellation.

The implementation uses `HttpClient`, `System.Text.Json`, and existing
NuGetFetch abstractions only. It introduces no platform-specific API,
reflection, inspected-assembly loading, or synchronous blocking and therefore
inherits NuGetFetch's desktop and browser/Wasm compatibility contract.

## Evidence and non-claims

The focused Release suite gates:

- a nuget.org-shaped real Package Details leaf and its `created` receipt time;
- the specification-defined `published` fallback and explicit basis;
- exact package, version, kind, commit ID, and commit-time correspondence;
- duplicate properties, malformed values, and over-bound response bodies;
- no credential forwarding to an off-origin advertised leaf;
- typed missing-leaf and deadline failures plus caller cancellation; and
- pre-network rejection of Delete and foreign-client events.

The real shape is grounded in the documented nuget.org Catalog resource and
the live `Util.Biz.Payments@0.0.4-preview` Package Details leaf. Tests remain
hermetic and preserve only the fields required by this contract.

The suite does not claim that every feed publishes Catalog, every Details leaf
has `created`, source receipt equals publisher release intent, or every
fallback timestamp is suitable security-release evidence. Those are outside
this owner's claim.

[catalog-spec]: https://learn.microsoft.com/nuget/api/catalog-resource
