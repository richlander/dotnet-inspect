using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using System.Collections.Concurrent;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The multi-assembly resolution environment a decompile session reads through:
/// an <see cref="IAssemblyBindingPolicy"/> plus a pool of assemblies opened on demand.
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
    readonly ConcurrentDictionary<
        AssemblyAcquisitionRegistration,
        Lazy<OpenedAssembly?>> _openedRegistrations =
            new(ReferenceEqualityComparer.Instance);
    readonly object _typeResolutionGenerationGate = new();
    readonly TypeResolutionCatalog _typeResolutionCatalog = new();
    readonly IAssemblyBindingPolicy _bindingPolicy;
    readonly IAssemblyReferenceResolver? _resolver;

    public MetadataContext(IAssemblyReferenceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _bindingPolicy = new AssemblyReferenceBindingPolicy(resolver);
    }

    /// <summary>
    /// Creates a decompiler metadata context over an existing immutable
    /// assembly-binding policy.
    /// </summary>
    internal MetadataContext(IAssemblyBindingPolicy bindingPolicy)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        _bindingPolicy = bindingPolicy;
    }

    internal IAssemblyReferenceResolver Resolver =>
        _resolver
        ?? throw new InvalidOperationException(
            "This metadata context uses an assembly binding policy directly.");

    /// <summary>
    /// Returns the cached reader for an on-disk assembly, opening it on first
    /// request. Returns null when the file is missing, carries no managed
    /// metadata, or cannot be read; the null is cached so the path is not
    /// retried.
    /// </summary>
    internal OpenedAssembly? Open(string path)
    {
        return _opened.GetOrAdd(path, p => new Lazy<OpenedAssembly?>(() => OpenDesignated(p))).Value;
    }

    /// <summary>
    /// Opens a path the caller named directly. Naming an exact file is a
    /// designation, so the result keeps core-library identity; see
    /// <see cref="CoreLibraryIdentityTrust"/>. The designation is stated as
    /// provenance and answered by the rule rather than granted directly, so
    /// this site is entitled for a reason the rule can be asked about.
    /// </summary>
    static OpenedAssembly? OpenDesignated(string path)
    {
        OpenedAssembly? opened = OpenedAssembly.TryOpen(path);
        if (opened is not null)
        {
            CoreLibraryIdentityTrust.GrantIfEntitled(
                opened.Reader,
                AssemblyResolutionProvenance.Designated("MetadataContext designation"));
        }
        return opened;
    }

    internal OpenedAssembly? Open(ResolvedAssemblyReference assembly)
        => _openedRegistrations.GetOrAdd(
            assembly.Registration,
            _ => new Lazy<OpenedAssembly?>(
                () => OpenResolved(assembly),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>
    /// Opens an assembly that reference resolution selected, and records
    /// whether its acquisition entitles it to core-library identity. Discovery
    /// is trusted by provenance alone; see
    /// <see cref="CoreLibraryIdentityTrust"/>.
    /// </summary>
    OpenedAssembly? OpenResolved(ResolvedAssemblyReference assembly)
    {
        OpenedAssembly? opened = OpenedAssembly.TryOpen(assembly.OpenRead);
        if (opened is not null)
        {
            CoreLibraryIdentityTrust.GrantIfEntitled(
                opened.Reader,
                assembly.Provenance);
        }
        return opened;
    }

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
        lock (_typeResolutionGenerationGate)
        {
            using TypeResolutionContext context =
                _typeResolutionCatalog.CreateContext(
                    _bindingPolicy,
                    [root],
                    [request]);
            return context.Resolve(request);
        }
    }

    /// <summary>
    /// Resolves both requests in one frozen catalog generation and accepts only
    /// exact catalog-issued correspondence. Duplicate artifacts remain
    /// indeterminate and therefore do not compare equal.
    /// <c>ConcurrentResolution_DoesNotInvalidateDefinitionCorrespondence</c>
    /// and <c>DistinctSignatureTypeDefinitions_DoNotCorrespond</c> gate both
    /// boundaries.
    /// </summary>
    internal bool ResolveToSameDefinition(
        ResolvedAssemblyReference leftRoot,
        TypeResolutionRequest leftRequest,
        ResolvedAssemblyReference rightRoot,
        TypeResolutionRequest rightRequest)
    {
        lock (_typeResolutionGenerationGate)
        {
            using TypeResolutionContext context =
                _typeResolutionCatalog.CreateContext(
                    _bindingPolicy,
                    [leftRoot, rightRoot],
                    [leftRequest, rightRequest]);
            TypeResolutionOutcome leftOutcome =
                context.Resolve(leftRequest);
            TypeResolutionOutcome rightOutcome =
                context.Resolve(rightRequest);
            return leftOutcome is TypeResolutionOutcome.Resolved left
                && rightOutcome is TypeResolutionOutcome.Resolved right
                && _typeResolutionCatalog.Compare(
                    left.Definition.Key,
                    right.Definition.Key)
                    is DefinitionCorrespondence.Same;
        }
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

    public void Dispose()
    {
        foreach (var opened in _opened.Values)
            if (opened.IsValueCreated) opened.Value?.Dispose();
        foreach (var opened in _openedRegistrations.Values)
            if (opened.IsValueCreated) opened.Value?.Dispose();
        _opened.Clear();
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
        bool unavailableEstablished = false;
        try
        {
            stream = openRead();
            pe = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);
            if (!MetadataFormatAdmission.AdmitImage(pe))
            {
                unavailableEstablished = true;
                return null;
            }
            OpenedAssembly result = new(
                stream,
                pe,
                MetadataFormatAdmission.GetMetadataReader(pe));
            stream = null;
            pe = null;
            return result;
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            DisposeAfterFailure(ref pe, ref stream, ex);
            throw;
        }
        catch (MalformedMetadataRootException ex)
        {
            DisposeAfterFailure(ref pe, ref stream, ex);
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or BadImageFormatException
                or UnauthorizedAccessException)
        {
            DisposeAfterFailure(ref pe, ref stream, ex);
            return null;
        }
        catch (Exception ex)
        {
            DisposeAfterFailure(ref pe, ref stream, ex);
            throw;
        }
        finally
        {
            if (unavailableEstablished)
                DisposeWithoutReplacingOutcome(ref pe, ref stream);
            else
            {
                pe?.Dispose();
                stream?.Dispose();
            }
        }
    }

    static void DisposeAfterFailure(
        ref PEReader? pe,
        ref Stream? stream,
        Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        DisposeWithoutReplacingOutcome(ref pe, ref stream);
    }

    static void DisposeWithoutReplacingOutcome(
        ref PEReader? pe,
        ref Stream? stream)
    {
        try
        {
            pe?.Dispose();
        }
        catch
        {
        }
        pe = null;

        try
        {
            stream?.Dispose();
        }
        catch
        {
        }
        stream = null;
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
