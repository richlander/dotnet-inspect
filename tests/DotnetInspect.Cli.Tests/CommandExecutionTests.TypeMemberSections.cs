using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_MemberSections_KeepTheSameDeclarations(bool includeAll)
    {
        string[] subject =
        ["type", "System.Text.Json.JsonSerializer", "--library",
            Path.Combine(AppContext.BaseDirectory, "RealAssets", "DiffAnalysis", "10.0.0", "System.Text.Json.dll")];
        if (includeAll)
            subject = [.. subject, "--all"];
        var plain = await RunAppAsync([.. subject, "-S", SectionNames.Members, "--jsonl"]);
        var documented = await RunAppAsync([.. subject, "-S", SectionNames.DocumentedMembers, "--jsonl"]);
        Assert.Equal(0, plain.Exit);
        Assert.Equal(0, documented.Exit);
        JsonNode[] rows = JsonLines(plain.Output);
        JsonNode[] documentedRows = JsonLines(documented.Output);
        Assert.True(rows.Length > 15);
        if (!includeAll)
            Assert.Equal(15, rows.Count(row => row["anchor"]!.GetValue<string>().StartsWith("Serialize~", StringComparison.Ordinal)));
        Assert.Equal(rows.Length, documentedRows.Length);
        var count = await RunAppAsync([.. subject, "-S", SectionNames.DocumentedMembers, "--count"]);
        Assert.Equal(0, count.Exit);
        Assert.Equal(rows.Length.ToString(), count.Output.Trim());
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Null(rows[index]["description"]);
            Assert.False(string.IsNullOrWhiteSpace(documentedRows[index]["description"]?.GetValue<string>()));
            documentedRows[index].AsObject().Remove("description");
            Assert.True(JsonNode.DeepEquals(rows[index], documentedRows[index]));
        }
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 5)]
    public async Task Type_MemberSections_MissingDocumentationKeepsDeclarations(
        bool includeAll, int expectedCount)
    {
        string directory = Path.Combine(Path.GetTempPath(), "type-member-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assembly = Path.Combine(directory, "Members.dll");
        File.Copy(TestAssemblyPath, assembly);
        try
        {
            string[] subject =
            ["type", typeof(TypeMemberSectionsFixture).FullName!, "--library", assembly];
            if (includeAll)
                subject = [.. subject, "--all"];
            var plain = await RunAppAsync([.. subject, "-S", SectionNames.Members, "--jsonl"]);
            var documented = await RunAppAsync([.. subject, "-S", SectionNames.DocumentedMembers, "--jsonl"]);
            Assert.Equal(0, plain.Exit);
            Assert.Equal(0, documented.Exit);
            JsonNode[] rows = JsonLines(plain.Output);
            JsonNode[] documentedRows = JsonLines(documented.Output);
            Assert.Equal(expectedCount, rows.Length);
            Assert.Equal(rows.Length, documentedRows.Length);
            Assert.All(documentedRows, row =>
                Assert.Contains("documentation", row["description"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
            JsonNode selected = rows.Single(row => row["signature"]!.GetValue<string>().Contains("int value"));
            var replay = await RunAppAsync("member", typeof(TypeMemberSectionsFixture).FullName!,
                selected["anchor"]!.GetValue<string>(), "--library", assembly);
            Assert.Equal(0, replay.Exit);
            Assert.Contains("int value", replay.Output);
            var count = await RunAppAsync([.. subject, "-S", SectionNames.DocumentedMembers, "--count"]);
            Assert.Equal(0, count.Exit);
            Assert.Equal(expectedCount.ToString(), count.Output.Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SectionNames.Members)]
    [InlineData(SectionNames.DocumentedMembers)]
    public async Task Type_MemberSections_KeepCountsWindowsAndFormats(string section)
    {
        string[] subject =
        ["type", "System.Text.Json.JsonSerializer", "--platform", "System.Text.Json",
            "-S", section, "-m", "Serialize", "--rows", "2..3"];
        var rows = await RunAppAsync([.. subject, "--jsonl"]);
        var count = await RunAppAsync([.. subject, "--count"]);
        var document = await RunAppAsync([.. subject, "--json"]);
        var projected = await RunAppAsync([.. subject, "--columns", "Anchor", "--tsv"]);
        Assert.Equal(0, rows.Exit);
        Assert.Equal(0, count.Exit);
        Assert.Equal(0, document.Exit);
        Assert.Equal(0, projected.Exit);
        Assert.Equal(2, JsonLines(rows.Output).Length);
        Assert.Equal("2", count.Output.Trim());
        Assert.StartsWith("anchor\n", projected.Output);
        using JsonDocument parsed = JsonDocument.Parse(document.Output);
        Assert.Equal(2, parsed.RootElement.EnumerateObject().Single().Value.GetArrayLength());
    }

    [Theory]
    [InlineData(SectionNames.Members)]
    [InlineData(SectionNames.DocumentedMembers)]
    public async Task Type_MemberSections_EmptyPopulationKeepsZeroCount(string section)
    {
        string[] subject =
        ["type", "System.Text.Json.JsonSerializer", "--platform", "System.Text.Json",
            "-S", section, "-k", "field"];
        var rows = await RunAppAsync([.. subject, "--jsonl"]);
        var count = await RunAppAsync([.. subject, "--count"]);
        Assert.Equal(0, rows.Exit);
        Assert.Equal(0, count.Exit);
        Assert.Empty(JsonLines(rows.Output));
        Assert.Equal("0", count.Output.Trim());
    }

    [Fact]
    public async Task Type_MemberSections_ProjectionDoesNotRequestDocumentation()
    {
        string[] subject =
        ["type", "System.Text.Json.JsonSerializer", "--platform", "System.Text.Json",
            "-S", SectionNames.Members];
        var plain = await RunAppAsync([.. subject, "--markdown", "-v:d"]);
        var projected = await RunAppAsync([.. subject, "--columns", "Description", "--tsv"]);
        Assert.Equal(0, plain.Exit);
        Assert.DoesNotContain("Description", plain.Output);
        Assert.NotEqual(0, projected.Exit);
    }
}

public class TypeMemberSectionsFixture
{
    public int Value;
    public void Execute() { }
    public void Execute(int value) { Value = value; }
    private void Reset() { Value = 0; }
}
