using System.Text.Json;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

// Inherits Speed=Slow; covered by focused pre-merge and daily Deep Inspect gates.
public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("System.Reflection.BindingFlags", "Instance = 4", "Static = 8")]
    [InlineData("System.Math", "public const double PI = 3.141592653589793", "public const double E = 2.718281828459045")]
    public async Task Type_ApiDeclarations_PreserveConstantValues(
        string type,
        string firstConstant,
        string secondConstant)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", type,
            "--platform", "System.Private.CoreLib",
            "-S", "API Declarations",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(firstConstant, output);
        Assert.Contains(secondConstant, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_ApiDeclarations_InterfaceConstantsRemainAvailable(
        bool includeAll)
    {
        string[] scope = includeAll ? ["--all"] : [];
        var (exit, output, error) = await RunAppAsync([
            "type", "DotnetInspector.Fixtures.IApiDeclarationConstantsFixture",
            "--library", typeof(IApiDeclarationConstantsFixture).Assembly.Location,
            "-S", "API Declarations",
            "--tips", "q",
            .. scope]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("const int Answer = 42;", output);
        Assert.DoesNotContain("=>", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_ApiDeclarations_PreserveFloatingPointNegativeZero(
        bool includeAll)
    {
        string[] scope = includeAll ? ["--all"] : [];
        var (exit, output, error) = await RunAppAsync([
            "type", "DotnetInspector.Fixtures.ApiDeclarationConstantsFixture",
            "--library", typeof(ApiDeclarationConstantsFixture).Assembly.Location,
            "-S", "API Declarations",
            "--tips", "q",
            .. scope]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("const double NegativeZero = -0D;", output);
        Assert.Contains("const float FloatNegativeZero = -0F;", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_ApiDeclarations_ExtensionsBelongToDeclaringType(
        bool includeAll)
    {
        string[] scope = includeAll ? ["--all"] : [];
        string assembly = typeof(BodyShapeFixture).Assembly.Location;
        var (receiverExit, receiverOutput, receiverError) = await RunAppAsync([
            "type", "DotnetInspector.Fixtures.BodyShapeFixture",
            "--library", assembly,
            "-S", "API Declarations",
            "--tips", "q",
            .. scope]);
        var (declaringExit, declaringOutput, declaringError) = await RunAppAsync([
            "type", "DotnetInspector.Fixtures.BodyShapeFixtureExtensions",
            "--library", assembly,
            "-S", "API Declarations",
            "--tips", "q",
            .. scope]);

        Assert.Equal(0, receiverExit);
        Assert.Empty(receiverError);
        Assert.DoesNotContain("ProjectedCreation", receiverOutput);
        Assert.Equal(0, declaringExit);
        Assert.Empty(declaringError);
        Assert.Contains(
            "ProjectedCreation(this BodyShapeFixture value);",
            declaringOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_ApiDeclarations_PrivateExplicitImplementationsRequireAll(
        bool includeAll)
    {
        string[] scope = includeAll ? ["--all"] : [];
        var (exit, output, error) = await RunAppAsync([
            "type", "System.Collections.Generic.List<T>",
            "--platform", "System.Private.CoreLib",
            "-S", "API Declarations",
            "--tips", "q",
            .. scope]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        if (includeAll)
            Assert.Contains("System.Collections.IList.get_IsFixedSize();", output);
        else
            Assert.DoesNotContain("System.Collections.IList.get_IsFixedSize();", output);
    }

    [Fact]
    public async Task Type_ApiDeclarations_DefaultsToNativeApiVisibleSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("using System;\nnamespace System.Text.Json;", output);
        Assert.Contains("protected JsonNamingPolicy();", output);
        Assert.Contains("public abstract string ConvertName(string name);", output);
        Assert.DoesNotContain("static JsonNamingPolicy();", output);
        Assert.DoesNotContain("=>", output);
        Assert.DoesNotContain("# System.Text.Json.JsonNamingPolicy", output);
    }

    [Fact]
    public async Task Type_ApiDeclarations_AllIncludesNonPublicDeclarations()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations",
            "--all",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("protected JsonNamingPolicy();", output);
        Assert.Contains("static JsonNamingPolicy();", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public async Task Type_ApiDeclarations_MarkdownUsesCodeSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations",
            "--format=markdown",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Text.Json.JsonNamingPolicy", output);
        Assert.Contains("## API Declarations", output);
        Assert.Contains("```csharp", output);
        Assert.Contains("protected JsonNamingPolicy();", output);
    }

    [Fact]
    public async Task Type_ApiDeclarations_JsonRetainsCompletedEnvelope()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations",
            "--format=json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(
            "Available",
            root.GetProperty("content").GetProperty("outcome")
                .GetString());
        Assert.Equal(
            "ApiVisible",
            root.GetProperty("content").GetProperty("scope")
                .GetString());
        Assert.Contains(
            "protected JsonNamingPolicy();",
            root.GetProperty("content").GetProperty("text")
                .GetString());
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share").GetProperty("kind")
                .GetString());
        Assert.Equal(
            0,
            root.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task Type_ApiDeclarations_WithEmptySiblingRemainsDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations,Fields",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "Note: section 'Fields' has no data",
            error);
        Assert.StartsWith("# System.Text.Json.JsonNamingPolicy", output);
        Assert.Contains("## API Declarations", output);
        Assert.Contains("```csharp", output);
    }

    [Fact]
    public async Task Type_ApiDeclarations_PrintUsesExistingDocumentRoute()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy",
            "--platform", "System.Text.Json",
            "-S", "API Declarations",
            "--print",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("using System;\nnamespace System.Text.Json;", output);
        Assert.Contains("protected JsonNamingPolicy();", output);
    }
}
