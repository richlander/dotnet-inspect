using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class WithExpressionPassTests
{
    static readonly TypeRef Point = TypeRef.Definition("SyntheticAssembly", "Synthetic", "Point");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void CompilerGeneratedCloneWithSingleMutation_RaisesToWithExpression()
    {
        var function = FunctionWithClone(compilerGenerated: true);

        new WithExpressionPass().Run(function, PassContext.None);

        var withExpression = Assert.Single(function.Descendants.OfType<WithExpression>());
        Assert.Equal(["X"], withExpression.Members);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { X = dx };", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void MultipleContiguousMutations_RaiseInSourceOrder()
    {
        var function = FunctionWithClone(compilerGenerated: true, secondMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        var withExpression = Assert.Single(function.Descendants.OfType<WithExpression>());
        Assert.Equal(["X", "Y"], withExpression.Members);
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { X = dx, Y = dy };", output);
    }

    [Fact]
    public void SameNamedNonGeneratedClone_DoesNotRaise()
    {
        var function = FunctionWithClone(compilerGenerated: false);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    [Fact]
    public void DuplicateMemberMutation_DoesNotFoldIntoInvalidWithExpression()
    {
        var function = FunctionWithClone(compilerGenerated: true, secondMember: true, duplicateMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    [Fact]
    public void CloneWithExtraUse_DoesNotFoldIntoSingleExpression()
    {
        var function = FunctionWithClone(compilerGenerated: true, keepAliveUse: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    [Fact]
    public void CloneReceiverThatReferencesTargetSlot_DoesNotEraseReceiver()
    {
        var function = FunctionWithClone(compilerGenerated: true, receiverUsesTargetSlot: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    static IrFunction FunctionWithClone(
        bool compilerGenerated,
        bool keepAliveUse = false,
        bool receiverUsesTargetSlot = false,
        bool secondMember = false,
        bool duplicateMember = false)
    {
        var clone = new MethodRef(Point, "<Clone>$", Point, [], HasThis: true)
        {
            CompilerGenerated = compilerGenerated ? MetadataFactState.Yes : MetadataFactState.No,
        };
        MethodRef Setter(string name) => new(Point, $"set_{name}", Void, [Int32], HasThis: true)
        {
            IsSpecialName = true,
        };
        var keepAlive = new MethodRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", Void, [TypeRef.CoreLib("System", "Object")], HasThis: false);

        const int slot = 256;
        var block = new Block();
        if (receiverUsesTargetSlot)
            block.Add(new StoreStackSlot(slot, new LoadArgument(0, "point", Point)));
        block.Add(new StoreStackSlot(
            slot,
            new Call(clone, isVirtual: true, [receiverUsesTargetSlot
                ? new LoadStackSlot(slot, Point)
                : new LoadArgument(0, "point", Point)])));
        block.Add(new StoreProperty(Setter("X"), new LoadStackSlot(slot, Point), [], new LoadArgument(1, "dx", Int32)));
        if (secondMember)
            block.Add(new StoreProperty(
                Setter(duplicateMember ? "X" : "Y"),
                new LoadStackSlot(slot, Point),
                [],
                new LoadArgument(2, "dy", Int32)));
        if (keepAliveUse)
            block.Add(new ExpressionStatement(new Call(keepAlive, isVirtual: false, [new LoadStackSlot(slot, Point)])));
        block.Add(new Return(new LoadStackSlot(slot, Point)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "Shift",
            TypeRef.Definition("SyntheticAssembly", "Synthetic", "Owner"),
            new MethodSignature(Point, [new Parameter("point", Point), new Parameter("dx", Int32), new Parameter("dy", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
