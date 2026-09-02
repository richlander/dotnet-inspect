using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Host- or bundle-owned registry of peer definition records. Scenarios compose
/// peers by id; record count never activates a scenario implicitly.
/// </summary>
public sealed class InspectionDefinitionRegistry
{
    private readonly Dictionary<(InspectionDefinitionKind Kind, string Id), InspectionDefinitionRecord> _records =
        new();

    /// <summary>
    /// Snapshot of registered records. Enumeration is isolated from later <see cref="Add"/> calls.
    /// </summary>
    public IReadOnlyCollection<InspectionDefinitionRecord> Records =>
        new ReadOnlyCollection<InspectionDefinitionRecord>(_records.Values.ToArray());

    /// <summary>
    /// Snapshot of registered scenarios. Enumeration is isolated from later <see cref="Add"/> calls.
    /// </summary>
    public IReadOnlyList<ScenarioDefinition> Scenarios =>
        new ReadOnlyCollection<ScenarioDefinition>(
            _records.Values.OfType<ScenarioDefinition>().ToArray());

    public void Add(InspectionDefinitionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = (record.Kind, record.Id);
        if (!_records.TryAdd(key, record))
        {
            throw new InspectionDefinitionException(
                $"Duplicate {record.Kind.ToString().ToLowerInvariant()} id '{record.Id}'.");
        }
    }

    public void AddJson(string json) => Add(InspectionDefinitionJson.Parse(json));

    public void AddJson(ReadOnlyMemory<byte> utf8Json) => Add(InspectionDefinitionJson.Parse(utf8Json));

    public bool TryGet<TRecord>(string id, [NotNullWhen(true)] out TRecord? record)
        where TRecord : InspectionDefinitionRecord
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        TRecord? match = null;
        foreach (var candidate in _records.Values)
        {
            if (candidate is not TRecord typed || typed.Id != id)
                continue;
            if (match is not null)
            {
                // Cross-kind id reuse is legal; untyped/base lookup must not pick by registration order.
                record = null;
                return false;
            }

            match = typed;
        }

        record = match;
        return match is not null;
    }

    public bool TryGet(InspectionDefinitionKind kind, string id, [NotNullWhen(true)] out InspectionDefinitionRecord? record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _records.TryGetValue((kind, id), out record);
    }

    /// <summary>
    /// Resolves a scenario composition into a host-facing activation plan.
    /// Does not acquire packages or open an <see cref="AssemblyContextGroup"/>.
    /// </summary>
    public ResolvedScenario ResolveScenario(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (!TryGet<ScenarioDefinition>(scenarioId, out var scenario))
        {
            throw new InspectionDefinitionException(
                $"Unknown scenario id '{scenarioId}'.");
        }

        WorkspaceDefinition? workspace = null;
        WorkspaceContextDefinition? selectedContext = null;
        IReadOnlyList<ResolvedWorkspaceContext> contexts = Array.Empty<ResolvedWorkspaceContext>();

        if (!string.IsNullOrWhiteSpace(scenario.Workspace))
        {
            if (!TryGet<WorkspaceDefinition>(scenario.Workspace, out workspace))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown workspace '{scenario.Workspace}'.");
            }

            contexts = new ReadOnlyCollection<ResolvedWorkspaceContext>(
                workspace.Contexts
                    .Select(context => ResolveContext(workspace, context))
                    .ToArray());

            if (!string.IsNullOrWhiteSpace(scenario.Context))
            {
                selectedContext = workspace.Contexts.FirstOrDefault(c => c.Name == scenario.Context)
                    ?? throw new InspectionDefinitionException(
                        $"Scenario '{scenario.Id}' references unknown context '{scenario.Context}' in workspace '{workspace.Id}'.");
            }
            else if (workspace.Contexts.Count == 1)
            {
                selectedContext = workspace.Contexts[0];
            }
            else
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' must set context because workspace '{workspace.Id}' has multiple contexts.");
            }
        }
        else if (string.IsNullOrWhiteSpace(scenario.Input))
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' has neither workspace nor input.");
        }

        QueryDefinition? query = null;
        if (!string.IsNullOrWhiteSpace(scenario.Query))
        {
            if (!TryGet<QueryDefinition>(scenario.Query, out query))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown query '{scenario.Query}'.");
            }
        }

        ViewDefinition? view = null;
        if (!string.IsNullOrWhiteSpace(scenario.View))
        {
            if (!TryGet<ViewDefinition>(scenario.View, out view))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown view '{scenario.View}'.");
            }
        }

        ResolvedNavigation? navigation = null;
        if (!string.IsNullOrWhiteSpace(scenario.Navigation))
        {
            if (!TryGet<NavigationDefinition>(scenario.Navigation, out var navigationDefinition))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown navigation '{scenario.Navigation}'.");
            }

            navigation = ResolveNavigation(navigationDefinition);
        }

        // Cross-kind reference guard: a scenario field must not resolve to the wrong kind.
        // TryGet&lt;T&gt; already enforces type; also reject id collisions across kinds when the
        // wrong kind is the only match for a bare lookup — covered by typed TryGet.

        return new ResolvedScenario(
            scenario,
            workspace,
            selectedContext?.Name,
            contexts,
            query,
            view,
            navigation);
    }

    private ResolvedWorkspaceContext ResolveContext(
        WorkspaceDefinition workspace,
        WorkspaceContextDefinition context)
    {
        if (!string.IsNullOrWhiteSpace(context.Subscribe))
        {
            throw new InspectionDefinitionException(
                $"Workspace '{workspace.Id}' context '{context.Name}' uses subscribe '{context.Subscribe}', which is not lowered in this slice. Inline members are supported.");
        }

        var members = context.Members
            .Select(DefinitionCoordinateLowering.ToWorkspaceMember)
            .ToArray();

        return new ResolvedWorkspaceContext(
            new WorkspaceContextDescriptor(
                new WorkspaceContextAddress(
                    workspace.Id,
                    context.Name),
                context.Framework,
                context.RuntimeIdentifier),
            new ReadOnlyCollection<WorkspaceMemberCoordinate>(members));
    }

    private static ResolvedNavigation ResolveNavigation(NavigationDefinition navigation)
    {
        var tabs = new List<ResolvedNavigationTab>(navigation.Tabs.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in navigation.Tabs)
        {
            if (!seenIds.Add(tab.Id))
            {
                throw new InspectionDefinitionException(
                    $"Navigation '{navigation.Id}' has duplicate tab id '{tab.Id}'.");
            }

            if (tab.Coordinate is null)
            {
                throw new InspectionDefinitionException(
                    $"Navigation '{navigation.Id}' tab '{tab.Id}' uses subscribe, which is not lowered in this slice.");
            }

            tabs.Add(new ResolvedNavigationTab(
                tab.Id,
                DefinitionCoordinateLowering.ToWorkspaceMember(tab.Coordinate),
                tab.Framework,
                tab.RuntimeIdentifier));
        }

        var focusIndex = tabs.FindIndex(tab => tab.Id == navigation.Focus);
        if (focusIndex < 0)
        {
            throw new InspectionDefinitionException(
                $"Navigation '{navigation.Id}' focus '{navigation.Focus}' is invalid.");
        }

        return new ResolvedNavigation(
            navigation.Id,
            new ReadOnlyCollection<ResolvedNavigationTab>(tabs),
            navigation.Focus,
            focusIndex);
    }
}

/// <summary>Host-facing result of resolving one scenario composition.</summary>
public sealed class ResolvedScenario
{
    internal ResolvedScenario(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        string? selectedContextName,
        IReadOnlyList<ResolvedWorkspaceContext> contexts,
        QueryDefinition? query,
        ViewDefinition? view,
        ResolvedNavigation? navigation)
    {
        Scenario = scenario;
        Workspace = workspace;
        SelectedContextName = selectedContextName;
        Contexts = contexts;
        Query = query;
        View = view;
        Navigation = navigation;
    }

    public ScenarioDefinition Scenario { get; }

    public string ScenarioId => Scenario.Id;

    public string? Title => Scenario.Title;

    public string? Description => Scenario.Description;

    public WorkspaceDefinition? Workspace { get; }

    /// <summary>
    /// Null when the scenario is workspace-free (input-only).
    /// </summary>
    public string? SelectedContextName { get; }

    public IReadOnlyList<ResolvedWorkspaceContext> Contexts { get; }

    public bool CreatesAssemblyContextGroup => Workspace is not null;

    public QueryDefinition? Query { get; }

    public ViewDefinition? View { get; }

    public ResolvedNavigation? Navigation { get; }

    public ResolvedWorkspaceContext? SelectedContext =>
        SelectedContextName is null
            ? null
            : Contexts.FirstOrDefault(context => context.Name == SelectedContextName);
}

/// <summary>One lowered workspace context ready for <see cref="WorkspaceContextLoader"/>.</summary>
public sealed class ResolvedWorkspaceContext
{
    internal ResolvedWorkspaceContext(
        WorkspaceContextDescriptor descriptor,
        IReadOnlyList<WorkspaceMemberCoordinate> members)
    {
        Descriptor = descriptor;
        Members = members;
    }

    public WorkspaceContextDescriptor Descriptor { get; }

    public WorkspaceContextAddress Address => Descriptor.Address;

    public string Name => Descriptor.Name;

    public string? Framework => Descriptor.Framework;

    public string? RuntimeIdentifier => Descriptor.RuntimeIdentifier;

    public IReadOnlyList<WorkspaceMemberCoordinate> Members { get; }
}

/// <summary>Lowered navigation tabs and focus.</summary>
public sealed class ResolvedNavigation
{
    internal ResolvedNavigation(
        string id,
        IReadOnlyList<ResolvedNavigationTab> tabs,
        string focusTabId,
        int focusIndex)
    {
        Id = id;
        Tabs = tabs;
        FocusTabId = focusTabId;
        FocusIndex = focusIndex;
    }

    public string Id { get; }

    public IReadOnlyList<ResolvedNavigationTab> Tabs { get; }

    public string FocusTabId { get; }

    public int FocusIndex { get; }

    public ResolvedNavigationTab FocusTab => Tabs[FocusIndex];
}

/// <summary>One lowered navigation tab.</summary>
public sealed class ResolvedNavigationTab
{
    internal ResolvedNavigationTab(
        string id,
        WorkspaceMemberCoordinate coordinate,
        string? framework,
        string? runtimeIdentifier)
    {
        Id = id;
        Coordinate = coordinate;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
    }

    public string Id { get; }

    public WorkspaceMemberCoordinate Coordinate { get; }

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }
}

internal static class DefinitionCoordinateLowering
{
    public static WorkspaceMemberCoordinate ToWorkspaceMember(DefinitionMemberCoordinate coordinate) =>
        coordinate switch
        {
            DefinitionMemberCoordinate.PackageCoordinate package =>
                WorkspaceMemberCoordinate.Package(
                    package.Id,
                    package.Version,
                    package.Framework,
                    package.RuntimeIdentifier),
            DefinitionMemberCoordinate.PlatformCoordinate platform =>
                WorkspaceMemberCoordinate.Platform(
                    platform.Family,
                    platform.Assembly,
                    platform.Version,
                    platform.Framework),
            DefinitionMemberCoordinate.EmbeddedCoordinate embedded =>
                WorkspaceMemberCoordinate.Embedded(
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName),
            DefinitionMemberCoordinate.ProjectCoordinate =>
                throw new InspectionDefinitionException(
                    "project coordinates require a filesystem host and are not lowered to WorkspaceMemberCoordinate in this slice."),
            DefinitionMemberCoordinate.LocalCoordinate =>
                throw new InspectionDefinitionException(
                    "local coordinates require a filesystem host and are not lowered to WorkspaceMemberCoordinate in this slice."),
            DefinitionMemberCoordinate.DirectoryCoordinate =>
                throw new InspectionDefinitionException(
                    "directory coordinates require a filesystem host and are not lowered to WorkspaceMemberCoordinate in this slice."),
            _ => throw new InspectionDefinitionException(
                $"Unsupported coordinate kind '{coordinate.Kind}'."),
        };
}
