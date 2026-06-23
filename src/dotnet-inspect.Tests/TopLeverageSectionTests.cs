using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class TopLeverageSectionTests
{
    [Fact]
    public async Task TypeTopLeverage_TsvHonorsRowLimit()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            IncludeSections = [SectionNames.TopLeverage],
            Tsv = true,
            Rows = 2,
            OneLine = true,
            OneLineExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Header row plus exactly two data rows.
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("member", lines[0]);
        Assert.Contains("SharedHelper()", result.Output);
    }

    [Fact]
    public async Task TypeTopLeverage_RanksMostCalledMemberFirst()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            IncludeSections = [SectionNames.TopLeverage],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Top Leverage", result.Output);
        Assert.Contains("| Member | Callers | Fanout | Depth | Loop Calls |", result.Output);
        // SharedHelper is called by EntryA/EntryB/EntryC -> three direct callers.
        Assert.Contains("`SharedHelper()` | 3 |", result.Output);
        // The most-leveraged member ranks ahead of its callers.
        Assert.True(
            result.Output.IndexOf("SharedHelper()", StringComparison.Ordinal)
                < result.Output.IndexOf("EntryA()", StringComparison.Ordinal),
            "expected SharedHelper to rank ahead of EntryA");
    }

    [Fact]
    public async Task TypeTopLeverage_StaysSilentWhenNotExplicitlySelected()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## Top Leverage", result.Output);
    }

    [Fact]
    public async Task TypeEffectiveDiscovery_ListsTopLeverageAsOptIn()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            Discover = [],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Top Leverage\tsection", result.Output);
    }
}

public static class LeverageSampleType
{
    public static void EntryA() => SharedHelper();

    public static void EntryB() => SharedHelper();

    public static void EntryC() => SharedHelper();

    public static void SharedHelper() => System.Console.WriteLine("shared");
}
