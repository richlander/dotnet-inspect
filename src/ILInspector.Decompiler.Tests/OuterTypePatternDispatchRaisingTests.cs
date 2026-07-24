using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Issue #3113: <see cref="PatternSwitchExpressionPass"/> raises the outer
/// type-pattern dispatch of <see cref="ScatteredReturnDispatchSample.Dispatch"/>
/// — the #2978 witness — all the way back to its original nested
/// <c>switch</c> expression. #3094 raised only the inner <c>s.Length</c>
/// comparison-chain arm; this closes the outer dispatch, which needs three new
/// recognizer capabilities the compiler emits here:
/// <list type="bullet">
/// <item>a <em>diamond intro arm</em> — the matched value lives in the <c>if</c>'s
///   <c>else</c> and the remaining arms in its <c>then</c> (csc's shape when an
///   arm value spans multiple blocks, here the nested switch expression);</item>
/// <item>a <em>value-type arm</em> — <c>if (!(o is int)) return default; i =
///   (int)o;</c>, an isinst test then a separate unbox.any bind with no isinst
///   intro local;</item>
/// <item>a <em>flipped-comparison guard</em> — a <c>when i &gt; 0</c> whose
///   failure lowers to <c>if (i &lt;= 0) return default;</c>, recovered by
///   negating the relational.</item>
/// </list>
/// The shared default <c>_ =&gt; Fail(out x)</c> is a method call reached by
/// several identical <c>return Fail(out x)</c> paths, so folding also needs
/// structural call identity (SameSinkValue), not just re-evaluable-place identity.
///
/// The compiler-backed positive is the witness itself. The synthetic negatives
/// pin the new discriminators: a value-type arm whose unbox type disagrees with
/// its isinst test, a guard whose failure diverges from the default, and a
/// diamond whose matched arm does not reach the shared default.
/// </summary>
[Trait("Area", "Pass")]
public class OuterTypePatternDispatchRaisingTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Node = TypeRef.CoreLib("Synthetic", "Node");
    static readonly TypeRef Leaf = TypeRef.CoreLib("Synthetic", "Leaf");

    static IrFunction Raised(System.Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return function;
    }

    static void RunPass(IrFunction function) => new PatternSwitchExpressionPass().Run(function, PassContext.None);

    // ── Compiler-backed positive: the #2978 witness ─────────────────────────

    [Fact]
    public void Witness_FullyRaisesToNestedSwitchExpression()
    {
        var function = Raised(typeof(ScatteredReturnDispatchSample), nameof(ScatteredReturnDispatchSample.Dispatch));

        // Outer type-pattern dispatch is a single pattern switch expression; no
        // if/statement residue survives.
        var patternSwitch = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(patternSwitch.HasDefault);
        Assert.Equal(2, patternSwitch.Arms.Count);

        // Arm 0: `string s` — unguarded reference arm whose value is the inner
        // (value-constant) switch expression raised by #3094.
        Assert.Contains("string", patternSwitch.Arms[0].PatternType.ToDisplayString());
        Assert.False(patternSwitch.Arms[0].HasGuard);
        Assert.Single(function.Descendants.OfType<SwitchExpression>());

        // Arm 1: `int i when i > 0` — the guarded value-type arm.
        Assert.Contains("int", patternSwitch.Arms[1].PatternType.ToDisplayString());
        Assert.True(patternSwitch.Arms[1].HasGuard);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return o switch", output);
        Assert.Contains("string s => s.Length switch { 0 => Fail(out x), 1 => Win(1, out x), _ => Fail(out x) },", output);
        Assert.Contains("int i when i > 0 => Win(i, out x),", output);
        Assert.Contains("_ => Fail(out x),", output);
    }

    // ── Synthetic builder mirroring the witness's outer dispatch ────────────

    // Builds the witness's outer temp-form cascade:
    //   V9 = <switch value>;
    //   L0 = V9 as Leaf;
    //   if (!L0) { if (!(V9 is <unboxType-as-testType>)) return <default>;
    //              L1 = (int)V9;
    //              if (L1 <= 0) return <guardFailDefault>;
    //              return Win(L1); }
    //   else     { return <leafValue> <plus optional dead tail> }
    // The Leaf arm is the diamond intro arm; the int arm is the value-type arm
    // with a flipped `L1 <= 0` guard. Knobs break exactly one invariant.
    static IrFunction DiamondValueTypeCascade(
        TypeRef valueTypeTest,
        IrExpression noMatchDefault,
        IrExpression guardFailDefault,
        IrExpression leafValue,
        bool elseDeadTail = false)
    {
        IrExpression Win() => new Call(
            new MethodRef(Node, "Win", Bool, [Int32], HasThis: false),
            isVirtual: false,
            [new LoadLocal(1, Int32)]);

        var vtDispatchThen = new Block();
        vtDispatchThen.Add(new Return(noMatchDefault));
        var vtDispatch = new IfStatement(
            new LogicalNot(new IsInstance(valueTypeTest, new LoadLocal(9, Node))),
            vtDispatchThen,
            null);

        var guardThen = new Block();
        guardThen.Add(new Return(guardFailDefault));
        var guard = new IfStatement(
            new Comparison(ComparisonKind.LessThanOrEqual, isUnsigned: false, new LoadLocal(1, Int32), new Constant(0, Int32)),
            guardThen,
            null);

        var then = new Block();
        then.Add(vtDispatch);
        then.Add(new StoreLocal(1, Int32, new UnboxAny(Int32, new LoadLocal(9, Node))));
        then.Add(guard);
        then.Add(new Return(Win()));

        var elseBlock = new Block();
        elseBlock.Add(new Return(leafValue));
        if (elseDeadTail)
            elseBlock.Add(new Return(new Constant(true, Bool)));

        var block = new Block(0);
        block.Add(new StoreLocal(9, Node, new LoadArgument(0, "o", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(9, Node))));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), then, elseBlock));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("o", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Int32, Bool, Node, Node, Node, Node, Node, Node, Node), container);
    }

    static Call Default() => new(new MethodRef(Node, "Fail", Bool, [], HasThis: false), isVirtual: false, []);

    [Fact]
    public void Synthetic_DiamondValueTypeGuard_Raises()
    {
        // All invariants intact: the value-type unbox matches its isinst test, the
        // guard failure returns the same call as the default, and the diamond's
        // else reaches the default. Folds to a two-arm switch expression.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool));

        RunPass(function);

        var patternSwitch = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, patternSwitch.Arms.Count);
        Assert.True(patternSwitch.HasDefault);
        Assert.Contains("Leaf", patternSwitch.Arms[0].PatternType.ToDisplayString());
        Assert.False(patternSwitch.Arms[0].HasGuard);
        Assert.Contains("int", patternSwitch.Arms[1].PatternType.ToDisplayString());
        Assert.True(patternSwitch.Arms[1].HasGuard);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_ValueTypeUnboxTypeDiffersFromTest_DoesNotRaise()
    {
        // The value-type arm's isinst tests `Leaf` but the unbox binds `int`. A
        // switch arm's pattern is one type; a test/unbox type disagreement is not
        // a value-type pattern bind, so IsValueTypeArm declines and the cascade
        // stays if/return.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Leaf,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_GuardFailDivergesFromDefault_DoesNotRaise()
    {
        // The value-type arm's `when` guard failure returns `Other()`, a different
        // call than the shared `Fail()` default. In a switch expression a failed
        // guard falls to the default arm, so a divergent guard-fail value means
        // the fold is not faithful — SameSinkValue distinguishes the two calls and
        // the cascade is left as-is.
        var other = new Call(new MethodRef(Node, "Other", Bool, [], HasThis: false), isVirtual: false, []);
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: other,
            leafValue: new Constant(true, Bool));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_DiamondElseDoesNotReachDefault_DoesNotRaise()
    {
        // The diamond's `else` (the Leaf arm value) is followed by a second
        // statement, so the matched body does not reach the shared default tail.
        // The arm value must be a single reachable run; an unreached tail means
        // this is not the diamond arm shape, so decline.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            elseDeadTail: true);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }
}
