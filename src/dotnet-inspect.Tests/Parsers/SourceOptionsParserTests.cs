using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.Tests.Parsers;

[Collection("Console")]
public class SourceOptionsParserTests
{
    private static (Command Root, SharedOptions Opts, SourceOptionsParser.SourceCommandArgs Args) CreateTestCommand()
    {
        var opts = new SharedOptions();
        var sourceCommand = new Command("source", "test");

        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var assemblyOption = new Option<string?>("--library");
        var platformOption = new Option<string?>("--platform");
        var frameworkOption = new Option<string?>("--framework");
        var tfmOption = new Option<string?>("--tfm");
        var allOption = new Option<bool>("--all");
        var memberOption = new Option<string?>("-m");
        memberOption.Aliases.Add("--member");
        var typeFilterOption = new Option<string?>("-t");
        typeFilterOption.Aliases.Add("--type");
        var verifyOption = new Option<bool>("--verify");
        var browsableUrlsOption = new Option<bool>("--browsable-urls");
        var catOption = new Option<bool>("--cat");
        var ilOffsetOption = new Option<string?>("--il-offset");
        var compactOption = new Option<bool>("--compact");

        sourceCommand.Arguments.Add(argsArg);
        sourceCommand.Options.Add(packageOption);
        sourceCommand.Options.Add(assemblyOption);
        sourceCommand.Options.Add(platformOption);
        sourceCommand.Options.Add(frameworkOption);
        sourceCommand.Options.Add(tfmOption);
        sourceCommand.Options.Add(allOption);
        sourceCommand.Options.Add(memberOption);
        sourceCommand.Options.Add(typeFilterOption);
        sourceCommand.Options.Add(verifyOption);
        sourceCommand.Options.Add(browsableUrlsOption);
        sourceCommand.Options.Add(catOption);
        sourceCommand.Options.Add(ilOffsetOption);
        sourceCommand.Options.Add(opts.Json);
        sourceCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(sourceCommand);
        opts.AddSectionOptionsTo(sourceCommand);
        sourceCommand.Options.Add(opts.Markdown);
        sourceCommand.Options.Add(opts.PlainText);
        opts.AddOutputOptionsTo(sourceCommand);
        opts.AddNuGetOptionsTo(sourceCommand);

        sourceCommand.SetAction((_, _) => Task.FromResult(0));

        var root = new RootCommand { sourceCommand };
        var args = new SourceOptionsParser.SourceCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, typeFilterOption, verifyOption, browsableUrlsOption, catOption,
            ilOffsetOption, compactOption, opts.OneLine, opts.NoHeaders);

        return (root, opts, args);
    }

    private static async Task<SourceOptions> ParseSuccessAsync(params string[] args)
    {
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(args);
        Assert.Empty(parseResult.Errors);

        var result = await SourceOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        var success = Assert.IsType<SourceOptionsParser.Success>(result);
        return success.Options;
    }

    [Fact]
    public async Task Positional_QualifiedTypeMember_ResolvesPlatformTypeAndMember()
    {
        var options = await ParseSuccessAsync("source", "System.Text.Json.JsonSerializer.SerializeToNode");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.PackagePath);
        Assert.Equal("SerializeToNode", options.MemberName);
    }

    [Fact]
    public async Task Positional_QualifiedTypeMemberWithOverload_ResolvesIndex()
    {
        var options = await ParseSuccessAsync("source", "System.Text.Json.JsonSerializer.SerializeToNode:1");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Equal("SerializeToNode", options.MemberName);
        Assert.Equal(1, options.OverloadIndex);
    }

    [Fact]
    public async Task Positional_QualifiedTypeMemberWithTypo_PreservesTypeForSuggestions()
    {
        var options = await ParseSuccessAsync("source", "System.Text.Json.JsonSerializizer.SerializeToNode");

        Assert.Equal("JsonSerializizer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.PackagePath);
        Assert.Equal("SerializeToNode", options.MemberName);
    }

    [Fact]
    public async Task ExplicitPlatform_WithLocalDottedMember_SplitsTypeAndMember()
    {
        var options = await ParseSuccessAsync(
            "source", "JsonSerializer.SerializeToNode", "--platform", "System.Text.Json");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Equal("SerializeToNode", options.MemberName);
    }

    [Fact]
    public async Task ExplicitPlatform_WithQualifiedTypeOnly_PreservesQualifiedType()
    {
        var options = await ParseSuccessAsync(
            "source", "System.Text.Json.JsonSerializer", "--platform", "System.Text.Json");

        Assert.Equal("System.Text.Json.JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.MemberName);
    }

    [Fact]
    public async Task Positional_PackageTypeSyntax_DoesNotUseRootPlatformFallback()
    {
        var options = await ParseSuccessAsync("source", "System.CommandLine", "Command");

        Assert.Equal("System.CommandLine", options.PackagePath);
        Assert.Equal("Command", options.TypeName);
        Assert.Null(options.PlatformAssembly);
        Assert.Null(options.MemberName);
    }

    [Fact]
    public async Task ExplicitMarkdown_SuppressesTips()
    {
        var options = await ParseSuccessAsync("source", "JsonSerializer", "--package", "System.Text.Json", "--markdown");

        Assert.True(options.FormatExplicitlySet);
        Assert.False(options.IsRawOutput);
        Assert.Equal(TipLevel.Quiet, options.TipLevel);
    }
}
