namespace ILInspector.Decompiler.Pipeline;

/// <summary>Closed representation changes shared by value and coalescing correspondence.</summary>
internal static partial class ClassicInverseExpressionRules
{
    internal static bool IsTypeOf(Call call, TypeOf expression)
        => MemberIdentity.IsTypeGetTypeFromHandle(call)
            && call.ConstrainedTo is null
            && call.Callee.DeclaringType.Equals(TypeRef.CoreLib("System", "Type"))
            && call.Callee.TypeArguments.IsDefaultOrEmpty
            && call.Callee.DefinitionParameterTypes.IsDefaultOrEmpty
            && call.Callee.DefinitionReturnType is null
            && !call.Callee.HasRefReadOnlyParameters
            && call.Callee.ParameterRefKinds.All(static kind => kind == ArgumentRefKind.Value)
            && call.Arguments is
                [LoadToken { Kind: RuntimeTokenKind.Type, Type: { } target, SourceOffset: >= 0 }]
            && target.Equals(expression.Type);

    internal static bool SameTree(
        IrNode raw,
        IrNode planning,
        ClassicInverseBudget budget,
        IrNode? sourceReplacement = null,
        IrNode? outputReplacement = null,
        TypeRef? selectedTarget = null)
    {
        if (!budget.Charge())
            return false;
        if (ReferenceEquals(raw, sourceReplacement))
            return ReferenceEquals(planning, outputReplacement);
        if (raw is IrExpression value && planning is Coerce coerce && raw is not Coerce)
        {
            return IsSinkCoercion(value, coerce, budget, selectedTarget)
                && SameTree(raw, coerce.Operand, budget, sourceReplacement, outputReplacement, selectedTarget);
        }
        if (raw.SourceOffset < 0 || raw.SourceOffset != planning.SourceOffset)
            return false;
        if (raw is Call typeCall && planning is TypeOf typeOf)
            return IsTypeOf(typeCall, typeOf);
        if (raw is Convert conversion && planning is Constant convertedLiteral)
            return IsRetypedLiteral(conversion, convertedLiteral, budget, selectedTarget);
        if (raw is Comparison comparison
            && TryMatchBooleanNegation(comparison, planning, budget))
        {
            if (planning is LogicalNot not)
                return SameTree(comparison.Left, not.Operand, budget,
                    sourceReplacement, outputReplacement);
            var inner = (Comparison)comparison.Left;
            var inverted = (Comparison)planning;
            return SameTree(inner.Left, inverted.Left, budget, sourceReplacement, outputReplacement)
                && SameTree(inner.Right, inverted.Right, budget, sourceReplacement, outputReplacement);
        }
        if (raw.Children.Count != planning.Children.Count)
            return false;

        bool same = (raw, planning) switch
        {
            (LoadLocalAddress left, LoadLocalAddress right) =>
                left.Index == right.Index && Equals(left.Type, right.Type),
            (LoadLocal left, LoadLocal right) =>
                left.Index == right.Index && Equals(left.Type, right.Type),
            (Call { ConstrainedTo: null } call, LoadProperty property) =>
                call.Callee.Name.StartsWith("get_", StringComparison.Ordinal)
                && call.Callee == property.Accessor
                && call.IsVirtual == property.IsVirtual
                && call.Callee.HasThis == property.HasInstance,
            (Constant left, Constant right) =>
                ClassicInverseRealizationRules.PayloadEquals(left, right)
                || IsRetypedLiteral(left, right, budget, selectedTarget),
            _ => raw.GetType() == planning.GetType()
                && ClassicInverseRealizationRules.PayloadEquals(raw, planning),
        };
        if (!same)
            return false;
        for (int i = 0; i < raw.Children.Count; i++)
        {
            if (!SameTree(raw.Children[i], planning.Children[i], budget,
                    sourceReplacement, outputReplacement))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool TryMatchBooleanNegation(
        Comparison comparison,
        IrNode replacement,
        ClassicInverseBudget budget)
    {
        if (!budget.Charge()
            || comparison.SourceOffset < 0
            || comparison.SourceOffset != replacement.SourceOffset
            || comparison.IsUnsigned
            || comparison.Left.SourceOffset < 0
            || comparison.Left.ResultType is not { } operandType
            || !MemberIdentity.IsCoreLibraryType(operandType, "System", "Boolean")
            || comparison.Right is not Constant { SourceOffset: >= 0 } literal
            || !TryBooleanLiteral(literal, out bool value)
            || !(comparison.Kind == ComparisonKind.Equal && !value
                || comparison.Kind == ComparisonKind.NotEqual && value))
        {
            return false;
        }

        if (replacement is LogicalNot not)
        {
            return not.Operand.SourceOffset == comparison.Left.SourceOffset
                && Equals(not.Operand.ResultType, comparison.Left.ResultType);
        }
        if (comparison.Left is not Comparison inner
            || replacement is not Comparison inverted
            || inner.SourceOffset < 0
            || inner.Left.SourceOffset < 0
            || inner.Right.SourceOffset < 0)
        {
            return false;
        }

        StackFamily? family = TypeFamilies.Of(inner.Left.ResultType)
            ?? TypeFamilies.Of(inner.Right.ResultType);
        if (inner.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual)
            && family is not (StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F))
        {
            return false;
        }
        return inverted.Kind == Conditions.Inverse(inner.Kind)
            && inverted.IsUnsigned == (family == StackFamily.F
                ? !inner.IsUnsigned : inner.IsUnsigned)
            && inverted.Left.SourceOffset == inner.Left.SourceOffset
            && inverted.Right.SourceOffset == inner.Right.SourceOffset
            && Equals(inverted.Left.ResultType, inner.Left.ResultType)
            && Equals(inverted.Right.ResultType, inner.Right.ResultType);
    }

    static bool TryBooleanLiteral(Constant literal, out bool value)
    {
        value = false;
        if (literal.Value is int integer && integer is 0 or 1
            && MemberIdentity.IsCoreLibraryType(literal.Type, "System", "Int32"))
        {
            value = integer == 1;
            return true;
        }
        if (literal.Value is bool boolean
            && MemberIdentity.IsCoreLibraryType(literal.Type, "System", "Boolean"))
        {
            value = boolean;
            return true;
        }
        return false;
    }

}
