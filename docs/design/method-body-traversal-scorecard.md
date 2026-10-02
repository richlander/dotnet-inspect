# Method-body traversal scorecard

## Status

Prototype experiment for
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577). It asks
whether several instruction-level analyzers can share one body decode cheaply
enough to justify the Method source's fused traversal. It is not a design
owner. Producer requests remain owned by
[Producer Planning](producer-planning.md), request collapse by
[Query Space Composition](query-space-composition.md), and decoded instruction
identity by the [Instruction substrate](instruction-substrate.md).

The prototype and exact-agreement check are implemented. Accepted NativeAOT
performance evidence and a production Method-source decision remain
**unverified**.

## Question

For three complete instruction folds over the same admitted managed Method
bodies, how do these execution shapes compare?

1. Three independent non-materializing instruction scans.
2. One composable scan that routes each instruction to three typed folds.
3. One hand-fused callback in the same streaming decoder.
4. One instructions-only materialization followed by three array passes.
5. One instructions-only materialization followed by one fused array pass.
6. One full Layer 0 construction followed by three array passes.
7. One full Layer 0 construction followed by one fused array pass.

The three facts are direct invocation count (`call`, `callvirt`, `newobj`),
allocation-opcode count (`newobj`, `newarr`, `box`), and explicit throw count
(`throw`, `rethrow`). Every lane also reports the same managed-body and
instruction counts.

The first question is whether eliminating two decodes and byte-stream walks is
materially valuable. The second is whether a composable typed fold stays near
the hand-fused callback under the same current `InstructionDecoder.Visit`
overhead. The instructions-only lanes isolate the cost of retaining fully
decoded operands and branch targets. The Layer 0 lanes additionally include
exception-flow and block-graph construction. Each pair also shows whether
repeated instruction-array traversal is itself significant.

## Preparation and cost boundary

`PreparedMethodBodies` loads the immutable image, enumerates MethodDefs,
admits managed IL bodies, and constructs every `MethodBodyBlock` before a
timed terminal. This follows the prepared-source boundary demonstrated by
[#9143](https://github.com/richlander/dotnet-inspect/pull/9143): admission,
binding, and retained-source setup are not repeatedly charged as terminal
compute.

The scorecard stays wholly within the body-decode cost class added by
[#9154](https://github.com/richlander/dotnet-inspect/pull/9154). It does not
mix metadata-only questions with an optional body lane, because one body lane
would set the cost of the complete request set.

All lanes consume the shared `InstructionDecoder` and `MethodInstructions`
substrates. The prototype does not add a second IL reader, alter the production
Method source, or define QuerySpace request-set behavior.

## Harness

`tools/MethodBodyTraversalScorecard` uses the shared performance-oracle
scorecard:

```console
method-body-traversal-scorecard check <assembly>...
method-body-traversal-scorecard time \
  --rounds 6 --budget-ms 2000 --tsv results.tsv <assembly>...
```

`check` compares the complete summary from every lane before timing, including
whether full Layer 0 construction was incomplete for any admitted body.
`time` rotates the lanes through repeated NativeAOT measurements and reports
ratios to the three-independent-stream baseline.

The initial real assets are System.Text.Json and System.Private.CoreLib from
the repository development runtime. Additional assets should represent a
small library, a body-dense application library, and a large platform library.

## Decision use

- If the hand-fused callback is not clearly ahead of independent scans, shared
  instruction traversal does not justify request-collapse machinery by
  performance.
- If the composable stream is materially behind the hand-fused callback under
  the same decoder, the consumer-dispatch shape needs revision before
  production adoption.
- If instructions-only materialization wins when random-access consumers join
  streaming folds, the Method source should materialize once and share it.
- If any consumer requests full Layer 0, its exception-flow and block-graph
  construction cost must be evaluated separately from instruction retention;
  the Method source should still construct the deepest requested body value
  once and share it for the unit.
- Any production adoption must preserve each request's independent terminal,
  failure, and participation evidence. This scorecard measures execution cost;
  it does not establish those semantics.
