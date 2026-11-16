using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// Evaluates admitted Ecosystem Find artifacts for one normalized Type
/// question and returns the completed host-neutral inspection result.
/// </summary>
public static class EcosystemFindIndexedTypeInspection
{
    private static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "ecosystem-find/indexed-type/share",
            "Indexed Ecosystem Type Find requests do not yet have a "
                + "canonical Workspace Share projection.");

    public static InspectionEnvelope<
        EcosystemFindIndexedIntegrationResult.Type> Execute(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity>
            demandedSources,
        TypeFindQuestion question,
        int? maximumRows,
        string? typeFilter = null,
        CancellationToken cancellationToken = default)
        => ExecuteCore(
            artifacts,
            demandedSources,
            question,
            maximumRows,
            typeFilter,
            resultSession: null,
            cancellationToken);

    public static InspectionEnvelope<
        EcosystemFindIndexedIntegrationResult.Type> Execute(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity>
            demandedSources,
        TypeFindQuestion question,
        int? maximumRows,
        FindSemanticResultSession resultSession,
        string? typeFilter = null,
        CancellationToken cancellationToken = default)
        => ExecuteCore(
            artifacts,
            demandedSources,
            question,
            maximumRows,
            typeFilter,
            resultSession
                ?? throw new ArgumentNullException(nameof(resultSession)),
            cancellationToken);

    private static InspectionEnvelope<
        EcosystemFindIndexedIntegrationResult.Type> ExecuteCore(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity>
            demandedSources,
        TypeFindQuestion question,
        int? maximumRows,
        string? typeFilter,
        FindSemanticResultSession? resultSession,
        CancellationToken cancellationToken)
    {
        var request = new EcosystemFindIndexedIntegrationRequest(
            artifacts,
            demandedSources,
            question,
            maximumRows,
            typeFilter);
        var content =
            (EcosystemFindIndexedIntegrationResult.Type)
                (resultSession is null
                    ? EcosystemFindIndexedIntegration.Evaluate(
                        request,
                        cancellationToken)
                    : EcosystemFindIndexedIntegration.Evaluate(
                        request,
                        resultSession,
                        cancellationToken));
        return new(content, Share);
    }

    public static JsonElement ProjectContent(
        EcosystemFindIndexedIntegrationResult.Type content) =>
        JsonSerializer.SerializeToElement(
            content,
            EcosystemFindIndexedTypeInspectionJsonContext.Default
                .IndexedTypeFindContent);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(
    typeof(EcosystemFindIndexedIntegrationResult.Type),
    TypeInfoPropertyName = "IndexedTypeFindContent")]
[JsonSerializable(
    typeof(TypeFindPattern.Glob),
    TypeInfoPropertyName = "TypeFindPatternGlob")]
[JsonSerializable(
    typeof(MemberFindPattern.Glob),
    TypeInfoPropertyName = "MemberFindPatternGlob")]
internal sealed partial class EcosystemFindIndexedTypeInspectionJsonContext :
    JsonSerializerContext;
