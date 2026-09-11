# Artifact Ownership and Borrowing

## Status

This document is the normative owner for Artifact resource classification,
resource-free content references, transferable retained-content ownership, and
synchronous scoped content borrowing.

It expands step 9 of
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md) and is
tracked end to end by
[#6647](https://github.com/richlander/dotnet-inspect/issues/6647).

## Authority and exact claim

Artifact acquisition and Workspace composition owns this claim:

> One published immutable Artifact has one resource-free content reference.
> Current query policy authorizes selection and issuance, while an
> Artifact-issued content lease is the separately transferable child
> obligation for continued access to that exact retained content. Content is
> exposed only through synchronous scoped borrows, and Artifact session
> settlement releases source acquisition resources only after transferred
> content leases and admitted accesses quiesce.

This is one focused Artifact-owner adoption of the shared resource protocol. It
does not define Library membership, Metadata decoding, package or Platform
selection, House settlement, Workspace logical scope, or host presentation.

The first consumer is the artifact-backed
[`LibraryContentOwner`](library-ownership-and-borrowing.md#librarycontentowner).
That owner consumes Artifact-issued child obligations without redefining their
identity, borrowing, or release semantics.

## Basis

The normative basis is
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md):

- references and receipts carry identity and evidence, never hidden authority;
- resource issuers, not consumers, issue resource-named leases;
- ownership transfer moves one release obligation into a recipient;
- work crossing `await` owns authority rather than retaining a borrow;
- content borrows are synchronous and scoped; and
- asynchronous aggregate settlement drains children before releasing their
  backing resources.

The existing Artifact owner already supplies important parts of this shape:

- immutable retained content;
- generation-scoped `ArtifactIdentity`;
- admission and query authorization;
- `readonly ref struct` content views over `ReadOnlySpan<byte>`;
- gate-atomic access registration;
- query-authorization replacement and revocation; and
- asynchronous session quiescence before acquisition-lease cleanup.

This adoption adds one deliberate dependency:
`Inspector.Artifacts` may reference the lower `Inspector.Resources` contract
floor for ownership declarations and shared snapshot vocabulary. It remains
independent of Metadata, package, storage, Workspace composition, and host
implementations. `Inspector.Artifacts.Workspaces` consumes both contract
floors.

The current `ArtifactContentReference` is migration evidence, not the target.
It captures both `ArtifactSetSession` and `ArtifactQueryLease`, so registration,
roles, digests, and content opening appear to be reference operations while
actually using hidden revocable authority. A downstream aggregate cannot own
that shape without retaining another caller's query-policy lease.

## Contract vocabulary

### `ArtifactContentReference`

`ArtifactContentReference` is the resource-free reference to one exact
immutable Artifact content item in one published Artifact generation.

It contains:

- the owner-issued `ArtifactIdentity` and generation correspondence;
- the `ArtifactDescriptor`;
- the exact `ArtifactAcquisitionRegistration`;
- immutable Workspace roles assigned to that Artifact; and
- resource-free provenance reachable from the registration.

It contains no session, authorization, lease, content buffer, stream, callback,
delegate, service, client, release action, or reopening capability.

Creating a reference is an authorized observation. Once returned, its recorded
descriptor, registration, provenance, and roles are immutable evidence and do
not require reauthorization. The reference may outlive its Artifact session.
It then remains useful as correspondence evidence but cannot reopen or
reacquire content.

Reference equality is exact process-local Artifact correspondence. Equal
filenames, media types, package coordinates, assembly identities, digests, or
bytes do not substitute another reference.

### `ArtifactQueryLease`

`ArtifactQueryLease` remains the resource-named operation authority for one
current Artifact query policy.

It authorizes:

- catalog and role selection;
- correspondence and provenance observation;
- query-scoped content borrowing;
- on-demand digest requests; and
- issuance of a retained-content child lease for an exact selected reference.

Replacing or revoking the authorization rejects every later operation through
an older query lease. Disposing the query lease settles that query operation.
Neither action revokes a child content lease that was already issued and
transferred.

This distinction preserves the existing policy boundary: a changed query plan
cannot enumerate or select through stale authority. It also preserves the
ownership boundary: policy replacement cannot silently invalidate a child
obligation already accepted by another aggregate.

### `ArtifactContentLease`

`ArtifactContentLease` is the Artifact-issued child ownership obligation for
one exact `ArtifactContentReference`.

Issuance requires:

- a published, non-retiring Artifact session;
- a current query lease issued by that session; and
- an exact content reference issued by the same session and generation.

Successful issuance returns one live lease naming that exact reference. The
caller owns it until synchronous release or explicit transfer into an
aggregate. The lease exposes no catalog, selection, binding, designation, or
role-policy operations.

After issuance:

- replacing, revoking, or disposing the query authorization or query lease
  does not revoke the content lease;
- the lease can borrow only its exact retained content;
- session retirement rejects new content leases but preserves existing ones;
  and
- releasing the lease reports child completion to the Artifact session.

The lease uses synchronous disposal because release only ends one child
obligation and reports it to the asynchronously settling Artifact session. The
session owns aggregate quiescence and acquisition-resource cleanup.

An abandoned content lease intentionally leaves session settlement incomplete.
The owner does not invalidate retained bytes, detach the lease silently, or
turn the leak into successful cleanup.

### Scoped retained-content view

The content lease supplies a synchronous callback with an Artifact-owned
`readonly ref struct` view containing:

- the exact Artifact identity and generation;
- the matching resource-free content reference; and
- `ReadOnlySpan<byte>` over the immutable retained image.

Only Artifact constructs the view. The callback result is detached or
independently owned. The view and span cannot be retained, returned, stored in
a heap object, carried across `await`, or passed across Browser interop.

The existing admission and query views remain phase-specific authorization
borrows. The content-lease view is ownership-backed rather than
query-policy-backed; it does not replace admission projection or current
query-policy validation.

## Resource classification

Artifact adopts the shared declaration protocol with these meanings:

| Type or family | Classification | Terminal obligation |
| --- | --- | --- |
| `ArtifactSetSession` | Asynchronous aggregate resource owner | Reject new work, drain content children and admitted accesses, release acquisition leases, and expose cleanup failure |
| `IArtifactAcquisitionLease` | Asynchronous source-issued resource | Settle source-specific acquisition retention after transfer or rejection |
| `ArtifactAdmissionLease` | Synchronous operation resource | End admission access before publication or abort completes |
| `ArtifactQueryLease` | Synchronous operation resource | End one current-policy query operation |
| `ArtifactContentLease` | Synchronous child resource | End continued access to one exact retained content item |
| `ArtifactContributionScope` | Synchronous admission capability resource | Close contribution authority before admission completes |
| Returned compatibility stream | Synchronous owned child resource | Close one admitted stream and release its parent access registration |
| Authorization objects | Issuer authority, not a lease | Owner revocation; no consumer transfer or release obligation |
| References, identities, descriptors, registrations, roles, provenance, digests, outcomes, and receipts | Resource-free evidence | None |

`ArtifactGenerationAuthority` remains an internal issuer mechanism. The
declaration boundary classifies the public or transferred obligations rather
than promoting an implementation coordinator into another product resource.

Successful acquisition transfers each `IArtifactAcquisitionLease` into the
Artifact session. A rejected acquisition result owns no hidden successful
lease. If validation or aggregate acceptance fails before transfer, the caller
retains the lease; after transfer, the Artifact session owns cleanup on every
terminal path.

## Issuance and transfer

The content-lease handoff is:

```text
current Artifact query operation
  -> selects exact resource-free ArtifactContentReference
  -> Artifact session validates current query authority and exact reference
  -> Artifact session issues ArtifactContentLease
  -> caller transfers the child obligation into a downstream aggregate
  -> downstream operations use only synchronous scoped borrows
  -> aggregate releases the child
  -> Artifact session observes child completion
```

Issuance is all-or-rejection. A failed request does not return a partially live
lease or consume caller ownership. Successful aggregate acceptance follows the
shared transfer rule: before acceptance the caller owns the child; after
acceptance the recipient owns it.

The content lease is per Artifact rather than per Library or per consumer.
Changing the downstream consumer does not rename or change the lease. A
Library owner may aggregate several leases for API, implementation, XML
documentation, or PDB content, while Artifact remains unaware of those roles.

Issuance does not copy the complete retained image. The Artifact session keeps
the immutable backing content and source acquisition obligations; the child
lease keeps that exact parent retention live until release.

## Query borrows and ownership-backed borrows

Artifact supports two explicit post-publication borrow paths:

1. A current `ArtifactQueryLease` authorizes a synchronous query borrow after
   current catalog and policy validation.
2. An `ArtifactContentLease` authorizes a synchronous ownership-backed borrow
   of its one exact content item.

Both paths:

- register the access before invoking consumer code;
- reject missing, foreign, stale-generation, disposed, or ended authority
  without invoking the callback;
- preserve consumer exceptions and cancellation;
- expose immutable retained bytes without reopening the source; and
- end the access registration when the callback returns or propagates.

Only the query path revalidates current query authorization. The ownership path
instead validates the live content lease and exact reference. Treating an
already issued child lease as if it were still a query-policy lease would make
unrelated policy replacement revoke a downstream owner's accepted resource.

An access admitted before authorization replacement, content-lease release, or
session retirement may finish. Later access is rejected by the applicable
authority. This is the same admitted-work boundary already used by returned
streams and scoped query callbacks.

## Session retirement and quiescence

Beginning `ArtifactSetSession.DisposeAsync` is the linearized retirement
transition:

1. reject new admission, query operations, and content-lease issuance;
2. revoke current admission and query authorization;
3. keep previously issued content leases usable while their owning aggregates
   settle, including synchronous borrows requested through those live children;
4. asynchronously wait for all content leases, scoped accesses, and
   compatibility streams to quiesce;
5. release every transferred acquisition lease exactly once; and
6. complete with visible cleanup success or failure.

The session never blocks a thread while waiting for child owners. This is
required for single-threaded Browser/Wasm as well as retained desktop hosts.

Workspace Root retirement supplies the production ordering. It closes new Root
query entry, drains Root query operations, settles dependent aggregate owners
such as Library owners and assembly groups, and only then permits the Artifact
session to finish. The stateless CLI follows the same ordering before process
exit. This document consumes that composition; it does not redefine Root
publication or Workspace close.

The Artifact session may begin retirement before a content child is released.
That child remains usable because it owns already-issued authority. Session
settlement remains incomplete until the child releases. No timeout or
success-shaped forced revocation is implied.

## Digests and compatibility streams

An `ArtifactContentReference` never computes a digest or opens a stream.

On-demand digest requests remain Artifact-owned operations over retained bytes.
The caller supplies either current query authority or, when required by an
ownership-backed consumer, a matching live content lease. The resulting digest
is detached resource-free evidence and may escape. Cached digest reuse never
bypasses authority validation.

Parameterless `ArtifactContentReference.OpenRead()` and
`GetContentDigest()` are migration surfaces to retire. While stream consumers
remain, an explicit Artifact authority operation returns an owned child stream
whose disposal keeps the parent access registration live. No resource-free
reference captures the opener.

Metadata's `ResolvedAssemblyReference` opener and path compatibility are owned
by the Metadata migration. Artifact adopter work supplies explicit references,
leases, and callbacks but does not redefine Metadata's image-lifetime contract
in this design.

## Failure and completion algebra

Artifact keeps these outcomes distinct:

- `Issued`: one exact content child obligation transferred to the caller;
- `Unauthorized`: query authorization or its lease is absent, stale, revoked,
  foreign, disposed, or ended;
- `ReferenceMismatch`: the reference belongs to another session, generation,
  or content item;
- `SessionRetiring`: issuance began after Artifact retirement;
- `LeaseReleased`: ownership-backed access used a released child lease;
- `BorrowRejected`: owner validation rejected before callback invocation;
- `CallbackFailed`: the callback threw after a borrow began and the borrow
  ended before propagation; and
- `ReleaseFailed`: acquisition or aggregate cleanup failed during observed
  asynchronous settlement.

Concrete APIs may reuse an existing closed result algebra when it preserves
these distinctions. They must not convert a mismatch, stale authority,
abandoned child, or cleanup failure into absent content or successful empty
output.

## Stateful interaction model

[`ArtifactContentOwnership.tla`](models/artifact-content-ownership/ArtifactContentOwnership.tla)
models current-policy issuance, authorization replacement, transferred child
use, session retirement, and backing acquisition-resource release.

The model checks:

- content issuance requires a current query lease and an open session;
- later query-policy replacement does not revoke an issued content child;
- backing acquisition resources release only after content children and
  borrows settle; and
- a requested retirement eventually completes under explicit child-release
  and borrow-completion fairness.

Its broken-policy configurations demonstrate that ungated issuance,
query-bound transferred children, and immediate backing release each violate a
different required property. The model establishes bounded design evidence,
not implementation conformance.

## Production adoption and retirement

Issue #6647 owns seven slices:

1. lock this focused Artifact contract and its stateful interaction model;
2. declare current Artifact resource effects under `Inspector.Resources`;
3. implement the per-content lease and asynchronous session drain;
4. make `ArtifactContentReference` resource-free and require explicit
   authority for content and digest operations;
5. replace Artifact-owned hidden-reference gates and adopt the assembly-only
   fixture host;
6. migrate Metadata and Queries consumers through separately reviewed
   owner-scoped adopter work; and
7. transfer Artifact children into the Library owner and validate the shared
   CLI and Browser/Wasm Workspace Root paths.

The target retires:

- session and query-lease capture in `ArtifactContentReference`;
- parameterless content and digest operations on that reference;
- ordinary delegates that hide Artifact authority; and
- downstream ownership of a query-policy lease merely to keep selected bytes
  alive.

Compatibility retirement may stage across owner-scoped PRs. No intermediate
slice may present a resource-free reference as sufficient content authority.

## Required gates

Implementation claims remain unverified until Release gates equivalent to
these exist:

- `ArtifactContentReference_IsResourceFreeEvidence`
- `ArtifactContentReference_PreservesExactRegistrationRolesAndGeneration`
- `ArtifactContentLease_IssuanceRequiresCurrentQueryAuthority`
- `ArtifactContentLease_RejectsForeignOrMismatchedReference`
- `ArtifactContentLease_SurvivesQueryAuthorizationReplacement`
- `ArtifactContentLease_BorrowsOnlyItsExactRetainedContent`
- `ArtifactContentLease_BorrowCannotEscapeCallback`
- `ArtifactContentLease_ReleaseRejectsLaterBorrow`
- `ArtifactContentLease_ActiveBorrowPinsBackingRelease`
- `ArtifactSetSession_RetirementRejectsNewContentLeases`
- `ArtifactSetSession_RetirementDrainsTransferredContentLeases`
- `ArtifactSetSession_ReleasesAcquisitionAfterContentChildren`
- `ArtifactContentLease_CallbackFailureAndCancellationRemainVisible`
- `ArtifactDigest_RequiresExplicitQueryOrContentAuthority`
- `ArtifactCompatibilityStream_DeclaresAndReleasesParentRetention`
- `LocalOnlyHost_UsesExplicitArtifactAuthorityWithoutHiddenReferenceLease`
- `BrowserWorkspace_RetiresArtifactChildrenWithoutBlocking`

The existing Artifact scoped-content, stream, digest, session-cleanup, Root
retirement, local-only host, and Browser/Wasm gates remain supporting evidence.
They do not establish the new child-ownership or resource-free-reference
claims until the focused gates above land.

## Non-claims

This design does not claim:

- that current C# prevents ownership aliases or use after transfer;
- that a content lease is a query-policy snapshot or grants catalog access;
- that Artifact roles are Library roles;
- that equal bytes, digests, paths, filenames, package coordinates, or
  assembly identities establish Artifact correspondence;
- that local content mutates within one published Artifact generation;
- that query authorization replacement creates a new Artifact generation;
- that a borrow crosses `await` or Browser interop;
- that Artifact owns Library multi-content borrowing or aggregate release;
- that Artifact owns Metadata opener retirement; or
- that an abandoned child lease may be forcibly revoked and reported as clean
  settlement.
