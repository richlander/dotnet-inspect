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
        => Walk(
            handle,
            HandleKind.TypeDefinition,
            static entity => (TypeDefinitionHandle)entity,
            current => reader.GetTypeDefinition(current).GetDeclaringType());

    /// <summary>Walks a TypeRef resolution-scope chain from its outermost type to the requested leaf.</summary>
    public static RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>
        WalkTypeReferenceResolutionScope(
            MetadataReader reader,
            TypeReferenceHandle handle)
        => Walk(
            handle,
            HandleKind.TypeReference,
            static entity => (TypeReferenceHandle)entity,
            current => reader.GetTypeReference(current).ResolutionScope);

    /// <summary>Walks an ExportedType implementation chain from its outermost type to the requested leaf.</summary>
    public static RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>
        WalkExportedTypeImplementationChain(
            MetadataReader reader,
            ExportedTypeHandle handle)
        => Walk(
            handle,
            HandleKind.ExportedType,
            static entity => (ExportedTypeHandle)entity,
            current => reader.GetExportedType(current).Implementation);

    static RelationshipTraversalResult<RelationshipChain<THandle>> Walk<THandle>(
        EntityHandle start,
        HandleKind relationshipKind,
        Func<EntityHandle, THandle> convert,
        Func<THandle, EntityHandle> next)
        where THandle : struct
    {
        if (start.IsNil)
        {
            return Reject<THandle>(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                "The relationship starts with a nil handle.",
                start,
                consumedNodes: 0);
        }

        THandle firstHandle;
        EntityHandle current;
        try
        {
            firstHandle = convert(start);
            current = next(firstHandle);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<THandle>(ex, start, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<THandle>(ex, start, consumedNodes: 1);
        }

        if (current.IsNil || current.Kind != relationshipKind)
        {
            return new RelationshipTraversalResult<RelationshipChain<THandle>>.Completed(
                new RelationshipChain<THandle>([firstHandle], current),
                consumedNodes: 1);
        }

        var visited = new HashSet<EntityHandle> { start };
        var leafToRoot = ImmutableArray.CreateBuilder<THandle>();
        leafToRoot.Add(firstHandle);

        while (!current.IsNil && current.Kind == relationshipKind)
        {
            if (!visited.Add(current))
            {
                return Reject<THandle>(
                    RelationshipTraversalRejectionKind.Cycle,
                    $"The {relationshipKind} relationship repeats handle {FormatHandle(current)}.",
                    current,
                    leafToRoot.Count);
            }

            if (leafToRoot.Count >= MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                return Reject<THandle>(
                    RelationshipTraversalRejectionKind.NodeBudget,
                    $"The {relationshipKind} relationship exceeds "
                    + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes.",
                    current,
                    leafToRoot.Count);
            }

            try
            {
                var typedHandle = convert(current);
                leafToRoot.Add(typedHandle);
                current = next(typedHandle);
            }
            catch (BadImageFormatException ex)
            {
                return Malformed<THandle>(ex, current, leafToRoot.Count);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed<THandle>(ex, current, leafToRoot.Count);
            }
        }

        var rootToLeaf = ImmutableArray.CreateBuilder<THandle>(leafToRoot.Count);
        for (int i = leafToRoot.Count - 1; i >= 0; i--)
            rootToLeaf.Add(leafToRoot[i]);

        return new RelationshipTraversalResult<RelationshipChain<THandle>>.Completed(
            new RelationshipChain<THandle>(rootToLeaf.MoveToImmutable(), current),
            leafToRoot.Count);
    }

    static RelationshipTraversalResult<RelationshipChain<THandle>> Malformed<THandle>(
        Exception exception,
        EntityHandle subject,
        int consumedNodes)
        where THandle : struct
        => Reject<THandle>(
            RelationshipTraversalRejectionKind.MalformedMetadata,
            exception.Message,
            subject,
            consumedNodes);

    static RelationshipTraversalResult<RelationshipChain<THandle>> Reject<THandle>(
        RelationshipTraversalRejectionKind kind,
        string detail,
        EntityHandle subject,
        int consumedNodes)
        where THandle : struct
        => new RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected(
            new RelationshipTraversalRejection(
                kind,
                detail,
                subject,
                consumedNodes));

    static string FormatHandle(EntityHandle handle)
        => $"0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8}";
}
