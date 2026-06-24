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
    public async Task TypeTopLeverage_IncludesVisibilityAndStableSelector()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            IncludeSections = [SectionNames.TopLeverage],
            IncludeAll = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Visibility |", result.Output);
        Assert.Contains("| Stable |", result.Output);
        // SharedHelper is a public method with a copyable stable selector.
        Assert.Matches(@"`SharedHelper\(\)`.*\| public \|.*`SharedHelper~[0-9a-f]{10}`", result.Output);
    }

    [Fact]
    public async Task TypeTopLeverage_StableSelectorRoundTripsToMemberCommand()
    {
        var ranked = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            IncludeSections = [SectionNames.TopLeverage],
            IncludeAll = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));
        var match = System.Text.RegularExpressions.Regex.Match(ranked.Output, @"([A-Za-z0-9_]+)~([0-9a-f]{10})");
        Assert.True(match.Success, "expected a Name~digest selector in Top Leverage output");

        // The emitted selector resolves through member selection (the digest round-trips,
        // and a digest + detail section no longer trips the auto-select conflict).
        var drilled = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            MemberFilter = [match.Groups[1].Value],
            MemberDigest = match.Groups[2].Value,
            IncludeAll = true,
            IncludeSections = [SectionNames.Callers],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, drilled.ExitCode);
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
    [Fact]
    public async Task MemberTopLeverage_ScopesRowsToSelectedMember()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            MemberFilter = [nameof(LeverageSampleType.SharedHelper)],
            OverloadIndex = 1,
            IncludeAll = true,
            IncludeSections = [SectionNames.TopLeverage],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        // Member scope is now selectable (#1264) and rows are restricted to the selected
        // member, not the whole declaring type.
        Assert.Contains("## Top Leverage", result.Output);
        Assert.Contains("SharedHelper", result.Output);
        Assert.DoesNotContain(nameof(LeverageSampleType.EntryA), result.Output);
    }

    [Fact]
    public void LibraryTopLeverage_PopulatesVisibilityStableAndNameNSelector()
    {
        var rows = DotnetInspector.Inspectors.LibraryMetadataService.ScanTopLeverage(
            typeof(LeverageSampleType).Assembly.Location, new DotnetInspector.Output.VerboseLogger(false));

        Assert.NotNull(rows);
        var helper = Assert.Single(rows!, r => r.Member.EndsWith("LeverageSampleType.SharedHelper()"));
        Assert.Equal("public", helper.Visibility);
        Assert.Matches(@"^SharedHelper~[0-9a-f]{10}$", helper.Stable);
        Assert.Equal("SharedHelper", helper.Selector); // single overload -> bare name

        // Overloaded methods carry Name:N selectors.
        var overloads = rows!.Where(r => r.Member.Contains("LeverageSampleType.Overloaded(")).ToList();
        Assert.Equal(2, overloads.Count);
        Assert.All(overloads, o => Assert.Matches(@"^Overloaded:[12]$", o.Selector));
    }

    [Fact]
    public async Task LibraryTopLeverage_StableSelectorRoundTripsToMemberCommand()
    {
        var rows = DotnetInspector.Inspectors.LibraryMetadataService.ScanTopLeverage(
            typeof(LeverageSampleType).Assembly.Location, new DotnetInspector.Output.VerboseLogger(false));
        var helper = rows!.First(r => r.Member.EndsWith("LeverageSampleType.SharedHelper()"));
        var match = System.Text.RegularExpressions.Regex.Match(helper.Stable!, @"([A-Za-z0-9_]+)~([0-9a-f]{10})");
        Assert.True(match.Success, "expected a Name~digest selector on the library-scope row");

        // The library-derived digest is the Member Index digest and resolves through member selection.
        var drilled = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(LeverageSampleType).FullName,
            AssemblyPath = typeof(LeverageSampleType).Assembly.Location,
            MemberFilter = [match.Groups[1].Value],
            MemberDigest = match.Groups[2].Value,
            IncludeAll = true,
            IncludeSections = [SectionNames.Callers],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, drilled.ExitCode);
    }
}

public static class LeverageSampleType
{
    public static void EntryA() => SharedHelper();

    public static void EntryB() => SharedHelper();

    public static void EntryC() => SharedHelper();

    public static void SharedHelper() => System.Console.WriteLine("shared");

    // Two public overloads -> Name:N selectors (Overloaded:1 / Overloaded:2).
    public static int Overloaded(int x) => x;

    public static int Overloaded(string s) => s.Length;
}
