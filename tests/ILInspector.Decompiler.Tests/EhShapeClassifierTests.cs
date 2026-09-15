using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class EhShapeClassifierTests
{
    [Fact]
    public void BackwardLeaveBehindOuterSharedMerge_ClassifiesOuterGuard()
    {
        var function = FunctionWithOuterSharedMergeAndBackwardLeave();

        Assert.Equal("prologue-epilogue-guard", EhShapeClassifier.Classify(function));
    }

    [Fact]
    public void BackwardLeaveWithoutOuterMerge_RemainsLeaveRetryLoop()
    {
        var function = FunctionWithBackwardLeaveOnly();

        Assert.Equal("leave-retry-loop", EhShapeClassifier.Classify(function));
    }

    static IrFunction FunctionWithOuterSharedMergeAndBackwardLeave()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");
        var owner = TypeRef.CoreLib("Synthetic", "Owner");
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryBody = new BlockContainer();
        var retry = new Block(0x0020);
        retry.Add(new Leave(0x0000));
        retryBody.Add(retry);
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(0x0030));

        var root = new BlockContainer();
        var head = new Block(0x0000);
        head.Add(new ConditionalBranch(new LoadArgument(0, "flag", boolType), 0x0020));
        var falseArm = new Block(0x0010);
        falseArm.Add(new Branch(0x0040));
        var tryHolder = new Block(0x0020);
        tryHolder.Add(new TryCatch(retryBody, [new CatchClause(exception, catchBody)]));
        var trueExit = new Block(0x0030);
        trueExit.Add(new Branch(0x0040));
        var merge = new Block(0x0040);
        merge.Add(new Return(null));
        foreach (var block in (Block[])[head, falseArm, tryHolder, trueExit, merge])
            root.Add(block);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [new Parameter("flag", boolType)], HasThis: false, GenericParameterCount: 0),
            [],
            root);
    }

    static IrFunction FunctionWithBackwardLeaveOnly()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var owner = TypeRef.CoreLib("Synthetic", "Owner");
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryBody = new BlockContainer();
        var retry = new Block(0x0010);
        retry.Add(new Leave(0x0010));
        retryBody.Add(retry);
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(0x0020));

        var root = new BlockContainer();
        var holder = new Block(0x0010);
        holder.Add(new TryCatch(retryBody, [new CatchClause(exception, catchBody)]));
        root.Add(holder);

        return new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            root);
    }
}
