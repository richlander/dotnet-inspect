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
[workspace retention contract](../../inspect-web/README.md).
Archive validation, source authorization, producer continuity, and store
publication retain their existing owners and behavior.

## Selected ranged content

A ranged consumer may supply `IPackageRangedContentPolicy` alongside its
complete-transfer policy. After directory validation and semantic entry
selection, acquisition reserves the selected expanded-byte total before
materializing entry bodies, including bodies read from the entry cache. The
reservation remains held across a partial entry-cache hit and the network read
of missing entries. Checked ranged content completes the reservation; failure,
cancellation, or complete-transfer fallback releases it without completion.
Complete fallback then uses ordinary archive-transfer admission.

Completion may publish the immutable ranged content into the host's bounded
session cache. Such content never answers a complete-archive acquisition.
The validated directory's archive length remains available for package-size
measurements without implying that the archive itself is retained.

The first consumer is Inspect Web ordinary package realization. `Avalonia`
12.1.2 targeting net10.0 motivates this adoption: the 10.1 MB archive contains
multiple target groups, while the Browser needs the selected surface and
implementation folders plus package-authored evidence. The Browser preserves
its Library inventory and binding context, charges selected expanded content,
and retains complete acquisition for callers requiring the whole archive.
Before/after measurements invoke the actual `QueryPackage` export in Release;
they measure native host execution, not Browser/Wasm network timing.

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

### Website package-open measurements

The [probe](../../eng/measure-inspect-web-package-open.cs) calls the production
`PackageExports.QueryPackage` export twice per process. Both revisions were
published as Release NativeAOT Linux executables with the repository SDK.
Baseline is `5e415b31a`; the candidate adopts selected ranged content.
[Raw timings and output hashes](../../eng/inspect-web-package-open-evidence.tsv)
record seven alternating baseline/candidate process pairs per package. Cold
means a fresh process; warm means its second export call. Times below are
medians, including surface projection and JSON serialization.

| Package / target | Cold before → after (ms) | Warm before → after (ms) | TCP bytes before → after |
| --- | --- | --- | --- |
| Avalonia 12.1.2 / net10.0 | 985.9 → 1139.9 | 707.1 → 706.1 | 10,168,056 → 4,811,592 |
| Microsoft.CodeAnalysis.CSharp 4.11.0 / net8.0 | 1155.2 → 1154.9 | 771.3 → 767.8 | 16,957,489 → 4,137,041 |
| Dapper 2.1.66 / net8.0 | 138.2 → 139.4 | 14.4 → 14.3 | 446,593 → 446,598 |

TCP totals come from separate `strace -f -yy` runs summing successful
`read`, `recvfrom`, `recvmsg`, and `readv` results on TCP sockets across both
calls; they include TLS and metadata traffic. Large-package transfer fell
52.7% and 75.6%; these timings demonstrate no consistent latency improvement.
The below-cut Dapper path retains complete acquisition. Every baseline and
candidate export has an identical JSON hash for its package. The CSharp
fixture reaches existing Browser surface limits on both revisions.

Retained content is charged honestly: Avalonia grows from 10,143,223 compressed
bytes to 16,475,372 selected expanded bytes; CSharp falls from 16,919,271 to
12,593,086 bytes; Dapper stays at 437,579 bytes. Range acquisition trades
transfer against expanded-content retention, so transfer savings alone do not
establish memory or end-to-end latency savings.

Selected-content admission is exercised by the cold, warm, and partial-cache
cases in `PackageRangedRealizationTests.Capacity`, including complete fallback
releasing its sparse reservation. `BrowserEngineBoundaryTests.PackageRange`
checks folder selection, accounting, warm reuse, and distinct target requests
while an earlier generation remains leased.
