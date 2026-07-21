using System.Text.Json.Serialization;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>Research facets requested for one MethodDef token and IL offset.</summary>
[Flags]
public enum ILOffsetProjectionCapabilities
{
    None = 0,
    SourceLocation = 1 << 0,
    InstructionContext = 1 << 1,
    ExceptionContext = 1 << 2,
    CallsiteContext = 1 << 3,
    ReturnAddressContext = 1 << 4,
    AllocationContext = 1 << 5,
    SafetyContext = 1 << 6,
    CostContext = 1 << 7,
}

/// <summary>
/// Inputs for producing a coordinate projection from an already-open metadata/source session.
/// </summary>
public sealed record ILOffsetProjectionRequest(
    SourceLinkService Source,
    int MethodToken,
    int ILOffset,
    ILOffsetProjectionCapabilities Capabilities,
    bool BrowsableUrls = false,
    Action<string>? Log = null);

/// <summary>The stage that prevented an IL-offset projection from being produced.</summary>
public enum ILOffsetProjectionFailureKind
{
    NoMetadata,
    MemberUnavailable,
    InstructionUnavailable,
    ExceptionUnavailable,
    CallsiteUnavailable,
    ReturnAddressUnavailable,
    SourceUnavailable,
    AllocationAnalysisUnavailable,
    SafetyAnalysisUnavailable,
    CostAnalysisUnavailable,
}

/// <summary>A typed projection failure with command-independent diagnostic text.</summary>
public sealed record ILOffsetProjectionFailure(
    ILOffsetProjectionFailureKind Kind,
    string Message,
    string? Detail = null);

/// <summary>The produced coordinate projection or its typed failure.</summary>
public sealed class ILOffsetProjectionOutcome
{
    ILOffsetProjectionOutcome(
        ILOffsetProjection? projection,
        ILOffsetProjectionFailure? failure)
    {
        Projection = projection;
        Failure = failure;
    }

    public ILOffsetProjection? Projection { get; }
    public ILOffsetProjectionFailure? Failure { get; }
    public bool Succeeded => Projection is not null && Failure is null;

    public static ILOffsetProjectionOutcome Success(ILOffsetProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new(projection, null);
    }

    public static ILOffsetProjectionOutcome Failed(
        ILOffsetProjectionFailureKind kind,
        string message,
        string? detail = null)
        => new(null, new ILOffsetProjectionFailure(kind, message, detail));
}

/// <summary>
/// View-compatible Metadata, Instructions, Analysis, and SourceLink evidence at one IL coordinate.
/// </summary>
public sealed class ILOffsetProjection
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    public string? ILOffset { get; init; }
    public string? MatchedOffset { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
    public ILOffsetMemberContext? MemberContext { get; init; }
    public ILOffsetInstructionContext? InstructionContext { get; init; }
    public List<ILOffsetExceptionContext>? ExceptionContext { get; init; }
    public ILOffsetCallsiteContext? CallsiteContext { get; init; }
    public ILOffsetReturnAddressContext? ReturnAddressContext { get; init; }
    public List<ILOffsetAllocationContext>? AllocationContext { get; init; }
    public List<ILOffsetSafetyContext>? SafetyContext { get; init; }
    public List<ILOffsetCostContext>? CostContext { get; init; }
}

public sealed class ILOffsetMemberContext
{
    public string? Assembly { get; init; }
    public string? Type { get; init; }
    public string? TypeKind { get; init; }
    public string? Member { get; init; }
    public string? Signature { get; init; }
    public string? MemberKind { get; init; }
    public string? Visibility { get; init; }
    public string? Static { get; init; }
    public string? Async { get; init; }
    public string? MetadataToken { get; init; }
    public string? ILOffset { get; init; }
}

public sealed class ILOffsetInstructionContext
{
    public string? ILOffset { get; init; }
    public string? Boundary { get; init; }
    public string? Opcode { get; init; }
    public string? OperandKind { get; init; }
    public string? Operand { get; init; }
    public string? OperandToken { get; init; }
    public string? BranchTargets { get; init; }
    public string? NextOffset { get; init; }
    public int? Length { get; init; }
    public int? Block { get; init; }
    public string? TerminatesBlock { get; init; }
    public string? FallsThrough { get; init; }
}

public sealed class ILOffsetExceptionContext
{
    public int Region { get; init; }
    public string? Context { get; init; }
    public string? Clause { get; init; }
    public string? TryRange { get; init; }
    public string? HandlerRange { get; init; }
    public string? FilterRange { get; init; }
    public string? CaughtType { get; init; }
}

public sealed class ILOffsetCallsiteContext
{
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    public string? OperandToken { get; init; }
    public string? ReturnAddress { get; init; }
}

public sealed class ILOffsetReturnAddressContext
{
    public string? ILOffset { get; init; }
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    public string? OperandToken { get; init; }
}

public sealed class ILOffsetAllocationContext
{
    public string? ILOffset { get; init; }
    public string? AllocationKind { get; init; }
    public string? AllocatedType { get; init; }
    public string? CountedAsHeap { get; init; }
    public string? Frequency { get; init; }
    public string? Escape { get; init; }

    [JsonPropertyName("escape_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EscapeKind { get; init; }

    [JsonPropertyName("est_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EstimatedSizeBytes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SizeTier { get; init; }

    public string? InLoop { get; init; }
    public string? Path { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PathConfidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostDominance { get; init; }

    public string? Evidence { get; init; }

    [JsonPropertyName("multiplicity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Multiplicity { get; init; }

    [JsonPropertyName("churned_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChurnedType { get; init; }
}

public sealed class ILOffsetSafetyContext
{
    public string? ILOffset { get; init; }
    public string? SafetyKind { get; init; }
    public string? Operation { get; init; }
    public string? Requirement { get; init; }
    public string? Evidence { get; init; }
}

public sealed class ILOffsetCostContext
{
    public string? ILOffset { get; init; }
    public string? CostKind { get; init; }
    public string? Operation { get; init; }
    public string? InLoop { get; init; }
    public string? Evidence { get; init; }
}
