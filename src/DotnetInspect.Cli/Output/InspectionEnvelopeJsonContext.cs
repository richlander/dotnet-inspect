using System.Text.Json.Serialization;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InspectionContentKind))]
[JsonSerializable(typeof(InspectionPortableProjection))]
[JsonSerializable(typeof(InspectionPortableProjection.Available))]
[JsonSerializable(typeof(InspectionPortableProjection.NonProjectable))]
[JsonSerializable(typeof(InspectionPortableProjectionFailureReason))]
[JsonSerializable(typeof(InspectionDiagnostic))]
internal partial class InspectionEnvelopeJsonContext : JsonSerializerContext;
