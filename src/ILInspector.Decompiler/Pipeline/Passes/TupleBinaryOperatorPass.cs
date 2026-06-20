using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's arity-2 tuple-valued <c>==</c>/<c>!=</c> lowering back into the
/// tuple binary operator. Two shapes are recovered:
/// <list type="bullet">
/// <item>The <em>whole-tuple</em> form (<c>left == right</c>, both operands
/// <c>ValueTuple</c> locals): the proof is the pair of hidden ValueTuple operand
/// spills feeding ordered <c>Item1</c>/<c>Item2</c> comparisons.</item>
/// <item>The <em>element-literal</em> form (<c>(a, b) == (c, d)</c>, any arity):
/// the proof is csc's <em>eager</em> operand evaluation — every element except
/// the first is spilled to an unnamed temporary <em>before</em> the first test,
/// then compared element-wise. Hand-written <c>a == c &amp;&amp; b == d</c>
/// evaluates lazily and leaves no such spill prologue, so the eager spills are a
/// reliable signature that does not collide with ordinary short-circuit code.</item>
/// </list>
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
            for (int i = 1; i < block.Children.Count; i++)
            {
                if (i >= 2 && (TryRaiseReturnAt(function, block, i, stepper)
                        || i < block.Children.Count - 1 && TryRaiseSlotAt(function, block, i, stepper))
                    || TryRaiseLiteralAt(function, block, i, stepper))
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

    // ---- Element-literal form: `(a, b) == (c, d)` of any arity. ----

    static bool TryRaiseLiteralAt(IrFunction function, Block block, int consumerIndex, Stepper stepper)
    {
        if (GetLiteralLogical(block.Children[consumerIndex]) is not { } logical
            || !TryGetLiteralKind(logical.Kind, out var comparisonKind, out bool isEquality))
        {
            return false;
        }

        // Flatten the left-nested logical chain into element comparisons.
        var comparisons = new List<Comparison>();
        if (!TryFlatten(logical, logical.Kind, comparisonKind, comparisons) || comparisons.Count < 2)
            return false;

        // Collect the eager spill prologue: consecutive unnamed StoreLocals
        // immediately preceding the consumer.
        var spills = new Dictionary<int, StoreLocal>();
        for (int j = consumerIndex - 1; j >= 0; j--)
        {
            if (block.Children[j] is not StoreLocal store
                || HasSourceLocalName(function, store.Index)
                || !spills.TryAdd(store.Index, store))
            {
                break;
            }
        }
        if (spills.Count == 0)
            return false;

        // Resolve every comparison operand: a load of a spilled temp inlines the
        // stored element; anything else stays as-is. Track spill consumption.
        var leftRefs = new List<IrExpression>(comparisons.Count);
        var rightRefs = new List<IrExpression>(comparisons.Count);
        var used = new Dictionary<int, int>();
        foreach (var comparison in comparisons)
        {
            leftRefs.Add(ResolveLiteralOperand(comparison.Left, spills, used));
            rightRefs.Add(ResolveLiteralOperand(comparison.Right, spills, used));
        }

        // Clean shape: every spilled temp consumed exactly once. A dangling or
        // multiply-used spill means this is not a tuple literal comparison.
        if (used.Count != spills.Count || used.Values.Any(count => count != 1))
            return false;

        // Element result types must be known to synthesize the ValueTuple type.
        var leftTypes = LiteralElementTypes(leftRefs);
        var rightTypes = LiteralElementTypes(rightRefs);
        if (leftTypes is null || rightTypes is null)
            return false;

        var leftTuple = new TupleExpression(MakeTupleType(leftTypes.Value), DetachAll(leftRefs));
        var rightTuple = new TupleExpression(MakeTupleType(rightTypes.Value), DetachAll(rightRefs));
        var tupleBinary = new TupleBinaryExpression(isEquality, leftTuple.TupleType, leftTuple, rightTuple);
        tupleBinary.InheritSourceOffset(logical);

        stepper.StepOver("raise element-literal tuple comparison to tuple binary operator", logical);
        logical.ReplaceWith(tupleBinary);
        foreach (var spill in spills.Values)
            spill.Detach();
        return true;
    }

    static LogicalBinary? GetLiteralLogical(IrNode statement) => statement switch
    {
        Return { Value: LogicalBinary logical } => logical,
        StoreStackSlot { Value: LogicalBinary logical } => logical,
        StoreLocal { Value: LogicalBinary logical } => logical,
        _ => null,
    };

    static bool TryGetLiteralKind(LogicalKind kind, out ComparisonKind comparisonKind, out bool isEquality)
    {
        switch (kind)
        {
            case LogicalKind.And:
                comparisonKind = ComparisonKind.Equal;
                isEquality = true;
                return true;
            case LogicalKind.Or:
                comparisonKind = ComparisonKind.NotEqual;
                isEquality = false;
                return true;
            default:
                comparisonKind = default;
                isEquality = false;
                return false;
        }
    }

    // A `(a, b, c) == ...` lowering is a left-nested chain of the same logical
    // kind whose leaves are element comparisons, in tuple element order.
    static bool TryFlatten(IrExpression node, LogicalKind kind, ComparisonKind comparisonKind, List<Comparison> comparisons)
    {
        if (node is LogicalBinary logical)
        {
            return logical.Kind == kind
                && TryFlatten(logical.Left, kind, comparisonKind, comparisons)
                && TryFlatten(logical.Right, kind, comparisonKind, comparisons);
        }

        if (node is Comparison comparison && comparison.Kind == comparisonKind && !comparison.IsUnsigned)
        {
            comparisons.Add(comparison);
            return true;
        }

        return false;
    }

    static IrExpression ResolveLiteralOperand(IrExpression operand, Dictionary<int, StoreLocal> spills, Dictionary<int, int> used)
    {
        if (operand is LoadLocal load && spills.TryGetValue(load.Index, out var store))
        {
            used[load.Index] = used.GetValueOrDefault(load.Index) + 1;
            return store.Value;
        }
        return operand;
    }

    static ImmutableArray<TypeRef>? LiteralElementTypes(List<IrExpression> elements)
    {
        var builder = ImmutableArray.CreateBuilder<TypeRef>(elements.Count);
        foreach (var element in elements)
        {
            if (element.ResultType is not { } type)
                return null;
            builder.Add(type);
        }
        return builder.MoveToImmutable();
    }

    static TypeRef MakeTupleType(ImmutableArray<TypeRef> elementTypes)
        => TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple"), elementTypes);

    static List<IrExpression> DetachAll(List<IrExpression> refs)
    {
        foreach (var node in refs)
            node.Detach();
        return refs;
    }

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is { } name
            && CSharpNaming.IsUsableIdentifier(name);
}
