using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionEnvelope<PackageVersionSettlementOutcome>))]
[JsonSerializable(typeof(PackageVersionSettlementOutcome))]
public partial class PackageVersionSettlementJsonContext : JsonSerializerContext;
