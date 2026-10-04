# Method-body demand scorecard

## Status

Stack slice 2 above
[#9165](https://github.com/richlander/dotnet-inspect/pull/9165), for
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577). It evaluates
the demand-driven shallow retained-prefix model owned by
[Instruction substrate](instruction-substrate.md#layer-0--layer-1). Production
QuerySpace collapse and Method-source routing remain outside this slice.

The follow-up stack slice adds a resource-free reference planner: real analyzer
declarations state their minimum instruction Access and Detail, the planner
joins those facets, and the scorecard executes the selected no-retention or
retained source. The cross-domain capability specification remains a
post-merge follow-up.

Accepted NativeAOT performance evidence is **unverified**.

## Question

For every admitted managed Method body, determine whether it contains any
explicit `throw` or `rethrow` opcode, then count the matching methods. Compare:

1. one non-materializing stream stopping at the first throw;
2. one cursor over a lazy retained shallow prefix, also stopping at the first
   throw; and
3. one eager full decode followed by an early-exit array scan.

The second lane is the proposed retained-prefix model. It retains only offset,
opcode, and encoded extent; operand values and branch targets are resolved
separately on demand. The body suffix remains unscanned once throw presence
settles, while the shallow prefix stays available for indexed readers or
another cursor. The first lane is the no-retention performance floor; the
third shows the cost of eagerly completing the full decoded representation.

The planner-selected first lane is driven by the throw-presence analyzer's
`ForwardOnly + OpcodeAndExtent` declaration. A separate real multi-analyzer
query combines throw presence, calls, allocations, stable-getter recognition,
and bounded allocation-flow probes:

| Analyzer | Access | Detail |
| --- | --- | --- |
| Throw presence | ForwardOnly | OpcodeAndExtent |
| Direct calls | ForwardOnly | OpcodeAndExtent |
| Allocations | ForwardOnly | OpcodeAndExtent |
| Stable getter | ForwardOnly | SelectiveOperands |
| Bounded flow | RetainedPrefix | SelectiveOperands |

The joined multi-analyzer demand is
`RetainedPrefix + SelectiveOperands`, so its planned lane uses one lazy shallow
shared sequence. Removing stable getter and bounded flow leaves three shallow
forward analyzers and selects one no-retention stream. The declaration names
semantic need rather than `InstructionDecoder` or `InstructionSequence`;
physical source selection remains the planner's decision.

## Boundaries

`PreparedMethodBodies` performs image loading, MethodDef admission, and one
immutable IL snapshot per admitted body before timed terminals. Every lane
reads the same prepared snapshot, so no lane pays a body-byte copy during
measurement. This scorecard therefore measures shallow prefix retention above
the non-materializing `InstructionDecoder.Visit` floor.

The standalone bounded-flow scenario prepares its known probe offsets only
when that scenario is requested, during exact checking before its timed lanes.
Loading an asset or running a throw-only or forward-shallow plan performs no
flow-probe instruction scan. The mixed classifier query discovers its
allocation candidates inside every measured lane because candidate discovery
is part of that query.

The scorecard does not claim that every random-access analyzer can settle
early. It establishes the sequence mechanics for analyzers that request
instructions incrementally. Full-body Count, complete control-flow, and
iterative dataflow consumers still force the frontier to EOF.

## Harness

```console
method-body-demand-scorecard check <assembly>...
method-body-demand-scorecard time \
  --rounds 6 --budget-ms 2000 --tsv results.tsv <assembly>...
```

`check` compares each body's throw-presence result and the complete aggregate
summary from all three lanes before timing. `time` rotates implementations
through repeated NativeAOT measurement.

## Decision use

- If the lazy shallow cursor materially beats eager instruction decode on
  real assets, retained-prefix demand avoids enough suffix work to justify its
  state and replay model.
- Its ratio to the no-retention stream sizes the cost of shallow prefix retention
  for a consumer that also needs indexed access.
- Production adoption still requires QuerySpace to group compatible requests
  and the Method source to preserve each request's independent settlement,
  failure, and participation evidence.

## Local development signal

The planner-enabled `linux-x64` NativeAOT development run used the .NET 11 RC1
runtime assemblies, six rounds, five warmups, and a two-second cell budget.
All lanes agreed for every admitted body, including the five-analyzer result
over 51,626 bodies: zero aggregate mismatches and zero per-body mismatches.

| Single-classifier lane | Ratio to planner-selected stream | Asset range |
| --- | ---: | ---: |
| Planner-selected stream | 1.00x | 1.00-1.00x |
| Lazy shallow cursor | 2.34x | 2.28-2.39x |
| Eager decoded instructions | 3.61x | 2.68-4.25x |

| Five-classifier lane | Ratio to eager separate passes | Asset range |
| --- | ---: | ---: |
| Eager decoded separate passes | 1.00x | 1.00-1.00x |
| Eager decoded fused pass | 0.93x | 0.78-1.07x |
| Planner-selected lazy shared | 0.67x | 0.55-0.86x |

The lazy shallow single-classifier lane costs about 65% of eager full decode
while preserving cursor replay, indexed prefix access, and on-demand detail
resolution. The mixed query's declared needs select that retained source and
run about 33% faster than eager separate passes. A pure shallow forward set
selects the stream instead and pays no retention. These numbers are diagnostic,
not accepted evidence; the controlled-host NativeAOT gate remains unverified.
