# NuGet credential-plugin session lifecycle model

`NuGetPluginSessionLifecycle.tla` is the executable interaction companion to
[NuGet feed authentication](../../nuget-authentication.md). It models one
credential-plugin connection from its symmetric handshake through concurrent
credential requests and terminal pipe loss.

The model addresses interaction questions that are difficult to settle from
the protocol sequence alone:

- Can the host become ready without answering the plugin's handshake?
- Can concurrent responses or progress messages affect the wrong request?
- Can one request receive more than one terminal outcome?
- Does pipe loss close admission before pending requests are collected?
- Does a malformed plugin-originated handshake receive a response or terminate
  the connection instead of becoming abandoned work?
- Do initialization, requests after their final Progress renewal, and shutdown
  eventually settle under the stated fairness assumptions?

## Model boundary

The owner is the credential-plugin conversation in
`docs/design/nuget-authentication.md`. The finite model contains:

- one plugin process and read loop;
- one symmetric initialization conversation;
- two host-originated requests, enough to exercise correlation and serialized
  writes;
- repeatable progress messages, with one-bit event parity that keeps every
  renewal observable while preserving a finite state space;
- caller cancellation, plugin response, plugin fault, timeout, and pipe-loss
  outcomes; and
- a split pipe-loss sequence that exposes admission between observing EOF,
  collecting pending requests, and settling that collection.

The plugin is an external protocol peer, not a hostile in-process caller. Its
messages may be malformed, delayed, reordered across request IDs, or terminated
by process or pipe failure.

A well-formed but unsupported handshake is distinct from a malformed handshake.
The former receives the protocol's error response in every mode. The
`InboundFailureMode` mutation applies only to a payload that cannot be
deserialized, matching the current unobserved-task path.

Plugin discovery, executable selection, HTTP challenge and retry policy,
credential scope, redirects, authentication types, interactive token
acquisition, and token secrecy are outside the model. Provider disposal
concurrent with public calls is also excluded: the production composition keeps
the provider for process lifetime, and this model does not establish a broader
thread-safety contract for `PluginCredentialProvider`.

The model abstracts the five post-handshake initialization messages into one
authentication-claim decision. It therefore checks that readiness requires the
successful symmetric handshake and Authentication claim, but not the individual
wire shape or ordering of `MonitorNuGetProcessExit`, `Initialize`,
`GetOperationClaims`, and `SetLogLevel`.

Responses and progress messages carry an explicit modeled request ID. The
positive actions derive their target from that ID; the mutation modes instead
route to a different request. Per-request input and effect state makes those
correlation checks independent of the chosen transition target. Unknown and
stale request IDs are outside the finite model; the implementation ignores
them when `_pending` has no matching entry.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every state remains within the declared finite shape. |
| `RequestCompletionIsExact` | A completed request has exactly one typed outcome; live and unused requests have none. |
| `WriterIsOwnedByLiveWork` | The serialized writer belongs to one live request or inbound handshake. |
| `ReadyRequiresSymmetricHandshake` | Readiness requires both handshake directions and the Authentication claim. |
| `RequestAdmissionHasLiveReceiver` | A request is admitted only while the read loop can receive its response. |
| `ResponsesCompleteOnlyTheirRequest` | Per-request response receipt and successful completion agree. |
| `ProgressRenewsOnlyItsRequest` | Per-request Progress receipt and deadline-renewal effects agree. |
| `InboundFailureIsContained` | Malformed inbound handshake handling cannot disappear as unobserved work. |
| `ShutdownSettlementIsComplete` | The shutdown collection settles every request admitted before admission closed. |
| `ClosedConnectionIsQuiescent` | A closed connection owns no writer or live request and cannot admit work. |
| `ClosedConnectionIsAbsorbing` | Once closed, the connection cannot move back to an earlier lifecycle state. |
| `WriterPreemptionIsContained` | An independently recorded timeout or cancellation of the active writer preserves its cause, closes the connection, and classifies every other live request as connection-closed. |
| `InitializationEventuallySettles` | Under weak fairness for protocol handling, initialization reaches ready, failed, or closing state. |
| `InboundHandshakeEventuallySettles` | A received inbound handshake is answered or the connection terminates. |
| `MalformedInboundEventuallySettles` | Once a malformed inbound handshake is received, it receives a response or the connection terminates. |
| `EveryRequestSettlesAfterProgressStops` | Once a live request receives no further Progress renewals, its deadline eventually gives it one outcome. |
| `ObservedShutdownEventuallyCloses` | Once pipe loss is observed, internal shutdown reaches closed state. |

`Safety.cfg` checks all state invariants. `Liveness.cfg` checks the four
liveness properties, the closed-state and writer-preemption action properties,
and the core completion and quiescence invariants.

## Fairness and environment assumptions

Weak fairness applies to internal handling after a plugin message arrives,
host initialization and request deadlines, writer acquisition when the writer
is available, malformed-payload failure, and the internal shutdown steps.
Plugin delivery, successful transport writes, beginning a credential request,
receiving a response, progress, fault, caller cancellation, pipe loss, and the
point after which no further Progress arrives are environment actions and are
not required to occur.

The positive model makes request timeout effective from registration through
the serialized write and response wait. `CurrentStalledWrite.cfg` instead
matches the current control flow, where the timer may expire but `SendAsync`
does not observe it until `WriteAsync` returns. With no fairness assumption that
the plugin drains stdin, TLC finds that a request can remain in the writer
forever after Progress has stopped. The positive rule does not abandon that
writer and reuse the pipe: timeout or caller cancellation while a request owns
the writer terminates the connection and settles every other admitted request
as connection-closed. `WriterPreemptionIsContained` checks that action-level
relationship independently; its two mutation configurations demonstrate unsafe
writer reuse and incorrect peer classification. Two additional mutations swap
the initiating timeout and cancellation outcomes; the lifecycle phase records
the cause independently so both swaps fail.

Progress may repeat indefinitely. The model does not claim that a request
settles while the plugin keeps renewing its deadline. `StopProgress` records
the environment condition that no further renewal will arrive;
`EveryRequestSettlesAfterProgressStops` checks settlement from that point.
`UnboundedProgress.cfg` checks the stronger unconditional claim and produces a
cycle in which Progress and deadline ticks repeat forever.

## Implementation alignment

The model and C# tests provide different evidence. TLC checks the design's
permitted interleavings; the implementation gates below exercise selected
product behavior:

| Model rule | Implementation evidence |
| --- | --- |
| Initialization follows the required wire sequence | `PluginProtocolTests.InitializationFollowsTheProtocolSequence` |
| The symmetric handshake accepts only protocol 2.0.0 | `PluginProtocolTests.CompatibleInboundHandshakeUsesProtocolTwo`, `PluginProtocolTests.InvalidOrUnsupportedInboundHandshakeReceivesAnErrorResponse`, and `PluginProtocolTests.InvalidOrUnsupportedOutboundHandshakeStopsInitialization` |
| A dying process settles the current request as plugin failure | `PluginProtocolTests.WhenOnePluginDiesDuringTheRequest_TheNextIsTried` |
| Caller cancellation remains caller cancellation | `PluginProtocolTests.CallerCancellationContinuesToPropagate`, `PluginProtocolTests.CanceledRequestAfterReceiverLossRemainsCancellation`, and `PluginProtocolTests.CancellationWhileWaitingForClosedAdmissionRemainsCancellation` |
| A malformed response header does not end the read loop | `PluginProtocolTests.AProtocolMessageWithNonStringHeadersIsIgnoredRatherThanEndingTheConversation` |
| Concurrent request-ID correlation and out-of-order replies | Unverified. |
| Progress renews the matching implementation timer | Unverified; the design previously described this as tested, but no matching test exists. |
| Pipe loss closes admission before pending requests are collected | `PluginProtocolTests.ARequestAfterReceiverLossIsRejectedWithoutWaitingForItsTimeout`, `PluginProtocolTests.ReceiverLossSettlesARequestAdmittedBeforeThePendingSnapshot`, and `PluginProtocolTests.AdmissionCannotRegisterDuringTheTerminalPendingSnapshot` |
| A stalled in-progress write is bounded by terminating the connection | Not implemented; `CurrentStalledWrite.cfg` abstracts the current control flow. |
| Malformed plugin-originated payloads settle inbound work | `PluginProtocolTests.InvalidOrUnsupportedInboundHandshakeReceivesAnErrorResponse` and `PluginProtocolTests.MalformedInboundLogReceivesAnErrorResponse` |

Formal model-to-implementation correspondence is unverified. In particular,
`CurrentInboundFailure.cfg` abstracts the untracked malformed-payload path in
`PluginConnection.HandleInboundRequestAsync`; its counterexample is evidence
about those modeled mechanics, not a proof that every implementation execution
matches the abstraction.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks correlation, exact completion, handshake authority, writer ownership, inbound-failure containment, and shutdown safety. |
| `Liveness.cfg` | Checks initialization, inbound handling, admitted-request, and shutdown progress. |
| `BrokenHostOnlyHandshake.cfg` | Lets host readiness depend only on the reply to its own handshake. It must violate `ReadyRequiresSymmetricHandshake`. |
| `BrokenInitializationTimeout.cfg` | Removes the host's independent initialization timeout. It must violate `InitializationEventuallySettles` when the plugin remains silent. |
| `BrokenInboundSettlement.cfg` | Combines malformed abandoned work with no host timeout. It must violate `MalformedInboundEventuallySettles` through the malformed path rather than a stalled well-formed response write. |
| `CurrentInboundFailure.cfg` | Drops a malformed plugin-originated handshake as unobserved work. It must violate `InboundFailureIsContained`. |
| `BrokenResponseCorrelation.cfg` | Completes another live request instead of the response's request ID. It must violate `ResponsesCompleteOnlyTheirRequest`. |
| `BrokenProgressCorrelation.cfg` | Renews another live request instead of the progress message's request ID. It must violate `ProgressRenewsOnlyItsRequest`. |
| `CurrentShutdownAdmission.cfg` | Admits a request after the read loop stops. It must violate `RequestAdmissionHasLiveReceiver`. |
| `CurrentShutdownSnapshot.cfg` | Leaves admission open while shutdown snapshots pending requests. It must violate `ShutdownSettlementIsComplete`. |
| `CurrentStalledWrite.cfg` | Lets timeout become observable only after a serialized write returns. It must violate `EveryRequestSettlesAfterProgressStops` when the write stalls. |
| `BrokenWriterPreemptionReuse.cfg` | Releases the writer without terminating the connection. It must violate `WriterPreemptionIsContained`. |
| `BrokenWriterPreemptionClassification.cfg` | Terminates the connection but classifies another live request as timed out. It must violate `WriterPreemptionIsContained`. |
| `BrokenTimeoutPreemptionCause.cfg` | Records timeout preemption but reports caller cancellation for the initiating request. It must violate `WriterPreemptionIsContained`. |
| `BrokenCancellationPreemptionCause.cfg` | Records caller-cancellation preemption but reports timeout for the initiating request. It must violate `WriterPreemptionIsContained`. |
| `UnboundedProgress.cfg` | Checks the intentionally unsupported stronger claim that every request settles even while Progress renewals continue forever. |

All configurations disable TLC's deadlock check because ready, failed, closed,
and completed-request states may intentionally stutter.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because TLC processes sharing a
metadata directory can remove one another's state.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/nuget-plugin-session-lifecycle

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config Safety.cfg NuGetPluginSessionLifecycle.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config Liveness.cfg NuGetPluginSessionLifecycle.tla
```

The fourteen non-positive configurations are expected to exit unsuccessfully:

```bash
for config in \
  BrokenHostOnlyHandshake \
  BrokenInitializationTimeout \
  BrokenInboundSettlement \
  CurrentInboundFailure \
  BrokenResponseCorrelation \
  BrokenProgressCorrelation \
  CurrentShutdownAdmission \
  CurrentShutdownSnapshot \
  CurrentStalledWrite \
  BrokenWriterPreemptionReuse \
  BrokenWriterPreemptionClassification \
  BrokenTimeoutPreemptionCause \
  BrokenCancellationPreemptionCause \
  UnboundedProgress
do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" NuGetPluginSessionLifecycle.tla
done
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 15,477 | 6,139 | 24 | All 10 invariants passed. |
| Liveness | 15,477 | 6,139 | 24 | Four liveness properties and two action properties passed. |

The safety run gave nonzero coverage to all 21 actions enabled by the positive
mode. This includes 501 `TickDeadline`, 77 `ReceiveResponse`, 17
`ReceiveProgress`, 105 `StopProgress`, 1,021 `ObservePipeClosed`, 1,081
`CaptureShutdownSnapshot`, and 896 `SettleShutdownSnapshot` transitions.
`DropMalformedInbound` is disabled by the positive containment mode and is
exercised by the malformed-input counterexample configurations.

During model construction, TLC found that the first positive specification
allowed this sequence:

1. Both handshake messages arrived.
2. The pipe closed before the inbound response was written.
3. Handshake handling continued despite the stopped read loop.
4. The connection published `Ready` with no receiver.

That was a specification error rather than a product finding. The corrected
model makes receiver loss supersede all remaining initialization progress and
checks the rule through `RequestAdmissionHasLiveReceiver` and
`ReadyRequiresSymmetricHandshake`.

Every non-positive configuration exited unsuccessfully on its intended claim.
Six invariant configurations returned TLC status 12; the eight temporal or
action-property configurations returned status 13.

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Host-only handshake | 122 / 102 | 3 | The host received its own successful handshake response and published `Ready` before receiving or answering the plugin's handshake. |
| Missing initialization timeout | 15,373 / 6,098 | 24 | With no fair host timeout and no required peer delivery or transport completion, the session remained in `Handshaking`. |
| Missing inbound settlement | 15,335 / 6,092 | 24 | A malformed plugin-originated handshake became abandoned work and, without host timeout, remained unsettled. |
| Current inbound failure | 59 / 56 | 3 | A malformed plugin-originated handshake faulted abandoned handling without sending the mandatory response; the host can recover only through its independent initialization timeout. |
| Response correlation | 1,894 / 1,015 | 13 | Two requests waited concurrently; receipt of the response carrying request 1's ID completed request 2. |
| Progress correlation | 1,848 / 991 | 13 | Two requests waited concurrently; receipt of Progress carrying request 1's ID recorded a renewal effect for request 2. |
| Current shutdown admission | 656 / 480 | 8 | The read loop stopped, but request 1 was admitted with no live receiver. |
| Current shutdown snapshot | 953 / 653 | 10 | The read loop captured an empty pending set; request 1 was then admitted before settlement, escaped the snapshot, and depended on its ordinary request timeout. |
| Current stalled write | 10,873 / 5,283 | 24 | Request 1 acquired the serialized writer, Progress stopped, the transport never completed the write, and the already-running timer could not settle the request because its result was observed only after the write. |
| Writer-preemption reuse | 684 / 507 | 9 | Caller cancellation released the active writer while leaving the connection ready, the reader running, and admission open. |
| Writer-preemption classification | 734 / 535 | 10 | Caller cancellation closed the connection but classified another live request as timed out instead of connection-closed. |
| Timeout-preemption cause | 812 / 577 | 10 | Timeout closed the connection but reported caller cancellation for the initiating request. |
| Cancellation-preemption cause | 684 / 507 | 9 | Caller cancellation closed the connection but reported timeout for the initiating request. |
| Unbounded Progress | 15,477 / 6,139 | 24 | Request 1 alternated deadline expiry and Progress renewal forever, disproving unconditional settlement while renewals continue. |

The runs used:

- TLA+ tools `v1.8.0`, TLC
  `2026.08.21.155922` (`9787e65`);
- `tla2tools.jar` SHA-256
  `eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`;
- Homebrew OpenJDK `25.0.4.1`; and
- a run on 2026-08-28.

The recorded configurations use two concurrent host requests and one inbound
handshake. Progress may repeat indefinitely. A separate safety run with
`RequestCount = 3` also completed with no error after generating 683,390 states
and finding 190,591 distinct states at depth 31.
