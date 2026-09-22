using System.Collections.ObjectModel;
using QuerySpace;

namespace DotnetInspector.Queries.Definitions;

/// <summary>The acquisition-source kind carried by one workspace share tab.</summary>
public enum WorkspaceShareSourceKind
{
    Package = 0,
    Group = 1,
}

/// <summary>
/// One navigation source in a versioned workspace share packet.
/// </summary>
public sealed record WorkspaceShareTab
{
    internal WorkspaceShareTab(
        WorkspaceShareSourceKind sourceKind,
        string source,
        string? version,
        string? framework,
        string? runtimeIdentifier)
    {
        SourceKind = sourceKind;
        Source = source;
        Version = version;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
    }

    public WorkspaceShareSourceKind SourceKind { get; }

    /// <summary>A package id or a leading-colon group expression.</summary>
    public string Source { get; }

    /// <summary>
    /// An exact package version or the base group segment's exact pin.
    /// Null means the coordinate floats.
    /// </summary>
    public string? Version { get; }

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }
}

/// <summary>
/// One binding-consistent context expressed as indexes into
/// <see cref="WorkspaceSharePacket.Tabs"/>.
/// </summary>
public sealed class WorkspaceShareContext
{
    internal WorkspaceShareContext(int[] tabIndexes)
    {
        TabIndexes = new ReadOnlyCollection<int>((int[])tabIndexes.Clone());
    }

    /// <summary>
    /// Ordered tab indexes. Order is member overlay and binding precedence,
    /// not navigation order.
    /// </summary>
    public IReadOnlyList<int> TabIndexes { get; }
}

/// <summary>
/// One committed view row in a format-2-through-4 workspace share packet.
/// </summary>
public sealed class WorkspaceShareViewState
{
    internal WorkspaceShareViewState(
        int? tabIndex,
        PortableSubjectRequest? subject,
        PortableRetainedSubjectContext? context,
        string? facet,
        int[]? queryIndexes = null,
        PortableLibraryIdentity[]? libraries = null)
    {
        TabIndex = tabIndex;
        Subject = subject;
        Context = context;
        Facet = facet;
        QueryIndexes = new ReadOnlyCollection<int>(
            queryIndexes is null ? [] : (int[])queryIndexes.Clone());
        Libraries = new ReadOnlyCollection<PortableLibraryIdentity>(
            libraries is null
                ? []
                : (PortableLibraryIdentity[])libraries.Clone());
    }

    /// <summary>
    /// Null for the leading Workspace row; otherwise the exact index into
    /// <see cref="WorkspaceSharePacket.Tabs"/>.
    /// </summary>
    public int? TabIndex { get; }

    public PortableSubjectRequest? Subject { get; }

    public PortableRetainedSubjectContext? Context { get; }

    public string? Facet { get; }

    public IReadOnlyList<int> QueryIndexes { get; }

    public IReadOnlyList<PortableLibraryIdentity> Libraries { get; }
}

/// <summary>
/// The validated semantic model for one canonical <c>w</c> query value.
/// </summary>
/// <remarks>
/// The packet separates coordinate/binding state from committed view state.
/// This type does not resolve view ids, acquire artifacts, or execute a query.
/// </remarks>
public sealed class WorkspaceSharePacket
{
    internal WorkspaceSharePacket(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        int activeTabIndex,
        int selectedContextIndex,
        string? lens,
        string? type,
        string? memberAnchor,
        string? memberSignature,
        string? section,
        string[] libraries)
    {
        FormatVersion = WorkspaceSharePacketCodec.LegacyFormatVersion;
        Tabs = new ReadOnlyCollection<WorkspaceShareTab>(
            (WorkspaceShareTab[])tabs.Clone());
        Contexts = new ReadOnlyCollection<WorkspaceShareContext>(
            (WorkspaceShareContext[])contexts.Clone());
        Registrations = Array.Empty<WorkspaceRegistration>();
        PackageSources = Array.Empty<WorkspacePackageSourceDefinition>();
        Queries = Array.Empty<PortableQueryIdentity>();
        FocusedTabIndex = activeTabIndex;
        ActiveTabIndex = activeTabIndex;
        SelectedContextIndex = selectedContextIndex;
        Lens = lens;
        Type = type;
        MemberAnchor = memberAnchor;
        MemberSignature = memberSignature;
        Section = section;
        Libraries = new ReadOnlyCollection<string>((string[])libraries.Clone());
        ViewStates = Array.Empty<WorkspaceShareViewState>();
    }

    internal WorkspaceSharePacket(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        int? focusedTabIndex,
        int selectedContextIndex,
        WorkspaceShareViewState[] viewStates,
        PortableQueryIdentity[]? queries = null)
    {
        FormatVersion = WorkspaceSharePacketCodec.Format2Version;
        Tabs = new ReadOnlyCollection<WorkspaceShareTab>(
            (WorkspaceShareTab[])tabs.Clone());
        Contexts = new ReadOnlyCollection<WorkspaceShareContext>(
            (WorkspaceShareContext[])contexts.Clone());
        Registrations = Array.Empty<WorkspaceRegistration>();
        PackageSources = Array.Empty<WorkspacePackageSourceDefinition>();
        Queries = new ReadOnlyCollection<PortableQueryIdentity>(
            queries is null
                ? []
                : (PortableQueryIdentity[])queries.Clone());
        FocusedTabIndex = focusedTabIndex;
        ActiveTabIndex = focusedTabIndex ?? -1;
        SelectedContextIndex = selectedContextIndex;
        Lens = null;
        Type = null;
        MemberAnchor = null;
        MemberSignature = null;
        Section = null;
        Libraries = Array.Empty<string>();
        ViewStates = new ReadOnlyCollection<WorkspaceShareViewState>(
            (WorkspaceShareViewState[])viewStates.Clone());
    }

    internal WorkspaceSharePacket(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        WorkspaceRegistration[] registrations,
        int? focusedTabIndex,
        int? selectedContextIndex,
        WorkspaceShareViewState[] viewStates,
        PortableQueryIdentity[]? queries = null)
        : this(
            WorkspaceSharePacketCodec.CurrentFormatVersion,
            tabs,
            contexts,
            registrations,
            [],
            focusedTabIndex,
            selectedContextIndex,
            viewStates,
            queries)
    {
    }

    private WorkspaceSharePacket(
        int formatVersion,
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        WorkspaceRegistration[] registrations,
        WorkspacePackageSourceDefinition[] packageSources,
        int? focusedTabIndex,
        int? selectedContextIndex,
        WorkspaceShareViewState[] viewStates,
        PortableQueryIdentity[]? queries)
    {
        if (formatVersion is not (
            WorkspaceSharePacketCodec.CurrentFormatVersion
            or WorkspaceSharePacketCodec.Format4Version
            or WorkspaceSharePacketCodec.Format5Version))
        {
            throw new ArgumentOutOfRangeException(
                nameof(formatVersion),
                formatVersion,
                "A registration-bearing packet must use format 3, 4, or 5.");
        }
        if (formatVersion != WorkspaceSharePacketCodec.Format5Version
            && packageSources.Length != 0)
        {
            throw new ArgumentException(
                "Workspace package sources require packet format 5.",
                nameof(packageSources));
        }
        if (formatVersion == WorkspaceSharePacketCodec.Format5Version
            && packageSources.Length == 0)
        {
            throw new ArgumentException(
                "A format-5 Workspace packet requires at least one package source.",
                nameof(packageSources));
        }
        WorkspacePackageSourceDefinition.ValidateSet(packageSources);

        FormatVersion = formatVersion;
        Tabs = new ReadOnlyCollection<WorkspaceShareTab>(
            (WorkspaceShareTab[])tabs.Clone());
        Contexts = new ReadOnlyCollection<WorkspaceShareContext>(
            (WorkspaceShareContext[])contexts.Clone());
        Registrations = new ReadOnlyCollection<WorkspaceRegistration>(
            (WorkspaceRegistration[])registrations.Clone());
        PackageSources =
            new ReadOnlyCollection<WorkspacePackageSourceDefinition>(
                (WorkspacePackageSourceDefinition[])packageSources.Clone());
        Queries = new ReadOnlyCollection<PortableQueryIdentity>(
            queries is null
                ? []
                : (PortableQueryIdentity[])queries.Clone());
        FocusedTabIndex = focusedTabIndex;
        ActiveTabIndex = focusedTabIndex ?? -1;
        SelectedContextIndex = selectedContextIndex;
        Lens = null;
        Type = null;
        MemberAnchor = null;
        MemberSignature = null;
        Section = null;
        Libraries = Array.Empty<string>();
        ViewStates = new ReadOnlyCollection<WorkspaceShareViewState>(
            (WorkspaceShareViewState[])viewStates.Clone());
    }

    internal static WorkspaceSharePacket CreateV4(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        WorkspaceRegistration[] registrations,
        int? focusedTabIndex,
        int? selectedContextIndex,
        WorkspaceShareViewState[] viewStates,
        PortableQueryIdentity[]? queries = null) =>
        new(
            WorkspaceSharePacketCodec.Format4Version,
            tabs,
            contexts,
            registrations,
            [],
            focusedTabIndex,
            selectedContextIndex,
            viewStates,
            queries);

    internal static WorkspaceSharePacket CreateV5(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        WorkspaceRegistration[] registrations,
        WorkspacePackageSourceDefinition[] packageSources,
        int? focusedTabIndex,
        int? selectedContextIndex,
        WorkspaceShareViewState[] viewStates,
        PortableQueryIdentity[]? queries = null) =>
        new(
            WorkspaceSharePacketCodec.Format5Version,
            tabs,
            contexts,
            registrations,
            packageSources,
            focusedTabIndex,
            selectedContextIndex,
            viewStates,
            queries);

    public int FormatVersion { get; }

    public IReadOnlyList<WorkspaceShareTab> Tabs { get; }

    public IReadOnlyList<WorkspaceShareContext> Contexts { get; }

    /// <summary>Format-3-or-4 ordered portable Workspace registrations.</summary>
    public IReadOnlyList<WorkspaceRegistration> Registrations { get; }

    /// <summary>Format-5 ordered credential-free package source declarations.</summary>
    public IReadOnlyList<WorkspacePackageSourceDefinition> PackageSources { get; }

    /// <summary>Format-2-through-5 canonical packet-local query identities.</summary>
    public IReadOnlyList<PortableQueryIdentity> Queries { get; }

    /// <summary>
    /// The focused direct-Package tab, or null when a committed packet selects
    /// the leading Workspace row.
    /// </summary>
    public int? FocusedTabIndex { get; }

    /// <summary>
    /// The format-1 active tab index. Format 2 through 4 consumers should use
    /// <see cref="FocusedTabIndex"/>; this value is -1 when the Workspace row
    /// is selected.
    /// </summary>
    public int ActiveTabIndex { get; }

    /// <summary>
    /// The selected context, or null for a format-3-or-4 registration-only
    /// Workspace.
    /// </summary>
    public int? SelectedContextIndex { get; }

    /// <summary>Format-1 lens token.</summary>
    public string? Lens { get; }

    /// <summary>Format-1 flattened Type selector.</summary>
    public string? Type { get; }

    /// <summary>Format-1 member anchor.</summary>
    public string? MemberAnchor { get; }

    /// <summary>Format-1 member signature.</summary>
    public string? MemberSignature { get; }

    /// <summary>Format-1 section token.</summary>
    public string? Section { get; }

    /// <summary>Format-1 filename-stem Library scope.</summary>
    public IReadOnlyList<string> Libraries { get; }

    /// <summary>Format-2-through-5 committed view rows.</summary>
    public IReadOnlyList<WorkspaceShareViewState> ViewStates { get; }
}

/// <summary>Why a workspace share packet could not be decoded.</summary>
public enum WorkspaceSharePacketFailureKind
{
    Empty,
    EncodedLimitExceeded,
    InvalidBase64Url,
    DecodedLimitExceeded,
    InvalidJson,
    UnsupportedFormat,
    InvalidShape,
    JsonValueLimitExceeded,
    NonCanonical,
}

/// <summary>Typed failure while decoding or emitting workspace share state.</summary>
public sealed class WorkspaceSharePacketException : Exception
{
    public WorkspaceSharePacketException(
        WorkspaceSharePacketFailureKind kind,
        string message)
        : base(message)
    {
        Kind = kind;
    }

    public WorkspaceSharePacketException(
        WorkspaceSharePacketFailureKind kind,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public WorkspaceSharePacketFailureKind Kind { get; }
}
