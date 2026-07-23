namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a non-union type-pattern if/return dispatch over a plain receiver back
/// into a <c>switch</c> expression with a default arm. This is the shape csc
/// lowers a <c>value switch { T t when g =&gt; v, ..., _ =&gt; d }</c> to when the
/// receiver is an ordinary expression (not a discriminated-union <c>.Value</c>).
///
/// Two lowered shapes are recognized, both producing a
/// <see cref="PatternSwitchExpression"/>:
/// <list type="number">
///   <item>the nested <c>as</c>/null-test intro chain (<see cref="TryMatch"/>),
///   where a switch value is bound once to a temp, each arm's matched body trails
///   its <c>if (!Lk)</c> dispatch test, and the remaining arms nest in the
///   dispatch <c>then</c> — csc's form when an arm carries a single-level property
///   subpattern (<c>U { Prop: T inner }</c>) or a bound local outlives its test;</item>
///   <item>the flat inline cascade (<see cref="TryMatchInlineArms"/>), a run of
///   two or more <em>unguarded</em> <c>if (P is Tk xk) { return vk; }</c> arms over
///   one re-evaluable place ending in a bare <c>return &lt;default&gt;</c> — csc's
///   form when every arm's bound local is used only inside its own matched branch,
///   so <c>IsPatternPass</c> folds each arm to a positive <c>is</c> test.</item>
/// </list>
///
/// The pass is deliberately narrow. Both shapes require a re-evaluable switch
/// value (<c>LoadArgument</c>/<c>LoadLocal</c>) read only by the arm tests and not
/// reassigned across them, every arm a pure type test of that value, every
/// no-match path yielding the identical default the trailing <c>return</c> yields,
/// and each pattern-bound local referenced only within its own arm.
///
/// The intro-chain shape additionally admits guarded arms: a failed <c>when</c>
/// routing straight to the default (rather than the next arm) is valid precisely
/// because that shape is distinctively compiler-lowered (a spilled switch value
/// nesting its remaining arms), so csc's mutual-exclusivity proof stands behind it.
/// The flat inline shape carries no such proof — it is exactly what an ordinary
/// hand-written <c>if (P is T x)</c> ladder produces — so it folds only unguarded
/// arms, whose immediate-return-on-match preserves the ladder's top-to-bottom,
/// first-match-wins order unconditionally. For the same reason it additionally
/// requires pairwise non-subsumption: a ladder is valid however its arm types
/// overlap, but a <c>switch</c> expression rejects an arm an earlier one hides
/// (CS8510, e.g. <c>Base</c> before <c>Derived</c>). The inline fold proves
/// disjointness from metadata through the import-stamped
/// <see cref="IrFunction.TypeSubsumption"/> oracle and declines when any pair
/// subsumes or cannot be resolved.
/// </summary>
public sealed class PatternSwitchExpressionPass : IIrPass
{
    public string Name => "pattern-switch-expression";

    sealed record ArmData(TypeRef PatternType, int? LocalIndex, PropertySubpattern? Subpattern, IrExpression? Guard, IrExpression Value);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is not [var block])
                continue;

            if (TryMatch(function, block, out int startIndex, out var switchExpression))
            {
                FoldInto(block, startIndex, switchExpression!);
                context.Stepper.StepOver("raise nested type-pattern dispatch to switch expression", block);
            }
            else if (TryMatchInlineArms(function, block, out int inlineStart, out var inlineSwitch))
            {
                FoldInto(block, inlineStart, inlineSwitch!);
                context.Stepper.StepOver("raise inline type-pattern if/return dispatch to switch expression", block);
            }
        }
    }

    // Replace the recognized cascade — everything from `startIndex` to the end of
    // the block — with a single `return <switch>`.
    static void FoldInto(Block block, int startIndex, PatternSwitchExpression switchExpression)
    {
        block.SetChild(startIndex, new Return(switchExpression));
        for (int i = block.Children.Count - 1; i > startIndex; i--)
            block.Children[i].Detach();
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

    // The second recognized shape: a flat cascade of positive inline
    // `IsPattern` arms over one re-evaluable place, ending in a bare
    // `return <default>`:
    //
    //   if (P is T0 x0) { MATCHED0 }
    //   if (P is T1 x1) { MATCHED1 }
    //   ...
    //   return <default>;
    //
    // csc lowers a `P switch { T0 x0 => v0, T1 x1 => v1, ..., _ => d }` to this
    // when every arm's bound local is used only inside its own matched branch, so
    // `IsPatternPass` folds each `x = P as Tk; if (x != null) { ... }` arm into a
    // positive `P is Tk xk` test with the matched body nested in its `then`. The
    // remaining arms follow as siblings (not nested), which is why the intro-chain
    // parser above does not see them.
    //
    // Faithfulness: the arms are tried top-to-bottom exactly as the if/return
    // cascade already does, so an unguarded fold reorders nothing regardless of
    // type overlap — each match returns immediately, so first-match-wins is
    // preserved unconditionally. This path is deliberately restricted to unguarded,
    // single-level arms: unlike the compiler-lowered intro-chain shape, the flat
    // inline shape is exactly what a hand-written `if (P is T x)` ladder produces,
    // so it carries no proof that arm types are mutually exclusive, and a guarded
    // arm (whose guard-fail returns the shared default rather than falling through
    // to the next arm, as a `switch` would) could change which arm wins when types
    // overlap. Guarded and property-subpattern inline arms are therefore declined
    // here; the intro-chain matcher still handles them. Two arms are required: a
    // lone `if (P is T t) return v; return d;` is idiomatically an `if`, not a
    // switch, and folding it would be a broad, ambiguous reshape.
    bool TryMatchInlineArms(IrFunction function, Block block, out int startIndex, out PatternSwitchExpression? switchExpression)
    {
        startIndex = -1;
        switchExpression = null;

        var children = block.Children;
        // Need at least two arms plus the trailing default return.
        if (children.Count < 3)
            return false;

        // The default is the value the block's trailing `return` yields.
        if (children[^1] is not Return { Value: { } defaultValue })
            return false;

        // Locate the first inline arm; anything before it is left untouched.
        int firstArm = -1;
        IrExpression? switchValue = null;
        for (int i = 0; i < children.Count - 1; i++)
        {
            if (IsInlineArm(children[i], out var isPattern) && IsReEvaluablePlace(isPattern.Value))
            {
                firstArm = i;
                switchValue = isPattern.Value;
                break;
            }
        }
        if (firstArm < 0 || switchValue is null)
            return false;

        // Every statement from the first arm to the trailing default must be an
        // inline arm over the identical switch value, whose matched branch yields a
        // non-default value and whose every other path reaches the default.
        var arms = new List<ArmData>();
        for (int i = firstArm; i < children.Count - 1; i++)
        {
            if (!IsInlineArm(children[i], out var isPattern)
                || !PlaceIdentity.SameOperand(isPattern.Value, switchValue))
                return false;

            var matched = ((IfStatement)children[i]).Then.Children;
            if (!TryParseMatchedBody(matched, 0, isPattern.Type, isPattern.LocalIndex, defaultValue, out var arm, out int next)
                || !ReachesDefaultTail(matched, next, defaultValue))
                return false;

            // The inline path folds unguarded, single-level arms only. A guarded
            // inline arm is NOT safe to fold: unlike the compiler-lowered
            // intro-chain shape (a spilled switch value nesting its remaining arms),
            // the flat inline shape is exactly what an ordinary hand-written
            // `if (P is T x) { ... }` ladder produces, so it carries no proof that
            // the arm types are mutually exclusive. A `switch` routes a failed
            // `when` to the NEXT arm, but this shape's guard-fail returns the shared
            // default and exits; when an arm type overlaps a later arm (e.g. `string`
            // is both `IComparable` and `ICloneable`), folding would change which arm
            // wins. Unguarded arms have no such fall-through: each match returns
            // immediately, so the fold preserves the ladder's top-to-bottom,
            // first-match-wins order unconditionally. Property-subpattern arms carry
            // the same inner guard-fail routing and are likewise declined here; the
            // intro-chain matcher still handles both.
            if (arm.Guard is not null || arm.Subpattern is not null)
                return false;

            arms.Add(arm);
        }
        if (arms.Count < 2)
            return false;

        // Consumed nodes: the arm run plus the trailing default return.
        var consumed = children.Skip(firstArm).ToList();

        // The switch value must not be reassigned across the cascade, or the arm
        // tests would not all read the same value the fold evaluates once.
        if (!PlaceStableAcross(switchValue, consumed))
            return false;

        // Each pattern-bound local must be referenced only inside ITS OWN arm. A
        // switch-expression pattern variable is scoped to a single arm, so a later
        // arm reading an earlier arm's binding — legal while the ladder's `if`
        // keeps it alive — would reference an out-of-scope variable once folded.
        // Check the pattern's ACTUAL bound slot (from the `IsPattern`), not just a
        // local the arm body happens to use: an arm that binds but never reads its
        // local still introduces that name, so driving the check off the binding
        // closes the gap where an unused binding would skip scoping entirely.
        // Bounding each local to its arm's own subtree also subsumes the "not read
        // after the cascade" check.
        for (int j = 0; j < arms.Count; j++)
        {
            var armNode = children[firstArm + j];
            int boundLocal = ((IsPattern)((IfStatement)armNode).Condition).LocalIndex;
            if (boundLocal >= 0
                && !ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, boundLocal, [armNode]))
                return false;
        }

        // No earlier arm's type may subsume a later arm's. A first-match-wins
        // ladder is valid however its type tests overlap, but a `switch`
        // expression rejects an arm the compiler proves unreachable (CS8510):
        // `case Base` before `case Derived` is legal as an `if` ladder yet does
        // not compile as a `switch`. The intro-chain shape is trusted here because
        // it is distinctively compiler-lowered (csc never emits a subsumed
        // switch); the flat inline shape is also what a hand-written ladder
        // produces, so the fold must prove pairwise non-subsumption from metadata.
        // The oracle answers Yes/No only from resolved types; an unresolved link
        // (or no oracle, on synthetic/stage-dump paths) is treated as possibly
        // subsuming, so the ladder is left intact rather than reordered blindly.
        var subsumes = function.TypeSubsumption;
        if (subsumes is null)
            return false;
        for (int earlier = 0; earlier < arms.Count; earlier++)
        {
            for (int later = earlier + 1; later < arms.Count; later++)
            {
                if (subsumes(arms[earlier].PatternType, arms[later].PatternType) != MetadataFactState.No)
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
        startIndex = firstArm;
        return true;
    }

    // An inline arm: `if (P is Tk xk) { MATCHED }`, no `else`.
    static bool IsInlineArm(IrNode node, out IsPattern isPattern)
    {
        if (node is IfStatement { HasElse: false, Condition: IsPattern pattern })
        {
            isPattern = pattern;
            return true;
        }
        isPattern = null!;
        return false;
    }

    // Whether the switch value's variable is never reassigned within the consumed
    // cascade, so each arm test reads the value the fold evaluates once.
    static bool PlaceStableAcross(IrExpression place, IReadOnlyList<IrNode> consumed)
    {
        (bool isArgument, int index) = place switch
        {
            LoadArgument argument => (true, argument.Index),
            LoadLocal local => (false, local.Index),
            _ => (false, -1),
        };
        if (index < 0)
            return false;

        foreach (var root in consumed)
        {
            foreach (var node in root.Descendants.Prepend(root))
            {
                bool reassigns = isArgument
                    ? node is StoreArgument storeArgument && storeArgument.Index == index
                    : node is StoreLocal storeLocal && storeLocal.Index == index;
                if (reassigns)
                    return false;
            }
        }
        return true;
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
