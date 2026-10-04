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
the question closes. If it is, a QuerySpace request set must sit near the
fused pass and clearly ahead of N independent terminals to validate the design;
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
  over nested marker types; the QuerySpace column performs the production call and
  counts its method-kind observations so every column answers the same
  question. The production row counts both kinds, so it equals the lane only
  on assets that declare no extension properties: System.Text.Json,
  System.Linq, and System.Net.Http do not; CoreLib declares 50, and its row
  reads 638 where the lane reads 588.
- Fidelity is defined on Roslyn-produced assemblies and safety on any
  assembly. Exact agreement is required on the pinned assets; a hostile image
  must stay bounded and inert, and a wrong-but-contained count there is not a
  mismatch.

## Columns

| Column | Role |
| --- | --- |
| QuerySpace | The three production calls as Library Info makes them at the head: Async Methods through the `MethodClassificationQuery` Count terminal; Extension Methods and Union Types as rows built then counted. A candidate that changes a production path adds QuerySpace (Base) from the base binary; this slice changes none, so only QuerySpace appears |
| LINQ ×3 | Three idiomatic streaming `System.Linq` pipelines |
| NLinq ×3 | Three NLinq `CountFold`s over the fixture's population sources; the oracle |
| NLinq fused | One fold over the TypeDef source answering every lane; each MethodDef row is read once; the ceiling |
| QuerySpace ×3 | Three independent QuerySpace Count terminals, measured as QuerySpace at the slice 2 head with the rows-then-count path as QuerySpace (Base); after Extension Methods and Union Types gain Count sources |
| QuerySpace request set | One request set over one physical traversal; slice 3, after #9137 |

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

Exact agreement first: 15 answers compared across QuerySpace, LINQ ×3,
NLinq ×3, and NLinq fused on the five assets, 0 mismatches. The QuerySpace
column equals the
production `library -S "Library Info"` rows on System.Text.Json, System.Linq,
and System.Net.Http; on CoreLib the production Extension Methods row is 638
because it also counts 50 extension properties outside the lane.

Local osx-arm64 NativeAOT at head `9fc4fce87` (six rotated rounds, 2 s budget
per cell; a development signal, not the accepted Linux measurement):

| Asset | QuerySpace | LINQ ×3 | NLinq ×3 | NLinq fused |
| --- | ---: | ---: | ---: | ---: |
| System.Text.Json | 502.3 µs | 364.3 µs | 282.1 µs | 262.1 µs |
| System.Private.CoreLib | 9,249.5 µs | 6,252.4 µs | 5,582.2 µs | 5,564.9 µs |
| System.Linq | 2,321.0 µs | 366.5 µs | 337.0 µs | 330.9 µs |
| System.Net.Http | 143.6 µs | 186.8 µs | 123.8 µs | 107.6 µs |
| OptInNet11 fixture | 19.9 µs | 10.5 µs | 8.5 µs | 8.4 µs |

Geometric-mean ratios to NLinq ×3: QuerySpace 2.23× (1.16–6.89), LINQ ×3
1.24×, NLinq fused 0.95× (0.87–1.00). Allocation per answer is byte-equal
for NLinq ×3 and NLinq fused on three assets and within 0.3 percent on
System.Text.Json and CoreLib; QuerySpace allocates 1.3× to 3.9× more.

Accepted linux-x64 NativeAOT on `dotnet-inspect-perf-3` (Ubuntu 24.04,
4 vCPU) under one `perf-guard` lease, same tool source as head `9164a61e`,
six rotated rounds, 2 s budget per cell, assets from the Linux runtime
`11.0.0-rc.1.26425.128` (CoreLib SHA-256 `9573ebab…`); 15 answers compared,
0 mismatches:

| Asset | QuerySpace | LINQ ×3 | NLinq ×3 | NLinq fused |
| --- | ---: | ---: | ---: | ---: |
| System.Text.Json | 873.5 µs | 655.5 µs | 474.7 µs | 442.0 µs |
| System.Private.CoreLib | 13,934.8 µs | 8,911.3 µs | 7,262.6 µs | 7,008.8 µs |
| System.Linq | 2,495.8 µs | 680.7 µs | 619.8 µs | 622.7 µs |
| System.Net.Http | 211.1 µs | 290.8 µs | 176.8 µs | 153.8 µs |
| OptInNet11 fixture | 33.5 µs | 17.7 µs | 14.2 µs | 14.5 µs |

Geometric-mean ratios to NLinq ×3: QuerySpace 2.09× (1.19–4.03), LINQ ×3
1.31× (1.10–1.64), NLinq fused 0.96× (0.87–1.02). Allocation per answer is
byte-equal for NLinq ×3 and NLinq fused on three assets and within 0.3
percent on System.Text.Json and CoreLib; QuerySpace allocates 1.3× to 3.9×
more.
Both hosts agree on direction and magnitude.

Two readings follow:

- **Fusion is not the win.** Reading each TypeDef and MethodDef once instead
  of up to twice saves between nothing and 13 percent, 4 to 5 percent on the
  geometric mean, and loses within noise on `System.Linq` and the fixture.
  The lanes' cost is their per-row predicate work, chiefly the Async lane's
  attribute walk and the Extension lane's signature decode, not the traversal.
  Under the decision rule this is not "clearly ahead", so a request set over
  these lanes cannot earn more than this ceiling; its value here is one shared
  plan and settlement, not shared reads. Slice 3 is not warranted on this
  evidence.
- **Rows-then-count is the loss.** QuerySpace's gap to NLinq ×3 is the Extension
  Methods and Union Types rows being materialized with signatures and anchors
  and then counted: 4.0× and 4.4 MB on `System.Linq`, 1.9× and 12.8 MB on
  CoreLib. The CoreLib QuerySpace cell also pays for the 50 extension properties the
  lane excludes, so its ratio slightly overstates the rows-then-count cost of
  the method half alone. Adopting those rows as Count terminals (slice 2)
  captures that win without request sets.
