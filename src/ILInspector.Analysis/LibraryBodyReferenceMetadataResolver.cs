using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Owns cross-assembly type-definition resolution and acquired metadata for one
/// library-body analysis.
/// </summary>
internal sealed class LibraryBodyReferenceMetadataResolver : IDisposable
{
    readonly MetadataReader _reader;
    readonly TypeResolutionCatalog? _resolutionCatalog;
    readonly AssemblyReferenceBindingPolicy? _bindingPolicy;
    readonly ResolvedAssemblyReference? _rootAssembly;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ReferencedAssemblyMetadata?> _referencedAssemblyCache =
            new(ReferenceEqualityComparer.Instance);

    internal LibraryBodyReferenceMetadataResolver(
        string path,
        MetadataReader reader,
        IAssemblyReferenceResolver? resolver,
        ImmutableArray<byte> rootImage)
    {
        _reader = reader;
        if (resolver is not null && reader.IsAssembly)
        {
            string fullPath = Path.GetFullPath(path);
            byte[]? bytes = rootImage.IsDefault
                ? null
                : ImmutableCollectionsMarshal.AsArray(rootImage);
            _rootAssembly = ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                fullPath,
                bytes is null
                    ? () => File.OpenRead(fullPath)
                    : () => new MemoryStream(
                        bytes,
                        writable: false),
                AssemblyResolutionProvenance.Local(
                    "LibraryBodyIndex"));
            _bindingPolicy =
                new AssemblyReferenceBindingPolicy(resolver);
            _resolutionCatalog = new TypeResolutionCatalog();
        }
    }

    public void Dispose()
    {
        foreach (var assembly in _referencedAssemblyCache.Values)
            assembly?.Dispose();
        _referencedAssemblyCache.Clear();
        _resolutionCatalog?.Dispose();
    }

    internal (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(TypeReferenceHandle handle) =>
        TryResolveExternalTypeDefinition(
            handle,
            new HashSet<TypeReferenceHandle>());

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(
            TypeReferenceHandle handle,
            HashSet<TypeReferenceHandle> visited)
    {
        if (handle.IsNil || !visited.Add(handle))
            return null;

        var typeRef = _reader.GetTypeReference(handle);
        string name = _reader.GetString(typeRef.Name);
        string ns = _reader.GetString(typeRef.Namespace);
        return typeRef.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => TryResolveTopLevelExternalType(
                (AssemblyReferenceHandle)typeRef.ResolutionScope,
                ns,
                name),
            HandleKind.TypeReference => TryResolveNestedExternalType(
                (TypeReferenceHandle)typeRef.ResolutionScope,
                ns,
                name,
                visited),
            _ => null,
        };
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveTopLevelExternalType(
            AssemblyReferenceHandle assemblyReference,
            string ns,
            string name)
    {
        if (_resolutionCatalog is null
            || _bindingPolicy is null
            || _rootAssembly is null
            || MetadataTypeDefinitionName.Create(ns, [name])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return null;
        }

        var identity =
            AssemblyReferenceIdentity.From(
                _reader,
                assemblyReference);
        var scope = ScopeForReference(assemblyReference);
        var request = TypeResolutionRequest.FromReference(
            identity,
            AssemblyBindingOrigin.FromAssembly(_rootAssembly),
            scope,
            valid.Name);
        using TypeResolutionContext context =
            _resolutionCatalog.CreateContext(
                _bindingPolicy,
                [_rootAssembly],
                [request]);
        if (context.Resolve(request)
            is not TypeResolutionOutcome.Resolved resolved)
        {
            return null;
        }

        ReferencedAssemblyMetadata? metadata =
            OpenReferencedAssembly(
                resolved.Definition.Assembly.Assembly);
        return metadata is not null
            && resolved.Definition.Address.TryResolve(
                metadata.Reader,
                out TypeDefinitionHandle definition)
                ? (metadata.Reader, definition)
                : null;
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveNestedExternalType(
            TypeReferenceHandle declaringReference,
            string ns,
            string name,
            HashSet<TypeReferenceHandle> visited)
    {
        var declaring =
            TryResolveExternalTypeDefinition(
                declaringReference,
                visited);
        if (declaring is not { } resolvedDeclaring)
            return null;

        var declaringDefinition =
            resolvedDeclaring.DefiningReader.GetTypeDefinition(
                resolvedDeclaring.Definition);
        foreach (var nestedHandle
            in declaringDefinition.GetNestedTypes())
        {
            var nested =
                resolvedDeclaring.DefiningReader.GetTypeDefinition(
                    nestedHandle);
            if ((ns.Length == 0
                    || resolvedDeclaring.DefiningReader.StringComparer.Equals(
                        nested.Namespace,
                        ns))
                && resolvedDeclaring.DefiningReader.StringComparer.Equals(
                    nested.Name,
                    name))
            {
                return (
                    resolvedDeclaring.DefiningReader,
                    nestedHandle);
            }
        }

        return null;
    }

    ReferencedAssemblyMetadata? OpenReferencedAssembly(
        ResolvedAssemblyReference assembly)
    {
        lock (_referencedAssemblyCache)
        {
            if (_referencedAssemblyCache.TryGetValue(
                    assembly.Registration,
                    out ReferencedAssemblyMetadata? cached))
            {
                return cached;
            }

            ReferencedAssemblyMetadata? opened =
                ReferencedAssemblyMetadata.TryOpen(assembly);
            _referencedAssemblyCache[assembly.Registration] = opened;
            return opened;
        }
    }

    AssemblyResolutionScope ScopeForReference(
        AssemblyReferenceHandle handle) =>
        FrameworkAssemblyKeys.IsFrameworkReference(_reader, handle)
            ? AssemblyResolutionScope.Platform
            : AssemblyResolutionScope.Any;

    sealed class ReferencedAssemblyMetadata(
        Stream stream,
        PEReader peReader) : IDisposable
    {
        public MetadataReader Reader { get; } =
            peReader.GetMetadataReader();

        internal static ReferencedAssemblyMetadata? TryOpen(
            ResolvedAssemblyReference assembly)
        {
            Stream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = assembly.OpenRead();
                peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return null;
                var metadata =
                    new ReferencedAssemblyMetadata(
                        stream,
                        peReader);
                stream = null;
                peReader = null;
                return metadata;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
            {
                return null;
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }

        public void Dispose()
        {
            peReader.Dispose();
            stream.Dispose();
        }
    }
}
