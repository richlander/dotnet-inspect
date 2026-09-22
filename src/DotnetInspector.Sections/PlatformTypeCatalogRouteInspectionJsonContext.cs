using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(InspectionEnvelope<PlatformTypeCatalogRouteOutcome>),
    TypeInfoPropertyName = "PlatformTypeCatalogRouteInspectionEnvelope")]
public partial class PlatformTypeCatalogRouteInspectionJsonContext :
    JsonSerializerContext;
