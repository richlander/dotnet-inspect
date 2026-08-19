using System.Text.Json.Serialization;

namespace ILInspector.SourceLink;

/// <summary>The observed usability of a SourceLink document map.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceLinkMapStatus>))]
public enum SourceLinkMapStatus
{
    Absent,
    Usable,
    PartiallyUsable,
    Unusable,
}

/// <summary>A SourceLink document mapping exactly as decoded from the portable PDB.</summary>
public sealed record SourceLinkMapEntry(string Document, string? Url);

/// <summary>The resource limit that stopped SourceLink map inspection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceLinkMapLimitKind>))]
public enum SourceLinkMapLimitKind
{
    None,
    EncodedBytes,
    Mappings,
}

/// <summary>
/// SourceLink map facts read directly from one portable PDB without claiming assembly identity.
/// </summary>
public sealed record SourceLinkMapAudit(
    SourceLinkMapInspection Map,
    IReadOnlyList<SourceLinkMapEntry> Entries,
    int EncodedBytes,
    SourceLinkMapLimitKind LimitKind = SourceLinkMapLimitKind.None)
{
    [JsonIgnore]
    public bool LimitExceeded => LimitKind != SourceLinkMapLimitKind.None;
}

/// <summary>
/// Parse and entry-validation facts for one SourceLink document map.
/// </summary>
/// <param name="Status">Whether the map is absent, fully usable, partially usable, or unusable.</param>
/// <param name="Error">Why the whole map is unusable, or null when parsing succeeded.</param>
/// <param name="DocumentKeys">All document keys exactly as authored.</param>
/// <param name="RejectedKeys">Document keys whose mappings were rejected individually.</param>
public sealed record SourceLinkMapInspection(
    SourceLinkMapStatus Status,
    string? Error,
    [property: JsonIgnore] IReadOnlyList<string> DocumentKeys,
    IReadOnlyList<string> RejectedKeys)
{
    public static SourceLinkMapInspection Absent { get; } =
        new(SourceLinkMapStatus.Absent, null, [], []);

    [JsonIgnore]
    public bool IsPresent => Status != SourceLinkMapStatus.Absent;

    [JsonIgnore]
    public bool IsUsable =>
        Status is SourceLinkMapStatus.Usable or SourceLinkMapStatus.PartiallyUsable;

    [JsonIgnore]
    public bool HasDiagnostics =>
        Error is not null || RejectedKeys is { Count: > 0 };
}

/// <summary>SourceLink-aware path and map facts for an assembly's current PDB state.</summary>
public sealed record SourceLinkDebugAudit(
    SourceLinkMapInspection SourceLinkMap,
    bool? HasNormalizedPaths,
    IReadOnlyList<string>? NonNormalizedPaths);
