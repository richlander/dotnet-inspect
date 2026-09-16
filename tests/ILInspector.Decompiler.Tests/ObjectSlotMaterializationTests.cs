using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ObjectSlotMaterializationTests
{
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactObjectPreservesNullOrBoxWithoutExpandingConversions(bool box, bool untypedLoad)
    {
        IrExpression value = box ? new Box(Int32, new Constant(42, Int32)) : new Constant(null, Object);
        var function = Function(Object,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, untypedLoad ? null : Object)));

        Assert.False(CoercionDomain.InDomain(Object, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(Object, Assert.Single(function.Locals));
        Assert.Same(value, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Contains("object S_0", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectObserversMustAllAgree(bool underivable)
    {
        IrNode observer = underivable
            ? new ExpressionStatement(new LoadStackSlot(0, type: null))
            : Observe(new LoadStackSlot(0, String), String);
        var function = Function(Object,
            new StoreStackSlot(0, new Constant(null, Object)),
            observer,
            new Return(new LoadStackSlot(0, Object)));

        Assert.Equal(underivable ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ObjectTestimonyDoesNotInventReferenceConversionsOrBoxing(bool mixed, bool unboxed)
    {
        var nonExact = unboxed ? new Constant(42, Int32) : new Constant("text", String);
        var function = mixed
            ? Function(Object,
                new StoreStackSlot(0, new Box(Int32, new Constant(42, Int32))),
                Observe(new LoadStackSlot(0, Object), Object),
                new StoreStackSlot(0, nonExact),
                new Return(new LoadStackSlot(0, Object)))
            : Function(Object,
                new StoreStackSlot(0, nonExact),
                new Return(new LoadStackSlot(0, Object)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function))
            .Vetoes.HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("generic")]
    [InlineData("rectangular-array")]
    public void OnlyNominalCoreObjectIsNewlyAdmitted(string kind)
    {
        var type = kind switch
        {
            "foreign" => TypeRef.Definition("Other", "System", "Object"),
            "generic" => TypeRef.GenericInstance(Object, [Int32]),
            "rectangular-array" => TypeRef.MdArray(Object, 2),
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
    public void ObjectCopyComponentsRemainAtomic(bool incomplete)
    {
        var value = new Box(Int32, new Constant(42, Int32));
        var function = Function(incomplete ? Void : Object,
            new StoreStackSlot(0, value),
            new StoreStackSlot(1, new LoadStackSlot(0, Object)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, Object)));
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
        Assert.Equal([Object, Object], function.Locals);
        Assert.Same(value, Assert.Single(function.Descendants.OfType<Box>()));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ObjectSlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(ObjectSlotMaterializationSamples.BoxAndObserve))]
    public void CompilerProducedRetainedObjectsMaterialize(string method)
    {
        using var source = MetadataSource.Open(typeof(ObjectSlotMaterializationSamples).Assembly.Location);
        AssertMaterializes(source, typeof(ObjectSlotMaterializationSamples).FullName!, method, Object, "object");
    }

    [Fact]
    public void RealRoslynObjectWebMaterializes()
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        AssertMaterializes(source, "Microsoft.CodeAnalysis.ExceptionUtilities", "UnexpectedValue", Object, "object");
    }

    [Fact]
    public void CompilerProducedObjectSwapKeepsTheExistingRaise()
    {
        using var source = MetadataSource.Open(typeof(ObjectSlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(ObjectSlotMaterializationSamples).FullName!,
            nameof(ObjectSlotMaterializationSamples.SwapObjects));

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
    [InlineData(nameof(ObjectSlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(ObjectSlotMaterializationSamples.BoxAndObserve))]
    [InlineData(nameof(ObjectSlotMaterializationSamples.SwapObjects))]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedObjectFixturesRecompileExactly(string methodName)
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(ObjectSlotMaterializationSamples).Assembly.Location,
            type => type == typeof(ObjectSlotMaterializationSamples).FullName,
            method => method.Method == methodName));
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    static ExpressionStatement Observe(IrExpression value, TypeRef type) =>
        new(new Call(new MethodRef(Owner, "Observe", Void, [type], HasThis: false),
            isVirtual: false, [value]));
}
