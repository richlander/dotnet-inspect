# Decompiler pass execution receipts

Issue: [#9445](https://github.com/richlander/dotnet-inspect/issues/9445)

## Owned claim

For an opt-in observed run, `IrPasses` issues one ordered receipt after each
completed pass. The receipt identifies the one-based pass ordinal, the pass
name, and whether that pass changed the observed IR projection.

The default receipt API observes `IrPrinter.Dump`, the same projection used by
the staged pipeline. It retains only the preceding and current projections.
Ordinary `IrPasses.Run` does not create projections or receipts.

This document owns only execution receipts and their change-attribution
semantics. [Decompiler pass
composition](decompiler-pass-composition.md) owns which pass lists are valid
and their correctness-bearing relationships.

## Why this slice exists

Several diagnostics need to know which passes changed a method but do not need
the stage text:

- the DecompilerHarness `--pass-impact` histogram and method listing;
- opt-in corpus feature coverage; and
- second-run attribution in the idempotence sensor.

Those consumers currently call `RunWithStages`, retain the import projection
and every post-pass projection, and then reduce adjacent strings to pass names.
For a large method, retained diagnostic state scales with the full projection
size multiplied by the pass count even though the consumer keeps only a small
set of names.

The manager already owns pass order and the exact before/after boundary.
Issuing the attribution there preserves occurrence identity and lets
name-only consumers discard each prior projection as soon as the next
boundary is compared.

## Receipt contract

`PassExecutionReceipt` contains:

- `Ordinal`: the one-based position in the executed pass list;
- `PassName`: the registered pass name; and
- `Changed`: ordinal string inequality between the observed projection before
  and after that pass.

Repeated pass types and names produce separate receipts. Aggregating
occurrences by pass name is a consumer choice.

A receipt is issued only after the pass and the same post-pass invariant check
used by staged observation complete. If either throws, the run fails visibly
and no success-shaped receipt is issued for that occurrence.

The receipt says only that the observed projection changed. It does not claim
that the pass improved fidelity, changed runtime semantics, mutated a
particular node, or performed useful work.

## Adoption

Name-only consumers use receipts and aggregate `Changed` pass names. A
consumer that needs stage text continues to use `RunWithStages`:

- `--pass-impact` uses receipts unless `--show-diff` requests textual hunks;
- corpus feature coverage uses receipts;
- the idempotence sensor uses receipts for its second run; and
- stage dumps, staged diffs, and per-pass textual diffs remain unchanged.

This split keeps the diagnostic result stable while avoiding retained stage
lists where no stage is rendered.

## Boundaries

This slice does not add:

- pass timing or performance conclusions;
- a structural hash or mutation counter;
- a requirement that passes record `Stepper` events;
- a generic observer or plugin framework;
- scheduling, automatic reordering, or dynamic pass registration; or
- shared analyses, caching, preservation, or invalidation.

Timing is a separate evidence question. Analysis reuse requires an owner for
freshness and invalidation before it can be introduced.

## Compatibility

The receipt path uses the existing pass runner, IR projection, collections,
and ordinal string comparison. It adds no inspected-assembly loading,
reflection, Roslyn dependency, threading requirement, or platform-specific
API. The path remains SRM-only, NativeAOT-friendly, and compatible with a
single-threaded Browser/Wasm host.

## Gates

Release tests establish:

- one receipt per completed pass in pipeline order;
- final-IR parity with `Run` and `RunWithStages`;
- `Changed` parity with adjacent staged projections;
- separate ordinals for repeated pass names, including independently changed
  and unchanged occurrences; and
- unchanged stage-dump and pass-diff behavior.

The Decompiler fast suite covers the adopted harness consumers and ordinary
pipeline behavior.
