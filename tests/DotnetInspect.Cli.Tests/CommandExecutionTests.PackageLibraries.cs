using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Fact]
    public async Task AllLibraries_EmptyMatch_CountsZeroRatherThanReturningSilently()
    {
        // An empty match short-circuits ahead of the render path, which is exactly where a
        // projection goes missing without an empty-result probe to catch it. The section has to
        // be one this package genuinely has no rows for, not an unknown name: an unknown -S is
        // rejected before the render path is ever reached, so it would prove nothing here.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--library", "-S", "Non-normalized Paths",
            "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(0, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AllLibraries_CategoryCount_RendersCountMap()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "@Library",
                "--count", "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement.EnumerateArray().ToArray();
            Assert.True(rows.Length > 1);
            Assert.Contains(
                rows,
                row => row.GetProperty("section").GetString() == "Library Info");
            Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AllLibraries_MetadataSection_RendersAndCountsTypedRows()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Metadata: TypeRef",
                "--count", "--tips", "q");
            var (singleExit, singleOutput, singleError) = await RunAppAsync(
                "package", packagePath, "--library", "Layout.dll", "-S", "Metadata: TypeRef",
                "--count", "--tips", "q");
            var (renderExit, rendered, renderError) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Metadata: TypeRef",
                "--markdown", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Equal(0, singleExit);
            Assert.Equal(0, renderExit);
            Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
            Assert.DoesNotContain("Error:", singleError, StringComparison.Ordinal);
            Assert.DoesNotContain("Error:", renderError, StringComparison.Ordinal);
            Assert.Equal(singleOutput.Trim(), output.Trim());
            Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
            Assert.Contains("## Metadata: TypeRef (", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AllLibraries_BareSelectCount_PreservesFixedOverview()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S",
                "--count", "--json", "--tips", "q");
            var (renderExit, rendered, renderError) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "--tips", "q");
            var (treeExit, treeOutput, treeError) = await RunAppAsync(
                "package", packagePath, "--library", "-S",
                "--count", "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Equal(0, renderExit);
            Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
            Assert.DoesNotContain("Error:", renderError, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(output);
            var sections = document.RootElement
                .EnumerateArray()
                .Select(row => row.GetProperty("section").GetString())
                .ToArray();
            var renderedSections = rendered
                .Split('\n')
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line[3..].Split(" (", 2, StringSplitOptions.None)[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.Equal(
                sections.Order(StringComparer.OrdinalIgnoreCase),
                renderedSections.Order(StringComparer.OrdinalIgnoreCase));

            Assert.Equal(1, treeExit);
            Assert.Empty(treeOutput);
            Assert.Contains("exactly one selected shape", treeError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibrariesIdentifierConfusionAudit_CollectsTransitiveReferences()
    {
        var (packagePath, tempDir) = CreateIdentifierConfusionReferencePackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("IdentifierConfusionReferenceClosure[", output);
            Assert.Contains("U+03BF→O", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibrariesIdentifierConfusionAudit_PreservesHealthyResultsOnTraversalFailure()
    {
        var (packagePath, tempDir) = CreateIdentifierConfusionReferencePackage();
        try
        {
            string packageRoot = Path.Combine(tempDir, "content");
            string libraryDirectory = Path.Combine(packageRoot, "lib", "net8.0");
            WriteReferenceFixtureAssembly(
                Path.Combine(libraryDirectory, "A.Valid.dll"),
                "\u0405ystem.Valid");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Bridge.dll"),
                "not a managed assembly");
            File.Delete(packagePath);
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Contains("U+0405→S", output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                error);
            Assert.DoesNotContain("Bridge", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibrariesIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-decode-package-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libraryDirectory);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(libraryDirectory, "A.Valid.dll"),
                "\u0405ystem.Valid");
            WriteMalformedAssemblyReferenceNameAssembly(
                Path.Combine(libraryDirectory, "Root.dll"));
            string packagePath = Path.Combine(
                tempDir,
                "Identifier.Reference.Decode.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Contains("U+0405→S", output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                error);
            Assert.DoesNotContain("System.Runtime", error);

            var signals = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.Signals,
                "--tips",
                "q");

            Assert.Equal(1, signals.Exit);
            Assert.Contains(
                "| Identity | Identifier confusion | Unavailable "
                + "| invalid assembly metadata |",
                signals.Output);
            Assert.DoesNotContain(
                "| Identity | Identifier confusion | None |",
                signals.Output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                signals.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// The aggregate Library sections pool rows across libraries and pick their
    /// columns from the pooled data, so they are declared as a runtime-column
    /// <c>MarkoutTable</c> rather than appended as Markdown text. This is the gate for that
    /// routing on the real command path: <c>--rows</c> must window the aggregate table even
    /// though nothing post-processes the rendered document any more.
    ///
    /// The separator assertion is the observable signature of the routing. markout sizes a
    /// separator to its header text; the hand-built table this replaced always emitted a fixed
    /// <c>---</c>, so a revert to string building would restore <c>| --- | --- | --- |</c> and
    /// fail here.
    /// </summary>
    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatedSection_WindowsRowsAtTheWriterSeam()
    {
        var (exit, all, _) = await RunAppAsync(
            "package", "System.Text.Json", "--library", "-S", "Switches");
        var (windowedExit, windowed, _) = await RunAppAsync(
            "package", "System.Text.Json", "--library", "-S", "Switches", "--rows", "2");

        Assert.Equal(0, exit);
        Assert.Equal(0, windowedExit);
        Assert.Contains("## Switches", all, StringComparison.Ordinal);
        Assert.Contains("| Library | TFM | Kind | Switch | API |", all, StringComparison.Ordinal);
        Assert.Contains("| ---- | ------ | --- |", all, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', all);
        Assert.DoesNotContain('\r', windowed);

        static int DataRows(string output) =>
            output.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal))
            - 2; // header and separator

        Assert.True(DataRows(all) > 2, $"expected an unwindowed table wider than the window, got {DataRows(all)} rows");
        Assert.Equal(2, DataRows(windowed));
    }

    /// <summary>
    /// The aggregate Library document is assembled by hand, and every block boundary is
    /// produced by one helper that appends a block plus its trailing blank line. That is easy to
    /// break silently in either direction: #3963 doubled the blank before every section on Windows,
    /// and a separator rewritten to emit one newline instead of two would run the sections
    /// together. Neither produces a carriage return, so neither is visible to a line-ending
    /// assertion.
    /// <para>
    /// The invariant asserted here is therefore two-sided -- each <c>##</c> heading is preceded by
    /// exactly one blank line, so a missing blank and an extra blank both fail. The two ends of the
    /// document are covered separately, because a heading loop cannot see them: it opens with the
    /// package's title heading followed by one blank line, and it carries no trailing whitespace,
    /// so the assembling <c>TrimEnd</c> cannot be dropped. All of these have teeth on every
    /// platform. <see cref="PackageCommand_AllLibraries_MarkdownUsesLfThroughout"/> covers the
    /// complementary property, the line ending itself, over the per-library path; that assertion
    /// has teeth only on the nightly <c>platform-test (win-x64)</c> leg.
    /// </para>
    /// <para>
    /// Both producers are covered, because they fail differently. The aggregated path (#3951, which
    /// the per-library test does not reach) is exercised by <c>-S Switches,References</c> against
    /// a real package. The per-library path appears there only as the document's last block, where
    /// a trailing newline it emitted would be absorbed by the assembling <c>TrimEnd</c> and go
    /// unnoticed; it is therefore also run against a local two-library package, so that a boundary
    /// between two per-library blocks is measured directly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatedSection_SeparatesBlocksWithOneBlankLine()
    {
        var (exit, aggregated, _) = await RunAppAsync(
            "package", "System.Text.Json", "--library", "-S", "Switches,References");

        Assert.Equal(0, exit);
        Assert.Contains("## Switches", aggregated, StringComparison.Ordinal);
        AssertBlocksSeparatedByOneBlankLine(aggregated, "# system.text.json");

        // The per-library producer, which the run above reaches only as the document's last block,
        // where a trailing newline it emitted would be absorbed by the assembling TrimEnd. This
        // package ships two libraries for the selected framework, so the boundary between two
        // per-library blocks is measured rather than inferred.
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (localExit, perLibrary, _) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, localExit);
            AssertBlocksSeparatedByOneBlankLine(perLibrary, "# test.libraryfiles");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_MarkdownUsesLfThroughout()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info");
            var (windowedExit, windowed, windowedError) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info", "--rows", "20");

            Assert.Equal(0, exit);
            Assert.Equal(0, windowedExit);
            Assert.DoesNotContain("Tip:", error);
            Assert.DoesNotContain("Tip:", windowedError);
            Assert.Contains('\n', output);
            Assert.Contains('\n', windowed);
            Assert.DoesNotContain('\r', output);
            Assert.DoesNotContain('\r', windowed);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--rows", "1")]
    public async Task
        PackageCommand_ExactLibraryInfoRejectsSemanticTerminalBeforeAcquisition(
            params string[] terminal)
    {
        string package =
            $"Definitely.Missing.Package.{Guid.NewGuid():N}";
        string[] args =
        [
            "package",
            package,
            "--library",
            "Missing.dll",
            "-S",
            SectionNames.LibraryInfo,
            .. terminal,
            "--tips",
            "q",
        ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Section '{SectionNames.LibraryInfo}' is scalar",
            error);
        Assert.Contains(terminal[0], error);
        Assert.DoesNotContain(package, error);
    }

    [Fact]
    public async Task
        PackageCommand_ExactLibraryFixedOverviewRejectsCountBeforeAcquisition()
    {
        string package =
            $"Definitely.Missing.Package.{Guid.NewGuid():N}";

        var (exit, output, error) = await RunAppAsync(
            "package",
            package,
            "--library",
            "Missing.dll",
            "-S",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Section '{SectionNames.LibraryInfo}' is scalar",
            error);
        Assert.Contains("--count", error);
        Assert.DoesNotContain(package, error);
    }

    [Fact]
    public async Task
        PackageCommand_AllLibraries_UnsupportedArtifactRoleShapePreservesLegacyOutput()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Microsoft.TestPlatform.TestHost@17.14.1",
            "--library",
            "-S",
            "@Integrations",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains(
            "matched sections have no data across all libraries",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Microsoft.CSharp@4.7.0", "netcoreapp2.0")]
    [InlineData("System.Private.ServiceModel@4.10.3", "netstandard2.0")]
    public async Task
        PackageLibraryRoutes_SelectedEmptyCompileGroupDoNotFallback(
            string package,
            string selectedTfm)
    {
        string library =
            $"{package[..package.IndexOf('@')]}.dll";
        var aggregate = await RunAppAsync(
            "package",
            package,
            "--library",
            "-S",
            "Library Info",
            "--tips",
            "q");
        var packageExact = await RunAppAsync(
            "package",
            package,
            "--library",
            library,
            "-S",
            "Library Info",
            "--tips",
            "q");
        var libraryExact = await RunAppAsync(
            "library",
            library,
            "--package",
            package,
            "-S",
            "Library Info",
            "--tips",
            "q");

        foreach (var (exit, output, error) in
            new[] { aggregate, packageExact, libraryExact })
        {
            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                $"selected compile group for TFM '{selectedTfm}'",
                error,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Package_AllLibraries_RejectsTree()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "--tree", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Library aggregate inspection cannot be combined with --tree",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_AllLibraries_ReferenceHierarchyRequiresExactLibrary()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library",
                "-S", "Reference Hierarchy", "--count", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Reference Hierarchy requires one exact library",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--library", null)]
    [InlineData("--library=", null)]
    [InlineData("--library:", null)]
    [InlineData("--library", "")]
    [InlineData("--library", " ")]
    public async Task PackageCommand_LibraryFlag_BareUsesSingleLibraryAggregate(
        string libraryOption,
        string? libraryValue)
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            string[] libraryTokens = libraryValue is null
                ? [libraryOption]
                : [libraryOption, libraryValue];
            var (exit, output, error) = await RunAppAsync(
                ["package", packagePath, .. libraryTokens, "-S", "Library Info"]);
            var schema = await RunAppAsync(
                [
                    "package",
                    packagePath,
                    .. libraryTokens,
                    "-D",
                    "--schema",
                    "--tips",
                    "q",
                ]);

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary 1.0.0", output);
            Assert.Contains(
                "## Library Info (lib/net10.0/Test.Primary.dll)",
                output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
            Assert.Equal(0, schema.Exit);
            Assert.Contains("@Library", schema.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_SelectedReferencesCollectsDirectReferences()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "References");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary 1.0.0", output);
            Assert.Contains(
                "## References (lib/net10.0/Test.Primary.dll)",
                output);
            Assert.Contains("System.Runtime", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_ReferenceHierarchyTreeHonorsProjectionBoundaries()
    {
        var originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        var outputPath = Path.Combine(tempDir, "references.md");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "mermaid");
            var environment = await RunAppAsync(
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--tips", "q");
            var explicitMarkdown = await RunAppAsync(
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--markdown", "--tips", "q");
            var bare = await RunAppAsync(
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--bare", "--markdown", "--tips", "q");
            var file = await RunAppAsync(
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--markdown",
                "--out", outputPath, "--tips", "q");
            var noHeader = await RunAppAsync(
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--no-header",
                "--markdown", "--tips", "q");

            Assert.Equal(1, environment.Exit);
            Assert.Empty(environment.Output);
            Assert.Contains("--tree cannot be combined with row projections or non-Markdown formats", environment.Error);
            Assert.Equal(1, explicitMarkdown.Exit);
            Assert.NotEmpty(explicitMarkdown.Error);
            Assert.Contains("DotnetInspect.Cli.Tests", explicitMarkdown.Output);
            Assert.Equal(1, bare.Exit);
            Assert.Empty(bare.Output);
            Assert.Contains(
                "--bare cannot be combined with --json, --jsonl, --tsv, --table, --markdown, --plaintext, or --mermaid.",
                bare.Error);
            Assert.Equal(1, file.Exit);
            Assert.Empty(file.Output);
            Assert.NotEmpty(file.Error);
            Assert.Contains("DotnetInspect.Cli.Tests", File.ReadAllText(outputPath));
            Assert.Equal(1, noHeader.Exit);
            Assert.Empty(noHeader.Output);
            Assert.Contains("--tree cannot be combined with row projections or non-Markdown formats", noHeader.Error);

            var windowed = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--markdown",
                "--lines", "-n", "2", "--tips", "q");
            var windowedFile = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath, "--library", "Test.Primary.dll",
                "-S", "Reference Hierarchy", "--tree", "--markdown",
                "--lines", "-n", "2", "--out", outputPath, "--tips", "q");

            Assert.Equal(1, windowed.Exit);
            Assert.Equal(2, windowed.Output.Count(character => character == '\n'));
            Assert.Equal(windowed.Exit, windowedFile.Exit);
            Assert.Equal(windowed.Error, windowedFile.Error);
            Assert.Empty(windowedFile.Output);
            string written = File.ReadAllText(outputPath);
            Assert.DoesNotContain('\r', written);
            Assert.Equal(windowed.Output.ReplaceLineEndings("\n"), written);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_ExplicitSelectsLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "Latest.Two.dll", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Latest.Two.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_BareUsesMultiLibraryAggregate()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.One.dll)",
                output);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.Two.dll)",
                output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        PackageCommand_LibraryFlag_UsesCompatibleCompileProjection()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "--tfm",
                "net11.0",
                "--library",
                "-S",
                "Library Info",
                "--tips",
                "q");
            var exact = await RunAppAsync(
                "library",
                "Latest.One.dll",
                "--package",
                packagePath,
                "--tfm",
                "net11.0",
                "-S",
                "Library Info",
                "--tips",
                "q");

            Assert.True(
                result.Exit == 0,
                $"Expected success.{Environment.NewLine}"
                    + $"Error: {result.Error}{Environment.NewLine}"
                    + $"Output: {result.Output}");
            Assert.Empty(result.Error);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.One.dll)",
                result.Output);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.Two.dll)",
                result.Output);
            Assert.DoesNotContain("lib/net8.0/Older.dll", result.Output);
            Assert.True(
                exact.Exit == 0,
                $"Expected success.{Environment.NewLine}"
                    + $"Error: {exact.Error}{Environment.NewLine}"
                    + $"Output: {exact.Output}");
            Assert.Empty(exact.Error);
            Assert.Contains("# Latest.One.dll", exact.Output);
            Assert.DoesNotContain("lib/net8.0/Older.dll", exact.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        PackageCommand_LibraryFlag_HonorsExplicitEmptyCompileGroup()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                archive.CreateEntry("ref/net11.0/_._");
            }

            var package = await RunAppAsync(
                "package",
                packagePath,
                "--tfm",
                "net11.0",
                "--library",
                "-S",
                "Library Info",
                "--tips",
                "q");
            var library = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "--tfm",
                "net11.0",
                "-S",
                "Library Info",
                "--tips",
                "q");

            Assert.Equal(1, package.Exit);
            Assert.Empty(package.Output);
            Assert.Contains(
                "selected compile group for TFM 'net11.0'",
                package.Error);
            Assert.Equal(package, library);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RejectsRemovedComputedPole()
    {
        // The embedded-library render path resolves -S against the same curated LibrarySections
        // pipeline, so removed computed poles are unresolvable there too.
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "@Hidden", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("not found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_PackageSource_DefaultsToAggregateAndExactNarrows()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var aggregate = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "-S",
                "Library Info",
                "--tips",
                "q");
            var exact = await RunAppAsync(
                "library",
                "Latest.Two.dll",
                "--package",
                packagePath,
                "-S",
                "Library Info",
                "--tips",
                "q");

            Assert.Equal(0, aggregate.Exit);
            Assert.Empty(aggregate.Error);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.One.dll)",
                aggregate.Output);
            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.Two.dll)",
                aggregate.Output);

            Assert.Equal(0, exact.Exit);
            Assert.Empty(exact.Error);
            Assert.Contains("# Latest.Two.dll", exact.Output);
            Assert.DoesNotContain("Latest.One.dll", exact.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_NamesakeLibrary_UsesManagedAssemblyIdentity()
    {
        string packageId =
            typeof(CommandExecutionTests).Assembly.GetName().Name!;
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        try
        {
            string packageRoot = Path.Combine(tempDir, "content");
            string libDir = Path.Combine(
                packageRoot,
                "lib",
                "net10.0");
            Directory.CreateDirectory(libDir);
            File.Copy(
                TestAssemblyPath,
                Path.Combine(libDir, "Unexpected.FileName.dll"));
            var (systemRuntime, _, _, resolveError) =
                PlatformResolver.ResolveAssembly("System.Runtime");
            Assert.True(
                resolveError is null && systemRuntime is not null,
                resolveError);
            File.Copy(
                systemRuntime!,
                Path.Combine(libDir, "Neighbor.dll"));
            string packagePath = Path.Combine(
                tempDir,
                $"{packageId}.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var result = await RunAppAsync(
                "package",
                packagePath,
                "--tfm",
                "net11.0",
                "--namesake-library",
                "-S",
                "Library Info",
                "--tips",
                "q");
            var libraryResult = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "--tfm",
                "net11.0",
                "--namesake-library",
                "-S",
                "Library Info",
                "--tips",
                "q");

            Assert.True(
                result.Exit == 0,
                $"Expected success.{Environment.NewLine}"
                    + $"Error: {result.Error}{Environment.NewLine}"
                    + $"Output: {result.Output}");
            Assert.Empty(result.Error);
            Assert.Contains("# Unexpected.FileName.dll", result.Output);
            Assert.DoesNotContain("Neighbor.dll", result.Output);
            Assert.Equal(result, libraryResult);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_BarePackageRemainsPackageScoped()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "Package Info",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("## Package Info", result.Output);
            Assert.DoesNotContain("## Library Info", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Bare <c>-S</c> must remain the fixed overview after package inspection delegates to the
    /// all-libraries path. The count map names the complete request, while the rendered headings
    /// prove that the same preset reached effective-section selection and data collection.
    /// </summary>
    [Fact]
    public async Task PackageCommand_AllLibraries_BareSelectCount_MapDescribesBareSelectRender()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "System.Text.Json",
            "System.Collections");
        try
        {
            var (renderExit, renderOutput, renderError) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "--tips", "q");
            var (countExit, countOutput, countError) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "--count", "--tips", "q");

            Assert.Equal(0, renderExit);
            Assert.Equal(0, countExit);
            Assert.DoesNotContain("Tip:", renderError);
            Assert.DoesNotContain("Tip:", countError);

            var rendered = renderOutput.ReplaceLineEndings("\n").Split('\n')
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line =>
                {
                    var heading = line[3..].Trim();
                    var provenance = heading.IndexOf(" (", StringComparison.Ordinal);
                    return provenance >= 0 ? heading[..provenance] : heading;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var mapped = countOutput.ReplaceLineEndings("\n").Split('\n')
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
                .Select(line => line.Split('|'))
                .Where(cells => cells.Length > 2)
                .Select(cells => (
                    Section: cells[1].Trim(),
                    Count: cells[2].Trim()))
                .Where(row => row.Section.Length > 0
                    && row.Section != "Section"
                    && !row.Section.StartsWith('-'))
                .ToDictionary(
                    row => row.Section,
                    row => int.Parse(
                        row.Count,
                        CultureInfo.InvariantCulture),
                    StringComparer.OrdinalIgnoreCase);
            var renderedCounts = CountRenderedMarkdownTableRowsBySection(renderOutput)
                .GroupBy(
                    row =>
                    {
                        var provenance = row.Key.IndexOf(
                            " (",
                            StringComparison.Ordinal);
                        return provenance >= 0
                            ? row.Key[..provenance]
                            : row.Key;
                    },
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(row => row.Value),
                    StringComparer.OrdinalIgnoreCase);

            var expected = LibrarySections.CreatePipeline().BareSelectSectionNames;
            Assert.Equal(expected.Order(), rendered.Order());
            Assert.Equal(expected.Order(), mapped.Keys.Order());
            foreach (var section in expected)
            {
                Assert.True(
                    renderedCounts.TryGetValue(
                        section,
                        out var renderedCount),
                    $"{section} must render in this fixture.");
                Assert.True(
                    renderedCount > 0,
                    $"{section} must render rows in this fixture.");
                int semanticCount =
                    section.Equals(
                        SectionNames.LibraryInfo,
                        StringComparison.OrdinalIgnoreCase)
                        ? renderOutput
                            .ReplaceLineEndings("\n")
                            .Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries)
                            .Count(line => line.StartsWith(
                                "## Library Info (",
                                StringComparison.Ordinal))
                        : renderedCount;
                Assert.Equal(
                    semanticCount,
                    mapped[section]);
            }

            foreach (var format in new[]
                     {
                         "--json",
                         "--table",
                         "--tsv",
                         "--jsonl",
                     })
            {
                var (formattedExit, formattedOutput, formattedError) =
                    await RunAppAsync(
                        "package",
                        packagePath,
                        "--library",
                        "-S",
                        "--count",
                        format,
                        "--tips",
                        "q");

                Assert.Equal(0, formattedExit);
                Assert.DoesNotContain(
                    "unprojected output",
                    formattedError,
                    StringComparison.OrdinalIgnoreCase);

                if (format is "--json" or "--jsonl")
                {
                    var documents = format == "--json"
                        ? JsonDocument.Parse(formattedOutput).RootElement
                            .EnumerateArray()
                            .Select(element => element.Clone())
                            .ToArray()
                        : formattedOutput
                            .ReplaceLineEndings("\n")
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                            .ToArray();
                    Assert.Equal(mapped.Count, documents.Length);
                    foreach (var document in documents)
                    {
                        Assert.Equal(
                            JsonValueKind.Number,
                            document.GetProperty("count").ValueKind);
                        Assert.Equal(
                            mapped[document.GetProperty("section").GetString()!],
                            document.GetProperty("count").GetInt32());
                    }
                }
                else if (format == "--tsv")
                {
                    var rows = formattedOutput
                        .ReplaceLineEndings("\n")
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Skip(1)
                        .Select(line => line.Split('\t'))
                        .ToDictionary(
                            cells => cells[0],
                            cells => int.Parse(
                                cells[1],
                                CultureInfo.InvariantCulture),
                            StringComparer.OrdinalIgnoreCase);
                    Assert.Equal(mapped.Count, rows.Count);
                    foreach (var (section, count) in mapped)
                        Assert.Equal(count, rows[section]);
                }
                else
                {
                    var lines = formattedOutput
                        .ReplaceLineEndings("\n")
                        .Split('\n');
                    foreach (var (section, count) in mapped)
                    {
                        Assert.Contains(
                            lines,
                            line => line.Contains(
                                    section,
                                    StringComparison.Ordinal)
                                && line.Contains(
                                    count.ToString(CultureInfo.InvariantCulture),
                                    StringComparison.Ordinal));
                    }
                }
            }

            var (categoryExit, categoryOutput, categoryError) =
                await RunAppAsync(
                    "package",
                    packagePath,
                    "--library",
                    "-S",
                    SectionCategoryNames.Library,
                    "--count",
                    "--tips",
                    "q");

            Assert.Equal(0, categoryExit);
            Assert.Contains("| Section | Count |", categoryOutput);
            foreach (var section in LibrarySections
                         .CreatePipeline()
                         .GetCategoryMap()[SectionCategoryNames.Library])
            {
                Assert.Contains($"| {section} |", categoryOutput);
            }
            Assert.DoesNotContain(
                CountOutput.SingleSectionRequiredMessage,
                categoryError);

            var (metadataExit, metadataOutput, metadataError) =
                await RunAppAsync(
                    "package",
                    packagePath,
                    "--library",
                    "-S",
                    SectionCategoryNames.Metadata,
                    "--count",
                    "--tips",
                    "q");

            Assert.Equal(0, metadataExit);
            Assert.DoesNotContain(
                $"| {MetadataSectionNames.Heap} |",
                metadataOutput);
            var metadataImageRow = metadataOutput
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Single(line => line.StartsWith(
                    $"| {MetadataSectionNames.Image} |",
                    StringComparison.Ordinal));
            var metadataImageCount = int.Parse(
                metadataImageRow.Split('|')[2].Trim(),
                CultureInfo.InvariantCulture);
            var (metadataImageRenderExit, metadataImageRender, _) =
                await RunAppAsync(
                    "package",
                    packagePath,
                    "--library",
                    "-S",
                    MetadataSectionNames.Image,
                    "--tips",
                    "q");
            Assert.Equal(0, metadataImageRenderExit);
            Assert.Equal(
                CountRenderedMarkdownTableRows(metadataImageRender),
                metadataImageCount);
            Assert.Contains(
                $"## {MetadataSectionNames.Image} (ref/",
                metadataImageRender);
            Assert.Equal(
                2,
                metadataImageRender
                    .ReplaceLineEndings("\n")
                    .Split('\n')
                    .Count(line => line.StartsWith(
                        $"## {MetadataSectionNames.Image} (",
                        StringComparison.Ordinal)));
            Assert.DoesNotContain(
                "unprojected output",
                metadataError,
                StringComparison.OrdinalIgnoreCase);

            var (emptyExit, emptyOutput, emptyError) =
                await RunAppAsync(
                    "package",
                    packagePath,
                    "--library",
                    "-S",
                    $"{SectionNames.IdentifierConfusion},{SectionNames.NonNormalizedPaths}",
                    "--count",
                    "--json",
                    "--tips",
                    "q");

            Assert.Equal(0, emptyExit);
            using var emptyDocument = JsonDocument.Parse(emptyOutput);
            var emptyRows = emptyDocument.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.OrdinalIgnoreCase);
            Assert.Equal(0, emptyRows[SectionNames.IdentifierConfusion]);
            Assert.Equal(0, emptyRows[SectionNames.NonNormalizedPaths]);
            Assert.All(
                emptyDocument.RootElement.EnumerateArray(),
                row => Assert.Equal(
                    JsonValueKind.Number,
                    row.GetProperty("count").ValueKind));
            Assert.DoesNotContain(
                "unprojected output",
                emptyError,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RendersLibraryInfoPerHighestTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info", "--rows", "20");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AggregateLibraryInfoCountsLibraries()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var count = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.LibraryInfo,
                "--count",
                "--tips",
                "q");
            var rows = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.LibraryInfo,
                "--rows",
                "1",
                "--tips",
                "q");
            var windowedCount = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.LibraryInfo,
                "--rows",
                "1",
                "--count",
                "--tips",
                "q");
            var jsonRows = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                SectionNames.LibraryInfo,
                "--rows",
                "1",
                "--json",
                "--tips",
                "q");

            Assert.Equal(0, count.Exit);
            Assert.Empty(count.Error);
            Assert.Equal(
                2,
                int.Parse(
                    count.Output.Trim(),
                    CultureInfo.InvariantCulture));
            Assert.Equal(0, rows.Exit);
            Assert.DoesNotContain("Tip:", rows.Error);
            Assert.Single(
                rows.Output.Split('\n'),
                line => line.StartsWith(
                        "## Library Info (",
                        StringComparison.Ordinal));
            Assert.Equal(0, windowedCount.Exit);
            Assert.Empty(windowedCount.Error);
            Assert.Equal("1", windowedCount.Output.Trim());
            Assert.Equal(0, jsonRows.Exit);
            Assert.DoesNotContain("Tip:", jsonRows.Error);
            using JsonDocument jsonDocument =
                JsonDocument.Parse(jsonRows.Output);
            Assert.Single(jsonDocument.RootElement.EnumerateArray());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        PackageCommand_AllLibraries_LibraryInfoDetailedDiscoveryDeclaresInventory()
    {
        string package =
            $"Definitely.Missing.Package.{Guid.NewGuid():N}";

        var result = await RunAppAsync(
            "package",
            package,
            "--library",
            "-D",
            SectionNames.LibraryInfo,
            "--details",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument document =
            JsonDocument.Parse(result.Output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            SectionNames.LibraryInfo,
            row.GetProperty("name").GetString());
        Assert.Equal(
            "inventory",
            row.GetProperty("shape").GetString());
        Assert.Equal(
            ["rows", "count"],
            row.GetProperty("terminals")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task
        PackageCommand_ExactLibraryInfoDetailedDiscoveryDeclaresScalar()
    {
        string package =
            $"Definitely.Missing.Package.{Guid.NewGuid():N}";

        var result = await RunAppAsync(
            "package",
            package,
            "--library",
            "Missing.dll",
            "-D",
            SectionNames.LibraryInfo,
            "--details",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument document =
            JsonDocument.Parse(result.Output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "scalar",
            row.GetProperty("shape").GetString());
        Assert.Empty(
            row.GetProperty("terminals").EnumerateArray());
    }

    [Fact]
    public async Task
        PackageCommand_AllLibraries_OtherDetailedDiscoveryFailsVisibly()
    {
        string package =
            $"Definitely.Missing.Package.{Guid.NewGuid():N}";

        var result = await RunAppAsync(
            "package",
            package,
            "--library",
            "-D",
            "Symbols",
            "--details",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Detailed aggregate Library discovery currently supports only "
                + $"'{SectionNames.LibraryInfo}'",
            result.Error);
        Assert.DoesNotContain(package, result.Error);
    }

    [Fact]
    public async Task PackageCommand_DetailsRequiresDiscovery()
    {
        var result = await RunAppAsync(
            "package",
            "Anything",
            "--library",
            "--details",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--details requires -D/--discover",
            result.Error);
    }

    [Fact]
    public async Task
        PackageCommand_AllLibraries_MixedJsonLibraryRowsFailVisibly()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                $"{SectionNames.LibraryInfo},Symbols",
                "--rows",
                "1",
                "--json",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Aggregate JSON row selection with Library Info requires "
                    + "exactly one selected section",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TfmAllIncludesEveryTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "--tfm", "all", "-S", "Library Info", "--rows", "12");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TfmAllPreservesFrameworkFolder()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        try
        {
            var content = Path.Combine(tempDir, "content");
            var uap = Path.Combine(content, "lib", "uap10.0");
            var portable = Path.Combine(
                content,
                "lib",
                "portable-net45+win8");
            Directory.CreateDirectory(uap);
            Directory.CreateDirectory(portable);
            var (configuration, _, _, configurationError) =
                PlatformResolver.ResolveAssembly(
                    "Microsoft.Extensions.Configuration");
            var (json, _, _, jsonError) =
                PlatformResolver.ResolveAssembly(
                    "Microsoft.Extensions.Configuration.Json");
            Assert.True(
                configurationError is null && configuration is not null,
                configurationError);
            Assert.True(
                jsonError is null && json is not null,
                jsonError);
            foreach (string framework in new[] { uap, portable })
            {
                File.Copy(
                    configuration,
                    Path.Combine(
                        framework,
                        "Microsoft.Extensions.Configuration.dll"));
                File.Copy(
                    json,
                    Path.Combine(
                        framework,
                        "Microsoft.Extensions.Configuration.Json.dll"));
            }
            var packagePath = Path.Combine(
                tempDir,
                "Test.FrameworkFolders.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "--tfm",
                "all",
                "-S", "Integrations",
                "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("\tportable-net45+win8\t", output);
            Assert.Contains("\tuap10.0\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatesIntegrationsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "@Integrations", "--rows", "40");

            Assert.Equal(0, exit);
            Assert.Contains("## Integrations", output);
            Assert.Contains(
                "| Library | TFM | Integration | Kind | Shape | Symbol |",
                output);
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("Test.BlankIdentity", " ")]
    public async Task PackageCommand_AllLibraries_BlankNuspecIdentityFallsBack(
        string packageId,
        string packageVersion)
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration");
        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry =
                    archive.CreateEntry("Test.BlankIdentity.nuspec");
                await using Stream stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package>
                      <metadata>
                        <id>{{packageId}}</id>
                        <version>{{packageVersion}}</version>
                      </metadata>
                    </package>
                    """);
            }

            var (exit, output, _) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S", "Integrations");

            Assert.Equal(0, exit);
            Assert.Contains(
                "## Integrations",
                output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_BlankAssemblyNameReportsFailureWithoutAbortingHealthyParticipants()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration");
        try
        {
            string malformedPath = Path.Combine(tempDir, "BlankName.dll");
            WriteBlankAssemblyNameAssembly(malformedPath);
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                ZipArchiveEntry healthyEntry = archive.Entries.Single(
                    entry => entry.FullName.EndsWith(
                        "Microsoft.Extensions.Configuration.dll",
                        StringComparison.Ordinal));
                string directory = healthyEntry.FullName[..(
                    healthyEntry.FullName.LastIndexOf('/') + 1)];
                archive.CreateEntryFromFile(
                    malformedPath,
                    $"{directory}BlankName.dll");
            }

            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S", "Integrations",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.False(
                string.IsNullOrWhiteSpace(output),
                error);
            Assert.Contains(
                "## Integrations",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Could not select library descriptor for",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "BlankName.dll",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_BlankAssemblyNameReportsFailureAndSuppressesOpportunities()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"blank-name-opportunity-{Guid.NewGuid():N}");
        try
        {
            string content = Path.Combine(tempDir, "content");
            string framework = Path.Combine(content, "lib", "net8.0");
            Directory.CreateDirectory(framework);
            WriteBlankAssemblyNameAssembly(
                Path.Combine(framework, "BlankName.dll"));
            string packagePath = Path.Combine(
                tempDir,
                "BlankName.Opportunity.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.DoesNotContain(
                "Azure.Test.ExampleClient",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Could not select library descriptor for",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "BlankName.dll",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "No libraries could be read",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_GroupedFailureSurvivesHostFailureAcrossOutputPaths()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration");
        try
        {
            string malformedPath = Path.Combine(
                tempDir,
                "MalformedIntegrations.dll");
            WriteMalformedTypeNameAssembly(malformedPath);
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                ZipArchiveEntry healthyEntry = archive.Entries.Single(
                    entry => entry.FullName.EndsWith(
                        "Microsoft.Extensions.Configuration.dll",
                        StringComparison.Ordinal));
                string directory = healthyEntry.FullName[..(
                    healthyEntry.FullName.LastIndexOf('/') + 1)];
                archive.CreateEntryFromFile(
                    malformedPath,
                    $"{directory}MalformedIntegrations.dll");
            }

            string[][] outputOptions =
            [
                [],
                ["--json"],
                ["--count"],
                ["--tsv"],
            ];
            foreach (string[] outputOption in outputOptions)
            {
                string[] arguments =
                [
                    "package",
                    packagePath,
                    "--library",
                    "-S", "Integrations",
                    "--tips",
                    "q",
                    .. outputOption,
                ];
                var (exit, output, error) =
                    await RunAppAsync(arguments);

                Assert.Equal(1, exit);
                switch (outputOption.FirstOrDefault())
                {
                    case null:
                        Assert.Contains(
                            "## Integrations",
                            output,
                            StringComparison.Ordinal);
                        break;
                    case "--json":
                        Assert.StartsWith("[", output);
                        break;
                    case "--count":
                        Assert.True(
                            int.TryParse(
                                output.Trim(),
                                CultureInfo.InvariantCulture,
                                out int count));
                        Assert.True(count > 0);
                        break;
                    case "--tsv":
                        Assert.Contains(
                            "package\tversion\tlibrary\ttfm",
                            output,
                            StringComparison.Ordinal);
                        break;
                }
                Assert.Contains(
                    "Integrations inspection failed for",
                    error,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "MalformedIntegrations.dll",
                    error,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_MetadataOverflowPreservesHealthyOutput()
    {
        const string HealthyAssembly =
            "Microsoft.Extensions.Configuration";
        var (packagePath, tempDir) = CreateLocalRefPackage(
            HealthyAssembly);
        try
        {
            var (sourcePath, _, _, error) =
                PlatformResolver.ResolveAssembly(HealthyAssembly);
            Assert.Null(error);
            Assert.NotNull(sourcePath);
            string malformedPath = Path.Combine(
                tempDir,
                "Overflow.dll");
            WriteOverflowingMetadataStreamCountAssembly(
                sourcePath,
                malformedPath);
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                ZipArchiveEntry healthyEntry = archive.Entries.Single(
                    entry => entry.FullName.EndsWith(
                        $"{HealthyAssembly}.dll",
                        StringComparison.Ordinal));
                string directory = healthyEntry.FullName[..(
                    healthyEntry.FullName.LastIndexOf('/') + 1)];
                archive.CreateEntryFromFile(
                    malformedPath,
                    $"{directory}Overflow.dll");
            }

            var (exit, output, commandError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S", "Integrations",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.False(
                string.IsNullOrWhiteSpace(output),
                commandError);
            Assert.Contains(
                "## Integrations",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Could not select library descriptor for",
                commandError,
                StringComparison.Ordinal);
            Assert.Contains(
                "Overflow.dll",
                commandError,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                nameof(OverflowException),
                commandError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_MalformedMetadataPreflightIsIncompleteAcrossOutputPaths()
    {
        const string HealthyAssembly =
            "Microsoft.Extensions.Configuration";
        var (packagePath, tempDir) = CreateLocalRefPackage(
            HealthyAssembly);
        try
        {
            var (sourcePath, _, _, error) =
                PlatformResolver.ResolveAssembly(HealthyAssembly);
            Assert.Null(error);
            Assert.NotNull(sourcePath);
            string malformedPath = Path.Combine(
                tempDir,
                "MalformedMetadata.dll");
            WriteTruncatedMetadataTableAssembly(
                sourcePath,
                malformedPath);
            Assert.Throws<BadImageFormatException>(
                () => ResolvedAssemblyReference
                    .CreateFromPathIfManaged(
                        malformedPath,
                        AssemblyResolutionProvenance.Local(
                            "malformed metadata preflight test")));

            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Update))
            {
                ZipArchiveEntry healthyEntry = archive.Entries.Single(
                    entry => entry.FullName.EndsWith(
                        $"{HealthyAssembly}.dll",
                        StringComparison.Ordinal));
                string directory = healthyEntry.FullName[..(
                    healthyEntry.FullName.LastIndexOf('/') + 1)];
                archive.CreateEntryFromFile(
                    malformedPath,
                    $"{directory}MalformedMetadata.dll");
            }

            string[][] outputOptions =
            [
                [],
                ["--json"],
                ["--count"],
                ["--tsv"],
            ];
            foreach (string[] outputOption in outputOptions)
            {
                string[] arguments =
                [
                    "package",
                    packagePath,
                    "--library",
                    "-S", "Integrations",
                    "--tips",
                    "q",
                    .. outputOption,
                ];
                var (exit, output, commandError) =
                    await RunAppAsync(arguments);

                Assert.Equal(1, exit);
                Assert.False(string.IsNullOrWhiteSpace(output));
                if (outputOption.Length == 0)
                    Assert.DoesNotContain('\r', output);
                Assert.Contains(
                    "Could not select library descriptor for",
                    commandError,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "MalformedMetadata.dll",
                    commandError,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatedMarkdown_PreservesSingleLibraryProvenance()
    {
        var (packagePath, tempDir) =
            CreateLocalIntegrationOpportunityPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                "| Library | TFM | Integration | API | Integration Type | Look For |",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "| `lib/net10.0/IntegrationOpportunityFixture.dll` | `net10.0` |",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatedMarkdown_ContainsPackageControlledTfm()
    {
        const string Tfm = "net10.0\u202ERED";
        var (packagePath, tempDir) =
            CreateLocalIntegrationOpportunityPackage(Tfm);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain('\u202E', output);
            Assert.Contains(
                @"`net10.0\u202ERED`",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_IntegrationOpportunities_UsesGroupQueryResult()
    {
        var (packagePath, tempDir) =
            CreateLocalIntegrationOpportunityPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--rows",
                "20");

            Assert.Equal(0, exit);
            Assert.Contains("## Integration Opportunities", output);
            Assert.Contains(
                "| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder |",
                output);
            Assert.Contains(
                "| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration |",
                output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_ReportsOpportunityFailure()
    {
        HashSet<InspectionQueryDefinition> queries =
            [AssemblyContextIntegrationsQuery.Definition];
        AssemblyContextIntegrationsBatch batch =
            Assert.IsType<AssemblyContextIntegrationsBatch>(
                await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
                    queries,
                    LibrarySections.CreateGroupQueryRegistry(),
                    [
                        new AssemblyContextIntegrationsInput(
                            TestAssemblyPath,
                            AssemblyResolutionProvenance.Local(
                                "all-libraries failure test")),
                    ]));
        var integrations = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            batch.EntryFor(TestAssemblyPath));
        var inspection = new LibraryInspection
        {
            FileName = "Broken.dll",
            AssemblyIntegrationOpportunitiesEntry =
                new AssemblyIntegrationOpportunitiesEntry.Failed(
                    integrations.Subject,
                    new BadImageFormatException("opportunity failure")),
        };
        var options = new LibraryOptions
        {
            IncludeSections = ["Integration Opportunities"],
        };
        List<string>? sections = null;

        var (output, error) = await ConsoleCapture.RunAsync(
            () => sections = PackageCommand.GetAllLibrariesSections(
                [inspection],
                options,
                LibrarySections.CreatePipeline()));

        Assert.Empty(output);
        Assert.Empty(Assert.IsType<List<string>>(sections));
        Assert.Contains(
            "Integration Opportunities inspection failed "
            + "(Assembly context integration opportunities): opportunity failure",
            error);
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Integrations", "--tsv");

            Assert.Equal(0, exit);
            string[] lines = output.ReplaceLineEndings("\n").Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
            string[] headers = lines[0].Split('\t');
            PackageCommand.AllLibrariesRowSchema rowSchema =
                Assert.Single(
                    PackageCommand.AllLibrariesRowSchemas,
                    schema => schema.Section.Equals(
                        IntegrationSectionNames.Integrations,
                        StringComparison.Ordinal));
            Assert.Equal(rowSchema.StableHeaders, headers);
            Assert.All(
                lines[1..],
                line => Assert.Equal(
                    headers.Length,
                    line.Split('\t').Length));
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Library Info is an inventory of Libraries, even when a row format lowers each selected
    /// scalar Library value to multiple provenance-bearing field rows.
    /// </summary>
    [Fact]
    public async Task PackageCommand_AllLibraries_RowFormats_WindowLibrariesBeforeFieldLowering()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (countExit, countOutput, countError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "2",
                "--count",
                "--tips",
                "q");
            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "2",
                "--tsv",
                "--tips",
                "q");
            var (jsonlExit, jsonlOutput, jsonlError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "2",
                "--jsonl",
                "--tips",
                "q");

            Assert.Equal(0, countExit);
            Assert.Equal(0, tsvExit);
            Assert.Equal(0, jsonlExit);
            Assert.Equal(2, int.Parse(
                countOutput.Trim(),
                CultureInfo.InvariantCulture));

            var tsvRows = SplitOutputLines(tsvOutput).Skip(1).ToArray();
            Assert.True(tsvRows.Length > 4);
            Assert.Equal(
                2,
                tsvRows
                    .GroupBy(row => row.Split('\t')[2])
                    .Count());

            var jsonlRows = SplitOutputLines(jsonlOutput)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Equal(tsvRows.Length, jsonlRows.Length);
            Assert.Equal(
                2,
                jsonlRows
                    .GroupBy(document => document.RootElement
                        .GetProperty("library")
                        .GetString())
                    .Count());
            Assert.DoesNotContain("Tip:", countError);
            Assert.DoesNotContain("Tip:", tsvError);
            Assert.DoesNotContain("Tip:", jsonlError);

            foreach (var document in jsonlRows)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RowFormats_TailWindowMatchesMarkdownRows()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (markdownExit, markdownOutput, markdownError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "1",
                "--tail",
                "--tips",
                "q");
            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "1",
                "--tail",
                "--tsv",
                "--tips",
                "q");
            var (jsonlExit, jsonlOutput, jsonlError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "1",
                "--tail",
                "--jsonl",
                "--tips",
                "q");

            Assert.Equal(0, markdownExit);
            Assert.Equal(0, tsvExit);
            Assert.Equal(0, jsonlExit);

            Assert.Contains(
                "## Library Info (lib/net10.0/Latest.Two.dll)",
                markdownOutput);
            Assert.DoesNotContain(
                "## Library Info (lib/net10.0/Latest.One.dll)",
                markdownOutput);
            var markdownFields = SplitOutputLines(markdownOutput)
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
                .Skip(2)
                .Select(line => line.Split('|')[1].Trim())
                .ToArray();
            string[][] tsvRows = SplitOutputLines(tsvOutput)
                .Skip(1)
                .Select(line => line.Split('\t'))
                .ToArray();
            Assert.All(
                tsvRows,
                row => Assert.Equal(
                    "lib/net10.0/Latest.Two.dll",
                    row[2]));
            var tsvFields = tsvRows
                .Select(row => row[4])
                .ToArray();
            var jsonlRows = SplitOutputLines(jsonlOutput)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.All(
                jsonlRows,
                document => Assert.Equal(
                    "lib/net10.0/Latest.Two.dll",
                    document.RootElement
                        .GetProperty("library")
                        .GetString()));
            var jsonlFields = jsonlRows
                .Select(document => document.RootElement
                    .GetProperty("field")
                    .GetString())
                .ToArray();

            Assert.NotEmpty(markdownFields);
            Assert.Equal(markdownFields, tsvFields);
            Assert.Equal(markdownFields, jsonlFields);
            Assert.DoesNotContain("Tip:", markdownError);
            Assert.DoesNotContain("Tip:", tsvError);
            Assert.DoesNotContain("Tip:", jsonlError);

            foreach (var document in jsonlRows)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (countExit, countOutput, countError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S", "Integrations",
                "--rows",
                "1",
                "--count",
                "--tips",
                "q");
            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S", "Integrations",
                "--rows",
                "1",
                "--tsv",
                "--tips",
                "q");

            Assert.Equal(0, countExit);
            Assert.Equal(0, tsvExit);
            Assert.Equal(
                1,
                int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
            Assert.Single(SplitOutputLines(tsvOutput).Skip(1));
            Assert.DoesNotContain("Tip:", countError);
            Assert.DoesNotContain("Tip:", tsvError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregateRowFormats_WindowSameRowsAsMarkdown()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "System.Text.Json",
            "System.Linq.Expressions");
        try
        {
            var (markdownExit, markdownOutput, markdownError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Switches",
                "--rows",
                "1",
                "--tips",
                "q");
            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Switches",
                "--rows",
                "1",
                "--tsv",
                "--tips",
                "q");

            Assert.Equal(0, markdownExit);
            Assert.Equal(0, tsvExit);

            var markdownRow = SplitOutputLines(markdownOutput)
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
                .Skip(2)
                .First()
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => System.Net.WebUtility.HtmlDecode(
                    cell.Trim().Trim('`')))
                .ToArray();
            var tsvRow = SplitOutputLines(tsvOutput)
                .Skip(1)
                .Single()
                .Split('\t');
            var tsvProjection = new[]
            {
                tsvRow[2],
                tsvRow[3],
                tsvRow[4],
                tsvRow[5],
                tsvRow[6]
            };

            Assert.Equal(markdownRow, tsvProjection);
            Assert.DoesNotContain("Tip:", markdownError);
            Assert.DoesNotContain("Tip:", tsvError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_OpportunityRowFormat_WindowSameRowAsMarkdown()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiLibraryIntegrationOpportunityPackage();
        try
        {
            var (markdownExit, markdownOutput, markdownError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--rows",
                "2",
                "--tips",
                "q");
            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--rows",
                "2",
                "--tsv",
                "--tips",
                "q");

            Assert.Equal(0, markdownExit);
            Assert.Equal(0, tsvExit);

            var markdownRows = SplitOutputLines(markdownOutput)
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
                .Skip(2)
                .Select(line => line
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(cell => System.Net.WebUtility.HtmlDecode(
                        cell.Trim().Trim('`'))))
                .Select(cells => string.Join('\t', cells))
                .ToArray();
            var tsvRows = SplitOutputLines(tsvOutput)
                .Skip(1)
                .Select(line => line.Split('\t'))
                .Select(row => string.Join(
                    '\t',
                    row[2],
                    row[3],
                    row[4],
                    row[5],
                    row[6],
                    row[7]))
                .ToArray();

            Assert.Equal(markdownRows, tsvRows);
            Assert.DoesNotContain("Tip:", markdownError);
            Assert.DoesNotContain("Tip:", tsvError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RowFormat_WindowMissPreservesHeader()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--rows",
                "100..",
                "--tsv",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Equal(
                "package\tversion\tlibrary\ttfm\tfield\tvalue",
                output.Trim());
            Assert.DoesNotContain(
                "matched section has no row data",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RowFormat_UsesEffectiveSelectionCardinality()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "-S",
                "Switches",
                "--table",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.NotEmpty(result.Output);
            Assert.Contains("Library", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "requires exactly one section",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvRejectsIntegrationCategory()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "@Integrations", "--tsv");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("requires one concrete section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_JsonlEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Integrations", "--jsonl");

            Assert.Equal(0, exit);
            var documents = SplitOutputLines(output)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Contains(documents, document =>
                document.RootElement.GetProperty("library").GetString()?.EndsWith("Microsoft.Extensions.Configuration.Json.dll", StringComparison.Ordinal) == true);
            Assert.All(documents, document =>
                Assert.False(document.RootElement.TryGetProperty("section", out _)));
            Assert.DoesNotContain("Tip:", error);

            foreach (var document in documents)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibrariesOption_ReturnsReplacementGuidance()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "'--all-libraries' is no longer valid",
                error);
            Assert.Contains(
                "Use '--library'",
                error);

            var help = await RunAppAsync("package", "--help");
            Assert.Equal(0, help.Exit);
            Assert.Contains("--namesake-library", help.Output);
            Assert.DoesNotContain("--all-libraries", help.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_LibrarySourceFilesSection_PreservesTypeFilterAndPreferRenderedUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--library", "Newtonsoft.Json.dll",
            "-S", "Source Files", "-t", "JsonConvert", "--prefer-rendered-urls", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Name: Newtonsoft.Json", output);
        Assert.Contains("Newtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task PackageLibraryMode_BareSelect_MatchesStandaloneLibraryBareSelect()
    {
        // The nested library view is constructed from the package options, so bare -S has to be
        // carried across the boundary explicitly. Before #3547 it rode along inside Select as the
        // "@Default" string and propagated for free; a dedicated flag does not, and dropping it
        // silently downgrades the nested view to "no sections requested".
        var (nestedExit, nestedOutput, _) = await RunAppAsync("package", "Markout", "--library", "-S");
        var (standaloneExit, standaloneOutput, _) = await RunAppAsync("library", "Markout", "-S");

        Assert.Equal(0, nestedExit);
        Assert.Equal(0, standaloneExit);

        static List<string> Headings(string output) => output
            .Split('\n')
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();

        Assert.NotEmpty(Headings(nestedOutput));
        Assert.Equal(Headings(standaloneOutput), Headings(nestedOutput));
    }

    [Fact]
    public async Task PackageLibraryMode_ValueJsonArray_CarriesShapeProjection()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "System.Runtime.dll",
                "-S", "Library Info", "--fields", "Assembly Version",
                "--value", "--json-array", "--row", "first", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var row = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("Library Info", row.GetProperty("section").GetString());
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", row.GetProperty("value").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageLibraryMode_Value_CarriesProjectionRow()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "System.Runtime.dll",
                "-S", "Library Info", "--fields", "Assembly Version",
                "--value", "--row", "2", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("row 2 is not in this section", error);
            Assert.DoesNotContain("produced unprojected output", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--urls", "selected section has no URL values")]
    [InlineData("--paths", "selected section has no path values")]
    [InlineData("--json-array", "--json-array requires --value, --urls, --paths, or --print")]
    public async Task PackageLibraryMode_CarriesRejectedProjectionOptions(
        string option,
        string expectedError)
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "System.Runtime.dll",
                "-S", "Library Info", option, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(expectedError, error);
            Assert.DoesNotContain("produced unprojected output", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_LibraryFiles_RendersAllLibFiles()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            // The lib/ slice is no longer its own section: --path is the scoping mechanism.
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "lib/**");

            Assert.Equal(0, exit);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| lib/net10.0/Latest.One.dll |", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.Contains("| lib/net10.0/Latest.Two.dll |", output);
            Assert.Contains("| lib/net8.0/Older.dll |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_NonCountFormats_WriteToOutputFile()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var formats = new (string Name, string[] Arguments)[]
            {
                ("markdown", ["-S", "Library Info", "--rows", "2"]),
                ("json", ["--json"]),
                ("table", ["-S", "Library Info", "--rows", "2", "--table"]),
                ("tsv", ["-S", "Library Info", "--rows", "2", "--tsv"]),
                ("jsonl", ["-S", "Library Info", "--rows", "2", "--jsonl"])
            };

            foreach (var (name, arguments) in formats)
            {
                string outputPath = Path.Combine(tempDir, $"{name}.txt");
                var baseline = await RunAppAsync(
                    [
                        "package",
                        packagePath,
                        "--library",
                        "--tips",
                        "q",
                        .. arguments
                    ]);
                var redirected = await RunAppAsync(
                    [
                        "package",
                        packagePath,
                        "--library",
                        "--tips",
                        "q",
                        .. arguments,
                        "--out",
                        outputPath
                    ]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Empty(baseline.Error);
                Assert.Empty(redirected.Error);
                Assert.Empty(redirected.Output);
                var written = File.ReadAllText(outputPath);
                Assert.Equal(
                    baseline.Output.ReplaceLineEndings("\n"),
                    written);
                Assert.DoesNotContain('\r', written);
                if (name == "json")
                    Assert.EndsWith("\n", baseline.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_OutputFile_PreservesLineWindows()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            foreach (string[] lineWindow in new[]
                     {
                         new[] { "-n", "3", "--lines" },
                         ["-n", "3", "--tail-lines"]
                     })
            {
                string outputPath = Path.Combine(
                    tempDir,
                    lineWindow.Contains("--tail-lines") ? "tail.txt" : "head.txt");
                var baseline = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package",
                        packagePath,
                        "--library",
                        "-S",
                        "Library Info",
                        "--table",
                        "--tips",
                        "q",
                        .. lineWindow
                    ]);
                var redirected = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package",
                        packagePath,
                        "--library",
                        "-S",
                        "Library Info",
                        "--table",
                        "--tips",
                        "q",
                        .. lineWindow,
                        "--out",
                        outputPath
                    ]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(3, baseline.Output.Count(character => character == '\n'));
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Equal(baseline.Error, redirected.Error);
                Assert.Empty(redirected.Output);
                var written = File.ReadAllText(outputPath);
                Assert.Equal(
                    baseline.Output.ReplaceLineEndings("\n"),
                    written);
                Assert.DoesNotContain('\r', written);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_OutputFile_IsIncludedInInfoMetrics()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        string outputPath = Path.Combine(tempDir, "output.txt");
        try
        {
            var baseline = await RunAppInDirectoryAsync(
                tempDir,
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--table",
                "--info");
            var redirected = await RunAppInDirectoryAsync(
                tempDir,
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "--table",
                "--info",
                "--out",
                outputPath);

            Assert.Equal(0, baseline.Exit);
            Assert.Equal(baseline.Exit, redirected.Exit);
            Assert.Empty(redirected.Output);
            var written = File.ReadAllText(outputPath);
            Assert.Equal(
                baseline.Output.ReplaceLineEndings("\n"),
                written);
            Assert.DoesNotContain('\r', written);

            static string OutputMetric(string error) =>
                SplitOutputLines(error).Single(line =>
                    line.StartsWith("| Output |", StringComparison.Ordinal));

            Assert.Equal(
                $"| Output | {CacheOutputFormatter.FormatSize(written.Length)} |",
                OutputMetric(redirected.Error));
            Assert.DoesNotContain(
                "| Output | 0 B |",
                redirected.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_CountOutputFile_PreservesLineWindowsAndInfoMetrics()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            foreach (string[] lineWindow in new[]
                     {
                         new[] { "-n", "1", "--lines" },
                         ["-n", "1", "--tail-lines"]
                     })
            {
                string outputPath = Path.Combine(
                    tempDir,
                    lineWindow.Contains("--tail-lines") ? "count-tail.jsonl" : "count-head.jsonl");
                var baseline = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package",
                        packagePath,
                        "--library",
                        "-S",
                        "@Library",
                        "--count",
                        "--jsonl",
                        "--info",
                        .. lineWindow
                    ]);
                var redirected = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package",
                        packagePath,
                        "--library",
                        "-S",
                        "@Library",
                        "--count",
                        "--jsonl",
                        "--info",
                        .. lineWindow,
                        "--out",
                        outputPath
                    ]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(1, baseline.Output.Count(character => character == '\n'));
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Empty(redirected.Output);
                var written = File.ReadAllText(outputPath);
                Assert.Equal(
                    baseline.Output.ReplaceLineEndings("\n"),
                    written);
                Assert.DoesNotContain('\r', written);
                Assert.False(
                    File.ReadAllBytes(outputPath)
                        .AsSpan()
                        .StartsWith(Encoding.UTF8.GetPreamble()));

                static string OutputMetric(string error) =>
                    SplitOutputLines(error).Single(line =>
                        line.StartsWith("| Output |", StringComparison.Ordinal));

                Assert.Equal(
                    $"| Output | {CacheOutputFormatter.FormatSize(written.Length)} |",
                    OutputMetric(redirected.Error));
                Assert.DoesNotContain(
                    "| Output | 0 B |",
                    redirected.Error,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_EmptyOutput_TruncatesAndValidatesOutputFile()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        string outputPath = Path.Combine(tempDir, "empty.txt");
        File.WriteAllText(outputPath, "stale");
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--tsv",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "matched sections have no data",
                result.Error,
                StringComparison.Ordinal);
            Assert.Empty(File.ReadAllText(outputPath));

            File.WriteAllText(outputPath, "stale");
            var invalidSelection = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "@Integrations",
                "--tsv",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(1, invalidSelection.Exit);
            Assert.Empty(invalidSelection.Output);
            Assert.Contains(
                "requires one concrete section",
                invalidSelection.Error,
                StringComparison.Ordinal);
            Assert.Equal("stale", File.ReadAllText(outputPath));

            File.WriteAllText(outputPath, "stale");
            var unsupportedSelection = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Library Info",
                "-S",
                "Inspection Failures",
                "--tsv",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(1, unsupportedSelection.Exit);
            Assert.Empty(unsupportedSelection.Output);
            Assert.Contains(
                "does not support section: Inspection Failures",
                unsupportedSelection.Error,
                StringComparison.Ordinal);
            Assert.Equal("stale", File.ReadAllText(outputPath));

            string invalidPath = Path.Combine(
                tempDir,
                "missing",
                "output.txt");
            var invalid = await RunAppAsync(
                "package",
                packagePath,
                "--library",
                "-S",
                "Integration Opportunities",
                "--tsv",
                "--out",
                invalidPath,
                "--tips",
                "q");

            Assert.Equal(1, invalid.Exit);
            Assert.Empty(invalid.Output);
            Assert.NotEmpty(invalid.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_AllLibraries_LibraryInfoWindowsLibrariesBeforeFieldLowering()
    {
        var (package, directory) = CreateLocalLibPackage();
        try
        {
            var allRows = await RunAppAsync(
                "package",
                package,
                "--library",
                "-S",
                "Library Info",
                "--jsonl");
            var allCount = await RunAppAsync(
                "package",
                package,
                "--library",
                "-S",
                "Library Info",
                "--count");
            var rows = await RunAppAsync(
                "package",
                package,
                "--library",
                "-S",
                "Library Info",
                "--jsonl",
                "--rows",
                "1");
            var count = await RunAppAsync(
                "package",
                package,
                "--library",
                "-S",
                "Library Info",
                "--count",
                "--rows",
                "1");

            Assert.Equal(0, allRows.Exit);
            Assert.Equal(0, allCount.Exit);
            Assert.Equal(0, rows.Exit);
            Assert.Equal(0, count.Exit);
            Assert.Empty(allRows.Error);
            Assert.Empty(allCount.Error);
            Assert.Empty(rows.Error);
            Assert.Empty(count.Error);
            string[] all = allRows.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("2", allCount.Output.Trim());
            Assert.True(all.Length > 2);
            Assert.Contains(
                all,
                row => row.Contains(
                    "\"field\":\"Union Types\"",
                    StringComparison.Ordinal));
            JsonDocument[] windowedRows = rows.Output
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.True(windowedRows.Length > 2);
            Assert.Single(
                windowedRows
                    .Select(document => document.RootElement
                        .GetProperty("library")
                        .GetString())
                    .Distinct(StringComparer.Ordinal));
            Assert.Equal("1", count.Output.Trim());

            foreach (JsonDocument document in windowedRows)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
