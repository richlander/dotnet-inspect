using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// CodeView debug info needed for symbol server lookup (no SRM types in signature).
/// </summary>
public record CodeViewInfo(Guid Guid, int Age, string PdbFileName, bool IsPortable);

/// <summary>
/// Source document info for strict verification (no SRM types in signature).
/// </summary>
public record SourceDocument(string FilePath, bool IsEmbedded, string? ResolvedUrl);

/// <summary>
/// Wraps PE + PDB readers, exposes high-level operations with no SRM in public signatures.
/// CLI orchestrates PDB acquisition (download via Packages), then calls back into this context.
/// </summary>
public class PdbContext : IDisposable
{
    private readonly PEReader _peReader;
    private readonly FileStream _peStream;
    private readonly Action<string>? _log;
    private readonly string _assemblyPath;

    private MetadataReaderProvider? _pdbProvider;
    private MetadataReader? _pdbReader;
    private SourceLinkResolver? _resolver;
    private readonly List<IDisposable> _disposables = [];

    /// <summary>
    /// The path to the assembly file that was opened.
    /// </summary>
    public string AssemblyPath => _assemblyPath;

    // --- PE/Assembly ---
    public bool HasMetadata => _peReader.HasMetadata;

    // --- Debug directory (POCO) ---
    public bool HasReproducibleFlag { get; private set; }
    public bool HasEmbeddedPdb { get; private set; }
    public string? CodeViewPdbPath { get; private set; }
    public bool? HasNormalizedPaths { get; private set; }
    public List<string>? NonNormalizedPaths { get; private set; }

    // --- PDB acquisition ---
    public CodeViewInfo? PdbId { get; private set; }
    public bool NeedsPdb => PdbId != null && !HasPdb;
    public bool HasPdb { get; private set; }
    public bool WindowsPdbDetected { get; set; }
    public string? PdbFormat { get; private set; }
    public string? PdbLocation { get; private set; }
    public string? SymbolServer { get; private set; }

    // --- SourceLink ---
    public string? SourceLinkJson { get; private set; }
    public bool HasSourceLink => SourceLinkJson != null;

    private PdbContext(FileStream peStream, PEReader peReader, string assemblyPath, Action<string>? log)
    {
        _peStream = peStream;
        _peReader = peReader;
        _assemblyPath = assemblyPath;
        _log = log;
    }

    /// <summary>
    /// Opens a PE file and probes for PDB (embedded, then standalone adjacent).
    /// After return, check NeedsPdb to see if CLI should download a PDB.
    /// </summary>
    public static PdbContext Open(string assemblyPath, Action<string>? log = null)
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

        var context = new PdbContext(stream, peReader, assemblyPath, log);

        if (!peReader.HasMetadata)
            return context;

        context.ReadDebugDirectory();
        context.TryLoadLocalPdb();

        return context;
    }

    /// <summary>
    /// Extracts assembly info from the PE reader.
    /// </summary>
    public AssemblyInfo ExtractAssemblyInfo(bool includeReferences = false)
        => AssemblyInspector.ExtractAssemblyInfo(_peReader, includeReferences);

    /// <summary>
    /// Creates an AssemblyInfo for a native (non-managed) binary.
    /// </summary>
    public AssemblyInfo CreateNativeInfo()
    {
        var info = AssemblyInspector.CreateNativeInfo(_peReader);
        bool isNativeAot = AssemblyInspector.DetectNativeAot(_peReader);
        info.IsNativeAot = isNativeAot;
        info.CompilationType = isNativeAot ? "NativeAOT" : "Native";
        return info;
    }

    /// <summary>
    /// Loads a PDB from a file path (called by CLI after downloading).
    /// </summary>
    public void LoadPdbFromFile(string pdbFilePath, string? pdbLocation = null, string? symbolServer = null)
    {
        try
        {
            var stream = File.OpenRead(pdbFilePath);
            _disposables.Add(stream);

            // Check for Portable PDB magic header (BSJB)
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
                    _log?.Invoke("Windows PDB detected (not supported)");
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
            SymbolServer = symbolServer;

            SourceLinkJson = AssemblyInspector.ExtractSourceLinkFromReader(_pdbReader);
            _resolver = _pdbReader != null ? SourceLinkResolver.Create(_pdbReader) : null;

            _log?.Invoke($"Loaded PDB: {PdbFormat}, {PdbLocation}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Error loading PDB: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves source information for a type by name.
    /// </summary>
    public SourceLinkResolver.TypeSourceInfo? ResolveTypeSource(string typeName)
    {
        if (_resolver == null || _pdbReader == null || !_peReader.HasMetadata)
            return null;

        var metadataReader = _peReader.GetMetadataReader();
        return _resolver.ResolveTypeSource(metadataReader, _pdbReader, typeName);
    }

    /// <summary>
    /// Resolves source file and line range for a specific method overload.
    /// </summary>
    public SourceLinkResolver.MethodSourceInfo? ResolveMethodSource(string typeName, string methodName, int overloadIndex, bool publicOnly = false)
    {
        if (_resolver == null || _pdbReader == null || !_peReader.HasMetadata)
            return null;

        var reader = _peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeName)
                continue;

            int matchCount = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;

                if (publicOnly && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                if (matchCount == overloadIndex)
                    return _resolver.ResolveMethodSourceRange(_pdbReader, methodHandle);

                matchCount++;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a type forwarder target assembly name for a given type.
    /// </summary>
    public string? FindTypeForwarder(string typeName)
    {
        if (!_peReader.HasMetadata)
            return null;

        var reader = _peReader.GetMetadataReader();
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            var exportedType = reader.GetExportedType(exportedTypeHandle);
            if (!exportedType.IsForwarder)
                continue;

            var fullName = reader.GetFullTypeName(exportedType);

            if (fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                    return reader.GetString(assemblyRef.Name);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Enumerates all source documents in the PDB for strict verification.
    /// </summary>
    public IEnumerable<SourceDocument> EnumerateSourceDocuments()
    {
        if (_pdbReader == null)
            yield break;

        // GUID for embedded source: 0E8A571B-6926-466E-B4AD-8AB04611F5FE
        var embeddedSourceGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

        foreach (var docHandle in _pdbReader.Documents)
        {
            var document = _pdbReader.GetDocument(docHandle);
            string filePath = _pdbReader.GetString(document.Name);

            // Skip non-source files
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

            string? resolvedUrl = _resolver?.ApplySourceLinkMapping(filePath);
            yield return new SourceDocument(filePath, isEmbedded, resolvedUrl);
        }
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink information.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => _resolver?.ExtractRepositoryUrl();

    /// <summary>
    /// Gets the SourceLinkResolver for batch operations (e.g. resolving multiple types).
    /// Returns null if no PDB/SourceLink is available.
    /// </summary>
    internal SourceLinkResolver? GetResolver() => _resolver;

    /// <summary>
    /// Gets the PDB MetadataReader for batch operations.
    /// Returns null if no PDB is loaded.
    /// </summary>
    internal MetadataReader? GetPdbReader() => _pdbReader;

    /// <summary>
    /// Gets the PE MetadataReader for batch operations.
    /// Returns null if no metadata is available.
    /// </summary>
    internal MetadataReader? GetMetadataReader()
        => _peReader.HasMetadata ? _peReader.GetMetadataReader() : null;

    /// <summary>
    /// Cheap presence flags for section discovery, using the already-open PEReader.
    /// </summary>
    public PresenceFlags ScanPresenceFlags()
        => AssemblyDetailScanner.ScanPresenceFlags(_peReader);

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

        try { _peReader.Dispose(); } catch { }
        try { _peStream.Dispose(); } catch { }
    }

    // --- Private implementation ---

    private void ReadDebugDirectory()
    {
        CodeViewDebugDirectoryData? portableCodeView = null;
        CodeViewDebugDirectoryData? windowsCodeView = null;

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

                CodeViewPdbPath = cvData.Path;

                if (!cvData.Path.StartsWith("/_/", StringComparison.Ordinal) &&
                    Path.GetDirectoryName(cvData.Path) is string dir && !string.IsNullOrEmpty(dir))
                {
                    HasNormalizedPaths = false;
                    NonNormalizedPaths ??= [];
                    NonNormalizedPaths.Add($"PDB Path: {cvData.Path}");
                }
                else
                {
                    HasNormalizedPaths = true;
                }

                if (isPortable)
                {
                    portableCodeView = cvData;
                    PdbId = new CodeViewInfo(cvData.Guid, cvData.Age, Path.GetFileName(cvData.Path), true);
                }
                else
                {
                    windowsCodeView = cvData;
                    if (portableCodeView == null)
                    {
                        // Only use Windows PDB as fallback
                        PdbId = new CodeViewInfo(cvData.Guid, cvData.Age, Path.GetFileName(cvData.Path), false);
                    }
                }
            }

            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                HasEmbeddedPdb = true;
                PdbFormat = "Portable";
                PdbLocation = "Embedded";

                var provider = _peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                _disposables.Add(provider);
                _pdbProvider = provider;
                _pdbReader = provider.GetMetadataReader();
                HasPdb = true;

                SourceLinkJson = AssemblyInspector.ExtractSourceLinkFromReader(_pdbReader);
                _resolver = SourceLinkResolver.Create(_pdbReader);

                _log?.Invoke("Using embedded PDB");
            }
        }

        if (windowsCodeView != null && portableCodeView != null)
        {
            _log?.Invoke("Found both Windows (.ni.pdb) and Portable PDB entries, using Portable");
        }
    }

    private void TryLoadLocalPdb()
    {
        if (HasPdb)
            return;

        var pdbPath = Path.ChangeExtension(_assemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
            return;

        // Check header
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
                _log?.Invoke("Standalone Windows PDB found (not supported)");
                return;
            }

            if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
                return;
        }
        catch
        {
            return;
        }

        // It's a Portable PDB — open it
        LoadPdbFromFile(pdbPath, "Standalone");
    }
}
