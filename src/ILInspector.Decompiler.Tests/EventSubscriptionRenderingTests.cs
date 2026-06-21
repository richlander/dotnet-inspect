using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An IL call to an event's compiler-generated add_/remove_ accessor cannot be
// spelled as a direct method call in C# — `e.add_X(h)` is CS0571 "cannot
// explicitly call operator or accessor". The PropertySugarPass must raise such
// calls to an EventSubscription, which the printer renders as `e += h` / `e -= h`.
public class EventSubscriptionRenderingTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void StaticEventAdd_RaisesToPlusEquals()
    {
        var function = Raised(nameof(CfgSampleClass.HookConsoleCancel));

        var subscription = Assert.Single(function.Descendants.OfType<EventSubscription>());
        Assert.True(subscription.IsAdd);
        Assert.Equal("CancelKeyPress", subscription.EventName);
        // The lowered add_ accessor call is gone.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "add_CancelKeyPress");

        var output = Print(nameof(CfgSampleClass.HookConsoleCancel));
        Assert.Contains("Console.CancelKeyPress += h;", output);
        Assert.DoesNotContain("add_CancelKeyPress", output);
    }

    [Fact]
    public void InstanceEventRemove_RaisesToMinusEquals()
    {
        var function = Raised(nameof(CfgSampleClass.UnhookProcessExit));

        var subscription = Assert.Single(function.Descendants.OfType<EventSubscription>());
        Assert.False(subscription.IsAdd);
        Assert.Equal("ProcessExit", subscription.EventName);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "remove_ProcessExit");

        var output = Print(nameof(CfgSampleClass.UnhookProcessExit));
        Assert.Contains("domain.ProcessExit -= h;", output);
        Assert.DoesNotContain("remove_ProcessExit", output);
    }
}
