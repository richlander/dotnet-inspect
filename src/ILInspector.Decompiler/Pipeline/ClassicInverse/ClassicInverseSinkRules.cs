namespace ILInspector.Decompiler.Pipeline;

internal static partial class ClassicInverseExpressionRules
{
    internal static bool IsSinkCoercion(
        IrExpression raw,
        Coerce planning,
        ClassicInverseBudget budget,
        TypeRef? selectedTarget = null)
    {
        if (!budget.Charge()
            || raw.SourceOffset < 0
            || raw.SourceOffset != planning.SourceOffset
            || raw.SourceOffset != planning.Operand.SourceOffset
            || !Equals(raw.ResultType, planning.Operand.ResultType)
            || !Equals(selectedTarget ?? SinkType(raw, budget), planning.Target)
            || !Equals(SinkType(planning, budget), planning.Target)
            || raw.ResultType is not { } source)
            return false;

        if (raw is Constant { Value: int value })
        {
            return TypeFamilies.Of(source) == TypeFamilies.Of(planning.Target)
                && CSharpConversionRules.ConstantFits(value, planning.Target);
        }
        if (raw is Constant { Value: long wide })
        {
            return TypeFamilies.Of(source) == TypeFamilies.Of(planning.Target)
                && CSharpConversionRules.ConstantFits(wide, planning.Target);
        }
        IrFunction? function = FunctionOf(raw, budget);
        return function is not null
            && CoercionRendering.CanSpellSlotCoercion(source, planning.Target,
                function.TypeShapes, function.EnumUnderlyingTypes);
    }

    internal static bool IsRetypedLiteral(
        IrExpression raw,
        Constant planning,
        ClassicInverseBudget budget,
        TypeRef? selectedTarget = null)
    {
        if (!budget.Charge() || raw.SourceOffset < 0 || raw.SourceOffset != planning.SourceOffset)
            return false;
        TypeRef? target = selectedTarget ?? SinkType(raw, budget);
        if (target is null || !target.Equals(planning.Type)
            || !Equals(SinkType(planning, budget), target))
            return false;

        if (raw is Constant { Value: int integerValue } integer
            && MemberIdentity.IsCoreLibraryType(integer.Type, "System", "Int32"))
        {
            if (MemberIdentity.IsCoreLibraryType(target, "System", "Boolean"))
                return integerValue is 0 or 1 && planning.Value is bool boolean && boolean == (integerValue == 1);
            if (MemberIdentity.IsCoreLibraryType(target, "System", "Char"))
                return CSharpConversionRules.ConstantFits(integerValue, target)
                    && planning.Value is char character && character == integerValue;
        }

        IrFunction? rawFunction = FunctionOf(raw, budget);
        IrFunction? planningFunction = FunctionOf(planning, budget);
        if (rawFunction is null || planningFunction is null
            || !CoercionRendering.CanSpellIntegerToEnum(raw.ResultType, target, rawFunction.TypeShapes)
            || planningFunction.TypeShapes.GetValueOrDefault(target) != TypeShape.Enum)
            return false;
        rawFunction.EnumUnderlyingTypes.TryGetValue(target, out TypeRef? underlying);
        planningFunction.EnumUnderlyingTypes.TryGetValue(target, out TypeRef? otherUnderlying);
        if (underlying is not null && otherUnderlying is not null && !underlying.Equals(otherUnderlying))
            return false;
        underlying ??= otherUnderlying;

        return raw switch
        {
            Constant { Value: int value } constant
                when MemberIdentity.IsCoreLibraryType(constant.Type, "System", "Int32")
                    && (underlying is null || TypeFamilies.Of(underlying) == StackFamily.I4) =>
                Equals(planning.Value, value)
                    && (underlying is null
                        || CSharpConversionRules.SameNumericSlotWidth(constant.Type, underlying)
                        || CSharpConversionRules.ConstantFits(value, underlying)),
            Constant { Value: long value } constant
                when MemberIdentity.IsCoreLibraryType(constant.Type, "System", "Int64")
                    && (underlying is null || TypeFamilies.Of(underlying) == StackFamily.I8) =>
                Equals(planning.Value, value),
            Convert
            {
                IsChecked: false,
                IsUnsigned: false,
                Target: { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary,
                    Namespace: "System", Name: "Int64" or "UInt64" } widened,
                Operand: Constant { Value: int value, SourceOffset: >= 0 } constant,
            } when (underlying is null || TypeFamilies.Of(underlying) == StackFamily.I8)
                && MemberIdentity.IsCoreLibraryType(constant.Type, "System", "Int32") =>
                Equals(planning.Value, widened.Name == "UInt64" ? (long)(uint)value : (long)value),
            _ => false,
        };
    }

    internal static TypeRef? SinkType(IrNode node, ClassicInverseBudget budget)
    {
        if (!budget.Charge())
            return null;
        return node.Parent switch
        {
            Call call => ParameterType(call.Callee, node.ChildIndex - (call.Callee.HasThis ? 1 : 0)),
            NewObject creation => ParameterType(creation.Constructor, node.ChildIndex),
            StoreLocal store when ReferenceEquals(store.Value, node) => store.Type,
            StoreArgument store when ReferenceEquals(store.Value, node) => store.Type,
            StoreField store when ReferenceEquals(store.Value, node) => store.Field.Type,
            StoreProperty store when ReferenceEquals(store.Value, node)
                && !store.Accessor.ParameterTypes.IsDefaultOrEmpty => store.Accessor.ParameterTypes[^1],
            Box box => box.Type,
            Coerce coerce => coerce.Target,
            Conditional conditional when !ReferenceEquals(conditional.Condition, node) => conditional.ResultType,
            Comparison comparison when ReferenceEquals(comparison.Left, node) && comparison.Right is not Constant =>
                comparison.Right.ResultType,
            Comparison comparison when ReferenceEquals(comparison.Right, node) && comparison.Left is not Constant =>
                comparison.Left.ResultType,
            Return => FunctionOf(node, budget)?.Signature.ReturnType,
            _ => null,
        };
    }

    static TypeRef? ParameterType(MethodRef method, int index)
        => !method.ParameterTypes.IsDefault && index >= 0 && index < method.ParameterTypes.Length
            ? method.ParameterTypes[index] : null;

    static IrFunction? FunctionOf(IrNode node, ClassicInverseBudget budget)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (!budget.Charge())
                return null;
            if (current is IrFunction function)
                return function;
            if (current is Lambda or LocalFunctionStatement)
                return null;
        }
        return null;
    }
}
