using System.Text.Json;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;

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
}
