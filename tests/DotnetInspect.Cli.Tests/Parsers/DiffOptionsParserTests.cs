using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests.Parsers;

/// <summary>
/// Focused tests for <see cref="DiffOptionsParser"/> covering the <c>--repo</c> plumbing that
/// threads local git clones into the Implementation Diff PDB-source acquisition.
/// </summary>
[Collection("Console")]
public class DiffOptionsParserTests
{
    /// <summary>
    /// Creates the diff command with shared options and args for testing.
    /// Mirrors InspectionCommandDefinitions.CreateDiffCommand but exposes the args.
    /// </summary>
    private static (Command Root, SharedOptions Opts, DiffOptionsParser.DiffCommandArgs Args) CreateTestCommand()
    {
        var opts = new SharedOptions();
        var diffCommand = new Command("diff", "test");

        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var platformOption = new Option<string?>("--platform");
        var libraryOption = new Option<string?>("--library");
        var frameworkOption = new Option<string?>("--framework");
        var tfmOption = new Option<string?>("--tfm");
        var allOption = new Option<bool>("--all");
        var historyOption = new Option<bool>("--history");
        var atOption = new Option<string[]>("--at")
        {
            AllowMultipleArgumentsPerToken = false
        };
        var maxProbesOption = new Option<int?>("--max-probes");
        var samplePercentOption =
            new Option<int?>("--sample-percent");
        var majorVersionsOption =
            new Option<bool>("--major-versions");
        var prereleaseOption = new Option<bool>("--preview");
        var countOption = new Option<bool>("--count");
        var typeFilterOption = new Option<string[]>("-t") { AllowMultipleArgumentsPerToken = false };
        typeFilterOption.Aliases.Add("--type");
        var memberFilterOption = new Option<string[]>("-m") { AllowMultipleArgumentsPerToken = false };
        memberFilterOption.Aliases.Add("--member");
        var nameOnlyOption = new Option<bool>("--name-only");
        var breakingOption = new Option<bool>("--breaking");
        var additiveOption = new Option<bool>("--additive");
        var changedOption = new Option<bool>("--changed");
        var allocRegressionsOption = new Option<bool>("--alloc-regressions");
        var pdbSourceOption = new Option<bool>("--pdb-source");
        var legacyAuthoredSourceOption = new Option<bool>("--authored-source") { Hidden = true };
        var repoOption = new Option<string[]>("--repo") { AllowMultipleArgumentsPerToken = false };
        var findingOption = new Option<string?>("--finding");
        var legendOption = new Option<bool>("--legend");
        var compactOption = new Option<bool>("--compact");

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(libraryOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(historyOption);
        diffCommand.Options.Add(atOption);
        diffCommand.Options.Add(maxProbesOption);
        diffCommand.Options.Add(samplePercentOption);
        diffCommand.Options.Add(majorVersionsOption);
        diffCommand.Options.Add(prereleaseOption);
        diffCommand.Options.Add(countOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(memberFilterOption);
        opts.AddTableOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Json);
        diffCommand.Options.Add(opts.Markdown);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(changedOption);
        diffCommand.Options.Add(allocRegressionsOption);
        diffCommand.Options.Add(pdbSourceOption);
        diffCommand.Options.Add(legacyAuthoredSourceOption);
        diffCommand.Options.Add(repoOption);
        diffCommand.Options.Add(findingOption);
        diffCommand.Options.Add(legendOption);
        diffCommand.Options.Add(compactOption);
        diffCommand.Options.Add(opts.Envelope);
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Discover);
        diffCommand.Options.Add(opts.Tree);
        diffCommand.Options.Add(opts.Select);

        diffCommand.SetAction((_, _) => Task.FromResult(0));

        var root = new RootCommand { diffCommand };
        var args = new DiffOptionsParser.DiffCommandArgs(
            argsArg, packageOption, platformOption, libraryOption, frameworkOption, tfmOption, allOption,
            historyOption, atOption, maxProbesOption, samplePercentOption, majorVersionsOption, prereleaseOption, countOption,
            typeFilterOption, memberFilterOption, opts.NoHeaders, nameOnlyOption, breakingOption, additiveOption,
            changedOption, allocRegressionsOption, pdbSourceOption, legacyAuthoredSourceOption, findingOption, legendOption, repoOption, compactOption);

        return (root, opts, args);
    }

    private static DiffOptions ParseSuccess(params string[] args)
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(args);
        Assert.Empty(parseResult.Errors);

        var result = DiffOptionsParser.Parse(parseResult, opts, cmdArgs);
        var success = Assert.IsType<DiffOptionsParser.Success>(result);
        return success.Options;
    }

    [Fact]
    public void RepoOption_PopulatesSourceRepositories()
    {
        var options = ParseSuccess(
            "diff",
            "--package", "System.Text.Json@8.0.0..9.0.0",
            "--repo", @"C:\clone-a",
            "--repo", @"C:\clone-b");

        Assert.Equal([@"C:\clone-a", @"C:\clone-b"], options.SourceRepositories);
    }

    [Fact]
    public void SurveyOptionsPopulateEvaluationPolicyInputs()
    {
        DiffOptions options = ParseSuccess(
            "diff",
            "--history",
            "--package", "Example@1.0.0..8.0.0",
            "--sample-percent", "50",
            "--max-probes", "10");

        Assert.Equal(50, options.SamplePercent);
        Assert.Equal(10, options.MaxProbes);
    }

    [Fact]
    public void MajorVersionsOptionPopulatesEvaluationPolicyInput()
    {
        DiffOptions options = ParseSuccess(
            "diff",
            "--history",
            "--package", "Example@8.0.0..11.0.0",
            "--major-versions");

        Assert.True(options.MajorVersions);
    }

    [Fact]
    public void RepoOption_DefaultsToEmpty()
    {
        var options = ParseSuccess("diff", "--package", "System.Text.Json@8.0.0..9.0.0");

        Assert.Empty(options.SourceRepositories);
    }

    [Theory]
    [InlineData("--pdb-source")]
    [InlineData("--authored-source")]
    public void PdbSourceOption_AndLegacyAlias_EnablePdbSource(string option)
    {
        var options = ParseSuccess(
            "diff",
            "--package", "System.Text.Json@8.0.0..9.0.0",
            option);

        Assert.True(options.IncludePdbSource);
    }
}
