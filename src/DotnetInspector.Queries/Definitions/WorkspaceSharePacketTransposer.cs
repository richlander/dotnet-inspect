using System.Collections.ObjectModel;
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
            return InvalidDefinition(
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
        expression = subscription;
        pin = null;
        failure = null;

        if (subscription.Length < 2 || subscription[0] != ':')
        {
            failure = InvalidDefinition(
                path,
                "A group subscription must begin with ':'.");
            return false;
        }

        int pinSeparator = subscription.IndexOf('@');
        if (pinSeparator < 0)
        {
            if (WorkspaceSharePacketCodec.IsGroupExpression(subscription))
                return true;

            failure = InvalidDefinition(
                path,
                "The subscription is not a valid group expression.");
            return false;
        }
        if (subscription.IndexOf('@', pinSeparator + 1) >= 0)
        {
            failure = NonProjectable(
                path,
                "Packet v1 cannot preserve multiple group pins.");
            return false;
        }

        int baseEnd = subscription.AsSpan(1).IndexOfAny(':', '+', '@');
        baseEnd = baseEnd < 0 ? subscription.Length : baseEnd + 1;
        if (pinSeparator != baseEnd)
        {
            failure = NonProjectable(
                path,
                "Packet v1 can preserve only a pin on the base group segment.");
            return false;
        }
        if (!subscription.AsSpan(1, baseEnd - 1).SequenceEqual("Platform"))
        {
            failure = NonProjectable(
                path,
                "Packet v1 can preserve a group pin only on a Platform base.");
            return false;
        }

        int pinEndOffset = subscription.AsSpan(pinSeparator + 1).IndexOfAny(':', '+');
        int pinEnd = pinEndOffset < 0
            ? subscription.Length
            : pinSeparator + 1 + pinEndOffset;
        if (pinEnd == pinSeparator + 1)
        {
            failure = InvalidDefinition(path, "A group pin must not be empty.");
            return false;
        }

        if (!TryNormalizeVersion(
            subscription[(pinSeparator + 1)..pinEnd],
            out pin))
        {
            failure = InvalidDefinition(
                path,
                "A group pin must be one exact NuGet version without build metadata.");
            return false;
        }

        expression = string.Concat(
            subscription.AsSpan(0, pinSeparator),
            subscription.AsSpan(pinEnd));
        if (!WorkspaceSharePacketCodec.IsGroupExpression(expression))
        {
            failure = InvalidDefinition(
                path,
                "The subscription is not a valid group expression.");
            return false;
        }

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
