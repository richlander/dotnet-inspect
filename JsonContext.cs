using System.Text.Json.Serialization;

namespace DotnetInspector;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InspectionResult))]
[JsonSerializable(typeof(AssemblyAudit))]
[JsonSerializable(typeof(RidPackageReference))]
public partial class JsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiSurface))]
public partial class ApiJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiType))]
[JsonSerializable(typeof(DocComment))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class ApiTypeJsonContext : JsonSerializerContext
{
}
