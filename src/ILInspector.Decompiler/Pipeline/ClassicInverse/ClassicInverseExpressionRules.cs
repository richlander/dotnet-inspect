namespace ILInspector.Decompiler.Pipeline;

/// <summary>Closed representation changes shared by value and coalescing correspondence.</summary>
internal static class ClassicInverseExpressionRules
{
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

    internal static bool IsRetypedBooleanArgument(Constant raw, Constant planning)
    {
        if (raw.Value is not int value || value is not (0 or 1)
            || planning.Value is not bool boolean || boolean != (value == 1)
            || !MemberIdentity.IsCoreLibraryType(raw.Type, "System", "Int32")
            || !MemberIdentity.IsCoreLibraryType(planning.Type, "System", "Boolean")
            || raw.Parent is not Call call)
        {
            return false;
        }

        int parameter = raw.ChildIndex - (call.Callee.HasThis ? 1 : 0);
        return parameter >= 0 && parameter < call.Callee.ParameterTypes.Length
            && MemberIdentity.IsCoreLibraryType(
                call.Callee.ParameterTypes[parameter], "System", "Boolean");
    }
}
