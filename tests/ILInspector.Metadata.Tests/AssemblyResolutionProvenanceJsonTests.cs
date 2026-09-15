using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class AssemblyResolutionProvenanceJsonTests
{
    [Theory]
    [InlineData("package")]
    [InlineData("platform")]
    [InlineData("project")]
    [InlineData("local")]
    [InlineData("embedded")]
    [InlineData("designated")]
    public void ProvenanceRetainsItsCaseAndFieldsAcrossJson(string kind)
    {
        AssemblyResolutionProvenance provenance = kind switch
        {
            "package" => AssemblyResolutionProvenance.Package(
                "Npgsql.EntityFrameworkCore.PostgreSQL", "8.0.4", "net8.0", null),
            "platform" => AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App", "11.0.0", "runtime"),
            "project" => AssemblyResolutionProvenance.Project(
                "src/App/App.csproj", "net8.0", "linux-x64"),
            "local" => AssemblyResolutionProvenance.Local("input.dll"),
            "embedded" => AssemblyResolutionProvenance.Embedded("input", "digest", "input.dll"),
            "designated" => AssemblyResolutionProvenance.Designated("input.dll"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        string json = JsonSerializer.Serialize(
            provenance, ProvenanceTestJsonContext.Default.AssemblyResolutionProvenance);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(kind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(provenance, JsonSerializer.Deserialize(
            json, ProvenanceTestJsonContext.Default.AssemblyResolutionProvenance));
        if (kind == "package")
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("rid").ValueKind);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssemblyResolutionProvenance))]
internal partial class ProvenanceTestJsonContext : JsonSerializerContext;
