using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TupleSwitchExpressionPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UInt32 = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Double = TypeRef.CoreLib("System", "Double");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    static Block Leaf(string value)
    {
        var block = new Block();
        block.Add(new Return(new Constant(value, String)));
        return block;
    }

    static Block Wrap(IfStatement inner)
    {
        var block = new Block();
        block.Add(inner);
        return block;
    }

    static Comparison GT(int argIndex, string name, int constant, TypeRef? type = null, bool isUnsigned = false, TypeRef? constantType = null)
        => new(ComparisonKind.GreaterThan, isUnsigned, new LoadArgument(argIndex, name, type ?? Int32), new Constant(constant, constantType ?? type ?? Int32));
    static Comparison LT(int argIndex, string name, int constant, TypeRef? type = null, bool isUnsigned = false, TypeRef? constantType = null)
        => new(ComparisonKind.LessThan, isUnsigned, new LoadArgument(argIndex, name, type ?? Int32), new Constant(constant, constantType ?? type ?? Int32));

    static IrFunction MakeFunction(IfStatement root, ImmutableArray<Parameter> parameters, ImmutableArray<TypeRef> localTypes = default)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(root);
        body.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(String, parameters, HasThis: false, GenericParameterCount: 0),
            localTypes.IsDefault ? [] : localTypes,
            body);
    }

    /// <summary>
    /// Builds the exact nested if/return comparison tree
    /// <see cref="ReturnDispatchPass"/> produces for LadderRung5.Quadrant's
    /// two-component tuple relational-pattern switch: testing `y` under BOTH
    /// the `x &gt; 0` and `x &lt; 0` branches lands two leaves that are fully
    /// determined (`x &gt; 0, y == 0` and `x &lt; 0, y == 0`) on the same
    /// default value as the truly partial `x == 0` leaf (`y` never tested).
    /// </summary>
    static IrFunction BuildQuadrantShapedTree()
    {
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 0), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 0), xPositive, xNegative);
        return MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32)]);
    }

    /// <summary>
    /// Same shape as <see cref="BuildQuadrantShapedTree"/>'s root
    /// <see cref="IfStatement"/> alone (not wrapped in a function), reading
    /// argument indices <paramref name="xArg"/>/<paramref name="yArg"/> and
    /// tagging every leaf value with <paramref name="suffix"/> so two
    /// instances built with disjoint argument indices can coexist in one
    /// function without colliding on either place or value.
    /// </summary>
    static IfStatement BuildQuadrantIfStatement(int xArg, int yArg, string suffix)
    {
        var xPositive = Wrap(new IfStatement(GT(yArg, "y", 0), Leaf("I" + suffix), Wrap(new IfStatement(LT(yArg, "y", 0), Leaf("IV" + suffix), Leaf("axis" + suffix)))));
        var xNegativeInner = Wrap(new IfStatement(GT(yArg, "y", 0), Leaf("II" + suffix), Wrap(new IfStatement(LT(yArg, "y", 0), Leaf("III" + suffix), Leaf("axis" + suffix)))));
        var xNegative = Wrap(new IfStatement(LT(xArg, "x", 0), xNegativeInner, Leaf("axis" + suffix)));

        return new IfStatement(GT(xArg, "x", 0), xPositive, xNegative);
    }

    static BlockContainer WrapInContainer(IfStatement root)
    {
        var container = new BlockContainer();
        var block = new Block();
        block.Add(root);
        container.Add(block);
        return container;
    }

    static BlockContainer EmptyContainer()
    {
        var container = new BlockContainer();
        container.Add(new Block());
        return container;
    }

    [Fact]
    public void Octant_FoldsToThreeComponentTupleSwitch()
    {
        // Compiler-backed breadth coverage: componentCount > 2 folds the same
        // way as LadderRung5.Quadrant's two-component tree.
        var function = Raised(nameof(CfgSampleClass.Octant));

        var tupleSwitch = Assert.Single(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.Equal(3, tupleSwitch.ComponentCount);
        Assert.Equal(5, tupleSwitch.Arms.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return (x, y, z) switch", output);
        Assert.Contains("(> 0, > 0, > 0) => \"+++\",", output);
        Assert.Contains("(> 0, > 0, < 0) => \"++-\",", output);
        Assert.Contains("(> 0, < 0, > 0) => \"+-+\",", output);
        Assert.Contains("(< 0, > 0, > 0) => \"-++\",", output);
        Assert.Contains("_ => \"other\",", output);
    }

    [Fact]
    public void UIntQuadrant_FoldsToTwoComponentUnsignedTupleSwitch()
    {
        // Compiler-backed positive: x and y are genuinely uint, so csc compiles
        // their </> as clt.un/cgt.un (Comparison.IsUnsigned = true against a
        // uint place). This proves the fold's signedness proof positively
        // recognizes a correctly-unsigned comparison against an unsigned
        // place, not merely declines every unsigned flag the way a blanket
        // rule (mirroring IsPatternPass's own decline) would.
        var function = Raised(nameof(CfgSampleClass.UIntQuadrant));

        var tupleSwitch = Assert.Single(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.Equal(2, tupleSwitch.ComponentCount);
        Assert.Equal(5, tupleSwitch.Arms.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return (x, y) switch", output);
        Assert.Contains("=> \"I\",", output);
        Assert.Contains("=> \"II\",", output);
        Assert.Contains("=> \"III\",", output);
        Assert.Contains("=> \"IV\",", output);
        Assert.Contains("_ => \"axis\",", output);
    }

    [Fact]
    public void TwoLocalFunctionQuadrants_BothFoldIndependently()
    {
        // Compiler-backed coverage for the completeness bug found in Gemini's
        // adversarial review of 23c34bae: TupleSwitchExpressionPass.Run()
        // returned right after its first successful container fold. Each
        // static local function here is raised through its own independent
        // recursive LocalFunctionRaisingPass pipeline run (a fresh Run() call
        // per local function, not a shared container list), so this proves
        // the fold generalizes across the whole method — both quadrants fold
        // to their own TupleSwitchExpression, and no eligible nested if/goto
        // tree survives in either local function or the host body.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.TwoLocalFunctionQuadrants));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        var switches = function!.Descendants.OfType<TupleSwitchExpression>().ToList();
        Assert.Equal(2, switches.Count);
        Assert.All(switches, s => Assert.Equal(2, s.ComponentCount));
        Assert.All(switches, s => Assert.Equal(5, s.Arms.Count));
        // No eligible nested if/return (or lowered if/goto) comparison tree
        // is left behind in either local function or the host body.
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.Equal(2, function.Descendants.OfType<LocalFunctionStatement>().Count());

        string output = result.Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("static string QuadrantA(int x, int y) => (x, y) switch", output);
        Assert.Contains("static string QuadrantB(int x, int y) => (x, y) switch", output);
        Assert.Contains("\"IA\"", output);
        Assert.Contains("\"IIA\"", output);
        Assert.Contains("\"IIIA\"", output);
        Assert.Contains("\"IVA\"", output);
        Assert.Contains("_ => \"axisA\"", output);
        Assert.Contains("\"IB\"", output);
        Assert.Contains("\"IIB\"", output);
        Assert.Contains("\"IIIB\"", output);
        Assert.Contains("\"IVB\"", output);
        Assert.Contains("_ => \"axisB\"", output);
        Assert.Contains("QuadrantA(x1, y1)", output);
        Assert.Contains("QuadrantB(x2, y2)", output);
    }

    [Fact]
    public void TwoIndependentDispatchContainers_BothFoldInOneRun()
    {
        // Regression for the completeness bug found in Gemini's adversarial
        // review of 23c34bae: TupleSwitchExpressionPass.Run() returned right
        // after its FIRST successful container fold, so a function with two
        // independently-eligible dispatch containers only ever raised one
        // tuple switch.
        //
        // Exhaustive investigation found no natural, compiler-emitted C#
        // shape that leaves two ReturnDispatchPass-eligible siblings
        // standing for a single TupleSwitchExpressionPass.Run() call to see:
        // an if-guarded try/finally's outer container is correctly declined
        // (the guard's true-target is the try/finally construct itself, not
        // a return); two sequential try/finally blocks where the first
        // always returns leave Roslyn to eliminate the second as dead code;
        // and any return of a value out of a protected region (try/finally
        // or try/catch) is always routed through a shared temp local rather
        // than a direct Return, which breaks the single-block/single-return
        // leaf shape ReturnDispatchPass and this pass both require. Loop
        // bodies fare no better: DoWhileLoopPass only carves out a loop
        // that actually has a back edge, and any block that isn't a pure
        // guard or a terminal return arm (an increment, or the loop's own
        // "keep iterating" fallthrough) makes ReturnDispatchPass decline the
        // whole container. So two hand-built, TryFinally-wrapped containers
        // — each in exactly the shape ReturnDispatchPass itself produces,
        // without needing ReturnDispatchPass to run — is the direct, precise
        // way to prove Run()'s own iteration completeness in isolation,
        // matching this file's established convention of driving the pass
        // directly off a hand-built tree for the other narrow declines below.
        var containerA = WrapInContainer(BuildQuadrantIfStatement(xArg: 0, yArg: 1, suffix: "A"));
        var containerB = WrapInContainer(BuildQuadrantIfStatement(xArg: 2, yArg: 3, suffix: "B"));

        var body = new BlockContainer();
        var block0 = new Block(0);
        block0.Add(new TryFinally(containerA, EmptyContainer()));
        body.Add(block0);
        var block1 = new Block(1);
        block1.Add(new TryFinally(containerB, EmptyContainer()));
        body.Add(block1);

        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(String, [new Parameter("x1", Int32), new Parameter("y1", Int32), new Parameter("x2", Int32), new Parameter("y2", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var switches = function.Descendants.OfType<TupleSwitchExpression>().ToList();
        Assert.Equal(2, switches.Count);
        Assert.All(switches, s => Assert.Equal(2, s.ComponentCount));
        Assert.All(switches, s => Assert.Equal(5, s.Arms.Count));
        // No eligible nested if tree remains in either container once fixed.
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void SignedPlaceWithUnsignedCompare_DeclinesFold()
    {
        // The headline signedness bug (#2867 follow-up): x is signed Int32,
        // but its comparisons are marked IsUnsigned = true. csc never emits
        // that for an int place (only uint/ulong/nuint compare unsigned), so
        // trusting the flag would silently reorder a negative x as if it
        // were large and positive when recompiled as `x is > 0`. The fold
        // must decline rather than emit that pattern.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 0, isUnsigned: true), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 0, isUnsigned: true), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void UnsignedPlaceWithIncompatibleSignedOrdering_DeclinesFold()
    {
        // x is genuinely UInt32 — csc always compiles a uint's </> unsigned
        // (clt.un/cgt.un) — but these comparisons are marked IsUnsigned =
        // false, an ordering csc never emits for a uint place. Accepting it
        // would emit `x is > 0` for a uint x, which recompiles UNSIGNED
        // (matching uint's own semantics), silently changing the ordering
        // the tree's actual (signed) comparisons used. Declines rather than
        // guesses which ordering was intended.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 0, type: UInt32), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 0, type: UInt32), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", UInt32), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void AnchorOutOfRangeForNarrowPlace_DeclinesFold()
    {
        // x is byte; the anchor constant 1000 does not fit byte's 0..255
        // range (matching real IL, where a byte comparison's constant stays
        // Int32-typed — TypedConstantsPass only retypes bool/char/enum
        // sinks). Emitting `x is > 1000` for a byte x is CS0031 (not an
        // implicit constant-expression conversion); the fold must decline
        // rather than emit an anchor the place's type cannot represent.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 1000, type: Byte, constantType: Int32), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 1000, type: Byte, constantType: Int32), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Byte), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void NegativeAnchorForUnsignedPlace_DeclinesFold()
    {
        // x is genuinely uint with correctly-unsigned comparisons (proving
        // this decline is independent of the signedness-match check above),
        // but the anchor constant is -1 — not implicitly convertible to uint
        // (CS0031), and if coerced would silently mean uint.MaxValue rather
        // than the tree's actual comparand. Declines rather than emits an
        // out-of-range anchor.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", -1, type: UInt32, isUnsigned: true), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", -1, type: UInt32, isUnsigned: true), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", UInt32), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void QuadrantShapedTree_MergesDuplicateDefaultValuedLeavesIntoOneArm()
    {
        var function = BuildQuadrantShapedTree();

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var tupleSwitch = Assert.Single(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.Equal(2, tupleSwitch.ComponentCount);
        // Exactly 5 arms: I, II, III, IV, and ONE merged default — not 7 (the
        // two fully-determined `y == 0` "axis" leaves must not survive as
        // their own explicit arms alongside the trailing default).
        Assert.Equal(5, tupleSwitch.Arms.Count);
        Assert.Equal(1, tupleSwitch.Arms.Count(arm => arm.IsDefault));
        Assert.Empty(function.Descendants.OfType<IfStatement>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return (x, y) switch", output);
        Assert.Contains("=> \"I\",", output);
        Assert.Contains("=> \"II\",", output);
        Assert.Contains("=> \"III\",", output);
        Assert.Contains("=> \"IV\",", output);
        Assert.Contains("_ => \"axis\",", output);
        // Only one line should produce "axis" — the merged default, not a
        // redundant explicit `(> 0, 0)`/`(< 0, 0)` arm.
        Assert.Single(output.Split('\n'), line => line.Contains("\"axis\""));
    }

    [Fact]
    public void AmbiguousDefaultValues_DeclinesFold()
    {
        // Two structurally-partial leaves (y never tested along either path)
        // with DIFFERENT values: there is no single default value the
        // trailing `_` could safely stand for, so the whole rewrite must
        // decline rather than guess which one is "the" default.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 0), Leaf("Q"), Leaf("R")));

        var root = new IfStatement(GT(0, "x", 0), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void EffectfulLeaf_DeclinesFold()
    {
        // A side-effecting prefix statement ahead of a leaf's Return makes the
        // Then block a two-statement block, which is outside the narrow
        // single-statement shape ReturnDispatchPass produces — the fold must
        // decline rather than drop or reorder the effect.
        var effectfulThen = new Block();
        effectfulThen.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        effectfulThen.Add(new Return(new Constant("I", String)));

        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), effectfulThen, Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(LT(0, "x", 0), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 0), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32)], [Int32]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void NonExhaustiveTree_DeclinesFold()
    {
        // Missing else on the innermost y-comparisons: the tree doesn't cover
        // y <= 0 (or y >= 0) at all, so it isn't the exhaustive shape the
        // fold requires.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), elseArm: null));
        var xNegative = Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), elseArm: null));

        var root = new IfStatement(GT(0, "x", 0), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void MixedInputTree_DeclinesFold()
    {
        // A third variable `z` is tested on only one path, so no leaf ever
        // constrains all three discovered components consistently with a
        // single shared default value — this is the "mixed input" shape the
        // fold must decline rather than mis-attribute to one component pair.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Leaf("axis")));
        var xNegative = Wrap(new IfStatement(GT(2, "z", 0), Leaf("II"), Leaf("axis")));

        var root = new IfStatement(GT(0, "x", 0), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Int32), new Parameter("y", Int32), new Parameter("z", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void FloatComponent_DeclinesFold()
    {
        // A float component: ordered/unordered float compares disagree on
        // NaN, so admitting a floating-point place is out of scope (mirrors
        // IsPatternPass's own float decline) — the fold must not raise this.
        var xPositive = Wrap(new IfStatement(GT(1, "y", 0), Leaf("I"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("IV"), Leaf("axis")))));
        var xNegativeInner = Wrap(new IfStatement(GT(1, "y", 0), Leaf("II"), Wrap(new IfStatement(LT(1, "y", 0), Leaf("III"), Leaf("axis")))));
        var xNegative = Wrap(new IfStatement(new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadArgument(0, "x", Double), new Constant(0.0, Double)), xNegativeInner, Leaf("axis")));

        var root = new IfStatement(new Comparison(ComparisonKind.GreaterThan, isUnsigned: false, new LoadArgument(0, "x", Double), new Constant(0.0, Double)), xPositive, xNegative);
        var function = MakeFunction(root, [new Parameter("x", Double), new Parameter("y", Int32)]);

        new TupleSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<TupleSwitchExpression>());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }
}
