using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class CloneCandidatesSectionTests
{
    static string FixturePath =>
        FixtureCatalog.CloneSearchMembers.AssemblyPath();

    public CloneCandidatesSectionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public void StructuralSchemaUsesGeneratedCandidateColumns()
    {
        SectionSchema generated = Assert.IsType<SectionSchema>(
            CloneCandidateViewContext.Default
                .GetSchemaInfo<CloneCandidateTableView>()!
                .ToDocumentSchema()
                .GetSection(SectionNames.CloneCandidates));
        SectionSchema structural = Assert.IsType<SectionSchema>(
            LibraryCommand.CreateStructuralSchema()
                .GetSection(SectionNames.CloneCandidates));

        Assert.Equal("column", structural.ItemKind);
        Assert.Equal(
            generated.Items.Select(item => item.Name),
            structural.Items.Select(item => item.Name));
    }

    static Task<(int ExitCode, string Output, string Error)> Run(
        params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });

    [Theory]
    [InlineData("Self", "SimilarNames")]
    [InlineData("Self", "All")]
    [InlineData("SelfAndRegisteredEcosystems", "SimilarNames")]
    [InlineData("SelfAndRegisteredEcosystems", "All")]
    [InlineData("Everything", "SimilarNames")]
    [InlineData("Everything", "All")]
    public async Task Type_AllBreadthAndDiscoveryCombinationsExecute(
        string breadth,
        string discovery)
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--where",
            $"Breadth={breadth}",
            "--where",
            $"Discovery={discovery}",
            "--count",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.True(
            int.TryParse(result.Output.Trim(), out int count)
                && count > 0,
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task Type_DefaultsAndJsonPreservePortableIdentity()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "-n",
            "1",
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement root = json.RootElement;
        Assert.Equal("Everything", root.GetProperty("breadth").GetString());
        Assert.Equal(
            "SimilarNames",
            root.GetProperty("discovery").GetString());
        Assert.True(root.GetProperty("coverage_is_complete").GetBoolean());
        Assert.Equal(1, root.GetProperty("rows").GetArrayLength());

        JsonElement left =
            root.GetProperty("rows")[0].GetProperty("left");
        Assert.StartsWith(
            "DotnetInspector.CloneSearchFixtures!",
            left.GetProperty("address_display").GetString());
        Assert.Equal(
            "Designated",
            left.GetProperty("participant")
                .GetProperty("provenance")
                .GetProperty("kind")
                .GetString());
        Assert.True(
            root.GetProperty("receipt")
                .GetProperty("returned_pairs")
                .GetInt32() > 1);
    }

    [Theory]
    [InlineData("Raise", 1)]
    [InlineData("Value", 2)]
    [InlineData("Changed", 2)]
    public async Task Member_LogicalMemberExpandsAccessorBodies(
        string member,
        int expectedSeedMethods)
    {
        var result = await Run(
            "member",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-m",
            member,
            "-S",
            SectionNames.CloneCandidates,
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            expectedSeedMethods,
            json.RootElement.GetProperty("receipt")
                .GetProperty("seed_methods")
                .GetInt32());
        Assert.Equal(
            expectedSeedMethods,
            json.RootElement.GetProperty("seeds").GetArrayLength());
        Assert.Equal(
            member,
            json.RootElement.GetProperty("seed")
                .GetProperty("member")
                .GetProperty("member_name")
                .GetString());
    }

    [Theory]
    [InlineData("Value:1", 0x06000001)]
    [InlineData("Value:2", 0x06000002)]
    [InlineData("Changed:1", 0x06000003)]
    [InlineData("Changed:2", 0x06000004)]
    public async Task Member_ExplicitAccessorSeedsOnlySelectedBody(
        string member,
        int expectedMethodToken)
    {
        var result = await Run(
            "member",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-m",
            member,
            "-S",
            SectionNames.CloneCandidates,
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            1,
            json.RootElement.GetProperty("receipt")
                .GetProperty("seed_methods")
                .GetInt32());
        Assert.Equal(
            expectedMethodToken,
            json.RootElement.GetProperty("seeds")[0]
                .GetProperty("seed")
                .GetProperty("method_definition_token")
                .GetInt32());
    }

    [Theory]
    [InlineData("Item:1", 2)]
    [InlineData("Item:2", 1)]
    public async Task Member_OverloadedIndexerSelectionUsesSelectedProperty(
        string member,
        int expectedSeedMethods)
    {
        var result = await Run(
            "member",
            "Cases.Lookup",
            "--library",
            FixturePath,
            "-m",
            member,
            "-S",
            SectionNames.CloneCandidates,
            "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            expectedSeedMethods,
            json.RootElement
                .GetProperty("receipt")
                .GetProperty("seed_methods")
                .GetInt32());
    }

    [Fact]
    public async Task Member_OverloadedLogicalMemberRequiresExactSelection()
    {
        var result = await Run(
            "member",
            "Cases.Lookup",
            "--library",
            FixturePath,
            "-m",
            "Item",
            "-S",
            SectionNames.CloneCandidates,
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires a single selected overload",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_BaseCategoryDoesNotRunCloneCandidates()
    {
        var result = await Run(
            "member",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-m",
            "Raise",
            "-S",
            SectionCategoryNames.Member,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            "clone_candidates",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_FieldReportsBodylessFailure()
    {
        var result = await Run(
            "member",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-m",
            "Tag",
            "-S",
            SectionNames.CloneCandidates,
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "SeedMemberHasNoMethodBody",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type", "Cases.Widget", "Type Info")]
    [InlineData("member", "Cases.Widget", "Signature")]
    public async Task PredicatesRejectExplicitNonCloneSection(
        string command,
        string type,
        string section)
    {
        string[] target = command == "member"
            ? [command, type, "--library", FixturePath, "-m", "Value"]
            : [command, type, "--library", FixturePath];
        var result = await Run(
            [
                .. target,
                "-S",
                section,
                "--where",
                "Breadth=Self",
                "-T",
                "q",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"target section '{SectionNames.CloneCandidates}'",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_TabularOutputDisclosesFiniteCliScope()
    {
        var result = await Run(
            "library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--where",
            "Breadth=Everything",
            "-n",
            "1",
            "--table",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Rank", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "this CLI slice supplies the selected exact library only",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_JsonProjectionSupportsColumnsAndRows()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--columns",
            "Rank;Score",
            "-n",
            "2",
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.TryGetProperty("summary", out _));
        JsonElement rows =
            json.RootElement.GetProperty("clone_candidates");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.All(
            rows.EnumerateArray(),
            row => Assert.Equal(
                ["rank", "score"],
                row.EnumerateObject().Select(property => property.Name)));
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task FieldsAreRejectedAcrossOutputFormats(string format)
    {
        var result = await Run(
            "library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            "Breadth",
            format,
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is row-oriented and does not support --fields",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task BareFieldsAreRejectedAcrossOutputFormats(string format)
    {
        var result = await Run(
            "library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            format,
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is row-oriented and does not support --fields",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandlessTypeRouteRejectsBareFields()
    {
        var result = await Run(
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is row-oriented and does not support --fields",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task BareFieldsAreRejectedForAllInspectionRoutes(
        string command)
    {
        string[] target = command switch
        {
            "library" => [command, FixturePath],
            "type" => [command, "Cases.Widget", "--library", FixturePath],
            _ =>
            [
                command,
                "Cases.Widget",
                "--library",
                FixturePath,
                "-m",
                "Raise",
            ],
        };
        var result = await Run(
            [
                .. target,
                "-S",
                SectionNames.CloneCandidates,
                "--fields",
                "--json",
                "-T",
                "q",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is row-oriented and does not support --fields",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageLibraryRouteRejectsBareFields()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"clone-package-{Guid.NewGuid():N}");
        string content = Path.Combine(directory, "content");
        string libraryDirectory = Path.Combine(content, "lib", "net10.0");
        Directory.CreateDirectory(libraryDirectory);
        string libraryName = Path.GetFileName(FixturePath);
        File.Copy(FixturePath, Path.Combine(libraryDirectory, libraryName));
        File.WriteAllText(
            Path.Combine(content, "Clone.Candidates.Tests.nuspec"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Clone.Candidates.Tests</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>Clone Candidates routing fixture</description>
              </metadata>
            </package>
            """);
        string package = Path.Combine(
            directory,
            "Clone.Candidates.Tests.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, package);

        try
        {
            var result = await Run(
                "package",
                package,
                "--library",
                libraryName,
                "-S",
                SectionNames.CloneCandidates,
                "--fields",
                "--json",
                "-T",
                "q");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "is row-oriented and does not support --fields",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Participants")]
    [InlineData("Returned pairs")]
    [InlineData("Ranked pairs")]
    [InlineData("Retrieval pairs")]
    [InlineData("Name comparisons")]
    [InlineData("Participant scope")]
    [InlineData("Work")]
    public async Task SummaryFieldNamesAreRejectedWithoutAliases(
        string field)
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            field,
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is row-oriented and does not support --fields",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task ExplicitSelectionRejectsPerformanceTriageFilter(
        string command)
    {
        string[] target = command switch
        {
            "library" => [command, FixturePath],
            "type" => [command, "Cases.Widget", "--library", FixturePath],
            _ =>
            [
                command,
                "Cases.Widget",
                "--library",
                FixturePath,
                "-m",
                "Raise",
            ],
        };
        var result = await Run(
            [
                .. target,
                "-S",
                SectionNames.CloneCandidates,
                "--where",
                "Member=NoSuchMember",
                "-T",
                "q",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot be combined with Body Shapes or Performance Triage",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitSelectionRejectsPerformanceTriageRanking()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--top",
            "1",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot be combined with Body Shapes or Performance Triage",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Breadth=Unknown", "Unknown Clone Candidates Breadth value")]
    [InlineData("Discovery=Unknown", "Unknown Clone Candidates Discovery value")]
    [InlineData("Breadth=Self", "at most one --where Breadth")]
    public async Task QueryPredicatesRejectInvalidOrDuplicateValues(
        string predicate,
        string expectedError)
    {
        string[] repeated = predicate == "Breadth=Self"
            ? ["--where", predicate, "--where", "Breadth=Everything"]
            : ["--where", predicate];
        var result = await Run(
            [
                "type",
                "Cases.Widget",
                "--library",
                FixturePath,
                .. repeated,
                "-T",
                "q",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            expectedError,
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--tsv", 3)]
    [InlineData("--jsonl", 2)]
    public async Task Type_StreamFormatsEmitOnlyWindowedCandidateRows(
        string format,
        int expectedLines)
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--columns",
            "Rank;Score",
            "-n",
            "2",
            format,
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            expectedLines,
            result.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(
            "this CLI slice supplies the selected exact library only",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticTailSelectsTheSameCandidateAcrossFormats()
    {
        var complete = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--json",
            "-T",
            "q");
        using var completeJson = JsonDocument.Parse(complete.Output);
        JsonElement expectedRow = completeJson.RootElement
            .GetProperty("rows")
            .EnumerateArray()
            .Last();
        string expectedAddress = expectedRow
            .GetProperty("right")
            .GetProperty("address_display")
            .GetString()!;

        foreach (string format in new[]
        {
            "--markdown",
            "--table",
            "--tsv",
            "--jsonl",
            "--json",
        })
        {
            var selected = await Run(
                "type",
                "Cases.Widget",
                "--library",
                FixturePath,
                "-S",
                SectionNames.CloneCandidates,
                "-n",
                "1",
                "--tail",
                format,
                "-T",
                "q");

            Assert.Equal(0, selected.ExitCode);
            Assert.Contains(
                expectedAddress,
                selected.Output,
                StringComparison.Ordinal);
        }

        var projected = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--columns",
            "Right",
            "-n",
            "1",
            "--tail",
            "--json",
            "-T",
            "q");

        Assert.Equal(0, projected.ExitCode);
        using var projectedJson = JsonDocument.Parse(projected.Output);
        Assert.Equal(
            expectedAddress,
            projectedJson.RootElement
                .GetProperty("clone_candidates")[0]
                .GetProperty("right")
                .GetString());
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task CountObservesSemanticHeadAcrossSubjectHosts(
        string command)
    {
        string[] subject = command switch
        {
            "library" => ["library", FixturePath],
            "type" =>
                ["type", "Cases.Widget", "--library", FixturePath],
            "member" =>
                [
                    "member",
                    "Cases.Widget",
                    "--library",
                    FixturePath,
                    "-m",
                    "Value",
                ],
            _ => throw new InvalidOperationException(),
        };
        var result = await Run(
            [
                .. subject,
                "-S",
                SectionNames.CloneCandidates,
                "-n",
                "1",
                "--count",
                "--json",
                "-T",
                "q",
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task QueryPredicateImplicitSelectionAdoptsSemanticRows()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "--where",
            "Breadth=Self",
            "-n",
            "1",
            "--count",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task UnavailableSemanticWindowWithholdsOutput()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--rows",
            "999..1000",
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires row 1000",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "ranked candidates are available",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonLineSelectionRejectsBeforeSourceResolution()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            "missing-clone-candidates.dll",
            "-S",
            SectionNames.CloneCandidates,
            "-n",
            "1",
            "--lines",
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "missing-clone-candidates.dll",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericLegacyRowsAreRejectedBeforeSourceResolution()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            "missing-clone-candidates.dll",
            "-S",
            SectionNames.CloneCandidates,
            "--rows",
            "1",
            "--json",
            "-T",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--rows",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "missing-clone-candidates.dll",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticSelectionFailureKeepsIncompleteCoverageVisible()
    {
        CloneCandidateDocument document = IncompleteDocument();
        var options = new CloneCandidateOutputOptions(
            OutputFormat.Json,
            CompactJson: false,
            NoHeader: false,
            Count: false,
            Columns: null,
            Fields: null,
            FieldsExplicitlySet: false,
            RowSelection: RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(2, 2),
                ]),
            Rows: null,
            SelectedSectionCount: 1,
            Tree: false,
            Mermaid: false,
            Print: false,
            Value: false,
            Urls: false,
            Paths: false);

        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            CloneCandidatesCommand.Write(
                new CloneCandidatePresentationResult.Available(document),
                options)));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires row 2",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "The Clone Candidates result is incomplete.",
            result.Error,
            StringComparison.Ordinal);
    }

    static CloneCandidateDocument IncompleteDocument()
    {
        var assembly = new AssemblyReferenceIdentity(
            "Clone.Candidates.Tests",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        Guid moduleVersionId = Guid.NewGuid();
        var participant = new CloneCandidateParticipantIdentity(
            0,
            assembly,
            AssemblyResolutionProvenance.Designated("test fixture"),
            moduleVersionId);
        var method = new CloneCandidateMethodIdentity(
            participant,
            moduleVersionId,
            0x06000001,
            new InertString(
                TextPolicy.Field,
                "Clone.Candidates.Tests!Cases.Widget.Value"));
        var row = new CloneCandidateRow(
            1,
            method,
            method,
            new CloneCandidateSimilarity(
                Score: 10_000,
                OperationScore: 10_000,
                PositionScore: 10_000,
                BlockScore: 10_000,
                EdgeScore: 10_000,
                LocalScore: 10_000,
                SeedInstructions: 1,
                CandidateInstructions: 1,
                SeedBlocks: 1,
                CandidateBlocks: 1,
                SeedEdges: 0,
                CandidateEdges: 0,
                SeedLocals: 0,
                CandidateLocals: 0),
            NameQualification: null);
        var seedCoverage = new CloneCandidateSeedCoverage(
            method,
            StructuralCloneRetrievalDisposition.LimitReached,
            rankedPairs: 1,
            suppressedPairs: 0,
            blockers: [],
            failures: []);
        var libraryCoverage = new CloneCandidateLibraryCoverage(
            participant,
            StructuralCloneParticipantMembership.ContainingLibrary,
            admitted: true,
            candidateMethods: 1,
            discoveredMethods: 1,
            retrievalPairs: 1,
            nameComparisonWork: 1,
            failures: [],
            analysisBlockers: []);

        return new CloneCandidateDocument(
            CloneCandidateDocument.CurrentSchemaVersion,
            new CloneCandidateSeed(
                CloneCandidateSeedKind.Library,
                type: null,
                member: null),
            StructuralCloneCandidateBreadth.Everything,
            StructuralCloneCandidateDiscovery.All,
            nameSimilarityThreshold: 0.5,
            new WorkspaceStructuralCloneSearchLimits(),
            scopeChangedDuringSearch: false,
            coverageIsComplete: false,
            rows: [row],
            seeds: [seedCoverage],
            libraries: [libraryCoverage],
            new CloneCandidateReceipt(
                SeedMethods: 1,
                CandidateMethods: 1,
                DiscoveredMethods: 1,
                AdmittedLibraries: 1,
                ExcludedLibraries: 0,
                NameComparisonWork: 1,
                RetrievalPairs: 1,
                RetrievalCalls: 1,
                RankedPairs: 1,
                SuppressedPairs: 0,
                ReturnedPairs: 1,
                ResultLimitReached: false));
    }
}
