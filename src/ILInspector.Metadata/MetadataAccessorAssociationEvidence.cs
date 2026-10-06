using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public readonly record struct MetadataAccessorAssociationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method);

public enum MetadataAccessorAssociationFailureReason
{
    MalformedMetadata,
    AmbiguousAssociation,
    BudgetExceeded,
    SessionUnavailable,
}

public enum MetadataAccessorAssociationStage
{
    RequestValidation,
    AssociationCensus,
    AssociationResolution,
    ResultRetention,
}

public enum MetadataAccessorAssociationMechanism
{
    ImageAdmission,
    AddressResolution,
    DirectOwnership,
    AssociationOrdering,
    RelationshipLookup,
    RoleValidation,
    StructuredRetention,
}

public sealed record MetadataAccessorAssociationFailure(
    MetadataAccessorAssociationRequest Request,
    MetadataAccessorAssociationFailureReason Reason,
    MetadataAccessorAssociationStage Stage,
    MetadataAccessorAssociationMechanism Mechanism,
    string Detail,
    int? PhysicalRowNumber = null,
    ushort? RawSemantics = null,
    int? ObservedAssociationCount = null,
    MetadataMethodSemanticsFailure? CensusFailure = null,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public sealed record MetadataAccessorAssociationCertificate(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method,
    MetadataAccessorDeclarationAddress Declaration,
    int PhysicalRowNumber,
    ushort RawSemantics,
    MetadataAccessorSemanticsRole Role);

public abstract record MetadataAccessorAssociationResult
{
    private protected MetadataAccessorAssociationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Related
        : MetadataAccessorAssociationResult
    {
        internal Related(
            MetadataAccessorAssociationCertificate certificate,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            Certificate = certificate;
        }

        public MetadataAccessorAssociationCertificate Certificate { get; }
    }

    public sealed record Absent
        : MetadataAccessorAssociationResult
    {
        internal Absent(MetadataOperationCounters counters)
            : base(counters)
        {
        }
    }

    public sealed record Rejected
        : MetadataAccessorAssociationResult
    {
        internal Rejected(
            MetadataAccessorAssociationFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataAccessorAssociationFailure Failure { get; }
    }
}

internal sealed class MetadataAccessorAssociationEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly MethodSemanticsAssociationSession _associations;

    internal MetadataAccessorAssociationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        MethodSemanticsAssociationSession associations)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(associations);
        _reader = reader;
        _context = context;
        _associations = associations;
    }

    internal MetadataAccessorAssociationResult Relate(
        MetadataAccessorAssociationRequest request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var site = new MetadataAccessorAssociationSite(
            MetadataAccessorAssociationStage.RequestValidation,
            MetadataAccessorAssociationMechanism.AddressResolution);
        try
        {
            if (!request.Type.TryResolve(
                    _reader,
                    out TypeDefinitionHandle type)
                || !request.Method.TryResolve(
                    _reader,
                    out MethodDefinitionHandle method))
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .SessionUnavailable,
                    site,
                    "The requested TypeDef or MethodDef does not resolve in this image.");
            }

            _context.Charge(
                MetadataOperationDimension.DeclarationCandidates);
            MethodDefinition definition =
                _reader.GetMethodDefinition(method);
            if (definition.GetDeclaringType() != type)
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .SessionUnavailable,
                    site with
                    {
                        Mechanism =
                            MetadataAccessorAssociationMechanism
                                .DirectOwnership,
                    },
                    "The requested MethodDef is not directly declared by the requested TypeDef.");
            }

            MetadataMethodSemanticsAssociationResult census =
                _associations.Post();
            if (census
                is MetadataMethodSemanticsAssociationResult.Rejected
                    censusRejected)
            {
                MetadataMethodSemanticsFailure failure =
                    censusRejected.Failure;
                return Reject(
                    request,
                    failure.Reason
                        == MetadataMethodSemanticsFailureReason
                            .BudgetExceeded
                        ? MetadataAccessorAssociationFailureReason
                            .BudgetExceeded
                        : MetadataAccessorAssociationFailureReason
                            .MalformedMetadata,
                    new(
                        MetadataAccessorAssociationStage
                            .AssociationCensus,
                        MetadataAccessorAssociationMechanism
                            .RelationshipLookup,
                        failure.PhysicalRowNumber),
                    "The complete MethodSemantics census was rejected.",
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
                    MetadataAccessorAssociationFailureReason
                        .MalformedMetadata,
                    new(
                        MetadataAccessorAssociationStage
                            .AssociationCensus,
                        MetadataAccessorAssociationMechanism
                            .AssociationOrdering),
                    "The MethodSemantics associations are not in nondecreasing physical order.");
            }

            site = new(
                MetadataAccessorAssociationStage.AssociationResolution,
                MetadataAccessorAssociationMechanism.RelationshipLookup);
            ImmutableArray<int> indexes =
                completed.FindMethodIndexes(
                    method,
                    () => _context.ObserveWork(
                        MetadataOperationWorkKind
                            .AccessorReverseLookupProbe),
                    token);
            if (indexes.IsDefaultOrEmpty)
            {
                return new MetadataAccessorAssociationResult.Absent(
                    _context.Counters);
            }

            _context.Charge(
                MetadataOperationDimension.RelationshipEdges,
                indexes.Length);
            MetadataMethodSemanticsAssociation first =
                completed.Associations[indexes[0]];
            site = site with
            {
                PhysicalRowNumber = first.PhysicalRowNumber,
                RawSemantics = first.RawSemantics,
            };
            if (indexes.Length != 1)
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .AmbiguousAssociation,
                    site,
                    "The requested MethodDef has multiple physical MethodSemantics associations.",
                    observedAssociationCount: indexes.Length);
            }

            MetadataAccessorDeclarationKind declarationKind =
                first.AssociationKind switch
                {
                    MetadataMethodSemanticsAssociationKind.Property =>
                        MetadataAccessorDeclarationKind.Property,
                    MetadataMethodSemanticsAssociationKind.Event =>
                        MetadataAccessorDeclarationKind.Event,
                    _ => throw new InvalidOperationException(
                        "Unknown MethodSemantics association kind."),
                };
            if (!MetadataAccessorSemanticsRoleDecoder.TryDecode(
                    declarationKind,
                    first.RawSemantics,
                    out MetadataAccessorSemanticsRole role))
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .MalformedMetadata,
                    site with
                    {
                        Mechanism =
                            MetadataAccessorAssociationMechanism
                                .RoleValidation,
                    },
                    "The MethodSemantics row does not contain exactly one legal role for its association kind.");
            }

            MetadataAccessorDeclarationAddress declaration =
                new(
                    request.Type.ModuleVersionId,
                    declarationKind,
                    first.AssociationRowNumber);
            if (!declaration.TryResolve(
                    _reader,
                    out EntityHandle aggregate))
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .MalformedMetadata,
                    site with
                    {
                        Mechanism =
                            MetadataAccessorAssociationMechanism
                                .AddressResolution,
                    },
                    "The MethodSemantics aggregate address does not resolve in this image.");
            }

            TypeDefinitionHandle aggregateOwner =
                declarationKind switch
                {
                    MetadataAccessorDeclarationKind.Property =>
                        _reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)aggregate)
                            .GetDeclaringType(),
                    MetadataAccessorDeclarationKind.Event =>
                        _reader.GetEventDefinition(
                            (EventDefinitionHandle)aggregate)
                            .GetDeclaringType(),
                    _ => default,
                };
            if (aggregateOwner != type)
            {
                return Reject(
                    request,
                    MetadataAccessorAssociationFailureReason
                        .MalformedMetadata,
                    site with
                    {
                        Mechanism =
                            MetadataAccessorAssociationMechanism
                                .DirectOwnership,
                    },
                    "The associated property or event is not directly declared by the MethodDef's TypeDef.");
            }

            site = new(
                MetadataAccessorAssociationStage.ResultRetention,
                MetadataAccessorAssociationMechanism
                    .StructuredRetention,
                first.PhysicalRowNumber,
                first.RawSemantics);
            _context.Charge(
                MetadataOperationDimension.StructuredNodes);
            _context.ObserveWork(
                MetadataOperationWorkKind
                    .AccessorAssociationPublication);
            token.ThrowIfCancellationRequested();
            return new MetadataAccessorAssociationResult.Related(
                new(
                    request.Type,
                    request.Method,
                    declaration,
                    first.PhysicalRowNumber,
                    first.RawSemantics,
                    role),
                _context.Counters);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            return Reject(
                request,
                MetadataAccessorAssociationFailureReason.BudgetExceeded,
                site,
                "The metadata operation budget was exhausted while resolving the accessor association.",
                budgetDimension: ex.Dimension,
                budgetLimit: ex.Limit,
                attemptedCharge: ex.AttemptedCharge);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return Reject(
                request,
                MetadataAccessorAssociationFailureReason.MalformedMetadata,
                site,
                "The accessor association could not be read from the admitted metadata image.");
        }
    }

    MetadataAccessorAssociationResult.Rejected Reject(
        MetadataAccessorAssociationRequest request,
        MetadataAccessorAssociationFailureReason reason,
        MetadataAccessorAssociationSite site,
        string detail,
        int? observedAssociationCount = null,
        MetadataMethodSemanticsFailure? censusFailure = null,
        MetadataOperationDimension? budgetDimension = null,
        long? budgetLimit = null,
        long? attemptedCharge = null) =>
        new(
            new(
                request,
                reason,
                site.Stage,
                site.Mechanism,
                detail,
                site.PhysicalRowNumber,
                site.RawSemantics,
                observedAssociationCount,
                censusFailure,
                budgetDimension,
                budgetLimit,
                attemptedCharge),
            _context.Counters);

    readonly record struct MetadataAccessorAssociationSite(
        MetadataAccessorAssociationStage Stage,
        MetadataAccessorAssociationMechanism Mechanism,
        int? PhysicalRowNumber = null,
        ushort? RawSemantics = null);
}
