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
- one participant-local lazy open, including budget rejection and rejection
  after reservation;
- cached ready or rejected outcomes for ordinary access, with unavailable
  one-shot completion terminally releasing its participant;
- callback-local views that survive release of the group's retained reference;
- the release-after-use path used by one-shot asynchronous participant work;
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
| `ParticipantOpensAtMostOnce` | A participant's cached ready, rejected, or released result is never reopened. |
| `ActiveCallbacksHoldLocalViews` | An active callback retains its local immutable image view even if the group drops its own reference. |
| `NoAdmissionAfterDisposal` | Callback admission occurs only while the group is open. |
| `GroupReleaseWaitsForQuiescence` | Full-group resource release begins only after every admitted callback settles. |
| `OwnedResourcesPrecedeGroupSnapshots` | Full-group release disposes owned resources before participant snapshots. |
| `RejectedReleaseAfterUseIsTerminal` | A one-shot callback that observes a cached rejection releases that participant before completing while the group remains open. |
| `ActiveViewsSurviveGroupRelease` | Full-group snapshot release never reaches a participant still used by an active callback. |
| `GroupReleaseBeginsExactlyOnce` | Disposal claims the full-group release path at most once. |
| `GroupReleaseRequiresDisposal` | Full-group release cannot begin while the group remains open. |
| `ReleasedGroupOwnsNothing` | A released group retains no image charge, resource, or participant snapshot. |
| `ParticipantLocalOpening` | A participant waiting in another participant's open path does not disable this participant's open action. |
| `EveryAdmittedCallbackSettles` | Under weak fairness, each admitted callback reaches active completion or a cached unavailable result. |
| `EveryStartedOpenSettles` | Under weak fairness, each started open reaches ready, rejected, or released. |
| `DisposedGroupEventuallyReleases` | Under weak fairness, a disposed group eventually reaches terminal release. |

The admission, quiescence, resource-order, and active-view claims use
independent monotonic witness variables. Weakening the corresponding
transition guard falsifies the witness rather than making the invariant a
restatement of that guard. The rejected release-after-use claim is a
post-state invariant over completed one-shot callbacks and has its own
retention mutation.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Explores three callbacks over two participants with one retained-image slot and checks every safety invariant. |
| `Liveness.cfg` | Explores two callbacks over two participants and checks callback, open, and disposal progress under weak fairness. |
| `BrokenEarlyRelease.cfg` | Enables a deliberate mutation that lets full-group release begin before callbacks quiesce; TLC must report a counterexample. |
| `BrokenResourceOrder.cfg` | Lets full-group snapshot release begin while the owned resource remains live; TLC must report the ordering violation. |
| `BrokenRejectedRetention.cfg` | Retains a rejected participant after unavailable one-shot completion; TLC must report the terminal-release violation. |

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
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC -cleanup \
  -config BrokenEarlyRelease.cfg \
  AssemblyContextGroupLifecycle.tla
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC -cleanup \
  -config BrokenResourceOrder.cfg \
  AssemblyContextGroupLifecycle.tla
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC -cleanup \
  -config BrokenRejectedRetention.cfg \
  AssemblyContextGroupLifecycle.tla
```

Run these commands sequentially. Concurrent TLC processes in one directory
share the default `states/` checkpoint path unless each receives a distinct
`-metadir`.

The first two commands must complete without errors. The broken configurations
must fail `GroupReleaseWaitsForQuiescence`,
`OwnedResourcesPrecedeGroupSnapshots`, and
`RejectedReleaseAfterUseIsTerminal`, respectively. A successful mutation run
would mean its probe no longer exercises the intended rule.

## TLC evidence

Checked on Linux with Eclipse Temurin/OpenJDK `25.0.4.1` and the
repository-pinned TLA+ `v1.8.0` prerelease (`TLC2 2026.08.21.155922`, rev
`9787e65`). The checked `tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 10,671 | 4,797 | 19 |
| `Liveness.cfg` | No error | 1,343 | 739 | 17 |
| `BrokenEarlyRelease.cfg` | `GroupReleaseWaitsForQuiescence` violated | 52 | 46 | 4 |
| `BrokenResourceOrder.cfg` | `OwnedResourcesPrecedeGroupSnapshots` violated | 57 | 49 | 4 |
| `BrokenRejectedRetention.cfg` | `RejectedReleaseAfterUseIsTerminal` violated | 229 | 155 | 6 |

The normal configurations explored their complete bounded state graphs. The
broken configurations stopped at their first expected counterexamples:
full-group release began while a callback was still live, snapshot release
began before resource release, and unavailable one-shot completion retained its
rejected participant.
