using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class MetricSectionTests
{
    [Fact]
    public async Task
        LibraryImplementationProfiles_RendersPhysicalBodyRows()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.MemberMetrics],
                Markdown = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "## Member Metrics",
            result.Output);
        Assert.Contains("Analyze(int, int)", result.Output);
        Assert.Contains("AnalyzeAsync(int)", result.Output);
        Assert.Contains("| Evidence Method |", result.Output);
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_OrdersByBodySizeAndShowsOverloadEdges()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.TypeMetrics],
                IncludeAll = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "## Type Metrics",
            result.Output);
        Assert.Contains("| Incoming Overloads |", result.Output);
        Assert.Contains("| Overload Targets |", result.Output);
        Assert.Contains("| Async |", result.Output);
        Assert.True(
            result.Output.IndexOf(
                "Analyze(int, int)",
                StringComparison.Ordinal)
            < result.Output.IndexOf(
                "Analyze(int)",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task
        MemberImplementationProfiles_ShowsTheSelectedOverloadFamily()
    {
        var result = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(new MemberOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                MemberFilter =
                    ["Analyze"],
                IncludeAll = true,
                IncludeSections =
                    [SectionNames.MemberMetrics],
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Analyze(int)", result.Output);
        Assert.Contains("Analyze(int, int)", result.Output);
        Assert.Contains("Analyze(string)", result.Output);
        Assert.DoesNotContain("Other(int)", result.Output);
    }

    [Fact]
    public async Task TypeMetrics_DoesNotResolveInMemberCatalog()
    {
        var result = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(new MemberOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                MemberFilter = ["Analyze"],
                Select = [SectionNames.TypeMetrics],
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SectionNames.TypeMetrics, result.Error);
    }

    [Fact]
    public async Task MemberMetrics_DoesNotResolveInTypeCatalog()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                Select = [SectionNames.MemberMetrics],
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SectionNames.MemberMetrics, result.Error);
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_RemainsExplicitOnly()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeAll = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Detailed,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            "## Type Metrics",
            result.Output);
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_BroadDocumentJsonSkipsExactOnlySection()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                Select = ["*"],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            "Type Metrics",
            result.Error,
            StringComparison.Ordinal);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            JsonValueKind.Object,
            json.RootElement.ValueKind);
    }

    [Fact]
    public async Task
        ExactAccessorImplementationProfiles_RestrictsToSelectedBody()
    {
        var result = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(new MemberOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                MemberFilter = ["Value"],
                OverloadIndex = 1,
                IncludeAll = true,
                IncludeSections =
                    [SectionNames.MemberMetrics],
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("get_Value()", result.Output);
        Assert.DoesNotContain("set_Value(int)", result.Output);
    }

    [Fact]
    public void
        ExactAccessorImplementationProfiles_RestrictsDiagnosticsToSelectedBody()
    {
        var type = new ApiType
        {
            Namespace = "Fixture",
            Name = "AccessorSample",
        };
        var diagnostic = new AnalysisDiagnostic(
            2,
            "set_Value",
            "decode failed",
            DeclaringType: TypeRef.Definition(
                "Fixture",
                "Fixture",
                "AccessorSample"));
        var drillByToken =
            new Dictionary<
                int,
                (string? Stable, string Visibility, string Selector)>
            {
                [1] = default,
                [2] = default,
            };

        Assert.False(
            ApiOutputFormatter.IncludesImplementationProfileDiagnostic(
                diagnostic,
                type,
                drillByToken,
                restrictToModelMembers: true,
                selectedMethodToken: 1));
        Assert.True(
            ApiOutputFormatter.IncludesImplementationProfileDiagnostic(
                diagnostic,
                type,
                drillByToken,
                restrictToModelMembers: true,
                selectedMethodToken: 2));
    }

    [Fact]
    public async Task
        EventImplementationProfiles_IncludeBothAccessors()
    {
        var result = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(new MemberOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                MemberFilter = ["Changed"],
                IncludeAll = true,
                IncludeSections =
                    [SectionNames.MemberMetrics],
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("add_Changed(System.Action)", result.Output);
        Assert.Contains("remove_Changed(System.Action)", result.Output);
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_AttachOverloadTargetsToPhysicalBody()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.TypeMetrics],
                IncludeAll = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                MarkdownExplicitlySet = true,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        string[] rows = result.Output
            .Split('\n')
            .Where(line => line.Contains(
                "AnalyzeAsync(string)",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Single(
            rows,
            row => row.Contains(
                "AnalyzeAsync(int)",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_JsonlPreservesRawProfileAndExactEdges()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.TypeMetrics],
                IncludeAll = true,
                Jsonl = true,
                Tabular = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string[] lines = result.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        string profileLine = Assert.Single(
            lines,
            line =>
            {
                using JsonDocument candidate =
                    JsonDocument.Parse(line);
                return candidate.RootElement
                    .GetProperty("member")
                    .GetString() == "Analyze(int)";
            });
        using JsonDocument row = JsonDocument.Parse(profileLine);
        JsonElement root = row.RootElement;
        foreach (string property in new[]
        {
            "member_token",
            "evidence_token",
            "basic_blocks",
            "conditional_branches",
            "switches",
            "switch_targets",
            "normal_flow_complexity",
            "catch_regions",
            "filter_regions",
            "finally_regions",
            "fault_regions",
            "locals",
            "distinct_callees",
            "throws",
            "complete",
            "overload_relationships",
        })
        {
            Assert.True(
                root.TryGetProperty(property, out _),
                $"Missing JSONL property '{property}'.");
        }
        Assert.Equal(
            1 + int.Parse(root.GetProperty("conditional_branches").GetString()!)
                - int.Parse(root.GetProperty("switches").GetString()!)
                + int.Parse(root.GetProperty("switch_targets").GetString()!),
            int.Parse(
                root.GetProperty("normal_flow_complexity").GetString()!));

        string relationships =
            root.GetProperty("overload_relationships")
                .GetString()!;
        Assert.Contains("caller=0x", relationships);
        Assert.Contains(";callee=0x", relationships);
        Assert.Contains(";evidence=0x", relationships);
        Assert.Contains(";offset=IL_", relationships);
        Assert.Contains(";kind=Call", relationships);
    }

    [Fact]
    public async Task
        TypeImplementationProfiles_RejectsDocumentJson()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(new TypeOptions
            {
                TypeName =
                    "ILInspector.Analysis.ImplementationProfileFixtures."
                    + "ImplementationProfileSample",
                AssemblyPath =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.TypeMetrics],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Document --json cannot represent Type Metrics analysis.",
            result.Error);
    }

    [Fact]
    public async Task
        LibraryImplementationProfiles_RejectsDocumentJson()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.MemberMetrics],
                JsonOutput = true,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Document --json cannot represent Member Metrics analysis.",
            result.Error);
    }

    [Fact]
    public async Task
        LibraryImplementationProfiles_CountComposesWithJson()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName =
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                IncludeSections =
                    [SectionNames.MemberMetrics],
                Count = true,
                JsonOutput = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.True(
            int.TryParse(result.Output.Trim(), out int count));
        Assert.True(count > 0);
    }
}
