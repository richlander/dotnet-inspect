using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(
    typeof(InspectionEnvelope<LibraryOverviewOutcome>),
    TypeInfoPropertyName = "LibraryOverviewInspectionEnvelope")]
[JsonSerializable(
    typeof(LibraryOverviewOutcome),
    TypeInfoPropertyName = "LibraryOverviewOutcome")]
[JsonSerializable(
    typeof(LibraryOverviewOutcome.Available),
    TypeInfoPropertyName = "LibraryOverviewAvailable")]
[JsonSerializable(
    typeof(LibraryOverviewOutcome.Incomplete),
    TypeInfoPropertyName = "LibraryOverviewIncomplete")]
[JsonSerializable(
    typeof(LibraryOverviewOutcome.Rejected),
    TypeInfoPropertyName = "LibraryOverviewRejected")]
[JsonSerializable(
    typeof(LibraryOverviewOutcome.Failed),
    TypeInfoPropertyName = "LibraryOverviewFailed")]
[JsonSerializable(
    typeof(LibraryOverviewIncompleteReason),
    TypeInfoPropertyName = "LibraryOverviewIncompleteReason")]
[JsonSerializable(
    typeof(LibraryOverviewIncompleteReason.ExtractionBound),
    TypeInfoPropertyName = "LibraryOverviewExtractionBound")]
[JsonSerializable(
    typeof(LibraryOverviewIncompleteReason.MetadataInspectionFailures),
    TypeInfoPropertyName = "LibraryOverviewMetadataInspectionFailures")]
[JsonSerializable(
    typeof(InspectionShare.NonProjectable),
    TypeInfoPropertyName = "InspectionShareNonProjectable")]
public partial class LibraryOverviewInspectionJsonContext :
    JsonSerializerContext;
