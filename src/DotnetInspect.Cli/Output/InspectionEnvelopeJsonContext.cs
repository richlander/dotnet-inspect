using System.Text.Json.Serialization;
using DotnetInspector.Sections;

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
