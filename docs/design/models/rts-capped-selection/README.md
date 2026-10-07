# Rank-first capped RTS selection Lean proof

This Lean 4 model is proof evidence for
[issue #9492](https://github.com/richlander/dotnet-inspect/issues/9492), the
second slice of the Lean pilot in
[#9482](https://github.com/richlander/dotnet-inspect/issues/9482). The
normative owner remains
[`fact-planned-compile-back-harness.md#standalone-method-target-selection`](../../fact-planned-compile-back-harness.md#standalone-method-target-selection).
This model changes no product contract or runtime path.

## Why Lean here

[#9343](https://github.com/richlander/dotnet-inspect/pull/9343) made capped
target selection demand-shaped: rank first, decide in rank order, and stop at
the cap. Its equality with the complete pre-cap plan is checked by one
`ReturnToSenderTargetScorecard select-repeat` fingerprint on the pinned
14-assembly corpus at cap 700. The theorems below hold for every scoped
population, eligibility decision, declaration-candidate decision, and cap.

## Proven claims

| Owner claim | Theorem |
| --- | --- |
| The loop selects the cap-prefix of eligible ranked bodies | `capped_selected` |
| Selection equals the complete pre-cap plan's prefix | `rankFirst_eq_eager` |
| The host's global cap across assemblies preserves that equality | `acrossAssemblies_congr` |
| The receipt counts only the evaluated ranked prefix | `capped_receipt_prefix` |
| The plan evaluates no more bodies than the population | `capped_evaluated_le` |
| The plan stops at the cap-th eligible body | `capped_stops_at_cap` |
| The plan settles the cap or exhausts; an exhausted run selected every eligible body | `capped_settles_or_exhausts` |
| A settled, non-exhausted published receipt, including `RankedBodyCount`, is unchanged by any same-size replacement of its unevaluated bodies, so it does not reveal their eligibility | `published_settled_ignores_unevaluated` |

`capped` mirrors the loop in
`ReturnToSenderTargetSourceSession.SelectCappedTargets`. It evaluates one
ranked body, counts declaration candidates and excluded declaration
candidates, adds eligible bodies, and breaks once the selected count equals
the cap. A cap of zero returns no selection; C# rejects it. `published` adds
the receipt's `RankedBodyCount`, the ranked population size, so receipt
theorems cover every field `ReturnToSenderCappedTargetSelection` publishes.

Selected targets return in metadata order by sorting the selected list by
metadata sequence. Because `rankFirst_eq_eager` proves the two selected lists
are equal, any such reordering yields equal output.

## Correspondence hypotheses

`rankFirst_eq_eager` assumes three things, each checked by reading the code at
this head rather than proven:

- **Rankings are strict.** LINQ `OrderBy(Hash).ThenBy(Key)` is stable, so both
  plans order by `(Hash, Key, metadata position)`. The eager collector numbers
  only eligible bodies while rank-first numbers every scoped body, but both
  numberings preserve metadata order.
- **Rankings agree on eligible bodies.** The eager key appends
  `ReturnToSenderTargetDecision.StableIdentitySuffix`. Rank-first appends
  `generic-arity:N` from `GetStableRankedSampleCandidates`. Both are computed
  from the method's generic parameter count
  (`ReturnToSenderTargetSource.cs` `Evaluate`, `IrImporter.cs`
  `GetStableRankedSampleCandidates`), and both prefix the same
  `StableSampleKey`.
- **Eligibility is one deterministic decision.** Both plans call `Evaluate`.
  Eager admits a body when its decision has a target and no exclusion, and
  that is exactly when `Evaluate` reports `Eligible`.

If a later change gives eligible bodies different keys, for example by deriving
the eager suffix from a richer decision, the theorem no longer applies, and the
plans can select different targets.

## Non-claims

- No theorem establishes correspondence with the C# implementation.
- `acrossAssemblies` models the scorecard host's single global cap. The corpus
  host's per-assembly cap and its visible failure when a cap does not settle
  are host-owned and not modeled; `capped_settles_or_exhausts` gives the
  condition that host checks.
- Work is counted in decisions. Allocation, time, and NativeAOT behavior
  remain #9343's measured evidence.
- No CI job builds this project yet.

## Run

The model pins Lean 4.34.1 and has no package dependencies:

```bash
cd docs/design/models/rts-capped-selection
lake build
```

`lake build` fails on any unproven or ill-typed theorem. The key theorems
depend only on Lean's standard `propext`, `Classical.choice`, and `Quot.sound`
axioms; check with `#print axioms`.
