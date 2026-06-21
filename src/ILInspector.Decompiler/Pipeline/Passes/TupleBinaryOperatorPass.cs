using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's tuple-valued <c>==</c>/<c>!=</c> lowering back into the tuple
/// binary operator. Three shapes are recovered:
/// <list type="bullet">
/// <item>The <em>whole-tuple</em> form (<c>left == right</c>, both operands
/// <c>ValueTuple</c> locals): the proof is the pair of hidden ValueTuple operand
/// spills feeding ordered <c>ItemN</c> comparisons.</item>
/// <item>The <em>element-literal</em> form (<c>(a, b) == (c, d)</c>, any arity):
/// the proof is csc's <em>eager</em> operand evaluation — every element except
/// the first is spilled to an unnamed temporary <em>before</em> the first test,
/// then compared element-wise.</item>
/// <item>The <em>mixed</em> form (<c>(a, b) == pair</c>): one operand a literal,
/// the other a tuple variable accessed via <c>Item1</c>/<c>Item2</c> loads on its
/// own hidden spill.</item>
/// </list>
/// Hand-written <c>a == c &amp;&amp; b == d</c> evaluates lazily and leaves no such
/// spill prologue, so the eager spills are a reliable signature that does not
/// collide with ordinary short-circuit code.
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
                    || TryRaiseOperandComparisonAt(function, block, i, stepper))
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
            || !TryMatchLogical(logical, leftStore.Index, rightStore.Index, tupleType, out bool isEquality))
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
            || !MemberIdentity.IsSupportedValueTupleType(tupleType, out var arity)
            || arity != 2
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
            || !MemberIdentity.IsSupportedValueTupleType(left.Type, out _))
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

    static bool TryMatchLogical(
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
        if (!MemberIdentity.IsSupportedValueTupleType(tupleType, out var arity))
            return false;

        var comparisons = new List<Comparison>();
        if (!TryFlatten(logical, logical.Kind, comparisonKind, comparisons)
            || comparisons.Count != arity)
        {
            return false;
        }

        for (int i = 0; i < comparisons.Count; i++)
            if (!IsItemComparison(comparisons[i], comparisonKind, leftLocal, rightLocal, tupleType, i + 1))
                return false;

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

    // ---- Operand-comparison form: `(a, b) == (c, d)`, `(a, b) == pair`, etc. ----
    //
    // Each side of the comparison chain is independently a tuple literal (element
    // spills + inline operands) or a tuple variable (Item1/Item2 loads on a shared
    // unnamed ValueTuple spill). This subsumes the element-literal form and the
    // mixed literal-vs-variable form; the whole-tuple Logical form is caught first
    // by TryRaiseReturnAt above.

    static bool TryRaiseOperandComparisonAt(IrFunction function, Block block, int consumerIndex, Stepper stepper)
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

        // csc evaluates every tuple operand eagerly into the spill prologue before
        // the first element comparison, so the LAST element's operands are spilled
        // even though they are compared last. A hand-written `&& x == y` appended to
        // a tuple comparison (`(a, b) == (c, d) && e == f`) is evaluated lazily
        // after the tuple's short-circuit branch, so both its operands are fresh
        // inline loads, never prologue spills. The flattened chain therefore ends in
        // a comparison with no spill backing. Require the last comparison to read at
        // least one operand from the prologue; otherwise the chain ends in an
        // appended comparison, and collapsing it would invent an extra tuple element
        // and drop the `&&`'s short-circuit (evaluating e/f — including their side
        // effects — unconditionally). A genuine inline element (a const or the single
        // first-evaluated operand) only ever sits opposite a spilled operand, so this
        // never rejects a real tuple literal.
        var lastComparison = comparisons[^1];
        if (!ConsumesSpill(lastComparison.Left, spills) && !ConsumesSpill(lastComparison.Right, spills))
            return false;

        if (!TryClassifySide(comparisons, comparison => comparison.Left, spills, out var leftPlan)
            || !TryClassifySide(comparisons, comparison => comparison.Right, spills, out var rightPlan))
        {
            return false;
        }

        // A tuple-variable operand cannot supply both sides — its single stored
        // value would be detached twice.
        if (leftPlan.VariableSpill >= 0 && leftPlan.VariableSpill == rightPlan.VariableSpill)
            return false;

        // Clean shape: every spilled temp is consumed, an element spill exactly
        // once (a variable spill is consumed once per Item load, validated above).
        var total = new Dictionary<int, int>(leftPlan.Consumed);
        foreach (var (index, count) in rightPlan.Consumed)
            total[index] = total.GetValueOrDefault(index) + count;
        if (total.Count != spills.Count)
            return false;
        var variableSpills = new HashSet<int>();
        if (leftPlan.VariableSpill >= 0)
            variableSpills.Add(leftPlan.VariableSpill);
        if (rightPlan.VariableSpill >= 0)
            variableSpills.Add(rightPlan.VariableSpill);
        foreach (var (index, count) in total)
            if (!variableSpills.Contains(index) && count != 1)
                return false;

        var left = Materialize(leftPlan);
        var right = Materialize(rightPlan);
        var tupleBinary = new TupleBinaryExpression(isEquality, leftPlan.TupleType, left, right);
        tupleBinary.InheritSourceOffset(logical);

        stepper.StepOver("raise tuple operand comparisons to tuple binary operator", logical);
        logical.ReplaceWith(tupleBinary);
        foreach (var spill in spills.Values)
            spill.Detach();
        return true;
    }

    // An operand is backed by the eager spill prologue when it is an element spill
    // load (a literal element) or an Item load on a spilled tuple variable. A fresh
    // inline load (parameter, field, call) is not — it marks lazily evaluated code.
    static bool ConsumesSpill(IrExpression operand, Dictionary<int, StoreLocal> spills)
        => operand switch
        {
            LoadLocal load => spills.ContainsKey(load.Index),
            LoadField { Instance: LoadLocal receiver } => spills.ContainsKey(receiver.Index),
            _ => false,
        };

    /// <summary>A recovered tuple operand: a literal (rebuild the elements) or a variable (the stored tuple).</summary>
    sealed class SidePlan
    {
        public required TypeRef TupleType { get; init; }
        public IrExpression? Variable { get; init; }
        public List<IrExpression>? Elements { get; init; }
        public int VariableSpill { get; init; } = -1;
        public required Dictionary<int, int> Consumed { get; init; }
    }

    static bool TryClassifySide(List<Comparison> comparisons, Func<Comparison, IrExpression> select, Dictionary<int, StoreLocal> spills, out SidePlan plan)
        => TryClassifyVariableSide(comparisons, select, spills, out plan)
            || TryClassifyLiteralSide(comparisons, select, spills, out plan);

    // A tuple variable: every comparison's operand on this side is an Item load on
    // the same unnamed ValueTuple spill, in element order.
    static bool TryClassifyVariableSide(List<Comparison> comparisons, Func<Comparison, IrExpression> select, Dictionary<int, StoreLocal> spills, out SidePlan plan)
    {
        plan = null!;
        if (select(comparisons[0]) is not LoadField { Instance: LoadLocal receiver }
            || !spills.TryGetValue(receiver.Index, out var store)
            || !MemberIdentity.IsSupportedValueTupleType(store.Type, out var arity)
            || arity != comparisons.Count)
        {
            return false;
        }

        for (int i = 0; i < comparisons.Count; i++)
            if (!IsTupleItemLoad(select(comparisons[i]), receiver.Index, store.Type, i + 1))
                return false;

        plan = new SidePlan
        {
            TupleType = store.Type,
            Variable = store.Value,
            VariableSpill = receiver.Index,
            Consumed = new Dictionary<int, int> { [receiver.Index] = comparisons.Count },
        };
        return true;
    }

    // A tuple literal: each operand is an element — an inline expression or a
    // single-use spilled temp that inlines back into the literal.
    static bool TryClassifyLiteralSide(List<Comparison> comparisons, Func<Comparison, IrExpression> select, Dictionary<int, StoreLocal> spills, out SidePlan plan)
    {
        plan = null!;
        var elements = new List<IrExpression>(comparisons.Count);
        var consumed = new Dictionary<int, int>();
        foreach (var comparison in comparisons)
        {
            var operand = select(comparison);
            if (operand is LoadLocal load && spills.TryGetValue(load.Index, out var store))
            {
                consumed[load.Index] = consumed.GetValueOrDefault(load.Index) + 1;
                elements.Add(store.Value);
            }
            else
            {
                elements.Add(operand);
            }
        }

        var types = LiteralElementTypes(elements);
        if (types is null)
            return false;

        plan = new SidePlan
        {
            TupleType = MakeTupleType(types.Value),
            Elements = elements,
            Consumed = consumed,
        };
        return true;
    }

    static IrExpression Materialize(SidePlan plan)
    {
        if (plan.Variable is { } variable)
        {
            variable.Detach();
            return variable;
        }

        foreach (var element in plan.Elements!)
            element.Detach();
        return new TupleExpression(plan.TupleType, plan.Elements!);
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

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is { } name
            && CSharpNaming.IsUsableIdentifier(name);
}
