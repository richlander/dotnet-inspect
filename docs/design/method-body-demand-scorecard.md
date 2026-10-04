# Method-body demand scorecard

## Status

Stack slice 2 above
[#9165](https://github.com/richlander/dotnet-inspect/pull/9165), for
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577). It evaluates
the demand-driven shallow retained-prefix model owned by
[Instruction substrate](instruction-substrate.md#layer-0--layer-1). Production
QuerySpace collapse and Method-source routing remain outside this slice.

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

## Boundaries

`PreparedMethodBodies` performs image loading, MethodDef admission, and one
immutable IL snapshot per admitted body before timed terminals. Every lane
reads the same prepared snapshot, so no lane pays a body-byte copy during
measurement. This scorecard therefore measures shallow prefix retention above
the non-materializing `InstructionDecoder.Visit` floor.

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
- Its ratio to the no-copy cursor sizes the cost of shallow prefix retention
  for a consumer that also needs indexed access.
- Production adoption still requires QuerySpace to group compatible requests
  and the Method source to preserve each request's independent settlement,
  failure, and participation evidence.

## Local development signal

The corrected `linux-x64` NativeAOT development run used the .NET 11 RC1
runtime assemblies, six rounds, five warmups, and a two-second cell budget.
All three lanes agreed for every admitted body: eight aggregate comparisons,
zero aggregate mismatches, and zero per-body mismatches.

| Lane | Ratio to no-retention stream | Asset range |
| --- | ---: | ---: |
| No-retention stream | 1.00x | 1.00-1.00x |
| Lazy shallow cursor | 2.26x | 2.03-2.38x |
| Eager decoded instructions | 4.04x | 3.68-4.79x |

The lazy shallow lane therefore costs about 56% of eager full decode in this
development run while preserving cursor replay, indexed prefix access, and
on-demand detail resolution. It remains inappropriate for a pure streaming
consumer that needs none of those capabilities. These numbers are diagnostic,
not accepted evidence; the controlled-host NativeAOT gate remains unverified.
