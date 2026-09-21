using ILInspector.Metadata;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Projects one owner-issued exact Type selection into a restored schema-4
/// Workspace scenario.
/// </summary>
public static class WorkspaceTypeScenarioProjection
{
    public static WorkspaceSharePacketProjectionResult Project(
        CompleteWorkspaceActivation activation,
        WorkspaceDeclarationContext selectedContext,
        WorkspaceDeclarationMember definingSource,
        MetadataTypeDefinitionName type,
        ViewFacetId? facet = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(selectedContext);
        ArgumentNullException.ThrowIfNull(definingSource);
        ArgumentNullException.ThrowIfNull(type);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(activation.SelectedContext, selectedContext))
        {
            return Failed(
                "scenario.context",
                "The exact Type source does not belong to the selected "
                    + "Workspace context.");
        }
        if (!selectedContext.Receipt.Members.Any(member =>
                ReferenceEquals(member, definingSource)))
        {
            return Failed(
                "scenario.context.members",
                "The exact Type source is not a member of the selected "
                    + "Workspace context.");
        }

        CommittedScenarioDefinitionSet source =
            activation.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version2 version2 =>
                    version2.Definitions,
                CompleteRestorationResolvedState.Version3 version3 =>
                    version3.Definitions,
                CompleteRestorationResolvedState.Version4 version4 =>
                    version4.Definitions,
                CompleteRestorationResolvedState.Version5 version5 =>
                    version5.Definitions,
                _ => throw new InvalidOperationException(
                    "Unknown complete-restoration resolved state."),
            };
        if (source.Scenario.SchemaVersion is not (
                InspectionDefinitionSchema.Version4
                or InspectionDefinitionSchema.Version5))
        {
            return Failed(
                "scenario.schemaVersion",
                "A derived Type scenario requires Workspace packet schema "
                    + "version 4 or 5.");
        }

        WorkspaceDefinition workspace =
            source.Workspace
            ?? throw new InvalidOperationException(
                "A complete Workspace scenario requires a Workspace definition.");
        CommittedNavigationDefinition navigation =
            source.Navigation
            ?? throw new InvalidOperationException(
                "A complete Workspace scenario requires Navigation.");
        CommittedViewDefinition view =
            source.View
            ?? throw new InvalidOperationException(
                "A complete Workspace scenario requires a committed view.");
        string contextName =
            source.Scenario.Context
            ?? throw new InvalidOperationException(
                "A selected Workspace context requires a scenario context.");
        WorkspaceContextDefinition contextDefinition =
            workspace.Contexts.Single(context => string.Equals(
                context.Name,
                contextName,
                StringComparison.Ordinal));

        if (definingSource.Origin
            is not WorkspaceDeclarationOrigin.ContextLoad origin)
        {
            return Failed(
                "selection.source",
                "The exact Type source has no portable Workspace declaration.");
        }

        NavigationTabDefinition[] matchingTabs =
        [
            .. navigation.Tabs.Where(tab =>
                tab.Coordinate is not null
                && DefinitionCoordinateLowering.ToWorkspaceMember(
                    tab.Coordinate) == origin.Declared),
        ];
        if (matchingTabs.Length == 0
            && contextDefinition.Subscribe is { } subscription)
        {
            matchingTabs =
            [
                .. navigation.Tabs.Where(tab => string.Equals(
                    tab.Subscribe,
                    subscription,
                    StringComparison.Ordinal)),
            ];
        }
        if (matchingTabs.Length != 1)
        {
            return Failed(
                "navigation.focus",
                matchingTabs.Length == 0
                    ? "The exact Type source has no existing portable "
                        + "Navigation tab."
                    : "The exact Type source matches more than one portable "
                        + "Navigation tab.");
        }

        NavigationTabDefinition tab = matchingTabs[0];
        CommittedViewStateDefinition[] states = [.. view.States];
        int stateIndex = Array.FindIndex(
            states,
            state => string.Equals(
                state.Navigation,
                tab.Id,
                StringComparison.Ordinal));
        if (stateIndex < 0)
        {
            return Failed(
                "view.states",
                "The exact Type source tab has no committed view state.");
        }

        PortableLibraryIdentity library;
        try
        {
            library = PortableIdentityProjection.FromAssembly(
                definingSource.AssemblyIdentity);
        }
        catch (InvalidOperationException ex)
        {
            return Failed("selection.library", ex.Message);
        }

        CommittedViewStateDefinition existing = states[stateIndex];
        states[stateIndex] = new CommittedViewStateDefinition(
            tab.Id,
            new PortableSubjectRequest.Type(),
            new PortableRetainedSubjectContext.Type(library, type),
            facet?.Value,
            existing.Queries,
            existing.Libraries);
        var derivedNavigation = new CommittedNavigationDefinition(
            navigation.SchemaVersion,
            navigation.Id,
            navigation.Tabs,
            tab.Id);
        var derivedView = new CommittedViewDefinition(
            view.SchemaVersion,
            view.Id,
            states);
        var derived = new CommittedScenarioDefinitionSet(
            source.Scenario,
            workspace,
            derivedNavigation,
            derivedView,
            source.Catalogs,
            source.NavigationTargetMatchMode);
        return WorkspaceSharePacketTransposer.ToPacket(
            derived,
            cancellationToken);
    }

    static WorkspaceSharePacketProjectionResult Failed(
        string path,
        string message) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            path,
            message);
}

internal static class PortableIdentityProjection
{
    internal static PortableLibraryIdentity FromAssembly(
        AssemblyReferenceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Version version = identity.Version
            ?? throw new InvalidOperationException(
                "The selected Library has no exact assembly version.");
        return new PortableLibraryIdentity(
            identity.Name,
            version.ToString(4),
            string.IsNullOrEmpty(identity.Culture)
                || identity.Culture.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : identity.Culture,
            string.IsNullOrEmpty(identity.PublicKeyToken)
                ? null
                : identity.PublicKeyToken);
    }
}
