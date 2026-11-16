using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// The exact-name <c>diff -S "Changed Members" --type</c> section: the CLI
/// consumer of Finding Diff Any's Type route (finding-diff-any.md#type,
/// diff-capabilities.md ladder step 6.3).
/// </summary>
[Collection("Console")]
public sealed class ChangedMembersDiffCommandTests
{
    const string Section = "Changed Members";

    [Fact]
    public async Task Tsv_IsOneRowPerReportedMember()
    {
        var (exitCode, output, error) = await Run("-S", Section, "--type", "FindingDiffAnyFixture.BodyOnly");

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("owner\tmember\tapi\tbody\tselector", lines[0]);
        string[] row = Assert.Single(lines[1..]).Split('\t');
        Assert.Equal("Member", row[0]);
        Assert.Contains("BodyOnly", row[1], StringComparison.Ordinal);
        Assert.Equal(["Unchanged", "Changed"], row[2..4]);
        Assert.NotEmpty(row[4]);
    }

    [Fact]
    public async Task UnchangedType_WritesOnlyTheHeader()
    {
        var (exitCode, output, error) = await Run("-S", Section, "--type", "FindingDiffAnyFixture.Unchanged");

        Assert.True(exitCode == 0, error);
        Assert.Equal(
            ["owner\tmember\tapi\tbody\tselector"],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Json_IsTheRouteDocumentWithItsReceipt()
    {
        var (exitCode, output, error) = await Run(
            "-S", Section, "--type", "FindingDiffAnyFixture.Outer+Inner", "--json");

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.Equal("Compared", root.GetProperty("outcome").GetString());
        Assert.Equal("FindingDiffAnyFixture.Outer.Inner", root.GetProperty("fullName").GetString());
        Assert.Equal(
            "FindingDiffAnyFixture.Outer+Inner",
            root.GetProperty("comparison").GetProperty("type").GetString());
        Assert.Equal("Changed", root.GetProperty("counts").GetProperty("body").GetString());
        Assert.NotEmpty(root.GetProperty("rows").EnumerateArray());
        JsonElement receipt = root.GetProperty("receipt");
        Assert.Equal(
            ["FindingDiffAnyFixture.Outer.Inner"],
            receipt.GetProperty("typesAfter").EnumerateArray().Select(static name => name.GetString()));
    }

    [Fact]
    public async Task Markdown_StatesTheTypeAndItsChangedMembers()
    {
        var (exitCode, output, error) = await Run(
            "-S", Section, "--type", "FindingDiffAnyFixture.BodyOnly", "--markdown");

        Assert.True(exitCode == 0, error);
        Assert.Contains("Changed Members: FindingDiffAnyFixture.BodyOnly", output, StringComparison.Ordinal);
        Assert.Contains("1 of ", output, StringComparison.Ordinal);
        Assert.Contains("compared Members differs.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingType_IsAnError()
    {
        var (exitCode, _, error) = await Run("-S", Section);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires exactly one --type", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbsentType_IsNotCompared()
    {
        var (exitCode, _, error) = await Run("-S", Section, "--type", "FindingDiffAnyFixture.DoesNotExist");

        Assert.Equal(1, exitCode);
        Assert.Contains("Changed Members not compared", error, StringComparison.Ordinal);
        Assert.Contains("absent from both", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberFilter_IsRejected()
    {
        var (exitCode, _, error) = await Run(
            "-S", Section, "--type", "FindingDiffAnyFixture.BodyOnly", "--member", "Run");

        Assert.Equal(1, exitCode);
        Assert.Contains("--member", error, StringComparison.Ordinal);
    }

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] options)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{pair.OldAssemblyPath()}..{pair.NewAssemblyPath()}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }
}
