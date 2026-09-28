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

A hostile image that exhausts the pointer analyzer's work budget fails the
pointer analyzer. P/Invoke Methods and Async Methods still answer. Legacy
failed all three together.

## Owner and exact claim

**Method classification analyzers** owns this exact claim:

> The P/Invoke, async, and pointer-signature analyzers each publish, for one
> method-definition image, the rows `MethodClassificationScanner.Scan` publishes
> for its classification on the same image, with the same method name,
> declaring type, namespace, signature text, classification, module name,
> anchor, and return type. Rows from two or more analyzers, merged by metadata
> order and then by the legacy order P/Invoke, async, pointer, equal the
> legacy list. The only exception is budget containment: each analyzer carries
> its own work budget and identity-decode-failure budget, so a budget failure
> fails only the analyzer that exhausted it.

This owner defines:

- the scope: public, non-accessor methods on types whose name does not start
  with `<`, as legacy defines it;
- each analyzer's classification test and row projection;
- the shared scope classifier and the classes each analyzer accepts;
- per-analyzer budget containment; and
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
questions. Here each analyzer owns one of each. Each analyzer charges its
budgets where it decodes: the pointer probe for the pointer analyzer, and
identity and signature projection for every analyzer. Exhausting a budget
fails that analyzer with the legacy exception, as a contained producer
failure. It does not fail its siblings.

On ordinary inputs no budget is exhausted, and output equals legacy. On a
hostile input that exhausts a budget, one analyzer can fail while another
answers. That is the only difference from legacy that anyone can observe, and
it is intended: a consumer that asked only for async methods is no longer
denied them because of a pointer-signature attack.

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
order, so its observations are unchanged. When one analyzer fails, the Finding
reports that failure. Sections that asked only for another analyzer still
render.

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
- **Budget containment.** The existing hostile classification fixtures run
  against each analyzer and keep their allocation bounds. One test shows the
  pointer analyzer failing on budget exhaustion while the async and P/Invoke
  analyzers answer.
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
