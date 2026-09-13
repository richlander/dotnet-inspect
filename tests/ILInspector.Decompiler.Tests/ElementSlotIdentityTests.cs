using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ElementSlotIdentityTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef Kind = TypeRef.Definition("Synthetic", "Samples", "Kind");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerProducedElementConditionalMaterializesIdentity(bool enumElement)
    {
        var sample = enumElement ? typeof(ElementSlotIdentitySamples) : typeof(CfgSampleClass);
        string method = enumElement
            ? nameof(ElementSlotIdentitySamples.StoreKind)
            : nameof(CfgSampleClass.CharConditionalElementStore);
        using var source = MetadataSource.Open(sample.Assembly.Location);
        var function = IrImporter.Import(source, sample.FullName!, method);
        Assert.NotNull(function);
        var context = PassContext.ForImport(reference => IrImporter.Import(source, reference));
        foreach (var pass in IrPasses.Default)
        {
            if (pass is SlotMaterializationPass)
                break;
            pass.Run(function, context);
        }

        var element = Assert.Single(function.Descendants.OfType<StoreElement>());
        var target = CoercionSinks.StoreElementTarget(element, function.TypeShapes);
        var load = Assert.IsType<LoadStackSlot>(element.Value);
        Assert.Equal(Int32, load.Type);
        Assert.Equal(Int32, CoercionSinks.TestifiedSlotTypes(
            function.Body, function.Signature.ReturnType, function.TypeShapes)[load.Slot]);
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function),
            candidate => candidate.Slot == load.Slot);
        Assert.True(decision.WillMaterialize);
        Assert.Equal(target, decision.Type);

        new SlotMaterializationPass().Run(function, context);
        new CoercionInsertionPass().Run(function, context);

        Assert.IsType<LoadLocal>(element.Value);
        Assert.Equal(target, element.Value.ResultType);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), node => node.Slot == load.Slot);
        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(), node => node.Slot == load.Slot);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedElementConditionalBindsWithRetainedTemporaries(bool enumElement)
    {
        var sample = enumElement ? typeof(ElementSlotIdentitySamples) : typeof(CfgSampleClass);
        string method = enumElement
            ? nameof(ElementSlotIdentitySamples.StoreKind)
            : nameof(CfgSampleClass.CharConditionalElementStore);
        var result = Assert.Single(FidelityCheck.Evaluate(
            sample.Assembly.Location, type => type == sample.FullName,
            candidate => candidate.Method == method));

        // Both renders retain index/value temporaries; this gates binding,
        // not opcode-exact lowering of the original stack-only assignment.
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.Status);
        Assert.NotNull(result.RecompiledOpcodes);
        Assert.Contains("stloc", result.RecompiledOpcodes);
    }

    [Theory]
    [InlineData(0, 65535)]
    [InlineData(43, 45)]
    public void MaterializesRepresentableCharConditional(int left, int right)
    {
        var function = Function(Char, Conditional(left, right));
        AssertMaterializes(function, Char);
    }

    [Theory]
    [InlineData("SByte", -128, 127)]
    [InlineData("Byte", 0, 255)]
    [InlineData("Int16", -32768, 32767)]
    [InlineData("UInt16", 0, 65535)]
    [InlineData("Int32", int.MinValue, int.MaxValue)]
    [InlineData("UInt32", 0, int.MaxValue)]
    public void MaterializesEnumConditionalUsingResolvedBackingRange(string backing, int left, int right)
    {
        var function = EnumFunction(Conditional(left, right), backing);
        AssertMaterializes(function, Kind);
    }

    [Theory]
    [InlineData(-1, 45)]
    [InlineData(43, 65536)]
    public void OutOfRangeCharArmRetainsElementVeto(int left, int right)
        => AssertElementVeto(Function(Char, Conditional(left, right)));

    [Theory]
    [InlineData("Byte", -1)]
    [InlineData("Byte", 256)]
    [InlineData("SByte", 128)]
    [InlineData("UInt16", 65536)]
    [InlineData("UInt32", -1)]
    [InlineData(null, 1)]
    public void EnumRangeOrMissingBackingRetainsElementVeto(string? backing, int value)
        => AssertElementVeto(EnumFunction(Conditional(value, 0), backing));

    [Fact]
    public void WideEnumTargetStillRequiresSlotCoercionEvidence()
    {
        var function = EnumFunction(Conditional(1, 0), "Int64");
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(Kind, decision.Type);
        Assert.Equal(SlotMaterializationVeto.UnrenderableStoreType, decision.Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void WideCharProducerStillRequiresSlotCoercionEvidence()
    {
        var wide = TypeRef.CoreLib("System", "Int64");
        var function = Function(Char, new Conditional(new LoadArgument(0, "flag", Boolean),
            new Constant(43L, wide), new Constant(45L, wide)));
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(Char, decision.Type);
        Assert.Equal(SlotMaterializationVeto.UnrenderableStoreType, decision.Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void UnknownEnumShapeDoesNotRecoverIdentity()
    {
        var function = EnumFunction(Conditional(1, 0), "Int32");
        function.TypeShapes = new Dictionary<TypeRef, TypeShape>();
        AssertElementVeto(function);
    }

    [Fact]
    public void NonconstantArmRetainsElementVeto()
        => AssertElementVeto(Function(Char, new Conditional(
            new LoadArgument(0, "flag", Boolean),
            new Constant(43, Int32), new LoadArgument(2, "value", Int32))));

    [Theory]
    [InlineData("numeric")]
    [InlineData("other-element")]
    [InlineData("underivable")]
    public void EveryObserverStillTestifies(string observer)
    {
        var additional = new LoadStackSlot(0, observer == "underivable" ? null : Int32);
        var function = Function(Char, Conditional(43, 45), observer switch
        {
            "numeric" => new StoreArgument(2, "value", Int32, additional),
            "other-element" => new StoreElement(Int32,
                new LoadArgument(3, "numbers", TypeRef.SzArray(Int32)), new Constant(0, Int32), additional),
            "underivable" => new ExpressionStatement(additional),
            _ => throw new ArgumentOutOfRangeException(nameof(observer)),
        });
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(observer == "underivable"
            ? SlotMaterializationVeto.UnderivableTypeTestimony
            : SlotMaterializationVeto.ConflictingTypeTestimony, decision.Vetoes);
        AssertRetained(function);
    }

    [Fact]
    public void EveryStoreMustPreserveTheElementValue()
    {
        var function = Function(Char, Conditional(43, 45),
            new StoreStackSlot(0, Conditional(43, 65536)),
            Element(Char, new LoadStackSlot(0, Int32)));
        AssertElementVeto(function);
    }

    [Fact]
    public void UndecidedDirectCopyRetainsTheWholeComponent()
    {
        var function = Function(Char, Conditional(43, 45),
            new StoreStackSlot(1, new LoadStackSlot(0, Int32)),
            Element(Char, new LoadStackSlot(1, Int32)));
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, decision =>
            Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
        AssertRetained(function);
    }

    static Conditional Conditional(int left, int right)
        => new(new LoadArgument(0, "flag", Boolean),
            new Constant(left, Int32), new Constant(right, Int32));

    static StoreElement Element(TypeRef type, IrExpression value)
        => new(type.Equals(Kind) ? Int32 : type,
            new LoadArgument(1, "values", TypeRef.SzArray(type)), new Constant(0, Int32), value);

    static IrFunction Function(TypeRef target, IrExpression value, params IrNode[] observers)
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, value));
        block.Add(Element(target, new LoadStackSlot(0, Int32)));
        foreach (var observer in observers)
            block.Add(observer);
        var body = new BlockContainer();
        body.Add(block);
        return new("M", Owner, new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flag", Boolean), new Parameter("values", TypeRef.SzArray(target)),
                new Parameter("value", Int32), new Parameter("numbers", TypeRef.SzArray(Int32))],
            HasThis: false, GenericParameterCount: 0), [], body);
    }

    static IrFunction EnumFunction(IrExpression value, string? backing)
    {
        var function = Function(Kind, value);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [Kind] = TypeShape.Enum };
        if (backing is not null)
            function.EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>
                { [Kind] = TypeRef.CoreLib("System", backing) };
        return function;
    }

    static void AssertMaterializes(IrFunction function, TypeRef target)
    {
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize);
        Assert.Equal(target, decision.Type);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Equal(target, Assert.Single(function.Locals));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant();
    }

    static void AssertElementVeto(IrFunction function)
    {
        Assert.Equal(SlotMaterializationVeto.ElementStoreIdentityRecovery,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    static void AssertRetained(IrFunction function)
    {
        int stores = function.Descendants.OfType<StoreStackSlot>().Count();
        int loads = function.Descendants.OfType<LoadStackSlot>().Count();
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Equal(stores, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Equal(loads, function.Descendants.OfType<LoadStackSlot>().Count());
        Assert.Empty(function.Locals);
        function.CheckInvariant();
    }
}
