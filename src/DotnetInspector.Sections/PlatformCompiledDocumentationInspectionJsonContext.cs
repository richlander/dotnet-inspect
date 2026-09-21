using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>),
    TypeInfoPropertyName = "PlatformCompiledDocumentationInspectionEnvelope")]
[JsonSerializable(
    typeof(PlatformCompiledDocumentationInspectionOutcome),
    TypeInfoPropertyName = "PlatformCompiledDocumentationInspectionOutcome")]
[JsonSerializable(
    typeof(InspectionShare.Available),
    TypeInfoPropertyName = "InspectionShareAvailable")]
[JsonSerializable(
    typeof(CompiledDocumentationOutcome.Available),
    TypeInfoPropertyName = "CompiledDocumentationAvailable")]
public partial class PlatformCompiledDocumentationInspectionJsonContext :
    JsonSerializerContext;
