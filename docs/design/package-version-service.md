# Package Version Service

## Status, owner, and claim

This document is the normative owner for `PackageVersionService`, the
component that joins the persistent cache and the package-source fetch clients
under the [consistency principles](version-resolution.md#consistency-principles)
of Package Version Selection. It is the design step for
[#8285](https://github.com/richlander/dotnet-inspect/issues/8285) changes 2
and 3; adoption by each consumer is a separate slice.

Given one `PackageVersionSelectionRequest`, its declared discovery freshness,
and the authorized sources for its package, the service returns one
`PackageVersionDiscoveryResult` for selection, together with the freshness
that result actually has: current, refreshed for this request, or a prior
resolution served after a refresh could not complete. It is the only place
that decides whether a request is answered from a prior resolution, from
discovery, or from both, and therefore the only place that makes `Name` and
`Name@latest` behave differently.

The service defines:

- the prior-resolution store, its key, and its freshness evidence;
- the decision between serving a prior resolution and discovering, driven by
  the request's declared freshness requirement;
- the refresh budget, serve-prior-on-failure behavior, expiry jitter, and the
  per-invocation refresh cap; and
- the freshness value stamped on the receipt, including the new served-prior
  value.

It consumes, and does not redefine, the request family and receipt of
[version resolution](version-resolution.md), the read/write cache paths of
`DotnetInspector.Cache.PersistentCache`, and `IPackageSourceClient` version
discovery from the [Package Source Model](package-source-model.md). Source
authorization, source-result adoption, selection semantics
(`PackageVersionSelectionResolver`), and payload acquisition stay with their
owners.

## Basis

Two code paths resolve unbound requests today and disagree. The migrated
`PackageHouse` selecting demand discovers on every request and stamps
`RefreshedForRequest`, so `LatestStable`, whose declared requirement is
`Current`, is served exactly like `AlwaysLatest`. The legacy `PackageExtractor`
path keeps a one-hour latest-version cache under the `versions-v5` category,
and when it has expired and local payloads exist it bounds discovery to one
second and fails on timeout rather than using the expired entry. Neither path
honors the principle that a prior resolution is preferred over blocking, and
the one-second budget satisfies neither principle: it blocks, then discards
the answer it was protecting.

The join point is `DotnetInspector.Packages`. `DotnetInspector.Cache` and
`NuGetFetch` do not reference each other, and `Packages` already references
both and hosts both paths.

## Contract

### Inputs

- A `PackageVersionSelectionRequest`. Its `Freshness` is the requirement:
  `Current` permits a prior resolution; `RefreshedForRequest` (only
  `AlwaysLatest`) forbids one.
- A `PackageSourceAuthorization` for the package, issued by the Package
  Source Model. The service discovers only through authorized sources.
- A `PackageVersionDiscoveryContract` (prerelease, unlisted, limit), owned by
  version resolution.
- An operation context carrying the caller's cancellation and timeouts, and
  an invocation-scoped refresh budget (below).

### Prior-resolution store

One store, adopted from the legacy path's `versions-v5` latest entries rather
than duplicated. The key is the source key, the normalized package ID, and the
prerelease policy. The value is the resolved version. The freshness evidence
is the entry's write time; no separate expiry file exists. Listing entries
(`listings:` keys) are outside this owner and remain with the legacy listing
paths until they adopt the service.

### Decision

| Requirement | Prior entry | Result |
| --- | --- | --- |
| `RefreshedForRequest` | any | discover; on failure, fail visibly; stamp `RefreshedForRequest` |
| `Current` | none | discover under the ordinary operation timeout; on failure, fail visibly; stamp `RefreshedForRequest` |
| `Current` | within its window | serve it without discovery; stamp `Current` |
| `Current` | past its window | serve it and refresh within the refresh budget; on success stamp `RefreshedForRequest`; on failure or budget exhaustion serve the prior and stamp `ServedPrior` with the entry's age |

The window is the base TTL adjusted by a deterministic jitter derived from a
hash of the key, within ±20 percent, so entries written together do not expire
together. The base TTL remains one hour until a consumer's design changes it.

The refresh budget is per invocation: at most N stale entries are refreshed
synchronously (initial N is 8), each under a per-refresh time bound, and the
remainder are served as `ServedPrior`. A consumer that must refresh everything
uses `AlwaysLatest`, or the request-level always-check modifier that maps
every unbound coordinate in the invocation to `AlwaysLatest`.

A served prior resolution is never labeled `Current`. `Current` means the
entry is inside its window; `ServedPrior` means a refresh was attempted or
skipped and the prior answer was used anyway.

### Receipt

`PackageVersionDiscoveryFreshness` gains `ServedPrior`. The receipt retains
the freshness value and, for `ServedPrior`, the entry's age. Hosts disclose
`ServedPrior` as one warning per invocation naming the count of packages
served from prior resolutions; the per-package detail is available at
diagnostic verbosity. The resolved version is disclosed in the result as
today.

### Failure

Discovery failures keep their Package Source Model shape. The service adds no
failure kind; it only decides whether a failure is terminal (no prior, or
`RefreshedForRequest`) or absorbed into `ServedPrior`. Offline mode is a
consumer policy: with `DOTNET_INSPECT_OFFLINE`, every `Current` request with a
prior entry is `ServedPrior` regardless of age, and every request without one
fails visibly as today.

## Pathological cases and gates

All run in the CLI suite's Release configuration through the source-scoped
feed harness that already fakes feeds, refusals, and slow responses:

1. Expired entry, refusing source: served prior, one warning, exit 0,
   `ServedPrior` on the receipt.
2. Expired entry, slow source beyond the per-refresh bound: served prior;
   the entry's age is reported.
3. Expired entry, fast source: refreshed; `RefreshedForRequest`; the entry is
   rewritten.
4. No entry, failing source: visible failure, unchanged from today.
5. `AlwaysLatest` with a fresh entry: discovery still happens; a refusing
   source fails visibly.
6. Jitter determinism: the same key yields the same window across processes.
7. Refresh cap: with more stale entries than N, exactly N discoveries occur
   and the rest are `ServedPrior`.
8. `Current` label honesty: no path stamps `Current` on an answer whose entry
   is past its window.

Motivating asset: the 44-member Microsoft.Extensions package set through
`find IChatClient --extensions`, measured at 4.2 s on the hourly refresh
before this design, with the target under one second warm and no cliff.

## Adoption

1. This document and the service with its contract suite (public consumer,
   no friend access), including the `ServedPrior` receipt value.
2. `PackageHouse` selecting demands adopt it. This is the primary CLI path;
   `Name` and `Name@latest` become observable there. Corrective but breaking:
   `Name` stops discovering on every request.
3. `PackageExtractor` latest resolution adopts it for `find`, the search
   scopes, and unversioned `AssemblySetRequest` acquisition; its private
   TTL check, `CachedVersionResolutionTimeout`, and cached-version error path
   retire. Corrective but breaking: the one-second timeout error becomes a
   served prior with a warning.
4. The request-level always-check modifier (#8285 change 3) maps every
   unbound coordinate to `AlwaysLatest` through this service.
5. #8271's default population consumes it with shipped advertised versions,
   so first use is bound and later use is `Current` or `ServedPrior`.

Browser/Wasm: Inspect Web binds at advertisement and does not resolve
unbound coordinates through this path today; adoption there is a separate
slice if Spotlight ever offers an unbound open.

## Non-claims

This design does not:

- change selection semantics, listing rules, or range and wildcard behavior;
- promise set-level coherence across coordinates;
- change payload caching or acquisition;
- define the always-check modifier's spelling; or
- define TTL values beyond keeping the current one-hour base until a consumer
  changes it.
