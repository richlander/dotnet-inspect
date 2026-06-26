using DotnetInspector.Models;
using System.Text.Json;
using DotnetInspector.Views;
using DotnetInspector;
using DotnetInspector.Commands;
using ILInspector.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Tests;

public class OutputFormatterTests
{
    [Fact]
    public void WriteTable_WithoutRowLimit_MatchesRenderThenWrite()
    {
        // The uncapped path serializes straight to the writer instead of materializing the
        // whole table as a string (#1205); output must be identical to render-then-write.
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("Name\tValue\nA\t1\nB\t2\n");

        var direct = new StringWriter();
        OutputFormatter.WriteTable(direct, showHeader: true, serialize, maxRows: null);

        Assert.Equal(OutputFormatter.RenderTable(showHeader: true, serialize), direct.ToString());
    }

    [Fact]
    public void WriteTable_WithRowLimit_StillTrimsRows()
    {
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("Name\tValue\nA\t1\nB\t2\n");

        var capped = new StringWriter();
        OutputFormatter.WriteTable(capped, showHeader: true, serialize, maxRows: 1);

        Assert.Equal(
            OutputFormatter.LimitRenderedTableRows(OutputFormatter.RenderTable(true, serialize), 1, hasHeader: true),
            capped.ToString());
    }

    [Fact]
    public void WriteTable_ToLineLimitingWriter_PreservesBufferedSemantics()
    {
        // The line-limiting writer counts newlines per write call, so WriteTable must keep the
        // buffered render-then-write path for it (output identical to writing the rendered
        // string), even without a row cap.
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("L1\nL2\nL3\nL4\n");

        var directInner = new StringWriter();
        OutputFormatter.WriteTable(new LineLimitingTextWriter(directInner, maxLines: 2), showHeader: true, serialize, maxRows: null);

        var bufferedInner = new StringWriter();
        var bufferedWriter = new LineLimitingTextWriter(bufferedInner, maxLines: 2);
        bufferedWriter.Write(OutputFormatter.RenderTable(showHeader: true, serialize));

        Assert.Equal(bufferedInner.ToString(), directInner.ToString());
    }

    [Fact]
    public void BuildMemberDrillMap_SuppressesAmbiguousStableForOverloadedIndexers()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "property", Name = "Item", Signature = "int this[int index]", GetterToken = 1001 },
                new ApiMember { Kind = "property", Name = "Item", Signature = "int this[string key]", GetterToken = 1002 },
                new ApiMember { Kind = "property", Name = "Count", Signature = "int Count", GetterToken = 1003 },
            ]
        };

        var map = ApiOutputFormatter.BuildMemberDrillMap(type);

        // Overloaded indexers share the parameter-free property canonical signature, so the
        // ambiguous Stable digest is suppressed while the Name:N selector disambiguates.
        Assert.True(map.TryGetValue(1001, out var first));
        Assert.Null(first.Stable);
        Assert.Matches(@"^Item:[12]$", first.Selector);
        Assert.True(map.TryGetValue(1002, out var second));
        Assert.Null(second.Stable);

        // A uniquely-named property keeps its round-tripping Stable selector.
        Assert.True(map.TryGetValue(1003, out var count));
        Assert.Matches(@"^Count~[0-9a-f]{10}$", count.Stable);
        Assert.Equal("Count", count.Selector);
    }

    [Fact]
    public void PopulateOptimizationOpportunities_RendersRowsForMatchingType()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            typeof(OutputFormatterTests).Assembly.Location,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage });

        var rows = Assert.IsType<List<OptimizationOpportunityRow>>(view.OptimizationOpportunityRows);
        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => row.Shape == "small-array");
    }

    [Fact]
    public void RenderTypeSectionsMarkdown_PopulatesOptimizationOpportunitiesWhenRequested()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity)
                }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains("small-array", markdown);
    }

    [Fact]
    public void RenderTypeSectionsMarkdown_ScopesOptimizationOpportunitiesToSelectedMember()
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(CreateSmallArrayOpportunity),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity),
                    MetadataToken = method.MetadataToken
                }
            ]
        };
        // OverloadIndex selects the single-member detail pipeline, which restricts rows
        // to the selected member instead of the whole declaring type.
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            OverloadIndex = 1,
            MemberFilter = [nameof(CreateSmallArrayOpportunity)],
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains(nameof(CreateSmallArrayOpportunity), markdown);
        Assert.DoesNotContain(nameof(CreateTemporaryArray), markdown);
    }

    private static int[] CreateSmallArrayOpportunity()
    {
        var value = 3;
        return new int[value];
    }

    private static void CreateTemporaryArray()
    {
        byte[] bytes = new byte[4];
        _ = bytes.Length;
    }

    // The local function compiles to a compiler-generated method (<...>g__Make|...)
    // declared on this type, carrying the small-array opportunity from `new int[3]`.
    private static int[] HasGeneratedLocalFunctionOpportunity()
    {
        return Make();
        static int[] Make() => new int[3];
    }

    [Fact]
    public void RenderOptimizationOpportunities_SuppressesGeneratedMethods()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = nameof(HasGeneratedLocalFunctionOpportunity) }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        // Generated implementation details are not actionable source fixes and are not
        // selectable at member scope (the API surface omits them), so they are suppressed
        // unconditionally — including under --all — to keep the contract consistent (#1267).
        var defaultMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);
        var allMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options with { IncludeAll = true });

        Assert.DoesNotContain("g__Make", defaultMarkdown);
        Assert.DoesNotContain("g__Make", allMarkdown);
    }

    [Fact]
    public void ScanOptimizationOpportunities_OrdersByTriagePriority()
    {
        var rows = LibraryMetadataService.ScanOptimizationOpportunities(
            typeof(OutputFormatterTests).Assembly.Location, new VerboseLogger(false));

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        // Root Reach is the leverage join: at least one opportunity sits in a reached method.
        Assert.Contains(rows, r => r.RootReach > 0);

        // Triage priority: in-loop (hot) rows first, then by confidence, then by descending
        // Root Reach. Verify the composite key is monotonically non-increasing.
        static (int loop, int conf, int reach) Key(OptimizationOpportunitySummary r) =>
            (r.Loop == "loop" ? 1 : 0,
             r.Confidence == "high" ? 2 : r.Confidence == "medium" ? 1 : 0,
             r.RootReach);
        for (int i = 1; i < rows.Count; i++)
            Assert.True(Compare(Key(rows[i - 1]), Key(rows[i])) >= 0,
                $"rows not ordered by triage priority at index {i}");

        static int Compare((int loop, int conf, int reach) a, (int loop, int conf, int reach) b)
        {
            if (a.loop != b.loop) return a.loop - b.loop;
            if (a.conf != b.conf) return a.conf - b.conf;
            return a.reach - b.reach;
        }
    }

    [Fact]
    public void ScanOptimizationOpportunities_SuppressesGeneratedMethods()
    {
        var rows = LibraryMetadataService.ScanOptimizationOpportunities(
            typeof(OutputFormatterTests).Assembly.Location, new VerboseLogger(false));

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, r =>
            r.Member.Contains("<>c")
            || r.Member.Contains(">g__")
            || r.Member.Contains(">b__")
            || r.Member.Contains(">d__")
            || r.Member.Contains("c__Display")
            || r.Member.Contains("<PrivateImplementationDetails>"));
    }

    [Fact]
    public void BuildShapeView_GroupsMethodOverloadsByLogicalName()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(string value)" },
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(ReadOnlySpan<char> value)" },
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (2 logical, 3 overloads)", methods.Text);
        Assert.NotNull(methods.Children);
        Assert.Equal(["string Format()", "Parse (2 overloads)"], methods.Children.Select(c => c.Text));
    }

    [Fact]
    public void BuildShapeView_KeepsSingleOverloadSignature()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (1)", methods.Text);
        var child = Assert.Single(methods.Children!);
        Assert.Equal("string Format()", child.Text);
    }

    [Fact]
    public void GetMemberSignatureSortKey_StripsMethodGenericListOnly()
    {
        var member = new ApiMember
        {
            Kind = "method",
            Name = "Task",
            Signature = "System.Threading.Tasks.Task<T> Task<T>(T value)"
        };

        Assert.Equal(
            "System.Threading.Tasks.Task<T> Task(T value)",
            ApiOutputFormatter.GetMemberSignatureSortKey(member));
    }

    [Fact]
    public void PopulateMemberSignature_EscapesKeywordMethodAndQualifiedTypeNames()
    {
        var keywordMethodType = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "return", Signature = "int return(int value)" },
            ]
        };
        var keywordTypeReturn = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "CreateKeywordType", Signature = "Probe.class CreateKeywordType()" },
            ]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Normal };
        var methodView = new TypeView();
        var returnTypeView = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(methodView, keywordMethodType, options);
        ApiOutputFormatter.PopulateMemberSignature(returnTypeView, keywordTypeReturn, options);

        var methodSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(methodView.SignatureRows));
        var returnTypeSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(returnTypeView.SignatureRows));
        Assert.Equal("<code>public int @return(int value)</code>", methodSignature.Signature);
        Assert.Equal("<code>public Probe.@class CreateKeywordType()</code>", returnTypeSignature.Signature);
    }

    [Fact]
    public void TypeViewSchema_DoesNotOwnFirstClassMemberRows()
    {
        var schema = ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema();

        Assert.Null(schema.GetSection("Method Groups"));
        Assert.Null(schema.GetSection("Methods"));
        Assert.Null(schema.GetSection("Operators"));
        Assert.Null(schema.GetSection("Explicit Interface Implementations"));
        Assert.Null(schema.GetSection("Extension Methods"));
        Assert.Null(schema.GetSection("Events"));
    }

    [Fact]
    public void TypeDocumentSchema_MergesFirstClassMemberViews()
    {
        var schema = ApiCommand.GetTypeDocumentSchema(new MemberOptions());

        Assert.NotNull(schema.GetSection("Method Groups"));
        Assert.NotNull(schema.GetSection("Methods"));
        Assert.NotNull(schema.GetSection("Operators"));
        Assert.NotNull(schema.GetSection("Explicit Interface Implementations"));
        Assert.NotNull(schema.GetSection("Extension Methods"));
        Assert.NotNull(schema.GetSection("Events"));
    }

    [Fact]
    public async Task DiscoverOutput_Tsv_RendersHeaderedTsvRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type", "Sim");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, tsv: true)));

        Assert.Equal(0, exit);
        Assert.Equal(
            "name\tkind\nPattern\tcolumn\nType\tcolumn\nSim\tcolumn\n",
            output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task DiscoverOutput_Jsonl_RendersJsonLineRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, jsonl: true)));

        Assert.Equal(0, exit);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("Pattern", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("column", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task DiscoverOutput_Json_RendersJsonRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, json: true)));

        Assert.Equal(0, exit);
        Assert.Contains("\"name\":\"Pattern\"", output);
        Assert.Contains("\"kind\":\"column\"", output);
    }

    [Fact]
    public void CountMarkdownTableRows_CountsDataRowsOnly()
    {
        const string markdown = """
        # Title

        ## Methods

        | Name | Signature |
        | ---- | --------- |
        | Read | void Read() |
        | Write | void Write() |

        ## Notes

        Not a table.
        """;

        Assert.Equal(2, CountOutput.CountMarkdownTableRows(markdown));
    }

    [Fact]
    public void CountMarkdownTableRows_SumsMultipleTables()
    {
        const string markdown = """
        | Field | Value |
        | ----- | ----- |
        | Name | Example |

        | Name |
        | ---- |
        | One |
        | Two |
        """;

        Assert.Equal(3, CountOutput.CountMarkdownTableRows(markdown));
    }

    [Fact]
    public void CountMarkdownTableRows_IgnoresCodeFences()
    {
        const string markdown = """
        ```md
        | Not | Data |
        | --- | ---- |
        | One | Two |
        ```

        | Name |
        | ---- |
        | Real |
        """;

        Assert.Equal(1, CountOutput.CountMarkdownTableRows(markdown));
    }

    [Fact]
    public void AssertMarkdownTablesHaveUniformColumnCounts_CatchesMalformedRows()
    {
        const string markdown = """
        | Name | Value |
        | ---- | ----- |
        | A | 1 |
        | B |
        """;

        Assert.Throws<InvalidOperationException>(() => AssertMarkdownTablesHaveUniformColumnCounts(markdown));
    }

    [Fact]
    public void RepresentativeMarkdownTables_HaveUniformColumnCounts()
    {
        var packageResult = new InspectionResult
        {
            PackageName = "Test.Package",
            Version = "1.0.0",
            LibraryFiles = ["lib/net10.0/Test.Package.dll"],
            SignatureResult = new SignatureVerificationResult
            {
                AuthorVerified = true,
                Publisher = "Example Publisher",
                Repository = "nuget.org",
                RepositoryVerified = true,
                StatusMessage = "Valid"
            },
            AuditSignals =
            [
                new AuditSignal("Package", "README", "Yes", "nuspec")
            ]
        };
        var packageOptions = new InspectionOptions
        {
            IncludeSections =
            [
                PackageSections.PackageInfo,
                PackageSections.LibraryFiles,
                PackageSections.Signature,
                PackageSections.Signals
            ]
        };
        var packageOutput = OutputFormatter.FormatResult(
            packageResult, packageOptions, PackageSectionDescriptors.CreatePipeline());

        var libraryInspection = CreateTestAudit("Test.dll", "net9.0");
        libraryInspection.Integrations =
        [
            new IntegrationSummary("OpenTelemetry", 2)
        ];
        libraryInspection.OpenTelemetry =
        [
            new IntegrationSignal("Tracing", "System.Diagnostics.ActivitySource"),
            new IntegrationSignal("Metrics", "System.Diagnostics.Metrics.UpDownCounter<T>")
        ];
        libraryInspection.SourceIntegrityChecked = true;
        libraryInspection.SourceIntegrityMismatched = 1;
        libraryInspection.SourceIntegrityMismatches = ["/_/src/A.cs"];
        var libraryOptions = new LibraryOptions
        {
            Verbosity = Verbosity.Normal,
            IncludeSections =
            [
                "Library Info",
                "Integrations",
                "OpenTelemetry",
                "SourceLink Integrity"
            ]
        };
        var libraryOutput = SerializeWithInclude(
            libraryInspection,
            LibrarySections.CreatePipeline().ComputeIncludeSections(
                libraryInspection, libraryOptions.Verbosity, libraryOptions.IncludeSections));

        AssertMarkdownTablesHaveUniformColumnCounts(packageOutput);
        AssertMarkdownTablesHaveUniformColumnCounts(libraryOutput);
    }

    [Fact]
    public void MarkdownSectionOrderer_ReordersH2SectionsAndKeepsFenceHeadings()
    {
        const string markdown = """
        # Title

        intro

        ## Zebra

        ```md
        ## Not a section
        ```

        ## Alpha

        A

        ## Beta

        B
        """;

        var output = MarkdownSectionOrderer.Apply(markdown, ["Beta", "Alpha", "Zebra"]);

        Assert.True(output.IndexOf("## Beta", StringComparison.Ordinal) < output.IndexOf("## Alpha", StringComparison.Ordinal));
        Assert.True(output.IndexOf("## Alpha", StringComparison.Ordinal) < output.IndexOf("## Zebra", StringComparison.Ordinal));
        Assert.Contains("## Not a section", output);
        Assert.Contains("B\n\n## Alpha", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvKeepsHeaderAndLimitsDataRows()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, 2, hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\nA\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvWithoutHeaderLimitsFromFirstLine()
    {
        var tsv = "A\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, 2, hasHeader: false).ReplaceLineEndings("\n");

        Assert.Equal("A\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlHasNoHeaderLineEvenWhenHeaderRequested()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        // hasHeader is true (callers pass !--no-header) but jsonl rows are self-describing.
        var output = OutputFormatter.LimitRenderedTableRows(jsonl, 2, hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("{\"name\":\"A\"}\n{\"name\":\"B\"}\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_MarkdownDelegatesToMarkdownLimiter()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n| C |\n";

        var output = OutputFormatter.LimitRenderedTableRows(markdown, 2, hasHeader: true).ReplaceLineEndings("\n");

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
    }

    [Fact]
    public void LimitRenderedTableRows_NullLimitIsUnchanged()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\n";

        Assert.Equal(tsv, OutputFormatter.LimitRenderedTableRows(tsv, null, hasHeader: true));
    }

    [Fact]
    public void LimitMarkdownTableRows_LimitsEachTable()
    {
        const string markdown = """
        # Title

        ## First

        | Name |
        | ---- |
        | A |
        | B |
        | C |

        ## Second

        | Value |
        | ----- |
        | 1 |
        | 2 |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, 2);

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
        Assert.Contains("| 1 |", output);
        Assert.Contains("| 2 |", output);
    }

    [Fact]
    public void LimitMarkdownTableRows_IgnoresCodeFences()
    {
        const string markdown = """
        ```md
        | Name |
        | ---- |
        | A |
        | B |
        ```

        | Name |
        | ---- |
        | A |
        | B |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, 1);

        Assert.Contains("| B |\n```", output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("| B |\n", output.ReplaceLineEndings("\n").Split("```")[2]);
    }

    [Fact]
    public void MultiAssemblyReport_HasSingleH1()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Single(output.Split('\n'), l => l.StartsWith("# "));
    }

    [Fact]
    public void SingleAssemblyAudit_HasSingleH1()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var output = Serialize(inspection);

        Assert.Single(output.Split('\n'), l => l.StartsWith("# "));
    }

    [Fact]
    public void MultiAssemblyReport_HasH2AssembliesSection()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("## Libraries", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH3PerTfm()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("### Test.dll (net9.0)", output);
        Assert.Contains("### Test.dll (net8.0)", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasH4SectionsPerItem()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.Contains("#### Library Info", output);
    }

    [Fact]
    public void MultiAssemblyReport_HasCompactLine()
    {
        var report = CreateTestReport("Test.dll", true, "net9.0", "net8.0");
        var output = Serialize(report);

        // AutoFieldsCount = 7 renders the first 7 scalar properties as a compact hero line
        Assert.Contains("Name: Test", output);
    }

    [Fact]
    public void MultiAssemblyReport_TitleFromPackageName()
    {
        var report = CreateTestReport("Test.dll", false, "net9.0", "net8.0");
        var output = Serialize(report);

        Assert.StartsWith("# Test", output.TrimStart());
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtNormalVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Normal);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_CountsIntegrationCategories()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasDependencyInjectionSupport = true;
        inspection.HasLoggingSupport = true;
        inspection.HasOpenTelemetrySupport = true;
        inspection.OpenTelemetry =
        [
            new IntegrationSignal("Tracing", "System.Diagnostics.ActivitySource"),
            new IntegrationSignal("Metrics", "System.Diagnostics.Metrics.Meter")
        ];

        var output = Serialize(inspection);

        Assert.Contains("| Integrations | 3 |", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_UsesExactIntegrationAndSwitchCounts()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.IntegrationCount = 1;
        inspection.SwitchCount = 5;

        var output = Serialize(inspection);

        Assert.Contains("| Integrations | 1 |", output);
        Assert.Contains("| Switches | 5 |", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_FieldsAreAlphabetical()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.AssemblyInfo!.InformationalVersion = "1.0.0+abc";
        inspection.AssemblyInfo.MethodDefinitionCount = 42;
        inspection.HasOpenTelemetrySupport = true;

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| Architecture |", StringComparison.Ordinal)
            < output.IndexOf("| Assembly Version |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Informational Version |", StringComparison.Ordinal)
            < output.IndexOf("| Integrations |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Integrations |", StringComparison.Ordinal)
            < output.IndexOf("| Methods |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Source |", StringComparison.Ordinal)
            < output.IndexOf("| Switches |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Switches |", StringComparison.Ordinal)
            < output.IndexOf("| Target Framework |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Target Framework |", StringComparison.Ordinal)
            < output.IndexOf("| Type Forwarders |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtDetailedVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Detailed);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_MetadataIncludesDeterministic()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.IsDeterministic = true;
        inspection.HasReproducibleFlag = true;
        var output = Serialize(inspection);

        Assert.Contains("Deterministic", output);
        Assert.Contains("Reproducible", output);
    }

    [Fact]
    public void SingleAudit_CustomAttributes_AreSortedByName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.CustomAttributes =
        [
            new CustomAttributeSummary { Name = "NeutralResourcesLanguage", Target = "Assembly", Value = "en-US" },
            new CustomAttributeSummary { Name = "AssemblyMetadata(Serviceable)", Target = "Assembly", Value = "True" },
            new CustomAttributeSummary { Name = "AssemblyDefaultAlias", Target = "Assembly", Value = "Test" }
        ];

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("AssemblyDefaultAlias", StringComparison.Ordinal)
            < output.IndexOf("AssemblyMetadata(Serviceable)", StringComparison.Ordinal));
        Assert.True(output.IndexOf("AssemblyMetadata(Serviceable)", StringComparison.Ordinal)
            < output.IndexOf("NeutralResourcesLanguage", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_MethodSections_AreSortedByTypeThenName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.UnsafeMembers =
        [
            new UnsafeMemberSummary { Member = "B.Type.A()", Reason = "Unsafe signature", Detail = "void A()", Kind = "signature" },
            new UnsafeMemberSummary { Member = "A.Type.Z()", Reason = "Unsafe signature", Detail = "void Z()", Kind = "signature" }
        ];
        inspection.ExtensionMethods =
        [
            new ExtensionMethodSummary { ExtendedType = "B.Type", MethodName = "A", ExtensionClass = "Extensions" },
            new ExtensionMethodSummary { ExtendedType = "A.Type", MethodName = "Z", ExtensionClass = "Extensions" }
        ];

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| `A.Type.Z()` |", StringComparison.Ordinal)
            < output.IndexOf("| `B.Type.A()` |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Z | method | A.Type |", StringComparison.Ordinal)
            < output.IndexOf("| A | method | B.Type |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_SymbolFields_AreSortedByFieldName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.Builder = "Microsoft";
        inspection.PdbFormat = "Portable";
        inspection.PdbLocation = "Symbol Package";
        inspection.SourceLinkJson = "{}";
        inspection.HasSourceLink = true;
        inspection.SymbolServer = "msdl.microsoft.com";

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| Builder |", StringComparison.Ordinal)
            < output.IndexOf("| PDB Format |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| PDB Path |", StringComparison.Ordinal)
            < output.IndexOf("| Source Link |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Source Link |", StringComparison.Ordinal)
            < output.IndexOf("| Symbol Server |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_SourceLinkAudit_UsesAvailableSourceFilesLabel()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.AllSourcesAccessible = false;
        inspection.AccessibleSourceFiles = 343;
        inspection.TotalSourceFiles = 345;
        inspection.EmbeddedSourceFiles = 2;

        var output = Serialize(inspection);

        Assert.Contains("| Source Files | 343/345 available |", output);
        Assert.DoesNotContain("accessible or embedded", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_RendersMismatchedFilesInSection()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityMismatched = 2;
        inspection.SourceIntegrityMismatches =
        [
            "/_/src/A.cs",
            "/_/src/B.cs"
        ];

        var output = Serialize(inspection);

        Assert.Contains("## SourceLink Integrity", output);
        Assert.Contains("| Mismatched | 2 |", output);
        Assert.Contains("| Mismatched Files | `/_/src/A.cs`, `/_/src/B.cs` |", output);
        Assert.DoesNotContain("Source integrity mismatch:", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_RendersLineEndingNormalizedCount()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityLineEndingNormalized = 2;

        var output = Serialize(inspection);

        Assert.Contains("## SourceLink Integrity", output);
        Assert.Contains("| CR/LF Mismatch | 2 normalized |", output);
        Assert.Contains("| Status | Verified |", output);
        Assert.Contains("| Verified | 2 |", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_FieldsAreAlphabetical()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityMismatched = 1;
        inspection.SourceIntegrityLineEndingNormalized = 1;
        inspection.SourceIntegrityUnverifiable = 1;
        inspection.SourceIntegrityMismatches = ["/_/src/A.cs"];

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| CR/LF Mismatch |", StringComparison.Ordinal)
            < output.IndexOf("| Mismatched |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Mismatched Files |", StringComparison.Ordinal)
            < output.IndexOf("| Status |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Unverifiable |", StringComparison.Ordinal)
            < output.IndexOf("| Verified |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_Signals_DoNotRenderSourceLinkCrlfMismatch()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = true;
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityLineEndingNormalized = 2;

        AuditSignalBuilder.PopulateLibraryAudit(typeof(OutputFormatterTests).Assembly.Location, inspection, new VerboseLogger(false));
        var output = Serialize(inspection);

        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("SourceLink CR/LF", output);
        Assert.Contains("## SourceLink Integrity", output);
        Assert.Contains("| CR/LF Mismatch | 2 normalized |", output);
    }

    [Fact]
    public void SingleAudit_Signals_AbsentSourceLink_DoesNotClaimSourceLinkFound()
    {
        // A PDB is present but carries no SourceLink data. The SourceLink signal must report
        // "Not found" without claiming SourceLink data was found in the PDB (#675).
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = false;
        inspection.PdbLocation = "standalone";
        inspection.SourceLinkUnavailableReason = "PDB checked; no SourceLink data";

        AuditSignalBuilder.PopulateLibraryAudit(typeof(OutputFormatterTests).Assembly.Location, inspection, new VerboseLogger(false));

        var sourceLink = Assert.Single(inspection.AuditSignals!, s => s.Signal == "SourceLink");
        Assert.Equal("Not found", sourceLink.Value);
        Assert.DoesNotContain("SourceLink data found", sourceLink.Evidence);
        Assert.Contains("no SourceLink data", sourceLink.Evidence);
    }

    private static LibraryInspection CreateTestAudit(string fileName, string? tfm)
    {
        return new LibraryInspection
        {
            FileName = fileName,
            FileType = "dll",
            Tfm = tfm,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = Path.GetFileNameWithoutExtension(fileName),
                AssemblyVersion = "1.0.0.0",
                TargetFramework = tfm != null ? $".NETCoreApp,Version=v{tfm[3..]}" : null,
                Architecture = "AnyCPU"
            }
        };
    }

    private static void AssertMarkdownTablesHaveUniformColumnCounts(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCodeFence = false;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (IsCodeFence(lines[i]))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence || !IsTableLine(lines[i]) || !IsSeparatorLine(lines[i + 1]))
                continue;

            var expected = CountCells(lines[i]);
            var tableStart = i + 1;
            i++;
            while (i < lines.Length && IsTableLine(lines[i]))
            {
                var actual = CountCells(lines[i]);
                if (actual != expected)
                    throw new InvalidOperationException($"Markdown table row {i + 1} has {actual} columns; expected {expected}. Table starts at line {tableStart}.");
                i++;
            }
        }

        static bool IsCodeFence(string line)
            => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

        static bool IsTableLine(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
        }

        static bool IsSeparatorLine(string line)
        {
            if (!IsTableLine(line))
                return false;

            var cells = line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);
            return cells.Length > 0 && cells.All(cell =>
                cell.Length > 0
                && cell.Any(c => c == '-')
                && cell.All(c => c is '-' or ':' or ' '));
        }

        static int CountCells(string line)
            => line.Trim().Trim('|').Split('|').Length;
    }

    private static LibraryInspectionReport CreateTestReport(string fileName, bool topFieldsOnly, params string[] tfms)
    {
        var inspections = tfms.Select(tfm => CreateTestAudit(fileName, tfm)).ToList();
        return new LibraryInspectionReport
        {
            Title = Path.GetFileNameWithoutExtension(fileName),
            Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
        };
    }

    private static string Serialize(LibraryInspectionReport report)
    {
        return MarkoutSerializer.Serialize(report, InspectionContext.Default).TrimEnd();
    }

    private static string Serialize(LibraryInspection inspection, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        return MarkoutSerializer.Serialize(view, InspectionContext.Default).TrimEnd();
    }

    // ===== API Output Formatter Tests =====

    private static ApiSurface CreateTestApiSurface(int typeCount = 3)
    {
        var types = Enumerable.Range(1, typeCount).Select(i => new ApiType
        {
            Namespace = "TestLib",
            Name = $"Type{i}",
            Kind = "class",
            Members = [new ApiMember { Name = "Method1", Kind = "method", Signature = "void Method1()" }]
        }).ToList();

        return new ApiSurface
        {
            Name = "TestLib",
            Source = "NuGet",
            Version = "1.0.0",
            Tfm = "net10.0",
            Types = types,
            PublicTypeCount = types.Count,
            PublicMethodCount = types.Count,
            PublicPropertyCount = 0
        };
    }

    [Fact]
    public void ApiFullSurface_QuietMode_SuppressesTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.DoesNotContain("## Classes", output);
        Assert.DoesNotContain("Type1", output);
    }

    [Fact]
    public void ApiFullSurface_MinimalMode_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_QuietWithTypeFilter_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        // Glob upgrade: quiet + TypeFilter should behave as minimal
        var options = new TypeOptions
        {
            Verbosity = Verbosity.Minimal,  // caller upgrades quiet to minimal for globs
            TypeFilter = "Type1*"
        };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_SourceAndTfm_PresentInCompactLine()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
        Assert.Contains("Version: 1.0.0", output);
    }

    [Fact]
    public void TypeView_SourceAndTfm_PresentInCompactLine()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" }]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var view = ApiOutputFormatter.BuildTypeView(type, "TestLib", "TestLib", "1.0.0", "NuGet", "net10.0", options);
        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        var output = writer.ToString().TrimEnd();

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
    }

    [Fact]
    public void TypeView_NullSource_OmitsSourceField()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = []
        };
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var view = ApiOutputFormatter.BuildTypeView(type, "TestLib", null, null, null, null, options);
        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        var output = writer.ToString().TrimEnd();

        Assert.DoesNotContain("Source:", output);
        Assert.DoesNotContain("TFM:", output);
    }

    [Fact]
    public void ApiTypeWriterOptions_IncludeFieldsProjection()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class"
        };
        var options = new ApiOptions
        {
            Columns = ["Name"],
            Fields = ["Title"]
        };

        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);

        Assert.NotNull(writerOptions.Projection);
        Assert.Equal(["Name"], writerOptions.Projection!.IncludeColumns);
        Assert.Equal(["Title"], writerOptions.Projection!.IncludeFields);
    }

    [Fact]
    public void ApiSurfaceWriterOptions_IncludeFieldsProjection()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions
        {
            Columns = ["Name"],
            Fields = ["Title"]
        };

        var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);

        Assert.NotNull(writerOptions.Projection);
        Assert.Equal(["Name"], writerOptions.Projection!.IncludeColumns);
        Assert.Equal(["Title"], writerOptions.Projection!.IncludeFields);
    }

    [Fact]
    public void PackageSignature_FieldsAreAlphabetical()
    {
        var result = new InspectionResult
        {
            PackageName = "Test.Package",
            Version = "1.0.0",
            SignatureResult = new SignatureVerificationResult
            {
                AuthorVerified = true,
                Publisher = "Example Publisher",
                Repository = "nuget.org",
                RepositoryVerified = true,
                StatusMessage = "Valid"
            }
        };

        var output = OutputFormatter.FormatResult(result, new InspectionOptions
        {
            IncludeSections = [PackageSections.Signature]
        }, PackageSectionDescriptors.CreatePipeline());

        Assert.True(output.IndexOf("| Author Verified |", StringComparison.Ordinal)
            < output.IndexOf("| Publisher |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Repository |", StringComparison.Ordinal)
            < output.IndexOf("| Repository Verified |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Signed |", StringComparison.Ordinal)
            < output.IndexOf("| Status |", StringComparison.Ordinal));
    }

    // ===== Quiet Output Tests =====

    [Fact]
    public void LibraryQuiet_ThreeLines()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, Verbosity.Quiet);
        var output = SerializeWithInclude(inspection, includeSections, topFieldsOnly: true);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains("Name: ", lines[2]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void LibrarySelectedSection_IncludesCompactContext()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.Source = "NuGet";
        inspection.PlatformVersion = "1.2.3";
        inspection.AuditSignals =
        [
            new AuditSignal("Provenance", "SourceLink", "Present", "PDB")
        ];
        var output = SerializeWithInclude(
            inspection,
            includeSections: ["Signals"],
            topFieldsOnly: true);

        Assert.StartsWith("# Test.dll", output.TrimStart());
        Assert.DoesNotContain("# Test.dll (net9.0)", output);
        Assert.Contains("Name: Test", output);
        Assert.Contains("Version: 1.2.3", output);
        Assert.Contains("Source: NuGet", output);
        Assert.Contains("## Signals", output);
    }

    [Fact]
    public void LibrarySelectedSection_FormatterUsesCompactContext()
    {
        var options = new LibraryOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = ["Signals"],
            Format = OutputFormat.Markdown
        };

        Assert.True(OutputFormatter.ShouldRenderLibraryContext(options));
    }

    [Fact]
    public void PackageQuiet_ThreeLines()
    {
        var result = CreateTestPackageResult();
        var view = new InspectionResultView(result);
        var output = MarkoutSerializer.Serialize(view, InspectionContext.Default, new MarkoutWriterOptions
        {
            IncludeSections = [PackageSections.Summary],
            IncludeDescription = false
        }).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void PackageDefaultOutput_OmitsTitleVersion()
    {
        var result = CreateTestPackageResult();
        var options = new InspectionOptions { Verbosity = Verbosity.Minimal };

        var output = OutputFormatter.FormatResult(result, options, PackageSectionDescriptors.CreatePipeline());

        Assert.StartsWith("# TestPackage", output.TrimStart());
        Assert.DoesNotContain("# TestPackage (1.0.0)", output);
        Assert.Contains("Version", output);
    }

    [Fact]
    public void PackageSelectedSection_IncludesCompactContextWithoutDescriptionOrTitleVersion()
    {
        var result = CreateTestPackageResult();
        result.Description = "Package description that should only appear in default views.";
        result.Source = "NuGet";
        result.AuditSignals =
        [
            new AuditSignal("NuGet", "Known vulnerabilities", "0", "NuGet advisory data")
        ];
        var options = new InspectionOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = [PackageSections.Signals]
        };

        var output = OutputFormatter.FormatResult(result, options, PackageSectionDescriptors.CreatePipeline());

        Assert.StartsWith("# TestPackage", output.TrimStart());
        Assert.DoesNotContain("# TestPackage (1.0.0)", output);
        Assert.Contains("Version: 1.0.0", output);
        Assert.Contains("Source: NuGet", output);
        Assert.DoesNotContain(result.Description, output);
        Assert.Contains("## Signals", output);
    }

    [Fact]
    public void PackageSelectedSection_FormatterUsesCompactContext()
    {
        var options = new InspectionOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = [PackageSections.Signals]
        };

        Assert.True(OutputFormatter.ShouldRenderPackageContext(options));
    }

    [Fact]
    public void ApiQuiet_ThreeLines()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    private static string RenderFullApi(ApiSurface api, ApiOptions options)
    {
        var (view, truncatedCount) = ApiOutputFormatter.BuildFullApiView(api, options);
        var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more types*");
        return writer.ToString().TrimEnd();
    }

    private static string SerializeWithInclude(LibraryInspection inspection, HashSet<string>? includeSections, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        return MarkoutSerializer.Serialize(view, InspectionContext.Default, new MarkoutWriterOptions
        {
            IncludeSections = includeSections
        }).TrimEnd();
    }

    private static InspectionResult CreateTestPackageResult()
    {
        return new InspectionResult
        {
            PackageName = "TestPackage",
            Version = "1.0.0",
            PackageTypes = ["Library"],
            Published = DateTimeOffset.Parse("2025-01-15"),
        };
    }

    [Fact]
    public void LibraryCompactView_AllSourcePaths_ShowSameFields()
    {
        var modified = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var assemblyInfo = new AssemblyInfo
        {
            AssemblyName = "TestLib",
            AssemblyVersion = "10.0.0.0",
            TargetFramework = ".NETCoreApp,Version=v10.0",
            Architecture = "AnyCPU"
        };

        var platform = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = SourceKind.Platform,
            PlatformVersion = "10.0.1",
            LastModified = modified
        };

        var nuget = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "NuGet",
            LastModified = modified
        };

        var file = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "File",
            LastModified = modified
        };

        var platformOutput = Serialize(platform, topFieldsOnly: true);
        var nugetOutput = Serialize(nuget, topFieldsOnly: true);
        var fileOutput = Serialize(file, topFieldsOnly: true);

        // Extract field names from compact line (format: "Name: value | Name: value | ...")
        static HashSet<string> ExtractFieldNames(string output)
        {
            var compactLine = output.Split('\n').First(l => l.Contains('|'));
            return compactLine.Split('|')
                .Select(f => f.Trim().Split(':')[0].Trim())
                .ToHashSet();
        }

        var platformFields = ExtractFieldNames(platformOutput);
        var nugetFields = ExtractFieldNames(nugetOutput);
        var fileFields = ExtractFieldNames(fileOutput);

        Assert.Equal(platformFields, nugetFields);
        Assert.Equal(platformFields, fileFields);

        // Verify expected fields are present
        Assert.Contains("Name", platformFields);
        Assert.Contains("Version", platformFields);
        Assert.Contains("TFM", platformFields);
        Assert.Contains("Arch", platformFields);
        Assert.Contains("Size", platformFields);
        Assert.Contains("Source", platformFields);
        Assert.Contains("Modified", platformFields);
    }
}
