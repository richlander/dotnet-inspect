# Package payload capacity

`DotnetInspector.Packages` owns the host-capacity handoff in package payload
admission. Its claim is narrow: acquisition awaits a capacity reservation before
materializing a response body, then holds the reservation until validated
publication or abandonment. Cache hits retain their existing admission path.

## Consumer and basis

[Browser artifact adoption #5576](https://github.com/richlander/dotnet-inspect/issues/5576)
needs to await artifact-backed scope cleanup before reusing retained-memory
capacity. The transfer policy is the existing handoff between package admission
and host capacity; making that handoff asynchronous avoids a second cache,
blocking single-threaded Wasm, or abandoning cleanup tasks.

[Prerequisite #5849](https://github.com/richlander/dotnet-inspect/issues/5849)
replaces the synchronous policy callback rather than retaining parallel APIs.
Both source-list/HTTP and typed-source acquisition await the same policy.
Policies whose capacity work is synchronous return a completed `ValueTask`.
This follows the adjacent `IPackageStore.CommitAsync` convention: a
host-neutral asynchronous handoff with a synchronous in-memory fast path.
The browser's actual asynchronous eviction follows in #5576.
[Tracker #5577](https://github.com/richlander/dotnet-inspect/issues/5577)
includes that adoption and the required legacy-realization retirement #5840.

## Boundary

The policy receives the exact package coordinate, producer key, advertised
length, and existing transfer cancellation token. It returns a capacity
reservation or fails. Pending capacity work, provisional reservations, and
cooperative cancellation belong to the host until that handoff settles.
Acquisition keeps the response alive and does not read its body while waiting.
It awaits settlement rather than abandoning an outstanding policy task.

After handoff, acquisition owns the returned reservation. Cancellation observed
at handoff prevents body materialization and releases the reservation without
completing it. Successful archive validation and store publication precede
reservation completion; unsuccessful admission releases it without completion.
The existing distinction between source failures and visible host-policy
failures is unchanged, including when a policy fails after suspension.

This contract does not choose host eviction, cache identity, single-flight, or
scope lifetime policy. The browser owns those in its
[workspace retention contract](../../prototypes/inspect-web/README.md).
Archive validation, source authorization, producer continuity, and store
publication retain their existing owners and behavior.

## Evidence

The Release `PackagePayloadAcquisitionTests` suite exercises both acquisition
entry points with these outcome-level gates:

- `TransferPolicy_AwaitsCapacityBeforeReadingPayload`: delayed reservation,
  unread retained response, then validation/commit before completion.
- `TransferPolicy_CancellationWhileAwaitingCapacityClosesPayload`: cooperative
  cancellation closes the unread response without store publication.
- `TransferPolicy_AsyncRefusalClosesUnreadPayload`: an asynchronous host-policy
  refusal remains visible and closes the unread response.
- `TransferPolicy_CancellationAtCapacityHandoffReleasesReservation`: cancellation
  at handoff closes the response and releases uncompleted capacity.

The existing `TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit`,
`TransferPolicy_RejectedPayloadDisposesWithoutCompleting`, and
`TransferPolicy_CanRequireContentLengthBeforeBodyRead` cases retain the
synchronous-policy and rejected-payload evidence.
