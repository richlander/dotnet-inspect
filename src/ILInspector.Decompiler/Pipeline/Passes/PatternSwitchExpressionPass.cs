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

    sealed record ArmData(TypeRef PatternType, int? LocalIndex, PropertySubpattern? Subpattern, IrExpression? Guard, IrExpression Value, bool IsInline);

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
                if (TryFold(function, children, i + 1, scrutinee, svValue, new IrNode[] { svStore }, minArms: 1, out switchExpression))
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
                && TryFold(function, children, i, directScrutinee, directScrutinee.Place, System.Array.Empty<IrNode>(), minArms: 2, out switchExpression))
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
        if (!TryParseChain(region, 0, scrutinee, defaultValue, arms) || arms.Count < minArms)
            return false;

        // PR A (#3028) scopes the newly recognized heterogeneous surface — a
        // direct (temp-less) scrutinee, or any inline-positive sibling arm — to
        // unguarded arms. A guarded arm whose guard failure short-circuits to the
        // trailing default folds faithfully only when no later arm can match the
        // same value; in the folded switch a failed `when` guard falls through to
        // the later arms instead. Proving no overlap needs a type-disjointness
        // oracle this SRM-only, no-inspected-assembly-loading pass does not have.
        // The pre-existing temp-form intro cascade (#3022) keeps its guarded arms
        // under the compiler's proven mutual exclusivity; guarded heterogeneous
        // arms are deferred to a follow-up that carries a real disjointness oracle.
        bool isNewSurface = scrutinee.TempLocal is null || arms.Any(a => a.IsInline);
        if (isNewSurface && arms.Any(a => a.Guard is not null))
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
    // Two arm shapes are recognized and may be mixed:
    //   * Intro-chain arm: `intro Lk = isinst Tk(SV); if (!Lk) { REST } MATCHED`
    //     where MATCHED (this arm's guard + value) trails the dispatch test as
    //     sibling statements and REST (the remaining arms, ultimately a bare
    //     `return <default>`) nests inside the test's `then` branch. The dispatch
    //     test carries no `else`: its `then` always returns, so csc drops it.
    //   * Inline-positive arm: `if (SV is Tk x) { MATCHED }` whose no-match falls
    //     straight through to the following sibling statement (the next arm, or the
    //     bare `return <default>`).
    bool TryParseChain(IReadOnlyList<IrNode> stmts, int index, Scrutinee scrutinee, IrExpression defaultValue, List<ArmData> arms)
    {
        if (index >= stmts.Count)
            return false;

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
            // Inline arms are the newly recognized heterogeneous surface; PR A
            // (#3028) folds them only unguarded (the guard gate in TryFold rejects
            // a guarded new-surface fold). Tag the arm inline so that gate applies.
            arms.Add(new ArmData(inlineType, inlineIndex, Subpattern: null, inlineGuard, inlineValue, IsInline: true));
            return TryParseChain(stmts, index + 1, scrutinee, defaultValue, arms);
        }

        if (!IsArmIntro(stmts[index], scrutinee, out int patternLocal, out var patternType, out _))
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
        return TryParseChain(dispatch.Then.Children, 0, scrutinee, defaultValue, arms);
    }

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

            // Intro-chain arm: no-match nests in the dispatch test's `then` branch.
            if (IsArmIntro(stmts[index], scrutinee, out int local, out _, out _)
                && index + 1 < stmts.Count
                && stmts[index + 1] is IfStatement { HasElse: false } dispatch
                && IsNegatedLocalTest(dispatch.Condition, local))
            {
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
            arm = new ArmData(patternType, outerLocal, subpattern, innerGuard, innerValue, IsInline: false);
            nextIndex = index + 2;
            return true;
        }

        // Bare type-pattern arm: a guarded (or unguarded) value run.
        if (!TryParseGuardedValue(stmts, index, defaultValue, out var guard, out var value, out int consumed, out _))
            return false;
        int? localIndex = ReferencesLocalIn(guard, patternLocal) || ReferenceOwnership.SubtreeReferencesLocal(value, patternLocal)
            ? patternLocal
            : null;
        arm = new ArmData(patternType, localIndex, Subpattern: null, guard, value, IsInline: false);
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
        if (arm.LocalIndex is { } outer)
            yield return outer;
        if (arm.Subpattern is { } sub)
            yield return sub.LocalIndex;
    }
}
