using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Fixed safety ceilings for artifact-derived metadata operations.</summary>
public static class MetadataSafetyPolicy
{
    /// <summary>
    /// Maximum encoded characters in one structural type or method key.
    /// Gated by <c>OversizedStructuralSignature_FailsClosed</c>.
    /// </summary>
    public const int MaxStructuralSignatureChars = 1024 * 1024;

    /// <summary>
    /// Maximum encoded characters produced across one structural-signature
    /// builder's lifetime. Gated by
    /// <c>BuildMethodKey_CumulativeWorkBudgetFailsBeforeRepeatingDecode</c>.
    /// </summary>
    public const int MaxStructuralSignatureWorkChars = 4 * 1024 * 1024;

    /// <summary>
    /// Maximum work charged while constructing one member anchor. Type-name
    /// occurrences are charged by character length with a short-leaf floor,
    /// and every composite type node (arrays, pointers, generics, function
    /// pointers) is charged a fixed per-node unit, so
    /// discarded modifier subtrees that are deep or wide cannot amplify past
    /// this ceiling before rejection. TypeDef/TypeRef leaves charge from
    /// UTF-8 storage and materialize names only when rendered, so unique long
    /// discarded modifiers cannot force large string allocations on cache
    /// miss either. Gated by
    /// <c>CreateMethodAnchor_RepeatedTypeNamesFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_WideGenericModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_WideTypeRefGenericModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_UniqueLongTypeRefModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchorInfo_RepeatedLongNamesExhaustSharedProjectionBudget</c>,
    /// and
    /// <c>CreateMethodAnchorInfo_HighGenericArityExhaustsBeforeContextAllocation</c>.
    /// The caller-owned cumulative overload also charges names, rendered
    /// strings, canonical identity, selector output, and fingerprint input
    /// against this ceiling.
    /// </summary>
    public const int MaxAnchorSignatureWorkChars =
        MaxStructuralSignatureWorkChars;

    /// <summary>
    /// Maximum metadata-name and type-node work used to project one MethodDef
    /// into a member signature shape, including names from erased custom
    /// modifiers and legacy generic-parameter compatibility. Gated by
    /// <c>MetadataAdapter_RefusesErasedModifierAmplificationBeforeLargeAllocation</c>
    /// and
    /// <c>LegacyCompatibility_RefusesGenericNameAmplificationBeforeLargeAllocation</c>.
    /// </summary>
    public const int MaxMemberSignatureShapeWorkChars =
        MaxStructuralSignatureWorkChars;

    /// <summary>
    /// Maximum type nodes examined before decoding one metadata signature.
    /// This bounds the iterative guard stack and SRM's decoded parameter/type
    /// materialization for structurally shallow but hostile signatures. Gated by
    /// <c>Resolve_OversizedShallowSignatureRejectsBeforeLargeAllocation</c>.
    /// </summary>
    public const int MaxSignatureTypeNodes = 64 * 1024;

    /// <summary>
    /// Maximum MethodDef rows scanned by one correspondence operation.
    /// Gated by <c>Resolve_DuplicateRowsStayWithinAllocationBudget</c>.
    /// </summary>
    public const int MaxCorrespondenceMethodRows = 256 * 1024;

    /// <summary>
    /// Maximum matching MethodDef addresses materialized before malformed
    /// duplicate metadata is rejected. Gated by
    /// <c>Resolve_DuplicateCandidatesFailClosedAtCap</c>.
    /// </summary>
    public const int MaxCorrespondenceCandidates = 1024;

    /// <summary>
    /// Maximum Property, Event, and MethodSemantics rows scanned while
    /// indexing memory-safety accessor associations.
    /// </summary>
    public const int MaxMemorySafetyAssociationRows =
        MaxCorrespondenceMethodRows;

    /// <summary>
    /// Maximum custom-attribute rows inspected for one memory-safety module or
    /// member query.
    /// </summary>
    public const int MaxMemorySafetyAttributeRows =
        MaxCorrespondenceCandidates;

    /// <summary>
    /// Maximum type-name materialization work for one memory-safety attribute
    /// scan.
    /// </summary>
    public const int MaxMemorySafetyNameWorkChars =
        MaxStructuralSignatureWorkChars;

    /// <summary>
    /// Maximum CustomAttribute rows walked once while proving that owner-range
    /// attribute lookups observe every physical row.
    /// </summary>
    public const int MaxMemorySafetyCustomAttributeOrderRows = 1024 * 1024;

    /// <summary>
    /// Maximum <see cref="BadImageFormatException"/> failures while decoding
    /// method anchors during one classified-method scan. Each failure is
    /// already bounded per anchor, but catch-and-continue would otherwise
    /// multiply that cost by method count on hostile multi-method images.
    /// Gated by
    /// <c>Scan_MultiMethodHostileIdentitiesFailClosedBeforeLargeAllocation</c>.
    /// </summary>
    public const int MaxClassificationIdentityDecodeFailures = 3;

    /// <summary>
    /// Maximum cumulative anchor-signature work charged across one classified-
    /// method scan. Prevents many near-limit successful identities from
    /// multiplying per-anchor cost when none individually trips the failure
    /// counter. Gated by
    /// <c>Scan_NearLimitMultiMethodIdentitiesFailClosedBeforeLargeAllocation</c>.
    /// </summary>
    public const int MaxClassificationScanWorkChars =
        MaxAnchorSignatureWorkChars;

    /// <summary>
    /// Maximum unique handles in one TypeDef, TypeRef, or ExportedType
    /// relationship chain.
    /// </summary>
    public const int MaxRelationshipNodes = 256;

    /// <summary>
    /// Maximum characters in one structured type name: its namespace plus every root-to-leaf
    /// segment, plus one delimiter per segment boundary.
    /// </summary>
    /// <remarks>
    /// The segment-count ceiling alone bounds nesting depth, not size: each of up to
    /// <see cref="MaxRelationshipNodes"/> segments carries an artifact-authored string of
    /// arbitrary length, so a malformed image can encode a name of hundreds of megabytes within
    /// the depth budget. Real names are far smaller — the deepest generated names in the .NET
    /// libraries are a few hundred characters — so this ceiling only trips on input that was
    /// never a name. Legacy readers preflight UTF-8 storage then recheck decoded length;
    /// gated by
    /// <c>SharedOversizeHeapString_IsRejectedBeforeAggregateMaterialization</c>,
    /// <c>ManySmallSegments_AreRejectedOnAggregateEncodedLength</c>,
    /// <c>LeafAppendOverBudget_IsRejectedBeforeLeafMaterialization</c>, and
    /// <c>StructuredRead_ReportsNameBudgetNotMalformed</c>.
    /// </remarks>
    public const int MaxTypeNameCharacters = 4096;

    /// <summary>
    /// Decodes one component of an aggregate metadata type name only when its
    /// encoded length can still produce a decoded value within the caller's
    /// remaining UTF-16 character budget.
    /// </summary>
    public static bool TryReadTypeNameComponent(
        MetadataReader reader,
        StringHandle handle,
        ref int remainingCharacters,
        out string value)
    {
        const int MaxUtf8BytesPerUtf16Character = 3;
        ArgumentOutOfRangeException.ThrowIfNegative(remainingCharacters);
        value = "";
        if (reader.GetBlobReader(handle).Length
            > (long)remainingCharacters
                * MaxUtf8BytesPerUtf16Character)
            return false;

        string decoded = reader.GetString(handle);
        if (decoded.Length > remainingCharacters)
            return false;

        remainingCharacters -= decoded.Length;
        value = decoded;
        return true;
    }

    /// <summary>
    /// Decodes one metadata string only after its UTF-8 storage is within the
    /// structural-signature ceiling. Projected virtual strings may be materialized
    /// by SRM while their storage length is obtained, but remain subject to the
    /// same decoded-length ceiling. Gated by
    /// <c>Resolve_OversizedMethodNameRejectsBeforeLargeAllocation</c>.
    /// </summary>
    public static string ReadStructuralString(
        MetadataReader reader,
        StringHandle handle)
    {
        if (reader.GetBlobReader(handle).Length
            > MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The metadata string exceeds the structural-signature budget.");
        }

        string value = reader.GetString(handle);
        if (value.Length > MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The metadata string exceeds the structural-signature budget.");
        }
        return value;
    }
}

/// <summary>Bounded iterative walks over SRM metadata relationships.</summary>
public static class MetadataRelationshipTraversal
{
    /// <summary>Walks a TypeDef declaring-type chain from its outermost type to the requested leaf.</summary>
    public static RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>
        WalkTypeDefinitionDeclaringChain(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        => Walk<TypeDefinitionHandle, TypeDefinitionRelationship>(
            reader,
            handle);

    /// <summary>
    /// Walks a TypeDef declaring-type chain into caller-owned storage without allocating
    /// on a completed traversal.
    /// </summary>
    public static bool TryWalkTypeDefinitionDeclaringChain(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Span<TypeDefinitionHandle> rootToLeaf,
        out int consumedNodes,
        out EntityHandle terminal,
        out RelationshipTraversalRejection? rejection)
        => TryWalk<TypeDefinitionHandle, TypeDefinitionRelationship>(
            reader,
            handle,
            rootToLeaf,
            out consumedNodes,
            out terminal,
            out rejection);

    /// <summary>Walks a TypeRef resolution-scope chain from its outermost type to the requested leaf.</summary>
    public static RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>
        WalkTypeReferenceResolutionScope(
            MetadataReader reader,
            TypeReferenceHandle handle)
        => Walk<TypeReferenceHandle, TypeReferenceRelationship>(
            reader,
            handle);

    /// <summary>
    /// Walks a TypeRef resolution-scope chain into caller-owned storage without allocating
    /// on a completed traversal.
    /// </summary>
    public static bool TryWalkTypeReferenceResolutionScope(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Span<TypeReferenceHandle> rootToLeaf,
        out int consumedNodes,
        out EntityHandle terminal,
        out RelationshipTraversalRejection? rejection)
        => TryWalk<TypeReferenceHandle, TypeReferenceRelationship>(
            reader,
            handle,
            rootToLeaf,
            out consumedNodes,
            out terminal,
            out rejection);

    /// <summary>Walks an ExportedType implementation chain from its outermost type to the requested leaf.</summary>
    public static RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>
        WalkExportedTypeImplementationChain(
            MetadataReader reader,
            ExportedTypeHandle handle)
        => Walk<ExportedTypeHandle, ExportedTypeRelationship>(
            reader,
            handle);

    /// <summary>
    /// Walks an ExportedType implementation chain into caller-owned storage without allocating
    /// on a completed traversal.
    /// </summary>
    public static bool TryWalkExportedTypeImplementationChain(
        MetadataReader reader,
        ExportedTypeHandle handle,
        Span<ExportedTypeHandle> rootToLeaf,
        out int consumedNodes,
        out EntityHandle terminal,
        out RelationshipTraversalRejection? rejection)
        => TryWalk<ExportedTypeHandle, ExportedTypeRelationship>(
            reader,
            handle,
            rootToLeaf,
            out consumedNodes,
            out terminal,
            out rejection);

    static RelationshipTraversalResult<RelationshipChain<THandle>> Walk<THandle, TRelationship>(
        MetadataReader reader,
        EntityHandle start)
        where THandle : unmanaged
        where TRelationship : struct, IRelationship<THandle>
    {
        Span<THandle> rootToLeaf =
            stackalloc THandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!TryWalk<THandle, TRelationship>(
                reader,
                start,
                rootToLeaf,
                out int consumedNodes,
                out EntityHandle terminal,
                out var rejection))
        {
            return new RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected(
                rejection!);
        }

        return new RelationshipTraversalResult<RelationshipChain<THandle>>.Completed(
            new RelationshipChain<THandle>(
                ImmutableArray.Create(rootToLeaf[..consumedNodes]),
                terminal),
            consumedNodes);
    }

    static bool TryWalk<THandle, TRelationship>(
        MetadataReader reader,
        EntityHandle start,
        Span<THandle> rootToLeaf,
        out int consumedNodes,
        out EntityHandle terminal,
        out RelationshipTraversalRejection? rejection)
        where THandle : unmanaged
        where TRelationship : struct, IRelationship<THandle>
    {
        if (rootToLeaf.Length < MetadataSafetyPolicy.MaxRelationshipNodes)
            throw new ArgumentException(
                $"Relationship storage must hold at least "
                + $"{MetadataSafetyPolicy.MaxRelationshipNodes} handles.",
                nameof(rootToLeaf));

        consumedNodes = 0;
        terminal = default;
        rejection = null;
        if (start.IsNil)
        {
            rejection = CreateRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                "The relationship starts with a nil handle.",
                start,
                consumedNodes: 0);
            return false;
        }

        EntityHandle current = start;
        int count = 0;
        while (!current.IsNil && current.Kind == TRelationship.RelationshipKind)
        {
            for (int i = 0; i < count; i++)
            {
                if (TRelationship.ToEntity(rootToLeaf[i]) == current)
                {
                    rejection = CreateRejection(
                        RelationshipTraversalRejectionKind.Cycle,
                        $"The {TRelationship.RelationshipKind} relationship repeats handle "
                        + $"{FormatHandle(current)}.",
                        current,
                        count);
                    consumedNodes = count;
                    return false;
                }
            }

            if (count >= MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                rejection = CreateRejection(
                    RelationshipTraversalRejectionKind.NodeBudget,
                    $"The {TRelationship.RelationshipKind} relationship exceeds "
                    + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes.",
                    current,
                    count);
                consumedNodes = count;
                return false;
            }

            rootToLeaf[count++] = TRelationship.Convert(current);
            try
            {
                current = TRelationship.Next(
                    reader,
                    rootToLeaf[count - 1]);
            }
            catch (BadImageFormatException ex)
            {
                rejection = CreateRejection(
                    RelationshipTraversalRejectionKind.MalformedMetadata,
                    ex.Message,
                    TRelationship.ToEntity(rootToLeaf[count - 1]),
                    count);
                consumedNodes = count;
                return false;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                rejection = CreateRejection(
                    RelationshipTraversalRejectionKind.MalformedMetadata,
                    ex.Message,
                    TRelationship.ToEntity(rootToLeaf[count - 1]),
                    count);
                consumedNodes = count;
                return false;
            }
        }

        rootToLeaf[..count].Reverse();

        consumedNodes = count;
        terminal = current;
        return true;
    }

    interface IRelationship<THandle>
        where THandle : unmanaged
    {
        static abstract HandleKind RelationshipKind { get; }
        static abstract THandle Convert(EntityHandle handle);
        static abstract EntityHandle ToEntity(THandle handle);
        static abstract EntityHandle Next(MetadataReader reader, THandle handle);
    }

    readonly struct TypeDefinitionRelationship
        : IRelationship<TypeDefinitionHandle>
    {
        public static HandleKind RelationshipKind => HandleKind.TypeDefinition;
        public static TypeDefinitionHandle Convert(EntityHandle handle)
            => (TypeDefinitionHandle)handle;
        public static EntityHandle ToEntity(TypeDefinitionHandle handle)
            => handle;
        public static EntityHandle Next(
            MetadataReader reader,
            TypeDefinitionHandle handle)
            => reader.GetTypeDefinition(handle).GetDeclaringType();
    }

    readonly struct TypeReferenceRelationship
        : IRelationship<TypeReferenceHandle>
    {
        public static HandleKind RelationshipKind => HandleKind.TypeReference;
        public static TypeReferenceHandle Convert(EntityHandle handle)
            => (TypeReferenceHandle)handle;
        public static EntityHandle ToEntity(TypeReferenceHandle handle)
            => handle;
        public static EntityHandle Next(
            MetadataReader reader,
            TypeReferenceHandle handle)
            => reader.GetTypeReference(handle).ResolutionScope;
    }

    readonly struct ExportedTypeRelationship
        : IRelationship<ExportedTypeHandle>
    {
        public static HandleKind RelationshipKind => HandleKind.ExportedType;
        public static ExportedTypeHandle Convert(EntityHandle handle)
            => (ExportedTypeHandle)handle;
        public static EntityHandle ToEntity(ExportedTypeHandle handle)
            => handle;
        public static EntityHandle Next(
            MetadataReader reader,
            ExportedTypeHandle handle)
            => reader.GetExportedType(handle).Implementation;
    }

    static RelationshipTraversalRejection CreateRejection(
        RelationshipTraversalRejectionKind kind,
        string detail,
        EntityHandle subject,
        int consumedNodes)
        => new(
            kind,
            detail,
            subject,
            consumedNodes);

    static string FormatHandle(EntityHandle handle)
        => $"0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8}";
}
