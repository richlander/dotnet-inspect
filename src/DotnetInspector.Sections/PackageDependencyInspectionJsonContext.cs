using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(InspectionEnvelope<PackageDependencyInspectionOutcome>),
    TypeInfoPropertyName = "PackageDependencyInspectionEnvelope")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome),
    TypeInfoPropertyName = "PackageDependencyInspectionOutcome")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.Available),
    TypeInfoPropertyName = "PackageDependencyInspectionAvailable")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.NoManifest),
    TypeInfoPropertyName = "PackageDependencyInspectionNoManifest")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.Failed),
    TypeInfoPropertyName = "PackageDependencyInspectionFailed")]
[JsonSerializable(
    typeof(InspectionShare.NonProjectable),
    TypeInfoPropertyName = "InspectionShareNonProjectable")]
public partial class PackageDependencyInspectionJsonContext
    : JsonSerializerContext;
