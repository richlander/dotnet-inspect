# Source availability audit

Status: Implemented existing contract

## Decision

`SourceAvailabilityService` owns whether one source-document census has
sufficient current or reusable final-origin evidence to classify each compiler
source observation as embedded, accessible, or missing for the current
availability audit.

The audit is an operation-scoped reachability fold. It does not prove that a
file is absent from its repository or that reachable content matches the
compiler-recorded checksum.

This document specifies an existing host-neutral service. It introduces no new
capability, shared substrate, host, or rendering path. The CLI library and
package SourceLink views are its current consumers. A browser/Wasm host may use
the same service without supplying a persistent cache.

## Owners and boundaries

The service consumes, but does not redefine, these owner-issued facts:

- [Source finding producers](source-finding-producers.md) owns the
  source-document census and its canonical paths, storage kinds, and resolved
  URLs.
- `SourceLinkUrls` owns immutable-content recognition, and
  `SourceFetchOriginValidator` applies the SourceLink provenance rule for
  admitting a final response origin.
- `HttpRetryHelper` owns the bounded, retried HTTP HEAD operation and its
  transport result.
- `DotnetInspector.Cache.PersistentCache` owns generic persistence, age checks,
  and storage mechanics.
- `ISourceLinkQueryCache` is the optional host adapter for those mechanics.
- `SourceAvailabilityQuery` owns query composition and the typed result.
- CLI library and package sections own presentation.

`SourceIntegrityService` is adjacent but outside this decision. It separately
owns content acquisition and checksum verification.

## Census and denominator

The input is one completed source-document census. The audit considers only
compiler-language observations: C#, Visual Basic, and F# documents selected by
their canonical file extension.

Each considered observation remains in the denominator even when another
observation resolves to the same URL. Cache reuse across audits does not
collapse compiler documents or their report paths.

An empty considered census produces `AllSourcesAccessible = false`. The result
does not turn lack of evidence into success.

## Classification

The service classifies each considered observation in this order:

1. Embedded storage is accessible without cache or network work.
2. No resolved URL is missing.
3. A non-HTTP(S) URL is missing.
4. An authored document path beneath `/artifacts/obj/` is missing. The authored
   path identifies where the compiler consumed the build-intermediate file;
   the canonical SourceLink remainder is repository identity and does not
   rewrite that compiler observation.
5. An admitted reusable positive observation is accessible.
6. Otherwise, the service performs the owned HEAD operation.
7. Cancellation observed after HEAD completion propagates before response
   classification or cache publication.
8. A successful response is accessible only when
   `SourceFetchOriginValidator.Validate` admits its final origin.
9. Every other result is missing for this operation.

Missing paths are reported using the authored document path in ordinal order.
Embedded, accessible, and missing counts are counts of considered observations.
`AllSourcesAccessible` is true only when at least one observation was
considered and none was missing.

Operational diagnostics must not include package-authored URLs or paths. The
authored paths that consumers may present remain separately typed in
`MissingSourceFiles`.

## Current observation and final origin

A live success is not sufficient by itself. For a URL with an attributable
repository origin, the final response URL must preserve the complete origin
tuple. An unattributed requested URL carries no repository provenance claim
and remains admissible under `SourceLinkProvenance`'s rule.

A changed or unattributable final origin for an attributed request is missing
for the current operation and cannot publish reusable evidence.

Non-success HTTP results do not currently carry the final response URL across
the `HttpRetryHelper` boundary. They therefore cannot establish a durable
negative observation. A 404 is still missing for the current operation, but it
is not evidence that a later audit may reuse.

## Cache subject and reuse

The cache subject is:

```text
(source-audit-v2, resolved URL, positive observation)
```

The literal resolved URL is the key because reachability is a property of that
HTTP resource. Compiler document identity remains separate and is used for
counting and reporting.

The service may read or publish only the `ok` positive extension. The stored
value is an opaque presence marker; it carries no user-visible fact.

Positive reuse follows provenance mutability:

- When `SourceLinkUrls.IsImmutable` establishes a full commit-pinned content
  origin, the observation has no age limit.
- Every other positive observation has a maximum age of one day.

Only a live successful response whose final origin is admitted may publish an
`ok` observation. Embedded, rejected, unsupported, build-intermediate,
non-success, and transport-failure paths publish nothing. Cancellation
observed before response classification also publishes nothing.

The category and `ok` extension bind reusable entries to this decision's
final-origin admission. An entry from another category or extension cannot
satisfy this audit.

## Failure and cancellation

Malformed or unsupported resolved URLs remain typed missing observations; they
do not abort the census.

HTTP and transport failures remain visible as missing observations and in the
aggregate summary. They are not converted into durable misses.

The caller's cancellation token flows into the owned HTTP operation.
Cancellation observed before response classification propagates, is not
cacheable evidence, and is not translated into a reusable availability result.

## Concurrency and ordering

The service performs uncached HEAD operations with a maximum concurrency of 16.
Completion order does not affect the result: missing authored paths are sorted
ordinally, and all counts are census totals.

Cache reuse is per resolved URL across audits. Each compiler observation still
contributes independently to the result counts.

## Pathological cases

The contract-defining cases are:

- an empty or non-compiler census is not all-accessible;
- embedded source uses neither cache nor network;
- unsupported, unresolved, and authored build-intermediate observations use
  neither cache nor network, including when SourceLink gives the latter a
  non-artifact canonical remainder;
- an immutable positive observation is reused without expiry;
- a mutable positive observation is reused for at most one day;
- a 404 and every other non-success remain operation-local;
- cancellation observed as a HEAD completes publishes no positive entry;
- an attributed request redirected to a different origin is missing and
  publishes no cache entry; and
- duplicate URLs retain per-document counts and use the same cross-audit cache
  subject.

## Required gates

`SourceLinkQueryServiceTests` must gate:

- mixed embedded, reachable, missing, and ignored observations;
- empty and duplicate-URL censuses retain the explicit denominator semantics;
- positive cache category, URL key, extension, mutability-based age, and reuse;
- absence of negative publication for 404 and other non-success responses;
- cancellation between HEAD completion and classification publishes nothing;
- absence of publication after final-origin rejection; and
- absence of cache and network work for locally classified observations.

`SourceLinkProvenanceTests` owns immutable URL recognition and final-origin
admission. `HttpRetryHelperTests` owns retry and cancellation behavior. This
service consumes those gates rather than duplicating their internal matrices.

## Non-claims

This decision does not claim:

- source content integrity, checksum agreement, or repository completeness;
- permanent absence after any HTTP result;
- provenance for URLs outside the recognized origin grammars;
- freshness beyond the stated positive reuse policy;
- stable snapshots of local files or remote repositories; or
- parallel or exhaustive source acquisition.
