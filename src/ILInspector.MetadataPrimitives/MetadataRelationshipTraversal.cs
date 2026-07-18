using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Fixed safety ceilings for artifact-derived metadata operations.</summary>
public static class MetadataSafetyPolicy
{
    /// <summary>
    /// Maximum unique handles in one TypeDef, TypeRef, or ExportedType
    /// relationship chain.
    /// </summary>
    public const int MaxRelationshipNodes = 256;
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
