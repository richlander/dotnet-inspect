# Scalar self-update decisions

## Owner and claim

The decompiler's statement raising owns this contract. Its single claim is
that ordinary scalar stores carry the decision that they update the same
place, including the choice of a unit increment or decrement, before printing.
The C# printer consumes that decision rather than rediscovering the destination
inside the right-hand side.

This is a bounded retirement under
[value-typed emission](value-typed-emission.md), not a claim that all numeric
binding is decided in IR. The existing printer continues to own enum operand
coercion, enum-shift decomposition, unchecked mixed-sign binding, shift-mask spelling,
and overflow-context syntax.

## Representation and admission

Retain the existing store nodes and their complete read/compute/write children.
A typed scalar-update annotation records binary update, increment, or
decrement. Unlike replacing the store with a new expression, this preserves
the existing local-write, declaration, accessor-evidence, and effect consumers.
Cloning preserves the annotation along with the unchanged operation.
This follows the existing increment and pointer-update passes' pre-print
decision boundary, but deliberately retains the store representation rather
than introducing another implicit-write node family.

The decision runs after coercion insertion. Only a surviving binary whose
left operand reads the exact destination is eligible. Locals, arguments,
fields, properties/indexers, and indirect stores use the existing bounded
place-identity grammar. Field identity and property signature and dispatch
must agree; effectful receivers and indices do not acquire equality merely
because their printed text agrees. Pointer variables and reference rebindings
are outside the domain.
Volatility must agree between the read and write. Indirect opcode type equality
is not a storage-identity test: a Boolean read uses `ldind.u1`, whereas its
write uses `stind.i1`. The typed address owns the destination; the existing
numeric renderer still owns binding the operation to it.

The [checked integer operand binder](checked-integer-operands.md) may expose
that read through a `Coerce` to the destination's exact semantic type.
That identity boundary is transparent to place matching; a different target
type is not. This keeps a checked signed operation over unsigned storage from
becoming an unsigned compound, or vice versa.

An integer `1` on the right of addition or subtraction selects increment or
decrement, including a coerced integer `1`. Other admitted binaries
select a binary self-update. That selection does not promise that C# has a
compound operator for the destination type: enum shifts retain the existing
explicit assignment and cast.

The annotation describes the final operation, not a request to search or
repair a later tree. A pipeline that changes that operation must run the
decision again. Reconstruction imports defer the decision until their host
pipeline reaches emission. Independently raised nested bodies complete their
own pipeline; eligible late-embedded lambda bodies must also reach this
boundary. The lowered pipeline also runs the decision, preserving its existing
scalar statement spelling rather than silently changing its numeric binding.

Residual stack slots keep their existing printer-owned decision because their
final place identity still depends on slot reconciliation. This slice does not
copy that reconciliation or replace it with equality of slot numbers. Array
element updates, value-producing prefix/postfix expressions, pointer updates,
and user-defined operator updates retain their existing owners.

## Raise obligations

**Lowering shell:** Roslyn Release emits a store of a binary whose left operand
reads that same scalar destination. The fixture family and the runtime Math
witness pin this shell; a converted or otherwise wrapped store value remains
outside this decision's domain.

**Consumed ownership:** the annotation consumes no nodes, temporaries, or
entries. Rendering may use one destination evaluation only when the bounded
place grammar establishes the repeated read. Exact member identity and
matching accessor dispatch are required. A virtual getter paired with a base
setter remains an explicit assignment, as demonstrated by issue #7562.

**Control flow:** this decision does not restructure branches, loops,
exception regions, or transfers. Existing owners still place the statements;
the decision also applies when those owners place a store in a loop header.

**Replacement:** retain the existing numeric-binding renderer and independently
measure product-generated compile-back. Changed accessor dispatch, evaluation
count/order, overflow behavior, or newly invalid C# falsifies the contract.
Pre-existing numeric or surrounding-construct failures are not certified by an
unchanged rendering.

## Production adoption and evidence

The shared decompiler pipeline is the production consumer for both CLI and
Browser/Wasm. Neither host adds a parallel matcher. Ordinary statements,
both `for` header positions, checked contexts, annotated source, and both
memory-safety modes consume the same store decision.

The motivating real asset is `System.Math.BitIncrement(double)` from
dotnet/runtime, whose `ulong` updates exercise mixed-sign IL operands.
Preserve the numeric binding behavior rather than replacing it with a
syntactically shorter but unfaithful update.

Contract gates are Release tests asserting the decided IR and emitted output,
compiler-produced boundary fixtures, and native compile-back of product-owned
artifacts. Fixed-input base/head rendering distinguishes an ownership-only
change from an output change. Any changed output requires its own compile-back
result; an unchanged rendering is not evidence of newly recovered source.
Original source and structural comparison reports accompany the PR evidence,
including any reported correspondence gaps.

[Scalar update overflow context](scalar-update-overflow-context.md) owns the
subsequent checked-context repair (#7563 and #7570), not this ownership
retirement. Known unchanged limits stay tracked rather than promoted to
success: #7564 owns volatile declarations missing from native compilation
artifacts, and #7492 owns unsupported closure-body shapes.
