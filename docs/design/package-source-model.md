# Package source model

This document defines what it means for dotnet-inspect to support NuGet package
sources. It covers source configuration, package source mapping, local stores,
source-bound caches, package discovery, exact payload acquisition, and
NuGet.org-specific enrichment. It also owns the composition boundary that turns
an eligible desktop producer route into typed source operations and projects
their results back into package-source outcomes.

Browser source implementations, NuGet Gallery access without the v3 service
index, portable source bundles, ephemeral credentials, and library-owned
timeouts are defined by
[browser package sources](browser-package-sources.md).
For the desktop composition boundary introduced below, protocol discovery and
request mechanics remain owned by NuGetFetch. Host HTTP pipeline construction,
offline enforcement, and network diagnostic rendering remain owned by
DotnetInspector.Core. That boundary consumes those adjacent contracts without
redefining them.

It is the target contract. The
[implementation boundaries](#implementation-boundaries) section distinguishes
the behavior already present from the work needed to complete the model.

The central rule is:

> Package sources authorize package candidates. Caches may fulfill an authorized exact
> coordinate, but they do not introduce candidates and their payload must
> retain the identity of the source that supplied it.

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
| Source client | A protocol-specific implementation that supplies package-source capabilities behind the common candidate and payload contracts. |
| Producer route | One stable producer identity together with its source kind and ordered runtime transport profiles. |
| Typed source identity | The NuGetFetch HTTP identity carried by owner-issued protocol results. It may fold runtime query credentials and does not replace package cache or provenance identity. |
| Candidate observation | A normalized coordinate together with the producer that reported it, the discovery contract, and source-relative listing state. |
| Availability observation | A transient environment- and transport-scoped result describing whether an authorized producer can currently supply a coordinate. |

Source identity has two parts with different purposes:

- the configured source name is what `<packageSourceMapping>` refers to; and
- the canonical HTTP endpoint or local directory identifies the producer in
  caches.

Names do not prove two feeds are the same. HTTP endpoint canonicalization does
not make path or query case-insensitive. It folds exactly one optional trailing
path slash because `/feed` and `/feed/` are alternate spellings of one endpoint;
repeated trailing slashes and fragments remain distinct. This endpoint identity
is shared by credential scoping and legacy cache keys, where folding a query or
fragment could let one configured endpoint answer for another.
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
Runtime v3 clients own isolated credential-free transports rather than
accepting a shared client or opaque caller handler. Their default handler
disables cookies, default credentials, and preauthentication on desktop.
Browser/Wasm applies `BrowserRequestCredentials.Omit` to each request instead
of setting unsupported handler properties. Source credentials travel through
the typed credential parameter so the library can enforce the origin boundary.
Desktop redirects are followed by a bounded source-owned handler that reapplies
authorization only to the credential's original origin. Exceeding five
redirects is rejected as a source-response safety-bound failure. Malformed raw
targets, unusable IDNA hosts, and embedded user information are rejected before
another request is formed.
This is gated by
`RuntimeFactoriesDoNotAcceptSharedHttpClient` and
`DefaultV3TransportHasNoAmbientCredentialMechanisms`,
`BrowserV3TransportAvoidsUnsupportedHandlerConfiguration`,
`BrowserNuGetRequestsOmitAmbientCredentials`,
`DesktopRedirectsScopeAuthorizationToOriginalOrigin`,
`DesktopRedirectLimitAllowsFiveAndRejectsSix`,
`RedirectLimitIsResponseRejected`,
`MalformedRedirectTargetIsInvalidResponse`, and the `NuGetFetch`
`browser-wasm` build.

The typed source-client compatibility adapter therefore derives its typed
source identity from a query-bearing legacy service index's origin and path
while retaining its query and fragment only in runtime transport configuration.
This does not change the stricter package producer endpoint identity used for
credential adoption, payload provenance, or legacy caches. Portable descriptors
reject queries and fragments. Two immutable content domains that need distinct
typed source identities require distinct endpoint paths rather than a
query-only distinction.

Package-source identity is broader than a NuGet v3 service-index URL. A
standard v3 feed, the built-in NuGet Gallery browser implementation, and a
local folder may implement the same candidate and payload contracts through
different transports. Those implementation differences remain below source
resolution: consumers receive typed capabilities, candidates, failures, and
producer provenance rather than protocol URLs.

The NuGet Gallery browser implementation and the canonical NuGet.org v3 source
share the producer identity
`https://api.nuget.org/v3/index.json`; the browser implementation may use that
identity without requesting the blocked service index. User-interface registry
IDs and transport profiles do not replace producer identity. Candidate cache
keys also identify the discovery contract and its version so a listed-only
search result cannot answer a complete listing-aware enumeration.

Several transport profiles may implement one producer. Source resolution
collapses them by producer identity before candidate queries. A transport
failure falls through to another applicable profile and does not create a
second candidate source or a partial aggregate; the producer fails only when
all of its applicable transports fail.

## Desktop typed source-client boundary

This section defines one package-source-owned responsibility: adapting an
eligible desktop producer route to an owner-issued typed source client.
`DotnetInspector.Packages` owns that composition. It is a target contract. Its
safety properties remain unverified until the
[required implementation gates](#required-implementation-gates) run in a
Release suite.

### Immediate input and output

For each eligible configured source, the package layer first forms an
identity-free classification input containing:

- the configured source aliases and configured source value selected by source
  policy;
- the package coordinate or candidate capability requested by the operation;
  and
- the network capability selected for the public package operation.

Classification either returns the package-owned classification-failure variant
or constructs one of two producer-bearing route inputs:

| Route input | Required fields |
| --- | --- |
| HTTP | Canonical endpoint producer identity; typed source identity; ordered runtime transport profiles; owner-issued source-client factory for each profile; the #4776 compatibility-request credential policy; and the #4770 public operation context. |
| Local folder | Canonical local-directory producer identity; local source kind; implemented package-layer local-store capabilities; and caller cancellation. It has no typed source identity, HTTP transport profile, or source-client factory. |

The package producer identity owns configured-alias collapse after mapping,
route authority, recorded payload provenance, payload-cache slot identity, and
payload-cache authorization. Candidate-cache identity adds the discovery
contract and version. The typed source identity selects and labels HTTP
protocol operations only.

Runtime transport-profile order controls failover but does not create another
content identity or invalidate candidate evidence for the same immutable
producer and discovery contract. A signed query remains part of canonical HTTP
producer identity even when the typed source identity folds it; raw query text
is hashed in cache keys and redacted in diagnostics.
The package layer passes the network capability unchanged and cannot turn an
offline operation into permission to contact a source.

An owner-issued source operation returns either its typed value or a
`PackageSourceFailure`. `DotnetInspector.Packages` wraps that result in one
package-owned route outcome with these variants:

- a successful HTTP operation, retaining the owner-issued search or version
  candidates, exact manifest, or payload value together with package producer
  identity, typed source identity, and selected transport profile;
- a failed HTTP operation, retaining the owner-issued failure, package producer
  identity, typed source identity, and selected transport profile;
- a local-route observation, retaining canonical local producer identity and
  either a package-layer local-store value or an unsupported-capability reason,
  with no typed source identity or transport profile; or
- a pre-client classification failure, retaining a safe configured-source
  reference, syntactic source kind, and reason without a producer identity.

The classification-failure variant cannot authorize a cache entry, contribute
candidate provenance, or assert package absence. Its safe source reference is
the contained, redacted display produced by
`PackageSourceDisplay.ForDiagnostics`; a configured source name is never used
directly because an unmatched URL override uses its URL as the name.

Raw resource URLs, response bodies, and credentials are not package-layer
failure data. Caller cancellation propagates with the caller token instead of
becoming a source failure.

### Classify before selecting a transport

Source kind is decided before any HTTP client or service-index normalization is
selected:

| Source kind | Package-layer action |
| --- | --- |
| HTTP endpoint | Ask the owner-issued factory for the endpoint's typed capability. A syntactically valid HTTP URL is not assumed to be a valid NuGet v3 source. |
| Local path or `file://` directory | Construct the local-route input and return a local-route observation. Until [local-feed acquisition](https://github.com/richlander/dotnet-inspect/issues/3759) supplies a requested capability, retain canonical directory identity with an unsupported-capability reason and never rewrite the source as HTTP. |
| Unsupported URI scheme or malformed runtime endpoint | Return the package-owned classification-failure variant without constructing an HTTP request, minting a producer identity, or exposing a raw argument exception. |

An unsupported local capability is not an unreadable remote source. It is
reported with its own source kind rather than an HTTP failure. When an
aggregate requires every eligible source, that observation makes the result
non-authoritative: the operation must fail or explicitly report a partial
result. It cannot silently grant authority to a cache or another source.

### Protocol and host handoff

The package layer decides which producer may be queried. For an HTTP route, it
obtains the typed source client from the owner-issued factory and invokes its
operations with the context issued by #4770. A local route remains within the
package layer and never constructs that client. NuGetFetch owns service-index
discovery, endpoint construction and validation, protocol-level retry, bounded
body reading, source-client transport construction and lifetime, and the typed
source-operation result.
DotnetInspector.Core owns its host HTTP pipeline, offline enforcement, and
redacted network diagnostics.

The package layer does not reconstruct v3 resource URLs or add a second retry
loop around a typed operation. A compatibility request that still must follow
a feed-discovered resource remains part of the same producer route and uses
the request-policy hook issued by the NuGetFetch-owned #4776 prerequisite. This
prevents a fallback from bypassing policy merely because it has not yet
migrated to the typed protocol operation.

This boundary does not prescribe whether an adjacent source-client
implementation owns an isolated transport or receives policy through another
owner-issued factory. Transport construction, mutability, and disposal remain
part of that adjacent owner's contract.

DotnetInspector.Packages owns each typed HTTP client handle it obtains. It
retains that handle until a returned payload stream is consumed or disposed,
or transfers both into one wrapper with the same lifetime. It disposes the
handle on every path that returns no payload. This is client-handle
orchestration, not a claim about how the adjacent client owns its transport.

### Credential and origin boundary

The package layer consumes the source client's declared credential-origin
contract; it does not redefine how typed protocol requests or redirects enforce
that contract. For a compatibility request the package layer still constructs,
the configured producer origin is compared with the feed-derived target before
the request reaches authentication. The owner-issued policy from #4776 marks a
cross-origin request as ineligible for plugin authentication; a same-origin
request remains eligible.

The package layer does not discover plugins, cache credentials, construct
authorization headers, or infer authorization from URL text. Those mechanisms
remain adjacent-owner responsibilities. The package layer's obligation is to
preserve the producer origin and apply the #4776 request policy to every
compatibility request it constructs.

### Routes, deadlines, and projection

Configured source aliases collapse by canonical endpoint or local-directory
producer identity after mapping.
Transport profiles for one producer form one ordered route rather than
separate candidate sources. Source declaration order between different
producers is not precedence and cannot make one source's candidate
authoritative over another.

The request deadline and operation ceiling are the library-owned bounds defined
by [browser package sources](browser-package-sources.md#timeout-ownership).
The owner-issued carrier required to compose that rule across typed clients is
tracked by #4770. One public operation ceiling spans every selected producer,
transport-profile failover, retry, and payload read. The package layer passes
the same operation context through every route; neither a new producer nor
another transport profile resets it.

Package-layer projection preserves:

- package producer identity, typed source identity, and transport-profile
  diagnostics without substituting one identity for another;
- source-failure kind;
- timeout kind and configured duration;
- caller cancellation as cancellation rather than timeout; and
- payload-stream ownership and the deadline that remains active through
  consumption.

If a source returns a payload after the public operation ceiling, the package
layer disposes the unreturned stream before producing a typed operation
timeout. If consuming an already returned payload raises an expected request
timeout, the package acquisition layer projects a new content-free failure
with the same timeout kind and duration; another authorized producer may be
tried only while the public operation ceiling remains. An operation-ceiling
timeout terminates the public operation and cannot enable producer failover.
Archive validation and store failures remain payload-policy outcomes rather
than transport failures.

### Aggregation and completeness

Each typed HTTP client or local-store adapter reports one producer at a time;
the package layer owns multi-source composition. Exact pinned acquisition may
succeed from one eligible producer. An aggregate that selects latest or
wildcard versions, claims authoritative absence, or otherwise depends on the
complete candidate set requires a terminal observation from every eligible
producer. A capability gap is itself non-terminal for completeness: the
operation must fail or explicitly report partial authority. A healthy subset
cannot silently become the whole authority.

Candidate caches remain producer- and discovery-contract-scoped observations.
They may replace a request only for the producer and discovery contract that
produced them, and every aggregate is recomposed against the current eligible
set.
Local sources without the operation capability remain explicit
unsupported-capability observations. They are not rewritten as failed HTTP
feeds, but they prevent an authoritative all-source aggregate unless the
result shape explicitly represents partial authority.

### Required implementation gates

An implementation may claim this boundary only when the named Release test
projects contain non-vacuous gates for these outcomes.

`src/dotnet-inspect.Tests` owns the package-layer composition gates:

- `PackageSourceClientProvider_LocalSourcesNeverSelectHttpTransport` proves
  plain paths and `file://` directories do not enter HTTP selection.
- `PackageSourceClientProvider_UnsupportedSchemesBecomeRouteClassificationFailures`
  proves the identity-free configured source is classified before route
  construction and an unsupported scheme returns the package-owned pre-client
  variant without a request or producer identity, using only the contained
  redacted source display.
- `PackageRoutePreservesOfflineNetworkCapability` proves route adaptation does
  not construct or invoke an HTTP source client, and observes no request, when
  the operation lacks network permission.
- `PackageCompatibilityRecoverySuppressesPluginAuthenticationCrossOrigin`
  proves compatibility recovery suppresses plugin acquisition for a
  feed-declared foreign origin, with a same-origin positive control. This gate
  depends on the owner-issued policy from #4776.
- `PackageSourceFailureDataExcludesResourceUrlsAndCredentials` proves
  package-layer failure projection does not retain signed queries, response
  text, or authorization data, including when an unmatched signed URL is also
  the configured source name.

`src/DotnetInspector.Services.Tests` owns aggregation, acquisition, and
projection gates:

- `TransportProfilesShareThePublicOperationCeiling` proves neither another
  profile nor another producer resets the operation ceiling. This gate depends
  on the owner-issued carrier from #4770.
- `PackageStreamTimeoutPreservesKindAndDuration` and
  `RouteTimeoutPreservesKindAndDuration` prove request and operation timeout
  identity survives package-layer projection. These gates depend on the typed
  failure shape from #4770.
- `PackagePayloadCallerCancellationRetainsToken` proves caller cancellation is
  not projected as a source timeout.
- `PackageRouteOutcomeCarriesManifestWithoutPayloadProjection` proves the
  generic success variant retains an exact manifest as its owner-issued type.
- `PayloadKeepsSourceClientAliveThroughConsumption` proves client-handle
  disposal cannot invalidate a caller-owned payload stream.
- `FailedRouteDisposesSourceClientHandle` proves every path without a returned
  payload releases the package layer's client handle.
- `LatePayloadAfterOperationCeilingIsDisposed` proves the package layer does not
  leak an unreturned stream.
- `ProducerRouteCollapsesTransportProfilesIntoOneCandidateSource` proves
  transport failover does not create duplicate source authority.
- `ProducerAliasesCollapseByCanonicalEndpointIdentity` proves query-distinct
  configured endpoints do not collapse merely because their typed source
  identities match.
- `WildcardResolutionRequiresEveryEligibleSource` proves a healthy subset or
  capability gap cannot produce an authoritative aggregate.
- `LocalUnsupportedCapabilityPreventsAuthoritativeAggregateWithoutHttpFailure`
  proves a filesystem-free operation returns the local-route variant with
  canonical directory identity and no typed source identity or transport
  profile, prevents an authoritative aggregate, and does not route the source
  through HTTP diagnostics.
- `CandidateCacheUsesProducerAndDiscoveryContractIdentity` proves transport
  profile changes do not become content identity while incompatible discovery
  contracts remain isolated.
- `PayloadCacheAuthorizationRetainsCanonicalEndpointIdentity` proves
  query-distinct configured endpoints remain distinct cache authorities even
  when their typed producer identities match.

These gates must fail when the corresponding classification, request-policy
hook, timeout field, shared operation context, or completeness check is
removed. A test that only observes a generic failure kind or an empty result is
insufficient.

### Non-claims

This boundary does not define:

- NuGet protocol resource parsing, endpoint normalization, or internal retry;
- source-client transport construction or lifetime;
- host HTTP handler construction, offline exception rendering, or credential
  provider lifecycle;
- browser source profiles, portable source bundles, or browser persistence;
- local-folder package discovery and acquisition;
- CLI wording or output layout; or
- enrichment endpoint policy beyond consuming the producer eligibility already
  defined by this document.

## Resolving active and eligible sources

Without source options, dotnet-inspect resolves the same effective
configuration hierarchy as NuGet restore for the working directory.
`PackageSources.Default` models NuGet.org as the lowest-precedence source
layer; discovered computer-level, user-level, and directory-level configs are
then merged over it. Ordinary collections merge in NuGet precedence order with
`<clear/>`, disabled sources, and nearer re-enablement. Administrator sources
from `NuGetDefaults.Config` are different: `<clear/>` cannot remove them, but
a nearer `disabledPackageSources` entry can disable or re-enable them. With no
configuration, the default layer remains active. `<clear/>` replaces the
accumulated ordinary sources with `PackageSources.Empty`, so a deliberately
empty active set does not authorize NuGet.org; source resolution fails without
disclosing the requested package id.

An explicitly named `--nugetconfig` is a source-selection act, not absence of
configuration. Its merge starts from `PackageSources.Empty`, so only that file
supplies configuration. The file must exist, be valid, and declare a usable
source; an empty selected file fails rather than searching a feed the caller
did not name.

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

When discovery cannot complete, producer-authorized payload versions may be
shown as diagnostic exact-pin suggestions. They are never selected
automatically and do not become candidates for any discovery operation.

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

## Worked examples

Unless one is shown, these examples assume the complete configuration
hierarchy contains no package-source configuration, administrator default
source, or package source mapping.

### Pinned package, default source

```bash
dotnet-inspect package Foo@1.2.3
```

The caller supplied the exact coordinate, so no candidate query is needed.
NuGet.org is the implicit eligible source. If
`global-packages/foo/1.2.3/.nupkg.metadata` records NuGet.org, dotnet-inspect
opens that payload without network access:

```text
Producer: nuget.org
Payload location: global-packages
```

If the metadata is absent or records another feed, the global entry is a miss.
An authorized NuGet.org slot in the dotnet-inspect app cache may still answer.
dotnet-inspect downloads `Foo@1.2.3` from NuGet.org only when no authorized
payload cache answers, then commits it to that app-cache slot.

### Bare package, default source

```bash
dotnet-inspect package Foo
```

The command must first determine which version `Foo` means. NuGet.org is the
implicit candidate source:

1. Use NuGet.org's source-scoped candidate cache when it is fresh.
2. Otherwise, query NuGet.org and select the latest stable version.
3. Retain NuGet.org as the feed that reported the selected coordinate.
4. Open an app-cache or global-packages payload only when its producer is
   NuGet.org; otherwise download the payload from NuGet.org.

An installed `Foo@1.2.3` payload does not by itself make `1.2.3` a candidate.
With an empty candidate cache, this command queries NuGet.org even when that
payload is already in global packages. The network candidate query may still be
followed by a local payload hit.

### One explicit source

```bash
dotnet-inspect package Foo \
  --source https://feed-a.example/v3/index.json
```

Only feed A supplies candidates. If discovery selects `Foo@1.2.3` and A
reported it, a cached payload recorded from A may answer. A global-packages
entry recorded from NuGet.org or feed B is ignored, even though its id and
version match.

The pinned form skips A's candidate query but keeps the same payload rule:

```bash
dotnet-inspect package Foo@1.2.3 \
  --source https://feed-a.example/v3/index.json
```

### Equivalent `nuget.config`

When no administrator default source is active, this configuration has the
same source authority as `--source` naming feed A:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="feed-a"
         value="https://feed-a.example/v3/index.json" />
  </packageSources>
</configuration>
```

Feed A supplies candidates and authorizes payloads recorded from A. `<clear/>`
removes ordinary inherited feeds and their cached-payload authority; it does
not remove administrator sources from `NuGetDefaults.Config`, which must be
disabled through `disabledPackageSources`. It also does not disable payload
caching for a source subsequently added to the configuration.

### Offline operation

| Request | Cached state | Result |
| --- | --- | --- |
| `Foo@1.2.3 --offline` | Authorized payload | Succeeds without candidate metadata. |
| `Foo --offline` | Fresh candidate list selects `1.2.3`; authorized payload exists | Succeeds entirely from caches. |
| `Foo --offline` | Payload exists, but candidate metadata is absent | Fails because content cannot introduce a version candidate. |
| `Foo --offline` | Fresh candidate metadata exists, but no authorized payload exists | Fails because network payload acquisition is prohibited. |

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
| Prefix manifest profile | Search by package-ID prefix, retain candidate metadata and producer provenance, then request only each selected coordinate's bounded exact manifest. Package archives and assemblies are not profile inputs. |
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

NuGetFetch now exposes typed source-operation results. Candidate observations
carry normalized coordinates, producer identity, discovery contract, and
`listed`, `unlisted`, `unknown`, or `not-applicable` state. Exact payload
results retain their coordinate, producer, transport profile, payload kind,
and caller-owned stream. Expected source failures retain the source transport
and exact coordinate when applicable, and are classified without retaining
source URLs or response text. A payload stream remains deadline-bound after it
is returned, but a later consumption failure remains an exception because the
operation result has already completed. These transport results do not yet
perform multi-source aggregation and are not environment availability
observations.
The v3 source client owns service-index `PackageBaseAddress` discovery plus
version-index, exact-manifest, and exact-package URL construction. The legacy
`NuGetClient` delegates to that source-owned primitive and retains only its
compatibility choice to bypass canonical NuGet.org service-index discovery.
V3 symbol payload remains unsupported because the protocol has no
package-base-relative symbol download contract.

The [desktop typed source-client
boundary](#desktop-typed-source-client-boundary) is not a current-behavior
claim. #4653 is the implementation candidate; #4770 and #4776 supply its
adjacent owner-issued prerequisites, and the named Release gates define when
that candidate may claim the boundary.

The current implementation source-scopes downloaded package content and
candidate metadata, aggregates versions across sources while retaining the
reporting feeds, uses global-folder payloads only when their recorded producer
is authorized, applies layered `<packageSourceMapping>` configuration per
package id, preserves aliases through mapping before collapsing producers, and
threads caller options through routing and platform-pack probes. The remaining
implementation work includes:

- completing config and override semantics, including the complete NuGet config
  hierarchy and config-relative local source paths
  ([#3739](https://github.com/richlander/dotnet-inspect/issues/3739));
- carrying payload producer provenance through package inspection indexes,
  projected platform packs, and package-associated symbols
  ([#3738](https://github.com/richlander/dotnet-inspect/issues/3738));
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
