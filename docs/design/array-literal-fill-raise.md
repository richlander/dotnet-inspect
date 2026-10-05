# Array literal fill raise

`ArrayLiteralFromStoresPass` owns one raise: a compiler-emitted array
allocation followed by a contiguous run of constant-index element stores
becomes one `ArrayLiteral`. This document is the normative owner of when that
raise fires and what the raised literal carries. The pass's doc comment states
the same rules beside the code; the
[value-typed emission](value-typed-emission.md) sink table records the one
coercion row the literal adds.

## Motivating assets

- Newtonsoft.Json 13.0.4, `DynamicProxyMetaObject<T>.GetArgArray(DynamicMetaObject[], DynamicMetaObject)`
  (#9249): a two-element `Expression[]` whose second element is a conditional.
- Newtonsoft.Json 13.0.4, `StringUtils.FormatWith` (the two-, three-, and
  four-argument overloads): `params object[]` fills of two to four elements.
- Microsoft.CodeAnalysis.CSharp 5.0.0,
  `OverloadResolutionResult<T>.ReportWrongCallingConvention`: a two-element
  `object[]` argument array built inline in a `DiagnosticInfoWithSymbols`
  constructor call.

## Fill run

A place that receives `new T[n]` (constant `n`, 1 to 4,096) raises when a
later run in the same block stores indices `0 … n-1` in increasing order. All
of the following must hold:

- a local's declared element type equals the allocated element type, because
  array covariance would let a stored value fail at `new U[] { … }`;
- no element value reads, writes, or addresses the place;
- nothing between the allocation and the run touches the place;
- after the run, the place is read exactly once and never written,
  addressed, or element-stored again.

The literal replaces the run's first store, so the element values evaluate
where the IL evaluated them. Only the allocation moves; an unreachable fresh
array has no observable identity or ordering.

## Copy chains

The importer mints a fresh stack slot for every `dup`. A multi-element fill
therefore reaches the pass as a direct copy chain:

```csharp
S_256 = new object[2];
S_256[0] = arg0;
S_257 = S_256;      // dup
S_257[1] = arg1;
return format.FormatWith(provider, S_257);
```

A synthetic stack slot whose only store in the function copies a member of
the chain names the same array reference. For a stack-slot place:

- the chain is one place, and every fill-run check above applies to the union
  of its members;
- copy statements between the allocation and the end of the run retire with
  the raise;
- the single escaping read, through any member, reads the allocation's slot;
- nothing retires unless the literal commits.

Locals never join a chain, because a typed local can be re-bound or addressed.
A copy whose target slot has a second store is a join, not an alias, and the
run declines.

## Spilled elements

csc evaluates an element that needs its own control flow (a conditional) into
a stack slot between the array/index pushes and the `stelem`. The place can
be a stack slot or a local: csc keeps the array in a local when it is read in
a loop, and in Debug builds. A slot is part of the run, as element `k`'s
value, when all of the following hold:

- it is stored immediately before element `k`'s store, for `k ≥ 1`;
- it has exactly one store and exactly one load;
- that one load is the element store's value.

Its value evaluates after the element-0…`k-1` stores and after the
effect-free array load (slot or local) and constant index, exactly as in the
IL. A
spilled value that reads the place declines, like any element value. A slot
read anywhere else is a shared carrier; it declines.

## Element coercion

Each literal element keeps the coercion sink its element store had. The
literal's element type is the `newarr` token, the semantic target
`CoercionSinks.StoreElementTarget` derives from the array, never a `stelem`
storage width:

- `CoercionSinks` enumerates every `ArrayLiteral` element at that type;
- an untyped slot load in an element testifies that type;
- the printer routes every element through `CoerceText` at that type.

Without this, a cross-assembly enum fill raised as
`new StringComparison[] { 4, 5 }` (CS0266) where the stores printed
`(StringComparison)4`.

## Gates

| Property | Gate |
| --- | --- |
| Fill-run guards (order, covariance, place observation, escape count, re-binding) | `ArrayLiteralFromStoresPassTests` |
| Chains of two and four, a copy ahead of the run, spilled conditional element, nested `object[][]` | `ArrayLiteralCopyChainTests` positive facts |
| Alias read before the run, element reading an alias, second escape, multi-store alias, local copy, blocked run leaving the chain untouched, shared spill, spill reading the place | `ArrayLiteralCopyChainTests` decline facts |
| Real witnesses through the full pipeline | `ArrayLiteralCopyChainTests.RealDupChainsRaise` (Newtonsoft.Json `GetArgArray`, `FormatWith` ×3) |
| Spilled elements in a local array (synthetic and csc-compiled, including a shared merge slot that must decline) | `ArrayLiteralCopyChainTests.SpilledConditionalElementInALocalArray_JoinsTheRun`, `CompiledLocalArraySpillsRender` |
| Element coercion | `EnumCastPrinterTests.CrossAssemblyEnumArray_CastsElementStore` (compiles the render) |

## Out of scope

- Runs interrupted by nested allocations, `?.`, or `??` elements, or by shared
  multi-store element carriers (for example `JsonWriter..cctor`). These keep
  their allocation and stores.
- Multi-dimensional arrays and arrays filled through `InitializeArray` (the
  RVA path owned by `RvaSpanPass`).
