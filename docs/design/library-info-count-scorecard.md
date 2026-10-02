# Library Info multi-question Count scorecard

## Status

Experiment record for [#9153](https://github.com/richlander/dotnet-inspect/issues/9153).
It asks one question and records the evidence that answers it. It is not a
design owner: the evidence contract is
[Performance oracles for QuerySpace enablement](../evidence-and-validation.md#performance-oracles-for-queryspace-enablement),
the Library Info rows belong to
[Library Info composition](library-info-composition.md), and request sets
belong to [QuerySpace composition](query-space-composition.md).

## Question

Does one shared metadata pass answering several Library Info Count questions
beat N independent passes by enough to justify QuerySpace request sets? The
planner scorecard framing is a decision rule, not a hope: if a hand-fused pass
is not clearly ahead of N independent NLinq folds, request sets cannot win and
the question closes. If it is, a Planner request set must sit near the fused
pass and clearly ahead of N independent terminals to validate the design;
matching N means reconsider.

## Lanes

Three Library Info rows are Count questions over the same TypeDef, MethodDef,
and CustomAttribute tables. Each lane applies the same gate and predicates as
the product path for that row, through product-owned predicates, so every
column answers the same question and only the read machinery differs.

| Lane | Product path today | Predicate |
| --- | --- | --- |
| Async Methods | `MethodClassificationQuery` Count terminal | `MethodClassificationScope` gate, then runtime-async flag or `AsyncStateMachine`/`AsyncIteratorStateMachine` attribute |
| Extension Methods | `ExtensionMethodsQuery`, rows built then counted | static type with `[Extension]`, not hidden; public static method with `[Extension]`, not hidden, signature decodes with one or more parameters |
| Union Types | `UnionTypesQuery`, rows built then counted | TypeDef with `[Union]` |

Boundaries, decided with the operator:

- Easy-count forms only. No lane decodes IL. Switches is excluded because half
  of its count walks method bodies.
- Free table counts (Types, Methods, Custom Attributes, Resources, Type
  Forwarders) are excluded; they would pad the lane count without testing
  anything.
- The Extension Methods lane is the method half of the row. Extension
  properties from C# 14 extension blocks select through a private product path
  over nested marker types; the Old column counts method-kind observations so
  every column answers the same question.
- Fidelity is defined on Roslyn-produced assemblies and safety on any
  assembly. Exact agreement is required on the pinned assets; a hostile image
  must stay bounded and inert, and a wrong-but-contained count there is not a
  mismatch.

## Columns

| Column | Role |
| --- | --- |
| Old | The three production calls as Library Info makes them today |
| LINQ ×3 | Three idiomatic streaming `System.Linq` pipelines |
| NLinq ×3 | Three NLinq `CountFold`s over the fixture's population sources; the oracle |
| NLinq fused | One fold over the TypeDef source answering every lane; each MethodDef row is read once; the ceiling |
| Planner ×3 | Three independent QuerySpace Count terminals; slice 2, after Extension Methods and Union Types gain Count sources |
| Planner request set | One request set over one physical traversal; slice 3, after #9137 |

The harness is `tools/LibraryInfoCountScorecard` over the shared
`DotnetInspector.PerformanceOracles` scorecard: rotated rounds, the median of
round medians, exact-agreement check before any timing, and allocated bytes per
answer beside time. The session and image are opened once outside every timed
cell, so no column pays acquisition per terminal.

## Assets

Pinned real assets from the installed runtime `11.0.0-rc.1.26425.128`:
`System.Text.Json`, `System.Private.CoreLib`, `System.Linq`, and
`System.Net.Http`. Union Types is zero on every real asset today, so the
`ILInspector.Decompiler.Fixtures.OptInNet11` fixture supplies a nonzero union
case for correctness, not for timing claims.

## Results

Recorded per candidate head in the pull request and summarized here once the
Linux NativeAOT measurement is accepted.
