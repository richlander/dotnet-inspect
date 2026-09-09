# Inspect Web Worker control model

This finite model checks the operation-addressed control mechanism owned by
[the Inspect Web Worker runtime](../../inspect-web-worker-runtime.md). It does
not model Package Query policy, managed coordinator state, browser rendering,
or concrete payload codecs.

The model keeps operation state, feature controllability, the page's one
outstanding request, the per-operation control sequence, the Worker's sequence
high-water, handler execution, and acknowledgment identity and delivery
separate. Settlement may release the operation payload while a control handler
or acknowledgment remains pending.

`Safety.cfg` checks:

- at most one page-side control request is outstanding;
- acknowledgments name the pending request;
- `NotActive` follows an operation that is no longer controllable;
- settlement does not discard the pending request;
- replay is never acknowledged; and
- a running handler owns the exact fresh sequence.

It also checks that a pending request eventually reaches `Acknowledged` or
`NotActive` under weak fairness for Worker handling and acknowledgment
delivery.

The remaining configurations are bounded evidence:

- `ReachabilitySettlementBeforeAcknowledgment.cfg` proves the settlement race
  is reachable;
- `BrokenSecondOutstanding.cfg` proves the one-outstanding invariant detects a
  queued second request;
- `BrokenMismatchedAcknowledgment.cfg` proves the committed-outcome invariant
  detects acceptance of a response naming a different operation and control
  sequence;
- `BrokenReplayAcknowledged.cfg` proves the replay invariant detects an
  acknowledged replay; and
- `BrokenDropPendingOnSettlement.cfg` proves settlement cannot erase the
  request before its acknowledgment.

Run the repository gate:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  ./eng/run-tla-checks.sh \
  docs/design/models/inspect-web-worker-control
```
