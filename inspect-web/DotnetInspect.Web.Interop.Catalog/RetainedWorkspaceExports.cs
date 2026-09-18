using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    [JSExport]
    public static async Task<string> ActivateRetainedWorkspaceDefinition(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.ActivateAsync(
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static async Task<string> DeactivateRetainedWorkspaceDefinition(
        string retainedDefinitionId)
    {
        BrowserRetainedWorkspaceDeactivationResult result =
            await BrowserRetainedWorkspaceActivationService.DeactivateAsync(
                    retainedDefinitionId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceDeactivationResult);
    }

    [JSExport]
    public static async Task<string> ObserveRetainedWorkspaceSettlement(
        string settlementId)
    {
        BrowserRetainedWorkspaceSettlementResult result =
            await BrowserRetainedWorkspaceActivationService
                .ObserveSettlementAsync(settlementId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceSettlementResult);
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserRetainedWorkspaceActivationService
{
    static BrowserRetainedWorkspaceActivationOwner _owner =
        CreateOwner();

    internal static BrowserRetainedWorkspaceActivationOwner Owner => _owner;

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        ActivateAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
    {
        BrowserRetainedWorkspaceActivationRequest request;
        try
        {
            request = new(
                retainedDefinitionId,
                label,
                canonicalLocation,
                canonicalPacket);
        }
        catch (ArgumentException ex)
        {
            return new(
                "failed",
                null,
                new("InvalidRequest", ex.Message));
        }

        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
            activation = await _owner.ActivateAsync(request)
                .ConfigureAwait(false);
        return activation switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Activated activated =>
                new("activated", Installation(activated.Installation), null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .NoEffect noEffect =>
                new("noEffect", Installation(noEffect.Installation), null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Superseded =>
                new("superseded", null, null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Failed failed =>
                new(
                    "failed",
                    null,
                    new(
                        failed.Failure.GetType().Name,
                        failed.Failure.Message)),
            _ => throw new InvalidOperationException(
                "Retained Workspace activation returned an unsupported result."),
        };
    }

    internal static async Task<BrowserRetainedWorkspaceDeactivationResult>
        DeactivateAsync(
        string retainedDefinitionId)
    {
        DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult outcome =
            await _owner.DeactivateAsync(retainedDefinitionId)
                .ConfigureAwait(false);
        BrowserRetainedWorkspaceDeactivationResult result = outcome switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .Deactivated deactivated =>
                new(
                    "deactivated",
                    Settlement(deactivated.Settlement),
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .CleanupFailed failed =>
                new(
                    "cleanupFailed",
                    Settlement(failed.Settlement),
                    "The active Workspace could not be settled."),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .NoEffect =>
                new("noEffect", null, null),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .Rejected rejected =>
                new("rejected", null, rejected.Message),
            _ => throw new InvalidOperationException(
                "Retained Workspace deactivation returned an unsupported result."),
        };
        return result;
    }

    internal static async Task<BrowserRetainedWorkspaceSettlementResult>
        ObserveSettlementAsync(
        string settlementId)
    {
        DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult outcome =
            await _owner.ObserveSettlementAsync(settlementId)
                .ConfigureAwait(false);
        BrowserRetainedWorkspaceSettlementResult result = outcome switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult
                    .Settled settled =>
                new("settled", Settlement(settled.Settlement)),
            DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult.Unknown =>
                new("unknown", null),
            _ => throw new InvalidOperationException(
                "Retained Workspace settlement returned an unsupported result."),
        };
        return result;
    }

    internal static async Task ResetForTestsAsync()
    {
        BrowserRetainedWorkspaceActivationOwner prior = _owner;
        _owner = CreateOwner();
        await prior.DisposeAsync().ConfigureAwait(false);
    }

    static BrowserRetainedWorkspaceActivationOwner CreateOwner() =>
        new(BrowserCompleteRestorationOptions.Create);

    static BrowserRetainedWorkspaceInstallation Installation(
        DotnetInspect.Web.BrowserRetainedWorkspaceInstallation installation) =>
        new(
            installation.RetainedDefinitionId,
            installation.Label,
            installation.CanonicalLocation,
            installation.CanonicalPacket
                ?? throw new InvalidOperationException(
                    "The packet activation export cannot project a non-packet retained definition."),
            installation.RealizationId,
            installation.PublicationOrdinal,
            new(
                installation.Navigation.ActiveStateIndex,
                [
                    .. installation.Navigation.States.Select(
                        static state =>
                            new BrowserRetainedWorkspaceView(
                                state.NavigationId,
                                state.SubjectKind,
                                state.Facet)),
                ]),
            installation.Predecessor is null
                ? null
                : new(
                    installation.Predecessor.SettlementId,
                    installation.Predecessor.Retirement.Reason.ToString()));

    static BrowserRetainedWorkspaceSettlement Settlement(
        WorkspaceRealizationSettlement settlement) =>
        new(
            settlement.Succeeded,
            settlement.Reason.ToString(),
            settlement.Failure?.Message);
}
