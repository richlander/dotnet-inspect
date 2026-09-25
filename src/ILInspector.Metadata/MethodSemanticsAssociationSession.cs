using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MetadataMethodSemanticsAssociationKind
{
    Event,
    Property,
}

public readonly record struct MetadataMethodSemanticsAssociation(
    int PhysicalRowNumber,
    ushort RawSemantics,
    MethodDefinitionHandle Method,
    MetadataMethodSemanticsAssociationKind AssociationKind,
    int AssociationRowNumber);

public enum MetadataMethodSemanticsFailureReason
{
    BudgetExceeded,
    NoMetadata,
    UnsupportedWindowsMetadata,
    MetadataRootMalformed,
    MetadataReaderRejected,
    InvalidTableLayout,
    NilMethod,
    MethodOutOfRange,
    NilAssociation,
    AssociationOutOfRange,
}

public sealed record MetadataMethodSemanticsFailure(
    MetadataMethodSemanticsFailureReason Reason,
    string Detail,
    int RowsVisited,
    int? PhysicalRowNumber = null,
    MetadataRootMalformedReason? MetadataRootReason = null,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public abstract record MetadataMethodSemanticsAssociationResult
{
    private protected MetadataMethodSemanticsAssociationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Completed
        : MetadataMethodSemanticsAssociationResult
    {
        internal Completed(
            ImmutableArray<MetadataMethodSemanticsAssociation> associations,
            bool associationsAreNondecreasing,
            MetadataOperationCounters counters)
            : base(counters)
        {
            Associations = associations;
            AssociationsAreNondecreasing =
                associationsAreNondecreasing;
        }

        public ImmutableArray<MetadataMethodSemanticsAssociation>
            Associations { get; }

        public bool AssociationsAreNondecreasing { get; }
    }

    public sealed record Rejected
        : MetadataMethodSemanticsAssociationResult
    {
        internal Rejected(
            MetadataMethodSemanticsFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataMethodSemanticsFailure Failure { get; }
    }
}

public sealed class MethodSemanticsAssociationSession
{
    readonly object _gate = new();
    MetadataDeclarationSession? _owner;
    MetadataMethodSemanticsAssociationResult? _cached;

    internal MethodSemanticsAssociationSession(
        MetadataDeclarationSession owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public MetadataMethodSemanticsAssociationResult Post()
    {
        MetadataDeclarationSession owner = GetOwner();
        owner.EnsureAccessForMethodSemantics();

        lock (_gate)
        {
            owner = GetOwner();
            owner.EnsureAccessForMethodSemantics();
            return _cached ??= PostCold(owner);
        }
    }

    MetadataMethodSemanticsAssociationResult PostCold(
        MetadataDeclarationSession owner)
    {
        MetadataOperationContext operation =
            owner.OperationContextForMethodSemantics;
        if (owner.ImageAdmissionForMethodSemantics
            is MetadataImageAdmissionResult.Rejected rejected)
        {
            return new MetadataMethodSemanticsAssociationResult.Rejected(
                new MetadataMethodSemanticsFailure(
                    MetadataMethodSemanticsFailureReason.BudgetExceeded,
                    "The metadata image was not admitted.",
                    RowsVisited: 0,
                    BudgetDimension:
                        MetadataOperationDimension.MetadataRows,
                    BudgetLimit:
                        rejected.Failure.MaxMetadataRows,
                    AttemptedCharge:
                        rejected.Failure.ImageMetadataRows),
                operation.Counters);
        }

        operation.ObserveWork(
            MetadataOperationWorkKind.MethodSemanticsAssociationRead);
        MethodSemanticsReadBudget budget =
            operation.CreateMethodSemanticsReadBudget();
        MethodSemanticsReadResult result =
            MethodSemanticsRowReader.Read(
                owner.PEReaderForMethodSemantics,
                budget);
        operation.Charge(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            budget.RetainedAssociations);

        return result switch
        {
            MethodSemanticsReadResult.Success success =>
                Complete(success, operation.Counters),
            MethodSemanticsReadResult.NoMetadata =>
                Reject(
                    MetadataMethodSemanticsFailureReason.NoMetadata,
                    "The image has no managed metadata.",
                    operation.Counters),
            MethodSemanticsReadResult.UnsupportedWindowsMetadata =>
                Reject(
                    MetadataMethodSemanticsFailureReason
                        .UnsupportedWindowsMetadata,
                    "Windows Metadata is not supported.",
                    operation.Counters),
            MethodSemanticsReadResult.MalformedInput malformed =>
                Reject(malformed, operation.Counters),
            MethodSemanticsReadResult
                .RetainedAssociationBudgetExceeded exceeded =>
                Reject(
                    exceeded,
                    operation.MethodSemanticsAssociationLimit,
                    operation.Counters),
            _ => throw new InvalidOperationException(
                "Unknown MethodSemantics read result."),
        };
    }

    static MetadataMethodSemanticsAssociationResult.Completed Complete(
        MethodSemanticsReadResult.Success success,
        MetadataOperationCounters counters)
    {
        var associations =
            ImmutableArray.CreateBuilder<
                MetadataMethodSemanticsAssociation>(
                    success.Rows.Length);
        foreach (MethodSemanticsRow row in success.Rows)
        {
            associations.Add(
                new MetadataMethodSemanticsAssociation(
                    row.RowNumber,
                    row.RawSemantics,
                    row.Method,
                    row.AssociationKind switch
                    {
                        MethodSemanticsAssociationKind.Event =>
                            MetadataMethodSemanticsAssociationKind.Event,
                        MethodSemanticsAssociationKind.Property =>
                            MetadataMethodSemanticsAssociationKind.Property,
                        _ => throw new InvalidOperationException(
                            "Unknown MethodSemantics association kind."),
                    },
                    row.AssociationRowNumber));
        }

        return new MetadataMethodSemanticsAssociationResult.Completed(
            associations.MoveToImmutable(),
            success.AssociationsAreNondecreasing,
            counters);
    }

    static MetadataMethodSemanticsAssociationResult.Rejected Reject(
        MetadataMethodSemanticsFailureReason reason,
        string detail,
        MetadataOperationCounters counters) =>
        new(
            new MetadataMethodSemanticsFailure(
                reason,
                detail,
                RowsVisited: 0),
            counters);

    static MetadataMethodSemanticsAssociationResult.Rejected Reject(
        MethodSemanticsReadResult.MalformedInput malformed,
        MetadataOperationCounters counters) =>
        new(
            new MetadataMethodSemanticsFailure(
                malformed.Reason switch
                {
                    MethodSemanticsMalformedReason.MetadataRootMalformed =>
                        MetadataMethodSemanticsFailureReason
                            .MetadataRootMalformed,
                    MethodSemanticsMalformedReason.MetadataReaderRejected =>
                        MetadataMethodSemanticsFailureReason
                            .MetadataReaderRejected,
                    MethodSemanticsMalformedReason.InvalidTableLayout =>
                        MetadataMethodSemanticsFailureReason
                            .InvalidTableLayout,
                    MethodSemanticsMalformedReason.NilMethod =>
                        MetadataMethodSemanticsFailureReason.NilMethod,
                    MethodSemanticsMalformedReason.MethodOutOfRange =>
                        MetadataMethodSemanticsFailureReason
                            .MethodOutOfRange,
                    MethodSemanticsMalformedReason.NilAssociation =>
                        MetadataMethodSemanticsFailureReason.NilAssociation,
                    MethodSemanticsMalformedReason.AssociationOutOfRange =>
                        MetadataMethodSemanticsFailureReason
                            .AssociationOutOfRange,
                    _ => throw new InvalidOperationException(
                        "Unknown MethodSemantics malformed reason."),
                },
                "The MethodSemantics table could not be read mechanically.",
                malformed.RowsVisited,
                malformed.RowNumber,
                malformed.MetadataRootReason),
            counters);

    static MetadataMethodSemanticsAssociationResult.Rejected Reject(
        MethodSemanticsReadResult.RetainedAssociationBudgetExceeded exceeded,
        long limit,
        MetadataOperationCounters counters) =>
        new(
            new MetadataMethodSemanticsFailure(
                MetadataMethodSemanticsFailureReason.BudgetExceeded,
                "The retained MethodSemantics association budget was exhausted.",
                exceeded.RowsVisited,
                exceeded.RowsVisited,
                BudgetDimension:
                    MetadataOperationDimension
                        .RetainedMethodSemanticsAssociations,
                BudgetLimit: limit,
                AttemptedCharge: 1),
            counters);

    MetadataDeclarationSession GetOwner() =>
        _owner
        ?? throw new ObjectDisposedException(
            nameof(MethodSemanticsAssociationSession));

    internal void Retire()
    {
        lock (_gate)
        {
            _cached = null;
            _owner = null;
        }
    }
}
