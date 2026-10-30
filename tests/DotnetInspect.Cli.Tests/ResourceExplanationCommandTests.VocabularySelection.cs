using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ResourceExplanationCommandTests
{
    [Theory]
    [InlineData(".data")]
    [InlineData(".hal")]
    public async Task VocabularyReading_ExactValuesUseSharedSelectionAndCloseOnlyRequiredTier(string projection)
    {
        var result = await RunAsync("explain", "vocabularies/csharp.style-choices", projection,
            "--json", "-Q", "slot-local-names");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var dataset = new VocabularyExplanation(CliVocabularyComposition.Snapshot)
            .SelectData(new("vocabularies/csharp.style-choices"), ["slot-local-names"]);
        JsonElement expected = dataset.ToJson(path => "inspect-resource:/" + path.Value,
            hal: projection == ".hal",
            bindHalAddress: path => "inspect-resource:/" + path.Value + "?projection=hal");
        Assert.True(JsonElement.DeepEquals(expected, document.RootElement));
        JsonElement vocabularies = projection == ".hal"
            ? document.RootElement.GetProperty("_embedded").GetProperty("vocabularies")
            : document.RootElement.GetProperty("vocabularies");
        Assert.Equal(2, projection == ".hal" ? vocabularies.GetArrayLength() : vocabularies.EnumerateObject().Count());
        Assert.Equal(4, dataset.Resources.Length);
    }

    [Theory]
    [InlineData("-Q")]
    [InlineData("--query-help")]
    public async Task VocabularyReading_ListSelectionPreservesNativeCaseAndOwnerOrder(string option)
    {
        var result = await RunAsync("explain", "vocabularies/csharp.body-kinds", ".data", "--json",
            option, " ObjectCreationExpression; AddressExpression, ObjectCreationExpression ");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement vocabulary = document.RootElement.GetProperty("vocabularies").GetProperty("csharp.body-kinds");
        Assert.Equal(["AddressExpression", "ObjectCreationExpression"], vocabulary.GetProperty("value_order")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(72, vocabulary.GetProperty("catalog_values").GetInt32());
        Assert.False(vocabulary.GetProperty("values").TryGetProperty("AwaitExpression", out _));
    }

    [Fact]
    public async Task VocabularyReading_NonDefaultSelectionDoesNotIncludeOtherDefaults()
    {
        var result = await RunAsync("explain", "vocabularies/api.accessibility", ".data", "--json", "-Q", "private");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var vocabulary = document.RootElement.GetProperty("vocabularies").GetProperty("api.accessibility");
        Assert.Equal("private", Assert.Single(vocabulary.GetProperty("values").EnumerateObject()).Name);
        Assert.Empty(vocabulary.GetProperty("defaults").EnumerateArray());
        Assert.Empty(vocabulary.GetProperty("property_sets").GetProperty("default").EnumerateArray());
    }

    [Theory]
    [InlineData(".data")]
    [InlineData(".hal")]
    public async Task VocabularyReading_BareOptionEqualsFullReading(string projection)
    {
        var full = await RunAsync("explain", "vocabularies/csharp.body-kinds", projection, "--json");
        var bare = await RunAsync("explain", "vocabularies/csharp.body-kinds", projection, "--json", "-Q");
        Assert.Equal(0, bare.ExitCode);
        Assert.Equal(full.Output, bare.Output);
    }

    [Fact]
    public async Task VocabularyReading_ProjectedHalAddressCanNarrowWithoutInventingAnAddress()
    {
        const string href = "inspect-resource:/vocabularies/csharp.body-kinds?projection=hal";
        var result = await RunAsync("explain", href, "--json", "-Q", "ObjectCreationExpression");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var links = document.RootElement.GetProperty("_links");
        Assert.Single(links.EnumerateObject());
        Assert.Equal(href, links.GetProperty("collection").GetProperty("href").GetString());
        var full = await RunAsync("explain", href, "--json");
        Assert.Equal(0, full.ExitCode);
        using var fullDocument = JsonDocument.Parse(full.Output);
        Assert.Equal(72, fullDocument.RootElement.GetProperty("_embedded").GetProperty("vocabularies")[0]
            .GetProperty("_embedded").GetProperty("values").GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ; ")]
    [InlineData("objectcreationexpression")]
    [InlineData("Object*")]
    [InlineData("unknown")]
    public async Task VocabularyReading_InvalidIdentityIsVisibleAndDoesNotOpenDestination(string id)
    {
        string file = Path.Combine(Path.GetTempPath(), "vocabulary-reading-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "existing content", TestContext.Current.CancellationToken);
            var result = await RunAsync("explain", "vocabularies/csharp.body-kinds", ".hal", "--json",
                "-Q", id, "--output", file);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
            Assert.Contains(string.IsNullOrWhiteSpace(id) || id == " , ; " ? "at least one" : "Unknown vocabulary value identity",
                result.Error);
            Assert.Equal("existing content", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("vocabularies", ".hal", "--json")]
    [InlineData("vocabularies/csharp.body-kinds/values/objectcreationexpression", ".hal", "--json")]
    [InlineData("package-query/query", ".hal", "--json")]
    [InlineData("literal", ".hal", "--json")]
    [InlineData("vocabularies/csharp.body-kinds", "--json")]
    [InlineData("vocabularies/csharp.body-kinds", ".contract", "--json")]
    [InlineData("vocabularies/csharp.body-kinds", ".hal", "--plaintext")]
    [InlineData("vocabularies/csharp.body-kinds", ".hal", "--json", "--depth", "0")]
    [InlineData("vocabularies/csharp.body-kinds", ".hal", "--json", "-n", "1")]
    public async Task VocabularyReading_RejectsInapplicableAdmission(params string[] args)
    {
        var result = await RunAsync(["explain", .. args, "-Q", "ObjectCreationExpression"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }
}
