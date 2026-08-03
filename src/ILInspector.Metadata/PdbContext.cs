using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// CodeView debug info needed for symbol server lookup (no SRM types in signature).
/// </summary>
public record CodeViewInfo(Guid Guid, int Age, string PdbFileName, bool IsPortable);

/// <summary>
/// Source document info for strict verification (no SRM types in signature).
/// <paramref name="Checksum"/> is the document hash recorded in the PDB and
/// <paramref name="ChecksumAlgorithm"/> its algorithm name (e.g. "SHA256"); both may be null.
/// </summary>
public record SourceDocument(
    string FilePath,
    bool IsEmbedded,
    string? ResolvedUrl,
    byte[]? Checksum = null,
    string? ChecksumAlgorithm = null,
    int DocumentRowId = 0,
    string? CanonicalPath = null);

/// <summary>
/// A method-to-document relationship extracted from portable-PDB sequence points.
/// The metadata token and document row identify the same-version coordinates; the
/// member anchor and canonical document path provide cross-version identity.
/// </summary>
public sealed record MemberSourceInfo(
    MemberAnchor Anchor,
    int MetadataToken,
    int DocumentRowId,
    string FilePath,
    string CanonicalPath,
    string? ResolvedUrl,
    int StartLine,
    int EndLine,
    bool IsPrimaryDocument = false,
    bool IsFinalizer = false);

public record ILOffsetMemberContextInfo(
    string? Assembly,
    string Type,
    string TypeKind,
    string Member,
    string Signature,
    string MemberKind,
    string Visibility,
    bool Static,
    string? Async,
    int MetadataToken,
    int ILOffset);

public record ILOffsetExceptionContextInfo(
    int Region,
    string Context,
    string Clause,
    int TryStart,
    int TryEnd,
    int HandlerStart,
    int HandlerEnd,
    int? FilterStart,
    int? FilterEnd,
    string? CaughtType);

public record MethodExceptionRegionInfo(
    int Region,
    string Clause,
    int TryStart,
    int TryEnd,
    int HandlerStart,
    int HandlerEnd,
    int? FilterStart,
    int? FilterEnd,
    string? CaughtType);

/// <summary>
/// Wraps PE + PDB readers, exposes high-level operations with no SRM in public signatures.
/// CLI orchestrates PDB acquisition (download via Packages), then calls back into this context.
/// </summary>
public class PdbContext : IDisposable
{
    private readonly PEReader _peReader;
    private readonly Stream _peStream;
    private readonly bool _entireImagePrefetched;
    private readonly Action<string>? _log;
    private readonly string? _assemblyPath;
    private readonly string _assemblyDisplayName;

    private MetadataReaderProvider? _pdbProvider;
    private MetadataReader? _pdbReader;
    private SourceLinkResolver? _resolver;
    private bool? _isReferenceAssembly;
    private SourceDocumentPathResolver _sourceDocumentPathResolver = SourceDocumentPathResolver.Empty;
    private readonly List<IDisposable> _disposables = [];
    private MethodBodySource? _methodBodies;
    private bool _disposed;

    /// <summary>
    /// The path to the assembly file that was opened.
    /// </summary>
    public string AssemblyPath => _assemblyPath
        ?? throw new InvalidOperationException(
            "This assembly was opened from a descriptor without a filesystem path.");

    /// <summary>
    /// The acquisition-owned path, when the descriptor supplied one.
    /// </summary>
    public string? AssemblyPathOrNull => _assemblyPath;

    /// <summary>
    /// The log callback, if any.
    /// </summary>
    internal Action<string>? Log => _log;

    /// <summary>
    /// Session-bound method-body and operand access without exposing raw readers.
    /// </summary>
    public MethodBodySource MethodBodies
    {
        get
        {
            EnsureAlive();
            return _methodBodies ??= new MethodBodySource(_peReader, EnsureAlive);
        }
    }

    /// <summary>
    /// Gets immutable content for the fully prefetched PE image. Consumers can
    /// inspect this snapshot without reopening the target file or receiving the
    /// context-owned reader.
    /// </summary>
    public ImmutableArray<byte> GetPrefetchedImage()
    {
        EnsureAlive();
        if (!_entireImagePrefetched)
        {
            throw new InvalidOperationException(
                "Parallel body analysis requires a fully prefetched PE image.");
        }

        return _peReader.GetEntireImage().GetContent();
    }

    // --- PE/Assembly ---
    public bool HasMetadata => _peReader.HasMetadata;

    /// <summary>
    /// File size captured at open time (avoids repeated fstat syscalls).
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// Last write time captured at open time (avoids repeated lstat syscalls).
    /// </summary>
    public DateTime LastWriteTimeUtc { get; }

    // --- Debug directory (POCO) ---
    public bool HasReproducibleFlag { get; private set; }
    public bool HasEmbeddedPdb { get; private set; }
    public string? CodeViewPdbPath { get; private set; }

    /// <summary>
    /// On-disk path to a successfully loaded *portable* PDB file (standalone or downloaded from a
    /// symbol server), or null for embedded/Windows/no PDB. Lets callers (e.g. the decompiler)
    /// reuse the acquired symbols for local-variable names after this context is disposed.
    /// </summary>
    public string? PortablePdbPath { get; private set; }
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

    private PdbContext(
        Stream peStream,
        PEReader peReader,
        string? assemblyPath,
        string assemblyDisplayName,
        Action<string>? log,
        bool entireImagePrefetched)
    {
        _peStream = peStream;
        _peReader = peReader;
        _entireImagePrefetched = entireImagePrefetched;
        _assemblyPath = assemblyPath;
        _assemblyDisplayName = assemblyDisplayName;
        _log = log;
        FileSize = peStream.Length;
        LastWriteTimeUtc = peStream is FileStream fileStream
            ? File.GetLastWriteTimeUtc(fileStream.SafeFileHandle)
            : default;
    }

    /// <summary>
    /// Opens a PE file and probes for PDB (embedded, then standalone adjacent).
    /// After return, check NeedsPdb to see if CLI should download a PDB.
    /// </summary>
    public static PdbContext Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, PEStreamOptions.Default);

    /// <summary>
    /// Opens an acquisition descriptor through its authoritative stream factory.
    /// The optional path is used only for adjacent PDB discovery and file metadata.
    /// </summary>
    public static PdbContext Open(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Open(
            assembly.OpenRead(),
            assembly.Path,
            assembly.Identity.Name,
            log,
            PEStreamOptions.Default);
    }

    /// <summary>
    /// This context's open reader, lent to <see cref="AssemblyInspectionSession.Borrow"/> so the
    /// facet surface reads the same bytes this context read instead of reopening
    /// the source. Internal because the reader is metadata-internal; borrowers go
    /// through the session.
    /// </summary>
    internal PEReader BorrowedPEReader
    {
        get
        {
            EnsureAlive();
            return _peReader;
        }
    }

    /// <summary>
    /// This context's liveness check, lent to a borrowing session so the borrow fails loudly
    /// instead of reading through a released handle. See
    /// <see cref="AssemblyInspectionSession.Borrow"/>.
    /// </summary>
    internal void EnsureAliveForBorrower() => EnsureAlive();

    /// <summary>
    /// Opens a PE file with its complete image prefetched so downstream body
    /// producers can safely share the reader during parallel analysis.
    /// </summary>
    internal static PdbContext OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
        => Open(
            assemblyPath,
            log,
            PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen);

    static PdbContext Open(
        string assemblyPath,
        Action<string>? log,
        PEStreamOptions streamOptions)
        => Open(
            File.OpenRead(assemblyPath),
            assemblyPath,
            Path.GetFileName(assemblyPath),
            log,
            streamOptions);

    static PdbContext Open(
        Stream stream,
        string? assemblyPath,
        string assemblyDisplayName,
        Action<string>? log,
        PEStreamOptions streamOptions)
    {
        PEReader peReader;
        try
        {
            peReader = new PEReader(stream, streamOptions);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        var context = new PdbContext(
            stream,
            peReader,
            assemblyPath,
            assemblyDisplayName,
            log,
            (streamOptions & PEStreamOptions.PrefetchEntireImage) != 0);

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

    /// <summary>Extracts an API surface from the already-open PE image.</summary>
    public ApiSurface ExtractApiSurface(
        bool includeAll = false,
        bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(_peReader, includeAll, typesOnly);

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
                stream.Dispose();
                return;
            }

            var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            if (!PdbMatchesAssembly(reader))
            {
                provider.Dispose();
                stream.Dispose();
                _log?.Invoke($"Portable PDB identity mismatch: {Path.GetFileName(pdbFilePath)} does not match {_assemblyDisplayName}");
                return;
            }

            _disposables.Add(stream);
            _disposables.Add(provider);
            _pdbProvider = provider;
            _pdbReader = reader;

            HasPdb = true;
            PdbFormat = "Portable";
            PdbLocation = pdbLocation ?? "Standalone";
            PortablePdbPath = pdbFilePath;
            SymbolServer = symbolServer;

            SourceLinkJson = AssemblyInspector.ExtractSourceLinkFromReader(_pdbReader);
            _sourceDocumentPathResolver = SourceDocumentPath.CreateResolver(SourceLinkJson);
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
    /// Whether the MethodDef <paramref name="methodToken"/> addresses carries an IL body:
    /// <see langword="true"/> when it does, <see langword="false"/> when it does not — an
    /// abstract, interface, extern, or runtime-implemented method — and <see langword="null"/>
    /// when the token cannot be read as a MethodDef in this assembly, which is not the same
    /// answer as "no body" and must not be reported as one (issue #3299).
    /// </summary>
    /// <remarks>
    /// A reference assembly answers <see langword="null"/> for every token: it strips all IL, so
    /// its RVAs report the image's surface-only nature rather than anything about the method.
    /// </remarks>
    public bool? MethodHasBody(int methodToken)
    {
        if (!_peReader.HasMetadata)
            return null;

        try
        {
            // Handle() rejects an invalid token by throwing, and an inspected assembly is
            // untrusted input, so decode inside the guard rather than ahead of it.
            var handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            var reader = _peReader.GetMetadataReader();
            if (IsReferenceAssembly(reader))
                return null;

            return reader.GetMethodDefinition((MethodDefinitionHandle)handle).RelativeVirtualAddress != 0;
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the assembly carries <c>ReferenceAssemblyAttribute</c>, cached because the answer
    /// is fixed for the image.
    /// </summary>
    private bool IsReferenceAssembly(MetadataReader reader)
    {
        if (_isReferenceAssembly is { } cached)
            return cached;

        bool result = false;
        if (reader.IsAssembly)
        {
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                if (IsReferenceAssemblyAttribute(reader, reader.GetCustomAttribute(handle)))
                {
                    result = true;
                    break;
                }
            }
        }

        _isReferenceAssembly = result;
        return result;
    }

    /// <summary>
    /// Whether <paramref name="attribute"/> is <c>ReferenceAssemblyAttribute</c>. The constructor
    /// is a MemberReference when the attribute type lives in another assembly and a MethodDef when
    /// it is defined in this one — which is the common case, since the reference assemblies that
    /// most need this test define the attribute themselves.
    /// </summary>
    private static bool IsReferenceAssemblyAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        StringHandle name;
        StringHandle @namespace;

        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference)
                    return false;

                var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                name = typeRef.Name;
                @namespace = typeRef.Namespace;
                break;

            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var typeDef = reader.GetTypeDefinition(method.GetDeclaringType());
                name = typeDef.Name;
                @namespace = typeDef.Namespace;
                break;

            default:
                return false;
        }

        return reader.StringComparer.Equals(name, "ReferenceAssemblyAttribute")
            && reader.StringComparer.Equals(@namespace, "System.Runtime.CompilerServices");
    }

    public ILOffsetMemberContextInfo? ResolveMemberContext(int methodToken, int ilOffset)
    {
        if (!_peReader.HasMetadata)
            return null;

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
            return null;

        try
        {
            var reader = _peReader.GetMetadataReader();
            var methodHandle = (MethodDefinitionHandle)handle;
            var method = reader.GetMethodDefinition(methodHandle);
            var type = reader.GetTypeDefinition(method.GetDeclaringType());
            var assembly = reader.GetAssemblyDefinition();
            var typeName = reader.GetFullTypeName(type);
            var methodName = reader.GetString(method.Name);
            var signature = FormatMemberSignature(reader, type, method, methodName);
            var async = MethodClassificationScanner.ClassifyAsyncMethod(reader, method) switch
            {
                MethodClassification.RuntimeAsync => "Runtime",
                MethodClassification.StateMachineAsync => "State machine",
                _ => null
            };

            return new ILOffsetMemberContextInfo(
                Assembly: reader.GetString(assembly.Name),
                Type: typeName,
                TypeKind: GetTypeKind(reader, type),
                Member: $"{typeName}.{methodName}",
                Signature: signature,
                MemberKind: GetMemberKind(methodName),
                Visibility: GetVisibility(method.Attributes),
                Static: (method.Attributes & MethodAttributes.Static) != 0,
                Async: async,
                MetadataToken: methodToken,
                ILOffset: ilOffset);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ILOffsetExceptionContextInfo> ResolveExceptionContext(int methodToken, int ilOffset, out string? error)
    {
        error = null;
        if (!_peReader.HasMetadata)
            return [];

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return [];
        }

        try
        {
            var reader = _peReader.GetMetadataReader();
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return [];
            }

            var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            List<ILOffsetExceptionContextInfo> rows = [];
            var regions = body.ExceptionRegions;
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                var tryEnd = region.TryOffset + region.TryLength;
                var handlerEnd = region.HandlerOffset + region.HandlerLength;
                int? filterStart = region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : null;
                int? filterEnd = region.Kind == ExceptionRegionKind.Filter ? region.HandlerOffset : null;
                var context = GetExceptionContext(region, ilOffset, tryEnd, handlerEnd, filterStart, filterEnd);
                if (context is null)
                    continue;

                rows.Add(new ILOffsetExceptionContextInfo(
                    Region: i + 1,
                    Context: context,
                    Clause: FormatExceptionClause(region.Kind),
                    TryStart: region.TryOffset,
                    TryEnd: tryEnd,
                    HandlerStart: region.HandlerOffset,
                    HandlerEnd: handlerEnd,
                    FilterStart: filterStart,
                    FilterEnd: filterEnd,
                    CaughtType: ResolveCatchType(reader, region)));
            }

            return rows;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not resolve exception context for token 0x{methodToken:X}+0x{ilOffset:X}.";
            return [];
        }
    }

    public IReadOnlyList<MethodExceptionRegionInfo> ResolveExceptionRegions(int methodToken, out string? error)
    {
        error = null;
        if (!_peReader.HasMetadata)
            return [];

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return [];
        }

        try
        {
            var reader = _peReader.GetMetadataReader();
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return [];
            }

            var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            List<MethodExceptionRegionInfo> rows = [];
            var regions = body.ExceptionRegions;
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                var tryEnd = region.TryOffset + region.TryLength;
                var handlerEnd = region.HandlerOffset + region.HandlerLength;
                int? filterStart = region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : null;
                int? filterEnd = region.Kind == ExceptionRegionKind.Filter ? region.HandlerOffset : null;
                rows.Add(new MethodExceptionRegionInfo(
                    Region: i + 1,
                    Clause: FormatExceptionClause(region.Kind),
                    TryStart: region.TryOffset,
                    TryEnd: tryEnd,
                    HandlerStart: region.HandlerOffset,
                    HandlerEnd: handlerEnd,
                    FilterStart: filterStart,
                    FilterEnd: filterEnd,
                    CaughtType: ResolveCatchType(reader, region)));
            }

            return rows;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not resolve exception regions for token 0x{methodToken:X}.";
            return [];
        }
    }

    /// <summary>
    /// Resolves source file and line range for a specific method overload.
    /// </summary>
    public SourceLinkResolver.MethodSourceInfo? ResolveMethodSource(string typeName, string methodName, int overloadIndex, bool publicOnly = false, int metadataToken = 0)
    {
        if (_resolver == null || _pdbReader == null || !_peReader.HasMetadata)
            return null;

        var reader = _peReader.GetMetadataReader();

        // When the caller already resolved the exact member, resolve source by its
        // metadata token rather than name + overload index. Name/index resolution
        // counts methods in raw metadata order and can drift from the extractor's
        // visibility-filtered overload numbering (e.g. a finalizer preceded in
        // metadata by an unrelated private method also named "Finalize"), which
        // would otherwise return the wrong member's source.
        if (metadataToken != 0)
        {
            var tokenHandle = MetadataTokens.Handle(metadataToken);
            if (tokenHandle.Kind != HandleKind.MethodDefinition)
                return null;
            return _resolver.ResolveMethodSourceRange(_pdbReader, (MethodDefinitionHandle)tokenHandle);
        }

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

    private static string FormatMemberSignature(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        string methodName)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, type, method);
            var signature = GuardedSignatureText.MethodText(reader, method, context)
                .GetValueOrThrow();
            return SignatureRenderer.RenderDecodedSignature(reader, method, methodName, signature);
        }
        catch
        {
            return methodName + "(...)";
        }
    }

    private static string GetMemberKind(string methodName)
        => methodName switch
        {
            ".ctor" => "constructor",
            ".cctor" => "static constructor",
            _ => "method"
        };

    private static string GetVisibility(MethodAttributes attributes)
        => (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Private => "private",
            MethodAttributes.Family => "protected",
            MethodAttributes.Assembly => "internal",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.FamANDAssem => "private protected",
            _ => "private"
        };

    private static string GetTypeKind(MetadataReader reader, TypeDefinition type)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
            return "interface";

        var baseName = GetBaseTypeName(reader, type);
        if (baseName == "System.Enum")
            return "enum";
        if (baseName == "System.ValueType")
            return "struct";
        if (baseName == "System.MulticastDelegate")
            return "delegate";

        return "class";
    }

    private static string? GetBaseTypeName(MetadataReader reader, TypeDefinition type)
        => type.BaseType.Kind switch
        {
            HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)type.BaseType)),
            HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)type.BaseType)),
            _ => null
        };

    private static string? GetExceptionContext(
        ExceptionRegion region,
        int offset,
        int tryEnd,
        int handlerEnd,
        int? filterStart,
        int? filterEnd)
    {
        if (filterStart is { } fs && filterEnd is { } fe && offset >= fs && offset < fe)
            return "filter";
        if (offset >= region.HandlerOffset && offset < handlerEnd)
            return region.Kind switch
            {
                ExceptionRegionKind.Catch => "catch handler",
                ExceptionRegionKind.Filter => "filter handler",
                ExceptionRegionKind.Finally => "finally handler",
                ExceptionRegionKind.Fault => "fault handler",
                _ => "handler"
            };
        if (offset >= region.TryOffset && offset < tryEnd)
            return "try";
        return null;
    }

    private static string FormatExceptionClause(ExceptionRegionKind kind)
        => kind switch
        {
            ExceptionRegionKind.Catch => "catch",
            ExceptionRegionKind.Filter => "filter",
            ExceptionRegionKind.Finally => "finally",
            ExceptionRegionKind.Fault => "fault",
            _ => kind.ToString()
        };

    private static string? ResolveCatchType(MetadataReader reader, ExceptionRegion region)
        => region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil
            ? TypeResolver.GetTypeName(reader, region.CatchType)
            : null;

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

            var sourceLink = _sourceDocumentPathResolver.Resolve(filePath);

            byte[]? checksum = null;
            string? checksumAlgorithm = null;
            if (!document.Hash.IsNil)
            {
                checksum = _pdbReader.GetBlobBytes(document.Hash);
                checksumAlgorithm = MapHashAlgorithm(_pdbReader.GetGuid(document.HashAlgorithm));
            }

            yield return new SourceDocument(
                filePath,
                isEmbedded,
                sourceLink.ResolvedUrl,
                checksum,
                checksumAlgorithm,
                MetadataTokens.GetRowNumber(docHandle),
                sourceLink.CanonicalPath);
        }
    }

    /// <summary>
    /// Enumerates method-to-document mappings from visible portable-PDB sequence points.
    /// A method may produce multiple rows when sequence points span multiple documents.
    /// </summary>
    public IEnumerable<MemberSourceInfo> EnumerateMemberSources(
        IReadOnlySet<int>? metadataTokens = null)
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            yield break;

        var metadata = _peReader.GetMetadataReader();
        foreach (var methodHandle in EnumerateSelectedMethods(metadata, metadataTokens))
        {
            int metadataToken = MetadataTokens.GetToken(methodHandle);
            var debugInfo = _pdbReader.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
            var currentDocument = debugInfo.Document;
            var primaryDocument = debugInfo.Document;
            Dictionary<DocumentHandle, (int StartLine, int EndLine)> ranges = [];

            foreach (var point in debugInfo.GetSequencePoints())
            {
                if (!point.Document.IsNil)
                    currentDocument = point.Document;
                if (point.IsHidden || currentDocument.IsNil)
                    continue;
                // Multi-document methods may omit the root document; in that case,
                // the first visible sequence point is the stable presentation choice.
                if (primaryDocument.IsNil)
                    primaryDocument = currentDocument;

                if (ranges.TryGetValue(currentDocument, out var range))
                {
                    ranges[currentDocument] = (
                        Math.Min(range.StartLine, point.StartLine),
                        Math.Max(range.EndLine, point.EndLine));
                }
                else
                {
                    ranges[currentDocument] = (point.StartLine, point.EndLine);
                }
            }

            if (ranges.Count == 0)
                continue;

            var method = metadata.GetMethodDefinition(methodHandle);
            var anchor = ApiMemberIdentity.CreateMethodAnchor(
                metadata,
                method.GetDeclaringType(),
                method);
            bool isFinalizer = ApiSurfaceExtractor.IsFinalizerMethod(metadata, methodHandle);

            foreach (var (documentHandle, range) in ranges
                .OrderBy(static item => MetadataTokens.GetRowNumber(item.Key)))
            {
                var document = _pdbReader.GetDocument(documentHandle);
                string filePath = _pdbReader.GetString(document.Name);
                var sourceLink = _sourceDocumentPathResolver.Resolve(filePath);
                yield return new MemberSourceInfo(
                    anchor,
                    metadataToken,
                    MetadataTokens.GetRowNumber(documentHandle),
                    filePath,
                    sourceLink.CanonicalPath,
                    sourceLink.ResolvedUrl,
                    range.StartLine,
                    range.EndLine,
                    IsPrimaryDocument: documentHandle == primaryDocument,
                    IsFinalizer: isFinalizer);
            }
        }
    }

    private static IEnumerable<MethodDefinitionHandle> EnumerateSelectedMethods(
        MetadataReader metadata,
        IReadOnlySet<int>? metadataTokens)
    {
        if (metadataTokens is null)
        {
            foreach (var methodHandle in metadata.MethodDefinitions)
                yield return methodHandle;
            yield break;
        }

        int methodCount = metadata.GetTableRowCount(TableIndex.MethodDef);
        foreach (int token in metadataTokens.Order())
        {
            const int methodDefinitionToken = 0x06000000;
            const int tokenTypeMask = unchecked((int)0xFF000000);
            const int rowMask = 0x00FFFFFF;
            int row = token & rowMask;
            if ((token & tokenTypeMask) != methodDefinitionToken || row == 0 || row > methodCount)
                continue;

            yield return MetadataTokens.MethodDefinitionHandle(row);
        }
    }

    /// <summary>
    /// Reads compiler options recorded in portable-PDB module custom debug information.
    /// </summary>
    public IReadOnlyList<CompilationOptionInfo> GetCompilationOptions()
        => _pdbReader is null
            ? []
            : PdbCompilationInfoReader.ReadOptions(_pdbReader);

    /// <summary>
    /// Reads compiler references recorded in portable-PDB module custom debug information.
    /// </summary>
    public IReadOnlyList<CompilationReferenceInfo> GetCompilationReferences()
        => _pdbReader is null
            ? []
            : PdbCompilationInfoReader.ReadReferences(_pdbReader);

    // Well-known source document hash algorithm GUIDs (System.Reflection.Metadata).
    private static readonly Guid s_hashSha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid s_hashSha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    internal static string? MapHashAlgorithm(Guid algorithm)
    {
        if (algorithm == s_hashSha256) return "SHA256";
        if (algorithm == s_hashSha1) return "SHA1";
        return null;
    }

    /// <summary>
    /// Establishes where this PDB's source is actually fetched from, over the documents the PDB
    /// declares. Returns a result that says why when no single origin describes them all.
    /// </summary>
    public SourceLinkFetch.SourceLinkProvenanceResult Provenance()
        => _resolver is null || _pdbReader is null
            ? new SourceLinkFetch.SourceLinkProvenanceResult(null, "the PDB carries no SourceLink map")
            : _resolver.Provenance(_pdbReader);

    /// <summary>
    /// Extracts the repository URL from SourceLink information, or null when no single origin
    /// describes every document the assembly resolves.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => Provenance().Origin?.RepositoryUrl;

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset.
    /// Works even without SourceLink (returns file path + line, no URL).
    /// </summary>
    public SourceLinkResolver.ILOffsetSourceInfo? ResolveByILOffset(int methodToken, int ilOffset)
    {
        if (_pdbReader == null || !_peReader.HasMetadata)
            return null;

        var metadataReader = _peReader.GetMetadataReader();

        if (_resolver != null)
            return _resolver.ResolveByILOffset(metadataReader, _pdbReader, methodToken, ilOffset);

        return SourceLinkResolver.ResolveByILOffsetDirect(metadataReader, _pdbReader, methodToken, ilOffset);
    }

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
    /// Cheap metadata-backed presence flags for section discovery, using the
    /// already-open PEReader. IL-backed presence is outside this scan.
    /// </summary>
    public PresenceFlags ScanPresenceFlags()
        => AssemblyDetailScanner.ScanPresenceFlags(_peReader);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
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

    void EnsureAlive()
        => ObjectDisposedException.ThrowIf(_disposed, this);

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
                _sourceDocumentPathResolver = SourceDocumentPath.CreateResolver(SourceLinkJson);
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
        if (_assemblyPath is null)
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

    private bool PdbMatchesAssembly(MetadataReader pdbReader)
    {
        if (PdbId is not { IsPortable: true } expected)
            return true;

        var id = pdbReader.DebugMetadataHeader?.Id;
        if (id is not { Length: >= 16 })
        {
            _log?.Invoke("PDB identity missing or too short to verify");
            return false;
        }

        Span<byte> guidBytes = stackalloc byte[16];
        id.Value.AsSpan(0, 16).CopyTo(guidBytes);
        var actual = new Guid(guidBytes);
        if (actual == expected.Guid)
            return true;

        _log?.Invoke($"PDB GUID mismatch: assembly expects {expected.Guid:D}; PDB has {actual:D}");
        return false;
    }
}
