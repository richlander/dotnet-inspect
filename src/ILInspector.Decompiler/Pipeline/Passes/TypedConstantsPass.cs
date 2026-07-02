namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Retypes integer constants by the position that consumes them: IL has no
/// bool or char constants (ldc.i4 serves all I4 types), so a 0 returned from
/// a bool-returning method IS false — recovering that is raising, not
/// guessing. Inverse of the compiler's constant lowering.
/// </summary>
public sealed class TypedConstantsPass : IIrPass
{
    public string Name => "typed-constants";

    public void Run(IrFunction function, PassContext context)
    {
        var shapes = function.TypeShapes;
        var stepper = context.Stepper;
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case Return { Value: { } value }:
                    Retype(value, function.Signature.ReturnType, shapes, stepper);
                    break;
                case StoreLocal { Value: { } value } store:
                    Retype(value, store.Type, shapes, stepper);
                    break;
                case StoreArgument { Value: { } value } store:
                    Retype(value, store.Type, shapes, stepper);
                    break;
                case StoreField { Value: { } value } store:
                    Retype(value, store.Field.Type, shapes, stepper);
                    break;
                case StoreProperty { Value: { } value } store:
                    Retype(value, store.Accessor.ParameterTypes is { IsDefault: false, Length: > 0 } setter ? setter[^1] : null, shapes, stepper);
                    break;
                case NullCoalescingAssignment { Value: { } value } assign:
                    Retype(value, assign.LocalType, shapes, stepper);
                    break;
                case NullCoalescingFieldAssignment { Value: { } value } assign:
                    Retype(value, assign.Field.Type, shapes, stepper);
                    break;
                case NullCoalescingPropertyAssignment { Value: { } value } assign:
                    Retype(value, assign.PropertyType, shapes, stepper);
                    break;
                case Call call:
                    RetypeArguments(call.Callee, call.Arguments, call.Callee.HasThis ? 1 : 0, shapes, stepper);
                    break;
                case NewObject ctor:
                    RetypeArguments(ctor.Constructor, ctor.Arguments, 0, shapes, stepper);
                    break;
                // Either side: a constant compared against a typed non-constant
                // carries that side's identity (`5 == e` occurs in IL as readily
                // as `e == 5`).
                case Comparison { Left: { } left, Right: { } right }:
                    if (left is not Constant && left.ResultType is { } leftType)
                        Retype(right, leftType, shapes, stepper);
                    else if (right is not Constant && right.ResultType is { } rightType)
                        Retype(left, rightType, shapes, stepper);
                    break;
                case Box { Operand: { } operand } box:
                    Retype(operand, box.Type, shapes, stepper);
                    break;
                // The stelem opcode carries only a storage width (`stelem.i8` says
                // Int64, not the long-backed enum), so the semantic target is the
                // array's element type when it resolves — the same
                // storage-vs-semantic split the printer's StoreElementTargetType
                // makes.
                case StoreElement { Value: { } value } store:
                    Retype(value, ElementTargetType(store), shapes, stepper);
                    break;
                // `stind.i1`/`stind.i2` carry only a storage width (sbyte/short), so a
                // `bool`/`char` location stored through a managed ref or pointer keeps
                // the opcode's integer type. The address's pointee type is the real
                // target — `ref bool wasSymlink = 0` is `wasSymlink = false`.
                case StoreIndirect { Value: { } value } store
                    when (PointeeType(store.Address.ResultType) ?? store.Type) is { } indirectType:
                    Retype(value, indirectType, shapes, stepper);
                    break;
                // `flags & 16` is `BindingFlags & int` — CS0019. The bitwise
                // operators are the enum-flag idiom; an int constant beside an
                // enum operand carries that enum's identity, so retype it (the
                // printer then names the member or casts).
                case Binary { Kind: BinaryKind.And or BinaryKind.Or or BinaryKind.Xor } binary:
                    if (EnumOperandType(binary.Left, shapes) is { } leftEnum && binary.Right is Constant or Convert)
                        Retype(binary.Right, leftEnum, shapes, stepper);
                    else if (EnumOperandType(binary.Right, shapes) is { } rightEnum && binary.Left is Constant or Convert)
                        Retype(binary.Left, rightEnum, shapes, stepper);
                    break;
                // An enum-typed join (set by the diamond/store-chain folds) types
                // both arms. Enum only: bool/char conditionals belong to
                // BooleanFoldingPass, whose 0/1-arm reconciliation must not be
                // preempted here.
                case Conditional { MergedType: { } merged } conditional
                    when shapes.GetValueOrDefault(merged) == TypeShape.Enum:
                    Retype(conditional.WhenTrue, merged, shapes, stepper);
                    Retype(conditional.WhenFalse, merged, shapes, stepper);
                    break;
                // Switch case labels are deliberately not retyped: SwitchSection
                // holds them outside the child tree (ReplaceWith cannot reach
                // them) and the printer spells labels through EnumConstantText.
            }
        }
    }

    static TypeRef? ElementTargetType(StoreElement store)
        => store.Array.ResultType is { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } element }
            ? element
            : store.ElementType;

    static TypeRef? PointeeType(TypeRef? type)
        => type is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } ? type.ElementType : null;

    static TypeRef? EnumOperandType(IrExpression operand, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
    {
        if (operand is Constant)
            return null;
        // A `ldind.i4`/`ldind.i1`/... through a `ref EnumType` (or `EnumType*`)
        // yields a LoadIndirect typed by the opcode width (int/byte/...), not the
        // pointee enum — so `styles & 64` reads as `int & int` in the IR but binds
        // as `enum & int` in C# (CS0019). The declared type is the pointee enum;
        // see through the load so the constant carries that enum's identity.
        var type = operand is LoadIndirect { Address.ResultType: { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { } pointee } }
            ? pointee
            : operand.ResultType;
        return type is { } && shapes.GetValueOrDefault(type) == TypeShape.Enum ? type : null;
    }

    static void RetypeArguments(MethodRef callee, IReadOnlyList<IrExpression> arguments, int receiverOffset, IReadOnlyDictionary<TypeRef, TypeShape> shapes, Stepper stepper)
    {
        // A callee resolved through an unusual seam (e.g. a reconstructed
        // state-machine MoveNext) can carry an uninitialized parameter list;
        // `.Length` on a default ImmutableArray throws. Retype only against a
        // materialized signature — leave the rest, don't crash the pass.
        if (callee.ParameterTypes.IsDefault)
            return;
        for (int i = 0; i < callee.ParameterTypes.Length && i + receiverOffset < arguments.Count; i++)
            Retype(arguments[i + receiverOffset], callee.ParameterTypes[i], shapes, stepper);
    }

    static void Retype(IrExpression expression, TypeRef? target, IReadOnlyDictionary<TypeRef, TypeShape> shapes, Stepper stepper)
    {
        // A retype position whose target type did not resolve carries no
        // identity to recover; skip it rather than dereference a null target.
        if (target is null)
            return;
        switch (expression)
        {
            case Constant constant:
                RetypeConstant(constant, target, shapes, stepper);
                return;
            // A long/ulong-backed enum constant lowers as `ldc.i4 N; conv.i8`
            // (or `conv.u8`), so at the sink the payload is a Convert over the
            // int constant, not a constant — and it never names or retypes.
            // Fold the widening into a typed constant: the value survives (a
            // conv.u8 target zero-extends the 32-bit payload, a conv.i8 target
            // sign-extends — keyed off the target, as in TryEnumCastLiteral)
            // and csc regenerates the identical ldc.i4/conv pair from the
            // member name or cast on recompile. Unchecked 8-byte widenings
            // only: a conv.ovf traps on an out-of-range value and a narrowing
            // truncates, so neither folds; enum targets only, where the
            // recovered identity pays for erasing the IL-history node.
            case Convert
            {
                IsChecked: false,
                Target: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Int64" or "UInt64" } convertTarget,
                Operand: Constant { Value: int payload },
            } convert when shapes.GetValueOrDefault(target) == TypeShape.Enum:
                long widened = convertTarget.Name == "UInt64" ? (uint)payload : payload;
                stepper.StepOver($"fold widening conv into enum {target.Name} constant", convert);
                convert.ReplaceWith(new Constant(widened, target));
                return;
        }
    }

    static void RetypeConstant(Constant constant, TypeRef target, IReadOnlyDictionary<TypeRef, TypeShape> shapes, Stepper stepper)
    {
        if (constant.Value is int value)
        {
            if (target is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" })
            {
                switch (target.Name)
                {
                    case "Boolean" when value is 0 or 1:
                        stepper.StepOver($"retype constant {value} to bool", constant);
                        constant.ReplaceWith(new Constant(value == 1, target));
                        return;
                    case "Char" when value >= char.MinValue && value <= char.MaxValue:
                        stepper.StepOver($"retype constant {value} to char", constant);
                        constant.ReplaceWith(new Constant((char)value, target));
                        return;
                }
            }
        }
        else if (constant.Value is not long)
        {
            return;
        }

        // An integer flowing into an enum position carries the enum's identity;
        // the printer names it from the resolved member map. The value is kept
        // (an int, or an ldc.i8 for a long-backed enum); only the type changes.
        // Idempotent: a constant already carrying the target (an earlier run of
        // this pass) is left alone rather than re-replaced on every run.
        if (shapes.GetValueOrDefault(target) == TypeShape.Enum && constant.Type?.Equals(target) != true)
        {
            stepper.StepOver($"retype constant to enum {target.Name}", constant);
            constant.ReplaceWith(new Constant(constant.Value, target));
        }
    }
}
