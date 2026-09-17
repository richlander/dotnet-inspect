using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    static readonly BrowserRetainedWorkspaceActivationCoordinator
        RetainedWorkspaceActivation =
            BrowserRetainedWorkspaceActivationCoordinator.CreateProduction();

    [JSExport]
    public static async Task<string> ActivateRetainedWorkspace(
        string retainedDefinitionId,
        string packet)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await RetainedWorkspaceActivation.ActivatePacketAsync(
                retainedDefinitionId,
                packet).ConfigureAwait(false);
        return JsonSerializer.Serialize(
            Project(result),
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResultDto);
    }

    [JSExport]
    public static async Task<string> AwaitRetainedWorkspaceSettlement(
        string settlementId)
    {
        BrowserRetainedWorkspaceSettlementResult result =
            await RetainedWorkspaceActivation.AwaitSettlementAsync(
                settlementId).ConfigureAwait(false);
        return JsonSerializer.Serialize(
            Project(result),
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceSettlementResultDto);
    }

    static BrowserRetainedWorkspaceActivationResultDto Project(
        BrowserRetainedWorkspaceActivationResult result) =>
        result switch
        {
            BrowserRetainedWorkspaceActivationResult.Activated activated =>
                Success(
                    "activated",
                    activated.Selection,
                    activated.PredecessorSettlement
                        ?.SettlementId),
            BrowserRetainedWorkspaceActivationResult.NoEffect noEffect =>
                Success("noEffect", noEffect.Selection, null),
            BrowserRetainedWorkspaceActivationResult.Failed failed =>
                new(
                    Kind: "failed",
                    failed.RetainedDefinitionId,
                    ActivationId: null,
                    CanonicalPacket: null,
                    NavigationJson: null,
                    PredecessorSettlementId: null,
                    FailedSettlementCount:
                        RetainedWorkspaceActivation.FailedSettlements.Length,
                    Failure: new(
                        failed.Failure.GetType().Name,
                        failed.Failure.Message)),
            BrowserRetainedWorkspaceActivationResult.Superseded superseded =>
                new(
                    Kind: "superseded",
                    superseded.RetainedDefinitionId,
                    ActivationId: null,
                    CanonicalPacket: null,
                    NavigationJson: null,
                    PredecessorSettlementId: null,
                    FailedSettlementCount:
                        RetainedWorkspaceActivation.FailedSettlements.Length,
                    Failure: null),
            _ => throw new InvalidOperationException(
                "Unknown Browser retained Workspace activation result."),
        };

    static BrowserRetainedWorkspaceActivationResultDto Success(
        string kind,
        BrowserRetainedWorkspaceSelection selection,
        string? predecessorSettlementId)
    {
        string canonicalPacket = selection.Workspace.Projection switch
        {
            CompleteRestorationProjection.Projectable projectable =>
                projectable.CanonicalPacket,
            _ => throw new InvalidOperationException(
                "Packet activation must retain its canonical packet."),
        };
        return new(
            kind,
            selection.RetainedDefinitionId,
            selection.ActivationId,
            canonicalPacket,
            JsonSerializer.Serialize(
                selection.Navigation,
                NavigationConsumerJsonContext.Default.NavigationConsumerResult),
            predecessorSettlementId,
            RetainedWorkspaceActivation.FailedSettlements.Length,
            Failure: null);
    }

    static BrowserRetainedWorkspaceSettlementResultDto Project(
        BrowserRetainedWorkspaceSettlementResult result) =>
        result switch
        {
            BrowserRetainedWorkspaceSettlementResult.Settled settled =>
                new(
                    Kind: "settled",
                    settled.SettlementId,
                    Succeeded: settled.Settlement.Succeeded,
                    Reason: settled.Settlement.Reason.ToString(),
                    Failure: settled.Settlement.Succeeded
                        ? null
                        : SettlementFailure(settled.Settlement)),
            BrowserRetainedWorkspaceSettlementResult.Unavailable unavailable =>
                new(
                    Kind: "unavailable",
                    unavailable.SettlementId,
                    Succeeded: null,
                    Reason: null,
                    Failure: null),
            _ => throw new InvalidOperationException(
                "Unknown Browser retained Workspace settlement result."),
        };

    static BrowserRetainedWorkspaceActivationFailure SettlementFailure(
        WorkspaceRealizationSettlement settlement)
    {
        Exception failure = settlement.Failure
            ?? new WorkspaceRealizationSettlementException(settlement);
        return new(
            failure.GetType().Name,
            failure.Message);
    }
}
