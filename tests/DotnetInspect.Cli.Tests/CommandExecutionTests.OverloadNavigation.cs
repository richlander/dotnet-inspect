using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Member_OverloadSections_PreserveRowsAndReplayableAnchors()
    {
        string[] subject =
        [
            "member", "System.Text.Json.JsonSerializer.DeserializeAsync",
            "--platform", "System.Text.Json",
        ];
        var plain = await RunAppAsync(
            [.. subject, "-S", SectionNames.Overloads, "--jsonl"]);
        var documented = await RunAppAsync(
            [.. subject, "-S", SectionNames.DocumentedOverloads, "--jsonl"]);

        Assert.Equal(0, plain.Exit);
        Assert.Equal(0, documented.Exit);
        Assert.Empty(plain.Error);
        Assert.Empty(documented.Error);
        JsonNode[] rows = JsonLines(plain.Output);
        JsonNode[] documentedRows = JsonLines(documented.Output);
        Assert.Equal(10, rows.Length);
        Assert.Equal(rows.Length, documentedRows.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Equal(
                $"DeserializeAsync:{index + 1}",
                rows[index]["select"]!.GetValue<string>());
            Assert.Matches(
                @"^DeserializeAsync~[0-9a-f]{10}$",
                rows[index]["anchor"]!.GetValue<string>());
            Assert.Null(rows[index]["description"]);
            Assert.False(string.IsNullOrEmpty(
                documentedRows[index]["description"]!.GetValue<string>()));
            documentedRows[index].AsObject().Remove("description");
            Assert.True(JsonNode.DeepEquals(rows[index], documentedRows[index]));
        }

        var ordinal = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer",
            rows[6]["select"]!.GetValue<string>(),
            "--platform", "System.Text.Json");
        var anchor = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer",
            rows[6]["anchor"]!.GetValue<string>(),
            "--platform", "System.Text.Json");
        Assert.Equal(0, ordinal.Exit);
        Assert.Equal(ordinal, anchor);
        Assert.Contains(rows[6]["signature"]!.GetValue<string>(), ordinal.Output);

        var tree = await RunAppAsync([.. subject, "--tree"]);
        Assert.Equal(0, tree.Exit);
        Assert.Contains(
            $":7 {rows[6]["signature"]!.GetValue<string>()}",
            tree.Output);
    }

    [Theory]
    [InlineData(SectionNames.Overloads)]
    [InlineData(SectionNames.DocumentedOverloads)]
    public async Task Member_OverloadSections_KeepWindowsCountsAndProjections(
        string section)
    {
        string[] subject =
        [
            "member", "System.Text.Json.JsonSerializer.DeserializeAsync",
            "--platform", "System.Text.Json", "-S", section,
            "--rows", "2..3",
        ];
        var rows = await RunAppAsync([.. subject, "--jsonl"]);
        var count = await RunAppAsync([.. subject, "--count"]);
        var projected = await RunAppAsync(
            [.. subject, "--columns", "Anchor", "--tsv"]);
        var document = await RunAppAsync([.. subject, "--json"]);

        Assert.Equal(0, rows.Exit);
        Assert.Equal(0, count.Exit);
        Assert.Equal(0, projected.Exit);
        Assert.Equal(0, document.Exit);
        Assert.Equal(2, JsonLines(rows.Output).Length);
        Assert.Contains("DeserializeAsync:2", rows.Output);
        Assert.DoesNotContain("DeserializeAsync:1\"", rows.Output);
        Assert.Equal("2", count.Output.Trim());
        Assert.StartsWith("anchor\nDeserializeAsync~", projected.Output);
        using JsonDocument parsed = JsonDocument.Parse(document.Output);
        JsonElement population = parsed.RootElement.EnumerateObject().Single().Value;
        Assert.Equal(2, population.GetArrayLength());
    }

    [Theory]
    [InlineData(SectionNames.Overloads, false)]
    [InlineData(SectionNames.DocumentedOverloads, true)]
    public async Task Member_OverloadSections_DiscoveryDoesNotRequireAttachedRows(
        string section,
        bool documented)
    {
        var result = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.DeserializeAsync",
            "--platform", "System.Text.Json",
            "--discover", section,
            "--markdown");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains("Select", result.Output);
        Assert.Contains("Anchor", result.Output);
        Assert.Contains("Signature", result.Output);
        Assert.Equal(documented, result.Output.Contains("Description"));
    }

    [Fact]
    public async Task Member_OverloadSections_ComposePlainAndDocumentedPopulations()
    {
        var result = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.DeserializeAsync",
            "--platform", "System.Text.Json",
            "-S", $"{SectionNames.DocumentedOverloads};{SectionNames.Overloads}",
            "--rows", "2..3", "--json");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonProperty[] sections = [.. document.RootElement.EnumerateObject()];
        Assert.Equal(2, sections.Length);
        JsonElement[] documented =
            [.. document.RootElement.GetProperty("documented_overloads").EnumerateArray()];
        JsonElement[] plain =
            [.. document.RootElement.GetProperty("overloads").EnumerateArray()];
        Assert.Equal(2, plain.Length);
        Assert.Equal(plain.Length, documented.Length);
        for (int index = 0; index < plain.Length; index++)
        {
            Assert.False(plain[index].TryGetProperty("description", out _));
            Assert.True(documented[index].TryGetProperty("description", out _));
            Assert.Equal(
                plain[index].GetProperty("anchor").GetString(),
                documented[index].GetProperty("anchor").GetString());
        }
    }

    [Fact]
    public async Task Member_OverloadSections_DocumentationIsExplicitAndMissingRowsRemain()
    {
        string directory = Path.Combine(
            Environment.CurrentDirectory, "artifacts",
            $"overload-documentation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "System.Text.Json.dll");
        try
        {
            File.Copy(typeof(JsonSerializer).Assembly.Location, path);
            File.WriteAllText(
                Path.ChangeExtension(path, ".xml"),
                """
                <doc><members>
                  <member name="M:System.Text.Json.JsonSerializer.Serialize``1(``0,System.Text.Json.JsonSerializerOptions)">
                    <summary>Serializes one JSON value.</summary>
                  </member>
                </members></doc>
                """);
            string[] subject =
            [
                "member", "System.Text.Json.JsonSerializer.Serialize",
                "--library", path,
            ];
            var plain = await RunAppAsync(
                [.. subject, "-S", SectionNames.Overloads, "--table"]);
            var documented = await RunAppAsync(
                [.. subject, "-S", SectionNames.DocumentedOverloads, "--jsonl"]);
            var invalidProjection = await RunAppAsync(
                [.. subject, "-S", SectionNames.Overloads,
                    "--columns", "Description", "--table"]);

            Assert.Equal(0, plain.Exit);
            Assert.DoesNotContain("Description", plain.Output);
            Assert.DoesNotContain("Serializes one JSON value.", plain.Output);
            Assert.Equal(0, documented.Exit);
            Assert.Equal(15, JsonLines(documented.Output).Length);
            Assert.Contains("Serializes one JSON value.", documented.Output);
            Assert.All(JsonLines(documented.Output), row =>
                Assert.False(string.IsNullOrEmpty(
                    row["description"]!.GetValue<string>())));
            Assert.Equal(1, invalidProjection.Exit);
            Assert.DoesNotContain("Serializes one JSON value.", invalidProjection.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonNode[] JsonLines(string output) =>
    [
        .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonNode.Parse(line)!),
    ];
}
