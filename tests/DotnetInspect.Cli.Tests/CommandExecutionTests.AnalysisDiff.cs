using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriagePredicates_DoNotAlterDiscovery(string command)
    {
        string[] BaseArgs() => command switch
        {
            "library" => [command, TestAssemblyPath, "--discover"],
            "type" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            "member" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        var baseline = await RunAppAsync(BaseArgs());
        var filtered = await RunAppAsync([.. BaseArgs(), "--loop"]);
        var whereFiltered = await RunAppAsync([.. BaseArgs(), "--where", "Shape=box-value-type"]);

        Assert.Equal(0, baseline.Exit);
        Assert.Equal(0, filtered.Exit);
        Assert.Equal(0, whereFiltered.Exit);
        Assert.Equal(baseline.Output, filtered.Output);
        Assert.Equal(baseline.Output, whereFiltered.Output);
    }

    [Fact]
    public async Task PerformanceTriageShape_UnknownShapeReportsValidShapes()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "--triage-shape", "typo-shape", "--tsv");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unknown Performance Triage shape 'typo-shape'", error);
        Assert.Contains("capturing-delegate", error);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_ReportsOncePathQuantity()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--triage-shape", "allocation-fanout",
            "--where", "Member=*CreateAllocationFanout*",
            "--where", "OncePaths>=4",
            "--order-by", "OncePaths desc",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.Contains("\"shape\": \"allocation-fanout\"", output);
        Assert.Contains("\"provenance\": \"aggregate\"", output);
        Assert.Contains("\"direct_sites\": 1", output);
        Assert.Contains("\"once_paths\": 4", output);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_IsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Member=*CreateAllocationFanout*",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.DoesNotContain("\"shape\": \"allocation-fanout\"", output);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_IncludesDirectCallerLoopEvidence()
    {
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "--triage-shape", "allocation-fanout",
            "--where", "Member=*BoxDirect*",
            "--where", "CallerLoop=direct",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"shape\": \"allocation-fanout\"", output);
        Assert.Contains("\"caller_loop\": \"direct\"", output);
        Assert.Contains("\"caller_loop_depth\": 1", output);
        Assert.Contains("\"direct_sites\": 1", output);
    }

    [Fact]
    public async Task ResourceTriage_IsExplicitAndUsesExactBoundaryEvidence()
    {
        var defaultResult = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-v:m",
            "--tips",
            "q");
        var markdown = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");
        var jsonl = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--jsonl",
            "--tips",
            "q");
        var tsv = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tsv",
            "--tips",
            "q");
        var empty = await RunAppAsync(
            "library",
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");

        Assert.Equal(0, defaultResult.Exit);
        Assert.DoesNotContain(SectionNames.ArrayPoolEscapes, defaultResult.Output);

        Assert.Equal(0, markdown.Exit);
        Assert.Empty(markdown.Error);
        Assert.Contains("## Array Pool Escapes", markdown.Output);
        Assert.Contains("ReadBeforeReturn", markdown.Output);
        Assert.DoesNotContain(
            nameof(ResourceTriageFixture.TransformWithUnrelatedReadAfterReturn),
            markdown.Output);
        Assert.Contains("analysis.resource-lifecycle", markdown.Output);
        Assert.Contains("pool-churn-on-exception", markdown.Output);
        Assert.Contains("System.IO.Stream::Read", markdown.Output);

        Assert.Equal(0, jsonl.Exit);
        Assert.Empty(jsonl.Error);
        Assert.Contains("\"finding\":\"analysis.resource-lifecycle\"", jsonl.Output);
        Assert.Contains("\"provenance\":\"exact\"", jsonl.Output);
        Assert.Contains("\"actionability\":\"untrusted-input boundary\"", jsonl.Output);
        Assert.Contains("\"acquire_il\":\"IL_", jsonl.Output);
        Assert.Contains("\"boundary_il\":\"IL_", jsonl.Output);

        Assert.Equal(0, tsv.Exit);
        Assert.Empty(tsv.Error);
        Assert.StartsWith(
            "member\tcandidate\tfinding\tprovenance\tresource\tshape\timpact\tactionability\tboundary\tacquire_il\tboundary_il",
            tsv.Output);

        Assert.Equal(1, empty.Exit);
        Assert.Empty(empty.Output);
        Assert.Equal(
            "This section (Array Pool Escapes) produced no output.",
            empty.Error.Trim());
        Assert.DoesNotContain(
            "No actionable resource lifecycle candidates found.",
            empty.Output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_FiltersRowsByField()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Allocation=boxed *",
            "--where", "Path=straight-line",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.Contains("\"shape\": \"box-value-type\"", output);
        Assert.Contains("\"allocation\": \"boxed System.Int32\"", output);
        Assert.Contains("\"path\": \"straight-line\"", output);
        Assert.DoesNotContain("\"shape\": \"stackalloc-candidate\"", output);
    }

    [Fact]
    public async Task PerformanceTriage_ExposesAndFiltersFindingProvenance()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Finding=analysis.allocation",
            "--where", "Operation=box",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.Contains("\"candidate\": \"pt~", output);
        Assert.Contains("\"finding\": \"analysis.allocation\"", output);
        Assert.Contains("\"provenance\": \"exact\"", output);
        Assert.Contains("\"operation\": \"box\"", output);
        Assert.Contains("\"assembly\": \"", output);
        Assert.Contains("\"module_version_id\": \"", output);
        Assert.Contains("\"method_token\": \"0x06", output);
        Assert.Contains("\"token\": \"0x", output);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriage_ExposesDirectCallerLoopEvidenceWithoutChangingLocalLoop(string command)
    {
        const string typeName = "ILInspector.Analysis.CallerLoopFixtures.CallerLoopFixture";
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        string[] sourceArgs = command switch
        {
            "library" => [command, assemblyPath],
            "type" => [command, typeName, "--library", assemblyPath],
            "member" => [command, typeName, "BoxDirect", "--library", assemblyPath],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        var (exit, output, error) = await RunAppAsync(
            [
                .. sourceArgs,
                "-S", "Performance Triage",
                "--where", "CallerLoop=direct",
                "--where", "CallerLoopDepth>=1",
                "--order-by", "CallerLoopDepth desc",
                command == "library" ? "--json" : "--jsonl",
                "--tips", "q",
            ]);

        // Library scope surfaces rich diagnostics in the nested `performance` JSON (pretty-printed);
        // type/member scope keeps the flat triage row (compact JSONL).
        string sep = command == "library" ? ": " : ":";
        string depth = command == "library" ? "\"caller_loop_depth\": 1" : "\"caller_loop_depth\":\"1\"";

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("BoxDirect(int)", output);
        Assert.Contains($"\"caller_loop\"{sep}\"direct\"", output);
        Assert.Contains(depth, output);
        Assert.Contains("InvokeDirectInLoop", output);
        Assert.Contains($"\"loop\"{sep}\"\"", output);
        Assert.Contains($"\"candidate\"{sep}\"pt~", output);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriage_ReportsIncompleteAnalysisAcrossScopes(
        string command)
    {
        const string typeName =
            "ILInspector.Analysis.AsyncSiblingFriendFixtures."
            + "MalformedAsyncSourceFixture";
        string assemblyPath =
            FixtureCatalog.AnalysisAsyncSiblingFriend
                .AssemblyPath();
        string[] sourceArgs = command switch
        {
            "library" => [command, assemblyPath],
            "type" =>
            [
                command,
                typeName,
                "--library",
                assemblyPath,
            ],
            "member" =>
            [
                command,
                typeName,
                "AnalyzeAsync",
                "--library",
                assemblyPath,
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(command),
                command,
                null),
        };

        var result = await RunAppAsync(
        [
            .. sourceArgs,
            "-S",
            command == "library"
                ? SectionNames.PerformanceAsync
                : SectionNames.PerformanceTriage,
            "--tips",
            "q",
        ]);

        Assert.Contains(
            "warning: performance analysis incomplete",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "MalformedAsyncSourceFixture::AnalyzeAsync",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriage_SuppressesDiagnosticsOutsideSelectedScope(
        string command)
    {
        const string typeName =
            "ILInspector.Analysis.AsyncSiblingFriendFixtures."
            + "FriendProtectedReceiver";
        string assemblyPath =
            FixtureCatalog.AnalysisAsyncSiblingFriend
                .AssemblyPath();
        string[] sourceArgs = command == "type"
            ?
            [
                command,
                typeName,
                "--library",
                assemblyPath,
            ]
            :
            [
                command,
                typeName,
                "PublicAnalyzeAsync",
                "--library",
                assemblyPath,
            ];

        var result = await RunAppAsync(
        [
            .. sourceArgs,
            "-S",
            SectionNames.PerformanceTriage,
            "--tips",
            "q",
        ]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        if (command == "member")
        {
            Assert.Contains(
                "PublicAnalyzeAsync",
                result.Output,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(
                "PublicAnalyzeAsync",
                result.Output,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriage_DocumentJsonRejectsUnsupportedAnalysis(
        string command)
    {
        const string typeName =
            "ILInspector.Analysis.AsyncSiblingFriendFixtures."
            + "MalformedAsyncSourceFixture";
        string assemblyPath =
            FixtureCatalog.AnalysisAsyncSiblingFriend
                .AssemblyPath();
        string[] sourceArgs = command == "type"
            ? [command, typeName, "--library", assemblyPath]
            :
            [
                command,
                typeName,
                "AnalyzeAsync",
                "--library",
                assemblyPath,
            ];

        var result = await RunAppAsync(
        [
            .. sourceArgs,
            "-S",
            SectionNames.PerformanceTriage,
            "--json",
            "--tips",
            "q",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Document --json cannot represent Performance Triage analysis.",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task PerformanceTriageOrderByCallerLoopDepth_PutsMissingEvidenceLast(string direction)
    {
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "-S", "Performance Triage",
            "--order-by", $"CallerLoopDepth {direction}",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"caller_loop\": \"direct\"", output);
        Assert.Contains("\"caller_loop_depth\": 1", output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_NormalizesMetadataToken()
    {
        var baseline = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Finding=analysis.allocation",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, baseline.Exit);
        AssertOnlyPerformanceAnalysisWarnings(baseline.Error);
        string token = FirstPerformanceRow(baseline.Output).GetProperty("token").GetString()!;
        string unpaddedToken = $"0x{token[2..].TrimStart('0')}";

        var filtered = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", $"Token={unpaddedToken}",
            "--json",
            "--tips", "q");

        Assert.Equal(0, filtered.Exit);
        AssertOnlyPerformanceAnalysisWarnings(filtered.Error);
        Assert.Contains($"\"token\": \"{token}\"", filtered.Output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_MemberAcceptsDisplayedShortSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", nameof(FactsTableFixture),
            "--library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Member=BoxInt(int)",
            "--tsv",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("BoxInt(int)", output);
        Assert.Contains("\tbox-value-type\t", output);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_OrdersBeforeTop()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance: Boxing",
            "--where", "Shape=box-value-type",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        using var document = JsonDocument.Parse(output.Trim());
        var boxing = document.RootElement.GetProperty("performance").GetProperty("boxing");
        Assert.Equal(1, boxing.GetArrayLength());
        Assert.Equal("box-value-type", boxing[0].GetProperty("shape").GetString());
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_AcceptsHumanColumnNames()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance:*",
            "--where", "Path Confidence=dominates-return",
            "--order-by", "Root Reach desc",
            "--top", "1",
            "--tsv",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        var rows = output.TrimEnd().Split('\n');
        Assert.Single(rows.Skip(1));
    }

    [Fact]
    public async Task PerformanceTriageFilters_AutoSelectHomogeneousPerformanceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--where", "Priority>=low",
            "--top", "1",
            "--tsv",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.StartsWith("member\t", output);
        Assert.Single(output.TrimEnd().Split('\n').Skip(1));
    }

    [Fact]
    public async Task PerformanceTriageWhere_FiltersByPostDominance()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Post Dominance=return-post-dominates",
            "--order-by", "PostDominance desc,RootReach desc",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        var rows = PerformanceRows(output);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(
            "return-post-dominates",
            row.GetProperty("post_dominance").GetString()));
    }

    [Fact]
    public async Task PerformanceTriageCount_AppliesWhereBeforeTop()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance: Boxing",
            "--where", "Shape=box-value-type",
            "--top", "1",
            "--count",
            "--tips", "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 1, $"expected post-filter count before --top, got {count}");
    }

    // ===== Performance sections (kind-scoped decomposition of the library "Performance Triage" monolith) =====

    [Fact]
    public async Task PerformanceSections_NotInDefaultView()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-v:m", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Performance", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task PerformanceSection_SingleKind_RendersOnlyThatKind()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Boxing", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.DoesNotContain("## Performance: Arrays", output);
        Assert.DoesNotContain("## Performance: Closures", output);
    }

    [Fact]
    public async Task PerformanceAsyncEvidence_RendersAsCodeSpan_WithoutHtmlEscapingCompilerName()
    {
        // Evidence embeds compiler-generated names with angle brackets (e.g. the async state
        // machine <GetAsyncEnumerator>d__1). It must render as a code span like the Member and
        // Allocation columns, showing the brackets literally — not HTML-escaped as &lt;/&gt;.
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Async", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Async", output);
        Assert.Contains("async state-machine allocation (<", output);
        Assert.DoesNotContain("async state-machine allocation (&lt;", output);

        // Machine output stays raw (no code-span markup, unescaped brackets).
        var (tsvExit, tsv, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Async", "--tsv", "--tips", "q");
        Assert.Equal(0, tsvExit);
        Assert.Contains("async state-machine allocation (<", tsv);
        Assert.DoesNotContain("&lt;", tsv);
    }

    [Fact]
    public async Task PerformanceAsync_FindsSyncCallsWithAsyncSiblings()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            "Performance: Async",
            "--triage-shape",
            "sync-call-in-async",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        AssertOnlyPerformanceAnalysisWarnings(error);
        Assert.Contains("## Performance: Async", output);
        Assert.Contains("CallsSyncSiblingFromAsync", output);
        Assert.Contains("ReadValueAsync", output);
        Assert.Contains("CallsFileReadLinesFromAsync", output);
        Assert.Contains("System.IO.File::ReadLinesAsync", output);
    }

    [Fact]
    public async Task PerformanceTriageEvidence_TypeScope_RendersAsCodeSpan_WithoutHtmlEscapingGenerics()
    {
        // The type/member Performance Triage lens has the same Evidence column; a generic value-type
        // box (box System.Memory<T>) must render as a code span with literal angle brackets, not the
        // HTML-escaped &lt;T&gt;. The Allocation column already renders it literally.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.Serialization.Converters.MemoryConverter",
            "--platform", "System.Text.Json", "--all", "-S", "Performance Triage", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("box System.Memory<T>", output);
        Assert.DoesNotContain("box System.Memory&lt;T&gt;", output);
    }

    [Fact]
    public async Task PerformanceGroup_RendersMultipleKindSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.Contains("## Performance: Arrays", output);
        Assert.Contains("## Performance: Closures and Delegates", output);
    }

    [Fact]
    public async Task PerformanceGroup_JsonEmitsNestedProjection_NotRetiredMonolithKey()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        using var document = JsonDocument.Parse(output.Trim());
        Assert.False(
            document.RootElement.TryGetProperty("optimization_opportunities", out _),
            "retired monolith key must be absent");

        var performance = document.RootElement.GetProperty("performance");
        Assert.True(performance.TryGetProperty("boxing", out var boxing));
        Assert.True(boxing.GetArrayLength() > 0);
        Assert.True(performance.TryGetProperty("arrays", out _));
    }

    [Fact]
    public async Task PerformanceKind_AbsentWhenEmpty()
    {
        // A tiny fixture assembly with no async candidates: the async kind must be absent, not an
        // empty section (il-offset gating parity).
        var (exit, output, error) = await RunAppAsync(
            "library", FixtureCatalog.AnalysisLookalike.AssemblyPath(),
            "-S", "Performance: Async", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        AssertOnlyPerformanceAnalysisWarnings(
            error,
            "This section (Performance: Async) produced no output.");
    }

    [Fact]
    public async Task PerformanceGroup_CountEmitsPerKindMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Section | Count |", output);
        Assert.Contains("Performance: Boxing", output);
        // Empty kinds still report a zero row so agents can cheaply probe the whole category.
        Assert.Contains("Performance: Other", output);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--json")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--table")]
    [InlineData("--plaintext")]
    public async Task PerformanceGroup_CountMapHonorsSelectedFormat(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            "@Performance",
            "--count",
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Performance: Boxing", output, StringComparison.Ordinal);
        Assert.Contains("Performance: Other", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerformanceGroup_CountMapRejectsMermaidWithoutRejectingScalarCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--count", "--mermaid",
            "--tips", "q");
        var (scalarExit, scalarOutput, scalarError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "References", "--count", "--mermaid",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot render multiple sections as Mermaid", error, StringComparison.Ordinal);
        Assert.DoesNotContain("| Section | Count |", output, StringComparison.Ordinal);
        Assert.Equal(0, scalarExit);
        Assert.Empty(scalarError);
        Assert.True(int.TryParse(scalarOutput.Trim(), out _));
    }

    [Fact]
    public async Task PerformanceLegacyName_RedirectsToGroup()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.Contains("## Performance: Arrays", output);
    }

    [Fact]
    public async Task PerformanceGroup_TabularRendersSingleKindLabeledTable()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance:*", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The flattened group renders as one self-describing table: exactly one header, and its
        // leading column is the kind label so each row says which performance kind it belongs to.
        var headerCount = lines.Count(line => line.StartsWith("kind\t", StringComparison.Ordinal));
        Assert.Equal(1, headerCount);
        Assert.StartsWith("kind\tmember\t", lines[0]);
        // Rows from more than one kind are present and labeled (e.g. Boxing and Arrays).
        var kinds = lines.Skip(1).Select(l => l.Split('\t')[0]).Distinct().ToList();
        Assert.Contains("Boxing", kinds);
        Assert.Contains("Arrays", kinds);
    }

    [Fact]
    public async Task PerformanceDomain_TabularRequiresAConcreteHomogeneousSelection()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("display one section at a time", error);
        Assert.Contains("Performance:*", error);
    }

    [Fact]
    public async Task PerformanceGroup_JsonlEmitsOnlyValidRecords_NoBlankSeparators()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance:*", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        // Every emitted line must parse as JSON (no blank inter-section separators), and each record
        // must carry its kind label so the flattened stream is self-describing.
        foreach (var line in output.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0)
                continue;
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("kind", out _));
        }
        Assert.DoesNotContain(output.TrimEnd('\n').Split('\n'), line => line.Length == 0);
    }

    [Fact]
    public async Task PerformanceGroup_NoHeaderTabular_PreservesEveryRow_NoBlankSeparators()
    {
        // With --no-header the flattened table emits no header line, so every line is a data row and
        // identical rows must all survive. Row count must match the with-header data-row count, and
        // no blank section separators may leak into the stream.
        var withHeader = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance:*", "--tsv", "--order-by", "Allocation", "--tips", "q");
        var noHeader = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance:*", "--tsv", "--no-header", "--order-by", "Allocation", "--tips", "q");

        Assert.Equal(0, withHeader.Exit);
        Assert.Equal(0, noHeader.Exit);

        var dataRows = withHeader.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => !line.StartsWith("kind\t", StringComparison.Ordinal));
        var noHeaderRows = noHeader.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(dataRows, noHeaderRows.Length);
        Assert.DoesNotContain(noHeader.Output.TrimEnd('\n').Split('\n'), line => line.Length == 0);
    }

    [Fact]
    public async Task PerformanceTriageShape_SectionMappingIsCaseInsensitive()
    {
        // A differently-cased --triage-shape is accepted by validation; it must resolve to the same
        // kind section its findings bucket into, not silently route to Performance: Other.
        var lower = await RunAppAsync(
            "library", "System.Text.Json", "--triage-shape", "box-value-type", "--count", "--tips", "q");
        var upper = await RunAppAsync(
            "library", "System.Text.Json", "--triage-shape", "BOX-VALUE-TYPE", "--count", "--tips", "q");

        Assert.Equal(0, lower.Exit);
        Assert.Equal(0, upper.Exit);
        Assert.True(int.TryParse(lower.Output.Trim(), out var lowerCount) && lowerCount > 0, lower.Output);
        Assert.Equal(lower.Output.Trim(), upper.Output.Trim());
    }

    [Fact]
    public async Task PerformanceSections_CatalogHidden_CategoryDiscoverableAndDrillable()
    {
        // Bare -D (effective discovery) must not list the kind-scoped performance sections at the top
        // level — the @Performance category is their single discoverable entrypoint — yet they must
        // stay reachable by drilling into that category.
        var bare = await RunAppAsync("library", "System.Text.Json", "-D", "--tips", "q");
        Assert.Equal(0, bare.Exit);
        var bareSectionNames = bare.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("| section", StringComparison.Ordinal))
            .Select(ExtractSectionName)
            .ToArray();
        Assert.DoesNotContain(bareSectionNames, n => n.StartsWith("Performance: ", StringComparison.Ordinal));
        Assert.Contains(bare.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            l => ExtractSectionName(l) == "@Performance" && l.Contains("category", StringComparison.Ordinal));

        var drill = await RunAppAsync("library", "System.Text.Json", "-D", "@Performance", "--tips", "q");
        Assert.Equal(0, drill.Exit);
        Assert.Contains("Performance: Boxing", drill.Output);
        Assert.Contains("Performance: Other", drill.Output);
    }

    [Fact]
    public async Task PerformanceGroup_TableRendersOneHeader_AndRowsCapCountsDataRowsOnly()
    {
        // The flattened pretty table must be one aligned table: exactly one header regardless of how
        // many kinds contribute, and a --rows cap must yield header + N data rows (embedded per-kind
        // headers previously inflated the count and stole a row slot).
        const int cap = 5;
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance:*", "--table", "--rows", cap.ToString(),
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headerCount = lines.Count(l => l.StartsWith("Kind", StringComparison.Ordinal) && l.Contains("Member", StringComparison.Ordinal));
        Assert.Equal(1, headerCount);
        // One header + cap data rows.
        Assert.Equal(cap + 1, lines.Length);
    }

    [Fact]
    public async Task PerformanceTriageWhere_UnknownFieldReportsSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--where", "Allocaton=boxed *",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Field 'Allocaton' is not filterable", error);
        Assert.Contains("Allocation", error);
    }

    [Theory]
    [InlineData("RootReach>=abc", "expects an integer")]
    [InlineData("Confidence>=bogus", "expects one of low, medium, high")]
    public async Task PerformanceTriageWhere_InvalidValuesReportDiagnostics(string predicate, string expected)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--where", predicate,
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_TriageCompositeMustBeStandalone()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--order-by", "Triage desc,RootReach desc",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Triage is a composite order", error);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_EmptyTermsReportDiagnostic()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--order-by", ",",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--order-by requires at least one field", error);
    }

    [Fact]
    public async Task Diff_TypeFilter_LongNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "System.Text.Json.Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Summary", output);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_Changes_LocalPair_RendersTypedApiComparisonInMarkdownAndJson()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"diff-query-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var oldPath = Path.Combine(tempDir, "old.dll");
            var newPath = Path.Combine(tempDir, "new.dll");
            WriteApiDiffQueryAssembly(oldPath, includeAddedMethod: false);
            WriteApiDiffQueryAssembly(newPath, includeAddedMethod: true);
            var range = $"{oldPath}..{newPath}";

            var markdown = await RunAppAsync(
                "diff", "--library", range,
                "-S", DiffSections.Changes.Name,
                "--tips", "q");
            var json = await RunAppAsync(
                "diff", "--library", range,
                "-S", DiffSections.Changes.Name,
                "--json", "--tips", "q");

            Assert.Equal(0, markdown.Exit);
            Assert.Empty(markdown.Error);
            Assert.Contains("### Widget", markdown.Output);
            Assert.Contains("Added", markdown.Output);
            Assert.Equal(0, json.Exit);
            Assert.Empty(json.Error);
            Assert.Contains("\"changes\"", json.Output);
            Assert.Contains("\"type\": \"Widget\"", json.Output);
            Assert.Contains("Added", json.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Diff_InspectionFailures_AreNeverReportedAsCleanAcrossOutputModes()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"diff-failure-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string oldPath =
                Path.Combine(tempDir, "old.dll");
            string newPath =
                Path.Combine(tempDir, "new.dll");
            WriteMalformedAdjacencyAssembly(
                oldPath,
                malformedAssemblyReference: true,
                referenceFromPublicSurface: true);
            WriteMalformedAdjacencyAssembly(
                newPath,
                malformedAssemblyReference: true,
                referenceFromPublicSurface: true);
            string range = $"{oldPath}..{newPath}";

            var markdown = await RunAppAsync(
                "diff",
                "--library",
                range,
                "--tips",
                "q");
            var json = await RunAppAsync(
                "diff",
                "--library",
                range,
                "--json",
                "--tips",
                "q");

            Assert.Equal(1, markdown.Exit);
            Assert.Contains(
                "## Inspection Failures",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "public-key token must contain exactly 8 bytes",
                markdown.Output,
                StringComparison.Ordinal);
            Assert.Equal(1, json.Exit);
            Assert.Contains(
                "public-key token must contain exactly 8 bytes",
                json.Output,
                StringComparison.Ordinal);

            var findingMarkdown = await RunAppAsync(
                "diff",
                "--library",
                range,
                "-t",
                "N.Healthy",
                "-S",
                DiffSections.FindingTransitions.Name,
                "--tips",
                "q");
            var findingJson = await RunAppAsync(
                "diff",
                "--library",
                range,
                "-t",
                "N.Healthy",
                "-S",
                DiffSections.FindingTransitions.Name,
                "--json",
                "--tips",
                "q");
            var findingTable = await RunAppAsync(
                "diff",
                "--library",
                range,
                "-t",
                "N.Healthy",
                "-S",
                DiffSections.FindingTransitions.Name,
                "--table",
                "--tips",
                "q");
            var analysisTable = await RunAppAsync(
                "diff",
                "--library",
                range,
                "-t",
                "N.Healthy",
                "-S",
                DiffSections.AnalysisDiff.Name,
                "--table",
                "--tips",
                "q");

            Assert.Equal(1, findingMarkdown.Exit);
            Assert.Contains(
                "## Inspection Failures",
                findingMarkdown.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "invalid AssemblyRef row",
                findingMarkdown.Output,
                StringComparison.Ordinal);
            Assert.Equal(1, findingJson.Exit);
            Assert.Contains(
                "inspection_failures",
                findingJson.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "invalid AssemblyRef row",
                findingJson.Output,
                StringComparison.Ordinal);
            Assert.Equal(1, findingTable.Exit);
            Assert.Contains(
                "API comparison is incomplete",
                findingTable.Error,
                StringComparison.Ordinal);
            Assert.Equal(1, analysisTable.Exit);
            Assert.DoesNotContain(
                "# Diff",
                analysisTable.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "## Inspection Failures",
                analysisTable.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "API comparison is incomplete",
                analysisTable.Error,
                StringComparison.Ordinal);

            string[][] singleShapeModes =
            [
                ["--table"],
                ["--tsv"],
                ["--jsonl"],
                ["--name-only"],
            ];
            foreach (string[] mode in singleShapeModes)
            {
                var result = await RunAppAsync(
                    [
                        "diff",
                        "--library",
                        range,
                        "--tips",
                        "q",
                        .. mode,
                    ]);

                Assert.Equal(1, result.Exit);
                Assert.Contains(
                    "API comparison is incomplete",
                    result.Error,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Diff_TypeFilter_ShortNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_NoMatches_PrintsNote()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "DefinitelyMissingNamespace", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Summary", output);
        Assert.Contains("no changes", output);
        Assert.Contains("type filter matched no changed types", error);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsPackageTypeIntroduction()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@8.0.6..9.0.0",
            "-t", "System.Text.Json.Schema.JsonSchemaExporter",
            "-S", "Finding Transitions", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("api.type", output);
        Assert.Contains("System.Text.Json.Schema.JsonSchemaExporter", output);
        Assert.Contains("8.0.6", output);
        Assert.Contains("9.0.0", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_ComplexityContext_ProjectsLocalPopulationFields()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-S", "Complexity Context",
            "--columns",
            "Member,State,Delta,PopulationSize,PercentileRank,Kind",
            "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string changedLine = Assert.Single(
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line =>
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                return root.TryGetProperty(
                        "member",
                        out JsonElement member)
                    && member.GetString()!
                        .Contains(
                            "RegressesAllocInLoop",
                            StringComparison.Ordinal)
                    && root.TryGetProperty(
                        "state",
                        out JsonElement state)
                    && state.GetString() == "changed";
            });
        using var changedDocument = JsonDocument.Parse(changedLine);
        JsonElement changed = changedDocument.RootElement;
        Assert.Equal("1", changed.GetProperty("delta").GetString());
        Assert.Equal(
            "analysis.complexity.normal-flow",
            changed.GetProperty("kind").GetString());
        Assert.True(
            int.Parse(changed.GetProperty("population_size").GetString()!)
                > 1);
        Assert.InRange(
            double.Parse(
                changed.GetProperty("percentile_rank").GetString()!,
                CultureInfo.InvariantCulture),
            0,
            100);
    }

    [Fact]
    public async Task Diff_StructuralContext_ProjectsTypedCohortFields()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-S", "Structural Context",
            "--columns",
            "Member,State,InstructionDelta,ComplexityDelta,LoopDelta,"
                + "AllocationDelta,InstructionDirection,CohortSize,"
                + "PopulationSize,Kind",
            "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string changedLine = Assert.Single(
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line =>
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                return root.TryGetProperty("member", out JsonElement member)
                    && member.GetString()!.Contains(
                        "RegressesAllocInLoop",
                        StringComparison.Ordinal);
            });
        using var changedDocument = JsonDocument.Parse(changedLine);
        JsonElement changed = changedDocument.RootElement;
        Assert.Equal("13", changed.GetProperty("instruction_delta").GetString());
        Assert.Equal("1", changed.GetProperty("complexity_delta").GetString());
        Assert.Equal("1", changed.GetProperty("loop_delta").GetString());
        Assert.Equal("1", changed.GetProperty("allocation_delta").GetString());
        Assert.Equal(
            "increased",
            changed.GetProperty("instruction_direction").GetString());
        Assert.Equal(
            ImplementationComplexityFindings.StructuralCohortDescriptor.Id,
            changed.GetProperty("kind").GetString());
        Assert.True(
            int.Parse(changed.GetProperty("population_size").GetString()!)
                > 1);
        Assert.InRange(
            int.Parse(changed.GetProperty("cohort_size").GetString()!),
            1,
            int.Parse(changed.GetProperty("population_size").GetString()!));

        var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-S", "Structural Context",
            "--json", "--tips", "q");

        Assert.Equal(0, jsonExit);
        Assert.Empty(jsonError);
        using var jsonDocument = JsonDocument.Parse(jsonOutput);
        JsonElement jsonRow = Assert.Single(
            jsonDocument.RootElement
                .GetProperty("structural_context")
                .EnumerateArray(),
            row => row.GetProperty("member").GetString()!.Contains(
                "RegressesAllocInLoop",
                StringComparison.Ordinal));
        Assert.Equal(
            JsonValueKind.Number,
            jsonRow.GetProperty("instruction_delta").ValueKind);
        Assert.Equal(13, jsonRow.GetProperty("instruction_delta").GetInt32());
        Assert.Equal(
            JsonValueKind.Number,
            jsonRow.GetProperty("cohort_size").ValueKind);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsAllocationIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "RegressesAllocInLoop",
            "--finding", "analysis.allocation",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.allocation", output);
        Assert.Contains("RegressesAllocInLoop", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsCallSiteIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "RegressesAllocInLoop",
            "--finding", "analysis.call-site",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.call-site", output);
        Assert.Contains("RegressesAllocInLoop", output);
        Assert.Contains(".Add(", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsUnsafetyIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "AddsUnsafe",
            "--finding", "analysis.unsafety",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.unsafety", output);
        Assert.Contains("AddsUnsafe", output);
        Assert.Contains("StackAlloc", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Theory]
    [InlineData("csharp.line", "return 1;", "return 2;")]
    [InlineData("il.op", "ldc.i4 1", "ldc.i4 2")]
    public async Task Diff_FindingTransitions_ConfirmsImplementationOccurrenceChanges(
        string descriptor,
        string oldEvidence,
        string newEvidence)
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "ConstantValue",
            "--finding", descriptor,
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Removed", output);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains(descriptor, output);
        Assert.Contains(oldEvidence, output);
        Assert.Contains(newEvidence, output);
    }

    [Theory]
    [InlineData("csharp.line")]
    [InlineData("il.op")]
    public async Task Diff_FindingTransitions_ImplementationFindingsRequireOneMemberBeforeAcquisition(
        string descriptor)
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", descriptor,
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"--finding {descriptor} requires exactly one --member", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_AllocationRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.allocation",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires exactly one --member", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_CallSiteRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.call-site",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--finding analysis.call-site requires exactly one --member",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_UnsafetyRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.unsafety",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--finding analysis.unsafety requires exactly one --member",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsUnknownDescriptorBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.unknown",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unsupported Finding descriptor 'analysis.unknown'", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RequiresFocusedTargetBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-S", "Finding Transitions", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions requires --type", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsCompatibilityFiltersBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget", "-S", "Finding Transitions",
            "--additive", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsImplementationDiffBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-S", "Finding Transitions",
            "-S", "Implementation Diff",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions must be selected by itself", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_ComplexityContext_RejectsCompositionBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-S", "Complexity Context",
            "-S", "Implementation Diff",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Complexity Context must be selected by itself",
            error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_StructuralContext_RejectsCompositionBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-S", "Structural Context",
            "-S", "Implementation Diff",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Structural Context must be selected by itself",
            error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ImpliedSelectionRejectsCompositionBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-m", "HotPath",
            "--finding", "analysis.allocation",
            "-S", "Analysis Diff",
            "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions must be selected by itself", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_IsDiscoverableAndSelectableForLocalPair()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        var range = $"{oldPath}..{newPath}";

        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "diff", "--library", range, "-D", "--schema", "--tips", "q");
        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "diff", "--library", range,
            "-t", "DiffFixtureSample.DiffSample",
            "-S", "Finding Transitions", "--table", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Empty(discoverError);
        Assert.Contains("Finding Transitions", discoverOutput);
        Assert.Contains("Complexity Context", discoverOutput);
        Assert.Contains("Structural Context", discoverOutput);
        Assert.Equal(0, selectExit);
        Assert.Empty(selectError);
        Assert.Contains("PairFinding.Present", selectOutput);
        Assert.Contains("DiffFixtureSample.DiffSample", selectOutput);
    }

    [Fact]
    public async Task Diff_DiscoveryUsesAuthoredCategoryWithoutComputedPoles()
    {
        var bare = await RunAppAsync(
            "diff", "-D", "--table", "--tips", "q");
        var category = await RunAppAsync(
            "diff", "-D", SectionCategoryNames.Diff, "--schema", "--table",
            "--tips", "q");

        Assert.Equal(0, bare.Exit);
        Assert.Empty(bare.Error);
        Assert.StartsWith(SectionCategoryNames.Diff, bare.Output, StringComparison.Ordinal);
        Assert.Contains(DiffSections.Changes.Name, bare.Output, StringComparison.Ordinal);
        Assert.Contains(
            DiffSections.AnalysisDiff.Name,
            bare.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            DiffSections.ImplementationDiff.Name,
            bare.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@All", bare.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Default", bare.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hidden", bare.Output, StringComparison.Ordinal);

        Assert.Equal(0, category.Exit);
        Assert.Empty(category.Error);
        Assert.Contains(DiffSections.Changes.Name, category.Output, StringComparison.Ordinal);
        Assert.Contains(DiffSections.AnalysisDiff.Name, category.Output, StringComparison.Ordinal);
        Assert.Contains(
            DiffSections.ImplementationDiff.Name,
            category.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            DiffSections.FindingTransitions.Name,
            category.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_SchemaRequiresDiscovery()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--schema", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--schema requires -D/--discover.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData()]
    [InlineData("--schema")]
    public async Task Diff_DiscoveryGlobDoesNotExposeExactOnlyFindingTransitions(
        params string[] schema)
    {
        var (exit, output, error) = await RunAppAsync(
        [
            "diff", "-D", "*Transitions", .. schema, "--table", "--tips", "q",
        ]);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Transition", output, StringComparison.Ordinal);
        Assert.Contains("Section '*Transitions' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_SelectGlobMatchingOnlyFindingTransitionsFailsBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-S", "*Transitions", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("No sections match '*Transitions'.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-old.dll", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@All")]
    [InlineData("@Default")]
    [InlineData("@Hidden")]
    public async Task Diff_ComputedCategoryPolesAreRejected(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-S", selector, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Select value '{selector}' not found.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("missing-old.dll", error, StringComparison.Ordinal);
    }
}
