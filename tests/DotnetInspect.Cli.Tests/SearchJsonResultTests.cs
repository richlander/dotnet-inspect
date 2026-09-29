using System.Text.Json;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;

namespace DotnetInspect.Cli.Tests;

public class SearchJsonResultTests
{
    [Fact]
    public void ExtensionResult_PreservesPublicJsonFieldNames()
    {
        var result = ExtensionMethodJsonResult.From(new ExtensionMethodResult
        {
            MethodName = "Select",
            ExtensionClass = "Enumerable",
            ExtendedType = "IEnumerable<T>",
            Assembly = "System.Linq",
            SourceVersion = "11.0.0",
        });

        var json = JsonSerializer.Serialize(
            new List<ExtensionMethodJsonResult> { result },
            ExtensionsJsonContext.Default.ListExtensionMethodJsonResult);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement[0];

        Assert.Equal("Select", item.GetProperty("method").GetString());
        Assert.Equal("Enumerable", item.GetProperty("class").GetString());
        Assert.Equal("IEnumerable<T>", item.GetProperty("extended_type").GetString());
        Assert.Equal("System.Linq", item.GetProperty("library").GetString());
        Assert.Equal("11.0.0", item.GetProperty("source_version").GetString());
        Assert.False(item.TryGetProperty("method_name", out _));
    }

    [Fact]
    public void TypeRelationResult_PreservesPublicJsonFieldNames()
    {
        var result = new TypeRelationResult(
            "Example.Widget",
            "interface",
            "Example",
            "Package",
            "1.0.0");

        var json = JsonSerializer.Serialize(
            new List<TypeRelationResult> { result },
            TypeRelationsJsonContext.Default.ListTypeRelationResult);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement[0];

        Assert.Equal("Example.Widget", item.GetProperty("type").GetString());
        Assert.Equal("Example", item.GetProperty("library").GetString());
        Assert.False(item.TryGetProperty("type_name", out _));
    }
}
