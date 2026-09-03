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

An HTTP declaration containing a query, fragment, or redacted credential-like
path component cannot use a durable key derived from that text. Hashing the
untreated value would retain a credential guess verifier, while using the
credential-free producer key would collapse distinct authorities. Such an
authority remains fully usable through its opaque runtime identity, but
cross-process candidate and payload cache reuse is unavailable until an
independent non-secret stable authority ID exists. A source name alone is not
sufficient because the same name can later designate another endpoint.

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
gates malformed HTTP syntax and an unusable credential-provider scope at the
live CLI boundary.

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
version evidence. A selected local authority is classified without
constructing HTTP state and currently produces the explicit
capability-unavailable result owned by #5400.
When the Gallery route cannot complete its registration listing-state join,
its retained flat-container candidates are explicitly partial rather than
authoritative.
Malformed selected declarations and unusable configurable authentication
scopes become attributed pre-client failures before transport construction.
Other valid selected authorities still run, and usable peer evidence is
reported as partial.

Offline version enumeration and the `--versions-with-feed`,
`--include-unlisted`, latest-version, range, payload, metadata, search, and
extraction paths remain on the legacy composition until their package-owned
adoption slices land. The process-global authentication decorator therefore
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
