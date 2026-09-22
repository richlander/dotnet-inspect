using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// The packet-local canonical records represented by one workspace share packet.
/// </summary>
public sealed class WorkspaceSharePacketDefinitionSet
{
    public WorkspaceSharePacketDefinitionSet(
        WorkspaceDefinition workspace,
        NavigationDefinition navigation,
        ViewDefinition view,
        ScenarioDefinition scenario)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        View = view ?? throw new ArgumentNullException(nameof(view));
        Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        Records = new ReadOnlyCollection<InspectionDefinitionRecord>(
            [Workspace, Navigation, View, Scenario]);
    }

    public WorkspaceDefinition Workspace { get; }

    public NavigationDefinition Navigation { get; }

    public ViewDefinition View { get; }

    public ScenarioDefinition Scenario { get; }

    public IReadOnlyList<InspectionDefinitionRecord> Records { get; }
}

public enum WorkspaceSharePacketProjectionFailureKind
{
    NonProjectable,
    InvalidDefinitionSet,
}

public sealed record WorkspaceSharePacketProjectionFailure(
    WorkspaceSharePacketProjectionFailureKind Kind,
    string Path,
    string Message);

public sealed class WorkspaceSharePacketProjectionResult
{
    private WorkspaceSharePacketProjectionResult(
        WorkspaceSharePacket? packet,
        WorkspaceSharePacketProjectionFailure? failure)
    {
        Packet = packet;
        Failure = failure;
    }

    public bool Succeeded => Packet is not null;

    public WorkspaceSharePacket? Packet { get; }

    public WorkspaceSharePacketProjectionFailure? Failure { get; }

    internal static WorkspaceSharePacketProjectionResult Success(WorkspaceSharePacket packet) =>
        new(packet, null);

    internal static WorkspaceSharePacketProjectionResult Failed(
        WorkspaceSharePacketProjectionFailureKind kind,
        string path,
        string message) =>
        new(null, new WorkspaceSharePacketProjectionFailure(kind, path, message));
}

/// <summary>
/// Transposes the bounded URL packet to and from its packet-local definition records.
/// </summary>
public static class WorkspaceSharePacketTransposer
{
    public const string WorkspaceId = "share-workspace";
    public const string NavigationId = "share-navigation";
    public const string ViewId = "share-view";
    public const string ScenarioId = "share-scenario";

    public static WorkspaceSharePacketDefinitionSet ToDefinitions(
        WorkspaceSharePacket packet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceSharePacket canonical = WorkspaceSharePacketCodec.Decode(
            WorkspaceSharePacketCodec.Encode(packet),
            cancellationToken);
        if (canonical.FormatVersion != WorkspaceSharePacketCodec.LegacyFormatVersion)
        {
            throw new ArgumentException(
                "Use ToCommittedDefinitions for workspace share format 2, 3, or 4.",
                nameof(packet));
        }

        return ToDefinitionsCore(canonical, cancellationToken);
    }

    private static WorkspaceSharePacketDefinitionSet ToDefinitionsCore(
        WorkspaceSharePacket canonical,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contexts = new WorkspaceContextDefinition[canonical.Contexts.Count];
        for (int contextIndex = 0; contextIndex < canonical.Contexts.Count; contextIndex++)
        {
            WorkspaceShareContext packetContext = canonical.Contexts[contextIndex];
            WorkspaceShareTab first = canonical.Tabs[packetContext.TabIndexes[0]];
            var members = new List<DefinitionMemberCoordinate>();
            string? subscribe = null;

            foreach (int tabIndex in packetContext.TabIndexes)
            {
                WorkspaceShareTab tab = canonical.Tabs[tabIndex];
                if (tab.SourceKind == WorkspaceShareSourceKind.Group)
                {
                    subscribe = ToSubscription(tab);
                    continue;
                }

                members.Add(new DefinitionMemberCoordinate.PackageCoordinate(
                    tab.Source,
                    tab.Version,
                    tab.Framework,
                    tab.RuntimeIdentifier));
            }

            contexts[contextIndex] = new WorkspaceContextDefinition(
                $"g{contextIndex}",
                first.Framework,
                first.RuntimeIdentifier,
                subscribe,
                members);
        }

        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceId,
            contexts);

        var navigationTabs = new NavigationTabDefinition[canonical.Tabs.Count];
        for (int tabIndex = 0; tabIndex < canonical.Tabs.Count; tabIndex++)
        {
            WorkspaceShareTab tab = canonical.Tabs[tabIndex];
            navigationTabs[tabIndex] = tab.SourceKind == WorkspaceShareSourceKind.Group
                ? new NavigationTabDefinition(
                    $"t{tabIndex}",
                    subscribe: ToSubscription(tab),
                    framework: tab.Framework,
                    runtimeIdentifier: tab.RuntimeIdentifier)
                : new NavigationTabDefinition(
                    $"t{tabIndex}",
                    coordinate: new DefinitionMemberCoordinate.PackageCoordinate(
                        tab.Source,
                        tab.Version,
                        tab.Framework,
                        tab.RuntimeIdentifier));
        }

        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            NavigationId,
            navigationTabs,
            $"t{canonical.ActiveTabIndex}");

        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            ViewId,
            lens: canonical.Lens,
            type: canonical.Type,
            memberAnchor: canonical.MemberAnchor,
            memberSignature: canonical.MemberSignature,
            section: canonical.Section,
            libraries: canonical.Libraries);

        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            ScenarioId,
            workspace: WorkspaceId,
            context: $"g{canonical.SelectedContextIndex}",
            view: ViewId,
            navigation: NavigationId);

        return new WorkspaceSharePacketDefinitionSet(workspace, navigation, view, scenario);
    }

    public static CommittedScenarioDefinitionSet ToCommittedDefinitions(
        WorkspaceSharePacket packet,
        CancellationToken cancellationToken = default) =>
        ToCommittedDefinitionsCore(
            packet,
            queryDescriptors: null,
            cancellationToken);

    public static CommittedScenarioDefinitionSet ToCommittedDefinitions(
        WorkspaceSharePacket packet,
        IEnumerable<PortableQueryDefinitionDescriptor> queryDescriptors,
        CancellationToken cancellationToken = default) =>
        ToCommittedDefinitionsCore(
            packet,
            queryDescriptors
                ?? throw new ArgumentNullException(nameof(queryDescriptors)),
            cancellationToken);

    private static CommittedScenarioDefinitionSet ToCommittedDefinitionsCore(
        WorkspaceSharePacket packet,
        IEnumerable<PortableQueryDefinitionDescriptor>? queryDescriptors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceSharePacket canonical = WorkspaceSharePacketCodec.Decode(
            WorkspaceSharePacketCodec.Encode(packet),
            cancellationToken);
        if (canonical.FormatVersion is not (
            WorkspaceSharePacketCodec.Format2Version
            or WorkspaceSharePacketCodec.CurrentFormatVersion
            or WorkspaceSharePacketCodec.Format4Version
            or WorkspaceSharePacketCodec.Format5Version))
        {
            throw new ArgumentException(
                "Workspace share packet must use format 2, 3, 4, or 5.",
                nameof(packet));
        }

        int schemaVersion = canonical.FormatVersion;
        WorkspaceContextDefinition[] contexts =
            ToWorkspaceContexts(canonical);
        NavigationTabDefinition[] navigationTabs =
            ToNavigationTabs(canonical);
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceId,
            contexts,
            registrations:
                schemaVersion is InspectionDefinitionSchema.Version3
                    or InspectionDefinitionSchema.Version4
                    or InspectionDefinitionSchema.Version5
                    ? canonical.Registrations
                    : null,
            packageSources:
                schemaVersion == InspectionDefinitionSchema.Version5
                    ? canonical.PackageSources
                    : null);
        var navigation = new CommittedNavigationDefinition(
            schemaVersion,
            NavigationId,
            navigationTabs,
            canonical.FocusedTabIndex is int focused
                ? $"t{focused}"
                : null);

        var queries =
            new CommittedQueryDefinition[canonical.Queries.Count];
        for (int index = 0; index < canonical.Queries.Count; index++)
        {
            PortableQueryIdentity identity = canonical.Queries[index];
            queries[index] = new CommittedQueryDefinition(
                schemaVersion,
                $"q{index}",
                identity);
        }

        var states =
            new CommittedViewStateDefinition[canonical.ViewStates.Count];
        for (int index = 0; index < canonical.ViewStates.Count; index++)
        {
            WorkspaceShareViewState state = canonical.ViewStates[index];
            states[index] = new CommittedViewStateDefinition(
                state.TabIndex is int tabIndex ? $"t{tabIndex}" : null,
                state.Subject,
                state.Context,
                state.Facet,
                [
                    .. state.QueryIndexes
                        .Select(queryIndex => $"q{queryIndex}")
                        .Order(StringComparer.Ordinal)
                ],
                state.Libraries);
        }

        var view = new CommittedViewDefinition(
            schemaVersion,
            ViewId,
            states);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            ScenarioId,
            workspace: WorkspaceId,
            context: canonical.SelectedContextIndex is int selected
                ? $"g{selected}"
                : null,
            view: ViewId,
            navigation: NavigationId);

        var registry = new InspectionDefinitionRegistry();
        foreach (PortableQueryDefinitionDescriptor descriptor in
            queryDescriptors ?? [])
        {
            if (descriptor.QueryId == PackageQuery.DefinitionDescriptor.QueryId)
            {
                if (!ReferenceEquals(
                        descriptor,
                        PackageQuery.DefinitionDescriptor))
                {
                    throw new InspectionDefinitionException(
                        $"Portable query descriptor '{descriptor.QueryId}' conflicts "
                            + "with the built-in descriptor.");
                }
                continue;
            }
            registry.AddQueryDescriptor(descriptor);
        }
        registry.Add(workspace);
        registry.Add(navigation);
        foreach (CommittedQueryDefinition query in queries)
            registry.Add(query);
        registry.Add(view);
        registry.Add(scenario);
        return AssertCommitted(
            registry.PreparePacketScenarioWithCancellation(
                ScenarioId,
                cancellationToken),
            schemaVersion);
    }

    /// <summary>
    /// Projects one exact, resolved schema-version-1 Workspace-root state into
    /// a complete schema-version-3 packet.
    /// </summary>
    /// <remarks>
    /// This is not packet canonicalization. The caller supplies resolved
    /// definitions whose package and group coordinates are already pinned;
    /// the result authors a new committed definition set.
    /// </remarks>
    public static WorkspaceSharePacketProjectionResult
        ToCompleteWorkspacePacket(
            WorkspaceSharePacketDefinitionSet resolvedDefinitions,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolvedDefinitions);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceSharePacketProjectionResult? failure =
            ValidateDefinitionSet(
                resolvedDefinitions,
                cancellationToken);
        if (failure is not null)
            return failure;

        ViewDefinition legacyView = resolvedDefinitions.View;
        if (legacyView.Lens is not null
            || legacyView.Type is not null
            || legacyView.MemberAnchor is not null
            || legacyView.MemberSignature is not null
            || legacyView.MemberKey is not null
            || legacyView.Section is not null
            || legacyView.Libraries.Count != 0)
        {
            return NonProjectable(
                "view",
                "Complete Workspace capture requires the Workspace root view.");
        }

        WorkspaceSharePacketProjectionResult effectiveTopology =
            ToPacket(
                resolvedDefinitions,
                cancellationToken,
                canonicalizeFormat1: false,
                WorkspaceSharePacketCodec.MaxFormat3Tabs,
                "Complete Workspace packet");
        if (!effectiveTopology.Succeeded)
            return effectiveTopology;
        WorkspaceSharePacket effectivePacket =
            effectiveTopology.Packet ?? throw new UnreachableException();
        WorkspaceSharePacketDefinitionSet effectiveDefinitions =
            ToDefinitionsCore(effectivePacket, cancellationToken);

        for (int index = 0; index < effectivePacket.Tabs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = $"navigation.tabs[{index}]";
            WorkspaceShareTab effectiveTab = effectivePacket.Tabs[index];
            if (effectiveTab.SourceKind == WorkspaceShareSourceKind.Package)
            {
                if (effectiveTab.Version is null
                    || effectiveTab.Framework is null)
                {
                    return NonProjectable(
                        path + ".coordinate",
                        "Complete Workspace capture requires an exact package version and framework.");
                }
                continue;
            }

            if (effectiveTab.Framework is null)
            {
                return NonProjectable(
                    path,
                    "Complete Workspace capture requires an exact group pin and framework.");
            }
            if (effectiveTab.Version is null)
            {
                return NonProjectable(
                    path + ".subscribe",
                    "Complete Workspace capture requires an exact group pin.");
            }
        }

        WorkspaceDefinition legacyWorkspace = effectiveDefinitions.Workspace;
        NavigationDefinition legacyNavigation =
            effectiveDefinitions.Navigation;
        ScenarioDefinition legacyScenario = effectiveDefinitions.Scenario;
        NavigationTabDefinition focusedTab = legacyNavigation.Tabs.Single(
            tab => string.Equals(
                tab.Id,
                legacyNavigation.Focus,
                StringComparison.Ordinal));
        if (focusedTab.Coordinate
            is not DefinitionMemberCoordinate.PackageCoordinate)
        {
            return NonProjectable(
                "navigation.focus",
                "Complete Workspace capture can preserve focus only on a direct Package coordinate.");
        }

        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version3,
            WorkspaceId,
            legacyWorkspace.Contexts,
            registrations: []);
        var navigation = new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version3,
            NavigationId,
            legacyNavigation.Tabs,
            legacyNavigation.Focus);
        var states =
            new List<CommittedViewStateDefinition>(
                legacyNavigation.Tabs.Count + 1)
            {
                new(
                    navigation: null,
                    subject: new PortableSubjectRequest.Workspace()),
            };
        foreach (NavigationTabDefinition tab in legacyNavigation.Tabs)
        {
            states.Add(
                tab.Coordinate
                    is not DefinitionMemberCoordinate.PackageCoordinate
                        ? new CommittedViewStateDefinition(tab.Id)
                        : string.Equals(
                            tab.Id,
                            legacyNavigation.Focus,
                            StringComparison.Ordinal)
                            ? new CommittedViewStateDefinition(
                                tab.Id,
                                new PortableSubjectRequest.Workspace())
                            : new CommittedViewStateDefinition(
                                tab.Id,
                                new PortableSubjectRequest.Package(),
                                new PortableRetainedSubjectContext.Package()));
        }
        var view = new CommittedViewDefinition(
            InspectionDefinitionSchema.Version3,
            ViewId,
            states);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
            ScenarioId,
            workspace: workspace.Id,
            context: legacyScenario.Context,
            view: view.Id,
            navigation: navigation.Id);

        var registry = new InspectionDefinitionRegistry();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);
        CommittedScenarioDefinitionSet committed = AssertCommitted(
            registry.PreparePacketScenarioWithCancellation(
                ScenarioId,
                cancellationToken),
            InspectionDefinitionSchema.Version3);
        return ToPacket(committed, cancellationToken);
    }

    private static WorkspaceContextDefinition[] ToWorkspaceContexts(
        WorkspaceSharePacket packet)
    {
        var contexts =
            new WorkspaceContextDefinition[packet.Contexts.Count];
        for (int contextIndex = 0;
            contextIndex < packet.Contexts.Count;
            contextIndex++)
        {
            WorkspaceShareContext packetContext =
                packet.Contexts[contextIndex];
            WorkspaceShareTab first =
                packet.Tabs[packetContext.TabIndexes[0]];
            var members = new List<DefinitionMemberCoordinate>();
            string? subscribe = null;

            foreach (int tabIndex in packetContext.TabIndexes)
            {
                WorkspaceShareTab tab = packet.Tabs[tabIndex];
                if (tab.SourceKind == WorkspaceShareSourceKind.Group)
                {
                    subscribe = ToSubscription(tab);
                    continue;
                }

                members.Add(
                    new DefinitionMemberCoordinate.PackageCoordinate(
                        tab.Source,
                        tab.Version,
                        tab.Framework,
                        tab.RuntimeIdentifier));
            }

            contexts[contextIndex] = new WorkspaceContextDefinition(
                $"g{contextIndex}",
                first.Framework,
                first.RuntimeIdentifier,
                subscribe,
                members);
        }

        return contexts;
    }

    private static NavigationTabDefinition[] ToNavigationTabs(
        WorkspaceSharePacket packet)
    {
        var tabs = new NavigationTabDefinition[packet.Tabs.Count];
        for (int tabIndex = 0; tabIndex < packet.Tabs.Count; tabIndex++)
        {
            WorkspaceShareTab tab = packet.Tabs[tabIndex];
            tabs[tabIndex] =
                tab.SourceKind == WorkspaceShareSourceKind.Group
                    ? new NavigationTabDefinition(
                        $"t{tabIndex}",
                        subscribe: ToSubscription(tab),
                        framework: tab.Framework,
                        runtimeIdentifier: tab.RuntimeIdentifier)
                    : new NavigationTabDefinition(
                        $"t{tabIndex}",
                        coordinate:
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                tab.Source,
                                tab.Version,
                                tab.Framework,
                                tab.RuntimeIdentifier));
        }

        return tabs;
    }

    private static CommittedScenarioDefinitionSet AssertCommitted(
        InspectionDefinitionScenarioPreparationResult result,
        int schemaVersion) =>
        (result, schemaVersion) switch
        {
            (InspectionDefinitionScenarioPreparationResult.Version2 version2,
                InspectionDefinitionSchema.Version2) =>
                version2.Definitions,
            (InspectionDefinitionScenarioPreparationResult.Version3 version3,
                InspectionDefinitionSchema.Version3) =>
                version3.Definitions,
            (InspectionDefinitionScenarioPreparationResult.Version4 version4,
                InspectionDefinitionSchema.Version4) =>
                version4.Definitions,
            (InspectionDefinitionScenarioPreparationResult.Version5 version5,
                InspectionDefinitionSchema.Version5) =>
                version5.Definitions,
            _ => throw new UnreachableException(),
        };

    public static WorkspaceSharePacketProjectionResult ToPacket(
        WorkspaceSharePacketDefinitionSet definitions,
        CancellationToken cancellationToken = default) =>
        ToPacket(
            definitions,
            cancellationToken,
            canonicalizeFormat1: true,
            WorkspaceSharePacketCodec.MaxFormat1Tabs,
            "Packet v1");

    private static WorkspaceSharePacketProjectionResult ToPacket(
        WorkspaceSharePacketDefinitionSet definitions,
        CancellationToken cancellationToken,
        bool canonicalizeFormat1,
        int maxTabs,
        string packetName)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceDefinition workspace = definitions.Workspace;
        NavigationDefinition navigation = definitions.Navigation;
        ViewDefinition view = definitions.View;
        ScenarioDefinition scenario = definitions.Scenario;

        WorkspaceSharePacketProjectionResult? failure =
            ValidateDefinitionSet(
                definitions,
                cancellationToken);
        if (failure is not null)
            return failure;

        failure =
            ValidateRecordEnvelope(workspace, navigation, view, scenario);
        if (failure is not null)
            return failure;
        if (workspace.Contexts.Count > WorkspaceSharePacketCodec.MaxContexts)
        {
            return NonProjectable(
                "workspace.contexts",
                $"{packetName} supports at most "
                    + $"{WorkspaceSharePacketCodec.MaxContexts} contexts.");
        }

        var contextSources = new List<IReadOnlyList<SourceTuple>>(workspace.Contexts.Count);
        var allSources = new List<SourceTuple>();
        for (int contextIndex = 0; contextIndex < workspace.Contexts.Count; contextIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceContextDefinition context = workspace.Contexts[contextIndex];
            string path = $"workspace.contexts[{contextIndex}]";

            if (!TryNormalizeFramework(context.Framework, out string? framework))
            {
                return InvalidDefinition(
                    path + ".framework",
                    "A context framework must be valid acquisition-target text.");
            }
            if (!TryNormalizeRuntimeIdentifier(
                context.RuntimeIdentifier,
                out string? runtimeIdentifier))
            {
                return InvalidDefinition(
                    path + ".runtimeIdentifier",
                    "A context runtime identifier must use canonical lowercase target text.");
            }

            var normalizedMembers =
                new List<PackageSourceDeclaration>(context.Members.Count);
            for (int memberIndex = 0; memberIndex < context.Members.Count; memberIndex++)
            {
                if (context.Members[memberIndex]
                    is not DefinitionMemberCoordinate.PackageCoordinate package)
                {
                    return NonProjectable(
                        $"{path}.members[{memberIndex}]",
                        "Packet v1 context members must be package coordinates.");
                }
                if (!PackageCoordinateResolver.IsCanonicalPackageId(package.Id))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}].id",
                        "A packet package member must use a valid NuGet package id.");
                }
                if (!TryNormalizeVersion(package.Version, out string? version))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}].version",
                        "A packet package member must use one exact NuGet version without build metadata.");
                }
                if (!TryNormalizeFramework(
                    package.Framework,
                    out string? memberFramework))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}].framework",
                        "A package member framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    package.RuntimeIdentifier,
                    out string? memberRuntimeIdentifier))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}].runtimeIdentifier",
                        "A package member runtime identifier must use canonical lowercase target text.");
                }

                framework = MergeContextTarget(
                    framework,
                    memberFramework,
                    path + ".framework",
                    out failure);
                if (failure is not null)
                    return failure;

                runtimeIdentifier = MergeContextTarget(
                    runtimeIdentifier,
                    memberRuntimeIdentifier,
                    path + ".runtimeIdentifier",
                    out failure);
                if (failure is not null)
                    return failure;

                normalizedMembers.Add(new PackageSourceDeclaration(
                    package.Id,
                    version,
                    memberFramework,
                    memberRuntimeIdentifier));
            }

            var sources = new List<SourceTuple>();
            if (context.Subscribe is not null)
            {
                if (!TryProjectSubscription(
                    context.Subscribe,
                    path + ".subscribe",
                    out string expression,
                    out string? pin,
                    out failure))
                    return failure!;

                sources.Add(new SourceTuple(
                    WorkspaceShareSourceKind.Group,
                    expression,
                    pin,
                    framework,
                    runtimeIdentifier));
            }

            for (int memberIndex = 0; memberIndex < normalizedMembers.Count; memberIndex++)
            {
                PackageSourceDeclaration member = normalizedMembers[memberIndex];

                if (!MatchesOrInherits(member.Framework, framework)
                    || !MatchesOrInherits(
                        member.RuntimeIdentifier,
                        runtimeIdentifier))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}]",
                        "Every member in a packet context must have one effective framework and runtime identifier.");
                }

                sources.Add(new SourceTuple(
                    WorkspaceShareSourceKind.Package,
                    member.Name,
                    member.Version,
                    framework,
                    runtimeIdentifier));
            }

            if (sources.Count == 0)
                return InvalidDefinition(path, "Packet contexts cannot be empty.");

            contextSources.Add(new ReadOnlyCollection<SourceTuple>(sources));
            foreach (SourceTuple source in sources)
            {
                if (!allSources.Any(candidate => SameSource(candidate, source)))
                    allSources.Add(source);
            }
        }
        if (allSources.Count > maxTabs)
        {
            return NonProjectable(
                "workspace.contexts",
                $"{packetName} supports at most {maxTabs} source tuples.");
        }
        if (navigation.Tabs.Count > maxTabs)
        {
            return NonProjectable(
                "navigation.tabs",
                $"{packetName} supports at most {maxTabs} navigation tabs.");
        }

        var packetTabs = new WorkspaceShareTab[navigation.Tabs.Count];
        var matchedSources = new List<SourceTuple>();
        var navigationTabIds = new HashSet<string>(StringComparer.Ordinal);
        for (int tabIndex = 0; tabIndex < navigation.Tabs.Count; tabIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NavigationTabDefinition tab = navigation.Tabs[tabIndex];
            string path = $"navigation.tabs[{tabIndex}]";
            if (!navigationTabIds.Add(tab.Id))
                return InvalidDefinition(path + ".id", "Navigation tab ids must be unique.");

            SourceSelector selector;
            if (tab.Subscribe is not null)
            {
                if (!TryProjectSubscription(
                    tab.Subscribe,
                    path + ".subscribe",
                    out string expression,
                    out string? pin,
                    out failure))
                    return failure!;
                if (!TryNormalizeFramework(tab.Framework, out string? framework))
                {
                    return InvalidDefinition(
                        path + ".framework",
                        "A group navigation framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    tab.RuntimeIdentifier,
                    out string? runtimeIdentifier))
                {
                    return InvalidDefinition(
                        path + ".runtimeIdentifier",
                        "A group navigation runtime identifier must use canonical lowercase target text.");
                }

                selector = new SourceSelector(
                    WorkspaceShareSourceKind.Group,
                    expression,
                    pin,
                    framework,
                    runtimeIdentifier);
            }
            else
            {
                if (tab.Coordinate
                    is not DefinitionMemberCoordinate.PackageCoordinate coordinate)
                {
                    return NonProjectable(
                        path + ".coordinate",
                        "Packet v1 navigation coordinates must be source-only package coordinates.");
                }
                if (tab.Framework is not null || tab.RuntimeIdentifier is not null)
                {
                    return NonProjectable(
                        path,
                        "Package navigation targets must declare framework and runtime identifier on the coordinate.");
                }
                if (!PackageCoordinateResolver.IsCanonicalPackageId(coordinate.Id))
                {
                    return InvalidDefinition(
                        path + ".coordinate.id",
                        "A packet navigation source must use a valid NuGet package id.");
                }
                if (!TryNormalizeVersion(coordinate.Version, out string? version))
                {
                    return InvalidDefinition(
                        path + ".coordinate.version",
                        "A packet navigation source must use one exact NuGet version without build metadata.");
                }
                if (!TryNormalizeFramework(
                    coordinate.Framework,
                    out string? framework))
                {
                    return InvalidDefinition(
                        path + ".coordinate.framework",
                        "A package navigation framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    coordinate.RuntimeIdentifier,
                    out string? runtimeIdentifier))
                {
                    return InvalidDefinition(
                        path + ".coordinate.runtimeIdentifier",
                        "A package navigation runtime identifier must use canonical lowercase target text.");
                }

                selector = new SourceSelector(
                    WorkspaceShareSourceKind.Package,
                    coordinate.Id,
                    version,
                    framework,
                    runtimeIdentifier);
            }

            SourceTuple[] matches = FindMatches(selector, allSources);
            if (matches.Length != 1)
            {
                return InvalidDefinition(
                    path,
                    matches.Length == 0
                        ? "The navigation source does not match a workspace context source."
                        : "The navigation source is ambiguous across effective context targets.");
            }

            SourceTuple match = matches[0];
            if (matchedSources.Any(source => SameSource(source, match)))
            {
                return InvalidDefinition(
                    path,
                    "Navigation contains a duplicate packet source tuple.");
            }

            matchedSources.Add(match);
            packetTabs[tabIndex] = new WorkspaceShareTab(
                match.Kind,
                match.Name,
                match.Version,
                match.Framework,
                match.RuntimeIdentifier);
        }

        if (matchedSources.Count != allSources.Count)
        {
            return NonProjectable(
                "navigation.tabs",
                "Packet v1 cannot preserve workspace sources omitted from navigation.");
        }

        var packetContexts = new WorkspaceShareContext[contextSources.Count];
        for (int contextIndex = 0; contextIndex < contextSources.Count; contextIndex++)
        {
            IReadOnlyList<SourceTuple> sources = contextSources[contextIndex];
            var indexes = new int[sources.Count];
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                indexes[sourceIndex] = matchedSources.FindIndex(
                    candidate => SameSource(candidate, sources[sourceIndex]));
            }

            for (int previousIndex = 0;
                previousIndex < contextIndex;
                previousIndex++)
            {
                if (packetContexts[previousIndex].TabIndexes.SequenceEqual(indexes))
                {
                    return NonProjectable(
                        $"workspace.contexts[{contextIndex}]",
                        "Packet v1 cannot preserve distinct contexts with identical source composition.");
                }
            }

            packetContexts[contextIndex] = new WorkspaceShareContext(indexes);
        }

        int focusIndex = -1;
        for (int tabIndex = 0; tabIndex < navigation.Tabs.Count; tabIndex++)
        {
            if (string.Equals(
                navigation.Tabs[tabIndex].Id,
                navigation.Focus,
                StringComparison.Ordinal))
            {
                focusIndex = tabIndex;
                break;
            }
        }
        if (focusIndex < 0)
        {
            return InvalidDefinition(
                "navigation.focus",
                "Navigation focus must name one tab.");
        }

        int selectedContextIndex = scenario.Context is null
            ? 0
            : workspace.Contexts
                .Select((context, index) => (context, index))
                .Where(item => string.Equals(
                    item.context.Name,
                    scenario.Context,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .Single();
        if (selectedContextIndex < 0)
        {
            return InvalidDefinition(
                "scenario.context",
                "Scenario context must name one workspace context.");
        }

        string[] libraries = [.. view.Libraries];
        var packet = new WorkspaceSharePacket(
            packetTabs,
            packetContexts,
            focusIndex,
            selectedContextIndex,
            view.Lens,
            view.Type,
            view.MemberAnchor,
            view.MemberSignature,
            view.Section,
            libraries);

        if (!canonicalizeFormat1)
            return WorkspaceSharePacketProjectionResult.Success(packet);

        try
        {
            WorkspaceSharePacket canonical = WorkspaceSharePacketCodec.Decode(
                WorkspaceSharePacketCodec.Encode(packet),
                cancellationToken);
            return WorkspaceSharePacketProjectionResult.Success(canonical);
        }
        catch (WorkspaceSharePacketException ex)
        {
            return ex.Kind is WorkspaceSharePacketFailureKind.EncodedLimitExceeded
                or WorkspaceSharePacketFailureKind.DecodedLimitExceeded
                or WorkspaceSharePacketFailureKind.JsonValueLimitExceeded
                ? NonProjectable("$", ex.Message)
                : InvalidDefinition("$", ex.Message);
        }
    }

    public static WorkspaceSharePacketProjectionResult ToPacket(
        CommittedScenarioDefinitionSet definitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceDefinition? workspace = definitions.Workspace;
        CommittedNavigationDefinition? navigation = definitions.Navigation;
        CommittedViewDefinition? view = definitions.View;
        ScenarioDefinition scenario = definitions.Scenario;
        int schemaVersion = scenario.SchemaVersion;
        string format = schemaVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        if (workspace is null || navigation is null || view is null)
        {
            return InvalidDefinition(
                "scenario",
                $"Workspace share format {format} requires workspace, navigation, and view records.");
        }

        if (schemaVersion is InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5)
        {
            WorkspaceSharePacketProjectionResult? registrationFailure =
                ValidateProjectableRegistrations(workspace, format);
            if (registrationFailure is not null)
                return registrationFailure;
        }

        WorkspaceSharePacketProjectionResult? failure =
            ValidateCommittedDefinitionSet(
                definitions,
                cancellationToken);
        if (failure is not null)
            return failure;

        WorkspaceSharePacket? basis = null;
        if (workspace.Contexts.Count != 0)
        {
            WorkspaceSharePacketProjectionResult topology =
                ProjectCommittedTopology(
                    workspace,
                    navigation,
                    scenario,
                    cancellationToken);
            if (!topology.Succeeded)
                return topology;
            basis = topology.Packet ?? throw new UnreachableException();
        }

        if (workspace.Title is not null || workspace.Description is not null)
        {
            return NonProjectable(
                "workspace",
                $"Packet format {format} cannot preserve workspace presentation text.");
        }
        if (workspace.Groups.Count != 0 || definitions.Catalogs.Count != 0)
        {
            return NonProjectable(
                "workspace.groups",
                $"Packet format {format} cannot preserve authored group declarations.");
        }
        if (scenario.Title is not null || scenario.Description is not null)
        {
            return NonProjectable(
                "scenario",
                $"Packet format {format} cannot preserve scenario presentation text.");
        }

        PortableQueryIdentity[] packetQueries =
        [
            .. definitions.Queries
                .Select(query => query.Identity)
                .Distinct()
                .OrderBy(
                    identity => identity.Vocabulary,
                    StringComparer.Ordinal)
                .ThenBy(
                    identity => identity.Payload,
                    PortableQueryModel.ScalarOrder)
        ];
        if (packetQueries.Length > WorkspaceSharePacketCodec.MaxQueries)
        {
            return NonProjectable(
                "queries",
                $"Packet format {format} supports at most "
                    + $"{WorkspaceSharePacketCodec.MaxQueries} distinct queries.");
        }
        var packetQueryIndexes = packetQueries
            .Select((identity, index) => (identity, index))
            .ToDictionary(item => item.identity, item => item.index);
        Dictionary<string, int> queryIndexesById =
            definitions.Queries.ToDictionary(
                query => query.Id,
                query => packetQueryIndexes[query.Identity],
                StringComparer.Ordinal);

        int? focusedTabIndex = null;
        if (navigation.Focus is not null)
        {
            focusedTabIndex = navigation.Tabs
                .Select((tab, index) => (tab, index))
                .Where(item => string.Equals(
                    item.tab.Id,
                    navigation.Focus,
                    StringComparison.Ordinal))
                .Select(item => (int?)item.index)
                .SingleOrDefault();
            if (focusedTabIndex is null)
            {
                return InvalidDefinition(
                    "navigation.focus",
                    "Navigation focus must name one direct Package tab.");
            }
        }

        var packetStates =
            new WorkspaceShareViewState[view.States.Count];
        for (int index = 0; index < view.States.Count; index++)
        {
            CommittedViewStateDefinition state = view.States[index];
            packetStates[index] = new WorkspaceShareViewState(
                index == 0 ? null : index - 1,
                state.Subject,
                state.Context,
                state.Facet,
                [
                    .. state.Queries
                        .Select(queryId => queryIndexesById[queryId])
                        .Distinct()
                        .Order()
                ],
                [.. state.Libraries]);
        }

        WorkspaceSharePacket packet = schemaVersion switch
        {
            InspectionDefinitionSchema.Version5 =>
                WorkspaceSharePacket.CreateV5(
                    basis is null ? [] : [.. basis.Tabs],
                    basis is null ? [] : [.. basis.Contexts],
                    [.. workspace.Registrations],
                    [.. workspace.PackageSources],
                    focusedTabIndex,
                    basis?.SelectedContextIndex,
                    packetStates,
                    packetQueries),
            InspectionDefinitionSchema.Version4 =>
                WorkspaceSharePacket.CreateV4(
                    basis is null ? [] : [.. basis.Tabs],
                    basis is null ? [] : [.. basis.Contexts],
                    [.. workspace.Registrations],
                    focusedTabIndex,
                    basis?.SelectedContextIndex,
                    packetStates,
                    packetQueries),
            InspectionDefinitionSchema.Version3 =>
                new WorkspaceSharePacket(
                    basis is null ? [] : [.. basis.Tabs],
                    basis is null ? [] : [.. basis.Contexts],
                    [.. workspace.Registrations],
                    focusedTabIndex,
                    basis?.SelectedContextIndex,
                    packetStates,
                    packetQueries),
            _ =>
                new WorkspaceSharePacket(
                    [.. basis!.Tabs],
                    [.. basis.Contexts],
                    focusedTabIndex,
                    basis.SelectedContextIndex
                        ?? throw new UnreachableException(),
                    packetStates,
                    packetQueries),
        };
        try
        {
            WorkspaceSharePacket canonical = WorkspaceSharePacketCodec.Decode(
                WorkspaceSharePacketCodec.Encode(packet),
                cancellationToken);
            return WorkspaceSharePacketProjectionResult.Success(canonical);
        }
        catch (WorkspaceSharePacketException ex)
        {
            return ex.Kind is
                WorkspaceSharePacketFailureKind.EncodedLimitExceeded
                or WorkspaceSharePacketFailureKind.DecodedLimitExceeded
                or WorkspaceSharePacketFailureKind.JsonValueLimitExceeded
                ? NonProjectable("$", ex.Message)
                : InvalidDefinition("$", ex.Message);
        }
    }

    private static WorkspaceSharePacketProjectionResult?
        ValidateCommittedDefinitionSet(
            CommittedScenarioDefinitionSet definitions,
            CancellationToken cancellationToken)
    {
        int schemaVersion = definitions.Scenario.SchemaVersion;
        if (schemaVersion is not (
            InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5))
        {
            return InvalidDefinition(
                "scenario.schemaVersion",
                "Workspace share committed packets require "
                    + "schema-version-2, schema-version-3, schema-version-4, "
                    + "or schema-version-5 records.");
        }

        foreach (InspectionDefinitionRecord record in definitions.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.SchemaVersion != schemaVersion)
            {
                return InvalidDefinition(
                    record.Kind.ToString().ToLowerInvariant()
                        + ".schemaVersion",
                    $"Workspace share format {schemaVersion} requires "
                        + $"schema-version-{schemaVersion} records.");
            }

            try
            {
                InspectionDefinitionJson.ValidatePortableRecord(record);
                if (record is not CommittedViewDefinition)
                    _ = InspectionDefinitionJson.Serialize(record);
            }
            catch (InspectionDefinitionException ex)
            {
                return InvalidDefinition(
                    record.Kind.ToString().ToLowerInvariant(),
                    ex.Message);
            }
        }

        WorkspaceSharePacketProjectionResult? groupFailure =
            ValidateCatalogGroups(
                definitions.Workspace?.Groups
                    ?? Array.Empty<CatalogGroupDefinition>(),
                "workspace.groups",
                cancellationToken);
        if (groupFailure is not null)
            return groupFailure;
        for (int index = 0; index < definitions.Catalogs.Count; index++)
        {
            groupFailure = ValidateCatalogGroups(
                definitions.Catalogs[index].Groups,
                $"catalogs[{index}].groups",
                cancellationToken);
            if (groupFailure is not null)
                return groupFailure;
        }

        var registry = new InspectionDefinitionRegistry();
        foreach (PortableQueryDefinitionDescriptor descriptor in
            definitions.QueryBindings
                .Select(binding => binding.Descriptor)
                .DistinctBy(descriptor => descriptor.QueryId))
        {
            if (descriptor.QueryId == PackageQuery.DefinitionDescriptor.QueryId)
                continue;
            registry.AddQueryDescriptor(descriptor);
        }
        try
        {
            foreach (InspectionDefinitionRecord record in
                definitions.Records)
                registry.Add(record);
            _ = definitions.NavigationTargetMatchMode
                is NavigationTargetMatchMode.Exact
                    ? registry.PreparePacketScenarioWithCancellation(
                        definitions.Scenario.Id,
                        cancellationToken)
                    : registry.PrepareScenarioWithCancellation(
                        definitions.Scenario.Id,
                        cancellationToken);
        }
        catch (InspectionDefinitionException ex)
        {
            return InvalidDefinition("scenario", ex.Message);
        }

        return null;
    }

    private static WorkspaceSharePacketProjectionResult?
        ValidateProjectableRegistrations(
            WorkspaceDefinition workspace,
            string format)
    {
        if (workspace.Registrations.Count
            > WorkspaceSharePacketCodec.MaxRegistrations)
        {
            return NonProjectable(
                "workspace.registrations",
                $"Packet format {format} supports at most "
                    + $"{WorkspaceSharePacketCodec.MaxRegistrations} registrations.");
        }

        for (int index = 0; index < workspace.Registrations.Count; index++)
        {
            string path = $"workspace.registrations[{index}]";
            switch (workspace.Registrations[index])
            {
                case WorkspaceRegistration.ExactLibrary exact:
                    if (exact.Coordinate is not (
                        ExactLibrarySourceCoordinate.Package
                        or ExactLibrarySourceCoordinate.Platform))
                    {
                        return NonProjectable(
                            path + ".coordinate",
                            $"Packet format {format} preserves only Package- or Platform-origin exact Libraries.");
                    }
                    break;
                case WorkspaceRegistration.PackagePrefix:
                    break;
                case WorkspaceRegistration.Ecosystem ecosystem:
                    if (ecosystem.Declaration.IntegrationScanner is not null)
                    {
                        return NonProjectable(
                            path + ".integrationScanner",
                            $"Packet format {format} cannot preserve an Ecosystem integration scanner.");
                    }

                    for (int populationIndex = 0;
                        populationIndex
                            < ecosystem.Declaration.Populations.Length;
                        populationIndex++)
                    {
                        WorkspaceEcosystemPopulationDeclaration population =
                            ecosystem.Declaration.Populations[populationIndex];
                        if (population
                                is WorkspaceEcosystemPopulationDeclaration
                                    .ExactLibrary library
                            && library.Coordinate is not (
                                ExactLibrarySourceCoordinate.Package
                                or ExactLibrarySourceCoordinate.Platform))
                        {
                            return NonProjectable(
                                path + $".populations[{populationIndex}]",
                                $"Packet format {format} preserves only Package- or Platform-origin exact Libraries.");
                        }
                    }
                    break;
                default:
                    return NonProjectable(
                        path,
                        $"Packet format {format} cannot preserve this registration kind.");
            }
        }

        return null;
    }

    private static WorkspaceSharePacketProjectionResult
        ProjectCommittedTopology(
            WorkspaceDefinition workspace,
            CommittedNavigationDefinition navigation,
            ScenarioDefinition scenario,
            CancellationToken cancellationToken)
    {
        string topologyFocus = navigation.Focus ?? navigation.Tabs[0].Id;
        if (navigation.Focus is not null)
        {
            NavigationTabDefinition focusedTab = navigation.Tabs.Single(
                tab => string.Equals(
                    tab.Id,
                    navigation.Focus,
                    StringComparison.Ordinal));
            if (focusedTab.Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate)
            {
                return InvalidDefinition(
                    "navigation.focus",
                    "Committed Workspace share focus must name one direct Package tab.");
            }
        }

        var legacyWorkspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            workspace.Id,
            workspace.Contexts);
        var legacyNavigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            navigation.Id,
            navigation.Tabs,
            topologyFocus);
        var legacyView = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            ViewId);
        var legacyScenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            scenario.Id,
            workspace: legacyWorkspace.Id,
            context: scenario.Context,
            view: legacyView.Id,
            navigation: legacyNavigation.Id);

        return ToPacket(
            new WorkspaceSharePacketDefinitionSet(
                legacyWorkspace,
                legacyNavigation,
                legacyView,
                legacyScenario),
            cancellationToken,
            canonicalizeFormat1: false,
            workspace.SchemaVersion switch
            {
                InspectionDefinitionSchema.Version3 =>
                    WorkspaceSharePacketCodec.MaxFormat3Tabs,
                InspectionDefinitionSchema.Version4 =>
                    WorkspaceSharePacketCodec.MaxFormat4Tabs,
                InspectionDefinitionSchema.Version5 =>
                    WorkspaceSharePacketCodec.MaxFormat5Tabs,
                _ => WorkspaceSharePacketCodec.MaxFormat2Tabs,
            },
            $"Packet format {workspace.SchemaVersion}");
    }

    public static WorkspaceSharePacketProjectionResult ToPacket(
        ShareProjectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ViewFacetId overview = InspectionViewFacetCatalog.Registry
            .GetRequiredDescriptor(
                StructuralSubjectKind.Member,
                ViewFacetRole.MemberOverview)
            .Id;
        if (plan.Facet != overview)
        {
            return NonProjectable(
                "plan.facet",
                $"Packet v1 cannot project member facet '{plan.Facet.Value}'.");
        }
        if (plan.Basis.Source.Package is not { } package)
        {
            return NonProjectable(
                "plan.basis.source",
                "Packet v1 member scenarios require package provenance.");
        }
        if (string.IsNullOrWhiteSpace(plan.Basis.Source.Framework))
        {
            return NonProjectable(
                "plan.basis.source.framework",
                "Packet v1 member scenarios require one resolved target framework.");
        }
        if (plan.Basis.Target.TypeDefinition is not { } type)
        {
            return NonProjectable(
                "plan.basis.target.type",
                "Packet v1 member scenarios require structured metadata type identity.");
        }

        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                package.PackageId,
                package.PackageVersion,
                plan.Basis.Source.Framework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: plan.Basis.Source.Framework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            ViewId,
            lens: "api",
            type: type.ToEscapedFullName(),
            memberAnchor: plan.Basis.Target.Member.Fingerprint,
            libraries: [plan.Basis.Source.LibraryKey]);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
            ScenarioId,
            workspace: workspace.Id,
            context: "g0",
            view: view.Id,
            navigation: navigation.Id);

        return ToPacket(
            new WorkspaceSharePacketDefinitionSet(
                workspace,
                navigation,
                view,
                scenario),
            cancellationToken);
    }

    private static WorkspaceSharePacketProjectionResult? ValidateDefinitionSet(
        WorkspaceSharePacketDefinitionSet definitions,
        CancellationToken cancellationToken)
    {
        (InspectionDefinitionRecord Record, string Path)[] records =
        [
            (definitions.Workspace, "workspace"),
            (definitions.Navigation, "navigation"),
            (definitions.View, "view"),
            (definitions.Scenario, "scenario"),
        ];
        foreach ((InspectionDefinitionRecord record, string path) in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.SchemaVersion
                != InspectionDefinitionSchema.Version1)
            {
                return InvalidDefinition(
                    path + ".schemaVersion",
                    "WorkspaceSharePacket format 1 requires a schema-version-1 definition set.");
            }
            try
            {
                _ = InspectionDefinitionJson.Serialize(record);
            }
            catch (InspectionDefinitionException ex)
            {
                return InvalidDefinition(path, ex.Message);
            }
        }

        WorkspaceDefinition workspace = definitions.Workspace;
        NavigationDefinition navigation = definitions.Navigation;
        ScenarioDefinition scenario = definitions.Scenario;
        if (scenario.Input is not null)
        {
            return InvalidDefinition(
                "scenario.input",
                "Packet scenarios must reference the supplied workspace.");
        }
        if (!string.Equals(scenario.Workspace, workspace.Id, StringComparison.Ordinal))
        {
            return InvalidDefinition(
                "scenario.workspace",
                "Scenario must reference the supplied workspace.");
        }
        if (!string.Equals(
            scenario.Navigation,
            navigation.Id,
            StringComparison.Ordinal))
        {
            return InvalidDefinition(
                "scenario.navigation",
                "Scenario must reference the supplied navigation.");
        }
        if (!string.Equals(
            scenario.View,
            definitions.View.Id,
            StringComparison.Ordinal))
        {
            return InvalidDefinition(
                "scenario.view",
                "Scenario must reference the supplied view.");
        }
        if (scenario.Context is not null
            && workspace.Contexts.All(context => !string.Equals(
                context.Name,
                scenario.Context,
                StringComparison.Ordinal)))
        {
            return InvalidDefinition(
                "scenario.context",
                "Scenario context must name one workspace context.");
        }
        if (scenario.Context is null && workspace.Contexts.Count != 1)
        {
            return InvalidDefinition(
                "scenario.context",
                "A scenario over multiple workspace contexts must select one context.");
        }

        WorkspaceSharePacketProjectionResult? failure =
            ValidateCatalogGroups(
                workspace.Groups,
                "workspace.groups",
                cancellationToken);
        if (failure is not null)
            return failure;

        var allSources = new AuthoredSourceIndex();
        for (int contextIndex = 0;
            contextIndex < workspace.Contexts.Count;
            contextIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceContextDefinition context = workspace.Contexts[contextIndex];
            string path = $"workspace.contexts[{contextIndex}]";
            if (!TryNormalizeFramework(context.Framework, out string? framework))
            {
                return InvalidDefinition(
                    path + ".framework",
                    "A context framework must be valid acquisition-target text.");
            }
            if (!TryNormalizeRuntimeIdentifier(
                context.RuntimeIdentifier,
                out string? runtimeIdentifier))
            {
                return InvalidDefinition(
                    path + ".runtimeIdentifier",
                    "A context runtime identifier must use canonical lowercase target text.");
            }

            var memberTargets =
                new (DefinitionMemberCoordinate Coordinate, string? Framework, string? RuntimeIdentifier)[context.Members.Count];
            for (int memberIndex = 0;
                memberIndex < context.Members.Count;
                memberIndex++)
            {
                DefinitionMemberCoordinate member = context.Members[memberIndex];
                string memberPath = $"{path}.members[{memberIndex}]";
                failure = ValidateCoordinate(member, memberPath);
                if (failure is not null)
                    return failure;

                GetCoordinateTargets(
                    member,
                    out string? memberFramework,
                    out string? memberRuntimeIdentifier);
                framework = MergeContextTarget(
                    framework,
                    memberFramework,
                    path + ".framework",
                    out failure);
                if (failure is not null)
                    return failure;
                runtimeIdentifier = MergeContextTarget(
                    runtimeIdentifier,
                    memberRuntimeIdentifier,
                    path + ".runtimeIdentifier",
                    out failure);
                if (failure is not null)
                    return failure;

                memberTargets[memberIndex] = (
                    member,
                    memberFramework,
                    memberRuntimeIdentifier);
            }

            var contextSourceSet = new HashSet<AuthoredSourceIdentity>();
            if (context.Subscribe is not null)
            {
                if (!TryParseSubscription(
                    context.Subscribe,
                    path + ".subscribe",
                    out ParsedGroupSubscription parsed,
                    out failure))
                {
                    return failure;
                }

                AuthoredSourceIdentity source = AuthoredSourceIdentity.ForGroup(
                    parsed.CanonicalSubscription,
                    framework,
                    runtimeIdentifier);
                _ = contextSourceSet.Add(source);
                _ = allSources.Add(source);
            }

            for (int memberIndex = 0;
                memberIndex < memberTargets.Length;
                memberIndex++)
            {
                var member = memberTargets[memberIndex];
                if (!MatchesOrInherits(member.Framework, framework)
                    || !MatchesOrInherits(
                        member.RuntimeIdentifier,
                        runtimeIdentifier))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}]",
                        "Every member in a packet context must have one effective framework and runtime identifier.");
                }

                AuthoredSourceIdentity source = CreateAuthoredSource(
                    member.Coordinate,
                    framework,
                    runtimeIdentifier);
                if (!contextSourceSet.Add(source))
                {
                    return InvalidDefinition(
                        $"{path}.members[{memberIndex}]",
                        "A workspace context must not repeat one source.");
                }
                _ = allSources.Add(source);
            }
        }

        var selectors = new List<(AuthoredSourceIdentity Selector, string Path)>(
            navigation.Tabs.Count);
        var tabIds = new HashSet<string>(StringComparer.Ordinal);
        for (int tabIndex = 0; tabIndex < navigation.Tabs.Count; tabIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NavigationTabDefinition tab = navigation.Tabs[tabIndex];
            string path = $"navigation.tabs[{tabIndex}]";
            if (!tabIds.Add(tab.Id))
                return InvalidDefinition(path + ".id", "Navigation tab ids must be unique.");
            if (!TryNormalizeFramework(tab.Framework, out string? tabFramework))
            {
                return InvalidDefinition(
                    path + ".framework",
                    "A navigation framework must be valid acquisition-target text.");
            }
            if (!TryNormalizeRuntimeIdentifier(
                tab.RuntimeIdentifier,
                out string? tabRuntimeIdentifier))
            {
                return InvalidDefinition(
                    path + ".runtimeIdentifier",
                    "A navigation runtime identifier must use canonical lowercase target text.");
            }

            if (tab.Subscribe is not null)
            {
                if (!TryParseSubscription(
                    tab.Subscribe,
                    path + ".subscribe",
                    out ParsedGroupSubscription parsed,
                    out failure))
                {
                    return failure;
                }
                selectors.Add((
                    AuthoredSourceIdentity.ForGroup(
                        parsed.CanonicalSubscription,
                        tabFramework,
                        tabRuntimeIdentifier),
                    path));

                continue;
            }

            DefinitionMemberCoordinate coordinate = tab.Coordinate!;
            failure = ValidateCoordinate(coordinate, path + ".coordinate");
            if (failure is not null)
                return failure;
            GetCoordinateTargets(
                coordinate,
                out string? framework,
                out string? runtimeIdentifier);
            framework = MergeNavigationTarget(
                framework,
                tabFramework,
                path + ".framework",
                out failure);
            if (failure is not null)
                return failure;
            runtimeIdentifier = MergeNavigationTarget(
                runtimeIdentifier,
                tabRuntimeIdentifier,
                path + ".runtimeIdentifier",
                out failure);
            if (failure is not null)
                return failure;
            selectors.Add((
                CreateAuthoredSource(
                    coordinate,
                    framework,
                    runtimeIdentifier),
                path));
        }

        int focusMatches = navigation.Tabs.Count(tab => string.Equals(
            tab.Id,
            navigation.Focus,
            StringComparison.Ordinal));
        if (focusMatches != 1)
        {
            return InvalidDefinition(
                "navigation.focus",
                "Navigation focus must name exactly one tab.");
        }

        var matchedSources = new HashSet<AuthoredSourceIdentity>();
        foreach ((AuthoredSourceIdentity selector, string path) in selectors)
        {
            AuthoredSourceMatch match = allSources.Find(selector);
            if (match.Count != 1)
            {
                return InvalidDefinition(
                    path,
                    match.Count == 0
                        ? "The navigation source does not match a workspace context source."
                        : "The navigation source is ambiguous across effective context targets.");
            }
            if (!matchedSources.Add(match.Identity!))
            {
                return InvalidDefinition(
                    path,
                    "Navigation contains a duplicate packet source tuple.");
            }
        }

        return matchedSources.Count == allSources.Count
            ? null
            : NonProjectable(
                "navigation.tabs",
                "Packet v1 cannot preserve workspace sources omitted from navigation.");
    }

    private static WorkspaceSharePacketProjectionResult? ValidateCatalogGroups(
        IReadOnlyList<CatalogGroupDefinition> groups,
        string path,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogGroupDefinition group = groups[groupIndex];
            string groupPath = $"{path}[{groupIndex}]";
            if (!WorkspaceSharePacketCodec.IsGroupName(group.Name.AsSpan()))
            {
                return InvalidDefinition(
                    groupPath + ".name",
                    "A catalog group name is not valid group grammar.");
            }
            if (!names.Add(group.Name))
            {
                return InvalidDefinition(
                    groupPath + ".name",
                    "Sibling catalog group names must be unique.");
            }

            for (int memberIndex = 0;
                memberIndex < group.Members.Count;
                memberIndex++)
            {
                WorkspaceSharePacketProjectionResult? failure =
                    ValidateCoordinate(
                        group.Members[memberIndex],
                        $"{groupPath}.members[{memberIndex}]");
                if (failure is not null)
                    return failure;
            }

            WorkspaceSharePacketProjectionResult? childFailure =
                ValidateCatalogGroups(
                    group.Children,
                    groupPath + ".children",
                    cancellationToken);
            if (childFailure is not null)
                return childFailure;
        }

        return null;
    }

    private static WorkspaceSharePacketProjectionResult? ValidateCoordinate(
        DefinitionMemberCoordinate coordinate,
        string path)
    {
        switch (coordinate)
        {
            case DefinitionMemberCoordinate.PackageCoordinate package:
                if (!PackageCoordinateResolver.IsCanonicalPackageId(package.Id))
                {
                    return InvalidDefinition(
                        path + ".id",
                        "A package coordinate must use a valid NuGet package id.");
                }
                if (!TryNormalizeVersion(package.Version, out _))
                {
                    return InvalidDefinition(
                        path + ".version",
                        "A package coordinate must use one exact NuGet version without build metadata.");
                }
                if (!TryNormalizeFramework(package.Framework, out _))
                {
                    return InvalidDefinition(
                        path + ".framework",
                        "A package framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    package.RuntimeIdentifier,
                    out _))
                {
                    return InvalidDefinition(
                        path + ".runtimeIdentifier",
                        "A package runtime identifier must use canonical lowercase target text.");
                }
                break;
            case DefinitionMemberCoordinate.PlatformCoordinate platform:
                if (!RealizedMemberCoordinate.IsCanonicalPlatformFamily(
                    platform.Family))
                {
                    return InvalidDefinition(
                        path + ".family",
                        "A platform family must be 'runtime' or 'aspnetcore'.");
                }
                if (!TryNormalizeVersion(platform.Version, out _))
                {
                    return InvalidDefinition(
                        path + ".version",
                        "A platform coordinate must use one exact version without build metadata.");
                }
                if (!TryNormalizeFramework(platform.Framework, out _))
                {
                    return InvalidDefinition(
                        path + ".framework",
                        "A platform framework must be valid acquisition-target text.");
                }
                if (platform.Assembly is not null
                    && !RealizedMemberCoordinate.IsAssemblySimpleName(
                        platform.Assembly))
                {
                    return InvalidDefinition(
                        path + ".assembly",
                        "A platform assembly must be an assembly simple name.");
                }
                break;
            case DefinitionMemberCoordinate.ProjectCoordinate project:
                if (!TryNormalizeFramework(project.Framework, out _))
                {
                    return InvalidDefinition(
                        path + ".framework",
                        "A project framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    project.RuntimeIdentifier,
                    out _))
                {
                    return InvalidDefinition(
                        path + ".runtimeIdentifier",
                        "A project runtime identifier must use canonical lowercase target text.");
                }
                break;
            case DefinitionMemberCoordinate.DirectoryCoordinate directory:
                if (!TryNormalizeFramework(directory.Framework, out _))
                {
                    return InvalidDefinition(
                        path + ".framework",
                        "A directory framework must be valid acquisition-target text.");
                }
                if (!TryNormalizeRuntimeIdentifier(
                    directory.RuntimeIdentifier,
                    out _))
                {
                    return InvalidDefinition(
                        path + ".runtimeIdentifier",
                        "A directory runtime identifier must use canonical lowercase target text.");
                }
                break;
            case DefinitionMemberCoordinate.EmbeddedCoordinate embedded:
                if (!RealizedMemberCoordinate.IsCanonicalContentRef(
                    embedded.ContentRef))
                {
                    return InvalidDefinition(
                        path + ".contentRef",
                        "An embedded content reference must use canonical bundle-relative syntax.");
                }
                if (!RealizedMemberCoordinate.IsCanonicalDigest(
                    embedded.Digest))
                {
                    return InvalidDefinition(
                        path + ".digest",
                        "An embedded digest must be canonical lowercase hexadecimal SHA-256.");
                }
                if (!RealizedMemberCoordinate.IsAssemblySimpleName(
                    embedded.DeclaredName))
                {
                    return InvalidDefinition(
                        path + ".declaredName",
                        "An embedded declared name must be an assembly simple name.");
                }
                break;
            case DefinitionMemberCoordinate.LocalCoordinate:
                break;
            default:
                return InvalidDefinition(
                    path,
                    "The coordinate kind is not a known definition coordinate.");
        }

        return null;
    }

    private static void GetCoordinateTargets(
        DefinitionMemberCoordinate coordinate,
        out string? framework,
        out string? runtimeIdentifier)
    {
        string? declaredFramework;
        string? declaredRuntimeIdentifier;
        switch (coordinate)
        {
            case DefinitionMemberCoordinate.PackageCoordinate package:
                declaredFramework = package.Framework;
                declaredRuntimeIdentifier = package.RuntimeIdentifier;
                break;
            case DefinitionMemberCoordinate.PlatformCoordinate platform:
                declaredFramework = platform.Framework;
                declaredRuntimeIdentifier = null;
                break;
            case DefinitionMemberCoordinate.ProjectCoordinate project:
                declaredFramework = project.Framework;
                declaredRuntimeIdentifier = project.RuntimeIdentifier;
                break;
            case DefinitionMemberCoordinate.DirectoryCoordinate directory:
                declaredFramework = directory.Framework;
                declaredRuntimeIdentifier = directory.RuntimeIdentifier;
                break;
            default:
                declaredFramework = null;
                declaredRuntimeIdentifier = null;
                break;
        }

        _ = TryNormalizeFramework(declaredFramework, out framework);
        _ = TryNormalizeRuntimeIdentifier(
            declaredRuntimeIdentifier,
            out runtimeIdentifier);
    }

    private static AuthoredSourceIdentity CreateAuthoredSource(
        DefinitionMemberCoordinate coordinate,
        string? framework,
        string? runtimeIdentifier)
    {
        switch (coordinate)
        {
            case DefinitionMemberCoordinate.PackageCoordinate package:
                _ = TryNormalizeVersion(package.Version, out string? packageVersion);
                return new AuthoredSourceIdentity(
                    "package",
                    package.Id.ToLowerInvariant(),
                    packageVersion,
                    null,
                    null,
                    framework,
                    runtimeIdentifier);
            case DefinitionMemberCoordinate.PlatformCoordinate platform:
                _ = TryNormalizeVersion(platform.Version, out string? platformVersion);
                return new AuthoredSourceIdentity(
                    "platform",
                    platform.Family,
                    platform.Assembly,
                    platformVersion,
                    null,
                    framework,
                    runtimeIdentifier);
            case DefinitionMemberCoordinate.EmbeddedCoordinate embedded:
                return new AuthoredSourceIdentity(
                    "embedded",
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName,
                    null,
                    framework,
                    runtimeIdentifier);
            case DefinitionMemberCoordinate.ProjectCoordinate project:
                return new AuthoredSourceIdentity(
                    "project",
                    project.Path,
                    null,
                    null,
                    null,
                    framework,
                    runtimeIdentifier);
            case DefinitionMemberCoordinate.LocalCoordinate local:
                return new AuthoredSourceIdentity(
                    "local",
                    local.Path,
                    null,
                    null,
                    null,
                    framework,
                    runtimeIdentifier);
            case DefinitionMemberCoordinate.DirectoryCoordinate directory:
                return new AuthoredSourceIdentity(
                    "directory",
                    directory.Path,
                    null,
                    null,
                    null,
                    framework,
                    runtimeIdentifier);
            default:
                throw new UnreachableException();
        }
    }

    private static WorkspaceSharePacketProjectionResult? ValidateRecordEnvelope(
        WorkspaceDefinition workspace,
        NavigationDefinition navigation,
        ViewDefinition view,
        ScenarioDefinition scenario)
    {
        if (workspace.Title is not null || workspace.Description is not null)
            return NonProjectable("workspace", "Packet v1 cannot preserve workspace presentation text.");
        if (workspace.Groups.Count != 0)
            return NonProjectable("workspace.groups", "Packet v1 cannot preserve document-local group declarations.");
        if (view.MemberKey is not null)
            return NonProjectable("view.memberKey", "Packet v1 preserves member anchors or signatures, not member keys.");

        if (scenario.Title is not null || scenario.Description is not null)
            return NonProjectable("scenario", "Packet v1 cannot preserve scenario presentation text.");
        if (scenario.Query is not null)
            return NonProjectable("scenario.query", "Packet v1 cannot preserve an authored query preset reference.");
        if (scenario.Input is not null)
            return InvalidDefinition("scenario.input", "Packet scenarios must reference the supplied workspace.");
        if (!string.Equals(scenario.Workspace, workspace.Id, StringComparison.Ordinal))
            return InvalidDefinition("scenario.workspace", "Scenario must reference the supplied workspace.");
        if (!string.Equals(scenario.Navigation, navigation.Id, StringComparison.Ordinal))
            return InvalidDefinition("scenario.navigation", "Scenario must reference the supplied navigation.");
        if (!string.Equals(scenario.View, view.Id, StringComparison.Ordinal))
            return InvalidDefinition("scenario.view", "Scenario must reference the supplied view.");
        return null;
    }

    private static string? MergeContextTarget(
        string? contextValue,
        string? memberValue,
        string path,
        out WorkspaceSharePacketProjectionResult? failure)
    {
        failure = null;
        if (contextValue is null)
            return memberValue;
        if (memberValue is null || string.Equals(contextValue, memberValue, StringComparison.Ordinal))
            return contextValue;

        failure = InvalidDefinition(
            path,
            "Context and package member target declarations disagree.");
        return null;
    }

    private static string? MergeNavigationTarget(
        string? coordinateValue,
        string? tabValue,
        string path,
        out WorkspaceSharePacketProjectionResult? failure)
    {
        failure = null;
        if (coordinateValue is null)
            return tabValue;
        if (tabValue is null
            || string.Equals(coordinateValue, tabValue, StringComparison.Ordinal))
        {
            return coordinateValue;
        }

        failure = InvalidDefinition(
            path,
            "Coordinate and navigation tab target declarations disagree.");
        return null;
    }

    private static bool MatchesOrInherits(string? declared, string? effective) =>
        declared is null || string.Equals(declared, effective, StringComparison.Ordinal);

    private static SourceTuple[] FindMatches(
        SourceSelector selector,
        IReadOnlyList<SourceTuple> sources)
    {
        SourceTuple[] matches = sources
            .Where(source => Matches(selector, source))
            .ToArray();
        if (matches.Length <= 1
            || (selector.Framework is not null
                && selector.RuntimeIdentifier is not null))
        {
            return matches;
        }

        SourceTuple[] exactNullMatches = matches
            .Where(source =>
                (selector.Framework is not null || source.Framework is null)
                && (selector.RuntimeIdentifier is not null
                    || source.RuntimeIdentifier is null))
            .ToArray();
        return exactNullMatches.Length == 0 ? matches : exactNullMatches;
    }

    private static bool Matches(SourceSelector selector, SourceTuple source) =>
        selector.Kind == source.Kind
        && SameName(selector.Kind, selector.Name, source.Name)
        && string.Equals(selector.Version, source.Version, StringComparison.Ordinal)
        && (selector.Framework is null
            || string.Equals(selector.Framework, source.Framework, StringComparison.Ordinal))
        && (selector.RuntimeIdentifier is null
            || string.Equals(
                selector.RuntimeIdentifier,
                source.RuntimeIdentifier,
                StringComparison.Ordinal));

    private static bool SameSource(SourceTuple left, SourceTuple right) =>
        left.Kind == right.Kind
        && SameName(left.Kind, left.Name, right.Name)
        && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && string.Equals(left.Framework, right.Framework, StringComparison.Ordinal)
        && string.Equals(
            left.RuntimeIdentifier,
            right.RuntimeIdentifier,
            StringComparison.Ordinal);

    private static bool SameName(
        WorkspaceShareSourceKind kind,
        string left,
        string right) =>
        string.Equals(
            left,
            right,
            kind == WorkspaceShareSourceKind.Package
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string ToSubscription(WorkspaceShareTab tab)
    {
        if (tab.Version is null)
            return tab.Source;

        int separator = tab.Source.AsSpan(1).IndexOfAny(':', '+');
        separator = separator < 0 ? -1 : separator + 1;
        return separator < 0
            ? $"{tab.Source}@{tab.Version}"
            : string.Concat(
                tab.Source.AsSpan(0, separator),
                "@",
                tab.Version,
                tab.Source.AsSpan(separator));
    }

    private static bool TryProjectSubscription(
        string subscription,
        string path,
        out string expression,
        out string? pin,
        out WorkspaceSharePacketProjectionResult? failure)
    {
        expression = "";
        pin = null;
        if (!TryParseSubscription(
            subscription,
            path,
            out ParsedGroupSubscription parsed,
            out failure))
        {
            return false;
        }

        expression = parsed.Expression;
        if (parsed.Pins.Count == 0)
            return true;
        if (parsed.Pins.Count > 1)
        {
            failure = NonProjectable(
                path,
                "Packet v1 cannot preserve multiple group pins.");
            return false;
        }

        NormalizedGroupPin groupPin = parsed.Pins[0];
        if (groupPin.SegmentIndex != 0)
        {
            failure = NonProjectable(
                path,
                "Packet v1 can preserve only a pin on the base group segment.");
            return false;
        }

        int baseEnd = expression.AsSpan(1).IndexOfAny(':', '+');
        ReadOnlySpan<char> baseName = baseEnd < 0
            ? expression.AsSpan(1)
            : expression.AsSpan(1, baseEnd);
        if (!baseName.SequenceEqual("Platform"))
        {
            failure = NonProjectable(
                path,
                "Packet v1 can preserve a group pin only on a Platform base.");
            return false;
        }

        pin = groupPin.Version;
        return true;
    }

    private static bool TryParseSubscription(
        string subscription,
        string path,
        out ParsedGroupSubscription parsed,
        out WorkspaceSharePacketProjectionResult? failure)
    {
        parsed = default!;
        failure = null;
        if (!WorkspaceSharePacketCodec.TryParseGroupExpression(
            subscription,
            out IReadOnlyList<GroupExpressionPin> pins))
        {
            failure = InvalidDefinition(
                path,
                "The subscription is not a valid group expression.");
            return false;
        }

        var normalizedPins = new NormalizedGroupPin[pins.Count];
        var expression = new StringBuilder(subscription.Length);
        var canonical = new StringBuilder(subscription.Length);
        int cursor = 0;
        for (int index = 0; index < pins.Count; index++)
        {
            GroupExpressionPin syntax = pins[index];
            string versionText = subscription.Substring(
                syntax.ValueStart,
                syntax.ValueLength);
            if (!TryNormalizeVersion(versionText, out string? version))
            {
                failure = InvalidDefinition(
                    path,
                    "A group pin must be one exact NuGet version without build metadata.");
                return false;
            }

            expression.Append(
                subscription.AsSpan(
                    cursor,
                    syntax.SeparatorIndex - cursor));
            canonical.Append(
                subscription.AsSpan(
                    cursor,
                    syntax.ValueStart - cursor));
            canonical.Append(version);
            cursor = syntax.ValueStart + syntax.ValueLength;
            normalizedPins[index] = new NormalizedGroupPin(
                syntax.SegmentIndex,
                version!);
        }

        expression.Append(subscription.AsSpan(cursor));
        canonical.Append(subscription.AsSpan(cursor));
        parsed = new ParsedGroupSubscription(
            expression.ToString(),
            canonical.ToString(),
            new ReadOnlyCollection<NormalizedGroupPin>(normalizedPins));
        return true;
    }

    private static bool TryNormalizeVersion(
        string? value,
        out string? normalized)
    {
        normalized = value;
        if (value is null)
            return true;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(value, out NuGetVersion? version))
        {
            return false;
        }

        normalized = version.ToNormalizedString().ToLowerInvariant();
        return true;
    }

    private static bool TryNormalizeFramework(
        string? value,
        out string? normalized)
    {
        normalized = value;
        if (value is null)
            return true;
        if (!PackageCoordinateResolver.IsAcquisitionTargetText(value))
            return false;

        normalized = value.ToLowerInvariant();
        return true;
    }

    private static bool TryNormalizeRuntimeIdentifier(
        string? value,
        out string? normalized)
    {
        normalized = value;
        return value is null
            || PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(value);
    }

    private static WorkspaceSharePacketProjectionResult NonProjectable(
        string path,
        string message) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            path,
            message);

    private static WorkspaceSharePacketProjectionResult InvalidDefinition(
        string path,
        string message) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet,
            path,
            message);

    private sealed record PackageSourceDeclaration(
        string Name,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier);

    private sealed record ParsedGroupSubscription(
        string Expression,
        string CanonicalSubscription,
        IReadOnlyList<NormalizedGroupPin> Pins);

    private sealed record NormalizedGroupPin(
        int SegmentIndex,
        string Version);

    private sealed record AuthoredSourceIdentity(
        string Kind,
        string Primary,
        string? Secondary,
        string? Tertiary,
        string? Quaternary,
        string? Framework,
        string? RuntimeIdentifier)
    {
        public static AuthoredSourceIdentity ForGroup(
            string subscription,
            string? framework,
            string? runtimeIdentifier) =>
            new(
                "group",
                subscription,
                null,
                null,
                null,
                framework,
                runtimeIdentifier);
    }

    private readonly record struct AuthoredSourceCore(
        string Kind,
        string Primary,
        string? Secondary,
        string? Tertiary,
        string? Quaternary)
    {
        public static AuthoredSourceCore From(AuthoredSourceIdentity source) =>
            new(
                source.Kind,
                source.Primary,
                source.Secondary,
                source.Tertiary,
                source.Quaternary);
    }

    private readonly record struct AuthoredTarget(
        string? Framework,
        string? RuntimeIdentifier);

    private readonly record struct NullableTarget(string? Value);

    private readonly record struct AuthoredSourceMatch(
        int Count,
        AuthoredSourceIdentity? Identity);

    private sealed class AuthoredSourceIndex
    {
        private readonly HashSet<AuthoredSourceIdentity> _sources = [];
        private readonly Dictionary<AuthoredSourceCore, TargetBucket> _byCore =
            [];

        public int Count => _sources.Count;

        public bool Add(AuthoredSourceIdentity source)
        {
            if (!_sources.Add(source))
                return false;

            AuthoredSourceCore core = AuthoredSourceCore.From(source);
            if (!_byCore.TryGetValue(core, out TargetBucket? bucket))
            {
                bucket = new TargetBucket();
                _byCore.Add(core, bucket);
            }

            bucket.Add(source);
            return true;
        }

        public AuthoredSourceMatch Find(AuthoredSourceIdentity selector)
        {
            if (!_byCore.TryGetValue(
                AuthoredSourceCore.From(selector),
                out TargetBucket? bucket))
            {
                return default;
            }

            return bucket.Find(
                new AuthoredTarget(
                    selector.Framework,
                    selector.RuntimeIdentifier));
        }
    }

    private sealed class TargetBucket
    {
        private readonly Dictionary<AuthoredTarget, AuthoredSourceIdentity>
            _exact = [];
        private readonly Dictionary<NullableTarget, MatchAccumulator>
            _byFramework = [];
        private readonly Dictionary<NullableTarget, MatchAccumulator>
            _byRuntimeIdentifier = [];
        private readonly MatchAccumulator _all = new();

        public void Add(AuthoredSourceIdentity source)
        {
            var target = new AuthoredTarget(
                source.Framework,
                source.RuntimeIdentifier);
            _exact.Add(target, source);
            Add(_byFramework, new NullableTarget(source.Framework), source);
            Add(
                _byRuntimeIdentifier,
                new NullableTarget(source.RuntimeIdentifier),
                source);
            _all.Add(source);
        }

        public AuthoredSourceMatch Find(AuthoredTarget selector)
        {
            if (_exact.TryGetValue(
                selector,
                out AuthoredSourceIdentity? exact))
            {
                return new AuthoredSourceMatch(1, exact);
            }
            if (selector.Framework is not null
                && selector.RuntimeIdentifier is not null)
            {
                return default;
            }
            if (selector.Framework is not null)
            {
                return Find(
                    _byFramework,
                    new NullableTarget(selector.Framework));
            }
            if (selector.RuntimeIdentifier is not null)
            {
                return Find(
                    _byRuntimeIdentifier,
                    new NullableTarget(selector.RuntimeIdentifier));
            }

            return _all.Snapshot();
        }

        private static void Add(
            Dictionary<NullableTarget, MatchAccumulator> index,
            NullableTarget key,
            AuthoredSourceIdentity source)
        {
            if (!index.TryGetValue(key, out MatchAccumulator? accumulator))
            {
                accumulator = new MatchAccumulator();
                index.Add(key, accumulator);
            }

            accumulator.Add(source);
        }

        private static AuthoredSourceMatch Find(
            Dictionary<NullableTarget, MatchAccumulator> index,
            NullableTarget key) =>
            index.TryGetValue(key, out MatchAccumulator? accumulator)
                ? accumulator.Snapshot()
                : default;
    }

    private sealed class MatchAccumulator
    {
        private int _count;
        private AuthoredSourceIdentity? _identity;

        public void Add(AuthoredSourceIdentity source)
        {
            _count++;
            _identity ??= source;
        }

        public AuthoredSourceMatch Snapshot() =>
            new(_count, _count == 1 ? _identity : null);
    }

    private sealed record SourceTuple(
        WorkspaceShareSourceKind Kind,
        string Name,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier);

    private sealed record SourceSelector(
        WorkspaceShareSourceKind Kind,
        string Name,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier);
}
