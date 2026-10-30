# Body-use terminal folding compiler probe

This C# NativeAOT probe is the companion to the
[body-use terminal folding Lean model](../../docs/design/models/body-use-terminal-folding/),
which owns the exact claim and the semantic proof for
[issue #9025](https://github.com/richlander/dotnet-inspect/issues/9025). The
normative owner remains
[`analysis-library-body-use.md`](../../docs/design/analysis-library-body-use.md).
This prototype changes no product contract or runtime path.

The probe implements the Lean model's fold shapes in C#: direct terminal
loops, a static generic fold as the NLinq-shaped alternative, and a runtime
switch that enters the same generic fold as the QuerySpace-shaped alternative.
It checks their observations against the direct loops and measures NativeAOT
time, allocation, and code shape, which Lean cannot establish.

## Run

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
