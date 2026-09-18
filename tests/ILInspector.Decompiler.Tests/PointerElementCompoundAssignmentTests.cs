using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class PointerElementCompoundAssignmentTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.PointerElementUpdateSamples";
    static readonly TypeRef Owner = TypeRef.Definition("Samples", "Samples", "PointerUpdates");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UInt64 = TypeRef.CoreLib("System", "UInt64");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef NativeInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Pointer = TypeRef.Pointer(UInt64);

    [Theory]
    [InlineData("Accumulate", 2)]
    [InlineData("Signed32", 1)]
    [InlineData("Unsigned32", 1)]
    [InlineData("Signed64", 1)]
    [InlineData("Operations", 5)]
    [InlineData("ConstantIndex", 2)]
    [InlineData("SideEffects", 1)]
    [InlineData("MutatingPointer", 1)]
    [InlineData("MutatingIndex", 1)]
    [InlineData("RetainedCapture", 0)]
    [InlineData("CheckedUpdate", 0)]
    [InlineData("NarrowElement", 0)]
    [InlineData("LongIndex", 0)]
    [InlineData("ByteOffset", 0)]
    public void CompilerProducedUpdatesRespectTheAdmissionInBothMemoryModes(string method, int expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, FixtureType, method);
            Assert.Equal(expected, function.Descendants.OfType<PointerElementCompoundAssignment>().Count());
            Assert.Empty(CoercionInvariant.Check(function));
            function.CheckInvariant(includeSemantics: true);
            var output = CSharpPrinter.Print(function).Output;
            Assert.NotNull(output);
            if (expected > 0)
            {
                Assert.DoesNotContain("S_", output);
                if (updated)
                    Assert.Contains("unsafe", output);
            }
        }
    }

    [Fact]
    public void RoslynHashDependencyRaisesBothScalarUpdates()
    {
        using var source = MetadataSource.Open(typeof(Compilation).Assembly.Location);
        var function = Raise(source, "System.IO.Hashing.XxHashShared", "Accumulate512Inlined");

        Assert.Equal(2, function.Descendants.OfType<PointerElementCompoundAssignment>().Count());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("accumulators[", output);
        Assert.DoesNotContain("S_", output);
        Assert.Empty(CoercionInvariant.Check(function));
    }

    [Fact]
    public void PreviouslyMaterializedAccumulateStorageIsConsumedByTheRaise()
    {
        using var source = MetadataSource.Open(typeof(ExactPointerSlotMaterializationSamples).Assembly.Location);
        var function = Raise(source, typeof(ExactPointerSlotMaterializationSamples).FullName!,
            nameof(ExactPointerSlotMaterializationSamples.Accumulate));

        Assert.NotEmpty(function.Descendants.OfType<PointerElementCompoundAssignment>());
        Assert.DoesNotContain("S_", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void AddressIndexAndRightOperandMoveOnceInTheirOriginalOrder()
    {
        var function = Synthetic("calls");
        var calls = function.Descendants.OfType<Call>().ToArray();
        Assert.Equal(["Address", "Index", "Right"], calls.Select(call => call.Callee.Name));

        new PointerElementCompoundAssignmentPass().Run(function, PassContext.None);

        var assignment = Assert.Single(function.Descendants.OfType<PointerElementCompoundAssignment>());
        Assert.Same(calls[0], assignment.Pointer);
        Assert.Same(calls[1], assignment.Index);
        Assert.Same(calls[2], assignment.Value);
        Assert.Equal(calls, function.Descendants.OfType<Call>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void NestedSlotPoolsDoNotBecomeOuterObservationsOrRewriteTargets()
    {
        var outer = Synthetic("calls");
        var inner = Synthetic("calls");
        var before = inner.Body.Descendants.ToArray();
        var innerBody = inner.Body;
        innerBody.Detach();
        var lambda = new Lambda(
            TypeRef.Definition("Samples", "Samples", "UpdateAction"),
            inner.Signature.Parameters, [], [], false, false, innerBody);
        Assert.Single(outer.Body.Children.OfType<Block>()).Add(new ExpressionStatement(lambda));

        new PointerElementCompoundAssignmentPass().Run(outer, PassContext.None);

        Assert.Single(outer.DescendantsOutsideNestedFunctions.OfType<PointerElementCompoundAssignment>());
        Assert.Equal(before, lambda.Body.Descendants);
        outer.CheckInvariant();
    }

    [Theory]
    [InlineData("additional-load")]
    [InlineData("additional-store")]
    [InlineData("intervening-statement")]
    [InlineData("different-destination")]
    [InlineData("different-read")]
    [InlineData("read-width")]
    [InlineData("write-width")]
    [InlineData("right-type")]
    [InlineData("volatile-read")]
    [InlineData("volatile-write")]
    [InlineData("checked-update")]
    [InlineData("checked-address")]
    [InlineData("checked-scale")]
    [InlineData("checked-conversion")]
    [InlineData("unsigned-conversion")]
    [InlineData("unsigned-index")]
    [InlineData("no-native-conversion")]
    [InlineData("commuted-pointer")]
    [InlineData("wrong-scale")]
    [InlineData("unaligned-offset")]
    [InlineData("division")]
    [InlineData("capture-entry")]
    [InlineData("update-entry")]
    public void UnownedOrUnprovenUpdatesRemainUnchanged(string shape)
    {
        var function = Synthetic(shape);
        string before = IrPrinter.Dump(function);

        new PointerElementCompoundAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<PointerElementCompoundAssignment>());
        Assert.Equal(before, IrPrinter.Dump(function));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task AdmittedUpdatesAndRetainedCaptureRecompileExactlyWithoutTheFloor(bool updated)
    {
        string[] methods =
        [
            "Accumulate", "Signed32", "Unsigned32", "Signed64", "Operations", "ConstantIndex",
            "SideEffects", "MutatingPointer", "MutatingIndex", "RetainedCapture",
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
        IrExpression pointer = shape == "calls"
            ? Call("Address", Pointer)
            : new LoadArgument(0, "values", Pointer);
        IrExpression index = shape == "calls"
            ? Call("Index", Int32)
            : new LoadArgument(1, "index",
                shape == "unsigned-index" ? TypeRef.CoreLib("System", "UInt32") : Int32);
        IrExpression nativeIndex = shape == "no-native-conversion"
            ? index
            : new Convert(NativeInt, shape == "checked-conversion", shape == "unsigned-conversion", index);
        IrExpression offset = shape == "unaligned-offset"
            ? new Constant(3, Int32)
            : new Binary(BinaryKind.Multiply, shape == "checked-scale", false, nativeIndex,
                new Constant(shape == "wrong-scale" ? 4 : 8, Int32));
        var address = shape == "commuted-pointer"
            ? new Binary(BinaryKind.Add, false, false, offset, pointer)
            : new Binary(BinaryKind.Add, shape == "checked-address", false, pointer, offset);
        var capture = new StoreStackSlot(0, address);
        var read = new LoadIndirect(shape == "read-width" ? Int32 : Int64,
            new LoadStackSlot(shape == "different-read" ? 1 : 0, Pointer))
        {
            IsVolatile = shape == "volatile-read",
        };
        IrExpression value = shape == "calls"
            ? Call("Right", UInt64)
            : new LoadArgument(2, "value", shape == "right-type" ? Int32 : UInt64);
        var operation = new Binary(shape == "division" ? BinaryKind.Divide : BinaryKind.Add,
            shape == "checked-update", false, read, value);
        var update = new StoreIndirect(shape == "write-width" ? Int32 : Int64,
            new LoadStackSlot(shape == "different-destination" ? 1 : 0, Pointer), operation)
        {
            IsVolatile = shape == "volatile-write",
        };
        var block = new Block(0);
        block.Add(capture);
        if (shape == "intervening-statement")
            block.Add(new ExpressionStatement(Call("Observe", Void)));
        block.Add(update);
        if (shape == "additional-load")
            block.Add(new ExpressionStatement(new LoadStackSlot(0, Pointer)));
        if (shape == "additional-store")
            block.Add(new StoreStackSlot(0, new LoadArgument(0, "values", Pointer)));
        var body = new BlockContainer();
        body.Add(block);
        if (shape is "capture-entry" or "update-entry")
        {
            capture.SetSourceOffset(10);
            update.SetSourceOffset(20);
            var external = new Block(30);
            external.Add(new Branch(shape == "capture-entry" ? 10 : 20));
            body.Add(external);
        }
        return new IrFunction("Update", Owner,
            new MethodSignature(Void,
                [new Parameter("values", Pointer), new Parameter("index", Int32), new Parameter("value", UInt64)],
                HasThis: false, GenericParameterCount: 0), [], body);
    }

    static Call Call(string name, TypeRef result)
        => new(new MethodRef(Owner, name, result, [], HasThis: false), isVirtual: false, []);
}
