using System.Text.Json;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class SourcePayloadPrintTests
{
    public SourcePayloadPrintTests() => NuGetCache.Initialize("dotnet-inspect");

    // Two production invocations measured over two seconds; daily and focused gates own this.
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(SectionNames.DecompiledSource)]
    [InlineData(SectionNames.AnnotatedSource)]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    [InlineData(SectionNames.IL)]
    public async Task PrintPreservesTheNativePayload(string section)
    {
        var direct = await RunAsync(section);
        var printed = await RunAsync(section, "--print");

        Assert.Equal(0, direct.ExitCode);
        Assert.Empty(direct.Error);
        Assert.Equal(0, printed.ExitCode);
        Assert.Empty(printed.Error);
        Assert.NotEmpty(direct.Output);
        Assert.DoesNotContain("```", direct.Output);
        Assert.Equal(direct.Output.TrimEnd(), printed.Output.TrimEnd());
    }

    // PR-fast: untagged cases make bounded offline single-member requests.
    [Theory]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    public async Task OverlayPrintHonorsExplicitMarkdown(string section)
    {
        var result = await RunAsync(section, "--print", "--markdown");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.StartsWith($"# {section}", result.Output);
        Assert.Contains("```csharp\n", result.Output);
        Assert.Contains("public int GetArrayLength()", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CostOverlayPrintRetainsCostAnnotations()
    {
        var result = await RunMemberAsync(
            typeof(CostOverlayFixture), "Caller:1",
            SectionNames.CostOverlay, "--print", "--all");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("cost.callee", result.Output);
        Assert.Contains("alloc-loop", result.Output);
    }

    [Fact]
    public async Task SemanticsOverlayPrintRetainsSemanticsAnnotations()
    {
        var result = await RunAsync(SectionNames.SemanticsOverlay, "--print");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("semantics.callee", result.Output);
        Assert.Contains("InvalidOperationException", result.Output);
    }

    [Theory]
    [InlineData(SectionNames.CostOverlay, "--json")]
    [InlineData(SectionNames.CostOverlay, "--jsonl")]
    [InlineData(SectionNames.CostOverlay, "--json-array")]
    [InlineData(SectionNames.SemanticsOverlay, "--json")]
    [InlineData(SectionNames.SemanticsOverlay, "--jsonl")]
    [InlineData(SectionNames.SemanticsOverlay, "--json-array")]
    public async Task OverlayPrintUsesTheExistingDocumentProjection(
        string section,
        string format)
    {
        var result = await RunAsync(section, "--print", format);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement row = format == "--json-array"
            ? Assert.Single(document.RootElement.EnumerateArray())
            : document.RootElement;
        Assert.Equal(1, row.GetProperty("row").GetInt32());
        Assert.Equal(section, row.GetProperty("section").GetString());
        Assert.Equal(section, row.GetProperty("label").GetString());
        Assert.Contains(
            "public int GetArrayLength()",
            row.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    public async Task OverlayPrintRejectsAnAbsentDocumentRow(string section)
    {
        var result = await RunAsync(section, "--print", "--row", "2");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("row 2 is not in this section", result.Error);
    }

    [Theory]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    public async Task OverlayPrintWithoutABodyFailsWithoutSubstituteText(string section)
    {
        var result = await RunMemberAsync(
            typeof(JsonNamingPolicy), "ConvertName:1", section, "--print");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("selected section has no printable rows", result.Error);
    }

    [Theory]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    public async Task OverlayPrintHonorsTheRenderedLineLimit(string section)
    {
        var result = await RunAsync(section, "--print", "-n", "2", "--lines");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            2,
            result.Output.TrimEnd('\r', '\n').Split('\n').Length);
        Assert.StartsWith("public int GetArrayLength()", result.Output);
    }

    [Fact]
    public async Task FindingCensusStillRejectsPayloadProjection()
    {
        var result = await RunAsync(SectionNames.FindingCensus, "--print");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("indivisible document payload", result.Error);
    }

    static Task<(int ExitCode, string Output, string Error)> RunAsync(
        string section,
        params string[] options) =>
        RunMemberAsync(typeof(JsonElement), "GetArrayLength:1", section, options);

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        Type type,
        string member,
        string section,
        params string[] options) =>
        ConsoleCapture.RunAsync(async () =>
        {
            string? originalOffline =
                Environment.GetEnvironmentVariable("DOTNET_INSPECT_OFFLINE");
            try
            {
                Environment.SetEnvironmentVariable("DOTNET_INSPECT_OFFLINE", "1");
                string[] args =
                [
                    "member", type.FullName!,
                    "--library", type.Assembly.Location,
                    member, "-S", section, "--tips", "q",
                    .. options,
                ];
                var root = CommandLineBuilder.CreateRootCommand();
                string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
                return await CommandLineBuilder.InvokeAsync(
                    root.Parse(processed), processed);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "DOTNET_INSPECT_OFFLINE", originalOffline);
            }
        });
}
