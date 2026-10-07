# Method-source terminal work budget Lean proof

This Lean 4 prototype is proof evidence for the terminal-work bounds in
[#9364](https://github.com/richlander/dotnet-inspect/pull/9364), part of the
Lean pilot in
[#9482](https://github.com/richlander/dotnet-inspect/issues/9482). The
normative owner remains
[`method-query-source.md`](../../docs/design/method-query-source.md), which
says source requests own explicit finite terminal-work bounds,
execution-local accounting, and exact receipts. This prototype changes no
product contract or runtime path.

## Why Lean here

PR #9364 adds a `MethodDefinitionTerminalWorkBudget` to every
`MethodDefinitionExecution`. A body's first acquisition makes three membership
checks against the budget's admitted-method set, then one insertion and one
`GetILReader().Length` read. A repeat acquisition makes two checks. No
production caller at #9364's head passes finite limits, so every production
execution is `Unbounded`, and it runs in one of two modes.

Untracked, through `MethodDefinitionExecution.Execute(description,
sourceName, peReader)`:

- `MethodClassificationQuery`'s direct executions
- `AnalysisLibraryBodyUseService`
- `UnsafeEvidencePresence`

Tracked, through `MethodQuerySource.Execute`:

- `MethodClassificationQuery.Execute(prepared)` request sets, including
  shared multi-lane executions
- `UnsafeEvidencePresenceQuery` through `AssemblyAnalysisOperation`

The question is which parts of that accounting can change an observable
result. Each classification below is proved over every acquisition sequence
and every MethodDef table, not over sampled images.

## Proven claims

| Budget work | Classification | Theorem |
| --- | --- | --- |
| Capacity and byte checks under `Unbounded` | Redundant: no sequence over one ECMA-335 MethodDef table reaches `int.MaxValue` bodies or `long.MaxValue` bytes | `unbounded_never_reaches_limit`, from `run_ok_of_universe_fits` |
| The whole budget in an untracked `Unbounded` execution | Redundant: it can neither fail nor publish | `untracked_unbounded_budget_unobservable` |
| The admitted-method set in a tracked lane | Derivable: it equals the lane's `BodiesAcquired` coverage on success | `tracked_admitted_eq_acquired` |
| The same set at exhaustion | Derivable: body exhaustion leaves it equal to `BodiesAcquired`; encoded-IL exhaustion leaves it short by exactly the one acquired-but-unadmitted body | `getBodyTracked_failure` |
| `Admit`'s internal `RequireBodyCapacity` | Redundant: `GetBody` has already made the same check against the same state | `admit_eq_admitUnchecked_of_require`, `getBody_eq_unchecked` |
| `GetBody`'s pre-acquisition `RequireBodyCapacity` | Required: it is the only check that rejects an over-limit body before acquiring it | none needed |

## Measured effect

These results allow the budget to be simplified without changing what it
publishes:

- Omit the budget for untracked `Unbounded` executions.
- Drop the limit checks for tracked `Unbounded` lanes, which can never fail.
  Those lanes still publish `TerminalWork.EncodedIlBytes`, which cannot be
  derived from `BodiesAcquired`, so they must keep a byte counter.
- Take a tracked lane's admitted set from `BodiesAcquired`.
- Drop `Admit`'s repeated check.

That is a code reduction, not a measured speedup. #9364's own result for
`type System.Text.StringBuilder --library <CoreLib> --discover @Audit --tsv`
is a +6 to +9 ms median on Linux x64. The NativeAOT comparisons below ran on
osx-arm64 against RC1 CoreLib, with interleaved paired samples and identical
output. Each cell is the paired median against base `1a3cc7ab0` in one run.

| Variant | Run 1 (40) | Run 2 (50) | Run 3 (50) | Run 4 (50) |
| --- | ---: | ---: | ---: | ---: |
| A/A control: base plus one unused method | — | -1.0 ms | +2.0 ms | -1.0 ms |
| #9364 head `11890aaf1` | +4.9 ms | +4.1 ms | +4.7 ms | +0.7 ms |
| No budget in untracked `Unbounded` executions | +4.5 ms | +4.7 ms | +3.6 ms | -6.4 ms |
| No budget in any `Unbounded` execution, which is every execution at this head | — | — | — | -0.0 ms |

- **#9364's cost is small and depends on the host.** On a quiet host it was
  +4 to +5 ms, about 0.5% of a roughly 790 ms command. It did not reproduce in
  run 4, when the host was busier and medians were near 890 ms.
- **The budget is not a resolvable part of that cost.** Removing it from
  every `Unbounded` execution leaves the result inside the A/A spread.

## Model correspondence

| Lean | C# at `11890aaf1` |
| --- | --- |
| `Limits` | `MethodDefinitionTerminalWorkLimits` |
| `unbounded` | `MethodDefinitionTerminalWorkLimits.Unbounded` |
| `Budget.admitted` | `_admittedMethods` (`MethodDefinitionHandleCoverageBuilder`) |
| `Budget.bytes` | `_encodedIlBytes` |
| `require` | `RequireBodyCapacity` |
| `admit` | `Admit`; the byte test `il > max - bytes` is `bytes + il > max` under the retained `bytes ≤ max`, which every successful admission preserves (assumed, not a proved lemma) |
| `getBody` | the budget calls in `MethodDefinitionUnit.GetBody`, for both the cached and newly read body |
| `Lane.acquired`, `record` | `_requestSourceCoverage.RecordBodyAcquired` into `_bodiesAcquired` |
| `published` | `PublishSourceCoverage` through `RecordTerminalWork`, which a disabled builder ignores |

`unbounded_never_reaches_limit` assumes two ECMA-335 facts:

- A MethodDef table has fewer than `2^24` rows, because metadata tokens carry
  a 24-bit row number.
- `GetILReader().Length` is a nonnegative `int`.

The bound therefore holds even when several MethodDefs share one body RVA.
It has large slack: `(2^24 - 1) * (2^31 - 1)` is about `2^55`, far below
`long.MaxValue`.

The model does not cover:

- `MethodDefinitionHandleCoverageBuilder`'s range and set representation;
  it models the set the builder implements.
- Exception routing after a limit is reached.
- Generated-expansion probe accounting, which has its own budget.

## Build

```sh
lake build
```

The build uses Lean `v4.34.1` with no external packages, and must finish with
no `sorry` and no warnings.
