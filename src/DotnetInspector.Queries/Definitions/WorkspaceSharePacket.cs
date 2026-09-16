using System.Collections.ObjectModel;

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
/// One query-free committed view row in a format-2 workspace share packet.
/// </summary>
public sealed class WorkspaceShareViewState
{
    internal WorkspaceShareViewState(
        int? tabIndex,
        PortableSubjectRequest? subject,
        PortableRetainedSubjectContext? context,
        string? facet)
    {
        TabIndex = tabIndex;
        Subject = subject;
        Context = context;
        Facet = facet;
    }

    /// <summary>
    /// Null for the leading Workspace row; otherwise the exact index into
    /// <see cref="WorkspaceSharePacket.Tabs"/>.
    /// </summary>
    public int? TabIndex { get; }

    public PortableSubjectRequest? Subject { get; }

    public PortableRetainedSubjectContext? Context { get; }

    public string? Facet { get; }
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
        WorkspaceShareViewState[] viewStates)
    {
        FormatVersion = WorkspaceSharePacketCodec.CurrentFormatVersion;
        Tabs = new ReadOnlyCollection<WorkspaceShareTab>(
            (WorkspaceShareTab[])tabs.Clone());
        Contexts = new ReadOnlyCollection<WorkspaceShareContext>(
            (WorkspaceShareContext[])contexts.Clone());
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

    public int FormatVersion { get; }

    public IReadOnlyList<WorkspaceShareTab> Tabs { get; }

    public IReadOnlyList<WorkspaceShareContext> Contexts { get; }

    /// <summary>
    /// The focused direct-Package tab, or null when format 2 selects the
    /// leading Workspace row.
    /// </summary>
    public int? FocusedTabIndex { get; }

    /// <summary>
    /// The format-1 active tab index. Format 2 consumers should use
    /// <see cref="FocusedTabIndex"/>; this value is -1 when the Workspace row
    /// is selected.
    /// </summary>
    public int ActiveTabIndex { get; }

    public int SelectedContextIndex { get; }

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

    /// <summary>Format-2 committed view rows.</summary>
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
