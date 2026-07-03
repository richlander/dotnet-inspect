# Inverse node ledger (generated)

Generated from the `[InverseOf]` / `[NotInverted]` annotations in
`ILInspector.Decompiler` by the test reflector; drift-gated by a test. Do not
edit by hand. See [inverse-architecture.md](inverse-architecture.md) for the framing.

## Type assertions

| Node | Forward construct (Roslyn) | Oracle (RyuJIT) | Naming | Precondition | Witness |
| --- | --- | --- | --- | --- | --- |
| `Binary` | BoundBinaryOperator (arithmetic/bitwise/shift) | RyuJitImporter | Inherited | result type is the ECMA binary-numeric result of the operand stack types | corpus compile-back |
| `Box` | BoundConversion (boxing) | RyuJitImporter | Inherited | target is the boxed value type | box/unbox fixtures |
| `CastClass` | BoundConversion (reference cast) | RyuJitImporter | Inherited | target is the reference type the cast checks | cast fixtures; corpus compile-back |
| `Coerce` | BoundConversion (implicit, target-driven) | RyuJitStackNormalization | Native | sink type recoverable and distinguishable from the stack type | CoerceChokePointTests, CoercionInvariantTests, corpus render-text A/B |
| `Comparison` | BoundBinaryOperator (comparison) / ceq·clt·cgt | RyuJitImporter | Native | result is bool (the ceq/clt/cgt integer 0/1 result) | corpus compile-back |
| `Convert` | BoundConversion (numeric) | RyuJitStackNormalization | Inherited | none — models the conv.* that ran | round-trips by construction; corpus compile-back |
| `LoadArgument` | BoundParameter / ldarg | None | Inherited | type is the argument's declared type (parameter signature; declaring type for `this`) | corpus compile-back |
| `LoadField` | BoundFieldAccess / ldfld·ldsfld | None | Inherited | type is the field's declared type (field signature) | corpus compile-back |
| `LoadLocal` | BoundLocal / ldloc | None | Inherited | type is the local's declared type (local variable signature) | corpus compile-back |
| `Unary` | BoundUnaryOperator (neg/not) | RyuJitImporter | Inherited | result type is the operand's type (neg/not preserve the operand type) | corpus compile-back |
| `Unbox` | BoundConversion (unboxing → managed pointer) | RyuJitImporter | Inherited | operand is a box of the value type; result is a managed pointer into it | box/unbox fixtures; corpus compile-back |
| `UnboxAny` | BoundConversion (unboxing) | RyuJitImporter | Inherited | target is the unbox.any type token (value type, reference type, or type parameter) | box/unbox fixtures; corpus compile-back |

## Declared non-inverse boundaries

| Node | Reason |
| --- | --- |
| _(none yet)_ | — |
