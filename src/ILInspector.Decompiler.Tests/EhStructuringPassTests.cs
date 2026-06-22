using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class EhStructuringPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static IrFunction LeaveRetryOutsideTry()
    {
        var body = new BlockContainer();

        var retry = new Block(0x0000);
        retry.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        body.Add(retry);

        var tryBlock = new Block(0x0010);
        tryBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        tryBlock.Add(new Leave(0x0000));
        body.Add(tryBlock);

        var finallyBlock = new Block(0x0020);
        finallyBlock.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        finallyBlock.Add(new EndFinally());
        body.Add(finallyBlock);

        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Finally,
                    TryOffset: 0x0010,
                    TryLength: 0x0010,
                    HandlerOffset: 0x0020,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    static IrFunction LeaveIntoSameTry()
    {
        var body = new BlockContainer();

        var tryEntry = new Block(0x0010);
        tryEntry.Add(new Leave(0x0018));
        body.Add(tryEntry);

        var tryTarget = new Block(0x0018);
        tryTarget.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        body.Add(tryTarget);

        var finallyBlock = new Block(0x0030);
        finallyBlock.Add(new EndFinally());
        body.Add(finallyBlock);

        var tail = new Block(0x0040);
        tail.Add(new Return(null));
        body.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    HandlerKind.Finally,
                    TryOffset: 0x0010,
                    TryLength: 0x0020,
                    HandlerOffset: 0x0030,
                    HandlerLength: 0x0010,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };
    }

    [Fact]
    public void LeaveRetryOutsideTry_ConsumesFinallyRegion()
    {
        var function = LeaveRetryOutsideTry();

        new EhStructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("try", output);
        Assert.Contains("finally", output);
    }

    [Fact]
    public void LeaveIntoSameTry_KeepsRegionFlat()
    {
        var function = LeaveIntoSameTry();

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }
}
