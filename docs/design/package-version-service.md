# Package Version Service

## Status, owner, and claim

This document is the normative owner for `PackageVersionService`, the
component that joins the persistent cache and the package-source fetch clients
under the [consistency principles](version-resolution.md#consistency-principles)
of Package Version Selection. It is the design step for
[#8285](https://github.com/richlander/dotnet-inspect/issues/8285) changes 2
and 3; adoption by each consumer is a separate slice.

Given one `PackageVersionSelectionRequest`, its declared discovery freshness,
and the `PackageSourceAuthorization` for its package, the service settles the
request either from discovery or from a **prior settlement** it retained for
the same request under the same authorization, and says which. It is the only
place that decides between a prior settlement and discovery, and therefore the
only place that makes `Name` and `Name@latest` behave differently.

The service defines:

- the prior-settlement store, its key, its value, and its freshness evidence;
- the decision between serving a prior settlement and discovering, driven by
  the request's declared freshness requirement;
- the refresh budget, serve-prior-on-failure behavior, expiry jitter, and the
  per-invocation refresh cap; and
- the eviction of a prior whose coordinate no longer acquires.

It consumes, and does not redefine, the request family and receipt of
[version resolution](version-resolution.md), the exact pinned-candidate path
and settlement receipts of [PackageHouse](package-house.md), the read/write
cache paths of `DotnetInspector.Cache.PersistentCache`, and `IPackageSourceClient`
version discovery from the [Package Source Model](package-source-model.md).
Source authorization, source-result adoption, selection semantics
(`PackageVersionSelectionResolver`), listing operations, and payload
acquisition stay with their owners.

Two claims transfer to version resolution, both described below: a new
receipt arm, `Prior`, and a new `PackageVersionDiscoveryFreshness` value,
`ServedPrior`, which only the `Prior` arm may carry. This document names
them; version resolution adopts them when the service is implemented. Two
bounded rules transfer to PackageHouse: its decision receipt accepts `Prior`
for a selecting demand, and when acquisition after a `Prior` decision ends in
`NotFound`, PackageHouse reports the coordinate to the service's eviction
entry point and appends a `Stage(Acquisition)` failure that names the
eviction. No other owner's contract changes.

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

A prior cannot be served by replaying discovery. A `Resolved` receipt requires
an authoritative `PackageVersionDiscoveryResult` whose candidates the current
source generation issued; a stored snapshot cannot issue them. What a prior
can be is what principle 1 already names: an advertised exact version that
the product accepts as bound. Serving a prior therefore uses the exact
pinned-candidate path, which authorizes one coordinate for the current
generation without enumerating peer versions, and reports through a receipt
arm that says no discovery was performed.

The join point is `DotnetInspector.Packages`. `DotnetInspector.Cache` and
`NuGetFetch` do not reference each other, and `Packages` already references
both and hosts both paths.

## Contract

### Inputs

- A `PackageVersionSelectionRequest`. Its `Freshness` is the requirement:
  `Current` permits a prior settlement; `RefreshedForRequest` (only
  `AlwaysLatest`) forbids one.
- A `PackageSourceAuthorization` for the package, issued by the Package
  Source Model. The service discovers only through, and serves priors only
  for, exactly this authorization.
- A `PackageVersionDiscoveryContract` (prerelease, unlisted, limit), owned by
  version resolution.
- An operation context carrying the caller's cancellation and timeouts, and
  an invocation-scoped refresh budget (below).

### Prior-settlement store

The store retains prior settlements, not discovery evidence.

- **Key:** the ordered set of authorized source keys; the normalized package
  ID; the request kind and its parameters (`LatestStable`,
  `LatestPrerelease`, `Wildcard` with its normalized prefix, `Range` with its
  range and address); and the discovery contract's prerelease and unlisted
  policy. A request under a different authorization or contract is a
  different key. There is no per-source entry and no aggregation rule: the
  authorization set is part of the identity.
- **Value:** the settled exact version and the source key that reported it.
- **Freshness evidence:** the entry's write time. No separate expiry file.
- **Category:** a new versioned `PersistentCache` category. The legacy
  `versions-v5` latest entries are read as seed priors for `LatestStable` and
  `LatestPrerelease` under a single-source authorization during step 3 of
  adoption, then retire with the legacy path. `listings:` entries are outside
  this owner and remain with the listing operations.

### Decision

| Requirement | Prior entry | Result |
| --- | --- | --- |
| `RefreshedForRequest` | any | discover; on failure, fail visibly; `Resolved` with `RefreshedForRequest` |
| `Current` | none | discover under the ordinary operation timeout; on failure, fail visibly; `Resolved` with `RefreshedForRequest` |
| `Current` | within its window | serve it without discovery; `Prior` with `Current` |
| `Current` | past its window | serve it and refresh within the refresh budget; on success `Resolved` with `RefreshedForRequest` and the entry rewritten; on failure or budget exhaustion `Prior` with `ServedPrior` and the entry's age |

Every successful discovery for a `Current`-requirement request writes or
rewrites the entry. Discovery for `AlwaysLatest` also rewrites the entry for
the corresponding `LatestStable` or `LatestPrerelease` key, so an explicit
refresh benefits later bare requests.

The window is the base TTL adjusted by a deterministic jitter derived from a
hash of the key, within ±20 percent, so entries written together do not expire
together. The base TTL remains one hour until a consumer's design changes it.

The refresh budget is per invocation: at most N past-window entries are
refreshed synchronously (initial N is 8), each under a per-refresh time bound,
and the remainder are served as `ServedPrior`. A consumer that must refresh
everything uses `AlwaysLatest`, or the request-level always-check modifier
that maps every unbound coordinate in the invocation to `AlwaysLatest`.

Offline mode is a consumer policy: with `DOTNET_INSPECT_OFFLINE`, every
`Current`-requirement request with a prior entry is `Prior` with `ServedPrior`
regardless of age, and every request without one fails visibly as today.

### Serving a prior

A served prior settles the selecting demand as an exact coordinate. The
service asks the current source generation for the pinned candidate of the
prior coordinate under the request's authorization; PackageHouse retains that
candidate and coordinate exactly as it does for an exact demand. Pinned
resolution checks authorization only and never reports absence, so a prior
whose coordinate has since disappeared from every authorized source is
detected only when acquisition fails, inside PackageHouse, after the `Prior`
receipt has been issued.

Under the Settle profile no acquisition runs, so a prior served there is a
settlement only and is never checked for existence; the check happens in
whichever later operation acquires the coordinate.

That failure stays visible, as any acquisition failure does: the request
fails now with the ordinary not-found result. Only PackageHouse observes that
failure, so PackageHouse is the component that evicts: on `NotFound` after a
`Prior` decision it reports the coordinate to the service's eviction entry
point and appends a `Stage(Acquisition)` failure stating that a retained
prior was evicted, so a caller can rerun at once and the next request settles
by discovery. Neither PackageHouse nor the service re-enters settlement within
the same demand. This is not
a bound request degrading: the binding was the product's own prior, not a
version the user supplied or accepted, and the user-visible outcome is one
visible failure followed by fresh discovery, never a silent substitution.

### Receipt

Version resolution gains one receipt arm, `Prior`. It retains the exact
request, the prior coordinate, the pinned candidate the current generation
issued for it, the freshness (`Current` or `ServedPrior`), and for
`ServedPrior` the entry's age. It retains no discovery result. PackageHouse's
decision receipt accepts `Prior` for a selecting demand when its coordinate and
candidate match the receipt, alongside the existing `Resolved` rule.

`PackageVersionDiscoveryFreshness` gains `ServedPrior`. Only the `Prior` arm
may carry it: version resolution's discovery-compatibility rule rejects
`ServedPrior` for `Resolved` and every other discovery-backed arm, and the
`Prior` arm accepts only `Current` or `ServedPrior`. A served prior is never
labeled `Current`: `Current` means the entry is inside its window;
`ServedPrior` means a refresh was attempted or skipped and the prior answer
was used anyway.

Hosts disclose `ServedPrior` as one warning per invocation naming the count
of packages served from prior settlements; the per-package detail, including
age, is available at diagnostic verbosity. The resolved version is disclosed
in the result as today.

### Failure

Discovery failures keep their Package Source Model shape. The service adds no
failure kind; it only decides whether a failure is terminal (no prior, or
`RefreshedForRequest`) or absorbed into `ServedPrior`.

## Pathological cases and gates

The service has a public-consumer contract suite with a fake
`IPackageSourceClient` that can refuse, delay, or answer, and a fake clock.
The CLI cases run through the source-scoped feed harness in
`SourceScopedRoutingTests`. Today that harness serves one package's versions
from one fake feed, returns a chosen refusal status for a refused source, can
demand authorization, records request URLs, and can write local feeds through
its test helpers. It cannot delay a response, serve several packages, or
backdate an entry, so timing, cap, and determinism cases stay in the contract
suite. Adoption step 1 adds one harness helper: seed a prior-settlement entry
for the new category with a controllable write time, so the in-window and
past-window preconditions of the CLI cases can be created. All gates run in
Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Past-window entry, refusing source | `Prior`/`ServedPrior`, one warning, exit 0 | CLI harness |
| 2. Past-window entry, source slower than the per-refresh bound | `Prior`/`ServedPrior` with age | contract suite (delay) |
| 3. Past-window entry, fast source | `Resolved`/`RefreshedForRequest`; entry rewritten | CLI harness |
| 4. No entry, failing source | visible failure, unchanged | CLI harness |
| 5. `AlwaysLatest` with an in-window entry | discovery happens; refusing source fails visibly; entry rewritten on success | CLI harness |
| 6. Jitter determinism | same key, same window across processes | contract suite |
| 7. Refresh cap | with more past-window entries than N, exactly N discoveries, the rest `ServedPrior` | contract suite (fake clock) |
| 8. `Current` label honesty | no path stamps `Current` on a past-window entry | contract suite |
| 9. Prior coordinate absent from every source | first request fails visibly with not-found and the eviction diagnostic; the entry is gone; the next request discovers and settles | CLI harness, two invocations (local feed with the version removed, then restored or replaced) |
| 10. Authorization change | a prior under one source set is a miss under another | contract suite |
| 11. `Wildcard` and `Range` keys | a `LatestPrerelease` prior is never served to a `Wildcard` or `Range` request | contract suite |

Motivating asset: the 44-member Microsoft.Extensions package set through
`find IChatClient --extensions`, measured at 4.2 s on the hourly refresh
before this design, with the target under one second warm and no cliff.

## Adoption

1. This document; the service with its contract suite, including its
   eviction entry point; the `Prior` receipt arm and `ServedPrior` freshness
   value adopted by version resolution, including the rule that only `Prior`
   carries `ServedPrior`; the decision-receipt rule extended in PackageHouse.
2. `PackageHouse` selecting demands adopt it for `LatestStable` and
   `LatestPrerelease`, together with the eviction hook and its
   `Stage(Acquisition)` failure on the not-found path, since this is the
   first point where priors are served and case 9 becomes runnable. This is
   the primary CLI path; `Name` and `Name@latest` become observable there.
   Corrective but breaking: `Name` stops discovering on every request.
   `Wildcard` and `Range` continue to discover until a later slice adopts
   their keys.
3. `PackageExtractor` latest resolution adopts it for `find`, the search
   scopes, and unversioned `AssemblySetRequest` acquisition; its private TTL
   check, `CachedVersionResolutionTimeout`, and cached-version error path
   retire, and the `versions-v5` latest entries are read as seeds then
   retired. Corrective but breaking: the one-second timeout error becomes a
   served prior with a warning.
4. The request-level always-check modifier (#8285 change 3) maps every
   unbound coordinate to `AlwaysLatest` through this service.
5. #8271's default population consumes it with shipped advertised versions
   as pre-seeded priors, so first use is bound and later use is `Current` or
   `ServedPrior`.

Browser/Wasm: Inspect Web binds at advertisement and does not resolve
unbound coordinates through this path today; adoption there is a separate
slice if Spotlight ever offers an unbound open.

## Non-claims

This design does not:

- change selection semantics, listing rules, or range and wildcard behavior;
- store or replay discovery evidence;
- promise set-level coherence across coordinates;
- change payload caching or acquisition;
- define the always-check modifier's spelling; or
- define TTL values beyond keeping the current one-hour base until a consumer
  changes it.
