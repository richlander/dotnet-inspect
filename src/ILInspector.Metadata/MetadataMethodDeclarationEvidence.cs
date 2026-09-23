using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;
using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

public readonly record struct MetadataMethodDeclarationRequest(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method);

public enum MetadataMethodDeclarationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    Cycle,
    BudgetExceeded,
    UnsupportedShape,
}

public enum MetadataMethodDeclarationStage
{
    RequestValidation,
    MethodDefinitionRead,
    GenericContextRead,
    SignatureDecode,
    ParameterCorrespondence,
    MarkerRead,
    ResultRetention,
}

public enum MetadataMethodDeclarationMechanism
{
    ImageAdmission,
    AddressResolution,
    DirectOwnership,
    RowRead,
    HandleValidation,
    RelationshipTraversal,
    TypeSpecificationRoot,
    SignatureDecode,
    CustomAttributeIdentification,
    TextRetention,
}

public sealed record MetadataMethodDeclarationFailure(
    MetadataMethodDeclarationRequest Request,
    MetadataMethodDeclarationFailureReason Reason,
    MetadataMethodDeclarationStage Stage,
    MetadataMethodDeclarationMechanism Mechanism,
    string Detail,
    EntityHandle RelevantHandle,
    MetadataOperationDimension? BudgetDimension = null,
    long? BudgetLimit = null,
    long? AttemptedCharge = null);

public abstract record MetadataMethodDeclarationResult
{
    private protected MetadataMethodDeclarationResult(
        MetadataOperationCounters counters) => Counters = counters;

    public MetadataOperationCounters Counters { get; }

    public sealed record Posted : MetadataMethodDeclarationResult
    {
        internal Posted(
            MetadataMethodDeclarationEvidence evidence,
            MetadataOperationCounters counters) : base(counters) =>
            Evidence = evidence;

        public MetadataMethodDeclarationEvidence Evidence { get; }
    }

    public sealed record Rejected : MetadataMethodDeclarationResult
    {
        internal Rejected(
            MetadataMethodDeclarationFailure failure,
            MetadataOperationCounters counters) : base(counters) =>
            Failure = failure;

        public MetadataMethodDeclarationFailure Failure { get; }
    }
}

public enum MetadataConstructorCandidate
{
    None,
    InstanceConstructorCandidate,
    StaticConstructorCandidate,
}

public sealed record MetadataMarkerCount(int Count, bool IsComplete);

public sealed record MetadataParameterMarkerEvidence(
    bool IsComplete,
    int IsReadOnlyCount,
    int RequiresLocationCount,
    int ParamArrayCount,
    int ParamCollectionCount,
    int ScopedRefCount,
    int UnscopedRefCount);

public sealed record MetadataParameterDeclarationEvidence(
    bool HasRow,
    InertString? Name,
    ParameterAttributes Attributes,
    MetadataParameterMarkerEvidence Markers);

public sealed record MetadataGenericParameterDeclarationEvidence(
    bool IsMethodParameter,
    int Index,
    InertString Name,
    GenericParameterAttributes Attributes,
    ImmutableArray<MetadataTypeIdentity> Constraints,
    MetadataMarkerCount IsUnmanaged);

public sealed record MetadataMethodDeclarationEvidence(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method,
    InertString Name,
    MethodAttributes Attributes,
    MethodImplAttributes ImplementationAttributes,
    bool HasBodyRva,
    MetadataMethodSignatureIdentity Signature,
    ImmutableArray<MetadataGenericParameterDeclarationEvidence>
        TypeParameters,
    ImmutableArray<MetadataGenericParameterDeclarationEvidence>
        MethodParameters,
    MetadataParameterDeclarationEvidence ReturnParameter,
    ImmutableArray<MetadataParameterDeclarationEvidence> Parameters,
    MetadataConstructorCandidate ConstructorCandidate,
    bool OperatorCandidate,
    bool FinalizerShapeCandidate);

internal readonly record struct MetadataMethodDeclarationSite(
    MetadataMethodDeclarationStage Stage,
    MetadataMethodDeclarationMechanism Mechanism,
    EntityHandle Handle = default);

internal enum MetadataDeclarationMarkerKind
{
    IsReadOnly,
    RequiresLocation,
    ParamArray,
    ParamCollection,
    ScopedRef,
    UnscopedRef,
    IsUnmanaged,
}

internal readonly record struct MetadataDeclarationMarkerRead(
    bool IsComplete,
    MetadataDeclarationMarkerKind? Kind);

internal sealed class MetadataMethodDeclarationEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly Func<Action, Action<TypeDefinitionHandle>, Action,
        Action<int>, MetadataTypeDefinitionIndex> _getIndex;
    readonly HashSet<TypeSpecificationHandle> _validatedSpecs = [];
    MetadataMethodDeclarationRequest _request;
    TypeSpecificationHandle _activeSpec;
    CancellationToken _token;

    internal MetadataMethodDeclarationEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        Func<Action, Action<TypeDefinitionHandle>, Action,
            Action<int>, MetadataTypeDefinitionIndex> getIndex)
    {
        _reader = reader;
        _context = context;
        _getIndex = getIndex;
    }

    internal MetadataMethodDeclarationResult Post(
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress method,
        CancellationToken token)
    {
        _request = new(type, method);
        _token = token;
        try
        {
            var site = new MetadataMethodDeclarationSite(
                MetadataMethodDeclarationStage.RequestValidation,
                MetadataMethodDeclarationMechanism.AddressResolution);
            token.ThrowIfCancellationRequested();
            if (!type.TryResolve(_reader, out TypeDefinitionHandle typeHandle)
                || !method.TryResolve(_reader, out MethodDefinitionHandle methodHandle))
            {
                throw Refuse(site,
                    MetadataMethodDeclarationFailureReason.InvalidRequest,
                    "The requested TypeDef or MethodDef does not resolve in this image.");
            }
            Charge(site, MetadataOperationDimension.DeclarationCandidates);
            ValidateOwnerRangeOrdering();
            site = new(MetadataMethodDeclarationStage.MethodDefinitionRead,
                MetadataMethodDeclarationMechanism.RowRead, methodHandle);
            MethodDefinition definition = Read(site,
                () => _reader.GetMethodDefinition(methodHandle));
            Charge(site with { Mechanism =
                MetadataMethodDeclarationMechanism.DirectOwnership },
                MetadataOperationDimension.RelationshipEdges);
            if (Read(site, definition.GetDeclaringType) != typeHandle)
            {
                throw Refuse(site with { Mechanism =
                    MetadataMethodDeclarationMechanism.DirectOwnership },
                    MetadataMethodDeclarationFailureReason.InvalidRequest,
                    "The MethodDef is not directly declared by the requested TypeDef.");
            }
            TypeDefinition owner = Read(site,
                () => _reader.GetTypeDefinition(typeHandle));
            var genericSite = new MetadataMethodDeclarationSite(
                MetadataMethodDeclarationStage.GenericContextRead,
                MetadataMethodDeclarationMechanism.RelationshipTraversal,
                typeHandle);
            ChargeDeclaringTypeGenericParameterEdges(
                owner,
                genericSite);
            ImmutableArray<GenericParameterHandle> typeGenericHandles =
                ReadGenericParameterHandles(
                    owner.GetGenericParameters,
                    genericSite);
            ImmutableArray<GenericParameterHandle> methodGenericHandles =
                ReadGenericParameterHandles(
                    definition.GetGenericParameters,
                    genericSite with { Handle = methodHandle });
            GenericContext generic = CreateContext(genericSite, owner, definition);
            var signatureSite = new MetadataMethodDeclarationSite(
                MetadataMethodDeclarationStage.SignatureDecode,
                MetadataMethodDeclarationMechanism.SignatureDecode,
                methodHandle);
            BlobHandle signatureBlob = Read(signatureSite, () => definition.Signature);
            Charge(signatureSite, MetadataOperationDimension.SignatureBytes,
                Read(signatureSite, () => _reader.GetBlobReader(signatureBlob).Length));
            SignatureBlobGuard.CompleteValidationKind validation = Read(
                signatureSite, () => SignatureBlobGuard.ValidateComplete(
                    _reader, signatureBlob, SignatureBlobGuard.Kind.Method));
            if (validation != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                throw Refuse(signatureSite,
                    validation is SignatureBlobGuard.CompleteValidationKind.DepthBudgetExceeded
                        or SignatureBlobGuard.CompleteValidationKind.NodeBudgetExceeded
                        ? MetadataMethodDeclarationFailureReason.BudgetExceeded
                        : MetadataMethodDeclarationFailureReason.MalformedMetadata,
                    "The MethodDef signature is incomplete or exceeds structural limits.");
            }
            var decoded = Read(signatureSite, () =>
                GuardedProviderDecode.MethodResult(
                    _reader, signatureBlob, Provider(signatureSite),
                    generic, (TypeNode)new DegradedTypeNode()));
            MethodSignature<TypeNode> signature = decoded.Value;
            if (decoded.IsDegraded
                || signature.ReturnType.IsDegraded
                || signature.ParameterTypes.Any(p => p.IsDegraded)
                || signature.GenericParameterCount != generic.MethodParameters.Count)
            {
                throw Refuse(signatureSite,
                    MetadataMethodDeclarationFailureReason.MalformedMetadata,
                    "The MethodDef signature or generic arity is incomplete.");
            }
            if (signature.Header.Kind != SignatureKind.Method)
            {
                throw Refuse(
                    signatureSite,
                    MetadataMethodDeclarationFailureReason.MalformedMetadata,
                    "The MethodDef row does not carry a method signature.");
            }
            ValidateType(signature.ReturnType, generic, signatureSite);
            foreach (TypeNode parameter in signature.ParameterTypes)
                ValidateType(parameter, generic, signatureSite);

            var retentionSite = new MetadataMethodDeclarationSite(
                MetadataMethodDeclarationStage.ResultRetention,
                MetadataMethodDeclarationMechanism.TextRetention,
                methodHandle);
            string name = ReadName(definition.Name, retentionSite);
            InertString inertName = Retain(name, retentionSite);
            ImmutableArray<MetadataGenericParameterDeclarationEvidence>
                typeParameters = ReadGenerics(
                    typeGenericHandles,
                    false,
                    new GenericContext(generic.TypeParameters, []),
                    genericSite);
            ImmutableArray<MetadataGenericParameterDeclarationEvidence>
                methodParameters = ReadGenerics(
                    methodGenericHandles,
                    true,
                    generic,
                    genericSite);

            var paramSite = new MetadataMethodDeclarationSite(
                MetadataMethodDeclarationStage.ParameterCorrespondence,
                MetadataMethodDeclarationMechanism.RowRead,
                methodHandle);
            var rows = new MetadataParameterDeclarationEvidence?[
                signature.ParameterTypes.Length + 1];
            ImmutableArray<ParameterHandle> parameterHandles =
                ReadParameterHandles(definition, paramSite);
            foreach (ParameterHandle handle in parameterHandles)
            {
                (int sequence,
                    StringHandle parameterNameHandle,
                    ParameterAttributes parameterAttributes,
                    CustomAttributeHandleCollection markerRange) =
                    Read(
                        paramSite with { Handle = handle },
                        () =>
                        {
                            Parameter row =
                                _reader.GetParameter(handle);
                            return (
                                row.SequenceNumber,
                                row.Name,
                                row.Attributes,
                                row.GetCustomAttributes());
                        });
                if ((uint)sequence >= (uint)rows.Length || rows[sequence] is not null)
                {
                    throw Refuse(paramSite with { Handle = handle },
                        MetadataMethodDeclarationFailureReason.MalformedMetadata,
                        "The Param sequence is duplicated or outside the signature.");
                }
                var markerSite = new MetadataMethodDeclarationSite(
                    MetadataMethodDeclarationStage.MarkerRead,
                    MetadataMethodDeclarationMechanism.CustomAttributeIdentification,
                    handle);
                ImmutableArray<CustomAttributeHandle> markerHandles =
                    ReadCustomAttributeHandles(
                        () => markerRange,
                        markerSite);
                MetadataParameterMarkerEvidence markers =
                    ReadParameterMarkers(markerHandles, markerSite);
                InertString parameterName =
                    Retain(
                        ReadName(parameterNameHandle, markerSite),
                        markerSite);
                Charge(paramSite with { Handle = handle },
                    MetadataOperationDimension.StructuredNodes);
                rows[sequence] = new(
                    true,
                    parameterName,
                    parameterAttributes,
                    markers);
            }
            Charge(paramSite, MetadataOperationDimension.StructuredNodes, 2);
            var noRow = new MetadataParameterDeclarationEvidence(
                false, null, 0, new(true, 0, 0, 0, 0, 0, 0));
            var parameters = ImmutableArray.CreateBuilder<
                MetadataParameterDeclarationEvidence>(rows.Length - 1);
            for (int i = 1; i < rows.Length; i++)
                parameters.Add(rows[i] ?? noRow);
            bool special = (definition.Attributes & MethodAttributes.SpecialName) != 0;
            bool runtimeSpecial = (definition.Attributes & MethodAttributes.RTSpecialName) != 0;
            MetadataConstructorCandidate constructor =
                special && runtimeSpecial
                    ? name switch
                    {
                        ".ctor" => MetadataConstructorCandidate.InstanceConstructorCandidate,
                        ".cctor" => MetadataConstructorCandidate.StaticConstructorCandidate,
                        _ => MetadataConstructorCandidate.None,
                    }
                    : MetadataConstructorCandidate.None;
            var identity = ProjectSignature(signature, retentionSite);
            bool finalizer = name == "Finalize"
                && (definition.Attributes & MethodAttributes.Static) == 0
                && signature.Header.IsInstance
                && signature.ParameterTypes.IsEmpty
                && signature.ReturnType is PrimitiveTypeNode
                    { Name: "void" };
            Charge(retentionSite, MetadataOperationDimension.StructuredNodes);
            _context.ObserveWork(
                MetadataOperationWorkKind.MethodDeclarationPublication);
            token.ThrowIfCancellationRequested();
            return new MetadataMethodDeclarationResult.Posted(
                new(type, method, inertName, definition.Attributes,
                    definition.ImplAttributes, definition.RelativeVirtualAddress != 0,
                    identity, typeParameters, methodParameters,
                    rows[0] ?? noRow, parameters.MoveToImmutable(),
                    constructor, IsOperatorCandidate(name, special),
                    finalizer),
                _context.Counters);
        }
        catch (DeclarationRejectedException ex)
        {
            return new MetadataMethodDeclarationResult.Rejected(
                new(_request, ex.Reason, ex.Site.Stage, ex.Site.Mechanism,
                    ex.Message, ex.Site.Handle, ex.Dimension, ex.Limit,
                    ex.Attempted), _context.Counters);
        }
    }

    GenericContext CreateContext(
        MetadataMethodDeclarationSite site,
        TypeDefinition type,
        MethodDefinition method)
    {
        Charge(site, MetadataOperationDimension.StructuredNodes);
        try
        {
            return Read(site, () => GenericContext.ForMethodWithRelationshipObserver(
                _reader, type, method,
                _ => _context.ObserveWork(
                    MetadataOperationWorkKind.GenericParameterNameMaterialization),
                name => Charge(site, MetadataOperationDimension.RetainedText,
                    VisualEncoder.MeasureEncodedLength(TextPolicy.Field, name)),
                _ => { }));
        }
        catch (GenericContextRelationshipRejectedException ex)
        {
            throw Refuse(site with { Handle = ex.Rejection.Subject },
                ex.Rejection.Kind switch
                {
                    RelationshipTraversalRejectionKind.Cycle =>
                        MetadataMethodDeclarationFailureReason.Cycle,
                    RelationshipTraversalRejectionKind.NodeBudget =>
                        MetadataMethodDeclarationFailureReason.BudgetExceeded,
                    _ => MetadataMethodDeclarationFailureReason.MalformedMetadata,
                }, ex.Message);
        }
        catch (GenericContextBudgetExceededException ex)
        {
            throw Refuse(site, MetadataMethodDeclarationFailureReason.BudgetExceeded,
                ex.Message);
        }
    }

    ImmutableArray<MetadataGenericParameterDeclarationEvidence> ReadGenerics(
        ImmutableArray<GenericParameterHandle> handles,
        bool isMethod,
        GenericContext context,
        MetadataMethodDeclarationSite site)
    {
        var result = ImmutableArray.CreateBuilder<
            MetadataGenericParameterDeclarationEvidence>(handles.Length);
        foreach (GenericParameterHandle handle in handles)
        {
            GenericParameter parameter = Read(site with { Handle = handle },
                () => _reader.GetGenericParameter(handle));
            if (parameter.Index != result.Count)
            {
                throw Refuse(site with { Handle = handle },
                    MetadataMethodDeclarationFailureReason.MalformedMetadata,
                    "Generic parameter indices must be contiguous.");
            }
            var constraints = ImmutableArray.CreateBuilder<MetadataTypeIdentity>();
            ImmutableArray<GenericParameterConstraintHandle>
                constraintHandles = ReadConstraintHandles(
                    parameter,
                    site with { Handle = handle });
            foreach (GenericParameterConstraintHandle constraintHandle in
                constraintHandles)
            {
                GenericParameterConstraint constraint = Read(
                    site with { Handle = constraintHandle },
                    () => _reader.GetGenericParameterConstraint(constraintHandle));
                EntityHandle target = Read(
                    site with { Handle = constraintHandle },
                    () => constraint.Type);
                Charge(site with { Handle = target },
                    MetadataOperationDimension.RelationshipEdges);
                if (!IsValidType(target))
                {
                    throw Refuse(site with { Handle = target },
                        MetadataMethodDeclarationFailureReason.MalformedMetadata,
                        "The GenericParamConstraint target is invalid.");
                }
                TypeNode node = DecodeConstraint(target, context,
                    site with { Handle = target });
                ValidateType(node, context, site with { Handle = target });
                constraints.Add(Project(node, site with { Handle = target }));
            }
            ImmutableArray<CustomAttributeHandle> markerHandles =
                ReadCustomAttributeHandles(
                    parameter.GetCustomAttributes,
                    site with { Handle = handle });
            var markers = ReadUnmanagedMarkers(
                markerHandles,
                site with
                {
                    Stage = MetadataMethodDeclarationStage.MarkerRead,
                    Mechanism =
                        MetadataMethodDeclarationMechanism
                            .CustomAttributeIdentification,
                    Handle = handle,
                });
            Charge(site, MetadataOperationDimension.StructuredNodes);
            result.Add(new(isMethod, parameter.Index,
                Retain(ReadName(parameter.Name, site), site),
                parameter.Attributes, constraints.ToImmutable(), markers));
        }
        return result.MoveToImmutable();
    }

    TypeNode DecodeConstraint(EntityHandle target, GenericContext generic,
        MetadataMethodDeclarationSite site)
    {
        TypeNodeProvider provider = Provider(site);
        return target.Kind switch
        {
            HandleKind.TypeDefinition => Read(site, () =>
                provider.GetTypeFromDefinition(_reader,
                    (TypeDefinitionHandle)target, 0x12)),
            HandleKind.TypeReference => Read(site, () =>
                provider.GetTypeFromReference(_reader,
                    (TypeReferenceHandle)target, 0x12)),
            HandleKind.TypeSpecification =>
                DecodeConstraintTypeSpecification(
                    (TypeSpecificationHandle)target,
                    provider,
                    generic,
                    site),
            _ => throw Refuse(site,
                MetadataMethodDeclarationFailureReason.MalformedMetadata,
                "The constraint does not reference a type."),
        };
    }

    TypeNode DecodeConstraintTypeSpecification(
        TypeSpecificationHandle handle,
        TypeNodeProvider provider,
        GenericContext generic,
        MetadataMethodDeclarationSite site)
    {
        ValidateSpec(site, _reader, handle);
        return Read(
            site,
            () => GuardedProviderDecode.TypeSpec(
                _reader,
                handle,
                provider,
                generic,
                (TypeNode)new DegradedTypeNode()));
    }

    ImmutableArray<GenericParameterHandle> ReadGenericParameterHandles(
        Func<GenericParameterHandleCollection> readRange,
        MetadataMethodDeclarationSite site)
    {
        GenericParameterHandleCollection range = Read(site, readRange);
        int count = Read(site, () => range.Count);
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges,
            count);
        return Read(site, () => range.ToImmutableArray());
    }

    ImmutableArray<GenericParameterConstraintHandle>
        ReadConstraintHandles(
            GenericParameter parameter,
            MetadataMethodDeclarationSite site)
    {
        GenericParameterConstraintHandleCollection range = Read(
            site,
            parameter.GetConstraints);
        int count = Read(site, () => range.Count);
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges,
            count);
        return Read(site, () => range.ToImmutableArray());
    }

    ImmutableArray<ParameterHandle> ReadParameterHandles(
        MethodDefinition method,
        MetadataMethodDeclarationSite site)
    {
        ParameterHandleCollection range = Read(
            site,
            method.GetParameters);
        int count = Read(site, () => range.Count);
        if (count < 0)
        {
            throw Refuse(
                site,
                MetadataMethodDeclarationFailureReason.MalformedMetadata,
                "The MethodDef ParamList range has a negative row count.");
        }
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges,
            count);
        return Read(site, () => range.ToImmutableArray());
    }

    ImmutableArray<CustomAttributeHandle> ReadCustomAttributeHandles(
        Func<CustomAttributeHandleCollection> readRange,
        MetadataMethodDeclarationSite site)
    {
        CustomAttributeHandleCollection range = Read(site, readRange);
        int count = Read(site, () => range.Count);
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges,
            count);
        return Read(site, () => range.ToImmutableArray());
    }

    void ChargeDeclaringTypeGenericParameterEdges(
        TypeDefinition type,
        MetadataMethodDeclarationSite site)
    {
        TypeDefinitionHandle declaringType = Read(
            site,
            type.GetDeclaringType);
        if (declaringType.IsNil)
            return;

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        bool complete;
        int consumed;
        EntityHandle terminal;
        RelationshipTraversalRejection? rejection;
        try
        {
            complete = MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    _reader,
                    declaringType,
                    chain,
                    out consumed,
                    out terminal,
                    out rejection,
                    handle => Charge(
                        site with { Handle = handle },
                        MetadataOperationDimension.RelationshipEdges));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            throw Refuse(
                site,
                MetadataMethodDeclarationFailureReason.MalformedMetadata,
                ex.Message);
        }

        if (!complete)
        {
            RelationshipTraversalRejection failure =
                rejection
                ?? throw new InvalidOperationException(
                    "An incomplete declaring-type traversal lacks a rejection.");
            throw Refuse(
                site with { Handle = failure.Subject },
                failure.Kind switch
                {
                    RelationshipTraversalRejectionKind.Cycle =>
                        MetadataMethodDeclarationFailureReason.Cycle,
                    RelationshipTraversalRejectionKind.NodeBudget =>
                        MetadataMethodDeclarationFailureReason
                            .BudgetExceeded,
                    _ =>
                        MetadataMethodDeclarationFailureReason
                            .MalformedMetadata,
                },
                failure.Detail);
        }
        if (!terminal.IsNil)
        {
            throw Refuse(
                site with { Handle = terminal },
                MetadataMethodDeclarationFailureReason.MalformedMetadata,
                "A TypeDef declaring chain has a non-nil terminal.");
        }

        for (int index = 0; index < consumed; index++)
        {
            TypeDefinitionHandle handle = chain[index];
            TypeDefinition definition = Read(
                site with { Handle = handle },
                () => _reader.GetTypeDefinition(handle));
            _ = ReadGenericParameterHandles(
                definition.GetGenericParameters,
                site with { Handle = handle });
        }
    }

    void ValidateType(TypeNode node, GenericContext context,
        MetadataMethodDeclarationSite site)
    {
        if (node.IsDegraded)
        {
            throw Refuse(site,
                MetadataMethodDeclarationFailureReason.MalformedMetadata,
                "A declaration type cannot be decoded completely.");
        }
        string? invalid = MetadataStructuralTypeValidator.Validate(
            node, context.TypeParameters.Count, context.MethodParameters.Count,
            "The declaration type");
        if (invalid is not null)
            throw Refuse(site, MetadataMethodDeclarationFailureReason.MalformedMetadata,
                invalid);
    }

    MetadataMethodSignatureIdentity ProjectSignature(
        MethodSignature<TypeNode> signature, MetadataMethodDeclarationSite site)
    {
        Charge(site, MetadataOperationDimension.StructuredNodes);
        var parameters = ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
            signature.ParameterTypes.Length);
        foreach (TypeNode parameter in signature.ParameterTypes)
            parameters.Add(Project(parameter, site));
        return new(signature.Header.RawValue, signature.GenericParameterCount,
            signature.RequiredParameterCount, Project(signature.ReturnType, site),
            parameters.MoveToImmutable());
    }

    MetadataTypeIdentity Project(TypeNode node, MetadataMethodDeclarationSite site) =>
        new MetadataTypeIdentityProjector(
            () => Charge(site, MetadataOperationDimension.StructuredNodes),
            text => Retain(text, site),
            detail => Refuse(site,
                MetadataMethodDeclarationFailureReason.MalformedMetadata, detail))
            .Project(node);

    TypeNodeProvider Provider(MetadataMethodDeclarationSite site) =>
        new(
            beforeRetain: value => Charge(ActiveSite(site),
                MetadataOperationDimension.RetainedText, value.Length),
            beforeMaterialize: amount => Charge(ActiveSite(site),
                MetadataOperationDimension.StructuredNodes, amount),
            beforeCreateNode: () => Charge(ActiveSite(site),
                MetadataOperationDimension.StructuredNodes),
            beforeTypeSpecificationDecode: (reader, handle) =>
                ValidateSpec(site, reader, handle),
            retainExactScope: true,
            beforeTypeDefinitionResolve: (_, handle) => Charge(
                site with { Handle = handle }, MetadataOperationDimension.RelationshipEdges),
            beforeTypeReferenceResolve: (_, handle) => Charge(
                site with { Handle = handle }, MetadataOperationDimension.RelationshipEdges),
            typeSpecificationScope: EnterSpec,
            relationshipRejected: rejection => throw Refuse(
                site with { Handle = rejection.Subject,
                    Mechanism = MetadataMethodDeclarationMechanism.RelationshipTraversal },
                rejection.Kind == RelationshipTraversalRejectionKind.Cycle
                    ? MetadataMethodDeclarationFailureReason.Cycle
                    : rejection.Kind is
                        RelationshipTraversalRejectionKind.NodeBudget
                            or RelationshipTraversalRejectionKind.NameBudget
                        ? MetadataMethodDeclarationFailureReason.BudgetExceeded
                        : MetadataMethodDeclarationFailureReason.MalformedMetadata,
                rejection.Detail),
            beforeRelationshipFollow: handle => Charge(site with { Handle = handle },
                MetadataOperationDimension.RelationshipEdges),
            getLocalTypeDefinitions: reader => Index(site, reader));

    MetadataTypeDefinitionIndex Index(MetadataMethodDeclarationSite site,
        MetadataReader reader)
    {
        if (!ReferenceEquals(reader, _reader))
            throw new InvalidOperationException("A foreign reader reached the TypeDef index.");
        try
        {
            return _getIndex(_token.ThrowIfCancellationRequested,
                handle => Charge(site with { Handle = handle },
                    MetadataOperationDimension.RelationshipEdges),
                () => Charge(site, MetadataOperationDimension.StructuredNodes),
                amount => Charge(site, MetadataOperationDimension.RetainedText, amount));
        }
        catch (MetadataTypeDefinitionIndexFailureException ex)
        {
            throw Refuse(site with { Handle = ex.Subject },
                ex.Kind switch
                {
                    MetadataTypeDefinitionIndexFailureKind.Cycle =>
                        MetadataMethodDeclarationFailureReason.Cycle,
                    MetadataTypeDefinitionIndexFailureKind.BudgetExceeded =>
                        MetadataMethodDeclarationFailureReason.BudgetExceeded,
                    _ => MetadataMethodDeclarationFailureReason.MalformedMetadata,
                }, ex.Message);
        }
    }

    MetadataMethodDeclarationSite ActiveSite(MetadataMethodDeclarationSite site) =>
        _activeSpec.IsNil ? site : site with { Handle = _activeSpec };

    IDisposable EnterSpec(MetadataReader reader, TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
            throw new InvalidOperationException("A foreign reader reached the TypeSpec.");
        TypeSpecificationHandle previous = _activeSpec;
        _activeSpec = handle;
        return new SpecScope(this, previous);
    }

    sealed class SpecScope(
        MetadataMethodDeclarationEvidenceOperation owner,
        TypeSpecificationHandle previous) : IDisposable
    {
        public void Dispose() => owner._activeSpec = previous;
    }

    void ValidateSpec(
        MetadataMethodDeclarationSite parent,
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!ReferenceEquals(reader, _reader))
            throw new InvalidOperationException("A foreign reader reached the TypeSpec.");
        MetadataMethodDeclarationSite site = parent with
        {
            Mechanism =
                MetadataMethodDeclarationMechanism.TypeSpecificationRoot,
            Handle = handle,
        };
        BlobHandle blob = Read(site, () => reader.GetTypeSpecification(handle).Signature);
        Charge(site, MetadataOperationDimension.SignatureBytes,
            Read(site, () => reader.GetBlobReader(blob).Length));
        if (_validatedSpecs.Contains(handle))
            return;
        TypeSpecificationRootReadResult? failure = TypeSpecificationRoot.ValidateGraph(
            reader, handle,
            (active, bytes) => Charge(site with { Handle = active },
                MetadataOperationDimension.SignatureBytes, bytes),
            dependency => Charge(site with { Handle = dependency },
                MetadataOperationDimension.RelationshipEdges),
            validated => _validatedSpecs.Add(validated));
        if (failure is not null)
        {
            throw failure switch
            {
                TypeSpecificationRootReadResult.Cycle cycle =>
                    Refuse(site with { Handle = cycle.Subject },
                        MetadataMethodDeclarationFailureReason.Cycle, cycle.Detail),
                TypeSpecificationRootReadResult.BudgetExceeded budget =>
                    Refuse(site with { Handle = budget.Subject },
                        MetadataMethodDeclarationFailureReason.BudgetExceeded, budget.Detail),
                TypeSpecificationRootReadResult.Malformed malformed =>
                    Refuse(site with { Handle = malformed.Subject },
                        MetadataMethodDeclarationFailureReason.MalformedMetadata,
                        malformed.Detail),
                TypeSpecificationRootReadResult.Unsupported unsupported =>
                    Refuse(site with { Handle = unsupported.Subject },
                        MetadataMethodDeclarationFailureReason.UnsupportedShape,
                        unsupported.Detail),
                _ => throw new InvalidOperationException("Unknown TypeSpec result."),
            };
        }
    }

    MetadataMarkerCount ReadUnmanagedMarkers(
        ImmutableArray<CustomAttributeHandle> attributes,
        MetadataMethodDeclarationSite site)
    {
        int count = 0;
        bool complete = true;
        foreach (CustomAttributeHandle handle in attributes)
        {
            MetadataDeclarationMarkerRead marker =
                ReadAttributeMarker(handle, site);
            complete &= marker.IsComplete;
            if (marker.Kind
                == MetadataDeclarationMarkerKind.IsUnmanaged)
            {
                count++;
            }
        }
        Charge(site, MetadataOperationDimension.StructuredNodes);
        return new(count, complete);
    }

    MetadataParameterMarkerEvidence ReadParameterMarkers(
        ImmutableArray<CustomAttributeHandle> attributes,
        MetadataMethodDeclarationSite site)
    {
        bool complete = true;
        int readOnly = 0, location = 0, array = 0, collection = 0,
            scoped = 0, unscoped = 0;
        foreach (CustomAttributeHandle handle in attributes)
        {
            MetadataDeclarationMarkerRead marker =
                ReadAttributeMarker(handle, site);
            complete &= marker.IsComplete;
            switch (marker.Kind)
            {
                case MetadataDeclarationMarkerKind.IsReadOnly:
                    readOnly++;
                    break;
                case MetadataDeclarationMarkerKind.RequiresLocation:
                    location++;
                    break;
                case MetadataDeclarationMarkerKind.ParamArray:
                    array++;
                    break;
                case MetadataDeclarationMarkerKind.ParamCollection:
                    collection++;
                    break;
                case MetadataDeclarationMarkerKind.ScopedRef:
                    scoped++;
                    break;
                case MetadataDeclarationMarkerKind.UnscopedRef:
                    unscoped++;
                    break;
            }
        }
        Charge(site, MetadataOperationDimension.StructuredNodes);
        return new(complete, readOnly, location, array, collection, scoped, unscoped);
    }

    MetadataDeclarationMarkerRead ReadAttributeMarker(
        CustomAttributeHandle handle,
        MetadataMethodDeclarationSite site)
    {
        site = site with { Handle = handle };
        EntityHandle constructor = Read(
            site,
            () => _reader.GetCustomAttribute(handle).Constructor);
        Charge(site, MetadataOperationDimension.RelationshipEdges);
        EntityHandle parent = constructor.Kind switch
        {
            HandleKind.MemberReference => Read(
                site,
                () => _reader.GetMemberReference(
                    (MemberReferenceHandle)constructor).Parent),
            HandleKind.MethodDefinition => Read(
                site,
                () => _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructor)
                    .GetDeclaringType()),
            _ => default,
        };
        if (parent.IsNil)
            return new(false, null);

        Charge(
            site with { Handle = parent },
            MetadataOperationDimension.RelationshipEdges);
        MetadataTypeDefinitionName? name = Read(
            site,
            () => ResolveMarkerTypeName(parent, site));
        return name is null
            ? new(false, null)
            : new(true, ClassifyMarker(name));
    }

    MetadataTypeDefinitionName? ResolveMarkerTypeName(
        EntityHandle parent,
        MetadataMethodDeclarationSite site)
    {
        bool resolved;
        string? name;
        RelationshipTraversalRejection? rejection;
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                resolved = TypeResolver.TryGetTypeNameFromDefinition(
                    _reader,
                    (TypeDefinitionHandle)parent,
                    amount => Charge(
                        site,
                        MetadataOperationDimension.StructuredNodes,
                        amount),
                    out name,
                    out rejection,
                    enforceCharacterBudget: true,
                    beforeRelationshipFollow: handle => Charge(
                        site with { Handle = handle },
                        MetadataOperationDimension.RelationshipEdges));
                break;
            case HandleKind.TypeReference:
                resolved = TypeResolver.TryGetTypeNameFromReference(
                    _reader,
                    (TypeReferenceHandle)parent,
                    amount => Charge(
                        site,
                        MetadataOperationDimension.StructuredNodes,
                        amount),
                    out name,
                    out rejection,
                    enforceCharacterBudget: true,
                    beforeRelationshipFollow: handle => Charge(
                        site with { Handle = handle },
                        MetadataOperationDimension.RelationshipEdges));
                break;
            default:
                return null;
        }

        if (resolved)
        {
            MetadataTypeDefinitionNameReadResult exact =
                parent.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        MetadataTypeDefinitionNameReader.Read(
                            _reader,
                            (TypeDefinitionHandle)parent,
                            beforeMaterialize: amount => Charge(
                                site,
                                MetadataOperationDimension
                                    .StructuredNodes,
                                amount)),
                    HandleKind.TypeReference =>
                        MetadataTypeDefinitionNameReader.Read(
                            _reader,
                            (TypeReferenceHandle)parent,
                            beforeMaterialize: amount => Charge(
                                site,
                                MetadataOperationDimension
                                    .StructuredNodes,
                                amount)),
                    _ => throw new InvalidOperationException(
                        "A marker type has an unsupported handle."),
                };
            if (exact is MetadataTypeDefinitionNameReadResult.Read read)
                return read.Name;
            if (exact is MetadataTypeDefinitionNameReadResult.Rejected
                {
                    Failure.RelationshipKind:
                        RelationshipTraversalRejectionKind.NodeBudget
                            or RelationshipTraversalRejectionKind.NameBudget,
                } rejected)
            {
                throw Refuse(
                    site,
                    MetadataMethodDeclarationFailureReason
                        .BudgetExceeded,
                    rejected.Failure.Detail);
            }
            return null;
        }

        if (rejection?.Kind is
            RelationshipTraversalRejectionKind.NodeBudget
                or RelationshipTraversalRejectionKind.NameBudget)
        {
            throw Refuse(
                site with { Handle = rejection.Subject },
                MetadataMethodDeclarationFailureReason.BudgetExceeded,
                rejection.Detail);
        }
        return null;
    }

    static MetadataDeclarationMarkerKind? ClassifyMarker(
        MetadataTypeDefinitionName name)
    {
        if (name.Segments.Length != 1)
            return null;

        string segment = name.Segments[0];
        return (name.Namespace, segment) switch
        {
            ("System.Runtime.CompilerServices",
                "IsReadOnlyAttribute") =>
                MetadataDeclarationMarkerKind.IsReadOnly,
            ("System.Runtime.CompilerServices",
                "RequiresLocationAttribute") =>
                MetadataDeclarationMarkerKind.RequiresLocation,
            ("System", "ParamArrayAttribute") =>
                MetadataDeclarationMarkerKind.ParamArray,
            ("System.Runtime.CompilerServices",
                "ParamCollectionAttribute") =>
                MetadataDeclarationMarkerKind.ParamCollection,
            ("System.Runtime.CompilerServices",
                "ScopedRefAttribute") =>
                MetadataDeclarationMarkerKind.ScopedRef,
            ("System.Diagnostics.CodeAnalysis",
                "UnscopedRefAttribute") =>
                MetadataDeclarationMarkerKind.UnscopedRef,
            ("System.Runtime.CompilerServices",
                "IsUnmanagedAttribute") =>
                MetadataDeclarationMarkerKind.IsUnmanaged,
            _ => null,
        };
    }

    void ValidateOwnerRangeOrdering()
    {
        var site = new MetadataMethodDeclarationSite(
            MetadataMethodDeclarationStage.MethodDefinitionRead,
            MetadataMethodDeclarationMechanism.HandleValidation);
        int previous = -1;
        int customAttributeRows =
            _reader.GetTableRowCount(TableIndex.CustomAttribute);
        for (int row = 1; row <= customAttributeRows; row++)
        {
            CustomAttributeHandle handle =
                MetadataTokens.CustomAttributeHandle(row);
            MetadataMethodDeclarationSite rowSite =
                site with { Handle = handle };
            Charge(
                rowSite,
                MetadataOperationDimension.RelationshipEdges);
            EntityHandle parent = Read(
                rowSite,
                () => _reader.GetCustomAttribute(handle).Parent);
            int coded;
            try
            {
                coded = CodedIndex.HasCustomAttribute(parent);
            }
            catch (ArgumentException ex)
            {
                throw Refuse(
                    rowSite,
                    MetadataMethodDeclarationFailureReason
                        .MalformedMetadata,
                    ex.Message);
            }
            if (coded < previous)
            {
                throw Refuse(
                    rowSite,
                    MetadataMethodDeclarationFailureReason
                        .MalformedMetadata,
                    "The CustomAttribute table is not ordered by parent.");
            }
            previous = coded;
        }

        previous = -1;
        int genericParameterRows =
            _reader.GetTableRowCount(TableIndex.GenericParam);
        for (int row = 1; row <= genericParameterRows; row++)
        {
            GenericParameterHandle handle =
                MetadataTokens.GenericParameterHandle(row);
            MetadataMethodDeclarationSite rowSite =
                site with { Handle = handle };
            Charge(
                rowSite,
                MetadataOperationDimension.RelationshipEdges);
            EntityHandle parent = Read(
                rowSite,
                () => _reader.GetGenericParameter(handle).Parent);
            int coded;
            try
            {
                coded = CodedIndex.TypeOrMethodDef(parent);
            }
            catch (ArgumentException ex)
            {
                throw Refuse(
                    rowSite,
                    MetadataMethodDeclarationFailureReason
                        .MalformedMetadata,
                    ex.Message);
            }
            if (coded < previous)
            {
                throw Refuse(
                    rowSite,
                    MetadataMethodDeclarationFailureReason
                        .MalformedMetadata,
                    "The GenericParam table is not ordered by owner.");
            }
            previous = coded;
        }

        int previousOwner = 0;
        int constraintRows =
            _reader.GetTableRowCount(
                TableIndex.GenericParamConstraint);
        for (int row = 1; row <= constraintRows; row++)
        {
            GenericParameterConstraintHandle handle =
                MetadataTokens.GenericParameterConstraintHandle(row);
            MetadataMethodDeclarationSite rowSite =
                site with { Handle = handle };
            Charge(
                rowSite,
                MetadataOperationDimension.RelationshipEdges);
            GenericParameterHandle owner = Read(
                rowSite,
                () => _reader.GetGenericParameterConstraint(handle)
                    .Parameter);
            int ownerRow = MetadataTokens.GetRowNumber(owner);
            if (ownerRow <= 0
                || ownerRow > genericParameterRows
                || ownerRow < previousOwner)
            {
                throw Refuse(
                    rowSite,
                    MetadataMethodDeclarationFailureReason
                        .MalformedMetadata,
                    "The GenericParamConstraint table is not ordered by a valid owner.");
            }
            previousOwner = ownerRow;
        }
    }

    string ReadName(StringHandle handle, MetadataMethodDeclarationSite site)
    {
        int bytes = Read(site, () => _reader.GetBlobReader(handle).Length);
        if (bytes > MetadataSafetyPolicy.MaxStructuralSignatureChars)
            throw Refuse(site, MetadataMethodDeclarationFailureReason.BudgetExceeded,
                "A declaration name exceeds the structural string limit.");
        Charge(site, MetadataOperationDimension.StructuredNodes, bytes);
        return Read(site, () => MetadataSafetyPolicy.ReadStructuralString(_reader, handle));
    }

    InertString Retain(string value, MetadataMethodDeclarationSite site)
    {
        Charge(site with { Mechanism = MetadataMethodDeclarationMechanism.TextRetention },
            MetadataOperationDimension.RetainedText,
            VisualEncoder.MeasureEncodedLength(TextPolicy.Field, value));
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
        return table is not null && row > 0
            && row <= _reader.GetTableRowCount(table.Value);
    }

    static bool IsOperatorCandidate(string name, bool specialName)
    {
        if (!specialName)
            return false;

        int separator = name.LastIndexOf('.');
        ReadOnlySpan<char> memberName =
            separator < 0 ? name : name.AsSpan(separator + 1);
        return memberName.StartsWith("op_", StringComparison.Ordinal);
    }

    void Charge(MetadataMethodDeclarationSite site,
        MetadataOperationDimension dimension, long amount = 1)
    {
        _token.ThrowIfCancellationRequested();
        try { _context.Charge(dimension, amount); }
        catch (MetadataOperationBudgetExceededException ex)
        {
            throw new DeclarationRejectedException(
                MetadataMethodDeclarationFailureReason.BudgetExceeded, site,
                $"The {ex.Dimension} budget cannot accept a charge of {ex.AttemptedCharge}.",
                ex.Dimension, ex.Limit, ex.AttemptedCharge);
        }
    }

    T Read<T>(MetadataMethodDeclarationSite site, Func<T> action)
    {
        _token.ThrowIfCancellationRequested();
        try { return action(); }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw Refuse(site, MetadataMethodDeclarationFailureReason.MalformedMetadata,
                ex.Message);
        }
    }

    static DeclarationRejectedException Refuse(MetadataMethodDeclarationSite site,
        MetadataMethodDeclarationFailureReason reason, string detail) =>
        new(reason, site, detail);
}

internal sealed class DeclarationRejectedException(
    MetadataMethodDeclarationFailureReason reason,
    MetadataMethodDeclarationSite site,
    string detail,
    MetadataOperationDimension? dimension = null,
    long? limit = null,
    long? attempted = null) : Exception(detail)
{
    internal MetadataMethodDeclarationFailureReason Reason { get; } = reason;
    internal MetadataMethodDeclarationSite Site { get; } = site;
    internal MetadataOperationDimension? Dimension { get; } = dimension;
    internal long? Limit { get; } = limit;
    internal long? Attempted { get; } = attempted;
}
