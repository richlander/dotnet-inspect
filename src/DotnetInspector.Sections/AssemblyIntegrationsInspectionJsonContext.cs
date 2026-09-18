using System.Text.Json.Serialization;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssemblyIntegrationsEntry))]
[JsonSerializable(
    typeof(AssemblyIntegrationsEntry.Available),
    TypeInfoPropertyName = "AssemblyIntegrationsAvailable")]
[JsonSerializable(
    typeof(AssemblyIntegrationsEntry.Selected),
    TypeInfoPropertyName = "AssemblyIntegrationsSelected")]
[JsonSerializable(
    typeof(AssemblyIntegrationsEntry.Rejected),
    TypeInfoPropertyName = "AssemblyIntegrationsRejected")]
[JsonSerializable(
    typeof(AssemblyIntegrationsEntry.Failed),
    TypeInfoPropertyName = "AssemblyIntegrationsFailed")]
[JsonSerializable(typeof(AssemblyIntegrationOpportunitiesInspectionResult))]
[JsonSerializable(
    typeof(AssemblyIntegrationOpportunitiesEntry.Available),
    TypeInfoPropertyName = "AssemblyIntegrationOpportunitiesAvailable")]
[JsonSerializable(
    typeof(AssemblyIntegrationOpportunitiesEntry.Rejected),
    TypeInfoPropertyName = "AssemblyIntegrationOpportunitiesRejected")]
[JsonSerializable(
    typeof(AssemblyIntegrationOpportunitiesEntry.Failed),
    TypeInfoPropertyName = "AssemblyIntegrationOpportunitiesFailed")]
public partial class AssemblyIntegrationsInspectionJsonContext
    : JsonSerializerContext;
