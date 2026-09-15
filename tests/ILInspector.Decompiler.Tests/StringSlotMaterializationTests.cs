using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class StringSlotMaterializationTests
{
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializesExactStringWithoutExpandingCoercionDomain(bool typedNull)
    {
        var function = Function(StringType,
            new StoreStackSlot(0, new Constant(typedNull ? null : "text", StringType)),
            new Return(new LoadStackSlot(0, StringType)));

        Assert.False(CoercionDomain.InDomain(StringType, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);

        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(StringType, Assert.Single(function.Locals));
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<Coerce>());
        Assert.Contains(typedNull ? "string S_0 = null;" : "string S_0 = \"text\";",
            CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void StringReturnSinkSuppliesUntypedLoadTestimony()
    {
        var function = Function(StringType,
            new StoreStackSlot(0, new Constant("text", StringType)),
            new Return(new LoadStackSlot(0, type: null)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize);
        Assert.Equal(StringType, decision.Type);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(StringType, Assert.Single(function.Descendants.OfType<LoadLocal>()).Type);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringObserversMustAllAgree(bool underivable)
    {
        IrNode observer = underivable
            ? new ExpressionStatement(new LoadStackSlot(0, type: null))
            : new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", Void, [Object], HasThis: false),
                isVirtual: false, [new LoadStackSlot(0, Object)]));
        var function = Function(StringType,
            new StoreStackSlot(0, new Constant("text", StringType)),
            observer,
            new Return(new LoadStackSlot(0, StringType)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(underivable
            ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony, decision.Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void ObjectTypedNullProducerStaysDeferred()
    {
        var function = Function(StringType,
            new StoreStackSlot(0, new Constant(null, Object)),
            new Return(new LoadStackSlot(0, StringType)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain
            | SlotMaterializationVeto.UnrenderableStoreType, decision.Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void EveryStringProducerMustAlreadyHaveTheTestifiedType()
    {
        var function = Function(StringType,
            new StoreStackSlot(0, new Constant("text", StringType)),
            new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", Void, [StringType], HasThis: false),
                isVirtual: false, [new LoadStackSlot(0, StringType)])),
            new StoreStackSlot(0, new Constant(null, Object)),
            new Return(new LoadStackSlot(0, StringType)));

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain
            | SlotMaterializationVeto.UnrenderableStoreType,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("foreign-string")]
    public void OtherExactReferenceTypesRemainDeferred(string kind)
    {
        var type = kind switch
        {
            "array" => TypeRef.SzArray(StringType),
            "foreign-string" => TypeRef.Definition("Other", "System", "String"),
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
    public void StringCopyComponentsRemainAtomic(bool incomplete)
    {
        IrNode consumer = incomplete
            ? new ExpressionStatement(new LoadStackSlot(1, type: null))
            : new Return(new LoadStackSlot(1, StringType));
        var function = Function(incomplete ? Void : StringType,
            new StoreStackSlot(0, new Constant("text", StringType)),
            new StoreStackSlot(1, new LoadStackSlot(0, StringType)),
            consumer);

        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
        }
        else
        {
            Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
            new SlotMaterializationPass().Run(function, PassContext.None);
            Assert.Equal([StringType, StringType], function.Locals);
            Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
            Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
            function.CheckInvariant(includeSemantics: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringMaterializationPreservesStructuralFoldBoundaries(bool crossBlock)
    {
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new Constant("first", StringType)));
        var second = crossBlock ? new Block(4) : first;
        second.Add(new StoreStackSlot(0, new Constant("second", StringType)));
        if (crossBlock)
        {
            first.Add(new Branch(4));
            second.Add(new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", Void, [StringType], HasThis: false),
                isVirtual: false, [new LoadStackSlot(0, StringType)])));
        }
        second.Add(new Return(new LoadStackSlot(0, StringType)));
        var body = new BlockContainer();
        body.Add(first);
        if (crossBlock)
            body.Add(second);
        var function = new IrFunction("M", Owner,
            new MethodSignature(StringType, [], HasThis: false, GenericParameterCount: 0),
            [], body);

        Assert.Equal(crossBlock
            ? SlotMaterializationVeto.CrossBlockStoreFold
            : SlotMaterializationVeto.MultiStoreSingleLoadFold,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData("Roslyn.Utilities.PathUtilities", "NormalizePathPrefix")]
    [InlineData("Roslyn.Utilities.StringExtensions", "GetWithSingleAttributeSuffix")]
    public void RealRoslynStringWebsMaterialize(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        AssertMaterializes(source, typeName, methodName, StringType, "string");
    }

    [Fact]
    public void CompilerProducedReadAndObserveMaterializesRetainedString()
    {
        using var source = MetadataSource.Open(typeof(StringSlotMaterializationSamples).Assembly.Location);
        AssertMaterializes(source, typeof(StringSlotMaterializationSamples).FullName!,
            nameof(StringSlotMaterializationSamples.ReadAndObserve), StringType, "string");
    }

    [Fact]
    public void CompilerProducedStringSwapRetainsItsPendingCarrier()
    {
        using var source = MetadataSource.Open(typeof(StringSlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(StringSlotMaterializationSamples).FullName!,
            nameof(StringSlotMaterializationSamples.SwapStrings));

        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingReferenceSwap);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Contains("(first, second) = (second, first);", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(StringSlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(StringSlotMaterializationSamples.SwapStrings))]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedStringFixturesRecompileExactly(string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(StringSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(StringSlotMaterializationSamples).FullName,
            method => method.Method == methodName));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

}
