using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class SingleCaseValueSwitchTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.SingleCaseValueSwitchSamples";
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly string[] Methods = ["get_MaxLength", "OneCase", "ZeroCase", "CheckedArm", "EffectfulSelection"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompilerProducedResultJoinsBecomeExpressions(bool updated, bool lowered)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        foreach (string method in Methods)
        {
            var function = Raise(source, FixtureType, method, lowered);
            Assert.Single(function.Descendants.OfType<SwitchExpression>());
            string output = CSharpPrinter.Print(function).Output!;
            Assert.Contains(" switch", output);
            Assert.DoesNotContain("if (", output);
            Assert.DoesNotContain("V_", output);
        }
        Assert.Empty(Raise(source, FixtureType, "DirectReturns", lowered).Descendants.OfType<SwitchExpression>());
        var boxed = Raise(source, FixtureType, "BoxedCapacity", lowered);
        Assert.Empty(boxed.Descendants.OfType<SwitchExpression>());
        Assert.Equal(2, boxed.Descendants.OfType<Box>().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeSqlBytesUsesTheExistingSwitchExpression(bool lowered)
    {
        using var source = MetadataSource.Open(typeof(System.Data.SqlTypes.SqlBytes).Assembly.Location);
        var function = Raise(source, "System.Data.SqlTypes.SqlBytes", "get_MaxLength", lowered);
        Assert.Single(function.Descendants.OfType<SwitchExpression>());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("_state switch", output);
        Assert.Contains("SqlBytesCharsState.Stream =>", output);
        Assert.DoesNotContain("if (", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateResultJoinIsConsumedOnce(bool zeroTest)
    {
        var function = Function(zeroTest: zeroTest);
        var pass = new SingleCaseValueSwitchPass();

        pass.Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<SwitchExpression>());
        Assert.Empty(function.Descendants.OfType<StoreLocal>());
        Assert.Empty(function.Descendants.OfType<LoadLocal>());
        var once = function.Descendants.ToArray();
        pass.Run(function, PassContext.None);
        Assert.Equal(once, function.Descendants);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("extra-read")]
    [InlineData("extra-store")]
    [InlineData("address")]
    [InlineData("arm-type")]
    [InlineData("read-type")]
    [InlineData("different-local")]
    [InlineData("arm-entry")]
    [InlineData("intervening")]
    [InlineData("non-equality")]
    [InlineData("boolean")]
    public void OwnershipAndTypeNearMissesStayStructured(string scenario)
    {
        var function = Function(scenario);
        var before = function.Descendants.ToArray();

        new SingleCaseValueSwitchPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SwitchExpression>());
        Assert.Equal(before, function.Descendants);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("Int32", -1, true)]
    [InlineData("SByte", 127, true)]
    [InlineData("SByte", 128, false)]
    [InlineData("Byte", 255, true)]
    [InlineData("Byte", -1, false)]
    [InlineData("UInt32", 3, true)]
    [InlineData("UInt32", -1, false)]
    [InlineData("Char", 3, false)]
    [InlineData("Int64", 3, false)]
    public void CaseLabelMustFitTheSupportedGoverningType(string type, int label, bool accepted)
    {
        var function = Function(governingType: TypeRef.CoreLib("System", type), label: label);

        new SingleCaseValueSwitchPass().Run(function, PassContext.None);

        Assert.Equal(accepted, function.Descendants.OfType<SwitchExpression>().Any());
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumBackingMustBeKnown(bool known)
    {
        var enumType = TypeRef.CoreLib("Synthetic", "BufferState");
        var function = Function(governingType: enumType);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };
        if (known)
            function.EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = Int32 };

        new SingleCaseValueSwitchPass().Run(function, PassContext.None);

        Assert.Equal(known, function.Descendants.OfType<SwitchExpression>().Any());
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedLocalPoolsAreDistinctButCapturesPreventElimination(bool isolated)
    {
        var function = Function();
        var nestedBody = new BlockContainer();
        var nestedBlock = new Block();
        if (isolated)
            nestedBlock.Add(new StoreLocal(0, Int64, new Constant(7L, Int64)));
        nestedBlock.Add(new Return(new LoadLocal(0, Int64)));
        nestedBody.Add(nestedBlock);
        var delegateType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [Int64]);
        int delegateLocal = function.AddLocal(delegateType);
        function.Body.Blocks[0].Add(new StoreLocal(delegateLocal, delegateType,
            new Lambda(delegateType, [], isolated ? [Int64] : [], [], false, false, nestedBody)));

        new SingleCaseValueSwitchPass().Run(function, PassContext.None);

        Assert.Equal(isolated, function.Descendants.OfType<SwitchExpression>().Any());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompilerProducedCasesRemainExactWithoutTheFloor(bool updated)
    {
        var targets = Methods.Append("DirectReturns")
            .Select(method => new ReturnToSender.RequestedTarget(FixtureType, method, 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(FixturePath(updated), targets,
            sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, AssertExact);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task RuntimeSqlBytesRecompilesExactlyWithoutTheFloor()
    {
        string directory = Directory.CreateTempSubdirectory("single-case-value-switch-").FullName;
        try
        {
            // Inspect this module, not the unrelated sibling inventory of an entire runtime installation.
            string path = Path.Combine(directory, "System.Data.Common.dll");
            File.Copy(typeof(System.Data.SqlTypes.SqlBytes).Assembly.Location, path);
            var results = await ReturnToSender.CompileBackTargets(
                path, [new("System.Data.SqlTypes.SqlBytes", "get_MaxLength", 0)],
                sourceIndex: null, applyCompileBackFloor: false);
            AssertExact(Assert.Single(results));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerProducedMaxLengthProductSpellingRecompilesExactly(bool updated)
    {
        string path = FixturePath(updated);
        var result = Assert.Single(FidelityCheck.EvaluateTargets(
            [path],
            [
                new FidelityCheck.CompileBackTarget(
                    path,
                    FixtureType,
                    "get_MaxLength",
                    Overload: 0,
                    Signature: "() -> corelib:System.Int64"),
            ],
            lowered: false,
            options: StyleOptionCatalog.DefaultOptions));

        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}");
    }

    static void AssertExact(ReturnToSender.Result result)
    {
        Assert.False(result.UsedCompileBackFloor);
        Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
    }

    static string FixturePath(bool updated)
        => (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy).AssemblyPath();

    static IrFunction Raise(MetadataSource source, string type, string method, bool lowered)
    {
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        return function;
    }

    static IrFunction Function(
        string? scenario = null, bool zeroTest = false, TypeRef? governingType = null, int label = 3)
    {
        governingType ??= scenario == "boolean" ? TypeRef.CoreLib("System", "Boolean") : Int32;
        IrExpression governing = new LoadArgument(0, "state", governingType);
        IrExpression condition = zeroTest
            ? new LogicalNot(governing)
            : new Comparison(scenario == "non-equality" ? ComparisonKind.NotEqual : ComparisonKind.Equal,
                false, governing, new Constant(label, governingType));
        var matched = new Block(10);
        matched.Add(new StoreLocal(0, Int64, new Constant(1L, Int64)));
        var otherwise = new Block(20);
        otherwise.Add(new StoreLocal(scenario == "different-local" ? 1 : 0, Int64,
            scenario == "arm-type" ? new Constant(2, Int32) : new Constant(2L, Int64)));
        var body = new Block();
        if (scenario == "extra-store")
            body.Add(new StoreLocal(0, Int64, new Constant(0L, Int64)));
        body.Add(new IfStatement(condition, matched, otherwise));
        if (scenario == "intervening")
            body.Add(new ExpressionStatement(new Constant(0, Int32)));
        body.Add(new Return(new LoadLocal(0, scenario == "read-type" ? Int32 : Int64)));
        if (scenario == "extra-read")
            body.Add(new ExpressionStatement(new LoadLocal(0, Int64)));
        if (scenario == "address")
            body.Add(new ExpressionStatement(new LoadLocalAddress(0, Int64)));
        var container = new BlockContainer();
        if (scenario == "arm-entry")
        {
            var predecessor = new Block(0);
            predecessor.Add(new Branch(10));
            container.Add(predecessor);
        }
        container.Add(body);
        return new IrFunction("Capacity", TypeRef.CoreLib("Synthetic", "Buffer"),
            new MethodSignature(Int64, [new Parameter("state", governingType)], false, 0),
            [Int64, Int64], container);
    }
}
