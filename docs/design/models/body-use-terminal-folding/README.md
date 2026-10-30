# Body-use terminal folding Lean model

This Lean 4 model evaluates the mechanism-selection question in
[issue #9025](https://github.com/richlander/dotnet-inspect/issues/9025):
which fold shape should body-use occurrence production use for `Exists`,
`Count`, and `Rows` before choosing an NLinq- or QuerySpace-shaped
orchestrator?

The normative owner remains
[`analysis-library-body-use.md`](../../analysis-library-body-use.md).
This model changes no product contract or runtime path. Its companion C#
NativeAOT compiler probe lives in
[`prototypes/body-use-terminal-folding`](../../../../prototypes/body-use-terminal-folding/).

## Exact pilot claim

One source operand is admitted atomically as zero or more occurrence rows,
rejected without rows, or failed. The model compares:

- discrete terminal executions;
- one fused traversal with independently settling terminal state; and
- terminal selection performed once before entering a specialized fold.

The [C# probe](../../../../prototypes/body-use-terminal-folding/) uses a
static generic fold as the NLinq-shaped alternative and a runtime switch that
enters the same generic fold as the QuerySpace-shaped alternative. These are execution-shape counterfactuals, not calls into either
product.

Lean checks that:

- fused `Exists`, `Count`, and `Rows` observations equal their discrete
  executions;
- the fused traversal visits each physical operand once rather than once per
  terminal;
- `Exists` and `Count` construct no occurrence rows;
- `Exists` remains settled when a later operand fails;
- one admitted operand contributes all of its rows to `Count`; and
- a runtime terminal choice selects one specialized execution before source
  traversal.

The paired
[TLA+ lifecycle model](../analysis-body-use-terminal-kernel-lifecycle/)
does not restate fold equivalence. It checks the neighboring orchestration
question: whether a runtime host selects the fused kernel before work, advances
all active terminals atomically, publishes independently settled results,
preserves an early `Exists` result through later failure, and eventually
publishes every terminal.

The pathological demo admits two rows from one operand and then fails the next
operand. Discrete and fused `Exists` settle successfully after the admitted
operand, while `Count` and `Rows` retain the later failure.

## Run

The model pins Lean 4.34.1 and has no package dependencies:

```bash
cd docs/design/models/body-use-terminal-folding
lake build
lake exe body-use-terminal-folding
```

Run the paired lifecycle model with the repository-pinned TLA+ tools from the
repository root:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  eng/run-tla-checks.sh \
  docs/design/models/analysis-body-use-terminal-kernel-lifecycle
```

The C# fold shapes, NativeAOT measurements, and initial result are in the
[compiler probe](../../../../prototypes/body-use-terminal-folding/).

## Decision boundary

The Lean model can determine semantic equivalence and discrete work:

- when a terminal may settle;
- which terminal-specific work must stop;
- which work may be shared;
- whether row construction is required; and
- whether fused execution preserves independent outcomes.

Lean does not establish C# compiler transparency, NativeAOT inlining, code
size, or allocation. The companion
[C# probe](../../../../prototypes/body-use-terminal-folding/) implements the
same fold shapes, checks their observations against direct terminal loops, and
measures NativeAOT time and allocation. Its results remain prototype evidence rather
than accepted product-performance evidence.

TLA+ does not import the Lean proof. The two tools share a small named contract
of operand admission, terminal status, completion, failure, values, and rows.
Lean establishes the fixed-kernel relation; TLA+ checks that temporal
orchestration preserves it. C# checks remain necessary to connect either model
to an implementation.

## Non-claims

- This is not a public QuerySpace, Analysis, or NLinq API proposal.
- It does not model body decoding, ownership attribution, operand binding, or
  the complete diagnostic vocabulary.
- Its row values represent already authenticated admitted occurrences.
- Count and Rows are assumed to require exhaustion because this source has no
  accepted source-native cardinality witness.
- It does not prove correspondence with the current C# implementation.
- It does not provide a machine-checked proof connecting Lean and TLA+.
- It does not select NLinq or QuerySpace without the compiler probe and
  production scorecard evidence required by #9025.
