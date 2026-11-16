using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Cli.Output;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionShare))]
[JsonSerializable(typeof(InspectionShare.Available))]
[JsonSerializable(typeof(InspectionShare.NonProjectable))]
[JsonSerializable(typeof(InspectionDiagnostic))]
internal partial class InspectionEnvelopeJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphInspectionOutcome))]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphNodeClassification.Package),
    TypeInfoPropertyName = "PackageNodeClassification")]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphNodeClassification.Platform),
    TypeInfoPropertyName = "PlatformNodeClassification")]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphInspectionDestination.Package),
    TypeInfoPropertyName = "PackageDestination")]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphInspectionDestination.Platform),
    TypeInfoPropertyName = "PlatformDestination")]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphInspectionDestination.Unavailable),
    TypeInfoPropertyName = "UnavailableDestination")]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphInspectionOutcome.Unavailable),
    TypeInfoPropertyName = "UnavailableOutcome")]
[JsonSerializable(typeof(WorkspaceEcosystemPopulationDeclaration.Platform),
    TypeInfoPropertyName = "RegisteredPlatformPopulation")]
internal partial class PackageDependencyMemberCallGraphOutcomeJsonContext
    : JsonSerializerContext;
