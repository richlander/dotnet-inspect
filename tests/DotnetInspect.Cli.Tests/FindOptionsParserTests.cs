using System.CommandLine;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Tests;

public class FindOptionsParserTests
{
    private static IEnumerable<string> FindTipArgs(IEnumerable<Tip> tips)
        => tips.Where(t => t.Subcommand == FindCommand.Name).Select(t => t.Args);

    [Fact]
    public void PackagePrefixWithoutPattern_SelectsManifestProfile()
    {
        Assert.True(
            new FindOptions
            {
                PackagePrefix = "Microsoft.",
            }.IsPackageProfile);
    }

    [Fact]
    public void PackagePrefixWithPattern_PreservesApiSearch()
    {
        Assert.False(
            new FindOptions
            {
                Pattern = "JsonSerializer",
                PackagePrefix = "Microsoft.",
            }.IsPackageProfile);
    }

    [Fact]
    public void Literal_PreservesRawOperandAndExactPackageSelection()
    {
        const string operand = " leading\tliteral\r\n ";
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", operand,
             "--package", "Example@1.0.0", "--tfm", "net10.0"]);

        Assert.Empty(result.Errors);
        var option = Assert.IsType<Option<string?>>(
            result.CommandResult.Command.Options.Single(option => option.Name == "--literal"));
        Assert.Equal(operand, result.GetValue(option));
    }

    [Theory]
    [InlineData("Json*", null)]
    [InlineData("--package-prefix", "Example.")]
    [InlineData("--library", "Example.dll")]
    [InlineData("--platform", null)]
    [InlineData("--extensions", null)]
    [InlineData("--aspnetcore", null)]
    [InlineData("--project", "Example.csproj")]
    [InlineData("--bin", "bin")]
    [InlineData("--members", null)]
    [InlineData("--all", null)]
    [InlineData("-t", "1")]
    public void Literal_RejectsOtherSearchModesBeforeAcquisition(string option, string? value)
    {
        string[] extra = value is null ? [option] : [option, value];
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal",
             "--package", "Example@1.0.0", "--tfm", "net10.0", .. extra]);

        Assert.Contains(result.Errors,
            error => error.Message.Contains("cannot be combined", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Example")]
    [InlineData("Example@latest")]
    [InlineData("Example@[1.0,2.0)")]
    public void Literal_RequiresAnExactPackageVersion(string coordinate)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal", "--package", coordinate, "--tfm", "net10.0"]);

        // The planner's product-authored sentence only: the framework appends a parameter-name
        // line that names an internal parameter and renders as a raw resource key when resources
        // are trimmed.
        var error = Assert.Single(result.Errors);
        Assert.DoesNotContain("packageCoordinates", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Arg_ParamName_Name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Literal_RejectsNormalizedDuplicatesBeforeScopeDeduplication()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal",
             "--package", "Example@1.0.0", "--package", "example@1.0",
             "--tfm", "net10.0"]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(
            "An assembly query cannot contain duplicate package coordinates.",
            error.Message);
    }

    [Fact]
    public void Literal_RequiresAnExplicitTargetFramework()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal", "--package", "Example@1.0.0"]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(
            "--literal requires an explicit --tfm (for example --tfm net10.0).",
            error.Message);
    }

    [Fact]
    public void Literal_DoesNotAcquireAnImplicitPlatformScope()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal", "--tfm", "net10.0"]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(
            "An assembly query requires between 1 and 5 explicit ID@VERSION packages.",
            error.Message);
    }

    [Fact]
    public void Literal_DoesNotExposeUnsupportedHostRuntimeSelection()
    {
        Command find = Assert.Single(
            CommandLineBuilder.CreateRootCommand().Subcommands,
            command => command.Name == FindCommand.Name);

        // Browser navigation cannot preserve a runtime identifier, so no host
        // exposes one for this query even though the shared evaluator can bind
        // one for its own contract.
        Assert.DoesNotContain(
            find.Options,
            option => option.Name == "--rid"
                || option.Aliases.Contains("--rid"));

        var result = CommandLineBuilder.CreateRootCommand().Parse(
            ["find", "--literal", "literal", "--package", "Example@1.0.0",
             "--tfm", "net10.0", "--rid", "linux-x64"]);

        Assert.NotEmpty(result.Errors);
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
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    public void PackagePrefix_MetadataLimitFailuresRemainCleanCliErrors(
        Type exceptionType)
    {
        var error = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(CommandLineHelpers.IsPrefixResolutionFailure(error));
    }
}
