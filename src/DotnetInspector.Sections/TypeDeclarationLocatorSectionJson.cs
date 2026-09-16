using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

public static class TypeDeclarationLocatorSectionJson
{
    public static string Serialize(
        TypeDeclarationLocatorSectionResult result,
        bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        return compact
            ? JsonSerializer.Serialize(
                result,
                TypeDeclarationLocatorSectionCompactJsonContext
                    .Default
                    .TypeDeclarationLocatorSectionResult)
            : JsonSerializer.Serialize(
                result,
                TypeDeclarationLocatorSectionJsonContext
                    .Default
                    .TypeDeclarationLocatorSectionResult);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TypeDeclarationLocatorSectionResult))]
public partial class TypeDeclarationLocatorSectionJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TypeDeclarationLocatorSectionResult))]
public partial class TypeDeclarationLocatorSectionCompactJsonContext
    : JsonSerializerContext;
