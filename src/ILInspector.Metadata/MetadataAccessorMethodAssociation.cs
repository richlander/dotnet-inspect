using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public readonly record struct MetadataAccessorMethodAssociationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method);

public enum MetadataAccessorMethodAssociationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    BudgetExceeded,
    AmbiguousAssociation,
}

public enum MetadataAccessorMethodAssociationStage
{
    RequestValidation,
    AssociationCensus,
    AssociationValidation,
}

public enum MetadataAccessorMethodAssociationMechanism
{
    ImageAdmission,
    AddressResolution,
    DirectOwnership,
    AssociationOrdering,
    AssociationLookup,
    RoleValidation,
}

public sealed record MetadataAccessorMethodAssociationFailure(
    MetadataAccessorMethodAssociationRequest Request,
    MetadataAccessorMethodAssociationFailureReason Reason,
    MetadataAccessorMethodAssociationStage Stage,
    MetadataAccessorMethodAssociationMechanism Mechanism,
    string Detail,
    int? PhysicalRowNumber = null,
    ushort? RawSemantics = null,
    MetadataAccessorDeclarationAddress? RelevantDeclaration = null,
    MetadataMethodSemanticsFailure? CensusFailure = null,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public sealed record MetadataAccessorMethodAssociationEvidence(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method,
    MetadataAccessorDeclarationAddress Declaration,
    MetadataAccessorSemanticsRole Role,
    int PhysicalRowNumber,
    ushort RawSemantics);

public abstract record MetadataAccessorMethodAssociationResult
{
    private protected MetadataAccessorMethodAssociationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Related : MetadataAccessorMethodAssociationResult
    {
        internal Related(
            MetadataAccessorMethodAssociationEvidence evidence,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public MetadataAccessorMethodAssociationEvidence Evidence { get; }
    }

    public sealed record Absent : MetadataAccessorMethodAssociationResult
    {
        internal Absent(MetadataOperationCounters counters)
            : base(counters)
        {
        }
    }

    public sealed record Rejected : MetadataAccessorMethodAssociationResult
    {
        internal Rejected(
            MetadataAccessorMethodAssociationFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataAccessorMethodAssociationFailure Failure { get; }
    }
}

internal sealed class MetadataAccessorMethodAssociationOperation(
    MetadataReader reader,
    MetadataOperationContext context,
    MethodSemanticsAssociationSession associations)
{
    readonly MetadataReader _reader =
        reader ?? throw new ArgumentNullException(nameof(reader));
    readonly MetadataOperationContext _context =
        context ?? throw new ArgumentNullException(nameof(context));
    readonly MethodSemanticsAssociationSession _associations =
        associations ?? throw new ArgumentNullException(nameof(associations));

    internal MetadataAccessorMethodAssociationResult Relate(
        MetadataAccessorMethodAssociationRequest request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!request.Type.TryResolve(
                _reader,
                out TypeDefinitionHandle type)
            || !request.Method.TryResolve(
                _reader,
                out MethodDefinitionHandle method))
        {
            return Reject(
                request,
                MetadataAccessorMethodAssociationFailureReason
                    .InvalidRequest,
                MetadataAccessorMethodAssociationStage.RequestValidation,
                MetadataAccessorMethodAssociationMechanism.AddressResolution,
                "The requested TypeDef or MethodDef does not resolve in this image.");
        }

        try
        {
            _context.Charge(
                MetadataOperationDimension.DeclarationCandidates);
            _context.Charge(
                MetadataOperationDimension.RelationshipEdges);
            if (_reader.GetMethodDefinition(method).GetDeclaringType()
                != type)
            {
                return Reject(
                    request,
                    MetadataAccessorMethodAssociationFailureReason
                        .InvalidRequest,
                    MetadataAccessorMethodAssociationStage
                        .RequestValidation,
                    MetadataAccessorMethodAssociationMechanism
                        .DirectOwnership,
                    "The MethodDef is not directly declared by the requested TypeDef.");
            }
        }
        catch (MetadataOperationBudgetExceededException exceeded)
        {
            return Reject(
                request,
                exceeded,
                MetadataAccessorMethodAssociationStage.RequestValidation,
                MetadataAccessorMethodAssociationMechanism.DirectOwnership,
                "The MethodDef owner validation exceeded its budget.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return Reject(
                request,
                MetadataAccessorMethodAssociationFailureReason
                    .MalformedMetadata,
                MetadataAccessorMethodAssociationStage.RequestValidation,
                MetadataAccessorMethodAssociationMechanism.DirectOwnership,
                "The requested MethodDef owner could not be read.");
        }

        MetadataMethodSemanticsAssociationResult census =
            _associations.Post();
        if (census
            is MetadataMethodSemanticsAssociationResult.Rejected rejected)
        {
            MetadataMethodSemanticsFailure failure = rejected.Failure;
            return Reject(
                request,
                failure.Reason
                    == MetadataMethodSemanticsFailureReason.BudgetExceeded
                    ? MetadataAccessorMethodAssociationFailureReason
                        .BudgetExceeded
                    : MetadataAccessorMethodAssociationFailureReason
                        .MalformedMetadata,
                MetadataAccessorMethodAssociationStage.AssociationCensus,
                MetadataAccessorMethodAssociationMechanism
                    .AssociationLookup,
                "The complete MethodSemantics census was rejected.",
                physicalRowNumber: failure.PhysicalRowNumber,
                censusFailure: failure,
                budgetDimension: failure.BudgetDimension,
                budgetLimit: failure.BudgetLimit,
                attemptedCharge: failure.AttemptedCharge);
        }

        var completed =
            (MetadataMethodSemanticsAssociationResult.Completed)census;
        if (!completed.AssociationsAreNondecreasing)
        {
            return Reject(
                request,
                MetadataAccessorMethodAssociationFailureReason
                    .MalformedMetadata,
                MetadataAccessorMethodAssociationStage.AssociationCensus,
                MetadataAccessorMethodAssociationMechanism
                    .AssociationOrdering,
                "The MethodSemantics associations are not in nondecreasing physical order.");
        }

        ImmutableArray<int> indexes =
            completed.FindMethodAssociations(
                method,
                () => _context.ObserveWork(
                    MetadataOperationWorkKind
                        .AccessorMethodAssociationLookupProbe),
                token);
        if (indexes.IsEmpty)
            return new MetadataAccessorMethodAssociationResult.Absent(
                _context.Counters);

        MetadataAccessorMethodAssociationEvidence? evidence = null;
        foreach (int index in indexes)
        {
            token.ThrowIfCancellationRequested();
            MetadataMethodSemanticsAssociation association =
                completed.Associations[index];
            MetadataAccessorDeclarationKind kind =
                association.AssociationKind switch
                {
                    MetadataMethodSemanticsAssociationKind.Property =>
                        MetadataAccessorDeclarationKind.Property,
                    MetadataMethodSemanticsAssociationKind.Event =>
                        MetadataAccessorDeclarationKind.Event,
                    _ => throw new InvalidOperationException(
                        "Unknown MethodSemantics association kind."),
                };
            MetadataAccessorDeclarationAddress declaration =
                kind switch
                {
                    MetadataAccessorDeclarationKind.Property =>
                        MetadataAccessorDeclarationAddress.Create(
                            _reader,
                            MetadataTokens.PropertyDefinitionHandle(
                                association.AssociationRowNumber)),
                    MetadataAccessorDeclarationKind.Event =>
                        MetadataAccessorDeclarationAddress.Create(
                            _reader,
                            MetadataTokens.EventDefinitionHandle(
                                association.AssociationRowNumber)),
                    _ => throw new InvalidOperationException(
                        "Unknown accessor declaration kind."),
                };

            if (!MetadataAccessorSemantics.TryDecodeRole(
                    kind,
                    association.RawSemantics,
                    out MetadataAccessorSemanticsRole role))
            {
                return Reject(
                    request,
                    MetadataAccessorMethodAssociationFailureReason
                        .MalformedMetadata,
                    MetadataAccessorMethodAssociationStage
                        .AssociationValidation,
                    MetadataAccessorMethodAssociationMechanism
                        .RoleValidation,
                    "The MethodSemantics row does not contain exactly one legal role for this association kind.",
                    association.PhysicalRowNumber,
                    association.RawSemantics,
                    declaration);
            }

            try
            {
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                TypeDefinitionHandle owner = kind switch
                {
                    MetadataAccessorDeclarationKind.Property =>
                        _reader.GetPropertyDefinition(
                            MetadataTokens.PropertyDefinitionHandle(
                                association.AssociationRowNumber))
                            .GetDeclaringType(),
                    MetadataAccessorDeclarationKind.Event =>
                        _reader.GetEventDefinition(
                            MetadataTokens.EventDefinitionHandle(
                                association.AssociationRowNumber))
                            .GetDeclaringType(),
                    _ => default,
                };
                if (owner != type)
                {
                    return Reject(
                        request,
                        MetadataAccessorMethodAssociationFailureReason
                            .MalformedMetadata,
                        MetadataAccessorMethodAssociationStage
                            .AssociationValidation,
                        MetadataAccessorMethodAssociationMechanism
                            .DirectOwnership,
                        "The associated property or event is not directly declared by the MethodDef's TypeDef.",
                        association.PhysicalRowNumber,
                        association.RawSemantics,
                        declaration);
                }
            }
            catch (MetadataOperationBudgetExceededException exceeded)
            {
                return Reject(
                    request,
                    exceeded,
                    MetadataAccessorMethodAssociationStage
                        .AssociationValidation,
                    MetadataAccessorMethodAssociationMechanism
                        .DirectOwnership,
                    "The accessor aggregate owner validation exceeded its budget.",
                    association.PhysicalRowNumber,
                    association.RawSemantics,
                    declaration);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return Reject(
                    request,
                    MetadataAccessorMethodAssociationFailureReason
                        .MalformedMetadata,
                    MetadataAccessorMethodAssociationStage
                        .AssociationValidation,
                    MetadataAccessorMethodAssociationMechanism
                        .DirectOwnership,
                    "The associated property or event owner could not be read.",
                    association.PhysicalRowNumber,
                    association.RawSemantics,
                    declaration);
            }

            evidence ??= new(
                request.Type,
                request.Method,
                declaration,
                role,
                association.PhysicalRowNumber,
                association.RawSemantics);
        }

        if (indexes.Length > 1)
        {
            return Reject(
                request,
                MetadataAccessorMethodAssociationFailureReason
                    .AmbiguousAssociation,
                MetadataAccessorMethodAssociationStage
                    .AssociationValidation,
                MetadataAccessorMethodAssociationMechanism
                    .AssociationLookup,
                "The MethodDef participates in more than one physical MethodSemantics association.",
                evidence!.PhysicalRowNumber,
                evidence.RawSemantics,
                evidence.Declaration);
        }

        _context.ObserveWork(
            MetadataOperationWorkKind
                .AccessorMethodAssociationPublication);
        return new MetadataAccessorMethodAssociationResult.Related(
            evidence!,
            _context.Counters);
    }

    MetadataAccessorMethodAssociationResult.Rejected Reject(
        MetadataAccessorMethodAssociationRequest request,
        MetadataOperationBudgetExceededException exceeded,
        MetadataAccessorMethodAssociationStage stage,
        MetadataAccessorMethodAssociationMechanism mechanism,
        string detail,
        int? physicalRowNumber = null,
        ushort? rawSemantics = null,
        MetadataAccessorDeclarationAddress? relevantDeclaration = null) =>
        Reject(
            request,
            MetadataAccessorMethodAssociationFailureReason.BudgetExceeded,
            stage,
            mechanism,
            detail,
            physicalRowNumber,
            rawSemantics,
            relevantDeclaration,
            budgetDimension: exceeded.Dimension,
            budgetLimit: exceeded.Limit,
            attemptedCharge: exceeded.AttemptedCharge);

    MetadataAccessorMethodAssociationResult.Rejected Reject(
        MetadataAccessorMethodAssociationRequest request,
        MetadataAccessorMethodAssociationFailureReason reason,
        MetadataAccessorMethodAssociationStage stage,
        MetadataAccessorMethodAssociationMechanism mechanism,
        string detail,
        int? physicalRowNumber = null,
        ushort? rawSemantics = null,
        MetadataAccessorDeclarationAddress? relevantDeclaration = null,
        MetadataMethodSemanticsFailure? censusFailure = null,
        MetadataOperationDimension? budgetDimension = null,
        long? budgetLimit = null,
        long? attemptedCharge = null) =>
        new(
            new MetadataAccessorMethodAssociationFailure(
                request,
                reason,
                stage,
                mechanism,
                detail,
                physicalRowNumber,
                rawSemantics,
                relevantDeclaration,
                censusFailure,
                budgetDimension,
                budgetLimit,
                attemptedCharge),
            _context.Counters);
}
