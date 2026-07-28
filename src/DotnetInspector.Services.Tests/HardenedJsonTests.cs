using System.Text.Json;
using DotnetInspector.Core;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Negative fixtures for <see cref="HardenedJson"/>. JSON leaves duplicate-key resolution
/// undefined, so two readers of one payload can disagree; these pin that the product rejects such
/// payloads instead of silently binding one of the possible readings.
/// </summary>
public class HardenedJsonTests
{
    [Fact]
    public void Parse_RejectsDuplicateTopLevelProperty()
    {
        const string json = """{"id":"first","id":"second"}""";

        // Baseline: the unhardened parser accepts this and silently keeps one reading.
        using (var permissive = JsonDocument.Parse(json))
        {
            Assert.Equal("second", permissive.RootElement.GetProperty("id").GetString());
        }

        Assert.Throws<JsonException>(() => HardenedJson.Parse(json));
    }

    [Fact]
    public void Parse_RejectsDuplicateNestedProperty()
    {
        const string json = """{"outer":{"value":1,"value":2}}""";

        Assert.Throws<JsonException>(() => HardenedJson.Parse(json));
    }

    [Fact]
    public void Parse_RejectsDuplicatePropertyFromUtf8Bytes()
    {
        byte[] utf8 = "{\"id\":\"first\",\"id\":\"second\"}"u8.ToArray();

        Assert.Throws<JsonException>(() => HardenedJson.Parse(utf8.AsMemory()));
    }

    [Fact]
    public void Parse_AcceptsDistinctPropertiesThatDifferOnlyByCase()
    {
        // Duplicate detection is ordinal. Case-distinct names are distinct JSON members and must
        // keep parsing, otherwise the guard would reject ordinary payloads.
        using var doc = HardenedJson.Parse("""{"id":"a","Id":"b"}""");

        Assert.Equal("a", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("b", doc.RootElement.GetProperty("Id").GetString());
    }

    [Fact]
    public void Parse_AcceptsRepeatedNamesInSiblingObjects()
    {
        // The same name in different objects is not a duplicate.
        using var doc = HardenedJson.Parse("""{"a":{"name":1},"b":{"name":2}}""");

        Assert.Equal(1, doc.RootElement.GetProperty("a").GetProperty("name").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("b").GetProperty("name").GetInt32());
    }

    [Fact]
    public void Parse_AcceptsWellFormedDocument()
    {
        using var doc = HardenedJson.Parse("""{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0"}}""");

        Assert.Equal(
            ".NETCoreApp,Version=v10.0",
            doc.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString());
    }
}
