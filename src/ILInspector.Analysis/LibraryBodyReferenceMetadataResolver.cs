using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed record LibraryBodyRootSnapshot(
    ResolvedAssemblyReference Assembly,
    AssemblyImageSnapshot Snapshot);

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
        LibraryBodyRootSnapshot? rootSnapshot)
    {
        _reader = reader;
        if (resolver is not null && reader.IsAssembly)
        {
            if (rootSnapshot is not null)
            {
                _rootAssembly = rootSnapshot.Assembly;
            }
            else
            {
                string fullPath = Path.GetFullPath(path);
                _rootAssembly = ResolvedAssemblyReference.Create(
                    AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                    fullPath,
                    () => File.OpenRead(fullPath),
                    AssemblyResolutionProvenance.Local(
                        "LibraryBodyIndex"));
            }
            _bindingPolicy =
                new AssemblyReferenceBindingPolicy(resolver);
            _resolutionCatalog = new TypeResolutionCatalog();
            if (rootSnapshot is not null)
            {
                _resolutionCatalog.RegisterRetainedSnapshot(
                    rootSnapshot.Assembly,
                    rootSnapshot.Snapshot);
            }
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
        return TryResolveExternalTypeDefinition(
            identity,
            scope,
            valid.Name);
    }

    internal (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope,
            MetadataTypeDefinitionName type)
    {
        if (_resolutionCatalog is null
            || _bindingPolicy is null
            || _rootAssembly is null)
        {
            return null;
        }

        var request = TypeResolutionRequest.FromReference(
            identity,
            AssemblyBindingOrigin.FromAssembly(_rootAssembly),
            scope,
            type);
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
            OpenResolvedAssembly(
                context,
                resolved.Definition.Assembly);
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

    ReferencedAssemblyMetadata? OpenResolvedAssembly(
        TypeResolutionContext context,
        ResolvedAssemblyCandidate candidate)
    {
        lock (_referencedAssemblyCache)
        {
            if (_referencedAssemblyCache.TryGetValue(
                    candidate.Assembly.Registration,
                    out ReferencedAssemblyMetadata? cached))
            {
                return cached;
            }
        }

        ResolvedAssemblyReference? retained =
            context.RetainAssemblyReference(candidate);
        return retained is null
            ? null
            : OpenReferencedAssembly(retained);
    }

    AssemblyResolutionScope ScopeForReference(
        AssemblyReferenceHandle handle) =>
        FrameworkAssemblyKeys.IsFrameworkReference(_reader, handle)
            ? AssemblyResolutionScope.Platform
            : AssemblyResolutionScope.Any;

    internal sealed class ReferencedAssemblyMetadata(
        Stream stream,
        PEReader peReader) : IDisposable
    {
        public MetadataReader Reader { get; } =
            MetadataFormatAdmission.GetMetadataReader(peReader);

        internal static ReferencedAssemblyMetadata? TryOpen(
            ResolvedAssemblyReference assembly)
        {
            Stream? stream = null;
            PEReader? peReader = null;
            bool unavailableEstablished = false;
            try
            {
                stream = assembly.OpenRead();
                peReader = new PEReader(
                    stream,
                    PEStreamOptions.LeaveOpen);
                if (!MetadataFormatAdmission.AdmitImage(peReader))
                {
                    unavailableEstablished = true;
                    return null;
                }
                var metadata =
                    new ReferencedAssemblyMetadata(
                        stream,
                        peReader);
                stream = null;
                peReader = null;
                return metadata;
            }
            catch (UnsupportedMetadataFormatException ex)
            {
                AnalysisResourceCleanup.DisposeAfterFailure(
                    ref peReader,
                    ref stream,
                    ex);
                throw;
            }
            catch (MalformedMetadataRootException ex)
            {
                AnalysisResourceCleanup.DisposeAfterFailure(
                    ref peReader,
                    ref stream,
                    ex);
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
            {
                AnalysisResourceCleanup.DisposeAfterFailure(
                    ref peReader,
                    ref stream,
                    ex);
                return null;
            }
            catch (Exception ex)
            {
                AnalysisResourceCleanup.DisposeAfterFailure(
                    ref peReader,
                    ref stream,
                    ex);
                throw;
            }
            finally
            {
                if (unavailableEstablished)
                {
                    AnalysisResourceCleanup
                        .DisposeWithoutReplacingOutcome(
                            ref peReader,
                            ref stream);
                }
                else
                {
                    peReader?.Dispose();
                    stream?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            peReader.Dispose();
            stream.Dispose();
        }
    }
}
