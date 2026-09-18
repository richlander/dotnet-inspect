using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class CheckedIntegerOperandTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.CheckedIntegerOperandSamples";

    [Theory]
    [InlineData("Unsigned64", "UInt64", "ulong")]
    [InlineData("Signed64", "Int64", "long")]
    [InlineData("Unsigned32", "UInt32", "uint")]
    [InlineData("Signed32", "Int32", "int")]
    [InlineData("UnsignedNative", "UIntPtr", "nuint")]
    [InlineData("SignedNative", "IntPtr", "nint")]
    public void CheckedCompoundRetainsUncheckedOperandBinding(string method, string type, string keyword)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, method);
            var store = Assert.Single(function.Descendants.OfType<StoreArgument>());
            Assert.Equal(ScalarUpdateKind.Binary, store.UpdateKind);
            var binary = Assert.IsType<Binary>(store.Value);
            Assert.Equal(type, binary.Left.ResultType?.Name);
            Assert.Equal(type, Assert.IsType<Coerce>(binary.Right).Target.Name);
            Assert.Contains($"checked {{ value += unchecked(({keyword})", CSharpPrinter.Print(function).Output);
        }
    }

    [Theory]
    [InlineData("Unsigned64Header")]
    [InlineData("Signed64Header")]
    [InlineData("UnsignedNativeHeader")]
    [InlineData("SignedNativeHeader")]
    public void BothHeaderPositionsConsumeTheSameBinding(string method)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, method);
            var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
            foreach (var node in new[] { loop.Initializer, loop.Increment })
            {
                var store = Assert.IsAssignableFrom<ScalarStore>(node);
                Assert.Equal(ScalarUpdateKind.Binary, store.UpdateKind);
                var binary = Assert.IsType<Binary>(store.Value);
                Assert.Equal(binary.Right.ResultType, binary.Left.ResultType);
                Assert.IsType<Coerce>(binary.Right);
            }
            string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
            string header = Assert.Single(output.Split('\n'),
                line => line.TrimStart().StartsWith("for (", StringComparison.Ordinal));
            Assert.DoesNotContain("{", header);
            Assert.Equal(2, header.Split("= checked(").Length - 1);
            Assert.Equal(2, header.Split("unchecked(").Length - 1);
        }
    }

    [Theory]
    [InlineData("SignedOperationUnsignedDestination", false, "Int64", "UInt64")]
    [InlineData("UnsignedOperationSignedDestination", true, "UInt64", "Int64")]
    public void OpcodeDomainWinsOverTheDestination(string method, bool unsigned, string operandType, string resultType)
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, method);
        var store = Assert.Single(function.Descendants.OfType<StoreArgument>());
        Assert.Null(store.UpdateKind);
        var destination = Assert.IsType<Coerce>(store.Value);
        Assert.Equal(resultType, destination.Target.Name);
        var binary = Assert.IsType<Binary>(destination.Operand);
        Assert.Equal(unsigned, binary.IsUnsigned);
        Assert.Equal(operandType, binary.Left.ResultType?.Name);
        Assert.Equal(operandType, binary.Right.ResultType?.Name);
    }

    [Fact]
    public void BindingRetainsScalarPlacesAndUnitSteps()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        foreach (string method in new[] { "NativeReference", "NativePointer", "FieldsAndProperty", "UnitStep" })
        {
            var function = Raise(source, method);
            var stores = function.Descendants.OfType<ScalarStore>()
                .Where(store => store.Value is Binary { IsChecked: true }).ToArray();
            Assert.Equal(method == "FieldsAndProperty" ? 3 : 1, stores.Length);
            Assert.All(stores, store => Assert.NotNull(store.UpdateKind));
            if (method == "UnitStep")
            {
                Assert.Equal(ScalarUpdateKind.Increment, Assert.Single(stores).UpdateKind);
                Assert.Contains("checked { value++; }", CSharpPrinter.Print(function).Output);
            }
        }
    }

    [Fact]
    public void NestedBindingIsIdempotentAndRetainsBothDomains()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, "NestedDomains");
        var binaries = function.Descendants.OfType<Binary>().Where(binary => binary.IsChecked).ToArray();
        Assert.Equal(2, binaries.Length);
        Assert.Contains(binaries, binary => binary.IsUnsigned);
        Assert.Contains(binaries, binary => !binary.IsUnsigned);
        string before = IrPrinter.Dump(function);
        new CheckedIntegerOperandPass().Run(function, PassContext.None);
        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultAndLoweredPipelinesBindBeforeDestinationCoercion(bool lowered)
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source, FixtureType, "UnsignedOperationSignedDestination"));
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var binary = Assert.Single(function.Descendants.OfType<Binary>(), binary => binary.IsChecked);
        Assert.Equal("UInt64", binary.Left.ResultType?.Name);
        Assert.Equal("UInt64", binary.Right.ResultType?.Name);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CoercionInvariantReportsTheUndecidedOperand()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source, FixtureType, "Unsigned64"));
        IrPasses.Run(function, [.. IrPasses.Default.TakeWhile(pass => pass is not CheckedIntegerOperandPass)],
            PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.NotEmpty(CoercionInvariant.Check(function));
        new CheckedIntegerOperandPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(CoercionInvariant.Check(function));
    }

    [Fact]
    public void LambdaBodyReceivesTheBinding()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, "Lambda");
        var binary = Assert.Single(function.Descendants.OfType<Binary>(), binary => binary.IsChecked);
        Assert.Equal("UInt64", binary.Left.ResultType?.Name);
        Assert.Equal("UInt64", Assert.IsType<Coerce>(binary.Right).Target.Name);
        Assert.Contains("unchecked((ulong)amount)", CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData("Unsigned32NegationUpdate")]
    [InlineData("Unsigned32NegationExpression")]
    [InlineData("Unsigned32NegationWideReturn")]
    [InlineData("Unsigned32NegationBoxed")]
    [InlineData("Signed32Negation")]
    [InlineData("Unsigned32NegationNested")]
    [InlineData("Unsigned32NegationWidened")]
    public void UInt32NegationRetainsItsOriginalWidth(string method)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, method);
            var negate = Assert.Single(function.Descendants.OfType<Unary>(), unary => unary.Kind == UnaryKind.Negate);
            Assert.Equal("Int32", Assert.IsType<Coerce>(negate.Operand).Target.Name);
            Assert.Equal("Int32", negate.ResultType?.Name);
            var binary = Assert.Single(function.Descendants.OfType<Binary>(), operation => operation.IsChecked);
            Assert.Equal(binary.Left.ResultType, binary.Right.ResultType);
            Assert.Contains("(int)amount", CSharpPrinter.Print(function).Output);
            string before = IrPrinter.Dump(function);
            new CheckedIntegerOperandPass().Run(function, PassContext.None);
            Assert.Equal(before, IrPrinter.Dump(function));
        }
    }

    [Theory]
    [InlineData(BinaryKind.Add, false, "Int64", "UInt64")]
    [InlineData(BinaryKind.Add, false, "UInt32", "UInt32")]
    [InlineData(BinaryKind.Divide, false, "Int64", "UInt64")]
    [InlineData(BinaryKind.Add, true, "Int64", "Int32")]
    [InlineData(BinaryKind.Add, true, "Int64", "UInt32")]
    [InlineData(BinaryKind.Add, true, "UIntPtr", "Int32")]
    [InlineData(BinaryKind.Add, true, "Byte", "Byte")]
    public void OtherOperatorDomainsAreUnchanged(BinaryKind kind, bool check, string left, string right)
    {
        var leftType = TypeRef.CoreLib("System", left);
        var rightType = TypeRef.CoreLib("System", right);
        IrExpression rightOperand = new LoadArgument(1, "right", rightType);
        if (right == "UInt32")
            rightOperand = new Unary(UnaryKind.Negate, rightOperand);
        var binary = new Binary(kind, check, false,
            new LoadArgument(0, "left", leftType), rightOperand);
        var block = new Block(0);
        block.Add(new Return(binary));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction("Boundary", TypeRef.Definition("fixture", "", "Boundary"),
            new MethodSignature(leftType, [new Parameter("left", leftType), new Parameter("right", rightType)],
                HasThis: false, GenericParameterCount: 0), [], body);
        string before = IrPrinter.Dump(function);
        new CheckedIntegerOperandPass().Run(function, PassContext.None);
        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Fact]
    public void VisualBasicUInt32NegationBindsTheSignedSubtraction()
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "Microsoft.VisualBasic.Core.dll");
        using var source = MetadataSource.Open(path);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source,
            "Microsoft.VisualBasic.CompilerServices.Operators", "NegateUInt32"));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var binary = Assert.Single(function.Descendants.OfType<Binary>());
        Assert.True(binary.IsChecked);
        Assert.False(binary.IsUnsigned);
        Assert.Equal("Int64", Assert.IsType<Coerce>(binary.Right).Target.Name);
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains("(long)operand", output);
        Assert.DoesNotContain("(ulong)operand", output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompilerProducedBindingsRecompileExactlyWithoutTheFloor(bool updated)
    {
        string[] methods =
        [
            "Unsigned64", "Signed64", "Unsigned32", "Signed32", "UnsignedNative", "SignedNative",
            "Unsigned64Header", "Signed64Header", "UnsignedNativeHeader", "SignedNativeHeader",
            "SignedOperationUnsignedDestination", "UnsignedOperationSignedDestination", "NestedDomains",
            "UnsignedSubtract", "SignedMultiply", "NativeReference", "NativePointer", "UnsignedArray",
            "FieldsAndProperty", "UnitStep", "NegateUInt32", "Lambda", "UnsignedNegation",
            "Unsigned32NegationUpdate", "Unsigned32NegationExpression", "Unsigned32NegationWideReturn",
            "Unsigned32NegationBoxed", "Signed32Negation", "Unsigned32NegationNested", "Unsigned32NegationWidened",
        ];
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

    static IrFunction Raise(MetadataSource source, string method)
    {
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source, FixtureType, method));
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        return function;
    }
}
