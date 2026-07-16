using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.Tests.Parsers;

[Collection("Console")]
public class TypeOptionsParserTests
{
    static (Command Root, SharedOptions Opts, TypeOptionsParser.TypeCommandArgs Args) CreateTestCommand()
    {
        var opts = new SharedOptions();
        var typeCommand = new Command("type", "test");

        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var atOption = new Option<string?>("--at");
        var assemblyOption = new Option<string?>("--library");
        var platformOption = new Option<string?>("--platform");
        var projectOption = new Option<string?>("--project");
        var frameworkOption = new Option<string?>("--framework");
        var tfmOption = new Option<string?>("--tfm");
        var allOption = new Option<bool>("--all");
        var typeFilterOption = new Option<string?>("-t");
        var compactOption = new Option<bool>("--compact");
        var shapeOption = new Option<bool>("--shape");
        var unsafeOption = new Option<bool>("--unsafe");
        var memberOption = new Option<string[]>("-m") { AllowMultipleArgumentsPerToken = true };
        var kindOption = new Option<string[]>("-k") { AllowMultipleArgumentsPerToken = true };

        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(atOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(projectOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(typeCommand);
        typeCommand.Options.Add(shapeOption);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(memberOption);
        typeCommand.Options.Add(kindOption);
        opts.AddSectionOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markdown);
        typeCommand.Options.Add(opts.PlainText);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        typeCommand.SetAction((_, _) => Task.FromResult(0));

        var root = new RootCommand { typeCommand };
        var args = new TypeOptionsParser.TypeCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, projectOption, frameworkOption, tfmOption,
            allOption, typeFilterOption, compactOption, opts.NoHeaders,
            shapeOption, unsafeOption, memberOption, kindOption, atOption);

        return (root, opts, args);
    }

    static async Task<TypeOptions> ParseSuccessAsync(params string[] args)
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(args);
        Assert.Empty(parseResult.Errors);

        var result = await TypeOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        var success = Assert.IsType<TypeOptionsParser.Success>(result);
        return success.Options;
    }

    [Fact]
    public async Task PackageSource_WithEnvironmentJson_SetsJsonOutput()
    {
        var originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "json");
            var options = await ParseSuccessAsync("type", "JsonSerializer", "--package", "System.Text.Json");

            Assert.True(options.JsonOutput);
            Assert.True(options.FormatExplicitlySet);
            Assert.False(options.Tabular);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    [Fact]
    public async Task ProjectSource_SetsProjectPathAndTypeName()
    {
        var options = await ParseSuccessAsync("type", "Command", "--project", "App.csproj");

        Assert.Equal("Command", options.TypeName);
        Assert.Equal("App.csproj", options.ProjectPath);
        Assert.Null(options.PackagePath);
        Assert.Null(options.AssemblyPath);
        Assert.Null(options.PlatformAssembly);
    }

    [Fact]
    public async Task PackageRangeAddress_IsPreservedForLazyResolution()
    {
        var options = await ParseSuccessAsync(
            "type", "JsonSerializer",
            "--package", "System.Text.Json@8.0.0..9.0.0",
            "--at", "#2");

        Assert.Equal("System.Text.Json@8.0.0..9.0.0", options.PackagePath);
        Assert.Equal("#2", options.PackageRangeAddress);
    }

    [Fact]
    public async Task DashPrefixedTypeAfterDoubleDash_IsPositional()
    {
        var options = await ParseSuccessAsync(
            "type", "--package", "System.Text.Json", "--", "-Foo");

        Assert.Equal("-Foo", options.TypeName);
    }

    [Fact]
    public async Task UnknownOptionBeforeDoubleDash_RemainsUnrecognized()
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(
            ["type", "--platform", "System.Private.CoreLib", "--bogus", "--", "-Foo"]);
        Assert.Empty(parseResult.Errors);

        var result = await TypeOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        var error = Assert.IsType<TypeOptionsParser.UnrecognizedOption>(result);
        Assert.Equal("--bogus", error.Option);
    }

    [Fact]
    public async Task ProjectCannotCombineWithExplicitSource()
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["type", "Command", "--project", "App.csproj", "--package", "System.CommandLine"]);
        Assert.Empty(parseResult.Errors);

        var result = await TypeOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        var error = Assert.IsType<TypeOptionsParser.VersionError>(result);
        Assert.Contains("--project cannot be combined", error.Message);
    }
}
