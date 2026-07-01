using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberExceptionRegionsSectionTests
{
    [Fact]
    public async Task ExceptionRegionsSection_RendersMethodExceptionRegions()
    {
        var result = await RunExceptionRegionsAsync(nameof(MemberExceptionRegionsFixture.TryCatch));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Exception Regions", result.Output);
        Assert.Contains("| Region | Clause | Try Range | Handler Range | Caught Type |", result.Output);
        Assert.Contains("| 1 | catch | IL_", result.Output);
        Assert.Contains("System.DivideByZeroException", result.Output);
        Assert.DoesNotContain("Context", result.Output);
    }

    [Fact]
    public async Task ExceptionRegionsSection_RendersFinallyRegion()
    {
        var result = await RunExceptionRegionsAsync(nameof(MemberExceptionRegionsFixture.TryFinally));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Exception Regions", result.Output);
        Assert.Contains("| 1 | finally | IL_", result.Output);
        Assert.DoesNotContain("Caught Type", result.Output);
    }

    [Fact]
    public async Task ExceptionRegionsSection_RendersEmptyState_WhenExplicitlySelected()
    {
        var result = await RunExceptionRegionsAsync(nameof(MemberExceptionRegionsFixture.NoRegions));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Exception Regions", result.Output);
        Assert.Contains("No exception regions found in this method body.", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsExceptionRegionsForSelectedMember()
    {
        var result = await RunExceptionRegionsAsync(nameof(MemberExceptionRegionsFixture.TryCatch), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Exception Regions\tsection", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunExceptionRegionsAsync(string memberName, bool discover = false)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberExceptionRegionsFixture).FullName,
            AssemblyPath = typeof(MemberExceptionRegionsFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.ExceptionRegions],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
            OneLine = discover,
            Tsv = discover,
            OneLineExplicitlySet = discover,
            FormatExplicitlySet = true,
        }));
}

public static class MemberExceptionRegionsFixture
{
    public static int TryCatch(int value)
    {
        try
        {
            return 100 / value;
        }
        catch (DivideByZeroException)
        {
            return -1;
        }
    }

    public static int TryFinally(int value)
    {
        try
        {
            return value + 1;
        }
        finally
        {
            GC.KeepAlive(value);
        }
    }

    public static int NoRegions(int value) => value + 1;
}
