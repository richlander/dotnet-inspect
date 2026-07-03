# Inverse node ledger (generated)

Generated from the `[InverseOf]` / `[NotInverted]` annotations in
`ILInspector.Decompiler` by the test reflector; drift-gated by a test. Do not
edit by hand. See [inverse-architecture.md](inverse-architecture.md) for the framing.

## Type assertions

| Node | Forward construct (Roslyn) | Oracle (RyuJIT) | Naming | Precondition | Witness |
| --- | --- | --- | --- | --- | --- |
| `Box` | BoundConversion (boxing) | RyuJitImporter | Inherited | target is the boxed value type | box/unbox fixtures |
| `CastClass` | BoundConversion (reference cast) | RyuJitImporter | Inherited | target is the reference type the cast checks | cast fixtures; corpus compile-back |
| `Coerce` | BoundConversion (implicit, target-driven) | RyuJitStackNormalization | Native | sink type recoverable and distinguishable from the stack type | CoerceChokePointTests, CoercionInvariantTests, corpus render-text A/B |
| `Convert` | BoundConversion (numeric) | RyuJitStackNormalization | Inherited | none — models the conv.* that ran | round-trips by construction; corpus compile-back |
| `Unbox` | BoundConversion (unboxing → managed pointer) | RyuJitImporter | Inherited | operand is a box of the value type; result is a managed pointer into it | box/unbox fixtures; corpus compile-back |
| `UnboxAny` | BoundConversion (unboxing) | RyuJitImporter | Inherited | target is the unbox.any type token — an unboxed value type or a type parameter | box/unbox fixtures; corpus compile-back |

## Declared non-inverse boundaries

| Node | Reason |
| --- | --- |
| _(none yet)_ | — |
