using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
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
