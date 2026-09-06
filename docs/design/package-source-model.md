# Package source model

This document is the normative owner for package-source composition in
`DotnetInspector.Packages`. It defines how active configured source
declarations become package authorities, how package-source mapping selects
those authorities for one package ID, and how owner-issued source operations
become package-level candidate and payload results.

The owner composes policy and evidence. It does not infer authority from a
transport URL, producer label, display string, cache hit, or successful
response.

## Boundary

The package source model consumes these owner-issued inputs:

| Input | Owning contract | Use here |
| --- | --- | --- |
| Active configured source declarations and package-source mapping aliases | Package configuration | Select the declarations eligible for one canonical package ID. |
| HTTP endpoint admission and local-source classification inputs | Package configuration and [local package source identity](local-package-source-identity.md) | Classify before any source client or network authority exists. |
| Local search, version, manifest, payload, and failure outcomes | [Local folder package source](local-folder-package-source.md) | Consume bounded local source evidence without reinterpreting paths, layout, or host failures. |
| `PackageSourceAssociation`, `IPackageSourceClient`, `PackageSourceOperationResult<T>`, and source-result identity | [Browser package sources](browser-package-sources.md#nugetfetch-typed-source-result-identity) | Invoke protocol-independent operations and recover the exact caller authority from each result. |
| `NuGetOperationContext` and typed deadline failures | [Browser package sources](browser-package-sources.md#operation-context-handoff) | Share one caller identity and operation ceiling across every selected authority and route. |
| Plugin-authentication context and target authorization | [NuGet feed authentication](nuget-authentication.md#source-scoped-plugin-authentication-context) | Bind configurable V3 routes and compatibility requests to the selected configured authority. |
| Package-store publication and cache lookup | [Cache concurrency and publication](cache-concurrency.md) | Admit only candidate and payload entries authorized by the current package authority. |

It returns package-owned typed outcomes:

- a classified HTTP authority, classified local source, or pre-client failure;
- a resolved authority set for one package ID;
- candidate evidence pairing package authority with owner-issued producer and
  transport provenance;
- an authoritative, explicitly partial, or failed package aggregate;
- payload authorization pairing one exact coordinate with one or more
  configured authorities;
- credential-safe source failures or caller cancellation; and
- a payload whose lifetime remains owned by its caller.

This design does not own NuGetFetch identity, protocol discovery, endpoint
construction, retry, failure construction, authentication internals, deadline
mechanics, or stream translation. It does not own Core HTTP-pipeline
construction or offline diagnostic rendering. It does not define browser
source profiles, package-profile or CLI presentation, or local-folder feed
identity and acquisition. Canonical local identity is owned by
[Local package source identity](local-package-source-identity.md); folder-feed
capabilities are owned by
[Local folder package source](local-folder-package-source.md), and
package-level acquisition composition remains
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400);
package-profile projection remains owned by
[#4806](https://github.com/richlander/dotnet-inspect/issues/4806).

## Identity roles

The following roles are intentionally separate:

- A **configured alias** is the name and source declaration against which
  package-source mapping is evaluated. Several aliases may name one authority.
- A **configured package authority** is the package owner's canonical
  authorization unit. It determines candidate and payload eligibility.
- A **source association** is the opaque `PackageSourceAssociation` reference
  used to recover one configured package authority from a NuGetFetch result.
- A **source route** is one runtime client or local capability through which an
  authority can perform an operation. Several routes may implement one
  authority.
- **Producer identity** is NuGetFetch-owned, credential-free provenance.
  Several configured authorities may deliberately have the same producer.
- **Transport kind** records the protocol implementation that produced one
  result. It is evidence, not authority or precedence.

Configured alias names are not authority: a rename must not grant access to
another endpoint. Producer identity is not authority: it deliberately folds
query, fragment, credential rotation, and recognized credential-like path
slots. Transport kind is not authority: Gallery and V3 can implement one
authority, while two V3 routes can implement distinct authorities.

Each configured authority therefore has a package-owned opaque runtime
identity. Equality is owner-issued; no consumer recreates it from source text.
An authority can additionally have a durable cache key only when the package
owner can project a stable key without credential-dependent input while still
preserving every authority distinction.

For HTTP declarations, runtime authority equality canonicalizes only the
scheme, IDN host, effective port, percent-escape hex casing, and one trailing
path slash. It preserves the raw path, query, and fragment spelling, including
encoded-unreserved characters, dot segments, repeated slashes, and an empty
query or fragment marker. This stricter process-local key is neither the
NuGetFetch producer identity nor the legacy persistent-cache key. It must not
be rendered, persisted, or hashed into a cache path.

An HTTP declaration containing configured credentials, a query, fragment, or
redacted credential-like path component cannot use a durable key derived from
that text. Hashing the untreated value would retain a credential guess
verifier, while using the credential-free producer key would collapse distinct
authorities. Such an authority remains fully usable through its opaque runtime
identity, but cross-process candidate and payload cache reuse is unavailable
until an independent non-secret stable authority ID exists. A source name
alone is not sufficient because the same name can later designate another
endpoint.

Portable browser source IDs and owner-issued canonical local identities may
provide such an independent stable ID under their own contracts. The package
owner combines that ID with a versioned authority namespace; it does not hash a
credential-bearing URL to fill a missing identity.

`ConfiguredAuthority_QueryDistinctSameProducerSourcesRemainDistinct`,
`ConfiguredAuthority_CredentialPathRotationsRemainDistinctWithoutDiagnosticDisclosure`,
`ConfiguredAuthority_RawPathDistinctionsRemainSeparate`,
`PackageVersionListing_EncodedPathCredentialDoesNotCrossToLiteralPath`, and
`SourceClientComposition_PreservesRawProviderQuerySpelling`, together with the
authentication owner's
`CredentialRequestPreservesOriginalSourceSpelling`, are the required Release
gates for these distinctions.

## Classification precedes authority and transport

Classification consumes source syntax and its resolution base before any HTTP
producer, source association, authentication context, or runtime client is
created.

| Classification | Consequence |
| --- | --- |
| Admitted absolute `http` or `https` endpoint | The package owner may mint an HTTP configured authority and compose owner-issued HTTP routes. |
| Plain local path or `file://` source | The value goes only to the local-source identity owner. It never enters HTTP endpoint or client construction. |
| Unsupported scheme, malformed value, or unusable local input | A pre-client failure names the configured alias safely. It creates no package authority, authentication context, runtime client, or network request. |

Relative config paths resolve from the declaring config file; relative
command-line paths resolve from the command working directory. Path
canonicalization, `file://` equivalence, and platform case behavior are owned
by
[Local package source identity](local-package-source-identity.md). Folder
enumeration and local payload operations are owned by
[Local folder package source](local-folder-package-source.md). Package-level
adoption remains
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400). This owner
only dispatches to those boundaries and preserves their results.

A caller may bind ambient configuration discovery to an explicit absolute
directory. That directory replaces the process working directory only as the
starting point for the `NuGet.Config` hierarchy; it does not change the base
for a relative command-line source. This lets a replay consumer retain the
configuration context of an earlier command without converting the hierarchy
into one synthetic config file or reinterpreting it from the replay directory.
`ReplayConfigDirectory_DoesNotRebaseAnExplicitRelativeSource` and
`ReplayConfigDirectory_RejectsARelativeSourceResolutionBase` are the Release
gates for that handoff.

When the local owner classifies a valid authority but the current host lacks a
requested local capability, the package result retains that authority and a
typed capability-unavailable cause. It is not an HTTP failure, package absence,
or reason to contact the declaration as a URL. It makes an operation that
needs that source's evidence incomplete.

Network capability is checked after classification and authority resolution.
Offline mode may use an authorized cache entry, but it cannot convert an HTTP
route into a local source or an unsupported declaration into an HTTP request.

The classification boundary is gated by
`SourceClassification_PlainDirectoryNeverConstructsHttpTransport`,
`SourceClassification_FileUriNeverConstructsHttpTransport`,
`SourceClassification_UnsupportedSchemeCreatesNoAuthorityOrRequest`, and
`LocalCapabilityAbsence_IsVisibleNonHttpAndIncomplete`.
`PackageVersionListing_UnusableSourceSetupIsTypedBeforeTransport` additionally
gates malformed HTTP syntax, hosts rejected by NuGetFetch endpoint projection,
and an unusable credential-provider scope at the live CLI boundary.

## Resolving active and eligible authorities

Source resolution proceeds in two distinct domains:

1. Determine active configured aliases.
2. Evaluate package-source mapping against alias names for the canonical
   package ID.
3. Classify only the selected aliases.
4. Collapse selected aliases that the package owner proves designate one
   configured authority.

Collapsing before mapping would let an ineligible alias authorize an eligible
endpoint. Mapping therefore retains alias identity until the package-specific
selection is complete. A mapping-enabled configuration that matches no
pattern, maps only to inactive aliases, or is malformed is a typed mapping
failure and authorizes no source.

Aliases collapse only when their package-owned classified authority is equal.
They must also agree on credentials, provider-query authority, and every
route-affecting policy. Conflicting aliases fail before any client is created;
the owner does not pick the first declaration.

Declaration order has no semantic-version or same-tier payload precedence. It
may make diagnostics stable, but it cannot decide which version wins or
authorize bytes. `PackageSourceMapping_SelectsAliasesBeforeAuthorityCollapse`,
`ResolveSourcesForPackage_MappingClassifiesOnlySelectedAliases`,
`PackageSourceMapping_ConflictingAliasPoliciesFailBeforeClientCreation`, and
`SourceOrder_DoesNotChooseVersionOrSameTierPayload` gate these rules.
`ResolveSourcesForPackageWithFailures_RetainsValidPeer`,
`ResolveSourcesForPackageWithFailures_MappedUnsupportedAliasIsNotInactive`,
and `PackageVersionListing_UnsupportedConfiguredSourceRetainsValidPeer` gate
per-alias pre-client failure without suppressing valid selected authorities.

## Associations, routes, and authentication

The package owner creates exactly one `PackageSourceAssociation` for each live
configured authority and retains the reverse map. Every NuGetFetch route
composed for that authority receives the same association. Distinct
authorities receive distinct associations even when their producer identities,
network origins, or source names are equal.

Several routes may implement one authority only after authority equality and
policy compatibility are established. Shared producer identity is never that
proof. The built-in Gallery and canonical NuGet.org V3 transports can be
routes of one configured authority only when the host explicitly supplies that
composition; a consumer cannot infer it from the producer key or hostname.

Route order is failover inside one authority, not precedence among configured
authorities. A route success settles its authority. A request timeout,
transport failure, or unsupported capability can permit the next applicable
route while the shared operation context remains live. The authority fails
only after no applicable route can produce the required evidence.

For configurable V3 routes, the package owner supplies the same association
and canonical authority decision consumed by the authentication owner. The
provider-query URI retains the exact configured spelling selected for that
authority; parsing it for resource authorization cannot replace its plugin
lookup identity. Reusing or disposing a route does not independently create or
retire authentication authority. Replacing or releasing the configured
authority retires its authentication context under that owner's contract.
Gallery remains plugin-authentication-free.

Package-layer compatibility requests must execute through the selected
authority's source-bound request policy. A feed-advertised or redirect target
does not acquire authority from its URL. The authentication owner decides
whether that concrete target can use the route's context; an out-of-scope
target is sent without plugin authorization and its response remains visible.
If no owner-issued typed route can perform a compatibility request, the package
operation reports an unsupported capability rather than issuing a bare
`HttpClient` request.

These boundaries are gated by
`SourceClientComposition_OneAssociationPerAuthorityAcrossRoutes`,
`GalleryV3Composition_RequiresOwnerIssuedAuthorityEquality`,
`CompatibilityRequest_CrossScopeAuthenticationIsSuppressed`, and
`CompatibilityRequest_SameScopeAuthenticationRemainsAvailable`.

## Adopting source results

Every NuGetFetch result carries its exact caller association, producer
provenance, and transport kind. The package owner adopts a result only by
reference lookup of the returned association in the live authority map.
Producer equality, producer display, producer key parsing, legacy
`PackageSourceIdentity.Value`, transport kind, endpoint parsing, and source
declaration order are invalid recovery mechanisms.

An unknown, retired, or foreign association is a contract failure. Its
candidate, manifest, payload, or failure cannot enter an aggregate or cache.
The package owner preserves owner-issued producer and transport provenance
without changing their equality or rendering.

Candidate evidence pairs:

- the package-owned configured authority;
- the exact normalized coordinate;
- the owner-issued discovery contract and listing state; and
- the owner-issued producer and transport provenance.

Payload evidence additionally pairs that authority with the exact requested
coordinate. A payload result whose coordinate or association differs from the
request is rejected and disposed under the source-result lifetime contract.

`SourceResultAssociation_ForeignSameProducerResultIsRejected` and
`SourceResultAssociation_ExactAuthorityAndCoordinateAreRequired` gate this
boundary.

## Candidate aggregation

Candidate discovery queries every active, package-ID-eligible configured
authority whose semantics can affect the requested result. Routes of one
authority produce one authority outcome; they do not become independent votes.

A per-authority outcome is one of:

- authoritative candidate evidence, including an authoritative empty set;
- a typed capability absence;
- a typed source failure;
- a request timeout;
- a terminal operation timeout; or
- caller cancellation.

Package aggregates then have these states:

| State | Meaning |
| --- | --- |
| **Authoritative** | Every required authority settled with evidence sufficient for the requested contract. |
| **Partial** | Some usable evidence exists, but at least one required authority could not provide sufficient evidence. The result carries every retained candidate and every incomplete authority cause. |
| **Failed** | No safe result can satisfy the requested contract, configuration or mapping denied the operation, or the shared operation ceiling expired. |

`NotFound` and an authoritative empty version result are source-relative
evidence. Authentication, transport, timeout, malformed response, unsupported
capability, and unimplemented local capability are not absence.

Raw search or version enumeration may expose an explicit partial result when
its caller accepts partial evidence. Latest, wildcard, and range selection
cannot select from partial evidence when the missing authority could change
the answer. Authoritative package absence likewise requires authoritative
absence from every required authority. A requested result limit is not
incompleteness when each source operation establishes the bounded query
contract; an owner-issued source or page limit is.

Source order never chooses a semantic version. Selection applies the
version-resolution contract to the union of authority-bearing candidate
evidence only after the aggregate has sufficient completeness for that
operation.

The package owner may project those retained observations into one immutable,
resource-free `PackageAcquisitionCandidate`. A caller-pinned candidate carries
every usable authority eligible for its exact coordinate. A discovered
candidate carries only authorities whose adopted observations reported its
coordinate under the retained discovery contract. Its opaque correspondence
identity binds the coordinate, candidate kind, discovery contract, issuing
source context, and authority reference-identity set.

`PackageAcquisitionCandidateIssuer` is the host-neutral construction boundary
for hosts that already possess explicit `PackageSourceAuthorization` and
owner-issued `PackageSourceOperationResult<PackageVersionResult>` values. It
adopts those results, applies package-owned completeness and listing policy,
and issues the same candidate currency without requiring desktop
configuration or credential-provider services. Host adapters invoke source
clients; they do not reproduce candidate aggregation or correspondence.

The
[Package Dependency Candidate Query](package-dependency-candidate-resolution.md)
uses that currency for NuGet dependency constraints. It may choose a version
only from a discovery result whose complete retained contract admits
dependency-range selection; `Authoritative` without that contract is
insufficient.

The Release gates are
`Discovery_AllEligibleAuthoritiesMustSettleBeforeAuthoritativeSelection`,
`Discovery_UnreadableAuthorityCannotBecomePackageAbsence`,
`Discovery_PartialEvidenceCannotSelectLatestWildcardOrRange`, and
`Discovery_SourceOrderCannotChangeSelectedVersion`.

## Exact payload acquisition

A discovered coordinate can be acquired only from an authority that reported
that coordinate under the discovery contract used to select it. A pinned exact
coordinate is caller-supplied evidence and may be requested from any authority
eligible for that package ID.

An exact pinned acquisition may succeed from one authorized authority without
proving peer authorities readable. This is intentionally different from
candidate aggregation: the caller already chose the coordinate, and one
authorized byte source is sufficient. Peer authentication, transport, or
capability failures remain available as diagnostics but do not turn a
completed authorized payload into a partial payload.

Cold acquisition preserves NuGet's local-before-HTTP source tiers. There is no
precedence within one tier. A cached payload may answer before an uncached
authority is probed only when its retained authority is currently authorized
for that coordinate.

Symbols, manifests, RID companions, tool-wrapper redirects, and projected
platform packs independently reapply the package-ID and coordinate authority
rules. Existence of a primary package on one authority does not authorize a
related package or symbol endpoint on another.

`PinnedAcquisition_OneAuthorizedAuthorityMaySucceedWithoutPeerReadability`,
`DiscoveredPayload_RequiresReportingAuthority`,
`PayloadTier_LocalBeforeHttpWithoutDeclarationPrecedence`, and
`RelatedCoordinate_RecomputesPackageAuthority` gate these rules.

## Shared operation context and payload lifetime

One public package operation creates or consumes one `NuGetOperationContext`
and passes that exact instance to every selected authority and every route,
including local routes. A new source, retry, compatibility request, redirect,
or route fallback creates no new operation ceiling.

A request deadline can fail one route and permit another applicable route or
authorized authority while time remains. An operation-ceiling timeout is
terminal: outstanding work is cancelled, no later route starts, and collected
candidate evidence cannot be published as authoritative or used for automatic
selection. Concurrent lower-precedence transport failures cannot hide the
typed operation timeout.

Caller cancellation remains cancellation carrying the original caller token.
It does not become a source failure, partial result, or operation timeout.

When a source operation returns a payload stream, the caller owns that stream
and must keep the shared context alive until consumption or disposal
completes. The package owner does not dispose an externally supplied context.
It disposes only a context it created, and only after every owned source
operation and payload stream has settled.

The gates are
`OperationContext_RequestTimeoutMayFailOverWithinRemainingCeiling`,
`OperationContext_OperationTimeoutIsTerminalAcrossAuthorities`,
`OperationContext_CallerCancellationRetainsOriginalIdentity`, and
`PayloadLifetime_SharedContextOutlivesReturnedStream`.

## Candidate and payload stores

Candidate entries are keyed by:

- configured package authority;
- canonical package ID; and
- the complete discovery contract, including any contract version and options
  that change the candidate set.

Payload entries are keyed by configured package authority and exact normalized
coordinate. Both entry kinds retain producer and transport provenance as
evidence, never as authorization.

Query-distinct or credential-path-distinct configured authorities therefore do
not share entries merely because NuGetFetch gives them one producer identity.
Aliases already proven to be one authority may share. An authority without a
credential-safe durable key can use only authority-scoped process-local cache
state; it cannot fall back to a producer-keyed persistent entry.

The NuGet global packages folder is a payload cache, not a candidate source.
Its `.nupkg.metadata.source` must resolve unambiguously to an authority
currently authorized for the exact coordinate. Missing or ambiguous metadata,
an inactive authority, or producer equality without authority equality is a
cache miss.

Changing source selection, package-source mapping, credentials, or configured
authority never reinterprets an old entry as newly authorized. Cache namespaces
that used endpoint strings or NuGetFetch producer identity as authority are
legacy and must migrate to a new versioned namespace rather than relabeling
existing bytes.

`CandidateCache_QueryDistinctAuthoritiesDoNotShareEntries`,
`PayloadCache_QueryDistinctAuthoritiesDoNotShareEntries`,
`CredentialBearingAuthority_HasNoProducerKeyedPersistentFallback`,
`GlobalPackages_ProducerEqualityCannotAuthorizePayload`, and
`LegacyProducerScopedCache_IsNotReinterpretedAsAuthorityScoped` gate these
properties.

## Enrichment is a separate capability

Package metadata enrichment is not evidence for candidate or payload
authority. NuGet.org-specific downloads, publication dates, deprecation,
vulnerability, and registration metadata can run only when the canonical
NuGet.org authority is active and package-ID-eligible. Another source's
package identity is never sent to NuGet.org merely because the enrichment
service exists.

Enrichment failure does not revoke already established candidate or payload
authority. It remains a separate typed capability failure. Search, version
selection, and payload acquisition do not gain authority from enrichment data.

## Failure semantics

Failures identify their package authority and preserve the owner-issued
producer, transport, capability, coordinate, failure kind, and typed deadline
detail when those facts exist. They do not retain raw configured source text,
credentials, endpoint-bearing exception messages, response bodies, or archive
content.

Configuration, mapping, classification, source operation, aggregate
incompleteness, and caller cancellation remain distinguishable. No layer turns
an unreadable source into package absence, unsupported capability into a
transport error, request timeout into operation timeout, or cancellation into
an empty result.

CLI wording and structured-output projection remain presentation-owner work.

## Executable interaction model

The
[package source composition TLA+ model](models/package-source-composition/README.md)
checks the stateful portion of this design: concurrent authorities, ordered
route fallback inside one authority, exact association adoption, complete
versus partial discovery, pinned success from one authority, request timeout
fallback, and terminal operation timeout.

The model assumes already-classified eligible authorities and owner-issued
source outcomes. It does not model source syntax, URL or path canonicalization,
package-source mapping, authentication internals, persistent cache
construction, payload bytes, or implementation correspondence. The Release
gates named throughout this document remain the implementation evidence.

## Implementation boundary

Desktop adoption is staged. Ordinary online
`package <id> --versions` is the first package-owned consumer: it resolves
configured authorities, creates one association per authority and one
plugin-authentication context per configurable V3 authority, uses the
credential-free Gallery route for the exact anonymous NuGet.org authority,
uses one operation context across those authorities, adopts each typed result
through the exact association, and reports authoritative, partial, or failed
version evidence. A selected local authority invokes the existing bounded
NuGetFetch local-folder client without constructing an HTTP transport or
authentication context. Its complete version observations, including an empty
result, join the same aggregate as HTTP evidence through exact association
lookup. Unavailable local capability, missing roots, invalid archives, and
source limits remain attributed failures rather than package absence. The
NuGetFetch host contract is unchanged: desktop filesystems support local reads;
Browser/Wasm without a filesystem capability returns typed unsupported.
When the Gallery route cannot complete its registration listing-state join,
its retained flat-container candidates are explicitly partial rather than
authoritative.
Malformed selected declarations and unusable configurable authentication
scopes become attributed pre-client failures before transport construction.
Other valid selected authorities still run, and usable peer evidence is
reported as partial.

Online metadata-only version queries also use this composition: pinned
verification, latest-version, range enumeration, `--versions-with-feed`, and
`--include-unlisted`. Latest and range selection require authoritative
discovery; a healthy subset cannot choose the answer. A pinned verification
can report an observed exact coordinate with peer failures disclosed, but
cannot infer absence from unreadable peers. Failed operations, including the
terminal operation deadline, publish no query rows.

Online caller-pinned extraction and the discovered-coordinate acquisition seam
now adopt configured authorities as described below. Offline version queries
and extraction, metadata, search, and unmigrated payload consumers remain on the
legacy composition until their package-owned adoption slices land.
The process-global authentication decorator therefore
also remains solely for those legacy paths; it cannot be removed until they no
longer depend on it. This first live slice does not read or publish the legacy
producer-keyed version-list cache; authority-safe cache adoption remains a
later slice.

The current desktop paths that derive cache authorization from source URL
digests, collapse sources by producer-shaped endpoint identity, or iterate an
ordered source list are legacy behavior. They cannot claim this target
contract.

Implementation is complete only when the named non-vacuous Release gates
exercise the package-owned authority types and outcomes. Each gate must include
the positive behavior in its name and a close negative case that would pass if
producer identity, transport kind, source order, or a healthy subset were
mistakenly used as authority. Existing NuGetFetch and authentication gates
remain evidence for their owners; they do not substitute for these
package-composition gates.

### Local version-listing adoption

The CLI consumer is ordinary `package <id> --versions --source <folder>` (or a
mapped folder in `NuGet.Config`). Local and HTTP version evidence use the same
operation context, filtering, sorting, and final result limit. Directory layout
recognition and finite observation limits remain owned by NuGetFetch.

`PackageVersionListing_LocalFolderReadsVersionsWithoutHttpTransport`,
`PackageVersionListing_LocalMappingPrecedesCollapseAndKeepsDistinctRoots`,
`PackageVersionListing_LocalAndHttpUnionIsSortedBeforeLimit`,
`PackageVersionListing_EmptyLocalRootIsAbsenceButMissingRootFails`,
`PackageVersionListing_LocalFailureRetainsHttpPeerAsPartial`,
`PackageVersionListing_HttpFailureRetainsLocalPeerAsPartial`, and
`OperationContext_RequestTimeoutContinuesToLaterAuthorityWithinCeiling` are the
Release gates for this adoption. The existing terminal-operation-timeout and
HTTP source-association gates remain unchanged.

The production adoption path is tracked by
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400) in six steps:
configured authorities, ordinary version listing, metadata-only version
queries, caller-pinned payload/cache authority, discovered-coordinate
payload acquisition (latest/wildcard/range), and remaining CLI consumers/legacy
retirement. Exact pins are independently useful; splitting their adoption from
discovered coordinates avoids interpreting a legacy producer restriction as
reporting-authority evidence. The user explicitly approved
CLI-only continuation ("CLI is good enough. proceed"); browser adoption is
not a prerequisite for this workstream. These slices do not add browser
filesystem registration or claim that offline local discovery is supported.

### Caller-pinned payload acquisition

The production consumers are ordinary online single-package
`package <id>@<version>` inspection and exact package pins in the API resolver
used by `type`, `member`, and `match`, through
`PackageExtractor.ExtractPinnedPackageAsync`. These exact-pin paths use the same
configuration, mapping, client ownership, and association lookup as version
queries. An eligible cache hit
precedes cold acquisition; cold local sources precede HTTP sources regardless
of declaration order. A pin needs one usable authority, not readable peers.
Failures encountered before success remain available as typed diagnostics and
in verbose CLI output. Local archive enumeration remains NuGetFetch-owned.

The desktop store reuses existing admission and atomic publication. Local
authorities publish into `package-authority-content-v1`, keyed by their
`authority-v1` identity and exact coordinate, with producer evidence retained
separately. The old `package-content-v5` family remains for unmigrated consumers
and is never reinterpreted as this new namespace. Local global-packages reuse
requires the metadata source to resolve to the same canonical local authority.

HTTP authorities currently have no durable cache identity. Their admitted
payloads use authority-scoped temporary filesystem materialization, retained
until the extraction consumer calls the existing cleanup API. They do not
read or write persistent payload or derived package-index entries, including
HTTP global-packages entries. Temporary ownership is independent of the final
payload's cache origin: a cached redirect target does not release the consumer
from cleaning up earlier HTTP wrapper materialization. This deliberately trades
cross-invocation cache reuse for correct authority; it does not invent a durable
HTTP key from a credential-bearing endpoint or producer digest. Local derived
package indexes use the persistent authority key.

One operation context spans exact acquisition, stream consumption, publication,
and exact tool-wrapper redirects. Each redirect recomputes package-ID
authorization from the original source policy. The returned extraction retains
the configured authority separately from producer provenance. Remote metadata
enrichment is restricted to that resolved source representation; local payload
inspection uses archive metadata instead. All-library Integration inspection
uses its existing materialized-input path rather than reacquiring the root
through the legacy producer-authorized artifact path.

The exact-pin slice does not migrate multi-package inspection, offline
extraction, automatic payload selection, dependency commands, symbols,
manifest-only requests, platform projection, or workspace artifact acquisition.
Selected package and API/timeline range adoption are described below.
Those callers retain `ExtractPackageAsync` and its producer-keyed single-flight
registry until their own handoffs migrate. The new path does not join those
legacy flights or share caller-owned temporary directories across extractions.
Legacy discovered-coordinate restrictions remain on their existing path until
their resolver can carry reporting authorities end to end. The CLI-only
approval above covers this adoption; the store-independent composition API
remains reusable by a future browser host.

Release gates are `ConfiguredPayloadAcquisitionTests` (real local/HTTP payloads,
source tiers and mapping, partial peer failure, terminal deadlines,
cancellation, stream/commit lifetime, temporary extraction, and redirected
package authorization), `AuthorityScopedPackageStoreTests` (authority slots
and namespace/provenance separation), and
`PackageInspectorMetadataSourceTests.InspectAsync_ConfiguredAuthoritiesDoNotShareProducerIndexes`
(derived-index isolation). Existing source-routing, store-publication,
legacy extraction/concurrency, and payload-admission suites retain their
contracts.

### Discovered-coordinate payload acquisition

The production consumer in step 5 is ordinary online single-package inspection
with an omitted version, `@latest`, `--preview`, or a wildcard version. The
package layer also supplies an authority-preserving range-address acquisition
seam. The API and timeline consumers described below adopt retained range
discovery in the first focused part of step 6. Ordinary `package` payload
inspection still does not accept a range or `--at`.

Selection consumes complete, current candidate evidence from every eligible
configured authority. Each retained observation preserves its configured
authority, normalized coordinate, discovery contract, listing state, and
producer provenance. Per-feed display rows and legacy source-URL restrictions
are not selection receipts. Partial evidence, failed discovery, or an expired
operation cannot reach payload-cache lookup or acquisition.

Latest selects the highest listed version, excluding prereleases unless
requested. Wildcards retain the existing case-insensitive version-prefix
semantics, including matching prereleases. Range addresses retain
`PackageVersionVector`'s inclusive endpoints, caller direction, prerelease
rules, and explicit address selection. Metadata-only range enumeration stays
separate from acquisition.

Only authorities whose admitted observations reported the selected coordinate
under that selection contract may supply its payload, including cache hits.
Another eligible authority's warm cache is not a substitute for reporting
evidence. Among reporters, the existing cache-first and cold-local-before-HTTP
rules apply. Selection, acquisition, and tool-wrapper traversal share one
operation context; each redirected package independently reapplies its own
package-ID authority rather than inheriting the root's reporting set.

This path does not consult legacy producer-keyed candidate caches or use
payload-directory scans as candidate evidence. Selection therefore performs
fresh bounded discovery, including when an omitted-version request previously
used a legacy version-cache hit. `@latest` keeps its fresh-discovery meaning.
Authority-safe candidate caching remains deferred; local payload caching and
HTTP temporary ownership reuse the caller-pinned path unchanged.

The same six-step plan and recorded CLI-only approval apply. Multi-package
inspection, floating API selection, other consumers, offline behavior, and
corresponding legacy retirement remain in step 6. Existing Markout-backed
package views and
file/content projection remain the rendering boundary; no new default output
section is introduced.

The Release gates in `ConfiguredPayloadAcquisitionTests` cover this contract:
`PackageCommand_LocalSelectionPrintsSelectedPayload` and
`PackageCommand_SelectionRefreshesWithWarmLocalPayload` exercise the live
consumer and fresh discovery; `AcquireSelected_PartialDiscoveryDoesNotProbePayloadCaches`,
`AcquireSelected_NonReportingWarmCacheCannotSupplySelectedVersion`,
`AcquireSelected_QueryDistinctAuthoritiesDoNotShareReportingEvidence`, and
`AcquireSelected_GalleryListingEvidenceControlsAuthorization` distinguish
reporting authority from availability, producer identity, and listing state.
`AcquireSelected_RangeRetainsAddressDirectionAndReporter` gates the range seam.
The external-operation deadline, commit-lifetime, caller-cancellation, and
selected-wrapper tests gate the shared operation and temporary ownership.
Existing caller-pinned acquisition and authority-store gates remain applicable
to their shared implementation.

### API and timeline range consumers

Online API range inspection (`type`, `member`, and `match` with `--at`) and
`timeline` retain one complete configured-authority discovery together with
their immutable version vector. Every selected address consumes that same
evidence; an address is not converted into an unrestricted caller pin or a
producer-key restriction. Sparse and dense timeline evaluation do not
rediscover the vector between cells. Incomplete discovery prevents payload
acquisition, even when a healthy peer or a non-reporter's cache has bytes.

These vectors preserve the existing listed-only API/timeline policy. An
unlisted observation does not admit an endpoint or authorize its acquisition;
another authority's listed observation can independently admit it. Local and
V3 authorities without Gallery listing semantics retain their existing visible
listing convention. Metadata-only `package --versions` can include unlisted
rows and is a different operation, so its ordinals need not match a listed-only
vector. Caller-pinned API inspection can acquire an unlisted exact coordinate
without candidate discovery.

Opening a range is metadata-only. API inspection requires an explicit address;
timeline without `--at` renders unevaluated cells, repeated `--at` selects sparse
cells, and `--at all` explicitly requests dense evaluation. The existing
operation ceiling spans discovery and acquisition. Each successful extraction
owns independent temporary storage, valid after range disposal. Projection
consumes the already acquired package through the existing assembly-selection
policy rather than opening another package-acquisition path. Failed projection
and completed inspection release their transferred temporary storage.

Executable replay retains the source policy as well as the coordinate.
`match --similar` projects reporter authorities' configured source spellings
into a new exact-package invocation, retaining config and mapping context.
When the original policy already names only reporters, it can be reused
without disclosing endpoints. Those spellings are new invocation inputs,
never in-process authority receipts. Redirected final packages use their own
package policy, not the root's reporter set. Timeline recommendations retain
the original range source policy, config-discovery directory, TFM, prerelease,
and visibility options. Both use shared CLI quoting and disclosure checks;
undisclosable source values require config-backed replay instead of a lossy or
credential-bearing command.

The observable rendering remains the existing Markout-backed API, match, and
timeline views. This slice introduces no output section and does not migrate
API floating/wildcard selection, offline behavior, dependency acquisition,
multi-package commands, symbols, or workspace acquisition.

Release gates in `ConfiguredPayloadAcquisitionTests` are
`OpenRange_OneMetadataDiscoveryServesMultipleAddressesAndReporters`,
`Range_NonReportingWarmLocalCacheCannotAnswer`,
`OpenRange_GalleryUnlistedEndpointNeedsIndependentReporter`,
`ApiRange_LocalFeedSelectsTheRequestedAddress`,
`RangeConsumers_UnreadablePeerFailsBeforePayload`,
`TimelineRange_OneDiscoveryAcquiresOnlyExplicitAddresses`,
`TimelineRange_ProbeReplayRetainsWorkingDirectoryAndSelectionPolicy`, and
`ApiRange_ProjectionFailureCleansTransferredTemporaryDirectory`.
The range lifetime cases cover independent successful roots and wrapper
policy. `MatchDiscoveryTests` covers exact replay with local, HTTP, mapped,
ambient-config, and query-distinct sources. `AssemblySetResolverTests` covers
the reused TFM selection and transferred ownership.

### Metadata-only version queries

The consumer is the CLI version-query family, not package inspection. The
aggregate projects adopted observations into existing `PackageVersionInfo`
and `PackageVersionSourceInfo` presentation models. These projections are not
payload authorization receipts. Existing Markout-backed output paths retain
their Markdown, TSV, JSONL, row-window, and count shapes.

Listing state is per authority. An unlisted Gallery row is hidden by default
even if another authority lists that version; the merged listing is visible
if any authority lists it. Local/V3 sources without listing semantics retain
the existing visible/`listed` presentation convention. Feed labels are
credential-safe presentation only; colliding labels get operation-local
ordinals, never hashes of HTTP authority keys.

Limits apply to distinct versions after the union, not source rows. Range
limits apply after inclusive endpoint resolution in caller direction.
Explicit latest queries exclude unlisted versions even when their output
requests the listing column. Pinned queries enumerate including prereleases
and unlisted coordinates, compare normalized versions, and do not consult
legacy payload caches online. Raw partial listings (including `--versions 1`)
retain warnings; bare `--version`, explicit latest, and range queries fail
before rendering when evidence is partial.

`CliVersionQueries_LocalSelectorsUseCompleteEvidence`,
`CliVersionQueries_PartialEvidenceCannotSelectLatestOrRange`,
`CliVersionQueries_PinnedEvidenceDoesNotRequireReadablePeers`,
`CliVersionQueries_ListingLensesPreservePerAuthorityRows`, and
`CliVersionQueries_SourceOrderCannotChangeLatest` are the Release gates for
this slice. Payload-selecting wildcard/latest/range paths remain outside this
claim and migrate with their authority-preserving payload handoff.

### Reusable authority authorization

The reusable authorization seam projects selected package sources into
package-owned configured authority objects. It uses the same runtime authority
key as desktop source composition, mints one opaque source association per
authority, supports exact reverse lookup, and supplies a versioned persistent
cache key only when the package owner can form one without retained credentials
or collapsed authority distinctions. Alias mapping remains earlier than
authority collapse, and local and HTTP declarations are classified without
constructing a transport. An authority object and its association live for one
authorization answer; result adoption uses that answer's reverse map.
Host-supplied independently authorized sources remain distinct unless their
policy owner has already selected and collapsed aliases with equivalent
authority keys and policy.

The Release gates
`ConfiguredAuthority_QueryDistinctSameProducerSourcesRemainDistinct`,
`PackageSourceAuthorization_QueryDistinctAuthoritiesHaveExactAssociations`,
`PackageSourceAuthorization_CredentialPathAuthoritiesHaveNoPersistentKey`,
`PackageSourceAuthorization_HttpAuthorityWithoutStableIdHasNoPersistentKey`,
`SourceClassification_PlainDirectoryNeverConstructsHttpTransport`,
`SourceClassification_FileUriNeverConstructsHttpTransport`,
`SourceClassification_UnsupportedSchemeCreatesNoAuthorityOrRequest`,
`PackageSourceMapping_SelectsAliasesBeforeAuthorityCollapse`, and
`PackageSourceMapping_ConflictingAliasPoliciesFailBeforeClientCreation`
enforce this seam.

Typed route composition, exact result adoption, and version discovery are live
for the online desktop consumer described above. Payload and cache
authorization plus the remaining consumer migrations remain later slices of
[#5400](https://github.com/richlander/dotnet-inspect/issues/5400). The legacy
`Sources` projection remains available during those migrations; it is not an
alternative authority identity.
