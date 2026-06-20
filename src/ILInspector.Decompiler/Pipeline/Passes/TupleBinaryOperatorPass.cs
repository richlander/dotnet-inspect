namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's arity-2 tuple-valued <c>==</c>/<c>!=</c> lowering back into the
/// tuple binary operator. The proof is the pair of hidden ValueTuple operand
/// spills feeding ordered <c>Item1</c>/<c>Item2</c> comparisons.
/// </summary>
public sealed class TupleBinaryOperatorPass : IIrPass
{
    public string Name => "tuple-binary-operator";

    public void Run(IrFunction function, PassContext context)
    {
        while (RaiseOnce(function, context.Stepper))
        {
        }
    }

    static bool RaiseOnce(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            for (int i = 2; i < block.Children.Count; i++)
            {
                if (TryRaiseReturnAt(function, block, i, stepper)
                    || i < block.Children.Count - 1 && TryRaiseSlotAt(function, block, i, stepper))
                {
                    return true;
                }
            }
        }
        return false;
    }

    static bool TryRaiseReturnAt(IrFunction function, Block block, int returnIndex, Stepper stepper)
    {
        if (!TryGetOperandSpills(function, block, returnIndex, out var leftStore, out var rightStore, out var tupleType)
            || block.Children[returnIndex] is not Return { Value: LogicalBinary logical }
            || !TryMatchArity2Logical(logical, leftStore.Index, rightStore.Index, tupleType, out bool isEquality))
        {
            return false;
        }

        var tupleBinary = BuildTupleBinary(leftStore, rightStore, tupleType, isEquality, logical);

        stepper.StepOver("raise ValueTuple element comparisons to tuple binary operator", logical);
        logical.ReplaceWith(tupleBinary);
        leftStore.Detach();
        rightStore.Detach();
        return true;
    }

    static bool TryRaiseSlotAt(IrFunction function, Block block, int resultIndex, Stepper stepper)
    {
        if (!TryGetOperandSpills(function, block, resultIndex, out var leftStore, out var rightStore, out var tupleType)
            || block.Children[resultIndex] is not StoreStackSlot { Value: Conditional conditional } resultStore
            || block.Children[resultIndex + 1] is not Return { Value: LoadStackSlot returned }
            || returned.Slot != resultStore.Slot
            || !TryMatchArity2Conditional(conditional, leftStore.Index, rightStore.Index, tupleType, out bool isEquality))
        {
            return false;
        }

        var tupleBinary = BuildTupleBinary(leftStore, rightStore, tupleType, isEquality, conditional);

        stepper.StepOver("raise ValueTuple element comparisons to tuple binary operator", conditional);
        conditional.ReplaceWith(tupleBinary);
        leftStore.Detach();
        rightStore.Detach();
        return true;
    }

    static bool TryGetOperandSpills(
        IrFunction function,
        Block block,
        int consumerIndex,
        out StoreLocal leftStore,
        out StoreLocal rightStore,
        out TypeRef tupleType)
    {
        leftStore = null!;
        rightStore = null!;
        tupleType = null!;
        if (block.Children[consumerIndex - 2] is not StoreLocal left
            || block.Children[consumerIndex - 1] is not StoreLocal right
            || HasSourceLocalName(function, left.Index)
            || HasSourceLocalName(function, right.Index)
            || !left.Type.Equals(right.Type)
            || !MemberIdentity.IsSupportedValueTupleType(left.Type, out var arity)
            || arity != 2)
        {
            return false;
        }

        leftStore = left;
        rightStore = right;
        tupleType = left.Type;
        return true;
    }

    static TupleBinaryExpression BuildTupleBinary(
        StoreLocal leftStore,
        StoreLocal rightStore,
        TypeRef tupleType,
        bool isEquality,
        IrNode source)
    {
        var left = (IrExpression)leftStore.DetachChildren()[0];
        var right = (IrExpression)rightStore.DetachChildren()[0];
        var tupleBinary = new TupleBinaryExpression(isEquality, tupleType, left, right);
        tupleBinary.InheritSourceOffset(source);
        return tupleBinary;
    }

    static bool TryMatchArity2Conditional(
        Conditional conditional,
        int leftLocal,
        int rightLocal,
        TypeRef tupleType,
        out bool isEquality)
    {
        isEquality = false;
        if (!IsItemComparison(conditional.Condition, ComparisonKind.Equal, leftLocal, rightLocal, tupleType, item: 1))
            return false;
        if (conditional.WhenTrue is not Comparison finalComparison)
            return false;
        if (!IsItemComparison(finalComparison, finalComparison.Kind, leftLocal, rightLocal, tupleType, item: 2))
            return false;

        if (finalComparison.Kind == ComparisonKind.Equal
            && conditional.WhenFalse is Constant { Value: false })
        {
            isEquality = true;
            return true;
        }

        return finalComparison.Kind == ComparisonKind.NotEqual
            && conditional.WhenFalse is Constant { Value: true };
    }

    static bool TryMatchArity2Logical(
        LogicalBinary logical,
        int leftLocal,
        int rightLocal,
        TypeRef tupleType,
        out bool isEquality)
    {
        isEquality = false;
        ComparisonKind comparisonKind;
        switch (logical.Kind)
        {
            case LogicalKind.And:
                comparisonKind = ComparisonKind.Equal;
                break;
            case LogicalKind.Or:
                comparisonKind = ComparisonKind.NotEqual;
                break;
            default:
                return false;
        }
        if (!IsItemComparison(logical.Left, comparisonKind, leftLocal, rightLocal, tupleType, item: 1)
            || !IsItemComparison(logical.Right, comparisonKind, leftLocal, rightLocal, tupleType, item: 2))
        {
            return false;
        }

        isEquality = logical.Kind == LogicalKind.And;
        return true;
    }

    static bool IsItemComparison(
        IrExpression expression,
        ComparisonKind kind,
        int leftLocal,
        int rightLocal,
        TypeRef tupleType,
        int item)
        => expression is Comparison comparison
            && !comparison.IsUnsigned
            && comparison.Kind == kind
            && IsTupleItemLoad(comparison.Left, leftLocal, tupleType, item)
            && IsTupleItemLoad(comparison.Right, rightLocal, tupleType, item);

    static bool IsTupleItemLoad(IrExpression expression, int local, TypeRef tupleType, int item)
        => expression is LoadField
        {
            IsVolatile: false,
            Field: { Name: var name, DeclaringType: var declaringType, Type: var fieldType },
            Instance: LoadLocal receiver,
        }
        && receiver.Index == local
        && name == $"Item{item}"
        && declaringType.Equals(tupleType)
        && fieldType.Equals(tupleType.TypeArguments[item - 1]);

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is { } name
            && CSharpNaming.IsUsableIdentifier(name);
}
