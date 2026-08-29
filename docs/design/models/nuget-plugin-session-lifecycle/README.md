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
- Do initialization, admitted requests, and shutdown eventually settle under
  the stated fairness assumptions?

## Model boundary

The owner is the credential-plugin conversation in
`docs/design/nuget-authentication.md`. The finite model contains:

- one plugin process and read loop;
- one symmetric initialization conversation;
- two host-originated requests, enough to exercise correlation and serialized
  writes;
- one progress message per request, enough to distinguish renewal of the
  matching deadline from renewal of another request;
- caller cancellation, plugin response, plugin fault, timeout, and pipe-loss
  outcomes; and
- a split pipe-loss sequence that exposes admission between observing EOF,
  collecting pending requests, and settling that collection.

The plugin is an external protocol peer, not a hostile in-process caller. Its
messages may be malformed, delayed, reordered across request IDs, or terminated
by process or pipe failure.

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

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every state remains within the declared finite shape. |
| `RequestCompletionIsExact` | A completed request has exactly one typed outcome; live and unused requests have none. |
| `WriterIsOwnedByLiveWork` | The serialized writer belongs to exactly one live request or inbound handshake. |
| `ReadyRequiresSymmetricHandshake` | Readiness requires both handshake directions and the Authentication claim. |
| `RequestAdmissionHasLiveReceiver` | A request is admitted only while the read loop can receive its response. |
| `ResponsesCompleteOnlyTheirRequest` | A response completes only the request carrying its ID. |
| `ProgressRenewsOnlyItsRequest` | Progress renews only the matching live request's deadline. |
| `InboundFailureIsContained` | Malformed inbound handshake handling cannot disappear as unobserved work. |
| `ReadLoopLossClosesAdmission` | Observing terminal pipe loss closes request admission. |
| `ShutdownSettlementIsComplete` | The shutdown collection settles every request admitted before admission closed. |
| `ClosedConnectionIsQuiescent` | A closed connection owns no writer or live request and cannot admit work. |
| `InitializationEventuallySettles` | Under weak fairness for protocol handling, initialization reaches ready, failed, or closing state. |
| `InboundHandshakeEventuallySettles` | A received inbound handshake is answered or the connection terminates. |
| `EveryAdmittedRequestSettles` | Serialized write and deadline progress eventually give every admitted request one outcome. |
| `ObservedShutdownEventuallyCloses` | Once pipe loss is observed, internal shutdown reaches closed state. |

`Safety.cfg` checks all state invariants. `Liveness.cfg` checks the four
temporal properties with the core completion and quiescence invariants.

## Fairness and environment assumptions

Weak fairness applies to internal protocol handling, serialized writes,
deadline ticks and timeout classification, and the internal shutdown steps.
Beginning a credential request, receiving a response, progress, fault, caller
cancellation, and pipe loss are environment actions and are not required to
occur.

Progress is bounded to one message per request. This is a finite abstraction,
not a claim that real plugins emit at most one update. The bound ensures a
plugin cannot postpone the model's fallback timeout forever while preserving
the behavior under test: renewal targets one request identified by its request
ID.

## Implementation alignment

The model and C# tests provide different evidence. TLC checks the design's
permitted interleavings; the implementation gates below exercise selected
product behavior:

| Model rule | Implementation evidence |
| --- | --- |
| Initialization follows the required wire sequence | `PluginProtocolTests.InitializationFollowsTheProtocolSequence` |
| The host and plugin both participate in the handshake | The real subprocess used by `PluginProtocolTests.FullExchange_YieldsCredentials` will not serve credentials until its handshake is answered. |
| A dying process settles the current request as plugin failure | `PluginProtocolTests.WhenOnePluginDiesDuringTheRequest_TheNextIsTried` |
| Caller cancellation remains caller cancellation | `PluginProtocolTests.CallerCancellationContinuesToPropagate` |
| A malformed response header does not end the read loop | `PluginProtocolTests.AProtocolMessageWithNonStringHeadersIsIgnoredRatherThanEndingTheConversation` |
| Concurrent request-ID correlation and out-of-order replies | Unverified. |
| Progress renews the matching implementation timer | Unverified; the design previously described this as tested, but no matching test exists. |
| Pipe-loss admission and pending-request collection are atomic | Unverified. |
| Malformed plugin-originated payloads settle inbound work | Not implemented; tracked by #3551. |

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
| `CurrentInboundFailure.cfg` | Drops a malformed plugin-originated handshake as unobserved work. It must violate `InboundHandshakeEventuallySettles`. |
| `BrokenResponseCorrelation.cfg` | Completes another live request instead of the response's request ID. It must violate `ResponsesCompleteOnlyTheirRequest`. |
| `BrokenProgressCorrelation.cfg` | Renews another live request instead of the progress message's request ID. It must violate `ProgressRenewsOnlyItsRequest`. |
| `BrokenShutdownAdmission.cfg` | Leaves admission open while shutdown snapshots pending requests. It must violate `ShutdownSettlementIsComplete`. |

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

The five non-positive configurations are expected to exit unsuccessfully:

```bash
for config in \
  BrokenHostOnlyHandshake \
  CurrentInboundFailure \
  BrokenResponseCorrelation \
  BrokenProgressCorrelation \
  BrokenShutdownAdmission
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
| Safety | 4,034 | 2,003 | 22 | All 11 invariants passed. |
| Liveness | 4,034 | 2,003 | 22 | All four temporal properties passed across five behavior-checking branches. |

The safety run gave nonzero coverage to all 20 actions. This includes 95
`ReceiveResponse`, 20 `ReceiveProgress`, 255 `ObservePipeClosed`, 284
`CaptureShutdownSnapshot`, and 408 `SettleShutdownSnapshot` transitions.

During model construction, TLC found that the first positive specification
allowed this sequence:

1. Both handshake messages arrived.
2. The pipe closed before the inbound response was written.
3. Handshake handling continued despite the stopped read loop.
4. The connection published `Ready` with no receiver.

That was a specification error rather than a product finding. The corrected
model makes receiver loss supersede all remaining initialization progress and
checks the rule through `ReadLoopLossClosesAdmission` and
`ReadyRequiresSymmetricHandshake`.

Every non-positive configuration exited unsuccessfully on its intended claim.
The four invariant mutations returned TLC status 12; the temporal current-
mechanics configuration returned status 13.

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Host-only handshake | 69 / 64 | 3 | The host received its own successful handshake response and published `Ready` before receiving or answering the plugin's handshake. |
| Current inbound failure | 4,026 / 1,997 | 22 | A malformed plugin-originated handshake faulted abandoned handling, and the connection could stutter forever without responding or terminating. |
| Response correlation | 652 / 471 | 13 | Two requests waited concurrently; the response carrying request 1's ID completed request 2. |
| Progress correlation | 620 / 455 | 13 | Two requests waited concurrently; progress carrying request 1's ID renewed request 2's deadline. |
| Shutdown admission | 463 / 384 | 10 | The read loop stopped and captured an empty pending set; request 1 was then admitted before settlement and escaped the snapshot. |

The runs used:

- TLA+ tools `v1.8.0`, TLC
  `2026.08.21.155922` (`9787e65`);
- `tla2tools.jar` SHA-256
  `eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`;
- Homebrew OpenJDK `25.0.4.1`; and
- a run on 2026-08-28.

The model's finite bound is two concurrent host requests, one inbound
handshake, and at most one progress message per request.
