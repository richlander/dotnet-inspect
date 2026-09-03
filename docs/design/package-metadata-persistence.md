# Package metadata persistence

## Status

Target design for
[#5601](https://github.com/richlander/dotnet-inspect/issues/5601).
The current `metadata-v7` implementation has useful source separation,
one-hour expiry, present and absent markers, full-query force-refresh bypass,
and several partial-result nonpublication paths. It does not yet consume
package-owned configured authority, prove complete current-format
serialization, or preserve every meaningful metadata state. The complete
contract is therefore **unverified**.

## Decision

Package-metadata persistence owns one decision: whether a time-bounded
observation for one exact configured package authority, normalized package
coordinate, and full metadata projection may replace a fresh metadata
operation.

A lookup returns a reusable present observation, a reusable authoritative
absence observation, or a miss. Absence is evidence observed during one
bounded freshness window, not an immutable claim about a package coordinate.
Live partial or indeterminate operations remain visible to their caller but
cannot become reusable observations.

## Change classification and complexity

This design corrects an existing metadata-cache optimization used by the
existing `PackageInspector` consumer.
[#5601](https://github.com/richlander/dotnet-inspect/issues/5601) is the
end-to-end adoption tracker. The change adds no command, product capability,
shared substrate, browser path, output field, or rendering route. It changes
the existing service path without introducing a shared API, so it creates no
new browser/Wasm enablement plan or single-host substrate exception.

The full query is the existing product-consumed path.
The former `GetPublishedDateAsync` method and `v6-published` cache namespace
had only test callers and are removed in this slice rather than specified
without a consumer. `DotnetInspector.Services` is not separately packable;
current product callers use `FetchAllMetadataAsync`.

Rendering is unchanged. The cache continues to return typed `PackageMetadata`
consumed by the existing package presentation path; it does not format data or
introduce another lowering.

The design adds only the complexity required by observed correctness
boundaries:

- package-owned authority prevents one configured authority from consuming
  another authority's observation;
- separate present, absent, and miss outcomes prevent an empty present
  snapshot or unavailable cache entry from becoming package absence;
- production completion prevents partial enrichment from becoming a
  complete-looking warm result;
- closed serialization preserves meaningful null, false, zero, empty, and
  non-empty states; and
- bounded freshness ensures a newly published coordinate is reconsidered
  after an earlier absence observation expires.

No TLA+ model is required. This owner adds no coordination protocol:
`CoreCache` retains atomic file replacement, and correctness rests on immutable
subject equality, complete-value validation, and time-bounded reuse.

## Owner and boundaries

The owner is package-metadata persistence in `PackageMetadataService`,
currently in
`src/DotnetInspector.Services/PackageMetadataService.cs`.

It consumes:

- a package-owner-issued runtime authority and credential-safe durable
  configured-authority cache key;
- the package owner's normalized package ID and exact normalized version;
- compatible NuGet endpoint evidence already admitted and bounded under the
  NuGet API contracts; and
- `CoreCache` storage under one versioned metadata-persistence contract.

Within this owner, the authority-scoped metadata operation issues the typed
production outcome consumed by publication. Constructing that internal
completion currency from the already-admitted endpoint evidence is in scope;
endpoint discovery, transport, parsing, and admission remain supporting
mechanisms rather than additional owners. The owner also sets the one-hour
maximum freshness policy.

It returns exactly one of:

- a reusable complete present snapshot;
- a reusable authoritative absence observation; or
- a cache miss.

These are internal lookup and publication outcomes. The existing public
`PackageMetadata` return shape may still collapse an empty present result and
an unavailable operation for its caller; terminal source-composition outcome
typing is outside this persistence claim.

It does not own:

- source configuration, package-source mapping, authority construction,
  authorization, selection, aggregation, or cross-authority ordering;
- NuGet service discovery, endpoint compatibility, authentication, retry,
  deadline, response-bounding, or metadata-field parsing;
- package candidate or payload authority;
- `CoreCache` paths, hashing, atomic replacement, maintenance, or telemetry;
  or
- command selection, metadata disclosure, rendering, or output containment.

[Package source model](package-source-model.md) owns configured authority and
whether it has a credential-safe durable key. This design does not turn source
declaration order into authority precedence.
[NuGet API](nuget.md) supplies the endpoint and field evidence from which the
metadata operation forms its result.
[Inspection space](../inspection-space.md#corecache) owns the repository-wide
derived-cache rules, and `CoreCache` supplies only the storage mechanism.

## Cache subject

The logical subject is:

```text
PackageMetadataPersistenceSubject
  durable configured-authority cache key
  normalized package ID
  normalized package version
  metadata persistence contract
```

Every dimension is load-bearing:

- **Configured authority** preserves current package authorization. Producer
  identity and a source-URL digest are provenance approximations, not
  authority. If the package owner cannot issue a credential-safe durable key,
  the operation remains usable but persistent lookup and publication are
  disabled.
- **Coordinate** identifies the package version described by the observation.
  This owner consumes package-owned normalization rather than defining a
  second spelling.
- **Persistence contract** identifies the closed serialized projection and its
  state semantics. A contract change selects a successor namespace; old bytes
  are never reinterpreted under the new contract.

Current authorization is established before lookup. A durable key permits
cross-process reuse; it does not independently authorize an operation.
Authorities without a durable key may still use package-owner-issued
authority-scoped process-local state. This owner neither defines nor forbids
that adjacent optimization; it only declines persistent reuse.

The authority dimension applies to any configured authority that authorizes
its own compatible metadata operations. It does not allow another authority's
package identity to be sent to NuGet.org-specific enrichment routes.

## Observation outcomes

The persistent lookup algebra is:

| Outcome | Evidence | Reuse |
| --- | --- | --- |
| Present | The exact authority reported the coordinate present and the complete requested projection was produced. | Return the snapshot for this subject. |
| Absent | The exact authority's compatible existence operations definitively reported the coordinate absent, with no indeterminate result. | Return the time-bounded absence observation to the source-composition caller. |
| Miss | No current complete observation exists. | Run the ordinary authorized metadata operation. |

A live operation may additionally produce present-but-partial metadata or an
indeterminate result. Those are operation outcomes, not persistent values.
They may carry useful fields and diagnostics to the current caller but leave
the persistent subject unchanged.

An empty `PackageMetadata` value can be a valid present snapshot. It means the
coordinate exists but the completed query produced no optional field values.
It is never reconstructed from a null cache result and never represents
absence.

Authoritative absence requires at least one compatible existence operation.
Every selected equivalent operation must either be superseded by a successful
present result or settle definitively as not found. An unavailable service
index, missing existence capability, timeout, cancellation, malformed
response, unexpected status, identity mismatch, or failed equivalent
operation makes the result indeterminate rather than absent.

This owner returns the observation for one authority. The package source model
decides whether another authority may or must be evaluated; persistence does
not assign precedence to configured source order.

## Present projection and completion

A present snapshot is publishable only when the metadata producer issues a
typed complete outcome bound to the frozen cache subject. Completion means:

- package existence was established for the exact coordinate;
- every compatible operation selected by the query either completed
  successfully or its capability was authoritatively classified as not
  offered;
- every accepted response was bound to the requested package identity;
- every projection field has an explicit semantic state; and
- no failed, malformed, cancelled, timed-out, or indeterminate operation could
  make the snapshot partial.

Completion does not require every optional field to contain a value. A source
may successfully omit downloads, owners, publication date, listing state, or
another optional value. The completion outcome records that acquisition
settled; field defaults cannot reconstruct that evidence.

An operation that settles authoritatively without a value differs from an
operation failure. The persistent projection contains the current
`PackageMetadata` fields: publication time, total and version downloads,
version count, package size, verification, listing, owners, deprecation state
and value, and vulnerability state and values. Within this closed inventory, a
member's null, false, zero, empty, and non-empty states remain distinct whenever
the typed model distinguishes them. In particular:

| Evidence family | Persistent states |
| --- | --- |
| Vulnerabilities | Capability not offered; checked with no findings; checked with findings whose selected detail operations completed. |
| Deprecation | Capability unsupported; supported but no coordinate-applicable evidence; checked with no deprecation; deprecation value present. |
| Nullable scalar | Completed without a value; completed with the exact value, including `false` or zero. |
| Nullable collection | Completed without a value; completed empty; completed with values. |

A capability not offered by the authority can be part of a complete snapshot;
an operation that was offered but failed or became indeterminate cannot. The
production-completion outcome distinguishes those cases instead of inferring
them from `PackageMetadata` fields.

For a full query, a vulnerability entry with a recognized GitHub advisory
identifier selects the existing advisory-detail operation. A successful detail
response that omits optional CVE or summary fields is complete and preserves
those null states. A failed, timed-out, malformed, or oversized detail response
makes the live metadata partial and ineligible for publication. A
non-GitHub-advisory entry selects no such detail operation.

The complete serializer uses a closed inventory and terminal completion
evidence covering every member state. These prove serialized completeness,
while the producer-issued outcome separately proves acquisition completion;
neither can substitute for the other. The inventory is authoritative for each
field state, and the terminal record closes the envelope without restating
those states. A decoder rejects a missing, duplicate, unknown, malformed, or
unaccounted-for member. It validates the current contract rather than
accepting a prefix or an unchecked format field.

The current deprecation representation maps to the persistent states as
follows:

| State | Supported | Available | Value |
| --- | --- | --- | --- |
| Capability unsupported | `false` | `true` | `null` |
| No coordinate-applicable evidence | `true` | `false` | `null` |
| Checked without deprecation | `true` | `true` | `null` |
| Deprecation present | `true` | `true` | non-null |

Every other combination is invalid in a persisted present snapshot.

Cache envelope state, member names, delimiters, and completion framing are
writer-controlled. Feed-controlled text is encoded only as member data and
cannot create an absent outcome, another member, or a completion record.
Absence uses its own writer-controlled framed record and completion evidence;
it contains no present projection members.

## Freshness and replacement

Present and absent observations are reusable for no more than one hour after
successful publication. Cache access does not extend that window. Expiry
returns the subject to a miss; the next ordinary request performs a fresh
authorized operation.

An absent observation therefore means:

> This authority authoritatively reported this coordinate absent during the
> observation's still-current freshness window.

It does not mean that the coordinate can never be published. If the package
appears after the observation, discovery may be delayed only for the remaining
window. A fresh present outcome after expiry replaces the absence.

Force refresh bypasses either reusable outcome for that operation. A complete
fresh present or absent outcome may replace the prior entry. A partial,
indeterminate, failed, or cancelled refresh does not publish and does not turn
the previous entry into a new observation; its existing expiry is not
extended.

A repeated authoritative absence after expiry starts a new bounded window
because it is new source evidence. A cache read never does so.

The freshness contract assumes the host's ordinary wall clock and filesystem
timestamp behavior supplied through `CoreCache`. Within that assumption, reads
cannot extend an observation indefinitely. The owner does not claim a
cross-process monotonic clock or defend against same-machine clock or cache
file manipulation.

## Hit, miss, and failure semantics

An entry is a hit only when:

1. current authorization has selected its configured authority;
2. its durable subject equals the requested subject;
3. it is inside the one-hour freshness window;
4. its framing and every member decode successfully; and
5. its completion evidence covers the exact current projection.

Missing, expired, predecessor, truncated, malformed, semantically incomplete,
or differently bound entries are misses. An authority without a durable key
also receives a miss and cannot fall back to a URL- or producer-keyed
persistent entry.

Cache decode failure is not package or source corruption because the cache is
optional derived data. A miss runs the ordinary authorized operation.
A storage write failure leaves the live metadata outcome and diagnostics
usable. Neither failure can mint absence or a complete present snapshot.

Concurrent producers may observe mutable metadata at different times.
`CoreCache` atomic replacement may select either complete observation; readers
see an old complete entry, a new complete entry, or a miss, never a partially
published entry. This owner promises bounded freshness, not monotonic metadata
values or cross-process single flight.

## Pathological case

At 10:00, authority A authoritatively reports `Example@1.0.0` absent. At 10:01,
the package is published:

```text
10:00  absent observation published; expires no later than 11:00
10:01  package becomes available
10:30  ordinary lookup may reuse the bounded absence
10:30  force refresh must bypass it and can publish present metadata
11:00  ordinary lookup must miss and recheck the authority
```

Under the ordinary host-clock assumption, the cache may delay discovery within
its declared freshness tradeoff, but a read cannot preserve the absence
indefinitely. The neighboring positive case is a second authoritative absence
observed after expiry; that new evidence may begin another bounded window.

A separate completion boundary occurs when a vulnerability index yields one
valid page and one failed page. The current operation may report the partial
finding with its failure, but no warm operation may reinterpret that list as a
complete vulnerability result. The same rule covers a recognized GitHub
advisory whose detail request fails after the NuGet vulnerability page
succeeds. The neighboring complete case is a successful advisory response that
authoritatively omits optional CVE or summary fields.

## Precedent and deliberate divergence

NuGet.Client's HTTP cache provides the closest ecosystem precedent. At commit
[`e6aaa9a`](https://github.com/NuGet/NuGet.Client/tree/e6aaa9af1e451d6909bbf4be933cb96ad11da535),
it freshness-bounds cached positive version-list documents, validates complete
content before atomic publication, bypasses prior state for refresh, and does
not persist a separate exact-coordinate negative entry. Absence is inferred
from a fresh version list:

- [`SourceCacheContext`](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Protocol/SourceCacheContext.cs)
  owns the default maximum age and refresh controls;
- [`CachingUtility`](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Protocol/Utility/CachingUtility.cs)
  performs the timestamp freshness check; and
- [`HttpCacheUtility`](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpCacheUtility.cs)
  validates scratch content before making it visible.

This owner deliberately persists a direct absence observation because its
existing query can establish an exact coordinate through registration and
package-content probes without first obtaining a complete version-list
document. The divergence is bounded: absence receives no longer freshness than
present metadata, force refresh bypasses it, and indeterminate evidence cannot
publish it. This retains the existing request-saving behavior without treating
absence as permanent.

NuGet.Client's source-URL-derived cache identity is not adopted. The target
package source model requires package-owner-issued authority because equal
producer or endpoint identity need not mean equal configured authorization.

## Current implementation gap

`PackageMetadataService` currently keys `v6-full` entries with
`NuGetCache.GetSourceKey(source.Url)`. That separates many sources but does not
consume the package owner's configured authority and cannot satisfy the target
cache subject.

`MetadataFieldCache` recognizes exact `metadata-v7:absent` bytes or any payload
starting with `metadata-v7:present`. Although present entries write a
`formatVersion` field, the reader does not validate it and no completion
record closes the optional field inventory. Tail deletion can therefore
become a default-valued present snapshot. Serialization also collapses some
typed states: `IsVerified == false`, a zero version count, and empty
collections do not all round-trip distinctly from unavailable values.

The current `SourcePresence` and `Cacheable` flow supplies useful partial
behavior: definitive absence is separate from indeterminate status, empty
present metadata can be cached, and failed full-query catalog, search,
registration, or NuGet vulnerability index/page acquisition does not publish.
GitHub advisory detail failure is different: it currently returns without
making the vulnerability operation incomplete, so missing CVE or summary data
can publish as a complete-looking snapshot. Existing tests establish the
narrower full-query properties but do not prove advisory-detail completion,
target authority, freshness transition, complete projection, or framing.

Target adoption must use a successor subject and payload namespace rather than
relabeling `v6`/`metadata-v7` bytes. Until package source composition supplies
the authority inputs, the safe target behavior is the ordinary authorized
metadata path rather than producer- or URL-keyed persistent reuse.
Package-owner-issued process-local reuse may still apply. If no such state is
available, losing cross-process metadata reuse for an unkeyable authority is
the accepted cost of avoiding reuse that the current request did not
authorize.

## Required gates

The target remains unverified until Release tests establish:

- `PackageMetadataPersistence_SubjectControlsReuse`, covering distinct
  configured authorities with equal producer identity, an authority without a
  durable key, normalized coordinates, and predecessor namespace rejection
  followed by cold recomputation and reusable successor publication;
- `PackageMetadataPersistence_FreshObservationControlsReuse`, covering present
  and absent reuse without access-time extension, expiry, the
  absent-to-present transition, repeated fresh absence, force refresh, and an
  indeterminate refresh that cannot extend or replace prior evidence; and
- `PackageMetadataPersistence_PublishesOnlyCompleteCurrentOutcomes`, covering
  complete empty and populated snapshots, present-but-partial nonpublication,
  vulnerability and deprecation states, failed advisory-detail acquisition
  versus a successful advisory response with absent optional fields, exact
  false/zero/null/empty round-trip,
  feed-controlled text that cannot forge outcome, member, or completion
  framing, and storage-write failure that leaves the live outcome usable,
  plus malformed, truncated, deleted-member, duplicate-member,
  unknown-member, missing-completion, and semantically incomplete entries.

Existing source-scoping, empty-result, checked-clean vulnerability, and failed
enrichment tests, plus feed-text encoding and absence-marker injection tests,
remain evidence for their narrower current properties. They do not count as
the target gates under different names.

## Non-claims

This design does not:

- make a package coordinate permanently present or absent;
- define source ordering, fallback, aggregation, or selection;
- make metadata evidence authorize a package candidate or payload;
- derive configured authority from source URL, producer, display name, or
  cache bytes;
- require persistent reuse for an authority without a safe durable key;
- define NuGet endpoint compatibility, authentication, retry, or field parsing;
- make metadata values monotonic during their freshness window;
- make `CoreCache` a semantic cache owner;
- guarantee a cache hit, durable write, cross-process single flight, or
  operation across host clock rollback; or
- change package commands, output, disclosure, or rendering.
