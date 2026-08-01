using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SourceLinkFetch;

/// <summary>
/// A source document tracked in the PDB.
/// </summary>
public record SourceDocument(string FilePath, bool IsEmbedded, string? ResolvedUrl);

/// <summary>
/// CodeView debug info for symbol server lookup.
/// </summary>
public record CodeViewInfo(Guid Guid, int Age, string PdbFileName, bool IsPortable);

/// <summary>
/// Opens a PE assembly and its PDB to extract SourceLink information.
/// </summary>
public class SourceLinkReader : IDisposable
{
    private readonly PEReader _peReader;
    private readonly FileStream _peStream;
    private readonly List<IDisposable> _disposables = [];

    private MetadataReaderProvider? _pdbProvider;
    private MetadataReader? _pdbReader;
    private SourceLinkResolver? _resolver;

    /// <summary>Whether the PDB has been loaded (embedded or external).</summary>
    public bool HasPdb { get; private set; }

    /// <summary>Whether a PDB needs to be acquired externally.</summary>
    public bool NeedsPdb => PdbId is not null && !HasPdb;

    /// <summary>CodeView info for symbol server lookup, if present.</summary>
    public CodeViewInfo? PdbId { get; private set; }

    /// <summary>The PDB format: "Portable" or "Windows".</summary>
    public string? PdbFormat { get; private set; }

    /// <summary>Where the PDB was loaded from: "Embedded", "Standalone", etc.</summary>
    public string? PdbLocation { get; private set; }

    /// <summary>Whether a Windows PDB was detected (not supported for SourceLink).</summary>
    public bool WindowsPdbDetected { get; private set; }

    /// <summary>Whether the assembly has the reproducible build flag.</summary>
    public bool HasReproducibleFlag { get; private set; }

    /// <summary>Whether SourceLink information is present in the PDB.</summary>
    public bool HasSourceLink => SourceLinkJson is not null;

    /// <summary>The raw SourceLink JSON from the PDB.</summary>
    public string? SourceLinkJson { get; private set; }

    private SourceLinkProvenanceResult? _provenance;

    /// <summary>
    /// The origin every resolvable document is fetched from, or a reason why no single origin
    /// describes this assembly's source.
    /// </summary>
    public SourceLinkProvenanceResult Provenance =>
        _provenance ??= _pdbReader is null
            ? new SourceLinkProvenanceResult(null, "no PDB is loaded")
            : SourceLinkProvenance.Determine(_pdbReader);

    /// <summary>The repository URL, or null when provenance could not be established.</summary>
    public string? RepositoryUrl => Provenance.Origin?.RepositoryUrl;

    /// <summary>The revision source is served at, or null when provenance could not be established.</summary>
    public string? CommitHash => Provenance.Origin?.Revision;

    private SourceLinkReader(FileStream peStream, PEReader peReader)
    {
        _peStream = peStream;
        _peReader = peReader;
    }

    /// <summary>
    /// Opens a PE assembly and probes for PDB (embedded first, then standalone adjacent).
    /// After return, check <see cref="NeedsPdb"/> to see if an external PDB should be loaded.
    /// </summary>
    public static SourceLinkReader Open(string assemblyPath)
    {
        var stream = File.OpenRead(assemblyPath);
        PEReader peReader;
        try
        {
            peReader = new PEReader(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        var reader = new SourceLinkReader(stream, peReader);

        if (peReader.HasMetadata)
        {
            reader.ReadDebugDirectory();
            reader.TryLoadLocalPdb(assemblyPath);
        }

        return reader;
    }

    /// <summary>
    /// Loads a PDB from a file path (e.g. downloaded from a symbol server).
    /// </summary>
    public void LoadPdbFromFile(string pdbFilePath, string? pdbLocation = null)
    {
        try
        {
            var stream = File.OpenRead(pdbFilePath);
            _disposables.Add(stream);

            byte[] header = new byte[4];
            stream.ReadExactly(header, 0, 4);
            stream.Position = 0;

            if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
            {
                bool isWindowsPdb = header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r';
                if (isWindowsPdb)
                {
                    WindowsPdbDetected = true;
                    PdbFormat = "Windows";
                }
                return;
            }

            var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            _disposables.Add(provider);
            _pdbProvider = provider;
            _pdbReader = provider.GetMetadataReader();

            HasPdb = true;
            PdbFormat = "Portable";
            PdbLocation = pdbLocation ?? "Standalone";

            SourceLinkJson = SourceLinkResolver.ExtractSourceLinkJson(_pdbReader);
            _resolver = SourceLinkResolver.Create(_pdbReader);
            _provenance = null;
        }
        catch
        {
            // Silently fail — caller can check HasPdb
        }
    }

    /// <summary>
    /// Enumerates all source documents in the PDB with SourceLink URL resolution.
    /// </summary>
    public IEnumerable<SourceDocument> EnumerateSourceDocuments()
    {
        if (_pdbReader is null)
            yield break;

        var embeddedSourceGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

        foreach (var docHandle in _pdbReader.Documents)
        {
            var document = _pdbReader.GetDocument(docHandle);
            string filePath = _pdbReader.GetString(document.Name);

            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(".fs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isEmbedded = false;
            foreach (var cdiHandle in _pdbReader.GetCustomDebugInformation(docHandle))
            {
                var cdi = _pdbReader.GetCustomDebugInformation(cdiHandle);
                if (_pdbReader.GetGuid(cdi.Kind) == embeddedSourceGuid)
                {
                    isEmbedded = true;
                    break;
                }
            }

            string? resolvedUrl = _resolver?.ResolveUrl(filePath);
            yield return new SourceDocument(filePath, isEmbedded, resolvedUrl);
        }
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { }
        }
        _disposables.Clear();
        _pdbProvider = null;
        _pdbReader = null;
        _resolver = null;
        _provenance = null;

        try { _peReader.Dispose(); } catch { }
        try { _peStream.Dispose(); } catch { }
    }

    // ---- Private implementation ----

    private void ReadDebugDirectory()
    {
        foreach (var entry in _peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                HasReproducibleFlag = true;
            }

            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                var cvData = _peReader.ReadCodeViewDebugDirectoryData(entry);
                bool isPortable = entry.MinorVersion == 0x504d;

                if (isPortable || PdbId is null)
                {
                    PdbId = new CodeViewInfo(cvData.Guid, cvData.Age, Path.GetFileName(cvData.Path), isPortable);
                }
            }

            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                PdbFormat = "Portable";
                PdbLocation = "Embedded";

                var provider = _peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                _disposables.Add(provider);
                _pdbProvider = provider;
                _pdbReader = provider.GetMetadataReader();
                HasPdb = true;

                SourceLinkJson = SourceLinkResolver.ExtractSourceLinkJson(_pdbReader);
                _resolver = SourceLinkResolver.Create(_pdbReader);
                _provenance = null;
            }
        }
    }

    private void TryLoadLocalPdb(string assemblyPath)
    {
        if (HasPdb)
            return;

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
            return;

        try
        {
            using var checkStream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            checkStream.ReadExactly(header, 0, 4);

            if (header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r')
            {
                WindowsPdbDetected = true;
                PdbFormat = "Windows";
                PdbLocation = "Standalone";
                return;
            }

            if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
                return;
        }
        catch
        {
            return;
        }

        LoadPdbFromFile(pdbPath, "Standalone");
    }
}
