using NuGet.Versioning;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Derives one active Package scenario from an existing complete Workspace.
/// </summary>
public static class WorkspacePackageScenarioProjection
{
    public static WorkspaceSharePacketProjectionResult Project(
        CompleteWorkspaceActivation activation,
        WorkspaceDeclarationContext context,
        WorkspaceMemberCoordinate.PackageMember member,
        PackageRootBinding root,
        ViewFacetId? facet = null)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(root);

        if (!ReferenceEquals(activation.SelectedContext, context))
        {
            return Failure(
                "scenario.context",
                "The selected Package Root does not belong to the restored "
                    + "scenario's selected context.");
        }

        CommittedScenarioDefinitionSet source =
            activation.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version3 version3 =>
                    version3.Definitions,
                CompleteRestorationResolvedState.Version4 version4 =>
                    version4.Definitions,
                _ => throw new InvalidOperationException(
                    "Derived Package scenarios require schema version 3 or 4."),
            };
        WorkspaceDefinition workspace =
            source.Workspace
            ?? throw new InvalidOperationException(
                "A Workspace-backed scenario requires a Workspace definition.");
        CommittedNavigationDefinition navigation =
            source.Navigation
            ?? throw new InvalidOperationException(
                "A committed Workspace scenario requires Navigation.");
        CommittedViewDefinition view =
            source.View
            ?? throw new InvalidOperationException(
                "A committed Workspace scenario requires View state.");

        if (!string.Equals(
                member.PackageId,
                root.Root.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || (member.Version is not null
                && !VersionsEqual(
                    member.Version,
                    root.Root.PackageVersion)))
        {
            return Failure(
                "workspace.package",
                "The selected Package Root does not correspond to the "
                    + "preserved Package member.");
        }

        NavigationTabDefinition[] matchingTabs =
        [
            .. navigation.Tabs.Where(tab =>
                tab.Coordinate
                    is DefinitionMemberCoordinate.PackageCoordinate package
                && CoordinatesEqual(package, member)),
        ];
        if (matchingTabs.Length != 1)
        {
            return Failure(
                "navigation.tabs",
                matchingTabs.Length == 0
                    ? "The selected Package occurrence has no portable "
                        + "Navigation tab."
                    : "The selected Package occurrence has more than one "
                        + "portable Navigation tab.");
        }

        string navigationId = matchingTabs[0].Id;
        int stateIndex = view.States
            .Select((state, index) => (state, index))
            .Where(item => string.Equals(
                item.state.Navigation,
                navigationId,
                StringComparison.Ordinal))
            .Select(static item => item.index)
            .SingleOrDefault(-1);
        if (stateIndex < 0)
        {
            return Failure(
                "view.states",
                "The selected Package occurrence has no retained View state.");
        }

        CommittedViewStateDefinition selected = view.States[stateIndex];
        var active = new CommittedViewStateDefinition(
            selected.Navigation,
            new PortableSubjectRequest.Package(),
            selected.Context
                ?? new PortableRetainedSubjectContext.Package(),
            (facet ?? new ViewFacetId("package.overview")).Value,
            selected.Queries,
            selected.Libraries);
        CommittedViewStateDefinition[] states = [.. view.States];
        states[stateIndex] = active;

        var projectedView = new CommittedViewDefinition(
            view.SchemaVersion,
            view.Id,
            states);
        var projectedNavigation = new CommittedNavigationDefinition(
            navigation.SchemaVersion,
            navigation.Id,
            navigation.Tabs,
            navigationId);
        var projectedDefinitions = new CommittedScenarioDefinitionSet(
            source.Scenario,
            workspace,
            projectedNavigation,
            projectedView,
            source.Catalogs,
            source.NavigationTargetMatchMode,
            source.QueryBindings);
        return WorkspaceSharePacketTransposer.ToPacket(projectedDefinitions);
    }

    static bool CoordinatesEqual(
        DefinitionMemberCoordinate.PackageCoordinate coordinate,
        WorkspaceMemberCoordinate.PackageMember member) =>
        string.Equals(
            coordinate.Id,
            member.PackageId,
            StringComparison.OrdinalIgnoreCase)
        && NullableVersionsEqual(coordinate.Version, member.Version)
        && string.Equals(
            coordinate.Framework,
            member.Framework,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            coordinate.RuntimeIdentifier,
            member.RuntimeIdentifier,
            StringComparison.OrdinalIgnoreCase);

    static bool NullableVersionsEqual(string? left, string? right) =>
        left is null
            ? right is null
            : right is not null && VersionsEqual(left, right);

    static bool VersionsEqual(string left, string right) =>
        NuGetVersion.TryParse(left, out NuGetVersion? leftVersion)
        && NuGetVersion.TryParse(right, out NuGetVersion? rightVersion)
        && leftVersion == rightVersion;

    static WorkspaceSharePacketProjectionResult Failure(
        string path,
        string message) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            path,
            message);
}
