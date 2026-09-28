using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;
using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

public readonly record struct MetadataMethodImplementationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Body);

public readonly record struct MetadataMethodImplementationAddress(
    Guid ModuleVersionId,
    MethodImplementationHandle Handle)
{
    internal int Token => MetadataTokens.GetToken(Handle);
}

public readonly record struct MetadataMethodDeclarationAddress(
    Guid ModuleVersionId,
    EntityHandle Handle)
{
    internal int Token => MetadataTokens.GetToken(Handle);
}

public enum MetadataMethodImplementationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    Cycle,
    BudgetExceeded,
    UnsupportedShape,
    LocalOwnerAmbiguous,
    LocalDeclarationAmbiguous,
    SignatureMismatch,
}

public enum MetadataMethodImplementationStage
{
    RequestValidation,
    MethodImplementationScan,
    DeclarationRead,
    OwnerAuthentication,
    LocalDeclarationResolution,
    SignatureCorrespondence,
    ResultRetention,
}

public enum MetadataMethodImplementationMechanism
{
    ImageAdmission,
    AddressResolution,
    DirectOwnership,
    RowRead,
    HandleValidation,
    RelationshipTraversal,
    TypeSpecificationRoot,
    TypeDefinitionIndex,
    SignatureDecode,
    GenericSubstitution,
    TextRetention,
}

public sealed record MetadataMethodImplementationFailure(
    MetadataMethodImplementationRequest Request,
    MetadataMethodImplementationFailureReason Reason,
    MetadataMethodImplementationStage Stage,
    MetadataMethodImplementationMechanism Mechanism,
    string Detail,
    MethodImplementationHandle? RelevantRow,
    EntityHandle RelevantHandle,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

internal readonly record struct MetadataMethodImplementationFailureSite(
    MetadataMethodImplementationStage Stage,
    MetadataMethodImplementationMechanism Mechanism,
    MethodImplementationHandle? RelevantRow = null,
    EntityHandle RelevantHandle = default);

public abstract record MetadataMethodImplementationResult
{
    private protected MetadataMethodImplementationResult(
        MetadataOperationCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        Counters = counters;
    }

    public MetadataOperationCounters Counters { get; }

    public sealed record Related
        : MetadataMethodImplementationResult
    {
        internal Related(
            ImmutableArray<MetadataMethodImplementationCertificate>
                relationships,
            MetadataOperationCounters counters)
            : base(counters)
        {
            if (relationships.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A related result must carry at least one relationship.",
                    nameof(relationships));
            }

            Relationships = relationships;
        }

        public ImmutableArray<MetadataMethodImplementationCertificate>
            Relationships { get; }
    }

    public sealed record Absent
        : MetadataMethodImplementationResult
    {
        internal Absent(MetadataOperationCounters counters)
            : base(counters)
        {
        }
    }

    public sealed record Rejected
        : MetadataMethodImplementationResult
    {
        internal Rejected(
            MetadataMethodImplementationFailure failure,
            MetadataOperationCounters counters)
            : base(counters)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataMethodImplementationFailure Failure { get; }
    }
}

public enum MetadataSpecialNameEvidence
{
    KnownFalse,
    KnownTrue,
    Unknown,
}

public abstract record MetadataDeclarationDefinitionDisposition
{
    private protected MetadataDeclarationDefinitionDisposition()
    {
    }

    public sealed record LocalResolved(
        MetadataTypeDefinitionAddress Owner,
        MetadataMethodAddress Definition,
        MethodAttributes Attributes)
        : MetadataDeclarationDefinitionDisposition;

    public sealed record ExternalUnresolved(
        MetadataTypeScopeIdentity Scope)
        : MetadataDeclarationDefinitionDisposition;
}

public sealed record MetadataMethodImplementationCertificate(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Body,
    MetadataMethodImplementationAddress Relationship,
    MetadataMethodDeclarationAddress Declaration,
    InertString DeclarationName,
    MetadataTypeIdentity DeclarationOwner,
    MetadataMethodSignatureIdentity DeclarationSignature,
    MetadataDeclarationDefinitionDisposition Definition,
    MetadataSpecialNameEvidence SpecialName);

internal sealed class MetadataMethodImplementationEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly Func<
        Action,
        Action<TypeDefinitionHandle>,
        Action,
        Action<int>,
        MetadataTypeDefinitionIndex>
        _getTypeDefinitionIndex;
    readonly Dictionary<TypeReferenceHandle, TypeReferenceBinding>
        _typeReferenceBindings = [];
    readonly HashSet<TypeDefinitionHandle>
        _validatedTypeDefinitions = [];
    readonly HashSet<TypeSpecificationHandle>
        _validatedTypeSpecifications = [];
    readonly MetadataMethodStructuralSignature _signatures;
    MetadataMethodImplementationRequest _request;
    MethodSignature<TypeNode>? _bodySignature;
    GenericContext? _bodyContext;
    TypeSpecificationHandle _activeTypeSpecification;
    CancellationToken _token;

    internal MetadataMethodImplementationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        Func<
            Action,
            Action<TypeDefinitionHandle>,
            Action,
            Action<int>,
            MetadataTypeDefinitionIndex>
            getTypeDefinitionIndex)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(getTypeDefinitionIndex);
        _reader = reader;
        _context = context;
        _getTypeDefinitionIndex = getTypeDefinitionIndex;
        _signatures = new(
            (site, dimension, amount) =>
                Charge(site, dimension, amount),
            (site, reason, detail) =>
                Rejection(site, reason, detail));
    }

    TypeNodeProvider CreateTypeProvider(
        MetadataMethodImplementationFailureSite site) =>
        new(
            beforeRetain: value =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.RetainedText,
                    value.Length);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeTextRetention);
            },
            beforeMaterialize: amount =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.StructuredNodes,
                    amount);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeMaterialization);
            },
            beforeNameMaterialize: () =>
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNameMaterialization),
            beforePublicKeyMaterialize: () =>
                _context.ObserveWork(
                    MetadataOperationWorkKind.PublicKeyTokenMaterialization),
            beforeCreateNode: () =>
            {
                Charge(
                    ActiveTypeSpecificationSite(site),
                    MetadataOperationDimension.StructuredNodes);
                _context.ObserveWork(
                    MetadataOperationWorkKind.TypeNodeCreation);
            },
            beforeTypeSpecificationDecode: (reader, handle) =>
                ValidateTypeSpecificationGraph(
                    site,
                    reader,
                    handle),
            retainExactScope: true,
            enrichTypeReferenceName: (reader, handle, parts) =>
                EnrichTypeReferenceName(
                    site,
                    reader,
                    handle,
                    parts),
            resolveTypeReferenceScope:
                (reader, handle, projectedAssembly) =>
                ResolveTypeReferenceScope(
                    site,
                    reader,
                    handle,
                    projectedAssembly),
            beforeTypeDefinitionResolve: (reader, handle) =>
                BeforeTypeDefinitionResolve(
                    site,
                    reader,
                    handle),
            beforeTypeReferenceResolve: (reader, handle) =>
                BeforeTypeReferenceResolve(
                    site,
                    reader,
                    handle),
            typeSpecificationScope: EnterTypeSpecificationScope,
            nameBudgetRejected: rejection =>
                throw Rejection(
                    site with
                    {
                        RelevantHandle = rejection.Subject,
                    },
                    MetadataMethodImplementationFailureReason
                        .BudgetExceeded,
                    rejection.Detail));

    internal MetadataMethodImplementationResult Relate(
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress body,
        CancellationToken token)
    {
        _request = new(type, body);
        _token = token;
        try
        {
            token.ThrowIfCancellationRequested();
            var addressSite =
                new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage.RequestValidation,
                MetadataMethodImplementationMechanism.AddressResolution);
            if (!type.TryResolve(
                    _reader,
                    out TypeDefinitionHandle typeHandle))
            {
                Reject(
                    addressSite,
                    MetadataMethodImplementationFailureReason.InvalidRequest,
                    "The requested TypeDef address does not resolve in this module.");
            }
            if (!body.TryResolve(
                    _reader,
                    out MethodDefinitionHandle bodyHandle))
            {
                Reject(
                    addressSite,
                    MetadataMethodImplementationFailureReason.InvalidRequest,
                    "The requested MethodDef address does not resolve in this module.");
            }

            var ownershipSite =
                new MetadataMethodImplementationFailureSite(
                    MetadataMethodImplementationStage.RequestValidation,
                    MetadataMethodImplementationMechanism.DirectOwnership,
                    RelevantHandle: bodyHandle);
            (MethodDefinition bodyDefinition,
                TypeDefinitionHandle bodyOwner) =
                Read(
                    ownershipSite,
                    () =>
                    {
                        MethodDefinition definition =
                            _reader.GetMethodDefinition(bodyHandle);
                        return (
                            definition,
                            definition.GetDeclaringType());
                    });
            if (bodyOwner != typeHandle)
            {
                Reject(
                    ownershipSite,
                    MetadataMethodImplementationFailureReason.InvalidRequest,
                    "The requested MethodDef is not declared directly by the requested TypeDef.");
            }

            var relationships =
                ImmutableArray.CreateBuilder<
                    MetadataMethodImplementationCertificate>();
            int rowCount =
                _reader.GetTableRowCount(TableIndex.MethodImpl);
            for (int rid = 1; rid <= rowCount; rid++)
            {
                token.ThrowIfCancellationRequested();
                MethodImplementationHandle row =
                    MetadataTokens.MethodImplementationHandle(rid);
                var rowSite =
                    new MetadataMethodImplementationFailureSite(
                        MetadataMethodImplementationStage
                            .MethodImplementationScan,
                        MetadataMethodImplementationMechanism.RowRead,
                        row);
                Charge(
                    rowSite,
                    MetadataOperationDimension
                        .MethodImplementationRows);
                MethodImplementation implementation =
                    Read(
                        rowSite,
                        () => _reader.GetMethodImplementation(row));

                var classAccessSite =
                    new MetadataMethodImplementationFailureSite(
                        MetadataMethodImplementationStage
                            .MethodImplementationScan,
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                        row);
                Charge(
                    classAccessSite,
                    MetadataOperationDimension.RelationshipEdges);
                TypeDefinitionHandle implementationType = Read(
                    classAccessSite,
                    () => implementation.Type);
                var classSite = classAccessSite with
                {
                    RelevantHandle = implementationType,
                };
                if (!IsValid(implementationType))
                {
                    Reject(
                        classSite,
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                        "A MethodImpl Class does not identify a readable TypeDef row.");
                }
                if (implementationType != typeHandle)
                    continue;

                var bodyAccessSite =
                    new MetadataMethodImplementationFailureSite(
                        MetadataMethodImplementationStage
                            .MethodImplementationScan,
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                        row);
                Charge(
                    bodyAccessSite,
                    MetadataOperationDimension.RelationshipEdges);
                EntityHandle methodBody = Read(
                    bodyAccessSite,
                    () => implementation.MethodBody);
                var bodySite = bodyAccessSite with
                {
                    RelevantHandle = methodBody,
                };
                if (!IsReadableMethodDefOrRef(methodBody))
                {
                    Reject(
                        bodySite,
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                        "A MethodImpl body cannot be read well enough to decide relevance.");
                }
                if (methodBody.Kind != HandleKind.MethodDefinition
                    || (MethodDefinitionHandle)methodBody != bodyHandle)
                {
                    continue;
                }

                var declarationAccessSite =
                    new MetadataMethodImplementationFailureSite(
                        MetadataMethodImplementationStage.DeclarationRead,
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                        row);
                Charge(
                    declarationAccessSite,
                    MetadataOperationDimension.RelationshipEdges);
                EntityHandle declaration = Read(
                    declarationAccessSite,
                    () => implementation.MethodDeclaration);
                relationships.Add(
                    AuthenticateDeclaration(
                        type,
                        body,
                        typeHandle,
                        bodyHandle,
                        bodyDefinition,
                        row,
                        declaration,
                        token));
            }

            MetadataOperationCounters counters = _context.Counters;
            return relationships.Count == 0
                ? new MetadataMethodImplementationResult.Absent(counters)
                : new MetadataMethodImplementationResult.Related(
                    relationships.ToImmutable(),
                    counters);
        }
        catch (MetadataMethodImplementationRejectedException ex)
        {
            return new MetadataMethodImplementationResult.Rejected(
                new MetadataMethodImplementationFailure(
                    _request,
                    ex.Reason,
                    ex.Stage,
                    ex.Mechanism,
                    ex.Message,
                    ex.RelevantRow,
                    ex.RelevantHandle,
                    ex.BudgetDimension,
                    ex.BudgetLimit,
                    ex.AttemptedCharge),
                _context.Counters);
        }
    }

    MetadataMethodImplementationCertificate AuthenticateDeclaration(
        MetadataTypeDefinitionAddress typeAddress,
        MetadataMethodAddress bodyAddress,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle bodyHandle,
        MethodDefinition body,
        MethodImplementationHandle row,
        EntityHandle declaration,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return declaration.Kind switch
        {
            HandleKind.MethodDefinition =>
                AuthenticateMethodDefinition(
                    typeAddress,
                    bodyAddress,
                    typeHandle,
                    bodyHandle,
                    body,
                    row,
                    (MethodDefinitionHandle)declaration),
            HandleKind.MemberReference =>
                AuthenticateMemberReference(
                    typeAddress,
                    bodyAddress,
                    typeHandle,
                    bodyHandle,
                    body,
                    row,
                    (MemberReferenceHandle)declaration,
                    token),
            _ => throw Rejection(
                new MetadataMethodImplementationFailureSite(
                    MetadataMethodImplementationStage.DeclarationRead,
                    MetadataMethodImplementationMechanism.HandleValidation,
                    row,
                    declaration),
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "A MethodImpl declaration must be a MethodDef or MemberRef."),
        };
    }

    MetadataMethodImplementationCertificate AuthenticateMethodDefinition(
        MetadataTypeDefinitionAddress typeAddress,
        MetadataMethodAddress bodyAddress,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle bodyHandle,
        MethodDefinition body,
        MethodImplementationHandle row,
        MethodDefinitionHandle declarationHandle)
    {
        var declarationSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage.DeclarationRead,
                MetadataMethodImplementationMechanism.RowRead,
                row,
                declarationHandle);
        if (!IsValid(declarationHandle))
        {
            Reject(
                declarationSite with
                {
                    Mechanism =
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                },
                MetadataMethodImplementationFailureReason.MalformedMetadata,
                "A relevant MethodImpl declaration identifies an unreadable MethodDef row.");
        }

        (MethodDefinition declaration,
            TypeDefinitionHandle declarationOwner) =
            Read(
                declarationSite,
                () =>
                {
                    MethodDefinition definition =
                        _reader.GetMethodDefinition(
                            declarationHandle);
                    return (
                        definition,
                        definition.GetDeclaringType());
                });
        if (!IsValid(declarationOwner))
        {
            Reject(
                declarationSite with
                {
                    Mechanism =
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                    RelevantHandle = declarationOwner,
                },
                MetadataMethodImplementationFailureReason.MalformedMetadata,
                "A MethodDef declaration has an unreadable declaring type.");
        }

        OwnerEvidence owner = ReadOwner(
            declarationOwner,
            row);
        MethodSignature<TypeNode> bodySignature =
            GetBodySignature(
                typeHandle,
                body,
                bodyHandle,
                row);
        var signatureSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage
                    .SignatureCorrespondence,
                MetadataMethodImplementationMechanism.SignatureDecode,
                row,
                declarationHandle);
        TypeDefinition ownerDefinition = Read(
            signatureSite,
            () => _reader.GetTypeDefinition(declarationOwner));
        GenericContext declarationContext =
            CreateGenericContext(
                signatureSite,
                ownerDefinition,
                declaration);
        MethodSignature<TypeNode> declarationSignature =
            DecodeMethod(
                declaration,
                declarationContext,
                signatureSite);

        var substitutionSite = signatureSite with
        {
            Mechanism =
                MetadataMethodImplementationMechanism
                    .GenericSubstitution,
        };
        if (!_signatures.Match(
                bodySignature,
                declarationSignature,
                leftTypeArguments: [],
                rightTypeArguments: [],
                substitutionSite))
        {
            Reject(
                substitutionSite,
                MetadataMethodImplementationFailureReason.SignatureMismatch,
                "The MethodImpl body and MethodDef declaration signatures do not correspond.");
        }

        StringHandle declarationName = Read(
            declarationSite,
            () => declaration.Name);
        string name = ReadDeclarationName(
            declarationName,
            declarationSite);
        MethodAttributes attributes = declaration.Attributes;
        var retentionSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage.ResultRetention,
                MetadataMethodImplementationMechanism.TextRetention,
                row,
                declarationHandle);
        return CreateCertificate(
            typeAddress,
            bodyAddress,
            row,
            declarationHandle,
            name,
            owner.Node,
            declarationSignature,
            new MetadataDeclarationDefinitionDisposition.LocalResolved(
                Read(
                    retentionSite,
                    () => MetadataTypeDefinitionAddress.FromHandle(
                        _reader,
                        declarationOwner)),
                Read(
                    retentionSite,
                    () => MetadataMethodAddress.Create(
                        _reader,
                        declarationHandle)),
                attributes),
            (attributes & MethodAttributes.SpecialName) != 0
                ? MetadataSpecialNameEvidence.KnownTrue
                : MetadataSpecialNameEvidence.KnownFalse,
            retentionSite);
    }

    MetadataMethodImplementationCertificate AuthenticateMemberReference(
        MetadataTypeDefinitionAddress typeAddress,
        MetadataMethodAddress bodyAddress,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle bodyHandle,
        MethodDefinition body,
        MethodImplementationHandle row,
        MemberReferenceHandle declarationHandle,
        CancellationToken token)
    {
        var declarationSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage.DeclarationRead,
                MetadataMethodImplementationMechanism.RowRead,
                row,
                declarationHandle);
        if (!IsValid(declarationHandle))
        {
            Reject(
                declarationSite with
                {
                    Mechanism =
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                },
                MetadataMethodImplementationFailureReason.MalformedMetadata,
                "A relevant MethodImpl declaration identifies an unreadable MemberRef row.");
        }

        MemberReference declaration =
            Read(
                declarationSite,
                () => _reader.GetMemberReference(
                    declarationHandle));
        EntityHandle parent = Read(
            declarationSite,
            () => declaration.Parent);
        if (parent.Kind is not (
                HandleKind.TypeDefinition
                or HandleKind.TypeReference
                or HandleKind.TypeSpecification))
        {
            Reject(
                declarationSite with
                {
                    Mechanism =
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                    RelevantHandle = parent,
                },
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "A MemberRef declaration parent must be TypeDef, TypeRef, or TypeSpec.");
        }
        if (!IsValidType(parent))
        {
            Reject(
                declarationSite with
                {
                    Mechanism =
                        MetadataMethodImplementationMechanism
                            .HandleValidation,
                    RelevantHandle = parent,
                },
                MetadataMethodImplementationFailureReason.MalformedMetadata,
                "A relevant MemberRef parent identifies an unreadable type row.");
        }

        OwnerEvidence owner;
        MethodSignature<TypeNode> bodySignature;
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            bodySignature = GetBodySignature(
                typeHandle,
                body,
                bodyHandle,
                row);
            owner = ReadOwner(parent, row);
        }
        else
        {
            owner = ReadOwner(parent, row);
            bodySignature = GetBodySignature(
                typeHandle,
                body,
                bodyHandle,
                row);
        }
        var signatureSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage
                    .SignatureCorrespondence,
                MetadataMethodImplementationMechanism.SignatureDecode,
                row,
                declarationHandle);
        MethodSignature<TypeNode> declarationSignature =
            DecodeMemberReference(
                declaration,
                owner.FormalTypeParameterCount,
                signatureSite);
        token.ThrowIfCancellationRequested();
        StringHandle declarationName = Read(
            declarationSite,
            () => declaration.Name);
        string name = ReadDeclarationName(
            declarationName,
            declarationSite);
        MethodDefinitionHandle resolved = default;
        if (owner.IsLocal)
        {
            resolved = ResolveLocalDeclaration(
                owner,
                name,
                declarationSignature,
                row,
                declarationHandle,
                token);
        }

        var substitutionSite = signatureSite with
        {
            Mechanism =
                MetadataMethodImplementationMechanism
                    .GenericSubstitution,
        };
        if (!_signatures.Match(
                bodySignature,
                declarationSignature,
                leftTypeArguments: [],
                owner.TypeArguments,
                substitutionSite))
        {
            Reject(
                substitutionSite,
                MetadataMethodImplementationFailureReason.SignatureMismatch,
                "The MethodImpl body and MemberRef declaration signatures do not correspond.");
        }

        MetadataDeclarationDefinitionDisposition disposition;
        MetadataSpecialNameEvidence specialName;
        var retentionSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage.ResultRetention,
                MetadataMethodImplementationMechanism.TextRetention,
                row,
                declarationHandle);
        if (owner.IsLocal)
        {
            MethodDefinition definition =
                Read(
                    new MetadataMethodImplementationFailureSite(
                        MetadataMethodImplementationStage
                            .LocalDeclarationResolution,
                        MetadataMethodImplementationMechanism.RowRead,
                        row,
                        resolved),
                    () => _reader.GetMethodDefinition(resolved));
            MethodAttributes attributes = definition.Attributes;
            disposition =
                new MetadataDeclarationDefinitionDisposition.LocalResolved(
                    Read(
                        retentionSite,
                        () => MetadataTypeDefinitionAddress.FromHandle(
                            _reader,
                            owner.LocalDefinition)),
                    Read(
                        retentionSite,
                        () => MetadataMethodAddress.Create(
                            _reader,
                            resolved)),
                    attributes);
            specialName =
                (attributes & MethodAttributes.SpecialName) != 0
                    ? MetadataSpecialNameEvidence.KnownTrue
                    : MetadataSpecialNameEvidence.KnownFalse;
        }
        else
        {
            disposition =
                new MetadataDeclarationDefinitionDisposition
                    .ExternalUnresolved(
                        ProjectScope(
                            retentionSite,
                            owner.Node.ExactScope
                            ?? throw Rejection(
                                new MetadataMethodImplementationFailureSite(
                                    MetadataMethodImplementationStage
                                        .OwnerAuthentication,
                                    MetadataMethodImplementationMechanism
                                        .RelationshipTraversal,
                                    row,
                                    parent),
                                MetadataMethodImplementationFailureReason
                                    .MalformedMetadata,
                                "An external declaration owner has no exact scope identity.")));
            specialName = MetadataSpecialNameEvidence.Unknown;
        }

        return CreateCertificate(
            typeAddress,
            bodyAddress,
            row,
            declarationHandle,
            name,
            owner.Node,
            declarationSignature,
            disposition,
            specialName,
            retentionSite);
    }

    MethodDefinitionHandle ResolveLocalDeclaration(
        OwnerEvidence owner,
        string name,
        MethodSignature<TypeNode> declarationSignature,
        MethodImplementationHandle row,
        EntityHandle declarationHandle,
        CancellationToken token)
    {
        var ownerSite =
            new MetadataMethodImplementationFailureSite(
                MetadataMethodImplementationStage
                    .LocalDeclarationResolution,
                MetadataMethodImplementationMechanism.RowRead,
                row,
                owner.LocalDefinition);
        TypeDefinition definition =
            Read(
                ownerSite,
                () => _reader.GetTypeDefinition(
                    owner.LocalDefinition));
        MethodDefinitionHandleCollection methods =
            Read(
                ownerSite,
                definition.GetMethods);
        MethodDefinitionHandle match = default;
        foreach (MethodDefinitionHandle candidateHandle
            in methods)
        {
            token.ThrowIfCancellationRequested();
            var candidateSite =
                new MetadataMethodImplementationFailureSite(
                    MetadataMethodImplementationStage
                        .LocalDeclarationResolution,
                    MetadataMethodImplementationMechanism.RowRead,
                    row,
                    candidateHandle);
            Charge(
                candidateSite,
                MetadataOperationDimension.DeclarationCandidates);
            MethodDefinition candidate =
                Read(
                    candidateSite,
                    () => _reader.GetMethodDefinition(
                        candidateHandle));
            StringHandle candidateNameHandle = Read(
                candidateSite,
                () => candidate.Name);
            string candidateName =
                ReadCandidateName(
                    candidateNameHandle,
                    candidateSite);
            if (!string.Equals(
                    candidateName,
                    name,
                    StringComparison.Ordinal))
            {
                continue;
            }

            MethodSignature<TypeNode> candidateSignature =
                DecodeMethod(
                    candidate,
                    CreateGenericContext(
                        candidateSite,
                        definition,
                        candidate),
                    candidateSite with
                    {
                        Mechanism =
                            MetadataMethodImplementationMechanism
                                .SignatureDecode,
                    });
            if (!_signatures.Match(
                    candidateSignature,
                    declarationSignature,
                    leftTypeArguments: [],
                    rightTypeArguments: [],
                    candidateSite with
                    {
                        Mechanism =
                            MetadataMethodImplementationMechanism
                                .GenericSubstitution,
                    }))
            {
                continue;
            }

            if (!match.IsNil)
            {
                Reject(
                    candidateSite,
                    MetadataMethodImplementationFailureReason
                        .LocalDeclarationAmbiguous,
                    "More than one directly declared MethodDef matches the local MemberRef declaration.");
            }
            match = candidateHandle;
        }

        if (match.IsNil)
        {
            Reject(
                ownerSite with
                {
                    RelevantHandle = declarationHandle,
                },
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "No directly declared MethodDef matches the local MemberRef declaration.");
        }
        return match;
    }

    OwnerEvidence ReadOwner(
        EntityHandle parent,
        MethodImplementationHandle row)
    {
        var site = new MetadataMethodImplementationFailureSite(
            MetadataMethodImplementationStage.OwnerAuthentication,
            MetadataMethodImplementationMechanism
                .RelationshipTraversal,
            row,
            parent);
        return parent.Kind switch
        {
            HandleKind.TypeDefinition =>
                ReadTypeDefinitionOwner(
                    (TypeDefinitionHandle)parent,
                    site),
            HandleKind.TypeReference =>
                ReadTypeReferenceOwner(
                    (TypeReferenceHandle)parent,
                    site),
            HandleKind.TypeSpecification =>
                ReadTypeSpecificationOwner(
                    (TypeSpecificationHandle)parent,
                    site),
            _ => throw Rejection(
                site,
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "The declaration owner is outside the admitted type shapes."),
        };
    }

    OwnerEvidence ReadTypeDefinitionOwner(
        TypeDefinitionHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges);
        TypeNode node = Read(
            site,
            () => CreateTypeProvider(site)
                .GetTypeFromDefinition(
                    _reader,
                    handle,
                    rawTypeKind: 0));
        EnsureCompleteType(node, site);
        TypeDefinition definition = Read(
            site,
            () => _reader.GetTypeDefinition(handle));
        return new(
            node,
            IsLocal: true,
            handle,
            TypeArguments: [],
            FormalTypeParameterCount:
                definition.GetGenericParameters().Count);
    }

    OwnerEvidence ReadTypeReferenceOwner(
        TypeReferenceHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        TypeNode node = Read(
            site,
            () => CreateTypeProvider(site)
                .GetTypeFromReference(
                    _reader,
                    handle,
                    rawTypeKind: 0));
        EnsureCompleteType(node, site);
        LocalTypeResolution resolution =
            ResolveTypeReferenceRoot(handle, site);
        int formalTypeParameterCount = resolution.IsLocal
            ? Read(
                site,
                () => _reader.GetTypeDefinition(
                    resolution.Definition))
                .GetGenericParameters().Count
            : GetDeclaredTypeParameterCount(node);
        return new(
            node,
            resolution.IsLocal,
            resolution.Definition,
            TypeArguments: [],
            formalTypeParameterCount);
    }

    OwnerEvidence ReadTypeSpecificationOwner(
        TypeSpecificationHandle handle,
        MetadataMethodImplementationFailureSite ownerSite)
    {
        var rootSite = ownerSite with
        {
            Mechanism =
                MetadataMethodImplementationMechanism
                    .TypeSpecificationRoot,
            RelevantHandle = handle,
        };
        TypeSpecificationRootReadResult rootResult =
            TypeSpecificationRoot.Read(
                _reader,
                handle,
                beforeDecodeBytes: (active, bytes) =>
                    Charge(
                        rootSite with
                        {
                            RelevantHandle = active,
                        },
                        MetadataOperationDimension.SignatureBytes,
                        bytes),
                beforeDependencyEdge: dependency =>
                    Charge(
                        rootSite with
                        {
                            RelevantHandle = dependency,
                        },
                        MetadataOperationDimension.RelationshipEdges),
                graphValidated: handle =>
                    _validatedTypeSpecifications.Add(handle));
        TypeSpecificationRoot root = rootResult switch
        {
            TypeSpecificationRootReadResult.Read read => read.Root,
            TypeSpecificationRootReadResult.Cycle cycle =>
                throw Rejection(
                    rootSite with
                    {
                        RelevantHandle = cycle.Subject,
                    },
                    MetadataMethodImplementationFailureReason.Cycle,
                    cycle.Detail),
            TypeSpecificationRootReadResult.BudgetExceeded exceeded =>
                throw Rejection(
                    rootSite with
                    {
                        RelevantHandle = exceeded.Subject,
                    },
                    MetadataMethodImplementationFailureReason
                        .BudgetExceeded,
                    exceeded.Detail),
            TypeSpecificationRootReadResult.Malformed malformed =>
                throw Rejection(
                    rootSite with
                    {
                        RelevantHandle = malformed.Subject,
                    },
                    MetadataMethodImplementationFailureReason
                        .MalformedMetadata,
                    malformed.Detail),
            TypeSpecificationRootReadResult.Unsupported unsupported =>
                throw Rejection(
                    rootSite with
                    {
                        RelevantHandle = unsupported.Subject,
                    },
                    MetadataMethodImplementationFailureReason
                        .UnsupportedShape,
                    unsupported.Detail),
            _ => throw new InvalidOperationException(
                "Unknown TypeSpec root result."),
        };
        if (root.Kind != TypeSpecificationRootKind.NamedType)
        {
            Reject(
                rootSite,
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "A declaration-parent TypeSpec must have a named root.");
        }

        TypeNode node = Read(
            rootSite,
            () => GuardedProviderDecode.TypeSpec(
                _reader,
                handle,
                CreateTypeProvider(rootSite),
                context: _bodyContext,
                fallback: (TypeNode)new DegradedTypeNode()));
        EnsureCompleteType(node, rootSite);
        GenericContext bodyContext =
            _bodyContext
            ?? throw Rejection(
                rootSite,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "The body generic context was not available for the declaration owner.");
        _signatures.ValidateType(
            node,
            bodyContext.TypeParameters.Count,
            bodyContext.MethodParameters.Count,
            "The declaration owner TypeSpec",
            rootSite);
        ImmutableArray<TypeNode> arguments =
            node is GenericTypeNode generic
                ? generic.Arguments
                : [];
        if (arguments.Length != root.GenericArgumentCount)
        {
            Reject(
                rootSite,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "The TypeSpec root generic-argument count does not match its complete type tree.");
        }

        LocalTypeResolution resolution = root.Type.Kind switch
        {
            HandleKind.TypeDefinition =>
                new(
                    IsLocal: true,
                    (TypeDefinitionHandle)root.Type),
            HandleKind.TypeReference =>
                ResolveTypeReferenceRoot(
                    (TypeReferenceHandle)root.Type,
                    rootSite),
            _ => throw Rejection(
                rootSite,
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "A TypeSpec declaration root must resolve through TypeDef or TypeRef."),
        };

        int formalTypeParameterCount = resolution.IsLocal
            ? Read(
                rootSite,
                () => _reader.GetTypeDefinition(
                    resolution.Definition))
                .GetGenericParameters().Count
            : GetDeclaredTypeParameterCount(node);
        if (root.IsGenericInstantiation
            && formalTypeParameterCount != arguments.Length)
        {
            Reject(
                rootSite,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A constructed declaration owner supplies "
                    + $"{arguments.Length} arguments for "
                    + $"{formalTypeParameterCount} definition generic parameters.");
        }

        return new(
            node,
            resolution.IsLocal,
            resolution.Definition,
            arguments,
            formalTypeParameterCount);
    }

    void ValidateTypeSpecificationGraph(
        MetadataMethodImplementationFailureSite site,
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The TypeSpec validation route received a foreign metadata reader.");
        }
        var typeSpecSite = site with
        {
            Mechanism =
                MetadataMethodImplementationMechanism.TypeSpecificationRoot,
            RelevantHandle = handle,
        };
        BlobHandle signature = Read(
            typeSpecSite,
            () => reader.GetTypeSpecification(handle).Signature);
        int decodeBytes = Read(
            typeSpecSite,
            () => reader.GetBlobReader(signature).Length);
        Charge(
            typeSpecSite,
            MetadataOperationDimension.SignatureBytes,
            decodeBytes);
        if (_validatedTypeSpecifications.Contains(handle))
            return;

        TypeSpecificationRootReadResult? failure =
            TypeSpecificationRoot.ValidateGraph(
                reader,
                handle,
                beforeDecodeBytes: (active, bytes) =>
                    Charge(
                        typeSpecSite with
                        {
                            RelevantHandle = active,
                        },
                        MetadataOperationDimension.SignatureBytes,
                        bytes),
                beforeDependencyEdge: dependency =>
                    Charge(
                        typeSpecSite with
                        {
                            RelevantHandle = dependency,
                        },
                        MetadataOperationDimension.RelationshipEdges),
                graphValidated: validated =>
                    _validatedTypeSpecifications.Add(validated));
        if (failure is null)
            return;

        EntityHandle subject = failure switch
        {
            TypeSpecificationRootReadResult.Cycle cycle =>
                cycle.Subject,
            TypeSpecificationRootReadResult.BudgetExceeded exceeded =>
                exceeded.Subject,
            TypeSpecificationRootReadResult.Malformed malformed =>
                malformed.Subject,
            TypeSpecificationRootReadResult.Unsupported unsupported =>
                unsupported.Subject,
            _ => handle,
        };
        MetadataMethodImplementationFailureReason reason =
            failure switch
            {
                TypeSpecificationRootReadResult.Cycle =>
                    MetadataMethodImplementationFailureReason.Cycle,
                TypeSpecificationRootReadResult.BudgetExceeded =>
                    MetadataMethodImplementationFailureReason
                        .BudgetExceeded,
                TypeSpecificationRootReadResult.Malformed =>
                    MetadataMethodImplementationFailureReason
                        .MalformedMetadata,
                TypeSpecificationRootReadResult.Unsupported =>
                    MetadataMethodImplementationFailureReason
                        .UnsupportedShape,
                _ => throw new InvalidOperationException(
                    "Unknown TypeSpec graph failure."),
            };
        string detail = failure switch
        {
            TypeSpecificationRootReadResult.Cycle cycle =>
                cycle.Detail,
            TypeSpecificationRootReadResult.BudgetExceeded exceeded =>
                exceeded.Detail,
            TypeSpecificationRootReadResult.Malformed malformed =>
                malformed.Detail,
            TypeSpecificationRootReadResult.Unsupported unsupported =>
                unsupported.Detail,
            _ => throw new InvalidOperationException(
                "Unknown TypeSpec graph failure."),
        };
        throw Rejection(
            typeSpecSite with
            {
                RelevantHandle = subject,
            },
            reason,
            detail);
    }

    LocalTypeResolution ResolveTypeReferenceRoot(
        TypeReferenceHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        TypeReferenceBinding binding =
            EnsureTypeReferenceBinding(handle, site);
        if (!binding.IsLocal)
        {
            return new(
                IsLocal: false,
                Definition: default);
        }
        if (!binding.IsBound)
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A local TypeRef owner was not bound through its complete structured identity.");
        }
        return new(
            IsLocal: true,
            binding.Definition);
    }

    void BeforeTypeReferenceResolve(
        MetadataMethodImplementationFailureSite site,
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The owner-binding route received a foreign metadata reader.");
        }
        _ = EnsureTypeReferenceBinding(handle, site);
    }

    TypeReferenceBinding EnsureTypeReferenceBinding(
        TypeReferenceHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        if (_typeReferenceBindings.TryGetValue(
                handle,
                out TypeReferenceBinding cached))
        {
            return cached;
        }

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    _reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out RelationshipTraversalRejection? rejection,
                    beforeRelationshipFollow: active =>
                        Charge(
                            site with
                            {
                                RelevantHandle = active,
                            },
                            MetadataOperationDimension
                                .RelationshipEdges)))
        {
            MetadataMethodImplementationFailureReason reason =
                rejection!.Kind switch
                {
                    RelationshipTraversalRejectionKind.Cycle =>
                        MetadataMethodImplementationFailureReason.Cycle,
                    RelationshipTraversalRejectionKind.NodeBudget =>
                        MetadataMethodImplementationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                };
            throw Rejection(
                site with
                {
                    RelevantHandle = rejection.Subject,
                },
                reason,
                rejection.Detail);
        }

        bool isLocal = terminal.IsNil
            || terminal.Kind == HandleKind.ModuleDefinition;
        if (terminal.Kind is HandleKind.AssemblyReference
            or HandleKind.ModuleReference)
            isLocal = false;
        if (!terminal.IsNil
            && terminal.Kind is not (
                HandleKind.ModuleDefinition
                or HandleKind.AssemblyReference
                or HandleKind.ModuleReference))
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "A TypeRef declaration owner has an unsupported terminal scope.");
        }

        var binding = new TypeReferenceBinding(
            terminal,
            isLocal,
            IsBound: !isLocal,
            Definition: default);
        _typeReferenceBindings.Add(handle, binding);
        return binding;
    }

    MetadataTypeNameParts? EnrichTypeReferenceName(
        MetadataMethodImplementationFailureSite site,
        MetadataReader reader,
        TypeReferenceHandle handle,
        MetadataTypeNameParts parts)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The owner-binding route received a foreign metadata reader.");
        }

        TypeReferenceBinding binding =
            EnsureTypeReferenceBinding(handle, site);
        if (!binding.IsLocal)
            return parts;
        if (binding.IsBound)
        {
            return parts.WithIntroducedTypeParameterCounts(
                Read(
                    site,
                    () => MetadataDeclarationQuery
                        .GetIntroducedTypeParameterCounts(
                            _reader,
                            binding.Definition)));
        }

        MetadataTypeDefinitionNameResult nameResult =
            MetadataTypeDefinitionName.Create(
                parts.Namespace,
                [.. parts.Segments]);
        if (nameResult
            is not MetadataTypeDefinitionNameResult.Valid)
        {
            throw Rejection(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A local TypeRef owner has an invalid structured name.");
        }
        var valid =
            (MetadataTypeDefinitionNameResult.Valid)nameResult;

        var indexSite = site with
        {
            Mechanism =
                MetadataMethodImplementationMechanism.TypeDefinitionIndex,
            RelevantHandle = handle,
        };
        MetadataTypeDefinitionIndex definitions;
        try
        {
            definitions = _getTypeDefinitionIndex(
                _token.ThrowIfCancellationRequested,
                current => Charge(
                    indexSite with
                    {
                        RelevantHandle = current,
                    },
                    MetadataOperationDimension.RelationshipEdges),
                () =>
                {
                    Charge(
                        indexSite,
                        MetadataOperationDimension.StructuredNodes);
                    _context.ObserveWork(
                        MetadataOperationWorkKind
                            .TypeDefinitionIndexMaterialization);
                },
                amount =>
                {
                    Charge(
                        indexSite,
                        MetadataOperationDimension.RetainedText,
                        amount);
                    _context.ObserveWork(
                        MetadataOperationWorkKind
                            .TypeDefinitionIndexTextRetention);
                });
        }
        catch (MetadataTypeDefinitionIndexFailureException ex)
        {
            MetadataMethodImplementationFailureReason reason =
                ex.Kind switch
                {
                    MetadataTypeDefinitionIndexFailureKind.Cycle =>
                        MetadataMethodImplementationFailureReason.Cycle,
                    MetadataTypeDefinitionIndexFailureKind
                        .BudgetExceeded =>
                        MetadataMethodImplementationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                };
            throw Rejection(
                indexSite with
                {
                    RelevantHandle = ex.Subject,
                },
                reason,
                ex.Message);
        }

        TypeDefinitionHandle definition = default;
        bool ambiguous = false;
        bool found = Read(
            indexSite,
            () => definitions.TryGetDefinition(
                valid.Name,
                out definition,
                out ambiguous));
        if (ambiguous)
        {
            Reject(
                indexSite,
                MetadataMethodImplementationFailureReason
                    .LocalOwnerAmbiguous,
                "More than one local TypeDef matches the exact declaration-owner identity.");
        }
        if (!found)
        {
            Reject(
                indexSite,
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                "No local TypeDef matches the exact declaration-owner identity.");
        }

        binding = binding with
        {
            IsBound = true,
            Definition = definition,
        };
        _typeReferenceBindings[handle] = binding;
        return parts.WithIntroducedTypeParameterCounts(
            Read(
                site,
                () => MetadataDeclarationQuery
                    .GetIntroducedTypeParameterCounts(
                        _reader,
                        definition)));
    }

    MetadataTypeScopeDescriptor? ResolveTypeReferenceScope(
        MetadataMethodImplementationFailureSite site,
        MetadataReader reader,
        TypeReferenceHandle handle,
        ApiAssemblyIdentity? projectedAssembly)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The owner-binding route received a foreign metadata reader.");
        }

        TypeReferenceBinding binding =
            EnsureTypeReferenceBinding(handle, site);
        return Read(
            site,
            () => binding.Terminal.Kind switch
            {
                HandleKind.AssemblyReference =>
                    new MetadataTypeScopeDescriptor(
                        MetadataTypeScopeKind.AssemblyReference,
                        Guid.Empty,
                        ModuleName: null,
                        ProjectAssemblyIdentity(
                            projectedAssembly)),
                HandleKind.ModuleReference =>
                    new MetadataTypeScopeDescriptor(
                        MetadataTypeScopeKind.ModuleReference,
                        Guid.Empty,
                        ReadScopeString(
                            _reader.GetModuleReference(
                                (ModuleReferenceHandle)binding.Terminal)
                                .Name,
                            site),
                        _reader.IsAssembly
                            ? ProjectAssemblyIdentity(
                                projectedAssembly)
                            : null),
                HandleKind.ModuleDefinition =>
                    CurrentScopeIdentity(
                        site,
                        projectedAssembly),
                _ when binding.Terminal.IsNil =>
                    CurrentScopeIdentity(
                        site,
                        projectedAssembly),
                _ => null,
            });
    }

    MetadataTypeScopeDescriptor CurrentScopeIdentity(
        MetadataMethodImplementationFailureSite site,
        ApiAssemblyIdentity? projectedAssembly)
    {
        ModuleDefinition module = Read(
            site,
            _reader.GetModuleDefinition);
        return new(
            MetadataTypeScopeKind.CurrentModule,
            Read(
                site,
                () => MetadataModuleIdentity.ReadVersionId(_reader)),
            Read(
                site,
                () => ReadScopeString(module.Name, site)),
            _reader.IsAssembly
                ? ProjectAssemblyIdentity(projectedAssembly)
                : null);
    }

    string ReadScopeString(
        StringHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        int utf8Length = Read(
            site,
            () => _reader.GetBlobReader(handle).Length);
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            utf8Length);
        _context.ObserveWork(
            MetadataOperationWorkKind.TypeNameMaterialization);
        string value = Read(
            site,
            () => _reader.GetString(handle));
        EnsureCanCharge(
            site,
            MetadataOperationDimension.RetainedText,
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                value));
        return value;
    }

    static AssemblyReferenceIdentity ProjectAssemblyIdentity(
        ApiAssemblyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(
            identity.Name,
            identity.Version,
            identity.Culture,
            identity.PublicKeyToken);
    }

    void BeforeTypeDefinitionResolve(
        MetadataMethodImplementationFailureSite site,
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The owner-binding route received a foreign metadata reader.");
        }
        if (!_validatedTypeDefinitions.Add(handle))
            return;

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    _reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out RelationshipTraversalRejection? rejection,
                    beforeRelationshipFollow: active =>
                        Charge(
                            site with
                            {
                                RelevantHandle = active,
                            },
                            MetadataOperationDimension
                                .RelationshipEdges)))
        {
            MetadataMethodImplementationFailureReason reason =
                rejection!.Kind switch
                {
                    RelationshipTraversalRejectionKind.Cycle =>
                        MetadataMethodImplementationFailureReason.Cycle,
                    RelationshipTraversalRejectionKind.NodeBudget =>
                        MetadataMethodImplementationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                };
            throw Rejection(
                site with
                {
                    RelevantHandle = rejection.Subject,
                },
                reason,
                rejection.Detail);
        }
        if (!terminal.IsNil)
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A TypeDef declaring chain has a non-nil terminal.");
        }
    }

    MethodSignature<TypeNode> GetBodySignature(
        TypeDefinitionHandle typeHandle,
        MethodDefinition body,
        MethodDefinitionHandle bodyHandle,
        MethodImplementationHandle row)
    {
        if (_bodySignature is { } cached)
            return cached;

        var site = new MetadataMethodImplementationFailureSite(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            MetadataMethodImplementationMechanism.SignatureDecode,
            row,
            bodyHandle);
        TypeDefinition type = Read(
            site,
            () => _reader.GetTypeDefinition(typeHandle));
        _bodyContext = CreateGenericContext(
            site,
            type,
            body);
        _bodySignature = DecodeMethod(body, _bodyContext, site);
        return _bodySignature.Value;
    }

    MethodSignature<TypeNode> DecodeMethod(
        MethodDefinition method,
        GenericContext context,
        MetadataMethodImplementationFailureSite site)
    {
        BlobHandle signature = Read(
            site,
            () => method.Signature);
        ChargeSignature(signature, site);
        ValidateMethodSignature(
            signature,
            "MethodDef",
            site);

        GuardedProviderDecode.DecodeResult<MethodSignature<TypeNode>>
            decoded = Read(
                site,
                () => GuardedProviderDecode.MethodResult(
                    _reader,
                    signature,
                    CreateTypeProvider(site),
                    context,
                    (TypeNode)new DegradedTypeNode()));
        if (decoded.IsDegraded
            || SignatureIsDegraded(decoded.Value))
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A relevant MethodDef signature could not be decoded completely.");
        }
        if (decoded.Value.GenericParameterCount
            != context.MethodParameters.Count)
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A MethodDef signature generic arity disagrees with its GenericParam rows.");
        }
        _signatures.Validate(
            decoded.Value,
            context.TypeParameters.Count,
            context.MethodParameters.Count,
            "The MethodDef signature",
            site);
        return decoded.Value;
    }

    MethodSignature<TypeNode> DecodeMemberReference(
        MemberReference member,
        int typeParameterCount,
        MetadataMethodImplementationFailureSite site)
    {
        BlobHandle signatureHandle = Read(
            site,
            () => member.Signature);
        ChargeSignature(signatureHandle, site);
        ValidateMethodSignature(
            signatureHandle,
            "MemberRef",
            site);

        GuardedProviderDecode.DecodeResult<
            MethodSignature<TypeNode>> decoded =
            Read(
                site,
                () => GuardedProviderDecode.MethodResult(
                    _reader,
                    signatureHandle,
                    CreateTypeProvider(site),
                    context: (GenericContext?)null,
                    (TypeNode)new DegradedTypeNode()));
        if (decoded.IsDegraded
            || SignatureIsDegraded(decoded.Value))
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A relevant MemberRef signature could not be decoded completely.");
        }
        _signatures.Validate(
            decoded.Value,
            typeParameterCount,
            decoded.Value.GenericParameterCount,
            "The MemberRef signature",
            site);
        return decoded.Value;
    }

    MetadataMethodImplementationCertificate CreateCertificate(
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress body,
        MethodImplementationHandle relationship,
        EntityHandle declaration,
        string declarationName,
        TypeNode owner,
        MethodSignature<TypeNode> declarationSignature,
        MetadataDeclarationDefinitionDisposition disposition,
        MetadataSpecialNameEvidence specialName,
        MetadataMethodImplementationFailureSite site)
    {
        return new(
            type,
            body,
            new MetadataMethodImplementationAddress(
                type.ModuleVersionId,
                relationship),
            new MetadataMethodDeclarationAddress(
                type.ModuleVersionId,
                declaration),
            Retain(declarationName, site),
            ProjectType(owner, site),
            ProjectSignature(declarationSignature, site),
            disposition,
            specialName);
    }

    MetadataMethodSignatureIdentity ProjectSignature(
        MethodSignature<TypeNode> signature,
        MetadataMethodImplementationFailureSite site)
    {
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes);
        var parameters =
            ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
                signature.ParameterTypes.Length);
        foreach (TypeNode parameter in signature.ParameterTypes)
            parameters.Add(ProjectType(parameter, site));
        return new(
            signature.Header.RawValue,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            ProjectType(signature.ReturnType, site),
            parameters.MoveToImmutable());
    }

    MetadataTypeIdentity ProjectType(
        TypeNode node,
        MetadataMethodImplementationFailureSite site) =>
        CreateTypeIdentityProjector(site)
            .Project(node);

    MetadataTypeScopeIdentity ProjectScope(
        MetadataMethodImplementationFailureSite site,
        MetadataTypeScopeDescriptor scope) =>
        CreateTypeIdentityProjector(site)
            .ProjectScope(scope);

    MetadataTypeIdentityProjector CreateTypeIdentityProjector(
        MetadataMethodImplementationFailureSite site) =>
        new(
            beforeCreateNode: () =>
                Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes),
            retain: value => Retain(value, site),
            reject: detail =>
                Rejection(
                    site,
                    MetadataMethodImplementationFailureReason
                        .MalformedMetadata,
                    detail));

    InertString Retain(
        string value,
        MetadataMethodImplementationFailureSite site)
    {
        int encodedLength =
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                value);
        Charge(
            site,
            MetadataOperationDimension.RetainedText,
            encodedLength);
        return new InertString(TextPolicy.Field, value);
    }

    string ReadStructuralString(
        StringHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        var textSite = site with
        {
            Mechanism =
                MetadataMethodImplementationMechanism.TextRetention,
        };
        return Read(
            textSite,
            () => MetadataSafetyPolicy.ReadStructuralString(
                _reader,
                handle));
    }

    void EnsureStructuralStringWithinBudget(
        int encodedLength,
        MetadataMethodImplementationFailureSite site)
    {
        if (encodedLength
            <= MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            return;
        }

        throw Rejection(
            site with
            {
                Mechanism =
                    MetadataMethodImplementationMechanism.TextRetention,
            },
            MetadataMethodImplementationFailureReason.BudgetExceeded,
            "The metadata string exceeds the shared structural-string budget.");
    }

    string ReadDeclarationName(
        StringHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        int utf8Length = Read(
            site,
            () => _reader.GetBlobReader(handle).Length);
        EnsureStructuralStringWithinBudget(utf8Length, site);
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            utf8Length);
        _context.ObserveWork(
            MetadataOperationWorkKind.DeclarationNameMaterialization);
        string name = ReadStructuralString(handle, site);
        EnsureCanCharge(
            site,
            MetadataOperationDimension.RetainedText,
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                name));
        return name;
    }

    string ReadCandidateName(
        StringHandle handle,
        MetadataMethodImplementationFailureSite site)
    {
        int encodedLength = Read(
            site,
            () => _reader.GetBlobReader(handle).Length);
        EnsureStructuralStringWithinBudget(encodedLength, site);
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            encodedLength);
        _context.ObserveWork(
            MetadataOperationWorkKind.CandidateNameMaterialization);
        return ReadStructuralString(handle, site);
    }

    GenericContext CreateGenericContext(
        MetadataMethodImplementationFailureSite site,
        TypeDefinition type,
        MethodDefinition method)
    {
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes);
        _context.ObserveWork(
            MetadataOperationWorkKind.GenericContextConstruction);
        try
        {
            GenericContext context = Read(
                site,
                () => GenericContext
                    .ForMethodWithRelationshipObserver(
                        _reader,
                        type,
                        method,
                        _ =>
                            _context.ObserveWork(
                                MetadataOperationWorkKind
                                    .GenericParameterNameMaterialization),
                        name =>
                            Charge(
                                site,
                                MetadataOperationDimension.RetainedText,
                                name.Length),
                        active =>
                            Charge(
                                site with
                                {
                                    RelevantHandle = active,
                                },
                                MetadataOperationDimension
                                    .RelationshipEdges)));
            return context;
        }
        catch (GenericContextRelationshipRejectedException ex)
        {
            MetadataMethodImplementationFailureReason reason =
                ex.Rejection.Kind switch
                {
                    RelationshipTraversalRejectionKind.Cycle =>
                        MetadataMethodImplementationFailureReason.Cycle,
                    RelationshipTraversalRejectionKind.NodeBudget =>
                        MetadataMethodImplementationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataMethodImplementationFailureReason
                            .MalformedMetadata,
                };
            throw Rejection(
                site with
                {
                    RelevantHandle = ex.Rejection.Subject,
                },
                reason,
                ex.Rejection.Detail);
        }
        catch (GenericContextBudgetExceededException ex)
        {
            throw Rejection(
                site,
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                ex.Message);
        }
    }

    MetadataMethodImplementationFailureSite ActiveTypeSpecificationSite(
        MetadataMethodImplementationFailureSite site) =>
        _activeTypeSpecification.IsNil
            ? site
            : site with
            {
                RelevantHandle = _activeTypeSpecification,
            };

    IDisposable EnterTypeSpecificationScope(
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
        {
            throw new InvalidOperationException(
                "The TypeSpec accounting route received a foreign metadata reader.");
        }
        TypeSpecificationHandle prior = _activeTypeSpecification;
        _activeTypeSpecification = handle;
        return new TypeSpecificationAccountingScope(
            this,
            prior);
    }

    sealed class TypeSpecificationAccountingScope(
        MetadataMethodImplementationEvidenceOperation owner,
        TypeSpecificationHandle prior) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            owner._activeTypeSpecification = prior;
            _disposed = true;
        }
    }

    void ChargeSignature(
        BlobHandle signature,
        MetadataMethodImplementationFailureSite site)
    {
        int length = Read(
            site,
            () => _reader.GetBlobReader(signature).Length);
        Charge(
            site,
            MetadataOperationDimension.SignatureBytes,
            length);
    }

    void ValidateMethodSignature(
        BlobHandle signature,
        string subject,
        MetadataMethodImplementationFailureSite site)
    {
        SignatureBlobGuard.CompleteValidationKind validation =
            Read(
                site,
                () => SignatureBlobGuard.ValidateComplete(
                    _reader,
                    signature,
                    SignatureBlobGuard.Kind.Method));
        if (validation
            == SignatureBlobGuard.CompleteValidationKind.Valid)
        {
            return;
        }

        Reject(
            site,
            validation is
                SignatureBlobGuard.CompleteValidationKind
                    .DepthBudgetExceeded
                or SignatureBlobGuard.CompleteValidationKind
                    .NodeBudgetExceeded
                    ? MetadataMethodImplementationFailureReason
                        .BudgetExceeded
                    : MetadataMethodImplementationFailureReason
                        .MalformedMetadata,
            validation switch
            {
                SignatureBlobGuard.CompleteValidationKind
                    .DepthBudgetExceeded =>
                    $"A relevant {subject} signature exceeds the shared structural-depth budget.",
                SignatureBlobGuard.CompleteValidationKind
                    .NodeBudgetExceeded =>
                    $"A relevant {subject} signature exceeds the shared type-node budget.",
                _ =>
                    $"A relevant {subject} signature is incomplete or has trailing data.",
            });
    }

    void Charge(
        MetadataMethodImplementationFailureSite site,
        MetadataOperationDimension dimension,
        long amount = 1)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _context.Charge(dimension, amount);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            throw Rejection(
                site,
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                $"The {ex.Dimension} budget cannot accept a charge of "
                    + $"{ex.AttemptedCharge} at limit {ex.Limit}.",
                ex.Dimension,
                ex.Limit,
                ex.AttemptedCharge);
        }
    }

    void EnsureCanCharge(
        MetadataMethodImplementationFailureSite site,
        MetadataOperationDimension dimension,
        long amount)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _context.EnsureCanCharge(dimension, amount);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            throw Rejection(
                site,
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                $"The {ex.Dimension} budget cannot accept a charge of "
                    + $"{ex.AttemptedCharge} at limit {ex.Limit}.",
                ex.Dimension,
                ex.Limit,
                ex.AttemptedCharge);
        }
    }

    T Read<T>(
        MetadataMethodImplementationFailureSite site,
        Func<T> read)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            return read();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            throw Rejection(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                ex.Message);
        }
    }

    void Reject(
        MetadataMethodImplementationFailureSite site,
        MetadataMethodImplementationFailureReason reason,
        string detail) =>
        throw Rejection(site, reason, detail);

    MetadataMethodImplementationRejectedException Rejection(
        MetadataMethodImplementationFailureSite site,
        MetadataMethodImplementationFailureReason reason,
        string detail,
        MetadataOperationDimension? budgetDimension = null,
        long? budgetLimit = null,
        long? attemptedCharge = null) =>
        new(
            reason,
            site.Stage,
            site.Mechanism,
            detail,
            site.RelevantRow,
            site.RelevantHandle,
            budgetDimension,
            budgetLimit,
            attemptedCharge);

    static bool SignatureIsDegraded(
        MethodSignature<TypeNode> signature) =>
        signature.ReturnType.IsDegraded
        || signature.ParameterTypes.Any(
            parameter => parameter.IsDegraded);

    static int GetDeclaredTypeParameterCount(TypeNode node)
    {
        MetadataTypeNameParts? parts = node switch
        {
            NamedTypeNode named => named.MetadataName,
            GenericTypeNode generic => generic.MetadataName,
            _ => null,
        };
        if (parts is null)
            return 0;

        if (parts.IntroducedTypeParameterCounts is { } trusted
            && trusted.Count == parts.Segments.Count)
        {
            int trustedCount = 0;
            foreach (int count in trusted)
                trustedCount = checked(trustedCount + count);
            return trustedCount;
        }

        int declaredCount = 0;
        foreach (string segment in parts.Segments)
        {
            declaredCount = checked(
                declaredCount
                    + MetadataNameArity.OfSegment(segment));
        }
        return declaredCount;
    }

    void EnsureCompleteType(
        TypeNode node,
        MetadataMethodImplementationFailureSite site)
    {
        if (node.IsDegraded)
        {
            Reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "The declaration owner type could not be decoded completely.");
        }
    }

    bool IsValid(TypeDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(TableIndex.TypeDef);
    }

    bool IsValid(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(TableIndex.MethodDef);
    }

    bool IsValid(MemberReferenceHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(TableIndex.MemberRef);
    }

    bool IsValid(TypeReferenceHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(TableIndex.TypeRef);
    }

    bool IsValid(TypeSpecificationHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= _reader.GetTableRowCount(TableIndex.TypeSpec);
    }

    bool IsValidType(EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                IsValid((TypeDefinitionHandle)handle),
            HandleKind.TypeReference =>
                IsValid((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification =>
                IsValid((TypeSpecificationHandle)handle),
            _ => false,
        };

    bool IsReadableMethodDefOrRef(EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.MethodDefinition =>
                IsValid((MethodDefinitionHandle)handle),
            HandleKind.MemberReference =>
                IsValid((MemberReferenceHandle)handle),
            _ => false,
        };

    readonly record struct OwnerEvidence(
        TypeNode Node,
        bool IsLocal,
        TypeDefinitionHandle LocalDefinition,
        ImmutableArray<TypeNode> TypeArguments,
        int FormalTypeParameterCount);

    readonly record struct LocalTypeResolution(
        bool IsLocal,
        TypeDefinitionHandle Definition);

    readonly record struct TypeReferenceBinding(
        EntityHandle Terminal,
        bool IsLocal,
        bool IsBound,
        TypeDefinitionHandle Definition);
}

internal sealed class MetadataMethodImplementationRejectedException(
    MetadataMethodImplementationFailureReason reason,
    MetadataMethodImplementationStage stage,
    MetadataMethodImplementationMechanism mechanism,
    string detail,
    MethodImplementationHandle? relevantRow,
    EntityHandle relevantHandle,
    MetadataOperationDimension? budgetDimension = null,
    long? budgetLimit = null,
    long? attemptedCharge = null) : Exception(detail)
{
    internal MetadataMethodImplementationFailureReason Reason { get; } =
        reason;

    internal MetadataMethodImplementationStage Stage { get; } = stage;

    internal MetadataMethodImplementationMechanism Mechanism { get; } =
        mechanism;

    internal MethodImplementationHandle? RelevantRow { get; } =
        relevantRow;

    internal EntityHandle RelevantHandle { get; } = relevantHandle;

    internal MetadataOperationDimension? BudgetDimension { get; } =
        budgetDimension;

    internal long? BudgetLimit { get; } = budgetLimit;

    internal long? AttemptedCharge { get; } = attemptedCharge;
}
