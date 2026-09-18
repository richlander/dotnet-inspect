# Scalar update overflow context

## Owner and claim

Decompiler numeric emission owns this contract: spelling a checked scalar
self-update preserves the overflow behavior of its retained operands in
statement and `for`-header positions. A checked outer operation does not make
an unchecked child checked, and leaving the update restores the enclosing
emission context.

This is a correctness repair under [value-typed emission](value-typed-emission.md),
not another retirement of numeric inference from the printer.
[Scalar self-update decisions](scalar-self-updates.md) still owns admission,
place identity, and unit-step selection. No new places or operators are admitted.

## Context and syntax

A checked compound in statement position needs a checked block: a standalone
`checked(value += amount)` is not a legal C# statement expression. Its target
and right operand must be rendered in that block's lexical context. Existing
numeric expression rendering owns unchecked arithmetic, negation, conversions,
and nested checked operations; compound spelling must use that same mechanism,
not duplicate its classification.

A checked block is not legal in either `for` header position. There, retain an
ordinary assignment with a checked right-hand expression instead of compound
sugar. The admitted store retains the complete read/compute/write expression,
so this requires no inferred operator or reconstructed operand. Its target is
outside the right-hand checked expression.

This follows the C# checked/unchecked context rules and the existing numeric
expression and pointer-update emitters. It does not change control flow,
evaluation order, storage identity, accessor dispatch, or declaration placement.
Other update families retain their owners. In particular, value-producing and
user-defined increment expressions are not scalar stores.

## Evidence and adoption

Issues #7563 and #7570 supply compiler-produced Release counterexamples:
unchecked arithmetic under a checked compound, and checked blocks incorrectly
emitted in both local-driven `for` header positions. No nodes, blocks, or
temporaries are consumed. Newly checked child arithmetic, loss of an original
overflow check, or invalid header syntax falsifies the claim.

The operator explicitly approved fixture-backed work after a bounded search
found no real-library reproducer. That search covered the .NET 11 RC1 managed
framework, FSharp.Core, Roslyn, YamlDotNet, BouncyCastle, and NodaTime. These are
preservation candidates, not demonstrated real-library improvements. The
search's unrelated failed System.Text.Json projection is tracked in #7593.

Release compiler-fixture gates cover nested checked/unchecked operations,
conversion and negation, ordinary storage families, both header positions,
context restoration, and native compile-back without a repair floor. Existing
numeric and scalar-decision tests remain neighboring gates. Fixed-input
base/head Render A/B and product-issued annotated-source comparisons describe
the changed population without claiming that unchanged library output improved.
The native gate is `Speed=Slow` (measured at 2.08 seconds for its updated-memory
case) and runs as a focused pre-merge gate and in daily Deep Inspect; the
remaining new cases are PR-fast.

CLI and Browser/Wasm both consume the shared decompiler emission path. Neither
host adds an overflow classifier or a header workaround.
