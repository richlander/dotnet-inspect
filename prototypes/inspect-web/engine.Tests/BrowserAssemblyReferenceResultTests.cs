using System.Text.Json;
using InspectWeb.Engine.PackageFacade;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserAssemblyReferenceResultTests
{
    [Fact]
    public void AvailableReferencesSerializeAsAnInlineListCase()
    {
        BrowserAssemblyReferenceResult result = new(
            new BrowserAssemblyReferenceList(
                [new("Example.Dependency", "1.2.3.4", null, "abcdef")]));

        using JsonDocument document = JsonDocument.Parse(Serialize(result));
        JsonElement reference = Assert.Single(
            document.RootElement.GetProperty("references").EnumerateArray());
        Assert.Equal("Example.Dependency", reference.GetProperty("name").GetString());
        Assert.Equal("1.2.3.4", reference.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("culture").ValueKind);
        Assert.Equal("abcdef", reference.GetProperty("publicKeyToken").GetString());
    }

    [Fact]
    public void AvailableEmptyReferencesAreNotAMissingResult()
    {
        BrowserAssemblyReferenceResult result = new(new BrowserAssemblyReferenceList([]));

        Assert.Equal("{\"references\":[]}", Serialize(result));
    }

    [Theory]
    [InlineData("The selected assembly could not be inspected.")]
    [InlineData("")]
    public void FailureMessagesRemainStringCases(string message)
    {
        BrowserAssemblyReferenceResult result = new(message);

        using JsonDocument document = JsonDocument.Parse(Serialize(result));
        Assert.Equal(JsonValueKind.String, document.RootElement.ValueKind);
        Assert.Equal(message, document.RootElement.GetString());
    }

    [Fact]
    public void DefaultResultRetainsTheNativeNullAlternative()
    {
        Assert.Equal("null", Serialize(default));
    }

    static string Serialize(BrowserAssemblyReferenceResult result) =>
        JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserAssemblyReferenceResult);
}
