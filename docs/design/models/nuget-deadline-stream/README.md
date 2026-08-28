# NuGet deadline stream model

`DeadlineStreamLifecycle.tla` models the concurrency inside
`NuGetOperationDeadline.DeadlineStream` after a payload stream and its owner
have transferred to the caller. It checks the timeout, read, abort, EOF, and
disposal interactions that implement
[timeout ownership](../../browser-package-sources.md#timeout-ownership).

The model is a design specification. It checks that the intended rules are
consistent across the modelled interleavings; it does not prove that the C#
implementation conforms to them.

## Model boundary

The model contains one transferred payload stream, one non-empty asynchronous
read, and at most one caller disposal. It covers:

- a distinct per-read cancellation;
- caller cancellation, operation expiry, and request expiry;
- a cancellation callback delayed past monotonic deadline observation;
- data, EOF, a deadline-eligible transport abort, and a read stalled until
  owner disposal;
- owner abort success or failure;
- synchronous disposal order: inner, owner, deadline state;
- asynchronous disposal order: inner, deadline state, owner; and
- EOF racing deadline callback dispatch, caller disposal, and deadline-state
  completion.

The model deliberately excludes request acquisition before ownership transfer,
metadata-body parsing and its separate deadline, retries, multi-source
orchestration, HTTP transport details, zero-length reads, repeated reads, and
concurrent duplicate caller disposal. Those mechanisms are not needed to test
the transferred stream's interaction rules.

Both product payload call sites transfer an `HttpResponseMessage` as the owner.
The implementation may attempt owner disposal once from deadline abort and
again from later caller disposal. The model represents those as independent
paths and does not assert exactly-once owner disposal. It does assert that each
path starts at most once and that deadline-state completion cannot overtake a
running cancellation callback.

EOF and caller disposal compete to claim deadline cleanup. The winner
unregisters the callback and disposes the deadline resources. A caller-disposal
path that observes cleanup already claimed does not join its completion; it
continues its own owner cleanup while the original cleanup owner retains the
obligation to finish.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every state remains within the declared finite shape |
| `ResultShapeIsConsistent` | A completed read has exactly one typed result, and only deadline results retain abort-cleanup failure |
| `ReadResultIsWrittenAtMostOnce` | The read receives at most one terminal classification |
| `ReadCancellationPrecedesDeadlineTranslation` | A distinct per-read cancellation observed before timeout translation begins retains its own classification |
| `TransportFailureIsNotReclassified` | A deadline-eligible transport abort classified while no outer deadline has elapsed remains a transport failure even if the per-read token is also canceled |
| `ClassificationFollowsPrecedence` | Deadline attribution follows caller cancellation, operation expiry, then request expiry |
| `NoLateSuccess` | Data or EOF is admitted only when no applicable outer deadline has elapsed at the post-read check |
| `EofDisarmsDeadlineTranslation` | Once EOF wins the post-read check, later expiry cannot reinterpret that result as timeout |
| `AbortFailureIsRetained` | A deadline result produced after failed abort cleanup retains that failure |
| `AbortStartsAtMostOnce` | Callback and read-side deadline observation cannot each start an abort |
| `DeadlineOwnershipIsSafe` | Active deadline state has no cleanup owner, claimed state has exactly one, and resource cleanup does not overtake its running cancellation callback |
| `CompletedDisposalLeavesDeadlineOwned` | Completed caller disposal includes inner and caller-owner cleanup and leaves deadline cleanup claimed, though another owner may still be completing it |
| `StartedAbortEventuallyCompletes` | Every started abort finishes |
| `StartedDisposalEventuallyCompletes` | Every started synchronous or asynchronous disposal finishes |
| `ImmediateReadEventuallyCompletes` | A started non-stalling read reaches one result |
| `UnblockedStalledReadEventuallyCompletes` | Deadline expiry or caller disposal eventually releases and classifies a stalled read |
| `EofEventuallyCompletesDeadline` | EOF eventually unregisters and disposes the deadline state |

The transport-failure rule evaluates deadline state when the completed inner
result is classified. It does not preserve classification according to when
the inner operation first faulted; that occurrence time is not represented.

The liveness properties use weak fairness for internal read, abort, disposal,
and deadline-state progress. Caller cancellation, deadline expiry, read
cancellation, read start, and disposal start are environment actions and are
not assumed to occur. Inner and owner disposal are abstracted as operations
that eventually return; a transport whose disposal can block forever is
outside these liveness claims.

## Implementation alignment

The model and the C# tests provide different evidence. TLC checks the design's
permitted interleavings; the tests below check selected implementation
behaviors:

| Model rule | Implementation gate |
| --- | --- |
| Delayed callbacks cannot admit late data | `NuGetDeadlineRaceTests.StreamConsumption_UsesElapsedTimeWhenTimerCallbackIsDelayed` |
| A distinct per-read cancellation wins before timeout translation starts | `NuGetDeadlineTests.PreCancelledPerReadToken_PrecedesExpiredRequestDeadline` |
| An I/O failure classified while no deadline has elapsed remains an I/O failure | `NuGetDeadlineTests.IoFailureBeforeDeadline_RemainsAnIoFailure` |
| EOF disarms later deadline translation | `NuGetDeadlineTests.CompletedPackageStream_RemainsAtEofAfterDeadline` |
| Caller cancellation remains caller cancellation | `NuGetDeadlineTests.PackageCallerCancellation_IsNotReportedAsADeadline` |
| Async cleanup does not deadlock with abort | `NuGetDeadlineTests.DisposeAsync_DoesNotBlockOnAbortCleanup` and `InlineAsyncCompletion_DoesNotDeadlockAbortCleanup` |
| Abort cleanup failure remains visible | `NuGetDeadlineTests.RequestDeadline_DisposalFailureIsRetained` and `DisposeAsync_InlineCompletionRetainsAbortFailure` |

## Running TLC

Use the pinned tools from
[`docs/runbooks/tla-plus-setup.md`](../../../runbooks/tla-plus-setup.md). From
this directory, with `tla2tools.jar` in `$TLA_TOOLS`:

```sh
TLA_METADIR="${TMPDIR:-/tmp}/dotnet-inspect-deadline-stream"
mkdir -p "$TLA_METADIR"
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -metadir "$TLA_METADIR" -cleanup -coverage 1 \
  DeadlineStreamLifecycle.tla
```

The checked configuration is exhaustive and has no model constants. The
results below record the exact tool versions, state counts, search depth,
action coverage, and mutation probes.

## Checked evidence

The checked configuration uses the model boundary above as its finite bound:
one stream, one non-empty asynchronous read, one optional caller disposal, and
one occurrence of each cancellation or deadline fact. It has no configurable
constants.

The result below came from:

- TLA+ tools `v1.8.0`, TLC
  `2026.08.21.155922` (`9787e65`);
- Eclipse Temurin OpenJDK `25.0.4.1+1`, Windows x64; and
- a run on 2026-08-27.

SANY parsed the module successfully. TLC exhaustively generated 115,322 states,
found 33,077 distinct states, and completed the state graph at depth 18 with no
error in six seconds. TLC checked the five temporal-property branches over
165,385 total distinct behavior-checking states.

Action coverage reports transitions and distinct states from every one of the
28 model actions.

### Mutation probes

Each probe changed a scratch copy, enabled only the named claim, and ran TLC
again. All sixteen produced the expected named violation. `TypeOK` is the one
unprobed property because it is a state-shape guard rather than a behavioral
claim.

| Probe | Mutation | Claim | Result |
| --- | --- | --- | --- |
| DS1 | Record a completed data read with no result | `ResultShapeIsConsistent` | Violated |
| DS2 | Permit a second write of the read result | `ReadResultIsWrittenAtMostOnce` | Violated |
| DS3 | Record a timeout after an already observed per-read cancellation | `ReadCancellationPrecedesDeadlineTranslation` | Violated |
| DS4 | Let request expiry outrank simultaneous caller or operation expiry | `ClassificationFollowsPrecedence` | Violated |
| DS5 | Admit data after an applicable deadline elapsed | `NoLateSuccess` | Violated |
| DS6 | Reinterpret an established EOF as a later deadline | `EofDisarmsDeadlineTranslation` | Violated |
| DS7 | Drop abort-cleanup failure from a deadline result | `AbortFailureIsRetained` | Violated |
| DS8 | Let the cancellation callback start abort twice | `AbortStartsAtMostOnce` | Violated |
| DS9 | Complete deadline state while its callback is still running | `DeadlineOwnershipIsSafe` | Violated |
| DS10 | Finish asynchronous disposal without marking the stream disposed | `CompletedDisposalLeavesDeadlineOwned` | Violated |
| DS11 | Remove weak fairness from abort progress | `StartedAbortEventuallyCompletes` | Violated |
| DS12 | Remove weak fairness from disposal progress | `StartedDisposalEventuallyCompletes` | Violated |
| DS13 | Remove weak fairness from immediate-read progress | `ImmediateReadEventuallyCompletes` | Violated |
| DS14 | Remove weak fairness from unblocked stalled-read progress | `UnblockedStalledReadEventuallyCompletes` | Violated |
| DS15 | Remove weak fairness from deadline-state progress | `EofEventuallyCompletesDeadline` | Violated |
| DS16 | Route a transport failure to read cancellation while no outer deadline has elapsed at classification | `TransportFailureIsNotReclassified` | Violated |

The shipped model produced no material counterexample. The mutation
counterexamples establish that its behavioral claims are sensitive to the
specific precedence, completion, and fairness rules they describe; they do not
establish implementation conformance.
