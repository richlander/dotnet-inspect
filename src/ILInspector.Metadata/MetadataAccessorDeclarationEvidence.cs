using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MetadataAccessorDeclarationKind
{
    Property,
    Event,
}

public readonly record struct MetadataAccessorDeclarationAddress(
    Guid ModuleVersionId,
    MetadataAccessorDeclarationKind Kind,
    int RowNumber)
{
    public static MetadataAccessorDeclarationAddress Create(
        MetadataReader reader,
        PropertyDefinitionHandle property)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(
            MetadataModuleIdentity.ReadVersionId(reader),
            MetadataAccessorDeclarationKind.Property,
            MetadataTokens.GetRowNumber(property));
    }

    public static MetadataAccessorDeclarationAddress Create(
        MetadataReader reader,
        EventDefinitionHandle @event)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(
            MetadataModuleIdentity.ReadVersionId(reader),
            MetadataAccessorDeclarationKind.Event,
            MetadataTokens.GetRowNumber(@event));
    }

    public bool TryResolve(
        MetadataReader reader,
        out EntityHandle handle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        handle = default;

        TableIndex table = Kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                TableIndex.Property,
            MetadataAccessorDeclarationKind.Event =>
                TableIndex.Event,
            _ => default,
        };
        if (!Enum.IsDefined(Kind)
            || RowNumber <= 0
            || RowNumber > reader.GetTableRowCount(table))
        {
            return false;
        }

        try
        {
            if (ModuleVersionId
                != MetadataModuleIdentity.ReadVersionId(reader))
            {
                return false;
            }
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }

        handle = Kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                MetadataTokens.PropertyDefinitionHandle(RowNumber),
            MetadataAccessorDeclarationKind.Event =>
                MetadataTokens.EventDefinitionHandle(RowNumber),
            _ => default,
        };
        return true;
    }
}

public readonly record struct MetadataAccessorDeclarationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataAccessorDeclarationAddress Declaration);

public enum MetadataAccessorSemanticsRole
{
    Getter,
    Setter,
    AddOn,
    RemoveOn,
    Fire,
    Other,
}

public sealed record MetadataAccessorSemanticsOccurrence(
    int PhysicalRowNumber,
    ushort RawSemantics,
    MetadataAccessorSemanticsRole Role,
    MetadataMethodDeclarationEvidence Method);

public sealed record MetadataAccessorDeclarationEvidence(
    MetadataTypeDefinitionAddress Type,
    MetadataAccessorDeclarationAddress Declaration,
    ImmutableArray<MetadataAccessorSemanticsOccurrence> Accessors);

public enum MetadataAccessorDeclarationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    BudgetExceeded,
    AccessorDeclarationRejected,
}

public enum MetadataAccessorDeclarationStage
{
    RequestValidation,
    AssociationCensus,
    AccessorDeclaration,
    ResultRetention,
}

public enum MetadataAccessorDeclarationMechanism
{
    ImageAdmission,
    AddressResolution,
    AssociationOrdering,
    RoleValidation,
    DuplicateRole,
    DirectOwnership,
    MethodDeclaration,
    StructuredRetention,
}

public sealed record MetadataAccessorDeclarationFailure(
    MetadataAccessorDeclarationRequest Request,
    MetadataAccessorDeclarationFailureReason Reason,
    MetadataAccessorDeclarationStage Stage,
    MetadataAccessorDeclarationMechanism Mechanism,
    string Detail,
    int? PhysicalRowNumber = null,
    ushort? RawSemantics = null,
    MetadataMethodAddress? Method = null,
    MetadataMethodSemanticsFailure? CensusFailure = null,
    MetadataMethodDeclarationFailure? AccessorFailure = null,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public abstract record MetadataAccessorDeclarationResult
{
    private protected MetadataAccessorDeclarationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Posted : MetadataAccessorDeclarationResult
    {
        internal Posted(
            MetadataAccessorDeclarationEvidence evidence,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public MetadataAccessorDeclarationEvidence Evidence { get; }
    }

    public sealed record Rejected : MetadataAccessorDeclarationResult
    {
        internal Rejected(
            MetadataAccessorDeclarationFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataAccessorDeclarationFailure Failure { get; }
    }
}

internal sealed class MetadataAccessorDeclarationEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly MethodSemanticsAssociationSession _associations;
    readonly Func<
        MetadataTypeDefinitionAddress,
        MetadataMethodAddress,
        CancellationToken,
        MetadataMethodDeclarationResult> _postMethod;

    internal MetadataAccessorDeclarationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        MethodSemanticsAssociationSession associations,
        Func<
            MetadataTypeDefinitionAddress,
            MetadataMethodAddress,
            CancellationToken,
            MetadataMethodDeclarationResult> postMethod)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(associations);
        ArgumentNullException.ThrowIfNull(postMethod);
        _reader = reader;
        _context = context;
        _associations = associations;
        _postMethod = postMethod;
    }

    internal MetadataAccessorDeclarationResult Post(
        MetadataAccessorDeclarationRequest request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var site = new MetadataAccessorDeclarationSite(
            MetadataAccessorDeclarationStage.RequestValidation,
            MetadataAccessorDeclarationMechanism.AddressResolution);
        try
        {
            if (!request.Type.TryResolve(
                    _reader,
                    out TypeDefinitionHandle type)
                || !request.Declaration.TryResolve(
                    _reader,
                    out EntityHandle declaration))
            {
                return Reject(
                    request,
                    MetadataAccessorDeclarationFailureReason.InvalidRequest,
                    MetadataAccessorDeclarationStage.RequestValidation,
                    MetadataAccessorDeclarationMechanism.AddressResolution,
                    "The requested declaring TypeDef or accessor aggregate does not resolve in this image.");
            }

            site = new(
                MetadataAccessorDeclarationStage.RequestValidation,
                MetadataAccessorDeclarationMechanism.AddressResolution);
            _context.Charge(
                MetadataOperationDimension.DeclarationCandidates);
            TypeDefinitionHandle actualOwner =
                ReadDeclarationOwner(request, declaration);
            if (actualOwner != type)
            {
                return Reject(
                    request,
                    MetadataAccessorDeclarationFailureReason.InvalidRequest,
                    MetadataAccessorDeclarationStage.RequestValidation,
                    MetadataAccessorDeclarationMechanism.DirectOwnership,
                    "The requested property or event is not directly declared by the requested TypeDef.");
            }

            MetadataMethodSemanticsAssociationResult census =
                _associations.Post();
            if (census
                is MetadataMethodSemanticsAssociationResult.Rejected
                    rejected)
            {
                MetadataMethodSemanticsFailure failure =
                    rejected.Failure;
                return Reject(
                    request,
                    failure.Reason
                        == MetadataMethodSemanticsFailureReason
                            .BudgetExceeded
                        ? MetadataAccessorDeclarationFailureReason
                            .BudgetExceeded
                        : MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                    MetadataAccessorDeclarationStage.AssociationCensus,
                    MetadataAccessorDeclarationMechanism.RoleValidation,
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
                    MetadataAccessorDeclarationFailureReason
                        .MalformedMetadata,
                    MetadataAccessorDeclarationStage.AssociationCensus,
                    MetadataAccessorDeclarationMechanism
                        .AssociationOrdering,
                    "The MethodSemantics associations are not in nondecreasing physical order.");
            }

            var pending =
                ImmutableArray.CreateBuilder<PendingOccurrence>();
            var conventionalRoles =
                new HashSet<MetadataAccessorSemanticsRole>();
            foreach (MetadataMethodSemanticsAssociation association
                in completed.Associations)
            {
                token.ThrowIfCancellationRequested();
                if (!Matches(request.Declaration, association))
                    continue;

                site = new(
                    MetadataAccessorDeclarationStage.AssociationCensus,
                    MetadataAccessorDeclarationMechanism.RoleValidation,
                    association.PhysicalRowNumber,
                    association.RawSemantics);
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                if (!TryDecodeRole(
                        request.Declaration.Kind,
                        association.RawSemantics,
                        out MetadataAccessorSemanticsRole role))
                {
                    return Reject(
                        request,
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                        MetadataAccessorDeclarationStage
                            .AssociationCensus,
                        MetadataAccessorDeclarationMechanism
                            .RoleValidation,
                        "The MethodSemantics row does not contain exactly one legal role for this association kind.",
                        association.PhysicalRowNumber,
                        association.RawSemantics);
                }

                if (role != MetadataAccessorSemanticsRole.Other
                    && !conventionalRoles.Add(role))
                {
                    return Reject(
                        request,
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                        MetadataAccessorDeclarationStage
                            .AssociationCensus,
                        MetadataAccessorDeclarationMechanism
                            .DuplicateRole,
                        "The accessor aggregate contains a duplicate conventional semantic role.",
                        association.PhysicalRowNumber,
                        association.RawSemantics);
                }

                MethodDefinition method =
                    _reader.GetMethodDefinition(association.Method);
                if (method.GetDeclaringType() != type)
                {
                    return Reject(
                        request,
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                        MetadataAccessorDeclarationStage
                            .AssociationCensus,
                        MetadataAccessorDeclarationMechanism
                            .DirectOwnership,
                        "The associated MethodDef is not directly declared by the aggregate's TypeDef.",
                        association.PhysicalRowNumber,
                        association.RawSemantics,
                        MetadataMethodAddress.Create(
                            _reader,
                            association.Method));
                }

                pending.Add(
                    new(
                        association.PhysicalRowNumber,
                        association.RawSemantics,
                        role,
                        MetadataMethodAddress.Create(
                            _reader,
                            association.Method)));
            }

            var methods =
                new Dictionary<
                    MetadataMethodAddress,
                    MetadataMethodDeclarationEvidence>();
            var accessors =
                ImmutableArray.CreateBuilder<
                    MetadataAccessorSemanticsOccurrence>(
                        pending.Count);
            foreach (PendingOccurrence occurrence in pending)
            {
                token.ThrowIfCancellationRequested();
                if (!methods.TryGetValue(
                        occurrence.Method,
                        out MetadataMethodDeclarationEvidence? evidence))
                {
                    MetadataMethodDeclarationResult methodResult =
                        _postMethod(
                            request.Type,
                            occurrence.Method,
                            token);
                    if (methodResult
                        is MetadataMethodDeclarationResult.Rejected
                            methodRejected)
                    {
                        MetadataMethodDeclarationFailure failure =
                            methodRejected.Failure;
                        return Reject(
                            request,
                            failure.Reason
                                == MetadataMethodDeclarationFailureReason
                                    .BudgetExceeded
                                ? MetadataAccessorDeclarationFailureReason
                                    .BudgetExceeded
                                : MetadataAccessorDeclarationFailureReason
                                    .AccessorDeclarationRejected,
                            MetadataAccessorDeclarationStage
                                .AccessorDeclaration,
                            MetadataAccessorDeclarationMechanism
                                .MethodDeclaration,
                            "An associated MethodDef declaration could not be posted.",
                            occurrence.PhysicalRowNumber,
                            occurrence.RawSemantics,
                            occurrence.Method,
                            accessorFailure: failure,
                            budgetDimension: failure.BudgetDimension,
                            budgetLimit: failure.BudgetLimit,
                            attemptedCharge: failure.AttemptedCharge);
                    }

                    evidence =
                        ((MetadataMethodDeclarationResult.Posted)methodResult)
                            .Evidence;
                    methods.Add(occurrence.Method, evidence);
                }

                site = new(
                    MetadataAccessorDeclarationStage.ResultRetention,
                    MetadataAccessorDeclarationMechanism
                        .StructuredRetention,
                    occurrence.PhysicalRowNumber,
                    occurrence.RawSemantics,
                    occurrence.Method);
                _context.Charge(
                    MetadataOperationDimension.StructuredNodes);
                accessors.Add(
                    new(
                        occurrence.PhysicalRowNumber,
                        occurrence.RawSemantics,
                        occurrence.Role,
                        evidence));
            }

            site = new(
                MetadataAccessorDeclarationStage.ResultRetention,
                MetadataAccessorDeclarationMechanism.StructuredRetention);
            _context.Charge(
                MetadataOperationDimension.StructuredNodes);
            _context.ObserveWork(
                MetadataOperationWorkKind
                    .AccessorDeclarationPublication);
            token.ThrowIfCancellationRequested();
            return new MetadataAccessorDeclarationResult.Posted(
                new(
                    request.Type,
                    request.Declaration,
                    accessors.MoveToImmutable()),
                _context.Counters);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            return Reject(
                request,
                MetadataAccessorDeclarationFailureReason.BudgetExceeded,
                site.Stage,
                site.Mechanism,
                "The metadata operation budget was exhausted while validating or constructing the accessor aggregate.",
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method,
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
                MetadataAccessorDeclarationFailureReason.MalformedMetadata,
                site.Stage,
                site.Mechanism,
                "The accessor aggregate could not be read from the admitted metadata.",
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method);
        }
    }

    TypeDefinitionHandle ReadDeclarationOwner(
        MetadataAccessorDeclarationRequest request,
        EntityHandle declaration) =>
        request.Declaration.Kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                _reader.GetPropertyDefinition(
                    (PropertyDefinitionHandle)declaration)
                    .GetDeclaringType(),
            MetadataAccessorDeclarationKind.Event =>
                _reader.GetEventDefinition(
                    (EventDefinitionHandle)declaration)
                    .GetDeclaringType(),
            _ => default,
        };

    static bool Matches(
        MetadataAccessorDeclarationAddress request,
        MetadataMethodSemanticsAssociation association) =>
        association.AssociationRowNumber == request.RowNumber
        && association.AssociationKind
            == (request.Kind switch
            {
                MetadataAccessorDeclarationKind.Property =>
                    MetadataMethodSemanticsAssociationKind.Property,
                MetadataAccessorDeclarationKind.Event =>
                    MetadataMethodSemanticsAssociationKind.Event,
                _ => default,
            });

    static bool TryDecodeRole(
        MetadataAccessorDeclarationKind kind,
        ushort raw,
        out MetadataAccessorSemanticsRole role)
    {
        role = default;
        return kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                TryDecodePropertyRole(raw, out role),
            MetadataAccessorDeclarationKind.Event =>
                TryDecodeEventRole(raw, out role),
            _ => false,
        };
    }

    static bool TryDecodePropertyRole(
        ushort raw,
        out MetadataAccessorSemanticsRole role)
    {
        role = raw switch
        {
            (ushort)MethodSemanticsAttributes.Getter =>
                MetadataAccessorSemanticsRole.Getter,
            (ushort)MethodSemanticsAttributes.Setter =>
                MetadataAccessorSemanticsRole.Setter,
            (ushort)MethodSemanticsAttributes.Other =>
                MetadataAccessorSemanticsRole.Other,
            _ => default,
        };
        return raw is
            (ushort)MethodSemanticsAttributes.Getter
            or (ushort)MethodSemanticsAttributes.Setter
            or (ushort)MethodSemanticsAttributes.Other;
    }

    static bool TryDecodeEventRole(
        ushort raw,
        out MetadataAccessorSemanticsRole role)
    {
        role = raw switch
        {
            (ushort)MethodSemanticsAttributes.Adder =>
                MetadataAccessorSemanticsRole.AddOn,
            (ushort)MethodSemanticsAttributes.Remover =>
                MetadataAccessorSemanticsRole.RemoveOn,
            (ushort)MethodSemanticsAttributes.Raiser =>
                MetadataAccessorSemanticsRole.Fire,
            (ushort)MethodSemanticsAttributes.Other =>
                MetadataAccessorSemanticsRole.Other,
            _ => default,
        };
        return raw is
            (ushort)MethodSemanticsAttributes.Adder
            or (ushort)MethodSemanticsAttributes.Remover
            or (ushort)MethodSemanticsAttributes.Raiser
            or (ushort)MethodSemanticsAttributes.Other;
    }

    MetadataAccessorDeclarationResult.Rejected Reject(
        MetadataAccessorDeclarationRequest request,
        MetadataAccessorDeclarationFailureReason reason,
        MetadataAccessorDeclarationStage stage,
        MetadataAccessorDeclarationMechanism mechanism,
        string detail,
        int? physicalRowNumber = null,
        ushort? rawSemantics = null,
        MetadataMethodAddress? method = null,
        MetadataMethodSemanticsFailure? censusFailure = null,
        MetadataMethodDeclarationFailure? accessorFailure = null,
        MetadataOperationDimension? budgetDimension = null,
        long? budgetLimit = null,
        long? attemptedCharge = null) =>
        new(
            new(
                request,
                reason,
                stage,
                mechanism,
                detail,
                physicalRowNumber,
                rawSemantics,
                method,
                censusFailure,
                accessorFailure,
                budgetDimension,
                budgetLimit,
                attemptedCharge),
            _context.Counters);

    readonly record struct PendingOccurrence(
        int PhysicalRowNumber,
        ushort RawSemantics,
        MetadataAccessorSemanticsRole Role,
        MetadataMethodAddress Method);

    readonly record struct MetadataAccessorDeclarationSite(
        MetadataAccessorDeclarationStage Stage,
        MetadataAccessorDeclarationMechanism Mechanism,
        int? PhysicalRowNumber = null,
        ushort? RawSemantics = null,
        MetadataMethodAddress? Method = null);
}
