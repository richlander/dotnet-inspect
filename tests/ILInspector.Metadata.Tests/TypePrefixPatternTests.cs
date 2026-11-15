using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class TypePrefixPatternTests
{
    [Theory]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "JsonSerial", true)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "System.Text.Json.JsonSerial", true)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "System.Text.Json.Serial", false)]
    [InlineData("System.Text.Json.Serialization", new[] { "JsonConverter" }, "System.Text.Json.Serial", true)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "  jsonserial  ", true)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "JsonSerializer", false)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "System.Text.Json.JsonSerializer", false)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "Json.JsonSerial", false)]
    [InlineData("System.Text.Json", new[] { "JsonSerializer" }, "Serializer", false)]
    [InlineData("Example", new[] { "Outer`1", "Inner`2" }, "Outer<T>.In", true)]
    [InlineData("Example", new[] { "Outer`1", "Inner`2" }, "Outer`2", false)]
    [InlineData("Example", new[] { "Outer`1", "Inner`2" }, "Outer`1+In", false)]
    [InlineData("Literal+Namespace", new[] { "A+B", "Leaf.Dot" }, "A+B.Leaf.", true)]
    [InlineData("System", new[] { "StringBuilder" }, "string", true)]
    [InlineData("", new[] { "GlobalThing" }, "Global", true)]
    public void TypedAndCatalogNamesUseTheSameLiteralPrefixGrammar(
        string ns, string[] segments, string query, bool expected)
    {
        var name = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(ns, [.. segments])).Name;
        var prefix = new TypePrefixPattern(query);
        Assert.Equal(expected, prefix.Matches(name));
        string typeName = string.Join('.', segments);
        string fullName = ns.Length == 0 ? typeName : $"{ns}.{typeName}";
        Assert.Equal(expected, prefix.Matches(fullName, typeName));
    }
}
