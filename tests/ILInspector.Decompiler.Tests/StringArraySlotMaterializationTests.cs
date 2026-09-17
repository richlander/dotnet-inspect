using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class StringArraySlotMaterializationTests
{
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef StringArray = TypeRef.SzArray(String);
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef ObjectArray = TypeRef.SzArray(Object);
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
    public void ExactStringArrayPreservesItsProducerAndConversionDomain(string producer, bool untypedLoad)
    {
        IrExpression value = producer switch
        {
            "null" => new Constant(null, StringArray),
            "allocation" => new NewArray(String, new Constant(4, Int32)),
            "initializer" => new ArrayLiteral(String, StringArray, [new Constant("value", String)]),
            "cast" => new CastClass(StringArray, new Constant(null, ObjectArray)),
            _ => throw new ArgumentOutOfRangeException(nameof(producer)),
        };
        var function = Function(StringArray,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, untypedLoad ? null : StringArray)));

        Assert.False(CoercionDomain.InDomain(StringArray, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(StringArray, Assert.Single(function.Locals));
        Assert.Same(value, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Contains("string[] S_0", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("rank-one")]
    public void UnspellableReferenceArrayShapesRemainDeferred(string kind)
    {
        var type = kind switch
        {
            "generic" => TypeRef.SzArray(TypeRef.GenericInstance(String, [Int32])),
            "rank-one" => TypeRef.MdArray(String, 1),
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
    public void ArrayCompatibilityDoesNotReplaceExactProducerTestimony(bool widen)
    {
        var target = widen ? ObjectArray : StringArray;
        var producer = widen ? StringArray : ObjectArray;
        var function = Function(target,
            new StoreStackSlot(0, new Constant(null, target)),
            Observe(new LoadStackSlot(0, target), target),
            new StoreStackSlot(0, new Constant(null, producer)),
            new Return(new LoadStackSlot(0, target)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function))
            .Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryArrayObserverMustTestify(bool underivable)
    {
        IrNode observer = underivable
            ? new ExpressionStatement(new LoadStackSlot(0, type: null))
            : Observe(new LoadStackSlot(0, ObjectArray), ObjectArray);
        var function = Function(StringArray,
            new StoreStackSlot(0, new Constant(null, StringArray)),
            observer,
            new Return(new LoadStackSlot(0, StringArray)));

        Assert.Equal(underivable ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void CovariantSinkDoesNotWidenExplicitStringArrayStorage()
    {
        var function = Function(ObjectArray,
            new StoreStackSlot(0, new NewArray(String, new Constant(1, Int32))),
            new Return(new LoadStackSlot(0, StringArray)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(StringArray, Assert.Single(function.Locals));
        Assert.Empty(function.Descendants.OfType<CastClass>());
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringArrayCopyComponentsRemainAtomic(bool covariantObserver)
    {
        var target = covariantObserver ? ObjectArray : StringArray;
        var allocation = new NewArray(String, new Constant(1, Int32));
        var function = Function(target,
            new StoreStackSlot(0, allocation),
            new StoreStackSlot(1, new LoadStackSlot(0, StringArray)),
            new Return(new LoadStackSlot(1, target)));
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (covariantObserver)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Equal([StringArray, StringArray], function.Locals);
        Assert.Same(allocation, Assert.Single(function.Descendants.OfType<NewArray>()));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(StringArraySlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.MutateAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.CovariantReturn))]
    public void CompilerProducedRetainedArraysMaterialize(string method)
    {
        using var source = MetadataSource.Open(typeof(StringArraySlotMaterializationSamples).Assembly.Location);
        AssertMaterializes(source, typeof(StringArraySlotMaterializationSamples).FullName!, method, StringArray, "string[]");
    }

    [Fact]
    public void RealRoslynStringArrayWebMaterializes()
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        AssertMaterializes(source, "Roslyn.Utilities.PathUtilities", "ExpandAbsolutePathWithRelativeParts",
            StringArray, "string[]");
    }

    [Fact]
    public void CompilerProducedStringArraySwapKeepsTheExistingRaise()
    {
        using var source = MetadataSource.Open(typeof(StringArraySlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(StringArraySlotMaterializationSamples).FullName!,
            nameof(StringArraySlotMaterializationSamples.SwapArrays));
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
    [Trait("Speed", "Slow")]
    [InlineData(nameof(StringArraySlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.MutateAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.CovariantReturn))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.SwapArrays))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.MutateAndReplace))]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedStringArrayFixturesRecompileExactly(string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(StringArraySlotMaterializationSamples).Assembly.Location,
            type => type == typeof(StringArraySlotMaterializationSamples).FullName,
            method => method.Method == methodName));
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    static ExpressionStatement Observe(IrExpression value, TypeRef type) =>
        new(new Call(new MethodRef(Owner, "Observe", Void, [type], HasThis: false),
            isVirtual: false, [value]));
}
