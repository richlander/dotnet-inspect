# Single-case value switches

This document owns one reconstruction: a structured equality selection that
assigns a private result local in either arm and immediately returns that local
may become a single-case `SwitchExpression` with a default arm.

## Claim and boundary

The governing expression is evaluated once, the same selected value is
evaluated once, and the return stays in its original exception region.
The result local disappears only when its two arm stores and one convergence
read are its entire observable lifetime. Direct branch returns are not evidence
of this lowering and remain unchanged.

The accepted selection has two single-store arms followed immediately by the
return. Its condition is an equality with an integer constant, or the equivalent
zero test. The governing type is a known non-Boolean, non-character I4 integer
or an enum with such a known backing type; the case label must fit that type.
Both stored values, stores, and the convergence read have the same known type.
The case arm is the positive equality arm.

Top-level boxed arm values decline. The current switch-arm projection does not
preserve every explicit boxing conversion: mixed numeric operands can acquire
a common unboxed type before boxing. A shared IR result type of `object` alone
does not prove C# binding preserves the runtime boxed type. Enabling that family
requires a separate type-preserving projection fix, not this reconstruction.
That prerequisite is tracked in #7724.

Additional result reads, writes, address-taking, shared-scope captures, mismatched
types, unknown enum backing, and external entry into consumed labels decline
the raise. Independent nested-function local pools are not the enclosing
result local. Statements, transfers, or exception-region boundaries inside an
arm are outside this expression-only reconstruction.

These conditions justify a value-producing selection, not recovery of the
author's exact syntax. An explicitly written local with the same exclusive
lifetime can have the same lowering. Names, redundant annotations, literal
spelling, and a general switch/conditional style policy are not owned here.

## Motivation and production adoption

The real witness is .NET 11 RC1
`System.Data.SqlTypes.SqlBytes.get_MaxLength`, MethodDef `0x06001369` in
`System.Data.Common.dll` (#7698). Its PDB-selected reference source uses a
single-case switch expression with a conditional default value. Ordinary
structuring retains the result join, but return sinking dissolves it before
the existing multi-case switch raisers can recognize this smaller shape.

Reconstruct the expression before return sinking, using the existing
`SwitchExpression` and its existing typed arm and source-origin contracts.
This is one production-adoption step in both Default and Lowered pipelines;
the existing CLI and Browser/Wasm consumers receive the same decided tree.
No new printer semantic inference, rendering format, or host path is needed.
The overall thin-writer effort is #2095; this focused slice is #7698.

The existing comparison-chain switch raiser supplies the same private-result
join precedent. ILSpy's
[`ExpressionTransforms.HandleSwitchExpression`](https://github.com/icsharpcode/ILSpy/blob/72dbe6f41d480728ffa60bb68d1b95f9118d6b15/ICSharpCode.Decompiler/IL/Transforms/ExpressionTransforms.cs#L626)
also requires compatible arm exits and a common result variable, but consumes
its own switch-container representation. That is comparative evidence, not a
borrowed matcher or authority for our structured-if boundary.

## Evidence

`SingleCaseValueSwitchTests` gates the compiler-produced buffer-capacity
family, the real getter, exclusive storage and branch ownership, type
boundaries, and unchanged direct-return neighbors in Release. The same family
is compiled under both memory modes and inspected through Default and Lowered.
Its bounded native cases gate the independent compile-back outcome without a
repair floor; native evidence remains EH-blind and is not whole-program proof.

The falsifier is a changed evaluation count/order, selected value, exception
boundary, binding, or newly divergent native outcome. Fixed-input Render A/B,
Original/Before/After bodies, and product-issued structural comparisons supply
the broader population and presentation evidence. A correspondence gap remains
explicit; it does not license caller-created provenance.
