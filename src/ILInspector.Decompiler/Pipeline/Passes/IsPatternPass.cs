namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's type-pattern lowering into an <see cref="IsPattern"/>
/// expression — <c>value is T t</c>. The proven shape, after structuring and
/// boolean folding:
/// <code>
///   T t = value as T;          // StoreLocal t = IsInstance T(value)
///   if (t != null) { ...t... } // statement guard
///   // or, as a short-circuit expression:
///   ... t != null &amp;&amp; ...t... // LogicalAnd(LoadLocal t, rest)
/// </code>
/// The null test on <c>t</c> becomes <c>value is T t</c>, binding the pattern
/// variable for the guarded scope; the separate <c>as</c> store is dropped.
///
/// <para>Left flat the construct renders as a valid-but-inferior
/// <c>T t = value as T; if (t is not null) { ... }</c> — the owed
/// <c>is</c>-pattern idiom. Transactional: the pattern local must be referenced
/// only by the <c>as</c> store, the null test being replaced, and the scope the
/// test gates (the <c>if</c>-body or the <c>&amp;&amp;</c> right operand), and
/// the tested value must be side-effect-free so moving it into the pattern
/// cannot reorder observable effects. Otherwise the construct is left flat.</para>
///
/// <para>A property pattern <c>value is T { P: k }</c> lowers to the same
/// <c>as</c> store followed by <c>t != null &amp;&amp; t.P == k</c>; this pass
/// recovers it as <c>value is T t &amp;&amp; t.P == k</c>, and the printer folds
/// constant/relational comparisons to property sub-pattern text. A recursive
/// declaration sub-pattern <c>value is { P: T t }</c> lowers as a null guard,
/// property <c>as</c> store, and scoped null test; this pass raises the narrow
/// single-property captured-binding shape when the bound local does not
/// escape.</para>
/// </summary>
public sealed class IsPatternPass : IIrPass
{
    public string Name => "is-pattern";

    sealed record TestSite(IrExpression TestNode, IrNode TrueScope);
    sealed record RecursivePropertyDeclarationMatch(
        IfStatement NullGuard,
        IfStatement Consumer,
        StoreLocal PatternStore,
        StoreStackSlot TrueStore,
        StoreStackSlot FalseStore,
        IsInstance AsCast,
        LoadProperty Property);
    sealed record NestedRecursivePropertyDeclarationMatch(
        IfStatement NullGuard,
        IfStatement InnerGuard,
        StoreLocal PatternStore,
        IsInstance AsCast,
        LoadProperty Property);

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper)
            || TransformRecursivePropertyDeclaration(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal { Value: IsInstance asCast } store)
                    continue;

                // The tested value is inlined into the pattern, so it must not
                // depend on the pattern local and must be reorder-safe.
                if (ReferencesLocal(asCast.Operand, store.Index) || !IsSideEffectFree(asCast.Operand))
                    continue;

                if (FindTest(children[i + 1], store.Index) is not { } site)
                    continue;

                // Soundness: the pattern local must be referenced ONLY by the
                // as-cast store, the null test being replaced, and the scope that
                // test gates. A reference before the store or outside the gated
                // region means binding it inside the pattern would change which
                // paths see it definitely assigned — leave it flat.
                if (!ReferencedOnlyWithin(function, store.Index, [store, site.TestNode, site.TrueScope]))
                    continue;

                var value = (IrExpression)asCast.DetachChildren()[0];
                var pattern = new IsPattern(value, asCast.Type, store.Index);
                stepper.StepOver("raise as/null-test to is pattern", site.TestNode);
                site.TestNode.ReplaceWith(pattern);
                store.Detach();
                return true;
            }
        }
        return false;
    }

    static bool TransformRecursivePropertyDeclaration(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (i + 1 < children.Count
                    && children[i] is IfStatement slotNullGuard
                    && children[i + 1] is IfStatement consumer
                    && MatchRecursivePropertyDeclaration(function, slotNullGuard, consumer) is { } slotMatch)
                {
                    RaiseSlotRecursivePropertyDeclaration(slotMatch, stepper);
                    return true;
                }

                if (children[i] is IfStatement nestedNullGuard
                    && MatchNestedRecursivePropertyDeclaration(function, nestedNullGuard) is { } nestedMatch)
                {
                    RaiseNestedRecursivePropertyDeclaration(nestedMatch, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    static void RaiseSlotRecursivePropertyDeclaration(RecursivePropertyDeclarationMatch match, Stepper stepper)
    {
        var value = match.NullGuard.Condition;
        value.Detach();
        var pattern = new RecursivePropertyDeclarationPattern(
            value,
            match.Property.Accessor,
            match.AsCast.Type,
            match.PatternStore.Index);
        pattern.InheritSourceOffset(match.Consumer.Condition);

        stepper.StepOver("raise recursive property declaration pattern", match.NullGuard);
        match.Consumer.Condition.ReplaceWith(pattern);
        match.NullGuard.Detach();
    }

    static void RaiseNestedRecursivePropertyDeclaration(NestedRecursivePropertyDeclarationMatch match, Stepper stepper)
    {
        var value = match.NullGuard.Condition;
        value.Detach();
        var body = match.InnerGuard.Then;
        body.Detach();
        var pattern = new RecursivePropertyDeclarationPattern(
            value,
            match.Property.Accessor,
            match.AsCast.Type,
            match.PatternStore.Index);
        pattern.InheritSourceOffset(match.InnerGuard.Condition);

        stepper.StepOver("raise nested recursive property declaration pattern", match.NullGuard);
        match.NullGuard.ReplaceWith(new IfStatement(pattern, body, elseArm: null));
    }

    static RecursivePropertyDeclarationMatch? MatchRecursivePropertyDeclaration(IrFunction function, IfStatement nullGuard, IfStatement consumer)
    {
        if (!nullGuard.HasElse
            || nullGuard.Then.Children is not [StoreLocal { Value: IsInstance { Operand: LoadProperty property } asCast } store, StoreStackSlot trueStore]
            || nullGuard.Else!.Children is not [StoreStackSlot falseStore]
            || trueStore.Slot != falseStore.Slot)
        {
            return null;
        }

        if (!property.HasInstance
            || property.IndexArguments.Count != 0
            || !PlaceIdentity.SameVariable(nullGuard.Condition, property.Instance))
        {
            return null;
        }

        if (!IsPatternLocalNotNull(trueStore.Value, store.Index) || !IsFalseConstant(falseStore.Value))
            return null;

        if (consumer.Condition is not LoadStackSlot load || load.Slot != trueStore.Slot)
            return null;

        if (!ReferencedOnlyWithin(function, store.Index, [store, trueStore.Value, consumer.Then])
            || !StackSlotReferencedOnlyWithin(function, trueStore.Slot, [trueStore, falseStore, consumer.Condition]))
        {
            return null;
        }

        return new RecursivePropertyDeclarationMatch(nullGuard, consumer, store, trueStore, falseStore, asCast, property);
    }

    static NestedRecursivePropertyDeclarationMatch? MatchNestedRecursivePropertyDeclaration(IrFunction function, IfStatement nullGuard)
    {
        if (nullGuard.HasElse
            || nullGuard.Then.Children is not [StoreLocal { Value: IsInstance { Operand: LoadProperty property } asCast } store, IfStatement innerGuard])
        {
            return null;
        }

        if (!property.HasInstance
            || property.IndexArguments.Count != 0
            || innerGuard.HasElse
            || !PlaceIdentity.SameVariable(nullGuard.Condition, property.Instance)
            || !IsPatternLocalNotNull(innerGuard.Condition, store.Index)
            || !ReferencedOnlyWithin(function, store.Index, [store, innerGuard.Condition, innerGuard.Then]))
        {
            return null;
        }

        return new NestedRecursivePropertyDeclarationMatch(nullGuard, innerGuard, store, asCast, property);
    }

    static bool IsPatternLocalNotNull(IrExpression expression, int localIndex) => expression switch
    {
        LoadLocal load => load.Index == localIndex,
        Comparison { Kind: ComparisonKind.NotEqual, Left: LoadLocal load, Right: Constant { Value: null } }
            => load.Index == localIndex,
        Comparison { Kind: ComparisonKind.NotEqual, Left: Constant { Value: null }, Right: LoadLocal load }
            => load.Index == localIndex,
        Comparison { Kind: ComparisonKind.GreaterThan, IsUnsigned: true, Left: LoadLocal load, Right: Constant { Value: null } }
            => load.Index == localIndex,
        _ => false,
    };

    static bool IsFalseConstant(IrExpression expression)
        => expression is Constant { Value: false } or Constant { Value: 0 };

    static bool StackSlotReferencedOnlyWithin(IrFunction function, int slot, IrNode[] allowed)
    {
        foreach (var node in function.Descendants)
        {
            bool references = node switch
            {
                LoadStackSlot load => load.Slot == slot,
                StoreStackSlot store => store.Slot == slot,
                _ => false,
            };
            if (references && !allowed.Any(root => IsInside(node, root)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Locates the null test on the pattern local in the statement that follows
    /// the <c>as</c> store, plus the scope that test gates.
    /// </summary>
    static TestSite? FindTest(IrNode next, int index)
    {
        // Short-circuit form: `t != null && rest` — the leading conjunct of the
        // whole && chain is the bare truthy load of the pattern local; the full
        // chain is the scope entered only when the pattern matched.
        foreach (var logical in next.Descendants.OfType<LogicalBinary>())
        {
            if (logical.Kind == LogicalKind.And
                && LeftmostConjunct(logical) is LoadLocal andLoad
                && andLoad.Index == index)
            {
                return new TestSite(andLoad, logical);
            }
        }

        // Statement-guard form: `if (t != null) { ...t... }` — the whole
        // condition is the bare truthy load; the then-arm is the gated scope.
        if (next is IfStatement { Condition: LoadLocal guardLoad } guard && guardLoad.Index == index && guard.Else is null)
            return new TestSite(guard.Condition, guard.Then);

        return null;
    }

    static IrExpression LeftmostConjunct(LogicalBinary logical)
    {
        var current = logical.Left;
        while (current is LogicalBinary { Kind: LogicalKind.And } nested)
            current = nested.Left;
        return current;
    }

    /// <summary>
    /// An expression with no observable side effects: loads of locals,
    /// arguments, fields, and constants, plus reference conversions over them.
    /// Excludes calls (including property getters), allocations, and increments.
    /// </summary>
    static bool IsSideEffectFree(IrExpression expr) => expr switch
    {
        Constant or LoadLocal or LoadArgument or LoadLocalAddress or LoadArgumentAddress => true,
        LoadField field => field.Instance is null || IsSideEffectFree(field.Instance),
        LoadFieldAddress fieldAddress => fieldAddress.Instance is null || IsSideEffectFree(fieldAddress.Instance),
        Convert convert => IsSideEffectFree(convert.Operand),
        CastClass cast => IsSideEffectFree(cast.Operand),
        IsInstance isInstance => IsSideEffectFree(isInstance.Operand),
        _ => false,
    };

    static bool ReferencedOnlyWithin(IrFunction function, int index, IrNode[] allowed)
    {
        foreach (var node in function.Descendants)
        {
            bool references = node switch
            {
                LoadLocal load => load.Index == index,
                StoreLocal store => store.Index == index,
                LoadLocalAddress address => address.Index == index,
                _ => false,
            };
            if (references && !allowed.Any(root => IsInside(node, root)))
                return false;
        }
        return true;
    }

    static bool ReferencesLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => node switch
        {
            LoadLocal load => load.Index == index,
            StoreLocal store => store.Index == index,
            LoadLocalAddress address => address.Index == index,
            _ => false,
        });

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
