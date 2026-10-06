using System.Collections.Immutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public enum AnalysisLibraryBodyUseDisposition
{
    Complete,
    Qualified,
    Partial,
}

public enum AnalysisLibraryBodyUseOperandKind
{
    Call,
    Constructor,
    MethodReference,
    GenericMethodInstantiation,
    Field,
    Type,
    Array,
    Cast,
    TypeTest,
    Box,
    Unbox,
    Constrained,
    TypeToken,
    MethodToken,
    FieldToken,
}

public enum AnalysisLibraryBodyUseFidelity
{
    LogicalOwner,
    PhysicalOnly,
}

public enum AnalysisLibraryBodyUseDiagnosticKind
{
    MalformedBody,
    UnresolvedOperand,
    UnavailableLogicalOwner,
    Limit,
}

public enum AnalysisLibraryBodyUseRejectionKind
{
    UnsupportedImage,
    MalformedImage,
    MissingAssemblyIdentity,
    TypeInventory,
    Limit,
    Planning,
    Execution,
}

public sealed record AnalysisLibraryBodyUseLimits(
    int MaximumTypeDefinitions = 1_000_000,
    int MaximumRetainedTextCharacters = 16_000_000,
    int MaximumInstructionsPerBody = 1_000_000,
    int MaximumOccurrences = 10_000_000,
    int MaximumMethodSignatureBytes = 16_000_000)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumTypeDefinitions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumRetainedTextCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumInstructionsPerBody);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumOccurrences);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumMethodSignatureBytes);
    }
}

public sealed record AnalysisLibraryBodyUseRequest
{
    public AnalysisLibraryBodyUseRequest(
        AnalysisLibraryBodyUseLimits? limits = null)
    {
        Limits = limits ?? new AnalysisLibraryBodyUseLimits();
        Limits.Validate();
    }

    public AnalysisLibraryBodyUseLimits Limits { get; }
}

public sealed record AnalysisLibraryBodyUseReceipt(
    Guid ModuleVersionId,
    AssemblyReferenceIdentity Assembly,
    WorkReceipt Work);

public sealed record AnalysisLibraryBodyUseType(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    AssemblyTypeDefinitionKind DefinitionKind);

public sealed record AnalysisLibraryBodyUseOccurrence(
    MetadataTypeDefinitionAddress Source,
    MetadataTypeDefinitionName SourceType,
    MetadataTypeDefinitionAddress Target,
    MetadataTypeDefinitionName TargetType,
    int PhysicalMethodToken,
    AnalysisLibraryBodyUseOperandKind OperandKind,
    int OperandToken,
    int IlOffset,
    int OccurrenceOrdinal);

public sealed record AnalysisLibraryBodyUsePhysicalEvidence(
    MetadataTypeDefinitionAddress PhysicalSource,
    MetadataTypeDefinitionName PhysicalSourceType,
    int PhysicalMethodToken,
    AnalysisLibraryBodyUseFidelity Fidelity);

public sealed record AnalysisLibraryBodyUseCoverage
{
    public AnalysisLibraryBodyUseCoverage(
        int bodiesConsidered,
        int bodiesExamined,
        int bodiesPhysicalOnly,
        int bodiesUnavailable,
        int bodiesLimited,
        int operandsConsidered,
        int operandsExamined,
        int operandsUnavailable,
        int operandsLimited)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodiesConsidered);
        ArgumentOutOfRangeException.ThrowIfNegative(bodiesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(bodiesPhysicalOnly);
        ArgumentOutOfRangeException.ThrowIfNegative(bodiesUnavailable);
        ArgumentOutOfRangeException.ThrowIfNegative(bodiesLimited);
        ArgumentOutOfRangeException.ThrowIfNegative(operandsConsidered);
        ArgumentOutOfRangeException.ThrowIfNegative(operandsExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(operandsUnavailable);
        ArgumentOutOfRangeException.ThrowIfNegative(operandsLimited);
        if (bodiesExamined + bodiesPhysicalOnly + bodiesUnavailable
                + bodiesLimited
            != bodiesConsidered)
        {
            throw new ArgumentException(
                "Library body-use coverage must account for every body.");
        }
        if (operandsExamined + operandsUnavailable + operandsLimited
            != operandsConsidered)
        {
            throw new ArgumentException(
                "Library body-use coverage must account for every operand.");
        }

        BodiesConsidered = bodiesConsidered;
        BodiesExamined = bodiesExamined;
        BodiesPhysicalOnly = bodiesPhysicalOnly;
        BodiesUnavailable = bodiesUnavailable;
        BodiesLimited = bodiesLimited;
        OperandsConsidered = operandsConsidered;
        OperandsExamined = operandsExamined;
        OperandsUnavailable = operandsUnavailable;
        OperandsLimited = operandsLimited;
    }

    public int BodiesConsidered { get; }
    public int BodiesExamined { get; }
    public int BodiesPhysicalOnly { get; }
    public int BodiesUnavailable { get; }
    public int BodiesLimited { get; }
    public int OperandsConsidered { get; }
    public int OperandsExamined { get; }
    public int OperandsUnavailable { get; }
    public int OperandsLimited { get; }
}

public sealed record AnalysisLibraryBodyUseDiagnostic(
    AnalysisLibraryBodyUseDiagnosticKind Kind,
    int? MethodToken,
    int? IlOffset,
    string Detail,
    long? Limit = null,
    long? AttemptedCharge = null);

public sealed record AnalysisLibraryBodyUseResult(
    AnalysisLibraryBodyUseReceipt Receipt,
    AnalysisLibraryBodyUseDisposition Disposition,
    ImmutableArray<AnalysisLibraryBodyUseType> Types,
    ImmutableArray<AnalysisLibraryBodyUseOccurrence> Occurrences,
    ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> PhysicalEvidence,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public abstract record AnalysisLibraryBodyUseOutcome
{
    private protected AnalysisLibraryBodyUseOutcome()
    {
    }

    public sealed record Available(AnalysisLibraryBodyUseResult Result)
        : AnalysisLibraryBodyUseOutcome;

    public sealed record Rejected(
        AnalysisLibraryBodyUseRejectionKind Kind,
        string Detail)
        : AnalysisLibraryBodyUseOutcome;
}
