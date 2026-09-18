using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionEnvelope<PackageVersionPopulationOutcome>))]
[JsonSerializable(typeof(PackageVersionPopulationOutcome))]
public partial class PackageVersionPopulationJsonContext : JsonSerializerContext;
