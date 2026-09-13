using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;

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
            "--rows",
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
    public async Task Member_BroadSectionSelectionDoesNotRunCloneCandidates()
    {
        var result = await Run(
            "member",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-m",
            "Raise",
            "-S",
            "@All",
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
            "--rows",
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
    public async Task Type_JsonProjectionSupportsFieldsColumnsAndRows()
    {
        var result = await Run(
            "type",
            "Cases.Widget",
            "--library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            "Breadth;Coverage",
            "--columns",
            "Rank;Score",
            "--rows",
            "2",
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            ["breadth", "coverage"],
            json.RootElement.GetProperty("summary")
                .EnumerateObject()
                .Select(property => property.Name));
        JsonElement rows =
            json.RootElement.GetProperty("clone_candidates");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.All(
            rows.EnumerateArray(),
            row => Assert.Equal(
                ["rank", "score"],
                row.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task Library_JsonProjectionSupportsSummaryFields()
    {
        var result = await Run(
            "library",
            FixturePath,
            "-S",
            SectionNames.CloneCandidates,
            "--fields",
            "Breadth;Coverage",
            "--json",
            "-T",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            ["breadth", "coverage"],
            json.RootElement.GetProperty("summary")
                .EnumerateObject()
                .Select(property => property.Name));
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
            "--rows",
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
}
