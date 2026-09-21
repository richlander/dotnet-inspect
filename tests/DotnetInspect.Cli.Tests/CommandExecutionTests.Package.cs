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
    public async Task Package_SourceLinkFileLinesRejectJsonBeforePackageResolution()
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.Not.Resolve",
            "-S", "Source Files",
            "--lines", "-n", "1", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_NonSourceLinkFileSurfacesRetainLegacyWindowValidation()
    {
        var category = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.NotResolve",
            "-S", "@SourceLink", "--rows", "..1");
        var mixed = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.NotResolve",
            "-S", "Source Files,Package Info", "--rows", "..1");
        var library = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.NotResolve",
            "-S", "Source Files", "--library", "--rows", "..1");
        var multiple = await RunAppAsync(
            "--offline",
            "package",
            "Package.That.Must.NotResolve",
            "Package.That.Also.Must.NotResolve",
            "-S", "Source Files", "--rows", "..1");

        foreach (var result in new[]
        {
            category,
            mixed,
            library,
            multiple,
        })
        {
            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains("--rows", result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PackageDocumentDestinations_HonorExplicitLineSelection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.LineDestination",
            "README.md",
            "readme");
        string outputPath = Path.Combine(tempDir, "package-info.md");
        try
        {
            string[] arguments =
            [
                "package",
                packagePath,
                "-S",
                "Package Info",
                "--lines",
                "-n",
                "1",
                "--tips",
                "q",
            ];
            var stdout = await RunAppInDirectoryAsync(tempDir, arguments);
            var redirected = await RunAppInDirectoryAsync(
                tempDir,
                [.. arguments, "--output", outputPath]);

            Assert.Equal(0, stdout.Exit);
            Assert.Equal(0, redirected.Exit);
            Assert.Empty(stdout.Error);
            Assert.Empty(redirected.Output);
            Assert.Empty(redirected.Error);
            Assert.Equal(stdout.Output, File.ReadAllText(outputPath));
            Assert.Single(
                stdout.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ConcatenatedValuesPreserveImplicitFileRouting()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.ConcatenatedOptions",
            "README.md",
            "readme",
            extraFiles:
            [
                ("a.txt", "a"),
                ("b.txt", "b"),
            ]);
        string outputPath = Path.Combine(tempDir, "-1");
        try
        {
            string[] projection =
            [
                packagePath,
                "-S",
                "Package files",
                "--paths",
                "-T-n1",
            ];

            var direct = await RunAppInDirectoryAsync(
                tempDir,
                ["package", .. projection]);
            var routed = await RunAppInDirectoryAsync(
                tempDir,
                projection);

            Assert.Equal(direct, routed);
            Assert.Equal(0, routed.Exit);
            Assert.True(
                routed.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries).Length > 1);

            string[] redirected =
            [
                packagePath,
                "-S",
                "Package files",
                "--paths",
                "-o=-1",
                "--lines",
                "-1",
                "--tips",
                "q",
            ];

            direct = await RunAppInDirectoryAsync(
                tempDir,
                ["package", .. redirected]);
            Assert.Equal(0, direct.Exit);
            Assert.Empty(direct.Output);
            string directOutput = File.ReadAllText(outputPath);
            Assert.Single(
                directOutput.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));

            File.Delete(outputPath);
            routed = await RunAppInDirectoryAsync(
                tempDir,
                redirected);

            Assert.Equal(direct, routed);
            Assert.Equal(directOutput, File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageSection_Rows_WindowsTheTabularRenderAndAgreesWithCount()
    {
        // Regression (#3457): --count windowed the section through the markdown limiter, but the
        // tabular render never received options.Rows, so `--rows 1..1 --count` reported one row
        // while `--rows 1..1 --format jsonl` emitted the whole section. A count that does not describe
        // the payload it is counting is worse than no count at all.
        const string Package = "Newtonsoft.Json@13.0.4";

        // Negative case: with no window, the count and the render already agreed, and must still.
        var (bareCountExit, bareCountOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--count", "--tips", "q");
        var (bareRowsExit, bareRowsOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--format=jsonl", "--tips", "q");
        Assert.Equal(0, bareCountExit);
        Assert.Equal(0, bareRowsExit);
        var bareRows = bareRowsOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(bareRows > 2, "The section must have more rows than the window keeps for this test to prove anything.");
        Assert.Equal(bareRows, int.Parse(bareCountOutput.Trim(), CultureInfo.InvariantCulture));

        var (countExit, countOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--rows", "2..3", "--count", "--tips", "q");
        Assert.Equal(0, countExit);
        Assert.Equal(2, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));

        var (jsonlExit, jsonlOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--rows", "2..3", "--format=jsonl", "--tips", "q");
        Assert.Equal(0, jsonlExit);
        Assert.Equal(2, jsonlOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        // The window is absolute, so it names rows 2 and 3 of the section rather than the first
        // two, and the header survives it. Derive that expectation from the unwindowed render so
        // the assertion pins the windowing semantics rather than this package's field list.
        var (fullTsvExit, fullTsvOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--format=tsv", "--tips", "q");
        Assert.Equal(0, fullTsvExit);
        var fullTsvLines = fullTsvOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(fullTsvLines.Length > 3, "The section must have at least three data rows for the window to exclude one.");

        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "package", Package, "-S", "Package Info", "--rows", "2..3", "--format=tsv", "--tips", "q");
        Assert.Equal(0, tsvExit);
        var tsvLines = tsvOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal<string>([fullTsvLines[0], fullTsvLines[2], fullTsvLines[3]], tsvLines);
    }

    /// <summary>
    /// The package half of #3547 gap 6. The map reports a requested section that has no rows as
    /// zero rather than omitting it, which is what makes it a cheap probe of the whole overview -
    /// the same contract a category map already has.
    /// </summary>
    [Fact]
    public async Task Package_BareSelectCount_EmitsFixedOverviewMapIncludingEmptySections()
    {
        var (renderExit, renderOutput, _) = await RunAppAsync(
            "package", "NETStandard.Library@2.0.3", "-S", "--tips", "q");
        Assert.Equal(0, renderExit);

        // This package ships no README, so the section is requested but renders nothing. Without
        // such a section the zero-row half of the claim would be untested.
        Assert.DoesNotContain("## Package README file", renderOutput);

        var (exit, output, error) = await RunAppAsync(
            "package", "NETStandard.Library@2.0.3", "-S", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Section | Count |", output);
        Assert.Contains("| Package README file | 0 |", output);

        foreach (var section in PackageSectionDescriptors.CreatePipeline().BareSelectSectionNames)
            Assert.Contains($"| {section} |", output);
    }

    /// <summary>
    /// The negative case for the widened <c>--count</c> requirement: it accepts bare <c>-S</c>
    /// because that is a selection, not because the requirement was dropped.
    /// </summary>
    [Fact]
    public async Task Package_CountWithoutSelect_StillRequiresASelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--count", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(CountOutput.SectionRequiredMessage, error);
    }

    [Fact]
    public async Task Package_DependencyHierarchyCount_OwnsItsTypedEmptyProjection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.EmptyDependencies",
            "README.md",
            "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "Dependency Hierarchy",
                "--count",
                "--rows",
                "2..2",
                "--tips",
                "q");
            var json = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "Dependency Hierarchy",
                "--count",
                "--format=json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Equal("0", output.Trim());
            Assert.DoesNotContain("unprojected output", error);
            Assert.Equal(0, json.Exit);
            Assert.Equal("0", json.Output.Trim());
            Assert.DoesNotContain("unprojected output", json.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Versions_IncludeUnlisted_Count_MatchesTheListingItRenders()
    {
        // --include-unlisted renders through a separate listing path, so it needs its own
        // projection dispatch; without one the count is dropped and the audit fails the run.
        var (countExit, countOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "--include-unlisted", "--count", "--tips", "q");
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "--include-unlisted", "--format=jsonl", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);

        var rendered = rowsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.True(rendered > 0, "the probe must render rows, or it proves nothing.");
        Assert.Equal(rendered, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Tfms_Count_CountsTheListedFrameworks()
    {
        var (listExit, listOutput, _) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms");
        Assert.Equal(0, listExit);
        var expected = listOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(expected > 0, "The package must list frameworks for this test to prove anything.");

        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expected, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Tfms_SemanticTailSelectsTheSameFrameworkAcrossFormats()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string[] args =
            [
                "package",
                packagePath,
                "--tfms",
                "-n",
                "1",
                "--tail",
                "--tips",
                "q",
            ];

            var markdown = await RunAppAsync(args);
            var table = await RunAppAsync([.. args, "--format=table"]);
            var tsv = await RunAppAsync([.. args, "--format=tsv"]);
            var jsonl = await RunAppAsync([.. args, "--format=jsonl"]);
            var json = await RunAppAsync([.. args, "--format=json"]);
            var count = await RunAppAsync([.. args, "--count"]);

            foreach (var result in new[]
            {
                markdown,
                table,
                tsv,
                jsonl,
                json,
            })
            {
                Assert.Equal(0, result.Exit);
                Assert.Empty(result.Error);
                Assert.Contains("net8.0", result.Output, StringComparison.Ordinal);
                Assert.DoesNotContain("net10.0", result.Output, StringComparison.Ordinal);
            }

            Assert.Single(
                jsonl.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(json.Output);
            JsonElement tfm = Assert.Single(
                document.RootElement.EnumerateArray());
            Assert.Equal(
                "net8.0",
                tfm.GetProperty("tfm").GetString());

            Assert.Equal(0, count.Exit);
            Assert.Empty(count.Error);
            Assert.Equal("1", count.Output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Tfms_UnavailableWindowWithholdsOutput()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--tfms",
                "--rows",
                "2..3",
                "--format=json",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Package TFM row selection stage 1 requires row 3, "
                    + "but only 2 TFM rows are available.",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Tfms_LinesMakesRenderedClippingExplicit()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--tfms",
                "--lines",
                "-n",
                "1",
                "--tail",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("net8.0", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Tfms_RejectInvalidSelectionBeforePackageResolution()
    {
        var legacyCount = await RunAppAsync(
            "--offline",
            "package",
            "Package.That.Must.Not.Resolve",
            "--tfms",
            "--rows",
            "1",
            "--format=json");
        var jsonLines = await RunAppAsync(
            "--offline",
            "package",
            "Package.That.Must.Not.Resolve",
            "--tfms",
            "--lines",
            "-n",
            "1",
            "--format=json");

        Assert.Equal(1, legacyCount.Exit);
        Assert.Empty(legacyCount.Output);
        Assert.Contains(
            "--rows requires N..M, N.., or ..M with positive positions.",
            legacyCount.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            legacyCount.Error,
            StringComparison.Ordinal);

        Assert.Equal(1, jsonLines.Exit);
        Assert.Empty(jsonLines.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            jsonLines.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            jsonLines.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tfms_CompetingLayoutRetainsRenderedLineFallback()
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline",
            "package",
            "Package.That.Must.Not.Resolve",
            "--tfms",
            "--layout",
            "--rows",
            "..1");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Package.That.Must.Not.Resolve",
        "--tree",
        "--tree requires exactly one tree-shaped section (-S Dependencies).")]
    [InlineData(
        "Newtonsoft.Json@1.0.0..2.0.0",
        null,
        "Package range 'Newtonsoft.Json@1.0.0..2.0.0' requires --versions for package inspection.")]
    [InlineData(
        "Example@bad..2.0.0",
        null,
        "Invalid package version 'bad' in range 'Example@bad..2.0.0'.")]
    public async Task Tfms_CompetingTreeAndRangesRetainOwnedDiagnostics(
        string package,
        string? competingOption,
        string expectedError)
    {
        var args = new List<string>
        {
            "--offline",
            "package",
            package,
            "--tfms",
        };
        if (competingOption is not null)
            args.Add(competingOption);
        args.AddRange(["--rows", "1", "--tips", "q"]);

        var (exit, output, error) =
            await RunAppAsync([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            expectedError,
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "--rows requires",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--frontmatter", null, "--frontmatter/--yaml-header and --body require --print or --content.")]
    [InlineData("--body", null, "--frontmatter/--yaml-header and --body require --print or --content.")]
    [InlineData("--print", null, "--print is not available with --tfms")]
    [InlineData("--value", null, "--value is not available with --tfms")]
    [InlineData("--urls", null, "--urls is not available with --tfms")]
    [InlineData("--paths", null, "--paths is not available with --tfms")]
    [InlineData("--roots", null, "--tfms cannot be combined with --roots.")]
    [InlineData("--json-array", null, "--json-array requires --value, --urls, --paths, --roots, or --print.")]
    [InlineData("--row", "1", "--row requires --print, --value, --urls, --paths, or --roots.")]
    [InlineData("--columns", "count", "--fields/--columns are not available with --tfms")]
    [InlineData("--fields", "count", "--fields/--columns are not available with --tfms")]
    [InlineData("--envelope", null, "--envelope cannot be combined with --tfms.")]
    public async Task Tfms_CompetingProjectionsRetainOwnedDiagnostics(
        string option,
        string? value,
        string expectedError)
    {
        var args = new List<string>
        {
            "--offline",
            "package",
            "Package.That.Must.Not.Resolve",
            "--tfms",
            option,
        };
        if (value is not null)
            args.Add(value);
        args.AddRange(["--rows", "1", "--tips", "q"]);

        var (exit, output, error) =
            await RunAppAsync([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expectedError, error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "--rows requires N..M, N.., or ..M with positive positions.",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--type", "Example")]
    [InlineData("--lib", null)]
    [InlineData("--tools", null)]
    [InlineData("--tfm", "net8.0")]
    [InlineData("--match", "first")]
    [InlineData("--skip-empty", null)]
    [InlineData("--prefer-rendered-urls", null)]
    [InlineData("--schema", null)]
    [InlineData("--include-unlisted", null)]
    public async Task Tfms_CompetingModifiersRetainLegacyWindow(
        string option,
        string? value)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var args = new List<string>
            {
                "package",
                packagePath,
                "--tfms",
                option,
            };
            if (value is not null)
                args.Add(value);
            args.AddRange(["--rows", "1", "--tips", "q"]);

            var (exit, output, error) =
                await RunAppAsync([.. args]);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("net10.0", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Tfms_ShapeProjection_IsRefused()
    {
        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms", "--value");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--value is not available with --tfms", error);
    }

    [Fact]
    public async Task Versions_Count_CountsVersionsRatherThanPrintingOne()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "-n", "1", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // The defect printed the version itself here, which parses as neither a count nor a
        // failure, so assert the count and not merely a zero exit.
        Assert.Equal("1", output.Trim());
    }

    [Theory]
    [InlineData("Newtonsoft.Json@13.0.4", "--version", null)]
    [InlineData("Newtonsoft.Json@latest", "--version", null)]
    [InlineData("Newtonsoft.Json", "--versions-with-feed", "1")]
    public async Task Versions_Count_ValidatesTheRenderedBranchColumns(
        string package,
        string option,
        string? value)
    {
        var args = new List<string>
        {
            "package",
            package,
            option,
        };
        if (value is not null)
            args.AddRange(["-n", value]);
        args.AddRange(["--count", "--columns", "Listing", "--tips", "q"]);

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Listing", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_Count_IncludeUnlistedAcceptsListingColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Newtonsoft.Json@latest",
            "--version",
            "--include-unlisted",
            "--count",
            "--columns",
            "Listing",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Package_DiscoverSection_ListsPackageInfoFields()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Published.PackageInfoDiscovery",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "Package Info", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Authors | field |", output);
            Assert.Contains("| Version | field |", output);
            Assert.DoesNotContain("| Published | field |", output);

            var (projectExit, projected, projectError) = await RunAppAsync(
                "package", packagePath, "-S", "Package Info", "--fields", "Authors,Version", "-v:q", "--tips", "q");

            Assert.Equal(0, projectExit);
            Assert.DoesNotContain("not found", projectError);
            Assert.Contains("Authors", projected);
            Assert.Contains("tests", projected);
            Assert.Contains("Version", projected);
            Assert.Contains("1.0.0", projected);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Version")]
    public async Task Package_DefaultColumns_AllMissReportsCleanError(string column)
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.UnmatchedColumn",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--columns", column, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains($"No columns matched projection: {column}", error);
            Assert.DoesNotContain("System.InvalidOperationException", error);
            Assert.DoesNotContain("MarkoutProjection.ComputeColumnMap", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_OverlappingColumnPatterns_AreDeduplicated()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.OverlappingColumns",
            "README.md",
            "# Test package");
        try
        {
            var actual = await RunAppAsync(
                "package", packagePath, "--columns", "Field,*", "--format=table", "--tips", "q");
            var wildcard = await RunAppAsync(
                "package", packagePath, "--columns", "Fie*", "--format=tsv", "--tips", "q");
            var expected = await RunAppAsync(
                "package", packagePath, "--columns", "Field,Value", "--format=table", "--tips", "q");

            Assert.Equal(0, actual.Exit);
            Assert.Equal(0, wildcard.Exit);
            Assert.Equal(0, expected.Exit);
            Assert.Empty(actual.Error);
            Assert.Empty(wildcard.Error);
            Assert.Empty(expected.Error);
            Assert.Equal(expected.Output, actual.Output);
            Assert.StartsWith("field\n", wildcard.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DefaultFields_ValidProjectionStillRenders()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.ValidField",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--fields", "Authors", "--tips", "q");
            var mixed = await RunAppAsync(
                "package", packagePath,
                "-S", "Package Info",
                "--fields", "Version",
                "--columns", "Value",
                "--format=tsv",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Authors | tests |", output);
            Assert.Equal(0, mixed.Exit);
            Assert.Empty(mixed.Error);
            Assert.Contains("1.0.0", mixed.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiSectionProjectionMatchesAcrossTheDocument()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            // A projection is a document-wide allow list. Tables that do not expose a requested
            // column contribute nothing; the request succeeds when another selected table does.
            var (normalExit, normalOutput, normalError) = await RunAppAsync(
                "package", packagePath, "-v:n", "--columns", "TFM", "--tips", "q");

            Assert.Equal(0, normalExit);
            Assert.Empty(normalError);
            Assert.Contains("| TFM |", normalOutput);
            Assert.Contains("net8.0", normalOutput);

            var (overviewExit, overviewOutput, overviewError) = await RunAppAsync(
                "package", packagePath, "-S", "--columns", "Path", "--tips", "q");

            Assert.Equal(0, overviewExit);
            Assert.Empty(overviewError);
            Assert.Contains("| Path |", overviewOutput);
            Assert.Contains("README.md", overviewOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DefaultFieldsWithoutDataRemainNonFatal()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.EmptyFieldProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "package", packagePath, "--fields", "Downloads", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Contains("Note: 1 field has no data: Downloads", error);
            Assert.DoesNotContain("No fields matched projection", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiPackageFormatRefusalPrecedesProjectionHandling()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath, "--columns", "Missing", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Multiple package output requires --format json or a row format", error);
            Assert.DoesNotContain("No columns matched projection", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsPublishedPackageInfoField()
    {
        var (exit, output, error) = await RunAppAsync("package", "-D", "Package Info", "--schema", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Published | field |", output);
    }

    [Fact]
    public async Task Package_DiscoverSection_ListsSignalsColumns()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "Signals", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("not found", error);
            Assert.Contains("| Area | column |", output);
            Assert.Contains("| Signal | column |", output);
            Assert.Contains("| Value | column |", output);
            Assert.Contains("| Evidence | column |", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsArtifactTextAuditColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "-D",
            PackageSections.AuditArtifactText,
            "--schema",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Location | column |", output);
        Assert.Contains("| Concerns | column |", output);
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsIdentifierConfusionAuditColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "-D",
            PackageSections.AuditIdentifierConfusion,
            "--schema",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Location | column |", output);
        Assert.Contains("| Kind | column |", output);
        Assert.Contains("| Concern | column |", output);
        Assert.Contains("| Reserved Prefix | column |", output);
        Assert.Contains("| Similarity | column |", output);
        Assert.Contains("| Characters | column |", output);
    }

    [Fact]
    public async Task Package_DiscoverTree_UsesDiscoveryTreeNotFileTree()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.DiscoveryTree",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-D", "Package Info",
                "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Authors", output);
            Assert.DoesNotContain("README.md", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyHierarchy_DiscoveryUsesDistinctSchemaAndObsoleteInputFails()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            string missingSource = Path.Combine(
                tempDir,
                "missing-source");
            var effective = await RunAppAsync(
                "package", packagePath, "-D", "Dependency Hierarchy",
                "--source", missingSource, "--tips", "q");
            var tree = await RunAppAsync(
                "package", packagePath, "-D", "Dependency Hierarchy",
                "--tree", "--source", missingSource, "--tips", "q");
            var obsolete = await RunAppAsync(
                "package", packagePath, "-D", "--dependencies", "--tips", "q");
            var schema = await RunAppAsync(
                "package", "-D", "Dependency Hierarchy", "--schema",
                "--tips", "q");

            Assert.Equal(0, effective.Exit);
            Assert.Empty(effective.Error);
            Assert.Contains("Occurrence", effective.Output);

            Assert.Equal(0, tree.Exit);
            Assert.Empty(tree.Error);
            Assert.Contains("Occurrence", tree.Output);
            Assert.Contains("Parent Occurrence", tree.Output);
            Assert.DoesNotContain(
                "test.dependency.one",
                tree.Output,
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(0, schema.Exit);
            Assert.Empty(schema.Error);
            Assert.Contains("Occurrence", schema.Output);
            Assert.Contains("Edge ID", schema.Output);
            Assert.DoesNotContain("Manifest", schema.Output);
            Assert.DoesNotContain("Package Info", schema.Output);

            Assert.Equal(1, obsolete.Exit);
            Assert.Empty(obsolete.Output);
            Assert.Contains(
                "--dependencies has been removed",
                obsolete.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencySection_DefaultsToFlatTfmScopedTable()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Dependencies",
                "--tfm", "net9.0",
                "--source", Path.Combine(tempDir, "missing-feed"),
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## Dependencies", output);
            Assert.Contains("| net9.0 | Test.Dependency.One |", output);
            Assert.Contains("| net9.0 | Test.Dependency.Two |", output);
            Assert.DoesNotContain("| net8.0 |", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyHierarchy_PreservesDependsContentAndProjectsTree()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var package = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir, "--format=json",
                "--tips", "q");
            var depends = await RunAppAsync(
                "depends", "--package", packagePath,
                "--tfm", "net9.0", "--source", tempDir,
                "-S", "Dependency Hierarchy", "--format=json");
            var tree = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir, "--tree",
                "--tips", "q");

            Assert.Equal(0, package.Exit);
            Assert.Empty(package.Error);
            Assert.Equal(0, depends.Exit);
            Assert.Empty(depends.Error);
            Assert.Equal(0, tree.Exit);
            Assert.Empty(tree.Error);
            using JsonDocument packageJson =
                JsonDocument.Parse(package.Output);
            using JsonDocument dependsJson =
                JsonDocument.Parse(depends.Output);
            JsonElement packageHierarchy =
                packageJson.RootElement.GetProperty(
                    "dependency_hierarchy");
            Assert.True(JsonElement.DeepEquals(
                packageHierarchy.GetProperty("summary"),
                dependsJson.RootElement.GetProperty("summary")));
            Assert.True(JsonElement.DeepEquals(
                packageHierarchy.GetProperty("roots"),
                dependsJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("roots")));
            Assert.True(JsonElement.DeepEquals(
                packageHierarchy.GetProperty("occurrences"),
                dependsJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")));
            JsonElement[] sharedOccurrences =
            [
                .. packageHierarchy.GetProperty("occurrences")
                    .EnumerateArray()
                    .Where(occurrence =>
                        occurrence.GetProperty("target_identity")
                            .GetProperty("package")
                            .GetProperty("id")
                            .GetString()
                        == "test.dependency.shared"),
            ];
            Assert.Equal(2, sharedOccurrences.Length);
            Assert.Equal(
                2,
                sharedOccurrences
                    .Select(occurrence =>
                        occurrence.GetProperty("parent_occurrence_id")
                            .GetInt32())
                    .Distinct()
                    .Count());
            Assert.Contains(
                sharedOccurrences,
                occurrence =>
                    occurrence.GetProperty("disposition").GetString()
                    == "Expanded");
            Assert.Contains(
                sharedOccurrences,
                occurrence =>
                    occurrence.GetProperty("disposition").GetString()
                    == "Revisit");
            Assert.Contains("test.dependency.one", tree.Output);
            Assert.Contains("test.dependency.two", tree.Output);
            Assert.Contains("test.dependency.shared", tree.Output);
            Assert.Contains(
                "(revisit) test.dependency.shared",
                tree.Output);
            Assert.DoesNotContain("## Dependencies", tree.Output);

            var boundedPackage = await RunAppAsync(
                "package", packagePath,
                "-S", "Dependency Hierarchy",
                "--depth", "1",
                "--tfm", "net9.0",
                "--source", tempDir,
                "--format=json",
                "--tips", "q");
            var boundedDepends = await RunAppAsync(
                "depends", "--package", packagePath,
                "-S", "Dependency Hierarchy",
                "--depth", "1",
                "--tfm", "net9.0",
                "--source", tempDir,
                "--format=json");
            Assert.Equal(0, boundedPackage.Exit);
            Assert.Empty(boundedPackage.Error);
            Assert.Equal(0, boundedDepends.Exit);
            Assert.Empty(boundedDepends.Error);
            using JsonDocument boundedPackageJson =
                JsonDocument.Parse(boundedPackage.Output);
            using JsonDocument boundedDependsJson =
                JsonDocument.Parse(boundedDepends.Output);
            JsonElement boundedPackageOccurrences =
                boundedPackageJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            JsonElement boundedDependsOccurrences =
                boundedDependsJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            Assert.True(JsonElement.DeepEquals(
                boundedPackageOccurrences,
                boundedDependsOccurrences));
            Assert.DoesNotContain(
                boundedPackageOccurrences.EnumerateArray(),
                occurrence =>
                    occurrence.GetProperty("target_identity")
                        .GetProperty("package")
                        .GetProperty("id")
                        .GetString()
                    == "test.dependency.shared");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ObsoleteDependenciesInputAlwaysUsesRemovalDiagnostic()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            (string Lens, string[] Arguments)[] cases =
            [
                ("--layout", ["--layout"]),
                ("--tfms", ["--tfms"]),
                ("--versions", ["--versions"]),
                ("--content", ["--content", "--path", "README.md"]),
            ];

            foreach (var (lens, arguments) in cases)
            {
                var (exit, output, error) = await RunAppAsync(
                    ["package", packagePath, "--dependencies", .. arguments, "--tips", "q"]);

                Assert.Equal(1, exit);
                Assert.Empty(output);
                Assert.Contains("--dependencies has been removed", error);
                Assert.DoesNotContain(
                    $"cannot be combined with {lens}",
                    error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyHierarchy_SupportsStructuredLowerings()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var table = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=table", "--rows", "2", "--tips", "q");
            var json = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=json", "--rows", "2", "--tips", "q");
            var plaintext = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=plaintext", "--rows", "2..3", "--tips", "q");
            var dependsPlaintext = await RunAppAsync(
                "depends", "--package", packagePath,
                "--tfm", "net9.0", "--source", tempDir,
                "-S", "Dependency Hierarchy",
                "--format=plaintext", "--rows", "2..3");
            var composedPackage = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=json", "--rows", "2..4", "-n", "2", "--tips", "q");
            var composedDepends = await RunAppAsync(
                "depends", "--package", packagePath,
                "--tfm", "net9.0", "--source", tempDir,
                "-S", "Dependency Hierarchy",
                "--format=json", "--rows", "2..4", "-n", "2");
            var composedMultiSection = await RunAppAsync(
                "package", packagePath,
                "-S", "Package Info,Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=plaintext", "--rows", "2..4", "-n", "2",
                "--tips", "q");
            var clampedPackage = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--count", "--rows", "2..6", "--tips", "q");
            var clampedDepends = await RunAppAsync(
                "depends", "--package", packagePath,
                "--tfm", "net9.0", "--source", tempDir,
                "-S", "Dependency Hierarchy",
                "--count", "--rows", "2..6");
            var tailPackage = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                "--format=json", "--rows", "2", "--tail", "--tips", "q");
            var tailDepends = await RunAppAsync(
                "depends", "--package", packagePath,
                "--tfm", "net9.0", "--source", tempDir,
                "-S", "Dependency Hierarchy",
                "--format=json", "--rows", "2", "--tail");
            var directDependencyTail = await RunAppAsync(
                "package", packagePath, "-S", "Dependencies",
                "--tfm", "net9.0", "--source", tempDir,
                "--count", "--rows", "1", "--tail", "--tips", "q");

            Assert.Empty(table.Error);
            Assert.Equal(0, table.Exit);
            Assert.Contains("Parent Occurrence", table.Output);
            Assert.Equal(0, json.Exit);
            Assert.Empty(json.Error);
            using JsonDocument document = JsonDocument.Parse(json.Output);
            Assert.Equal(
                2,
                document.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")
                    .GetArrayLength());
            Assert.Equal(0, plaintext.Exit);
            Assert.Empty(plaintext.Error);
            Assert.Equal(0, dependsPlaintext.Exit);
            Assert.Empty(dependsPlaintext.Error);
            Assert.Empty(composedPackage.Error);
            Assert.Equal(0, composedPackage.Exit);
            Assert.Equal(0, composedDepends.Exit);
            Assert.Empty(composedDepends.Error);
            Assert.Equal(0, composedMultiSection.Exit);
            Assert.Empty(composedMultiSection.Error);
            Assert.Equal(0, clampedPackage.Exit);
            Assert.Empty(clampedPackage.Error);
            Assert.Equal("3", clampedPackage.Output.Trim());
            Assert.Equal(clampedPackage, clampedDepends);
            Assert.Empty(tailPackage.Error);
            Assert.Equal(0, tailPackage.Exit);
            Assert.Empty(tailDepends.Error);
            Assert.Equal(0, tailDepends.Exit);
            Assert.Equal(0, directDependencyTail.Exit);
            Assert.Empty(directDependencyTail.Error);
            Assert.Equal("1", directDependencyTail.Output.Trim());
            Assert.Equal(
                2,
                plaintext.Output.Split(
                    "package-dependency",
                    StringSplitOptions.None).Length - 1);
            Assert.Equal(
                dependsPlaintext.Output.Split(
                    "package-dependency",
                    StringSplitOptions.None).Length,
                plaintext.Output.Split(
                    "package-dependency",
                    StringSplitOptions.None).Length);
            using JsonDocument composedPackageJson =
                JsonDocument.Parse(composedPackage.Output);
            using JsonDocument composedDependsJson =
                JsonDocument.Parse(composedDepends.Output);
            JsonElement packageOccurrences =
                composedPackageJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            JsonElement dependsOccurrences =
                composedDependsJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            Assert.Equal(2, packageOccurrences.GetArrayLength());
            Assert.True(JsonElement.DeepEquals(
                packageOccurrences,
                dependsOccurrences));
            using JsonDocument tailPackageJson =
                JsonDocument.Parse(tailPackage.Output);
            using JsonDocument tailDependsJson =
                JsonDocument.Parse(tailDepends.Output);
            JsonElement tailPackageOccurrences =
                tailPackageJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            JsonElement tailDependsOccurrences =
                tailDependsJson.RootElement
                    .GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences");
            Assert.Equal(2, tailPackageOccurrences.GetArrayLength());
            Assert.True(JsonElement.DeepEquals(
                tailPackageOccurrences,
                tailDependsOccurrences));
            Assert.Equal(
                2,
                composedMultiSection.Output.Split(
                    "package-dependency",
                    StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyHierarchy_RowPositionUsesOptionIdentity()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        string rowsDirectory = Path.Combine(tempDir, "rows");
        Directory.CreateDirectory(rowsDirectory);
        foreach (string source in Directory.GetFiles(tempDir, "*.nupkg"))
        {
            File.Copy(
                source,
                Path.Combine(rowsDirectory, Path.GetFileName(source)));
        }

        string relativePackage =
            Path.Combine("rows", Path.GetFileName(packagePath));
        string absolutePackage =
            Path.Combine(rowsDirectory, Path.GetFileName(packagePath));
        try
        {
            var relative = await RunAppInDirectoryAsync(
                tempDir,
                "package", relativePackage,
                "--source", "rows",
                "-S", "Dependency Hierarchy",
                "--tfm", "net9.0",
                "-n", "1", "--rows", "2..2",
                "--count", "--tips", "q");
            var absolute = await RunAppInDirectoryAsync(
                tempDir,
                "package", absolutePackage,
                "--source", rowsDirectory,
                "-S", "Dependency Hierarchy",
                "--tfm", "net9.0",
                "-n", "1", "--rows", "2..2",
                "--count", "--tips", "q");
            var relativeDepends = await RunAppInDirectoryAsync(
                tempDir,
                "depends", "--package", relativePackage,
                "--source", "rows",
                "-S", "Dependency Hierarchy",
                "--tfm", "net9.0",
                "-n", "1", "--rows", "2..2",
                "--count");
            var absoluteDepends = await RunAppInDirectoryAsync(
                tempDir,
                "depends", "--package", absolutePackage,
                "--source", rowsDirectory,
                "-S", "Dependency Hierarchy",
                "--tfm", "net9.0",
                "-n", "1", "--rows", "2..2",
                "--count");

            Assert.Equal(absolute, relative);
            Assert.Equal(0, relative.Exit);
            Assert.Empty(relative.Error);
            Assert.Equal("0", relative.Output.Trim());
            Assert.Equal(absoluteDepends, relativeDepends);
            Assert.Equal(0, relativeDepends.Exit);
            Assert.Empty(relativeDepends.Error);
            Assert.Equal("0", relativeDepends.Output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependenciesCategory_ComposesDirectAndHierarchyResults()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var rendered = await RunAppAsync(
                "package", packagePath, "-S", "@Dependencies",
                "--tfm", "net9.0", "--source", tempDir, "--tips", "q");
            var counted = await RunAppAsync(
                "package", packagePath, "-S", "@Dependencies",
                "--tfm", "net9.0", "--source", tempDir,
                "--count", "--tips", "q");

            Assert.Equal(0, rendered.Exit);
            Assert.Empty(rendered.Error);
            Assert.Contains("## Dependencies", rendered.Output);
            Assert.Contains("## Dependency Hierarchy", rendered.Output);
            Assert.Contains("test.dependency.shared", rendered.Output);

            Assert.Equal(0, counted.Exit);
            Assert.Empty(counted.Error);
            Assert.Contains("| Dependencies | 2 |", counted.Output);
            Assert.Contains("| Dependency Hierarchy | 4 |", counted.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyAlias_PreservesProgrammaticSelectionConflict()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var rendered = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    new InspectionOptions
                    {
                        PackageArgs = [packagePath],
                        ShowDependencies = true,
                        IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            PackageSections.Manifest,
                        },
                    }));

            Assert.Equal(1, rendered.ExitCode);
            Assert.Empty(rendered.Output);
            Assert.Contains("--dependencies has been removed", rendered.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_StaticSchemaDiscovery_HonorsProgrammaticSelection()
    {
        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    Discover = [],
                    Schema = true,
                    Tree = true,
                    IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        PackageSections.Dependencies,
                    },
                }));

        Assert.Equal(0, rendered.ExitCode);
        Assert.Empty(rendered.Error);
        Assert.Contains("Dependencies", rendered.Output);
        Assert.DoesNotContain("Manifest", rendered.Output);
        Assert.DoesNotContain("Package Info", rendered.Output);
    }

    [Fact]
    public async Task Package_StaticSchemaDiscovery_HonorsBareSelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "-D", "--schema", "-S", "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var bareSections =
            PackageSectionDescriptors.CreatePipeline().BareSelectSectionNames;
        Assert.All(
            bareSections,
            section => Assert.Contains(section, output));
        Assert.DoesNotContain("Dependencies", output);

        var synthesized = await RunAppAsync(
            "package", "-D", "--schema", "-S",
            "--path", "README.md", "--tree", "--tips", "q");

        Assert.Equal(0, synthesized.Exit);
        Assert.Empty(synthesized.Error);
        Assert.Contains("Package files", synthesized.Output);
        Assert.DoesNotContain("Package Info", synthesized.Output);
        Assert.DoesNotContain("Manifest", synthesized.Output);
    }

    [Fact]
    public async Task Package_StaticDiscovery_SelectionPreservesCatalogBehavior()
    {
        var unselected = await RunAppAsync("package", "-D", "--tips", "q");
        var selected = await RunAppAsync(
            "package", "-D", "-S", PackageSections.SourceLinkFiles, "--tips", "q");

        Assert.Equal(0, unselected.Exit);
        Assert.Empty(unselected.Error);
        Assert.Equal(unselected, selected);
    }

    [Fact]
    public async Task Package_DependencyHierarchy_OutputFilePreservesWindowsAndInfo()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        var outputPath = Path.Combine(tempDir, "dependencies.md");
        try
        {
            foreach (string[] lineWindow in new[]
                     {
                         new[] { "-n", "2", "--lines" },
                         ["-n", "2", "--tail-lines"],
                     })
            {
                var baseline = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package", packagePath, "-S", "Dependency Hierarchy",
                        "--tree", "--tfm", "net9.0", "--source", tempDir,
                        "--tips", "q",
                        .. lineWindow,
                    ]);
                var redirected = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package", packagePath, "-S", "Dependency Hierarchy",
                        "--tree", "--tfm", "net9.0", "--source", tempDir,
                        "--tips", "q",
                        .. lineWindow,
                        "--output", outputPath,
                    ]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(2, baseline.Output.Count(character => character == '\n'));
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Equal(baseline.Error, redirected.Error);
                Assert.Empty(redirected.Output);
                var written = File.ReadAllText(outputPath);
                Assert.Equal(
                    baseline.Output.ReplaceLineEndings("\n"),
                    written);
                Assert.DoesNotContain('\r', written);
                Assert.False(
                    File.ReadAllBytes(outputPath).AsSpan().StartsWith(
                        new byte[] { 0xEF, 0xBB, 0xBF }));
            }

            var infoBaseline = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tree", "--tfm", "net9.0", "--source", tempDir,
                "--info");
            var infoRedirected = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tree", "--tfm", "net9.0", "--source", tempDir,
                "--info", "--output", outputPath);

            Assert.Equal(0, infoBaseline.Exit);
            Assert.Equal(infoBaseline.Exit, infoRedirected.Exit);
            Assert.Empty(infoRedirected.Output);
            var infoWritten = File.ReadAllText(outputPath);
            Assert.Equal(
                infoBaseline.Output.ReplaceLineEndings("\n"),
                infoWritten);
            Assert.DoesNotContain('\r', infoWritten);

            static string OutputMetric(string error) =>
                SplitOutputLines(error).Single(line =>
                    line.StartsWith("| Output |", StringComparison.Ordinal));

            Assert.Equal(
                $"| Output | {CacheOutputFormatter.FormatSize(infoWritten.Length)} |",
                OutputMetric(infoRedirected.Error));
            Assert.DoesNotContain(
                "| Output | 0 B |",
                infoRedirected.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--format=table", null)]
    [InlineData("--format=tsv", null)]
    [InlineData("--format=jsonl", null)]
    [InlineData("--columns", "Target,Disposition")]
    public async Task Package_DependencyHierarchy_ProjectionHonorsOutputFile(
        string projection,
        string? value)
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        var outputPath = Path.Combine(tempDir, "dependencies.out");
        try
        {
            List<string> arguments =
            [
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tfm", "net9.0", "--source", tempDir,
                projection,
            ];
            if (value is not null)
                arguments.Add(value);
            arguments.AddRange(["--tips", "q"]);

            var baseline = await RunAppInDirectoryAsync(
                tempDir,
                [.. arguments]);
            var redirected = await RunAppInDirectoryAsync(
                tempDir,
                [.. arguments, "--output", outputPath]);

            Assert.Equal(0, baseline.Exit);
            Assert.Empty(baseline.Error);
            Assert.NotEmpty(baseline.Output);
            Assert.Equal(baseline.Exit, redirected.Exit);
            Assert.Equal(baseline.Error, redirected.Error);
            Assert.Empty(redirected.Output);
            Assert.Equal(
                baseline.Output.ReplaceLineEndings("\n"),
                File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyAlias_PrintUsesAliasDiagnostic()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--dependencies", "--print", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--dependencies has been removed", error);
            Assert.DoesNotContain("-S/--select", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyTree_ProgrammaticSelectionRejectsLens()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var rendered = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    new InspectionOptions
                    {
                        PackageArgs = [packagePath],
                        Tree = true,
                        IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            PackageSections.DependencyHierarchy,
                        },
                        ListLayout = true,
                    }));

            Assert.Equal(1, rendered.ExitCode);
            Assert.Empty(rendered.Output);
            Assert.Contains("--tree cannot be combined with --layout", rendered.Error);

            var rowFormat = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    new InspectionOptions
                    {
                        PackageArgs = [packagePath],
                        Tree = true,
                        IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            PackageSections.DependencyHierarchy,
                        },
                        Tsv = true,
                    }));

            Assert.Equal(1, rowFormat.ExitCode);
            Assert.Empty(rowFormat.Output);
            Assert.Contains("--tree cannot be combined with count, shape, tabular, JSON, or field/column projections", rowFormat.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_TreeRequiresDependencyHierarchySelection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.TreeAlias",
            "README.md",
            "# Test package");
        try
        {
            var (exit, _, error) = await RunAppAsync("package", packagePath, "--tree", "--tips", "q");
            var (categoryExit, _, categoryError) = await RunAppAsync(
                "package", packagePath, "-S", "@Dependencies", "--tree", "--tips", "q");
            var (aliasExit, _, aliasError) = await RunAppAsync(
                "package", packagePath, "--dependencies", "-S", "Manifest", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("--tree requires exactly '-S \"Dependency Hierarchy\"'", error);
            Assert.DoesNotContain("--layout", error);
            Assert.Equal(1, categoryExit);
            Assert.Contains("--tree requires exactly '-S \"Dependency Hierarchy\"'", categoryError);
            Assert.DoesNotContain("--layout", categoryError);
            Assert.Equal(1, aliasExit);
            Assert.Contains("--dependencies has been removed", aliasError);
            Assert.DoesNotContain("--tree requires", aliasError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DependencyHierarchy_RejectsRowProjection()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Dependency Hierarchy",
                "--tree", "--count", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--tree cannot be combined with count, shape, tabular, JSON, or field/column projections", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_RejectDependencyHierarchy()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Dependency Hierarchy", "--format=json", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Multiple package inspection cannot include Dependency Hierarchy",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── package command ──────────────────────────────────────────────

    [Fact]
    public async Task Package_NonexistentPackage_ShowsError()
    {
        var options = new InspectionOptions
        {
            PackageArgs = ["NonexistentPackage123456"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Package_Detailed_DoesNotShowSignalsByDefault()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                Verbosity = Verbosity.Detailed
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("## Signals", output);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Discover_DefaultsToEffectiveSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("| Signals | section |", output);
            Assert.Contains("Manifest", output);
            // SourceLink: Files is reachable through its door rather than the top-level
            // catalog, so the door is what discovery has to advertise.
            Assert.Contains("| @Package | category |", output);
            Assert.Contains("| @SourceLink | category |", output);
            Assert.DoesNotContain("| SourceLink: Files | section |", output);
            Assert.DoesNotContain("Vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Discover_OrdersRowsByDiscoveryGroup()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Discovery",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            var rows = ExtractDiscoveryRows(output);

            Assert.Contains(rows, row => row.Name == "Package files" && row.Kind == "section");

            var regular = rows.Where(row => row.Kind == "section").Select(row => row.Name).ToArray();
            var categories = rows.Where(row => row.Kind == "category").Select(row => row.Name).ToArray();

            Assert.Equal(regular.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), regular);
            Assert.Equal(categories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), categories);
            // Curated catalogs lead with the topical doors, then the sections, and no longer
            // annotate rows as opt-in: the size-class and cost axes carry that instead.
            Assert.True(rows.FindLastIndex(row => row.Kind == "category") < rows.FindIndex(row => row.Kind == "section"));
            Assert.DoesNotContain(rows, row => row.Kind == "section (opt-in)");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsStaticSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--schema");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("Signals", output);
            Assert.Contains("SourceLink: Files", output);
            Assert.Contains("Manifest", output);
            Assert.Contains("Vulnerabilities", output);
            // @All/@Default/@Hidden are internal computed poles, not doors: curated discovery
            // advertises only the real category doors.
            Assert.Contains("| @Audit | category |", output);
            Assert.Contains("| @Dependencies | category |", output);
            Assert.Contains("| @Files | category |", output);
            Assert.Contains("| @Package | category |", output);
            Assert.Contains("| @SourceLink | category |", output);
            Assert.DoesNotContain("@All", output);
            Assert.DoesNotContain("@Default", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverPackageCategory_ListsPackageNativeSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "-D", "@Package", "--schema", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Package Info | section |", output);
        Assert.Contains("| Dependencies | section |", output);
        Assert.Contains("| Package files | section |", output);
        Assert.DoesNotContain("| Package README file | section |", output);
        Assert.DoesNotContain("| SourceLink: Files | section |", output);
    }

    [Fact]
    public async Task Package_BareSelect_RendersInfoPreset()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S");

            Assert.Equal(0, exit);
            Assert.Contains("## Package Info", output);
            Assert.Contains("## Manifest", output);
            // Package-growing sections stay out of the fixed overview...
            Assert.DoesNotContain("## Dependencies", output);
            Assert.DoesNotContain("## Target Frameworks", output);
            Assert.DoesNotContain("## Package files", output);
            // ...as do the network-bound ones, however small their row set.
            Assert.DoesNotContain("## Signals", output);
            Assert.DoesNotContain("## Statistics", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// The package command drops the computed <c>@All</c> pole
    /// (<see cref="DotnetInspector.Sections.SectionPipeline{TModel}.WithoutComputedPoles"/>):
    /// its sections are reachable by name, by topical door, and by verbosity, so a pole that
    /// renders a superset nobody asked for is a surface no discovery output describes. This is
    /// the gate that keeps them from being reintroduced.
    /// </summary>
    [Fact]
    public async Task Package_ComputedPoles_AreNotResolvable()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (allExit, _, allError) = await RunAppAsync("package", packagePath, "-S", "@All");
            Assert.Equal(1, allExit);
            Assert.Contains("'@All' not found", allError, StringComparison.Ordinal);

            // @Default is gone everywhere, not just here: it restated what bare -S already means
            // and had no spelling worth keeping (#3547).
            var (defaultExit, defaultOutput, defaultError) = await RunAppAsync("package", packagePath, "-S", "@Default");
            Assert.Equal(1, defaultExit);
            Assert.Contains("'@Default' not found", defaultError, StringComparison.Ordinal);
            Assert.DoesNotContain("## Package Info", defaultOutput);

            var (comboExit, _, comboError) = await RunAppAsync("package", packagePath, "-S", "@Default,Manifest");
            Assert.Equal(0, comboExit);
            Assert.Contains("'@Default' not found", comboError, StringComparison.Ordinal);

            var (bareExit, bareOutput, bareError) = await RunAppAsync("package", packagePath, "-S");
            Assert.Equal(0, bareExit);
            Assert.Contains("## Package Info", bareOutput);
            Assert.DoesNotContain("@Default", bareError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Bare <c>-S</c> is a request for the command's default preset, not a selector value, so it
    /// never appears in diagnostics. It used to travel as the literal string <c>"@Default"</c>,
    /// which meant that combining it with anything that contributes its own selector — here the
    /// <c>--path</c> sugar, which appends the Files section — pushed the internal encoding through
    /// resolution and leaked it as "Select value '@Default' not found" (#3547). The explicit
    /// selection wins, as it always did; only the spurious warning is gone.
    /// </summary>
    [Fact]
    public async Task Package_BareSelect_CombinedWithPathSugar_DoesNotLeakThePresetMarker()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "--path", "*.dll", "--paths");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("@Default", error, StringComparison.Ordinal);
            Assert.DoesNotContain("@Default", output, StringComparison.Ordinal);
            Assert.Contains(".dll", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_BareSelectWithDiscover_ListsSectionsRatherThanFailing()
    {
        // -S <name> -D has always listed the named section. Bare -S -D used to fail instead, but
        // only because the marker was the string "@Default" and package drops the computed poles,
        // so the discovery lookup missed. That is the same defect as gaps 2 and 3, so it clears
        // with them rather than being a separate behavior decision.
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "-D");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("@Default", error, StringComparison.Ordinal);
            Assert.DoesNotContain("@Default", output, StringComparison.Ordinal);
            Assert.Contains("| Package Info | section |", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NormalOutput_RendersLibraryFileSizes()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package files");

            Assert.Equal(0, exit);
            Assert.Contains("## Package files", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.DoesNotContain("| lib/net10.0/Latest.One.xml | 0 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiPackageCount_RendersStructuredSectionMap()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (packageInfoExit, packageInfoOutput, _) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info", "--count", "--format=json");
            var (packageInfoWildcardExit, packageInfoWildcardOutput, packageInfoWildcardError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--columns", "*", "--count", "--format=json");
            var (normalPackageInfoExit, normalPackageInfoOutput, normalPackageInfoError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--count", "--format=json", "-v:n");
            var (targetFrameworksExit, targetFrameworksOutput, _) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Target Frameworks", "--count", "--format=json");

            Assert.Equal(0, packageInfoExit);
            Assert.Equal(0, packageInfoWildcardExit);
            Assert.Empty(packageInfoWildcardError);
            Assert.Equal(packageInfoOutput, packageInfoWildcardOutput);
            Assert.Equal(0, normalPackageInfoExit);
            Assert.Empty(normalPackageInfoError);
            Assert.Equal(0, targetFrameworksExit);
            var packageInfoCount = int.Parse(
                normalPackageInfoOutput.Trim(), CultureInfo.InvariantCulture);
            var targetFrameworksCount = int.Parse(
                targetFrameworksOutput.Trim(), CultureInfo.InvariantCulture);

            var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info,Target Frameworks", "--count", "--format=json");

            Assert.Equal(0, jsonExit);
            Assert.Empty(jsonError);
            using var json = JsonDocument.Parse(jsonOutput);
            var counts = json.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.Ordinal);
            Assert.Equal(packageInfoCount, counts["Package Info"]);
            Assert.Equal(targetFrameworksCount, counts["Target Frameworks"]);

            var (tsvExit, tsvOutput, tsvError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info,Target Frameworks", "--count", "--format=tsv");
            Assert.Equal(0, tsvExit);
            Assert.Empty(tsvError);
            Assert.Equal(
                $"section\tcount\nPackage Info\t{packageInfoCount}\nTarget Frameworks\t{targetFrameworksCount}\n",
                tsvOutput.ReplaceLineEndings("\n"));

            var (markdownExit, markdownOutput, markdownError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info,Target Frameworks", "--count");
            Assert.Equal(0, markdownExit);
            Assert.Empty(markdownError);
            Assert.Contains($"| Package Info | {packageInfoCount} |\n", markdownOutput);
            Assert.Contains($"| Target Frameworks | {targetFrameworksCount} |\n", markdownOutput);

            var (projectedExit, projectedOutput, projectedError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info", "--fields", "Version", "--count", "--format=json");
            var (projectedRowsExit, projectedRowsOutput, projectedRowsError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info", "--fields", "Version", "--format=tsv");
            var (wildcardExit, wildcardOutput, wildcardError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info", "--fields", "V*", "--count");
            Assert.Equal(0, projectedExit);
            Assert.Empty(projectedError);
            Assert.Equal("2\n", projectedOutput.ReplaceLineEndings("\n"));
            Assert.Equal(0, projectedRowsExit);
            Assert.Empty(projectedRowsError);
            Assert.Equal(
                "package\tfield\tvalue\n"
                + "Test.Layout\tVersion\t1.0.0\n"
                + "Test.Layout\tVersion\t1.0.0\n",
                projectedRowsOutput.ReplaceLineEndings("\n"));
            Assert.Equal(0, wildcardExit);
            Assert.Empty(wildcardError);
            Assert.Equal("2\n", wildcardOutput.ReplaceLineEndings("\n"));

            foreach (var columns in new[] { "Version", "V*" })
            {
                var (wrongKindExit, wrongKindOutput, wrongKindError) =
                    await RunAppAsync(
                        "package", packagePath, packagePath,
                        "-S", "Package Info", "--columns", columns, "--count");
                Assert.Equal(1, wrongKindExit);
                Assert.Empty(wrongKindOutput);
                Assert.Contains($"No columns matched projection: {columns}", wrongKindError);
            }

            var (columnMapExit, columnMapOutput, columnMapError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info,Target Frameworks",
                    "--columns", "TFM", "--count", "--format=json");
            Assert.Equal(0, columnMapExit);
            Assert.Empty(columnMapError);
            using (var columnMap = JsonDocument.Parse(columnMapOutput))
            {
                var columnCounts = columnMap.RootElement
                    .EnumerateArray()
                    .ToDictionary(
                        row => row.GetProperty("section").GetString()!,
                        row => row.GetProperty("count").GetInt32(),
                        StringComparer.Ordinal);
                Assert.Equal(0, columnCounts["Package Info"]);
                Assert.Equal(targetFrameworksCount, columnCounts["Target Frameworks"]);
            }

            var (combinedExit, combinedOutput, combinedError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info,Target Frameworks",
                    "--fields", "Version", "--columns", "TFM",
                    "--count", "--format=json");
            var (singleCombinedExit, singleCombinedOutput, singleCombinedError) =
                await RunAppAsync(
                    "package", packagePath,
                    "-S", "Package Info,Target Frameworks",
                    "--fields", "Version", "--columns", "TFM",
                    "--count", "--format=json");
            Assert.Equal(0, combinedExit);
            Assert.Empty(combinedError);
            Assert.Equal(0, singleCombinedExit);
            Assert.Empty(singleCombinedError);
            using var singleCombined = JsonDocument.Parse(singleCombinedOutput);
            var singleCombinedCounts = singleCombined.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.Ordinal);
            using (var combined = JsonDocument.Parse(combinedOutput))
            {
                var combinedCounts = combined.RootElement
                    .EnumerateArray()
                    .ToDictionary(
                        row => row.GetProperty("section").GetString()!,
                        row => row.GetProperty("count").GetInt32(),
                        StringComparer.Ordinal);
                Assert.Equal(
                    2 * singleCombinedCounts["Package Info"],
                    combinedCounts["Package Info"]);
                Assert.Equal(
                    2 * singleCombinedCounts["Target Frameworks"],
                    combinedCounts["Target Frameworks"]);
            }

            var (fileColumnExit, fileColumnOutput, fileColumnError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package README file,Manifest",
                    "--columns", "Kind", "--count", "--format=json");
            Assert.Equal(0, fileColumnExit);
            Assert.Empty(fileColumnError);
            using (var fileColumnMap = JsonDocument.Parse(fileColumnOutput))
            {
                var fileColumnCounts = fileColumnMap.RootElement
                    .EnumerateArray()
                    .ToDictionary(
                        row => row.GetProperty("section").GetString()!,
                        row => row.GetProperty("count").GetInt32(),
                        StringComparer.Ordinal);
                Assert.Equal(0, fileColumnCounts["Package README file"]);
                Assert.True(fileColumnCounts["Manifest"] > 0);
            }

            foreach (var columns in new[] { "Path", "P*" })
            {
                var (extractedColumnExit, extractedColumnOutput, extractedColumnError) =
                    await RunAppAsync(
                        "package", packagePath, packagePath,
                        "-S", "Package README file,Manifest",
                        "--columns", columns, "--count", "--format=json");
                Assert.Equal(0, extractedColumnExit);
                Assert.Empty(extractedColumnError);
                using var extractedColumnMap = JsonDocument.Parse(extractedColumnOutput);
                var extractedColumnCounts = extractedColumnMap.RootElement
                    .EnumerateArray()
                    .ToDictionary(
                        row => row.GetProperty("section").GetString()!,
                        row => row.GetProperty("count").GetInt32(),
                        StringComparer.Ordinal);
                Assert.Equal(2, extractedColumnCounts["Package README file"]);
                Assert.Equal(0, extractedColumnCounts["Manifest"]);
            }

            var (composedExit, composedOutput, composedError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info,Package README file,Manifest",
                    "--fields", "Version", "--columns", "Path",
                    "--count", "--format=json");
            Assert.Equal(0, composedExit);
            Assert.Empty(composedError);
            using (var composedMap = JsonDocument.Parse(composedOutput))
            {
                var composedCounts = composedMap.RootElement
                    .EnumerateArray()
                    .ToDictionary(
                        row => row.GetProperty("section").GetString()!,
                        row => row.GetProperty("count").GetInt32(),
                        StringComparer.Ordinal);
                Assert.Equal(0, composedCounts["Package Info"]);
                Assert.Equal(2, composedCounts["Package README file"]);
                Assert.Equal(0, composedCounts["Manifest"]);
            }

            var (emptyProjectionExit, emptyProjectionOutput, emptyProjectionError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--fields", "Vulnerabilities",
                    "--format=tsv", "--no-header");
            var (emptyCountExit, emptyCountOutput, emptyCountError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--fields", "Vulnerabilities",
                    "--count");
            Assert.Equal(0, emptyProjectionExit);
            Assert.Empty(emptyProjectionOutput);
            Assert.Contains(
                "Note: 1 field has no data: Vulnerabilities",
                emptyProjectionError);
            Assert.Equal(0, emptyCountExit);
            Assert.Equal("0\n", emptyCountOutput.ReplaceLineEndings("\n"));
            Assert.Contains(
                "Note: 1 field has no data: Vulnerabilities",
                emptyCountError);

            var (windowedExit, windowedOutput, windowedError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info", "--count", "--format=tsv", "--rows", "1");
            Assert.Equal(0, windowedExit);
            Assert.Empty(windowedError);
            Assert.Equal("1\n", windowedOutput.ReplaceLineEndings("\n"));

            var (projectedWindowExit, projectedWindowOutput, projectedWindowError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--fields", "Version",
                    "--count", "--format=json", "--rows", "3..3");
            Assert.Equal(0, projectedWindowExit);
            Assert.Empty(projectedWindowError);
            Assert.Equal("0\n", projectedWindowOutput.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiPackageCount_AcquiresSignatureRows()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (singleExit, singleOutput, singleError) = await RunAppAsync(
                "package", packagePath,
                "-S", "Signature", "--count", "--tips", "q");
            var (multiExit, multiOutput, multiError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Signature", "--count", "--tips", "q");
            var (singleInfoExit, singleInfoOutput, singleInfoError) =
                await RunAppAsync(
                    "package", packagePath,
                    "-S", "Package Info", "--count", "--tips", "q");
            var (multiInfoExit, multiInfoOutput, multiInfoError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Package Info", "--count", "--tips", "q");

            Assert.Equal(0, singleExit);
            Assert.Empty(singleError);
            Assert.Equal(0, multiExit);
            Assert.Empty(multiError);
            Assert.Equal(
                2 * int.Parse(singleOutput.Trim(), CultureInfo.InvariantCulture),
                int.Parse(multiOutput.Trim(), CultureInfo.InvariantCulture));
            Assert.Equal(0, singleInfoExit);
            Assert.Empty(singleInfoError);
            Assert.Equal(0, multiInfoExit);
            Assert.Empty(multiInfoError);
            Assert.Equal(
                2 * int.Parse(singleInfoOutput.Trim(), CultureInfo.InvariantCulture),
                int.Parse(multiInfoOutput.Trim(), CultureInfo.InvariantCulture));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiPackageBareCount_PopulatesSelectedFileSections()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "--skip-empty", "--count", "--format=json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var json = JsonDocument.Parse(output);
            var counts = json.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.Ordinal);
            Assert.Equal(2, counts["Package nuspec file"]);
            Assert.Equal(2, counts["Package README file"]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_LegacyFileSectionNames_StillResolve()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            // "Grounding" was this section's canonical name, not a nickname, so scripts
            // spelling it must keep working after the rename.
            var (groundingExit, groundingOutput, _) = await RunAppAsync("package", packagePath, "-S", "Grounding");
            Assert.Equal(0, groundingExit);
            Assert.Contains("## Package README file", groundingOutput);

            var (nuspecExit, nuspecOutput, _) = await RunAppAsync("package", packagePath, "-S", "Files: Nuspec");
            Assert.Equal(0, nuspecExit);
            Assert.Contains("## Package nuspec file", nuspecOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Version", "1.0.0")]
    [InlineData("Authors", "tests")]
    [InlineData("Auth*", "tests")]
    public async Task Package_Value_PrintsPackageInfoField(
        string field,
        string expected)
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Value.PackageInfo", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package Info", "--fields", field, "--value");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(expected, output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ValuePattern_UsesCanonicalPackageInfoFieldName()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Value.PackageInfo.Pattern",
            "README.md",
            "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath,
                "-S", "Package Info",
                "--fields", "Auth*",
                "--value",
                "--format=json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("Authors", document.RootElement.GetProperty("label").GetString());
            Assert.Equal("tests", document.RootElement.GetProperty("value").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_QuietSignedValuePerformsExplicitVerification()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine@2.0.3",
            "-S",
            "Package Info",
            "--fields",
            "Signed",
            "--value",
            "-v:q",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("Yes", output.Trim());
    }

    [Theory]
    [InlineData("Signed")]
    [InlineData("Sign*")]
    public async Task Package_QuietSignedFieldPerformsExplicitVerification(
        string field)
    {
        var (packagePath, tempDir) =
            CreateLocalPackageWithoutReadme("Test.Quiet.Signed.Field");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--fields",
                field,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Signed | No |", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_QuietPackageInfoSelectionDoesNotRequestVerification()
    {
        var (packagePath, tempDir) =
            CreateLocalPackageWithoutReadme("Test.Quiet.Package.Info");
        try
        {
            var implicitInfo = await RunAppAsync(
                "package",
                packagePath,
                "-v:q",
                "--tips",
                "q");
            var explicitInfo = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "Package Info",
                "-v:q",
                "--tips",
                "q");
            var fixedOverview = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "-v:q",
                "--tips",
                "q");

            Assert.Equal(0, implicitInfo.Exit);
            Assert.Equal(0, explicitInfo.Exit);
            Assert.Equal(0, fixedOverview.Exit);
            Assert.Empty(implicitInfo.Error);
            Assert.Empty(explicitInfo.Error);
            Assert.Empty(fixedOverview.Error);
            Assert.DoesNotContain("| Signed |", implicitInfo.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("| Signed |", explicitInfo.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("| Signed |", fixedOverview.Output, StringComparison.Ordinal);
            Assert.Contains("Version: 1.0.0", implicitInfo.Output, StringComparison.Ordinal);
            Assert.Contains("| Version | 1.0.0 |", explicitInfo.Output, StringComparison.Ordinal);
            Assert.Contains("Version: 1.0.0", fixedOverview.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PrintRequiresSingleSelectedSection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Print.Requires.Select", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--print requires -S/--select to match exactly one printable section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_OrdinaryDocumentOutputStillPreservesExactBytes()
    {
        const string bidi = "\u202E";
        var readme = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes($"ordinary{bidi}readme\r\n"))
            .ToArray();
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Ordinary.ExactOutput",
            "README.md",
            "placeholder");
        var packageRoot = Path.Combine(tempDir, "content");
        File.WriteAllBytes(Path.Combine(packageRoot, "README.md"), readme);
        File.Delete(packagePath);
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        var outputPath = Path.Combine(tempDir, "exported.md");

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--content", "--bare",
                "--output", outputPath);

            Assert.Equal(0, exit);
            Assert.Empty(output);
            Assert.Empty(error);
            Assert.Equal(readme, File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ReportsAgentDocumentation()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.AgentDocs.Signal", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("| Documentation | Agent documentation | Yes | AGENTS.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAgents_TsvResolvesAgentsRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Agents.One", "README.md", "readme", "agents one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Agents.Two", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--format=tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.Agents.One\t1.0.0\tAGENTS.md\t10", output);
            Assert.Contains("Test.Agents.Two\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathFirst_UsesFirstMatchingSelector()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Match.First", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Match.Second", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--path", "@readme", "--match", "first", "--format=tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.First\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.Second\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAll_ReturnsAllMatchingSelectors()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Match.All", "README.md", "readme", "agents");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, packagePath, "--path", "@agents", "--path", "README.md", "--format=tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.All\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.All\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathSkipEmpty_OmitsEmptyPackages()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Skip.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Skip.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--skip-empty", "--format=tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Skip.HasAgents", output);
            Assert.DoesNotContain("Test.Skip.NoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_JsonEmitsArray()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Json.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Json.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--format=json");

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(output);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("Test.Json.One", doc.RootElement[0].GetProperty("package_name").GetString());
            Assert.Equal("Test.Json.Two", doc.RootElement[1].GetProperty("package_name").GetString());
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_SignatureJsonPopulatesEachSubject()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Signature.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Signature.Two",
                "README.md",
                "two");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(2, document.RootElement.GetArrayLength());
            Assert.All(
                document.RootElement.EnumerateArray(),
                package =>
                {
                    JsonElement signature =
                        package.GetProperty("signature_result");
                    Assert.Equal(
                        JsonValueKind.Object,
                        signature.ValueKind);
                    Assert.True(
                        signature.GetProperty("is_unsigned").GetBoolean());
                });
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_CountProjectionMissReportsCleanError()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Signature.Projection.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Signature.Projection.Two",
                "README.md",
                "two");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=json",
                "--count",
                "--columns",
                "Publisher");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "No columns matched projection: Publisher",
                error);
            Assert.DoesNotContain(
                "System.InvalidOperationException",
                error);
            Assert.DoesNotContain(
                "MarkoutWriter.ThrowIfProjectionMatchedNothing",
                error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_SignatureCountIgnoresPresentationAndWindowsCombinedRows()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Signature.Count.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Signature.Count.Two",
                "README.md",
                "two");
        try
        {
            var json = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=json",
                "--count");
            var defaultFormat = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--count");
            var tsv = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=tsv",
                "--count");
            var windowed = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=json",
                "--count",
                "--rows",
                "1");
            var table = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--format=table");
            var tsvRows = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--fields",
                "Signed",
                "--columns",
                "Package;Value",
                "--format=tsv");
            var jsonl = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--fields",
                "Signed",
                "--format=jsonl");
            var overlappingFields = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--fields",
                "Signed;*",
                "--format=tsv");
            var overlappingCount = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--fields",
                "Signed;*",
                "--count");

            Assert.Equal(0, json.Exit);
            Assert.Equal(0, defaultFormat.Exit);
            Assert.Equal(0, tsv.Exit);
            Assert.Equal(0, windowed.Exit);
            Assert.Equal(0, table.Exit);
            Assert.Equal(0, tsvRows.Exit);
            Assert.Equal(0, jsonl.Exit);
            Assert.Equal(0, overlappingFields.Exit);
            Assert.Equal(0, overlappingCount.Exit);
            Assert.Empty(json.Error);
            Assert.Empty(defaultFormat.Error);
            Assert.Empty(tsv.Error);
            Assert.Empty(windowed.Error);
            Assert.Empty(table.Error);
            Assert.Empty(tsvRows.Error);
            Assert.Empty(jsonl.Error);
            Assert.Empty(overlappingFields.Error);
            Assert.Empty(overlappingCount.Error);
            Assert.Equal(json.Output, defaultFormat.Output);
            Assert.Equal(json.Output, tsv.Output);
            Assert.True(
                int.Parse(json.Output, CultureInfo.InvariantCulture) > 1);
            Assert.Equal("1", windowed.Output.Trim());
            Assert.Contains("Test.Signature.Count.One", table.Output);
            Assert.Contains("Test.Signature.Count.Two", table.Output);
            Assert.Equal(
                [
                    "package\tvalue",
                    "Test.Signature.Count.One\tNo",
                    "Test.Signature.Count.Two\tNo",
                ],
                SplitOutputLines(tsvRows.Output));
            foreach (string row in SplitOutputLines(jsonl.Output))
            {
                using var document = JsonDocument.Parse(row);
                Assert.Equal(
                    ["package", "field", "value"],
                    document.RootElement
                        .EnumerateObject()
                        .Select(property => property.Name));
                Assert.Equal(
                    "Signed",
                    document.RootElement.GetProperty("field").GetString());
                Assert.Equal(
                    "No",
                    document.RootElement.GetProperty("value").GetString());
            }
            Assert.Equal(
                SplitOutputLines(overlappingFields.Output).Length - 1,
                int.Parse(
                    overlappingCount.Output,
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FixedOverviewCountValidatesFieldsAndRenderedColumns()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Overview.Projection.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Overview.Projection.Two",
                "README.md",
                "two");
        try
        {
            var count = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "--count",
                "--fields",
                "Bogus");
            var renderedColumn = await RunAppAsync(
                "package",
                firstPackage,
                "-S",
                "--count",
                "--columns",
                "Field");
            var rendered = await RunAppAsync(
                "package",
                firstPackage,
                "-S",
                "--columns",
                "Field");
            var selectedCount = await RunAppAsync(
                "package",
                firstPackage,
                "-S",
                "Signature",
                "--count",
                "--columns",
                "Field");
            var selectedRendered = await RunAppAsync(
                "package",
                firstPackage,
                "-S",
                "Signature",
                "--columns",
                "Field",
                "--format=table");
            var selectedUnknownColumn = await RunAppAsync(
                "package",
                firstPackage,
                "-S",
                "Signature",
                "--count",
                "--columns",
                "Bogus");

            Assert.Equal(1, count.Exit);
            Assert.Equal(0, renderedColumn.Exit);
            Assert.Equal(0, rendered.Exit);
            Assert.Equal(0, selectedCount.Exit);
            Assert.Equal(0, selectedRendered.Exit);
            Assert.Equal(1, selectedUnknownColumn.Exit);
            Assert.Empty(count.Output);
            Assert.Empty(renderedColumn.Error);
            Assert.Empty(rendered.Error);
            Assert.Empty(selectedCount.Error);
            Assert.Empty(selectedRendered.Error);
            Assert.Contains(
                "No columns matched projection: Bogus",
                selectedUnknownColumn.Error);
            Assert.Contains(
                "No fields matched projection: Bogus",
                count.Error);
            Assert.DoesNotContain(
                "| Package Info | 0 |",
                renderedColumn.Output);
            Assert.DoesNotContain(
                "| Signature | 0 |",
                renderedColumn.Output);
            Assert.Contains(
                "| Field |",
                rendered.Output);
            Assert.Contains(
                "Field",
                selectedRendered.Output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_MixedCountMapUsesCombinedColumns()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.MixedProjection.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.MixedProjection.Two",
                "README.md",
                "two");
        try
        {
            var bothSections = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info,Package README file",
                "--count",
                "--columns",
                "Package,Path");
            var packageInfoOnly = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info,Package README file",
                "--count",
                "--columns",
                "Field");
            var fixedOverview = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "--count",
                "--columns",
                "Package");
            var valueColumn = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "--count",
                "--columns",
                "Value");
            var signatureCount = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Signature",
                "--count",
                "--columns",
                "Package");

            Assert.Equal(0, bothSections.Exit);
            Assert.Equal(0, packageInfoOnly.Exit);
            Assert.Equal(0, fixedOverview.Exit);
            Assert.Equal(0, valueColumn.Exit);
            Assert.Equal(0, signatureCount.Exit);
            Assert.Empty(bothSections.Error);
            Assert.Empty(packageInfoOnly.Error);
            Assert.Empty(fixedOverview.Error);
            Assert.Empty(valueColumn.Error);
            Assert.Empty(signatureCount.Error);
            string packageInfoRow = Assert.Single(
                bothSections.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries),
                row => row.StartsWith(
                    "| Package Info |",
                    StringComparison.Ordinal));
            Assert.Contains(
                "| Package README file | 2 |",
                bothSections.Output);
            Assert.Contains(packageInfoRow, packageInfoOnly.Output);
            Assert.DoesNotContain(
                "| Package Info | 0 |",
                packageInfoOnly.Output);
            Assert.Contains(
                "| Package README file | 0 |",
                packageInfoOnly.Output);
            Assert.Contains(packageInfoRow, fixedOverview.Output);
            Assert.Contains(
                "| Package README file | 2 |",
                fixedOverview.Output);
            Assert.Contains(
                $"| Signature | {signatureCount.Output.Trim()} |",
                fixedOverview.Output);
            Assert.DoesNotContain(
                "| Signature | 0 |",
                valueColumn.Output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_MultiSectionFileCountsHonorEmptyRows()
    {
        var (withReadme, withReadmeDir) =
            CreateLocalReadmePackage(
                "Test.CountMap.Readme",
                "README.md",
                "readme");
        var (withoutReadme, withoutReadmeDir) =
            CreateLocalPackageWithoutReadme(
                "Test.CountMap.NoReadme");
        try
        {
            var count = await RunAppAsync(
                "package",
                withReadme,
                withoutReadme,
                "-S",
                "Package README file,Signature",
                "--count");
            var skipEmpty = await RunAppAsync(
                "package",
                withReadme,
                withoutReadme,
                "-S",
                "Package README file,Signature",
                "--count",
                "--skip-empty");
            var tail = await RunAppAsync(
                "package",
                withReadme,
                withoutReadme,
                "-S",
                "Package README file",
                "--columns",
                "Path",
                "--rows",
                "1",
                "--tail",
                "--format=tsv");
            var tailWithoutHeader = await RunAppAsync(
                "package",
                withReadme,
                withoutReadme,
                "-S",
                "Package README file",
                "--columns",
                "Path",
                "--rows",
                "1",
                "--tail",
                "--format=tsv",
                "--no-header");

            Assert.Equal(0, count.Exit);
            Assert.Equal(0, skipEmpty.Exit);
            Assert.Equal(0, tail.Exit);
            Assert.Equal(0, tailWithoutHeader.Exit);
            Assert.Empty(count.Error);
            Assert.Empty(skipEmpty.Error);
            Assert.Empty(tail.Error);
            Assert.Empty(tailWithoutHeader.Error);
            Assert.Contains(
                "| Package README file | 2 |",
                count.Output);
            Assert.Contains(
                "| Package README file | 1 |",
                skipEmpty.Output);
            Assert.Equal(
                "path\n\n",
                tail.Output.ReplaceLineEndings("\n"));
            Assert.Equal(
                "\n",
                tailWithoutHeader.Output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(withReadmeDir, recursive: true);
            Directory.Delete(withoutReadmeDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_FileProjectionAgreesAcrossCountAndRows()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.FileProjection.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.FileProjection.Two",
                "README.md",
                "two");
        try
        {
            var count = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package README file",
                "--count",
                "--columns",
                "Package");
            var rows = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package README file",
                "--format=tsv",
                "--columns",
                "Package");
            var pathCount = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@readme",
                "--count",
                "--columns",
                "Package");
            var fieldCount = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package files",
                "--count",
                "--fields",
                "Path");

            Assert.Equal(0, count.Exit);
            Assert.Equal(0, rows.Exit);
            Assert.Equal(0, pathCount.Exit);
            Assert.Equal(0, fieldCount.Exit);
            Assert.Empty(count.Error);
            Assert.Empty(rows.Error);
            Assert.Empty(pathCount.Error);
            Assert.Empty(fieldCount.Error);
            Assert.Equal("2", count.Output.Trim());
            Assert.Equal("2", pathCount.Output.Trim());
            Assert.True(
                int.Parse(
                    fieldCount.Output,
                    CultureInfo.InvariantCulture) > 2);
            Assert.Equal(
                [
                    "package",
                    "Test.FileProjection.One",
                    "Test.FileProjection.Two",
                ],
                SplitOutputLines(rows.Output));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_DiscoverRefusalPrecedesCountProjection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiDiscoverProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "-D",
                "--count",
                "--fields",
                "Name");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Multiple package inspection cannot be combined with -D/--discover.",
                error);
            Assert.DoesNotContain(
                "No fields matched projection",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiSectionCountUsesTheReducedTableShape()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiSectionCountFormat",
            "README.md",
            "# Test package");
        try
        {
            var single = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "@Files",
                "--format=jsonl",
                "--count");
            var multiple = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "-S",
                "@Files",
                "--format=tsv",
                "--count");
            var fixedOverview = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "-S",
                "--format=tsv",
                "--count");

            Assert.Equal(0, single.Exit);
            Assert.Equal(0, multiple.Exit);
            Assert.Equal(0, fixedOverview.Exit);
            Assert.Empty(single.Error);
            Assert.Empty(multiple.Error);
            Assert.Empty(fixedOverview.Error);
            Assert.All(
                single.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries),
                line =>
                {
                    using var row = JsonDocument.Parse(line);
                    Assert.Equal(JsonValueKind.String, row.RootElement.GetProperty("section").ValueKind);
                    Assert.Equal(JsonValueKind.Number, row.RootElement.GetProperty("count").ValueKind);
                });
            Assert.StartsWith(
                "section\tcount\n",
                multiple.Output.ReplaceLineEndings("\n"));
            Assert.StartsWith(
                "section\tcount\n",
                fixedOverview.Output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_LibraryModesAreRejected()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiLibraryMode",
            "README.md",
            "# Test package");
        try
        {
            var library = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "--library",
                "Test.Package.MultiLibraryMode.dll",
                "--format=tsv");
            var allLibraries = await RunAppAsync(
                "package",
                packagePath,
                packagePath,
                "--library",
                "--format=tsv");

            Assert.Equal(1, library.Exit);
            Assert.Equal(1, allLibraries.Exit);
            Assert.Empty(library.Output);
            Assert.Empty(allLibraries.Output);
            Assert.Contains(
                "Multiple package inspection cannot be combined with --library.",
                library.Error);
            Assert.Contains(
                "Multiple package inspection cannot be combined with --library.",
                allLibraries.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PackageInfoFieldsAgreeWithCount()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Fields.One",
                "README.md",
                "one",
                extraNuspecMetadata:
                """
                <licenseUrl>https://example.test/license</licenseUrl>
                """);
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Fields.Two",
                "README.md",
                "two",
                extraNuspecMetadata:
                """
                <licenseUrl>https://example.test/license</licenseUrl>
                """);
        try
        {
            var count = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "Ver*",
                "--columns",
                "Package",
                "--format=tsv",
                "--count");
            var rendered = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "Ver*",
                "--format=tsv");
            var unprojected = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--format=tsv");
            var column = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--columns",
                "Field",
                "--format=tsv",
                "--rows",
                "1");
            var ordered = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "Authors;Version",
                "--format=tsv");
            var absent = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "Owners;Version",
                "--format=tsv");
            var valueOnly = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "Version",
                "--columns",
                "Value",
                "--format=tsv");
            var overlappingNames = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package Info",
                "--fields",
                "License;License URL",
                "--format=tsv");

            Assert.Equal(0, count.Exit);
            Assert.Equal(0, rendered.Exit);
            Assert.Equal(0, unprojected.Exit);
            Assert.Equal(0, column.Exit);
            Assert.Equal(0, ordered.Exit);
            Assert.Equal(0, absent.Exit);
            Assert.Equal(0, valueOnly.Exit);
            Assert.Equal(0, overlappingNames.Exit);
            Assert.Empty(count.Error);
            Assert.Empty(rendered.Error);
            Assert.Empty(unprojected.Error);
            Assert.Empty(column.Error);
            Assert.Empty(ordered.Error);
            Assert.Contains(
                "Note: 1 field has no data: Owners",
                absent.Error);
            Assert.Empty(valueOnly.Error);
            Assert.Contains(
                "Note: 1 field has no data: License",
                overlappingNames.Error);
            string[] overlappingRows =
                SplitOutputLines(overlappingNames.Output);
            Assert.Equal(3, overlappingRows.Length);
            Assert.All(
                overlappingRows.Skip(1),
                row => Assert.Contains(
                    "\tLicense URL\t",
                    row,
                    StringComparison.Ordinal));
            Assert.Equal(
                ["value", "1.0.0", "1.0.0"],
                SplitOutputLines(valueOnly.Output));
            Assert.Equal("2", count.Output.Trim());
            string[] rows = SplitOutputLines(rendered.Output);
            Assert.Equal(3, rows.Length);
            Assert.All(
                rows.Skip(1),
                row => Assert.Contains(
                    "\tVersion\t",
                    row,
                    StringComparison.Ordinal));
            Assert.Equal(
                ["field", "Version"],
                SplitOutputLines(column.Output));
            Assert.Equal(
                ["Authors", "Version", "Authors", "Version"],
                SplitOutputLines(ordered.Output)
                    .Skip(1)
                    .Select(row => row.Split('\t')[1]));
            Assert.Equal(
                [
                    "Version",
                    "Type",
                    "Package Size (compressed)",
                    "Built",
                    "Source",
                    "Authors",
                    "License URL",
                    "Readme",
                    "Version",
                    "Type",
                    "Package Size (compressed)",
                    "Built",
                    "Source",
                    "Authors",
                    "License URL",
                    "Readme"
                ],
                SplitOutputLines(unprojected.Output)
                    .Skip(1)
                    .Select(row => row.Split('\t')[1]));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_TableCombinesPackageInfoRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Info.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Info.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--format=table");

            Assert.Equal(0, exit);
            Assert.Contains("Package", output);
            Assert.Contains("Field", output);
            Assert.Contains("Value", output);
            Assert.Contains("Test.Info.One", output);
            Assert.Contains("Test.Info.Two", output);
            Assert.Contains("Version", output);
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-v:d");

            Assert.Equal(0, exit);

            var sectionHeaders = SplitOutputLines(output)
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line[3..])
                .ToArray();

            Assert.NotEmpty(sectionHeaders);
            Assert.Equal(sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(), sectionHeaders);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ShowsMetadataSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Supported TFM", output);
            Assert.Contains("Portable", output);
            Assert.Contains("README", output);
            Assert.Contains("License", output);
            Assert.DoesNotContain("Dependency groups", output);
            Assert.Contains("Direct dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Known vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_Signals_ShowsSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_CountMatchesRenderedRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (renderExit, renderOutput, renderError) = await RunAppAsync(
                "package", packagePath, "-S", "Signals",
                "--format=tsv", "--no-header", "--tips", "q");
            var (countExit, countOutput, countError) = await RunAppAsync(
                "package", packagePath, "-S", "Signals",
                "--count", "--tips", "q");

            Assert.Equal(0, renderExit);
            Assert.Equal(0, countExit);
            Assert.Empty(renderError);
            Assert.Empty(countError);
            var renderedRows = renderOutput.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Length;
            Assert.True(renderedRows > 0);
            Assert.Equal(
                renderedRows.ToString(CultureInfo.InvariantCulture),
                countOutput.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_SignalsCountAggregatesRenderedRows()
    {
        var (firstPackagePath, firstTempDir) = CreateLocalRefPackage("System.Runtime");
        var (secondPackagePath, secondTempDir) = CreateLocalRefPackage("System.Collections");
        try
        {
            var (firstExit, firstOutput, firstError) = await RunAppAsync(
                "package", firstPackagePath, "-S", "Signals", "--count");
            var (secondExit, secondOutput, secondError) = await RunAppAsync(
                "package", secondPackagePath, "-S", "Signals", "--count");
            var (combinedExit, combinedOutput, combinedError) = await RunAppAsync(
                "package", firstPackagePath, secondPackagePath,
                "-S", "Signals", "--count", "--format=json");

            Assert.Equal(0, firstExit);
            Assert.Equal(0, secondExit);
            Assert.Equal(0, combinedExit);
            Assert.Empty(firstError);
            Assert.Empty(secondError);
            Assert.Empty(combinedError);
            var expected = int.Parse(firstOutput, CultureInfo.InvariantCulture)
                + int.Parse(secondOutput, CultureInfo.InvariantCulture);
            Assert.True(expected > 0);
            Assert.Equal(
                expected.ToString(CultureInfo.InvariantCulture),
                combinedOutput.Trim());
        }
        finally
        {
            Directory.Delete(firstTempDir, recursive: true);
            Directory.Delete(secondTempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_FixedOverviewCountPopulatesSections()
    {
        var (firstPackagePath, firstTempDir) =
            CreateLocalReadmePackage(
                "Test.FixedOverview.One",
                "README.md",
                "one");
        var (secondPackagePath, secondTempDir) =
            CreateLocalReadmePackage(
                "Test.FixedOverview.Two",
                "README.md",
                "two");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                firstPackagePath,
                secondPackagePath,
                "-S",
                "--count",
                "--format=json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var counts = document.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.Ordinal);
            Assert.Equal(2, counts["Package nuspec file"]);
            Assert.Equal(6, counts["Signature"]);
        }
        finally
        {
            Directory.Delete(firstTempDir, recursive: true);
            Directory.Delete(secondTempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_SignalsUseSelectedTfm()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath,
                "--tfm", "net9.0", "-S", "Signals", "--format=json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            foreach (var package in document.RootElement.EnumerateArray())
            {
                var directDependencies = Assert.Single(
                    package.GetProperty("audit_signals").EnumerateArray(),
                    signal => signal.GetProperty("signal").GetString() == "Direct dependencies");
                Assert.Equal("2", directDependencies.GetProperty("value").GetString());
                Assert.Equal("net9.0", directDependencies.GetProperty("evidence").GetString());
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_SignalsIncludePackageFileConcerns()
    {
        var (cleanPackage, cleanTempDir) = CreateLocalReadmePackage(
            "Test.Containment.Clean",
            "README.md",
            "readme");
        var (hostilePackage, hostileTempDir) = CreateLocalReadmePackage(
            "Test.Containment.Hostile",
            "README.md",
            "readme",
            extraFiles: [("docs/\u202Esecret.txt", "payload")]);
        try
        {
            var single = await RunAppAsync(
                "package",
                hostilePackage,
                "-v:q",
                "-S",
                PackageSections.Signals,
                "--format=json",
                "--tips",
                "q");
            var multi = await RunAppAsync(
                "package",
                cleanPackage,
                hostilePackage,
                "-v:q",
                "-S",
                PackageSections.Signals,
                "--format=json",
                "--tips",
                "q");

            Assert.True(
                single.Exit == 0,
                $"exit={single.Exit}\nstdout:\n{single.Output}\nstderr:\n{single.Error}");
            Assert.True(
                multi.Exit == 0,
                $"exit={multi.Exit}\nstdout:\n{multi.Output}\nstderr:\n{multi.Error}");
            Assert.Empty(single.Error);
            Assert.Empty(multi.Error);

            using var singleDocument = JsonDocument.Parse(single.Output);
            using var multiDocument = JsonDocument.Parse(multi.Output);
            JsonElement singleSignal = Assert.Single(
                singleDocument.RootElement.GetProperty("audit_signals").EnumerateArray(),
                IsArtifactTextContainmentSignal);
            JsonElement hostileResult = Assert.Single(
                multiDocument.RootElement.EnumerateArray(),
                package => package.GetProperty("package_name").GetString()
                    == "Test.Containment.Hostile");
            JsonElement multiSignal = Assert.Single(
                hostileResult.GetProperty("audit_signals").EnumerateArray(),
                IsArtifactTextContainmentSignal);

            Assert.Equal("Required", singleSignal.GetProperty("value").GetString());
            Assert.Equal("format/bidi (Cf)", singleSignal.GetProperty("evidence").GetString());
            Assert.Equal("Required", multiSignal.GetProperty("value").GetString());
            Assert.Equal("format/bidi (Cf)", multiSignal.GetProperty("evidence").GetString());
        }
        finally
        {
            Directory.Delete(cleanTempDir, recursive: true);
            Directory.Delete(hostileTempDir, recursive: true);
        }

        static bool IsArtifactTextContainmentSignal(JsonElement signal)
            => signal.GetProperty("signal").GetString() == "Artifact text containment";
    }

    [Fact]
    public async Task Package_Signals_RendersAvailableRegistryBackedRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.DoesNotContain("Known vulnerabilities", output);
            Assert.Contains("Dependencies with vulnerabilities", output);
            Assert.Contains("Deprecated dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAudit_InspectsStandalonePackagePdbWithoutAnAssembly()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.StandalonePdbAudit",
            "README.md",
            "readme");
        try
        {
            string hostileAssembly = FixtureCatalog.HostileLiterals.AssemblyPath();
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.CreateEntryFromFile(
                    Path.ChangeExtension(hostileAssembly, ".pdb"),
                    "symbols/AuditCanary.PDB");
            }

            var result = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                $"Signals,{PackageSections.AuditFindings}",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("| Audit | Findings | Detected | 2 findings", result.Output);
            Assert.Contains("1 SourceLink map", result.Output);
            Assert.Contains(
                "| symbols/AuditCanary.PDB | SourceLink control (Cc), format/bidi (Cf) |",
                result.Output);
            Assert.Contains(
                "| symbols/AuditCanary.PDB | SourceLink parent path segment |",
                result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAudit_MalformedStandaloneSourceLinkMapReportsPartial()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.MalformedStandalonePdbAudit",
            "README.md",
            "readme");
        try
        {
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.CreateEntryFromFile(
                    Path.ChangeExtension(
                        FixtureCatalog.SourceLinkMalformed.AssemblyPath(),
                        ".pdb"),
                    "symbols/Malformed.pdb");
            }

            var result = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                $"Signals,{PackageSections.AuditFindings}",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("| Audit | Findings | Partial |", result.Output);
            Assert.Contains(
                "| symbols/Malformed.pdb | invalid SourceLink map |",
                result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
