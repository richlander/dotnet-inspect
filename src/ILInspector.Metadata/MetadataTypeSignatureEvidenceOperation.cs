using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal sealed class MetadataTypeSignatureEvidenceOperation
{
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly MetadataTypeDefinitionAddress _request;
    readonly MetadataTypeDeclarationEvidence _declaration;
    readonly CancellationToken _token;

    internal MetadataTypeSignatureEvidenceOperation(
        MetadataReader reader,
        MetadataOperationContext context,
        MetadataTypeDefinitionAddress request,
        MetadataTypeDeclarationEvidence declaration,
        CancellationToken token)
    {
        _reader = reader ?? throw new ArgumentNullException(
            nameof(reader));
        _context = context ?? throw new ArgumentNullException(
            nameof(context));
        _request = request;
        _declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
        _token = token;
    }

    internal MetadataTypeSignatureResult Execute()
    {
        try
        {
            TypeDefinitionHandle handle = Resolve();
            MetadataTypeDeclarationSite baseSite = Site(
                MetadataTypeDeclarationStage
                    .SignatureBaseProjection,
                MetadataTypeDeclarationMechanism.RowRead,
                handle);
            TypeDefinition definition = Read(
                baseSite,
                () => _reader.GetTypeDefinition(handle));
            int typeParameterCount =
                _declaration.Signature.GenericParameters.Length;
            var shapes = new ShapeDecoder(this);

            PendingType? pendingBase =
                definition.BaseType.IsNil
                    ? null
                    : shapes.Decode(
                        definition.BaseType,
                        baseSite with
                        {
                            Mechanism =
                                MetadataTypeDeclarationMechanism
                                    .SignatureDecode,
                            Handle = definition.BaseType,
                        },
                        typeParameterCount);
            ImmutableArray<PendingGenericParameter>
                pendingParameters = ReadGenericParameters(
                    handle,
                    shapes);
            int nestedTypeCount = ReadNestedTypeCount(
                handle,
                definition);
            ShapeProjection projection = shapes.Freeze(handle);

            MetadataTypeSignatureTypeEvidence? baseType =
                pendingBase is null
                    ? null
                    : new(
                        projection.Project(pendingBase),
                        ReadDynamicFlags(
                            definition,
                            handle));
            ImmutableArray<
                MetadataTypeSignatureGenericParameterEvidence>
                parameters = ProjectParameters(
                    pendingParameters,
                    projection);

            Charge(
                Site(
                    MetadataTypeDeclarationStage.ResultRetention,
                    MetadataTypeDeclarationMechanism
                        .StructuredProjection,
                    handle),
                MetadataOperationDimension.StructuredNodes);
            _context.ObserveWork(
                MetadataOperationWorkKind.TypeSignaturePublication);
            _token.ThrowIfCancellationRequested();
            return new MetadataTypeSignatureResult.Posted(
                new(
                    _declaration,
                    baseType,
                    parameters,
                    nestedTypeCount),
                _context.Counters);
        }
        catch (SignatureRejectedException ex)
        {
            return new MetadataTypeSignatureResult.Rejected(
                ex.Failure,
                _context.Counters);
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            return new MetadataTypeSignatureResult.Rejected(
                new(
                    _request,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    MetadataTypeDeclarationStage.ResultRetention,
                    MetadataTypeDeclarationMechanism
                        .StructuredProjection,
                    "Metadata required by the complete Type Signature post could not be read.",
                    default,
                    null,
                    null,
                    null),
                _context.Counters);
        }
    }

    TypeDefinitionHandle Resolve()
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.RequestValidation,
            MetadataTypeDeclarationMechanism.AddressResolution,
            default);
        Charge(site, MetadataOperationDimension.MetadataRows);
        try
        {
            if (!_request.TryResolve(
                    _reader,
                    out TypeDefinitionHandle handle))
            {
                throw Refuse(
                    site,
                    MetadataTypeDeclarationFailureReason
                        .InvalidRequest,
                    "The TypeDef address does not resolve in the admitted image.");
            }
            return handle;
        }
        catch (SignatureRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.InvalidRequest,
                "The TypeDef address is invalid.");
        }
    }

    ImmutableArray<PendingGenericParameter>
        ReadGenericParameters(
            TypeDefinitionHandle handle,
            ShapeDecoder shapes)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage
                .SignatureConstraintProjection,
            MetadataTypeDeclarationMechanism
                .RelationshipTraversal,
            handle);
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        bool complete;
        int count;
        EntityHandle terminal;
        RelationshipTraversalRejection? rejection;
        try
        {
            complete = MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    _reader,
                    handle,
                    chain,
                    out count,
                    out terminal,
                    out rejection,
                    followed => Charge(
                        site with { Handle = followed },
                        MetadataOperationDimension.RelationshipEdges));
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason
                    .MalformedMetadata,
                "The TypeDef declaring chain is malformed.");
        }

        if (!complete || count == 0 || !terminal.IsNil)
        {
            throw Refuse(
                site with
                {
                    Handle = rejection?.Subject ?? terminal,
                },
                Map(rejection?.Kind),
                "The TypeDef declaring chain is invalid.");
        }

        var result =
            ImmutableArray.CreateBuilder<PendingGenericParameter>(
                _declaration.Signature.GenericParameters.Length);
        int declarationIndex = 0;
        int enclosingCount = 0;
        for (int chainIndex = 0;
            chainIndex < count;
            chainIndex++)
        {
            TypeDefinitionHandle owner = chain[chainIndex];
            TypeDefinition definition = Read(
                site with { Handle = owner },
                () => _reader.GetTypeDefinition(owner));
            GenericParameterHandleCollection parameters = Read(
                site with { Handle = owner },
                definition.GetGenericParameters);
            int cumulativeCount = 0;
            foreach (GenericParameterHandle parameterHandle
                in parameters)
            {
                MetadataTypeDeclarationSite parameterSite =
                    site with { Handle = parameterHandle };
                Charge(
                    parameterSite,
                    MetadataOperationDimension.RelationshipEdges);
                GenericParameter parameter = Read(
                    parameterSite,
                    () => _reader.GetGenericParameter(
                        parameterHandle));
                if (parameter.Parent != owner
                    || parameter.Index != cumulativeCount)
                {
                    throw Refuse(
                        parameterSite,
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "Type generic parameters have invalid ownership or ordering.");
                }

                if (cumulativeCount >= enclosingCount)
                {
                    if ((uint)declarationIndex
                        >= (uint)_declaration.Signature
                            .GenericParameters.Length)
                    {
                        throw Refuse(
                            parameterSite,
                            MetadataTypeDeclarationFailureReason
                                .MalformedMetadata,
                            "The declaration and Signature generic parameters differ.");
                    }
                    MetadataTypeGenericParameterDeclarationEvidence
                        declared = _declaration.Signature
                            .GenericParameters[declarationIndex++];
                    if (declared.DefinitionSegmentIndex
                            != chainIndex
                        || declared.MetadataIndex
                            != cumulativeCount)
                    {
                        throw Refuse(
                            parameterSite,
                            MetadataTypeDeclarationFailureReason
                                .MalformedMetadata,
                            "The declaration and Signature generic parameter order differs.");
                    }

                    GenericParameterConstraintHandleCollection
                        constraints = Read(
                            parameterSite,
                            parameter.GetConstraints);
                    var constraintShapes =
                        ImmutableArray.CreateBuilder<
                            PendingConstraint>(
                            constraints.Count);
                    foreach (
                        GenericParameterConstraintHandle
                            constraintHandle in constraints)
                    {
                        MetadataTypeDeclarationSite
                            constraintSite =
                                parameterSite with
                                {
                                    Handle = constraintHandle,
                                };
                        Charge(
                            constraintSite,
                            MetadataOperationDimension
                                .RelationshipEdges);
                        GenericParameterConstraint constraint =
                            Read(
                                constraintSite,
                                () => _reader
                                    .GetGenericParameterConstraint(
                                        constraintHandle));
                        if (constraint.Parameter != parameterHandle
                            || !IsValidTypeEntity(
                                constraint.Type))
                        {
                            throw Refuse(
                                constraintSite,
                                MetadataTypeDeclarationFailureReason
                                    .MalformedMetadata,
                                "A GenericParamConstraint row is invalid.");
                        }
                        constraintShapes.Add(
                            new(
                                shapes.Decode(
                                    constraint.Type,
                                    constraintSite with
                                    {
                                        Mechanism =
                                            MetadataTypeDeclarationMechanism
                                                .SignatureDecode,
                                    },
                                    parameters.Count),
                                ReadConstraintNullable(
                                    constraint,
                                    chain[..(chainIndex + 1)],
                                    constraintSite)));
                    }
                    if (constraintShapes.Count
                        != declared.ConstraintCount)
                    {
                        throw Refuse(
                            parameterSite,
                            MetadataTypeDeclarationFailureReason
                                .MalformedMetadata,
                            "The declaration and Signature constraint counts differ.");
                    }
                    result.Add(
                        new(
                            declared,
                            constraintShapes.MoveToImmutable()));
                }
                cumulativeCount++;
            }

            if (cumulativeCount < enclosingCount)
            {
                throw Refuse(
                    site with { Handle = owner },
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A nested Type has fewer generic parameters than its declaring Type.");
            }
            enclosingCount = cumulativeCount;
        }

        if (declarationIndex
            != _declaration.Signature.GenericParameters.Length)
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The declaration and Signature generic parameter populations differ.");
        }
        return result.MoveToImmutable();
    }

    int ReadNestedTypeCount(
        TypeDefinitionHandle handle,
        TypeDefinition definition)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage
                .SignatureNestingProjection,
            MetadataTypeDeclarationMechanism
                .RelationshipTraversal,
            handle);
        ImmutableArray<TypeDefinitionHandle> nested = Read(
            site,
            definition.GetNestedTypes);
        int count = 0;
        foreach (TypeDefinitionHandle child in nested)
        {
            Charge(
                site with { Handle = child },
                MetadataOperationDimension.RelationshipEdges);
            TypeDefinition childDefinition = Read(
                site with { Handle = child },
                () => _reader.GetTypeDefinition(child));
            TypeDefinitionHandle declaring = Read(
                site with { Handle = child },
                childDefinition.GetDeclaringType);
            if (declaring != handle)
            {
                throw Refuse(
                    site with { Handle = child },
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "An immediate nested Type has a different declaring Type.");
            }
            count++;
        }
        return count;
    }

    ImmutableArray<byte> ReadDynamicFlags(
        TypeDefinition definition,
        TypeDefinitionHandle handle)
    {
        if (definition.BaseType.Kind
            != HandleKind.TypeSpecification)
        {
            return [];
        }

        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.SignatureBaseProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        byte[]? flags = Read(
            site,
            () => DynamicReader.GetDynamicFlags(
                _reader,
                definition.GetCustomAttributes(),
                amount => Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes,
                    amount)));
        return flags is null ? [] : [.. flags];
    }

    byte? ReadConstraintNullable(
        GenericParameterConstraint constraint,
        ReadOnlySpan<TypeDefinitionHandle> ownerChain,
        MetadataTypeDeclarationSite site)
    {
        byte[]? bytes = Read(
            site,
            () => NullabilityReader.GetNullableBytes(
                _reader,
                constraint.GetCustomAttributes(),
                amount => Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes,
                    amount)));
        if (bytes is { Length: > 0 })
            return bytes[0];

        for (int index = ownerChain.Length - 1;
            index >= 0;
            index--)
        {
            TypeDefinitionHandle ownerHandle = ownerChain[index];
            TypeDefinition owner = Read(
                site with { Handle = ownerHandle },
                () => _reader.GetTypeDefinition(ownerHandle));
            byte? context = Read(
                site with { Handle = ownerHandle },
                () => NullabilityReader.GetNullableContext(
                    _reader,
                    owner.GetCustomAttributes(),
                    amount => Charge(
                        site,
                        MetadataOperationDimension
                            .StructuredNodes,
                        amount)));
            if (context is not null)
                return context.Value == 0 ? null : context.Value;
        }
        return null;
    }

    static ImmutableArray<
        MetadataTypeSignatureGenericParameterEvidence>
        ProjectParameters(
            ImmutableArray<PendingGenericParameter> pending,
            ShapeProjection projection)
    {
        var result =
            ImmutableArray.CreateBuilder<
                MetadataTypeSignatureGenericParameterEvidence>(
                pending.Length);
        foreach (PendingGenericParameter parameter in pending)
        {
            var constraints =
                ImmutableArray.CreateBuilder<
                    MetadataTypeSignatureConstraintEvidence>(
                    parameter.Constraints.Length);
            foreach (PendingConstraint constraint
                in parameter.Constraints)
            {
                constraints.Add(
                    new(
                        projection.Project(constraint.Type),
                        constraint.NullableFlag));
            }
            result.Add(
                new(
                    parameter.Declaration,
                    constraints.MoveToImmutable()));
        }
        return result.MoveToImmutable();
    }

    internal int AddName(
        StringHandle handle,
        MetadataTypeDeclarationSite site,
        Dictionary<StringHandle, int> nameSlots,
        List<BlobReader> names)
    {
        if (nameSlots.TryGetValue(handle, out int slot))
            return slot;

        BlobReader name = Read(
            site,
            () => _reader.GetBlobReader(handle));
        if (name.Length
            > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
                "A Signature Type name exceeds the structural string limit.");
        }
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            Math.Max(name.Length, 1));
        slot = names.Count;
        names.Add(name);
        nameSlots.Add(handle, slot);
        return slot;
    }

    internal void RetainNames(
        MetadataTypeDeclarationSite site,
        ReadOnlySpan<int> containedLengths)
    {
        foreach (int length in containedLengths)
        {
            Charge(
                site,
                MetadataOperationDimension.RetainedText,
                length);
        }
    }

    internal T Read<T>(
        MetadataTypeDeclarationSite site,
        Func<T> read)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            return read();
        }
        catch (SignatureRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason
                    .MalformedMetadata,
                "Metadata required by the complete Type Signature post is malformed.");
        }
    }

    internal void Charge(
        MetadataTypeDeclarationSite site,
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
            throw new SignatureRejectedException(
                new(
                    _request,
                    MetadataTypeDeclarationFailureReason
                        .BudgetExceeded,
                    site.Stage,
                    site.Mechanism,
                    "The complete Type Signature post exceeded its operation budget.",
                    site.Handle,
                    ex.Dimension,
                    ex.Limit,
                    ex.AttemptedCharge));
        }
    }

    internal SignatureRejectedException Refuse(
        MetadataTypeDeclarationSite site,
        MetadataTypeDeclarationFailureReason reason,
        string detail) =>
        new(new(
            _request,
            reason,
            site.Stage,
            site.Mechanism,
            detail,
            site.Handle,
            null,
            null,
            null));

    static MetadataTypeDeclarationSite Site(
        MetadataTypeDeclarationStage stage,
        MetadataTypeDeclarationMechanism mechanism,
        EntityHandle handle) =>
        new(stage, mechanism, handle);

    static MetadataTypeDeclarationFailureReason Map(
        RelationshipTraversalRejectionKind? kind) =>
        kind switch
        {
            RelationshipTraversalRejectionKind.Cycle =>
                MetadataTypeDeclarationFailureReason.Cycle,
            RelationshipTraversalRejectionKind.NodeBudget
                or RelationshipTraversalRejectionKind.NameBudget =>
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
            _ => MetadataTypeDeclarationFailureReason
                .MalformedMetadata,
        };

    static bool IsValidTypeEntity(EntityHandle handle) =>
        !handle.IsNil
        && handle.Kind
            is HandleKind.TypeDefinition
                or HandleKind.TypeReference
                or HandleKind.TypeSpecification;

    static bool IsMetadataException(Exception ex) =>
        ex is BadImageFormatException
            or ArgumentException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException
            or InvalidCastException
            or OverflowException;

    internal readonly record struct MetadataTypeDeclarationSite(
        MetadataTypeDeclarationStage Stage,
        MetadataTypeDeclarationMechanism Mechanism,
        EntityHandle Handle);

    internal sealed class SignatureRejectedException(
        MetadataTypeDeclarationFailure failure)
        : Exception(failure.Detail)
    {
        internal MetadataTypeDeclarationFailure Failure { get; } =
            failure;
    }

    readonly record struct PendingConstraint(
        PendingType Type,
        byte? NullableFlag);

    readonly record struct PendingGenericParameter(
        MetadataTypeGenericParameterDeclarationEvidence
            Declaration,
        ImmutableArray<PendingConstraint> Constraints);

    internal readonly record struct GenericContext(
        int TypeParameterCount);

    internal abstract record PendingType
    {
        internal sealed record Primitive(PrimitiveTypeCode Code)
            : PendingType;

        internal sealed record Named(
            int NamespaceSlot,
            ImmutableArray<int> SegmentSlots,
            ImmutableArray<int>
                IntroducedGenericParameterCounts,
            MetadataTypeSignatureNamedKind Kind)
            : PendingType;

        internal sealed record GenericInstance(
            Named Definition,
            ImmutableArray<PendingType> Arguments)
            : PendingType;

        internal sealed record TypeParameter(int Index)
            : PendingType;

        internal sealed record Array(
            PendingType Element,
            int Rank,
            ImmutableArray<int> Sizes,
            ImmutableArray<int> LowerBounds)
            : PendingType;

        internal sealed record SzArray(PendingType Element)
            : PendingType;

        internal sealed record Pointer(PendingType Element)
            : PendingType;

        internal sealed record ByReference(PendingType Element)
            : PendingType;

        internal sealed record FunctionPointer(
            SignatureHeader Header,
            int GenericParameterCount,
            PendingType ReturnType,
            ImmutableArray<PendingType> ParameterTypes,
            int RequiredParameterCount)
            : PendingType;

        internal sealed record Modified(
            PendingType Modifier,
            PendingType UnmodifiedType,
            bool IsRequired)
            : PendingType;

        internal sealed record Pinned(PendingType Element)
            : PendingType;

        internal sealed record Rejected : PendingType;
    }

    internal sealed class ShapeDecoder :
        ISignatureTypeProvider<PendingType, GenericContext>
    {
        readonly MetadataTypeSignatureEvidenceOperation _owner;
        readonly Dictionary<StringHandle, int> _nameSlots = [];
        readonly List<BlobReader> _names = [];
        MetadataTypeDeclarationSite _site;

        internal ShapeDecoder(
            MetadataTypeSignatureEvidenceOperation owner)
        {
            _owner = owner;
            _site = Site(
                MetadataTypeDeclarationStage
                    .SignatureBaseProjection,
                MetadataTypeDeclarationMechanism.SignatureDecode,
                default);
        }

        internal PendingType Decode(
            EntityHandle handle,
            MetadataTypeDeclarationSite site,
            int typeParameterCount)
        {
            MetadataTypeDeclarationSite prior = _site;
            _site = site;
            try
            {
                ChargeRelationship(handle);
                return handle.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        GetTypeFromDefinition(
                            _owner._reader,
                            (TypeDefinitionHandle)handle,
                            rawTypeKind: 0),
                    HandleKind.TypeReference =>
                        GetTypeFromReference(
                            _owner._reader,
                            (TypeReferenceHandle)handle,
                            rawTypeKind: 0),
                    HandleKind.TypeSpecification =>
                        DecodeTypeSpecification(
                            (TypeSpecificationHandle)handle,
                            new(typeParameterCount)),
                    _ => throw _owner.Refuse(
                        site,
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "A Signature Type handle has an invalid kind."),
                };
            }
            catch (SignatureRejectedException)
            {
                throw;
            }
            catch (Exception ex) when (IsMetadataException(ex))
            {
                throw _owner.Refuse(
                    site,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A Signature Type shape is malformed.");
            }
            finally
            {
                _site = prior;
            }
        }

        internal ShapeProjection Freeze(
            TypeDefinitionHandle handle)
        {
            Span<int> lengths = _names.Count <= 32
                ? stackalloc int[_names.Count]
                : new int[_names.Count];
            MetadataUtf8Name[] detached =
                MetadataUtf8Name.DetachShared(_names, lengths);
            _owner.RetainNames(
                Site(
                    MetadataTypeDeclarationStage.ResultRetention,
                    MetadataTypeDeclarationMechanism.TextRetention,
                    handle),
                lengths);
            return new(detached);
        }

        public PendingType GetPrimitiveType(
            PrimitiveTypeCode typeCode)
        {
            Node();
            return new PendingType.Primitive(typeCode);
        }

        public PendingType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            Node(handle);
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeDefinitionDeclaringChain(
                        reader,
                        handle,
                        chain,
                        out int count,
                        out EntityHandle terminal,
                        out RelationshipTraversalRejection?
                            rejection,
                        followed => _owner.Charge(
                            _site with { Handle = followed },
                            MetadataOperationDimension
                                .RelationshipEdges))
                || count == 0
                || !terminal.IsNil)
            {
                throw _owner.Refuse(
                    _site with
                    {
                        Handle = rejection?.Subject ?? terminal,
                    },
                    Map(rejection?.Kind),
                    "A Signature TypeDef name chain is invalid.");
            }

            TypeDefinitionHandle rootHandle = chain[0];
            TypeDefinition root = _owner.Read(
                _site with { Handle = rootHandle },
                () => reader.GetTypeDefinition(rootHandle));
            BlobReader namespaceName = _owner.Read(
                _site with { Handle = rootHandle },
                () => reader.GetBlobReader(root.Namespace));
            int namespaceSlot = Name(root.Namespace);
            int nameCharacters = NameCharacters(
                namespaceName);
            var segments = ImmutableArray.CreateBuilder<int>(
                count);
            var nameArities =
                ImmutableArray.CreateBuilder<NameArity>(count);
            for (int i = 0; i < count; i++)
            {
                TypeDefinitionHandle definitionHandle = chain[i];
                TypeDefinition definition = _owner.Read(
                    _site with { Handle = definitionHandle },
                    () => reader.GetTypeDefinition(
                        definitionHandle));
                BlobReader name = _owner.Read(
                    _site with { Handle = definitionHandle },
                    () => reader.GetBlobReader(definition.Name));
                segments.Add(Name(definition.Name));
                nameCharacters = AddNameCharacters(
                    nameCharacters,
                    name);
                nameArities.Add(ReadArity(name));
            }
            ValidateNameCharacters(
                nameCharacters,
                handle);
            List<int> introduced = _owner.Read(
                _site with { Handle = handle },
                () => MetadataDeclarationQuery
                    .GetIntroducedTypeParameterCounts(
                        reader,
                        handle,
                        followed => _owner.Charge(
                            _site with { Handle = followed },
                            MetadataOperationDimension
                                .RelationshipEdges)));
            for (int i = 0; i < introduced.Count; i++)
            {
                NameArity nameArity = nameArities[i];
                if (nameArity.IsCanonical
                    && nameArity.Arity != introduced[i])
                {
                    throw _owner.Refuse(
                        _site with { Handle = chain[i] },
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "A Signature TypeDef name has invalid generic arity.");
                }
            }
            return new PendingType.Named(
                namespaceSlot,
                segments.MoveToImmutable(),
                introduced.ToImmutableArray(),
                NamedKind(rawTypeKind));
        }

        public PendingType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Node(handle);
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        chain,
                        out int count,
                        out EntityHandle terminal,
                        out RelationshipTraversalRejection?
                            rejection,
                        followed => _owner.Charge(
                            _site with { Handle = followed },
                            MetadataOperationDimension
                                .RelationshipEdges))
                || count == 0
                || !IsValidResolutionScope(terminal))
            {
                throw _owner.Refuse(
                    _site with
                    {
                        Handle = rejection?.Subject ?? terminal,
                    },
                    Map(rejection?.Kind),
                    "A Signature TypeRef name or scope chain is invalid.");
            }

            TypeReferenceHandle rootHandle = chain[0];
            TypeReference root = _owner.Read(
                _site with { Handle = rootHandle },
                () => reader.GetTypeReference(rootHandle));
            BlobReader namespaceName = _owner.Read(
                _site with { Handle = rootHandle },
                () => reader.GetBlobReader(root.Namespace));
            int namespaceSlot = Name(root.Namespace);
            int nameCharacters = NameCharacters(
                namespaceName);
            var segments = ImmutableArray.CreateBuilder<int>(
                count);
            var introduced = ImmutableArray.CreateBuilder<int>(
                count);
            for (int i = 0; i < count; i++)
            {
                TypeReferenceHandle referenceHandle = chain[i];
                TypeReference reference = _owner.Read(
                    _site with { Handle = referenceHandle },
                    () => reader.GetTypeReference(
                        referenceHandle));
                BlobReader name = _owner.Read(
                    _site with { Handle = referenceHandle },
                    () => reader.GetBlobReader(reference.Name));
                segments.Add(Name(reference.Name));
                nameCharacters = AddNameCharacters(
                    nameCharacters,
                    name);
                introduced.Add(ReadArity(name).Arity);
            }
            ValidateNameCharacters(
                nameCharacters,
                handle);
            return new PendingType.Named(
                namespaceSlot,
                segments.MoveToImmutable(),
                introduced.MoveToImmutable(),
                NamedKind(rawTypeKind));
        }

        public PendingType GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            Node(handle);
            return DecodeTypeSpecification(
                handle,
                genericContext);
        }

        public PendingType GetArrayType(
            PendingType elementType,
            ArrayShape shape)
        {
            Node();
            return new PendingType.Array(
                elementType,
                shape.Rank,
                shape.Sizes,
                shape.LowerBounds);
        }

        public PendingType GetSZArrayType(
            PendingType elementType)
        {
            Node();
            return new PendingType.SzArray(elementType);
        }

        public PendingType GetByReferenceType(
            PendingType elementType)
        {
            Node();
            return new PendingType.ByReference(elementType);
        }

        public PendingType GetPointerType(
            PendingType elementType)
        {
            Node();
            return new PendingType.Pointer(elementType);
        }

        public PendingType GetPinnedType(
            PendingType elementType)
        {
            Node();
            return new PendingType.Pinned(elementType);
        }

        public PendingType GetGenericTypeParameter(
            GenericContext genericContext,
            int index)
        {
            Node();
            if ((uint)index
                >= (uint)genericContext.TypeParameterCount)
            {
                throw _owner.Refuse(
                    _site,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A Signature Type generic parameter index is out of range.");
            }
            return new PendingType.TypeParameter(index);
        }

        public PendingType GetGenericMethodParameter(
            GenericContext genericContext,
            int index)
        {
            Node();
            throw _owner.Refuse(
                _site,
                MetadataTypeDeclarationFailureReason
                    .MalformedMetadata,
                "A Type declaration Signature cannot reference a method generic parameter.");
        }

        public PendingType GetGenericInstantiation(
            PendingType genericType,
            ImmutableArray<PendingType> typeArguments)
        {
            Node();
            if (genericType is not PendingType.Named named)
            {
                throw _owner.Refuse(
                    _site,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A generic instance definition is not a named Type.");
            }
            long arity = 0;
            foreach (int count
                in named.IntroducedGenericParameterCounts)
            {
                arity += count;
            }
            if (arity != typeArguments.Length)
            {
                throw _owner.Refuse(
                    _site,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A generic instance argument count does not match its Type name.");
            }
            return new PendingType.GenericInstance(
                named,
                typeArguments);
        }

        public PendingType GetFunctionPointerType(
            MethodSignature<PendingType> signature)
        {
            Node();
            return new PendingType.FunctionPointer(
                signature.Header,
                signature.GenericParameterCount,
                signature.ReturnType,
                signature.ParameterTypes,
                signature.RequiredParameterCount);
        }

        public PendingType GetModifiedType(
            PendingType modifier,
            PendingType unmodifiedType,
            bool isRequired)
        {
            Node();
            return new PendingType.Modified(
                modifier,
                unmodifiedType,
                isRequired);
        }

        PendingType DecodeTypeSpecification(
            TypeSpecificationHandle handle,
            GenericContext genericContext)
        {
            TypeSpecification specification = _owner.Read(
                _site with { Handle = handle },
                () => _owner._reader.GetTypeSpecification(handle));
            int bytes = _owner.Read(
                _site with { Handle = handle },
                () => _owner._reader
                    .GetBlobReader(specification.Signature)
                    .Length);
            _owner.Charge(
                _site with { Handle = handle },
                MetadataOperationDimension.SignatureBytes,
                bytes);
            var rejected = new PendingType.Rejected();
            PendingType decoded = GuardedProviderDecode.TypeSpec(
                _owner._reader,
                handle,
                this,
                genericContext,
                rejected);
            if (ReferenceEquals(decoded, rejected))
            {
                throw _owner.Refuse(
                    _site with { Handle = handle },
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A Signature TypeSpec could not be decoded safely.");
            }
            return decoded;
        }

        int Name(StringHandle handle) =>
            _owner.AddName(
                handle,
                _site,
                _nameSlots,
                _names);

        void ChargeRelationship(EntityHandle handle) =>
            _owner.Charge(
                _site with { Handle = handle },
                MetadataOperationDimension.RelationshipEdges);

        void Node(EntityHandle handle = default) =>
            _owner.Charge(
                _site with
                {
                    Handle = handle.IsNil
                        ? _site.Handle
                        : handle,
                },
                MetadataOperationDimension.StructuredNodes);

        static NameArity ReadArity(BlobReader reader)
        {
            ReadOnlySpan<byte> name =
                MetadataUtf8Name.ReadExactBytes(reader);
            int tick = name.LastIndexOf((byte)'`');
            if (tick < 1 || tick == name.Length - 1)
                return default;
            ReadOnlySpan<byte> digits = name[(tick + 1)..];
            if (digits.Length > 5 || digits[0] == (byte)'0')
                return default;
            int value = 0;
            foreach (byte digit in digits)
            {
                if (digit is < (byte)'0' or > (byte)'9')
                    return default;
                value = (value * 10) + digit - (byte)'0';
            }
            return value <= MetadataNameArity.MaxArity
                ? new(true, value)
                : default;
        }

        static MetadataTypeSignatureNamedKind NamedKind(
            byte rawTypeKind) =>
            rawTypeKind switch
            {
                0x11 =>
                    MetadataTypeSignatureNamedKind.ValueType,
                0x12 =>
                    MetadataTypeSignatureNamedKind.Class,
                _ =>
                    MetadataTypeSignatureNamedKind.Unspecified,
            };

        static int NameCharacters(BlobReader name) =>
            Encoding.UTF8.GetCharCount(
                MetadataUtf8Name.ReadExactBytes(name));

        int AddNameCharacters(
            int current,
            BlobReader name)
        {
            int added = NameCharacters(name);
            if (current
                > MetadataSafetyPolicy.MaxTypeNameCharacters
                    - added
                    - 1)
            {
                throw _owner.Refuse(
                    _site,
                    MetadataTypeDeclarationFailureReason
                        .BudgetExceeded,
                    "A Signature Type name exceeds the structural name limit.");
            }
            return current + added + 1;
        }

        void ValidateNameCharacters(
            int characters,
            EntityHandle handle)
        {
            if (characters
                > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                throw _owner.Refuse(
                    _site with { Handle = handle },
                    MetadataTypeDeclarationFailureReason
                        .BudgetExceeded,
                    "A Signature Type name exceeds the structural name limit.");
            }
        }

        bool IsValidResolutionScope(EntityHandle scope)
        {
            int row = MetadataTokens.GetRowNumber(scope);
            return scope.Kind switch
            {
                HandleKind.ModuleDefinition => row == 1,
                HandleKind.ModuleReference =>
                    row > 0
                    && row <= _owner._reader.GetTableRowCount(
                        TableIndex.ModuleRef),
                HandleKind.AssemblyReference =>
                    row > 0
                    && row <= _owner._reader.GetTableRowCount(
                        TableIndex.AssemblyRef),
                _ => false,
            };
        }
    }

    internal sealed class ShapeProjection(
        MetadataUtf8Name[] names)
    {
        internal MetadataTypeSignatureType Project(
            PendingType type) =>
            type switch
            {
                PendingType.Primitive primitive =>
                    new MetadataTypeSignatureType.Primitive(
                        primitive.Code),
                PendingType.Named named =>
                    ProjectNamed(named),
                PendingType.GenericInstance generic =>
                    new MetadataTypeSignatureType.GenericInstance(
                        ProjectNamed(generic.Definition),
                        Project(generic.Arguments)),
                PendingType.TypeParameter parameter =>
                    new MetadataTypeSignatureType.TypeParameter(
                        parameter.Index),
                PendingType.Array array =>
                    new MetadataTypeSignatureType.Array(
                        Project(array.Element),
                        array.Rank,
                        array.Sizes,
                        array.LowerBounds),
                PendingType.SzArray array =>
                    new MetadataTypeSignatureType.SzArray(
                        Project(array.Element)),
                PendingType.Pointer pointer =>
                    new MetadataTypeSignatureType.Pointer(
                        Project(pointer.Element)),
                PendingType.ByReference byReference =>
                    new MetadataTypeSignatureType.ByReference(
                        Project(byReference.Element)),
                PendingType.FunctionPointer function =>
                    new MetadataTypeSignatureType.FunctionPointer(
                        function.Header,
                        function.GenericParameterCount,
                        Project(function.ReturnType),
                        Project(function.ParameterTypes),
                        function.RequiredParameterCount),
                PendingType.Modified modified =>
                    new MetadataTypeSignatureType.Modified(
                        Project(modified.Modifier),
                        Project(modified.UnmodifiedType),
                        modified.IsRequired),
                PendingType.Pinned pinned =>
                    new MetadataTypeSignatureType.Pinned(
                        Project(pinned.Element)),
                _ => throw new InvalidOperationException(
                    "A rejected pending Type cannot be projected."),
            };

        MetadataTypeSignatureType.Named ProjectNamed(
            PendingType.Named named)
        {
            var segments =
                ImmutableArray.CreateBuilder<MetadataUtf8Name>(
                    named.SegmentSlots.Length);
            foreach (int slot in named.SegmentSlots)
                segments.Add(names[slot]);
            return new(
                new(
                    names[named.NamespaceSlot],
                    segments.MoveToImmutable(),
                    named.IntroducedGenericParameterCounts),
                named.Kind);
        }

        ImmutableArray<MetadataTypeSignatureType> Project(
            ImmutableArray<PendingType> types)
        {
            var result =
                ImmutableArray.CreateBuilder<
                    MetadataTypeSignatureType>(types.Length);
            foreach (PendingType type in types)
                result.Add(Project(type));
            return result.MoveToImmutable();
        }
    }

    readonly record struct NameArity(
        bool IsCanonical,
        int Arity);
}
