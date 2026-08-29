# Inspect-web operation authority model

This directory model-checks the logical lifecycle owned by
[Inspect-web operation authority](../../inspect-web-operation-authority.md).
It supplements that readable design and does not prove TypeScript, browser,
producer, worker, interop, or managed implementation behavior.

## Scope

`InspectWebOperationAuthority.tla` models two ordered operations owned by one
feature session. Each operation can be queued, admitted to its physical
producer, report bounded progress, settle, and release. The owner can cancel,
supersede, or dispose while work is queued or running.

Logical outcome and physical producer state are distinct. Cancellation,
supersession, and disposal can complete a handle and revoke publication before
the producer settles. Physical settlement remains necessary before
operation-scoped resources release.

The model keeps these values abstract:

- request, progress, result, error, and cancellation-reason payloads;
- browser task and microtask queues;
- TypeScript implementation and DOM rendering;
- producer placement, cancellation checkpoints, and callback mechanics;
- multiple feature sessions and page-wide operation-ID allocation;
- worker epochs, transport, liveness, crashes, and restart;
- managed interop and generated facades;
- feature retry, caching, queueing, and shared-work policy; and
- arbitrary operation cardinality.

Cross-session identity uniqueness, safe-integer sequence exhaustion, producer
adapter behavior, and the neighboring browser-`fetch` case remain requirements
of the owning design's `inspect-web-operation-authority` implementation gate.

## Assumptions

- `OperationA` starts before `OperationB`.
- Starting `OperationB` supersedes an active `OperationA`.
- Each queued producer is eventually admitted or observes cancellation.
- Each running producer eventually succeeds, fails, or observes cancellation.
- Each settled producer eventually releases its operation-scoped resources.
- Progress is bounded to one attempt per operation in the checked
  configuration. One is sufficient to expose stale or post-terminal delivery;
  the bound makes no throughput claim.
- Disposal is optional. After disposal, physical producers may still settle
  and release, but no new logical operation is legal.

Weak fairness in `Spec` covers producer admission, settlement, and release. It
also starts each operation when doing so remains continuously enabled.

## Checked properties

| Design property | Model property |
| --- | --- |
| One logical outcome per operation | `OneLogicalCompletion`, `OutcomeCountAgrees` |
| One forwarded cancellation request | `CancellationForwardedAtMostOnce` |
| Progress and terminal publication require current authority | `PublicationRequiresAuthority` |
| Visible state remains owned by the current operation | `VisibleStateOwnedByCurrent` |
| Release follows physical settlement | `ReleasedProducerIsTerminal` |
| No callback is observed after release | `NoCallbackAfterRelease` |
| Observed pre-start cancellation prevents producer execution | `CanceledBeforeStartDoesNotRun` |
| Disposal prevents new operations | `DisposedOwnerStartsNothing` |
| Started producers eventually settle | `StartedEventuallySettles` |
| Started operations eventually receive a logical outcome | `StartedEventuallyCompletesLogically` |
| Settled producers eventually release | `SettledEventuallyReleases` |

## Running TLC

Use the repository-pinned TLA+ tools described by the
[setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/inspect-web-operation-authority
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup \
  -config InspectWebOperationAuthority.cfg \
  InspectWebOperationAuthority.tla
```

The recorded run used OpenJDK 21.0.12 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
With two operations and one progress attempt per operation, TLC generated
9,017 states, found 3,917 distinct states, reached depth 14, and reported no
error.

The positive model explicitly explores a suppressed callback attempt after
release. Three scratch guard-removal probes make stale progress delivery,
producer execution after observed pre-start cancellation, and post-release
callback delivery unconditional under the positive configuration. They
respectively violate `PublicationRequiresAuthority`,
`CanceledBeforeStartDoesNotRun`, and `NoCallbackAfterRelease`. These probes
establish non-vacuity of the modeled witnesses but are not additional product
behaviors or checked-in configurations.

## Counterexample mutations

Each mutation configuration fails with its named invariant:

| Configuration suffix | Deliberate defect | Expected violation |
| --- | --- | --- |
| `StaleProgress` | Delivers progress after authority is lost | `PublicationRequiresAuthority` |
| `StaleSuccess` | Publishes late success from an old operation | `PublicationRequiresAuthority` |
| `StaleFailure` | Publishes late failure from an old operation | `PublicationRequiresAuthority` |
| `DuplicateTerminal` | Completes logically after cancellation already completed the handle | `OneLogicalCompletion` |
| `CleanupMutatesNewer` | Old release changes the newer visible owner | `VisibleStateOwnedByCurrent` |
| `CallbackAfterRelease` | Delivers progress after callback release | `NoCallbackAfterRelease` |
| `StartAfterDispose` | Starts the second operation after owner disposal | `DisposedOwnerStartsNothing` |
| `RunCanceledBeforeStart` | Runs a queued producer after cancellation was observed | `CanceledBeforeStartDoesNotRun` |

Run a mutation by substituting its complete configuration filename in the TLC
command. A successful mutation check is a concrete invariant violation; a
clean exit means the mutation gate is vacuous or broken. All eight mutations
were run with OpenJDK 21.0.12, TLA+ tools 1.8.0, and one TLC worker; each
produced its named violation.
