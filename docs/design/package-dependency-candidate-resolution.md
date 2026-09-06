# Package dependency candidate resolution

This document owns the host-neutral composition from one normalized package
dependency declaration to one exact, source-authorized acquisition candidate.
It is tracked by
[#5765](https://github.com/richlander/dotnet-inspect/issues/5765).

**Status:** implementation contract.

## Owner and claim

**Package Dependency Candidate Query** in
`DotnetInspector.PackageQueries` owns:

> Given one caller-approved normalized dependency declaration and one
> package-source candidate capability, return the exact package candidate that
> NuGet dependency-range semantics select, or typed failure or incomplete
> evidence explaining why no candidate can be issued.

The query composes, but does not redefine, three owners:

- [Package Dependency Evidence](package-dependency-evidence.md) owns canonical
  package identity, canonical version constraint, declaration identity,
  framework scope, source spellings, and occurrence accounting.
- `PackageDependencyVersionRange` owns exact-declaration classification and
  NuGet version-range selection.
- [Package Source Model](package-source-model.md) owns configured authority,
  discovery completeness, source-result adoption, exact coordinates,
  reporting-authority evidence, operation deadlines, cancellation, and
  acquisition authority.

The query never parses a nuspec, reads a source configuration, opens a package,
selects a target framework, traverses a graph, or assigns Workspace capacity.
It does not select from `PackageVersionDiscoveryResult.SourceListings`; those
rows are presentation evidence, not acquisition receipts.

## Consumers and delivery

The first semantic consumer is
[Package Dependency Traversal](package-dependency-traversal.md), whose
implementation is tracked by
[#5996](https://github.com/richlander/dotnet-inspect/issues/5996). The
traversal query invokes this capability only for selected declarations under a
root that authorizes recursive source work. The unified CLI `depends` command
then consumes traversal under
[#5994](https://github.com/richlander/dotnet-inspect/issues/5994).

[Workspace Scope and Expansion](workspace-scope-and-expansion.md) is the
second consumer. Workspace performs its exact-package or package-prefix scope
test before invoking this query and retains candidate coalescing, Root
capacity, Artifact preparation, publication, and closure.

The current `DependencyResolutionService` is not an adapter consumer. It
parses legacy `PackageDependency` values inside its recursive tree walk and
does not possess owner-issued `PackageDependencyEvidenceDeclaration`
identities. Synthesizing those identities in the CLI or resolving only the
first edge through this query would create a second normalization path and
leave transitive behavior inconsistent. The production `depends` adoption
therefore occurs through the typed traversal sequence rather than a temporary
legacy bridge.

The package-owned `PackageAcquisitionCandidate` currency is nevertheless used
immediately by existing selected package acquisition. That production path
constructs candidate correspondence from the same retained authority-bearing
observations and limits payload acquisition to the candidate's reporting
authorities. The declaration query adds the dependency-specific consumer of
that shared currency without creating a test-only result shape.

## Contract shape

```text
caller-approved request
  - normalized declaration
  - optional owner-resolved project coordinate
  + package-source candidate capability
  + shared operation context
  + caller cancellation
        |
        v
Package Dependency Candidate Query
        |
        v
one closed result
  - resolved exact candidate + declaration + diagnostics
  - typed resolution or authorization failure
  - typed incomplete source evidence
```

The result always retains the complete normalized declaration. Two
declarations may therefore resolve to the same candidate correspondence while
remaining distinct declaration results.

## Request arms

The request is one of:

- **Declared** — classify and resolve the declaration's canonical constraint.
- **Restored coordinate** — validate an owner-issued
  `RestoredProjectPackageNodeIdentity` against the declaration, then authorize
  its exact coordinate without redundant version discovery.

The caller owns eligibility. A direct-only traversal root, closed Workspace,
or relationship outside a registered Workspace scope does not construct a
request and performs no source work.

The restored-coordinate arm is not permission to accept an arbitrary exact
version. The resolved coordinate's package ID must equal the declaration's
canonical package ID, and its version must satisfy the canonical constraint.
A mismatch is a typed correspondence failure.

## Exact declaration classification

`PackageDependencyVersionRange.GetExactVersion` is the sole discriminator:

- `[1.0.0]` and `[1.0, 1.0]` are exact;
- bare `1.0.0` is a minimum-inclusive range;
- bounded and floating ranges require discovery; and
- an omitted constraint is the NuGet all-stable range, not "latest."

An exact declaration is caller-pinned evidence. The source capability
authorizes the normalized coordinate against the package ID's configured
authority set without enumerating peer versions. At least one usable
authority is sufficient to issue the candidate. Configuration or
classification failures encountered beside usable authorities remain
diagnostic evidence on the successful result.

## Dependency-range discovery contract

Range selection uses one package-owned discovery contract:

- all versions are requested, with no caller result limit;
- prerelease observations are retained so the range owner, not a source
  filter, decides whether they satisfy the declaration;
- authoritatively unlisted versions are excluded from automatic range
  selection;
- every package-ID-eligible configured authority must settle; and
- the shared operation context spans every authority and route.

Listing-state interpretation remains owned by
[Package source model](package-source-model.md#metadata-only-version-queries).
Gallery discovery must complete its registration listing-state join before
the result can be authoritative. Local and generic V3 sources that do not
provide authoritative listing metadata retain the existing visible-candidate
convention; this query does not invent stronger listing evidence than the
source model supplies.

`PackageVersionDiscoveryResult` retains that complete contract. The query
rejects a result produced under another contract rather than treating
`Authoritative` alone as sufficient. In particular, a display listing limited
to one row cannot prove that a bounded or minimum-inclusive range has no
better NuGet match.

After authoritative discovery,
`PackageDependencyVersionRange.SelectBestSatisfying` selects from the
package-owned version sequence. The package owner then issues the exact
candidate from retained observations for that selected coordinate. The query
never reconstructs authority from a source label.

## Exact candidate currency

`PackageAcquisitionCandidate` is package-owned, immutable, and resource-free.
It contains:

- one normalized `PackageSourceCoordinate`;
- a closed origin: caller-pinned or discovered;
- the exact eligible acquisition authorities;
- for a discovered candidate, the owner-adopted
  `PackageCandidateObservation` corresponding to each reporting authority;
- the discovery contract when discovery selected the coordinate; and
- an opaque `PackageAcquisitionCandidateCorrespondence`.

Pinned candidates carry every usable authority authorized for that package ID
and no source observation. Discovered candidates carry only authorities whose
admitted observations reported the selected coordinate under the retained
discovery contract.

Candidate correspondence equality includes:

- normalized coordinate;
- pinned versus discovered origin;
- discovery contract when present;
- the issuing package-source context; and
- the reference-identity set of eligible acquisition authorities.

It does not use source labels, endpoint spelling, producer display, or list
object equality. Repeated resolution in one unchanged source context can
therefore coalesce the same candidate, while changed authority or discovery
context cannot inherit that correspondence.

The declaration remains outside candidate equality. Two declaration edges
that select the same exact candidate may share acquisition and manifest work
while retaining their distinct declaration identities, constraints, source
spellings, and graph edges.

## Result algebra

The query returns exactly one arm.

### Resolved

`Resolved` carries:

- the complete declaration;
- one `PackageAcquisitionCandidate`; and
- typed source diagnostics observed while issuing a safe pinned candidate.

Range selection is resolved only from authoritative discovery, so its
successful result has complete candidate evidence. A pinned result may retain
peer configuration diagnostics because one usable authorized authority is
sufficient for later exact acquisition.

### Failed

`Failed` carries one typed reason:

- **Authorization denied** — no usable authority is authorized for the exact
  coordinate;
- **No matching version** — authoritative dependency-range discovery contains
  no candidate satisfying the declaration; or
- **Resolved-coordinate mismatch** — an owner-resolved project coordinate
  names another package or does not satisfy the declaration.

Authoritative no-match is a statement about the declared range under the
retained source contract. It is not a claim that the package ID is globally
absent.

### Incomplete

`Incomplete` means finite source work could not establish the evidence needed
to issue a candidate.

For range selection, it retains the declaration, `Partial` or `Failed`
discovery state, the exact discovery contract, the number of admitted
candidate observations, and every typed `PackageAuthorityFailure`. For a
caller-pinned coordinate, it retains the declaration and the failures that
prevented the authorization operation from settling, including an operation
timeout.

Observed partial candidates never become an exact result, even when one
currently satisfies the range. A missing authority could change NuGet's
selected version.

## Operation and cancellation

The query accepts the caller's `NuGetOperationContext` and cancellation token.
When a context is supplied, it passes that exact context and matching caller
token unchanged to the source capability. Otherwise it creates one default
context for the complete query and passes that same instance through every
source operation and local selection step. Caller cancellation propagates as
cancellation carrying the caller token; it never becomes `Failed`,
`Incomplete`, or an authority failure.

An operation-ceiling timeout remains package-source incomplete evidence.
Neither the query nor a host retries after the shared operation has expired.

The returned result owns no stream, package archive, client, handler, or
operation context. Later acquisition uses the candidate's authority evidence
through a package-owned exact manifest or payload capability.

## Host adapters

The core depends only on `IPackageDependencyCandidateSource`. The interface is
limited to:

- authorizing one exact pinned coordinate; and
- performing dependency-range version discovery under the fixed complete
  contract.

`PackageAcquisitionCandidateIssuer` is the package-owned host-neutral
composition boundary. Given explicit `PackageSourceAuthorization` and one
owner-issued `PackageSourceOperationResult<PackageVersionResult>` per
authorized authority, it validates source associations, applies the
dependency listing/completeness policy, and issues the discovery aggregate
and candidate correspondence.

`AuthorizedPackageDependencyCandidateSource` binds the query interface to an
`IPackageSourceAuthorization` and caller-owned `IPackageSourceClient` values.
It is directly usable by Browser/Wasm with credential-free clients and by any
other host whose source authorization is already explicit.
`DesktopPackageDependencyCandidateSource` instead binds the same interface to
`DesktopPackageSourceComposition`, source options, credential-provider-aware
clients, and optional logging. Neither host implements range selection,
authority adoption, discovery completeness, or candidate correspondence.

The interface is not a universal source-realization protocol. It cannot
acquire payloads, manifests, symbols, metadata, or arbitrary query results.

## Determinism and validation

Candidate selection depends only on:

- the normalized declaration constraint;
- the complete retained candidate observations;
- the exact discovery contract; and
- NuGet version semantics.

Configured source order cannot change the selected semantic version.
Correspondence treats reporting authorities as a reference-identity set, not
as declaration precedence. Invalid or foreign authority/observation pairings
are rejected by the package owner before the query can consume them.

The implementation is gated in Release by:

| Property | Gate |
| --- | --- |
| Exact bracketed constraints bypass discovery | `CandidateResolution_ExactDeclarationUsesPinnedAuthorization` |
| Bare versions remain ranges | `CandidateResolution_BareVersionUsesCompleteDiscovery` |
| Bounded ranges use NuGet selection | `CandidateResolution_BoundedRangeSelectsAuthorizedCandidate` |
| Partial or failed discovery cannot select | `CandidateResolution_IncompleteDiscoveryDoesNotSelect` |
| Authoritative no-match is typed | `CandidateResolution_AuthoritativeNoMatchIsFailure` |
| Restored coordinates require declaration correspondence | `CandidateResolution_RestoredCoordinateMustSatisfyDeclaration` |
| Repeated exact candidates coalesce without erasing declarations | `CandidateResolution_CandidateCorrespondenceExcludesDeclarationIdentity` |
| Caller cancellation retains its token | `CandidateResolution_CallerCancellationPropagates` |
| Context-carried cancellation precedes local mismatch results | `CandidateResolution_ContextCancellationPrecedesRestoredMismatch` |
| Context-carried cancellation reaches pending source operations | `CandidateResolution_ContextCancellationReachesSourceOperation` |
| Pinned operation timeout remains incomplete evidence | `CandidateResolution_IncompletePinnedAuthorizationIsNotDenial` |
| A display-limited discovery result is not range evidence | `CandidateResolution_RejectsInsufficientDiscoveryContract` |
| Search observations cannot masquerade as complete enumeration | `CandidateResolution_RejectsNonEnumerationObservations` |
| Foreign authority observations are rejected | `CandidateResolution_ForeignAuthorityObservationIsRejected` |
| A Browser-shaped explicit source adapter issues the same candidate currency | `CandidateResolution_HostNeutralSourceIssuesRangeCandidate` |
| The query owns one shared operation when none is supplied | `CandidateResolution_QueryOwnsOneSharedOperationContextWhenOmitted` |
| Operation timeout stops later authorities, preserves attribution, and prevents publication | `CandidateResolution_OperationTimeoutStopsLaterAuthorities` |
| Operation timeout prevents publication after local range selection | `CandidateResolution_OperationTimeoutPreventsLocalSelectionPublication` |
| Gallery listing-state gaps cannot authorize range selection | `CandidateResolution_GalleryUnknownListingStateIsIncomplete` |
| Existing selected payload acquisition consumes the shared candidate currency | `AcquireSelected_NonReportingWarmCacheCannotSupplySelectedVersion` |
