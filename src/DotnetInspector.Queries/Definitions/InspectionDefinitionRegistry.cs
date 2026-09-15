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

        ScenarioRecordComposition records = ResolveScenarioRecords(scenario);
        ValidateSameSchemaVersion(scenario, records);
        if (scenario.SchemaVersion != InspectionDefinitionSchema.Version1)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenarioId}' uses schema version 2 and requires portable selector resolution.");
        }

        return ResolveVersion1Scenario(scenario);
    }

    /// <summary>
    /// Performs strict schema dispatch and resource-free composition. Version 2
    /// remains unresolved until the portable selector-resolution participant.
    /// </summary>
    public InspectionDefinitionScenarioPreparationResult PrepareScenario(
        string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (!TryGet<ScenarioDefinition>(scenarioId, out var scenario))
        {
            throw new InspectionDefinitionException(
                $"Unknown scenario id '{scenarioId}'.");
        }

        var records = ResolveScenarioRecords(scenario);
        ValidateSameSchemaVersion(scenario, records);
        ValidateSelectedContext(
            scenario,
            records.Workspace as WorkspaceDefinition);
        ValidateNavigationIds(records.Navigation);
        if (scenario.SchemaVersion == InspectionDefinitionSchema.Version2)
        {
            return new InspectionDefinitionScenarioPreparationResult.Version2(
                CreateCommittedScenario(records));
        }

        return new InspectionDefinitionScenarioPreparationResult.Version1(
            ResolveVersion1Scenario(scenario));
    }

    private ResolvedScenario ResolveVersion1Scenario(
        ScenarioDefinition scenario)
    {
        WorkspaceDefinition? workspace = null;
        WorkspacePlan? workspacePlan = null;
        WorkspaceContextDefinition? selectedContext = null;
        IReadOnlyList<ResolvedWorkspaceContext> contexts = Array.Empty<ResolvedWorkspaceContext>();

        if (!string.IsNullOrWhiteSpace(scenario.Workspace))
        {
            if (!TryGet<WorkspaceDefinition>(scenario.Workspace, out workspace))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown workspace '{scenario.Workspace}'.");
            }

            workspacePlan = CreateWorkspacePlan(workspace);
            contexts = new ReadOnlyCollection<ResolvedWorkspaceContext>(
                workspace.Contexts.Select((context, index) =>
                {
                    WorkspaceContextInput input = workspacePlan.Contexts[index];
                    return new ResolvedWorkspaceContext(
                        new WorkspaceContextDescriptor(
                            new WorkspaceContextAddress(workspace.Id, context.Name),
                            input.Framework,
                            input.RuntimeIdentifier),
                        input);
                }).ToArray());

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
            workspacePlan,
            selectedContext?.Name,
            contexts,
            query,
            view,
            navigation);
    }

    private ScenarioRecordComposition ResolveScenarioRecords(
        ScenarioDefinition scenario)
    {
        InspectionDefinitionRecord? workspace = ResolveOptional(
            InspectionDefinitionKind.Workspace,
            scenario.Workspace,
            scenario,
            "workspace");
        InspectionDefinitionRecord? navigation = ResolveOptional(
            InspectionDefinitionKind.Navigation,
            scenario.Navigation,
            scenario,
            "navigation");
        return new(
            scenario,
            workspace,
            ResolveOptional(
                InspectionDefinitionKind.Query,
                scenario.Query,
                scenario,
                "query"),
            ResolveOptional(
                InspectionDefinitionKind.View,
                scenario.View,
                scenario,
                "view"),
            navigation,
            ResolveCatalogDependencies(
                workspace as WorkspaceDefinition,
                navigation));
    }

    private InspectionDefinitionRecord? ResolveOptional(
        InspectionDefinitionKind kind,
        string? id,
        ScenarioDefinition scenario,
        string field)
    {
        if (id is null)
            return null;
        if (!TryGet(kind, id, out InspectionDefinitionRecord? record))
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' references unknown {field} '{id}'.");
        }

        return record;
    }

    private static void ValidateSameSchemaVersion(
        ScenarioDefinition scenario,
        ScenarioRecordComposition records)
    {
        foreach (InspectionDefinitionRecord record in records.Referenced)
        {
            if (record.SchemaVersion != scenario.SchemaVersion)
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' mixes schema version {scenario.SchemaVersion} "
                        + $"with {record.Kind.ToString().ToLowerInvariant()} "
                        + $"'{record.Id}' schema version {record.SchemaVersion}.");
            }
        }
    }

    private static void ValidateSelectedContext(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace)
    {
        if (workspace is null)
            return;
        if (scenario.Context is not null)
        {
            if (workspace.Contexts.All(
                    context => context.Name != scenario.Context))
            {
                throw new InspectionDefinitionException(
                    $"Scenario '{scenario.Id}' references unknown context "
                        + $"'{scenario.Context}' in workspace '{workspace.Id}'.");
            }
        }
        else if (workspace.Contexts.Count != 1)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' must set context because workspace "
                    + $"'{workspace.Id}' has multiple contexts.");
        }
    }

    private static void ValidateNavigationIds(
        InspectionDefinitionRecord? navigation)
    {
        IReadOnlyList<NavigationTabDefinition>? tabs = navigation switch
        {
            NavigationDefinition version1 => version1.Tabs,
            CommittedNavigationDefinition version2 => version2.Tabs,
            _ => null,
        };
        if (tabs is null)
            return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (NavigationTabDefinition tab in tabs)
        {
            if (!ids.Add(tab.Id))
            {
                throw new InspectionDefinitionException(
                    $"Navigation '{navigation!.Id}' has duplicate tab id '{tab.Id}'.");
            }
        }
    }

    private IReadOnlyList<CatalogDefinition> ResolveCatalogDependencies(
        WorkspaceDefinition? workspace,
        InspectionDefinitionRecord? navigation)
    {
        IEnumerable<string> subscriptions =
            (workspace?.Contexts
                .Select(context => context.Subscribe)
                .Where(value => value is not null)
                .Cast<string>()
                ?? [])
            .Concat(navigation switch
            {
                NavigationDefinition version1 =>
                    version1.Tabs
                        .Select(tab => tab.Subscribe)
                        .Where(value => value is not null)
                        .Cast<string>(),
                CommittedNavigationDefinition version2 =>
                    version2.Tabs
                        .Select(tab => tab.Subscribe)
                        .Where(value => value is not null)
                        .Cast<string>(),
                _ => [],
            });
        HashSet<string> roots = subscriptions
            .SelectMany(SubscriptionRoots)
            .ToHashSet(StringComparer.Ordinal);
        if (roots.Count == 0)
            return Array.Empty<CatalogDefinition>();

        return new ReadOnlyCollection<CatalogDefinition>(
            _records.Values
                .OfType<CatalogDefinition>()
                .Where(catalog => catalog.Groups.Any(
                    group => roots.Contains(group.Name)))
                .ToArray());
    }

    private static IEnumerable<string> SubscriptionRoots(string expression)
    {
        foreach (string part in expression.Split('+'))
        {
            ReadOnlySpan<char> path = part;
            if (!path.IsEmpty && path[0] == ':')
                path = path[1..];
            if (path.IsEmpty)
                continue;
            int end = path.IndexOfAny(':', '@');
            ReadOnlySpan<char> root = end < 0 ? path : path[..end];
            if (!root.IsEmpty)
                yield return root.ToString();
        }
    }

    private static CommittedScenarioDefinitionSet CreateCommittedScenario(
        ScenarioRecordComposition records)
    {
        ScenarioDefinition scenario = records.Scenario;
        WorkspaceDefinition? workspace = records.Workspace as WorkspaceDefinition;
        if (records.Workspace is not null && workspace is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' references an incompatible workspace record.");
        }

        CommittedNavigationDefinition? navigation =
            records.Navigation as CommittedNavigationDefinition;
        if (records.Navigation is not null && navigation is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a schema-version-2 navigation record.");
        }

        CommittedViewDefinition? view =
            records.View as CommittedViewDefinition;
        if (records.View is not null && view is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a schema-version-2 committed view record.");
        }

        if (records.Query is not null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' cannot reference a schema-version-2 query in the query-free record slice.");
        }

        if ((navigation is null) != (view is null))
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' must reference committed navigation and view together.");
        }
        if (navigation is not null && view is not null)
            ValidateCommittedView(navigation, view);

        return new CommittedScenarioDefinitionSet(
            scenario,
            workspace,
            navigation,
            view,
            records.Catalogs);
    }

    private static void ValidateCommittedView(
        CommittedNavigationDefinition navigation,
        CommittedViewDefinition view)
    {
        if (view.States.Count != navigation.Tabs.Count + 1)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' requires one Workspace state followed by one state for every navigation tab.");
        }

        CommittedViewStateDefinition workspaceState = view.States[0];
        if (workspaceState.Navigation is not null)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' must begin with its null-navigation Workspace state.");
        }
        if (workspaceState.Subject is PortableSubjectRequest.Package)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' Workspace state cannot request a Package subject.");
        }
        if (workspaceState.Context is not null)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' Workspace state cannot retain Package context.");
        }

        ValidateQueryFreeState(view, workspaceState, 0);
        for (int index = 0; index < navigation.Tabs.Count; index++)
        {
            NavigationTabDefinition tab = navigation.Tabs[index];
            CommittedViewStateDefinition state = view.States[index + 1];
            if (!string.Equals(
                    state.Navigation,
                    tab.Id,
                    StringComparison.Ordinal))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index + 1} must reference navigation tab '{tab.Id}'.");
            }

            if (tab.Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate
                && IsDecorated(state))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' non-Package navigation row '{tab.Id}' must remain undecorated.");
            }

            ValidateQueryFreeState(view, state, index + 1);
        }
    }

    private static bool IsDecorated(CommittedViewStateDefinition state) =>
        state.Subject is not null
        || state.Context is not null
        || state.Facet is not null
        || state.Queries.Count != 0
        || state.Libraries.Count != 0;

    private static void ValidateQueryFreeState(
        CommittedViewDefinition view,
        CommittedViewStateDefinition state,
        int index)
    {
        if (state.Queries.Count != 0)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {index} cannot reference queries until #6971 supplies owner codecs.");
        }
        if (state.Libraries.Count != 0)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {index} cannot carry query Library scope without query-owner codecs.");
        }
    }

    private static WorkspaceContextInput ResolveContextInput(
        WorkspaceDefinition workspace,
        WorkspaceContextDefinition context)
    {
        if (!string.IsNullOrWhiteSpace(context.Subscribe))
        {
            throw new InspectionDefinitionException(
                $"Workspace '{workspace.Id}' context '{context.Name}' uses subscribe '{context.Subscribe}', which is not lowered in this slice. Inline members are supported.");
        }

        return new WorkspaceContextInput
        {
            Framework = context.Framework,
            RuntimeIdentifier = context.RuntimeIdentifier,
            Members = context.Members
                .Select(DefinitionCoordinateLowering.ToWorkspaceMember)
                .ToArray(),
        };
    }

    internal static WorkspacePlan CreateWorkspacePlan(
        WorkspaceDefinition workspace) =>
        new(
            [],
            workspace.Contexts
                .Select(context => ResolveContextInput(workspace, context))
                .ToArray());

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

internal sealed record ScenarioRecordComposition(
    ScenarioDefinition Scenario,
    InspectionDefinitionRecord? Workspace,
    InspectionDefinitionRecord? Query,
    InspectionDefinitionRecord? View,
    InspectionDefinitionRecord? Navigation,
    IReadOnlyList<CatalogDefinition> Catalogs)
{
    public IEnumerable<InspectionDefinitionRecord> Referenced
    {
        get
        {
            if (Workspace is not null)
                yield return Workspace;
            if (Query is not null)
                yield return Query;
            if (View is not null)
                yield return View;
            if (Navigation is not null)
                yield return Navigation;
            foreach (CatalogDefinition catalog in Catalogs)
                yield return catalog;
        }
    }
}

/// <summary>Closed resource-free schema dispatch for one scenario graph.</summary>
public abstract record InspectionDefinitionScenarioPreparationResult
{
    private protected InspectionDefinitionScenarioPreparationResult()
    {
    }

    public sealed record Version1(
        ResolvedScenario Scenario)
        : InspectionDefinitionScenarioPreparationResult;

    public sealed record Version2(
        CommittedScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;
}

/// <summary>
/// Strictly composed schema-version-2 records before runtime selector
/// resolution.
/// </summary>
public sealed class CommittedScenarioDefinitionSet
{
    internal CommittedScenarioDefinitionSet(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        CommittedNavigationDefinition? navigation,
        CommittedViewDefinition? view,
        IReadOnlyList<CatalogDefinition> catalogs)
    {
        Scenario = scenario;
        Workspace = workspace;
        Navigation = navigation;
        View = view;
        Catalogs = catalogs;
        Records = new ReadOnlyCollection<InspectionDefinitionRecord>(
            new InspectionDefinitionRecord?[]
            {
                Workspace,
                Navigation,
                View,
                Scenario,
            }
            .Where(record => record is not null)
            .Cast<InspectionDefinitionRecord>()
            .Concat(Catalogs)
            .ToArray());

    }

    public ScenarioDefinition Scenario { get; }

    public WorkspaceDefinition? Workspace { get; }

    public CommittedNavigationDefinition? Navigation { get; }

    public CommittedViewDefinition? View { get; }

    public IReadOnlyList<CatalogDefinition> Catalogs { get; }

    public IReadOnlyList<InspectionDefinitionRecord> Records { get; }
}

/// <summary>Host-facing result of resolving one scenario composition.</summary>
public sealed class ResolvedScenario
{
    internal ResolvedScenario(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        WorkspacePlan? workspacePlan,
        string? selectedContextName,
        IReadOnlyList<ResolvedWorkspaceContext> contexts,
        QueryDefinition? query,
        ViewDefinition? view,
        ResolvedNavigation? navigation)
    {
        Scenario = scenario;
        Workspace = workspace;
        WorkspacePlan = workspacePlan;
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
    /// The exact resource-free construction plan lowered from
    /// <see cref="Workspace"/>, or null for a workspace-free scenario.
    /// </summary>
    public WorkspacePlan? WorkspacePlan { get; }

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
        WorkspaceContextInput input)
    {
        Descriptor = descriptor;
        Input = input;
    }

    public WorkspaceContextDescriptor Descriptor { get; }

    public WorkspaceContextAddress Address => Descriptor.Address;

    public string Name => Descriptor.Name;

    public string? Framework => Descriptor.Framework;

    public string? RuntimeIdentifier => Descriptor.RuntimeIdentifier;

    /// <summary>The exact context input retained by the scenario's Workspace plan.</summary>
    public WorkspaceContextInput Input { get; }

    public IReadOnlyList<WorkspaceMemberCoordinate> Members => Input.Members;
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
