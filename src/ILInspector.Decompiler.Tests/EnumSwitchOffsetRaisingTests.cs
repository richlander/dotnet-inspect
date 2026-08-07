using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class EnumSwitchOffsetRaisingTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_uint = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef s_enum = TypeRef.Definition("Synthetic", "Samples", "Boundary");

    [Fact]
    public void SubtractOffset_WrapsSignedLabelsWithoutChangingDispatch()
    {
        var function = Build(
            new Binary(
                BinaryKind.Subtract,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "value", s_enum),
                new Constant(int.MaxValue, s_int)),
            s_int,
            new Dictionary<long, string>
            {
                [int.MaxValue] = "Max",
                [int.MinValue] = "Min",
            });

        var node = Raise(function);

        Assert.IsType<LoadArgument>(node.Value);
        Assert.Equal(
            [int.MaxValue, int.MinValue],
            node.Sections.SelectMany(section => section.Labels).Select(label => Assert.IsType<int>(label.Value)));
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("switch (value)", output);
        Assert.Contains("case Boundary.Max:", output);
        Assert.Contains("case Boundary.Min:", output);
    }

    [Fact]
    public void AddOffset_WrapsUnsignedLabelsWithoutChangingDispatch()
    {
        var function = Build(
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "value", s_enum),
                new Constant(1, s_int)),
            s_uint,
            new Dictionary<long, string>
            {
                [0] = "Zero",
            });

        var node = Raise(function);

        Assert.IsType<LoadArgument>(node.Value);
        Assert.Equal(
            [-1, 0],
            node.Sections.SelectMany(section => section.Labels).Select(label => Assert.IsType<int>(label.Value)));
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("switch (value)", output);
        Assert.Contains("case unchecked((Boundary)(-1)):", output);
        Assert.Contains("case Boundary.Zero:", output);
    }

    [Fact]
    public void UnknownShapeNamedEnum_ReconstructsOffsetLikeSwitchLabelRendering()
    {
        var function = Build(
            new Binary(
                BinaryKind.Subtract,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "value", s_enum),
                new Constant(24, s_int)),
            s_int,
            declareEnumShape: false);

        var node = Raise(function);

        Assert.IsType<LoadArgument>(node.Value);
        Assert.Equal([24, 25], Labels(node));
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("switch (value)", output);
        Assert.Contains("case (Boundary)24:", output);
        Assert.Contains("case (Boundary)25:", output);
    }

    [Fact]
    public void CheckedArithmetic_RemainsInGoverningExpression()
    {
        var function = Build(
            new Binary(
                BinaryKind.Subtract,
                isChecked: true,
                isUnsigned: false,
                new LoadArgument(0, "value", s_enum),
                new Constant(24, s_int)),
            s_int);

        var node = Raise(function);

        var binary = Assert.IsType<Binary>(node.Value);
        Assert.True(binary.IsChecked);
        Assert.Equal([0, 1], Labels(node));
    }

    [Fact]
    public void VariableOffset_RemainsInGoverningExpression()
    {
        var function = Build(
            new Binary(
                BinaryKind.Subtract,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "value", s_enum),
                new LoadArgument(1, "offset", s_int)),
            s_int);

        var node = Raise(function);

        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1], Labels(node));
    }

    [Fact]
    public void PrimitiveArithmetic_RemainsInGoverningExpression()
    {
        var function = Build(
            new Binary(
                BinaryKind.Subtract,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(1, "offset", s_int),
                new Constant(24, s_int)),
            s_int);

        var node = Raise(function);

        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1], Labels(node));
    }

    static Switch Raise(IrFunction function)
    {
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        return Assert.Single(function.Descendants.OfType<Switch>());
    }

    static int[] Labels(Switch node)
        => [.. node.Sections.SelectMany(section => section.Labels).Select(label => Assert.IsType<int>(label.Value))];

    static IrFunction Build(
        IrExpression value,
        TypeRef underlying,
        IReadOnlyDictionary<long, string>? members = null,
        bool declareEnumShape = true)
    {
        var body = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(value, [0x10, 0x20]));
        body.Add(dispatch);

        var defaultBlock = new Block(4);
        defaultBlock.Add(new Return(new Constant(-1, s_int)));
        body.Add(defaultBlock);

        var firstCase = new Block(0x10);
        firstCase.Add(new Return(new Constant(10, s_int)));
        body.Add(firstCase);

        var secondCase = new Block(0x20);
        secondCase.Add(new Return(new Constant(20, s_int)));
        body.Add(secondCase);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Holder"),
            new MethodSignature(
                s_int,
                [
                    new Parameter("value", s_enum),
                    new Parameter("offset", s_int),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = declareEnumShape
                ? new Dictionary<TypeRef, TypeShape> { [s_enum] = TypeShape.Enum }
                : new Dictionary<TypeRef, TypeShape>(),
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [s_enum] = underlying },
            EnumMembers = members is null
                ? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()
                : new Dictionary<TypeRef, IReadOnlyDictionary<long, string>> { [s_enum] = members },
        };
    }
}

[Collection(FidelityGateCollection.Name)]
public class EnumSwitchOffsetFidelityTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedEnumSwitches_PreserveSwitchOpcodesOnCompileBack()
    {
        string fixtureType = typeof(SharedGuardSwitchFixture).FullName!;
        var methods = new HashSet<string>
        {
            nameof(SharedGuardSwitchFixture.Check),
            nameof(SharedGuardSwitchFixture.Classify),
        };
        var results = FidelityCheck.Evaluate(
                typeof(SharedGuardSwitchFixture).Assembly.Location,
                type => type == fixtureType)
            .Where(result => methods.Contains(result.Method))
            .ToList();

        Assert.Equal(methods.Count, results.Count);
        var classify = Assert.Single(results, result => result.Method == nameof(SharedGuardSwitchFixture.Classify));
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, classify.Status);

        // Check has a pre-existing unsigned-comparison residual in the guard
        // before its switch. Pin that as the only difference so this gate still
        // proves the reconstructed switch recompiles to the original sub/switch.
        var check = Assert.Single(results, result => result.Method == nameof(SharedGuardSwitchFixture.Check));
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, check.Status);
        Assert.Equal(
            check.OriginalOpcodes.Replace("ble.un", "ble", StringComparison.Ordinal),
            check.RecompiledOpcodes);
    }
}
