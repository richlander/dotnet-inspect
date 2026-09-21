using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Host- or bundle-owned registry of peer definition records. Scenarios compose
/// peers by id; record count never activates a scenario implicitly.
/// </summary>
public sealed class InspectionDefinitionRegistry
{
    private readonly Dictionary<(InspectionDefinitionKind Kind, string Id), InspectionDefinitionRecord> _records =
        new();
    private readonly Dictionary<string, PortableQueryDefinitionDescriptor>
        _queryDescriptors = new(StringComparer.Ordinal);

    public InspectionDefinitionRegistry()
    {
        AddQueryDescriptor(PackageQuery.DefinitionDescriptor);
    }

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

    public void AddQueryDescriptor(
        PortableQueryDefinitionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!_queryDescriptors.TryAdd(descriptor.QueryId, descriptor))
        {
            throw new InspectionDefinitionException(
                $"Duplicate portable query descriptor '{descriptor.QueryId}'.");
        }
    }

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
                $"Scenario '{scenarioId}' uses schema version "
                    + $"{scenario.SchemaVersion} and requires portable selector resolution.");
        }

        return ResolveVersion1Scenario(scenario);
    }

    /// <summary>
    /// Performs strict schema dispatch and resource-free composition. Versions
    /// 2 through 4 remain unresolved until the portable selector-resolution
    /// participant.
    /// </summary>
    public InspectionDefinitionScenarioPreparationResult PrepareScenario(
        string scenarioId) =>
        PrepareScenarioCore(
            scenarioId,
            NavigationTargetMatchMode.InheritOmitted,
            CancellationToken.None);

    internal InspectionDefinitionScenarioPreparationResult
        PrepareScenarioWithCancellation(
        string scenarioId,
        CancellationToken cancellationToken) =>
        PrepareScenarioCore(
            scenarioId,
            NavigationTargetMatchMode.InheritOmitted,
            cancellationToken);

    internal InspectionDefinitionScenarioPreparationResult PreparePacketScenario(
        string scenarioId) =>
        PrepareScenarioCore(
            scenarioId,
            NavigationTargetMatchMode.Exact,
            CancellationToken.None);

    internal InspectionDefinitionScenarioPreparationResult
        PreparePacketScenarioWithCancellation(
        string scenarioId,
        CancellationToken cancellationToken) =>
        PrepareScenarioCore(
            scenarioId,
            NavigationTargetMatchMode.Exact,
            cancellationToken);

    private InspectionDefinitionScenarioPreparationResult PrepareScenarioCore(
        string scenarioId,
        NavigationTargetMatchMode targetMatchMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        if (scenario.SchemaVersion is InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5)
        {
            CommittedScenarioDefinitionSet committed =
                CreateCommittedScenario(
                    records,
                    targetMatchMode,
                    cancellationToken);
            ValidateNavigationSources(
                records.Workspace as WorkspaceDefinition,
                records.Navigation,
                targetMatchMode);
            return scenario.SchemaVersion switch
            {
                InspectionDefinitionSchema.Version2 =>
                    new InspectionDefinitionScenarioPreparationResult.Version2(
                        committed),
                InspectionDefinitionSchema.Version3 =>
                    new InspectionDefinitionScenarioPreparationResult.Version3(
                        committed),
                InspectionDefinitionSchema.Version4 =>
                    new InspectionDefinitionScenarioPreparationResult.Version4(
                        committed),
                InspectionDefinitionSchema.Version5 =>
                    new InspectionDefinitionScenarioPreparationResult.Version5(
                        committed),
                _ => throw new UnreachableException(),
            };
        }

        return new InspectionDefinitionScenarioPreparationResult.Version1(
            CreateVersion1Scenario(records));
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
        InspectionDefinitionRecord? view = ResolveOptional(
            InspectionDefinitionKind.View,
            scenario.View,
            scenario,
            "view");
        return new(
            scenario,
            workspace,
            ResolveOptional(
                InspectionDefinitionKind.Query,
                scenario.Query,
                scenario,
                "query"),
            view,
            navigation,
            ResolveCommittedQueries(
                view as CommittedViewDefinition,
                scenario),
            ResolveCatalogDependencies(
                workspace as WorkspaceDefinition,
                navigation));
    }

    private IReadOnlyList<CommittedQueryDefinition>
        ResolveCommittedQueries(
            CommittedViewDefinition? view,
            ScenarioDefinition scenario)
    {
        if (view is null)
            return Array.Empty<CommittedQueryDefinition>();

        var queries = new Dictionary<string, CommittedQueryDefinition>(
            StringComparer.Ordinal);
        foreach (CommittedViewStateDefinition state in view.States)
        {
            foreach (string queryId in state.Queries)
            {
                if (!TryGet(
                    InspectionDefinitionKind.Query,
                    queryId,
                    out InspectionDefinitionRecord? record))
                {
                    throw new InspectionDefinitionException(
                        $"Scenario '{scenario.Id}' references unknown query "
                            + $"'{queryId}' from committed view '{view.Id}'.");
                }
                if (record is not CommittedQueryDefinition query)
                {
                    throw new InspectionDefinitionException(
                        $"Scenario '{scenario.Id}' committed view '{view.Id}' "
                            + $"references incompatible query '{queryId}'.");
                }

                queries.TryAdd(query.Id, query);
            }
        }

        return queries.Values
            .OrderBy(query => query.Id, StringComparer.Ordinal)
            .ToArray();
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
        else if (workspace.Contexts.Count == 0
            && workspace.SchemaVersion is (
                InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4
                or InspectionDefinitionSchema.Version5))
        {
            return;
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

    private static void ValidateNavigationSources(
        WorkspaceDefinition? workspace,
        InspectionDefinitionRecord? navigation,
        NavigationTargetMatchMode targetMatchMode)
    {
        if (workspace is null || navigation is null)
            return;

        _ = ResolveNavigationSources(
            workspace,
            navigation,
            targetMatchMode);
    }

    internal static IReadOnlyDictionary<
        string,
        PackageNavigationSource> ResolvePackageNavigationSources(
            WorkspaceDefinition workspace,
            InspectionDefinitionRecord? navigation,
            NavigationTargetMatchMode targetMatchMode)
    {
        if (navigation is null)
        {
            return new ReadOnlyDictionary<
                string,
                PackageNavigationSource>(
                    new Dictionary<
                        string,
                        PackageNavigationSource>());
        }

        IReadOnlyDictionary<string, ResolvedNavigationSource> sources =
            ResolveNavigationSources(
                workspace,
                navigation,
                targetMatchMode);
        return new ReadOnlyDictionary<
            string,
            PackageNavigationSource>(
                sources
                    .Where(pair =>
                        pair.Value.EffectiveCoordinate
                            is DefinitionMemberCoordinate.PackageCoordinate
                        && pair.Value.MemberIndex is not null)
                    .ToDictionary(
                        static pair => pair.Key,
                        static pair =>
                            new PackageNavigationSource(
                                pair.Value.ContextIndex,
                                pair.Value.MemberIndex!.Value,
                                (DefinitionMemberCoordinate.PackageCoordinate)
                                    pair.Value.EffectiveCoordinate!),
                        StringComparer.Ordinal));
    }

    internal static IReadOnlyDictionary<string, GroupNavigationSource>
        ResolveGroupNavigationSources(
            WorkspaceDefinition workspace,
            InspectionDefinitionRecord? navigation,
            NavigationTargetMatchMode targetMatchMode)
    {
        if (navigation is null)
            return new ReadOnlyDictionary<string, GroupNavigationSource>(
                new Dictionary<string, GroupNavigationSource>());

        return new ReadOnlyDictionary<string, GroupNavigationSource>(
            ResolveNavigationSources(workspace, navigation, targetMatchMode)
                .Where(static pair =>
                    pair.Value.EffectiveCoordinate is null
                    && pair.Value.MemberIndex is null)
                .ToDictionary(
                    static pair => pair.Key,
                    pair => new GroupNavigationSource(
                        pair.Value.ContextIndex,
                        workspace.Contexts[pair.Value.ContextIndex].Members.Count,
                        pair.Value.RuntimeIdentifier),
                    StringComparer.Ordinal));
    }

    internal static IReadOnlyList<PackageNavigationSource>
        ResolvePackageNavigationSourcePositions(
            WorkspaceDefinition workspace,
            NavigationTabDefinition tab,
            NavigationTargetMatchMode targetMatchMode)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Coordinate
            is not DefinitionMemberCoordinate.PackageCoordinate)
        {
            return [];
        }

        return MatchingNavigationSources(
                workspace,
                tab,
                targetMatchMode,
                coalesceEqualSources: false)
            .Where(static source =>
                source.EffectiveCoordinate
                        is DefinitionMemberCoordinate.PackageCoordinate
                && source.MemberIndex is not null)
            .Select(static source =>
                new PackageNavigationSource(
                        source.ContextIndex,
                        source.MemberIndex!.Value,
                        (DefinitionMemberCoordinate.PackageCoordinate)
                            source.EffectiveCoordinate!))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ResolvedNavigationSource>
        ResolveNavigationSources(
            WorkspaceDefinition workspace,
            InspectionDefinitionRecord navigation,
            NavigationTargetMatchMode targetMatchMode)
    {
        IReadOnlyList<NavigationTabDefinition> tabs = navigation switch
        {
            NavigationDefinition version1 => version1.Tabs,
            CommittedNavigationDefinition version2 => version2.Tabs,
            _ => throw new InspectionDefinitionException(
                $"Navigation '{navigation.Id}' has an incompatible record kind."),
        };
        var resolved =
            new Dictionary<string, ResolvedNavigationSource>(
                tabs.Count,
                StringComparer.Ordinal);
        var matchedSources = new HashSet<NavigationSourceIdentity>();
        foreach (NavigationTabDefinition tab in tabs)
        {
            NavigationSourceCandidate[] matches =
                MatchingNavigationSources(
                    workspace,
                    tab,
                    targetMatchMode,
                    coalesceEqualSources: true);
            if (matches.Length != 1)
            {
                throw new InspectionDefinitionException(
                    matches.Length == 0
                        ? $"Navigation tab '{tab.Id}' does not match an explicit "
                            + $"member or subscription target in workspace "
                            + $"'{workspace.Id}'."
                        : $"Navigation tab '{tab.Id}' is ambiguous across "
                            + "workspace contexts with different effective "
                            + "targets.");
            }

            NavigationSourceCandidate match = matches[0];
            if (!matchedSources.Add(match.Identity))
            {
                throw new InspectionDefinitionException(
                    $"Navigation tab '{tab.Id}' repeats a normalized source.");
            }
            resolved.Add(
                tab.Id,
                new ResolvedNavigationSource(
                    match.EffectiveCoordinate,
                    match.Identity.Target.Framework,
                    match.Identity.Target.RuntimeIdentifier,
                    match.ContextIndex,
                    match.MemberIndex));
        }

        return new ReadOnlyDictionary<string, ResolvedNavigationSource>(
            resolved);
    }

    private static NavigationSourceCandidate[] MatchingNavigationSources(
        WorkspaceDefinition workspace,
        NavigationTabDefinition tab,
        NavigationTargetMatchMode targetMatchMode,
        bool coalesceEqualSources)
    {
        NavigationSourceSelector selector = NavigationSelector(tab);
        IEnumerable<NavigationSourceCandidate> candidates =
            workspace.Contexts.SelectMany(ContextSources);
        if (coalesceEqualSources)
        {
            candidates = candidates
                .GroupBy(static source => source.Identity)
                .Select(static group => group.First());
        }

        return
        [
            .. candidates.Where(source =>
                source.Identity.Core == selector.Core
                && MatchesTarget(
                    selector.Target,
                    source.Identity.Target,
                    targetMatchMode)),
        ];
    }

    private static IEnumerable<NavigationSourceCandidate> ContextSources(
        WorkspaceContextDefinition context,
        int contextIndex)
    {
        NavigationTarget target = EffectiveContextTarget(context);
        if (context.Subscribe is not null)
        {
            yield return new NavigationSourceCandidate(
                new NavigationSourceIdentity(
                    new NavigationSourceCore(
                        "group",
                        NormalizeSubscription(context.Subscribe),
                        null,
                        null,
                        null),
                    target),
                null,
                contextIndex,
                null);
        }

        for (int memberIndex = 0;
            memberIndex < context.Members.Count;
            memberIndex++)
        {
            DefinitionMemberCoordinate member =
                context.Members[memberIndex];
            yield return new NavigationSourceCandidate(
                new NavigationSourceIdentity(
                    SourceCore(member),
                    target),
                ApplyEffectiveTarget(member, target),
                contextIndex,
                memberIndex);
        }
    }

    private static NavigationSourceSelector NavigationSelector(
        NavigationTabDefinition tab)
    {
        NavigationTarget tabTarget = new(
            NormalizeFramework(tab.Framework),
            NormalizeRuntimeIdentifier(tab.RuntimeIdentifier));
        if (tab.Subscribe is not null)
        {
            return new NavigationSourceSelector(
                new NavigationSourceCore(
                    "group",
                    NormalizeSubscription(tab.Subscribe),
                    null,
                    null,
                    null),
                tabTarget);
        }

        DefinitionMemberCoordinate coordinate =
            tab.Coordinate
            ?? throw new InspectionDefinitionException(
                $"Navigation tab '{tab.Id}' has no source.");
        NavigationTarget coordinateTarget = CoordinateTarget(coordinate);
        return new NavigationSourceSelector(
            SourceCore(coordinate),
            MergeTargets(
                coordinateTarget,
                tabTarget,
                $"Navigation tab '{tab.Id}'"));
    }

    /// <summary>
    /// Resolves the normalized context-wide target composed from the context
    /// and all target-bearing member declarations.
    /// </summary>
    public static EffectiveWorkspaceContextTarget ResolveEffectiveContextTarget(
        WorkspaceContextDefinition context)
    {
        ArgumentNullException.ThrowIfNull(context);
        NavigationTarget target = EffectiveContextTarget(context);
        return new(target.Framework, target.RuntimeIdentifier);
    }

    private static NavigationTarget EffectiveContextTarget(
        WorkspaceContextDefinition context)
    {
        var target = new NavigationTarget(
            NormalizeFramework(context.Framework),
            NormalizeRuntimeIdentifier(context.RuntimeIdentifier));
        foreach (DefinitionMemberCoordinate member in context.Members)
        {
            target = MergeTargets(
                target,
                CoordinateTarget(member),
                $"Workspace context '{context.Name}'");
        }

        return target;
    }

    private static NavigationTarget CoordinateTarget(
        DefinitionMemberCoordinate coordinate) =>
        coordinate switch
        {
            DefinitionMemberCoordinate.PackageCoordinate package =>
                new(
                    NormalizeFramework(package.Framework),
                    NormalizeRuntimeIdentifier(package.RuntimeIdentifier)),
            DefinitionMemberCoordinate.PlatformCoordinate platform =>
                new(
                    NormalizeFramework(platform.Framework),
                    null),
            DefinitionMemberCoordinate.ProjectCoordinate project =>
                new(
                    NormalizeFramework(project.Framework),
                    NormalizeRuntimeIdentifier(project.RuntimeIdentifier)),
            DefinitionMemberCoordinate.DirectoryCoordinate directory =>
                new(
                    NormalizeFramework(directory.Framework),
                    NormalizeRuntimeIdentifier(directory.RuntimeIdentifier)),
            _ => default,
        };

    private static NavigationSourceCore SourceCore(
        DefinitionMemberCoordinate coordinate) =>
        coordinate switch
        {
            DefinitionMemberCoordinate.PackageCoordinate package =>
                PackageSourceCore(package),
            DefinitionMemberCoordinate.PlatformCoordinate platform =>
                new(
                    "platform",
                    platform.Family.ToLowerInvariant(),
                    platform.Assembly,
                    NormalizeVersion(platform.Version),
                    null),
            DefinitionMemberCoordinate.EmbeddedCoordinate embedded =>
                new(
                    "embedded",
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName,
                    null),
            DefinitionMemberCoordinate.ProjectCoordinate project =>
                new("project", project.Path, null, null, null),
            DefinitionMemberCoordinate.LocalCoordinate local =>
                new("local", local.Path, null, null, null),
            DefinitionMemberCoordinate.DirectoryCoordinate directory =>
                new("directory", directory.Path, null, null, null),
            _ => throw new InspectionDefinitionException(
                $"Unsupported coordinate kind '{coordinate.Kind}'."),
        };

    private static NavigationSourceCore PackageSourceCore(
        DefinitionMemberCoordinate.PackageCoordinate package)
    {
        if (!PackageCoordinateResolver.IsCanonicalPackageId(package.Id))
        {
            throw new InspectionDefinitionException(
                $"Invalid Package id '{package.Id}'.");
        }

        return new NavigationSourceCore(
            "package",
            package.Id.ToLowerInvariant(),
            NormalizeVersion(package.Version),
            null,
            null);
    }

    private static DefinitionMemberCoordinate? ApplyEffectiveTarget(
        DefinitionMemberCoordinate? coordinate,
        NavigationTarget target) =>
        coordinate switch
        {
            DefinitionMemberCoordinate.PackageCoordinate package =>
                new DefinitionMemberCoordinate.PackageCoordinate(
                    package.Id,
                    package.Version,
                    target.Framework,
                    target.RuntimeIdentifier),
            DefinitionMemberCoordinate.PlatformCoordinate platform =>
                new DefinitionMemberCoordinate.PlatformCoordinate(
                    platform.Family,
                    platform.Assembly,
                    platform.Version,
                    target.Framework),
            DefinitionMemberCoordinate.ProjectCoordinate project =>
                new DefinitionMemberCoordinate.ProjectCoordinate(
                    project.Path,
                    target.Framework,
                    target.RuntimeIdentifier),
            DefinitionMemberCoordinate.DirectoryCoordinate directory =>
                new DefinitionMemberCoordinate.DirectoryCoordinate(
                    directory.Path,
                    target.Framework,
                    target.RuntimeIdentifier),
            _ => coordinate,
        };

    private static NavigationTarget MergeTargets(
        NavigationTarget left,
        NavigationTarget right,
        string owner) =>
        new(
            MergeTarget(
                left.Framework,
                right.Framework,
                owner,
                "framework"),
            MergeTarget(
                left.RuntimeIdentifier,
                right.RuntimeIdentifier,
                owner,
                "runtime identifier"));

    private static string? MergeTarget(
        string? left,
        string? right,
        string owner,
        string name)
    {
        if (left is null)
            return right;
        if (right is null || string.Equals(
                left,
                right,
                StringComparison.Ordinal))
        {
            return left;
        }

        throw new InspectionDefinitionException(
            $"{owner} has conflicting {name} declarations.");
    }

    private static bool MatchesTarget(
        NavigationTarget selector,
        NavigationTarget candidate,
        NavigationTargetMatchMode targetMatchMode) =>
        targetMatchMode is NavigationTargetMatchMode.Exact
            ? selector == candidate
            : (selector.Framework is null
            || string.Equals(
                selector.Framework,
                candidate.Framework,
                StringComparison.Ordinal))
        && (selector.RuntimeIdentifier is null
            || string.Equals(
                selector.RuntimeIdentifier,
                candidate.RuntimeIdentifier,
                StringComparison.Ordinal));

    internal static string? NormalizeVersion(string? value)
    {
        if (value is null)
            return null;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(value, out NuGetVersion? version))
        {
            throw new InspectionDefinitionException(
                $"Invalid exact version '{value}'.");
        }

        return version.ToNormalizedString().ToLowerInvariant();
    }

    internal static string? NormalizeFramework(string? value)
    {
        if (value is null)
            return null;
        if (!PackageCoordinateResolver.IsAcquisitionTargetText(value))
        {
            throw new InspectionDefinitionException(
                $"Invalid acquisition framework '{value}'.");
        }

        return value.ToLowerInvariant();
    }

    private static string? NormalizeRuntimeIdentifier(string? value)
    {
        if (value is null)
            return null;
        if (!PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(value))
        {
            throw new InspectionDefinitionException(
                $"Invalid runtime identifier '{value}'.");
        }

        return value;
    }

    private static string NormalizeSubscription(string subscription)
    {
        if (!WorkspaceSharePacketCodec.TryParseGroupExpression(
            subscription,
            out IReadOnlyList<GroupExpressionPin> pins))
        {
            throw new InspectionDefinitionException(
                $"Invalid group subscription '{subscription}'.");
        }

        var canonical = new StringBuilder(subscription.Length);
        int cursor = 0;
        foreach (GroupExpressionPin pin in pins)
        {
            canonical.Append(
                subscription.AsSpan(
                    cursor,
                    pin.ValueStart - cursor));
            canonical.Append(
                NormalizeVersion(subscription.Substring(
                    pin.ValueStart,
                    pin.ValueLength)));
            cursor = pin.ValueStart + pin.ValueLength;
        }
        canonical.Append(subscription.AsSpan(cursor));
        return canonical.ToString();
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

    private CommittedScenarioDefinitionSet CreateCommittedScenario(
        ScenarioRecordComposition records,
        NavigationTargetMatchMode targetMatchMode,
        CancellationToken cancellationToken)
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
                $"Scenario '{scenario.Id}' requires a committed navigation record.");
        }

        CommittedViewDefinition? view =
            records.View as CommittedViewDefinition;
        if (records.View is not null && view is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a committed view record.");
        }

        if (records.Query is not null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' must carry committed queries through its view states.");
        }

        if (workspace is not null && (navigation is null || view is null))
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' must reference committed navigation and view.");
        }
        if ((navigation is null) != (view is null))
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' must reference committed navigation and view together.");
        }
        if (workspace is { Contexts.Count: > 0 }
            && navigation is { Tabs.Count: 0 })
        {
            throw new InspectionDefinitionException(
                $"Committed navigation '{navigation.Id}' requires at least one tab when workspace '{workspace.Id}' has contexts.");
        }
        IReadOnlyList<BoundCommittedQuery> queryBindings =
            Array.Empty<BoundCommittedQuery>();
        if (navigation is not null && view is not null)
        {
            queryBindings = ValidateCommittedView(
                scenario,
                workspace,
                navigation,
                view,
                records.CommittedQueries,
                cancellationToken);
        }

        return new CommittedScenarioDefinitionSet(
            scenario,
            workspace,
            navigation,
            view,
            records.Catalogs,
            targetMatchMode,
            queryBindings);
    }

    private static Version1ScenarioDefinitionSet CreateVersion1Scenario(
        ScenarioRecordComposition records)
    {
        ScenarioDefinition scenario = records.Scenario;
        WorkspaceDefinition? workspace =
            records.Workspace as WorkspaceDefinition;
        if (records.Workspace is not null && workspace is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' references an incompatible workspace record.");
        }

        QueryDefinition? query = records.Query as QueryDefinition;
        if (records.Query is not null && query is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a schema-version-1 query record.");
        }

        ViewDefinition? view = records.View as ViewDefinition;
        if (records.View is not null && view is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a schema-version-1 view record.");
        }

        NavigationDefinition? navigation =
            records.Navigation as NavigationDefinition;
        if (records.Navigation is not null && navigation is null)
        {
            throw new InspectionDefinitionException(
                $"Scenario '{scenario.Id}' requires a schema-version-1 navigation record.");
        }

        return new Version1ScenarioDefinitionSet(
            scenario,
            workspace,
            query,
            view,
            navigation,
            records.Catalogs);
    }

    private IReadOnlyList<BoundCommittedQuery> ValidateCommittedView(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        CommittedNavigationDefinition navigation,
        CommittedViewDefinition view,
        IReadOnlyList<CommittedQueryDefinition> queries,
        CancellationToken cancellationToken)
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
        if (workspaceState.Subject is not PortableSubjectRequest.Workspace)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' Workspace state must request the Workspace subject.");
        }
        if (workspaceState.Context is not null)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' Workspace state cannot retain Package context.");
        }

        var queriesById = queries.ToDictionary(
            query => query.Id,
            StringComparer.Ordinal);
        var queryBindings = new List<BoundCommittedQuery>();
        WorkspaceContextDefinition? selectedContext =
            ResolveSelectedContext(scenario, workspace);
        ValidateQueryState(
            navigation,
            view,
            workspaceState,
            0,
            selectedContext,
            queriesById,
            queryBindings,
            cancellationToken);
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

            ValidateQueryState(
                navigation,
                view,
                state,
                index + 1,
                selectedContext,
                queriesById,
                queryBindings,
                cancellationToken);
        }

        ValidateEmptyWorkspaceComposition(
            scenario,
            workspace,
            navigation,
            view,
            queryBindings);
        return new ReadOnlyCollection<BoundCommittedQuery>(
            queryBindings.ToArray());
    }

    private static bool IsDecorated(CommittedViewStateDefinition state) =>
        state.Subject is not null
        || state.Context is not null
        || state.Facet is not null
        || state.Queries.Count != 0
        || state.Libraries.Count != 0;

    private void ValidateQueryState(
        CommittedNavigationDefinition navigation,
        CommittedViewDefinition view,
        CommittedViewStateDefinition state,
        int index,
        WorkspaceContextDefinition? selectedContext,
        IReadOnlyDictionary<string, CommittedQueryDefinition> queriesById,
        List<BoundCommittedQuery> queryBindings,
        CancellationToken cancellationToken)
    {
        if (state.Libraries.Count != 0 && index == 0)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' Workspace state cannot carry "
                    + "state-level Library scope.");
        }
        if (state.Libraries.Count != 0
            && navigation.Tabs[index - 1].Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {index} can carry Library "
                    + "scope only for a direct Package coordinate.");
        }

        var purposes = new HashSet<string>(StringComparer.Ordinal);
        bool consumesLibraryScope = false;
        foreach (string queryId in state.Queries)
        {
            if (!queriesById.TryGetValue(
                queryId,
                out CommittedQueryDefinition? query))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index} references "
                        + $"unknown query '{queryId}'.");
            }
            if (!_queryDescriptors.TryGetValue(
                query.QueryId,
                out PortableQueryDefinitionDescriptor? descriptor))
            {
                throw new InspectionDefinitionException(
                    $"Query '{query.Id}' uses unknown vocabulary "
                        + $"'{query.QueryId}'.");
            }
            if (!purposes.Add(descriptor.Purpose))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index} has duplicate "
                        + $"query purpose '{descriptor.Purpose}'.");
            }

            PortableQueryDefinitionInputs inputs =
                descriptor.Inputs;
            if (inputs.CoordinateFreePrimary)
            {
                cancellationToken.ThrowIfCancellationRequested();
                queryBindings.Add(
                    descriptor.Bind(
                        query,
                        new PortableQueryDefinitionAttachment(
                            subjectKind: null,
                            facetId: null,
                            stateCoordinate: null,
                            selectedContext: null,
                            stateLibraryScope: null),
                        cancellationToken));
                continue;
            }

            if (state.Subject is null
                || !inputs.SubjectKinds.Contains(state.Subject.Kind))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index} query "
                        + $"'{queryId}' does not accept its subject.");
            }
            if (state.Facet is null
                || !inputs.FacetIds.Contains(
                    state.Facet,
                    StringComparer.Ordinal))
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index} query "
                        + $"'{queryId}' does not accept facet '{state.Facet}'.");
            }

            DefinitionMemberCoordinate? stateCoordinate =
                index == 0
                    ? null
                    : navigation.Tabs[index - 1].Coordinate;
            ValidateInputRequirement(
                inputs.StateCoordinate,
                stateCoordinate is not null,
                view,
                index,
                queryId,
                "state coordinate");
            ValidateInputRequirement(
                inputs.SelectedContext,
                selectedContext is not null,
                view,
                index,
                queryId,
                "selected context");
            if (inputs.StateLibraryScope
                    == PortableQueryInputRequirement.Required
                && state.Libraries.Count == 0)
            {
                throw new InspectionDefinitionException(
                    $"Committed view '{view.Id}' state {index} query "
                        + $"'{queryId}' requires state-level Library scope.");
            }
            consumesLibraryScope |=
                inputs.StateLibraryScope
                    is not PortableQueryInputRequirement.Forbidden;

            cancellationToken.ThrowIfCancellationRequested();
            queryBindings.Add(
                descriptor.Bind(
                    query,
                    new PortableQueryDefinitionAttachment(
                        state.Subject.Kind,
                        state.Facet,
                        inputs.StateCoordinate
                            == PortableQueryInputRequirement.Forbidden
                            ? null
                            : stateCoordinate,
                        inputs.SelectedContext
                            == PortableQueryInputRequirement.Forbidden
                            ? null
                            : selectedContext,
                        inputs.StateLibraryScope
                            == PortableQueryInputRequirement.Forbidden
                            ? null
                            : state.Libraries),
                    cancellationToken));
        }

        if (state.Libraries.Count != 0 && !consumesLibraryScope)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {index} carries Library "
                    + "scope without a consuming query.");
        }
    }

    private static WorkspaceContextDefinition? ResolveSelectedContext(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace)
    {
        if (workspace is null)
            return null;
        if (scenario.Context is not null)
        {
            return workspace.Contexts.Single(
                context => string.Equals(
                    context.Name,
                    scenario.Context,
                    StringComparison.Ordinal));
        }

        return workspace.Contexts.Count == 1
            ? workspace.Contexts[0]
            : null;
    }

    private static void ValidateInputRequirement(
        PortableQueryInputRequirement requirement,
        bool present,
        CommittedViewDefinition view,
        int stateIndex,
        string queryId,
        string input)
    {
        if (requirement == PortableQueryInputRequirement.Required && !present)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {stateIndex} query "
                    + $"'{queryId}' requires {input}.");
        }
        if (requirement == PortableQueryInputRequirement.Forbidden && present)
        {
            throw new InspectionDefinitionException(
                $"Committed view '{view.Id}' state {stateIndex} query "
                    + $"'{queryId}' does not accept {input}.");
        }
    }

    private static void ValidateEmptyWorkspaceComposition(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        CommittedNavigationDefinition navigation,
        CommittedViewDefinition view,
        IReadOnlyList<BoundCommittedQuery> queryBindings)
    {
        if (workspace is null
            || workspace.Contexts.Count != 0
            || workspace.Registrations.Count != 0)
        {
            foreach (CommittedViewStateDefinition state in view.States)
            {
                foreach (string queryId in state.Queries)
                {
                    if (queryBindings.Any(
                        binding =>
                            string.Equals(
                                binding.Definition.Id,
                                queryId,
                                StringComparison.Ordinal)
                            && binding.Descriptor.Inputs.CoordinateFreePrimary))
                    {
                        throw new InspectionDefinitionException(
                            $"Coordinate-free primary query '{queryId}' cannot "
                                + "be mixed with Workspace context or registration state.");
                    }
                }
            }

            return;
        }

        CommittedViewStateDefinition workspaceState = view.States[0];
        bool queryOnly =
            scenario.SchemaVersion == InspectionDefinitionSchema.Version3
            && scenario.Context is null
            && navigation.Tabs.Count == 0
            && navigation.Focus is null
            && view.States.Count == 1
            && workspaceState.Navigation is null
            && workspaceState.Subject is PortableSubjectRequest.Workspace
            && workspaceState.Context is null
            && workspaceState.Facet is null
            && workspaceState.Libraries.Count == 0
            && workspaceState.Queries.Count == 1
            && queryBindings.Any(
                binding =>
                    string.Equals(
                        binding.Definition.Id,
                        workspaceState.Queries[0],
                        StringComparison.Ordinal)
                    && binding.Descriptor.Inputs.CoordinateFreePrimary);
        if (!queryOnly)
        {
            throw new InspectionDefinitionException(
                $"Schema-version-3 workspace '{workspace.Id}' with no contexts "
                    + "or registrations requires the exact query-only composition.");
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
            [.. workspace.Registrations],
            workspace.Contexts
                .Select(context => ResolveContextInput(workspace, context))
                .ToArray());

    internal static WorkspacePlan CreateCompleteRestorationWorkspacePlan(
        WorkspaceDefinition workspace) =>
        new(
            [.. workspace.Registrations],
            workspace.Contexts
                .Select(context =>
                    ResolveCompleteRestorationContextInput(workspace, context))
                .ToArray());

    private static WorkspaceContextInput ResolveCompleteRestorationContextInput(
        WorkspaceDefinition workspace,
        WorkspaceContextDefinition context)
    {
        if (string.IsNullOrWhiteSpace(context.Subscribe))
            return ResolveContextInput(workspace, context);

        NavigationTarget target = EffectiveContextTarget(context);
        if (target.Framework is null
            || !WorkspaceSharePacketCodec.TryParseGroupExpression(
                context.Subscribe,
                out IReadOnlyList<GroupExpressionPin> pins)
            || pins is not [{ SegmentIndex: 0 } pin]
            || pin.SeparatorIndex != ":Platform".Length
            || pin.ValueStart + pin.ValueLength != context.Subscribe.Length
            || !context.Subscribe.StartsWith(
                ":Platform@",
                StringComparison.Ordinal))
        {
            throw new InspectionDefinitionException(
                $"Workspace '{workspace.Id}' context '{context.Name}' uses "
                    + $"unsupported complete-restoration group "
                    + $"'{context.Subscribe}'. A single pinned :Platform group "
                    + "with an effective framework is required.");
        }

        string version = context.Subscribe.Substring(
            pin.ValueStart,
            pin.ValueLength);
        return new WorkspaceContextInput
        {
            Framework = target.Framework,
            RuntimeIdentifier = target.RuntimeIdentifier,
            Members =
            [
                // Package navigation indexes refer to the original member order.
                .. context.Members.Select(
                    DefinitionCoordinateLowering.ToWorkspaceMember),
                WorkspaceMemberCoordinate.Platform(
                    "runtime",
                    version: version,
                    framework: target.Framework),
            ],
        };
    }

    private static ResolvedNavigation ResolveNavigation(
        NavigationDefinition navigation)
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
                tab.RuntimeIdentifier,
                contextIndex: null,
                memberIndex: null));
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

    private readonly record struct NavigationSourceCore(
        string Kind,
        string Primary,
        string? Secondary,
        string? Tertiary,
        string? Quaternary);

    private readonly record struct NavigationTarget(
        string? Framework,
        string? RuntimeIdentifier);

    private readonly record struct NavigationSourceIdentity(
        NavigationSourceCore Core,
        NavigationTarget Target);

    private readonly record struct NavigationSourceSelector(
        NavigationSourceCore Core,
        NavigationTarget Target);

    private sealed record NavigationSourceCandidate(
        NavigationSourceIdentity Identity,
        DefinitionMemberCoordinate? EffectiveCoordinate,
        int ContextIndex,
        int? MemberIndex);

    private sealed record ResolvedNavigationSource(
        DefinitionMemberCoordinate? EffectiveCoordinate,
        string? Framework,
        string? RuntimeIdentifier,
        int ContextIndex,
        int? MemberIndex);
}

internal enum NavigationTargetMatchMode
{
    InheritOmitted,
    Exact,
}

internal sealed record PackageNavigationSource(
    int ContextIndex,
    int MemberIndex,
    DefinitionMemberCoordinate.PackageCoordinate EffectiveCoordinate);

internal sealed record GroupNavigationSource(
    int ContextIndex,
    int MemberIndex,
    string? RuntimeIdentifier);

internal sealed record ScenarioRecordComposition(
    ScenarioDefinition Scenario,
    InspectionDefinitionRecord? Workspace,
    InspectionDefinitionRecord? Query,
    InspectionDefinitionRecord? View,
    InspectionDefinitionRecord? Navigation,
    IReadOnlyList<CommittedQueryDefinition> CommittedQueries,
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
            foreach (CommittedQueryDefinition query in CommittedQueries)
                yield return query;
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
        Version1ScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;

    public sealed record Version2(
        CommittedScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;

    public sealed record Version3(
        CommittedScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;

    public sealed record Version4(
        CommittedScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;

    public sealed record Version5(
        CommittedScenarioDefinitionSet Definitions)
        : InspectionDefinitionScenarioPreparationResult;
}

/// <summary>
/// One normalized effective target shared by every member in a Workspace
/// context.
/// </summary>
public sealed record EffectiveWorkspaceContextTarget(
    string? Framework,
    string? RuntimeIdentifier);

/// <summary>
/// Strictly composed schema-version-1 records before complete-restoration
/// lowering or existing source-specific execution.
/// </summary>
public sealed class Version1ScenarioDefinitionSet
{
    internal Version1ScenarioDefinitionSet(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        QueryDefinition? query,
        ViewDefinition? view,
        NavigationDefinition? navigation,
        IReadOnlyList<CatalogDefinition> catalogs)
    {
        Scenario = scenario;
        Workspace = workspace;
        Query = query;
        View = view;
        Navigation = navigation;
        Catalogs = catalogs;
        Records = new ReadOnlyCollection<InspectionDefinitionRecord>(
            new InspectionDefinitionRecord?[]
            {
                Workspace,
                Query,
                View,
                Navigation,
                Scenario,
            }
            .Where(record => record is not null)
            .Cast<InspectionDefinitionRecord>()
            .Concat(Catalogs)
            .ToArray());
    }

    public ScenarioDefinition Scenario { get; }

    public WorkspaceDefinition? Workspace { get; }

    public QueryDefinition? Query { get; }

    public ViewDefinition? View { get; }

    public NavigationDefinition? Navigation { get; }

    public IReadOnlyList<CatalogDefinition> Catalogs { get; }

    public IReadOnlyList<InspectionDefinitionRecord> Records { get; }
}

/// <summary>
/// Strictly composed schema-version-2-through-4 records before runtime selector
/// resolution.
/// </summary>
public sealed class CommittedScenarioDefinitionSet
{
    internal CommittedScenarioDefinitionSet(
        ScenarioDefinition scenario,
        WorkspaceDefinition? workspace,
        CommittedNavigationDefinition? navigation,
        CommittedViewDefinition? view,
        IReadOnlyList<CatalogDefinition> catalogs,
        NavigationTargetMatchMode navigationTargetMatchMode =
            NavigationTargetMatchMode.InheritOmitted,
        IReadOnlyList<BoundCommittedQuery>? queryBindings = null)
    {
        Scenario = scenario;
        Workspace = workspace;
        Navigation = navigation;
        View = view;
        Catalogs = catalogs;
        QueryBindings = queryBindings is null
            ? Array.Empty<BoundCommittedQuery>()
            : new ReadOnlyCollection<BoundCommittedQuery>(
                queryBindings.ToArray());
        Queries = new ReadOnlyCollection<CommittedQueryDefinition>(
            QueryBindings
                .Select(binding => binding.Definition)
                .DistinctBy(query => query.Id, StringComparer.Ordinal)
                .OrderBy(query => query.Id, StringComparer.Ordinal)
                .ToArray());
        NavigationTargetMatchMode = navigationTargetMatchMode;
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
            .Concat(Queries)
            .Concat(Catalogs)
            .ToArray());

    }

    public ScenarioDefinition Scenario { get; }

    public WorkspaceDefinition? Workspace { get; }

    public CommittedNavigationDefinition? Navigation { get; }

    public CommittedViewDefinition? View { get; }

    public IReadOnlyList<CommittedQueryDefinition> Queries { get; }

    public IReadOnlyList<BoundCommittedQuery> QueryBindings { get; }

    public IReadOnlyList<CatalogDefinition> Catalogs { get; }

    internal NavigationTargetMatchMode NavigationTargetMatchMode { get; }

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
        string? runtimeIdentifier,
        int? contextIndex,
        int? memberIndex)
    {
        Id = id;
        Coordinate = coordinate;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
        ContextIndex = contextIndex;
        MemberIndex = memberIndex;
    }

    public string Id { get; }

    public WorkspaceMemberCoordinate Coordinate { get; }

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }

    internal int? ContextIndex { get; }

    internal int? MemberIndex { get; }
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
