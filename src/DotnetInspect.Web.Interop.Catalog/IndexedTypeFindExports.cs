using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Catalog;

public sealed record BrowserIndexedTypeFindOperationResult(
    BrowserIndexedTypeFindInspectionEnvelope Find,
    BrowserIndexedTypeFindCandidateActivation[] Activations);

public sealed record BrowserIndexedTypeFindInspectionEnvelope(
    JsonElement Content,
    BrowserVocabularyInspectionShare Share,
    BrowserVocabularyInspectionDiagnostic[] Diagnostics);

public sealed record BrowserIndexedTypeFindCandidateActivation(
    int MatchOrdinal,
    string? Identity,
    BrowserIndexedTypeFindActivationStatus Status,
    string? Action,
    string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserIndexedTypeFindActivationStatus>))]
public enum BrowserIndexedTypeFindActivationStatus
{
    Available,
    Unavailable,
    Stale,
    Ambiguous,
    Refused,
    Failed,
}

public sealed record BrowserIndexedTypeFindResult(
    BrowserIndexedTypeFindResultStatus Status,
    BrowserIndexedTypeFindOperationResult? Operation,
    string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserIndexedTypeFindResultStatus>))]
public enum BrowserIndexedTypeFindResultStatus
{
    Completed,
    Rejected,
    Unavailable,
    Stale,
}

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    [JSExport]
    public static async Task<string> FindIndexedTypes(
        string retainedDefinitionId,
        string realizationId,
        int resultGeneration,
        string text,
        int maximumRows)
    {
        BrowserEcosystemFindIndexAssetLoad assets =
            await BrowserEcosystemFindIndexAssets.LoadAsync()
                .ConfigureAwait(false);
        BrowserIndexedTypeFindResult wire;
        if (assets is BrowserEcosystemFindIndexAssetLoad.Invalid invalid)
        {
            wire = new(
                BrowserIndexedTypeFindResultStatus.Unavailable,
                Operation: null,
                invalid.Reason);
        }
        else
        {
            BrowserIndexedTypeFindExecutionResult result =
                await BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                        retainedDefinitionId,
                        realizationId,
                        ((BrowserEcosystemFindIndexAssetLoad.Available)assets)
                            .Artifacts,
                        text,
                        maximumRows,
                        resultGeneration)
                    .ConfigureAwait(false);
            wire = result switch
            {
                BrowserIndexedTypeFindExecutionResult.Completed completed =>
                    new(
                        BrowserIndexedTypeFindResultStatus.Completed,
                        ProjectIndexedTypeFindOperation(completed.Result),
                        Reason: null),
                BrowserIndexedTypeFindExecutionResult.Rejected rejected =>
                    new(
                        BrowserIndexedTypeFindResultStatus.Rejected,
                        Operation: null,
                        rejected.Reason),
                BrowserIndexedTypeFindExecutionResult.Unavailable unavailable =>
                    new(
                        BrowserIndexedTypeFindResultStatus.Unavailable,
                        Operation: null,
                        unavailable.Reason),
                BrowserIndexedTypeFindExecutionResult.Stale stale =>
                    new(
                        BrowserIndexedTypeFindResultStatus.Stale,
                        Operation: null,
                        stale.Reason),
                _ => throw new InvalidOperationException(
                    "Indexed Type Find returned an unsupported result."),
            };
        }
        return JsonSerializer.Serialize(
            wire,
            BrowserIndexedTypeFindJsonContext.Default
                .BrowserIndexedTypeFindResult);
    }

    internal static BrowserIndexedTypeFindOperationResult
        ProjectIndexedTypeFindOperation(
            DotnetInspect.Web.Interop.Catalog
                .BrowserIndexedTypeFindInspection result)
    {
        return new(
            new BrowserIndexedTypeFindInspectionEnvelope(
                EcosystemFindIndexedTypeInspection.ProjectContent(
                    result.Find.Content),
                BrowserVocabulary.ToBrowserShare(result.Find.Share),
                [
                    .. result.Find.Diagnostics.Select(
                        BrowserVocabulary.ToBrowserDiagnostic),
                ]),
            [
                .. result.Activations.Select(activation =>
                    new BrowserIndexedTypeFindCandidateActivation(
                        activation.MatchOrdinal,
                        activation.Identity,
                        Status(activation.Status),
                        activation.Action,
                        activation.Reason)),
            ]);
    }

    static BrowserIndexedTypeFindActivationStatus Status(
        DotnetInspect.Web.BrowserTypeFindActivationStatus status) =>
        status switch
        {
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Available =>
                BrowserIndexedTypeFindActivationStatus.Available,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Unavailable =>
                BrowserIndexedTypeFindActivationStatus.Unavailable,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Stale =>
                BrowserIndexedTypeFindActivationStatus.Stale,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Ambiguous =>
                BrowserIndexedTypeFindActivationStatus.Ambiguous,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Refused =>
                BrowserIndexedTypeFindActivationStatus.Refused,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Failed =>
                BrowserIndexedTypeFindActivationStatus.Failed,
            _ => throw new InvalidOperationException(
                "Indexed Type Find returned an unknown activation status."),
        };
}

internal static partial class BrowserRetainedWorkspaceActivationService
{
    static readonly BrowserIndexedTypeFindOperation IndexedTypeFind = new();

    internal static async ValueTask<BrowserIndexedTypeFindExecutionResult>
        FindIndexedTypesAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            string realizationId,
            IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
            string text,
            int maximumRows,
            long resultGeneration,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(artifacts);

        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                    retainedDefinitionId,
                    realizationId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserIndexedTypeFindExecutionResult.Unavailable(
                "The requested retained Workspace realization is not active.");
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        return IndexedTypeFind.Execute(
            operation,
            artifacts,
            retainedDefinitionId,
            realizationId,
            text,
            maximumRows,
            resultGeneration,
            cancellationToken);
    }

    internal static ValueTask<BrowserTypeFindActivationResult?>
        TryActivateIndexedTypeFindActionAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string action,
            CancellationToken cancellationToken = default) =>
        IndexedTypeFind.TryActivateAsync(
            owner,
            action,
            cancellationToken);

    internal static ValueTask<BrowserTypeFindActivationResult?>
        TryActivateIndexedTypeFindActionAsync(
            string action,
            CancellationToken cancellationToken = default) =>
        TryActivateIndexedTypeFindActionAsync(
            Owner,
            action,
            cancellationToken);

    internal static ValueTask<BrowserIndexedTypeFindExecutionResult>
        FindIndexedTypesAsync(
            string retainedDefinitionId,
            string realizationId,
            IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
            string text,
            int maximumRows,
            long resultGeneration,
            CancellationToken cancellationToken = default) =>
        FindIndexedTypesAsync(
            Owner,
            retainedDefinitionId,
            realizationId,
            artifacts,
            text,
            maximumRows,
            resultGeneration,
            cancellationToken);

    internal static void ResetIndexedTypeFindForTests() =>
        IndexedTypeFind.Reset();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserIndexedTypeFindResult))]
internal sealed partial class BrowserIndexedTypeFindJsonContext :
    JsonSerializerContext;
