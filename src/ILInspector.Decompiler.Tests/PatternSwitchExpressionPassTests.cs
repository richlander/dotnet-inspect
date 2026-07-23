using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Issue #3022: <see cref="PatternSwitchExpressionPass"/> raises the non-union
/// nested type-pattern <c>as</c>/null-test if/return cascade over a plain
/// receiver — with a single-level property-subpattern arm, <c>when</c> guards,
/// and a <c>_ =&gt; false</c> default — back into a <c>switch</c> expression.
///
/// Positive coverage is compiler-backed: a compiled fixture
/// (<see cref="PatternSwitchSample.Classify"/>) and the product method the raise
/// was written for (<c>YieldBreakLoopIteratorReconstruction.TryNormalizeContinueCondition</c>),
/// so the recognized shape is exactly what csc emits. The synthetic negatives
/// pin the safety guards that keep the fold from misfiring: every no-match and
/// guard-fail path must yield the identical default, and the switch value must
/// be a re-evaluable place read once.
/// </summary>
[Trait("Area", "Pass")]
public class PatternSwitchExpressionPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Node = TypeRef.CoreLib("Synthetic", "Node");
    static readonly TypeRef Leaf = TypeRef.CoreLib("Synthetic", "Leaf");

    static Constant False() => new(false, Bool);

    static IrFunction Raised(System.Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static void RunPass(IrFunction function) => new PatternSwitchExpressionPass().Run(function, PassContext.None);

    // ── Compiler-backed positives ──────────────────────────────────────────

    [Fact]
    public void CompiledFixture_RaisesToPatternSwitchExpression()
    {
        var function = Raised(typeof(PatternSwitchSample), nameof(PatternSwitchSample.Classify));

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(2, switchExpression.Arms.Count);

        var bare = switchExpression.Arms[0];
        Assert.Contains("Leaf", bare.PatternType.ToDisplayString());
        Assert.Null(bare.Subpattern);
        Assert.NotNull(bare.LocalIndex);
        Assert.True(bare.HasGuard);

        var subpatternArm = switchExpression.Arms[1];
        Assert.Contains("Wrapper", subpatternArm.PatternType.ToDisplayString());
        Assert.NotNull(subpatternArm.Subpattern);
        Assert.Equal("Inner", subpatternArm.Subpattern!.PropertyName);
        Assert.Contains("Leaf", subpatternArm.Subpattern.PatternType.ToDisplayString());
        Assert.True(subpatternArm.HasGuard);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return node switch", output);
        Assert.Contains("Leaf leaf when Exceeds(leaf.Weight, threshold) => Capture(leaf.Weight, out result),", output);
        Assert.Contains("Wrapper { Inner: Leaf inner } when Exceeds(inner.Weight, threshold) => Capture(-inner.Weight, out result),", output);
        Assert.Contains("_ => false,", output);
    }

    [Fact]
    public void ProductTargetMethod_SelfRaisesToPatternSwitchExpression()
    {
        // The method the raise was written for: import it straight from the
        // shipped decompiler assembly and prove its own lowering round-trips.
        var function = Raised(
            typeof(IrFunction).Assembly.GetType("ILInspector.Decompiler.Pipeline.YieldBreakLoopIteratorReconstruction", throwOnError: true)!,
            "TryNormalizeContinueCondition");

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.Contains("Comparison", switchExpression.Arms[0].PatternType.ToDisplayString());
        Assert.NotNull(switchExpression.Arms[1].Subpattern);
        Assert.Equal("Operand", switchExpression.Arms[1].Subpattern!.PropertyName);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return expression switch", output);
        Assert.Contains("Comparison comparison when ReadsLoopField(comparison.Left, loopFieldName)", output);
        // Issue #3033: both mutually-exclusive arms bind a source pattern variable
        // named `comparison`. Each arm is its own scope, so the second arm reuses
        // the source spelling rather than the printer's global name-dedup renaming
        // it to a synthetic `V_1`.
        Assert.Contains("LogicalNot { Operand: Comparison comparison } when ReadsLoopField(comparison.Left, loopFieldName)", output);
        Assert.DoesNotContain("Comparison V_", output);
        Assert.Contains("_ => false,", output);
    }

    // ── Compiler-backed positives: heterogeneous / inline arm intros (#3028) ─

    [Fact]
    public void HeterogeneousArmIntros_RaiseToPatternSwitchExpression()
    {
        // csc lowers this to a leading intro-chain arm (`Dot d = shape as Dot; if
        // (d is null) …`) plus inline-positive sibling arms (`if (shape is Bar b)
        // …`), over `shape` read directly (no switch-value temp). The pass folds
        // the whole heterogeneous cascade back into one switch expression.
        var function = Raised(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.Area));

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(3, switchExpression.Arms.Count);
        Assert.All(switchExpression.Arms, arm =>
        {
            Assert.Null(arm.Subpattern);
            Assert.False(arm.HasGuard);
            Assert.NotNull(arm.LocalIndex);
        });
        Assert.Contains("Dot", switchExpression.Arms[0].PatternType.ToDisplayString());
        Assert.Contains("Bar", switchExpression.Arms[1].PatternType.ToDisplayString());
        Assert.Contains("Box", switchExpression.Arms[2].PatternType.ToDisplayString());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return shape switch", output);
        Assert.Contains("Dot d => d.Radius,", output);
        Assert.Contains("Bar b => b.Length,", output);
        Assert.Contains("Box x => x.Side,", output);
        Assert.Contains("_ => -1,", output);
    }

    // ── Synthetic shape + negative guards ──────────────────────────────────

    // Minimal recognized shape (post-#3003 structuring): `sv = <place>; k = sv
    // as T; if (!k) { return <no-match default>; } [if (!g) { return <guard-fail
    // default>; }] return <value>;`. The matched body trails the dispatch test as
    // sibling statements; the no-match remainder (here just the default) nests in
    // the dispatch test's then-branch. A null guard omits the guard test.
    static IrFunction SingleArm(
        IrExpression switchValue,
        IrExpression? guard,
        IrExpression armValue,
        IrExpression noMatchDefault,
        IrExpression? guardFailDefault = null)
    {
        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, switchValue));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(5, Node))));

        var dispatchThen = new Block();
        dispatchThen.Add(new Return(noMatchDefault));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), dispatchThen, null));

        if (guard is not null)
        {
            var guardThen = new Block();
            guardThen.Add(new Return(guardFailDefault!));
            block.Add(new IfStatement(new LogicalNot(guard), guardThen, null));
        }

        block.Add(new Return(armValue));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Leaf, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_MatchingShape_Raises()
    {
        var function = SingleArm(
            switchValue: new LoadArgument(0, "node", Node),
            guard: null,
            armValue: new Constant(true, Bool),
            noMatchDefault: False());

        RunPass(function);

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Single(switchExpression.Arms);
        Assert.True(switchExpression.HasDefault);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_GuardFailYieldsNonDefault_DoesNotRaise()
    {
        // The no-match path yields false (the default), but the guard-fail path
        // yields 99. In a switch expression a failed `when` guard falls to the
        // default arm, so both no-match and guard-fail paths must yield the same
        // value; a divergent guard-fail value means this is not a faithful
        // switch and must be left as-is.
        var guard = new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);
        var function = SingleArm(
            switchValue: new LoadArgument(0, "node", Node),
            guard: guard,
            armValue: new Constant(true, Bool),
            noMatchDefault: False(),
            guardFailDefault: new Constant(99, Int32));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_SideEffectingSwitchValue_DoesNotRaise()
    {
        // The switch value is a method call, not a re-evaluable place. The
        // lowering reads the value once per arm test; folding a side-effecting
        // value into a single evaluation would change behavior, so decline.
        var effect = new MethodRef(Node, "Produce", Node, [], HasThis: false);
        var function = SingleArm(
            switchValue: new Call(effect, isVirtual: false, []),
            guard: null,
            armValue: new Constant(true, Bool),
            noMatchDefault: False());

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    static readonly TypeRef Twig = TypeRef.CoreLib("Synthetic", "Twig");

    // Builds a temp-less, direct-place cascade whose head is an intro-chain arm
    // (`L0 = place as Leaf; if (!L0) { REST } return 1;`) with `inlineArmCount`
    // inline-positive sibling arms nested in REST, bottoming out in the default.
    static IrFunction DirectIntroCascade(IrExpression place, int inlineArmCount)
    {
        var rest = new Block();
        for (int k = 0; k < inlineArmCount; k++)
        {
            var thenK = new Block();
            thenK.Add(new Return(new Constant(2 + k, Int32)));
            rest.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Twig, 1 + k), thenK, null));
        }
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, (IrExpression)place.Clone())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Twig, Node), container);
    }

    // Builds a temp-less, all-inline direct-place cascade (no intro-chain head):
    //   `if (place is Leaf a) { return 1; } if (place is Twig b) { return 2; }
    //   return <default>;`
    static IrFunction AllInlineCascade(IrExpression place)
    {
        var thenA = new Block();
        thenA.Add(new Return(new Constant(1, Int32)));
        var thenB = new Block();
        thenB.Add(new Return(new Constant(2, Int32)));

        var block = new Block(0);
        block.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Leaf, 0), thenA, null));
        block.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Twig, 1), thenB, null));
        block.Add(new Return(new Constant(-1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Node), container);
    }

    [Fact]
    public void Synthetic_DirectIntroPlusInlineSibling_Raises()
    {
        // Intro-chain head arm plus one inline-positive sibling arm over the same
        // re-evaluable place, no temp — the temp-less heterogeneous shape. Folds
        // to a two-arm switch.
        var function = DirectIntroCascade(new LoadArgument(0, "node", Node), inlineArmCount: 1);

        RunPass(function);

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_DirectIntroSingleArm_DoesNotRaise()
    {
        // A single intro-chain arm over a re-evaluable place is indistinguishable
        // from an ordinary `if (place is T t)` guard, which IsPatternPass renders
        // idiomatically. The direct (temp-less) form requires at least two arms,
        // so a lone guard is left as-is.
        var function = DirectIntroCascade(new LoadArgument(0, "node", Node), inlineArmCount: 0);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_AllInlineWithoutIntroHead_DoesNotRaise()
    {
        // Every arm is an inline-positive test with no leading intro-chain arm —
        // the shape a hand-written `if` chain lowers to, not a `switch`. PR A
        // anchors only on cascades whose head csc lowered to an intro-chain arm,
        // so this is left unfolded.
        var function = AllInlineCascade(new LoadArgument(0, "node", Node));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // Builds a direct-place cascade whose head is an intro-chain arm and whose one
    // inline sibling arm carries a `when` guard, in either the short-circuiting
    // negated form (`if (!G) return <default>; return V;`) or the fall-through
    // positive form (`if (G) return V;`):
    //   `L0 = place as Leaf; if (!L0) { if (place is Twig L1) { <guarded> }
    //    return <default>; } return 1;`
    static IrFunction DirectIntroGuardedInline(IrExpression place, bool shortCircuitToDefault)
    {
        IrExpression Guard() => new Call(new MethodRef(Twig, "Ok", Bool, [Twig], HasThis: false), isVirtual: false, [new LoadLocal(1, Twig)]);

        var armBody = new Block();
        if (shortCircuitToDefault)
        {
            // Negated form: guard failure returns the default immediately, skipping
            // any later arm that would still match the same value in a switch.
            var guardFail = new Block();
            guardFail.Add(new Return(new Constant(-1, Int32)));
            armBody.Add(new IfStatement(new LogicalNot(Guard()), guardFail, null));
            armBody.Add(new Return(new Constant(2, Int32)));
        }
        else
        {
            // Positive form: guard failure falls out of the `then` block to the
            // following sibling — the switch `when` fall-through semantics.
            var guardPass = new Block();
            guardPass.Add(new Return(new Constant(2, Int32)));
            armBody.Add(new IfStatement(Guard(), guardPass, null));
        }

        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Twig, 1), armBody, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, (IrExpression)place.Clone())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Node), container);
    }

    [Fact]
    public void Synthetic_InlineGuardShortCircuitsToDefault_DoesNotRaise()
    {
        // #3028 review (Gemini, Finding 1): an inline sibling arm whose guard
        // failure returns the default immediately (`if (!G) return <default>;`)
        // must not fold. Folding it to `Twig x when G => 2` makes a guard failure
        // fall through to later arms, but the IL short-circuited straight to the
        // default — divergent whenever a later arm matches the same value. Only
        // csc's fall-through guard shape is foldable, so this hand-shaped IL is
        // left alone.
        var function = DirectIntroGuardedInline(new LoadArgument(0, "node", Node), shortCircuitToDefault: true);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineGuardFallsThrough_DoesNotRaise()
    {
        // #3028 PR A scopes the newly recognized heterogeneous surface (a direct
        // scrutinee or any inline-positive arm) to UNGUARDED arms only. A guarded
        // arm whose failure short-circuits to the default folds faithfully only
        // when no later arm can match the same value, which needs a
        // type-disjointness oracle this SRM-only pass does not have. Even the safe
        // fall-through inline guard is therefore deferred to a follow-up; only the
        // unguarded new-surface shape folds under PR A.
        var function = DirectIntroGuardedInline(new LoadArgument(0, "node", Node), shortCircuitToDefault: false);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // Builds a direct-place cascade (intro head + one inline sibling) whose inline
    // arm value takes the address of the scrutinee place — the by-ref mutation
    // vector. `argAddress` selects an argument scrutinee (`&arg`) versus a local
    // scrutinee (`&local`) so both branches of the stability check are exercised.
    static IrFunction DirectIntroAddressOfScrutinee(bool argAddress)
    {
        IrExpression place = argAddress ? new LoadArgument(0, "node", Node) : new LoadLocal(7, Node);
        IrExpression address = argAddress ? new LoadArgumentAddress(0, "node", Node) : new LoadLocalAddress(7, Node);
        var mutate = new MethodRef(Node, "Mutate", Int32, [TypeRef.ByRef(Node)], HasThis: false);

        var armBody = new Block();
        armBody.Add(new Return(new Call(mutate, isVirtual: false, [address])));

        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Twig, 1), armBody, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, (IrExpression)place.Clone())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Node, Node, Node, Node, Node, Node), container);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Synthetic_DirectScrutineeAddressTaken_DoesNotRaise(bool argAddress)
    {
        // #3028 review (Gemini, Finding 2): a direct (temp-less) scrutinee is
        // re-read by every arm, but a switch expression reads it once. When the
        // cascade takes the address of the scrutinee's place (`&arg` / `&local`),
        // a by-ref call in a guard or value can mutate the value between arms, so
        // folding to a single read would diverge. The stability check rejects any
        // store or address-of the direct scrutinee inside the cascade.
        var function = DirectIntroAddressOfScrutinee(argAddress);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // Builds a direct-place cascade whose head is an intro-chain arm carrying a
    // short-circuiting `when` guard (`if (!G) return <default>;`), followed by one
    // inline sibling arm of `laterType`:
    //   `L0 = place as Leaf; if (!L0) { if (place is laterType L1) return 2;
    //    return -1; } if (!Guard(L0)) return -1; return 1;`
    static IrFunction DirectIntroShortCircuitPlusInline(IrExpression place, TypeRef laterType)
    {
        IrExpression Guard() => new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);

        var innerThen = new Block();
        innerThen.Add(new Return(new Constant(2, Int32)));
        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), laterType, 1), innerThen, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var guardFail = new Block();
        guardFail.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, (IrExpression)place.Clone())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new IfStatement(new LogicalNot(Guard()), guardFail, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Leaf, Node), container);
    }

    [Fact]
    public void Synthetic_IntroGuardShortCircuitOverlapsLaterArm_DoesNotRaise()
    {
        // #3028 review round 2 (GPT Finding A / Gemini Finding 1): a guarded
        // intro arm in the direct (temp-less) form whose guard failure
        // short-circuits to the default, followed by a later arm that can match
        // the same value (`Leaf` again, or any base type of `Leaf`). Folding it
        // makes the guard failure fall through to that later arm, whereas the IL
        // routed it to the default — divergent. PR A rejects the whole class by
        // declining any guarded arm on the new heterogeneous surface, so no
        // type-disjointness oracle is needed.
        var function = DirectIntroShortCircuitPlusInline(new LoadArgument(0, "node", Node), Leaf);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_IntroGuardDistinctLaterArmInDirectForm_DoesNotRaise()
    {
        // The same guarded intro arm with a distinct later type (`Twig`) is
        // semantically foldable, but PR A still declines it: the direct
        // (temp-less) form is the new heterogeneous surface, restricted to
        // unguarded arms. Guarded heterogeneous folding is deferred to a follow-up
        // that carries a real type-disjointness oracle. The pre-existing temp-form
        // intro cascade (#3022) keeps its guarded arms.
        var function = DirectIntroShortCircuitPlusInline(new LoadArgument(0, "node", Node), Twig);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // A direct argument-scrutinee cascade preceded by an aliasing store
    // (`ref9 = &arg;`) — the address escapes the cascade even though every arm
    // test re-reads `arg` directly.
    static IrFunction DirectCascadeWithArgAlias()
    {
        IrExpression Arg() => new LoadArgument(0, "node", Node);

        var innerThen = new Block();
        innerThen.Add(new Return(new Constant(2, Int32)));
        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern(Arg(), Twig, 1), innerThen, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(9, TypeRef.ByRef(Node), new LoadArgumentAddress(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, Arg())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Node, Node, Node, Node, Node, Node, Node, TypeRef.ByRef(Node)), container);
    }

    [Fact]
    public void Synthetic_DirectScrutineeAliasedBeforeCascade_DoesNotRaise()
    {
        // #3028 review round 2 (GPT, Finding B): the scrutinee argument's address
        // is taken into an alias BEFORE the cascade. A consumed-region-only scan
        // misses it, but a by-ref call through that alias inside a guard could
        // mutate `arg` between arm tests. The stability check scans the whole
        // method for an address-of the direct scrutinee and declines.
        var function = DirectCascadeWithArgAlias();

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // A temp-form cascade (`SV = arg;` then arms testing `SV`) whose inline arm
    // takes the address of the temp — `Mutate(ref SV)` — inside its value.
    static IrFunction TempCascadeWithTempAddress()
    {
        IrExpression Sv() => new LoadLocal(5, Node);
        var mutate = new MethodRef(Node, "Mutate", Int32, [TypeRef.ByRef(Node)], HasThis: false);

        var innerThen = new Block();
        innerThen.Add(new Return(new Call(mutate, isVirtual: false, [new LoadLocalAddress(5, Node)])));
        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern(Sv(), Twig, 1), innerThen, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, Sv())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Node, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_TempScrutineeAddressTaken_DoesNotRaise()
    {
        // #3028 review round 2 (GPT, Finding C): the temp-form ownership check
        // proves `SV` is referenced only inside the cascade, but an address-of
        // `SV` in an arm (`Mutate(ref SV)`) is such a reference — and the fold
        // deletes `SV`'s defining store, dangling it, while a by-ref mutation
        // would change the value later arms read. The read-only check declines.
        var function = TempCascadeWithTempAddress();

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }
}
