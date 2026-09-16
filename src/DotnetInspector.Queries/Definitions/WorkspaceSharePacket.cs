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
/// The validated semantic model for one canonical v1 <c>w</c> query value.
/// </summary>
/// <remarks>
/// The packet separates coordinate/binding state from optional initial view
/// state. This type does not resolve view ids, acquire artifacts, or execute a
/// query. <c>WorkspaceSharePacketCodecTests.Decode_CanonicalVector_RoundTripsExactly</c>
/// gates exact v1 decoding and canonical re-emission.
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
        Tabs = new ReadOnlyCollection<WorkspaceShareTab>(
            (WorkspaceShareTab[])tabs.Clone());
        Contexts = new ReadOnlyCollection<WorkspaceShareContext>(
            (WorkspaceShareContext[])contexts.Clone());
        ActiveTabIndex = activeTabIndex;
        SelectedContextIndex = selectedContextIndex;
        Lens = lens;
        Type = type;
        MemberAnchor = memberAnchor;
        MemberSignature = memberSignature;
        Section = section;
        Libraries = new ReadOnlyCollection<string>((string[])libraries.Clone());
    }

    public int FormatVersion => WorkspaceSharePacketCodec.CurrentFormatVersion;

    public IReadOnlyList<WorkspaceShareTab> Tabs { get; }

    public IReadOnlyList<WorkspaceShareContext> Contexts { get; }

    public int ActiveTabIndex { get; }

    public int SelectedContextIndex { get; }

    public string? Lens { get; }

    public string? Type { get; }

    public string? MemberAnchor { get; }

    public string? MemberSignature { get; }

    public string? Section { get; }

    public IReadOnlyList<string> Libraries { get; }
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
