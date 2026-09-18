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
    public static async Task<string> PrepareRetainedWorkspaceDefinition(
        string activationIntentId,
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        int? presentationActiveTabIndex)
    {
        BrowserRetainedWorkspacePreparationResult result =
            await BrowserRetainedWorkspaceActivationService.PrepareAsync(
                    activationIntentId,
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket,
                    presentationActiveTabIndex)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspacePreparationResult);
    }

    [JSExport]
    public static async Task<string> CommitRetainedWorkspaceActivation(
        string activationIntentId)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.CommitAsync(
                    activationIntentId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static async Task<string> CancelRetainedWorkspaceActivation(
        string activationIntentId)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.CancelAsync(
                    activationIntentId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static async Task<string>
        ActivateRetainedWorkspacePackageOccurrence(
            string retainedDefinitionId,
            string realizationId,
            string navigationId)
    {
        BrowserRetainedWorkspacePackageActivationResult result =
            await BrowserRetainedWorkspaceActivationService
                .ActivatePackageAsync(
                    retainedDefinitionId,
                    realizationId,
                    navigationId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspacePackageActivationResult);
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
    static readonly object Gate = new();
    static readonly Dictionary<
        string,
        BrowserRetainedWorkspaceActivationSession> Sessions =
        new(StringComparer.Ordinal);
    static BrowserRetainedWorkspaceActivationOwner _owner =
        CreateOwner();

    internal static BrowserRetainedWorkspaceActivationOwner Owner => _owner;

    internal static async Task<BrowserRetainedWorkspacePreparationResult>
        PrepareAsync(
            string activationIntentId,
            string retainedDefinitionId,
            string label,
            string canonicalLocation,
            string canonicalPacket,
            int? presentationActiveTabIndex)
    {
        BrowserRetainedWorkspaceActivationRequest request;
        try
        {
            request = Request(
                activationIntentId,
                retainedDefinitionId,
                label,
                canonicalLocation,
                canonicalPacket,
                presentationActiveTabIndex);
        }
        catch (ArgumentException ex)
        {
            return new(
                "failed",
                null,
                null,
                new("InvalidRequest", ex.Message));
        }

        BrowserRetainedWorkspaceActivationSession session;
        lock (Gate)
        {
            if (Sessions.ContainsKey(activationIntentId))
            {
                return new(
                    "failed",
                    null,
                    null,
                    new(
                        "InvalidRequest",
                        "The retained Workspace activation intent already exists."));
            }
            session = _owner.BeginActivation(request);
            Sessions.Add(activationIntentId, session);
        }

        try
        {
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                preparation = await session.Preparation.ConfigureAwait(false);
            if (preparation
                is not DotnetInspect.Web
                    .BrowserRetainedWorkspacePreparationResult.Prepared)
            {
                lock (Gate)
                    Sessions.Remove(activationIntentId);
            }
            return Preparation(preparation);
        }
        catch
        {
            lock (Gate)
                Sessions.Remove(activationIntentId);
            throw;
        }
    }

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        CommitAsync(string activationIntentId)
    {
        BrowserRetainedWorkspaceActivationSession? session =
            FindSession(activationIntentId);
        if (session is null)
        {
            return new(
                "failed",
                null,
                new(
                    "InvalidRequest",
                    "The retained Workspace activation intent is unavailable."));
        }

        _ = session.Commit();
        try
        {
            return Activation(
                await session.Completion.ConfigureAwait(false));
        }
        finally
        {
            lock (Gate)
                Sessions.Remove(activationIntentId);
        }
    }

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        CancelAsync(string activationIntentId)
    {
        BrowserRetainedWorkspaceActivationSession? session =
            FindSession(activationIntentId);
        if (session is null)
        {
            return new("superseded", null, null);
        }

        session.Supersede();
        try
        {
            return Activation(
                await session.Completion.ConfigureAwait(false));
        }
        finally
        {
            lock (Gate)
                Sessions.Remove(activationIntentId);
        }
    }

    internal static async Task<
        BrowserRetainedWorkspacePackageActivationResult>
        ActivatePackageAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId)
    {
        DotnetInspect.Web.BrowserRetainedWorkspacePackageActivationResult
            result = await _owner.ActivatePackageAsync(
                    retainedDefinitionId,
                    realizationId,
                    navigationId)
                .ConfigureAwait(false);
        return result switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspacePackageActivationResult
                    .Activated activated =>
                new(
                    Activated: true,
                    Superseded: false,
                    BrowserCatalogWireProjection.Project(
                        activated.Package.Surface)),
            DotnetInspect.Web.BrowserRetainedWorkspacePackageActivationResult
                    .Superseded =>
                new(Activated: false, Superseded: true, Package: null),
            _ => throw new InvalidOperationException(
                "Retained Workspace package activation returned an unsupported result."),
        };
    }

    static BrowserRetainedWorkspaceActivationRequest Request(
        string activationIntentId,
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        int? presentationActiveTabIndex)
        => new(
            activationIntentId,
            retainedDefinitionId,
            label,
            canonicalLocation,
            canonicalPacket,
            presentationActiveTabIndex);

    static BrowserRetainedWorkspaceActivationSession? FindSession(
        string activationIntentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationIntentId);
        lock (Gate)
            return Sessions.GetValueOrDefault(activationIntentId);
    }

    static BrowserRetainedWorkspaceActivationResult Activation(
        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult activation) =>
        activation switch
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

    static BrowserRetainedWorkspacePreparationResult Preparation(
        DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
            preparation) =>
        preparation switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Prepared prepared =>
                new(
                    "prepared",
                    PreparedInstallation(prepared.Installation),
                    null,
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .NoEffect noEffect =>
                new(
                    "noEffect",
                    null,
                    Installation(noEffect.Installation),
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Superseded =>
                new("superseded", null, null, null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Failed failed =>
                new(
                    "failed",
                    null,
                    null,
                    new(
                        failed.Failure.GetType().Name,
                        failed.Failure.Message)),
            _ => throw new InvalidOperationException(
                "Retained Workspace preparation returned an unsupported result."),
        };

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
        BrowserRetainedWorkspaceActivationSession[] sessions;
        lock (Gate)
        {
            sessions = [.. Sessions.Values];
            Sessions.Clear();
        }
        foreach (BrowserRetainedWorkspaceActivationSession session in sessions)
            session.Cancel();
        await Task.WhenAll(
                sessions.Select(static session => session.Completion))
            .ConfigureAwait(false);
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
            installation.CanonicalPacket,
            installation.RealizationId,
            installation.PublicationOrdinal,
            Definition(installation.Definition),
            Packages(installation.Packages),
            Navigation(installation.Navigation),
            installation.Predecessor is null
                ? null
                : new(
                    installation.Predecessor.SettlementId,
                    installation.Predecessor.Retirement.Reason.ToString()));

    static BrowserRetainedWorkspacePreparedInstallation PreparedInstallation(
        DotnetInspect.Web.BrowserRetainedWorkspacePreparedInstallation
            installation) =>
        new(
            Definition(installation.Definition),
            Packages(installation.Packages),
            Navigation(installation.Navigation));

    static BrowserRetainedWorkspaceDefinition Definition(
        BrowserRetainedWorkspaceDefinitionState definition) =>
        new(
            [
                .. definition.Tabs.Select(
                    static tab =>
                        new BrowserWorkspaceShareTab(
                            tab.Id,
                            tab.Kind,
                            tab.Source,
                            tab.Version,
                            tab.Framework,
                            tab.RuntimeIdentifier)),
            ],
            [
                .. definition.Contexts.Select(
                    static context =>
                        new BrowserWorkspaceShareContext(
                            context.Id,
                            [.. context.TabIds])),
            ],
            definition.ActiveTabId,
            definition.SelectedContextId);

    static BrowserRetainedWorkspacePackage[] Packages(
        IReadOnlyList<DotnetInspect.Web.BrowserRetainedWorkspacePackage>
            packages) =>
        [
            .. packages.Select(
                static package =>
                    new BrowserRetainedWorkspacePackage(
                        package.NavigationId,
                        package.Kind,
                        BrowserCatalogWireProjection.Project(
                            package.Surface))),
        ];

    static BrowserRetainedWorkspaceNavigation Navigation(
        BrowserRetainedWorkspaceNavigationState navigation) =>
        new(
            navigation.ActiveStateIndex,
            [
                .. navigation.States.Select(
                    static state =>
                        new BrowserRetainedWorkspaceView(
                            state.NavigationId,
                            state.SubjectKind,
                            state.Facet)),
            ]);

    static BrowserRetainedWorkspaceSettlement Settlement(
        WorkspaceRealizationSettlement settlement) =>
        new(
            settlement.Succeeded,
            settlement.Reason.ToString(),
            settlement.Failure?.Message);
}
