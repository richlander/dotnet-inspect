using DotnetInspector;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for command line parsing behavior.
/// </summary>
[Collection("Console")]
public class CommandLineTests
{
    [Fact]
    public void RootCommand_WithNoArgs_ShowsHelpWithoutError()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse([]);

        // Root command has a default action (help + tips), so no parse errors
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RootCommand_WithVerbosityQuiet_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["-v:q"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RootCommand_WithVerbosityQuiet_SuppressesTips()
    {
        var originalErr = Console.Error;
        var originalOut = Console.Out;
        var errWriter = new System.IO.StringWriter();
        Console.SetError(errWriter);
        Console.SetOut(TextWriter.Null);
        try
        {
            var root = CommandLineBuilder.CreateRootCommand();
            await root.Parse(["-v:q"]).InvokeAsync(null, TestContext.Current.CancellationToken);
            var stderr = errWriter.ToString();
            Assert.DoesNotContain("Tips:", stderr);
        }
        finally
        {
            Console.SetError(originalErr);
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task RootCommand_WithoutVerbosityQuiet_ShowsTips()
    {
        var originalErr = Console.Error;
        var originalOut = Console.Out;
        var originalTips = Environment.GetEnvironmentVariable("DOTNET_INSPECT_TIPS");
        var errWriter = new System.IO.StringWriter();
        Console.SetError(errWriter);
        Console.SetOut(TextWriter.Null);
        Environment.SetEnvironmentVariable("DOTNET_INSPECT_TIPS", null);
        try
        {
            var root = CommandLineBuilder.CreateRootCommand();
            await root.Parse([]).InvokeAsync(null, TestContext.Current.CancellationToken);
            var stderr = errWriter.ToString();
            Assert.Contains("Tips:", stderr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_TIPS", originalTips);
            Console.SetError(originalErr);
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteTips_WithQuietLevel_WritesNothing()
    {
        var originalErr = Console.Error;
        var errWriter = new System.IO.StringWriter();
        Console.SetError(errWriter);
        try
        {
            Hints.WriteTips(TipLevel.Quiet, new Tip("package", "Foo", "inspect"));
            Assert.Empty(errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void WriteTips_WithMinimalLevel_WritesTips()
    {
        var originalErr = Console.Error;
        var errWriter = new System.IO.StringWriter();
        Console.SetError(errWriter);
        try
        {
            Hints.WriteTips(TipLevel.Minimal, new Tip("package", "Foo", "inspect"));
            Assert.Contains("Tips:", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
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
    public void PackageCommand_WithDependencies_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "System.Text.Json", "--dependencies"]);

        Assert.Empty(result.Errors);
        Assert.Equal("package", result.CommandResult.Command.Name);
    }

    [Fact]
    public void PackageCommand_WithDependenciesAndTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["package", "System.Text.Json", "--dependencies", "--tfm", "net8.0"]);

        Assert.Empty(result.Errors);
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
    public void ApiCommand_WithShape_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--shape"]);

        Assert.Empty(result.Errors);
        Assert.Equal("api", result.CommandResult.Command.Name);
    }

    [Fact]
    public void ApiCommand_WithShapeAndTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--tfm", "net8.0", "--shape"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithShapeAndJson_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "JsonSerializer", "--package", "System.Text.Json", "--json", "--shape"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithShapeAndPlatform_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "List`1", "--platform", "System.Collections", "--shape"]);

        Assert.Empty(result.Errors);
        Assert.Equal("api", result.CommandResult.Command.Name);
    }

    [Fact]
    public void ApiCommand_WithShapeAndPlatformAndFramework_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "List`1", "--platform", "System.Collections", "--framework", "runtime", "--shape"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssemblyCommand_WithPackage_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["library", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("library", result.CommandResult.Command.Name);
    }

    [Fact]
    public void AssemblyCommand_WithTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["library", "System.Text.Json", "--tfm", "net8.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssemblyCommand_WithLocalPath_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["library", "MyLib.dll"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssemblyCommand_WithDependencies_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["library", "MyLib.dll", "--dependencies"]);

        Assert.Empty(result.Errors);
        Assert.Equal("library", result.CommandResult.Command.Name);
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
    public void PreprocessArgs_WithUnknownFirstArg_PrependsRouter()
    {
        var args = new[] { "System.Text.Json", "--versions" };
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Equal(["router", "System.Text.Json", "--versions"], result);
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

    [Theory]
    [InlineData("System.Runtime", true)]
    [InlineData("System.Text.Json", true)]
    [InlineData("Microsoft.Extensions.Logging", true)]
    [InlineData("Microsoft.AspNetCore.App", true)]
    [InlineData("Newtonsoft.Json", false)]
    [InlineData("Markout", false)]
    [InlineData("system.runtime", true)] // case-insensitive
    [InlineData("MICROSOFT.EXTENSIONS", true)]
    public void IsPlatformCandidate_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, PlatformResolver.IsPlatformCandidate(name));
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
        var result = CommandLineBuilder.ParseSectionList("Package,Statistics,Files");

        Assert.NotNull(result);
        Assert.Contains("Package", result);
        Assert.Contains("Statistics", result);
        Assert.Contains("Files", result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseSectionList_WithColonPrefix_ParsesCorrectly()
    {
        var result = CommandLineBuilder.ParseSectionList(":Package,Statistics");

        Assert.NotNull(result);
        Assert.Contains("Package", result);
        Assert.Contains("Statistics", result);
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

    [Fact]
    public void DiffCommand_WithPackageVersionRange_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
        Assert.Equal("diff", result.CommandResult.Command.Name);
    }

    [Fact]
    public void DiffCommand_WithPlatformVersionRange_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--platform", "System.Text.Json@9.0..10.0"]);

        Assert.Empty(result.Errors);
        Assert.Equal("diff", result.CommandResult.Command.Name);
    }

    [Fact]
    public void DiffCommand_WithTypeArgument_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "JsonSerializer", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithTypeFilter_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "-t", "JsonSerializer", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithMultipleTypeFilters_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "-t", "JsonSerializer", "-t", "JsonSerializerOptions", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithTypeFilterLongForm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--type", "JsonSerializer", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithAllFlag_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--package", "System.Text.Json@8.0.0..9.0.0", "--all"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithFramework_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--platform", "System.Text.Json@9.0..10.0", "--framework", "runtime"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithTfm_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--package", "System.Text.Json@8.0.0..9.0.0", "--tfm", "net8.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithTypeArgAndTypeFilter_ParsesCorrectly()
    {
        // Both positional type and -t should be allowed (merged together)
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "JsonSerializer", "-t", "JsonSerializerOptions", "--package", "System.Text.Json@8.0.0..9.0.0"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithPattern_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("find", result.CommandResult.Command.Name);
    }

    [Fact]
    public void FindCommand_WithOneLine_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--package", "System.Text.Json", "--oneline"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithOneLineGrouped_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--package", "System.Text.Json", "--oneline", "--grouped"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithFramework_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--framework", "runtime"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithMultiplePatterns_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Option*,Argument*,Command*", "--package", "System.CommandLine"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithLimit_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--framework", "runtime", "-n", "10"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FindCommand_WithNameOnly_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["find", "Json*", "--package", "System.Text.Json", "--name-only"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithStat_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--package", "System.Text.Json@8.0.0..9.0.0", "--stat"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DiffCommand_WithNameOnly_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["diff", "--package", "System.Text.Json@8.0.0..9.0.0", "--name-only"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SearchCommand_IsAliasForFind()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["search", "Json*", "--package", "System.Text.Json"]);

        Assert.Empty(result.Errors);
        Assert.Equal("find", result.CommandResult.Command.Name);
    }

    [Fact]
    public void PreprocessArgs_WithSearchCommand_ReturnsUnchanged()
    {
        var args = new[] { "search", "Json*", "--package", "Foo" };
        var result = CommandLineBuilder.PreprocessArgs(args);

        Assert.Equal(args, result);
    }

    [Fact]
    public void ApiCommand_WithHierarchy_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "Command", "--package", "System.CommandLine", "--hierarchy"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ApiCommand_WithInterfacesAndHierarchy_ParsesCorrectly()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["api", "Command", "--package", "System.CommandLine", "--interfaces", "--hierarchy"]);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(false, false, null, false)]  // --files
    [InlineData(false, false, null, true)]   // --layout
    [InlineData(true, false, null, false)]   // --files --lib
    [InlineData(true, false, null, true)]    // --layout --lib
    [InlineData(false, true, null, false)]   // --files --tools
    [InlineData(false, true, null, true)]    // --layout --tools
    [InlineData(false, false, "net8.0", false)] // --files --tfm net8.0
    [InlineData(false, false, "net8.0", true)]  // --layout --tfm net8.0
    public void WriteFileLayoutTips_NeverWritesTips(bool scopeLib, bool scopeTools, string? tfm, bool isLayout)
    {
        var originalErr = Console.Error;
        var originalTips = Environment.GetEnvironmentVariable("DOTNET_INSPECT_TIPS");
        var errWriter = new System.IO.StringWriter();
        Console.SetError(errWriter);
        Environment.SetEnvironmentVariable("DOTNET_INSPECT_TIPS", null);
        try
        {
            var options = new InspectionOptions
            {
                ScopeLib = scopeLib,
                ScopeTools = scopeTools,
                Tfm = tfm,
                TipLevel = TipLevel.Detailed,
            };
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(tempDir, "lib"));
            Directory.CreateDirectory(Path.Combine(tempDir, "tools"));
            try
            {
                PackageCommand.WriteFileLayoutTips(tempDir, options, "TestPackage", TipLevel.Detailed, isLayout);
                Assert.DoesNotContain("Tips:", errWriter.ToString());
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_TIPS", originalTips);
            Console.SetError(originalErr);
        }
    }
}
