using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Why a structured metadata type-definition name could not be created.</summary>
public enum MetadataTypeNameRejectionKind
{
    MissingNamespace,
    MissingSegments,
    MissingSegment,
}

/// <summary>Typed evidence for a rejected structured metadata type-definition name.</summary>
public sealed record MetadataTypeNameRejection(
    MetadataTypeNameRejectionKind Kind,
    int? SegmentIndex = null);

/// <summary>The result of validating a structured metadata type-definition name.</summary>
public abstract class MetadataTypeDefinitionNameResult
{
    private protected MetadataTypeDefinitionNameResult()
    {
    }

    public sealed class Valid : MetadataTypeDefinitionNameResult
    {
        internal Valid(MetadataTypeDefinitionName name) => Name = name;

        public MetadataTypeDefinitionName Name { get; }
    }

    public sealed class Rejected : MetadataTypeDefinitionNameResult
    {
        internal Rejected(MetadataTypeNameRejection rejection) =>
            Rejection = rejection;

        public MetadataTypeNameRejection Rejection { get; }
    }
}

/// <summary>
/// An exact reader-independent metadata lookup name: namespace plus
/// root-to-leaf metadata-name segments, including generic arity.
/// </summary>
public sealed class MetadataTypeDefinitionName : IEquatable<MetadataTypeDefinitionName>
{
    readonly int hashCode;

    MetadataTypeDefinitionName(string @namespace, ImmutableArray<string> segments)
    {
        Namespace = @namespace;
        Segments = segments;

        var hash = new HashCode();
        hash.Add(@namespace, StringComparer.Ordinal);
        foreach (string segment in segments)
            hash.Add(segment, StringComparer.Ordinal);
        hashCode = hash.ToHashCode();
    }

    public string Namespace { get; }
    public ImmutableArray<string> Segments { get; }

    public static MetadataTypeDefinitionNameResult Create(
        string? @namespace,
        ImmutableArray<string> segments)
    {
        if (@namespace is null)
        {
            return new MetadataTypeDefinitionNameResult.Rejected(
                new MetadataTypeNameRejection(
                    MetadataTypeNameRejectionKind.MissingNamespace));
        }

        if (segments.IsDefaultOrEmpty)
        {
            return new MetadataTypeDefinitionNameResult.Rejected(
                new MetadataTypeNameRejection(
                    MetadataTypeNameRejectionKind.MissingSegments));
        }

        for (int i = 0; i < segments.Length; i++)
        {
            if (string.IsNullOrEmpty(segments[i]))
            {
                return new MetadataTypeDefinitionNameResult.Rejected(
                    new MetadataTypeNameRejection(
                        MetadataTypeNameRejectionKind.MissingSegment,
                        i));
            }
        }

        return new MetadataTypeDefinitionNameResult.Valid(
            new MetadataTypeDefinitionName(@namespace, segments));
    }

    public bool Equals(MetadataTypeDefinitionName? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !StringComparer.Ordinal.Equals(Namespace, other.Namespace)
            || Segments.Length != other.Segments.Length)
        {
            return false;
        }

        for (int i = 0; i < Segments.Length; i++)
        {
            if (!StringComparer.Ordinal.Equals(Segments[i], other.Segments[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is MetadataTypeDefinitionName other && Equals(other);

    public override int GetHashCode() => hashCode;

    public static bool operator ==(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        EqualityComparer<MetadataTypeDefinitionName>.Default.Equals(left, right);

    public static bool operator !=(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        !(left == right);
}

internal abstract record MetadataTypeDefinitionNameReadResult
{
    private protected MetadataTypeDefinitionNameReadResult()
    {
    }

    internal sealed record Read(MetadataTypeDefinitionName Name) :
        MetadataTypeDefinitionNameReadResult;

    internal sealed record Rejected(MetadataTypeNameFailure Failure) :
        MetadataTypeDefinitionNameReadResult;
}

internal enum MetadataTypeDefinitionNameMatch
{
    NoMatch,
    Match,
    Rejected,
}

internal static class MetadataTypeDefinitionNameReader
{
    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return RejectedTraversal(rejection!);
        }

        return ReadChain<TypeDefinitionHandle, TypeDefinitionNameRow>(
            reader,
            rootToLeaf[..consumedNodes]);
    }

    internal static MetadataTypeDefinitionNameMatch Matches(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
    {
        if (!LeafMatches<TypeDefinitionHandle, TypeDefinitionNameRow>(
                reader,
                handle,
                name,
                out MetadataTypeDefinitionNameMatch leafResult,
                out failure))
        {
            return leafResult;
        }

        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            failure = MetadataTypeNameFailure.From(rejection!);
            return MetadataTypeDefinitionNameMatch.Rejected;
        }

        return MatchChain<TypeDefinitionHandle, TypeDefinitionNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            name,
            out failure);
    }

    internal static MetadataTypeDefinitionNameMatch Matches(
        MetadataReader reader,
        ExportedTypeHandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
    {
        if (!LeafMatches<ExportedTypeHandle, ExportedTypeNameRow>(
                reader,
                handle,
                name,
                out MetadataTypeDefinitionNameMatch leafResult,
                out failure))
        {
            return leafResult;
        }

        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkExportedTypeImplementationChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            failure = MetadataTypeNameFailure.From(rejection!);
            return MetadataTypeDefinitionNameMatch.Rejected;
        }

        return MatchChain<ExportedTypeHandle, ExportedTypeNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            name,
            out failure);
    }

    static bool LeafMatches<THandle, TRow>(
        MetadataReader reader,
        THandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeDefinitionNameMatch result,
        out MetadataTypeNameFailure? failure)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        failure = null;
        try
        {
            var (_, leafName) = TRow.GetName(reader, handle);
            if (!reader.StringComparer.Equals(leafName, name.Segments[^1]))
            {
                result = MetadataTypeDefinitionNameMatch.NoMatch;
                return false;
            }

            result = MetadataTypeDefinitionNameMatch.Match;
            return true;
        }
        catch (BadImageFormatException ex)
        {
            failure = RelationshipFailure(ex, TRow.ToEntity(handle), consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            failure = RelationshipFailure(ex, TRow.ToEntity(handle), consumedNodes: 1);
        }

        result = MetadataTypeDefinitionNameMatch.Rejected;
        return false;
    }

    static MetadataTypeDefinitionNameMatch MatchChain<THandle, TRow>(
        MetadataReader reader,
        ReadOnlySpan<THandle> rootToLeaf,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        failure = null;
        if (rootToLeaf.Length != name.Segments.Length)
            return MetadataTypeDefinitionNameMatch.NoMatch;

        for (int i = 0; i < rootToLeaf.Length; i++)
        {
            try
            {
                var (namespaceHandle, nameHandle) =
                    TRow.GetName(reader, rootToLeaf[i]);
                if (i == 0
                    && !reader.StringComparer.Equals(namespaceHandle, name.Namespace))
                {
                    return MetadataTypeDefinitionNameMatch.NoMatch;
                }

                if (!reader.StringComparer.Equals(nameHandle, name.Segments[i]))
                    return MetadataTypeDefinitionNameMatch.NoMatch;
            }
            catch (BadImageFormatException ex)
            {
                failure = RelationshipFailure(
                    ex,
                    TRow.ToEntity(rootToLeaf[i]),
                    i + 1);
                return MetadataTypeDefinitionNameMatch.Rejected;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                failure = RelationshipFailure(
                    ex,
                    TRow.ToEntity(rootToLeaf[i]),
                    i + 1);
                return MetadataTypeDefinitionNameMatch.Rejected;
            }
        }

        return MetadataTypeDefinitionNameMatch.Match;
    }

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> rootToLeaf =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return RejectedTraversal(rejection!);
        }

        return ReadChain<TypeReferenceHandle, TypeReferenceNameRow>(
            reader,
            rootToLeaf[..consumedNodes]);
    }

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        ExportedTypeHandle handle)
    {
        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkExportedTypeImplementationChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return RejectedTraversal(rejection!);
        }

        return ReadChain<ExportedTypeHandle, ExportedTypeNameRow>(
            reader,
            rootToLeaf[..consumedNodes]);
    }

    static MetadataTypeDefinitionNameReadResult ReadChain<THandle, TRow>(
        MetadataReader reader,
        ReadOnlySpan<THandle> rootToLeaf)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        var segments = ImmutableArray.CreateBuilder<string>(rootToLeaf.Length);
        string? @namespace = null;

        for (int i = 0; i < rootToLeaf.Length; i++)
        {
            THandle handle = rootToLeaf[i];
            try
            {
                var (namespaceHandle, nameHandle) = TRow.GetName(reader, handle);
                if (i == 0)
                    @namespace = reader.GetString(namespaceHandle);
                segments.Add(reader.GetString(nameHandle));
            }
            catch (BadImageFormatException ex)
            {
                return Malformed(ex, TRow.ToEntity(handle), i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed(ex, TRow.ToEntity(handle), i + 1);
            }
        }

        MetadataTypeDefinitionNameResult created =
            MetadataTypeDefinitionName.Create(@namespace, segments.ToImmutable());
        if (created is MetadataTypeDefinitionNameResult.Valid valid)
            return new MetadataTypeDefinitionNameReadResult.Read(valid.Name);

        MetadataTypeNameRejection invalid =
            ((MetadataTypeDefinitionNameResult.Rejected)created).Rejection;
        EntityHandle subject =
            TRow.ToEntity(rootToLeaf[invalid.SegmentIndex ?? 0]);
        return new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.Malformed(
                subject,
                $"Invalid structured metadata type name: {invalid.Kind}."));
    }

    static MetadataTypeDefinitionNameReadResult RejectedTraversal(
        RelationshipTraversalRejection rejection) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.From(rejection));

    static MetadataTypeDefinitionNameReadResult Malformed(
        Exception exception,
        EntityHandle subject,
        int consumedNodes) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            RelationshipFailure(exception, subject, consumedNodes));

    static MetadataTypeNameFailure RelationshipFailure(
        Exception exception,
        EntityHandle subject,
        int consumedNodes) =>
        MetadataTypeNameFailure.From(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                exception.Message,
                subject,
                consumedNodes));

    interface IMetadataTypeNameRow<THandle>
        where THandle : struct
    {
        static abstract (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            THandle handle);

        static abstract EntityHandle ToEntity(THandle handle);
    }

    readonly struct TypeDefinitionNameRow :
        IMetadataTypeNameRow<TypeDefinitionHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            return (definition.Namespace, definition.Name);
        }

        public static EntityHandle ToEntity(TypeDefinitionHandle handle) => handle;
    }

    readonly struct ExportedTypeNameRow :
        IMetadataTypeNameRow<ExportedTypeHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            ExportedTypeHandle handle)
        {
            ExportedType exported = reader.GetExportedType(handle);
            return (exported.Namespace, exported.Name);
        }

        public static EntityHandle ToEntity(ExportedTypeHandle handle) => handle;
    }

    readonly struct TypeReferenceNameRow :
        IMetadataTypeNameRow<TypeReferenceHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            return (reference.Namespace, reference.Name);
        }

        public static EntityHandle ToEntity(TypeReferenceHandle handle) => handle;
    }
}
