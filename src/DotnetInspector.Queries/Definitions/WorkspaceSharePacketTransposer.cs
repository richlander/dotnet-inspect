using System.Collections.ObjectModel;

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
            ValidateRecordEnvelope(workspace, navigation, view, scenario);
        if (failure is not null)
            return failure;

        var contextSources = new List<IReadOnlyList<SourceTuple>>(workspace.Contexts.Count);
        var allSources = new List<SourceTuple>();
        for (int contextIndex = 0; contextIndex < workspace.Contexts.Count; contextIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceContextDefinition context = workspace.Contexts[contextIndex];
            string path = $"workspace.contexts[{contextIndex}]";

            string? framework = context.Framework;
            string? runtimeIdentifier = context.RuntimeIdentifier;

            foreach (DefinitionMemberCoordinate member in context.Members)
            {
                if (member is not DefinitionMemberCoordinate.PackageCoordinate package)
                {
                    return Failed(
                        path + ".members",
                        "Packet v1 context members must be package coordinates.");
                }

                framework = MergeContextTarget(
                    framework,
                    package.Framework,
                    path + ".framework",
                    out failure);
                if (failure is not null)
                    return failure;

                runtimeIdentifier = MergeContextTarget(
                    runtimeIdentifier,
                    package.RuntimeIdentifier,
                    path + ".runtimeIdentifier",
                    out failure);
                if (failure is not null)
                    return failure;
            }

            var sources = new List<SourceTuple>();
            if (context.Subscribe is not null)
            {
                if (!TryParseSubscription(
                    context.Subscribe,
                    out string expression,
                    out string? pin))
                {
                    return Failed(
                        path + ".subscribe",
                        "Only an unpinned group expression or a base-pinned Platform expression fits packet v1.");
                }

                sources.Add(new SourceTuple(
                    WorkspaceShareSourceKind.Group,
                    expression,
                    pin,
                    framework,
                    runtimeIdentifier));
            }

            for (int memberIndex = 0; memberIndex < context.Members.Count; memberIndex++)
            {
                if (context.Members[memberIndex]
                    is not DefinitionMemberCoordinate.PackageCoordinate member)
                {
                    return Failed(
                        $"{path}.members[{memberIndex}]",
                        "Packet v1 context members must be package coordinates.");
                }

                if (!MatchesOrInherits(member.Framework, framework)
                    || !MatchesOrInherits(
                        member.RuntimeIdentifier,
                        runtimeIdentifier))
                {
                    return Failed(
                        $"{path}.members[{memberIndex}]",
                        "Every member in a packet context must have one effective framework and runtime identifier.");
                }

                sources.Add(new SourceTuple(
                    WorkspaceShareSourceKind.Package,
                    member.Id,
                    member.Version,
                    framework,
                    runtimeIdentifier));
            }

            if (sources.Count == 0)
                return Failed(path, "Packet contexts cannot be empty.");

            contextSources.Add(new ReadOnlyCollection<SourceTuple>(sources));
            foreach (SourceTuple source in sources)
            {
                if (!allSources.Any(candidate => SameSource(candidate, source)))
                    allSources.Add(source);
            }
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
                return Failed(path + ".id", "Navigation tab ids must be unique.");

            SourceSelector selector;
            if (tab.Subscribe is not null)
            {
                if (!TryParseSubscription(
                    tab.Subscribe,
                    out string expression,
                    out string? pin))
                {
                    return Failed(
                        path + ".subscribe",
                        "Only an unpinned group expression or a base-pinned Platform expression fits packet v1.");
                }

                selector = new SourceSelector(
                    WorkspaceShareSourceKind.Group,
                    expression,
                    pin,
                    tab.Framework,
                    tab.RuntimeIdentifier);
            }
            else
            {
                if (tab.Coordinate
                    is not DefinitionMemberCoordinate.PackageCoordinate coordinate)
                {
                    return Failed(
                        path + ".coordinate",
                        "Packet v1 navigation coordinates must be source-only package coordinates.");
                }
                if (tab.Framework is not null || tab.RuntimeIdentifier is not null)
                {
                    return Failed(
                        path,
                        "Package navigation targets must declare framework and runtime identifier on the coordinate.");
                }

                selector = new SourceSelector(
                    WorkspaceShareSourceKind.Package,
                    coordinate.Id,
                    coordinate.Version,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier);
            }

            SourceTuple[] matches = allSources
                .Where(source => Matches(selector, source))
                .ToArray();
            if (matches.Length != 1)
            {
                return Failed(
                    path,
                    matches.Length == 0
                        ? "The navigation source does not match a workspace context source."
                        : "The navigation source is ambiguous across effective context targets.");
            }

            SourceTuple match = matches[0];
            if (matchedSources.Any(source => SameSource(source, match)))
                return Failed(path, "Navigation contains a duplicate packet source tuple.");

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
            return Failed(
                "navigation.tabs",
                "Every workspace context source must have one navigation tab.");
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
            return Failed("navigation.focus", "Navigation focus must name one tab.");

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
            return Failed("scenario.context", "Scenario context must name one workspace context.");

        string[] libraries = view.Libraries
            .OrderBy(static library => library, StringComparer.Ordinal)
            .ToArray();
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
            return WorkspaceSharePacketProjectionResult.Failed(
                WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet,
                "$",
                ex.Message);
        }
    }

    private static WorkspaceSharePacketProjectionResult? ValidateRecordEnvelope(
        WorkspaceDefinition workspace,
        NavigationDefinition navigation,
        ViewDefinition view,
        ScenarioDefinition scenario)
    {
        if (workspace.Title is not null || workspace.Description is not null)
            return Failed("workspace", "Packet v1 cannot preserve workspace presentation text.");
        if (workspace.Groups.Count != 0)
            return Failed("workspace.groups", "Packet v1 cannot preserve document-local group declarations.");
        if (view.MemberKey is not null)
            return Failed("view.memberKey", "Packet v1 preserves member anchors or signatures, not member keys.");

        if (scenario.Title is not null || scenario.Description is not null)
            return Failed("scenario", "Packet v1 cannot preserve scenario presentation text.");
        if (scenario.Query is not null)
            return Failed("scenario.query", "Packet v1 cannot preserve an authored query preset reference.");
        if (scenario.Input is not null)
            return Failed("scenario.input", "Packet scenarios must reference the supplied workspace.");
        if (!string.Equals(scenario.Workspace, workspace.Id, StringComparison.Ordinal))
            return Failed("scenario.workspace", "Scenario must reference the supplied workspace.");
        if (!string.Equals(scenario.Navigation, navigation.Id, StringComparison.Ordinal))
            return Failed("scenario.navigation", "Scenario must reference the supplied navigation.");
        if (!string.Equals(scenario.View, view.Id, StringComparison.Ordinal))
            return Failed("scenario.view", "Scenario must reference the supplied view.");
        if (scenario.Context is null)
            return Failed("scenario.context", "Packet scenarios require an explicit selected context.");

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

        failure = Failed(
            path,
            "Context and package member target declarations disagree.");
        return null;
    }

    private static bool MatchesOrInherits(string? declared, string? effective) =>
        declared is null || string.Equals(declared, effective, StringComparison.Ordinal);

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

    private static bool TryParseSubscription(
        string subscription,
        out string expression,
        out string? pin)
    {
        expression = subscription;
        pin = null;

        int pinSeparator = subscription.IndexOf('@');
        if (pinSeparator < 0)
            return true;
        if (subscription.IndexOf('@', pinSeparator + 1) >= 0)
            return false;

        int baseEnd = subscription.AsSpan(1).IndexOfAny(':', '+', '@');
        baseEnd = baseEnd < 0 ? subscription.Length : baseEnd + 1;
        if (pinSeparator != baseEnd
            || !subscription.AsSpan(1, baseEnd - 1).SequenceEqual("Platform"))
        {
            return false;
        }

        int pinEndOffset = subscription.AsSpan(pinSeparator + 1).IndexOfAny(':', '+');
        int pinEnd = pinEndOffset < 0
            ? subscription.Length
            : pinSeparator + 1 + pinEndOffset;
        if (pinEnd == pinSeparator + 1)
            return false;

        pin = subscription[(pinSeparator + 1)..pinEnd];
        expression = string.Concat(
            subscription.AsSpan(0, pinSeparator),
            subscription.AsSpan(pinEnd));
        return true;
    }

    private static WorkspaceSharePacketProjectionResult Failed(
        string path,
        string message) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            path,
            message);

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
