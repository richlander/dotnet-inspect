# Classic async MoveNext lifecycle model

This TLA+ model pairs with the
[classic async splice Lean model](../classic-async-splice/). It checks the
re-entry lifecycle of a Roslyn classic async state machine that the
[general inverse](../../classic-async-reconstruction.md#general-inverse)
relies on when it removes the protocol roles and splices the continuations.

The normative owner remains
[`classic-async-reconstruction.md`](../../classic-async-reconstruction.md).
This model changes no product contract or runtime path. It is evidence about
the model, not about Roslyn or the C# inverse; the fixture family and the
compile-back gates named in the owner remain the evidence that the roles are
Roslyn's.

## Paired boundary

Lean owns the splice equivalence: for every body of its grammar and every
sequence of suspension choices, the lowered machine's user-effect trace equals
the sequential body's trace. This model does not restate that. It checks the
behavior over time that the role table assumes: the state field moves
`-1 → N → -1` around each suspension and ends at `-2`; a resume enters only
through dispatch on a stored state whose awaiter field is set; the builder
completes exactly once; nothing re-enters after `-2`; and an exit through an
enclosing finally runs the guarded body only when the state is `-1`.

The two models share one contract, the role table's state constants
(`0..Awaits-1` for suspension sites, `-1` running, `-2` done) and the two
completion callbacks (`SetResult`, `SetException`), and neither restates the
other's result.

## Correspondence to the role table

| Model action | Role table row |
| --- | --- |
| `Start` | the kickoff's `Start`, entering `MoveNext` with the state the kickoff stored (`-1`) |
| `Complete(k)` | suspension site `k` whose `IsCompleted` test was true: execution runs past the site |
| `Suspend(k)` | suspension site `k` storing `<>1__state = k`, `<>u__k`, and returning through enclosing finally regions |
| `ExitFinally` | the finally guard `state < 0`: the body runs only with the running state |
| `RunFinally` | a normal exit through a finally region while running |
| `Resume(k)` | dispatch on the stored state to its landing: awaiter restored and cleared, state reset to `-1` |
| `SetResult` | the completion tail: state `-2`, one `SetResult` |
| `SetException` | the completion catch: state `-2`, one `SetException` |

`Mutation` selects a deliberately broken machine for a negative control:
`ResumeWithoutStore` (a resume that ignores the stored state and awaiter),
`SkipLandingReset` (a landing that leaves the state at `N`),
`DoubleCompletion` (a completion path that may call `SetResult` twice), and
`FinallyOnSuspend` (a finally guard that runs the body while suspending).

## Checked properties

| Lifecycle property | Model property |
| --- | --- |
| While user code runs the state is `-1` | `RunningStateIsRunning` |
| A suspended machine holds a site state whose awaiter field is set | `SuspendedStoresItsAwaiter` |
| At most one awaiter field is live, and only while suspended | `OneLiveAwaiter` |
| The builder completes at most once | `CompletesAtMostOnce` |
| Done means completed exactly once with state `-2` | `DoneMeansCompletedOnce` |
| A finally body never runs while the machine returns suspended | `FinallyBodiesRanRunning` |
| Every resume entered through dispatch on the stored state with its awaiter | `ResumesEnterStoredState` |
| Under fairness every machine completes | `EventuallyDone` |

Done is terminal by construction: every action's precondition excludes the
`Done` phase, so `CHECK_DEADLOCK` is off.

## Configurations

| Configuration | Purpose | Expected result |
| --- | --- | --- |
| `Lifecycle.cfg` | Roslyn's lowering, three suspension sites | Exit `0` |
| `BrokenResumeWithoutStore.cfg` | Resume without the stored state and awaiter | Exit `12` (`OneLiveAwaiter`) |
| `BrokenSkipLandingReset.cfg` | Landing leaves the state at `N` | Exit `12` (`RunningStateIsRunning`) |
| `BrokenDoubleCompletion.cfg` | `SetResult` may run twice | Exit `12` (`CompletesAtMostOnce`) |
| `BrokenFinallyOnSuspend.cfg` | Finally body runs while suspending | Exit `12` (`FinallyBodiesRanRunning`) |

Run from this directory with the pinned `tla2tools.jar`:

```sh
java -cp "$TLA_TOOLS_JAR" tlc2.TLC -config Lifecycle.cfg -workers 2 -cleanup ClassicAsyncMoveNextLifecycle.tla
```

`eng/run-tla-checks.sh` enforces the expected exit codes above through
`eng/tla-expected-exit-codes.txt`.

## Limits

The model abstracts user code to the positions of its suspension sites, bounds
finally exits to one per site position so the state space stays finite, and
counts completions rather than recording their order. It does not model
await-in-`catch` or await-in-`finally` lowering (those carry their own
pending-exception selector; stage 2 declines the `finally` form), custom
awaiters, or Debug lowering.
