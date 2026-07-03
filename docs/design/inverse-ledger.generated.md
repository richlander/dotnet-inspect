# Inverse node ledger (generated)

Generated from the `[InverseOf]` / `[NotInverted]` annotations in
`ILInspector.Decompiler` by the test reflector; drift-gated by a test. Do not
edit by hand. See [inverse-architecture.md](inverse-architecture.md) for the framing.

## Type assertions

| Node | Forward construct (Roslyn) | Oracle (RyuJIT) | Naming | Precondition | Witness |
| --- | --- | --- | --- | --- | --- |
| `AddressOfMethod` | &method (BoundFunctionPointerLoad) / ldftn | None | Native | result is the contextual function-pointer type only when the address is stored to a local or field or returned (`MethodAddressPass` recovers the `delegate*` target's type for those parents); otherwise the managed function-pointer type built from the method's own signature | function-pointer fixtures; corpus compile-back |
| `ArrayLength` | BoundArrayLength / ldlen | None | Inherited | result is `System.Int32` — the array's `.Length` (`ldlen` pushes a native int; the `.Length` property is `int`) | array fixtures; corpus compile-back |
| `Binary` | BoundBinaryOperator (arithmetic/bitwise/shift) | RyuJitImporter | Inherited | result type is the ECMA binary-numeric result of the operand stack types | corpus compile-back |
| `Box` | BoundConversion (boxing) | RyuJitImporter | Inherited | target is the boxed value type | box/unbox fixtures |
| `Call` | BoundCall / call·callvirt | None | Inherited | result is the callee's return type (method signature); `IsVirtual` selects `callvirt`, `ConstrainedTo` carries a `constrained.` prefix | corpus compile-back |
| `CallIndirect` | BoundFunctionPointerInvocation / calli | None | Inherited | result is the `calli` standalone signature's return type; parameter types and calling convention come from that signature, not a resolved method (`IsInstance` marks a receiver absent from the parameter list) | function-pointer fixtures; corpus compile-back |
| `CastClass` | BoundConversion (reference cast) | RyuJitImporter | Inherited | target is the reference type the cast checks | cast fixtures; corpus compile-back |
| `Coerce` | BoundConversion (implicit, target-driven) | RyuJitStackNormalization | Native | sink type recoverable and distinguishable from the stack type | CoerceChokePointTests, CoercionInvariantTests, corpus render-text A/B |
| `Comparison` | BoundBinaryOperator (comparison) / ceq·clt·cgt | RyuJitImporter | Native | result is bool (the ceq/clt/cgt integer 0/1 result) | corpus compile-back |
| `Convert` | BoundConversion (numeric) | RyuJitStackNormalization | Inherited | none — models the conv.* that ran | round-trips by construction; corpus compile-back |
| `DelegateCreation` | BoundDelegateCreationExpression / newobj + ldftn·ldvirtftn | None | Inherited | result is the delegate type `DelegateType` (the `newobj` delegate constructor over a method-group `ldftn`/`ldvirtftn`) | function-pointer/delegate fixtures; corpus compile-back |
| `IndexFromEnd` | ^n (BoundFromEndIndexExpression) / new Index(n, fromEnd: true) | None | Native | result is `System.Index` (the `^n` from-end operand) | range/index fixtures; corpus compile-back |
| `InlineArraySpanConversion` | inline-array → span conversion / InlineArrayAsSpan | None | Native | result is the `Span<T>`/`ReadOnlySpan<T>` `SpanType` the inline-array AsSpan conversion produced (given) | inline-array fixtures; corpus compile-back |
| `IsInstance` | value as T (BoundAsOperator) / isinst | None | Inherited | result is the tested reference type `Type` (the `isinst` token); the value is that type or null | cast fixtures; corpus compile-back |
| `Lambda` | BoundLambda / closure lowering (synthesized method + delegate) | None | Inherited | result is the delegate type `DelegateType` the lambda is converted to; raised from the compiler's closure/display-class lowering | lambda/closure fixtures; corpus compile-back |
| `LoadArgument` | BoundParameter / ldarg | None | Inherited | type is the argument's declared type (parameter signature; declaring type for `this`) | corpus compile-back |
| `LoadArgumentAddress` | BoundParameter (by ref) / ldarga | None | Inherited | result is a managed reference (`ByRef`) to the argument's declared type (parameter signature; declaring type for `this`) | corpus compile-back |
| `LoadElement` | BoundArrayAccess / ldelem | RyuJitImporter | Inherited | result is the element type: the `ldelem` type token, the fixed type of a typed `ldelem.*` opcode (`ldelem.i4` → `int`, `ldelem.r8` → `double`, …), or — for `ldelem.ref`, which encodes no type — the array operand's element type | array fixtures; corpus compile-back |
| `LoadElementAddress` | BoundArrayAccess (by ref) / ldelema | None | Inherited | result is a managed reference (`ByRef`) to the array's element type (the `ldelema` type token); `IsReadOnly` marks the `readonly.` prefix (a read-only address) | corpus compile-back |
| `LoadField` | BoundFieldAccess / ldfld·ldsfld | None | Inherited | type is the field's declared type (field signature) | corpus compile-back |
| `LoadFieldAddress` | BoundFieldAccess (by ref) / ldflda·ldsflda | None | Inherited | result is a managed reference (`ByRef`) to the field's declared type (field signature) | corpus compile-back |
| `LoadFunctionPointer` | ldftn·ldvirtftn (method-group / function-pointer load) | None | Inherited | result is `System.IntPtr` — the raw method-address native int; `IsVirtual` selects `ldvirtftn` (dispatched on the receiver) over `ldftn` | function-pointer fixtures; corpus compile-back |
| `LoadIndirect` | ByRef/pointer dereference / ldobj·ldind.* | RyuJitImporter | Inherited | result is the opcode-encoded type (the `ldobj` token or the `ldind.*` element type); a `bool`/`char` location is recovered from the address's `ByRef`/pointer pointee (the `ldind.u1`/`ldind.u2` storage width is shared by `bool`/`byte` and `char`/`ushort`); `ldind.ref` encodes no type and takes the pointee | unsafe/indirect fixtures; corpus compile-back |
| `LoadLocal` | BoundLocal / ldloc | None | Inherited | type is the local's declared type (local variable signature) | corpus compile-back |
| `LoadLocalAddress` | BoundLocal (by ref) / ldloca | None | Inherited | result is a managed reference (`ByRef`) to the local's declared type — the receiver form for a value-type instance call or a `ref`/`out`/`in` argument | corpus compile-back |
| `LoadProperty` | BoundPropertyAccess / BoundIndexerAccess (get_ accessor call) | None | Native | type is the property or indexer's return type (accessor signature); raised from a get_ accessor call | corpus compile-back |
| `LoadStackSlot` | (synthesized) evaluation-stack slot | None | Native | `Type` is the reconciled type of a spilled evaluation-stack entry when known (the join of the slot's typed loads/stores — the value-typed-emission slot reconciliation), else null; no metadata token backs it | stack-slot fixtures; corpus compile-back |
| `LoadToken` | ldtoken (type/method/field handle) | None | Inherited | result is the `RuntimeTypeHandle`/`RuntimeMethodHandle`/`RuntimeFieldHandle` selected by the token `Kind` | corpus compile-back |
| `LocalFunctionInvocation` | BoundCall (local function) / call | None | Native | result is the local function's return type `ReturnType` (a `call` to a compiler-lowered local-function method, recognized and rendered as a local-function call) | local-function fixtures; corpus compile-back |
| `LogicalBinary` | BoundBinaryOperator (logical AND / OR) | None | Native | result is bool; raised from short-circuit branch patterns (IL has no logical-and/or encoding) | corpus compile-back |
| `LogicalNot` | BoundUnaryOperator (logical negation) | None | Native | result is bool; raised from ceq-zero / inverted-branch patterns (IL has no `!` opcode) | corpus compile-back |
| `NewArray` | BoundArrayCreation / newarr | None | Inherited | result is a single-dimension `T[]` (`SzArray`) of the `newarr` element-type token | array fixtures; corpus compile-back |
| `NewObject` | BoundObjectCreationExpression / newobj | None | Inherited | result is the constructed type (the `newobj` constructor's declaring type) | corpus compile-back |
| `NullConditional` | BoundConditionalAccess / ?. | None | Native | result is the member's unwrapped type (`Member.ResultType`); raised from the ?. null-check pattern (surrounding nodes carry any nullable wrapping / coalesce) | corpus compile-back |
| `RangeExpression` | BoundRangeExpression / start..end | None | Inherited | result is `System.Range` (the `start..end` operator; either endpoint may be omitted) | range/index fixtures; corpus compile-back |
| `SizeOf` | sizeof(T) (BoundSizeOfOperator) / sizeof | None | Inherited | result is `System.Int32` — the `sizeof(T)` byte size | corpus compile-back |
| `SliceExpression` | receiver[start..end] (range-indexer access) / GetSubArray·Slice | None | Native | result is the slice's type (the receiver's array/span type, given at construction), not an element type; raised from the range-slice lowering | range/index fixtures; corpus compile-back |
| `SpanLiteral` | new T[] { … } in ReadOnlySpan context / RuntimeHelpers.CreateSpan | None | Native | result is the `ReadOnlySpan<T>` `SpanType` the CreateSpan call produced (given); the elements are decoded from the field RVA blob | span-literal fixtures; corpus compile-back |
| `StackAllocArray` | stackalloc T[n] (Span form) / localloc + Span ctor | None | Inherited | result is the target-typed `Span<T>`/`ReadOnlySpan<T>` (given at construction); the element count is the constructor's `length` argument | unsafe/stackalloc fixtures; corpus compile-back |
| `StackAllocate` | stackalloc byte[n] (pointer form) / localloc | None | Inherited | result is `byte*` — localloc yields a raw byte pointer (the faithful element type is `byte`) | unsafe/stackalloc fixtures; corpus compile-back |
| `TypeOf` | typeof(T) (BoundTypeOfOperator) / ldtoken + GetTypeFromHandle | None | Inherited | result is `System.Type` — the folded `Type.GetTypeFromHandle(ldtoken T)` shape | corpus compile-back |
| `Unary` | BoundUnaryOperator (neg/not) | RyuJitImporter | Inherited | result type is the operand's type (neg/not preserve the operand type) | corpus compile-back |
| `Unbox` | BoundConversion (unboxing → managed pointer) | RyuJitImporter | Inherited | operand is a box of the value type; result is a managed pointer into it | box/unbox fixtures; corpus compile-back |
| `UnboxAny` | BoundConversion (unboxing) | RyuJitImporter | Inherited | target is the unbox.any type token (value type, reference type, or type parameter) | box/unbox fixtures; corpus compile-back |

## Declared non-inverse boundaries

| Node | Reason |
| --- | --- |
| _(none yet)_ | — |
