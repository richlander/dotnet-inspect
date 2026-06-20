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
                case Return { Value: Constant constant }:
                    Retype(constant, function.Signature.ReturnType, shapes, stepper);
                    break;
                case StoreLocal { Value: Constant constant } store:
                    Retype(constant, store.Type, shapes, stepper);
                    break;
                case StoreArgument { Value: Constant constant } store:
                    Retype(constant, store.Type, shapes, stepper);
                    break;
                case StoreField { Value: Constant constant } store:
                    Retype(constant, store.Field.Type, shapes, stepper);
                    break;
                case Call call:
                    RetypeArguments(call.Callee, call.Arguments, call.Callee.HasThis ? 1 : 0, shapes, stepper);
                    break;
                case NewObject ctor:
                    RetypeArguments(ctor.Constructor, ctor.Arguments, 0, shapes, stepper);
                    break;
                case Comparison { Left: { } left, Right: Constant constant }
                    when left is not Constant && left.ResultType is { } leftType:
                    Retype(constant, leftType, shapes, stepper);
                    break;
                case Box { Operand: Constant constant } box:
                    Retype(constant, box.Type, shapes, stepper);
                    break;
                case StoreElement { Value: Constant constant, ElementType: { } elementType }:
                    Retype(constant, elementType, shapes, stepper);
                    break;
                // `stind.i1`/`stind.i2` carry only a storage width (sbyte/short), so a
                // `bool`/`char` location stored through a managed ref or pointer keeps
                // the opcode's integer type. The address's pointee type is the real
                // target — `ref bool wasSymlink = 0` is `wasSymlink = false`.
                case StoreIndirect { Value: Constant constant } store
                    when (PointeeType(store.Address.ResultType) ?? store.Type) is { } indirectType:
                    Retype(constant, indirectType, shapes, stepper);
                    break;
                // `flags & 16` is `BindingFlags & int` — CS0019. The bitwise
                // operators are the enum-flag idiom; an int constant beside an
                // enum operand carries that enum's identity, so retype it (the
                // printer then names the member or casts).
                case Binary { Kind: BinaryKind.And or BinaryKind.Or or BinaryKind.Xor } binary:
                    if (EnumOperandType(binary.Left, shapes) is { } leftEnum && binary.Right is Constant rightConst)
                        Retype(rightConst, leftEnum, shapes, stepper);
                    else if (EnumOperandType(binary.Right, shapes) is { } rightEnum && binary.Left is Constant leftConst)
                        Retype(leftConst, rightEnum, shapes, stepper);
                    break;
            }
        }
    }

    static TypeRef? PointeeType(TypeRef? type)
        => type is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } ? type.ElementType : null;

    static TypeRef? EnumOperandType(IrExpression operand, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => operand is not Constant && operand.ResultType is { } type && shapes.GetValueOrDefault(type) == TypeShape.Enum
            ? type
            : null;

    static void RetypeArguments(MethodRef callee, IReadOnlyList<IrExpression> arguments, int receiverOffset, IReadOnlyDictionary<TypeRef, TypeShape> shapes, Stepper stepper)
    {
        for (int i = 0; i < callee.ParameterTypes.Length && i + receiverOffset < arguments.Count; i++)
        {
            if (arguments[i + receiverOffset] is Constant constant)
                Retype(constant, callee.ParameterTypes[i], shapes, stepper);
        }
    }

    static void Retype(Constant constant, TypeRef target, IReadOnlyDictionary<TypeRef, TypeShape> shapes, Stepper stepper)
    {
        if (constant.Value is not int value)
            return;
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

        // An integer flowing into an enum position carries the enum's identity;
        // the printer names it from the resolved member map. The value is kept
        // (still an int); only the constant's type changes.
        if (shapes.GetValueOrDefault(target) == TypeShape.Enum)
        {
            stepper.StepOver($"retype constant {value} to enum {target.Name}", constant);
            constant.ReplaceWith(new Constant(value, target));
        }
    }
}
