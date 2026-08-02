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

internal static class MetadataTypeDefinitionNameReader
{
    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeDefinitionHandle handle) =>
        ReadChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(reader, handle),
            current =>
            {
                TypeDefinition definition = reader.GetTypeDefinition(current);
                return (definition.Namespace, definition.Name);
            },
            static current => current);

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeReferenceHandle handle) =>
        ReadChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(reader, handle),
            current =>
            {
                TypeReference reference = reader.GetTypeReference(current);
                return (reference.Namespace, reference.Name);
            },
            static current => current);

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        ExportedTypeHandle handle) =>
        ReadChain(
            reader,
            MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(reader, handle),
            current =>
            {
                ExportedType exported = reader.GetExportedType(current);
                return (exported.Namespace, exported.Name);
            },
            static current => current);

    static MetadataTypeDefinitionNameReadResult ReadChain<THandle>(
        MetadataReader reader,
        RelationshipTraversalResult<RelationshipChain<THandle>> traversal,
        Func<THandle, (StringHandle Namespace, StringHandle Name)> getName,
        Func<THandle, EntityHandle> getSubject)
        where THandle : struct
    {
        if (traversal is RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected rejected)
        {
            return new MetadataTypeDefinitionNameReadResult.Rejected(
                MetadataTypeNameFailure.From(rejected.Rejection));
        }

        RelationshipChain<THandle> chain =
            ((RelationshipTraversalResult<RelationshipChain<THandle>>.Completed)traversal).Value;
        var segments = ImmutableArray.CreateBuilder<string>(chain.Handles.Length);
        string? @namespace = null;

        for (int i = 0; i < chain.Handles.Length; i++)
        {
            THandle handle = chain.Handles[i];
            try
            {
                var (namespaceHandle, nameHandle) = getName(handle);
                if (i == 0)
                    @namespace = reader.GetString(namespaceHandle);
                segments.Add(reader.GetString(nameHandle));
            }
            catch (BadImageFormatException ex)
            {
                return Malformed(ex, getSubject(handle), i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed(ex, getSubject(handle), i + 1);
            }
        }

        MetadataTypeDefinitionNameResult created =
            MetadataTypeDefinitionName.Create(@namespace, segments.ToImmutable());
        if (created is MetadataTypeDefinitionNameResult.Valid valid)
            return new MetadataTypeDefinitionNameReadResult.Read(valid.Name);

        MetadataTypeNameRejection invalid =
            ((MetadataTypeDefinitionNameResult.Rejected)created).Rejection;
        EntityHandle subject = getSubject(
            chain.Handles[invalid.SegmentIndex ?? 0]);
        return new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.Malformed(
                subject,
                $"Invalid structured metadata type name: {invalid.Kind}."));
    }

    static MetadataTypeDefinitionNameReadResult Malformed(
        Exception exception,
        EntityHandle subject,
        int consumedNodes) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.From(
                new RelationshipTraversalRejection(
                    RelationshipTraversalRejectionKind.MalformedMetadata,
                    exception.Message,
                    subject,
                    consumedNodes)));
}
