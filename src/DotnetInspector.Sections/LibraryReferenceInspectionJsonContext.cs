using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(InspectionEnvelope<LibraryReferenceInspectionOutcome>))]
[JsonSerializable(
    typeof(LibraryReferenceInspectionOutcome.Available),
    TypeInfoPropertyName = "LibraryReferenceInspectionAvailable")]
[JsonSerializable(
    typeof(LibraryReferenceInspectionOutcome.Rejected),
    TypeInfoPropertyName = "LibraryReferenceInspectionRejected")]
[JsonSerializable(
    typeof(LibraryReferenceInspectionOutcome.Failed),
    TypeInfoPropertyName = "LibraryReferenceInspectionFailed")]
[JsonSerializable(typeof(AssemblyReferenceRow))]
[JsonSerializable(typeof(LibraryReferenceInspectionRejection))]
[JsonSerializable(typeof(LibraryReferenceInspectionFailure))]
[JsonSerializable(
    typeof(InspectionShare.NonProjectable),
    TypeInfoPropertyName = "InspectionShareNonProjectable")]
public partial class LibraryReferenceInspectionJsonContext
    : JsonSerializerContext;
