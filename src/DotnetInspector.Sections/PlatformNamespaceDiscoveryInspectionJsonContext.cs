using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(InspectionEnvelope<PlatformNamespaceDiscoveryOutcome>),
    TypeInfoPropertyName = "PlatformNamespaceDiscoveryInspectionEnvelope")]
public partial class PlatformNamespaceDiscoveryInspectionJsonContext :
    JsonSerializerContext;
