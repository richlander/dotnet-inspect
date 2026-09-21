using DotnetInspect.Cli.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Tests;

public partial class OutputFormatterTests
{
    [Fact]
    public async Task DiscoverOutput_Tsv_RendersHeaderedTsvRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type", "Sim");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(
                ["Results"],
                schema,
                DiscoveryOutputRequest.Create(OutputFormat.Tsv))));

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
            Task.FromResult(DiscoverOutput.Execute(
                ["Results"],
                schema,
                DiscoveryOutputRequest.Create(OutputFormat.Jsonl))));

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
            Task.FromResult(DiscoverOutput.Execute(
                ["Results"],
                schema,
                DiscoveryOutputRequest.Create(OutputFormat.Json))));

        Assert.Equal(0, exit);
        Assert.Contains("\"name\":\"Pattern\"", output);
        Assert.Contains("\"kind\":\"column\"", output);
    }

    [Fact]
    public async Task DiscoverOutput_DocumentSelectionControlsAutomaticTreePromotion()
    {
        var schema = new DocumentSchema()
            .Add("First", "column", "Name")
            .Add("Second", "column", "Name");
        var category =
            new DiscoveryResourceIdentity(
                DiscoveryResourceKind.Category,
                "@Group");
        var first =
            new DiscoveryResourceIdentity(
                DiscoveryResourceKind.Section,
                "First");
        var second =
            new DiscoveryResourceIdentity(
                DiscoveryResourceKind.Section,
                "Second");
        var document =
            new DiscoveryDocument(
                "library",
                [
                    new DiscoveryResource(
                        category,
                        members: [first, second]),
                    new DiscoveryResource(first),
                    new DiscoveryResource(second),
                ],
                [category],
                new DiscoverySelection(
                    isCatalog: false,
                    [category],
                    [first, second]));

        var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(
                ["First", "Second"],
                schema,
                DiscoveryOutputRequest.Create(OutputFormat.Markdown),
                document: document)));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| First | section |", output);
        Assert.Contains("| Second | section |", output);
        Assert.DoesNotContain("@Group", output);
    }

    [Fact]
    public void CountProjection_CapturesTableRowsBySection()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(1, "Title");
            writer.WriteHeading(2, "Methods");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [new[] { "Read" }, new[] { "Write" }]);
            writer.WriteHeading(2, "Fields");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [new[] { "Value" }]);
        }, new MarkoutWriterOptions());

        Assert.Equal(3, projection.Total);
        Assert.Equal(2, projection.SectionCounts["Methods"]);
        Assert.Equal(1, projection.SectionCounts["Fields"]);
    }

    [Fact]
    public void CountProjection_AppliesRowWindowBeforeReduction()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(2, "Methods");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [
                    new[] { "One" },
                    new[] { "Two" },
                    new[] { "Three" }
                ]);
        }, OutputFormatter.CreateWindowedOptions(RowWindow.Head(2)));

        Assert.Equal(2, projection.Total);
        Assert.Equal(2, projection.SectionCounts["Methods"]);
    }

    [Fact]
    public void CountProjection_DoesNotCountNonTableContent()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(2, "Notes");
            writer.WriteParagraph("Not a row.");
            writer.WriteCodeStart("md");
            writer.WriteCodeEnd();
        }, new MarkoutWriterOptions());

        Assert.Equal(0, projection.Total);
        Assert.True(projection.WroteAnyContent);
    }

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Tsv)]
    [InlineData(OutputFormat.Jsonl)]
    [InlineData(OutputFormat.Table)]
    [InlineData(OutputFormat.PlainText)]
    public void CountProjection_SectionRowsRenderThroughEveryCompatibleFormat(
        OutputFormat format)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Methods"] = 17,
            ["Fields"] = 23
        };

        var output = CountOutput.RenderSectionCounts(
            counts, ["Methods", "Fields"], format);

        Assert.Contains("Methods", output, StringComparison.Ordinal);
        Assert.Contains("17", output, StringComparison.Ordinal);
        Assert.Contains("Fields", output, StringComparison.Ordinal);
        Assert.Contains("23", output, StringComparison.Ordinal);

        if (format == OutputFormat.Json)
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal(17, document.RootElement[0].GetProperty("count").GetInt32());
            Assert.Equal(23, document.RootElement[1].GetProperty("count").GetInt32());
        }
        else if (format == OutputFormat.Jsonl)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal(17, first.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(23, second.RootElement.GetProperty("count").GetInt32());
        }
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
                PackageSections.Signature,
                PackageSections.Signals
            ]
        };
        var packageOutput = OutputFormatter.FormatResult(
            packageResult, packageOptions, PackageSectionDescriptors.CreatePipeline());

        var libraryInspection = CreateTestAudit("Test.dll", "net9.0");
        libraryInspection.OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
            [
                new OpenTelemetrySignalInfo("Tracing", "System.Diagnostics.ActivitySource"),
                new OpenTelemetrySignalInfo("Metrics", "System.Diagnostics.Metrics.UpDownCounter<T>"),
            ],
            FindingTestData.Subject);
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
                "SourceLink: Integrity"
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
        // This claim is about which lines end up adjacent, so it is asserted against
        // normalized text. Line endings are the separate claim below; a raw string
        // literal carries whatever ending this source file is checked out with, so
        // spelling "\n" here would silently assert the platform rather than the shape.
        Assert.Contains("B\n\n## Alpha", output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// This is the gate for line-ending preservation in <c>MarkdownSectionOrderer</c>.
    /// Reordering selects the order sections appear in; it is not licensed to rewrite CRLF
    /// to LF, for the same reason row limiting is not — the same document would otherwise
    /// differ byte for byte depending on whether a section order was supplied. The orderer
    /// used to rejoin on a hardcoded '\n', which on Windows silently converted the whole
    /// document and broke every caller that split it on <see cref="Environment.NewLine"/>.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MarkdownSectionOrderer_PreservesDocumentLineEndings(string newline)
    {
        string markdown = string.Join(newline, ["# Title", "", "intro", "", "## Zebra", "", "Z", "", "## Alpha", "", "A"]);

        var output = MarkdownSectionOrderer.Apply(markdown, ["Alpha", "Zebra"]);

        Assert.True(output.IndexOf("## Alpha", StringComparison.Ordinal) < output.IndexOf("## Zebra", StringComparison.Ordinal));
        Assert.Equal(newline, MarkdownScan.DetectNewline(output));
        Assert.Equal(
            output.Split('\n').Length - 1,
            output.Split(newline).Length - 1);
    }
}
