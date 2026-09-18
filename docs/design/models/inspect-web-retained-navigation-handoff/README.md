# Inspect Web Retained Navigation Handoff Model

This model composes the cutover boundary from
[Inspect Web Retained Workspace Realization](../../inspect-web-retained-workspace-realization.md)
with the initial installation boundary from
[Inspect Web Navigation Consumer](../../inspect-web-navigation-consumer.md).
It begins when a restored candidate is ready, issues one exact realization,
publication ordinal, and Navigation effect authority at cutover, and permits
the two publications to reach TypeScript out of order.

The model does not repeat the later focus, announcement, history, or
synchronization lifecycle modeled by
[`UiEffectLifecycle.tla`](../inspect-web-navigation-consumer/UiEffectLifecycle.tla).

## Claims

The model checks that:

- only the current realization/ordinal/authority tuple installs,
- a lower publication cannot replace current presentation,
- installation is recorded before acknowledgement,
- stale out-of-order delivery is abandoned, and
- predecessor Navigation state is retired before successor presentation
  installs.

Two realizations and two publication ordinals are sufficient to exercise the
one-step replacement and out-of-order delivery boundary. This is bounded
evidence for that instance, not an unbounded proof.

## Configurations

| Configuration | Exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Complete bounded safety state space |
| `BrokenStaleInstallation.cfg` | 12 | Removing current-tuple validation permits stale installation |
| `BrokenEarlyAcknowledge.cfg` | 12 | Acknowledgement before recorded installation violates ordering |
| `BrokenMissingPredecessorRetirement.cfg` | 12 | Successor cutover without predecessor-slot retirement violates lifetime isolation |
| `ReachabilityOutOfOrderDelivery.cfg` | 12 | Successor consumption followed by stale predecessor abandonment is reachable |

Exit 12 is the expected invariant counterexample for a broken policy or a
negated reachability witness.

## Run

From the repository root:

```bash
model=docs/design/models/inspect-web-retained-navigation-handoff
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
```

Expected exit codes are registered in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).
