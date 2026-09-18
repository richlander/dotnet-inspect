using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class OrderedBinarySpillTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.OrderedBinarySpillSamples";
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Counter");
    static readonly string[] Methods =
    [
        "Conditional", "ReturnConditional", "UncheckedConditional", "Signed64", "Unsigned64",
        "Subtract", "Multiply", "SnapshotAcrossMutation", "EffectfulOperands",
        "LocalDestination",
    ];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompilerProducedConditionalSpillsRetireBeforePrinting(bool updated, bool lowered)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        foreach (string method in Methods)
        {
            var function = IrImporter.Import(source, FixtureType, method);
            Assert.NotNull(function);
            IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default,
                PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
            Assert.Empty(CoercionInvariant.Check(function));
            function.CheckInvariant(includeSemantics: true);
            string output = CSharpPrinter.Print(function).Output!;
            Assert.DoesNotContain("S_", output);
            if (method == "Conditional")
                Assert.Contains("checked { value += (choose ? unchecked(amount + 1) : (amount * 2)); }", output);
        }
    }

    [Fact]
    public void RuntimeTreeSizeUsesTheSameOrderedBinaryRaise()
    {
        using var source = MetadataSource.Open(typeof(System.Data.DataTable).Assembly.Location);
        var function = IrImporter.Import(source, "System.Data.RBTree`1", "RecomputeSize");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("S_", output);
        Assert.Contains("SubTreeSize(Left(nodeId)) + SubTreeSize(Right(nodeId)) + (", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedConditionalRemainsOutsideTheSingleBlockFold(bool updated)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        var function = IrImporter.Import(source, FixtureType, "NestedConditional");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("if (!first)", output);
        Assert.Contains("checked(S_0 + S_1)", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedCallsFoldAsAUnitAndKeepTheirOrigin(bool slotsOnly)
    {
        var first = Effect("First");
        var second = Effect("Second");
        var function = Function(
            new StoreStackSlot(0, first),
            new StoreStackSlot(1, second),
            Store(new LoadStackSlot(0, Int32), new LoadStackSlot(1, Int32)));
        var pass = new ExpressionInliningPass(slotsOnly);

        pass.Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        var binary = Assert.Single(function.Descendants.OfType<Binary>());
        Assert.Same(first, binary.Left);
        Assert.Same(second, binary.Right);
        Assert.True(binary.IsChecked);
        IrNode[] once = [.. function.Descendants];
        pass.Run(function, PassContext.None);
        Assert.Equal(once, function.Descendants);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("reversed")]
    [InlineData("extra-use")]
    [InlineData("extra-definition")]
    [InlineData("left-type")]
    [InlineData("right-type")]
    [InlineData("intervening")]
    [InlineData("nested-operand")]
    [InlineData("await")]
    [InlineData("nested-body-use")]
    public void OwnershipOrderAndTypeNearMissesRetainSpillStorage(string scenario)
    {
        var left = new LoadStackSlot(scenario == "reversed" ? 1 : 0,
            scenario == "left-type" ? Int64 : Int32);
        var right = new LoadStackSlot(scenario == "reversed" ? 0 : 1,
            scenario == "right-type" ? Int64 : Int32);
        IrExpression rightValue = scenario == "await"
            ? new AwaitExpression(new Call(new MethodRef(Holder, "NextAsync",
                TypeRef.GenericInstance(TypeRef.CoreLib("System.Threading.Tasks", "Task`1"), [Int32]),
                [], HasThis: false), isVirtual: false, []), Int32)
            : Effect("Second");
        var statements = new List<IrNode>();
        if (scenario == "extra-definition")
            statements.Add(new StoreStackSlot(0, Effect("Earlier")));
        statements.Add(new StoreStackSlot(0, Effect("First")));
        if (scenario == "intervening")
            statements.Add(new ExpressionStatement(Effect("Between")));
        statements.Add(new StoreStackSlot(1, rightValue));
        statements.Add(Store(scenario == "nested-operand" ? new Unary(UnaryKind.Negate, left) : left, right));
        if (scenario == "extra-use")
            statements.Add(new Return(new LoadStackSlot(0, Int32)));
        if (scenario == "nested-body-use")
        {
            var nested = new BlockContainer();
            var nestedBlock = new Block();
            nestedBlock.Add(new Return(new LoadStackSlot(0, Int32)));
            nested.Add(nestedBlock);
            var lambda = new Lambda(
                TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [Int32]),
                [], [], [], false, false, nested);
            statements.Add(new ExpressionStatement(lambda));
        }
        var function = Function([.. statements]);

        new ExpressionInliningPass(slotsOnly: true).Run(function, PassContext.None);

        Assert.NotEmpty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(1, function.Descendants.OfType<Call>().Count(call => call.Callee.Name == "First"));
        function.CheckInvariant();
    }

    [Fact]
    public void DifferentBlocksDoNotFormAnOrderedRun()
    {
        var function = Function(new StoreStackSlot(0, Effect("First")));
        var following = new Block(10);
        following.Add(new StoreStackSlot(1, Effect("Second")));
        following.Add(Store(new LoadStackSlot(0, Int32), new LoadStackSlot(1, Int32)));
        function.Body.Add(following);

        new ExpressionInliningPass(slotsOnly: true).Run(function, PassContext.None);

        Assert.NotEmpty(function.Descendants.OfType<StoreStackSlot>());
        function.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompilerProducedCasesRecompileExactlyWithoutTheFloor(bool updated)
    {
        var targets = Methods.Select(method => new ReturnToSender.RequestedTarget(FixtureType, method, 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            FixturePath(updated), targets, sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static string FixturePath(bool updated)
        => (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy).AssemblyPath();

    static Call Effect(string name)
        => new(new MethodRef(Holder, name, Int32, [], HasThis: false), isVirtual: false, []);

    static StoreArgument Store(IrExpression left, IrExpression right)
        => new(0, "value", Int32, new Binary(BinaryKind.Add, true, false, left, right));

    static IrFunction Function(params IrNode[] statements)
    {
        var body = new BlockContainer();
        var block = new Block();
        foreach (var statement in statements)
            block.Add(statement);
        body.Add(block);
        return new IrFunction("M", Holder,
            new MethodSignature(Int32, [new Parameter("value", Int32)], false, 0), [], body);
    }
}
