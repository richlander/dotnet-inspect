using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;

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
    typeof(LibraryTypePopulationRowsOutcome),
    TypeInfoPropertyName = "LibraryTypePopulationRowsOutcome")]
[JsonSerializable(
    typeof(LibraryTypePopulationRowsOutcome.Read),
    TypeInfoPropertyName = "LibraryTypePopulationRowsRead")]
[JsonSerializable(
    typeof(LibraryTypePopulationRowsOutcome.Unavailable),
    TypeInfoPropertyName = "LibraryTypePopulationRowsUnavailable")]
[JsonSerializable(
    typeof(LibraryTypePopulationRowsOutcome.Rejected),
    TypeInfoPropertyName = "LibraryTypePopulationRowsRejected")]
[JsonSerializable(
    typeof(LibraryTypePopulationRowsOutcome.Incomplete),
    TypeInfoPropertyName = "LibraryTypePopulationRowsIncomplete")]
[JsonSerializable(
    typeof(LibraryTypePopulationRowsOutcome.Failed),
    TypeInfoPropertyName = "LibraryTypePopulationRowsFailed")]
[JsonSerializable(
    typeof(LibraryTypeMemberCountOutcome),
    TypeInfoPropertyName = "LibraryTypeMemberCountOutcome")]
[JsonSerializable(
    typeof(LibraryTypeMemberCountOutcome.Counted),
    TypeInfoPropertyName = "LibraryTypeMemberCountCounted")]
[JsonSerializable(
    typeof(LibraryTypeMemberCountOutcome.NotApplicable),
    TypeInfoPropertyName = "LibraryTypeMemberCountNotApplicable")]
[JsonSerializable(
    typeof(LibraryEnablementsOutcome),
    TypeInfoPropertyName = "LibraryEnablementsOutcome")]
[JsonSerializable(
    typeof(LibraryEnablementsOutcome.Judged),
    TypeInfoPropertyName = "LibraryEnablementsJudged")]
[JsonSerializable(
    typeof(LibraryEnablementsOutcome.Failed),
    TypeInfoPropertyName = "LibraryEnablementsFailed")]
[JsonSerializable(
    typeof(InspectionShare.NonProjectable),
    TypeInfoPropertyName = "InspectionShareNonProjectable")]
public partial class LibraryInspectionJsonContext : JsonSerializerContext
{
}
