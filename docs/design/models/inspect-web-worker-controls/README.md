# Inspect Web Worker controls model

This model checks the operation-addressed control contract owned by
[`inspect-web-worker-runtime.md`](../../inspect-web-worker-runtime.md).
Package Query match-credit replenishment is the first named consumer, but the
model contains no Package Query policy.

The finite state space uses one operation and two possible control sequences.
It checks:

- at most one posted control is outstanding;
- cancellation, settlement, and `not-active` close further control admission;
- physical settlement retains an outstanding response obligation;
- only the exact control sequence can acknowledge the request; and
- hard Worker destruction completes a pending caller and releases the record.

`Safety.cfg` checks the contract. `BusyRejectionReachability.cfg` and
`SettlementBeforeAcknowledgmentReachability.cfg` each expect one invariant
violation to demonstrate overlap rejection and settlement before
acknowledgment independently. The three `Broken*.cfg` configurations show that
posting after closure, retiring before acknowledgment, and accepting a
mismatched acknowledgment violate the corresponding properties.
