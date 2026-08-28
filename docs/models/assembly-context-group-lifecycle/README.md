# Assembly context group lifecycle model

`AssemblyContextGroupLifecycle.tla` models the existing
`DotnetInspector.Queries.AssemblyContextGroup` callback, image-retention, and
disposal protocol. It is an executable interaction model for the workspace
lifetime described by [`../../inspection-space.md`](../../inspection-space.md).

## Scope

The model contains one group with bounded callbacks and participants. Each
participant has an independent image-opening state. Images have a unit retained
charge, so the configured budget measures the number of images that may be
reserved or ready at once.

The modeled interactions are:

- callback admission before disposal;
- participant-local lazy opening, including budget rejection, typed rejection
  after reservation, and exceptional failure before or after reservation that
  leaves ordinary access retryable;
- cached ready or rejected outcomes, distinct exceptional open and
  already-released failures, and callback ownership of each in-flight open;
- a separate finalizing phase after result publication or exceptional rollback,
  allowing same-participant waiters to run before outer callback completion;
- callback-local views that survive release of the group's retained reference;
- the release-after-use path used by one-shot asynchronous participant work,
  including successful, rejected, and exceptional completion;
- disposal that closes admission immediately and waits for callbacks to become
  quiescent;
- owned-resource release before full-group snapshot release; and
- exactly one terminal full-group release.

The `releaseOnExit` choice is immutable model input. TLC explores every
assignment, so each bounded callback may use ordinary retained access or the
one-shot release-after-use path.

## Non-claims

The model does not cover:

- package-role topology or its proposed asynchronous realization contract;
- artifact acquisition, source authorization, or workspace admission;
- metadata validation or immutable byte contents;
- exception payloads or `AggregateException` construction;
- workspace ownership of multiple groups; or
- implementation conformance.

The Release tests in
`src/DotnetInspector.Queries.Tests/InspectionWorkspaceTests.cs` remain the
implementation gates. The model checks whether the abstract protocol's own
rules are consistent over its bounded state space.

## Checked properties

| Property | Claim |
| --- | --- |
| `RetainedImagesStayWithinBudget` | Concurrent reservations never exceed the configured aggregate image budget. |
| `RetainedImageAccountingIsExact` | The retained charge equals the participants whose image is reserved or ready. |
| `OpeningOwnershipIsExact` | Every opening or reserved participant has exactly one admitted callback that owns that attempt. |
| `CallbackOutcomesMatchPhases` | Every finalizing or completed callback records success, typed rejection, released-participant failure, or exceptional open failure. |
| `ActiveCallbacksHoldLocalViews` | An active callback retains its local immutable image view even if the group drops its own reference. |
| `NoAdmissionAfterDisposal` | Callback admission occurs only while the group is open. |
| `GroupReleaseWaitsForQuiescence` | Full-group resource release begins only after every admitted callback settles. |
| `OwnedResourcesPrecedeGroupSnapshots` | Full-group release disposes owned resources before participant snapshots. |
| `RejectedReleaseAfterUseIsTerminal` | A one-shot callback that observes a cached rejection releases that participant before completing while the group remains open. |
| `CompletionHonorsReleasePolicy` | Ordinary completion retains the participant while one-shot completion releases it after acquiring the participant gate while the group remains open. |
| `ReleasedParticipantAccessFails` | Access that reaches an already-released participant records a terminal failure rather than a cached typed rejection. |
| `ExceptionalFailureRollsBackForRetry` | Exceptional failure records the opening callback's failure and returns the participant to a retryable state before finalization, preserving an unreserved charge or releasing a reservation. |
| `ActiveViewsSurviveGroupRelease` | Full-group snapshot release never reaches a participant still used by an active callback. |
| `GroupReleaseBeginsExactlyOnce` | Disposal claims the full-group release path at most once. |
| `GroupReleaseRequiresDisposal` | Full-group release cannot begin while the group remains open. |
| `ReleasedGroupOwnsNothing` | A released group retains no image charge, resource, or participant snapshot. |
| `ParticipantLocalOpening` | A participant waiting in another participant's open path does not disable this participant's open action. |
| `EveryAdmittedCallbackSettles` | Under weak fairness, each admitted callback reaches successful, rejected, or exceptional completion. |
| `EveryStartedOpenSettlesOrRollsBack` | Under weak fairness, each started open reaches ready, rejected, released, or retryable rollback. |
| `DisposedGroupEventuallyReleases` | Under weak fairness, a disposed group eventually reaches terminal release. |

The admission, quiescence, resource-order, active-view, completion-policy,
released-access, and exceptional-rollback claims use independent monotonic
witness variables. Weakening the corresponding transition rule falsifies its
witness rather than making the invariant a restatement of that rule. The
rejected release-after-use claim is a post-state invariant over completed
one-shot callbacks and has its own retention mutation.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Explores three callbacks over two participants with one retained-image slot and checks every safety invariant. |
| `Liveness.cfg` | Explores three callbacks over two participants, including a same-participant waiter, and checks callback, open, and disposal progress under weak fairness. |
| `BrokenEarlyRelease.cfg` | Enables a deliberate mutation that lets full-group release begin before callbacks quiesce; TLC must report a counterexample. |
| `BrokenActiveView.cfg` | Checks the early-release mutation only against active local-view survival, without the earlier quiescence failure masking it. |
| `BrokenResourceOrder.cfg` | Lets full-group snapshot release begin while the owned resource remains live; TLC must report the ordering violation. |
| `BrokenRejectedRetention.cfg` | Retains a rejected participant after unavailable one-shot completion; TLC must report the terminal-release violation. |
| `BrokenSuccessfulPolicy.cfg` | Inverts successful ordinary and one-shot release policy; TLC must report the completion-policy violation. |
| `BrokenReleasedAccess.cfg` | Reports access to an already-released participant as cached rejection; TLC must report the outcome violation. |
| `BrokenPreReservationFailure.cfg` | Caches exceptional pre-reservation failure as rejection while another participant saturates the budget; TLC must report the retryability violation without a charge change. |
| `BrokenExceptionalRollback.cfg` | Caches exceptional post-reservation failure as rejection; TLC must report the retryability violation after releasing the charge. |

## Running TLC

Use the repository-pinned `v1.8.0` `tla2tools.jar`:

```bash
cd docs/models/assembly-context-group-lifecycle
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg \
  AssemblyContextGroupLifecycle.tla
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Liveness.cfg \
  AssemblyContextGroupLifecycle.tla
for config in BrokenEarlyRelease BrokenActiveView BrokenResourceOrder \
  BrokenRejectedRetention BrokenSuccessfulPolicy BrokenReleasedAccess \
  BrokenPreReservationFailure BrokenExceptionalRollback; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    AssemblyContextGroupLifecycle.tla
done
```

Run these commands sequentially. Concurrent TLC processes in one directory
share the default `states/` checkpoint path unless each receives a distinct
`-metadir`.

The first two commands must complete without errors. The broken configurations
must fail `GroupReleaseWaitsForQuiescence`,
`ActiveViewsSurviveGroupRelease`,
`OwnedResourcesPrecedeGroupSnapshots`, and
`RejectedReleaseAfterUseIsTerminal`,
`CompletionHonorsReleasePolicy`,
`ReleasedParticipantAccessFails`, and
`ExceptionalFailureRollsBackForRetry` for both exceptional-failure probes,
respectively. A successful mutation run would mean its probe no longer
exercises the intended rule.

## TLC evidence

Checked on Linux with Eclipse Temurin/OpenJDK `25.0.4.1` and the
repository-pinned TLA+ `v1.8.0` prerelease (`TLC2 2026.08.21.155922`, rev
`9787e65`). The checked `tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 44,447 | 18,450 | 21 |
| `Liveness.cfg` | No error | 44,447 | 18,450 | 21 |
| `BrokenEarlyRelease.cfg` | `GroupReleaseWaitsForQuiescence` violated | 53 | 47 | 4 |
| `BrokenActiveView.cfg` | `ActiveViewsSurviveGroupRelease` violated | 3,066 | 1,466 | 10 |
| `BrokenResourceOrder.cfg` | `OwnedResourcesPrecedeGroupSnapshots` violated | 59 | 51 | 4 |
| `BrokenRejectedRetention.cfg` | `RejectedReleaseAfterUseIsTerminal` violated | 334 | 202 | 6 |
| `BrokenSuccessfulPolicy.cfg` | `CompletionHonorsReleasePolicy` violated | 259 | 168 | 6 |
| `BrokenReleasedAccess.cfg` | `ReleasedParticipantAccessFails` violated | 2,667 | 1,348 | 7 |
| `BrokenPreReservationFailure.cfg` | `ExceptionalFailureRollsBackForRetry` violated | 2,298 | 1,201 | 7 |
| `BrokenExceptionalRollback.cfg` | `ExceptionalFailureRollsBackForRetry` violated | 423 | 300 | 5 |

The normal configurations explored their complete bounded state graphs. The
broken configurations stopped at their first expected counterexamples:
full-group release began while a callback was still live, an active local view
was released by that mutation, snapshot release began before resource release,
unavailable one-shot completion retained its rejected participant, successful
completion inverted its release policy, already-released access returned cached
rejection, and exceptional failure before or after reservation cached a
terminal rejection instead of returning the participant to retryable state.
