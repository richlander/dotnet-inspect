# Worker nonterminal event ordering

Owner: [Worker runtime](../../inspect-web-worker-runtime.md#durable-nonterminal-delivery),
implementation tracker #5418, production adopter #5987.

This finite model isolates delivery of already-admitted operation messages on
one ordered Worker channel. Two pre-issued references retain the product join
currency: epoch token, operation ID, and operation sequence. It does not mint
identities or model admission, epoch allocation, logical publication,
backpressure, or managed release. Those existing contracts are preconditions,
not state machines copied into this model.

Each reference can post three events in batches of one or two entries and then
one terminal. Interleaved operations and posting while a batch is being handed
off exercise the ordering seam. Receiving a later channel message waits until
the current batch's entries have been handed off. Event ordinals stand for
opaque payloads, including progress and the feature-owned durable union. No
coalescing is modeled because the Worker handoff does not coalesce.

## Properties and limits

- `DeliveryIsOrderedPrefix`: each reference's handoffs retain its posted order.
- `TerminalFollowsEveryEntry`: terminal receipt cannot overtake prior entries.
- `BatchesAreBounded`: both the wire batches and partial delivery remain within
  the batch count bound.

`Safety.cfg` must exit 0. The three broken policies must exit 12: overtaking an
unfinished batch violates terminal ordering, while dropping or reversing an
entry violates the prefix or terminal property. The reachability configuration
must exit 12 when both references can finish; it is not a correctness failure.
These exact verdicts are part of `eng/tla-expected-exit-codes.txt`.

The production bound is 64 rather than two. This is finite safety evidence,
not an unbounded proof, liveness claim, or evidence that a feature publishes
after cancellation. The operation authority may suppress a valid handoff.
Codec and runtime cases in `inspect-web-worker-protocol` remain the
implementation gates.

## Recorded run

On 2026-09-05, the repository-pinned TLC build `2026.08.11.125311`
(`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`)
produced all five required exact verdicts. The baseline explored 8,552 distinct
states at depth 19. The reachability run reached full three-event delivery and
terminal receipt for both references.

```bash
TLA_TOOLS_JAR=<pinned-jar> \
  bash eng/run-tla-checks.sh docs/design/models/inspect-web-worker-events
```
