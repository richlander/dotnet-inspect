using System.CommandLine;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class FindOptionsParserTests
{
    private static IEnumerable<string> FindTipArgs(IEnumerable<Tip> tips)
        => tips.Where(t => t.Subcommand == FindCommand.Name).Select(t => t.Args);

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Theory]
    [InlineData("aspire", "Invalid ecosystem")]
    [InlineData("ecosystem.Aspire", "Invalid ecosystem")]
    [InlineData("ecosystem.missing", "Unknown ecosystem")]
    public async Task Ecosystem_RejectsNonCanonicalOrUnknownIds(
        string ecosystem,
        string expectedError)
    {
        var result = await Run(
            "find", "System.Object",
            "--ecosystem", ecosystem,
            "--platform", "System.Runtime",
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
            "--ecosystem", "ecosystem.aspire",
            "--platform", "System.Runtime",
            "--tfm", "net10.0");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "cannot be selected more than once",
            result.Error,
            StringComparison.Ordinal);
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
