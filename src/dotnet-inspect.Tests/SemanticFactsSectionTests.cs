using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using ILInspector.Analysis;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SemanticFactsSectionTests
{
    static readonly string TestAssemblyPath = typeof(SemanticFactsSectionTests).Assembly.Location;

    [Fact]
    public async Task MemberSemanticFacts_RenderObjectiveFacts()
    {
        var result = await RunMemberAsync(
            nameof(SemanticFactsFixture.AllSignals),
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Allocation Facts", result.Output);
        Assert.Contains("## Safety Facts", result.Output);
        Assert.Contains("## Cost Facts", result.Output);
        Assert.Contains("array", result.Output);
        Assert.Contains("stackalloc", result.Output);
        Assert.Contains("virtual dispatch", result.Output);
        Assert.DoesNotContain("Root Reach", result.Output);
        Assert.DoesNotContain("Fix Direction", result.Output);
    }

    [Fact]
    public async Task TypeSemanticFacts_RenderMemberIndexedFacts()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SemanticFactsFixture).FullName,
            AssemblyPath = TestAssemblyPath,
            IncludeSections = [SectionNames.AllocationFacts, SectionNames.SafetyFacts, SectionNames.CostFacts],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Allocation Facts", result.Output);
        Assert.Contains("| Member | IL Offset | Allocation Kind |", result.Output);
        Assert.Contains("`DotnetInspector.Tests.SemanticFactsFixture.AllSignals", result.Output);
        Assert.Contains("## Safety Facts", result.Output);
        Assert.Contains("## Cost Facts", result.Output);
    }

    [Fact]
    public async Task LibraryIlOffsetSemanticContexts_RenderPointFacts()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.AllSignals))!;
        var index = LibraryBodyIndex.Open(TestAssemblyPath);
        var allocationOffset = index.GetAllocationOccurrences()[method.MetadataToken][0].ILOffset;

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            ILOffsetParameter = $"0x{method.MetadataToken:X}+0x{allocationOffset:X}",
            IncludeSections = [SectionNames.AllocationContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Allocation Context", result.Output);
        Assert.Contains("| IL Offset | Allocation Kind |", result.Output);
        Assert.DoesNotContain("Performance Triage", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsSemanticFactSectionsAsOptIn()
    {
        var result = await RunMemberAsync(
            nameof(SemanticFactsFixture.AllSignals),
            discover: true,
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Allocation Facts | section", result.Output);
        Assert.Contains("| Safety Facts | section", result.Output);
        Assert.Contains("| Cost Facts | section", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        params string[] sections)
        => RunMemberAsync(member, discover: false, sections);

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        bool discover,
        params string[] sections)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SemanticFactsFixture).FullName,
            AssemblyPath = TestAssemblyPath,
            MemberFilter = [member],
            IncludeSections = new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase),
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));
}

public static class SemanticFactsFixture
{
    public static unsafe int AllSignals(IList<int> values)
    {
        var array = new int[values.Count];
        Span<int> scratch = stackalloc int[1];
        scratch[0] = values[0];
        return array.Length + scratch[0];
    }
}
