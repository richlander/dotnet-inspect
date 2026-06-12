namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Retypes integer constants by the position that consumes them: IL has no
/// bool or char constants (ldc.i4 serves all I4 types), so a 0 returned from
/// a bool-returning method IS false — recovering that is raising, not
/// guessing. Inverse of the compiler's constant lowering.
/// </summary>
public sealed class TypedConstantsPass : IIrPass
{
    public string Name => "typed-constants";

    public void Run(IrFunction function)
    {
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case Return { Value: Constant constant }:
                    Retype(constant, function.Signature.ReturnType);
                    break;
                case StoreLocal { Value: Constant constant } store:
                    Retype(constant, store.Type);
                    break;
                case StoreArgument { Value: Constant constant } store:
                    Retype(constant, store.Type);
                    break;
                case StoreField { Value: Constant constant } store:
                    Retype(constant, store.Field.Type);
                    break;
                case Call call:
                    RetypeArguments(call.Callee, call.Arguments, call.Callee.HasThis ? 1 : 0);
                    break;
                case NewObject ctor:
                    RetypeArguments(ctor.Constructor, ctor.Arguments, 0);
                    break;
                case Comparison { Left: { } left, Right: Constant constant }
                    when left is not Constant && left.ResultType is { } leftType:
                    Retype(constant, leftType);
                    break;
                case Box { Operand: Constant constant } box:
                    Retype(constant, box.Type);
                    break;
                case StoreElement { Value: Constant constant, ElementType: { } elementType }:
                    Retype(constant, elementType);
                    break;
                case StoreIndirect { Value: Constant constant, Type: { } indirectType }:
                    Retype(constant, indirectType);
                    break;
            }
        }
    }

    static void RetypeArguments(MethodRef callee, IReadOnlyList<IrExpression> arguments, int receiverOffset)
    {
        for (int i = 0; i < callee.ParameterTypes.Length && i + receiverOffset < arguments.Count; i++)
        {
            if (arguments[i + receiverOffset] is Constant constant)
                Retype(constant, callee.ParameterTypes[i]);
        }
    }

    static void Retype(Constant constant, TypeRef target)
    {
        if (constant.Value is not int value)
            return;
        if (target is not { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" })
            return;
        switch (target.Name)
        {
            case "Boolean" when value is 0 or 1:
                constant.ReplaceWith(new Constant(value == 1, target));
                break;
            case "Char" when value >= char.MinValue && value <= char.MaxValue:
                constant.ReplaceWith(new Constant((char)value, target));
                break;
        }
    }
}
