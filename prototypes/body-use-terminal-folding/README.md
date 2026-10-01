# Body-use terminal folding Lean pilot

This Lean 4 and TLA+ prototype evaluates the mechanism-selection question in
[issue #9025](https://github.com/richlander/dotnet-inspect/issues/9025):
which fold shape should body-use occurrence production use for `Exists`,
`Count`, and `Rows` before choosing an NLinq- or QuerySpace-shaped
orchestrator?

The normative owner remains
[`analysis-library-body-use.md`](../../docs/design/analysis-library-body-use.md).
This prototype changes no product contract or runtime path.

## Exact pilot claim

One source operand is admitted atomically as zero or more occurrence rows,
rejected without rows, or failed. The model compares:

- discrete terminal executions;
- one fused traversal with independently settling terminal state; and
- terminal selection performed once before entering a specialized fold.

The C# probe uses a static generic fold as the NLinq-shaped alternative and a
runtime switch that enters the same generic fold as the QuerySpace-shaped
alternative. These are execution-shape counterfactuals, not calls into either
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
[TLA+ lifecycle model](../../docs/design/models/analysis-body-use-terminal-kernel-lifecycle/)
does not restate fold equivalence. It checks the neighboring orchestration
question: whether a runtime host selects the fused kernel before work, advances
all active terminals atomically, publishes independently settled results,
preserves an early `Exists` result through later failure, and eventually
publishes every terminal.

The pathological demo admits two rows from one operand and then fails the next
operand. Discrete and fused `Exists` settle successfully after the admitted
operand, while `Count` and `Rows` retain the later failure.

## Run

The prototype pins Lean 4.34.1 and has no package dependencies:

```bash
cd prototypes/body-use-terminal-folding
lake build
lake exe body-use-terminal-folding
```

Run the paired lifecycle model with the repository-pinned TLA+ tools:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  eng/run-tla-checks.sh \
  docs/design/models/analysis-body-use-terminal-kernel-lifecycle
```

Build and run the equivalent C# fold shapes:

```bash
dotnet run \
  --project BodyUseTerminalFolding.CompilerProbe.csproj \
  -c Release -- check

dotnet publish BodyUseTerminalFolding.CompilerProbe.csproj \
  -c Release -r osx-arm64 \
  -o ../../artifacts/body-use-terminal-folding/osx-arm64

../../artifacts/body-use-terminal-folding/osx-arm64/\
body-use-terminal-folding-probe time

./inspect-nativeaot.sh osx-arm64
```

Replace `osx-arm64` with the current machine's runtime identifier. The publish
also emits a NativeAOT map file so the direct, static-fold, and planned-fold
entry points can be inspected for terminal selection or interface dispatch
inside their source loops.

## Initial result

The first `osx-arm64` NativeAOT probe establishes three useful facts:

- direct, static generic, and plan-selected folds produce identical values,
  failures, completion, operand visits, row construction, and row checksums;
- three independent terminals visit 1,025 physical operands in the 512-operand
  scenario, while the fused execution visits 512 and constructs the same 94
  Rows occurrences; and
- the NativeAOT map contains specialized direct, static-fold, planned-fold, and
  fused methods, but no generic `Fold` body or per-operand `Admit` methods.
  The constrained terminal operations were inlined into specialized loops.

NativeAOT emitted these method-code sizes:

| Shape | Method code |
| --- | ---: |
| Three direct terminal loops | 644 bytes total |
| Three static generic folds | 652 bytes total |
| One plan-selected method containing all three folds | 700 bytes |
| One fused `Exists`/`Count`/`Rows` traversal | 560 bytes |

The timing probe is diagnostic rather than accepted performance evidence. Its
repeated runs place full-source static and plan-selected Count/Rows folds in
the same range as direct loops with identical allocation. The one-operand
`Exists` case exposes fixed plan-selection overhead because almost no source
work remains. Fused execution is consistently faster than three independent
passes because it halves physical operand visits.

This supports a provisional mechanism: retain terminal intent as plan data,
select a typed fold once, and execute one specialized source loop whose active
terminal states settle independently. It does not support interpreting
terminal objects or interfaces once per operand.

The paired TLA+ model adds evidence that is intentionally outside the Lean
theorem: publication may occur after `Exists` settles but before a later
source failure, without invalidating or changing that published result. Its
negative controls also expose an orchestrator that advances the physical
source after updating only one active terminal, or that lets later failure
overwrite settled state.

The code-size result also argues against treating every runtime request set as
a novel nested generic composition. QuerySpace can retain and combine request
data, then select from a bounded family of typed fused kernels. An NLinq-style
fold remains useful as the kernel implementation technique rather than the
request orchestrator.

## Decision boundary

The Lean model can determine semantic equivalence and discrete work:

- when a terminal may settle;
- which terminal-specific work must stop;
- which work may be shared;
- whether row construction is required; and
- whether fused execution preserves independent outcomes.

Lean does not establish C# compiler transparency, NativeAOT inlining, code
size, or allocation. The companion C# probe implements the same fold shapes,
checks their observations against direct terminal loops, and measures
NativeAOT time and allocation. Its results remain prototype evidence rather
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
