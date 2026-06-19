using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The multi-assembly resolution environment a decompile session reads through:
/// an <see cref="AssemblyLocator"/> plus a pool of assemblies opened on demand.
/// Where <see cref="MetadataSource"/> owns the readers for the ONE assembly being
/// decompiled, this owns the readers for every OTHER assembly consulted while
/// recovering cross-assembly facts (the value-type-ness of a bare token today;
/// type identity and unsafe-callee bodies on the roadmap).
/// </summary>
/// <remarks>
/// Each defining assembly is opened at most once: <see cref="Open"/> caches the
/// <see cref="OpenedAssembly"/> by path (failures cached as null so a bad path is
/// not retried) and keeps its <see cref="PEReader"/> live for the context's
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
    readonly Dictionary<string, OpenedAssembly?> _opened = new(StringComparer.OrdinalIgnoreCase);

    public MetadataContext(AssemblyLocator locator) => Locator = locator;

    /// <summary>Resolves a referenced assembly name to an on-disk path.</summary>
    internal AssemblyLocator Locator { get; }

    /// <summary>
    /// Returns the cached reader for an on-disk assembly, opening it on first
    /// request. Returns null when the file is missing, carries no managed
    /// metadata, or cannot be read; the null is cached so the path is not
    /// retried.
    /// </summary>
    internal OpenedAssembly? Open(string path)
    {
        if (_opened.TryGetValue(path, out var cached))
            return cached;
        var opened = OpenedAssembly.TryOpen(path);
        _opened[path] = opened;
        return opened;
    }

    public void Dispose()
    {
        foreach (var opened in _opened.Values)
            opened?.Dispose();
        _opened.Clear();
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
    readonly FileStream _stream;
    readonly PEReader _pe;
    Dictionary<string, TypeDefinitionHandle>? _byFullName;

    OpenedAssembly(FileStream stream, PEReader pe, MetadataReader reader)
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
    {
        FileStream? stream = null;
        PEReader? pe = null;
        try
        {
            stream = File.OpenRead(path);
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
        _byFullName ??= BuildIndex();
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
