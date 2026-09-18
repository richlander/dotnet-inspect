using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class PointerCompoundAssignmentTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.PointerVariableUpdateSamples";
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef NativeInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Pointer = TypeRef.Pointer(TypeRef.CoreLib("System", "UInt64"));
    static readonly TypeRef Owner = TypeRef.Definition("Samples", "Samples", "PointerUpdates");

    [Theory]
    [InlineData("Bytes", 2)]
    [InlineData("Words", 2)]
    [InlineData("Steps", 3)]
    [InlineData("UnitAssignment", 2)]
    [InlineData("Local", 1)]
    [InlineData("ByReference", 2)]
    [InlineData("Indirect", 2)]
    [InlineData("Field", 2)]
    [InlineData("StaticField", 2)]
    [InlineData("CapturingLambda", 0)]
    [InlineData("CaptureLocal", 1)]
    [InlineData("Property", 1)]
    [InlineData("Indexer", 1)]
    [InlineData("Checked", 2)]
    [InlineData("CheckedBytes", 1)]
    [InlineData("CheckedIndex", 1)]
    [InlineData("Loop", 1)]
    [InlineData("MutatingPointer", 1)]
    [InlineData("MutatingField", 1)]
    [InlineData("UnsignedCount", 0)]
    [InlineData("LongCount", 0)]
    [InlineData("UnsignedLongCount", 0)]
    [InlineData("ByteOffset", 0)]
    [InlineData("ResultUsed", 0)]
    public void CompilerProducedUpdatesRespectAdmissionInBothMemoryModes(string method, int expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, FixtureType, method);
            Assert.Equal(expected, function.Descendants.OfType<PointerCompoundAssignment>().Count());
            Assert.Empty(CoercionInvariant.Check(function));
            function.CheckInvariant(includeSemantics: true);
            string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
            if (method == "Indirect" && updated)
                Assert.Contains("unsafe", output);
            if (updated && method is "Bytes" or "Words" or "Steps" or "UnitAssignment" or "Local"
                or "ByReference" or "Checked" or "CheckedBytes" or "CheckedIndex")
            {
                Assert.DoesNotContain("unsafe", output);
            }
            if (method == "Indirect")
                Assert.Contains("(*cursor)++;", output);
            if (method == "ByReference")
                Assert.Contains("cursor--;", output);
            if (method == "Checked")
            {
                Assert.Contains("checked { cursor += count; }", output);
                Assert.Contains("checked { cursor--; }", output);
            }
            if (method == "CheckedIndex")
                Assert.Contains("unchecked(count + 1)", output);
            if (method == "UnitAssignment")
                Assert.Contains("cursor++;", output);
        }
    }

    [Fact]
    public void RoslynVectorPointerUpdatesAreDecidedBeforePrinting()
    {
        using var source = MetadataSource.Open(typeof(Compilation).Assembly.Location);
        var function = Raise(source, "System.IO.Hashing.XxHashShared", "Accumulate512Inlined");
        var updates = function.Descendants.OfType<PointerCompoundAssignment>().ToArray();

        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.Equal(PointerUpdateKind.Add, update.Kind));
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains("accumulators += Vector256<ulong>.Count;", output);
        Assert.Contains("secret += Vector256<byte>.Count;", output);
        Assert.Contains("source += Vector256<byte>.Count;", output);
        Assert.Empty(CoercionInvariant.Check(function));
    }

    [Fact]
    public void PropertyUpdatesRetainBothExactAccessorReferences()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = Raise(source, FixtureType, "Indexer");
        var update = Assert.Single(function.Descendants.OfType<PointerCompoundAssignment>());
        var target = Assert.IsType<LoadProperty>(update.Target);
        List<ConsumedMemberEvidence> evidence = [];
        ConsumedMemberEvidence.AddFrom(update, evidence);
        ConsumedMemberEvidence.AddFrom(target, evidence);

        Assert.Contains(evidence, item => ReferenceEquals(item.Method, update.Setter));
        Assert.Contains(evidence, item => ReferenceEquals(item.Method, target.Accessor));
        Assert.Equal("set_Item", update.Setter!.Name);
        Assert.Equal("get_Item", target.Accessor.Name);
    }

    [Fact]
    public void UpdatedRulesKeepPointerArithmeticSafeAndDereferencesUnsafe()
    {
        using var source = MetadataSource.Open(FixturePath(true));
        var function = Raise(source, "ILInspector.Decompiler.Fixtures.NewUnsafe.PointerArithmeticFixtures", "PointerIncrement");
        Assert.Equal(2, function.Descendants.OfType<PointerCompoundAssignment>().Count());

        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains("p++;", output);
        Assert.Contains("p--;", output);
        Assert.Contains("unsafe(*p)", output);
        Assert.DoesNotContain("unsafe\n{", output.ReplaceLineEndings("\n"));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RaiseKeepsTheTargetAndEffectfulIndexInOrderAtTheSameEntry()
    {
        var function = Synthetic("call");
        var store = Assert.Single(function.Descendants.OfType<StoreArgument>());
        store.SetSourceOffset(10);
        var binary = Assert.IsType<Binary>(store.Value);
        var target = binary.Left;
        var call = Assert.Single(function.Descendants.OfType<Call>());
        var entry = new Block(20);
        entry.Add(new Branch(10));
        function.Body.Add(entry);

        new PointerCompoundAssignmentPass().Run(function, PassContext.None);

        var update = Assert.Single(function.Descendants.OfType<PointerCompoundAssignment>());
        Assert.Equal(10, update.SourceOffset);
        Assert.Same(target, update.Target);
        Assert.Same(call, update.Index);
        Assert.Equal(new IrNode[] { target, call }, update.Children);
        Assert.Single(function.Descendants.OfType<Call>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("different-target")]
    [InlineData("wrong-scale")]
    [InlineData("commuted-scale")]
    [InlineData("checked-scale")]
    [InlineData("checked-conversion")]
    [InlineData("unsigned-conversion")]
    [InlineData("wrong-native-type")]
    [InlineData("unsigned-index")]
    [InlineData("checked-signed-add")]
    public void UnprovenShellsRemainExplicitStores(string shape)
    {
        var function = Synthetic(shape);
        string before = IrPrinter.Dump(function);

        new PointerCompoundAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<PointerCompoundAssignment>());
        Assert.Equal(before, IrPrinter.Dump(function));
        Assert.DoesNotContain("+=", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    [Fact]
    public void OuterPassDoesNotConsumeANestedBody()
    {
        var outer = Synthetic("call");
        var inner = Synthetic("call");
        var body = inner.Body;
        body.Detach();
        var lambda = new Lambda(TypeRef.Definition("Samples", "Samples", "UpdateAction"),
            inner.Signature.Parameters, [], [], false, false, body);
        outer.Body.Children.OfType<Block>().Single().Add(new ExpressionStatement(lambda));
        var before = body.Descendants.ToArray();

        new PointerCompoundAssignmentPass().Run(outer, PassContext.None);

        Assert.Single(outer.DescendantsOutsideNestedFunctions.OfType<PointerCompoundAssignment>());
        Assert.Equal(before, body.Descendants);
        outer.CheckInvariant();
    }

    [Fact]
    public void LoweredModeDoesNotAskThePrinterToRecoverPointerUpdates()
    {
        using var source = MetadataSource.Open(FixturePath(false));
        var function = IrImporter.Import(source, FixtureType, "Words");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Lowered, PassContext.None);

        Assert.Empty(function.Descendants.OfType<PointerCompoundAssignment>());
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains("cursor = cursor + count;", output);
        Assert.DoesNotContain("+=", output);
    }

    [Fact]
    public void ATestifiedResidualSlotKeepsOneTypedStorageName()
    {
        var function = Synthetic("call");
        var block = Assert.Single(function.Body.Children.OfType<Block>());
        var original = Assert.Single(block.Children.OfType<StoreArgument>());
        var binary = Assert.IsType<Binary>(original.Value);
        binary.SetChild(0, new LoadStackSlot(256, Pointer));
        var value = original.Value;
        value.Detach();
        original.Detach();
        block.Add(new StoreStackSlot(256, new LoadArgument(0, "cursor", Pointer)));
        block.Add(new StoreStackSlot(256, value));

        new PointerCompoundAssignmentPass().Run(function, PassContext.None);

        var update = Assert.Single(function.Descendants.OfType<PointerCompoundAssignment>());
        Assert.Equal(256, Assert.IsType<LoadStackSlot>(update.Target).Slot);
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains("ulong* S_256 = cursor;", output);
        Assert.Contains("S_256 += Displacement();", output);
        Assert.DoesNotContain("S_256_1", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedLoopHeaderUsesAnExpressionRatherThanACheckedBlock(bool isChecked)
    {
        var function = Synthetic("call");
        var block = Assert.Single(function.Body.Children.OfType<Block>());
        var store = Assert.Single(block.Children.OfType<StoreArgument>());
        store.Detach();
        store.SetChild(0, new Binary(BinaryKind.Add, isChecked, isChecked,
            new LoadArgument(0, "cursor", Pointer), new Constant(8, Int32)));
        var loopBody = new Block(1);
        loopBody.Add(new Break());
        block.Add(new ForLoop(new LabelAnchor(), new Constant(true, TypeRef.CoreLib("System", "Boolean")), store, loopBody));

        new PointerCompoundAssignmentPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<PointerCompoundAssignment>());
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Contains(isChecked ? "checked(cursor++))" : "cursor++)", output);
        Assert.DoesNotContain("checked {", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task PointerArgumentAndIndirectUpdatesRecompileExactlyWithoutTheFloor(bool updated)
    {
        string[] methods =
        [
            "Bytes", "Words", "Steps", "UnitAssignment", "Local", "ByReference", "Indirect", "Field", "StaticField",
            "Property", "Checked", "CheckedBytes", "CheckedIndex", "Loop", "MutatingPointer", "MutatingField", "ByteOffset", "ResultUsed",
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

    static IrFunction Raise(MetadataSource source, string type, string method)
    {
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        return function;
    }

    static IrFunction Synthetic(string shape)
    {
        IrExpression index = shape == "call"
            ? new Call(new MethodRef(Owner, "Displacement", Int32, [], HasThis: false), false, [])
            : new LoadArgument(1, "count", shape == "unsigned-index" ? TypeRef.CoreLib("System", "UInt32") : Int32);
        var native = new Convert(shape == "wrong-native-type" ? TypeRef.CoreLib("System", "UIntPtr") : NativeInt,
            shape == "checked-conversion", shape == "unsigned-conversion", index);
        var scale = new Constant(shape == "wrong-scale" ? 4 : 8, Int32);
        var offset = shape == "commuted-scale"
            ? new Binary(BinaryKind.Multiply, false, false, scale, native)
            : new Binary(BinaryKind.Multiply, shape == "checked-scale", false, native, scale);
        var value = new Binary(BinaryKind.Add, shape == "checked-signed-add", false,
            new LoadArgument(shape == "different-target" ? 2 : 0, "cursor", Pointer), offset);
        var block = new Block(0);
        block.Add(new StoreArgument(0, "cursor", Pointer, value));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("Update", Owner,
            new MethodSignature(Void, [new Parameter("cursor", Pointer), new Parameter("count", Int32)],
                HasThis: false, GenericParameterCount: 0), [], body);
    }
}
