using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ByteArraySlotMaterializationTests
{
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef ByteArray = TypeRef.SzArray(Byte);
    static readonly TypeRef SByteArray = TypeRef.SzArray(TypeRef.CoreLib("System", "SByte"));
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Theory]
    [InlineData("null", false)]
    [InlineData("null", true)]
    [InlineData("allocation", false)]
    [InlineData("allocation", true)]
    [InlineData("initializer", false)]
    [InlineData("initializer", true)]
    [InlineData("cast", false)]
    [InlineData("cast", true)]
    public void ExactByteArrayPreservesItsProducerAndConversionDomain(string producer, bool untypedLoad)
    {
        IrExpression value = producer switch
        {
            "null" => new Constant(null, ByteArray),
            "allocation" => new NewArray(Byte, new Constant(4, Int32)),
            "initializer" => new ArrayLiteral(Byte, ByteArray, [new Constant((byte)255, Byte)]),
            "cast" => new CastClass(ByteArray, new Constant(null, Object)),
            _ => throw new ArgumentOutOfRangeException(nameof(producer)),
        };
        var function = Function(ByteArray,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, untypedLoad ? null : ByteArray)));

        Assert.False(CoercionDomain.InDomain(ByteArray, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(ByteArray, Assert.Single(function.Locals));
        Assert.Same(value, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Contains("byte[] S_0", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("rank-one")]
    [InlineData("rectangular")]
    public void UnspellableAndNonSzArrayShapesRemainDeferred(string kind)
    {
        var type = kind switch
        {
            "generic" => TypeRef.SzArray(TypeRef.GenericInstance(Byte, [Int32])),
            "rank-one" => TypeRef.MdArray(Byte, 1),
            "rectangular" => TypeRef.MdArray(Byte, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryProducerMustAlreadyBeTheExactArrayType(bool signedArray)
    {
        var function = Function(ByteArray,
            new StoreStackSlot(0, new NewArray(Byte, new Constant(1, Int32))),
            Observe(new LoadStackSlot(0, ByteArray), ByteArray),
            new StoreStackSlot(0, new Constant(null, signedArray ? SByteArray : Object)),
            new Return(new LoadStackSlot(0, ByteArray)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function))
            .Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayObserversMustAllAgree(bool underivable)
    {
        IrNode observer = underivable
            ? new ExpressionStatement(new LoadStackSlot(0, type: null))
            : Observe(new LoadStackSlot(0, SByteArray), SByteArray);
        var function = Function(ByteArray,
            new StoreStackSlot(0, new Constant(null, ByteArray)),
            observer,
            new Return(new LoadStackSlot(0, ByteArray)));

        Assert.Equal(underivable ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteArrayCopyComponentsRemainAtomic(bool incomplete)
    {
        var allocation = new NewArray(Byte, new Constant(1, Int32));
        var function = Function(incomplete ? Void : ByteArray,
            new StoreStackSlot(0, allocation),
            new StoreStackSlot(1, new LoadStackSlot(0, ByteArray)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, ByteArray)));
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Equal([ByteArray, ByteArray], function.Locals);
        Assert.Same(allocation, Assert.Single(function.Descendants.OfType<NewArray>()));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.MutateAndObserve))]
    public void CompilerProducedRetainedArraysMaterialize(string method)
    {
        using var source = MetadataSource.Open(typeof(ByteArraySlotMaterializationSamples).Assembly.Location);
        AssertMaterializes(source, typeof(ByteArraySlotMaterializationSamples).FullName!, method, ByteArray, "byte[]");
    }

    [Theory]
    [InlineData("Microsoft.CodeAnalysis.LittleEndianReader", "ReadReversed")]
    [InlineData("Microsoft.CodeAnalysis.CryptoBlobParser", "ReadReversed")]
    public void RealRoslynByteArrayWebsMaterialize(string type, string method)
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        AssertMaterializes(source, type, method, ByteArray, "byte[]");
    }

    [Fact]
    public void CompilerProducedByteArraySwapKeepsTheExistingRaise()
    {
        using var source = MetadataSource.Open(typeof(ByteArraySlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(ByteArraySlotMaterializationSamples).FullName!,
            nameof(ByteArraySlotMaterializationSamples.SwapArrays));
        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingStorageSwap);

        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Contains("(first, second) = (second, first);", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.MutateAndObserve))]
    [InlineData(nameof(ByteArraySlotMaterializationSamples.SwapArrays))]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedByteArrayFixturesRecompileExactly(string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(ByteArraySlotMaterializationSamples).Assembly.Location,
            type => type == typeof(ByteArraySlotMaterializationSamples).FullName,
            method => method.Method == methodName));
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    static ExpressionStatement Observe(IrExpression value, TypeRef type) =>
        new(new Call(new MethodRef(Owner, "Observe", Void, [type], HasThis: false),
            isVirtual: false, [value]));
}
