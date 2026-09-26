using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests.Parsers;

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
        var suppressRuntimeTypeFallbackOption =
            new Option<string?>(
                RouterCommandDefinition
                    .SuppressRuntimeTypeFallbackOptionName);
        var workspaceOption = new Option<string?>("--workspace");
        var shareOption = WorkspaceShareOption.Create("test");
        var allOption = new Option<bool>("--all");
        var typeFilterOption = new Option<string?>("-t");
        var compactOption = new Option<bool>("--compact");
        var unsafeOption = new Option<bool>("--unsafe");
        var repoOption = new Option<string[]>("--repo") { AllowMultipleArgumentsPerToken = false };
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
        typeCommand.Options.Add(suppressRuntimeTypeFallbackOption);
        typeCommand.Options.Add(workspaceOption);
        typeCommand.Options.Add(shareOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(typeCommand);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(repoOption);
        typeCommand.Options.Add(memberOption);
        typeCommand.Options.Add(kindOption);
        opts.AddSectionOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markdown);
        typeCommand.Options.Add(opts.PlainText);
        typeCommand.Options.Add(opts.Envelope);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        typeCommand.SetAction((_, _) => Task.FromResult(0));

        var root = new RootCommand { typeCommand };
        var args = new TypeOptionsParser.TypeCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, projectOption, frameworkOption, tfmOption,
            allOption, typeFilterOption, compactOption, opts.NoHeaders,
            unsafeOption, repoOption, memberOption, kindOption, atOption,
            workspaceOption, shareOption,
            suppressRuntimeTypeFallbackOption);

        return (root, opts, args);
    }

    [Fact]
    public async Task RepoOption_PopulatesSourceRepositories()
    {
        var options = await ParseSuccessAsync(
            "type", "Some.Type",
            "--library", "x.dll",
            "--repo", @"C:\clone-a",
            "--repo", @"C:\clone-b");

        Assert.Equal([@"C:\clone-a", @"C:\clone-b"], options.SourceRepositories);
    }

    [Fact]
    public async Task RepoOption_DefaultsToEmpty()
    {
        var options = await ParseSuccessAsync(
            "type", "Some.Type",
            "--library", "x.dll");

        Assert.Empty(options.SourceRepositories);
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

    [Theory]
    [InlineData("json")]
    [InlineData("table")]
    public async Task Envelope_IgnoresEnvironmentFormat(string format)
    {
        string? original =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                format);
            var options = await ParseSuccessAsync(
                "type",
                "JsonSerializer",
                "--package",
                "System.Text.Json@10.0.0",
                "--tfm",
                "net10.0",
                "--envelope");

            Assert.True(options.EnvelopeOutput);
            Assert.False(options.JsonOutput);
            Assert.False(options.Tabular);
            Assert.False(options.FormatExplicitlySet);
            Assert.Equal(OutputFormat.Json, options.Format);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                original);
        }
    }

    [Theory]
    [InlineData("q", TipLevel.Quiet)]
    [InlineData("m", TipLevel.Minimal)]
    [InlineData("d", TipLevel.Detailed)]
    public async Task Envelope_PreservesExplicitTipLevel(
        string value,
        TipLevel expected)
    {
        var options = await ParseSuccessAsync(
            "type",
            "JsonSerializer",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--envelope",
            "--tips",
            value);

        Assert.Equal(expected, options.TipLevel);
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
    public async Task NumericMemberAndTypeFilters_AreOrdinaryFilterInput()
    {
        var memberOptions = await ParseSuccessAsync(
            "type", "MemoryStream", "--platform", "System.Private.CoreLib", "-m", "1");
        var typeOptions = await ParseSuccessAsync(
            "type", "MemoryStream", "--platform", "System.Private.CoreLib", "-t", "1");

        Assert.Contains("1", memberOptions.MemberFilter);
        Assert.Null(memberOptions.Limit);
        Assert.Null(memberOptions.MemberLimit);
        Assert.Equal("1", typeOptions.TypeFilter);
        Assert.Null(typeOptions.Limit);
        Assert.Null(typeOptions.MemberLimit);
    }

    [Theory]
    [InlineData("AsSpan<T>")]
    [InlineData("AsSpan`1")]
    public async Task GenericArityMemberFilter_IsRejected(string selector)
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(
            ["type", "MemoryExtensions", "--platform", "System.Memory", "-m", selector]);
        Assert.Empty(parseResult.Errors);

        var result = await TypeOptionsParser.ParseAsync(parseResult, opts, cmdArgs);

        var error = Assert.IsType<TypeOptionsParser.VersionError>(result);
        Assert.Contains("does not support generic arity selectors", error.Error.Message);
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
        Assert.Contains("--project cannot be combined", error.Error.Message);
    }

    [Fact]
    public async Task Workspace_PreservesExactTypeAndShare()
    {
        var options = await ParseSuccessAsync(
            "type",
            "System.Text.Json.JsonSerializer",
            "--workspace",
            "packet",
            "--share",
            "packet");

        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            options.TypeName);
        Assert.Equal("packet", options.WorkspacePacket);
        Assert.Equal(
            WorkspaceShareFormat.Packet,
            options.ShareFormat);
        Assert.Null(options.PackagePath);
    }

    [Fact]
    public async Task Workspace_RejectsUrlInput()
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        ParseResult parseResult = root.Parse(
            [
                "type",
                "System.Text.Json.JsonSerializer",
                "--workspace",
                "https://dotnet-inspect.net/?w=packet",
            ]);
        Assert.Empty(parseResult.Errors);

        TypeOptionsParser.TypeParseResult result =
            await TypeOptionsParser.ParseAsync(
                parseResult,
                opts,
                cmdArgs);

        var error = Assert.IsType<TypeOptionsParser.VersionError>(
            result);
        Assert.Contains(
            "Base64URL Workspace packet string",
            error.Error.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "URLs are not supported",
            error.Error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type", "--workspace", "packet")]
    [InlineData("type", "System.*", "--workspace", "packet")]
    [InlineData(
        "type",
        "System.Text.Json.JsonSerializer",
        "--workspace",
        "packet",
        "--package",
        "System.Text.Json")]
    [InlineData(
        "type",
        "System.Text.Json.JsonSerializer",
        "--workspace",
        "packet",
        "--tfm",
        "net10.0")]
    public async Task Workspace_RejectsUnsupportedSourceOrSubject(
        params string[] commandLine)
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        ParseResult parseResult = root.Parse(commandLine);
        Assert.Empty(parseResult.Errors);

        TypeOptionsParser.TypeParseResult result =
            await TypeOptionsParser.ParseAsync(
                parseResult,
                opts,
                cmdArgs);

        Assert.IsType<TypeOptionsParser.VersionError>(result);
    }

    [Fact]
    public async Task ShareWithoutWorkspace_IsRejected()
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        ParseResult parseResult = root.Parse(
            [
                "type",
                "JsonSerializer",
                "--package",
                "System.Text.Json",
                "--share",
                "packet",
            ]);
        Assert.Empty(parseResult.Errors);

        TypeOptionsParser.TypeParseResult result =
            await TypeOptionsParser.ParseAsync(
                parseResult,
                opts,
                cmdArgs);

        var error = Assert.IsType<TypeOptionsParser.VersionError>(
            result);
        Assert.Contains(
            "--share on type requires --workspace",
            error.Error.Message);
    }
}
