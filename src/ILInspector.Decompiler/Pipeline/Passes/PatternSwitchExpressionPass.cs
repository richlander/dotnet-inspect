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
/// to the same lowering only when the arms cannot diverge from the cascade. A
/// guard or subpattern arm routes a type-match-but-refuted value to the default
/// in the cascade but to a later arm in a switch expression, so the pass raises
/// such an arm only when its pattern type is provably disjoint from every later
/// arm's type (via <see cref="PassContext.TypesProvablyDisjoint"/>); absent that
/// proof it declines and leaves the cascade untouched.
/// </summary>
public sealed class PatternSwitchExpressionPass : IIrPass
{
    public string Name => "pattern-switch-expression";

    // PatternLocal is the outer local every arm binds its `isinst` result to,
    // always present regardless of rendering. LocalIndex is the subset actually
    // spelled as a C# pattern variable (null when the outer type binds no name,
    // e.g. a pure `U { Prop: T inner }` arm). Scope validation uses PatternLocal;
    // rendering uses LocalIndex.
    sealed record ArmData(TypeRef PatternType, int PatternLocal, int? LocalIndex, PropertySubpattern? Subpattern, IrExpression? Guard, IrExpression Value);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is [var block] && TryMatch(function, block, context, out int startIndex, out var switchExpression))
            {
                block.SetChild(startIndex, new Return(switchExpression!));
                for (int i = block.Children.Count - 1; i > startIndex; i--)
                    block.Children[i].Detach();
                context.Stepper.StepOver("raise nested type-pattern dispatch to switch expression", block);
            }
        }
    }

    bool TryMatch(IrFunction function, Block block, PassContext context, out int startIndex, out PatternSwitchExpression? switchExpression)
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

            // No arm may bind its `isinst` result into the switch-value temp
            // itself. That store overwrites the receiver the remaining arm tests
            // re-read (so a later arm tests the previous arm's `isinst` result,
            // not the original value), whereas the raised switch evaluates every
            // arm against the one original value. Decline to preserve semantics.
            if (arms.Any(a => PatternLocals(a).Contains(svLocal)))
                continue;

            // A pattern variable is scoped to its own switch arm: no sibling
            // arm's guard/value and not the default may read it. In the lowered
            // cascade the bound local outlives its arm, but the raised C# would
            // reference an out-of-scope variable (CS0103). Decline if any pattern
            // local leaks across arms.
            if (!PatternLocalsAreArmScoped(arms, defaultValue))
                continue;

            // A refutable arm (a when-guard or a property subpattern) routes a
            // value that matches its type but fails the refinement to the default
            // in the cascade, whereas a switch expression routes it to the next
            // matching arm. The two agree only when no later arm's type can also
            // match, so require the refutable arm's type to be provably disjoint
            // from every later arm's type. Absent a disjointness oracle or a
            // proof, decline rather than risk a semantics change.
            if (!RefutableArmsDisjointFromLaterArms(arms, context))
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
        if (!TryParseMatchedBody(stmts, index + 2, patternType, patternLocal, defaultValue, out var arm, out int matchedNext, out bool fallsThrough))
            return false;
        // Nothing may follow the matched body at this level except an optional
        // trailing `return <default>`. When the matched body can fall through
        // (a positive-form guard or a property subpattern), that trailing
        // default MUST be present; otherwise the fall-through path would be
        // silently rerouted to whatever the recursion parses next.
        if (!ReachesDefaultTail(stmts, matchedNext, defaultValue, fallsThrough))
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
        out int nextIndex,
        out bool fallsThrough)
    {
        arm = null!;
        nextIndex = -1;
        fallsThrough = false;
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
            if (!TryParseGuardedValue(subIf.Then.Children, 0, defaultValue, out var innerGuard, out var innerValue, out int innerConsumed, out bool innerFallsThrough)
                || !ReachesDefaultTail(subIf.Then.Children, innerConsumed, defaultValue, innerFallsThrough))
                return false;
            var subpattern = new PropertySubpattern(subProperty.Accessor, subType, subStore.Index);
            // The outer pattern binds a local only if it is used by the guard or
            // value; a pure `U { Prop: T inner }` arm binds nothing outer.
            int? outerLocal = ReferencesLocalIn(innerGuard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(innerValue, patternLocal)
                ? patternLocal
                : null;
            arm = new ArmData(patternType, patternLocal, outerLocal, subpattern, innerGuard, innerValue);
            nextIndex = index + 2;
            // On submatch failure (`if (Lsub)` false) control drops past the
            // subpattern test to the enclosing statements, so this arm always
            // falls through; the caller must find an explicit default tail.
            fallsThrough = true;
            return true;
        }

        // Bare type-pattern arm: a guarded (or unguarded) value run.
        if (!TryParseGuardedValue(stmts, index, defaultValue, out var guard, out var value, out int consumed, out fallsThrough))
            return false;
        int? localIndex = ReferencesLocalIn(guard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(value, patternLocal)
            ? patternLocal
            : null;
        arm = new ArmData(patternType, patternLocal, localIndex, Subpattern: null, guard, value);
        nextIndex = index + consumed;
        return true;
    }

    // A value run in one of three shapes, all yielding (guard?, value):
    //   `return V;`                              -> unguarded, does not fall through
    //   `if (!G) return <default>; return V;`    -> guarded, negated form, does not fall through
    //   `if (G) { return V; }`                   -> guarded, positive form, falls through on guard-fail
    // `fallsThrough` reports whether a guard-fail path drops out of this run to
    // the following statement (rather than returning). The caller must then
    // require an explicit trailing `return <default>`; otherwise a fall-through
    // path could be silently rerouted (see ReachesDefaultTail).
    bool TryParseGuardedValue(
        IReadOnlyList<IrNode> stmts,
        int index,
        IrExpression defaultValue,
        out IrExpression? guard,
        out IrExpression value,
        out int consumed,
        out bool fallsThrough)
    {
        guard = null;
        value = null!;
        consumed = 0;
        fallsThrough = false;

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

        // Positive form: `if (G) { return V; }` — guard-fail falls through.
        if (guardIf.Then.Children is [Return { Value: { } positiveValue }] && !DefaultEquals(positiveValue, defaultValue))
        {
            guard = guardIf.Condition;
            value = positiveValue;
            consumed = 1;
            fallsThrough = true;
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

    // Confirms the statements after a matched body reach only the default. When
    // the body cannot fall through (unguarded or negated-form guard: every path
    // returns) an empty tail is sufficient. When it can fall through (positive
    // guard or property subpattern) a fall-through path exists that must land on
    // an explicit trailing `return <default>`; accepting an empty tail there
    // would let that path be rerouted, changing semantics.
    static bool ReachesDefaultTail(IReadOnlyList<IrNode> stmts, int index, IrExpression defaultValue, bool fallsThrough)
    {
        bool explicitDefault = index == stmts.Count - 1
            && stmts[index] is Return { Value: { } value } && DefaultEquals(value, defaultValue);
        if (fallsThrough)
            return explicitDefault;
        return index == stmts.Count || explicitDefault;
    }

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
        // Every arm binds its `isinst` result to an outer local, whether or not
        // that local is rendered as a C# pattern variable. Scope validation must
        // see all introduced locals — not only the rendered ones — so a sibling
        // arm or the default reading an unrendered outer local is still caught.
        yield return arm.PatternLocal;
        if (arm.Subpattern is { } sub)
            yield return sub.LocalIndex;
    }

    // A pattern variable belongs to exactly one arm. Reject if any arm's
    // guard/value reads a pattern local owned by a different arm, or if the
    // default reads any pattern local (all illegal once spelled as C# switch
    // arms with per-arm variable scope).
    static bool PatternLocalsAreArmScoped(List<ArmData> arms, IrExpression defaultValue)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < arms.Count; i++)
        {
            foreach (int local in PatternLocals(arms[i]))
            {
                // A slot shared by two arms cannot be scoped per-arm; decline.
                if (owner.ContainsKey(local))
                    return false;
                owner[local] = i;
            }
        }

        foreach (var (local, armIndex) in owner)
        {
            for (int i = 0; i < arms.Count; i++)
            {
                if (i == armIndex)
                    continue;
                if (ReferencesLocalIn(arms[i].Guard, local) || ReferenceOwnership.SubtreeReferencesLocal(arms[i].Value, local))
                    return false;
            }
            if (ReferenceOwnership.SubtreeReferencesLocal(defaultValue, local))
                return false;
        }
        return true;
    }

    // A refutable arm (guard or subpattern) is sound to raise only when its
    // pattern type is provably disjoint from every later arm's type, because
    // type-match-but-refuted routes to the default in the cascade but to a later
    // arm in the switch. Unguarded bare arms always return their value on type
    // match, so they never diverge and need no proof.
    static bool RefutableArmsDisjointFromLaterArms(List<ArmData> arms, PassContext context)
    {
        for (int i = 0; i < arms.Count - 1; i++)
        {
            if (arms[i].Guard is null && arms[i].Subpattern is null)
                continue;
            for (int j = i + 1; j < arms.Count; j++)
            {
                if (context.TypesProvablyDisjoint is not { } disjoint
                    || !disjoint(arms[i].PatternType, arms[j].PatternType))
                    return false;
            }
        }
        return true;
    }
}
