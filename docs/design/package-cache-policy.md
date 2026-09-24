# Package cache policy

## Status, owner, and claim

This document is the normative owner for **which remote package archives are
cached durably and which are read by range**. It is the third slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day.

The claim has two parts:

- **Durable identity for credential-free HTTP authorities.** An HTTP
  authority with no configured credential gets a persistent cache key, so its
  complete payloads are published to, and later served from, the durable
  authority-scoped store. This is the one claim this document transfers into
  the [package source model](package-source-model.md#candidate-and-payload-stores),
  which keeps every other store rule.
- **Size first.** A consumer that asks for ranged access learns the archive's
  size before transferring it: an archive at or under the size cut is
  acquired complete and cached; a larger one is read by range and not cached.

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
Choosing by size before transferring gives each archive the cheaper of the
two paths, with no background work.

Package sizes across one developer's NuGet global packages folder (1,696
packages, 4.5 GB): median 0.2 MB, 90th percentile 7.0 MB, 95th 16.4 MB, 99th
40.1 MB, largest 58 MB. Packages under 12 MB are 93.1% of packages and 33.3%
of bytes; the packages above it are mostly native tool and runtime payloads
(`runtime.*.ilcompiler`, `*.linux-x64` tool packages, browser-wasm runtime
packs), where a query reads a small fraction of the archive.

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
| Package Version Service priors | unchanged: the service keeps identifying an HTTP authority by its endpoint, as its [store key](package-version-service.md) defines, so existing priors stay valid |

Platform packs are acquired complete by the
[package-backed platform](package-backed-platform-realization.md) owner
through the authority-scoped store, so they become durably cached at any
size by this rule and are never subject to the size cut.

### Size first

A consumer that sets ranged access starts the ordinary complete acquisition
from each authority in the existing order. When the response advertises an
archive length above the size cut, the step abandons that response before
reading its body and reads the archive by range instead, as the
[ranged payload realization](package-source-model.md#ranged-payload-realization)
step does today, including its fallback to the complete fetch when the
ranged read fails. Otherwise, including when no length is advertised, the
complete acquisition proceeds and publishes to the authority's store. A
cache hit answers first, as always, so a durably cached archive costs no
request.

The size cut is 12 MB of archive. An archive at or under it costs one
request the first time and none afterwards. An archive above it costs one
abandoned request plus the ranged read on every invocation, and nothing is
cached.

The complete acquisition joins the process-local single-flight for its
coordinate, so a consumer that falls back from ranged content to the
complete archive in the same invocation never runs two transfers of it at
once.

## Host scope

Size first is package-owned: the lease step decides, and any host that sets
ranged access gets it. The CLI search Root sets it today. Inspect Web keeps
packages in an in-memory store for one session; it adopts ranged access in
the range-access design's Inspect Web slice, where this rule decides between
a complete in-memory fetch and a ranged read by the same cut.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Credential-free HTTP authority | persistent key; query-distinct and path-distinct endpoints get distinct keys; the key contains no endpoint text | `PackageSourceAuthorization_CredentialFreeHttpAuthorityHasPersistentKey`, replacing `..._HttpAuthorityWithoutStableIdHasNoPersistentKey` |
| 2. Configured credential, or user information in the endpoint | no persistent key | `PackageSourceAuthorization_CredentialPathAuthoritiesHaveNoPersistentKey`, extended |
| 3. Version-service priors for an HTTP authority | identity is still the endpoint | `PackageVersionServiceTests`, extended |
| 4. Search of an archive under the cut, then a second invocation | first: one complete request, published durably; second: cache hit, no package request | CLI harness, two invocations |
| 5. Search of an archive over the cut, twice | each: one abandoned request and the ranged read; nothing published | CLI harness, two invocations |
| 6. No advertised length | complete acquisition | contract suite |
| 7. A consumer other than the search Root | `package ID@VERSION` from a credential-free HTTP feed, twice: the second is a cache hit | CLI harness, two invocations |
| 8. Ranged content that does not cover the Root's selection, then the complete fallback | one complete transfer | CLI harness |
| 9. Two invocations acquiring the same coordinate | one publication | existing cache-concurrency gates |
| 10. Motivating assets | `Avalonia` 12.1.2: cold about the 0.26.0 cold time, warm about the 0.26.0 warm time; one package over the cut read by range on every invocation | preserved probe as design evidence |

The existing assertion that an HTTP extraction result carries no
`CacheScopeKey` (`ConfiguredPayloadAcquisitionTests`) changes with case 1.

## Adoption

1. This document, with the persistent-key rule recorded in the package
   source model as the target of step 2.
2. The persistent key for credential-free HTTP authorities and size first in
   the lease step, adopted by the exact-package search Root that #8415 put on
   the ranged path; gates 1 to 9. This step changes every consumer in the
   table above.
3. The remaining search scopes adopt ranged access, and with it size first,
   in the second part of range-access adoption step 2.

## Non-claims

This document does not:

- download anything in the background or after the command's work;
- cache ranged content or individual entries;
- change the ranged read, its outcomes, or its fallback;
- change platform-pack acquisition, which becomes durable only through the
  key rule;
- evict or bound the durable cache's total size, which remains the cache
  maintenance owner's.
