namespace ILInspector.Decompiler.Pipeline;

/// <summary>Decides same-place scalar updates after coercion. See docs/design/scalar-self-updates.md.</summary>
public sealed class ScalarSelfUpdatePass : IIrPass
{
    public string Name => "scalar-self-update";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var store in function.DescendantsOutsideNestedFunctions.OfType<ScalarStore>())
        {
            var targetType = store switch
            {
                StoreLocal local => local.Type,
                StoreArgument argument => argument.Type,
                StoreField field => field.Field.Type,
                StoreProperty property => property.Accessor.ParameterTypes.LastOrDefault(),
                StoreIndirect indirect => PointerArithmetic.PointeeType(indirect.Address) ?? indirect.Type,
                _ => null,
            };
            ScalarUpdateKind? kind = targetType?.Kind is not (TypeRefKind.Pointer or TypeRefKind.ByRef or TypeRefKind.FunctionPointer)
                && !TargetUsesMaterializedStackSlot(function, store)
                && store.Value is Binary binary
                && ReadsTarget(store, binary.Left, targetType)
                    ? Classify(binary)
                    : null;
            if (store.UpdateKind == kind)
                continue;

            context.Stepper.StepOver("decide scalar self-update", store);
            store.UpdateKind = kind;
        }
    }

    static bool TargetUsesMaterializedStackSlot(
        IrFunction function,
        ScalarStore store)
    {
        IrExpression? target = store switch
        {
            StoreField field => field.Instance,
            StoreProperty property => property.Instance,
            StoreIndirect indirect => indirect.Address,
            _ => null,
        };
        return target is not null
            && target.DescendantsAndSelfOutsideNestedFunctions
                .OfType<LoadLocal>()
                .Any(load =>
                    function.Locals[load.Index].Kind == TypeRefKind.ByRef
                    &&
                    function.TryGetMaterializedStackSlotLocal(
                        load.Index,
                        out _));
    }

    internal static ScalarUpdateKind Classify(Binary binary)
    {
        var right = binary.Right is Coerce { Operand: Constant constant } ? constant : binary.Right;
        return (binary.Kind, right) switch
        {
            (BinaryKind.Add, Constant { Value: 1 }) => ScalarUpdateKind.Increment,
            (BinaryKind.Subtract, Constant { Value: 1 }) => ScalarUpdateKind.Decrement,
            _ => ScalarUpdateKind.Binary,
        };
    }

    static bool ReadsTarget(ScalarStore store, IrExpression read, TypeRef? targetType)
        => (store, read is Coerce coercion && coercion.Target.Equals(targetType) ? coercion.Operand : read) switch
    {
        (StoreLocal s, LoadLocal l) => s.Index == l.Index,
        (StoreArgument s, LoadArgument l) => PlaceIdentity.SameArgument(s.Index, s.Parameter, l.Index, l.Parameter),
        (StoreField s, LoadField l)
            => s.IsVolatile == l.IsVolatile
                && Equals(s.Field, l.Field) && PlaceIdentity.SamePlace(s.Instance, l.Instance),
        (StoreProperty s, LoadProperty l)
            => s.PropertyName == l.PropertyName
                && Equals(s.Accessor.DeclaringType, l.Accessor.DeclaringType)
                && Equals(s.Accessor.ParameterTypes.LastOrDefault(), l.ResultType)
                && s.Accessor.ParameterTypes.Take(s.Accessor.ParameterTypes.Length - 1).SequenceEqual(l.Accessor.ParameterTypes)
                && s.IsVirtual == l.IsVirtual
                && PlaceIdentity.SameLValue(s.Instance, l.Instance)
                && PlaceIdentity.SameOperands(s.IndexArguments, l.IndexArguments),
        (StoreIndirect s, LoadIndirect l)
            => s.IsVolatile == l.IsVolatile && PlaceIdentity.SameLValue(s.Address, l.Address),
        _ => false,
    };
}
