using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Fact]
    public async Task RelationshipCommands_NamespacePrefixInputs_PrintPrefixBrowseHint()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "System.Text", "--tips", "q");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsOutput);
        Assert.Contains("looks like a namespace prefix", implementsError);
        Assert.Contains("type System.Text", implementsError);
        Assert.Contains("find \"System.Text*\" --platform", implementsError);

        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "System.Text", "--tips", "q");

        Assert.Equal(0, extensionsExit);
        Assert.Contains("No extension methods found", extensionsOutput);
        Assert.Contains("looks like a namespace prefix", extensionsError);
        Assert.Contains("type System.Text", extensionsError);
        Assert.Contains("find \"System.Text*\" --platform", extensionsError);
    }

    [Fact]
    public async Task Depends_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Text", "--tips", "q");

        Assert.Equal(1, dependsExit);
        Assert.Empty(dependsOutput);
        Assert.Contains(
            "Type 'System.Text' not found in the specified scope.",
            dependsError);
        Assert.Contains("looks like a namespace prefix", dependsError);
        Assert.Contains("type System.Text", dependsError);
        Assert.Contains("find \"System.Text*\" --platform", dependsError);
    }

    [Fact]
    public async Task Implements_Count_ComposesWithJson()
    {
        var (exit, output, _) = await RunAppAsync(
            "implements", "IDisposable", "--library", TestAssemblyPath, "--count", "--format=json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Extensions_Count_ComposesWithJson()
    {
        var (exit, output, _) = await RunAppAsync(
            "extensions", "String", "--library", TestAssemblyPath, "--count", "--format=json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Extensions_CountIsIndependentOfQuietSummaryShape()
    {
        var normal = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq",
            "--count", "--tips", "q");
        var quiet = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq",
            "--count", "-v", "q", "--tips", "q");
        var quietWindowed = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq",
            "--count", "-v", "q", "--rows", "1..1", "--tips", "q");

        Assert.Equal(0, normal.Exit);
        Assert.Equal(normal.Output, quiet.Output);
        Assert.NotEqual("0", normal.Output.Trim());
        Assert.Equal("1", quietWindowed.Output.Trim());
    }

    [Fact]
    public async Task SearchCounts_ApplyRowsAndValidateProjectedColumns()
    {
        var find = await RunAppAsync(
            "find", "*", "--platform", "System.Private.CoreLib",
            "--count", "--rows", "1..1", "--tips", "q");
        var members = await RunAppAsync(
            "find", ".ToString", "--platform", "System.Private.CoreLib",
            "--count", "--rows", "1..1", "--tips", "q");
        var implements = await RunAppAsync(
            "implements", "IDisposable", "--platform", "System.Private.CoreLib",
            "--count", "--rows", "1..1", "--tips", "q");
        var extensions = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq",
            "--count", "--rows", "1..1", "--tips", "q");
        var invalid = await RunAppAsync(
            "find", "*", "--platform", "System.Private.CoreLib",
            "--count", "--columns", "NoSuchColumn", "--tips", "q");

        foreach (var result in new[] { find, members, implements, extensions })
        {
            Assert.Equal(0, result.Exit);
            Assert.Equal("1", result.Output.Trim());
            Assert.Empty(result.Error);
        }

        Assert.Equal(1, invalid.Exit);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchColumn", invalid.Error);
        Assert.DoesNotContain("Payload", invalid.Error);
    }

    [Fact]
    public async Task Depends_DoesNotCertifyAbsenceWhenACandidateWasExcluded()
    {
        string unsupported = Path.Combine(
            Path.GetTempPath(),
            $"depends-absent-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            unsupported,
            MetadataTestImages.BuildWindowsMetadataImage());
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "depends", "Totally.Absent.TypeName`9",
                "--library", TestAssemblyPath,
                "--library", unsupported,
                "--count");

            Assert.Equal(
                DependsCommand.UncertifiedScanExitCode,
                exit);
            Assert.NotEqual(DependsCommand.TypeNotFoundExitCode, exit);
            Assert.Contains(
                Path.GetFileName(unsupported),
                error,
                StringComparison.Ordinal);

            // Marking the scan uncertified must not swallow the diagnosis. An
            // exit code that only says "uncertified" leaves the user with no
            // statement of what happened to the type they asked about.
            Assert.Contains(
                "not found in the specified scope",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(unsupported);
        }
    }

    [Fact]
    public async Task Depends_NamesAnExcludedUnsupportedAssemblyInsteadOfReportingACleanPartialGraph()
    {
        string unsupported = Path.Combine(
            Path.GetTempPath(),
            $"depends-unsupported-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            unsupported,
            MetadataTestImages.BuildWindowsMetadataImage());
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "depends", "SampleGenericClass`1",
                "--library", TestAssemblyPath,
                "--library", unsupported,
                "--count");

            Assert.Equal(DependsCommand.UncertifiedScanExitCode, exit);
            Assert.True(
                int.TryParse(output.Trim(), out var count) && count > 0,
                $"expected the healthy neighbor to still resolve, got: {output}");
            Assert.Contains(
                Path.GetFileName(unsupported),
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "unsupported metadata format",
                error,
                StringComparison.Ordinal);
            Assert.Contains("may be incomplete", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(unsupported);
        }
    }

    [Fact]
    public async Task Depends_Count_ComposesWithJson()
    {
        // The type with dependencies matters: a type with none short-circuits before the
        // JSON branch, which is why an earlier probe using such a type saw no defect.
        var (exit, output, _) = await RunAppAsync(
            "depends", "SampleGenericClass`1", "--library", TestAssemblyPath, "--count", "--format=json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), $"expected a bare count, got: {output}");
        Assert.True(count > 0, "fixture must have dependencies for this to be a meaningful regression test");
    }

    [Fact]
    public async Task Depends_Count_AppliesRowsToLogicalEdges()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Int128",
            "--count", "--rows", "2..2", "--tips", "q");
        var (renderExit, rendered, renderError) = await RunAppAsync(
            "depends", "System.Int128",
            "--rows", "2..2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);

        Assert.Equal(0, renderExit);
        Assert.Empty(renderError);
        Assert.Contains("IBinaryInteger", rendered);
        Assert.Contains("IBinaryNumber", rendered);
        Assert.DoesNotContain("IBitwiseOperators", rendered);
        Assert.DoesNotContain("IMinMaxValue", rendered);
    }

    [Fact]
    public async Task Depends_GraphFormatsShareTheLogicalEdgeWindow()
    {
        string[] window =
        [
            "depends", "System.Int128",
            "--rows", "2..3", "--tips", "q",
        ];
        var count = await RunAppAsync([.. window, "--count"]);
        var table = await RunAppAsync([.. window, "--format=table"]);
        var tsv = await RunAppAsync([.. window, "--format=tsv"]);
        var jsonl = await RunAppAsync([.. window, "--format=jsonl"]);
        var json = await RunAppAsync([.. window, "--format=json"]);
        var mermaid = await RunAppAsync([.. window, "--format=mermaid"]);

        foreach (var result in new[]
                 {
                     count,
                     table,
                     tsv,
                     jsonl,
                     json,
                     mermaid,
                 })
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
        }

        Assert.Equal("2", count.Output.Trim());
        Assert.Equal(3, NonEmptyLineCount(table.Output));
        Assert.Equal(3, NonEmptyLineCount(tsv.Output));
        Assert.Equal(2, NonEmptyLineCount(jsonl.Output));
        using (JsonDocument document = JsonDocument.Parse(json.Output))
        {
            Assert.Equal(
                2,
                document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
                    .GetArrayLength());
        }
        Assert.Equal(
            2,
            mermaid.Output.Split('\n').Count(static line =>
                line.Contains("-->", StringComparison.Ordinal)
                || line.Contains("-.->", StringComparison.Ordinal)));

        static int NonEmptyLineCount(string value) =>
            value.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [Fact]
    public async Task Depends_LimitUsesLogicalEdgeRowsAcrossSinks()
    {
        string[] limit =
        [
            "depends", "System.Int128",
            "-n", "2", "--tips", "q",
        ];
        var count = await RunAppAsync([.. limit, "--count"]);
        var table = await RunAppAsync([.. limit, "--format=table"]);
        var jsonl = await RunAppAsync([.. limit, "--format=jsonl"]);
        var mermaid = await RunAppAsync([.. limit, "--format=mermaid"]);

        foreach (var result in new[] { count, table, jsonl, mermaid })
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
        }

        Assert.Equal("2", count.Output.Trim());
        Assert.Equal(3, NonEmptyLineCount(table.Output));
        Assert.Equal(2, NonEmptyLineCount(jsonl.Output));
        Assert.Equal(
            2,
            mermaid.Output.Split('\n').Count(static line =>
                line.Contains("-->", StringComparison.Ordinal)
                || line.Contains("-.->", StringComparison.Ordinal)));

        static int NonEmptyLineCount(string value) =>
            value.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [Fact]
    public async Task Depends_TypeRowsPreserveTypedWindowFailure()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Int128",
            "--rows", "999..999", "--count", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires row 999",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Relationships has",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Depends_TypeLimitRequiresPositiveSemanticCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Int128",
            "-n", "0", "--count", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "positive whole number",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Depends_TypeJsonRetainsQueryAndSelectedRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Int128",
            "--format=json", "--rows", "1..1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Single(
            document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
                .EnumerateArray());
        Assert.Equal(
            "System.Int128",
            document.RootElement.GetProperty("queryResult").GetProperty("dependency")
                .GetProperty("matchedType").GetString());
        Assert.True(document.RootElement.GetProperty("queryResult").GetProperty("dependency")
            .GetProperty("relationships").GetArrayLength() > 1);
    }

    [Fact]
    public async Task Depends_TypeQueryFiltersOrdersAndRanksRelationshipRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Int128",
            "--where", "Kind=Interface",
            "--order-by", "Target desc",
            "--top", "1",
            "--format=jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        JsonElement relationship =
            JsonDocument.Parse(output).RootElement;
        Assert.Equal(
            "interface",
            relationship.GetProperty("relationship").GetString());
        Assert.Equal(
            "System.Numerics.IUnaryPlusOperators<System.Int128, System.Int128>",
            relationship.GetProperty("target").GetString());
    }

    [Fact]
    public async Task Depends_TypeLegacyRowsRetainOrderedStagePosition()
    {
        var windowThenTop = await RunAppAsync(
            "depends", "System.Int128",
            "--where", "Kind=Interface",
            "--order-by", "Target desc",
            "--rows", "2..3",
            "--top", "1",
            "--format=jsonl", "--tips", "q");
        var topThenWindow = await RunAppAsync(
            "depends", "System.Int128",
            "--where", "Kind=Interface",
            "--order-by", "Target desc",
            "--top", "1",
            "--rows", "2..3",
            "--format=jsonl", "--tips", "q");
        var legacyTail = await RunAppAsync(
            "depends", "System.Int128",
            "--rows", "1", "--tail",
            "--format=jsonl", "--tips", "q");

        Assert.Equal(0, windowThenTop.Exit);
        Assert.Empty(windowThenTop.Error);
        Assert.Single(
            windowThenTop.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(1, topThenWindow.Exit);
        Assert.Empty(topThenWindow.Output);
        Assert.Contains(
            "row selection stage 2",
            topThenWindow.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Relationships has 1",
            topThenWindow.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, legacyTail.Exit);
        Assert.Empty(legacyTail.Error);
        Assert.Single(
            legacyTail.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Depends_RootOnlyTypeJsonRetainsTheSelectedType()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.IDisposable",
            "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement dependency = document.RootElement.GetProperty("queryResult").GetProperty("dependency");
        Assert.Equal(
            "System.IDisposable",
            dependency.GetProperty("matchedType").GetString());
        Assert.Empty(dependency.GetProperty("tree").EnumerateArray());
        Assert.Empty(
            document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
                .EnumerateArray());
    }

    [Fact]
    public async Task Depends_TreeOverridesEnvironmentJson()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            var (exit, output, error) = await RunAppAsync(
                "depends", "System.Int128",
                "--tree", "--rows", "1", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("System.Int128", output);
            Assert.Contains("└", output);
            Assert.DoesNotContain("[{", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Fact]
    public async Task Depends_LibraryGraphJsonHonorsCompact()
    {
        var pretty = await RunAppAsync(
            "depends", "--library", TestAssemblyPath,
            "--format=json", "--tips", "q");
        var compact = await RunAppAsync(
            "depends", "--library", TestAssemblyPath,
            "--format=json", "--compact", "--tips", "q");

        Assert.Equal(0, pretty.Exit);
        Assert.Equal(0, compact.Exit);
        Assert.Empty(pretty.Error);
        Assert.Empty(compact.Error);
        Assert.True(
            pretty.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length > 1);
        Assert.Single(
            compact.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using JsonDocument document =
            JsonDocument.Parse(compact.Output);
        JsonElement hierarchy = document.RootElement.GetProperty(
            "dependency_hierarchy");
        Assert.Equal(
            JsonValueKind.Object,
            document.RootElement.ValueKind);
        Assert.True(
            hierarchy.GetProperty("occurrences").GetArrayLength() > 0);
        JsonElement evidence = hierarchy
            .GetProperty("occurrences")[0]
            .GetProperty("evidence_identity");
        Assert.Equal(
            "assembly-reference",
            evidence.GetProperty("kind").GetString());
        Assert.Equal(
            "assembly",
            evidence.GetProperty("assembly_reference")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task Depends_LibraryNetmoduleRetainsTypedRootWhenEmpty()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{Guid.NewGuid():N}.netmodule");
        WriteNetmodule(path);
        try
        {
            var graph = await RunAppAsync(
                "depends", "--library", path,
                "--format=json", "--compact", "--tips", "q");
            var count = await RunAppAsync(
                "depends", "--library", path,
                "--count", "--tips", "q");

            Assert.Equal(0, graph.Exit);
            Assert.Empty(graph.Error);
            Assert.Equal(0, count.Exit);
            Assert.Empty(count.Error);
            Assert.Equal("0", count.Output.Trim());
            using JsonDocument document =
                JsonDocument.Parse(graph.Output);
            JsonElement root = Assert.Single(
                document.RootElement.GetProperty("dependency_hierarchy")
                    .GetProperty("roots")
                    .EnumerateArray());
            JsonElement library = root.GetProperty("identity")
                .GetProperty("library");
            Assert.Equal(
                "module",
                library.GetProperty("kind").GetString());
            Assert.Equal(
                Path.GetFileName(path),
                library.GetProperty("name").GetString());
            Assert.NotEqual(
                Guid.Empty,
                library.GetProperty("module_version_id")
                    .GetGuid());
            Assert.Empty(
                document.RootElement.GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")
                    .EnumerateArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Depends_LibraryNetmoduleCanOwnReferenceEdges()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{Guid.NewGuid():N}.netmodule");
        WriteNetmodule(path, "System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "depends", "--library", path,
                "--format=json", "--compact", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement edge =
                document.RootElement.GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")
                    .EnumerateArray()
                    .First();
            Assert.Equal(
                "module",
                edge.GetProperty("source_identity")
                    .GetProperty("library")
                    .GetProperty("kind")
                    .GetString());
            Assert.Equal(
                "assembly",
                edge.GetProperty("target_identity")
                    .GetProperty("library")
                    .GetProperty("kind")
                    .GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RelationshipCommands_Count_RendersOnlyCount()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "IDisposable", "--platform", "--count");
        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "--count");
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Int128", "--count");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsError);
        Assert.True(int.Parse(implementsOutput.Trim()) > 0);

        Assert.Equal(0, extensionsExit);
        Assert.Empty(extensionsError);
        Assert.True(int.Parse(extensionsOutput.Trim()) > 0);

        Assert.Equal(0, dependsExit);
        Assert.Empty(dependsError);
        Assert.True(int.Parse(dependsOutput.Trim()) > 0);
    }

    [Fact]
    public async Task Depends_Count_WithEmptyDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Object", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task Depends_Count_WithEmptyLibraryDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "--library", "System.Private.CoreLib", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_BarePlatformBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "--platform", "IEnumerable<T>", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim()) > 0);
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_IsAccepted()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_FormatsSourceAsFrameworkAtVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "-v:n", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("runtime@", output);
        Assert.DoesNotContain("@runtime", output);
    }

    [Fact]
    public async Task Extensions_FailedAssemblyWarnsAndPreservesSuccessfulResults()
    {
        var corruptPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-corrupt-{Guid.NewGuid():N}.dll");
        try
        {
            await File.WriteAllTextAsync(
                corruptPath,
                "not a PE image",
                TestContext.Current.CancellationToken);

            var (exit, output, error) = await RunAppAsync(
                "extensions",
                "MetadataReader",
                "--library", typeof(MetadataFindings).Assembly.Location,
                "--library", corruptPath,
                "--count",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.True(int.Parse(output.Trim()) > 0);
            Assert.Contains(
                "Warning: Extension member inspection failed",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    [Fact]
    public async Task Extensions_JsonlAfterPackage_RendersJsonlAndDoesNotWarnAboutPackageFlag()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq",
            "--format=jsonl", "--rows", "1..2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("{", output.TrimStart());
        Assert.DoesNotContain("# Extension Methods", output);
    }

    [Fact]
    public async Task Implements_TypeColumn_RendersGenericNameAsCodeSpan()
    {
        var (exit, output, error) = await RunAppAsync(
            "implements", "System.Text.Json.Serialization.JsonConverter",
            "--platform", "System.Text.Json", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("`System.Text.Json.Serialization.JsonConverter<T>`", output);
        Assert.DoesNotContain("JsonConverter&#96;1", output);
    }
}
