using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Source;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceComparisonResultKind>))]
public enum BrowserSourceComparisonResultKind
{
    Succeeded,
    TooComplex,
    Failed,
    Canceled,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserSourceComparisonRequest(
    string PackageId,
    string BeforeVersion,
    string AfterVersion,
    string Framework,
    string Assembly,
    string TypeIdentity,
    string MemberName,
    string SelectorKey,
    int MetadataToken);

public sealed record BrowserSourceComparisonResult(
    int Version,
    BrowserSourceComparisonResultKind Kind,
    BrowserSourceComparison? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason,
    BrowserSourceDiffCapacity? Capacity);

internal abstract record BrowserSourceComparisonProjectionResult
{
    internal sealed record Complete(BrowserSourceComparison Comparison)
        : BrowserSourceComparisonProjectionResult;

    internal sealed record TooComplex(BrowserSourceDiffCapacity Capacity)
        : BrowserSourceComparisonProjectionResult;
}

public sealed record BrowserSourceComparison(
    BrowserSourceComparisonRequest Request,
    string Status,
    bool IsExact,
    BrowserSourceComparisonEndpoint Before,
    BrowserSourceComparisonEndpoint After,
    BrowserSourceDiff? Diff,
    string? Failure);

public sealed record BrowserSourceComparisonEndpoint(
    string PackageId,
    string Version,
    string Framework,
    string Assembly,
    string AssetPath,
    string? ModuleVersionId,
    string AssemblyIdentity,
    string? MemberIdentity,
    int? MetadataToken,
    string State,
    string? Detail,
    string? Text,
    string? BrowseUrl,
    string? RepositoryUrl,
    string? Revision);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffCapacityDimension>))]
public enum BrowserSourceDiffCapacityDimension
{
    RequestBytes,
    RawBeforeBytes,
    RawAfterBytes,
    RawBeforeLines,
    RawAfterLines,
    Relations,
    CoordinateOccurrences,
    MappedChanges,
    InnerMappings,
    Annotations,
    AnnotationTextBytes,
    AuxiliaryTextBytes,
    EncodedResultBytes,
}

public sealed record BrowserSourceDiffCapacity(
    BrowserSourceDiffCapacityDimension Dimension,
    int Limit,
    int Actual);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffRelationKind>))]
public enum BrowserSourceDiffRelationKind
{
    Addition,
    Removal,
    Correspondence,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffContentKind>))]
public enum BrowserSourceDiffContentKind
{
    Unchanged,
    Changed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffPlacementKind>))]
public enum BrowserSourceDiffPlacementKind
{
    Stable,
    Moved,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffLineTerminator>))]
public enum BrowserSourceDiffLineTerminator
{
    Unknown,
    Present,
    Absent,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffAnnotationTargetKind>))]
public enum BrowserSourceDiffAnnotationTargetKind
{
    Change,
    Line,
    Span,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffSide>))]
public enum BrowserSourceDiffSide
{
    Before,
    After,
    Both,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceDiffSeverity>))]
public enum BrowserSourceDiffSeverity
{
    Note,
    Tip,
    Important,
    Warning,
    Caution,
}

public sealed record BrowserSourceDiff(
    int Version,
    BrowserSourceDiffSequence Before,
    BrowserSourceDiffSequence After,
    BrowserSourceDiffRelation[] Relations,
    BrowserSourceDiffStatistics Statistics,
    BrowserSourceDiffChange[] Changes);

public sealed record BrowserSourceDiffSequence(
    string? Label,
    string[] Lines,
    BrowserSourceDiffLineTerminator FinalLineTerminator);

public sealed record BrowserSourceDiffRelation(
    BrowserSourceDiffRelationKind Kind,
    int[] BeforeCoordinates,
    int[] AfterCoordinates,
    BrowserSourceDiffContentKind? Content,
    BrowserSourceDiffPlacementKind? Placement);

public sealed record BrowserSourceDiffStatistics(
    int Added,
    int Removed,
    int ChangedBefore,
    int ChangedAfter,
    int MovedBefore,
    int MovedAfter);

public sealed record BrowserSourceDiffChange(
    BrowserSourceDiffRange Before,
    BrowserSourceDiffRange After,
    BrowserSourceDiffInnerMapping[] InnerMappings,
    BrowserSourceDiffAnnotation[] Annotations);

public sealed record BrowserSourceDiffRange(int Start, int Count);

public sealed record BrowserSourceDiffInnerMapping(
    BrowserSourceDiffSpan Before,
    BrowserSourceDiffSpan After);

public sealed record BrowserSourceDiffSpan(int Line, int Start, int Count);

public sealed record BrowserSourceDiffAnnotation(
    string Text,
    BrowserSourceDiffSeverity Severity,
    BrowserSourceDiffAnnotationTargetKind TargetKind,
    BrowserSourceDiffSide? Side,
    int? Line,
    BrowserSourceDiffSpan? Span);
