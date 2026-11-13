using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class BooleanFoldingSlotIdentityTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Kind = TypeRef.Definition("Synthetic", "Samples", "ArgumentKind");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");

    [Fact]
    public void EnumMergedConditionalDoesNotAcquireBooleanIdentity()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", Boolean),
            new Constant(1, Kind),
            new Constant(0, Kind))
        {
            MergedType = Kind,
        };
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, conditional));
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "kind", Kind)));
        block.Add(new StoreLocal(0, Kind, new LoadStackSlot(0, Kind)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("flag", Boolean), new Parameter("kind", Kind)],
                HasThis: false,
                GenericParameterCount: 0),
            [Kind],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [Kind] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [Kind] = Int32 },
        };

        new BooleanFoldingPass().Run(function, PassContext.None);

        Assert.Equal(Kind, conditional.MergedType);
        Assert.Equal(Kind, Assert.IsType<Constant>(conditional.WhenTrue).Type);
        Assert.Equal(Kind, Assert.IsType<Constant>(conditional.WhenFalse).Type);
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        Assert.Equal(Kind, decision.Type);
        function.CheckInvariant();
    }

    [Fact]
    public void NonEnumZeroOneConditionalKeepsExistingBooleanMaterialization()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", Boolean),
            new Constant(1, Int32),
            new Constant(0, Int32))
        {
            MergedType = Int32,
        };
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, conditional));
        block.Add(new StoreStackSlot(0, new Constant("unrelated", String)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        new BooleanFoldingPass().Run(function, PassContext.None);

        Assert.Equal(Boolean, conditional.MergedType);
        Assert.Equal(true, Assert.IsType<Constant>(conditional.WhenTrue).Value);
        Assert.Equal(false, Assert.IsType<Constant>(conditional.WhenFalse).Value);
        function.CheckInvariant();
    }

    [Fact]
    public void MixedIntegerStoresDoNotAcquireBooleanIdentity()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(0, Int32)));
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "count", Int32)));
        block.Add(new Return(new LogicalNot(new LoadStackSlot(0, Int32))));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                Boolean,
                [new Parameter("count", Int32)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.IsType<StoreStackSlot>(block.Children[0]).Value);
        Assert.Equal(0, constant.Value);
        Assert.Equal(Int32, constant.Type);
        Assert.Equal(Int32, Assert.Single(function.Descendants.OfType<LoadStackSlot>()).Type);
        function.CheckInvariant();
    }

    [Fact]
    public void UnclassifiedLoadDeclinesBooleanIdentityForTheWholeSlot()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(0, Int32)));
        block.Add(new StoreLocal(0, Boolean, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(1, Int32, new LoadStackSlot(0, type: null)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [Boolean, Int32],
            body);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.IsType<StoreStackSlot>(block.Children[0]).Value);
        Assert.Equal(0, constant.Value);
        Assert.Equal(Int32, constant.Type);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), static load => load.Type is null);
        function.CheckInvariant();
    }
}
