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
