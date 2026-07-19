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
            || TransformNegatedPropertyPatternGuard(function, context.Stepper)
            || FoldConditionalReturnOne(function, context.Stepper)
            || FoldNegatedConditionalPatternReturnOne(function, context.Stepper)
            || FoldClassUnionNullConditionalReturnOne(function, context.Stepper)
            || TransformRecursivePropertyDeclaration(function, context.Stepper)
            || FoldPositionalPatternReturnOne(function, context.Stepper)
            || FoldUnionValueReceiverCopy(function, context.Stepper)
            || RaiseGenericDeclarationPattern(function, context.Stepper)
            || InlineGenericPatternSubject(function, context.Stepper)
            || BindGenericPatternToCacheLocal(function, context.Stepper))
        {
        }
    }

    // #2862: raise csc's generic (unconstrained/struct-constrained) declaration-
    // pattern extraction to a real binding. The lowering caches the subject in a
    // read-only temp, tests it with `isinst`, and — because `T t = subject as T` is
    // illegal for a non-class-constrained T — re-tests and unboxes the value inline
    // at each use (`UnboxAny T(IsInstance T(x))`), the shape #2856 renders through an
    // `(object)` bridge. When the guarding `if (x is T)` structurally dominates every
    // such extraction (the #2856 proof boundaries) with no intervening write of
    // the tested value between that guard and each extraction, introduce
    // `if (x is T t)` and rewrite the dominated extractions to load the bound `t`. Only the positive, structured
    // shape is raised: `x is not T t` is illegal C# (CS8780), so negated/flat-CFG
    // guards stay on the #2856 object-bridge fallback.
    static bool RaiseGenericDeclarationPattern(IrFunction function, Stepper stepper)
    {
        // Only guards in the root function's scope may be raised: the pattern local
        // is minted on `function` via AddLocal, so a guard inside a nested lambda or
        // local function (whose locals live in a separate, immutable pool the inner
        // printer scopes independently) must stay on the #2856 bridge to avoid a
        // dangling local index. Skip nested scopes with the boundary-aware walk.
        foreach (var ifStatement in GenericDeclarationPatternProof
            .DescendantsOutsideNestedFunctions(function).OfType<IfStatement>().ToList())
        {
            if (ifStatement.Condition is not IsInstance guard
                || !GenericDeclarationPatternProof.IsReadOnlyOperand(guard.Operand))
            {
                continue;
            }

            var sites = ifStatement.Then.Descendants
                .OfType<UnboxAny>()
                .Where(unbox => unbox.Type.Equals(guard.Type)
                    && unbox.Operand is IsInstance inner
                    && inner.Type.Equals(guard.Type)
                    && GenericDeclarationPatternProof.SameTestedValue(inner.Operand, guard.Operand))
                .ToList();
            if (sites.Count == 0)
                continue;

            // Bind the pattern local at THIS guard, so every rewritten extraction
            // must read the exact value this guard tested: prove each site against
            // this specific guard (not merely some inner guard that re-tested a
            // mutated value past a write). If any dominated extraction is not proven
            // by this guard, retain the lowered shape rather than binding a subset
            // (which would strand the unprovable extraction with no visible `t`, or
            // rebind a re-read to a stale value).
            if (!sites.All(site => GenericDeclarationPatternProof.IsProvenBySpecificGuard(
                    ifStatement, site, (IsInstance)site.Operand)))
            {
                continue;
            }

            int patternLocal = function.AddLocal(guard.Type);
            // The tested value is boxed to `object` for the IL `isinst`; the C# pattern
            // tests the unboxed value directly, so drop an outer box when present.
            var testedValue = guard.Operand is Box outerBox ? outerBox.Operand : guard.Operand;
            var pattern = new IsPattern((IrExpression)testedValue.Clone(), guard.Type, patternLocal);
            stepper.StepOver("raise generic declaration pattern", ifStatement);
            ifStatement.Condition.ReplaceWith(pattern);
            foreach (var site in sites)
                site.ReplaceWith(new LoadLocal(patternLocal, guard.Type));
            return true;
        }
        return false;
    }

    // #2862 slice 2: fold the lowering-only subject temp into the pattern. After the
    // binding is raised, csc's `TSubject V = subject; if (V is T t)` still names the
    // cache; when V feeds only the pattern's value and the pattern is the guard's
    // whole condition (so the subject stays first-evaluated and evaluated exactly
    // once), inline it to `if (subject is T t)`. Retain the temp on any other use of
    // V, a self-reference, or a compound condition (an evaluation-order risk).
    static bool InlineGenericPatternSubject(IrFunction function, Stepper stepper)
    {
        // Same nested-scope restriction as the raise: the single-use ownership check
        // below (LocalReferencesOnlyWithin over `function`) reasons about the root
        // local pool, so only inline stores in the root scope. Blocks inside nested
        // functions carry indices into a different pool and must be left alone.
        foreach (var block in GenericDeclarationPatternProof
            .DescendantsOutsideNestedFunctions(function).OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                // The generic type test boxes the tested value, so the pattern's value
                // is `Box(LoadLocal V)`; peel the box to reach the local reference and
                // inline into it, leaving the box (printed as `(subject) is T t`).
                if (children[i] is not StoreLocal store
                    || children[i + 1] is not IfStatement { Condition: IsPattern pattern })
                {
                    continue;
                }

                var load = pattern.Value switch
                {
                    Box { Operand: LoadLocal boxed } => boxed,
                    LoadLocal bare => bare,
                    _ => null,
                };
                if (load is null || load.Index != store.Index)
                    continue;

                if (ReferenceOwnership.SubtreeReferencesLocal(store.Value, store.Index)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, load]))
                {
                    continue;
                }

                var value = (IrExpression)store.DetachChildren()[0];
                stepper.StepOver("inline generic declaration-pattern subject", children[i + 1]);
                load.ReplaceWith(value);
                store.Detach();
                return true;
            }
        }
        return false;
    }

    // #2862 slice (#2872): bind a multi-use generic declaration pattern directly
    // to csc's cache local and drop the redundant copy. When the binding is read
    // more than once, csc caches the single `isinst` extraction in a real,
    // source-named local, so RaiseGenericDeclarationPattern binds a synthesized
    // `V_N` and leaves `cacheLocal = V_N;` at the top of the guarded arm. When
    // that minted local feeds only the copy, and the cache local is assigned only
    // there and read only inside the guarded arm, re-point the pattern to the
    // cache local (recovering its name) and elide the copy — turning
    // `if (x is T V_N) { c = V_N; ... c ... }` into `if (x is T c) { ... c ... }`.
    static bool BindGenericPatternToCacheLocal(IrFunction function, Stepper stepper)
    {
        // Only root-scope guards: re-pointing the binding rewrites references in
        // the root local pool, so a nested-function guard (separate pool) is out.
        foreach (var ifStatement in GenericDeclarationPatternProof
            .DescendantsOutsideNestedFunctions(function).OfType<IfStatement>().ToList())
        {
            if (ifStatement.Condition is not IsPattern pattern)
                continue;

            // The copy must be the guarded arm's first statement, so the cache
            // local is bound before any read — exactly the pattern's own scope.
            if (ifStatement.Then.Children is not [StoreLocal copy, ..]
                || copy.Value is not LoadLocal { Index: var boundIndex }
                || boundIndex != pattern.LocalIndex
                || copy.Index == pattern.LocalIndex)
            {
                continue;
            }

            int cacheIndex = copy.Index;

            // The minted binding must feed only this copy: its single reference is
            // the `LoadLocal` inside `copy` (the pattern's LocalIndex is a slot
            // designation, not a Load/Store reference, so it is not counted here).
            if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, pattern.LocalIndex, [copy]))
                continue;

            // The cache local must be a single-assignment temp whose every use
            // is inside this guarded arm, so binding it in the pattern covers
            // all reads with the exact value the copy provided. The pattern's
            // tested value must not read it. Reference/binding checks count
            // every designation that carries a local index (pattern bindings,
            // foreach/using/fixed headers, deconstruction targets, catch
            // variables, null-coalescing targets), not just explicit
            // Load/Store, so an out-of-arm use expressed through one of those
            // node kinds cannot slip past and reference an undeclared local.
            if (function.Descendants.OfType<StoreLocal>().Count(s => s.Index == cacheIndex) != 1
                || ReferenceOwnership.SubtreeReferencesOrBindsLocal(pattern.Value, cacheIndex)
                || !ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, cacheIndex, [ifStatement.Then]))
            {
                continue;
            }

            // The cache local's managed address must not escape the guarded
            // arm. The only escape-free consumer we admit is an in-place
            // instance-call receiver whose result is not itself a managed
            // reference (such as `c.CompareTo(x)` returning an int). Any other
            // address use — storing the pointer into a wider-scoped
            // local/slot/field, a ref return, an indirect store, passing the
            // address as a by-ref argument, or a by-ref-returning receiver
            // call that forwards the pointer onward — could outlive the arm
            // once the binding narrows to the pattern's scope, so reject the
            // fold. Checking only the direct parent is not enough: a
            // by-ref-returning call forwards the pointer through a `Call` node,
            // so the admitted shape is matched positively rather than
            // denied by an incomplete escape-node list.
            if (function.Descendants.OfType<LoadLocalAddress>()
                .Where(address => address.Index == cacheIndex)
                .Any(address => !IsInPlaceReceiver(address)))
            {
                continue;
            }

            stepper.StepOver("bind generic declaration pattern to cache local", ifStatement);
            pattern.ReplaceWith(new IsPattern(
                (IrExpression)pattern.Value.Clone(), pattern.Type, cacheIndex));
            copy.Detach();
            return true;
        }
        return false;
    }

    // An address use is escape-free only when it is the receiver of an
    // instance call whose result is not a managed reference. Such a call
    // consumes the pointer in place (the struct receiver), so the pointer does
    // not outlive the guarded arm. A by-ref-returning receiver call, a static
    // call taking the address as a by-ref argument, or any non-call parent
    // (store, indirect store, ref return) may forward the pointer onward and
    // is therefore not admitted.
    static bool IsInPlaceReceiver(LoadLocalAddress address)
        => address.Parent is Call { Callee.HasThis: true } call
            && call.Children.Count > 0
            && ReferenceEquals(call.Children[0], address)
            && call.ResultType is not { Kind: TypeRefKind.ByRef };

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

    static bool FoldUnionValueReceiverCopy(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal copyStore
                    || TryUnionReceiverCopySource(copyStore.Value) is not { } receiver
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, copyStore.Index, [copyStore, children[i + 1]]))
                {
                    continue;
                }

                var properties = children[i + 1]
                    .Descendants
                    .OfType<LoadProperty>()
                    .Where(property => IsUnionValueProperty(function, property)
                        && property.Instance is LoadLocalAddress address
                        && address.Index == copyStore.Index)
                    .ToList();

                int localReferences = children[i + 1].Descendants.Count(node => node switch
                {
                    LoadLocal load => load.Index == copyStore.Index,
                    LoadLocalAddress address => address.Index == copyStore.Index,
                    _ => false
                });

                if (properties.Count == 0 || localReferences != properties.Count)
                    continue;

                foreach (var property in properties)
                    property.Instance!.ReplaceWith((IrExpression)receiver.Clone());

                stepper.StepOver("fold union receiver copy into pattern receiver", children[i + 1]);
                copyStore.Detach();
                return true;
            }
        }

        return false;
    }

    static IrExpression? TryUnionReceiverCopySource(IrExpression expression)
        => expression is LoadIndirect { Address: LoadArgument argument }
            ? (IrExpression)argument.Clone()
            : null;

    static bool TransformNegatedPropertyPatternGuard(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal { Value: IsInstance asCast } store
                    || children[i + 1] is not IfStatement guard
                    || !TryNegatedPropertyPatternGuard(function, asCast, store.Index, guard.Condition, out var condition))
                {
                    continue;
                }

                if (ReferenceOwnership.SubtreeReferencesLocal(asCast.Operand, store.Index)
                    || !IsSideEffectFree(function, asCast.Operand)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, guard.Condition]))
                {
                    continue;
                }

                stepper.StepOver("raise negated union property-pattern guard", guard);
                guard.Condition.ReplaceWith(condition);
                store.Detach();
                return true;
            }
        }

        return false;
    }

    static bool TryNegatedPropertyPatternGuard(
        IrFunction function,
        IsInstance asCast,
        int localIndex,
        IrExpression guard,
        out IrExpression condition)
    {
        condition = null!;
        if (asCast.Operand is not LoadProperty property
            || !IsValueTypeUnionValueProperty(function, property)
            || guard is not LogicalBinary { Kind: LogicalKind.Or } logical)
        {
            return false;
        }

        IrExpression other;
        if (IsPatternLocalNull(logical.Left, localIndex))
        {
            other = logical.Right;
        }
        else if (IsPatternLocalNull(logical.Right, localIndex))
        {
            other = logical.Left;
        }
        else
        {
            return false;
        }

        var pattern = new IsPattern((IrExpression)asCast.Operand.Clone(), asCast.Type, localIndex);
        condition = new LogicalNot(new LogicalBinary(LogicalKind.And, pattern, Conditions.Negate((IrExpression)other.Clone())));
        return true;
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
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, guard.Condition, whenTrue]))
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

    static bool FoldNegatedConditionalPatternReturnOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal { Value: IsInstance asCast } store
                    || children[i + 1] is not Return { Value: LogicalNot { Operand: Conditional conditional } returnNot }
                    || !TryNegatedConditionalPattern(function, asCast, store.Index, conditional, out var patternCondition))
                {
                    continue;
                }

                if (ReferenceOwnership.SubtreeReferencesLocal(asCast.Operand, store.Index)
                    || !IsSideEffectFree(function, asCast.Operand)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, store.Index, [store, returnNot]))
                {
                    continue;
                }

                stepper.StepOver("raise negated union pattern conditional", returnNot);
                returnNot.ReplaceWith(new LogicalNot(patternCondition));
                store.Detach();
                return true;
            }
        }

        return false;
    }

    static bool TryNegatedConditionalPattern(
        IrFunction function,
        IsInstance asCast,
        int localIndex,
        Conditional conditional,
        out IrExpression patternCondition)
    {
        patternCondition = null!;
        if (asCast.Operand is not LoadProperty property
            || !IsUnionValueProperty(function, property)
            || function.TypeShapes.GetValueOrDefault(NamedDefinition(property.Accessor.DeclaringType)) != TypeShape.ValueType)
        {
            return false;
        }

        IrExpression valueCondition;
        if (IsPatternLocalNull(conditional.Condition, localIndex)
            && IsFalseConstant(conditional.WhenTrue))
        {
            valueCondition = conditional.WhenFalse;
        }
        else if (IsPatternLocalNotNull(conditional.Condition, localIndex)
            && IsFalseConstant(conditional.WhenFalse))
        {
            valueCondition = conditional.WhenTrue;
        }
        else
        {
            return false;
        }

        var pattern = new IsPattern((IrExpression)asCast.Operand.Clone(), asCast.Type, localIndex);
        patternCondition = new LogicalBinary(LogicalKind.And, pattern, (IrExpression)valueCondition.Clone());
        return true;
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

    static bool FoldClassUnionNullConditionalReturnOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not IfStatement outer
                    || outer.HasElse
                    || outer.Then.Children is not [IfStatement inner]
                    || inner.HasElse
                    || inner.Then.Children is not [Return { Value: { } whenTrue }]
                    || children[i + 1] is not Return { Value: { } whenFalse }
                    || !TryClassUnionConditionalPattern(function, outer.Condition, inner.Condition, out var pattern))
                {
                    continue;
                }

                if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, pattern.LocalIndex, [inner.Condition, whenTrue]))
                    continue;

                var conditional = new Conditional((IrExpression)inner.Condition.Clone(), (IrExpression)whenTrue.Clone(), (IrExpression)whenFalse.Clone())
                {
                    MergedType = whenTrue.ResultType ?? whenFalse.ResultType
                };
                stepper.StepOver("raise class union null-guard return chain to conditional", outer);
                children[i + 1].ReplaceWith(new Return(conditional));
                outer.Detach();
                return true;
            }
        }

        return false;
    }

    static bool TryClassUnionConditionalPattern(
        IrFunction function,
        IrExpression receiver,
        IrExpression condition,
        out IsPattern pattern)
    {
        pattern = null!;
        var candidate = LeftmostAndOperand(condition);
        if (candidate is IsPattern directPattern)
        {
            return TryAccept(directPattern, out pattern);
        }

        return false;

        bool TryAccept(IsPattern candidatePattern, out IsPattern accepted)
        {
            accepted = null!;
            if (candidatePattern.Value is LoadProperty property
                && IsUnionValueProperty(function, property)
                && PlaceIdentity.SameVariable(property.Instance, receiver))
            {
                accepted = candidatePattern;
                return true;
            }
            return false;
        }
    }

    static IrExpression LeftmostAndOperand(IrExpression expression)
    {
        var current = expression;
        while (current is LogicalBinary { Kind: LogicalKind.And } and)
            current = and.Left;
        return current;
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

    static bool IsValueTypeUnionValueProperty(IrFunction function, LoadProperty property)
        => IsUnionValueProperty(function, property)
        && function.TypeShapes.GetValueOrDefault(NamedDefinition(property.Accessor.DeclaringType)) == TypeShape.ValueType;

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
