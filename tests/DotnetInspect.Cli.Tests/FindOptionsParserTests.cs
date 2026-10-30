using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class FindOptionsParserTests
{
    private static IEnumerable<string> FindTipArgs(IEnumerable<Tip> tips)
        => tips.Where(t => t.Subcommand == FindCommand.Name).Select(t => t.Args);

    [Fact]
    public void EmptyProgrammaticScopeUsesPlatformWorkspacePlan()
    {
        Assert.Same(
            EcosystemPackCatalog.CreatePlatformWorkspacePlan(),
            FindSourceCollector.CreateWorkspacePlan(new FindOptions()));
    }

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Theory]
    [InlineData("--packages", "--libraries")]
    [InlineData("--packages", "--members")]
    [InlineData("--libraries", "--members")]
    public async Task PopulationGrains_RejectConflictingSelectorsBeforeDiscovery(
        string first, string second)
    {
        var result = await Run("find", first, second, "-Q", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("mutually exclusive", result.Error);
    }

    [Theory]
    [InlineData("--packages", "Packages", "tool-format")]
    [InlineData("--libraries", "Libraries", "references")]
    public async Task PopulationGrains_DiscoverTheirOwnVocabularyWithoutPopulation(
        string grain, string section, string facet)
    {
        var result = await Run("find", grain, "-Q", section, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal($"find {grain}", document.RootElement.GetProperty("command").GetString());
        JsonElement description = Assert.Single(document.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(section, description.GetProperty("section").GetString());
        Assert.Contains(description.GetProperty("facets").EnumerateArray(),
            value => value.GetProperty("name").GetString() == facet);
    }

    [Theory]
    [InlineData("--packages", "--package", "Missing.Package")]
    [InlineData("--packages", "--platform", "runtime")]
    [InlineData("--libraries", "--package", "Missing.Package")]
    [InlineData("--libraries", "--ecosystem", "aspire")]
    [InlineData("--libraries", "--explain", null)]
    public async Task PopulationGrains_RejectOtherGrainsOptionsBeforeAcquisition(
        string grain, string option, string? value)
    {
        var result = await Run(
            ["find", grain, option, .. value is null ? [] : new[] { value }]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains($"{option} is not available with find {grain}", result.Error);
    }

    [Fact]
    public async Task LibraryGrain_RequiresExplicitFramework()
    {
        var result = await Run("find", "--libraries", "--platform");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("requires a framework", result.Error);
    }

    [Fact]
    public void PopulationGrains_RetireSubjectQuerySubcommands()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        foreach (string subject in new[] { "package", "library" })
            Assert.DoesNotContain(root.Subcommands.Single(command => command.Name == subject).Subcommands,
                command => command.Name == "query");
    }

    [Theory]
    [InlineData("find", "--platform", "System.Object", "-D")]
    [InlineData("find", "System.Object", "--platform", "-D")]
    public async Task ApiGrain_PreservesBarePlatformOrdering(params string[] args)
    {
        var result = await Run(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("ecosystem.missing", "Unknown ecosystem")]
    [InlineData("list", "Unknown ecosystem")]
    public async Task Ecosystem_RejectsUnknownIds(
        string ecosystem,
        string expectedError)
    {
        var result = await Run(
            "find", "System.Object",
            "--ecosystem", ecosystem,
            "--tfm", "net10.0");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            expectedError,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ecosystem_RejectsDuplicateIds()
    {
        var result = await Run(
            "find", "System.Object",
            "--ecosystem", "ecosystem.aspire",
            "--ecosystem", "ASPIRE",
            "--tfm", "net10.0");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "cannot be selected more than once",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("aspire")]
    [InlineData("ECOSYSTEM.ASPIRE")]
    public async Task Ecosystem_AcceptsShortAndCaseInsensitiveNames(string value)
    {
        var result = await Run("find", "AddProject", "--ecosystem", value, "-D");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Unknown ecosystem", result.Error);
    }

    [Theory]
    [InlineData("ai,blazor")]
    [InlineData("AI,BlAzOr")]
    [InlineData("all")]
    public async Task Ecosystem_AcceptsCommaSeparatedSelections(string value)
    {
        var result = await Run("find", "AddProject", "--ecosystem", value, "-D");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Unknown ecosystem", result.Error);
    }

    [Fact]
    public async Task EcosystemTake_AcceptsExplicitCandidateBound()
    {
        var result = await Run(
            "find", ".Add*", "--ecosystem", "aspire",
            "--take", "7", "-D");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Find --take", result.Error);
    }

    [Theory]
    [InlineData("0", "--take requires a positive whole number")]
    [InlineData("21", "--take must be between 1 and 20")]
    public async Task EcosystemTake_RejectsOutOfRangeBound(string value, string expected)
    {
        var result = await Run(
            "find", ".Add*", "--ecosystem", "aspire",
            "--take", value, "-D");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            expected,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EcosystemTake_RequiresEcosystemSource()
    {
        var result = await Run(
            "find", ".Add*", "--platform", "--take", "5", "-D");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --ecosystem with TSV or JSONL output",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--count")]
    public async Task EcosystemTake_RejectsNonStreamingOutput(string output)
    {
        var result = await Run(
            "find", ".Add*", "--ecosystem", "aspire",
            "--take", "5", output, "-D");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --ecosystem with TSV or JSONL output",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ecosystem_RejectsCompositionWithPackage()
    {
        var result = await Run(
            "find", "JsonSerializer", "--ecosystem", "aspire",
            "--package", "System.Text.Json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot be combined", result.Error);
    }

    [Fact]
    public void Ecosystem_DisablesImplicitPlatform()
    {
        Assert.False(new FindOptions { Ecosystems = [EcosystemPackIds.Aspire] }
            .UsesImplicitPlatform);
    }

    [Fact]
    public void Ecosystem_PrefixOnlyPopulationIsAccepted()
    {
        var id = EcosystemPackIds.MicrosoftExtensions;

        var layers = FindCommand.GetNamedLayers([id]);

        Assert.Contains(
            layers,
            layer => layer.Id == id
                && layer.Declaration.Populations.Any(population =>
                    population is
                        WorkspaceEcosystemPopulationDeclaration
                            .PackagePrefix));
    }

    [Fact]
    public void Ecosystem_RequiresAnId()
    {
        ParseResult result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "System.Object", "--ecosystem"]);

        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("-t")]
    [InlineData("--candidates")]
    [InlineData("--matches")]
    public void RetiredFindOptions_AreNotRecognized(string option)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "Json*", option, "1"]);

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                option,
                StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTips_MemberMode_PreservesLensAndCanonicalizesPattern()
    {
        var explicitTips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = "Serialize", Members = true }, "Serialize");
        var dotTips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = ".Serialize", Members = true }, ".Serialize");

        // Every find tip re-enables the member lens so following it does not silently revert to type
        // search...
        Assert.NotEmpty(FindTipArgs(explicitTips));
        Assert.All(FindTipArgs(explicitTips), args => Assert.Contains("--members", args));

        // ...and the explicit-flag and leading-dot forms produce identical tips.
        Assert.Equal(FindTipArgs(dotTips), FindTipArgs(explicitTips));
    }

    [Fact]
    public void BuildTips_MemberMode_PreservesConstructorPattern()
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = ".ctor", Members = true }, ".ctor");

        // ".ctor" must survive into the tip (not be stripped to "ctor"), so following the tip still
        // searches for constructors.
        Assert.All(FindTipArgs(tips), args => Assert.StartsWith(".ctor ", args));
    }

    [Fact]
    public void BuildTips_TypeMode_OmitsMembersFlag()
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = "JsonSerializer", Members = false }, "JsonSerializer");

        Assert.All(FindTipArgs(tips), args => Assert.DoesNotContain("--members", args));
    }

    [Fact]
    public void BuildTips_ZeroImplicitTypeResults_SuggestsPackageDiscovery()
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = "Newtonsoft.Json" },
            "Newtonsoft.Json",
            rowCount: 0);

        var tip = Assert.Single(
            tips,
            tip => tip.Subcommand == "find --packages");
        Assert.Equal("Newtonsoft.Json", tip.Args);
        Assert.Contains("find searches API symbols", tip.Comment);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void BuildTips_PackageDiscoverySuggestion_IsLimitedToZeroTypeResults(
        bool members,
        int rowCount)
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions
            {
                Pattern = "Newtonsoft.Json",
                Members = members,
            },
            "Newtonsoft.Json",
            rowCount);

        Assert.DoesNotContain(
            tips,
            tip => tip.Subcommand == "find --packages");
    }

    [Theory]
    [InlineData(false, FindQueryRouteKind.TypeResults)]
    [InlineData(true, FindQueryRouteKind.MemberResults)]
    public void QueryPlan_PreservesTheActiveLensAndOrderedRows(
        bool members,
        FindQueryRouteKind expectedRoute)
    {
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(2, 4),
                    RowSelectionIntentOperation<string>.Head(1),
                ]);

        Assert.True(
            FindQueryOptions.TryResolve(
                expectedRoute,
                selection,
                out FindQueryPlan plan,
                out string? error),
            error);
        var options = new FindOptions
        {
            Members = members,
            QueryPlan = plan,
        };

        Assert.Equal(
            expectedRoute,
            options.QueryPlan.RouteKind);
        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Head,
            ],
            options.EffectiveRowSelection!.Operations.Select(operation =>
                operation.Kind));
        Assert.Null(options.QueryPlan.InputRows);
    }

    [Fact]
    public void QueryPlan_RejectsALensRouteMismatch()
    {
        Assert.True(
            FindQueryOptions.TryResolve(
                FindQueryRouteKind.MemberResults,
                rowSelection: null,
                out FindQueryPlan plan,
                out string? error),
            error);
        var options = new FindOptions
        {
            Members = false,
            QueryPlan = plan,
        };

        Assert.Throws<InvalidOperationException>(
            () => options.EffectiveRowSelection);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    public void PackagePrefix_MetadataLimitFailuresRemainCleanCliErrors(
        Type exceptionType)
    {
        var error = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(CommandLineHelpers.IsPrefixResolutionFailure(error));
    }
}
