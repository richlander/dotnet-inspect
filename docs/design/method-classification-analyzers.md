# Method classification analyzers

## Status

Focused design for item 6 of
[#8733](https://github.com/richlander/dotnet-inspect/issues/8733). It replaces
`MethodClassificationScanner.Scan` with three
[Producer Planning](producer-planning.md) producers. It is the production
caller of the scope guards and type scope that landed in #8735. Not
implemented; every property below is **unverified** until its gate lands.

## Examples

`dotnet-inspect library System.Text.Json --section "Async Methods"` shows the
library's public async methods. The request names one producer, the async
analyzer. It checks its own scope and visits every public, non-accessor method
on a type whose name does not start with `<`. Nothing tests for P/Invoke
imports or decodes pointer signatures.

`dotnet-inspect library System.Text.Json`, with LibraryInfo, Signals, and the
classified-method Finding, needs all three questions. The request names all
three analyzers. Because all three declare the same shared scope classifier,
the planner runs that classifier once, and each analyzer is guarded by the
classes it accepts. The scope and the P/Invoke test run once per method, not
three times.

A hostile image that exhausts the pointer analyzer's work budget aborts the
execution. No analyzer publishes a result, and every consumer that asked for
one sees the same typed critical failure, just as legacy failed all three
questions together.

## Owner and exact claim

**Method classification analyzers** owns this exact claim:

> The P/Invoke, async, and pointer-signature analyzers each publish, for one
> method-definition image, the rows `MethodClassificationScanner.Scan` publishes
> for its classification on the same image, with the same method name,
> declaring type, namespace, signature text, classification, module name,
> anchor, and return type. Rows from two or more analyzers, merged by metadata
> order and then by the legacy order P/Invoke, async, pointer, equal the
> legacy list. Each analyzer charges its own work budget and identity-decode
> failure budget, sized as legacy's per-scan budgets. Exhausting any of them
> aborts the whole execution with a critical failure, so hostile inputs fail
> every requested question together, as legacy does.

This owner defines:

- the scope: public, non-accessor methods on types whose name does not start
  with `<`, as legacy defines it;
- each analyzer's classification test and row projection;
- the shared scope classifier and the classes each analyzer accepts;
- each analyzer's budgets and where it charges them; and
- the merge order that reproduces the legacy list.

This owner does not define:

- producer declarations, planning, scope guards, type scope, or the rule for
  choosing between an analyzer's own scope check and a shared classifier.
  [Producer Planning](producer-planning.md) owns those, the last in
  [Shared scope classifiers](producer-planning.md#shared-scope-classifiers).
- async-method recognition, pointer detection, anchors, or signature text.
  Metadata owns those, and this design reuses them unchanged.
- sections, Findings, Signals, or presentation, which the CLI owns.

## The analyzers

| Analyzer | Accepts, from the classifier | Row when |
| --- | --- | --- |
| P/Invoke | `PInvoke` | `MethodAttributes.PinvokeImpl` is set |
| Async | `Other` | `ClassifyAsyncMethod` returns runtime or state-machine async |
| Pointer signature | `Other` | the budgeted pointer probe finds a pointer in the return or a parameter type |

The shared scope classifier applies the legacy type scope, excluding types
whose name starts with `<`. It then classifies each in-scope method as
`PInvoke` or `Other`. Methods outside the method scope (not public, or an
accessor) belong to neither class, so no analyzer attempts them.

Async and pointer signature are independent. A method that is both async and
has a pointer signature produces one async row and one pointer row, as legacy
does. A P/Invoke method produces only its P/Invoke row, because legacy
classifies P/Invoke first and stops.

Asked alone, an analyzer tests the same scope itself, through its own type
scope and a method-scope check. That result must equal the guarded one; the
equivalence gate below checks it.

Each analyzer projects its rows with the legacy Metadata functions: declaring
type name formatting, method identity and anchor, and signature text with its
fallback. Metadata exposes these as a projection API with no Planning types,
and `MethodClassificationScanner.Scan` calls the same API until it retires.

## Budgets

Legacy shares one `MaxClassificationScanWorkChars` work budget and one
`MaxClassificationIdentityDecodeFailures` failure budget across all three
questions. Here each analyzer owns one of each, sized the same as legacy's
per-scan budgets. Each analyzer charges its budgets where it decodes: the
pointer probe for the pointer analyzer, and identity and signature
projection for every analyzer.

Exhausting any analyzer's budget is a critical failure under
[Budget exhaustion aborts the execution](producer-planning.md#budget-exhaustion-aborts-the-execution).
The execution stops, and no analyzer publishes a result. Every requested
analyzer's outcome is aborted, and its `CriticalFailure` names the analyzer,
the budget, and the method being visited. Hostile-input behaviour therefore
matches legacy: every question asked fails together, with one typed error.

An analyzer asked alone charges only its own budgets, so it can succeed on
an input where legacy's shared scan would have exhausted a budget on work the
consumer did not ask for. That is demand, not containment: the work that
would have exhausted the budget is never started. Once any requested
analyzer exhausts its budget, nothing is published.

Recoverable per-method failures are unchanged. Legacy skips a method whose
signature cannot be decoded, and counts it against the decode-failure budget.
The analyzers do the same.

## Demand

Each consumer asks only for what it shows:

| Consumer | Asks for |
| --- | --- |
| Async Methods section | async rows |
| P/Invoke Methods section | P/Invoke rows |
| Pointer-signature method list (`UnsafeMethods`) | pointer rows |
| Signals | P/Invoke, async, and pointer, fused |
| Classified-method Finding and LibraryInfo counts | P/Invoke, async, and pointer, fused |

The CLI splits the combined `ClassifiedMethodsQuery` result into one result
per analyzer. The Finding merges the three analyzers' rows in the legacy
order, so its observations are unchanged. On a critical failure, the Finding
reports the one typed critical failure, as every other requesting consumer
does.

## Layering

The analyzers live in `ILInspector.Analysis`, beside Producer Planning and the
unsafe-presence producer. They use Metadata's classification and projection
functions through a public API that names no Planning type, so Metadata does
not reference Planning. This is the simplest option. The first adopter already
put Planning in Analysis, `DotnetInspector.Queries` already references
Analysis, and no Metadata type moves.

## Production adoption

1. **CLI.** Async Methods first, as the demo. Then P/Invoke Methods, the
   pointer-signature list, Signals, the Finding, and the LibraryInfo counts.
   Each migrated consumer drops its read of the combined result.
2. **Browser/Wasm.** The browser has no consumer today, and
   `MethodClassificationScanner` is on its deny list. The analyzers are
   host-neutral. A future browser consumer uses them through the per-analyzer
   queries rather than the scanner. There is no browser adoption step until
   such a consumer is designed.
3. **Retirement.** When no consumer reads the combined result,
   `ClassifiedMethodsQuery`, `AssemblyInspectionSession.ClassifiedMethods`, and
   `MethodClassificationScanner.Scan` are removed. `ClassifyAsyncMethod` and
   the projection API stay, because the decompiler, C# shells, and PDB context
   use async classification.

## Verification

- **Equivalence on real assets.** For each analyzer, and for the fused merge,
  rows equal `MethodClassificationScanner.Scan` on the repository's pinned
  test packages, compared field by field, including anchors and return types.
  Boundary fixtures:
  - an empty type;
  - compiler-generated types;
  - an unreadable signature;
  - accessors and non-public methods;
  - a P/Invoke method;
  - a method that is both async and has a pointer signature (two rows);
  - runtime and state-machine async.
- **Asked alone equals guarded.** Each analyzer's rows are the same with its
  own scope check as with the shared classifier.
- **Budget exhaustion aborts.** The existing hostile classification fixtures
  run against each analyzer, keep their allocation bounds, and end in
  `Aborted` with a `CriticalFailure` naming that analyzer and budget. When two
  analyzers run together and one exhausts its budget, neither publishes a
  result, both are `Aborted` with the same `CriticalFailure`, and no method
  after the exhausting one is read.
- **End to end.** A NativeAOT base/head comparison of the migrated sections,
  per the [evidence contract](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization),
  on every supported terminal: rows, `--count`, `-n`, and `--rows`.
- **Postcard.** Old, NLinq, and Planner over the async question, against the
  NLinq fixture of
  [#8745](https://github.com/richlander/dotnet-inspect/issues/8745), per the
  [performance oracle guidance](../evidence-and-validation.md#performance-oracles-for-queryspace-enablement).
  The recorded 1.19–1.69× of hand-fused came from an experiment that treated
  async and pointer as exclusive and skipped projection and budgets, so it is
  not a prediction for this design and is re-measured.

## Existing budgeted producer

Unsafe-evidence presence already charges a budget: `UnsafePresenceWorkBudget`
bounds IL bytes and same-image correspondence. Exhausting it throws
`BadImageFormatException`, which the executor treats as a recoverable failure
at that method. For example, "same-image correspondence exceeds the assembly
budget" on Microsoft.CodeAnalysis.CSharp 4.14.0. Presence is the only
producer in its plan, so consumers already see the whole request fail. But
the failure is reported as `Failed` at one method, indistinguishable from an
unreadable body, and it would be contained if presence ever ran beside
another producer. Migrating it to the typed critical signal and the `Aborted`
outcome is follow-up work for Producer Planning, tracked in #8733, and it is
not part of this change.

## Open question for the operator

- **Browser scope.** The browser has no consumer of these sections, and the
  scanner is on its deny list, so this design adopts on the CLI only. The
  browser adopts the analyzers when a browser consumer is designed. Narrowing
  shared substrate to the CLI needs explicit approval.
