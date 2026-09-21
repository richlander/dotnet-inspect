# Inspect Web Retained Workspace Realization Model

This model exercises the Browser-owned selection contract from
[Inspect Web Retained Workspace Realization](../../inspect-web-retained-workspace-realization.md).
It instantiates `WorkspaceRealizationCutover` as the exact lower owner for
candidate construction, cutover, operation admission, drainage, and settlement.
The Browser model adds retained-definition identity, activation intent,
selection, active deletion, settlement presentation, and aggregate admission
without copying the coordinator transitions.

This prerequisite binds the coordinator owner's `coordinatorState` and
`closeTargets` variables explicitly. The retained-realization model remains an
`Open`-only consumer: it does not invoke `CloseCoordinator` or emulate terminal
retirement locally. Every imported action preserves the bound lifecycle state,
and `Coordinator!Safety` is rechecked over that behavior. A later completion
composition may consume the owner-issued close actions directly.

Two retained definitions, the coordinator's three realization identities, and
a two-charged-realization model bound are sufficient to exercise A, B, then A
reactivation, successor replacement, candidate failure, backpressure, and
predecessor settlement. Production uses the design's four-realization bound.
The model bounds are evidence for those instances, not an unbounded proof.

## Claims

The model checks that:

- a retained definition does not itself grant active realization authority,
- at most one exact realization is active,
- reactivating a definition uses fresh realization identity,
- stale activation completion cannot replace newer intent,
- candidate failure preserves the incumbent,
- active deletion waits for successful successor activation,
- failed predecessor settlement remains visible, and
- the configured aggregate realization bound is preserved, and
- draining predecessors eventually settle under weak fairness.

The model also checks reachability of fresh reactivation, failure with an
incumbent, and active deletion through replacement.

## Configurations

| Configuration | Exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Complete bounded safety state space |
| `Liveness.cfg` | 0 | Conditional predecessor-settlement progress |
| `BrokenReviveDefinition.cfg` | 12 | Retained definition identity cannot revive runtime authority |
| `BrokenStaleActivation.cfg` | 12 | Older activation completion cannot publish after newer intent |
| `BrokenFailureReplacesActive.cfg` | 12 | Clearing a real incumbent selection after candidate failure violates the active association |
| `BrokenDeleteBeforeReplacement.cfg` | 12 | Active deletion waits for successful replacement |
| `BrokenDeleteBeforeCandidateSettlement.cfg` | 12 | Displaced activation target remains retained until candidate settlement |
| `BrokenForgetSettlementFailure.cfg` | 12 | Browser composition preserves coordinator settlement failure |
| `BrokenNeverSettle.cfg` | 13 | Omitting predecessor settlement defeats liveness |
| `ReachabilityFreshReactivation.cfg` | 12 | A, B, then fresh A activation is reachable |
| `ReachabilityFailureWithIncumbent.cfg` | 12 | Candidate failure with an incumbent is reachable |
| `ReachabilityDeleteThroughReplacement.cfg` | 12 | Active deletion through successor activation is reachable |

Exit 12 is the expected invariant counterexample for a broken policy or a
negated reachability witness. Exit 13 is the expected temporal-property
counterexample for omitted settlement.

## Run

From the repository root:

```bash
model=docs/design/models/inspect-web-retained-workspace-realization
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
```

Expected exit codes are registered in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

The focused run on 2026-09-14 used TLA Tools `2026.08.11.125311` with OpenJDK
`25.0.4.1`. Safety and liveness each explored 841,859 generated states and
236,408 distinct states to depth 28. All twelve configured semantic verdicts
matched the manifest.
