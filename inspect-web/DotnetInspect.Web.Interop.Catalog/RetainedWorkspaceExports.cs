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

    [JSExport]
    public static string RecordRetainedWorkspaceNavigationInstallation(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService
            .RecordConsumerInstallation(
                realizationId,
                RequirePublicationOrdinal(publicationOrdinal),
                new(session, revision, intent, epoch));

    [JSExport]
    public static bool ValidateRetainedWorkspaceNavigationAuthority(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService
            .ValidateNavigationAuthority(
                realizationId,
                RequirePublicationOrdinal(publicationOrdinal),
                new(session, revision, intent, epoch));

    [JSExport]
    public static string AcknowledgeRetainedWorkspaceNavigation(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService.Acknowledge(
            realizationId,
            RequirePublicationOrdinal(publicationOrdinal),
            new(session, revision, intent, epoch));

    [JSExport]
    public static string AbandonRetainedWorkspaceNavigation(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService.Abandon(
            realizationId,
            RequirePublicationOrdinal(publicationOrdinal),
            new(session, revision, intent, epoch));

    static long RequirePublicationOrdinal(double value)
    {
        const double maximumSafeInteger = 9_007_199_254_740_991d;
        if (!double.IsFinite(value)
            || value < 0
            || value > maximumSafeInteger
            || Math.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The publication ordinal must be a non-negative safe integer.");
        }

        return checked((long)value);
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
                    failed.NavigationFailure
                        ?? "The active Workspace could not be settled."),
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

    internal static string RecordConsumerInstallation(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            _owner.RecordConsumerInstallation(
                realizationId,
                publicationOrdinal,
                authority));

    internal static bool ValidateNavigationAuthority(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        _owner.ValidateNavigationAuthority(
            realizationId,
            publicationOrdinal,
            authority);

    internal static string Acknowledge(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            _owner.Acknowledge(
                realizationId,
                publicationOrdinal,
                authority));

    internal static string Abandon(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            _owner.Abandon(
                realizationId,
                publicationOrdinal,
                authority));

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
            BrowserCatalogWireProjection.Project(installation.Navigation),
            [
                .. installation.Packages.Select(
                    static package =>
                        new BrowserRetainedWorkspacePackage(
                            package.NavigationId,
                            package.ConsumerPackageSubjectId,
                            BrowserCatalogWireProjection.Project(
                                package.Surface))),
            ],
            installation.Predecessor is null
                ? null
                : new(
                    installation.Predecessor.SettlementId,
                    installation.Predecessor.Retirement.Reason.ToString()),
            installation.Cleanup is null
                ? null
                : new(installation.Cleanup.Message));

    static BrowserRetainedWorkspaceSettlement Settlement(
        WorkspaceRealizationSettlement settlement) =>
        new(
            settlement.Succeeded,
            settlement.Reason.ToString(),
            settlement.Failure?.Message);

    static string AuthorityResult(NavigationAuthorityResult result) =>
        result switch
        {
            NavigationAuthorityResult.Accepted => "accepted",
            NavigationAuthorityResult.InvalidAuthority => "invalidAuthority",
            NavigationAuthorityResult.InstallationRequired =>
                "installationRequired",
            _ => throw new InvalidOperationException(
                "Navigation authority settlement returned an unknown result."),
        };
}
