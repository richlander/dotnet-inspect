using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class ScalarSelfUpdateTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.ScalarSelfUpdateSamples";
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Owner = TypeRef.Definition("Samples", "Samples", "ScalarUpdates");

    [Theory]
    [InlineData("Argument")]
    [InlineData("Local")]
    [InlineData("ByReference")]
    [InlineData("Indirect")]
    [InlineData("Field")]
    [InlineData("StaticField")]
    [InlineData("VolatileField")]
    [InlineData("Property")]
    [InlineData("Indexer")]
    [InlineData("Checked")]
    [InlineData("Loop")]
    [InlineData("LambdaLocal")]
    public void CompilerProducedUpdatesAreDecidedInBothMemoryModes(string method)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, FixtureType, method);
            Assert.Contains(function.Descendants.OfType<ScalarStore>(), store => store.UpdateKind is not null);
            Assert.Contains("+=", CSharpPrinter.Print(function).Output);
            Assert.Empty(CoercionInvariant.Check(function));
            function.CheckInvariant(includeSemantics: true);
        }
    }

    [Theory]
    [InlineData("DifferentField")]
    [InlineData("DifferentIndex")]
    [InlineData("EffectfulReceiver")]
    public void DifferentOrEffectfulPlacesRemainExplicit(string method)
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, FixtureType, method);
        Assert.All(function.Descendants.OfType<ScalarStore>(), store => Assert.Null(store.UpdateKind));
        Assert.DoesNotContain("+=", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void BooleanStorageDoesNotRequireIdenticalLoadAndStoreOpcodes()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, FixtureType, "BooleanReference");
        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());
        Assert.Equal(ScalarUpdateKind.Binary, store.UpdateKind);
        Assert.Contains("value |= other;", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealMathBitIncrementKeepsUnsignedBinding()
    {
        using var source = MetadataSource.Open(typeof(Math).Assembly.Location);
        var function = Raise(source, "System.Math", "BitIncrement");
        var updates = function.Descendants.OfType<ScalarStore>().Where(store => store.UpdateKind is not null).ToArray();
        Assert.Equal(2, updates.Length);
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.All(updates, update => Assert.Equal(TypeRef.CoreLib("System", "UInt64"), Assert.IsType<StoreLocal>(update).Type));
        Assert.Contains("V_0 -= 1;", output);
        Assert.Contains("V_0 += 1;", output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(BinaryKind.Add, ScalarUpdateKind.Increment, "value++;")]
    [InlineData(BinaryKind.Subtract, ScalarUpdateKind.Decrement, "value--;")]
    [InlineData(BinaryKind.Multiply, ScalarUpdateKind.Binary, "value *= 1;")]
    public void DecisionRetainsStoreChildrenAndSurvivesClone(BinaryKind kind, ScalarUpdateKind expected, string text)
    {
        var function = Synthetic(kind);
        var store = Assert.Single(function.Descendants.OfType<StoreArgument>());
        var children = store.Descendants.ToArray();
        Assert.DoesNotContain(text, CSharpPrinter.Print(function).Output);

        new ScalarSelfUpdatePass().Run(function, PassContext.None);

        Assert.Equal(expected, store.UpdateKind);
        Assert.Contains($"{expected} self-update", IrPrinter.Dump(function));
        Assert.Equal(children, store.Descendants);
        Assert.Contains(text, CSharpPrinter.Print(function).Output);
        Assert.Contains(text, CSharpPrinter.Print((IrFunction)function.Clone()).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RepeatedDecisionClearsAChangedOperation()
    {
        var function = Synthetic(BinaryKind.Add);
        var store = Assert.Single(function.Descendants.OfType<StoreArgument>());
        var pass = new ScalarSelfUpdatePass();
        pass.Run(function, PassContext.None);
        pass.Run(function, PassContext.None);
        Assert.Equal(ScalarUpdateKind.Increment, store.UpdateKind);

        store.SetChild(0, new Constant(0, Int));
        pass.Run(function, PassContext.None);

        Assert.Null(store.UpdateKind);
        Assert.Contains("value = 0;", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothLoopHeaderPositionsConsumeTheDecision(bool initializer)
    {
        var function = Synthetic(BinaryKind.Add);
        var block = Assert.Single(function.Body.Children.OfType<Block>());
        var store = Assert.Single(block.Children.OfType<StoreArgument>());
        var result = Assert.Single(block.Children.OfType<Return>());
        store.Detach();
        result.Detach();
        var body = new Block(1);
        body.Add(new Break());
        block.Add(new ForLoop(initializer ? store : new LabelAnchor(),
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            initializer ? new LabelAnchor() : store, body));
        block.Add(result);

        new ScalarSelfUpdatePass().Run(function, PassContext.None);

        Assert.Equal(ScalarUpdateKind.Increment, store.UpdateKind);
        Assert.Contains(initializer ? "for (value++; true; )" : "for (; true; value++)",
            CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void OuterDecisionDoesNotUseNestedLocalIdentities()
    {
        var function = Synthetic(BinaryKind.Add);
        var inner = Synthetic(BinaryKind.Subtract);
        var body = inner.Body;
        body.Detach();
        var lambda = new Lambda(TypeRef.Definition("Samples", "Samples", "UpdateAction"),
            inner.Signature.Parameters, [], [], false, false, body);
        Assert.Single(function.Body.Children.OfType<Block>()).Add(new ExpressionStatement(lambda));

        new ScalarSelfUpdatePass().Run(function, PassContext.None);

        Assert.NotNull(Assert.Single(function.DescendantsOutsideNestedFunctions.OfType<ScalarStore>()).UpdateKind);
        Assert.Null(Assert.Single(body.Descendants.OfType<ScalarStore>()).UpdateKind);
        function.CheckInvariant();
    }

    [Fact]
    public void LoweredPipelineRetainsItsExistingScalarSpelling()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = IrImporter.Import(source, FixtureType, "Argument");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Lowered);
        Assert.Contains(function.Descendants.OfType<ScalarStore>(), store => store.UpdateKind is not null);
        Assert.Contains("+=", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void DifferentAccessorDispatchIsNotASelfUpdate()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, FixtureType + ".DerivedCounter", "DifferentDispatch");
        var store = Assert.Single(function.Descendants.OfType<StoreProperty>());
        var read = Assert.Single(store.Value.Descendants.OfType<LoadProperty>());
        Assert.False(store.IsVirtual);
        Assert.True(read.IsVirtual);
        Assert.Null(store.UpdateKind);
        Assert.Contains("base.Value = Value + amount;", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Area", "Fidelity")]
    public async Task DifferentAccessorDispatchRecompilesExactly(bool updated)
    {
        var results = await ReturnToSender.CompileBackTargets(
            FixturePath(updated),
            [new(FixtureType + ".DerivedCounter", "DifferentDispatch", 0)],
            sourceIndex: null, applyCompileBackFloor: false);
        var result = Assert.Single(results);
        Assert.False(result.UsedCompileBackFloor);
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, $"{result.Status}: {result.Detail}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Area", "Fidelity")]
    public async Task ArgumentAndIndirectUpdatesRecompileExactly(bool updated)
    {
        string[] methods = ["Argument", "ByReference", "Indirect", "Field", "StaticField", "BooleanReference", "Property", "Checked"];
        var targets = methods.Select(method => new ReturnToSender.RequestedTarget(FixtureType, method, 0)).ToArray();
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

    static IrFunction Raise(MetadataSource source, string type, string method)
    {
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        return function;
    }

    static IrFunction Synthetic(BinaryKind kind)
    {
        var block = new Block(0);
        block.Add(new StoreArgument(0, "value", Int,
            new Binary(kind, false, false, new LoadArgument(0, "value", Int), new Constant(1, Int))));
        block.Add(new Return(new LoadArgument(0, "value", Int)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("Update", Owner,
            new MethodSignature(Int, [new Parameter("value", Int)], HasThis: false, GenericParameterCount: 0),
            [], body);
    }
}
