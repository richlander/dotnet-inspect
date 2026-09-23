using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(
    typeof(LibraryInspectionPlan),
    TypeInfoPropertyName = "LibraryInspectionPlan")]
[JsonSerializable(
    typeof(InspectionEnvelope<LibraryInspectionOutcome>),
    TypeInfoPropertyName = "LibraryInspectionEnvelope")]
[JsonSerializable(
    typeof(LibraryInspectionOutcome),
    TypeInfoPropertyName = "LibraryInspectionOutcome")]
[JsonSerializable(
    typeof(LibraryInspectionOutcome.Available),
    TypeInfoPropertyName = "LibraryInspectionAvailable")]
[JsonSerializable(
    typeof(LibraryInspectionOutcome.Rejected),
    TypeInfoPropertyName = "LibraryInspectionRejected")]
[JsonSerializable(
    typeof(LibraryInspectionOutcome.Failed),
    TypeInfoPropertyName = "LibraryInspectionFailed")]
[JsonSerializable(
    typeof(LibraryTypePopulationCountOutcome),
    TypeInfoPropertyName = "LibraryTypePopulationCountOutcome")]
[JsonSerializable(
    typeof(LibraryTypePopulationCountOutcome.Counted),
    TypeInfoPropertyName = "LibraryTypePopulationCounted")]
[JsonSerializable(
    typeof(LibraryTypePopulationCountOutcome.Unavailable),
    TypeInfoPropertyName = "LibraryTypePopulationCountUnavailable")]
[JsonSerializable(
    typeof(LibraryTypePopulationCountOutcome.Incomplete),
    TypeInfoPropertyName = "LibraryTypePopulationCountIncomplete")]
[JsonSerializable(
    typeof(InspectionShare.NonProjectable),
    TypeInfoPropertyName = "InspectionShareNonProjectable")]
public partial class LibraryInspectionJsonContext : JsonSerializerContext
{
}
