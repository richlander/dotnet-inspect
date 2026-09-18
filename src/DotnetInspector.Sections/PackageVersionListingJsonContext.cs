using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionEnvelope<PackageVersionListingOutcome>))]
[JsonSerializable(typeof(PackageVersionListingOutcome))]
public partial class PackageVersionListingJsonContext : JsonSerializerContext;
