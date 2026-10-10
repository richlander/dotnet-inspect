# Package cache policy

## Status, owner, and claim

This document is the normative owner for **which remote package archives are
cached durably and which are read by range**. It is the third slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day.

The claim has three parts:

- **Durable identity for credential-free HTTP authorities.** An HTTP
  authority with no configured credential gets a persistent cache key, so its
  complete payloads are published to, and later served from, the durable
  authority-scoped store. This is the one claim this document transfers into
  the [package source model](package-source-model.md#candidate-and-payload-stores),
  which keeps every other store rule.
- **Size first, for every package.** A consumer that asks for ranged access
  learns the archive's size before transferring it: an archive at or under
  the size cut is acquired complete and cached; a larger one is read by
  range. Platform packs follow the same rule; reference and runtime packs are
  both above the cut and are read by range, with the entry cache keeping each
  assembly a query uses. The CLI and Inspect Web apply the same rule.
- **An entry cache for ranged reads.** Every entry a ranged read materializes
  is kept durably, with the archive's directory, so a later read of the same
  entries costs no request and a read needing another entry costs only an
  ordinary ranged read of the entries it lacks.

It consumes, and does not redefine, the ranged read of
[package archive range access](package-archive-range-access.md), the lease
step and ranged content of the
[package source model](package-source-model.md#ranged-payload-realization),
the transactional publication and single-flight of
[cache concurrency and publication](cache-concurrency.md), and the payload
limits of [package payload capacity](package-payload-capacity.md).

## Basis

Measured 2026-09-23 on the motivating asset, `find .InvalidateMeasure
--package Avalonia@12.1.2 --tfm net10.0` against nuget.org, native linux-x64
builds, medians of eight cold/warm pairs:

| Build | Cold | Warm | Requests | Bytes received |
| --- | --- | --- | --- | --- |
| 0.26.0 | 0.73 s | 0.29 s | 1 | 10.2 MB |
| ranged read (#8415) | 0.57 s | 0.57 s | 8 | 3.9 MB |

The ranged read wins cold and loses warm, because nothing it reads is kept
and, at #8415's head, HTTP authorities have no durable store: every
invocation repeats it. On that link the complete 10 MB archive arrives in
0.4 s and is a 0.28 s cache hit afterwards.

A design that read by range and then downloaded the complete archive in the
background was reviewed and rejected
([#8442](https://github.com/richlander/dotnet-inspect/pull/8442), round 1).
On a fast link it exits no sooner than a complete download: the ranged read
and then the download, about 0.8 s against 0.73 s. On a slow link the
download does not finish before exit, so it restarts from zero in every
invocation. It also needs cross-process mutable state for its wanted-list.
Choosing by size before transferring takes one of the two paths per
archive, with no background work. Learning the size costs a probe: the step
reads the complete response's advertised length and abandons that response
for an archive over the cut, which costs one more round trip and, over
HTTP/1.1, a new connection for the ranged read that follows. The tables below
measure the ranged read alone, so above the cut its cold cost is one round
trip higher than they show.

A ranged read costs about one more round trip than a complete download and
saves every byte it does not select, so where the two cross depends on the
link. Measured 2026-09-23 on 39 nuget.org packages
from 0.35 to 17.3 MB, each three times, complete download against a ranged
read of the highest target framework's assemblies; the three slower links
are modeled from each package's measured requests and bytes:

| Archive size | Ranged wins, measured here (about 40–60 MB/s) | 100 Mbps, 30 ms | 25 Mbps, 50 ms | 10 Mbps, 80 ms |
| --- | --- | --- | --- | --- |
| under 1 MB | 4 of 12 | 0 of 12 | 7 of 12 | 11 of 12 |
| 1–2 MB | 4 of 6 | 5 of 6 | 6 of 6 | 6 of 6 |
| 2–4 MB | 2 of 7 | 7 of 7 | 7 of 7 | 7 of 7 |
| 4–8 MB | 5 of 7 | 7 of 7 | 7 of 7 | 7 of 7 |
| 8–20 MB | 7 of 7 | 7 of 7 | 7 of 7 | 7 of 7 |

The extra round trip is worth 0.38 MB of transfer at 100 Mbps and 0.10 MB
at 10 Mbps. Fetching all 39 packages cold costs 15.0 s complete against
7.3 s ranged at 100 Mbps, and 132.4 s against 38.9 s at 10 Mbps.

The 1 MB cut is the operator's choice (Rich, 2026-09-23), not a universal
break-even. It is close to the break-even of the 100 Mbps model. On the
faster measured link, 9 of the 27 packages above it lost to a complete
download, by tens of milliseconds, among them the .NET Core 3.1 reference
pack and `Microsoft.NETCore.App` 2.2.0, whose selection is most of the
archive. On the slower modeled links the ranged read wins most packages
under the cut too, at 10 Mbps 11 of 12. Below the cut the choice favors
keeping the whole archive: a complete archive holds every file
(documentation XML, the manifest, other frameworks) for later commands.
Adoption watches reference packs for an observable cost.

Package sizes across one developer's NuGet global packages folder (1,696
packages, 4.5 GB): median 0.2 MB, 90th percentile 7.0 MB, 99th 40.1 MB,
largest 58 MB. Packages at or under 1 MB are 74.8% of packages and 6.7% of
bytes.

Runtime packs are the clearest case. Measured 2026-09-23 against nuget.org
with the ranged reader's request plan, medians of five runs:

| Pack | Archive | Complete | One assembly by range | All managed assemblies by range |
| --- | --- | --- | --- | --- |
| `Microsoft.NETCore.App.Runtime.linux-x64` 10.0.0 | 39.9 MB | 1,102 ms | 71 ms, 0.9 MB | 681 ms, 28.3 MB |
| `Microsoft.NETCore.App.Runtime.win-x64` 9.0.9 | 39.0 MB | 855 ms | 78 ms, 0.9 MB | 411 ms, 27.7 MB |
| `Microsoft.AspNetCore.App.Runtime.linux-x64` 9.0.9 | 12.5 MB | 244 ms | 66 ms, 0.9 MB | 260 ms, 12.5 MB |

Each ranged read took two requests: the managed assemblies are contiguous,
so even all of them are one span. Queries against a runtime pack keep coming
back to the same few assemblies, so keeping those assemblies costs a
fraction of the pack and makes later reads local.

## Contract

### Durable identity for credential-free HTTP authorities

An HTTP authority whose source carries no configured credential and whose
endpoint carries no user information receives a persistent cache key: a
digest of its canonical endpoint as the runtime authority key spells it,
including path, query, and fragment, in the same versioned key namespace
local authorities use. Query-distinct and path-distinct endpoints stay
distinct authorities with distinct entries, and no endpoint text is written
to a path. Authorities with a configured credential keep process-local
storage only.

Two kinds of credential-free feed deserve naming. A feed authenticated by a
credential plugin is credential-free by this rule: its cached packages stay
readable after the plugin would refuse a new request, as in NuGet's own
global packages folder. A feed that authenticates with a token in its
endpoint query is also credential-free; its entries are keyed by the
endpoint including the token, so rotating the token starts a new authority
and orphans the old entries. Package versions are immutable, and a same-user
cache is outside the
[threat model](untrusted-data-threat-model.md#trust-boundaries).

The key is a property of the authority, so every consumer that reads it
changes for credential-free HTTP authorities, not only the ranged consumer:

| Consumer | Change |
| --- | --- |
| Authority-scoped payload store (package, diff history, depends, Package Query, search, and platform acquisition) | complete payloads publish to `package-authority-content-v1` and are served from it on later invocations, at any size |
| Package index cache | derived package indexes persist under the authority key |
| Dependency-graph JSON evidence | the authority's `persistentCacheKey` field carries the key instead of `null` |
| Extraction result `CacheScopeKey` | carries the key instead of `null` |
| Package Version Service priors | kept on the endpoint: the service's source identity must check for an HTTP authority before the persistent key, which it reads first today, so the identity stays the endpoint its [store key](package-version-service.md) defines and existing priors stay valid |

Platform packs acquired through the authority-scoped store become durably
cached by this rule. Whether they are acquired complete or by range is the
size-first rule below.

### Size first

A consumer that sets ranged access consults every authorized complete store
first, then every authorized entry cache, in the existing authority order.
Either can answer without a request: a complete payload always, and the entry
cache when it holds the archive's directory and every selected entry. A
cached directory also records the archive's total length, so an archive the
entry cache knows needs no size probe: the step goes straight to the ranged
read for its missing entries.

Otherwise the step starts the ordinary complete acquisition. When the
response advertises an archive length above the size cut, the step abandons
that response before reading its body and reads the archive by range instead,
as the
[ranged payload realization](package-source-model.md#ranged-payload-realization)
step does today, including its fallback to the complete fetch when the
ranged read fails. Otherwise, including when no length is advertised, the
complete acquisition proceeds and publishes to the authority's store.
Desktop package payload transports set `MaxResponseDrainSize` to zero.
Disposing an unread response closes its connection without draining its body;
fully consumed responses remain eligible for connection reuse. Abandonment
is ordinary disposal and does not read or depend on buffering timing.
Browser/Wasm uses the browser fetch transport, which has no configurable
HTTP response-drain policy; disposal follows browser cancellation behavior.
The desktop no-drain guarantee is not claimed for that host.

The size cut is 1 MB of archive and applies to every package, platform
packs included. An archive at or under it costs one request the first time
and none afterwards. An archive above it costs one abandoned request and the
ranged read the first time; afterwards, only requests for entries not yet in
the entry cache, and none when every selected entry is cached.

Production consumers inherit this package-owned size cut rather than choosing
their own. An exception requires a documented reason in this owning policy and
evidence that justifies the different acquisition tradeoff. Selecting fewer
entries, or requesting only directory facts, does not itself justify overriding
the cut. Fixture overrides may exercise both sides of the policy.

The search Root's coverage fallback is sequential: it takes the complete
archive only after the ranged read returns, so the two transfers never
overlap.

### The entry cache

A ranged read from an authority with a persistent key publishes what it
fetched to a durable entry cache, keyed like the complete store by authority
key and exact coordinate, in its own versioned cache family,
`package-authority-entries-v1`, registered for
[versioned retirement](cache-concurrency.md#versioned-cache-retirement):

- **The directory.** The archive's directory region, meaning the central
  directory and the end records exactly as read, and the archive's total
  length, published once.
- **Each materialized entry.** Its expanded bytes, published under a file
  name that is the lowercase hexadecimal SHA-256 digest of its exact archive
  path bytes, so it is the same on case-sensitive and case-insensitive
  filesystems. The archive path is
  never used as a filesystem path: an entry name is untrusted package input
  and reaches the disk only as data, as the
  [threat model's archive rule](untrusted-data-threat-model.md#package-archives-use-traversal-aware-extraction)
  requires. Names with `..`, rooted names, device names, and names that
  differ only by case therefore get distinct, contained files, and a read
  looks an entry up through the cached directory, never by name on disk.

Each item is immutable and published by the same atomic rename that
[cache concurrency](cache-concurrency.md) uses, so concurrent invocations
converge on one copy without locks.

A later ranged read of the coordinate consults the entry cache first. Each
selected entry that is cached is read from disk and checked against the
cached directory's declared length and CRC. When every selected entry is
present and valid, the read makes no request.

An invalid item, meaning an entry that fails its length or CRC check or a
cached directory that cannot be read, is never deleted or replaced, because
[cache concurrency](cache-concurrency.md#crash-and-failure-boundaries)
preserves invalid final entries rather than racing a concurrent winner.
Instead the step reports it in verbose output and takes the complete fetch,
the same rule as a changed archive below. The complete payload publishes to
the authority-scoped store, which answers before the entry cache from then
on, so the invalid item is never consulted again.

When some are missing, the step reopens the archive using its cached,
validated directory and fetches only missing entries. Remote feeds are assumed
immutable for an exact package ID/version on an authorized source (operator
direction, Rich, 2026-10-09). Moving to another operation does not require
revalidation: the cache already relies on the same assumption when every entry
is present. Cached directory evidence remains bound to its authority-scoped
store and exact coordinate. Neither a display name nor a bare coordinate
permits reuse across sources.

The range capability consumes the existing validated `ZipDirectory`, checks
its region under the current bounds, and opens a fresh reader with the new
operation context, deadline, cancellation and resolved credentials. The
[range-access owner](package-archive-range-access.md#reopening-with-retained-directory-evidence)
owns that mechanism. This policy retains no live reader or old operation lease
and adds no directory cache. The existing entry store owns the retained bytes.

Every newly fetched entry retains the existing HTTP response, local-header,
expanded-size and CRC checks. A refused or invalid ranged read keeps its
existing typed complete fallback. A changed total length is still refused;
feed republishing is outside the immutable-feed contract and is not promised
to be detected by a fresh directory comparison. Invalid cached directory or
entry bytes remain visible and retain the existing cache-bypass policy.

The motivating asset is `Newtonsoft.Json@13.0.4`, `net6.0`: the website Summary
already retains its directory. Reopening for its exact Library previously cost
another 65,557-byte tail before a 275,871-byte DLL span. Reuse removes that tail,
so Summary plus Overview costs four archive requests and 343,246 consumed
body bytes instead of five and 408,803. API and Enablements retain the shared
acquisition introduced under semantic content demand. The production adopter
is the common Package acquisition step, so Browser/Wasm and CLI entry-cache
reads receive the same behavior. Focused gates cover a missing cached entry,
invalid cached data, authority isolation, new operation cancellation, and
unchanged complete-download fallback. NativeAOT and browser comparisons retain
full result parity for Avalonia, Avalonia.Base, Roslyn, Newtonsoft and Dapper.

Because the directory is cached, a later read knows which of the entries its
demand needs are cached and which are missing without a request. Ranged reads
fetch whole folders, as [package read demand](package-read-demand.md) owns,
so a cached folder is complete.

### Immutable-feed directory reuse evidence, 2026-10-09

The comparison uses effective base `7105fb3680272fdea577dbd5c913cf14e0ba620f`
and code head `79e9bbd497254b3a0c411acfa6048513dd99a08c`. The base already
exposes reopening with retained directory evidence but does not adopt it.
This isolates the common acquisition policy change from the earlier shared
API/Enablements demand change. The decimal 1,000,000-byte complete-download
cutoff is unchanged.

Both hosts execute Package Summary followed by concurrent exact Library API
and Enablements, with shared demand enabled on both sides. Each scenario has
one discarded cold/warm pair followed by seven alternating before/after pairs.
Every pair preserves full Summary, API and Enablements output. Each selects
one Library; Roslyn preserves its existing explicit retained-text projection
limit rather than claiming a complete API. Other APIs remain available and
complete. Warm runs make no package requests.

NativeAOT uses SDK `11.0.100-rc.1.26425.128`, Release, Linux x64, on an AMD
Ryzen 9 9900X with Linux `7.0.0-38-generic`. Both apphosts are published with:

```sh
dotnet publish eng/measure-inspect-web-library-open.cs -c Release \
  -p:IsPublishable=true \
  -p:DefineConstants=WEB_PACKAGE_ENTRY_CACHE%3BWEB_LIBRARY_SHARED_DEMAND \
  -o <before-or-after-directory>
<apphost> <package> <version> <framework> overview [asset-id]
```

The before apphost SHA-256 is
`7c277f87b534725fab96fa957828f594ae773d1ce2783bf1e85455c5d6fd91b0`;
the after identity is
`f9c3a253f01f3e8d6d793b84d12074a4b1603ac24c42fa9fd8fe1de98135893a`.
The harness uses the production exports and a bounded in-memory entry-store
adapter; it does not model Cache Storage I/O. Operation timing includes
acquisition, inspection and serialized results. Process wall timing additionally
includes startup and output consumption and is retained in the evidence.
No builds or tests ran during measurement. CDN latency remains uncontrolled.

| Pinned scenario | Cold median ms, before → after | Cold tail ms, before → after | Warm median ms, before → after | Warm tail ms, before → after |
| --- | --- | --- | --- | --- |
| Avalonia 12.1.3, net10.0, default facade | 232.5 → 187.7 | 299.5 → 244.6 | 0.6 → 0.6 | 0.7 → 0.6 |
| Avalonia 12.1.3, net10.0, `compile:ref/net10.0/Avalonia.Base.dll` | 466.4 → 435.8 | 496.8 → 469.5 | 147.8 → 146.1 | 148.1 → 150.1 |
| Microsoft.CodeAnalysis.CSharp 5.9.0, netstandard2.0 | 343.0 → 337.8 | 416.7 → 370.9 | 53.0 → 51.8 | 53.7 → 54.0 |
| Newtonsoft.Json 13.0.4, net6.0 | 253.4 → 228.1 | 270.4 → 268.1 | 22.2 → 22.5 | 27.8 → 23.4 |
| Dapper 2.1.66, net8.0 | 119.1 → 121.0 | 146.1 → 135.1 | 10.6 → 10.8 | 11.6 → 13.9 |

Tail means the largest of seven samples, also nearest-rank p95 at this sample
count. Small timing differences, especially Dapper's unchanged complete-fetch
path and Roslyn, do not establish a performance change.

The Release website comparison uses the production page-owned Worker and
Cache Storage in headless Firefox. Both sites are published with
`InspectWebIncludeFrontend=true`; Node 24.21.0 runs the server and probe:

```sh
node browser/benchmark-library-overview.ts <before-url> <after-url> \
  <output-directory> 7 before-shared-demand
```

These are host corroboration timings, not substitutes for NativeAOT evidence.
The initial page paint is outside this probe. Startup readiness and output
consumption wall time are recorded separately.

| Scenario | Cold median ms, before → after | Cold tail ms, before → after | Warm median ms, before → after | Warm tail ms, before → after | Cold package requests |
| --- | --- | --- | --- | --- | --- |
| Avalonia facade | 732 → 700 | 761 → 718 | 12 → 12 | 13 → 13 | 6 → 5 |
| Avalonia.Base | 3174 → 3181 | 3275 → 3211 | 1944 → 1965 | 1973 → 1980 | 6 → 5 |
| Roslyn | 1826 → 1797 | 1901 → 1863 | 685 → 678 | 696 → 687 | 5 → 4 |
| Newtonsoft | 1282 → 1234 | 1324 → 1282 | 363 → 363 | 377 → 370 | 5 → 4 |
| Dapper | 874 → 866 | 914 → 925 | 186 → 185 | 190 → 194 | 1 → 1 |

The consistent result is removal of one directory transfer for each ranged
package, not a universal latency improvement. Avalonia.Base's browser cold
median is nearly flat and its warm median differs despite both paths doing
zero network work. Resident images and output identities remain unchanged.

Owner-issued typed transfer receipts independently confirm the consumed body
bytes below. The Release browser trace supplies each actual URL, Range and
status; its evidence envelope is not enriched with these receipts. A separate
untrimmed diagnostic observer reads the retained acquisition receipts without
counting one retained receipt twice. Its timings are excluded. The
[retained evidence](../evidence/web-directory-reuse-2026-10-09.json) contains
all NativeAOT samples, all 140 browser samples and request traces, and the
typed receipts at Summary and Overview boundaries. Diagnostic output also
matches between sides.

| Scenario | Consumed bytes, before → after |
| --- | --- |
| Avalonia facade | 138,904 → 73,347 |
| Avalonia.Base | 1,786,944 → 1,721,387 |
| Roslyn | 2,914,735 → 2,849,178 |
| Newtonsoft | 408,803 → 343,246 |
| Dapper | 437,579 → 437,579 |

Newtonsoft's remaining requests are the abandoned size probe (zero consumed
body bytes), the initial directory tail (65,557), the nuspec span (1,818),
and the exact net6.0 DLL span (275,871). The second directory tail is gone.
That saves 65,557 consumed bytes, or 16.0%, and one of five requests. These
counts exclude service discovery and runtime assets. The unchanged complete
path for Dapper confirms that this policy change does not retune the cutoff.

Validation: Release solution build; 19 archive-source tests, 105 common ranged
acquisition tests, all 371 Browser engine boundary tests, frontend build and
lint. The CLI configured-acquisition suite passes 237/244: seven existing API
presentation assertions also fail on the parent, expecting package/version
headings in rendered API text. No candidate-only CLI failures were found.

A complete payload in the durable store answers before the entry cache, as
the cache-first rule already orders them. Authorities without a persistent
key keep ranged content in memory only, as they do today. The entry cache is
not a package cache entry: it never answers a complete acquisition, and a
consumer that needs an entry it lacks reads that entry by range.

## Host scope

Both hosts keep complete downloads, so size first is one package-owned rule
for both. The CLI publishes them to the durable authority-scoped store.
Inspect Web gets the same effect from the browser: nuget.org marks package
archives `Cache-Control: max-age=86400`, and Inspect Web's requests do not
opt out of the HTTP cache. A persistent Chromium profile, restarted between
sessions, measured this on 2026-09-23 for `Avalonia` 12.1.2:

| Session | Time | Bytes from the network |
| --- | --- | --- |
| first | 2,916 ms | 10.1 MB |
| second, after a restart | 69 ms | 0 |
| third, after a restart | 156 ms | 0 |

After a day the browser revalidates with `Last-Modified`, which costs a
request but not the archive. Ranged `206` responses do not get this reuse,
which is a further reason to cache small archives complete.

Ranged responses are not reused by the browser's HTTP cache, so Inspect Web
keeps its entry cache in an explicit browser store. That and longer retention
of complete archives belong to the Inspect Web slice:

- **Force the cache for archives.** A package version is immutable, so the
  archive request can use the `force-cache` fetch mode. The browser then
  serves a cached archive at any age without revalidating, and still evicts
  under disk pressure. Version listings change and keep the default mode.
- **An explicit browser package store.** Inspect Web uses Cache Storage,
  whose request/response key-value model matches immutable directory and
  expanded-entry payloads and is available in its dedicated Worker without a
  service worker. Cache Storage and the origin private file system share the
  same origin quota and eviction policy, so OPFS would not improve retention.
  Entries survive refresh and browser restart, but are best-effort until the
  browser grants origin persistence and can still be removed explicitly by
  the user. `navigator.storage.persist()` may be denied and is not available
  in a Worker; Inspect Web therefore reports whether its origin is persistent
  rather than claiming every profile is. These semantics follow the
  [Storage API quota and eviction model](https://developer.mozilla.org/docs/Web/API/Storage_API/Storage_quotas_and_eviction_criteria),
  [`CacheStorage` Worker contract](https://developer.mozilla.org/docs/Web/API/CacheStorage),
  and [`persist()` contract](https://developer.mozilla.org/docs/Web/API/StorageManager/persist).
  If Cache Storage cannot be opened, Inspect Web reports the storage failure
  and continues without the entry-cache optimization. If only the persistence
  status query fails, the cache remains available and is reported as
  best-effort.
  The explicit store is Inspect Web's counterpart to the CLI's
  authority-scoped durable store.

Inspect Web's browser requests also set `redirect: "error"` today, so a feed
that redirects package downloads, as Azure Artifacts does with a `303` to its
blob store, fails in the browser. That slice decides whether to follow such
redirects.

For archives above the cut, a browser probe on 2026-09-23 compared ranged
reads with complete fetches. Headless Chromium fetched from nuget.org, with a
fresh context per run and medians of seven runs. It replayed the ranged
reader's request plan: the tail, merged spans with a 64 KiB gap, and six
requests in flight.

| Package | Archive | Complete | Ranged, preflighted | Ranged, preflight-free | Bytes moved, ranged |
| --- | --- | --- | --- | --- | --- |
| `Avalonia` 12.1.2 | 10.1 MB | 551 ms | 583 ms | 352 ms | 3.8 MB |
| `Microsoft.CodeAnalysis.CSharp` 4.11.0 | 16.9 MB | 550 ms | 995 ms | 371 ms | 2.3 MB |
| `Microsoft.Data.SqlClient` 5.2.2 | 13.2 MB | 598 ms | 382 ms | 205 ms | 0.3 MB |

Ranged reads beat the complete fetch in the browser only when their requests
need no CORS preflight. Chromium preflights a suffix range and any request
carrying `If-Range`. It preflighted every span, because all six start before
the first preflight is cached. nuget.org also serves browsers over HTTP/1.1,
so each request in flight holds its own connection. A preflight-free read
sends only `bytes=a-b` ranges. It takes the tail's position from the complete
response's `Content-Length`, which size first has already read and nuget.org
exposes. It omits `If-Range` and compares each response's validator with the
first instead. The probe also found that nuget.org's `ETag` is unquoted, so an
`If-Range` carrying it always returns the whole archive; only `Last-Modified`
works as a validator there. How a browser sends ranged requests belongs to the
[range-access browser host](package-archive-range-access.md#browser-host)
rule, which its Inspect Web slice amends with these findings. The probe drove
JavaScript `fetch`, not the wasm engine. .NET's `HttpClient` on wasm uses the
same `fetch`, so the network behavior carries over, but wasm decompression
cost is unmeasured until that slice.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Credential-free HTTP authority | persistent key; query-distinct and path-distinct endpoints get distinct keys; the key contains no endpoint text | `PackageSourceAuthorization_CredentialFreeHttpAuthorityHasPersistentKey` and `..._PathSecretAuthoritiesHaveDistinctKeysWithoutEndpointText`; `AuthorityScopedPackageStoreTests.HttpAuthorities_WithEqualProducersKeepSeparateDurableSlots` |
| 2. Configured credential, or user information in the endpoint | no persistent key | `PackageSourceAuthorization_CredentialPathAuthoritiesHaveNoPersistentKey` |
| 3. Version-service priors for an HTTP authority | identity is still the endpoint | `PackageVersionServiceTests.Entry_RecordsTheSourceIdentityTheKeyUses` |
| 4. Search of an archive under the cut, then a second invocation | first: one complete request, published durably; second: cache hit, no package request | `PackageRangedRealizationTests.SizeFirst_ArchiveAtOrUnderTheCut_IsAcquiredComplete`, then `RangedRealize_CachedPayloadAnswersWithoutTransfer` |
| 5. Search of an archive over the cut, twice | first: one abandoned request, the ranged read, and the directory and entries published to the entry cache; second: no package request | `ConfiguredPayloadAcquisitionTests.SearchCommand_RangedRead_TransfersOnlyTheSelectedAssembly`, two invocations; `PackageRangedRealizationTests.EntryCache_WarmReadOfTheSameSelection_MakesNoRequest` |
| 5a. A second query needing one more entry of the same immutable archive | one missing-entry request; no size probe or directory transfer | `PackageRangedRealizationTests.EntryCache_WarmReadMissingAnEntry_ReadsOnlyThatEntry` |
| 5e. Entries named with `..`, a rooted path, and two names that differ only by case | each published inside the entry cache under its digest; all three read back to their own content | `PackageEntryStoreTests.EntryCache_HostileEntryNames_StayContainedAndDistinct` |
| 5b. A cached entry whose bytes no longer match the cached directory, or a cached directory that cannot be read | one complete transfer answers the read; later reads are served from the complete store; the invalid item is left in place; a verbose diagnostic names it | `PackageRangedRealizationTests.EntryCache_InvalidItem_TakesTheCompleteFetch`, for an entry and for the directory |
| 5c. A source violates immutability and returns a different archive length | the entry response is refused; complete fallback answers without publishing mixed cached entries | `PackageRangedRealizationTests.EntryCache_ChangedArchiveLength_TakesTheCompleteFetch` |
| 5d. An authority without a persistent key | nothing published to the entry cache | `PackageEntryStoreTests.EntryCache_AuthorityWithoutPersistentKey_KeepsNothing` |
| 6. No advertised length | complete acquisition | `PackageRangedRealizationTests.SizeFirst_NoAdvertisedLength_IsAcquiredComplete` |
| 7. A consumer other than the search Root | `package ID@VERSION` from a credential-free HTTP feed, twice: the second is a cache hit | `ConfiguredPayloadAcquisitionTests.ExtractPinnedPackage_CredentialFreeHttpPinIsDurable` |
| 8. Ranged content that does not cover the Root's selection, then the complete fallback | one complete transfer | unverified: the ranged selection is the Root's own selection expanded to folders, so no fixture reaches the fallback |
| 9. Two invocations acquiring the same coordinate | one publication | existing cache-concurrency gates |
| 10. Motivating assets | `Avalonia` 12.1.2: cold about the ranged read of #8415 plus the size probe, warm no package request. `Microsoft.NETCore.App.Runtime.linux-x64` 10.0.0, one assembly: cold about 0.1 s and 0.9 MB, warm no package request | `eng/measure-package-read-demand.sh` for `Avalonia`, a preserved probe as design evidence; the runtime pack is gated when platform packs adopt ranged access in step 4 |

## Adoption

1. This document, with the persistent-key rule recorded in the package
   source model as the target of step 2.
2. The persistent key for credential-free HTTP authorities, size first, and
   the entry cache in the lease step, adopted by the exact-package search Root
   that #8415 put on the ranged path; gates 1 to 9. This step changes every
   consumer in the table above, and updates the comment in
   `AuthorityScopedFileSystemPackageStore` that says no durable HTTP
   authority identity exists. The search Root's offline branch keeps its
   local package cache path at this step; answering offline from the
   authority-scoped store and the entry cache is a later step.
3. The remaining search scopes adopt ranged access, and with it size first,
   in the second part of range-access adoption step 2.
4. The [package-backed platform](package-backed-platform-realization.md)
   owner adopts ranged access for platform packs, so reference and runtime
   packs are read by range, and records whether reference packs show an
   observable cost against a complete download. In the same step, platform packs
   settle "latest" through the
   [Package Version Service](package-version-service.md) as other packages
   do. Both changes are claims of those owners, adopted under their designs.
5. The Inspect Web document viewer adopts ranged access, size first, and a
   Cache Storage-backed browser entry cache for root README and Skill Markdown
   requests, with preflight-free ranged requests. Other Inspect Web package
   operations retain their existing acquisition paths until their owning
   designs adopt ranged access.

## Non-claims

This document does not:

- download anything in the background or after the command's work;
- change the ranged read, its outcomes, or its fallback;
- change platform-pack acquisition or version selection, which their owners
  adopt in step 4;
- evict or bound the durable cache's total size, which remains the cache
  maintenance owner's.
