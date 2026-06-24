using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallGraphSectionTests
{
    [Fact]
    public async Task CallGraphSection_RendersBoundedTree_WhenExplicitlySelected()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.RootCall));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
        // External callees (outside this assembly) are recorded as bounded leaves.
        Assert.Contains("(external)", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_RendersPerfCuesForFanoutDepthAndLoopingCalls()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.LoopHeavyCall));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("fanout", result.Output);
        Assert.Contains("depth", result.Output);
        Assert.Contains("loop", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsAllocationAndCopySignals()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.AllocCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Alloc", "Copy"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("alloc 1", result.Output);
        Assert.Contains("copy 1", result.Output);
        // Signals are opt-in: unrequested cues must not appear.
        Assert.DoesNotContain("fanout", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsExceptionSignals()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RiskyCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Throw", "Catch", "Finally", "Exceptions", "EvidenceIL"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("throw 1", result.Output);
        Assert.Contains("catch 1", result.Output);
        Assert.Contains("finally 1", result.Output);
        // The constructed exception type is a distinct field from the throw-site count (#1277).
        Assert.Contains("exceptions InvalidOperationException", result.Output);
        Assert.Contains("il IL_", result.Output);
        // Unrequested cost cues stay hidden.
        Assert.DoesNotContain("copy", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_UsesRequestedFieldsWhenRenderingNodeLabels()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.LoopHeavyCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Depth", "Loop"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("depth 4", result.Output);
        Assert.Contains("loop", result.Output);
        Assert.DoesNotContain("fanout", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_RendersEmptyStateNote_WhenNoOutboundCalls()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.NoCalls));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("No outbound calls found in this method body.", result.Output);
    }

    [Fact]
    public async Task CallerGraphSection_RendersBoundedReverseTree_WhenExplicitlySelected()
    {
        var result = await RunCallerGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Inner));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Caller Graph", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), result.Output);
        Assert.Contains("fanin", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_StaysSilent_WhenNotExplicitlySelected()
    {
        // Call Graph is opt-in (ExplicitOnly): a broad view must never auto-include it.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## Call Graph", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallGraphAsOptIn()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            OverloadIndex = 1,
            TipLevel = TipLevel.Quiet,
            Discover = [],
            Verbosity = Verbosity.Normal,
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Call Graph\tsection (opt-in)", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunCallGraphAsync(
        string typeName, string memberName)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

    static Task<(int ExitCode, string Output, string Error)> RunCallerGraphAsync(
        string typeName, string memberName)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.CallerGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));
}

public static class MemberCallGraphFixture
{
    public static void RootCall() => Mid();

    public static void Mid() => Inner();

    public static void Inner() => Console.WriteLine("leaf");

    public static void LoopHeavyCall()
    {
        for (int i = 0; i < 2; i++)
            RootCall();
    }

    public static void NoCalls()
    {
    }

    // new List<int> -> alloc; ToArray -> copy.
    public static int AllocCall(int[] data)
    {
        var list = new System.Collections.Generic.List<int>(data);
        return list.Count + System.Linq.Enumerable.ToArray(data).Length;
    }

    // throw + try/catch/finally -> throw/catch/finally signals.
    public static int RiskyCall(int x)
    {
        try
        {
            if (x < 0)
                throw new System.InvalidOperationException("negative");
            return 100 / x;
        }
        catch (System.DivideByZeroException)
        {
            return -1;
        }
        finally
        {
            System.GC.KeepAlive(x);
        }
    }
}
