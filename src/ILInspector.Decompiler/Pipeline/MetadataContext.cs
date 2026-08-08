using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using System.Collections.Concurrent;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The multi-assembly resolution environment a decompile session reads through:
/// an <see cref="IAssemblyReferenceResolver"/> plus a pool of assemblies opened on demand.
/// Where <see cref="MetadataSource"/> owns the readers for the ONE assembly being
/// decompiled, this owns the readers for every OTHER assembly consulted while
/// recovering cross-assembly facts (value-type-ness of a bare token, interface
/// membership for exact raise gates, and method facts such as by-ref call-site
/// spelling).
/// </summary>
/// <remarks>
/// Each defining assembly is opened at most once: <see cref="Open"/> caches the
/// <see cref="OpenedAssembly"/> by acquisition registration (failures cached as
/// null so a bad descriptor is not retried) and keeps its <see cref="PEReader"/> live for the context's
/// lifetime, so repeated lookups are O(1) with no re-open. A
/// <see cref="MetadataReader"/> is only valid while its <see cref="PEReader"/> is
/// alive, which is why this is <see cref="IDisposable"/>.
///
/// Lifetime is the caller's choice. A single-assembly consumer lets
/// <see cref="MetadataSource"/> create and own one implicitly. A batch consumer
/// that opens many assemblies in a loop (the corpus sweep) should create ONE
/// context outside the loop and pass it to each <see cref="MetadataSource.Open(string, string?, MetadataContext?)"/>
/// so a shared dependency such as CoreLib is opened once for the whole run rather
/// than once per assembly.
/// </remarks>
public sealed class MetadataContext : IDisposable
{
    static readonly AssemblyReferenceIdentity[] s_coreLibraryCandidates =
    [
        new(
            "System.Private.CoreLib",
            Version: null,
            Culture: null,
            PublicKeyToken: null),
        new(
            "System.Runtime",
            Version: null,
            Culture: null,
            PublicKeyToken: null),
        new(
            "mscorlib",
            Version: null,
            Culture: null,
            PublicKeyToken: null),
        new(
            "netstandard",
            Version: null,
            Culture: null,
            PublicKeyToken: null),
    ];

    readonly ConcurrentDictionary<string, Lazy<OpenedAssembly?>> _opened = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, Lazy<OpenedAssembly?>> _openedLocations = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<
        AssemblyAcquisitionRegistration,
        Lazy<OpenedAssembly?>> _openedRegistrations =
            new(ReferenceEqualityComparer.Instance);
    readonly TypeResolutionCatalog _typeResolutionCatalog = new();
    readonly AssemblyReferenceBindingPolicy _bindingPolicy;

    public MetadataContext(IAssemblyReferenceResolver resolver)
    {
        Resolver = resolver;
        _bindingPolicy = new AssemblyReferenceBindingPolicy(resolver);
    }

    internal IAssemblyReferenceResolver Resolver { get; }

    /// <summary>
    /// Returns the cached reader for an on-disk assembly, opening it on first
    /// request. Returns null when the file is missing, carries no managed
    /// metadata, or cannot be read; the null is cached so the path is not
    /// retried.
    /// </summary>
    internal OpenedAssembly? Open(string path)
    {
        return _opened.GetOrAdd(path, p => new Lazy<OpenedAssembly?>(() => OpenedAssembly.TryOpen(p))).Value;
    }

    internal OpenedAssembly? Open(TypeLocation location)
    {
        if (location.AssemblyPath is { Length: > 0 } path)
            return Open(path);

        return _openedLocations.GetOrAdd(location.AssemblyKey, _ => new Lazy<OpenedAssembly?>(() => OpenedAssembly.TryOpen(location.OpenRead))).Value;
    }

    internal OpenedAssembly? Open(ResolvedAssemblyReference assembly)
        => _openedRegistrations.GetOrAdd(
            assembly.Registration,
            _ => new Lazy<OpenedAssembly?>(
                () => OpenedAssembly.TryOpen(assembly.OpenRead),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    internal OpenedAssembly? Open(
        ResolvedTypeDefinition definition,
        out TypeDefinitionHandle handle)
    {
        OpenedAssembly? assembly = Open(definition.Assembly.Assembly);
        if (assembly is null
            || !definition.Address.TryResolve(assembly.Reader, out handle))
        {
            handle = default;
            return null;
        }

        return assembly;
    }

    internal TypeResolutionOutcome Resolve(
        ResolvedAssemblyReference root,
        TypeResolutionRequest request)
    {
        using TypeResolutionContext context =
            _typeResolutionCatalog.CreateContext(
                _bindingPolicy,
                [root],
                [request]);
        return context.Resolve(request);
    }

    internal ResolvedTypeDefinition? ResolveCoreLibraryDefinition(
        ResolvedAssemblyReference root,
        MetadataTypeDefinitionName type)
    {
        // Pipeline.TypeRef historically canonicalized several facade identities
        // to "corelib", erasing which explicit AssemblyRef supplied the type.
        // Probe those legacy identities as structured reference requests and
        // continue when an earlier facade binds but does not declare the type.
        foreach (AssemblyReferenceIdentity identity in s_coreLibraryCandidates)
        {
            var request = TypeResolutionRequest.FromReference(
                identity,
                AssemblyBindingOrigin.FromAssembly(root),
                AssemblyResolutionScope.Platform,
                type);
            if (Resolve(root, request)
                is TypeResolutionOutcome.Resolved resolved)
            {
                return resolved.Definition;
            }
        }

        return null;
    }

    internal ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        => Resolver.Resolve(identity, scope);

    public void Dispose()
    {
        foreach (var opened in _opened.Values)
            if (opened.IsValueCreated) opened.Value?.Dispose();
        foreach (var opened in _openedLocations.Values)
            if (opened.IsValueCreated) opened.Value?.Dispose();
        foreach (var opened in _openedRegistrations.Values)
            if (opened.IsValueCreated) opened.Value?.Dispose();
        _opened.Clear();
        _openedLocations.Clear();
        _openedRegistrations.Clear();
        _typeResolutionCatalog.Dispose();
    }
}

/// <summary>
/// A live PE/metadata reader for one referenced assembly, with a lazily built
/// full-name → <see cref="TypeDefinitionHandle"/> index so a type is found in a
/// single table pass and located in O(1) thereafter. Owned and disposed by the
/// <see cref="MetadataContext"/> that opened it.
/// </summary>
internal sealed class OpenedAssembly : IDisposable
{
    readonly Stream _stream;
    readonly PEReader _pe;
    volatile Dictionary<string, TypeDefinitionHandle>? _byFullName;
    readonly object _indexLock = new();

    OpenedAssembly(Stream stream, PEReader pe, MetadataReader reader)
    {
        _stream = stream;
        _pe = pe;
        Reader = reader;
    }

    public MetadataReader Reader { get; }

    /// <summary>
    /// Opens an assembly for reading, or returns null when the file is missing,
    /// carries no managed metadata, or cannot be read.
    /// </summary>
    public static OpenedAssembly? TryOpen(string path)
        => TryOpen(() => File.OpenRead(path));

    public static OpenedAssembly? TryOpen(Func<Stream> openRead)
    {
        Stream? stream = null;
        PEReader? pe = null;
        try
        {
            stream = openRead();
            pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                pe.Dispose();
                stream.Dispose();
                return null;
            }
            return new OpenedAssembly(stream, pe, pe.GetMetadataReader());
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            pe?.Dispose();
            stream?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Finds a top-level type by its full metadata name, building the
    /// full-name → handle index over the type table on first call. First
    /// definition wins, matching a linear first-match scan.
    /// </summary>
    public bool TryGetType(string fullName, out TypeDefinitionHandle handle)
    {
        if (_byFullName is null)
        {
            lock (_indexLock)
            {
                _byFullName ??= BuildIndex();
            }
        }
        return _byFullName.TryGetValue(fullName, out handle);
    }

    Dictionary<string, TypeDefinitionHandle> BuildIndex()
    {
        var map = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var handle in Reader.TypeDefinitions)
            map.TryAdd(Reader.GetFullTypeName(Reader.GetTypeDefinition(handle)), handle);
        return map;
    }

    public void Dispose()
    {
        _pe.Dispose();
        _stream.Dispose();
    }
}
