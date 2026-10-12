using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class BooleanFoldingSlotIdentityTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

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
