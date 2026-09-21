using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
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
            "--markdown",
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
            "--json",
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
