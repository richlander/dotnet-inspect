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
        Assert.Equal("set_X", Assert.Single(withExpression.Entries).ConsumedMethod?.Name);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { X = dx };", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void CompilerGeneratedCloneWithKeywordField_RaisesToWithExpression()
    {
        var function = FunctionWithField("else");

        new WithExpressionPass().Run(function, PassContext.None);

        var withExpression = Assert.Single(function.Descendants.OfType<WithExpression>());
        Assert.Equal(["else"], withExpression.Members);
        Assert.Empty(function.Descendants.OfType<StoreField>());
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { @else = dx };", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void CompilerGeneratedCloneWithInvalidFieldName_DoesNotRaise()
    {
        var function = FunctionWithField("bad-name");

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Single(function.Descendants.OfType<StoreField>());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    [Fact]
    public void MultipleContiguousMutations_RaiseInSourceOrder()
    {
        var function = FunctionWithClone(compilerGenerated: true, secondMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        var withExpression = Assert.Single(function.Descendants.OfType<WithExpression>());
        Assert.Equal(["X", "Y"], withExpression.Members);
        Assert.Equal(["set_X", "set_Y"], withExpression.Entries.Select(entry => entry.ConsumedMethod?.Name));
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { X = dx, Y = dy };", output);
    }

    [Fact]
    public void MultipleMutationsThroughCompilerCopy_RaiseInSourceOrder()
    {
        var function = FunctionWithClone(compilerGenerated: true, secondMember: true, copyBeforeSecondMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        var withExpression = Assert.Single(function.Descendants.OfType<WithExpression>());
        Assert.Equal(["X", "Y"], withExpression.Members);
        Assert.Equal(["set_X", "set_Y"], withExpression.Entries.Select(entry => entry.ConsumedMethod?.Name));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return point with { X = dx, Y = dy };", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
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
    public void DuplicateMemberMutationThroughCopy_DoesNotFoldIntoInvalidWithExpression()
    {
        var function = FunctionWithClone(
            compilerGenerated: true,
            secondMember: true,
            copyBeforeSecondMember: true,
            duplicateMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
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
    public void CloneCopyWithExtraUse_DoesNotFoldIntoSingleExpression()
    {
        var function = FunctionWithClone(
            compilerGenerated: true,
            secondMember: true,
            copyBeforeSecondMember: true,
            extraUseAfterCopy: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "<Clone>$");
        function.CheckInvariant();
    }

    [Fact]
    public void UnrelatedStackSlotCopy_DoesNotJoinWithExpressionRun()
    {
        var function = FunctionWithClone(
            compilerGenerated: true,
            secondMember: true,
            unrelatedCopyBeforeSecondMember: true);

        new WithExpressionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WithExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        Assert.Equal(3, function.Descendants.OfType<StoreStackSlot>().Count());
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
        bool duplicateMember = false,
        bool copyBeforeSecondMember = false,
        bool extraUseAfterCopy = false,
        bool unrelatedCopyBeforeSecondMember = false)
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
        const int copySlot = 257;
        const int unrelatedSlot = 258;
        int activeSlot = slot;
        var block = new Block();
        if (unrelatedCopyBeforeSecondMember)
            block.Add(new StoreStackSlot(unrelatedSlot, new LoadArgument(0, "point", Point)));
        if (receiverUsesTargetSlot)
            block.Add(new StoreStackSlot(slot, new LoadArgument(0, "point", Point)));
        block.Add(new StoreStackSlot(
            slot,
            new Call(clone, isVirtual: true, [receiverUsesTargetSlot
                ? new LoadStackSlot(slot, Point)
                : new LoadArgument(0, "point", Point)])));
        block.Add(new StoreProperty(Setter("X"), new LoadStackSlot(slot, Point), [], new LoadArgument(1, "dx", Int32)));
        if (secondMember)
        {
            if (copyBeforeSecondMember)
            {
                block.Add(new StoreStackSlot(copySlot, new LoadStackSlot(activeSlot, Point)));
                activeSlot = copySlot;
            }
            else if (unrelatedCopyBeforeSecondMember)
            {
                block.Add(new StoreStackSlot(copySlot, new LoadStackSlot(unrelatedSlot, Point)));
                activeSlot = copySlot;
            }
            if (extraUseAfterCopy)
                block.Add(new ExpressionStatement(new Call(keepAlive, isVirtual: false, [new LoadStackSlot(activeSlot, Point)])));
            block.Add(new StoreProperty(
                Setter(duplicateMember ? "X" : "Y"),
                new LoadStackSlot(activeSlot, Point),
                [],
                new LoadArgument(2, "dy", Int32)));
        }
        if (keepAliveUse)
            block.Add(new ExpressionStatement(new Call(keepAlive, isVirtual: false, [new LoadStackSlot(slot, Point)])));
        block.Add(new Return(new LoadStackSlot(activeSlot, Point)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "Shift",
            TypeRef.Definition("SyntheticAssembly", "Synthetic", "Owner"),
            new MethodSignature(Point, [new Parameter("point", Point), new Parameter("dx", Int32), new Parameter("dy", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithField(string fieldName)
    {
        var clone = new MethodRef(Point, "<Clone>$", Point, [], HasThis: true)
        {
            CompilerGenerated = MetadataFactState.Yes,
        };
        var field = new FieldRef(Point, fieldName, Int32);
        const int slot = 256;
        var block = new Block();
        block.Add(new StoreStackSlot(
            slot,
            new Call(clone, isVirtual: true, [new LoadArgument(0, "point", Point)])));
        block.Add(new StoreField(
            field,
            new LoadStackSlot(slot, Point),
            new LoadArgument(1, "dx", Int32)));
        block.Add(new Return(new LoadStackSlot(slot, Point)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "Shift",
            TypeRef.Definition("SyntheticAssembly", "Synthetic", "Owner"),
            new MethodSignature(
                Point,
                [new Parameter("point", Point), new Parameter("dx", Int32)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }
}
