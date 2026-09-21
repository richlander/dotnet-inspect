using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using QuerySpace;

namespace DotnetInspector.Queries.Definitions;

[JsonConverter(typeof(WorkspacePackageComponentPathJsonConverter))]
public sealed record WorkspacePackageComponentPath
{
    private const string Prefix = "packages/";
    private const string Absent = "~";

    private WorkspacePackageComponentPath(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryCreate(
        string? value,
        out WorkspacePackageComponentPath? path,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            path = null;
            error = "A Workspace Package component path is required.";
            return false;
        }

        string[] segments = value.Split('/');
        int separator = segments.Length == 4
            ? segments[1].LastIndexOf('@')
            : -1;
        if (segments.Length != 4
            || !string.Equals(segments[0], "packages", StringComparison.Ordinal)
            || separator <= 0
            || separator == segments[1].Length - 1)
        {
            path = null;
            error =
                "Workspace Package component paths use "
                + "'packages/ID@VERSION/TFM/RID'; '~' denotes an absent value.";
            return false;
        }

        string packageId = segments[1][..separator];
        string version = segments[1][(separator + 1)..];
        string framework = segments[2];
        string runtimeIdentifier = segments[3];
        if (!string.Equals(
                packageId,
                packageId.ToLowerInvariant(),
                StringComparison.Ordinal)
            || !PackageCoordinateResolver.IsCanonicalPackageId(packageId)
            || (version != Absent
                && !RealizedMemberCoordinate.IsCanonicalPackageVersion(
                    version))
            || (framework != Absent
                && !RealizedMemberCoordinate.IsCanonicalFramework(framework))
            || (runtimeIdentifier != Absent
                && !RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
                    runtimeIdentifier)))
        {
            path = null;
            error = "The Workspace Package component path is not canonical.";
            return false;
        }

        path = new WorkspacePackageComponentPath(value);
        error = null;
        return true;
    }

    internal static WorkspacePackageComponentPath Create(
        string packageId,
        string? version,
        string? framework,
        string? runtimeIdentifier)
    {
        string value =
            $"{Prefix}{packageId.ToLowerInvariant()}@"
            + $"{version ?? Absent}/{framework ?? Absent}/"
            + $"{runtimeIdentifier ?? Absent}";
        if (!TryCreate(value, out WorkspacePackageComponentPath? path, out _))
        {
            throw new ArgumentException(
                "The Package coordinate cannot be projected as a canonical "
                    + "Workspace component path.",
                nameof(packageId));
        }
        return path!;
    }

    internal static WorkspacePackageComponentPath Create(
        WorkspaceShareTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.SourceKind != WorkspaceShareSourceKind.Package)
        {
            throw new ArgumentException(
                "Only direct Package tabs have Package component paths.",
                nameof(tab));
        }

        return Create(
            tab.Source,
            tab.Version,
            tab.Framework,
            tab.RuntimeIdentifier);
    }

    internal static WorkspacePackageComponentPath Create(
        DefinitionMemberCoordinate.PackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Create(
            coordinate.Id,
            InspectionDefinitionRegistry.NormalizeVersion(coordinate.Version),
            InspectionDefinitionRegistry.NormalizeFramework(
                coordinate.Framework),
            NormalizeRuntimeIdentifier(coordinate.RuntimeIdentifier));
    }

    private static string? NormalizeRuntimeIdentifier(string? value)
    {
        if (value is null)
            return null;
        string normalized = value.ToLowerInvariant();
        if (!RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(normalized))
        {
            throw new ArgumentException(
                $"'{value}' is not a canonical runtime identifier.",
                nameof(value));
        }
        return normalized;
    }
}

public sealed class WorkspacePackageComponentPathJsonConverter :
    JsonConverter<WorkspacePackageComponentPath>
{
    public override WorkspacePackageComponentPath Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (!WorkspacePackageComponentPath.TryCreate(
                value,
                out WorkspacePackageComponentPath? path,
                out string? error))
        {
            throw new JsonException(error);
        }
        return path!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorkspacePackageComponentPath value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

[JsonConverter(typeof(WorkspaceContextComponentPathJsonConverter))]
public sealed record WorkspaceContextComponentPath
{
    private WorkspaceContextComponentPath(int index)
    {
        Index = index;
        Value = $"contexts/g{index}";
    }

    public string Value { get; }

    [JsonIgnore]
    internal int Index { get; }

    public override string ToString() => Value;

    public static bool TryCreate(
        string? value,
        out WorkspaceContextComponentPath? path,
        out string? error)
    {
        const string prefix = "contexts/g";
        if (value is null
            || !value.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(
                value.AsSpan(prefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int index)
            || index < 0
            || value != $"{prefix}{index}")
        {
            path = null;
            error =
                "Workspace context component paths use 'contexts/gN', where N "
                + "is the canonical packet-local context identity.";
            return false;
        }

        path = new WorkspaceContextComponentPath(index);
        error = null;
        return true;
    }

    internal static WorkspaceContextComponentPath Create(int index) =>
        new(index);
}

public sealed class WorkspaceContextComponentPathJsonConverter :
    JsonConverter<WorkspaceContextComponentPath>
{
    public override WorkspaceContextComponentPath Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (!WorkspaceContextComponentPath.TryCreate(
                value,
                out WorkspaceContextComponentPath? path,
                out string? error))
        {
            throw new JsonException(error);
        }
        return path!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorkspaceContextComponentPath value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed record WorkspacePackageComponentDescriptor(
    WorkspacePackageComponentPath Path,
    string PackageId,
    string? Version,
    string? Framework,
    string? RuntimeIdentifier,
    ImmutableArray<WorkspaceContextComponentPath> Contexts,
    bool Focused);

public sealed record WorkspaceContextComponentDescriptor(
    WorkspaceContextComponentPath Path,
    string? Framework,
    string? RuntimeIdentifier,
    ImmutableArray<WorkspacePackageComponentPath> Packages,
    bool Selected);

public sealed record WorkspaceComponentDocument(
    int SchemaVersion,
    ImmutableArray<WorkspaceContextComponentDescriptor> Contexts,
    ImmutableArray<WorkspacePackageComponentDescriptor> Packages);

public static class WorkspaceComponentCatalog
{
    public static WorkspaceComponentDocument Describe(
        WorkspaceSharePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var contextsByTab =
            new List<WorkspaceContextComponentPath>[packet.Tabs.Count];
        for (int tabIndex = 0; tabIndex < contextsByTab.Length; tabIndex++)
            contextsByTab[tabIndex] = [];

        var contexts =
            ImmutableArray.CreateBuilder<WorkspaceContextComponentDescriptor>(
                packet.Contexts.Count);
        for (int contextIndex = 0;
            contextIndex < packet.Contexts.Count;
            contextIndex++)
        {
            WorkspaceShareContext context = packet.Contexts[contextIndex];
            WorkspaceContextComponentPath path =
                WorkspaceContextComponentPath.Create(contextIndex);
            var contextPackages =
                ImmutableArray.CreateBuilder<WorkspacePackageComponentPath>();
            foreach (int tabIndex in context.TabIndexes)
            {
                contextsByTab[tabIndex].Add(path);
                WorkspaceShareTab tab = packet.Tabs[tabIndex];
                if (tab.SourceKind == WorkspaceShareSourceKind.Package)
                    contextPackages.Add(
                        WorkspacePackageComponentPath.Create(tab));
            }

            WorkspaceShareTab first = packet.Tabs[context.TabIndexes[0]];
            contexts.Add(new WorkspaceContextComponentDescriptor(
                path,
                first.Framework,
                first.RuntimeIdentifier,
                contextPackages.ToImmutable(),
                packet.SelectedContextIndex == contextIndex));
        }

        var packagesByPath =
            new HashSet<WorkspacePackageComponentPath>();
        var packages =
            ImmutableArray.CreateBuilder<WorkspacePackageComponentDescriptor>();
        for (int tabIndex = 0; tabIndex < packet.Tabs.Count; tabIndex++)
        {
            WorkspaceShareTab tab = packet.Tabs[tabIndex];
            if (tab.SourceKind != WorkspaceShareSourceKind.Package)
                continue;
            WorkspacePackageComponentPath path =
                WorkspacePackageComponentPath.Create(tab);
            if (!packagesByPath.Add(path))
            {
                throw new InvalidDataException(
                    $"Workspace Package component path '{path}' is not unique.");
            }

            packages.Add(new WorkspacePackageComponentDescriptor(
                path,
                tab.Source,
                tab.Version,
                tab.Framework,
                tab.RuntimeIdentifier,
                [.. contextsByTab[tabIndex]],
                packet.FocusedTabIndex == tabIndex));
        }

        return new WorkspaceComponentDocument(
            SchemaVersion: 1,
            contexts.MoveToImmutable(),
            packages.ToImmutable());
    }

    internal static int ResolvePackageTabIndex(
        WorkspaceSharePacket packet,
        WorkspacePackageComponentPath path)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(path);

        int match = -1;
        for (int index = 0; index < packet.Tabs.Count; index++)
        {
            WorkspaceShareTab tab = packet.Tabs[index];
            if (tab.SourceKind != WorkspaceShareSourceKind.Package
                || WorkspacePackageComponentPath.Create(tab) != path)
            {
                continue;
            }
            if (match >= 0)
            {
                throw new InvalidDataException(
                    $"Workspace Package component path '{path}' is ambiguous.");
            }
            match = index;
        }
        return match;
    }
}

public enum WorkspacePackageComponentEditFailureKind
{
    UnsupportedFormat,
    InvalidInput,
    UnknownComponent,
    DuplicateComponent,
    UnknownContext,
    IncompatibleContext,
    InvalidDerivedPacket,
}

public sealed record WorkspacePackageComponentEditFailure(
    WorkspacePackageComponentEditFailureKind Kind,
    string Detail);

public sealed record WorkspacePackageComponentEditResult
{
    private WorkspacePackageComponentEditResult(
        WorkspaceSharePacket? packet,
        WorkspacePackageComponentEditFailure? failure)
    {
        Packet = packet;
        Failure = failure;
    }

    public WorkspaceSharePacket? Packet { get; }

    public WorkspacePackageComponentEditFailure? Failure { get; }

    public bool Succeeded => Packet is not null;

    internal static WorkspacePackageComponentEditResult Success(
        WorkspaceSharePacket packet) =>
        new(packet, null);

    internal static WorkspacePackageComponentEditResult Refused(
        WorkspacePackageComponentEditFailureKind kind,
        string detail) =>
        new(null, new(kind, detail));
}

public static class WorkspacePackageComponentEditor
{
    public static WorkspacePackageComponentEditResult Add(
        WorkspaceSharePacket packet,
        string packageId,
        string? version,
        string? framework,
        string? runtimeIdentifier,
        WorkspaceContextComponentPath? context = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.FormatVersion == WorkspaceSharePacketCodec.LegacyFormatVersion)
            return UnsupportedFormat(packet.FormatVersion);

        string canonicalId;
        string? canonicalVersion;
        string? canonicalFramework;
        string? canonicalRuntimeIdentifier;
        try
        {
            canonicalId = packageId?.Trim()
                ?? throw new ArgumentNullException(nameof(packageId));
            if (!PackageCoordinateResolver.IsCanonicalPackageId(canonicalId))
            {
                throw new ArgumentException(
                    $"'{packageId}' is not a canonical NuGet package ID.",
                    nameof(packageId));
            }
            canonicalVersion =
                InspectionDefinitionRegistry.NormalizeVersion(version);
            canonicalFramework =
                InspectionDefinitionRegistry.NormalizeFramework(framework);
            canonicalRuntimeIdentifier =
                NormalizeRuntimeIdentifier(runtimeIdentifier);
        }
        catch (Exception error) when (error is ArgumentException
            or InspectionDefinitionException)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.InvalidInput,
                error.Message);
        }

        int? contextIndex = context?.Index ?? packet.SelectedContextIndex;
        if (contextIndex is int requested
            && (requested < 0 || requested >= packet.Contexts.Count))
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.UnknownContext,
                $"Workspace context component '{context}' is absent.");
        }

        if (contextIndex is int existingContext)
        {
            WorkspaceShareContext selected = packet.Contexts[existingContext];
            WorkspaceShareTab first = packet.Tabs[selected.TabIndexes[0]];
            canonicalFramework ??= first.Framework;
            canonicalRuntimeIdentifier ??= first.RuntimeIdentifier;
            if (!string.Equals(
                    canonicalFramework,
                    first.Framework,
                    StringComparison.Ordinal)
                || !string.Equals(
                    canonicalRuntimeIdentifier,
                    first.RuntimeIdentifier,
                    StringComparison.Ordinal))
            {
                return WorkspacePackageComponentEditResult.Refused(
                    WorkspacePackageComponentEditFailureKind
                        .IncompatibleContext,
                    $"Package target does not match Workspace context "
                        + $"'{WorkspaceContextComponentPath.Create(existingContext)}'.");
            }
        }
        else if (packet.Contexts.Count != 0)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.UnknownContext,
                "Select a Workspace context when the packet has no selected context.");
        }

        if (canonicalFramework is null)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.InvalidInput,
                "A target framework is required when adding the first "
                    + "Workspace Package component.");
        }

        var added = new WorkspaceShareTab(
            WorkspaceShareSourceKind.Package,
            canonicalId,
            canonicalVersion,
            canonicalFramework,
            canonicalRuntimeIdentifier);
        WorkspacePackageComponentPath addedPath =
            WorkspacePackageComponentPath.Create(added);
        if (WorkspaceComponentCatalog.ResolvePackageTabIndex(
                packet,
                addedPath) >= 0)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.DuplicateComponent,
                $"Workspace Package component '{addedPath}' already exists.");
        }

        int newTabIndex = packet.Tabs.Count;
        WorkspaceShareTab[] tabs = [.. packet.Tabs, added];
        WorkspaceShareContext[] contexts;
        int selectedContextIndex;
        if (contextIndex is int targetContext)
        {
            contexts = [.. packet.Contexts];
            WorkspaceShareContext current = contexts[targetContext];
            contexts[targetContext] =
                new WorkspaceShareContext(
                    [.. current.TabIndexes, newTabIndex]);
            selectedContextIndex =
                packet.SelectedContextIndex ?? targetContext;
        }
        else
        {
            contexts = [new WorkspaceShareContext([newTabIndex])];
            selectedContextIndex = 0;
        }

        WorkspaceShareViewState[] states =
        [
            .. packet.ViewStates,
            new WorkspaceShareViewState(
                newTabIndex,
                new PortableSubjectRequest.Package(),
                new PortableRetainedSubjectContext.Package(),
                facet: null),
        ];
        return CreateValidatedPacket(
            packet,
            tabs,
            contexts,
            packet.FocusedTabIndex,
            selectedContextIndex,
            states,
            [.. packet.Queries]);
    }

    public static WorkspacePackageComponentEditResult Remove(
        WorkspaceSharePacket packet,
        WorkspacePackageComponentPath path)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(path);
        if (packet.FormatVersion == WorkspaceSharePacketCodec.LegacyFormatVersion)
            return UnsupportedFormat(packet.FormatVersion);

        int removedTab;
        try
        {
            removedTab =
                WorkspaceComponentCatalog.ResolvePackageTabIndex(packet, path);
        }
        catch (InvalidDataException error)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.InvalidInput,
                error.Message);
        }
        if (removedTab < 0)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.UnknownComponent,
                $"Workspace Package component '{path}' is absent.");
        }

        WorkspaceShareTab[] tabs =
        [
            .. packet.Tabs.Where((_, index) => index != removedTab),
        ];
        var retainedContexts = new List<WorkspaceShareContext>();
        int? selectedContext = null;
        for (int oldContextIndex = 0;
            oldContextIndex < packet.Contexts.Count;
            oldContextIndex++)
        {
            int[] retainedIndexes =
            [
                .. packet.Contexts[oldContextIndex].TabIndexes
                    .Where(index => index != removedTab)
                    .Select(index => index > removedTab ? index - 1 : index),
            ];
            if (retainedIndexes.Length == 0)
                continue;
            if (packet.SelectedContextIndex == oldContextIndex)
                selectedContext = retainedContexts.Count;
            retainedContexts.Add(new WorkspaceShareContext(retainedIndexes));
        }
        if (selectedContext is null && retainedContexts.Count != 0)
            selectedContext = 0;

        int? focused = packet.FocusedTabIndex switch
        {
            null => null,
            int index when index == removedTab => null,
            int index when index > removedTab => index - 1,
            int index => index,
        };
        WorkspaceShareViewState[] retainedStates =
        [
            packet.ViewStates[0],
            .. packet.ViewStates
                .Skip(1)
                .Where((_, index) => index != removedTab)
                .Select(state => CloneState(
                    state,
                    state.TabIndex is int tabIndex && tabIndex > removedTab
                        ? tabIndex - 1
                        : state.TabIndex,
                    state.QueryIndexes)),
        ];
        (
            PortableQueryIdentity[] queries,
            WorkspaceShareViewState[] states) =
            PruneQueries([.. packet.Queries], retainedStates);

        return CreateValidatedPacket(
            packet,
            tabs,
            [.. retainedContexts],
            focused,
            selectedContext,
            states,
            queries);
    }

    private static (
        PortableQueryIdentity[] Queries,
        WorkspaceShareViewState[] States)
        PruneQueries(
            PortableQueryIdentity[] queries,
            WorkspaceShareViewState[] states)
    {
        var retained = new bool[queries.Length];
        foreach (WorkspaceShareViewState state in states)
        {
            foreach (int queryIndex in state.QueryIndexes)
                retained[queryIndex] = true;
        }

        var mapping = new int[queries.Length];
        var compact = new List<PortableQueryIdentity>();
        for (int index = 0; index < queries.Length; index++)
        {
            if (!retained[index])
                continue;
            mapping[index] = compact.Count;
            compact.Add(queries[index]);
        }

        WorkspaceShareViewState[] remapped =
        [
            .. states.Select(state => CloneState(
                state,
                state.TabIndex,
                state.QueryIndexes.Select(index => mapping[index]))),
        ];
        return ([.. compact], remapped);
    }

    private static WorkspaceShareViewState CloneState(
        WorkspaceShareViewState state,
        int? tabIndex,
        IEnumerable<int> queryIndexes) =>
        new(
            tabIndex,
            state.Subject,
            state.Context,
            state.Facet,
            [.. queryIndexes],
            [.. state.Libraries]);

    private static WorkspacePackageComponentEditResult CreateValidatedPacket(
        WorkspaceSharePacket source,
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        int? focusedTabIndex,
        int? selectedContextIndex,
        WorkspaceShareViewState[] states,
        PortableQueryIdentity[] queries)
    {
        try
        {
            WorkspaceSharePacket candidate = source.FormatVersion switch
            {
                WorkspaceSharePacketCodec.Format2Version =>
                    new WorkspaceSharePacket(
                        tabs,
                        contexts,
                        focusedTabIndex,
                        selectedContextIndex
                            ?? throw new InvalidDataException(
                                "Workspace share format 2 requires a selected context."),
                        states,
                        queries),
                WorkspaceSharePacketCodec.CurrentFormatVersion =>
                    new WorkspaceSharePacket(
                        tabs,
                        contexts,
                        [.. source.Registrations],
                        focusedTabIndex,
                        selectedContextIndex,
                        states,
                        queries),
                WorkspaceSharePacketCodec.Format4Version =>
                    WorkspaceSharePacket.CreateV4(
                        tabs,
                        contexts,
                        [.. source.Registrations],
                        focusedTabIndex,
                        selectedContextIndex,
                        states,
                        queries),
                WorkspaceSharePacketCodec.Format5Version =>
                    WorkspaceSharePacket.CreateV5(
                        tabs,
                        contexts,
                        [.. source.Registrations],
                        [.. source.PackageSources],
                        focusedTabIndex,
                        selectedContextIndex,
                        states,
                        queries),
                _ => throw new InvalidDataException(
                    $"Workspace share format {source.FormatVersion} is not editable."),
            };
            string encoded = WorkspaceSharePacketCodec.Encode(candidate);
            return WorkspacePackageComponentEditResult.Success(
                WorkspaceSharePacketCodec.Decode(encoded));
        }
        catch (Exception error) when (error is WorkspaceSharePacketException
            or InvalidDataException or ArgumentException)
        {
            return WorkspacePackageComponentEditResult.Refused(
                WorkspacePackageComponentEditFailureKind.InvalidDerivedPacket,
                error.Message);
        }
    }

    private static WorkspacePackageComponentEditResult UnsupportedFormat(
        int formatVersion) =>
        WorkspacePackageComponentEditResult.Refused(
            WorkspacePackageComponentEditFailureKind.UnsupportedFormat,
            $"Workspace share format {formatVersion} is not editable; "
                + "component editing requires format 2 or later.");

    private static string? NormalizeRuntimeIdentifier(string? value)
    {
        if (value is null)
            return null;
        string normalized = value.ToLowerInvariant();
        if (!RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(normalized))
        {
            throw new ArgumentException(
                $"'{value}' is not a canonical runtime identifier.",
                nameof(value));
        }
        return normalized;
    }
}
