using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using System.IO.Compression;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task PackageWithoutAllLibraries_RemainsPackageScoped()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Package files",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains("# Test.LibraryFiles", result.Output);
            Assert.Contains("## Package files", result.Output);
            Assert.DoesNotContain("## Libraries", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_UsesSharedLibraryRendering()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var markdown = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown", "--tips", "q");
            var tsv = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--tsv", "--rows", "1",
                "--tips", "q");
            var table = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--table", "--rows", "1",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--jsonl", "--rows", "1",
                "--tips", "q");
            var projected = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--tsv", "--columns", "Name",
                "--rows", "1",
                "--tips", "q");
            var producerOnly = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--tsv", "--columns", "Library",
                "--rows", "1",
                "--tips", "q");
            var fieldOnly = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--tsv", "--columns", "Field",
                "--rows", "1",
                "--tips", "q");
            var valueOnly = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--tsv", "--columns", "Value",
                "--rows", "1",
                "--tips", "q");
            var mixedProjection = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--tsv",
                "--fields", "Name",
                "--columns", "Library,Value",
                "--tips", "q");

            Assert.Equal(0, markdown.Exit);
            Assert.StartsWith(
                "# Test.LibraryFiles 1.0.0\n\n"
                + "## Library Info: lib/net10.0/Latest.One.dll (net10.0)\n",
                markdown.Output);
            Assert.DoesNotContain("## Libraries", markdown.Output);
            Assert.Contains(
                "## Library Info: lib/net10.0/Latest.One.dll (net10.0)",
                markdown.Output);
            Assert.Contains(
                "## Library Info: lib/net10.0/Latest.Two.dll (net10.0)",
                markdown.Output);
            Assert.DoesNotContain("Older.dll", markdown.Output);
            Assert.Empty(markdown.Error);

            Assert.Equal(0, tsv.Exit);
            Assert.Empty(tsv.Error);
            Assert.Single(
                tsv.Output.ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith(
                    "package\tpackage_version\tlibrary\ttfm\t",
                    StringComparison.Ordinal));
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                tsv.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                tsv.Output);

            Assert.Equal(0, table.Exit);
            Assert.Empty(table.Error);
            Assert.StartsWith("package", table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                table.Output);

            Assert.Equal(0, jsonl.Exit);
            Assert.Empty(jsonl.Error);
            Assert.Contains(
                "\"library\":\"lib/net10.0/Latest.One.dll\"",
                jsonl.Output);
            Assert.Contains(
                "\"library\":\"lib/net10.0/Latest.Two.dll\"",
                jsonl.Output);

            Assert.Equal(0, projected.Exit);
            Assert.Empty(projected.Error);
            Assert.StartsWith(
                "package\tpackage_version\tlibrary\ttfm\tname\n",
                projected.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                projected.Output);

            Assert.Equal(0, producerOnly.Exit);
            Assert.Empty(producerOnly.Error);
            string[] producerLines = producerOnly.Output
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(
                "package\tpackage_version\tlibrary\ttfm",
                producerLines[0]);
            Assert.Contains(
                producerLines,
                line => line.Contains(
                    "lib/net10.0/Latest.One.dll",
                    StringComparison.Ordinal));
            Assert.Contains(
                producerLines,
                line => line.Contains(
                    "lib/net10.0/Latest.Two.dll",
                    StringComparison.Ordinal));

            Assert.Equal(0, fieldOnly.Exit);
            Assert.Empty(fieldOnly.Error);
            Assert.StartsWith(
                "package\tpackage_version\tlibrary\ttfm\tfield\n",
                fieldOnly.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.One.dll\tnet10.0\t",
                fieldOnly.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.Two.dll\tnet10.0\t",
                fieldOnly.Output);

            Assert.Equal(0, valueOnly.Exit);
            Assert.Empty(valueOnly.Error);
            Assert.StartsWith(
                "package\tpackage_version\tlibrary\ttfm\tvalue\n",
                valueOnly.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.One.dll\tnet10.0\t",
                valueOnly.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.Two.dll\tnet10.0\t",
                valueOnly.Output);

            Assert.Equal(0, mixedProjection.Exit);
            Assert.Empty(mixedProjection.Error);
            Assert.StartsWith(
                "package\tpackage_version\tlibrary\ttfm\tvalue\n",
                mixedProjection.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.One.dll\tnet10.0\t",
                mixedProjection.Output);
            Assert.Contains(
                "\tlib/net10.0/Latest.Two.dll\tnet10.0\t",
                mixedProjection.Output);
            Assert.All(
                mixedProjection.Output
                    .ReplaceLineEndings("\n")
                    .Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries),
                line => Assert.Equal(5, line.Split('\t').Length));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RowFormats_WindowPerLibraryLikeMarkdownCount()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var count = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--rows", "2",
                "--count",
                "--tips", "q");
            var markdown = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--rows", "2",
                "--markdown",
                "--tips", "q");
            var tsv = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--rows", "2",
                "--tsv",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--rows", "2",
                "--jsonl",
                "--tips", "q");
            var json = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--rows", "2",
                "--json",
                "--tips", "q");

            Assert.Equal(0, count.Exit);
            Assert.Equal(0, markdown.Exit);
            Assert.Equal(0, tsv.Exit);
            Assert.Equal(0, jsonl.Exit);
            Assert.Equal(0, json.Exit);
            Assert.Equal(4, int.Parse(
                count.Output.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(
                [2, 2],
                SplitOutputLines(tsv.Output)
                    .Skip(1)
                    .GroupBy(row => row.Split('\t')[2])
                    .Select(group => group.Count())
                    .Order()
                    .ToArray());
            Assert.Equal(
                4,
                SplitOutputLines(markdown.Output)
                    .Count(line =>
                        line.StartsWith(
                            "| ",
                            StringComparison.Ordinal))
                    - 4);

            using var first = System.Text.Json.JsonDocument.Parse(
                SplitOutputLines(jsonl.Output)[0]);
            Assert.Equal(
                "Test.LibraryFiles",
                first.RootElement
                    .GetProperty("package")
                    .GetString());
            Assert.Equal(
                "net10.0",
                first.RootElement
                    .GetProperty("tfm")
                    .GetString());
            Assert.Equal(4, SplitOutputLines(jsonl.Output).Length);
            using (var document =
                   System.Text.Json.JsonDocument.Parse(json.Output))
            {
                Assert.Equal(
                    "Test.LibraryFiles",
                    document.RootElement
                        .GetProperty("package")
                        .GetString());
                Assert.Equal(
                    "1.0.0",
                    document.RootElement
                        .GetProperty("package_version")
                        .GetString());
                var section = Assert.Single(
                    document.RootElement
                        .GetProperty("sections")
                        .EnumerateArray());
                Assert.Equal(
                    SectionNames.LibraryInfo,
                    section.GetProperty("name").GetString());
                var rows = section
                    .GetProperty("rows")
                    .EnumerateArray()
                    .ToArray();
                Assert.Equal(4, rows.Length);
                Assert.Equal(
                    "net10.0",
                    rows[0].GetProperty("tfm").GetString());
            }
            Assert.Empty(count.Error);
            Assert.Empty(markdown.Error);
            Assert.Empty(tsv.Error);
            Assert.Empty(jsonl.Error);
            Assert.Empty(json.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection()
    {
        var (packagePath, tempDir) =
            CreateLocalSwitchLibraryPackage();
        try
        {
            var count = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Switches",
                "--rows", "1",
                "--count",
                "--tips", "q");
            var markdown = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Switches",
                "--rows", "1",
                "--markdown",
                "--tips", "q");
            var plainText = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Switches",
                "--rows", "1",
                "--plaintext",
                "--tips", "q");
            var tsv = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Switches",
                "--rows", "1",
                "--tsv",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Switches",
                "--rows", "1",
                "--jsonl",
                "--tips", "q");
            var json = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Switches",
                "--rows", "1",
                "--json",
                "--tips", "q");

            Assert.Equal(0, count.Exit);
            Assert.Equal(0, markdown.Exit);
            Assert.Equal(0, plainText.Exit);
            Assert.Equal(0, tsv.Exit);
            Assert.Equal(0, jsonl.Exit);
            Assert.Equal(0, json.Exit);
            Assert.Equal(
                1,
                int.Parse(
                    count.Output.Trim(),
                    System.Globalization.CultureInfo.InvariantCulture));
            Assert.Single(
                SplitOutputLines(tsv.Output).Skip(1));
            Assert.Contains(
                "## Switches\n",
                markdown.Output);
            Assert.DoesNotContain(
                "## Switches:",
                markdown.Output);
            Assert.Contains(
                "Switches",
                plainText.Output);
            Assert.Equal(
                1,
                plainText.Output.Split(
                    "DotnetInspector.Fixtures.AppContextOnly",
                    StringSplitOptions.None).Length - 1);
            Assert.Equal(
                1,
                SplitOutputLines(markdown.Output)
                    .Count(line =>
                        line.StartsWith(
                            "| ",
                            StringComparison.Ordinal))
                    - 2);
            string[] markdownRow =
                SplitOutputLines(markdown.Output)
                    .Where(line =>
                        line.StartsWith(
                            "| ",
                            StringComparison.Ordinal))
                    .Skip(2)
                    .Single()
                    .Split(
                        '|',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(cell =>
                        System.Net.WebUtility.HtmlDecode(
                            cell.Trim().Trim('`')))
                    .ToArray();
            string[] tsvRow =
                SplitOutputLines(tsv.Output)
                    .Skip(1)
                    .Single()
                    .Split('\t')
                    .Skip(2)
                    .ToArray();
            Assert.Equal(markdownRow, tsvRow);
            using var jsonlRow =
                System.Text.Json.JsonDocument.Parse(
                    Assert.Single(
                        SplitOutputLines(jsonl.Output)));
            using var jsonDocument =
                System.Text.Json.JsonDocument.Parse(json.Output);
            Assert.Equal(
                "Test.MultiLib",
                jsonDocument.RootElement
                    .GetProperty("package")
                    .GetString());
            Assert.Equal(
                "1.0.0",
                jsonDocument.RootElement
                    .GetProperty("package_version")
                    .GetString());
            var jsonSection = Assert.Single(
                jsonDocument.RootElement
                    .GetProperty("sections")
                    .EnumerateArray());
            Assert.Equal(
                SectionNames.Switches,
                jsonSection.GetProperty("name").GetString());
            var jsonRow = Assert.Single(
                jsonSection
                    .GetProperty("rows")
                    .EnumerateArray());
            var jsonlProperties =
                jsonlRow.RootElement
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => property.Value.GetString());
            var jsonProperties =
                jsonRow
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => property.Value.GetString());
            Assert.Equal(jsonlProperties.Count, jsonProperties.Count);
            foreach (var property in jsonlProperties)
            {
                Assert.True(
                    jsonProperties.TryGetValue(
                        property.Key,
                        out string? value));
                Assert.Equal(property.Value, value);
            }
            Assert.Empty(count.Error);
            Assert.Empty(markdown.Error);
            Assert.Empty(plainText.Error);
            Assert.Empty(tsv.Error);
            Assert.Empty(jsonl.Error);
            Assert.Empty(json.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("--dependencies")]
    public async Task PackageAllLibraries_RejectsExactLibraryOperations(
        string operation)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                operation,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "requires one exact Library",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_ReferenceHierarchyRequiresExactLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var discovery = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-D",
                "--schema",
                "--tips", "q");
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", SectionNames.ReferenceHierarchy,
                "--count",
                "--tips", "q");

            Assert.Equal(0, discovery.Exit);
            Assert.DoesNotContain(
                SectionNames.ReferenceHierarchy,
                discovery.Output);
            Assert.Empty(discovery.Error);
            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Reference Hierarchy requires one exact library",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_ReferenceHierarchyIsAbsentFromDiscovery()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-D", SectionNames.ReferenceHierarchy,
                "--schema",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Section 'Reference Hierarchy' not found",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageLibrary_ReferenceHierarchyUsesSharedLibraryRendering()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--library", "Test.Primary.dll",
                "-S", SectionNames.ReferenceHierarchy,
                "--tree",
                "--markdown",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains(
                "DotnetInspect.Cli.Tests",
                result.Output);
            Assert.NotEmpty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_HonorsOutputDestination()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string outputPath = Path.Combine(tempDir, "aggregate.md");
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown",
                "--out", outputPath,
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Empty(result.Error);
            Assert.Contains(
                "# Test.LibraryFiles",
                File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_AllTfmsRejectsPartialCompileSelection()
    {
        var (packagePath, tempDir) =
            CreatePackageWithHealthyAndEmptyReferenceGroups();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");
            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "declares an empty compile group for TFM 'net10.0'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_AllTfmsRetainsNestedCompileLibraries()
    {
        var (packagePath, tempDir) =
            CreateLocalNestedMultiTfmLibraryPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");
            var tsv = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--tsv",
                "--rows", "1",
                "--tips", "q");
            var table = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--table",
                "--rows", "1",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--jsonl",
                "--rows", "1",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "lib/net8.0/Direct.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "lib/net8.0/x64/Nested.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "lib/net10.0/x64/Nested.dll (net10.0)",
                result.Output);
            Assert.Empty(result.Error);
            Assert.Equal(0, tsv.Exit);
            Assert.StartsWith(
                "package\tpackage_version\tlibrary\ttfm\tfield\tvalue\n",
                tsv.Output);
            Assert.Contains(
                "\tlib/net8.0/Direct.dll\tnet8.0\t",
                tsv.Output);
            Assert.Contains(
                "\tlib/net10.0/x64/Nested.dll\tnet10.0\t",
                tsv.Output);
            Assert.Empty(tsv.Error);
            Assert.Equal(0, table.Exit);
            Assert.StartsWith("package", table.Output);
            Assert.Contains("net8.0", table.Output);
            Assert.Contains("net10.0", table.Output);
            Assert.Empty(table.Error);
            Assert.Equal(0, jsonl.Exit);
            Assert.Contains("\"tfm\":\"net8.0\"", jsonl.Output);
            Assert.Contains("\"tfm\":\"net10.0\"", jsonl.Output);
            Assert.Empty(jsonl.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_DescriptorlessParticipantIsVisible()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDescriptorlessParticipant();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains(
                "## Library Info: lib/net8.0/Readable.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "Could not select library descriptor for "
                + "'lib/net8.0/Text.dll'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_CountRejectsIncompleteParticipants()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDescriptorlessParticipant();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--count",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Count output is unavailable because one or more "
                + "selected package Libraries could not be inspected.",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_CountRejectsSelectedInspectionFailure()
    {
        var options = new LibraryOptions
        {
            Count = true,
            IncludeSections = [SectionNames.ArrayPoolEscapes],
        };
        var inspection = FailedResourceTriageInspection();
        bool rejected = false;

        var (output, error) = await ConsoleCapture.RunAsync(
            () => rejected =
                LibraryCommand.RejectIncompleteAggregateCount(
                    [inspection],
                    options,
                    LibrarySections.CreatePipeline(),
                    participantIncomplete: false));

        Assert.True(rejected);
        Assert.Equal(
            1,
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                LibrarySections.CreatePipeline(),
                inspection));
        Assert.Empty(output);
        Assert.Contains(
            "Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            error);
        Assert.Contains(
            "Count output is unavailable because one or more "
            + "selected package Libraries could not be inspected.",
            error);
    }

    [Fact]
    public async Task PackageAllLibraries_SelectedFailureSurvivesHealthyRows()
    {
        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.LibraryInfo],
        };
        var inspection = FailedClassifiedMethodsInspection();
        var pipeline = LibrarySections.CreatePipeline();

        Assert.DoesNotContain(
            SectionNames.LibraryInfo,
            pipeline.GetEmptySections(
                inspection,
                options.Verbosity,
                options.IncludeSections).Empty);

        var (output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.WarnEmptySections(
                [inspection],
                options,
                pipeline));

        Assert.Empty(output);
        Assert.Contains(
            "Classified Methods inspection failed "
            + "(Classified method): method scan failed",
            error);
        Assert.Equal(
            1,
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                pipeline,
                inspection));
    }

    [Fact]
    public async Task PackageAllLibraries_BareCountRejectsSelectedInspectionFailure()
    {
        var options = new LibraryOptions
        {
            Count = true,
            FixedOverview = true,
        };
        var inspection = FailedClassifiedMethodsInspection();
        bool rejected = false;

        var (output, error) = await ConsoleCapture.RunAsync(
            () => rejected =
                LibraryCommand.RejectIncompleteAggregateCount(
                    [inspection],
                    options,
                    LibrarySections.CreatePipeline(),
                    participantIncomplete: false));

        Assert.True(rejected);
        Assert.Equal(
            1,
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                LibrarySections.CreatePipeline(),
                inspection));
        Assert.Empty(output);
        Assert.Contains(
            "Classified Methods inspection failed "
            + "(Classified method): method scan failed",
            error);
        Assert.Contains(
            "Count output is unavailable because one or more "
            + "selected package Libraries could not be inspected.",
            error);
    }

    [Fact]
    public async Task PackageAllLibraries_IdentifierAuditPreservesHealthyResultsOnFailure()
    {
        var (packagePath, tempDir) =
            CreateIdentifierConfusionReferencePackage();
        try
        {
            string packageRoot = Path.Combine(tempDir, "content");
            string libraryDirectory =
                Path.Combine(packageRoot, "lib", "net8.0");
            WriteReferenceFixtureAssembly(
                Path.Combine(libraryDirectory, "A.Valid.dll"),
                "\u0405ystem.Valid");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Bridge.dll"),
                "not a managed assembly");
            File.Delete(packagePath);
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", SectionNames.IdentifierConfusion,
                "--tips", "q");
            var count = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", SectionNames.IdentifierConfusion,
                "--count",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains("U+0405→S", result.Output);
            Assert.Contains(
                "Could not select library descriptor for "
                + "'lib/net8.0/Bridge.dll'",
                result.Error);
            Assert.Contains(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata",
                result.Error);
            Assert.Equal(1, count.Exit);
            Assert.Empty(count.Output);
            Assert.Contains(
                "Identifier audit failed for 'lib/net8.0/Root.dll'",
                count.Error);
            Assert.Contains(
                "Count output is unavailable because one or more "
                + "selected package Libraries could not be inspected.",
                count.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (string PackagePath, string TempDir)
        CreateLocalNestedMultiTfmLibraryPackage()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        foreach (string framework in new[] { "net8.0", "net10.0" })
        {
            string libDir =
                Path.Combine(packageRoot, "lib", framework);
            string nestedDir = Path.Combine(libDir, "x64");
            Directory.CreateDirectory(nestedDir);
            File.Copy(
                TestAssemblyPath,
                Path.Combine(nestedDir, "Nested.dll"));
            if (framework == "net8.0")
            {
                File.Copy(
                    TestAssemblyPath,
                    Path.Combine(libDir, "Direct.dll"));
            }
        }

        string packagePath =
            Path.Combine(tempDir, "Nested.Tfm.All.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithHealthyAndEmptyReferenceGroups()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Healthy.dll"));
        string refDir = Path.Combine(packageRoot, "ref", "net10.0");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(Path.Combine(refDir, "_._"), "");

        string packagePath = Path.Combine(
            tempDir,
            "Partial.Compile.Selection.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithDescriptorlessParticipant()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Readable.dll"));
        File.WriteAllText(
            Path.Combine(libDir, "Text.dll"),
            "café",
            new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));

        string packagePath = Path.Combine(
            tempDir,
            "Descriptorless.Participant.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }
}
