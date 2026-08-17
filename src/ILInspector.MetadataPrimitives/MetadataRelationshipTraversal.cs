using System.Buffers;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;

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
    /// Maximum work charged while constructing one member-anchor signature
    /// tree. Type-name occurrences are charged by character length with a
    /// short-leaf floor, and every composite type node (arrays, pointers,
    /// generics, function pointers) is charged a fixed per-node unit, so
    /// discarded modifier subtrees that are deep or wide cannot amplify past
    /// this ceiling before rejection. TypeDef/TypeRef leaves charge from
    /// UTF-8 storage and materialize names only when rendered, so unique long
    /// discarded modifiers cannot force large string allocations on cache
    /// miss either. Gated by
    /// <c>CreateMethodAnchor_RepeatedTypeNamesFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_WideGenericModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchor_WideTypeRefGenericModoptsFailBeforeLargeAllocation</c>,
    /// and
    /// <c>CreateMethodAnchor_UniqueLongTypeRefModoptsFailBeforeLargeAllocation</c>.
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
    /// never a name.
    /// </remarks>
    public const int MaxTypeNameCharacters = 4096;

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

    /// <summary>
    /// Lower-bound character count of a TypeDef/TypeRef/ExportedType display name
    /// (namespace + nested segments + separators) without materializing strings.
    /// Returns false when the handle is not a named type or the chain is rejected.
    /// Gated by <c>GiantBaseTypeName_IsStoppedBeforeGetStringMaterialization</c>.
    /// </summary>
    public static bool TryCountTypeNameCharacters(
        MetadataReader reader,
        EntityHandle handle,
        out long characters)
    {
        characters = 0;
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => TryCountTypeDefinitionNameCharacters(
                    reader,
                    (TypeDefinitionHandle)handle,
                    out characters),
                HandleKind.TypeReference => TryCountTypeReferenceNameCharacters(
                    reader,
                    (TypeReferenceHandle)handle,
                    out characters),
                HandleKind.ExportedType => TryCountExportedTypeNameCharacters(
                    reader,
                    (ExportedTypeHandle)handle,
                    out characters),
                _ => false,
            };
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or DecoderFallbackException)
        {
            characters = 0;
            return false;
        }
    }

    /// <summary>
    /// Counts the fully-qualified attribute type name pointed at by a constructor
    /// MethodDef or MemberRef without materializing it.
    /// </summary>
    public static bool TryCountAttributeConstructorTypeNameCharacters(
        MetadataReader reader,
        EntityHandle constructorHandle,
        out long characters)
    {
        characters = 0;
        try
        {
            if (constructorHandle.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference(
                    (MemberReferenceHandle)constructorHandle);
                return TryCountTypeNameCharacters(reader, memberRef.Parent, out characters);
            }

            if (constructorHandle.Kind == HandleKind.MethodDefinition)
            {
                var methodDef = reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructorHandle);
                return TryCountTypeNameCharacters(
                    reader,
                    methodDef.GetDeclaringType(),
                    out characters);
            }

            return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or DecoderFallbackException)
        {
            characters = 0;
            return false;
        }
    }

    static bool TryCountTypeDefinitionNameCharacters(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        out long characters)
    {
        characters = 0;
        var typeDef = reader.GetTypeDefinition(handle);
        if (typeDef.GetDeclaringType().IsNil)
        {
            characters = CountNamespaceAndName(
                reader,
                typeDef.Namespace,
                typeDef.Name);
            return true;
        }

        var traversal = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            reader,
            handle);
        if (traversal is not RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed completed)
            return false;

        return TryCountChainNameCharacters(
            reader,
            completed.Value.Handles,
            current =>
            {
                var definition = reader.GetTypeDefinition(current);
                return (definition.Namespace, definition.Name);
            },
            out characters);
    }

    static bool TryCountTypeReferenceNameCharacters(
        MetadataReader reader,
        TypeReferenceHandle handle,
        out long characters)
    {
        characters = 0;
        var typeRef = reader.GetTypeReference(handle);
        if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
        {
            characters = CountNamespaceAndName(
                reader,
                typeRef.Namespace,
                typeRef.Name);
            return true;
        }

        var traversal = MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(
            reader,
            handle);
        if (traversal is not RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>.Completed completed)
            return false;

        return TryCountChainNameCharacters(
            reader,
            completed.Value.Handles,
            current =>
            {
                var reference = reader.GetTypeReference(current);
                return (reference.Namespace, reference.Name);
            },
            out characters);
    }

    static bool TryCountExportedTypeNameCharacters(
        MetadataReader reader,
        ExportedTypeHandle handle,
        out long characters)
    {
        characters = 0;
        var exported = reader.GetExportedType(handle);
        if (exported.Implementation.Kind != HandleKind.ExportedType)
        {
            characters = CountNamespaceAndName(
                reader,
                exported.Namespace,
                exported.Name);
            return true;
        }

        var traversal = MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(
            reader,
            handle);
        if (traversal is not RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>.Completed completed)
            return false;

        return TryCountChainNameCharacters(
            reader,
            completed.Value.Handles,
            current =>
            {
                var row = reader.GetExportedType(current);
                return (row.Namespace, row.Name);
            },
            out characters);
    }

    static bool TryCountChainNameCharacters<THandle>(
        MetadataReader reader,
        ImmutableArray<THandle> handles,
        Func<THandle, (StringHandle Namespace, StringHandle Name)> getName,
        out long characters)
        where THandle : struct
    {
        characters = 0;
        for (int index = 0; index < handles.Length; index++)
        {
            var (namespaceHandle, nameHandle) = getName(handles[index]);
            if (index == 0)
            {
                long namespaceCharacters = GetStringCharacterCount(reader, namespaceHandle);
                if (namespaceCharacters > 0)
                    characters += namespaceCharacters + 1;
            }
            else
            {
                characters++;
            }

            characters += GetStringCharacterCount(reader, nameHandle);
        }

        return true;
    }

    static long CountNamespaceAndName(
        MetadataReader reader,
        StringHandle namespaceHandle,
        StringHandle nameHandle)
    {
        long namespaceCharacters = GetStringCharacterCount(reader, namespaceHandle);
        long nameCharacters = GetStringCharacterCount(reader, nameHandle);
        return namespaceCharacters == 0
            ? nameCharacters
            : namespaceCharacters + 1 + nameCharacters;
    }

    /// <summary>
    /// Counts a metadata string's decoded UTF-16 characters without materializing it.
    /// </summary>
    /// <remarks>
    /// Streams UTF-8 through <see cref="Decoder.Convert"/>. <c>GetCharCount</c> does
    /// not retain incomplete multi-byte sequences across calls, so a 3-byte name that
    /// straddles the 4 KiB window (for example U+202E) would false-reject as invalid
    /// UTF-8 and skip the retained-text budget. Gated by
    /// <c>GetStringCharacterCount_MultiByteUtf8AcrossWindow_MatchesGetString</c> and
    /// <c>ExpandingMethodName_IsStoppedBeforeContainedSpellingMaterialization</c>.
    /// </remarks>
    public static long GetStringCharacterCount(
        MetadataReader reader,
        StringHandle handle)
    {
        var blob = reader.GetBlobReader(handle);
        if (blob.Length == 0)
            return 0;

        const int Window = 4096;
        byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(1, Math.Min(blob.Length, Window)));
        char[] charBuffer = ArrayPool<char>.Shared.Rent(Window);
        try
        {
            Decoder decoder = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetDecoder();
            long characters = 0;
            while (blob.RemainingBytes > 0)
            {
                int byteCount = Math.Min(blob.RemainingBytes, byteBuffer.Length);
                blob.ReadBytes(byteCount, byteBuffer, 0);
                bool flush = blob.RemainingBytes == 0;
                int byteOffset = 0;
                bool completed;
                do
                {
                    decoder.Convert(
                        byteBuffer,
                        byteOffset,
                        byteCount - byteOffset,
                        charBuffer,
                        0,
                        charBuffer.Length,
                        flush,
                        out int bytesUsed,
                        out int charsUsed,
                        out completed);
                    characters += charsUsed;
                    byteOffset += bytesUsed;
                }
                while (!completed);
            }
            return characters;
        }
        catch (DecoderFallbackException ex)
        {
            throw new BadImageFormatException(
                "The metadata string contains invalid UTF-8.",
                ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
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
