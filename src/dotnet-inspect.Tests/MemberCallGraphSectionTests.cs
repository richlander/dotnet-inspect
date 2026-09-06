using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallGraphSectionTests
{
    [Fact]
    public async Task PreResolvedSection_OverridesStaleRawSelector()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            Select = [SectionNames.Methods],
            IncludeSections = [SectionNames.Signature],
            Count = true,
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task TypeSuppliedSections_DoNotSuppressRawSelectorValidation()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            Select = ["No Such Section"],
            IncludeSections = [SectionNames.Methods],
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Select value 'No Such Section' not found.", result.Error);
    }

    [Fact]
    public async Task PreResolvedBodyShapes_ValidatesBeforeAcquisition()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-body-shapes.dll"),
            Select = [SectionNames.BodyShapes],
            IncludeSections = [SectionNames.BodyShapes],
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Section '{SectionNames.BodyShapes}' requires --where",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Fact]
    public async Task PreResolvedBodyShapes_UsesAuthoritativeExactSectionProvenance()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-exact-body-shapes.dll"),
            Select = [SectionNames.Signature],
            IncludeSections = [SectionNames.BodyShapes, SectionNames.Signature],
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Section '{SectionNames.BodyShapes}' requires --where",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Fact]
    public async Task PreResolvedBodyShapes_CategoryExpansionRemainsNonExact()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-category-body-shapes.dll"),
            Select = [SelectResolver.AllSelector],
            IncludeSections = [SectionNames.BodyShapes, SectionNames.Signature],
            ExactIncludeSectionsOverride = [],
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("File not found", result.Error);
        Assert.DoesNotContain("requires --where", result.Error);
    }

    [Fact]
    public async Task PreResolvedBodyKindQuery_RejectsAuthoritativeNonBodyShapeSelection()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-body-kind.dll"),
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            Select = [SectionNames.Signature],
            IncludeSections = [SectionNames.Signature],
            BodyKindQuery = new BodyKindQueryOptions { Kind = "expression-bodied" },
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"--where Kind=... targets section '{SectionNames.BodyShapes}' or '{SectionNames.BodyShapeSummary}'.",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Fact]
    public async Task PreResolvedBroadSection_IgnoresStaleRawSelectorAfterAcquisition()
    {
        var stale = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                Select = [SectionNames.Methods, SectionNames.Properties],
                IncludeSections = [SectionNames.Methods],
                Count = true,
                TipLevel = TipLevel.Quiet,
            }));
        var control = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                IncludeSections = [SectionNames.Methods],
                Count = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(0, stale.ExitCode);
        Assert.Equal(control.Output, stale.Output);
        Assert.Empty(stale.Error);
    }

    [Fact]
    public async Task PreResolvedPerformanceTriageJson_IgnoresStaleRawSelector()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
                OverloadIndex = 1,
                Select = [SectionNames.Methods],
                IncludeSections = [SectionNames.PerformanceTriage],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Document --json cannot represent Performance Triage analysis.",
            result.Error);
    }

    [Fact]
    public async Task PreResolvedPerformanceTriageJson_NormalizesExactSectionComparer()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
                OverloadIndex = 1,
                IncludeSections = [SectionNames.PerformanceTriage],
                ExactIncludeSectionsOverride = ["performance triage"],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Document --json cannot represent Performance Triage analysis.",
            result.Error);
    }

    [Fact]
    public async Task EmptyPreResolvedSection_DoesNotResolveStaleRawSelector()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            Select = [SectionNames.Methods],
            IncludeSections = [],
            Count = true,
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(CountOutput.SectionRequiredMessage, result.Error);
    }

    [Fact]
    public async Task EmptyPreResolvedSection_ValidatesBeforeAcquisition()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "Missing.Type.Member",
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-member-selection.dll"),
            Select = [SectionNames.Methods],
            IncludeSections = [],
            Count = true,
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(CountOutput.SectionRequiredMessage, result.Error);
        Assert.DoesNotContain("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyPreResolvedSection_SelectsNoDefaultSections()
    {
        var options = new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            IncludeSections = [],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
        };

        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(options));
        var type = new ApiType
        {
            Name = "Fixture",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = nameof(MemberCallGraphFixture.RootCall),
                    Kind = "method",
                    MetadataToken = 0x06000001
                }
            ]
        };
        var resolvedOptions = options with { MemberSectionsPreResolved = true };

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## ", result.Output);
        Assert.Empty(ApiCommand.GetRequestedMemberSections(type, resolvedOptions));
        Assert.Empty(
            ApiOutputFormatter.BuildTypeWriterOptions(type, resolvedOptions).IncludeSections!);
        Assert.Empty(
            ApiMemberSectionPipelines.Create(resolvedOptions).GetDiscoverableSections(
                type,
                resolvedOptions.IncludeSections,
                explicitInclude: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task EmptyPreResolvedSection_SelectsNoDefaultTabularRows(
        bool tsv,
        bool jsonl)
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                IncludeSections = [],
                Tabular = true,
                Tsv = tsv,
                Jsonl = jsonl,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Detailed
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PreResolvedDetailSection_IgnoresStaleAllSelectorDuringAutoSelection()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            Select = [SelectResolver.AllSelector],
            IncludeSections = [SectionNames.Signature],
            Count = true,
            TipLevel = TipLevel.Quiet,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public void PreResolvedDetailSections_IgnoreStaleAllSelectorDuringFormatting()
    {
        var type = new ApiType
        {
            Name = "Fixture",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = nameof(MemberCallGraphFixture.RootCall),
                    Kind = "method",
                    MetadataToken = 0x06000001,
                    Signature = "public void RootCall()"
                }
            ]
        };
        var options = new MemberOptions
        {
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            OverloadIndex = 1,
            Select = [SelectResolver.AllSelector],
            IncludeSections = [SectionNames.Signature, SectionNames.CallGraph],
            MemberSectionsPreResolved = true,
        };

        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);

        Assert.Equal(
            [SectionNames.Summary, SectionNames.Signature, SectionNames.CallGraph],
            writerOptions.IncludeSections);
    }

    [Fact]
    public async Task CallGraphSection_RendersEdgeTableByDefault()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.RootCall));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("| From | From Group | To | To Group | Label |", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
        // External callees (outside this assembly) are recorded as bounded leaves.
        Assert.Contains("(external)", result.Output);
        Assert.DoesNotContain("├─", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_LowersToAnEdgeTable_UnderTabularOutput()
    {
        // The section is a graph, not a fixed rendering: Markdown and TSV both lower the same
        // model to edge rows, with syntax selected only at the formatter boundary.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("from\tfrom_group\tto\tto_group", result.Output);
        // Grouping carries the external boundary the tree spells in the label.
        Assert.Contains("\tExternal", result.Output);
        Assert.Contains($"{nameof(MemberCallGraphFixture.RootCall)}", result.Output);
        Assert.Contains($"{nameof(MemberCallGraphFixture.Mid)}", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_CountsEdgeRowsAcrossRenderModes()
    {
        var baseOptions = new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Count = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        };

        var edgeTable = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with
            {
                Count = false,
                Tabular = true,
                Tsv = true,
                TabularExplicitlySet = true,
                FormatExplicitlySet = true,
            }));
        var markdown = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(baseOptions));
        var table = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with
            {
                Tabular = true,
                TabularExplicitlySet = true,
                FormatExplicitlySet = true,
            }));
        var tree = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { Tree = true }));
        var mermaid = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { MermaidOutput = true }));

        Assert.Equal(0, edgeTable.ExitCode);
        Assert.Equal(0, markdown.ExitCode);
        Assert.Equal(0, table.ExitCode);
        Assert.Equal(0, tree.ExitCode);
        Assert.Equal(0, mermaid.ExitCode);
        Assert.Equal(markdown.Output, table.Output);
        Assert.Equal(markdown.Output, tree.Output);
        Assert.Equal(markdown.Output, mermaid.Output);
        var edgeRows = edgeTable.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Count();
        Assert.True(edgeRows > 0, "fixture must produce a non-empty graph");
        Assert.Equal(
            edgeRows,
            int.Parse(markdown.Output.Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CallGraphSection_MultiSectionCountMapKeepsEdgeCardinality()
    {
        var baseOptions = new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Count = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        };

        var scalar = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(baseOptions));
        var map = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with
            {
                IncludeSections = [SectionNames.CallGraph, SectionNames.Calls],
                JsonOutput = true,
                Format = OutputFormat.Json,
                FormatExplicitlySet = true,
            }));

        Assert.Equal(0, scalar.ExitCode);
        Assert.Equal(0, map.ExitCode);
        using var document = System.Text.Json.JsonDocument.Parse(map.Output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(
            int.Parse(scalar.Output.Trim(), System.Globalization.CultureInfo.InvariantCulture),
            counts[SectionNames.CallGraph]);
        Assert.Contains(SectionNames.Calls, counts.Keys);
    }

    [Fact]
    public async Task CallGraphSection_CountMapHonorsExcludingColumnProjection()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
                MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
                IncludeSections = [SectionNames.CallGraph, SectionNames.Calls],
                Columns = ["Callee"],
                Count = true,
                JsonOutput = true,
                Format = OutputFormat.Json,
                FormatExplicitlySet = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Normal,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = System.Text.Json.JsonDocument.Parse(result.Output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(0, counts[SectionNames.CallGraph]);
        Assert.True(counts[SectionNames.Calls] > 0);
    }

    [Fact]
    public async Task CallGraphSection_AbsoluteWindowSelectsTheSameEdgeAcrossLowerings()
    {
        var baseOptions = new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Rows = RowWindow.Range(2, 2),
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        };

        var markdown = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(baseOptions));
        var tree = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { Tree = true }));
        var mermaid = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { MermaidOutput = true }));
        var tsv = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with
            {
                Tabular = true,
                Tsv = true,
                TabularExplicitlySet = true,
                FormatExplicitlySet = true,
            }));
        var count = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { Count = true }));

        Assert.Equal(0, markdown.ExitCode);
        Assert.Equal(0, tree.ExitCode);
        Assert.Equal(0, mermaid.ExitCode);
        Assert.Equal(0, tsv.ExitCode);
        Assert.Equal(0, count.ExitCode);
        foreach (var output in new[] { markdown.Output, tree.Output, mermaid.Output, tsv.Output })
        {
            Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
            Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
            Assert.DoesNotContain(nameof(MemberCallGraphFixture.LoopHeavyCall), output);
        }
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public async Task CallGraphSection_StandaloneTreeWritesOnlyTheTree()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Tree = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("├─", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Call Graph", result.Output);
        Assert.DoesNotContain("| From |", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_StandaloneMermaidWritesOnlyTheDiagram()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            MermaidOutput = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("graph TD", result.Output, StringComparison.Ordinal);
        Assert.Contains("classDef markoutFocus", result.Output);
        Assert.DoesNotContain("```mermaid", result.Output);
        Assert.DoesNotContain("## Call Graph", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_EmbeddedMermaidComposesWithOtherMarkdownSections()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.Signature, SectionNames.CallGraph],
            EmbeddedMermaid = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Signature", result.Output);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("```mermaid", result.Output);
        Assert.Contains("graph TD", result.Output);
        Assert.DoesNotContain("| From | From Group |", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_PrettyTableTsvAndJsonlUseTheSameRows()
    {
        var baseOptions = new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Tabular = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        };

        var table = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(baseOptions));
        var tsv = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { Tsv = true }));
        var jsonl = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            baseOptions with { Jsonl = true }));

        Assert.Equal(0, table.ExitCode);
        Assert.Equal(0, tsv.ExitCode);
        Assert.Equal(0, jsonl.ExitCode);
        foreach (var output in new[] { table.Output, tsv.Output, jsonl.Output })
        {
            Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
            Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
            Assert.Contains(nameof(MemberCallGraphFixture.Inner), output);
        }
        Assert.Contains("From", table.Output);
        Assert.StartsWith("from\t", tsv.Output, StringComparison.Ordinal);
        Assert.StartsWith("{\"from\":", jsonl.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallGraphSection_TreeRejectsAnotherOutputFormat()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "Missing.Type",
            AssemblyPath = "missing.dll",
            IncludeSections = [SectionNames.CallGraph],
            Tree = true,
            FormatExplicitlySet = true,
            FormatFlagExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--tree is a standalone output format", result.Error);
    }

    [Fact]
    public async Task CallGraphSection_StandaloneMermaidRequiresOnlyCallGraph()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = "Missing.Type",
            AssemblyPath = "missing.dll",
            IncludeSections = [SectionNames.Signature, SectionNames.CallGraph],
            MermaidOutput = true,
            FormatFlagExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--mermaid requires exactly one selected graph", result.Error);
    }

    [Fact]
    public async Task CallGraphSection_EmptyMarkdownWindowRendersTableHeadersWithoutFocusRow()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Rows = RowWindow.Range(100, 100),
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        var section = result.Output[result.Output.IndexOf("## Call Graph", StringComparison.Ordinal)..];
        Assert.Contains("| From | From Group | To | To Group | Label |", section);
        Assert.DoesNotContain("No inbound callers or outbound calls found for this method.", section);
        Assert.DoesNotContain(nameof(MemberCallGraphFixture.RootCall), section);
    }

    [Fact]
    public async Task CallGraphSection_EmptyTreeWindowRendersEmptyStateWithoutFocusRow()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            Rows = RowWindow.Range(100, 100),
            Tree = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No inbound callers or outbound calls found for this method.", result.Output);
        Assert.DoesNotContain(nameof(MemberCallGraphFixture.RootCall), result.Output);
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
    public async Task CallGraphSection_DoesNotDefaultCuesForAnotherSectionsField()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.LoopHeavyCall)],
            IncludeSections = [SectionNames.CallGraph, SectionNames.Facts],
            Fields = ["Category"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("## Facts", result.Output);
        Assert.DoesNotContain("fanout", result.Output);
        Assert.DoesNotContain("fanin", result.Output);
        Assert.DoesNotContain("depth", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ColumnsDoNotProjectGraphFields()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.AllocCall)],
            IncludeSections =
                [SectionNames.CallGraph, SectionNames.AllocationFacts],
            Columns = ["Allocation Kind"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains("## Allocation Facts", result.Output);
        Assert.DoesNotContain("## Call Graph", result.Output);
        Assert.DoesNotContain("alloc 1", result.Output);
        Assert.DoesNotContain("fanout", result.Output);
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
    public async Task CallGraphSection_EvidenceILRetainsAllocationOffsetsWithoutAllocField()
    {
        var result = await ConsoleCapture.RunAsync(() =>
            MemberCommand.ExecuteAsync(new MemberOptions
            {
                TypeName = typeof(MemberCallGraphFixture).FullName!,
                AssemblyPath =
                    typeof(MemberCallGraphFixture).Assembly.Location,
                MemberFilter =
                    [nameof(MemberCallGraphFixture.AllocCall)],
                IncludeSections = [SectionNames.CallGraph],
                Fields = ["EvidenceIL"],
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Normal,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("il IL_0001,IL_000C", result.Output);
        Assert.DoesNotContain("alloc ", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsAsyncAlternativeOpportunities()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.CallsSyncSiblingFromAsync)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["AsyncAlternatives"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("async alternatives 1", result.Output);
        Assert.DoesNotContain("fanout", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_DoesNotProjectAsyncAlternativesByDefault()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!,
            nameof(MemberCallGraphFixture.CallsSyncSiblingFromAsync));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("async alternatives", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsAsyncAlternativesInJsonl()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.CallsSyncSiblingFromAsync)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Async"],
            Tabular = true,
            Jsonl = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("async alternatives 1", result.Output);
        Assert.StartsWith("{\"from\":", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesAsyncAlternativeFieldWildcard()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.CallsSyncSiblingFromAsync)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["AsyncA*"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("async alternatives 1", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesAllWildcardSignalFields()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.WildcardSignals)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["A*"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("alloc 1", result.Output);
        Assert.Contains("async alternatives 1", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsAsyncAlternativesAcrossAssemblies()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter =
                [nameof(MemberCallGraphFixture.CrossAssemblyAsyncAlternative)],
            CallerScopeAssemblies =
                [typeof(DiffCommand).Assembly.Location],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["AsyncAlternatives"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("ExecuteAsync", result.Output);
        Assert.Contains("async alternatives 1", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_OmitsSuppressedGeneratedAsyncAlternatives()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.CreateAsyncCallback)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["AsyncAlternatives"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("async alternatives", result.Output);
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
    public async Task CallGraphSection_RendersEmptyStateNote_WhenNoCallersOrCallees()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.NoCalls));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("No inbound callers or outbound calls found for this method.", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_IncludesInboundCallers_NotJustOutboundCallees()
    {
        // The section is bidirectional: one graph centred on the selected member carries both
        // halves, so a member with no callees still shows who reaches it.
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Inner));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
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
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Call Graph\tsection", result.Output);
        Assert.DoesNotContain("(opt-in)", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesPropertyGetterAccessor()
    {
        // A property has no body of its own; the default accessor ordinal addresses the
        // getter, and the graph roots at the getter's metadata name (#3265).
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("get_Descriptor", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesPropertySetterAccessorByOrdinal()
    {
        // Accessor ordinal 2 addresses the setter: its callee, distinct from the getter's.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.Descriptor)],
            OverloadIndex = 2,
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("set_Descriptor", result.Output);
        Assert.Contains("Consume", result.Output);
        Assert.DoesNotContain("Describe", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesEventAdderAccessor()
    {
        // An event target resolves to its adder accessor, whose field-like body combines
        // delegates (#3265).
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Triggered));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("add_Triggered", result.Output);
        Assert.Contains("Combine", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PropertyGetterRendersAccessorDeclaration()
    {
        // The getter renders a real method header (not the property's bare return type)
        // with the setter's body kept off it (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("get_Descriptor(", result.Output);
        Assert.DoesNotContain("set_Descriptor(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PropertySetterRendersVoidAccessorDeclaration()
    {
        // Accessor ordinal 2 renders the setter: void return, a trailing `value` parameter.
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("void set_Descriptor(", result.Output);
        Assert.Contains("value", result.Output);
        Assert.DoesNotContain("get_Descriptor(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_EventAdderRendersVoidAccessorDeclaration()
    {
        // The adder renders as a real void method taking the delegate value, not the
        // event's bare delegate type as a headless declaration (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Triggered), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("void add_Triggered(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_VirtualPropertyGetterKeepsVirtualModifier()
    {
        // The accessor shares the property's slot, so a virtual getter renders `virtual`
        // rather than the owner's bare accessibility (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberAccessorModifierFixture).FullName!,
            nameof(MemberAccessorModifierFixture.Label), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("public virtual string get_Label()", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_OverridePropertyGetterKeepsOverrideModifier()
    {
        var result = await RunDecompiledAsync(
            typeof(DerivedAccessorModifierFixture).FullName!,
            nameof(DerivedAccessorModifierFixture.Label), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("public override string get_Label()", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PrivateSetterKeepsAccessorAccessibility()
    {
        // A `private set` must render `private`, not the property's public accessibility (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberAccessorModifierFixture).FullName!,
            nameof(MemberAccessorModifierFixture.State), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("private void set_State(bool value)", result.Output);
        Assert.DoesNotContain("public void set_State(", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunDecompiledAsync(
        string typeName, string memberName, int? overloadIndex)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [memberName],
            OverloadIndex = overloadIndex,
            IncludeSections = [SectionNames.DecompiledSource],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

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
}

public static class MemberCallGraphFixture
{
    public static void RootCall() => Mid();

    public static void Mid() => Inner();

    public static void Inner() => Console.WriteLine("leaf");

    // Property whose accessors have distinct, non-trivial bodies so accessor addressing
    // (Descriptor:1 = getter, Descriptor:2 = setter) resolves to different call trees (#3265).
    public static string Descriptor
    {
        get => Describe();
        set => Consume(value);
    }

    static string Describe()
    {
        Inner();
        return "descriptor";
    }

    static void Consume(string value) => Console.WriteLine(value);

    // Field-like event: the compiler generates add/remove accessor bodies whose call graph
    // an event target resolves to via its adder/remover accessor (#3265).
    public static event Action? Triggered;

    public static void Raise() => Triggered?.Invoke();

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

    public static int ReadValue(int value) => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }

    public static Task<int> WildcardSignals(int[] data)
    {
        _ = AllocCall(data);
        return CallsSyncSiblingFromAsync(data.Length);
    }

    public static Task<int> CrossAssemblyAsyncAlternative() =>
        DiffCommand.ExecuteAsync(new DiffOptions());

    public static Func<int, Task<int>> CreateAsyncCallback() =>
        async value =>
        {
            await Task.Yield();
            return ReadValue(value);
        };
}

// Instance fixtures whose accessors carry non-default modifiers, so the synthesized
// accessor declarations must reflect virtual/override (from the owning slot) and the
// per-accessor accessibility of a `private set` rather than the owner's aggregate (#3265).
public class MemberAccessorModifierFixture
{
    public virtual string Label { get; } = "base";

    public bool State { get; private set; }
}

public class DerivedAccessorModifierFixture : MemberAccessorModifierFixture
{
    public override string Label => "derived";
}
