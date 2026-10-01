# Analysis body-use terminal-kernel lifecycle model

This model pairs with the standalone Lean prototype in
[`prototypes/body-use-terminal-folding`](../../../../prototypes/body-use-terminal-folding/).
It evaluates whether TLA+ adds distinct evidence at the boundary between a
selected fused terminal kernel and its runtime host.

The normative product owner remains
[`analysis-library-body-use.md`](../../analysis-library-body-use.md). This is
prototype evidence for
[issue #9025](https://github.com/richlander/dotnet-inspect/issues/9025), not
an accepted product contract.

## Paired boundary

Lean owns the fixed-kernel question: for every modeled operand list, fused
`Exists`, `Count`, and `Rows` observations equal discrete execution, and the
fused traversal performs no more physical operand visits.

TLA+ does not repeat that proof. It treats the Lean observations as the kernel
contract and checks the neighboring lifecycle question: can a runtime select
that kernel once, advance every active terminal atomically for each physical
operand, publish independently settled results, preserve them through later
failure, and eventually publish every terminal?

The model uses the same two pathological source shapes as the prototype:

- failure after `Exists` settles, where later `Count` and `Rows` fail; and
- complete exhaustion, where `Count` and `Rows` retain all three admitted
  occurrences.

Publication may interleave with source traversal. In particular, the model
reaches a state where `Exists` is published before the next operand fails,
while `Count` and `Rows` remain active.

## Checked properties

| Boundary property | Model property |
| --- | --- |
| No operand work starts before the fused kernel is selected | `SelectionPrecedesWork` |
| One physical source position is advanced per operand action | `PhysicalTraversalFollowsCursor` |
| Every active terminal observes the complete visited prefix | `ActiveTerminalsFollowPhysicalTraversal` |
| Settled terminals stop receiving operand work | `SettledTerminalsStopAtSettlement` |
| Later failure cannot invalidate a settled terminal | `SettlementIsStable` |
| Count and Rows receive the same atomic admission | `CountAndRowsShareAdmission` |
| An early publication cannot drift from its terminal | `PublishedObservationsRemainCurrent` |
| Failure and exhaustion produce the Lean kernel observations | `FailureScenarioMatchesLeanKernel`, `CompleteScenarioMatchesLeanKernel` |
| Fair execution publishes every terminal and completes | `EventuallyDone` |

## Configurations

| Configuration | Purpose | Expected result |
| --- | --- | --- |
| `SafetyFailure.cfg` | Early `Exists` settlement followed by source failure | Exit `0` |
| `SafetyComplete.cfg` | Complete source exhaustion | Exit `0` |
| `BrokenInvalidateSettled.cfg` | Later failure incorrectly fails settled `Exists` | Exit `12` |
| `BrokenSplitAdmission.cfg` | Physical traversal advances after updating only one terminal | Exit `12` |
| `ReachabilityEarlyPublication.cfg` | Demonstrates publication before the later failure | Exit `12` |

The two mutations are intentionally host-lifecycle mistakes rather than
alternative fold algorithms. Their counterexamples are the added value of the
pairing: the Lean proof establishes the kernel result, while these checks
exercise whether temporal orchestration preserves that result.

The recorded validation used TLA+ v1.8.0 build `2026.08.11.125311`
(`0894c34`):

| Configuration | Generated states | Distinct states | Depth | Exit |
| --- | ---: | ---: | ---: | ---: |
| `SafetyFailure.cfg` | 20 | 14 | 9 | 0 |
| `SafetyComplete.cfg` | 26 | 18 | 11 | 0 |
| `BrokenInvalidateSettled.cfg` | 5 | 5 | 5 | 12 |
| `BrokenSplitAdmission.cfg` | 4 | 4 | 4 | 12 |
| `ReachabilityEarlyPublication.cfg` | 19 | 14 | 9 | 12 |

## Abstractions

The model does not import a Lean proof certificate; Lean and TLA+ have no
machine-checked composition here. Their correspondence is the deliberately
small shared vocabulary of operand admission, terminal status, completion,
failure, values, and rows.

The model abstracts:

- body decoding and operand binding inside each admitted operand;
- cancellation, request replacement, and multiple concurrent plans;
- diagnostic contents beyond one visible decode failure;
- QuerySpace receipts and public result association;
- C# implementation correspondence and compiler behavior; and
- all terminal kinds other than `Exists`, `Count`, and `Rows`.

Those omissions keep the experiment focused on whether combining theorem and
lifecycle evidence is useful before any broader product model is proposed.

## Running TLC

Use the repository-pinned tools:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  eng/run-tla-checks.sh \
  docs/design/models/analysis-body-use-terminal-kernel-lifecycle
```
