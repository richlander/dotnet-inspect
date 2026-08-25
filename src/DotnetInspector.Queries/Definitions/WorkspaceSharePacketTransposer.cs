using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using DotnetInspector.Packages;
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
            InspectionDefinitionJson.CurrentSchemaVersion,
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
            InspectionDefinitionJson.CurrentSchemaVersion,
            NavigationId,
            navigationTabs,
            $"t{canonical.ActiveTabIndex}");

        var view = new ViewDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            ViewId,
            lens: canonical.Lens,
            type: canonical.Type,
            memberAnchor: canonical.MemberAnchor,
            memberSignature: canonical.MemberSignature,
            section: canonical.Section,
            libraries: canonical.Libraries);

        var scenario = new ScenarioDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            ScenarioId,
            workspace: WorkspaceId,
            context: $"g{canonical.SelectedContextIndex}",
            view: ViewId,
            navigation: NavigationId);

        return new WorkspaceSharePacketDefinitionSet(workspace, navigation, view, scenario);
    }

    public static WorkspaceSharePacketProjectionResult ToPacket(
        WorkspaceSharePacketDefinitionSet definitions,
        CancellationToken cancellationToken = default)
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
                $"Packet v1 supports at most {WorkspaceSharePacketCodec.MaxContexts} contexts.");
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
        if (allSources.Count > WorkspaceSharePacketCodec.MaxTabs)
        {
            return NonProjectable(
                "workspace.contexts",
                $"Packet v1 supports at most {WorkspaceSharePacketCodec.MaxTabs} source tuples.");
        }
        if (navigation.Tabs.Count > WorkspaceSharePacketCodec.MaxTabs)
        {
            return NonProjectable(
                "navigation.tabs",
                $"Packet v1 supports at most {WorkspaceSharePacketCodec.MaxTabs} navigation tabs.");
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

        int selectedContextIndex = workspace.Contexts
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

            var sources = new List<AuthoredSourceIdentity>();
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

                sources.Add(AuthoredSourceIdentity.ForGroup(
                    parsed.CanonicalSubscription,
                    framework,
                    runtimeIdentifier));
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

                sources.Add(CreateAuthoredSource(
                    member.Coordinate,
                    framework,
                    runtimeIdentifier));
            }

            var contextSourceSet = new HashSet<AuthoredSourceIdentity>();
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                AuthoredSourceIdentity source = sources[sourceIndex];
                if (!contextSourceSet.Add(source))
                {
                    return InvalidDefinition(
                        $"{path}.members[{sourceIndex}]",
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
        if (scenario.Context is null)
            return NonProjectable("scenario.context", "Packet scenarios require an explicit selected context.");

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
