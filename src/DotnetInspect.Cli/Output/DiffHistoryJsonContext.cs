using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Output;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiffHistoryJsonOutcome))]
[JsonSerializable(
    typeof(DiffHistoryApiMemberSubjectResolution.Failed),
    TypeInfoPropertyName = "DiffHistorySubjectResolutionFailed")]
[JsonSerializable(
    typeof(DiffHistoryApiParticipantEvidence.Available),
    TypeInfoPropertyName = "DiffHistoryParticipantAvailable")]
[JsonSerializable(
    typeof(DiffHistoryApiParticipantEvidence.Failed),
    TypeInfoPropertyName = "DiffHistoryParticipantFailed")]
[JsonSerializable(
    typeof(MemorySafetyRulesResult.Available),
    TypeInfoPropertyName = "MemorySafetyRulesAvailable")]
[JsonSerializable(
    typeof(MemorySafetyRulesResult.Unavailable),
    TypeInfoPropertyName = "MemorySafetyRulesUnavailable")]
[JsonSerializable(
    typeof(MemorySafetyMemberContractResult.Unavailable),
    TypeInfoPropertyName = "MemorySafetyMemberContractUnavailable")]
public sealed partial class DiffHistoryJsonContext : JsonSerializerContext;
