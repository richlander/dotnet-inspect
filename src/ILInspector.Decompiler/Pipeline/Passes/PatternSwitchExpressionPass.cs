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

    sealed record ArmData(TypeRef PatternType, int PatternLocal, int? LocalIndex, PropertySubpattern? Subpattern, IrExpression? Guard, IrExpression Value, bool IsInline);

    /// <summary>
    /// The value every arm of the cascade tests. csc reads it either through a
    /// once-bound temp (<c>StoreLocal SV = place</c>; arms test <c>LoadLocal
    /// SV</c>) or directly from a re-evaluable place (arms test the
    /// <c>LoadArgument</c>/<c>LoadLocal</c> itself). <see cref="Matches"/> is the
    /// single identity check both arm intros use, so temp and direct cascades
    /// share one recognizer.
    /// </summary>
    readonly struct Scrutinee(int? tempLocal, IrExpression place)
    {
        public int? TempLocal { get; } = tempLocal;
        public IrExpression Place { get; } = place;

        public bool Matches(IrExpression? operand)
            => TempLocal is { } t
                ? operand is LoadLocal load && load.Index == t
                : PlaceIdentity.SameOperand(operand, Place);
    }

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is [var block] && TryMatch(function, block, context.TypesProvablyDisjoint, out int startIndex, out var switchExpression))
            {
                block.SetChild(startIndex, new Return(switchExpression!));
                for (int i = block.Children.Count - 1; i > startIndex; i--)
                    block.Children[i].Detach();
                context.Stepper.StepOver("raise nested type-pattern dispatch to switch expression", block);
            }
        }
    }

    bool TryMatch(IrFunction function, Block block, System.Func<TypeRef, TypeRef, bool>? disjoint, out int startIndex, out PatternSwitchExpression? switchExpression)
    {
        startIndex = -1;
        switchExpression = null;

        var children = block.Children;
        // Smallest cascade: one arm plus the default tail (inline form is
        // `if (place is T x) { ... } return <default>`).
        if (children.Count < 2)
            return false;

        for (int i = 0; i < children.Count - 1; i++)
        {
            // Temp form: `StoreLocal SV = <re-evaluable place>` bound once, then
            // arms testing `LoadLocal SV`. The cascade region runs from the first
            // arm (children[i + 1]) to the end of the block; the switch-value
            // store is consumed alongside it.
            if (children[i] is StoreLocal { Value: { } svValue } svStore
                && IsReEvaluablePlace(svValue)
                && TestsScrutinee(children[i + 1], new Scrutinee(svStore.Index, svValue)))
            {
                var scrutinee = new Scrutinee(svStore.Index, svValue);
                if (TryFold(function, children, i + 1, scrutinee, svValue, new IrNode[] { svStore }, minArms: 1, disjoint, out switchExpression))
                {
                    startIndex = i;
                    return true;
                }
            }

            // Direct form: no temp; the leading arm is an intro-chain arm reading
            // a re-evaluable place directly (`Lk = place as T; if (!Lk) { REST }
            // MATCHED`), and later arms may be inline-positive siblings. Temp form
            // is tried first at each index, so a genuine temp cascade is consumed
            // there (with its `StoreLocal SV`). A single-arm direct cascade is left
            // alone: it is indistinguishable from an ordinary `if (place is T t)`
            // guard, which IsPatternPass already renders idiomatically.
            if (TryScrutineeFromDirectArm(children[i], out var directScrutinee)
                && !IsScrutineeStoredBefore(children, i, directScrutinee)
                && TryFold(function, children, i, directScrutinee, directScrutinee.Place, System.Array.Empty<IrNode>(), minArms: 2, disjoint, out switchExpression))
            {
                startIndex = i;
                return true;
            }
        }

        return false;
    }

    // Attempts to fold the cascade region `children[regionStart..]` into a switch
    // expression over `switchValue`. `headConsumed` are nodes before the region
    // that the fold also consumes (the switch-value temp store, for the temp
    // form). Discovers the default, parses the arm chain, and enforces that the
    // scrutinee temp and every pattern-bound local are referenced only inside the
    // consumed cascade before building the expression.
    bool TryFold(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int regionStart,
        Scrutinee scrutinee,
        IrExpression switchValue,
        IReadOnlyList<IrNode> headConsumed,
        int minArms,
        System.Func<TypeRef, TypeRef, bool>? disjoint,
        out PatternSwitchExpression? switchExpression)
    {
        switchExpression = null;

        var region = children.Skip(regionStart).ToList();
        // The default is the value the innermost no-match path returns; it is
        // discovered from the bottom of the cascade, then every other no-match and
        // guard-fail path is validated to yield the same value.
        if (!TryDiscoverDefault(region, 0, scrutinee, out var defaultValue) || defaultValue is null)
            return false;

        var arms = new List<ArmData>();
        if (!TryParseChain(region, 0, scrutinee, defaultValue, arms, fallThroughIsDefault: false) || arms.Count < minArms)
            return false;

        // A refutable (guarded or property-subpattern) non-last arm routes its
        // refinement FAILURE to the trailing default in the lowered cascade, but
        // to the NEXT arm in a switch expression. Reproducing that routing is
        // faithful only when no later arm's type can also match the same value —
        // require a provable-disjointness proof from the oracle and decline
        // without one. This holds on every surface: the temp-form intro cascade
        // (#3022), whose arms the compiler already proved mutually exclusive, and
        // the direct/inline heterogeneous surface (#3028), where a guarded intro
        // arm (`Dot d when g`) or subpattern arm is folded ONLY when the oracle
        // proves it disjoint from every later arm. (An unguarded arm always
        // matches its type and never falls through, so it carries no obligation
        // here; #3065's compiler-lowered head guarantees the CS8510 ordering.)
        if (!RefutableArmsDisjointFromLaterArms(arms, disjoint))
            return false;

        // [#3082 finding 2] Pattern variables are arm-scoped: no sibling arm's
        // guard/value and not the default may read a local another arm binds,
        // including an unrendered intro/inline match local (Fix A).
        if (!PatternLocalsAreArmScoped(arms, defaultValue))
            return false;

        var consumed = new List<IrNode>(headConsumed);
        consumed.AddRange(region);

        // A temp scrutinee must be read only inside the consumed cascade, and only
        // read there: a store or address-of the temp inside the cascade would let a
        // guard mutate the value later arms observe (and the fold deletes the temp's
        // defining store, dangling any surviving reference).
        if (scrutinee.TempLocal is { } tempLocal
            && (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, consumed)
                || !TempScrutineeReadOnly(tempLocal, region, consumed)))
            return false;

        // [#3082 Fix D] A temp scrutinee may be READ only by the arm type tests.
        // A read in an arm guard/value or in the default observes default(T)
        // once the fold deletes the temp's defining store; the read-only check
        // above rejects stores and address-of, not a misplaced load.
        if (scrutinee.TempLocal is { } tempReadLocal
            && (arms.Any(a => ReferencesLocalIn(a.Guard, tempReadLocal) || ReferenceOwnership.SubtreeReferencesLocal(a.Value, tempReadLocal))
                || ReferenceOwnership.SubtreeReferencesLocal(defaultValue, tempReadLocal)))
            return false;

        // [#3082 Fix E] The temp form re-spells the switch value as the governing
        // expression. If that place is a LoadLocal aliasing a rendered pattern
        // local, the emitted `V switch { T V => ... }` is unspellable — the
        // governing name collides with the arm's pattern variable / is out of
        // scope. (The direct form is covered by DirectScrutineeStable.)
        if (scrutinee.TempLocal is not null
            && switchValue is LoadLocal governing
            && arms.Any(a => a.LocalIndex == governing.Index || a.Subpattern?.LocalIndex == governing.Index))
            return false;

        // A direct (temp-less) scrutinee is re-read by every arm from its
        // underlying place. A switch expression evaluates the value once, so the
        // fold is faithful only if that place cannot change across the cascade —
        // neither by a direct store between arm tests nor by an aliased by-ref
        // mutation through an address taken anywhere in the method.
        if (scrutinee.TempLocal is null && !DirectScrutineeStable(function, scrutinee.Place, consumed))
            return false;

        // Each pattern-bound local must be referenced only inside the cascade.
        foreach (var arm in arms)
        {
            foreach (int local in PatternLocals(arm))
            {
                if (!ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, local, consumed))
                    return false;
            }
        }

        var builtArms = arms.Select(a => new PatternSwitchExpressionArm(
            a.PatternType,
            a.LocalIndex,
            a.Subpattern,
            (IrExpression)a.Value.Clone(),
            a.Guard is { } g ? (IrExpression)g.Clone() : null)).ToList();

        switchExpression = new PatternSwitchExpression(
            (IrExpression)switchValue.Clone(),
            builtArms,
            (IrExpression)defaultValue.Clone());
        return true;
    }

    // A chain of arms whose collective no-match fall-through reaches the default.
    // Three arm shapes are recognized and may be mixed:
    //   * Intro-chain arm (then-only): `intro Lk = isinst Tk(SV); if (!Lk) { REST }
    //     MATCHED` where MATCHED (this arm's guard + value) trails the dispatch as
    //     sibling statements and REST (the remaining arms, ultimately a bare
    //     `return <default>`) nests in the test's `then`. csc emits this when the
    //     arm always returns, so the dispatch needs no `else`.
    //   * Intro-chain arm (if/else): `intro Lk = isinst Tk(SV); if (!Lk) { REST }
    //     else { MATCHED }` with the `<default>` a shared statement after the
    //     dispatch. csc emits this when MATCHED can fall through — a `when` guard
    //     or property subpattern whose failure must reach the default — so both
    //     REST (then) and MATCHED (else) fall off their block into that shared
    //     default.
    //   * Inline-positive arm: `if (SV is Tk x) { MATCHED }` whose no-match falls
    //     straight through to the following sibling statement (the next arm, or the
    //     bare `return <default>`).
    // <paramref name="fallThroughIsDefault"/> is set when running off the end of
    // <paramref name="stmts"/> lands on the default (a `then`/`else` block whose
    // enclosing dispatch is trailed by the default); at the top level, and inside a
    // then-only `then` (whose fall-through reaches the sibling MATCHED, not the
    // default), it is false and the list must end in an explicit `return <default>`.
    bool TryParseChain(IReadOnlyList<IrNode> stmts, int index, Scrutinee scrutinee, IrExpression defaultValue, List<ArmData> arms, bool fallThroughIsDefault)
    {
        // Ran off the end: faithful only when fall-through lands on the default.
        if (index >= stmts.Count)
            return fallThroughIsDefault;

        // Bottom of the chain: a bare `return <default>`.
        if (index == stmts.Count - 1 && stmts[index] is Return { Value: { } tailValue } && DefaultEquals(tailValue, defaultValue))
            return true;

        // Inline-positive arm: the matched body lives in the `then` block and the
        // remaining arms follow as siblings after the `if`.
        if (IsInlineArm(stmts[index], scrutinee, out int inlineLocal, out var inlineType, out var inlineBody))
        {
            if (!TryParseGuardedValue(inlineBody.Children, 0, defaultValue, out var inlineGuard, out var inlineValue, out int inlineConsumed, out bool inlineShortCircuits)
                || !ReachesDefaultTail(inlineBody.Children, inlineConsumed, defaultValue))
                return false;
            // An inline arm's guard failure must fall through to the following
            // sibling arm, exactly as a switch `when` guard does. The negated form
            // (`if (!G) return <default>;`) short-circuits to the default on guard
            // failure, and a positive guard with any trailing statement does the
            // same — both skip later arms a switch expression would still test.
            // Only fall-through shapes are safe here: an unguarded body, or a
            // positive `if (G) { return V; }` that is the entire matched body.
            if (inlineGuard is not null && (inlineShortCircuits || inlineConsumed != inlineBody.Children.Count))
                return false;
            int? inlineIndex = ReferencesLocalIn(inlineGuard, inlineLocal) || ReferenceOwnership.SubtreeReferencesLocal(inlineValue, inlineLocal)
                ? inlineLocal
                : null;
            // Inline arms are the direct/inline heterogeneous surface (#3028). A
            // guarded inline arm routes its guard failure to the following sibling
            // (the fall-through shape enforced above), exactly as a switch `when`
            // does; RefutableArmsDisjointFromLaterArms in TryFold still requires
            // its type disjoint from every later arm before folding.
            arms.Add(new ArmData(inlineType, inlineLocal, inlineIndex, Subpattern: null, inlineGuard, inlineValue, IsInline: true));
            return TryParseChain(stmts, index + 1, scrutinee, defaultValue, arms, fallThroughIsDefault);
        }

        if (!IsArmIntro(stmts[index], scrutinee, out int patternLocal, out var patternType, out _))
            return false;
        if (index + 1 >= stmts.Count || stmts[index + 1] is not IfStatement dispatch)
            return false;
        if (!IsNegatedLocalTest(dispatch.Condition, patternLocal))
            return false;

        if (dispatch.HasElse)
        {
            // If/else form (csc's lowering of a refutable intro arm): MATCHED is
            // the `else`, REST the `then`, and both fall off their block into the
            // shared default that trails the dispatch.
            if (!DefaultReachedFrom(stmts, index + 2, defaultValue, fallThroughIsDefault))
                return false;
            if (!TryParseMatchedBody(dispatch.Else!.Children, 0, patternType, patternLocal, defaultValue, out var elseArm, out int elseNext))
                return false;
            // MATCHED's own fall-through (a guard/subpattern failure) runs off the
            // `else` block into that same shared default.
            if (!DefaultReachedFrom(dispatch.Else!.Children, elseNext, defaultValue, fallThroughIsDefault: true))
                return false;
            arms.Add(elseArm);
            // REST nests in `then`; its fall-through reaches the shared default.
            return TryParseChain(dispatch.Then.Children, 0, scrutinee, defaultValue, arms, fallThroughIsDefault: true);
        }

        // Then-only form: MATCHED body = the sibling statements after the dispatch.
        if (!TryParseMatchedBody(stmts, index + 2, patternType, patternLocal, defaultValue, out var arm, out int matchedNext))
            return false;
        // Nothing may follow the matched body at this level except an optional
        // trailing `return <default>` (an arm's own no-match fall-through).
        if (!ReachesDefaultTail(stmts, matchedNext, defaultValue))
            return false;

        arms.Add(arm);
        // REST nests in `then`; its fall-through reaches the sibling MATCHED (not
        // the default), so it must reach the default explicitly.
        return TryParseChain(dispatch.Then.Children, 0, scrutinee, defaultValue, arms, fallThroughIsDefault: false);
    }

    // Whether control arriving at <paramref name="index"/> reaches the default:
    // either the list ends here on an explicit `return <default>`, or it runs off
    // the end into an enclosing default (only when <paramref name="fallThroughIsDefault"/>).
    static bool DefaultReachedFrom(IReadOnlyList<IrNode> stmts, int index, IrExpression defaultValue, bool fallThroughIsDefault)
        => (index >= stmts.Count && fallThroughIsDefault)
            || (index == stmts.Count - 1 && stmts[index] is Return { Value: { } value } && DefaultEquals(value, defaultValue));

    // Walks the cascade to its bottom to recover the default value: the value the
    // innermost no-match path yields. An intro-chain arm's no-match nests in its
    // dispatch `then` (descend into it); an inline-positive arm's no-match falls
    // through to the following sibling (advance past it); the bottom is a bare
    // trailing `return <default>`.
    bool TryDiscoverDefault(IReadOnlyList<IrNode> stmts, int index, Scrutinee scrutinee, out IrExpression? defaultValue)
    {
        defaultValue = null;
        // Bounded to a generous statement budget so a malformed nest cannot loop.
        for (int guard = 0; guard < 4096; guard++)
        {
            if (index >= stmts.Count)
                return false;

            // Bottom: a bare `return <default>` as the final statement.
            if (index == stmts.Count - 1 && stmts[index] is Return { Value: { } tail })
            {
                defaultValue = tail;
                return true;
            }

            // Inline-positive arm: no-match falls through to the next sibling.
            if (IsInlineArm(stmts[index], scrutinee, out _, out _, out _))
            {
                index++;
                continue;
            }

            // Intro-chain arm: no-match nests in the dispatch test's `then` branch
            // (both the then-only and if/else forms). For the if/else form the
            // shared default trails the dispatch, so take it directly when present.
            if (IsArmIntro(stmts[index], scrutinee, out int local, out _, out _)
                && index + 1 < stmts.Count
                && stmts[index + 1] is IfStatement dispatch
                && IsNegatedLocalTest(dispatch.Condition, local))
            {
                if (dispatch.HasElse
                    && index + 2 == stmts.Count - 1
                    && stmts[index + 2] is Return { Value: { } shared })
                {
                    defaultValue = shared;
                    return true;
                }
                stmts = dispatch.Then.Children;
                index = 0;
                continue;
            }

            return false;
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
            if (!TryParseGuardedValue(subIf.Then.Children, 0, defaultValue, out var innerGuard, out var innerValue, out int innerConsumed, out _)
                || !ReachesDefaultTail(subIf.Then.Children, innerConsumed, defaultValue))
                return false;
            var subpattern = new PropertySubpattern(subProperty.Accessor, subType, subStore.Index);
            // The outer pattern binds a local only if it is used by the guard or
            // value; a pure `U { Prop: T inner }` arm binds nothing outer.
            int? outerLocal = ReferencesLocalIn(innerGuard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(innerValue, patternLocal)
                ? patternLocal
                : null;
            arm = new ArmData(patternType, patternLocal, outerLocal, subpattern, innerGuard, innerValue, IsInline: false);
            nextIndex = index + 2;
            return true;
        }

        // Bare type-pattern arm: a guarded (or unguarded) value run.
        if (!TryParseGuardedValue(stmts, index, defaultValue, out var guard, out var value, out int consumed, out _))
            return false;
        int? localIndex = ReferencesLocalIn(guard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(value, patternLocal)
            ? patternLocal
            : null;
        arm = new ArmData(patternType, patternLocal, localIndex, Subpattern: null, guard, value, IsInline: false);
        nextIndex = index + consumed;
        return true;
    }

    // A value run in one of three shapes, all yielding (guard?, value):
    //   `return V;`                              -> unguarded
    //   `if (!G) return <default>; return V;`    -> guarded, negated form
    //   `if (G) { return V; }`                   -> guarded, positive form
    // `shortCircuitsToDefault` is true only for the negated form, whose guard
    // failure returns the default immediately rather than falling through to the
    // following statement — the caller uses it to reject short-circuiting shapes
    // where fall-through order matters (inline sibling arms).
    bool TryParseGuardedValue(
        IReadOnlyList<IrNode> stmts,
        int index,
        IrExpression defaultValue,
        out IrExpression? guard,
        out IrExpression value,
        out int consumed,
        out bool shortCircuitsToDefault)
    {
        guard = null;
        value = null!;
        consumed = 0;
        shortCircuitsToDefault = false;

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
            shortCircuitsToDefault = true;
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

    static bool IsArmIntro(IrNode node, Scrutinee scrutinee, out int patternLocal, out TypeRef patternType, out IsInstance isInstance)
    {
        patternLocal = -1;
        patternType = null!;
        isInstance = null!;
        if (node is StoreLocal { Value: IsInstance { Operand: { } operand } test } store && scrutinee.Matches(operand))
        {
            patternLocal = store.Index;
            patternType = test.Type;
            isInstance = test;
            return true;
        }
        return false;
    }

    // An inline-positive arm: `if (scrutinee is Tk x) { MATCHED }` with no `else`.
    // The matched body is the `then` block; the no-match path falls through to the
    // following sibling statement.
    static bool IsInlineArm(IrNode node, Scrutinee scrutinee, out int patternLocal, out TypeRef patternType, out Block matchedBody)
    {
        patternLocal = -1;
        patternType = null!;
        matchedBody = null!;
        if (node is IfStatement { HasElse: false, Condition: IsPattern { Type: { } type, LocalIndex: var local, Value: { } operand }, Then: { } then }
            && scrutinee.Matches(operand))
        {
            patternLocal = local;
            patternType = type;
            matchedBody = then;
            return true;
        }
        return false;
    }

    // True when `node` is the first arm of a cascade over `scrutinee` — either an
    // intro-chain arm or an inline-positive arm. Used to confirm a temp store is
    // actually followed by a matching cascade before attempting the fold.
    static bool TestsScrutinee(IrNode node, Scrutinee scrutinee)
        => IsArmIntro(node, scrutinee, out _, out _, out _)
            || IsInlineArm(node, scrutinee, out _, out _, out _);

    // Recovers a direct (temp-less) scrutinee from a leading intro-chain arm that
    // reads a re-evaluable place directly (`Lk = place as T; if (!Lk) …`). The
    // all-inline form (a leading `if (place is T x)`) is deliberately not anchored
    // here: a bare inline `is` guard is idiomatically an `if`, not a switch, so
    // only cascades whose head csc lowered to an intro-chain arm are raised.
    static bool TryScrutineeFromDirectArm(IrNode node, out Scrutinee scrutinee)
    {
        scrutinee = default;
        if (node is StoreLocal { Value: IsInstance { Operand: { } introOperand } } && IsReEvaluablePlace(introOperand))
        {
            scrutinee = new Scrutinee(null, introOperand);
            return true;
        }
        return false;
    }

    // True when the direct scrutinee's place is a local written by an earlier
    // sibling statement — i.e. a switch-value temp. Such cascades belong to the
    // temp form (which consumes the store); the direct form must not steal them.
    static bool IsScrutineeStoredBefore(IReadOnlyList<IrNode> children, int index, Scrutinee scrutinee)
    {
        if (scrutinee.Place is not LoadLocal local)
            return false;
        for (int j = 0; j < index; j++)
        {
            if (children[j] is StoreLocal store && store.Index == local.Index)
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

    // A direct (temp-less) scrutinee's place must yield an identical value at each
    // arm's re-read. Reject (a) a store to the underlying local/argument anywhere
    // inside the cascade — a value change between arm tests — and (b) an address-of
    // the place ANYWHERE in the method: a live alias admits an indirect by-ref
    // mutation during the cascade that a consumed-only scan would miss. An
    // unrecognized place kind is refused outright.
    static bool DirectScrutineeStable(IrFunction function, IrExpression place, IReadOnlyCollection<IrNode> consumed)
    {
        switch (place)
        {
            case LoadLocal local:
                return !ConsumedContains(consumed, node => node is StoreLocal store && store.Index == local.Index)
                    && !function.Descendants.Any(node => node is LoadLocalAddress address && address.Index == local.Index);
            case LoadArgument argument:
                return !ConsumedContains(consumed, node => node is StoreArgument store && store.Index == argument.Index)
                    && !function.Descendants.Any(node => node is LoadArgumentAddress address && address.Index == argument.Index);
            default:
                return false;
        }
    }

    // A temp scrutinee (`StoreLocal SV = place`, its defining store consumed as
    // head) must be read-only across the cascade: no re-store of SV inside the arm
    // region, and no address-of SV anywhere in the consumed cascade. Either would
    // let a guard mutate the switch value between arm tests while the fold keeps a
    // single evaluation (and deletes SV's defining store).
    static bool TempScrutineeReadOnly(int tempLocal, IReadOnlyCollection<IrNode> region, IReadOnlyCollection<IrNode> consumed)
        => !ConsumedContains(region, node => node is StoreLocal store && store.Index == tempLocal)
            && !ConsumedContains(consumed, node => node is LoadLocalAddress address && address.Index == tempLocal);

    static bool ConsumedContains(IReadOnlyCollection<IrNode> roots, System.Func<IrNode, bool> predicate)
        => roots.Any(root => root.Descendants.Prepend(root).Any(predicate));

    static bool ReferencesLocalIn(IrExpression? expression, int local)
        => expression is not null && ReferenceOwnership.SubtreeReferencesLocal(expression, local);

    static bool DefaultEquals(IrExpression? left, IrExpression? right)
        => left is not null && right is not null && PlaceIdentity.SameOperand(left, right);

    static IEnumerable<int> PatternLocals(ArmData arm)
    {
        // Every intro/inline arm binds its match result to an always-present
        // outer local, rendered as a C# pattern variable or not. Scope validation
        // must see the unrendered local too (Fix A) so a sibling arm or the
        // default reading it is still caught. A negative sentinel means the arm
        // binds no outer local at all.
        if (arm.PatternLocal >= 0)
            yield return arm.PatternLocal;
        if (arm.Subpattern is { } sub)
            yield return sub.LocalIndex;
    }

    // [#3082 finding 1] A refutable (guarded or property-subpattern) non-last arm
    // routes its refinement failure to the default in the lowered cascade but to
    // the next arm in a switch expression. The rewrite is faithful only when no
    // later arm's pattern type can also match the value, which the oracle must
    // prove; absent a proof, decline (an unproven relationship is never disjoint).
    static bool RefutableArmsDisjointFromLaterArms(List<ArmData> arms, System.Func<TypeRef, TypeRef, bool>? disjoint)
    {
        for (int i = 0; i < arms.Count - 1; i++)
        {
            if (arms[i].Guard is null && arms[i].Subpattern is null)
                continue;
            for (int j = i + 1; j < arms.Count; j++)
            {
                if (disjoint is null || !disjoint(arms[i].PatternType, arms[j].PatternType))
                    return false;
            }
        }
        return true;
    }

    // [#3082 finding 2] A pattern variable is scoped to its own arm. A slot shared
    // by two arms, or a local read by a sibling arm's guard/value or by the
    // default, cannot be rendered as an arm-scoped pattern variable — decline.
    static bool PatternLocalsAreArmScoped(List<ArmData> arms, IrExpression defaultValue)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < arms.Count; i++)
        {
            foreach (int local in PatternLocals(arms[i]))
            {
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
}
