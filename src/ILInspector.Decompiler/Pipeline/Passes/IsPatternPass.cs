namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's type-pattern lowering into an <see cref="IsPattern"/>
/// expression — <c>value is T t</c>. The proven shape, after structuring and
/// boolean folding:
/// <code>
///   T t = value as T;          // StoreLocal t = IsInstance T(value)
///   if (t != null) { ...t... } // statement guard, optional else that cannot use t
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
/// test gates (the <c>if</c>-body or the <c>&amp;&amp;</c> right operand — not an
/// <c>else</c> arm), and
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
/// escape. A narrow positional pattern slice recognizes the structured
/// null/deconstruct/equality/relational-return shape for
/// <c>value is ("ok", &gt; 0)</c>, gated on compiler-generated deconstruction
/// temps so source-authored <c>Deconstruct(out name, out count)</c> guards stay
/// visible.</para>
/// </summary>
public sealed class IsPatternPass : IIrPass
{
    public string Name => "is-pattern";

    sealed record TestSite(IrExpression TestNode, IrNode TrueScope, bool PreserveLocalInPropertyPattern = false);
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
            || FoldConditionalReturnOne(function, context.Stepper)
            || TransformRecursivePropertyDeclaration(function, context.Stepper)
            || FoldPositionalPatternReturnOne(function, context.Stepper))
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
                if (ReferenceOwnership.SubtreeReferencesLocal(asCast.Operand, store.Index) || !IsSideEffectFree(function, asCast.Operand))
                    continue;

                bool allowStatementConjunction = asCast.Operand is LoadProperty property && IsUnionValueProperty(function, property);
                if (FindTest(children[i + 1], store.Index, allowStatementConjunction) is not { } site)
                    continue;

                // Soundness: the pattern local must be referenced ONLY by the
                // as-cast store, the null test being replaced, and the scope that
                // test gates. A reference before the store or outside the gated
                // region means binding it inside the pattern would change which
                // paths see it definitely assigned — leave it flat.
                if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, site.TestNode, site.TrueScope]))
                    continue;

                var value = (IrExpression)asCast.DetachChildren()[0];
                var pattern = new IsPattern(value, asCast.Type, store.Index)
                {
                    PreserveLocalInPropertyPattern = site.PreserveLocalInPropertyPattern
                };
                stepper.StepOver("raise as/null-test to is pattern", site.TestNode);
                site.TestNode.ReplaceWith(pattern);
                store.Detach();
                return true;
            }
        }
        return false;
    }

    static bool FoldConditionalReturnOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 2 < children.Count; i++)
            {
                if (children[i] is not StoreLocal { Value: IsInstance asCast } store
                    || children[i + 1] is not IfStatement guard
                    || children[i + 2] is not Return { Value: { } whenTrue }
                    || !TryUnionConditionalGuard(function, asCast, store.Index, guard, out var condition, out var whenFalse))
                {
                    continue;
                }

                if (ReferenceOwnership.SubtreeReferencesLocal(asCast.Operand, store.Index)
                    || !IsSideEffectFree(function, asCast.Operand)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, guard.Condition, guard, whenTrue]))
                {
                    continue;
                }

                var conditional = new Conditional(condition, (IrExpression)whenTrue.Clone(), (IrExpression)whenFalse.Clone())
                {
                    MergedType = whenTrue.ResultType ?? whenFalse.ResultType
                };
                stepper.StepOver("raise union pattern return chain to conditional", guard);
                children[i + 2].ReplaceWith(new Return(conditional));
                guard.Detach();
                store.Detach();
                return true;
            }
        }

        return false;
    }

    static bool TryUnionConditionalGuard(
        IrFunction function,
        IsInstance asCast,
        int localIndex,
        IfStatement guard,
        out IrExpression condition,
        out IrExpression whenFalse)
    {
        condition = null!;
        whenFalse = null!;
        if (guard.HasElse
            || guard.Then.Children is not [Return { Value: { } fallback }]
            || asCast.Operand is not LoadProperty property
            || !IsUnionValueProperty(function, property))
        {
            return false;
        }

        if (IsPatternLocalNull(guard.Condition, localIndex))
        {
            condition = new IsPattern((IrExpression)asCast.Operand.Clone(), asCast.Type, localIndex);
            whenFalse = fallback;
            return true;
        }

        if (guard.Condition is LogicalBinary { Kind: LogicalKind.Or } logical
            && IsPatternLocalNull(logical.Left, localIndex))
        {
            var pattern = new IsPattern((IrExpression)asCast.Operand.Clone(), asCast.Type, localIndex)
            {
                PreserveLocalInPropertyPattern = true
            };
            condition = new LogicalBinary(LogicalKind.And, pattern, Conditions.Negate((IrExpression)logical.Right.Clone()));
            whenFalse = fallback;
            return true;
        }

        return false;
    }

    static bool IsPatternLocalNull(IrExpression expression, int localIndex) => expression switch
    {
        LogicalNot { Operand: LoadLocal load } => load.Index == localIndex,
        Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal load, Right: Constant { Value: null } }
            => load.Index == localIndex,
        Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: null }, Right: LoadLocal load }
            => load.Index == localIndex,
        _ => false,
    };

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

        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, trueStore.Value, consumer.Then])
            || !ReferenceOwnership.StackSlotReferencesOnlyWithin(function, trueStore.Slot, [trueStore, falseStore, consumer.Condition]))
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
            || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, innerGuard.Condition, innerGuard.Then]))
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

    static bool FoldPositionalPatternReturnOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (i + 2 != children.Count
                    || children[i] is not IfStatement guard
                    || children[i + 1] is not Return { Value: Constant { Value: false } } falseReturn
                    || !TryBuildPositionalPattern(function, guard, out var pattern))
                {
                    continue;
                }

                stepper.StepOver("raise deconstruct guard to positional pattern", guard);
                guard.ReplaceWith(new Return(pattern));
                falseReturn.Detach();
                return true;
            }
        }
        return false;
    }

    static bool TryBuildPositionalPattern(IrFunction function, IfStatement guard, out PositionalPattern pattern)
    {
        pattern = null!;
        if (guard.Else is not null
            || !TryNullGuardValue(guard.Condition, out var value)
            || guard.Then.Children is not [DeconstructionAssignment deconstruction, IfStatement inner]
            || deconstruction.LocalIndices.Length != 2
            || deconstruction.IsDeclared.Any(declared => !declared)
            || deconstruction.LocalIndices.Any(index => HasSourceLocalName(function, index))
            || !PlaceIdentity.SameVariable(deconstruction.Source, value)
            || inner.Else is not null
            || inner.Then.Children.Count != 1
            || inner.Then.Children[0] is not Return { Value: { } returnValue }
            || !TryEqualitySubpattern(inner.Condition, deconstruction.LocalIndices[0], out var firstConstant)
            || !TryRelationalSubpattern(returnValue, deconstruction.LocalIndices[1], out var secondSubpattern, out var secondConstant)
            || !ReferenceOwnership.LocalReferencesOnlyWithin(function, deconstruction.LocalIndices[0], [deconstruction, inner.Condition])
            || !ReferenceOwnership.LocalReferencesOnlyWithin(function, deconstruction.LocalIndices[1], [deconstruction, returnValue]))
        {
            return false;
        }

        pattern = new PositionalPattern(
            (IrExpression)value.Clone(),
            [new PositionalPatternSubpattern(ComparisonKind.Equal), secondSubpattern],
            [(Constant)firstConstant.Clone(), (Constant)secondConstant.Clone()]);
        return true;
    }

    static bool TryNullGuardValue(IrExpression condition, out IrExpression value)
    {
        value = null!;
        if (condition is LoadArgument or LoadLocal)
        {
            value = condition;
            return true;
        }
        return false;
    }

    static bool TryEqualitySubpattern(IrExpression expression, int localIndex, out Constant constant)
    {
        if (expression is Call { Arguments: var args } call
            && MemberIdentity.IsStringEquality(call)
            && args.Count == 2
            && TryLocalConstant(args[0], args[1], localIndex, out constant))
        {
            return true;
        }

        if (expression is Comparison { Kind: ComparisonKind.Equal, IsUnsigned: false } comparison
            && TryLocalConstant(comparison.Left, comparison.Right, localIndex, out constant))
        {
            return true;
        }

        constant = null!;
        return false;
    }

    static bool TryRelationalSubpattern(
        IrExpression expression,
        int localIndex,
        out PositionalPatternSubpattern subpattern,
        out Constant constant)
    {
        subpattern = default;
        constant = null!;
        if (expression is not Comparison comparison)
            return false;

        ComparisonKind kind;
        if (comparison.Left is LoadLocal left && left.Index == localIndex && comparison.Right is Constant rightConstant)
        {
            kind = comparison.Kind;
            constant = rightConstant;
        }
        else if (comparison.Right is LoadLocal right && right.Index == localIndex && comparison.Left is Constant leftConstant)
        {
            kind = Conditions.Mirror(comparison.Kind);
            constant = leftConstant;
        }
        else
        {
            return false;
        }

        if (kind is not (ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual)
            || comparison.IsUnsigned
            || TypeFamilies.IsFloat(comparison.Left.ResultType)
            || TypeFamilies.IsFloat(comparison.Right.ResultType))
        {
            return false;
        }

        subpattern = new PositionalPatternSubpattern(kind);
        return true;
    }

    static bool TryLocalConstant(IrExpression left, IrExpression right, int localIndex, out Constant constant)
    {
        if (left is LoadLocal leftLocal && leftLocal.Index == localIndex && right is Constant rightConstant)
        {
            constant = rightConstant;
            return true;
        }

        if (right is LoadLocal rightLocal && rightLocal.Index == localIndex && left is Constant leftConstant)
        {
            constant = leftConstant;
            return true;
        }

        constant = null!;
        return false;
    }

    /// <summary>
    /// Locates the null test on the pattern local in the statement that follows
    /// the <c>as</c> store, plus the scope that test gates.
    /// </summary>
    static TestSite? FindTest(IrNode next, int index, bool allowStatementConjunction)
    {
        if (allowStatementConjunction
            && next is IfStatement { HasElse: false, Condition: LogicalBinary { Kind: LogicalKind.And } condition } andGuard
            && IsPatternLocalNotNull(LeftmostConjunct(condition), index))
        {
            return new TestSite(LeftmostConjunct(condition), andGuard, PreserveLocalInPropertyPattern: true);
        }

        // Short-circuit form: `t != null && rest` — the leading conjunct of the
        // whole && chain is the bare truthy load of the pattern local; the full
        // chain is the scope entered only when the pattern matched.
        foreach (var logical in next.Descendants.OfType<LogicalBinary>())
        {
            if (logical.Kind == LogicalKind.And)
            {
                var leftmost = LeftmostConjunct(logical);
                if (IsPatternLocalNotNull(leftmost, index))
                    return new TestSite(leftmost, logical);
            }
        }

        // Statement-guard form: `if (t != null) { ...t... } else { ... }` —
        // only the then-arm is gated by the successful pattern. The caller's
        // LocalReferencesOnlyWithin check rejects any else-arm use of t.
        if (next is IfStatement guard && IsPatternLocalNotNull(guard.Condition, index))
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
    static bool IsSideEffectFree(IrFunction function, IrExpression expr) => expr switch
    {
        Constant or LoadLocal or LoadArgument or LoadLocalAddress or LoadArgumentAddress => true,
        LoadField field => field.Instance is null || IsSideEffectFree(function, field.Instance),
        LoadFieldAddress fieldAddress => fieldAddress.Instance is null || IsSideEffectFree(function, fieldAddress.Instance),
        LoadProperty property when IsUnionValueProperty(function, property) => IsSimpleUnionValueReceiver(property.Instance),
        Convert convert => IsSideEffectFree(function, convert.Operand),
        CastClass cast => IsSideEffectFree(function, cast.Operand),
        IsInstance isInstance => IsSideEffectFree(function, isInstance.Operand),
        _ => false,
    };

    static bool IsUnionValueProperty(IrFunction function, LoadProperty property)
        => property.PropertyName == "Value"
        && property.IndexArguments.Count == 0
        && function.UnionTypes.Contains(NamedDefinition(property.Accessor.DeclaringType));

    static bool IsSimpleUnionValueReceiver(IrExpression? receiver) => receiver switch
    {
        LoadArgumentAddress or LoadArgument or LoadLocalAddress or LoadLocal => true,
        LoadFieldAddress field => field.Instance is null || IsSimpleUnionValueReceiver(field.Instance),
        LoadField field => field.Instance is null || IsSimpleUnionValueReceiver(field.Instance),
        _ => false,
    };

    static TypeRef NamedDefinition(TypeRef type)
        => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
        && index < function.LocalNames.Length
        && !string.IsNullOrEmpty(function.LocalNames[index]);

}
