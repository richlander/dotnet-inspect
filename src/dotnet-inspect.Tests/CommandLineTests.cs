using DotnetInspector;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for command line parsing behavior.
/// </summary>
public class CommandLineTests
{
    [Fact]
    public void RootCommand_WithNoArgs_RequiresCommand()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse([]);

        // Root command requires a subcommand
        Assert.Single(result.Errors);
        Assert.Contains("Required command was not provided", result.Errors[0].Message);
    }

    [Fact]
    public void PackageCommand_WithPackageName_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("package", result.CommandResult.Command.Name);
    }

    [Fact]
    public void PackageCommand_WithVersionsFlag_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "System.Text.Json", "--versions"]);

        Assert.Empty(result.Errors);
        Assert.Equal("package", result.CommandResult.Command.Name);
    }

    [Fact]
    public void PackageCommand_WithLimit_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "System.Text.Json", "--versions", "-n", "5"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PackageCommand_WithPreviewAlias_ParsesCorrectly()
    {
        var result1 = CommandLineBuilder.CreateRootCommand().Parse(["package", "Foo", "--preview"]);
        var result2 = CommandLineBuilder.CreateRootCommand().Parse(["package", "Foo", "--prerelease"]);

        Assert.Empty(result1.Errors);
        Assert.Empty(result2.Errors);
    }

    [Fact]
    public void ApiCommand_WithoutType_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("api", result.CommandResult.Command.Name);
    }

    [Fact]
    public void ApiCommand_WithType_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithMemberFilter_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "-m", "Serialize"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithMemberAlias_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--member", "Serialize"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithMultipleMembers_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "-m", "Serialize", "-m", "Deserialize"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithSignaturesOnly_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--signatures-only"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TypeCommand_WithPackage_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["type", "JsonSerializer", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("type", result.CommandResult.Command.Name);
    }

    [Fact]
    public void TypeCommand_WithTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["type", "JsonSerializer", "--package", "System.Text.Json", "--tfm", "net8.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TypeCommand_WithJson_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["type", "JsonSerializer", "--package", "System.Text.Json", "--json"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssemblyCommand_WithPackage_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["assembly", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("assembly", result.CommandResult.Command.Name);
    }

    [Fact]
    public void AssemblyCommand_WithTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["assembly", "--package", "System.Text.Json", "--tfm", "net8.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssemblyCommand_WithLocalPath_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["assembly", "MyLib.dll"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LlmsTxtCommand_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["llmstxt"]);

        Assert.Empty(result.Errors);
        Assert.Equal("llmstxt", result.CommandResult.Command.Name);
    }

    [Fact]
    public void HelpOption_IsAvailable()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["--help"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void VersionOption_IsAvailable()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["--version"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InvalidLimitValue_ProducesError()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "Foo", "-n", "not-a-number"]);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void PreprocessArgs_WithKnownCommand_ReturnsUnchanged()
    {
        var args = new[] { "package", "Foo" };
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Equal(args, result);
    }

    [Fact]
    public void PreprocessArgs_WithUnknownFirstArg_PrependsPackage()
    {
        var args = new[] { "System.Text.Json", "--versions" };
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Equal(["package", "System.Text.Json", "--versions"], result);
    }

    [Fact]
    public void PreprocessArgs_WithHelpFlag_ReturnsUnchanged()
    {
        var args = new[] { "--help" };
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Equal(args, result);
    }

    [Fact]
    public void PreprocessArgs_WithEmptyArgs_ReturnsEmpty()
    {
        var args = Array.Empty<string>();
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseVerbosity_WithNull_ReturnsMinimal()
    {
        var result = CommandLineBuilder.ParseVerbosity(null);

        Assert.Equal(Options.Verbosity.Minimal, result);
    }

    [Fact]
    public void ParseVerbosity_WithQ_ReturnsQuiet()
    {
        var result = CommandLineBuilder.ParseVerbosity("q");

        Assert.Equal(Options.Verbosity.Quiet, result);
    }

    [Fact]
    public void ParseVerbosity_WithColonPrefix_ParsesCorrectly()
    {
        var result = CommandLineBuilder.ParseVerbosity(":m");

        Assert.Equal(Options.Verbosity.Minimal, result);
    }

    [Fact]
    public void ParseSectionList_WithNull_ReturnsNull()
    {
        var result = CommandLineBuilder.ParseSectionList(null);

        Assert.Null(result);
    }

    [Fact]
    public void ParseSectionList_WithValidSections_ParsesCorrectly()
    {
        var result = CommandLineBuilder.ParseSectionList("1,3,5");

        Assert.NotNull(result);
        Assert.Contains(1, result);
        Assert.Contains(3, result);
        Assert.Contains(5, result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseSectionList_WithColonPrefix_ParsesCorrectly()
    {
        var result = CommandLineBuilder.ParseSectionList(":1,2");

        Assert.NotNull(result);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void ApiCommand_WithAllFlag_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--all"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithFilterOption_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "--package", "Spectre.Console", "--filter", "Progress*"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithAllFlagAndFilter_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "--package", "System.Text.Json", "--all", "--filter", "*Serializer*"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithFieldsOnly_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--fields-only"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_PackageWideWithFieldsOnly_ParsesCorrectly()
    {
        // --fields-only without a type name should still parse (even if behavior is limited)
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "--package", "System.Text.Json", "--fields-only"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithDocs_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "Newtonsoft.Json", "--docs"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithDocsAndMember_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonConvert", "--package", "Newtonsoft.Json", "--docs", "-m", "SerializeObject"]);

        Assert.Empty(result.Errors);
    }
}
