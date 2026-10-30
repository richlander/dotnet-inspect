# Decompiler pass analysis lifetime Lean proof

This Lean 4 model is proof evidence for
[#9519](https://github.com/richlander/dotnet-inspect/issues/9519).
The normative owner remains
[`decompiler-pass-analysis-lifetime.md`](../../decompiler-pass-analysis-lifetime.md).
The model changes no product contract or runtime path.

## Why Lean here

QuerySpace's
[producer-capability demand pilot](../producer-capability-demand/) proves
order-independent demand joins and shared read-only traversal over one stable
source. Decompiler passes add a different question: the source mutates between
consumers, so when may one previously constructed result remain current?

The model represents one analysis identity with a conservative generation.
Construction associates a result with the current generation. A preserving
pass keeps both; a non-preserving pass clears the result and advances the
generation.

## Proven claims

| Owner claim | Theorem |
| --- | --- |
| Acquisition always leaves a current result | `acquire_current` |
| A current result is reused | `acquire_reuses_current` |
| A missing result is constructed for the current generation | `acquire_constructs_missing` |
| One preserving pass retains a current result | `complete_preserving_keeps_current` |
| One non-preserving pass clears the result and advances the generation | `complete_invalidating_clears`, `complete_invalidating_advances` |
| Any all-preserving pass sequence retains the result | `preserving_sequence_keeps_current` |
| A consumer after invalidation constructs before use | `invalidation_requires_construction` |
| Checking only the final pass is unsound | `final_preservation_is_insufficient` |

The final theorem is the Decompiler analogue of the conjunctive-path lesson
from QuerySpace issue
[#9483](https://github.com/richlander/dotnet-inspect/issues/9483):
preservation belongs to every intervening edge, not only the latest one.

## Correspondence boundary

The C# implementation must separately gate that:

- analysis state is scoped to one function pipeline execution;
- the manager clears and advances an analysis generation after every completed
  non-preserving pass;
- a pass receives only its declared current result;
- nested imported-function pipelines have independent state; and
- receipts are issued only after pass and invariant completion.

Lean proves the abstract generation law. It does not prove correspondence with
the C# implementation or that a particular rewrite preserves branch-target
offsets.

## Non-claims

- No QuerySpace capability or terminal semantics are modeled.
- No demand join is restated; the QuerySpace pilot owns that proof.
- No theorem establishes elapsed time, allocation, or NativeAOT behavior.
- No CI job builds this project.

## Run

The model pins Lean 4.34.1 and has no package dependencies:

```bash
cd docs/design/models/decompiler-pass-analysis-lifetime
lake build
```

`lake build` fails on any unproven or ill-typed theorem. The `#print axioms`
output records the axioms used by the key theorems.
