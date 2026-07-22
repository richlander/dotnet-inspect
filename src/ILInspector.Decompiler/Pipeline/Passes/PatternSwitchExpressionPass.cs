namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a non-union nested type-pattern <c>as</c>/null-test if/return cascade
/// over a plain receiver back into a <c>switch</c> expression with a default
/// arm. This is the shape csc lowers a <c>value switch { T t when g =&gt; v, ...,
/// _ =&gt; d }</c> to when the receiver is an ordinary expression (not a
/// discriminated-union <c>.Value</c>) and one arm carries a single-level
/// property subpattern (<c>U { Prop: T inner }</c>).
///
/// The pass is deliberately narrow. It requires:
/// <list type="bullet">
///   <item>a switch value bound once to a temp from a re-evaluable place
///   (<c>LoadArgument</c>/<c>LoadLocal</c>), read only by the arm type tests;</item>
///   <item>every arm a pure <c>IsInstance</c> test of that temp (or, for the
///   subpattern, of a property of the arm's bound local);</item>
///   <item>every no-match and guard-fail path yielding the identical default
///   value the trailing <c>return</c> yields;</item>
///   <item>each pattern-bound local referenced only within its own arm.</item>
/// </list>
/// Faithfully reproducing the same arms, order, guards, and default round-trips
/// to the same lowering: guard-fail routing straight to the default (rather than
/// the next arm) is valid precisely because the compiler proved the arm pattern
/// types mutually exclusive.
/// </summary>
public sealed class PatternSwitchExpressionPass : IIrPass
{
    public string Name => "pattern-switch-expression";

    sealed record ArmData(TypeRef PatternType, int? LocalIndex, PropertySubpattern? Subpattern, IrExpression? Guard, IrExpression Value);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is [var block] && TryMatch(function, block, out int startIndex, out var switchExpression))
            {
                block.SetChild(startIndex, new Return(switchExpression!));
                for (int i = block.Children.Count - 1; i > startIndex; i--)
                    block.Children[i].Detach();
                context.Stepper.StepOver("raise nested type-pattern dispatch to switch expression", block);
            }
        }
    }

    bool TryMatch(IrFunction function, Block block, out int startIndex, out PatternSwitchExpression? switchExpression)
    {
        startIndex = -1;
        switchExpression = null;

        var children = block.Children;
        if (children.Count < 3)
            return false;

        // Locate the switch-value binding: `StoreLocal SV = <re-evaluable place>`
        // immediately followed by the first arm's `StoreLocal Lk = isinst Tk(LoadLocal SV)`.
        for (int i = 0; i < children.Count - 2; i++)
        {
            if (children[i] is not StoreLocal { Value: { } svValue } svStore)
                continue;
            if (!IsReEvaluablePlace(svValue))
                continue;
            if (!IsArmIntro(children[i + 1], svStore.Index, out _, out _, out _))
                continue;

            int svLocal = svStore.Index;
            // The cascade region runs from the first arm intro to the end of the
            // block. Each arm's matched body trails its dispatch test as sibling
            // statements, and the no-match remainder (further arms, ultimately the
            // default) nests inside that test's `then` branch.
            var region = children.Skip(i + 1).ToList();
            // The default is the value the innermost no-match path returns; it is
            // discovered from the bottom of the dispatch chain, then every other
            // no-match and guard-fail path is validated to yield the same value.
            if (!TryDiscoverDefault(region, svLocal, out var defaultValue) || defaultValue is null)
                continue;

            var arms = new List<ArmData>();
            if (!TryParseChain(region, 0, svLocal, defaultValue, arms) || arms.Count == 0)
                continue;

            // Consumed nodes: the switch-value store plus the entire cascade region.
            var consumed = new List<IrNode> { svStore };
            consumed.AddRange(children.Skip(i + 1));

            // The switch-value temp must be read only inside the consumed cascade.
            if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, svLocal, consumed))
                continue;

            // Each pattern-bound local must be referenced only inside the cascade.
            bool ownershipHolds = true;
            foreach (var arm in arms)
            {
                foreach (int local in PatternLocals(arm))
                {
                    if (!ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, local, consumed))
                    {
                        ownershipHolds = false;
                        break;
                    }
                }
                if (!ownershipHolds)
                    break;
            }
            if (!ownershipHolds)
                continue;

            var builtArms = arms.Select(a => new PatternSwitchExpressionArm(
                a.PatternType,
                a.LocalIndex,
                a.Subpattern,
                (IrExpression)a.Value.Clone(),
                a.Guard is { } g ? (IrExpression)g.Clone() : null)).ToList();

            switchExpression = new PatternSwitchExpression(
                (IrExpression)svValue.Clone(),
                builtArms,
                (IrExpression)defaultValue.Clone());
            startIndex = i;
            return true;
        }

        return false;
    }

    // A chain of arms whose collective no-match fall-through reaches the default.
    // Post-lowering, each arm is `intro Lk = isinst Tk(SV); if (!Lk) { REST }
    // MATCHED` where MATCHED (this arm's guard + value) trails the dispatch test
    // as sibling statements, and REST (the remaining arms, ultimately a bare
    // `return <default>`) nests inside the test's `then` branch. The dispatch
    // test carries no `else`: its `then` always returns, so csc drops it.
    bool TryParseChain(IReadOnlyList<IrNode> stmts, int index, int svLocal, IrExpression defaultValue, List<ArmData> arms)
    {
        if (index >= stmts.Count)
            return false;

        // Bottom of the chain: a bare `return <default>`.
        if (index == stmts.Count - 1 && stmts[index] is Return { Value: { } tailValue } && DefaultEquals(tailValue, defaultValue))
            return true;

        if (!IsArmIntro(stmts[index], svLocal, out int patternLocal, out var patternType, out _))
            return false;
        if (index + 1 >= stmts.Count || stmts[index + 1] is not IfStatement { HasElse: false } dispatch)
            return false;
        if (!IsNegatedLocalTest(dispatch.Condition, patternLocal))
            return false;

        // MATCHED body = the sibling statements after the dispatch test.
        if (!TryParseMatchedBody(stmts, index + 2, patternType, patternLocal, defaultValue, out var arm, out int matchedNext))
            return false;
        // Nothing may follow the matched body at this level except an optional
        // trailing `return <default>` (an arm's own no-match fall-through).
        if (!ReachesDefaultTail(stmts, matchedNext, defaultValue))
            return false;

        arms.Add(arm);
        // REST: the remaining arms (or the bare default) nest in the `then` branch.
        return TryParseChain(dispatch.Then.Children, 0, svLocal, defaultValue, arms);
    }

    // Walks the dispatch chain to its bottom to recover the default value: the
    // value the innermost `if (!Llast) { return <default>; }` no-match path
    // yields. Each level is `intro Lk = isinst Tk(SV); if (!Lk) { REST }`; when
    // REST is a bare `return D`, D is the default; otherwise descend into REST.
    bool TryDiscoverDefault(IReadOnlyList<IrNode> region, int svLocal, out IrExpression? defaultValue)
    {
        defaultValue = null;
        var current = region;
        // Bounded to the block's own statement budget so a malformed nest cannot loop.
        for (int guard = 0; guard < 1024; guard++)
        {
            if (current.Count < 2)
                return false;
            if (!IsArmIntro(current[0], svLocal, out int local, out _, out _))
                return false;
            if (current[1] is not IfStatement { HasElse: false } dispatch || !IsNegatedLocalTest(dispatch.Condition, local))
                return false;
            var then = dispatch.Then.Children;
            if (then is [Return { Value: { } value }])
            {
                defaultValue = value;
                return true;
            }
            current = then;
        }
        return false;
    }

    // Parse the body run for a matched arm (value is `patternType`, bound to
    // `patternLocal`), starting at `stmts[index]`. Sets `arm` and `nextIndex`
    // (the first statement after the arm; unconditional-return arms leave the
    // caller to confirm the tail reaches the default).
    bool TryParseMatchedBody(
        IReadOnlyList<IrNode> stmts,
        int index,
        TypeRef patternType,
        int patternLocal,
        IrExpression defaultValue,
        out ArmData arm,
        out int nextIndex)
    {
        arm = null!;
        nextIndex = -1;
        if (index >= stmts.Count)
            return false;

        // Property-subpattern arm: `StoreLocal Lsub = isinst Tsub(LoadProperty
        // Prop(LoadLocal Lk)); if (Lsub) { INNER }` then fall-through.
        if (stmts[index] is StoreLocal
            {
                Value: IsInstance { Type: { } subType, Operand: LoadProperty { Instance: LoadLocal { } propReceiver } subProperty }
            } subStore
            && propReceiver.Index == patternLocal
            && index + 1 < stmts.Count
            && stmts[index + 1] is IfStatement { HasElse: false } subIf
            && IsLocalTest(subIf.Condition, subStore.Index))
        {
            if (!TryParseGuardedValue(subIf.Then.Children, 0, defaultValue, out var innerGuard, out var innerValue, out int innerConsumed)
                || !ReachesDefaultTail(subIf.Then.Children, innerConsumed, defaultValue))
                return false;
            var subpattern = new PropertySubpattern(subProperty.Accessor, subType, subStore.Index);
            // The outer pattern binds a local only if it is used by the guard or
            // value; a pure `U { Prop: T inner }` arm binds nothing outer.
            int? outerLocal = ReferencesLocalIn(innerGuard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(innerValue, patternLocal)
                ? patternLocal
                : null;
            arm = new ArmData(patternType, outerLocal, subpattern, innerGuard, innerValue);
            nextIndex = index + 2;
            return true;
        }

        // Bare type-pattern arm: a guarded (or unguarded) value run.
        if (!TryParseGuardedValue(stmts, index, defaultValue, out var guard, out var value, out int consumed))
            return false;
        int? localIndex = ReferencesLocalIn(guard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(value, patternLocal)
            ? patternLocal
            : null;
        arm = new ArmData(patternType, localIndex, Subpattern: null, guard, value);
        nextIndex = index + consumed;
        return true;
    }

    // A value run in one of three shapes, all yielding (guard?, value):
    //   `return V;`                              -> unguarded
    //   `if (!G) return <default>; return V;`    -> guarded, negated form
    //   `if (G) { return V; }`                   -> guarded, positive form
    bool TryParseGuardedValue(
        IReadOnlyList<IrNode> stmts,
        int index,
        IrExpression defaultValue,
        out IrExpression? guard,
        out IrExpression value,
        out int consumed)
    {
        guard = null;
        value = null!;
        consumed = 0;

        if (index >= stmts.Count)
            return false;

        // Unguarded: `return V;`
        if (stmts[index] is Return { Value: { } directValue } && !DefaultEquals(directValue, defaultValue))
        {
            value = directValue;
            consumed = 1;
            return true;
        }

        if (stmts[index] is not IfStatement { HasElse: false } guardIf)
            return false;

        // Negated form: `if (!G) return <default>; return V;`
        if (guardIf.Condition is LogicalNot { Operand: { } negatedGuard }
            && guardIf.Then.Children is [Return { Value: { } negatedDefault }] && DefaultEquals(negatedDefault, defaultValue)
            && index + 1 < stmts.Count
            && stmts[index + 1] is Return { Value: { } guardedValue } && !DefaultEquals(guardedValue, defaultValue))
        {
            guard = negatedGuard;
            value = guardedValue;
            consumed = 2;
            return true;
        }

        // Positive form: `if (G) { return V; }`
        if (guardIf.Then.Children is [Return { Value: { } positiveValue }] && !DefaultEquals(positiveValue, defaultValue))
        {
            guard = guardIf.Condition;
            value = positiveValue;
            consumed = 1;
            return true;
        }

        return false;
    }

    static bool IsArmIntro(IrNode node, int svLocal, out int patternLocal, out TypeRef patternType, out IsInstance isInstance)
    {
        patternLocal = -1;
        patternType = null!;
        isInstance = null!;
        if (node is StoreLocal { Value: IsInstance { Operand: LoadLocal receiver } test } store && receiver.Index == svLocal)
        {
            patternLocal = store.Index;
            patternType = test.Type;
            isInstance = test;
            return true;
        }
        return false;
    }

    static bool IsNegatedLocalTest(IrExpression condition, int local)
        => condition is LogicalNot { Operand: LoadLocal load } && load.Index == local;

    static bool ReachesDefaultTail(IReadOnlyList<IrNode> stmts, int index, IrExpression defaultValue)
        => index == stmts.Count
            || (index == stmts.Count - 1 && stmts[index] is Return { Value: { } value } && DefaultEquals(value, defaultValue));

    static bool IsLocalTest(IrExpression condition, int local)
        => condition is LoadLocal load && load.Index == local;

    static bool IsReEvaluablePlace(IrExpression expression)
        => expression is LoadArgument or LoadLocal;

    static bool ReferencesLocalIn(IrExpression? expression, int local)
        => expression is not null && ReferenceOwnership.SubtreeReferencesLocal(expression, local);

    static bool DefaultEquals(IrExpression? left, IrExpression? right)
        => left is not null && right is not null && PlaceIdentity.SameOperand(left, right);

    static IEnumerable<int> PatternLocals(ArmData arm)
    {
        if (arm.LocalIndex is { } outer)
            yield return outer;
        if (arm.Subpattern is { } sub)
            yield return sub.LocalIndex;
    }
}
