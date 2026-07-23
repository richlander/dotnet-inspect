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
        Assert.Contains("LogicalNot { Operand: Comparison", output);
        Assert.Contains("_ => false,", output);
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

    // ── Inline cascade (issue #3028 PR A) ──────────────────────────────────

    // The second recognized shape: a flat run of positive inline
    // `if (P is Tk xk) { return vk; }` arms over one re-evaluable place, ending
    // in a bare `return <default>`. No switch-value store; each arm reads the
    // place directly. `IsPatternPass` produces this when every arm's bound local
    // is used only inside its own matched branch.
    static IrFunction InlineCascade(int parameterCount, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);

        var container = new BlockContainer();
        container.Add(block);
        var parameters = Enumerable.Range(0, parameterCount)
            .Select(i => new Parameter($"p{i}", Node))
            .ToImmutableArray();
        var signature = new MethodSignature(Node, parameters, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Leaf, Node, Node, Node, Node), container)
        {
            // Synthetic IR carries no MetadataSource, so stamp a disjointness
            // oracle the inline fold can consult: distinct synthetic types never
            // subsume, identical types do (an arm cannot precede itself in a
            // switch). Tests that need a subsuming pair override this.
            TypeSubsumption = (earlier, later) => earlier.Equals(later) ? MetadataFactState.Yes : MetadataFactState.No,
        };
    }

    static IfStatement InlineArm(IrExpression switchValue, TypeRef type, int localIndex, IrExpression armValue, IrExpression? guard = null, IrExpression? guardFailDefault = null)
    {
        var then = new Block();
        if (guard is not null)
        {
            var guardThen = new Block();
            guardThen.Add(new Return(armValue));
            then.Add(new IfStatement(guard, guardThen, null));
            then.Add(new Return(guardFailDefault!));
        }
        else
        {
            then.Add(new Return(armValue));
        }
        return new IfStatement(new IsPattern(switchValue, type, localIndex), then, null);
    }

    static LoadArgument Arg(int index) => new(index, $"p{index}", Node);

    [Fact]
    public void InlineCascade_CompiledFixture_RaisesToPatternSwitchExpression()
    {
        var function = Raised(typeof(InlinePatternSwitchSample), nameof(InlinePatternSwitchSample.Simplify));

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(4, switchExpression.Arms.Count);
        Assert.Contains("LocalRef", switchExpression.Arms[0].PatternType.ToDisplayString());
        Assert.Contains("ArgRef", switchExpression.Arms[1].PatternType.ToDisplayString());
        Assert.Contains("FieldRef", switchExpression.Arms[2].PatternType.ToDisplayString());
        Assert.Contains("ElementRef", switchExpression.Arms[3].PatternType.ToDisplayString());
        Assert.All(switchExpression.Arms, arm => Assert.Null(arm.Subpattern));
        Assert.All(switchExpression.Arms, arm => Assert.False(arm.HasGuard));

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return address switch", output);
        Assert.Contains("LocalRef local => new LocalRef(local.Index),", output);
        Assert.Contains("ArgRef arg => new ArgRef(arg.Index, arg.Name),", output);
        Assert.Contains("FieldRef field => new FieldRef(field.Field),", output);
        Assert.Contains("ElementRef element => element,", output);
        Assert.Contains("_ => null,", output);
    }

    [Fact]
    public void Synthetic_InlineTwoArms_Raises()
    {
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_InlineSingleArm_DoesNotRaise()
    {
        // A lone `if (P is T t) return v; return d;` is idiomatically an `if`,
        // not a switch; the inline fold requires two or more arms.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineDifferentReceivers_DoesNotRaise()
    {
        // Arm 1 tests p0 but arm 2 tests p1; there is no single switch value, so
        // the arms are not a switch and must not fold.
        var function = InlineCascade(
            2,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(1), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineInterruptedCascade_DoesNotRaise()
    {
        // A non-arm statement between two inline arms breaks the cascade: the run
        // is not a contiguous dispatch, so decline.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            new StoreLocal(2, Node, new Constant(null, Node)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineGuardFailYieldsNonDefault_DoesNotRaise()
    {
        // The first arm's guard-fail path returns 99 rather than the shared
        // default; in a switch expression a failed `when` falls to the default, so
        // a divergent guard-fail value means this is not a faithful switch.
        var guard = new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf), guard, guardFailDefault: new Constant(99, Int32)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineGuardedArmYieldsDefault_DoesNotRaise()
    {
        // Even a guarded arm whose guard-fail yields the SHARED default must not
        // fold on the inline path. A `switch` routes a failed `when` to the next
        // arm, but this flat ladder's guard-fail returns the default and exits; the
        // flat shape (unlike the compiler-lowered intro chain) carries no proof the
        // arm types are disjoint, so folding could change which arm wins when types
        // overlap. The inline path folds unguarded arms only.
        var guard = new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf), guard, guardFailDefault: new Constant(null, Node)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineCrossArmLocalReference_DoesNotRaise()
    {
        // Arm 1 binds and uses local 0; arm 2 ALSO reads local 0 (arm 1's pattern
        // variable). In the if-ladder that binding outlives its `if`, but a switch
        // arm's pattern variable is scoped to its own arm, so folding would emit an
        // out-of-scope reference. The per-arm scope check must decline.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(0, Node)),
            new Return(new Constant(null, Node)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void GuardedOverlap_CompiledFixture_DoesNotRaise()
    {
        // Compiled canary: csc emits the same flat inline `is` cascade as the
        // foldable cases, but the first arm carries a guard and its type overlaps a
        // later arm, so the fold must decline and leave the if-ladder intact.
        var function = Raised(typeof(InlinePatternSwitchSample), nameof(InlinePatternSwitchSample.GuardedOverlap));

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_InlineSubsumingArm_DoesNotRaise()
    {
        // The oracle reports the earlier arm's type subsumes the later's, so the
        // equivalent switch would emit an unreachable arm (CS8510). The fold must
        // decline and leave the first-match-wins if-ladder intact.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));
        function.TypeSubsumption = (earlier, later) =>
            earlier.Equals(Leaf) && later.Equals(Node) ? MetadataFactState.Yes : MetadataFactState.No;

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineUnresolvedSubsumption_DoesNotRaise()
    {
        // The oracle cannot resolve the arm types' relationship. A `No` is required
        // to fold; an `Unknown` is treated as possibly subsuming, so the fold
        // declines rather than reorder the ladder on an unproven disjointness.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));
        function.TypeSubsumption = (_, _) => MetadataFactState.Unknown;

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_InlineNoOracle_DoesNotRaise()
    {
        // With no MetadataSource-stamped oracle (synthetic/stage-dump paths), the
        // fold cannot prove disjointness and must decline.
        var function = InlineCascade(
            1,
            InlineArm(Arg(0), Leaf, 0, new LoadLocal(0, Leaf)),
            InlineArm(Arg(0), Node, 1, new LoadLocal(1, Node)),
            new Return(new Constant(null, Node)));
        function.TypeSubsumption = null;

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Subsumed_CompiledFixture_DoesNotRaise()
    {
        // Compiled reproduction of CS8510: an earlier `Shape` arm subsumes a later
        // `Circle : Shape` arm. Valid as an if-ladder, invalid as a switch. The
        // metadata subsumption oracle resolves the same-assembly base relationship
        // and the fold declines.
        var function = Raised(typeof(InlinePatternSwitchSample), nameof(InlinePatternSwitchSample.Subsumed));

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }
}
