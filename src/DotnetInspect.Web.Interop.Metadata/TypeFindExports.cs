using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Ecosystems;
using DotnetInspector.Sections;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
public static partial class MetadataExports
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
                BrowserTypeFindResultStatus.Unavailable,
                Operation: null,
                invalid.Reason);
        }
        else
        {
            BrowserIndexedTypeFindExecutionResult result =
                await BrowserRetainedWorkspaceActivationRegistry.Owner
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
                        BrowserTypeFindResultStatus.Completed,
                        ProjectIndexedTypeFindOperation(completed.Result),
                        Reason: null),
                BrowserIndexedTypeFindExecutionResult.Rejected rejected =>
                    new(
                        BrowserTypeFindResultStatus.Rejected,
                        Operation: null,
                        rejected.Reason),
                BrowserIndexedTypeFindExecutionResult.Unavailable unavailable =>
                    new(
                        BrowserTypeFindResultStatus.Unavailable,
                        Operation: null,
                        unavailable.Reason),
                BrowserIndexedTypeFindExecutionResult.Stale stale =>
                    new(
                        BrowserTypeFindResultStatus.Stale,
                        Operation: null,
                        stale.Reason),
                _ => throw new InvalidOperationException(
                    "Indexed Type Find returned an unsupported result."),
            };
        }
        return JsonSerializer.Serialize(
            wire,
            BrowserTypeFindJsonContext.Default.BrowserIndexedTypeFindResult);
    }

    [JSExport]
    public static async Task<string> FindTypes(
        string retainedDefinitionId,
        string realizationId,
        int resultGeneration,
        string text)
    {
        BrowserTypeFindExecutionResult result =
            await BrowserRetainedWorkspaceActivationRegistry.Owner
                .FindTypesAsync(
                    retainedDefinitionId,
                    realizationId,
                    text,
                    resultGeneration)
                .ConfigureAwait(false);
        BrowserTypeFindResult wire = result switch
        {
            BrowserTypeFindExecutionResult.Completed completed =>
                new(
                    BrowserTypeFindResultStatus.Completed,
                    Operation(completed.Result),
                    Reason: null),
            BrowserTypeFindExecutionResult.Rejected rejected =>
                new(
                    BrowserTypeFindResultStatus.Rejected,
                    Operation: null,
                    rejected.Reason),
            BrowserTypeFindExecutionResult.Unavailable unavailable =>
                new(
                    BrowserTypeFindResultStatus.Unavailable,
                    Operation: null,
                    unavailable.Reason),
            BrowserTypeFindExecutionResult.Stale stale =>
                new(
                    BrowserTypeFindResultStatus.Stale,
                    Operation: null,
                    stale.Reason),
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported result."),
        };
        return JsonSerializer.Serialize(
            wire,
            BrowserTypeFindJsonContext.Default.BrowserTypeFindResult);
    }

    static BrowserTypeFindOperationResult Operation(
        DotnetInspect.Web.BrowserTypeFindOperationResult result) =>
        new(
            new InspectionEnvelope<JsonElement>(
                JsonSerializer.SerializeToElement(
                    result.Find.Content,
                    TypeDeclarationLocatorSectionJsonContext.Default
                        .TypeDeclarationLocatorSectionResult),
                result.Find.Share,
                result.Find.Diagnostics),
            [
                .. result.Activations.Select(activation =>
                    new BrowserTypeFindCandidateActivation(
                        new(
                            activation.Candidate.AnswerOrdinal,
                            activation.Candidate.CandidateOrdinal),
                        Source(activation.Source),
                        Status(activation.Status),
                        activation.Action,
                        activation.Reason)),
            ]);

    internal static BrowserIndexedTypeFindOperationResult
        ProjectIndexedTypeFindOperation(
        DotnetInspect.Web.BrowserIndexedTypeFindOperationResult result)
    {
        var typeInfo =
            (JsonTypeInfo<EcosystemFindIndexedIntegrationResult.Type>)
                BrowserIndexedTypeFindContentJsonContext.Default.GetTypeInfo(
                    typeof(EcosystemFindIndexedIntegrationResult.Type))!;
        return new(
            new InspectionEnvelope<JsonElement>(
                JsonSerializer.SerializeToElement(
                    result.Find.Content,
                    typeInfo),
                result.Find.Share,
                result.Find.Diagnostics));
    }

    static BrowserTypeFindActivationSource Source(
        DotnetInspect.Web.BrowserTypeFindActivationSource source) =>
        source switch
        {
            DotnetInspect.Web.BrowserTypeFindActivationSource.Package =>
                BrowserTypeFindActivationSource.Package,
            DotnetInspect.Web.BrowserTypeFindActivationSource.Framework =>
                BrowserTypeFindActivationSource.Framework,
            DotnetInspect.Web.BrowserTypeFindActivationSource.Unsupported =>
                BrowserTypeFindActivationSource.Unsupported,
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported activation source."),
        };

    static BrowserTypeFindActivationStatus Status(
        DotnetInspect.Web.BrowserTypeFindActivationStatus status) =>
        status switch
        {
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Available =>
                BrowserTypeFindActivationStatus.Available,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Unavailable =>
                BrowserTypeFindActivationStatus.Unavailable,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Stale =>
                BrowserTypeFindActivationStatus.Stale,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Ambiguous =>
                BrowserTypeFindActivationStatus.Ambiguous,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Refused =>
                BrowserTypeFindActivationStatus.Refused,
            DotnetInspect.Web.BrowserTypeFindActivationStatus.Failed =>
                BrowserTypeFindActivationStatus.Failed,
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported activation status."),
        };
}
