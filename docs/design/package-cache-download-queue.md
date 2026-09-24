# Package cache download queue

## Status, owner, and claim

This document is the normative owner for **filling the durable package cache
after a ranged read**: when a ranged acquisition answers a query from a few
entries of a remote package, the complete archive is downloaded in the
background into the durable cache, so the next inspection of that package is
a cache hit. It is the third slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day, and it carries that issue's cache policy.

The claim has three parts:

- **Durable identity for credential-free HTTP authorities.** An HTTP authority
  with no configured credential gets a persistent cache key, so its complete
  payloads are published to, and later served from, the durable
  authority-scoped store. This is the one claim this document transfers into
  the [package source model](package-source-model.md#candidate-and-payload-stores),
  which keeps every other store rule.
- **The queue.** A ranged acquisition of an eligible package queues the
  complete download. The CLI runs it in the background of the same
  invocation, waits at most a short grace at exit, and records unfinished
  downloads in a persistent wanted-list that the next invocation resumes.
- **The size policy.** Which ranged reads queue a download.

It consumes, and does not redefine, the ranged read of
[package archive range access](package-archive-range-access.md), the lease
step and ranged content of the
[package source model](package-source-model.md#ranged-payload-realization),
the transactional publication of
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

The ranged read wins cold and loses warm by the whole ranged read, because
nothing it reads is kept and HTTP authorities have no durable store today:
every invocation repeats it. A complete download of the same archive takes
0.4 s on that link and is a 0.28 s cache hit afterwards. Downloading in the
background after the first ranged read keeps the fast first answer and
restores the cache hit.

Package sizes across one developer's NuGet global packages folder (1,696
packages, 4.5 GB): median 0.2 MB, 90th percentile 7.0 MB, 95th 16.4 MB, 99th
40.1 MB, largest 58 MB. Packages under 12 MB are 93.1% of packages and 33.3%
of bytes; the packages above it are mostly native tool and runtime payloads
(`runtime.*.ilcompiler`, `*.linux-x64` tool packages, browser-wasm runtime
packs) that a query rarely inspects twice.

## Contract

### Durable identity for credential-free HTTP authorities

An HTTP authority whose source carries no configured credential and whose
endpoint carries no user information receives a persistent cache key derived
from its canonical endpoint, including path and query, in the same versioned
key namespace local authorities use. Query-distinct and path-distinct
endpoints therefore stay distinct authorities with distinct entries, and the
key is a digest, so no endpoint text is written to a path.

Its complete payloads are then published to the durable authority-scoped
store and served from it under every existing rule: cache-first, the entry
answers only for a currently configured authority with the same key, and a
changed endpoint is a different authority. Authorities with a configured
credential keep process-local storage only.

A feed authenticated by a credential plugin rather than a configured
credential is credential-free by this rule. Its cached packages remain
readable after the plugin would refuse a new request, as they do in NuGet's
own global packages folder; package versions are immutable, and a same-user
cache is outside the
[threat model](untrusted-data-threat-model.md#trust-boundaries).

### The queue

A ranged acquisition that produced ranged content from an HTTP authority with
a persistent key, for an archive whose total length the ranged read derived
at or under the size cut, queues one download request: that authority's
persistent key and the exact coordinate. Nothing else queues: a cache hit, a
complete acquisition, a refusal that fell back to the complete fetch, and
authorities without a persistent key do not.

The queue runs each request as an ordinary complete candidate payload
acquisition from that authority into its durable store, under a fresh
operation lease with its own deadlines, so authorization, admission, and
transactional publication are exactly the complete path's. A request whose
authority is no longer configured under the same key is dropped. A
coordinate already in the durable store is complete and is dropped.

Queued work never changes the invocation's output or exit code. Its outcomes
appear only in verbose output. A failed download is dropped from the
wanted-list after its third failed attempt.

### Invocation lifetime

The CLI host starts a queued download as soon as it is queued, concurrently
with the rest of the command. When the command has written its output, the
host waits for queued downloads at most 1 s in total, then cancels the rest.
A cancelled download publishes nothing, as every cancelled acquisition
publishes nothing, and its request stays on the wanted-list.

The wanted-list is a versioned persistent-cache category holding authority
keys and coordinates only, never endpoints or credentials. At the start of
an online invocation that queues nothing itself, the host resumes at most
two wanted downloads in the background under the same grace. Offline, no
download starts and the wanted-list is kept.

### Size policy

The size cut is 12 MB of archive. Larger archives stay ranged-only: every
query reads its entries by range, and nothing is cached. Platform packs
(reference, runtime, and targeting packs) are acquired complete by the
[package-backed platform](package-backed-platform-realization.md) owner, not
by a ranged read, so they are cached at any size without passing through
this queue.

## Host scope

The queue is CLI host policy over package-owned steps. The host-neutral
parts are the persistent key rule and the payload result that says a ranged
read happened; the queue, the grace, and the wanted-list belong to a host
with a durable store and a process lifetime. Inspect Web keeps packages in
an in-memory store for one session and has neither, so it does not adopt the
queue; its ranged reads remain the range-access design's Inspect Web slice.

## Pathological cases and gates

All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Credential-free HTTP authority | persistent key; query-distinct and path-distinct endpoints get distinct keys; the key contains no endpoint text | `PackageSourceAuthorization_CredentialFreeHttpAuthorityHasPersistentKey` (replaces `..._HttpAuthorityWithoutStableIdHasNoPersistentKey`) |
| 2. Configured credential, or user information in the endpoint | no persistent key | `PackageSourceAuthorization_CredentialPathAuthoritiesHaveNoPersistentKey` (extended) |
| 3. Ranged read of an eligible package, then a second invocation | the first queues and completes the download within the grace; the second is a cache hit with no package request | CLI harness, two invocations |
| 4. Download slower than the grace | cancelled; nothing published; the next invocation resumes it and the one after is a cache hit | CLI harness, stalled then healthy feed |
| 5. Archive over the size cut | nothing queued; every invocation reads by range | CLI harness |
| 6. Complete acquisition, cache hit, refusal fallback, credentialed authority | nothing queued | contract suite |
| 7. Queued download fails | output and exit code unchanged; verbose diagnostic; dropped after the third failure | CLI harness |
| 8. Offline invocation with a non-empty wanted-list | no request; the list is kept | CLI harness |
| 9. Authority no longer configured under the same key | the request is dropped without a transfer | contract suite |
| 10. Two invocations downloading the same coordinate | one publication, as transactional publication guarantees | existing cache-concurrency gates |
| 11. Motivating asset | `Avalonia` 12.1.2, second invocation a cache hit at the 0.26.0 warm time | preserved probe as design evidence |

## Adoption

1. This document, with the persistent-key rule amended in the package source
   model.
2. The persistent key for credential-free HTTP authorities, the queue, the
   grace, and the wanted-list, adopted by the exact-package search Root that
   #8415 put on the ranged path; gates 1 to 10.
3. The remaining search scopes adopt ranged reads and the queue together in
   the second part of range-access adoption step 2.

## Non-claims

This document does not:

- change the ranged read, its outcomes, or when a consumer reads by range;
- cache ranged content or individual entries;
- change platform-pack acquisition or its caching;
- evict or bound the durable cache's total size, which remains the cache
  maintenance owner's; or
- adopt the queue in Inspect Web.
