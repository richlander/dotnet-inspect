using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class VoidNullConditionalPassTests
{
    static readonly TypeRef s_holder =
        TypeRef.Definition("Synthetic", "", "Holder");
    static readonly TypeRef s_resource =
        TypeRef.Definition("Synthetic", "", "Resource");
    static readonly TypeRef s_void =
        TypeRef.CoreLib("System", "Void");

    static IrFunction Build(
        bool externalCallArmEntry = false,
        bool receiverUsedAfter = false,
        bool mismatchedNullReturn = false)
    {
        const int valueSlot = 10;
        const int receiverSlot = 11;
        var get = new MethodRef(
            s_holder,
            "GetResource",
            s_resource,
            [],
            HasThis: false);
        var dispose = new MethodRef(
            s_resource,
            "Dispose",
            s_void,
            [],
            HasThis: true);

        var body = new BlockContainer();
        if (externalCallArmEntry)
        {
            var entry = new Block(-10);
            entry.Add(new ConditionalBranch(
                new Constant(true, TypeRef.CoreLib("System", "Boolean")),
                20));
            body.Add(entry);
        }

        var head = new Block(0);
        head.Add(new StoreStackSlot(
            valueSlot,
            new Call(get, isVirtual: false, [])));
        head.Add(new StoreStackSlot(
            receiverSlot,
            new LoadStackSlot(valueSlot, s_resource)));
        head.Add(new ConditionalBranch(
            new LoadStackSlot(valueSlot, s_resource),
            20));
        body.Add(head);

        var nullArm = new Block(10);
        nullArm.Add(mismatchedNullReturn
            ? new Throw(new Constant(null, s_resource))
            : new Return(null));
        body.Add(nullArm);

        var callArm = new Block(20);
        callArm.Add(new ExpressionStatement(
            new Call(
                dispose,
                isVirtual: true,
                [new LoadStackSlot(receiverSlot, s_resource)])));
        body.Add(callArm);

        var continuation = new Block(30);
        if (receiverUsedAfter)
        {
            continuation.Add(new ExpressionStatement(
                new Call(
                    dispose,
                    isVirtual: true,
                    [new LoadStackSlot(receiverSlot, s_resource)])));
        }
        continuation.Add(new Return(null));
        body.Add(continuation);

        return new IrFunction(
            "M",
            s_holder,
            new MethodSignature(
                s_void,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    [Fact]
    public void EffectfulVoidCall_RaisesQuestionDot_AndEvaluatesReceiverOnce()
    {
        var function = Build();

        new VoidNullConditionalPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var conditional = Assert.Single(
            function.Descendants.OfType<NullConditional>());
        var call = Assert.IsType<Call>(conditional.Member);
        Assert.Equal("GetResource", Assert.IsType<Call>(call.Arguments[0]).Callee.Name);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Equal(2, function.Body.Blocks.Count);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("GetResource()?.Dispose();", output);
        Assert.DoesNotContain("_ = GetResource()", output);
    }

    [Fact]
    public void ExternalEntryIntoCallArm_StaysFlat()
    {
        var function = Build(externalCallArmEntry: true);

        new VoidNullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void ReceiverSlotUsedAfterContinuation_StaysFlat()
    {
        var function = Build(receiverUsedAfter: true);

        new VoidNullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void DifferentNullPathTerminator_StaysFlat()
    {
        var function = Build(mismatchedNullReturn: true);

        new VoidNullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }
}
