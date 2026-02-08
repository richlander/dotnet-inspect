using System.Text.Json.Serialization;
using DotnetInspector.Metadata;

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
[JsonSerializable(typeof(SampleReference))]
[JsonSerializable(typeof(List<SampleReference>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class ApiTypeJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(ApiType))]
[JsonSerializable(typeof(DocComment))]
[JsonSerializable(typeof(SampleReference))]
[JsonSerializable(typeof(List<SampleReference>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class ApiTypeCompactJsonContext : JsonSerializerContext
{
}

// Platform command JSON types
public record PlatformFrameworksJson(List<PlatformFrameworkJson> Frameworks);
public record PlatformFrameworkJson(string ShortName, string LatestVersion, int AssemblyCount);
public record PlatformVersionsJson(string ShortName, List<string> Versions);
public record PlatformAssembliesJson(string Framework, string Version, List<PlatformAssemblyJson> Assemblies);
public record PlatformAssemblyJson(string Name, int? Types);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<PlatformFrameworkJson>))]
[JsonSerializable(typeof(List<PlatformVersionsJson>))]
[JsonSerializable(typeof(List<PlatformAssembliesJson>))]
public partial class PlatformJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<PlatformFrameworkJson>))]
[JsonSerializable(typeof(List<PlatformVersionsJson>))]
[JsonSerializable(typeof(List<PlatformAssembliesJson>))]
public partial class PlatformCompactJsonContext : JsonSerializerContext
{
}
