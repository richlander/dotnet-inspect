using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;
using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

public enum MetadataTypeDeclarationCategory
{
    Class,
    Interface,
    Struct,
    Enum,
    Delegate,
}

public enum MetadataTypeDeclarationFailureReason
{
    InvalidRequest,
    MalformedMetadata,
    Cycle,
    BudgetExceeded,
    UnsupportedShape,
}

public enum MetadataTypeDeclarationStage
{
    RequestValidation,
    TypeDefinitionRead,
    IdentityProjection,
    CategoryClassification,
    NestingValidation,
    ResultRetention,
}

public enum MetadataTypeDeclarationMechanism
{
    ImageAdmission,
    AddressResolution,
    RowRead,
    HandleValidation,
    RelationshipTraversal,
    CoreRootAuthentication,
    StructuredProjection,
    TextRetention,
}

public sealed record MetadataTypeDeclarationFailure(
    MetadataTypeDefinitionAddress Request,
    MetadataTypeDeclarationFailureReason Reason,
    MetadataTypeDeclarationStage Stage,
    MetadataTypeDeclarationMechanism Mechanism,
    string Detail,
    EntityHandle RelevantHandle,
    MetadataOperationDimension? BudgetDimension,
    long? BudgetLimit,
    long? BudgetAttempted);

public sealed record MetadataTypeDeclarationEvidence(
    MetadataTypeDefinitionAddress Type,
    MetadataNamedTypeIdentity DefinitionIdentity,
    MetadataTypeIdentity OpenSelfIdentity,
    MetadataTypeIdentity.Primitive? PrimitiveAlias,
    TypeAttributes Attributes,
    MetadataTypeDeclarationCategory Category,
    bool IsByRefLike,
    bool DefinesCoreLibraryRoot,
    MetadataTypeDefinitionAddress? DeclaringType);

public abstract record MetadataTypeDeclarationResult
{
    private protected MetadataTypeDeclarationResult(
        MetadataOperationCounters counters) =>
        Counters = counters;

    public MetadataOperationCounters Counters { get; }

    public sealed record Posted : MetadataTypeDeclarationResult
    {
        internal Posted(
            MetadataTypeDeclarationEvidence evidence,
            MetadataOperationCounters counters)
            : base(counters) =>
            Evidence = evidence;

        public MetadataTypeDeclarationEvidence Evidence { get; }
    }

    public sealed record Rejected : MetadataTypeDeclarationResult
    {
        internal Rejected(
            MetadataTypeDeclarationFailure failure,
            MetadataOperationCounters counters)
            : base(counters) =>
            Failure = failure;

        public MetadataTypeDeclarationFailure Failure { get; }
    }
}

internal sealed class MetadataTypeDeclarationEvidenceOperation
{
    readonly PEReader _peReader;
    readonly MetadataReader _reader;
    readonly MetadataOperationContext _context;
    readonly Func<
        Action,
        Action<TypeDefinitionHandle>,
        Action,
        Action<int>,
        MetadataTypeDefinitionIndex> _getIndex;
    readonly MetadataTypeDefinitionAddress _request;
    readonly CancellationToken _token;

    internal MetadataTypeDeclarationEvidenceOperation(
        PEReader peReader,
        MetadataReader reader,
        MetadataOperationContext context,
        Func<
            Action,
            Action<TypeDefinitionHandle>,
            Action,
            Action<int>,
            MetadataTypeDefinitionIndex> getIndex,
        MetadataTypeDefinitionAddress request,
        CancellationToken token)
    {
        _peReader = peReader
            ?? throw new ArgumentNullException(nameof(peReader));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _getIndex = getIndex
            ?? throw new ArgumentNullException(nameof(getIndex));
        _request = request;
        _token = token;
    }

    internal MetadataTypeDeclarationResult Execute()
    {
        try
        {
            _token.ThrowIfCancellationRequested();
            TypeDefinitionHandle handle = ResolveRequest();
            TypeDefinition definition = ReadDefinition(handle);
            TypeAttributes attributes = definition.Attributes;

            MetadataTypeDefinitionIndex index = GetIndex(handle);
            MetadataTypeDefinitionName selectedName =
                ReadName(handle);
            if (!index.TryGetDefinition(
                    selectedName,
                    out TypeDefinitionHandle unique,
                    out bool ambiguous)
                || ambiguous
                || unique != handle)
            {
                throw Refuse(
                    Site(
                        MetadataTypeDeclarationStage.IdentityProjection,
                        MetadataTypeDeclarationMechanism
                            .StructuredProjection,
                        handle),
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "The requested TypeDef does not have a unique structured name.");
            }

            ImmutableArray<int> introducedCounts =
                ReadIntroducedGenericParameterCounts(handle);
            int totalGenericParameterCount = introducedCounts.Sum();
            int[] declaringRows =
                ReadDeclaringRows(handle);

            bool definesCoreLibraryRoot =
                ReadCoreRootAuthentication(
                    handle,
                    declaringRows);
            MetadataTypeDeclarationCategory category =
                Classify(
                    handle,
                    selectedName,
                    definition.BaseType,
                    attributes,
                    definesCoreLibraryRoot,
                    index);
            bool isByRefLike =
                category == MetadataTypeDeclarationCategory.Struct
                && ReadIsByRefLike(definition, handle);
            bool isValueType =
                category is MetadataTypeDeclarationCategory.Struct
                    or MetadataTypeDeclarationCategory.Enum;

            MetadataTypeIdentity namedIdentity =
                ProjectDefinition(handle, isValueType);
            var named = namedIdentity
                as MetadataTypeIdentity.Named
                ?? throw Refuse(
                    Site(
                        MetadataTypeDeclarationStage.IdentityProjection,
                        MetadataTypeDeclarationMechanism
                            .StructuredProjection,
                        handle),
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "The TypeDef identity did not project as a named type.");
            if (!MetadataIdentitySequence.Equal(
                    named.Definition.IntroducedGenericParameterCounts,
                    introducedCounts))
            {
                throw Refuse(
                    Site(
                        MetadataTypeDeclarationStage.IdentityProjection,
                        MetadataTypeDeclarationMechanism
                            .StructuredProjection,
                        handle),
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "The TypeDef identity disagrees with generic ownership.");
            }

            MetadataTypeIdentity openSelf =
                CreateOpenSelf(
                    named.Definition,
                    isValueType,
                    totalGenericParameterCount,
                    handle);
            MetadataTypeIdentity.Primitive? primitiveAlias =
                CreatePrimitiveAlias(
                    selectedName,
                    totalGenericParameterCount,
                    definesCoreLibraryRoot,
                    handle);
            MetadataTypeDefinitionAddress? declaringType =
                ValidateNesting(
                    handle,
                    attributes,
                    named.Definition,
                    introducedCounts,
                    declaringRows);

            MetadataTypeDeclarationSite retentionSite = Site(
                MetadataTypeDeclarationStage.ResultRetention,
                MetadataTypeDeclarationMechanism.StructuredProjection,
                handle);
            Charge(
                retentionSite,
                MetadataOperationDimension.StructuredNodes);
            _context.ObserveWork(
                MetadataOperationWorkKind.TypeDeclarationPublication);
            _token.ThrowIfCancellationRequested();
            return new MetadataTypeDeclarationResult.Posted(
                new(
                    _request,
                    named.Definition,
                    openSelf,
                    primitiveAlias,
                    attributes,
                    category,
                    isByRefLike,
                    definesCoreLibraryRoot,
                    declaringType),
                _context.Counters);
        }
        catch (TypeDeclarationRejectedException ex)
        {
            return new MetadataTypeDeclarationResult.Rejected(
                new(
                    _request,
                    ex.Reason,
                    ex.Site.Stage,
                    ex.Site.Mechanism,
                    ex.Message,
                    ex.Site.Handle,
                    ex.Dimension,
                    ex.Limit,
                    ex.Attempted),
                _context.Counters);
        }
    }

    TypeDefinitionHandle ResolveRequest()
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.RequestValidation,
            MetadataTypeDeclarationMechanism.AddressResolution,
            default);
        try
        {
            if (!_request.TryResolve(
                    _reader,
                    out TypeDefinitionHandle handle))
            {
                throw Refuse(
                    site,
                    MetadataTypeDeclarationFailureReason.InvalidRequest,
                    "The TypeDef address does not resolve in the admitted image.");
            }

            Charge(
                site with { Handle = handle },
                MetadataOperationDimension.DeclarationCandidates);
            return handle;
        }
        catch (TypeDeclarationRejectedException)
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

    TypeDefinition ReadDefinition(TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.TypeDefinitionRead,
            MetadataTypeDeclarationMechanism.RowRead,
            handle);
        ValidateRawTypeDefinitionRow(handle, site);
        TypeDefinition definition = Read(
            site,
            () => _reader.GetTypeDefinition(handle));
        _ = Read(site, () => definition.Attributes);
        _ = Read(site, () => definition.Name);
        _ = Read(site, () => definition.Namespace);
        _ = Read(site, () => definition.BaseType);
        _ = Read(site, () => definition.GetFields().Count);
        _ = Read(site, () => definition.GetMethods().Count);
        return definition;
    }

    bool ReadIsByRefLike(
        TypeDefinition definition,
        TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.CategoryClassification,
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            handle);
        CustomAttributeHandleCollection attributes = Read(
            site,
            definition.GetCustomAttributes);
        foreach (CustomAttributeHandle attributeHandle in attributes)
        {
            _token.ThrowIfCancellationRequested();
            Charge(
                site with { Handle = attributeHandle },
                MetadataOperationDimension.RelationshipEdges);
            CustomAttribute attribute = Read(
                site with { Handle = attributeHandle },
                () => _reader.GetCustomAttribute(attributeHandle));
            string? attributeName = Read(
                site with { Handle = attribute.Constructor },
                () => AttributeReader.GetAttributeTypeName(
                    _reader,
                    attribute.Constructor,
                    beforeMaterialize: amount => Charge(
                        site,
                        MetadataOperationDimension.StructuredNodes,
                        amount)));
            if (attributeName
                == KnownAttributeNames.IsByRefLikeAttribute)
            {
                return true;
            }
        }

        return false;
    }

    void ValidateRawTypeDefinitionRow(
        TypeDefinitionHandle handle,
        MetadataTypeDeclarationSite site)
    {
        Read(
            site,
            () =>
            {
                int rowNumber =
                    MetadataTokens.GetRowNumber(handle);
                int rowCount =
                    _reader.GetTableRowCount(TableIndex.TypeDef);
                int rowSize =
                    _reader.GetTableRowSize(TableIndex.TypeDef);
                int tableOffset =
                    _reader.GetTableMetadataOffset(
                        TableIndex.TypeDef);
                int fieldIndexSize =
                    IndexSize(
                        ListTargetRowCount(
                            TableIndex.FieldPtr,
                            TableIndex.Field));
                int methodIndexSize =
                    IndexSize(
                        ListTargetRowCount(
                            TableIndex.MethodPtr,
                            TableIndex.MethodDef));
                int extendsIndexSize =
                    CodedIndexSize(
                        _reader.GetTableRowCount(
                            TableIndex.TypeDef),
                        _reader.GetTableRowCount(
                            TableIndex.TypeRef),
                        _reader.GetTableRowCount(
                            TableIndex.TypeSpec));
                int stringIndexSize =
                    (rowSize
                        - sizeof(uint)
                        - extendsIndexSize
                        - fieldIndexSize
                        - methodIndexSize)
                    / 2;
                if (stringIndexSize is not 2 and not 4
                    || rowSize
                        != sizeof(uint)
                            + (2 * stringIndexSize)
                            + extendsIndexSize
                            + fieldIndexSize
                            + methodIndexSize)
                {
                    throw new BadImageFormatException(
                        "The TypeDef row layout is invalid.");
                }

                PEMemoryBlock metadata =
                    _peReader.GetMetadata();
                int rowOffset = checked(
                    tableOffset
                    + ((rowNumber - 1) * rowSize));
                BlobReader row = metadata.GetReader(
                    rowOffset,
                    rowSize);
                uint flags = row.ReadUInt32();
                uint name = ReadIndex(
                    ref row,
                    stringIndexSize);
                uint @namespace = ReadIndex(
                    ref row,
                    stringIndexSize);
                uint extends = ReadIndex(
                    ref row,
                    extendsIndexSize);
                uint fieldList = ReadIndex(
                    ref row,
                    fieldIndexSize);
                uint methodList = ReadIndex(
                    ref row,
                    methodIndexSize);
                if (row.RemainingBytes != 0
                    || !IsValidTypeDefOrRef(extends)
                    || !IsValidListStart(
                        fieldList,
                        ListTargetRowCount(
                            TableIndex.FieldPtr,
                            TableIndex.Field))
                    || !IsValidListStart(
                        methodList,
                        ListTargetRowCount(
                            TableIndex.MethodPtr,
                            TableIndex.MethodDef)))
                {
                    throw new BadImageFormatException(
                        "The TypeDef row contains an invalid scalar.");
                }

                TypeDefinition definition =
                    _reader.GetTypeDefinition(handle);
                if (flags != (uint)definition.Attributes
                    || name
                        != (uint)MetadataTokens.GetHeapOffset(
                            definition.Name)
                    || @namespace
                        != (uint)MetadataTokens.GetHeapOffset(
                            definition.Namespace)
                    || extends != EncodeTypeDefOrRef(
                        definition.BaseType))
                {
                    throw new BadImageFormatException(
                        "The TypeDef row projection is inconsistent.");
                }

                if (rowNumber < rowCount)
                {
                    Charge(
                        site,
                        MetadataOperationDimension.RelationshipEdges);
                    BlobReader next = metadata.GetReader(
                        checked(rowOffset + rowSize),
                        rowSize);
                    next.Offset = checked(
                        sizeof(uint)
                        + (2 * stringIndexSize)
                        + extendsIndexSize);
                    uint nextFieldList = ReadIndex(
                        ref next,
                        fieldIndexSize);
                    uint nextMethodList = ReadIndex(
                        ref next,
                        methodIndexSize);
                    if (!IsValidListStart(
                            nextFieldList,
                            ListTargetRowCount(
                                TableIndex.FieldPtr,
                                TableIndex.Field))
                        || !IsValidListStart(
                            nextMethodList,
                            ListTargetRowCount(
                                TableIndex.MethodPtr,
                                TableIndex.MethodDef))
                        || nextFieldList < fieldList
                        || nextMethodList < methodList)
                    {
                        throw new BadImageFormatException(
                            "The TypeDef list ranges are invalid.");
                    }
                }
                return true;
            });
    }

    internal static int IndexSize(int rowCount) =>
        rowCount <= ushort.MaxValue ? 2 : 4;

    int ListTargetRowCount(
        TableIndex pointerTable,
        TableIndex definitionTable)
    {
        int pointers =
            _reader.GetTableRowCount(pointerTable);
        return pointers == 0
            ? _reader.GetTableRowCount(definitionTable)
            : pointers;
    }

    static int CodedIndexSize(
        int typeDefinitions,
        int typeReferences,
        int typeSpecifications) =>
        Math.Max(
            typeDefinitions,
            Math.Max(typeReferences, typeSpecifications))
            < (1 << 14)
            ? 2
            : 4;

    static uint ReadIndex(
        ref BlobReader reader,
        int size) =>
        size == 2
            ? reader.ReadUInt16()
            : reader.ReadUInt32();

    bool IsValidTypeDefOrRef(uint coded)
    {
        if (coded == 0)
            return true;
        uint row = coded >> 2;
        if (row == 0)
            return false;
        return (coded & 3) switch
        {
            0 => row
                <= _reader.GetTableRowCount(
                    TableIndex.TypeDef),
            1 => row
                <= _reader.GetTableRowCount(
                    TableIndex.TypeRef),
            2 => row
                <= _reader.GetTableRowCount(
                    TableIndex.TypeSpec),
            _ => false,
        };
    }

    static bool IsValidListStart(
        uint start,
        int rowCount) =>
        start >= 1
        && start <= (uint)rowCount + 1;

    static uint EncodeTypeDefOrRef(
        EntityHandle handle)
    {
        if (handle.IsNil)
            return 0;
        uint row =
            (uint)MetadataTokens.GetRowNumber(handle);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                row << 2,
            HandleKind.TypeReference =>
                (row << 2) | 1,
            HandleKind.TypeSpecification =>
                (row << 2) | 2,
            _ => uint.MaxValue,
        };
    }

    MetadataTypeDefinitionName ReadName(
        TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        MetadataTypeDefinitionNameReadResult result = Read(
            site,
            () => MetadataTypeDefinitionNameReader.Read(
                _reader,
                handle,
                beforeMaterialize: amount => Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes,
                    amount),
                chargeChain: amount => Charge(
                    site,
                    MetadataOperationDimension.RelationshipEdges,
                    amount),
                chargeCharacters: amount => Charge(
                    site,
                    MetadataOperationDimension.RetainedText,
                    amount)));
        return result switch
        {
            MetadataTypeDefinitionNameReadResult.Read read =>
                read.Name,
            MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                throw Refuse(
                    site,
                    rejected.Failure.RelationshipKind is
                        { } relationshipKind
                        ? Map(relationshipKind)
                        : MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                    "The TypeDef structured name is invalid."),
            _ => throw new InvalidOperationException(
                "Unknown TypeDef name result."),
        };
    }

    ImmutableArray<int> ReadIntroducedGenericParameterCounts(
        TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            handle);
        ValidateGenericParameterOwnerOrdering(site);
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
                    handle,
                    chain,
                    out consumed,
                    out terminal,
                    out rejection,
                    relationship => Charge(
                        site with { Handle = relationship },
                        MetadataOperationDimension.RelationshipEdges));
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The TypeDef declaring chain is malformed.");
        }

        if (!complete)
        {
            RelationshipTraversalRejection failure =
                rejection
                ?? throw new InvalidOperationException(
                    "An incomplete TypeDef traversal lacks a rejection.");
            throw Refuse(
                site with { Handle = failure.Subject },
                Map(failure.Kind),
                "The TypeDef declaring chain is invalid.");
        }
        if (!terminal.IsNil || consumed == 0)
        {
            throw Refuse(
                site with { Handle = terminal },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The TypeDef declaring chain has an invalid terminal.");
        }

        var introduced =
            ImmutableArray.CreateBuilder<int>(consumed);
        int enclosingCount = 0;
        for (int chainIndex = 0;
            chainIndex < consumed;
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
                    () => _reader.GetGenericParameter(parameterHandle));
                if (parameter.Parent != owner
                    || parameter.Index != cumulativeCount)
                {
                    throw Refuse(
                        parameterSite,
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "Type generic parameters have invalid ownership or ordering.");
                }
                cumulativeCount++;
            }

            if (cumulativeCount < enclosingCount)
            {
                throw Refuse(
                    site with { Handle = owner },
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "A nested type has fewer generic parameters than its declaring type.");
            }

            introduced.Add(cumulativeCount - enclosingCount);
            enclosingCount = cumulativeCount;
        }
        return introduced.MoveToImmutable();
    }

    void ValidateGenericParameterOwnerOrdering(
        MetadataTypeDeclarationSite site)
    {
        int previousOwner = -1;
        int rows =
            _reader.GetTableRowCount(TableIndex.GenericParam);
        for (int row = 1; row <= rows; row++)
        {
            GenericParameterHandle handle =
                MetadataTokens.GenericParameterHandle(row);
            MetadataTypeDeclarationSite rowSite =
                site with { Handle = handle };
            Charge(
                rowSite,
                MetadataOperationDimension.RelationshipEdges);
            EntityHandle parent = Read(
                rowSite,
                () => _reader.GetGenericParameter(handle)
                    .Parent);
            if (!IsValidGenericParameterOwner(parent))
            {
                throw Refuse(
                    rowSite,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A GenericParam row has an invalid owner.");
            }
            int owner;
            try
            {
                owner = CodedIndex.TypeOrMethodDef(parent);
            }
            catch (ArgumentException)
            {
                throw Refuse(
                    rowSite,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A GenericParam row has an invalid owner.");
            }
            if (owner < previousOwner)
            {
                throw Refuse(
                    rowSite,
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "The GenericParam table is not ordered by owner.");
            }
            previousOwner = owner;
        }
    }

    bool IsValidGenericParameterOwner(
        EntityHandle owner)
    {
        int row = MetadataTokens.GetRowNumber(owner);
        return owner.Kind switch
        {
            HandleKind.TypeDefinition =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.TypeDef),
            HandleKind.MethodDefinition =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.MethodDef),
            _ => false,
        };
    }

    bool ReadCoreRootAuthentication(
        TypeDefinitionHandle handle,
        int[] declaringRows)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.CategoryClassification,
            MetadataTypeDeclarationMechanism
                .CoreRootAuthentication,
            handle);
        try
        {
            if (_reader.GetTableRowCount(TableIndex.AssemblyRef) != 0)
                return false;

            int matches = 0;
            foreach (TypeDefinitionHandle candidate
                in _reader.TypeDefinitions)
            {
                Charge(
                    site with { Handle = candidate },
                    MetadataOperationDimension.RelationshipEdges);
                TypeDefinition definition = Read(
                    site with { Handle = candidate },
                    () => _reader.GetTypeDefinition(candidate));
                ValidateRawTypeDefinitionRow(
                    candidate,
                    site with { Handle = candidate });
                if (!Read(
                        site with { Handle = candidate },
                        () => CoreLibraryRootAuthentication
                            .IsValidTopLevelCoreLibraryRoot(
                                _reader,
                                definition)))
                {
                    continue;
                }
                if (declaringRows[
                        MetadataTokens.GetRowNumber(candidate)]
                    != 0)
                {
                    throw Refuse(
                        site with { Handle = candidate },
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "The core-library root has a declaring-type relationship.");
                }

                if (++matches > 1)
                    return false;
            }
            return matches == 1;
        }
        catch (TypeDeclarationRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "Core-library root authentication could not read the image.");
        }
    }

    MetadataTypeDeclarationCategory Classify(
        TypeDefinitionHandle handle,
        MetadataTypeDefinitionName selectedName,
        EntityHandle baseType,
        TypeAttributes attributes,
        bool definesCoreLibraryRoot,
        MetadataTypeDefinitionIndex index)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.CategoryClassification,
            MetadataTypeDeclarationMechanism.HandleValidation,
            handle);
        bool isInterface =
            (attributes & TypeAttributes.Interface) != 0;
        if (isInterface)
        {
            if (!baseType.IsNil)
            {
                throw Refuse(
                    site with { Handle = baseType },
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "An interface TypeDef has an extends relationship.");
            }
            return MetadataTypeDeclarationCategory.Interface;
        }

        if (definesCoreLibraryRoot
            && IsTopLevelSystemType(
                selectedName,
                out string selectedSimpleName)
            && selectedSimpleName
                is "ValueType"
                    or "Enum"
                    or "Delegate"
                    or "MulticastDelegate")
        {
            return MetadataTypeDeclarationCategory.Class;
        }

        if (baseType.IsNil)
            return MetadataTypeDeclarationCategory.Class;

        Charge(
            site with { Handle = baseType },
            MetadataOperationDimension.RelationshipEdges);
        return baseType.Kind switch
        {
            HandleKind.TypeDefinition =>
                ClassifyLocalBase(
                    (TypeDefinitionHandle)baseType,
                    definesCoreLibraryRoot,
                    index,
                    site),
            HandleKind.TypeReference =>
                ClassifyReferencedBase(
                    (TypeReferenceHandle)baseType,
                    definesCoreLibraryRoot,
                    index,
                    site),
            HandleKind.TypeSpecification =>
                ClassifySpecificationBase(
                    (TypeSpecificationHandle)baseType,
                    definesCoreLibraryRoot,
                    index,
                    site),
            _ => throw Refuse(
                site with { Handle = baseType },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The TypeDef extends handle has an invalid kind."),
        };
    }

    MetadataTypeDeclarationCategory ClassifyLocalBase(
        TypeDefinitionHandle baseType,
        bool definesCoreLibraryRoot,
        MetadataTypeDefinitionIndex index,
        MetadataTypeDeclarationSite site)
    {
        if (!definesCoreLibraryRoot)
            return MetadataTypeDeclarationCategory.Class;

        MetadataTypeDefinitionName name = ReadName(baseType);
        if (!IsTopLevelSystemType(name, out string simpleName))
            return MetadataTypeDeclarationCategory.Class;
        if (!index.TryGetDefinition(
                name,
                out TypeDefinitionHandle unique,
                out bool ambiguous)
            || ambiguous
            || unique != baseType)
        {
            throw Refuse(
                site with { Handle = baseType },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The local core category root is not unique.");
        }
        return CategoryFromCoreBase(simpleName);
    }

    MetadataTypeDeclarationCategory ClassifyReferencedBase(
        TypeReferenceHandle baseType,
        bool definesCoreLibraryRoot,
        MetadataTypeDefinitionIndex index,
        MetadataTypeDeclarationSite site)
    {
        MetadataTypeReferenceNameEvidence reference =
            ReadReferenceName(baseType, site);
        MetadataTypeDefinitionName name =
            reference.Name;
        EntityHandle scope = reference.TerminalScope;
        if (!IsValidResolutionScope(scope))
        {
            throw Refuse(
                site with { Handle = scope },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The TypeRef resolution scope is invalid.");
        }
        if (scope.Kind == HandleKind.ModuleDefinition)
        {
            if (!index.TryGetDefinition(
                    name,
                    out TypeDefinitionHandle local,
                    out bool ambiguous)
                || ambiguous)
            {
                throw Refuse(
                    site with { Handle = baseType },
                    MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                    "A same-module TypeRef does not resolve uniquely.");
            }
            return ClassifyLocalBase(
                local,
                definesCoreLibraryRoot,
                index,
                site);
        }
        if (scope.Kind != HandleKind.AssemblyReference)
        {
            return IsTopLevelSystemType(
                    name,
                    out string unresolvedSimpleName)
                && CategoryFromCoreBase(
                    unresolvedSimpleName)
                    is not MetadataTypeDeclarationCategory.Class
                ? throw Refuse(
                    site with { Handle = baseType },
                    MetadataTypeDeclarationFailureReason
                        .UnsupportedShape,
                    "The TypeRef category root cannot be authenticated from its resolution scope.")
                : MetadataTypeDeclarationCategory.Class;
        }
        AssemblyReferenceIdentity referenceIdentity =
            ReadAssemblyReferenceIdentity(
                (AssemblyReferenceHandle)scope,
                site with { Handle = scope });
        if (!Read(
                site with { Handle = scope },
                () => ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                    referenceIdentity)))
        {
            return MetadataTypeDeclarationCategory.Class;
        }

        return IsTopLevelSystemType(name, out string simpleName)
            ? CategoryFromCoreBase(simpleName)
            : MetadataTypeDeclarationCategory.Class;
    }

    MetadataTypeReferenceNameEvidence ReadReferenceName(
        TypeReferenceHandle handle,
        MetadataTypeDeclarationSite site)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        bool complete;
        EntityHandle terminal;
        RelationshipTraversalRejection? rejection;
        try
        {
            complete = MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    _reader,
                    handle,
                    chain,
                    out _,
                    out terminal,
                    out rejection,
                    relationship => Charge(
                        site with { Handle = relationship },
                        MetadataOperationDimension.RelationshipEdges));
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site with { Handle = handle },
                MetadataTypeDeclarationFailureReason
                    .MalformedMetadata,
                "The TypeRef resolution-scope chain is malformed.");
        }
        if (!complete)
        {
            RelationshipTraversalRejection failure =
                rejection
                ?? throw new InvalidOperationException(
                    "An incomplete TypeRef traversal lacks a rejection.");
            throw Refuse(
                site with { Handle = failure.Subject },
                Map(failure.Kind),
                "The TypeRef resolution-scope chain is invalid.");
        }

        MetadataTypeDefinitionNameReadResult result = Read(
            site with { Handle = handle },
            () => MetadataTypeDefinitionNameReader.Read(
                _reader,
                handle,
                beforeMaterialize: amount => Charge(
                    site with { Handle = handle },
                    MetadataOperationDimension.StructuredNodes,
                    amount),
                chargeChain: amount => Charge(
                    site with { Handle = handle },
                    MetadataOperationDimension.RelationshipEdges,
                    amount),
                chargeCharacters: amount => Charge(
                    site with { Handle = handle },
                    MetadataOperationDimension.RetainedText,
                    amount)));
        MetadataTypeDefinitionName name = result switch
        {
            MetadataTypeDefinitionNameReadResult.Read read =>
                read.Name,
            _ => throw Refuse(
                site with { Handle = handle },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The TypeRef structured name is invalid."),
        };
        return new(name, terminal);
    }

    bool IsValidResolutionScope(EntityHandle scope)
    {
        int row = MetadataTokens.GetRowNumber(scope);
        return scope.Kind switch
        {
            HandleKind.ModuleDefinition => row == 1,
            HandleKind.ModuleReference =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.ModuleRef),
            HandleKind.AssemblyReference =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.AssemblyRef),
            HandleKind.TypeReference =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.TypeRef),
            _ => false,
        };
    }

    AssemblyReferenceIdentity ReadAssemblyReferenceIdentity(
        AssemblyReferenceHandle handle,
        MetadataTypeDeclarationSite site)
    {
        System.Reflection.Metadata.AssemblyReference reference = Read(
            site,
            () => _reader.GetAssemblyReference(handle));
        StringHandle nameHandle = Read(site, () => reference.Name);
        StringHandle cultureHandle = Read(
            site,
            () => reference.Culture);
        BlobHandle keyHandle = Read(
            site,
            () => reference.PublicKeyOrToken);
        string name = ReadAssemblyReferenceText(
            nameHandle,
            allowNil: false,
            site)
            ?? throw new InvalidOperationException(
                "An assembly reference name cannot be nil.");
        string? culture = ReadAssemblyReferenceText(
            cultureHandle,
            allowNil: true,
            site);
        string? token = null;
        if (!keyHandle.IsNil)
        {
            int keyBytes = Read(
                site,
                () => _reader.GetBlobReader(keyHandle).Length);
            Charge(
                site,
                MetadataOperationDimension.StructuredNodes,
                keyBytes);
            Charge(
                site,
                MetadataOperationDimension.RetainedText,
                16);
            token = Read(
                site,
                () => AssemblyReferenceIdentity.TokenOrNull(
                    _reader,
                    keyHandle,
                    (reference.Flags
                        & AssemblyFlags.PublicKey) != 0));
        }
        return new(
            name,
            reference.Version,
            culture,
            token);
    }

    string? ReadAssemblyReferenceText(
        StringHandle handle,
        bool allowNil,
        MetadataTypeDeclarationSite site)
    {
        if (handle.IsNil)
        {
            if (allowNil)
                return null;
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "An assembly reference has no name.");
        }
        int bytes = Read(
            site,
            () => _reader.GetBlobReader(handle).Length);
        if (bytes
            > MetadataSafetyPolicy
                .MaxStructuralSignatureChars)
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
                "Assembly-reference identity text exceeds the structural limit.");
        }
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            bytes);
        Charge(
            site,
            MetadataOperationDimension.RetainedText,
            bytes);
        return Read(
            site,
            () => _reader.GetString(handle));
    }

    MetadataTypeDeclarationCategory ClassifySpecificationBase(
        TypeSpecificationHandle baseType,
        bool definesCoreLibraryRoot,
        MetadataTypeDefinitionIndex index,
        MetadataTypeDeclarationSite site)
    {
        TypeSpecificationRootReadResult result =
            TypeSpecificationRoot.Read(
                _reader,
                baseType,
                beforeDecodeBytes: (handle, amount) => Charge(
                    site with { Handle = handle },
                    MetadataOperationDimension.SignatureBytes,
                    amount),
                beforeDependencyEdge: handle => Charge(
                    site with { Handle = handle },
                    MetadataOperationDimension.RelationshipEdges),
                graphValidated: handle =>
                    _token.ThrowIfCancellationRequested());
        if (result
            is TypeSpecificationRootReadResult.Read read)
        {
            TypeSpecificationRoot root = read.Root;
            if (root is
                {
                    Kind: TypeSpecificationRootKind.NamedType,
                    RawTypeKind: 0x12,
                }
                && IsValidTypeEntity(root.Type))
            {
                return root.Type.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        ClassifyLocalBase(
                            (TypeDefinitionHandle)root.Type,
                            definesCoreLibraryRoot,
                            index,
                            site),
                    HandleKind.TypeReference =>
                        ClassifyReferencedBase(
                            (TypeReferenceHandle)root.Type,
                            definesCoreLibraryRoot,
                            index,
                            site),
                    _ => throw Refuse(
                        site with { Handle = root.Type },
                        MetadataTypeDeclarationFailureReason
                            .UnsupportedShape,
                        "The TypeSpec extends root cannot authenticate a declaration category."),
                };
            }
            throw Refuse(
                site with { Handle = root.Type },
                root.Kind == TypeSpecificationRootKind.NamedType
                    && !IsValidTypeEntity(root.Type)
                    ? MetadataTypeDeclarationFailureReason
                        .MalformedMetadata
                    : MetadataTypeDeclarationFailureReason
                        .UnsupportedShape,
                root.Kind == TypeSpecificationRootKind.NamedType
                    && !IsValidTypeEntity(root.Type)
                    ? "The TypeSpec extends root references an invalid row."
                    : "The TypeSpec extends encoding cannot authenticate a declaration category.");
        }

        return result switch
        {
            TypeSpecificationRootReadResult.Cycle cycle =>
                throw Refuse(
                    site with { Handle = cycle.Subject },
                    MetadataTypeDeclarationFailureReason.Cycle,
                    "The TypeSpec extends graph contains a cycle."),
            TypeSpecificationRootReadResult.BudgetExceeded budget =>
                throw Refuse(
                    site with { Handle = budget.Subject },
                    MetadataTypeDeclarationFailureReason.BudgetExceeded,
                    "The TypeSpec extends graph exceeded its structural budget."),
            TypeSpecificationRootReadResult.Malformed malformed =>
                throw Refuse(
                    site with { Handle = malformed.Subject },
                    MetadataTypeDeclarationFailureReason.MalformedMetadata,
                    "The TypeSpec extends encoding is malformed."),
            _ => throw Refuse(
                site with { Handle = baseType },
                MetadataTypeDeclarationFailureReason.UnsupportedShape,
                "The TypeSpec extends encoding cannot authenticate a declaration category."),
        };
    }

    bool IsValidTypeEntity(EntityHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.TypeDef),
            HandleKind.TypeReference =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.TypeRef),
            HandleKind.TypeSpecification =>
                row > 0
                && row <= _reader.GetTableRowCount(
                    TableIndex.TypeSpec),
            _ => false,
        };
    }

    static MetadataTypeDeclarationCategory CategoryFromCoreBase(
        string simpleName) =>
        simpleName switch
        {
            "ValueType" =>
                MetadataTypeDeclarationCategory.Struct,
            "Enum" =>
                MetadataTypeDeclarationCategory.Enum,
            "Delegate" or "MulticastDelegate" =>
                MetadataTypeDeclarationCategory.Delegate,
            _ => MetadataTypeDeclarationCategory.Class,
        };

    MetadataTypeIdentity ProjectDefinition(
        TypeDefinitionHandle handle,
        bool isValueType)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        TypeNode node = Read(
            site,
            () => Provider(site).GetTypeFromDefinition(
                _reader,
                handle,
                isValueType ? (byte)0x11 : (byte)0x12));
        return Project(node, site);
    }

    MetadataTypeIdentity CreateOpenSelf(
        MetadataNamedTypeIdentity definition,
        bool isValueType,
        int genericParameterCount,
        TypeDefinitionHandle handle)
    {
        if (genericParameterCount == 0)
        {
            Charge(
                Site(
                    MetadataTypeDeclarationStage.IdentityProjection,
                    MetadataTypeDeclarationMechanism
                        .StructuredProjection,
                    handle),
                MetadataOperationDimension.StructuredNodes);
            return new MetadataTypeIdentity.Named(
                definition,
                isValueType);
        }

        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        var arguments =
            ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
                genericParameterCount);
        for (int index = 0;
            index < genericParameterCount;
            index++)
        {
            Charge(
                site,
                MetadataOperationDimension.StructuredNodes);
            arguments.Add(
                new MetadataTypeIdentity.GenericParameter(
                    IsMethodParameter: false,
                    index));
        }
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes);
        return new MetadataTypeIdentity.GenericInstance(
            definition,
            isValueType,
            arguments.MoveToImmutable());
    }

    MetadataTypeIdentity.Primitive? CreatePrimitiveAlias(
        MetadataTypeDefinitionName name,
        int genericParameterCount,
        bool definesCoreLibraryRoot,
        TypeDefinitionHandle handle)
    {
        if (!definesCoreLibraryRoot
            || genericParameterCount != 0
            || !IsTopLevelSystemType(
                name,
                out string simpleName)
            || !TryGetPrimitiveTypeCode(
                simpleName,
                out PrimitiveTypeCode code))
        {
            return null;
        }

        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        MetadataTypeIdentity projected =
            Project(Provider(site).GetPrimitiveType(code), site);
        return projected
            as MetadataTypeIdentity.Primitive
            ?? throw new InvalidOperationException(
                "A primitive type did not project as primitive.");
    }

    int[] ReadDeclaringRows(
        TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.NestingValidation,
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            handle);
        int typeCount =
            _reader.GetTableRowCount(TableIndex.TypeDef);
        int physicalNestedCount =
            _reader.GetTableRowCount(TableIndex.NestedClass);
        Charge(
            site,
            MetadataOperationDimension.RelationshipEdges,
            checked(typeCount + physicalNestedCount));
        Charge(
            site,
            MetadataOperationDimension.StructuredNodes,
            typeCount + 1);

        var declaringRows = new int[typeCount + 1];
        int observedNestedCount = 0;
        try
        {
            foreach (TypeDefinitionHandle parentHandle
                in _reader.TypeDefinitions)
            {
                _token.ThrowIfCancellationRequested();
                TypeDefinition parent =
                    _reader.GetTypeDefinition(parentHandle);
                foreach (TypeDefinitionHandle nested
                    in parent.GetNestedTypes())
                {
                    observedNestedCount++;
                    int nestedRow =
                        MetadataTokens.GetRowNumber(nested);
                    if (nestedRow <= 0
                        || nestedRow > typeCount
                        || declaringRows[nestedRow] != 0)
                    {
                        throw Refuse(
                            site with { Handle = nested },
                            MetadataTypeDeclarationFailureReason
                                .MalformedMetadata,
                            "The NestedClass projection is invalid.");
                    }
                    declaringRows[nestedRow] =
                        MetadataTokens.GetRowNumber(
                            parentHandle);
                    if (_reader.GetTypeDefinition(nested)
                            .GetDeclaringType()
                        != parentHandle)
                    {
                        throw Refuse(
                            site with { Handle = nested },
                            MetadataTypeDeclarationFailureReason
                                .MalformedMetadata,
                            "The NestedClass projection is inconsistent.");
                    }
                }
            }
        }
        catch (TypeDeclarationRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The NestedClass projection is malformed.");
        }

        if (observedNestedCount != physicalNestedCount)
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The NestedClass table has unobservable rows.");
        }
        return declaringRows;
    }

    MetadataTypeDefinitionAddress? ValidateNesting(
        TypeDefinitionHandle handle,
        TypeAttributes attributes,
        MetadataNamedTypeIdentity identity,
        ImmutableArray<int> introducedCounts,
        int[] declaringRows)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.NestingValidation,
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            handle);
        TypeDefinitionHandle directDeclaring = default;
        TypeDefinitionHandle current = handle;
        int chainLength = 0;
        while (!current.IsNil)
        {
            if (chainLength
                == MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw Refuse(
                    site with { Handle = current },
                    MetadataTypeDeclarationFailureReason
                        .BudgetExceeded,
                    "The TypeDef declaring chain exceeds the structural limit.");
            }
            chainLength++;
            int currentRow =
                MetadataTokens.GetRowNumber(current);
            TypeDefinition definition = Read(
                site with { Handle = current },
                () => _reader.GetTypeDefinition(current));
            TypeAttributes visibility =
                definition.Attributes
                & TypeAttributes.VisibilityMask;
            bool nestedVisibility =
                visibility is not TypeAttributes.NotPublic
                    and not TypeAttributes.Public;
            TypeDefinitionHandle declaring = Read(
                site with { Handle = current },
                definition.GetDeclaringType);
            int projectedDeclaringRow =
                declaringRows[currentRow];
            if (declaring.IsNil)
            {
                if (nestedVisibility
                    || projectedDeclaringRow != 0)
                {
                    throw Refuse(
                        site with { Handle = current },
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "A declaring-chain root has nested visibility or a declaring relationship.");
                }
            }
            else
            {
                Charge(
                    site with { Handle = declaring },
                    MetadataOperationDimension
                        .RelationshipEdges);
                if (!nestedVisibility
                    || projectedDeclaringRow
                        != MetadataTokens.GetRowNumber(
                            declaring))
                {
                    throw Refuse(
                        site with { Handle = current },
                        MetadataTypeDeclarationFailureReason
                            .MalformedMetadata,
                        "A nested declaring-chain segment lacks matching visibility and relationship evidence.");
                }
                if (current == handle)
                    directDeclaring = declaring;
            }
            current = declaring;
        }

        if (chainLength != identity.Segments.Length
            || chainLength != introducedCounts.Length)
        {
            throw Refuse(
                site with { Handle = handle },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The structured identity and declaring chain have different lengths.");
        }

        TypeAttributes selectedVisibility =
            attributes & TypeAttributes.VisibilityMask;
        bool selectedIsNested =
            selectedVisibility is not TypeAttributes.NotPublic
                and not TypeAttributes.Public;
        if (selectedIsNested != !directDeclaring.IsNil)
        {
            throw Refuse(
                site with { Handle = handle },
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "The selected TypeDef visibility and declaring relationship disagree.");
        }
        return directDeclaring.IsNil
            ? null
            : MetadataTypeDefinitionAddress.FromHandle(
                _reader,
                directDeclaring);
    }

    MetadataTypeDefinitionIndex GetIndex(
        TypeDefinitionHandle handle)
    {
        MetadataTypeDeclarationSite site = Site(
            MetadataTypeDeclarationStage.IdentityProjection,
            MetadataTypeDeclarationMechanism.StructuredProjection,
            handle);
        try
        {
            return _getIndex(
                _token.ThrowIfCancellationRequested,
                definition => Charge(
                    site with { Handle = definition },
                    MetadataOperationDimension.RelationshipEdges),
                () => Charge(
                    site,
                    MetadataOperationDimension.StructuredNodes),
                amount => Charge(
                    site,
                    MetadataOperationDimension.RetainedText,
                    amount));
        }
        catch (MetadataTypeDefinitionIndexFailureException ex)
        {
            throw Refuse(
                site with { Handle = ex.Subject },
                ex.Kind switch
                {
                    MetadataTypeDefinitionIndexFailureKind.Cycle =>
                        MetadataTypeDeclarationFailureReason.Cycle,
                    MetadataTypeDefinitionIndexFailureKind
                        .BudgetExceeded =>
                        MetadataTypeDeclarationFailureReason
                            .BudgetExceeded,
                    _ => MetadataTypeDeclarationFailureReason
                        .MalformedMetadata,
                },
                "The TypeDef structured-name index is invalid.");
        }
    }

    TypeNodeProvider Provider(
        MetadataTypeDeclarationSite site) =>
        new(
            beforeRetain: value => Charge(
                site,
                MetadataOperationDimension.RetainedText,
                value.Length),
            beforeMaterialize: amount => Charge(
                site,
                MetadataOperationDimension.StructuredNodes,
                amount),
            beforeCreateNode: () => Charge(
                site,
                MetadataOperationDimension.StructuredNodes),
            retainExactScope: true,
            beforeTypeDefinitionResolve: (_, definition) =>
                Charge(
                    site with { Handle = definition },
                    MetadataOperationDimension.RelationshipEdges),
            relationshipRejected: rejection => throw Refuse(
                site with { Handle = rejection.Subject },
                Map(rejection.Kind),
                "The TypeDef identity relationship is invalid."),
            beforeRelationshipFollow: relationship =>
                Charge(
                    site with { Handle = relationship },
                    MetadataOperationDimension.RelationshipEdges),
            getLocalTypeDefinitions: reader =>
            {
                if (!ReferenceEquals(reader, _reader))
                {
                    throw new InvalidOperationException(
                        "A foreign reader reached the TypeDef index.");
                }
                return GetIndex(site.Handle.IsNil
                    ? default
                    : (TypeDefinitionHandle)site.Handle);
            });

    MetadataTypeIdentity Project(
        TypeNode node,
        MetadataTypeDeclarationSite site) =>
        new MetadataTypeIdentityProjector(
            () => Charge(
                site,
                MetadataOperationDimension.StructuredNodes),
            text => Retain(text, site),
            detail => Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                detail))
            .Project(node);

    InertString Retain(
        string value,
        MetadataTypeDeclarationSite site)
    {
        MetadataTypeDeclarationSite retentionSite = site with
        {
            Mechanism =
                MetadataTypeDeclarationMechanism.TextRetention,
        };
        Charge(
            retentionSite,
            MetadataOperationDimension.RetainedText,
            VisualEncoder.MeasureEncodedLength(
                TextPolicy.Field,
                value));
        return new InertString(TextPolicy.Field, value);
    }

    T Read<T>(
        MetadataTypeDeclarationSite site,
        Func<T> read)
    {
        try
        {
            _token.ThrowIfCancellationRequested();
            return read();
        }
        catch (TypeDeclarationRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (IsMetadataException(ex))
        {
            throw Refuse(
                site,
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                "Metadata required by the TypeDef post could not be read.");
        }
    }

    void Charge(
        MetadataTypeDeclarationSite site,
        MetadataOperationDimension dimension,
        int amount = 1)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _context.Charge(dimension, amount);
        }
        catch (MetadataOperationBudgetExceededException ex)
        {
            throw new TypeDeclarationRejectedException(
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
                site,
                "The TypeDef post exceeded its operation budget.",
                ex.Dimension,
                ex.Limit,
                ex.AttemptedCharge);
        }
    }

    TypeDeclarationRejectedException Refuse(
        MetadataTypeDeclarationSite site,
        MetadataTypeDeclarationFailureReason reason,
        string detail) =>
        new(reason, site, detail);

    static MetadataTypeDeclarationFailureReason Map(
        RelationshipTraversalRejectionKind kind) =>
        kind switch
        {
            RelationshipTraversalRejectionKind.Cycle =>
                MetadataTypeDeclarationFailureReason.Cycle,
            RelationshipTraversalRejectionKind.NodeBudget
                or RelationshipTraversalRejectionKind.NameBudget =>
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
            _ => MetadataTypeDeclarationFailureReason.MalformedMetadata,
        };

    static bool IsTopLevelSystemType(
        MetadataTypeDefinitionName name,
        out string simpleName)
    {
        simpleName = "";
        if (name.Namespace != "System"
            || name.Segments.Length != 1)
        {
            return false;
        }
        simpleName = name.Segments[0];
        return true;
    }

    static bool TryGetPrimitiveTypeCode(
        string simpleName,
        out PrimitiveTypeCode code)
    {
        code = simpleName switch
        {
            "Void" => PrimitiveTypeCode.Void,
            "Boolean" => PrimitiveTypeCode.Boolean,
            "Char" => PrimitiveTypeCode.Char,
            "SByte" => PrimitiveTypeCode.SByte,
            "Byte" => PrimitiveTypeCode.Byte,
            "Int16" => PrimitiveTypeCode.Int16,
            "UInt16" => PrimitiveTypeCode.UInt16,
            "Int32" => PrimitiveTypeCode.Int32,
            "UInt32" => PrimitiveTypeCode.UInt32,
            "Int64" => PrimitiveTypeCode.Int64,
            "UInt64" => PrimitiveTypeCode.UInt64,
            "Single" => PrimitiveTypeCode.Single,
            "Double" => PrimitiveTypeCode.Double,
            "String" => PrimitiveTypeCode.String,
            "Object" => PrimitiveTypeCode.Object,
            "IntPtr" => PrimitiveTypeCode.IntPtr,
            "UIntPtr" => PrimitiveTypeCode.UIntPtr,
            "TypedReference" =>
                PrimitiveTypeCode.TypedReference,
            _ => default,
        };
        return simpleName
            is "Void"
                or "Boolean"
                or "Char"
                or "SByte"
                or "Byte"
                or "Int16"
                or "UInt16"
                or "Int32"
                or "UInt32"
                or "Int64"
                or "UInt64"
                or "Single"
                or "Double"
                or "String"
                or "Object"
                or "IntPtr"
                or "UIntPtr"
                or "TypedReference";
    }

    MetadataTypeDeclarationSite Site(
        MetadataTypeDeclarationStage stage,
        MetadataTypeDeclarationMechanism mechanism,
        EntityHandle handle) =>
        new(stage, mechanism, handle);

    static bool IsMetadataException(Exception ex) =>
        ex is BadImageFormatException
            or ArgumentException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException
            or InvalidCastException
            or OverflowException;

    readonly record struct MetadataTypeDeclarationSite(
        MetadataTypeDeclarationStage Stage,
        MetadataTypeDeclarationMechanism Mechanism,
        EntityHandle Handle);

    readonly record struct MetadataTypeReferenceNameEvidence(
        MetadataTypeDefinitionName Name,
        EntityHandle TerminalScope);

    sealed class TypeDeclarationRejectedException(
        MetadataTypeDeclarationFailureReason reason,
        MetadataTypeDeclarationSite site,
        string detail,
        MetadataOperationDimension? dimension = null,
        long? limit = null,
        long? attempted = null)
        : Exception(detail)
    {
        internal MetadataTypeDeclarationFailureReason Reason { get; } =
            reason;
        internal MetadataTypeDeclarationSite Site { get; } = site;
        internal MetadataOperationDimension? Dimension { get; } =
            dimension;
        internal long? Limit { get; } = limit;
        internal long? Attempted { get; } = attempted;
    }
}
