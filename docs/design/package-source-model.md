# Package source model

This document defines what it means for dotnet-inspect to support NuGet package
sources. It covers source configuration, package source mapping, local stores,
source-bound caches, package discovery, exact payload acquisition, and
NuGet.org-specific enrichment.

It is the target contract. The
[implementation boundaries](#implementation-boundaries) section distinguishes
the behavior already present from the work needed to complete the model.

The central rule is:

> Feeds authorize package candidates. Caches may fulfill an authorized exact
> coordinate, but they do not introduce candidates and their payload must
> retain the identity of the feed that supplied it.

NuGet does not define the declaration order of HTTP sources as precedence. It
queries applicable sources and may obtain an exact package from any of them.
[Package Source Mapping][package-source-mapping] is the mechanism for limiting
which sources may serve a package id. dotnet-inspect follows that division:
configuration establishes the active sources, mapping narrows them for each
package id, and source provenance protects downloaded content after acquisition.

## Terms

| Term | Meaning |
| --- | --- |
| Coordinate | A package id and exact version. |
| Active source | A source selected by configuration and command-line options. |
| Eligible source | An active source permitted to serve a particular package id after package source mapping. |
| Candidate source | An eligible source that reported a particular coordinate during version discovery. |
| Producer | The eligible source that supplied the bytes for a coordinate, or the `explicit-local-input` origin for a directly named `.nupkg`. |
| Candidate cache | Source-scoped version metadata that can answer discovery without contacting its feed. |
| Payload cache | Exact package content retained with the identity of its producer. |
| Source-bound content | Content dotnet-inspect fetched and recorded against its producer. |
| Payload location | Where the inspected bytes were opened: an explicit file, global packages, the dotnet-inspect cache, a local feed, or a network response. |
| Enrichment endpoint | A service such as a symbol server or NuGet.org aggregate metadata API that is not itself a package source. |

Source identity has two parts with different purposes:

- the configured source name is what `<packageSourceMapping>` refers to; and
- the canonical HTTP endpoint or local directory identifies the producer in
  caches.

Names do not prove two feeds are the same. HTTP endpoint canonicalization does
not make path or query case-insensitive or discard a non-root trailing slash.
Local sources use a separate identity: config-relative paths resolve from the
declaring config's directory, CLI-relative paths resolve from the working
directory, and path and `file://` spellings normalize to one absolute directory.
Dot segments and trailing directory separators fold; path case follows the
platform. Symlinks are not resolved merely to establish identity. HTTP and
local identities occupy distinct namespaces. The cache identity rules are
detailed in [cache concurrency and publication](cache-concurrency.md).

Credentials are an access mechanism, not a durable source identity. Cache
eligibility does not retain a token, depend on token lifetime, or prove that the
caller could authenticate again. Multiple configured names for one canonical
endpoint remain aliases through package source mapping, then collapse to one
content producer. If eligible aliases define conflicting credential
configurations, resolution fails rather than choosing a credential
arbitrarily. A feed that serves different bytes for one coordinate at one
endpoint based on the caller's identity is not compatible with this
immutable-source assumption; it needs distinct source endpoints to keep those
content domains separate. Credentials selected for a configured endpoint may
be sent to package resources discovered on the same origin (scheme, host, and
port), but never to a cross-origin resource advertised by the feed.

## Resolving active and eligible sources

Without source options, dotnet-inspect resolves the same effective
configuration hierarchy as NuGet restore for the working directory. That
includes applicable defaults, computer-level, user-level, and directory-level
configs. Ordinary collections merge in NuGet precedence order with `<clear/>`,
disabled sources, and nearer re-enablement. Administrator sources from
`NuGetDefaults.Config` are different: `<clear/>` cannot remove them, but a
nearer `disabledPackageSources` entry can disable or re-enable them. NuGet.org
is the fallback only when the complete hierarchy contains no package-source
configuration. A configuration that deliberately resolves to an empty active
set does not authorize NuGet.org; source resolution fails without disclosing
the requested package id.

An explicitly named `--nugetconfig` is a source-selection act, not absence of
configuration. Only that file supplies configuration, and the implicit
NuGet.org fallback is suppressed. The file must exist, be valid, and declare a
usable source; an empty selected file fails rather than searching a feed the
caller did not name.

Only the final active source set matters downstream. Sources produced by
`nuget.config` merging are semantically identical to the same endpoints named
with repeated `--source`: they supply the same candidates and authorize the
same producer-matched payload-cache entries.

`<clear/>` removes inherited source authorization. With no subsequent source,
there are no candidates and no `global-packages` entry has an authorized
producer. Adding a source back enables that feed and payloads whose recorded
producer matches it. This has the same effect as `--no-nuget-cache` on entries
belonging to removed sources, but the mechanisms remain distinct:
`--no-nuget-cache` disables the global payload cache even for active sources.

The command-line source options compose as follows:

| Option | Effect |
| --- | --- |
| `--source URL` | Replaces configured package sources. Repeat it to select more than one. |
| `--add-source URL` | Adds a source to the active set. |
| `--nugetconfig PATH` | Selects one config instead of config discovery. |

Source replacement does not disable package source mapping. Mapping is loaded
from configuration independently and then applied to the active sources. Under
the target model, matching follows NuGet: mapping keys are compared with active
source names case-insensitively. A URL supplied through `--source` or
`--add-source` retains every configured-name alias when it matches that
producer. Mapping selects the package-specific aliases before they collapse to
one producer. An unmatched URL uses the URL itself as its source name.

This distinction matters when an override names an endpoint that configuration
does not. If a config maps `Contoso.*` to a source named `contoso`, an unmatched
URL override is not named `contoso`; no source is eligible and the command
reports a mapping failure. A URL override that matches the configured
`contoso` endpoint keeps that name and remains eligible.

### Package source mapping

When `<packageSourceMapping>` is absent, every active source is eligible for
every package id. When it is present, every id must match a pattern. Matching is
case-insensitive and uses NuGet's precedence:

1. An exact package id wins.
2. Otherwise, the longest matching prefix ending in `*` wins.
3. `*` is the least-specific prefix and acts as a default.

Only sources declaring the winning pattern are eligible. The same winning
pattern may appear under more than one source, in which case all of those
active sources are eligible. A package that matches no pattern, or whose mapped
source names are not active, fails as a mapping error before candidate or
payload lookup.

Mapping is evaluated independently for every package id. It therefore applies
to top-level packages, transitive dependencies, RID companion packages,
platform packs, tool-wrapper redirects, routing probes, and search results. A
dependency does not inherit its parent's producer.

Configured local-folder feeds are ordinary sources under this rule. They must
be active and mapped like HTTP feeds. The NuGet global packages folder is not a
configured feed; it is a provenance-bearing payload cache.

## Candidates before payloads

Source declaration order does not decide version selection and is not a
security boundary. Version discovery combines source-scoped candidate lists
from all eligible feeds, and selecting one version uses semantic-version
ordering. Each coordinate retains the feeds that reported it.

Content caches are not candidate lists. A version present only in
`global-packages` or the dotnet-inspect package-content cache does not appear in
`Name`, `--versions`, wildcard, or range resolution. A source-scoped version
list cache may answer for its feed because it stores that feed's discovery
result, not because package bytes happen to exist locally.

After discovery selects an exact coordinate, a payload cache may answer only
for an authorized producer:

- for a discovered coordinate, the authorized producers are the candidate
  sources that reported that version; and
- for a pinned coordinate, the caller supplied the candidate, so every eligible
  source is an authorized producer.

A source-bound app-cache slot may answer only when its producer is in that set.
A `global-packages` entry may answer only when `.nupkg.metadata.source` resolves
to a source in that set. Missing, ambiguous, or mismatched provenance is a cache
miss. The payload is then requested from an authorized producer and cached
under that producer's identity.

When payload sources must be queried, configured local-folder feeds are
considered before HTTP feeds, matching NuGet's documented source tiers. No
precedence is promised among sources in the same tier. If one exact producer
matters, configure or map the package id to one source.

## Candidate and payload stores

| Store | Supplies candidates | Supplies payloads | Source rule | Bypass |
| --- | --- | --- | --- | --- |
| Explicit local `.nupkg` | The named coordinate only | Yes | Producer is `explicit-local-input`; feed resolution does not apply | Choose a different input. |
| Source-scoped version cache | Yes | No | Answers only for the feed that produced the cached list | `@latest` or cache clearing. |
| NuGet global packages folder | No | Yes | `.nupkg.metadata.source` must be an authorized producer | `--no-nuget-cache`. |
| dotnet-inspect package cache | No | Yes | Its producer slot must be authorized | Clear or isolate the app cache. |
| Configured local-folder feed | Yes | Yes | Must be active and eligible | Remove it from the active source set. |
| HTTP feed | Yes | Yes | Must be active and eligible | Remove it from the active source set or use `--offline`. |

NuGet restore uses the global folder more permissively: an exact hit skips
source lookup and package source mapping. dotnet-inspect instead uses the
optional `.nupkg.metadata.source` field as an authorization requirement. This
is necessary for `--source A` to mean that A supplies both the candidate and
the bytes.

There is no restore-like compatibility mode. Source fidelity is the only
payload policy. In the common case it adds no network work because the recorded
producer remains active and eligible. A miss occurs when provenance is absent,
the source was removed or mapped out, or another source supplied the same
coordinate. Those are precisely the cases where source-blind reuse would make
the result ambiguous. After a strict miss, the source-scoped dotnet-inspect
cache serves later requests.

`--no-nuget-cache` disables the global payload cache but retains
dotnet-inspect's source-bound payload and candidate caches. `--offline` forbids
network access: a pinned coordinate can succeed from an authorized payload
cache, while discovery also requires an eligible local-folder feed or
source-scoped candidate cache. `--isolated` combines a separate app-cache root
with exclusion of the global packages folder.

## Feed-relative inspection

Package inspection is feed-relative. An id and version identify the NuGet
coordinate, but they do not establish which bytes dotnet-inspect examined when
multiple feeds publish that coordinate.

Standard package provenance therefore reports:

- the producer feed whose payload is being inspected, or
  `explicit-local-input` for a directly named `.nupkg`; and
- the payload location used for this invocation, such as `global-packages`,
  the source-scoped dotnet-inspect cache, a local feed, or a fresh network
  response.

These are independent. A package opened from `global-packages` still reports
NuGet.org or a custom feed as its producer. A package fetched from a feed and
then committed to the app cache keeps the same producer on later runs.

Version discovery exposes feed observations before a payload is selected:
`--versions-with-feed` reports every feed carrying each version. Exact
feed inspection selects one authorized producer and reports it. Direct file
inspection reports its explicit local origin instead; neither path presents
source-free provenance. Comparing differing payloads from multiple feeds is an
explicit future audit operation rather than hidden work in ordinary inspection.

## Operation contracts

Every operation that deals in package identities uses the same eligible-source
calculation. It may differ only in how answers from that set are combined.

| Operation | Contract |
| --- | --- |
| Latest or wildcard version | Query every eligible source or its candidate cache and select the highest matching semantic version. Content caches do not participate. |
| Version enumeration or range | Return the union from eligible source candidate lists. `--versions-with-feed` retains one row per version and reporting feed. |
| Discovered payload acquisition | Use a payload cache only when its producer reported the selected coordinate. On a miss, query those candidate sources and record the successful producer. |
| Pinned payload acquisition | The caller supplies the coordinate. Use a payload cache or source only when its producer is eligible for the package id. |
| Nuspec and dependency traversal | Apply mapping to each dependency id, including cache reads and nuspec-only requests. |
| Search | Search active sources with a search capability, then retain a result only when the reporting source is eligible for that result's package id. Carry source failures rather than presenting partial results as complete. |
| Routing and qualified-name probes | Use the caller's source options and mapping for package-existence and fallback probes. A probe cannot see a source the eventual command cannot use. |
| RID, platform-pack, and wrapper follow-up | Recalculate eligibility for every newly named package id. |
| Project restored-assets context | Bind to the assets and local package content selected by the existing restore; do not reinterpret its graph through current source options. |

Candidate caches are source-specific inputs, not merged authority. A cached
version list for one endpoint may replace a request to that endpoint, but the
answer is recomposed across the current eligible set and retains candidate
provenance. Package content cache keys include the producer endpoint. Mapping
and candidate authorization are evaluated before a payload cache entry can
answer.

### Failure semantics

Source failures are not package absence:

- no mapping match, or no active source named by the winning mapping, is a
  mapping error;
- authentication and transport failures identify the unreadable source;
- `not found` means every source required to establish absence was read and
  none supplied the package; and
- an operation claiming a complete aggregate, such as latest-version
  resolution, cannot silently claim an authoritative result while an eligible
  source is unreadable.

A pinned exact coordinate may succeed from one of several eligible sources
without proving that every peer source is readable. Aggregate operations must
either fail or mark an answer partial when an eligible source could change it.

## Enrichment is a separate capability

Package-source authority does not automatically authorize traffic to unrelated
services.

NuGet.org registration, catalog, download-count, deprecation, verification, and
vulnerability services may be queried for a package id only when NuGet.org is
eligible for that id. Merely listing NuGet.org somewhere in the active config
is insufficient when package source mapping assigns the id elsewhere. This
prevents a private package identity from being disclosed to NuGet.org.

PDB acquisition has its own provenance:

- embedded and adjacent PDBs belong to the selected package or library;
- NuGet.org has known producer-specific `.snupkg` download routes;
- custom and local feeds have no standard NuGet symbol-package download
  resource, so `.snupkg` acquisition from those producers is unsupported until
  an explicit endpoint contract exists;
- NuGet and Microsoft symbol servers are explicit enrichment endpoints, not
  package sources, and successful results record the server; and
- SourceLink URLs come from the inspected artifact and are governed by the
  untrusted-data and network-capability policies, not package source mapping.

Configuring a private package source therefore never grants permission to probe
NuGet.org for that package's `.snupkg`. Conversely, allowing a symbol-server
probe does not make that server eligible to supply package bytes.

## Implementation boundaries

The source-policy seam should resolve one typed result containing:

- the active sources, with configured names, canonical endpoints, credentials,
  and local/HTTP capability;
- the package-id-specific eligible sources after mapping;
- source-scoped candidate results and their reporting feeds;
- the payload-cache policy and authorized producer set; and
- diagnostics for unmatched mappings and unavailable required sources.

Consumers should not independently parse source options, resolve config, or
decide whether a cache key is allowed. Version discovery, package extraction,
nuspec reads, search, routing, metadata enrichment, RID verification, and
symbols should consume that shared result.

The current implementation already source-scopes downloaded package content,
aggregates versions across sources, and threads caller options through routing
probes. The remaining implementation work includes:

- honoring `<packageSourceMapping>` across every operation
  ([#3722](https://github.com/richlander/dotnet-inspect/issues/3722));
- completing config and override semantics, including intentional empty source
  sets, the complete NuGet config hierarchy, disabled-source merging, and
  preservation of all configured-name aliases for explicit endpoints
  ([#3739](https://github.com/richlander/dotnet-inspect/issues/3739));
- preserving non-root trailing slashes in producer identity and fencing cache
  entries written under the currently aliased identity
  ([#3737](https://github.com/richlander/dotnet-inspect/issues/3737));
- representing configured order structurally for stable presentation and
  diagnostics, without treating it as precedence
  ([#3724](https://github.com/richlander/dotnet-inspect/issues/3724));
- carrying payload producer provenance through package inspection indexes,
  projected platform packs, and package-associated symbols
  ([#3738](https://github.com/richlander/dotnet-inspect/issues/3738));
- separating candidate caches from payload caches and requiring
  provenance-matched `global-packages` payloads
  ([#3752](https://github.com/richlander/dotnet-inspect/issues/3752));
- exposing producer feed and payload location as standard package provenance;
- defining canonical local-source identity and acquiring packages and nuspecs
  from configured folder feeds
  ([#3759](https://github.com/richlander/dotnet-inspect/issues/3759));
- making aggregate failures authoritative or explicitly partial; and
- separating package-producer symbol lookup from public symbol-server
  enrichment.

## NuGet precedents

- [Package Source Mapping][package-source-mapping] defines mapping patterns,
  pattern precedence, transitive application, multiple eligible sources, and
  the global-packages-folder exception.
- [Package installation process][package-installation-process] documents that
  local sources precede HTTP sources and that multiple sources are consulted to
  find the best version.
- [Managing the global packages and cache folders][nuget-cache-folders]
  documents the global-folder-first lookup.
- NuGet's
  [`RemoteWalkContext`](https://github.com/NuGet/NuGet.Client/blob/c82ceb9ad93dc8fdcb51fb6807c8e8c70f1443e8/src/NuGet.Core/NuGet.DependencyResolver.Core/Remote/RemoteWalkContext.cs)
  applies mapping by package id and active source name.
- NuGet's
  [`ResolverUtility`](https://github.com/NuGet/NuGet.Client/blob/c82ceb9ad93dc8fdcb51fb6807c8e8c70f1443e8/src/NuGet.Core/NuGet.DependencyResolver.Core/ResolverUtility.cs)
  separates local and HTTP source tiers and does not make HTTP declaration
  order a precedence contract.

[nuget-cache-folders]: https://learn.microsoft.com/nuget/consume-packages/managing-the-global-packages-and-cache-folders
[package-installation-process]: https://learn.microsoft.com/nuget/concepts/package-installation-process
[package-source-mapping]: https://learn.microsoft.com/nuget/consume-packages/package-source-mapping
