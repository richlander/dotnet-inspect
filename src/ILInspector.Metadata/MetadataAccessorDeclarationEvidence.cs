using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;
using InertText;
using InertText.Encoding;

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

internal static class MetadataAccessorSemanticsRoleDecoder
{
    internal static bool TryDecode(
        MetadataAccessorDeclarationKind kind,
        ushort raw,
        out MetadataAccessorSemanticsRole role)
    {
        role = default;
        return kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                TryDecodeProperty(raw, out role),
            MetadataAccessorDeclarationKind.Event =>
                TryDecodeEvent(raw, out role),
            _ => false,
        };
    }

    static bool TryDecodeProperty(
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

    static bool TryDecodeEvent(
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
}

public enum MetadataEventTypeCategoryStatus
{
    ConfirmedDelegate,
    Unavailable,
}

public enum MetadataAccessorOrdinaryCallableStatus
{
    Ordinary,
    NonOrdinary,
}

public enum MetadataAccessorRoleCorrespondenceStatus
{
    NotApplicable,
    Exact,
    Mismatch,
}

public enum MetadataAccessorPrerequisiteStatus
{
    NotApplicable,
    Satisfied,
    Unavailable,
}

public enum MetadataPropertyAccessorMultiplicityStatus
{
    NotApplicable,
    Conventional,
    NonConventional,
}

public sealed record MetadataPropertySignatureIdentity(
    byte Header,
    int GenericParameterCount,
    int RequiredParameterCount,
    MetadataTypeIdentity ValueType,
    ImmutableArray<MetadataTypeIdentity> IndexParameterTypes)
{
    public bool Equals(MetadataPropertySignatureIdentity? other) =>
        other is not null
        && Header == other.Header
        && GenericParameterCount == other.GenericParameterCount
        && RequiredParameterCount == other.RequiredParameterCount
        && Equals(ValueType, other.ValueType)
        && MetadataIdentitySequence.Equal(
            IndexParameterTypes,
            other.IndexParameterTypes);

    public override int GetHashCode() =>
        MetadataIdentitySequence.Hash(
            Header,
            GenericParameterCount,
            RequiredParameterCount,
            ValueType,
            MetadataIdentitySequence.Hash(IndexParameterTypes));
}

public abstract record MetadataAccessorRootDeclarationEvidence(
    InertString Name)
{
    public sealed record Property(
        InertString Name,
        PropertyAttributes Attributes,
        MetadataPropertySignatureIdentity Signature)
        : MetadataAccessorRootDeclarationEvidence(Name);

    public sealed record Event(
        InertString Name,
        EventAttributes Attributes,
        MetadataTypeIdentity EventType,
        MetadataEventTypeCategoryStatus TypeCategory)
        : MetadataAccessorRootDeclarationEvidence(Name);
}

public sealed record MetadataAccessorRoleCorrespondenceEvidence(
    MetadataAccessorRoleCorrespondenceStatus Status,
    bool? ReturnTypeMatches,
    bool? ParameterTypesMatch,
    bool? ValueTypeMatches,
    bool? InstanceMatches);

public sealed record MetadataAccessorOccurrenceCorrespondenceEvidence(
    MetadataAccessorOrdinaryCallableStatus OrdinaryCallable,
    MetadataAccessorRoleCorrespondenceEvidence Role,
    MetadataAccessorPrerequisiteStatus Prerequisite);

public sealed record MetadataAccessorAggregateCorrespondenceEvidence(
    MetadataPropertyAccessorMultiplicityStatus PropertyMultiplicity,
    bool? EventAddRemoveStaticnessMatches);

public sealed record MetadataAccessorDeclarationSafetyEvidence(
    ApiTypeLayout DeclaringTypeLayout,
    ApiModuleMemorySafetyFacts Module,
    ApiMemberMemorySafetyFacts Declaration);

public sealed record MetadataAccessorSemanticsOccurrence(
    int PhysicalRowNumber,
    ushort RawSemantics,
    MetadataAccessorSemanticsRole Role,
    MetadataMethodDeclarationEvidence Method,
    MetadataAccessorOccurrenceCorrespondenceEvidence Correspondence,
    ApiMemberMemorySafetyFacts MemorySafety);

public sealed record MetadataAccessorDeclarationEvidence(
    MetadataTypeDefinitionAddress Type,
    MetadataAccessorDeclarationAddress Declaration,
    MetadataAccessorRootDeclarationEvidence Root,
    MetadataAccessorDeclarationSafetyEvidence Safety,
    MetadataAccessorAggregateCorrespondenceEvidence Correspondence,
    ImmutableArray<MetadataAccessorSemanticsOccurrence> Accessors);

public enum MetadataAccessorDeclarationFailureReason
{
    MalformedMetadata,
    RelationshipTraversal,
    BudgetExceeded,
    SessionUnavailable,
}

public enum MetadataAccessorDeclarationStage
{
    RequestValidation,
    RootDeclaration,
    AssociationCensus,
    AccessorDeclaration,
    ConsistencyValidation,
    SafetyEvidence,
    ResultRetention,
}

public enum MetadataAccessorDeclarationMechanism
{
    ImageAdmission,
    AddressResolution,
    RowRead,
    SignatureDecode,
    RelationshipTraversal,
    TextRetention,
    AssociationOrdering,
    RoleValidation,
    RoleCardinality,
    DirectOwnership,
    MethodDeclaration,
    SignatureCorrespondence,
    TypeCategory,
    MemorySafety,
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
    readonly Func<
        MetadataTypeDefinitionAddress,
        CancellationToken,
        MetadataTypeDeclarationResult> _postType;
    readonly Func<
        Action,
        Action<TypeDefinitionHandle>,
        Action,
        Action<int>,
        MetadataTypeDefinitionIndex> _getIndex;
    readonly Func<MemorySafetyMetadataIndex> _getMemorySafetyIndex;
    readonly HashSet<TypeSpecificationHandle> _validatedSpecs = [];
    CancellationToken _token;

    internal MetadataAccessorDeclarationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        MethodSemanticsAssociationSession associations,
        Func<
            MetadataTypeDefinitionAddress,
            MetadataMethodAddress,
            CancellationToken,
            MetadataMethodDeclarationResult> postMethod,
        Func<
            MetadataTypeDefinitionAddress,
            CancellationToken,
            MetadataTypeDeclarationResult> postType,
        Func<
            Action,
            Action<TypeDefinitionHandle>,
            Action,
            Action<int>,
            MetadataTypeDefinitionIndex> getIndex,
        Func<MemorySafetyMetadataIndex> getMemorySafetyIndex)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(associations);
        ArgumentNullException.ThrowIfNull(postMethod);
        ArgumentNullException.ThrowIfNull(postType);
        ArgumentNullException.ThrowIfNull(getIndex);
        ArgumentNullException.ThrowIfNull(getMemorySafetyIndex);
        _reader = reader;
        _context = context;
        _associations = associations;
        _postMethod = postMethod;
        _postType = postType;
        _getIndex = getIndex;
        _getMemorySafetyIndex = getMemorySafetyIndex;
    }

    internal MetadataAccessorDeclarationResult Post(
        MetadataAccessorDeclarationRequest request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _token = token;
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
                    MetadataAccessorDeclarationFailureReason
                        .SessionUnavailable,
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
                    MetadataAccessorDeclarationFailureReason
                        .SessionUnavailable,
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
            int addCount = 0;
            int removeCount = 0;
            int fireCount = 0;
            MetadataMethodSemanticsAssociationKind associationKind =
                request.Declaration.Kind switch
                {
                    MetadataAccessorDeclarationKind.Property =>
                        MetadataMethodSemanticsAssociationKind.Property,
                    MetadataAccessorDeclarationKind.Event =>
                        MetadataMethodSemanticsAssociationKind.Event,
                    _ => throw new InvalidOperationException(
                        "Unknown accessor declaration kind."),
                };
            MetadataMethodSemanticsAssociationRange range =
                completed.FindRange(
                    associationKind,
                    request.Declaration.RowNumber,
                    () => _context.ObserveWork(
                        MetadataOperationWorkKind
                            .AccessorAssociationLookupProbe),
                    token);
            for (int index = range.Start;
                index < range.Start + range.Count;
                index++)
            {
                token.ThrowIfCancellationRequested();
                MetadataMethodSemanticsAssociation association =
                    completed.Associations[index];

                site = new(
                    MetadataAccessorDeclarationStage.AssociationCensus,
                    MetadataAccessorDeclarationMechanism.RoleValidation,
                    association.PhysicalRowNumber,
                    association.RawSemantics);
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                if (!MetadataAccessorSemanticsRoleDecoder.TryDecode(
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

                switch (role)
                {
                    case MetadataAccessorSemanticsRole.AddOn:
                        addCount++;
                        break;
                    case MetadataAccessorSemanticsRole.RemoveOn:
                        removeCount++;
                        break;
                    case MetadataAccessorSemanticsRole.Fire:
                        fireCount++;
                        break;
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
            var resolved =
                ImmutableArray.CreateBuilder<
                    ResolvedOccurrence>(
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
                                switch
                                {
                                    MetadataMethodDeclarationFailureReason
                                        .BudgetExceeded =>
                                            MetadataAccessorDeclarationFailureReason
                                                .BudgetExceeded,
                                    MetadataMethodDeclarationFailureReason
                                        .Cycle =>
                                            MetadataAccessorDeclarationFailureReason
                                                .RelationshipTraversal,
                                    _ =>
                                        MetadataAccessorDeclarationFailureReason
                                            .MalformedMetadata,
                                },
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
                resolved.Add(
                    new(
                        occurrence.PhysicalRowNumber,
                        occurrence.RawSemantics,
                        occurrence.Role,
                        evidence));
            }

            TypeDefinition owner = _reader.GetTypeDefinition(type);
            MetadataAccessorRootDeclarationEvidence root =
                ReadRoot(
                    request,
                    declaration,
                    owner,
                    token,
                    ref site);
            if (request.Declaration.Kind
                    == MetadataAccessorDeclarationKind.Event
                && (addCount != 1
                    || removeCount != 1
                    || fireCount > 1))
            {
                return Reject(
                    request,
                    MetadataAccessorDeclarationFailureReason
                        .MalformedMetadata,
                    MetadataAccessorDeclarationStage.AssociationCensus,
                    MetadataAccessorDeclarationMechanism.RoleCardinality,
                    "An event aggregate requires exactly one add occurrence, exactly one remove occurrence, and at most one raise occurrence.");
            }
            AccessorCorrespondenceRead correspondence =
                ReadCorrespondence(
                    root,
                    resolved,
                    owner,
                    ref site);

            site = new(
                MetadataAccessorDeclarationStage.SafetyEvidence,
                MetadataAccessorDeclarationMechanism.MemorySafety);
            MemorySafetyMetadataIndex memorySafety =
                _getMemorySafetyIndex();
            Guid moduleVersionId =
                request.Declaration.ModuleVersionId;
            _context.ObserveWork(
                MetadataOperationWorkKind
                    .AccessorSafetyEvidenceRead);
            var safety = new MetadataAccessorDeclarationSafetyEvidence(
                (ApiTypeLayout)(
                    owner.Attributes & TypeAttributes.LayoutMask),
                new(moduleVersionId, memorySafety.Rules),
                ApiMemorySafetyFacts.Read(
                    _reader,
                    memorySafety,
                    moduleVersionId,
                    declaration));
            var accessors =
                ImmutableArray.CreateBuilder<
                    MetadataAccessorSemanticsOccurrence>(
                        correspondence.Occurrences.Length);
            foreach (ClassifiedOccurrence occurrence
                in correspondence.Occurrences)
            {
                token.ThrowIfCancellationRequested();
                _context.ObserveWork(
                    MetadataOperationWorkKind
                        .AccessorSafetyEvidenceRead);
                _context.Charge(
                    MetadataOperationDimension.StructuredNodes);
                accessors.Add(
                    new(
                        occurrence.PhysicalRowNumber,
                        occurrence.RawSemantics,
                        occurrence.Role,
                        occurrence.Method,
                        occurrence.Correspondence,
                        ApiMemorySafetyFacts.Read(
                            _reader,
                            memorySafety,
                            moduleVersionId,
                            occurrence.Method.Method.Handle)));
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
                    root,
                    safety,
                    correspondence.Aggregate,
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
        catch (AccessorDeclarationRejectedException ex)
        {
            return Reject(
                request,
                ex.Reason,
                site.Stage,
                site.Mechanism,
                ex.Message,
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method);
        }
        catch (GenericContextRelationshipRejectedException ex)
        {
            return Reject(
                request,
                ex.Rejection.Kind is
                    RelationshipTraversalRejectionKind.NodeBudget
                        or RelationshipTraversalRejectionKind.NameBudget
                    ? MetadataAccessorDeclarationFailureReason
                        .BudgetExceeded
                    : ex.Rejection.Kind
                        == RelationshipTraversalRejectionKind.Cycle
                            ? MetadataAccessorDeclarationFailureReason
                                .RelationshipTraversal
                            : MetadataAccessorDeclarationFailureReason
                                .MalformedMetadata,
                site.Stage,
                MetadataAccessorDeclarationMechanism
                    .RelationshipTraversal,
                ex.Message,
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method);
        }
        catch (GenericContextBudgetExceededException ex)
        {
            return Reject(
                request,
                MetadataAccessorDeclarationFailureReason
                    .BudgetExceeded,
                site.Stage,
                site.Mechanism,
                ex.Message,
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return Reject(
                request,
                MetadataAccessorDeclarationFailureReason.MalformedMetadata,
                site.Stage,
                site.Mechanism,
                ex.Message,
                site.PhysicalRowNumber,
                site.RawSemantics,
                site.Method);
        }
    }

    MetadataAccessorRootDeclarationEvidence ReadRoot(
        MetadataAccessorDeclarationRequest request,
        EntityHandle declaration,
        TypeDefinition owner,
        CancellationToken token,
        ref MetadataAccessorDeclarationSite site)
    {
        token.ThrowIfCancellationRequested();
        site = new(
            MetadataAccessorDeclarationStage.RootDeclaration,
            MetadataAccessorDeclarationMechanism.RowRead);
        GenericContext generic = GenericContext.ForTypeWithRelationshipObserver(
            _reader,
            owner,
            amount => _context.Charge(
                MetadataOperationDimension.StructuredNodes,
                amount),
            value => _context.Charge(
                MetadataOperationDimension.RetainedText,
                VisualEncoder.MeasureEncodedLength(
                    TextPolicy.Field,
                    value)),
            _ => _context.Charge(
                MetadataOperationDimension.RelationshipEdges));
        return request.Declaration.Kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                ReadPropertyRoot(
                    (PropertyDefinitionHandle)declaration,
                    generic,
                    ref site),
            MetadataAccessorDeclarationKind.Event =>
                ReadEventRoot(
                    (EventDefinitionHandle)declaration,
                    generic,
                    ref site),
            _ => throw new BadImageFormatException(
                "The accessor declaration kind is invalid."),
        };
    }

    MetadataAccessorRootDeclarationEvidence.Property ReadPropertyRoot(
        PropertyDefinitionHandle handle,
        GenericContext generic,
        ref MetadataAccessorDeclarationSite site)
    {
        PropertyDefinition definition =
            _reader.GetPropertyDefinition(handle);
        PropertyAttributes attributes = definition.Attributes;
        const PropertyAttributes allowedAttributes =
            PropertyAttributes.SpecialName
            | PropertyAttributes.RTSpecialName
            | PropertyAttributes.HasDefault;
        if ((attributes & ~allowedAttributes) != 0)
        {
            throw new BadImageFormatException(
                "The PropertyDef attributes contain reserved flags.");
        }
        StringHandle nameHandle = definition.Name;
        BlobHandle signatureBlob = definition.Signature;
        site = site with
        {
            Mechanism =
                MetadataAccessorDeclarationMechanism.SignatureDecode,
        };
        _context.Charge(
            MetadataOperationDimension.SignatureBytes,
            _reader.GetBlobReader(signatureBlob).Length);
        SignatureBlobGuard.CompleteValidationKind validation =
            SignatureBlobGuard.ValidateComplete(
                _reader,
                signatureBlob,
                SignatureBlobGuard.Kind.Property);
        if (validation
            != SignatureBlobGuard.CompleteValidationKind.Valid)
        {
            throw new AccessorDeclarationRejectedException(
                validation is SignatureBlobGuard.CompleteValidationKind
                        .DepthBudgetExceeded
                    or SignatureBlobGuard.CompleteValidationKind
                        .NodeBudgetExceeded
                    ? MetadataAccessorDeclarationFailureReason
                        .BudgetExceeded
                    : MetadataAccessorDeclarationFailureReason
                        .MalformedMetadata,
                "The PropertyDef signature is incomplete or exceeds structural limits.");
        }

        var decoded = GuardedProviderDecode.PropertyResult(
            _reader,
            definition,
            Provider(site),
            generic,
            (TypeNode)new DegradedTypeNode());
        MethodSignature<TypeNode> signature = decoded.Value;
        if (decoded.IsDegraded
            || signature.ReturnType.IsDegraded
            || signature.ParameterTypes.Any(
                static parameter => parameter.IsDegraded))
        {
            throw new BadImageFormatException(
                "The PropertyDef signature is not a complete ordinary property signature.");
        }

        string? signatureFailure =
            MetadataStructuralTypeValidator.ValidatePropertySignature(
                signature,
                generic.TypeParameters.Count,
                "The PropertyDef signature");
        if (signatureFailure is not null)
            throw new BadImageFormatException(signatureFailure);

        var parameters =
            ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
                signature.ParameterTypes.Length);
        foreach (TypeNode parameter in signature.ParameterTypes)
            parameters.Add(Project(parameter, site));
        var identity = new MetadataPropertySignatureIdentity(
            signature.Header.RawValue,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            Project(signature.ReturnType, site),
            parameters.MoveToImmutable());
        site = site with
        {
            Mechanism =
                MetadataAccessorDeclarationMechanism.TextRetention,
        };
        return new(
            Retain(ReadName(nameHandle)),
            attributes,
            identity);
    }

    MetadataAccessorRootDeclarationEvidence.Event ReadEventRoot(
        EventDefinitionHandle handle,
        GenericContext generic,
        ref MetadataAccessorDeclarationSite site)
    {
        EventDefinition definition =
            _reader.GetEventDefinition(handle);
        EventAttributes attributes = definition.Attributes;
        const EventAttributes allowedAttributes =
            EventAttributes.SpecialName
            | EventAttributes.RTSpecialName;
        if ((attributes & ~allowedAttributes) != 0)
        {
            throw new BadImageFormatException(
                "The EventDef attributes contain reserved flags.");
        }
        StringHandle nameHandle = definition.Name;
        EntityHandle eventTypeHandle = definition.Type;
        if (!IsValidType(eventTypeHandle))
        {
            throw new BadImageFormatException(
                "The EventDef type is not a valid TypeDef, TypeRef, or TypeSpec.");
        }

        site = site with
        {
            Mechanism =
                MetadataAccessorDeclarationMechanism.SignatureDecode,
        };
        TypeNode eventType = DecodeType(eventTypeHandle, generic, site);
        string? eventTypeFailure =
            MetadataStructuralTypeValidator.ValidateTypeSignature(
                eventType,
                generic.TypeParameters.Count,
                generic.MethodParameters.Count,
                "The EventDef type");
        if (eventTypeFailure is not null)
            throw new BadImageFormatException(eventTypeFailure);

        MetadataTypeIdentity identity = Project(eventType, site);
        site = site with
        {
            Mechanism =
                MetadataAccessorDeclarationMechanism.TypeCategory,
        };
        MetadataEventTypeCategoryStatus typeCategory =
            ReadEventTypeCategory(eventType, site);
        site = site with
        {
            Mechanism =
                MetadataAccessorDeclarationMechanism.TextRetention,
        };
        return new(
            Retain(ReadName(nameHandle)),
            attributes,
            identity,
            typeCategory);
    }

    AccessorCorrespondenceRead ReadCorrespondence(
        MetadataAccessorRootDeclarationEvidence root,
        ImmutableArray<ResolvedOccurrence>.Builder accessors,
        TypeDefinition owner,
        ref MetadataAccessorDeclarationSite site)
    {
        site = new(
            MetadataAccessorDeclarationStage.ConsistencyValidation,
            MetadataAccessorDeclarationMechanism
                .SignatureCorrespondence);
        var classified =
            ImmutableArray.CreateBuilder<ClassifiedOccurrence>(
                accessors.Count);
        foreach (ResolvedOccurrence accessor in accessors)
        {
            site = site with
            {
                PhysicalRowNumber = accessor.PhysicalRowNumber,
                RawSemantics = accessor.RawSemantics,
                Method = accessor.Method.Method,
            };
            DecodedAccessorSignature decoded =
                DecodeAccessorSignature(
                    accessor.Method.Method.Handle,
                    owner,
                    site);
            string? signatureFailure =
                MetadataStructuralTypeValidator
                    .ValidateMethodSignature(
                        decoded.Signature,
                        decoded.TypeParameterCount,
                        decoded.MethodParameterCount,
                        "The conventional accessor");
            if (signatureFailure is not null)
                throw new BadImageFormatException(signatureFailure);
            if (IsStatic(accessor.Method.Attributes)
                == decoded.Signature.Header.IsInstance)
            {
                throw new BadImageFormatException(
                    "An accessor MethodDef Static flag contradicts its signature instance bit.");
            }

            MetadataAccessorRoleCorrespondenceEvidence role =
                root switch
                {
                    MetadataAccessorRootDeclarationEvidence.Property
                        property =>
                        PropertyAccessorCorrespondence(
                            accessor.Role,
                            accessor.Method.Signature,
                            property.Signature),
                    MetadataAccessorRootDeclarationEvidence.Event
                        eventRoot =>
                        EventAccessorCorrespondence(
                            accessor.Role,
                            accessor.Method.Signature,
                            eventRoot.EventType),
                    _ => throw new InvalidOperationException(
                        "Unknown accessor root evidence."),
                };
            MetadataAccessorPrerequisiteStatus prerequisite =
                root is MetadataAccessorRootDeclarationEvidence.Event @event
                    && accessor.Role is (
                        MetadataAccessorSemanticsRole.AddOn
                        or MetadataAccessorSemanticsRole.RemoveOn)
                    ? @event.TypeCategory
                        == MetadataEventTypeCategoryStatus.ConfirmedDelegate
                            ? MetadataAccessorPrerequisiteStatus.Satisfied
                            : MetadataAccessorPrerequisiteStatus.Unavailable
                    : MetadataAccessorPrerequisiteStatus.NotApplicable;
            _context.Charge(
                MetadataOperationDimension.StructuredNodes,
                2);
            classified.Add(
                new(
                    accessor.PhysicalRowNumber,
                    accessor.RawSemantics,
                    accessor.Role,
                    accessor.Method,
                    new(
                        IsOrdinaryCallable(
                            decoded.Signature,
                            accessor.Method.Attributes)
                            ? MetadataAccessorOrdinaryCallableStatus.Ordinary
                            : MetadataAccessorOrdinaryCallableStatus
                                .NonOrdinary,
                        role,
                        prerequisite)));
        }

        MetadataPropertyAccessorMultiplicityStatus propertyMultiplicity =
            root is MetadataAccessorRootDeclarationEvidence.Property
                ? classified.Count(accessor =>
                        accessor.Role
                            == MetadataAccessorSemanticsRole.Getter) <= 1
                    && classified.Count(accessor =>
                        accessor.Role
                            == MetadataAccessorSemanticsRole.Setter) <= 1
                        ? MetadataPropertyAccessorMultiplicityStatus
                            .Conventional
                        : MetadataPropertyAccessorMultiplicityStatus
                            .NonConventional
                : MetadataPropertyAccessorMultiplicityStatus.NotApplicable;
        bool? eventStaticnessMatches = null;
        if (root is MetadataAccessorRootDeclarationEvidence.Event)
        {
            ClassifiedOccurrence add = classified.Single(accessor =>
                accessor.Role == MetadataAccessorSemanticsRole.AddOn);
            ClassifiedOccurrence remove = classified.Single(accessor =>
                accessor.Role == MetadataAccessorSemanticsRole.RemoveOn);
            eventStaticnessMatches =
                IsStatic(add.Method.Attributes)
                == IsStatic(remove.Method.Attributes);
        }

        _context.Charge(
            MetadataOperationDimension.StructuredNodes);
        return new(
            new(
                propertyMultiplicity,
                eventStaticnessMatches),
            classified.MoveToImmutable());

        static bool IsOrdinaryCallable(
            MethodSignature<TypeNode> signature,
            MethodAttributes attributes)
        {
            SignatureHeader header = signature.Header;
            return !header.HasExplicitThis
                && !header.IsGeneric
                && header.CallingConvention
                    == SignatureCallingConvention.Default
                && signature.GenericParameterCount == 0
                && signature.RequiredParameterCount
                    == signature.ParameterTypes.Length
                && IsStatic(attributes) != header.IsInstance;
        }

        static bool IsStatic(MethodAttributes attributes) =>
            (attributes & MethodAttributes.Static) != 0;

        DecodedAccessorSignature DecodeAccessorSignature(
            MethodDefinitionHandle handle,
            TypeDefinition owner,
            MetadataAccessorDeclarationSite site)
        {
            MethodDefinition definition =
                _reader.GetMethodDefinition(handle);
            GenericContext generic =
                GenericContext.ForMethodWithRelationshipObserver(
                    _reader,
                    owner,
                    definition,
                    amount => _context.Charge(
                        MetadataOperationDimension.StructuredNodes,
                        amount),
                    value => _context.Charge(
                        MetadataOperationDimension.RetainedText,
                        VisualEncoder.MeasureEncodedLength(
                            TextPolicy.Field,
                            value)),
                    _ => _context.Charge(
                        MetadataOperationDimension.RelationshipEdges));
            BlobHandle signatureBlob = definition.Signature;
            _context.Charge(
                MetadataOperationDimension.SignatureBytes,
                _reader.GetBlobReader(signatureBlob).Length);
            var decoded = GuardedProviderDecode.MethodResult(
                _reader,
                definition,
                Provider(site),
                generic,
                (TypeNode)new DegradedTypeNode());
            if (decoded.IsDegraded
                || decoded.Value.ReturnType.IsDegraded
                || decoded.Value.ParameterTypes.Any(
                    static parameter => parameter.IsDegraded))
            {
                throw new BadImageFormatException(
                    "An accessor MethodDef signature cannot be decoded completely.");
            }
            return new(
                decoded.Value,
                generic.TypeParameters.Count,
                generic.MethodParameters.Count);
        }
    }

    static MetadataAccessorRoleCorrespondenceEvidence
        PropertyAccessorCorrespondence(
            MetadataAccessorSemanticsRole role,
            MetadataMethodSignatureIdentity accessor,
            MetadataPropertySignatureIdentity property)
    {
        if (role is not (
                MetadataAccessorSemanticsRole.Getter
                or MetadataAccessorSemanticsRole.Setter))
        {
            return NotApplicableCorrespondence();
        }

        bool returnMatches;
        bool parameterTypesMatch;
        bool? valueTypeMatches;
        if (role == MetadataAccessorSemanticsRole.Getter)
        {
            returnMatches =
                Equals(accessor.ReturnType, property.ValueType);
            parameterTypesMatch =
                MetadataIdentitySequence.Equal(
                    accessor.ParameterTypes,
                    property.IndexParameterTypes);
            valueTypeMatches = null;
        }
        else
        {
            returnMatches = IsVoid(accessor.ReturnType);
            int indexCount = property.IndexParameterTypes.Length;
            parameterTypesMatch =
                accessor.ParameterTypes.Length >= indexCount
                && MetadataIdentitySequence.Equal(
                    accessor.ParameterTypes[..indexCount],
                    property.IndexParameterTypes);
            valueTypeMatches =
                accessor.ParameterTypes.Length == indexCount + 1
                && Equals(
                    accessor.ParameterTypes[^1],
                    property.ValueType);
        }

        bool instanceMatches =
            new SignatureHeader(accessor.Header).IsInstance
            == new SignatureHeader(property.Header).IsInstance;
        bool exact =
            returnMatches
            && parameterTypesMatch
            && valueTypeMatches is not false
            && instanceMatches;
        return new(
            exact
                ? MetadataAccessorRoleCorrespondenceStatus.Exact
                : MetadataAccessorRoleCorrespondenceStatus.Mismatch,
            returnMatches,
            parameterTypesMatch,
            valueTypeMatches,
            instanceMatches);
    }

    static MetadataAccessorRoleCorrespondenceEvidence
        EventAccessorCorrespondence(
            MetadataAccessorSemanticsRole role,
            MetadataMethodSignatureIdentity accessor,
            MetadataTypeIdentity eventType)
    {
        if (role is not (
                MetadataAccessorSemanticsRole.AddOn
                or MetadataAccessorSemanticsRole.RemoveOn))
        {
            return NotApplicableCorrespondence();
        }

        bool returnMatches = IsVoid(accessor.ReturnType);
        bool parameterTypesMatch =
            accessor.ParameterTypes.Length == 1
            && Equals(accessor.ParameterTypes[0], eventType);
        return new(
            returnMatches && parameterTypesMatch
                ? MetadataAccessorRoleCorrespondenceStatus.Exact
                : MetadataAccessorRoleCorrespondenceStatus.Mismatch,
            returnMatches,
            parameterTypesMatch,
            ValueTypeMatches: null,
            InstanceMatches: null);
    }

    static MetadataAccessorRoleCorrespondenceEvidence
        NotApplicableCorrespondence() =>
        new(
            MetadataAccessorRoleCorrespondenceStatus.NotApplicable,
            ReturnTypeMatches: null,
            ParameterTypesMatch: null,
            ValueTypeMatches: null,
            InstanceMatches: null);

    MetadataEventTypeCategoryStatus ReadEventTypeCategory(
        TypeNode eventType,
        MetadataAccessorDeclarationSite site)
    {
        while (eventType is ModifiedTypeNode modified)
            eventType = modified.Inner;

        MetadataTypeNameParts definition;
        MetadataTypeScopeDescriptor scope;
        bool isValueType;
        switch (eventType)
        {
            case NamedTypeNode named:
                definition = named.MetadataName
                    ?? throw new BadImageFormatException(
                        "The EventDef type lacks a complete definition name.");
                scope = named.ExactScope
                    ?? throw new BadImageFormatException(
                        "The EventDef type lacks an exact definition scope.");
                isValueType = !named.IsReferenceType;
                break;
            case GenericTypeNode generic:
                definition = generic.MetadataName
                    ?? throw new BadImageFormatException(
                        "The EventDef type lacks a complete definition name.");
                scope = generic.ExactScope
                    ?? throw new BadImageFormatException(
                        "The EventDef type lacks an exact definition scope.");
                isValueType = !generic.IsReferenceType;
                break;
            case GenericParameterNode:
                return MetadataEventTypeCategoryStatus.Unavailable;
            default:
                throw new BadImageFormatException(
                    "The EventDef type is not a delegate type.");
        }

        if (isValueType)
        {
            throw new BadImageFormatException(
                "The EventDef type is not a delegate type.");
        }
        if (scope.Kind
                != MetadataTypeScopeKind.CurrentModule
            || scope.ModuleVersionId
                != MetadataModuleIdentity.ReadVersionId(_reader))
        {
            return MetadataEventTypeCategoryStatus.Unavailable;
        }

        MetadataTypeDefinitionNameResult nameResult =
            MetadataTypeDefinitionName.Create(
                definition.Namespace,
                definition.Segments.ToImmutableArray());
        if (nameResult
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            throw new BadImageFormatException(
                "The local EventDef type does not have a valid definition name.");
        }

        MetadataTypeDefinitionIndex index = Index(_reader);
        if (!index.TryGetDefinition(
                valid.Name,
                out TypeDefinitionHandle handle,
                out bool ambiguous)
            || ambiguous)
        {
            throw new BadImageFormatException(
                "The local EventDef type does not resolve to one TypeDef.");
        }

        site = site with
        {
            Mechanism = MetadataAccessorDeclarationMechanism.TypeCategory,
        };
        MetadataTypeDeclarationResult result =
            _postType(
                MetadataTypeDefinitionAddress.FromHandle(_reader, handle),
                _token);
        if (result is MetadataTypeDeclarationResult.Rejected rejected)
        {
            throw new AccessorDeclarationRejectedException(
                rejected.Failure.Reason switch
                {
                    MetadataTypeDeclarationFailureReason.BudgetExceeded =>
                        MetadataAccessorDeclarationFailureReason
                            .BudgetExceeded,
                    MetadataTypeDeclarationFailureReason.Cycle =>
                        MetadataAccessorDeclarationFailureReason
                            .RelationshipTraversal,
                    _ =>
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                },
                rejected.Failure.Detail);
        }
        MetadataTypeDeclarationEvidence evidence =
            ((MetadataTypeDeclarationResult.Posted)result).Evidence;
        if (evidence.Category
            != MetadataTypeDeclarationCategory.Delegate)
        {
            throw new BadImageFormatException(
                "The local EventDef type is not a delegate.");
        }

        return MetadataEventTypeCategoryStatus.ConfirmedDelegate;
    }

    static bool IsVoid(MetadataTypeIdentity type)
    {
        while (type is MetadataTypeIdentity.Modified modified)
            type = modified.Type;

        return type is MetadataTypeIdentity.Primitive
        {
            Name: { } name,
        }
        && name.ToString() == "void";
    }

    static bool IsVoid(TypeNode type)
    {
        while (type is ModifiedTypeNode modified)
            type = modified.Inner;

        return type is PrimitiveTypeNode
        {
            Name: "void",
        };
    }

    void ValidateType(TypeNode node, GenericContext context)
    {
        if (node.IsDegraded)
        {
            throw new BadImageFormatException(
                "A declaration type cannot be decoded completely.");
        }
        string? invalid = MetadataStructuralTypeValidator.Validate(
            node,
            context.TypeParameters.Count,
            context.MethodParameters.Count,
            "The accessor declaration type");
        if (invalid is not null)
            throw new BadImageFormatException(invalid);
    }

    MetadataTypeIdentity Project(
        TypeNode node,
        MetadataAccessorDeclarationSite site) =>
        new MetadataTypeIdentityProjector(
            () => _context.Charge(
                MetadataOperationDimension.StructuredNodes),
            Retain,
            detail => new BadImageFormatException(detail))
            .Project(node);

    TypeNode DecodeType(
        EntityHandle handle,
        GenericContext context,
        MetadataAccessorDeclarationSite site)
    {
        TypeNodeProvider provider = Provider(site);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                provider.GetTypeFromDefinition(
                    _reader,
                    (TypeDefinitionHandle)handle,
                    rawTypeKind: 0x12),
            HandleKind.TypeReference =>
                provider.GetTypeFromReference(
                    _reader,
                    (TypeReferenceHandle)handle,
                    rawTypeKind: 0x12),
            HandleKind.TypeSpecification =>
                DecodeTypeSpecification(
                    (TypeSpecificationHandle)handle,
                    context,
                    provider,
                    site),
            _ => throw new BadImageFormatException(
                "The declaration type handle is invalid."),
        };
    }

    TypeNode DecodeTypeSpecification(
        TypeSpecificationHandle handle,
        GenericContext context,
        TypeNodeProvider provider,
        MetadataAccessorDeclarationSite site)
    {
        ValidateSpec(site, _reader, handle);
        return GuardedProviderDecode.TypeSpec(
            _reader,
            handle,
            provider,
            context,
            (TypeNode)new DegradedTypeNode());
    }

    TypeNodeProvider Provider(
        MetadataAccessorDeclarationSite site) =>
        new(
            beforeRetain: value => _context.Charge(
                MetadataOperationDimension.RetainedText,
                value.Length),
            beforeMaterialize: amount => _context.Charge(
                MetadataOperationDimension.StructuredNodes,
                amount),
            beforeCreateNode: () => _context.Charge(
                MetadataOperationDimension.StructuredNodes),
            beforeTypeSpecificationDecode: (reader, handle) =>
                ValidateSpec(site, reader, handle),
            retainExactScope: true,
            beforeTypeDefinitionResolve: (_, _) =>
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges),
            beforeTypeReferenceResolve: (_, _) =>
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges),
            relationshipRejected: rejection =>
                throw new GenericContextRelationshipRejectedException(
                    rejection),
            beforeRelationshipFollow: _ =>
                _context.Charge(
                    MetadataOperationDimension.RelationshipEdges),
            getLocalTypeDefinitions: reader =>
                Index(reader));

    MetadataTypeDefinitionIndex Index(
        MetadataReader reader)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "A foreign reader reached the TypeDef index.");
        }
        try
        {
            return _getIndex(
                _token.ThrowIfCancellationRequested,
                _ => _context.Charge(
                    MetadataOperationDimension.RelationshipEdges),
                () => _context.Charge(
                    MetadataOperationDimension.StructuredNodes),
                amount => _context.Charge(
                    MetadataOperationDimension.RetainedText,
                    amount));
        }
        catch (MetadataTypeDefinitionIndexFailureException ex)
        {
            throw new AccessorDeclarationRejectedException(
                ex.Kind switch
                {
                    MetadataTypeDefinitionIndexFailureKind
                        .BudgetExceeded =>
                            MetadataAccessorDeclarationFailureReason
                                .BudgetExceeded,
                    MetadataTypeDefinitionIndexFailureKind.Cycle =>
                        MetadataAccessorDeclarationFailureReason
                            .RelationshipTraversal,
                    _ =>
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                },
                ex.Message);
        }
    }

    void ValidateSpec(
        MetadataAccessorDeclarationSite site,
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "A foreign reader reached the TypeSpec.");
        }
        BlobHandle blob =
            reader.GetTypeSpecification(handle).Signature;
        _context.Charge(
            MetadataOperationDimension.SignatureBytes,
            reader.GetBlobReader(blob).Length);
        if (_validatedSpecs.Contains(handle))
            return;
        TypeSpecificationRootReadResult? failure =
            TypeSpecificationRoot.ValidateGraph(
                reader,
                handle,
                (_, bytes) => _context.Charge(
                    MetadataOperationDimension.SignatureBytes,
                    bytes),
                _ => _context.Charge(
                    MetadataOperationDimension.RelationshipEdges),
                validated => _validatedSpecs.Add(validated));
        if (failure is not null)
        {
            throw failure switch
            {
                TypeSpecificationRootReadResult.BudgetExceeded value =>
                    new AccessorDeclarationRejectedException(
                        MetadataAccessorDeclarationFailureReason
                            .BudgetExceeded,
                        value.Detail),
                TypeSpecificationRootReadResult.Malformed value =>
                    new AccessorDeclarationRejectedException(
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                        value.Detail),
                TypeSpecificationRootReadResult.Cycle value =>
                    new AccessorDeclarationRejectedException(
                        MetadataAccessorDeclarationFailureReason
                            .RelationshipTraversal,
                        value.Detail),
                TypeSpecificationRootReadResult.Unsupported value =>
                    new AccessorDeclarationRejectedException(
                        MetadataAccessorDeclarationFailureReason
                            .MalformedMetadata,
                        value.Detail),
                _ => new InvalidOperationException(
                    "Unknown TypeSpec result."),
            };
        }
    }

    string ReadName(StringHandle handle)
    {
        int bytes = _reader.GetBlobReader(handle).Length;
        if (bytes
            > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new AccessorDeclarationRejectedException(
                MetadataAccessorDeclarationFailureReason.BudgetExceeded,
                "An accessor root name exceeds the structural string limit.");
        }
        _context.Charge(
            MetadataOperationDimension.StructuredNodes,
            bytes);
        string name = MetadataSafetyPolicy.ReadStructuralString(
            _reader,
            handle);
        if (name.Length == 0)
        {
            throw new BadImageFormatException(
                "An accessor root name cannot be empty.");
        }
        return name;
    }

    InertString Retain(string value)
    {
        _context.Charge(
            MetadataOperationDimension.RetainedText,
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                value));
        return new(TextPolicy.Field, value);
    }

    bool IsValidType(EntityHandle handle)
    {
        TableIndex? table = handle.Kind switch
        {
            HandleKind.TypeDefinition => TableIndex.TypeDef,
            HandleKind.TypeReference => TableIndex.TypeRef,
            HandleKind.TypeSpecification => TableIndex.TypeSpec,
            _ => null,
        };
        int row = MetadataTokens.GetRowNumber(handle);
        return table is not null
            && row > 0
            && row <= _reader.GetTableRowCount(table.Value);
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

    readonly record struct ResolvedOccurrence(
        int PhysicalRowNumber,
        ushort RawSemantics,
        MetadataAccessorSemanticsRole Role,
        MetadataMethodDeclarationEvidence Method);

    readonly record struct ClassifiedOccurrence(
        int PhysicalRowNumber,
        ushort RawSemantics,
        MetadataAccessorSemanticsRole Role,
        MetadataMethodDeclarationEvidence Method,
        MetadataAccessorOccurrenceCorrespondenceEvidence
            Correspondence);

    readonly record struct AccessorCorrespondenceRead(
        MetadataAccessorAggregateCorrespondenceEvidence Aggregate,
        ImmutableArray<ClassifiedOccurrence> Occurrences);

    readonly record struct MetadataAccessorDeclarationSite(
        MetadataAccessorDeclarationStage Stage,
        MetadataAccessorDeclarationMechanism Mechanism,
        int? PhysicalRowNumber = null,
        ushort? RawSemantics = null,
        MetadataMethodAddress? Method = null);

    readonly record struct DecodedAccessorSignature(
        MethodSignature<TypeNode> Signature,
        int TypeParameterCount,
        int MethodParameterCount);

    sealed class AccessorDeclarationRejectedException(
        MetadataAccessorDeclarationFailureReason reason,
        string detail) : Exception(detail)
    {
        internal MetadataAccessorDeclarationFailureReason Reason { get; } =
            reason;
    }
}
