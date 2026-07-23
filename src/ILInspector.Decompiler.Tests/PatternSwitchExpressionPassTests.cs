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
    static readonly TypeRef Outer = TypeRef.CoreLib("Synthetic", "Outer");
    static readonly TypeRef Inner = TypeRef.CoreLib("Synthetic", "Inner");

    static Constant False() => new(false, Bool);

    static IrFunction Raised(System.Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        // Match the product path: wire the cross-method import seam and the
        // type-disjointness oracle (from the assembly's open metadata) so the
        // disjointness-gated switch-expression raise fires exactly as it does when
        // the shipped `member` command renders this method.
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return function;
    }

    static void RunPass(IrFunction function) => new PatternSwitchExpressionPass().Run(function, PassContext.None);

    static void RunPass(IrFunction function, System.Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint)
        => new PatternSwitchExpressionPass().Run(
            function,
            new PassContext(new Stepper(enabled: false), typesProvablyDisjoint: typesProvablyDisjoint));

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

    [Fact]
    public void GuardedHeterogeneousArm_RaisesOnlyWhenProvablyDisjoint()
    {
        // The direct/inline surface (#3028) extended to a refutable arm: the FIRST
        // arm is `when`-guarded (`Dot d when d.Radius > min`) over a type disjoint
        // from every later arm. csc lowers the guarded arm to an `if (d is null)
        // { REST } else { MATCHED }` dispatch whose default is a shared trailing
        // return; a Dot that fails the guard is routed to that default. Folding is
        // faithful ONLY because Dot is provably disjoint from Bar and Box, so it
        // depends on the type-disjointness oracle (#3082's AreProvablyDisjoint):
        // Raised wires the assembly's real oracle and the guarded arm folds.
        var function = Raised(typeof(HeterogeneousArmSample), nameof(HeterogeneousArmSample.GuardedArea));

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(3, switchExpression.Arms.Count);
        Assert.True(switchExpression.Arms[0].HasGuard);
        Assert.Contains("Dot", switchExpression.Arms[0].PatternType.ToDisplayString());
        Assert.False(switchExpression.Arms[1].HasGuard);
        Assert.False(switchExpression.Arms[2].HasGuard);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return shape switch", output);
        Assert.Contains("Dot d when d.Radius > min => d.Radius,", output);
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
    public void Synthetic_InlineGuardFallsThroughFinalArm_Raises()
    {
        // A guarded inline arm in the safe fall-through form (`if (G) { return V; }`)
        // that is the LAST arm folds without any disjointness proof: its guard
        // failure routes to the default in the lowered cascade AND in a switch
        // expression (there is no later arm to catch it), so the fold is faithful.
        // RefutableArmsDisjointFromLaterArms only constrains a refutable NON-last
        // arm, so this raises even with no oracle wired — exercising that the
        // "non-last" qualifier is load-bearing.
        var function = DirectIntroGuardedInline(new LoadArgument(0, "node", Node), shortCircuitToDefault: false);

        RunPass(function);

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
        Assert.False(switchExpression.Arms[0].HasGuard);
        Assert.True(switchExpression.Arms[1].HasGuard);
    }

    // Builds a direct-place (temp-less) cascade whose FIRST arm is a `when`-guarded
    // intro arm in csc's if/else lowering — `k0 = place as Leaf; if (!k0) { REST }
    // else { if (Ok(k0)) return 1; } return <default>;` — followed by a later
    // inline Twig arm in REST. The guarded Leaf arm PRECEDES the Twig arm, so a
    // Leaf that fails its guard routes to the default in the lowering but would
    // fall to the Twig arm in a switch: faithful only if Leaf and Twig are disjoint.
    static IrFunction DirectGuardedIfElseCascade()
    {
        IrExpression Place() => new LoadArgument(0, "node", Node);
        IrExpression Guard() => new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);

        // MATCHED (else): positive guard, fall-through to the shared default.
        var guardThen = new Block();
        guardThen.Add(new Return(new Constant(1, Int32)));
        var matched = new Block();
        matched.Add(new IfStatement(Guard(), guardThen, null));

        // REST (then): inline Twig arm + trailing default.
        var twigThen = new Block();
        twigThen.Add(new Return(new Constant(2, Int32)));
        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern(Place(), Twig, 1), twigThen, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, Place())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, matched));
        block.Add(new Return(new Constant(-1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig), container);
    }

    [Fact]
    public void Synthetic_DirectGuardedNonLastArm_RaisesOnlyWhenProvablyDisjoint()
    {
        // The #3028 load-bearing case on the direct/inline surface: a `when`-guarded
        // intro arm (csc's if/else lowering) that PRECEDES a later arm. A Leaf that
        // fails its guard routes to the default in the cascade but would fall to the
        // Twig arm in a switch — faithful only if Leaf and Twig cannot both match.
        // The oracle must PROVE disjointness; absent or negative never folds.

        // No oracle wired → decline.
        var noOracle = DirectGuardedIfElseCascade();
        RunPass(noOracle);
        Assert.Empty(noOracle.Descendants.OfType<PatternSwitchExpression>());

        // Oracle that cannot prove disjointness → decline.
        var refuses = DirectGuardedIfElseCascade();
        RunPass(refuses, (_, _) => false);
        Assert.Empty(refuses.Descendants.OfType<PatternSwitchExpression>());

        // Oracle proving the two distinct arm types disjoint → raise.
        var proves = DirectGuardedIfElseCascade();
        RunPass(proves, (a, b) => !a.Equals(b));
        var switchExpression = Assert.Single(proves.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
        Assert.True(switchExpression.Arms[0].HasGuard);
        Assert.False(switchExpression.Arms[1].HasGuard);
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

    // Builds a direct-place cascade whose head is an intro-chain arm carrying a
    // single-level property subpattern (`Outer { Inner: Leaf }`), followed by one
    // inline sibling arm of the SAME outer type (`Outer`):
    //   `L0 = place as Outer; if (!L0) { if (place is Outer) return 2; return -1; }
    //    L1 = L0.Inner as Leaf; if (L1) return 1; return -1;`
    // A failed subpattern (`Inner` not `Leaf`) routes straight to the default,
    // exactly as a failed `when` guard does.
    static IrFunction DirectSubpatternPlusInline(IrExpression place)
    {
        var accessor = new MethodRef(Outer, "get_Inner", Inner, [], HasThis: true);

        var innerThen = new Block();
        innerThen.Add(new Return(new Constant(2, Int32)));
        var rest = new Block();
        rest.Add(new IfStatement(new IsPattern((IrExpression)place.Clone(), Outer, 5), innerThen, null));
        rest.Add(new Return(new Constant(-1, Int32)));

        var subThen = new Block();
        subThen.Add(new Return(new Constant(1, Int32)));

        var block = new Block(0);
        block.Add(new StoreLocal(0, Outer, new IsInstance(Outer, (IrExpression)place.Clone())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Outer)), rest, null));
        block.Add(new StoreLocal(1, Leaf, new IsInstance(Leaf, new LoadProperty(accessor, new LoadLocal(0, Outer), []))));
        block.Add(new IfStatement(new LoadLocal(1, Leaf), subThen, null));
        block.Add(new Return(new Constant(-1, Int32)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("node", Outer)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Outer, signature, ImmutableArray.Create(Outer, Leaf), container);
    }

    [Fact]
    public void Synthetic_DirectSubpatternOverlapsLaterArm_DoesNotRaise()
    {
        // A property-subpattern arm (`Outer { Inner: Leaf }`) whose inner match
        // fails routes to the default just like a failed guard; folding it with a
        // later arm of the SAME outer type (`Outer`) would make that failure fall
        // through to the `Outer` arm instead — divergent. The two arms share the
        // outer type `Outer`, so they are NOT disjoint: even an oracle that proves
        // every DISTINCT pair disjoint declines here, because `Outer` is not
        // disjoint from `Outer`.
        var noOracle = DirectSubpatternPlusInline(new LoadArgument(0, "node", Outer));
        RunPass(noOracle);
        Assert.Empty(noOracle.Descendants.OfType<PatternSwitchExpression>());

        var distinctPairsDisjoint = DirectSubpatternPlusInline(new LoadArgument(0, "node", Outer));
        RunPass(distinctPairsDisjoint, (a, b) => !a.Equals(b));
        Assert.Empty(distinctPairsDisjoint.Descendants.OfType<PatternSwitchExpression>());
    }

    // ── #3082 soundness gates on the temp-form intro cascade ───────────────

    // Temp-form two-arm cascade whose FIRST arm carries a short-circuiting `when`
    // guard (`if (!G) return <default>;`) and whose second arm is a plain later
    // type: `SV = arg; k0 = SV as Leaf; if (!k0) { k1 = SV as Twig; if (!k1)
    // return false; return V1(k1); } if (!G(k0)) return false; return V0(k0);`.
    // A guard failure routes to the default in the lowering but would fall to the
    // Twig arm in a switch — faithful only if Leaf and Twig cannot both match.
    static IrFunction TwoArmTempGuardedFirst()
    {
        IrExpression Sv() => new LoadLocal(5, Node);
        IrExpression Guard() => new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);

        var arm1NoMatch = new Block();
        arm1NoMatch.Add(new Return(False()));
        var rest = new Block();
        rest.Add(new StoreLocal(1, Twig, new IsInstance(Twig, Sv())));
        rest.Add(new IfStatement(new LogicalNot(new LoadLocal(1, Twig)), arm1NoMatch, null));
        rest.Add(new Return(new Call(new MethodRef(Twig, "V1", Bool, [Twig], HasThis: false), isVirtual: false, [new LoadLocal(1, Twig)])));

        var guardFail = new Block();
        guardFail.Add(new Return(False()));

        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, Sv())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new IfStatement(new LogicalNot(Guard()), guardFail, null));
        block.Add(new Return(new Call(new MethodRef(Leaf, "V0", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)])));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_TempGuardedNonLastArm_RaisesOnlyWhenProvablyDisjoint()
    {
        // #3082 finding 1: a refutable (guarded) non-last arm folds faithfully
        // only when no later arm's type can match the same value. The oracle must
        // PROVE it; an absent or negative oracle is never treated as disjoint.

        // No oracle wired → decline.
        var noOracle = TwoArmTempGuardedFirst();
        RunPass(noOracle);
        Assert.Empty(noOracle.Descendants.OfType<PatternSwitchExpression>());

        // Oracle that cannot prove disjointness → decline.
        var refuses = TwoArmTempGuardedFirst();
        RunPass(refuses, (_, _) => false);
        Assert.Empty(refuses.Descendants.OfType<PatternSwitchExpression>());

        // Oracle proving the two distinct arm types disjoint → raise.
        var proves = TwoArmTempGuardedFirst();
        RunPass(proves, (a, b) => !a.Equals(b));
        var switchExpression = Assert.Single(proves.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
        Assert.True(switchExpression.Arms[0].HasGuard);
    }

    // Temp-form two-arm cascade whose FIRST arm binds an isinst local it never
    // renders (`k0` unused by the arm's own value), but whose default READS that
    // local: `SV = arg; k0 = SV as Leaf; if (!k0) { k1 = SV as Twig; if (!k1)
    // return k0; return true; } return true;`. Both arms are unguarded, so only
    // the arm-scoping gate (with Fix A's always-present local) catches the
    // default's read of the first arm's unrendered pattern local. The default is
    // a bare place load so it round-trips the default-equality check.
    static IrFunction TwoArmTempUnrenderedDefaultLeak()
    {
        IrExpression Sv() => new LoadLocal(5, Node);
        IrExpression Leak() => new LoadLocal(0, Leaf);

        var arm1NoMatch = new Block();
        arm1NoMatch.Add(new Return(Leak()));
        var rest = new Block();
        rest.Add(new StoreLocal(1, Twig, new IsInstance(Twig, Sv())));
        rest.Add(new IfStatement(new LogicalNot(new LoadLocal(1, Twig)), arm1NoMatch, null));
        rest.Add(new Return(new Constant(true, Bool)));

        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, Sv())));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(true, Bool)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Twig, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_UnrenderedPatternLocalReadByDefault_DoesNotRaise()
    {
        // #3082 finding 2 + Fix A: the first arm never renders its own isinst
        // local, so LocalIndex is null; but the default reads it. Rendering the
        // fold would emit `_ => k0` with k0 out of scope. The always-present
        // PatternLocal keeps the unrendered local visible to the scope check.
        var function = TwoArmTempUnrenderedDefaultLeak();

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_TempScrutineeReadInArmValue_DoesNotRaise()
    {
        // #3082 Fix D: the fold deletes the switch-value temp's defining store, so
        // a read of that temp in an arm value would observe default(T). The
        // read-only check rejects stores/address-of the temp, not a misplaced
        // load, so this dedicated gate declines the read.
        var function = SingleArm(
            switchValue: new LoadArgument(0, "node", Node),
            guard: null,
            armValue: new Call(new MethodRef(Node, "Use", Bool, [Node], HasThis: false), isVirtual: false, [new LoadLocal(5, Node)]),
            noMatchDefault: False());

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_GoverningValueAliasesRenderedPatternLocal_DoesNotRaise()
    {
        // #3082 Fix E: the switch value is re-read from local 0, which the arm
        // also binds as its rendered pattern variable. Folding would emit
        // `V_0 switch { Leaf V_0 => Use(V_0) }` — the governing name collides with
        // the arm's pattern variable and is out of scope. Decline.
        var function = SingleArm(
            switchValue: new LoadLocal(0, Leaf),
            guard: null,
            armValue: new Call(new MethodRef(Leaf, "Use", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            noMatchDefault: False());

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }
}
