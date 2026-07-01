using System.Reflection;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;

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
    string? ChecksumAlgorithm = null);

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

public record ILOffsetInstructionContextInfo(
    int ILOffset,
    string Boundary,
    string Opcode,
    string OperandKind,
    string? Operand,
    string? OperandToken,
    string? BranchTargets,
    int NextOffset,
    int Length,
    int? Block,
    bool TerminatesBlock,
    bool FallsThrough);

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

    /// <summary>
    /// The log callback, if any.
    /// </summary>
    internal Action<string>? Log => _log;

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

    private PdbContext(FileStream peStream, PEReader peReader, string assemblyPath, Action<string>? log)
    {
        _peStream = peStream;
        _peReader = peReader;
        _assemblyPath = assemblyPath;
        _log = log;
        FileSize = peStream.Length;
        LastWriteTimeUtc = File.GetLastWriteTimeUtc(peStream.SafeFileHandle);
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
                _log?.Invoke($"Portable PDB identity mismatch: {Path.GetFileName(pdbFilePath)} does not match {Path.GetFileName(_assemblyPath)}");
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

    public ILOffsetInstructionContextInfo? ResolveInstructionContext(int methodToken, int ilOffset, out string? error)
    {
        error = null;
        if (!_peReader.HasMetadata)
            return null;

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return null;
        }

        try
        {
            var reader = _peReader.GetMetadataReader();
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return null;
            }

            var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            var instructions = MethodInstructions.Decode(body);
            if (!instructions.IsComplete)
            {
                error = $"Could not decode IL for token 0x{methodToken:X}: {instructions.Blocks.IncompleteReason}";
                return null;
            }

            var instruction = instructions.InstructionAt(ilOffset);
            if (instruction is null)
            {
                error = $"IL offset 0x{ilOffset:X} is not an instruction boundary for token 0x{methodToken:X}.";
                return null;
            }

            var operand = ResolveInstructionOperand(reader, instruction);
            var blockIndex = instructions.BlockIndexAt(ilOffset);
            return new ILOffsetInstructionContextInfo(
                ILOffset: ilOffset,
                Boundary: "Exact",
                Opcode: ILDisassembler.GetDisplayName(instruction.OpCode),
                OperandKind: operand.Kind,
                Operand: operand.Value,
                OperandToken: operand.Token,
                BranchTargets: instruction.BranchTargets.IsDefaultOrEmpty
                    ? null
                    : string.Join(", ", instruction.BranchTargets.Select(FormatILOffset)),
                NextOffset: instruction.NextOffset,
                Length: instruction.Length,
                Block: blockIndex >= 0 ? blockIndex : null,
                TerminatesBlock: instruction.TerminatesBlock,
                FallsThrough: instruction.FallsThrough);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not resolve instruction context for token 0x{methodToken:X}+0x{ilOffset:X}.";
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

    private static string FormatMemberSignature(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        string methodName)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, type, method);
            var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
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

    private static (string Kind, string? Value, string? Token) ResolveInstructionOperand(
        MetadataReader reader,
        DecodedInstruction instruction)
    {
        int token = (int)instruction.OperandValue;
        return instruction.Operand switch
        {
            ILInspector.Instructions.OperandKind.None => ("None", null, null),
            ILInspector.Instructions.OperandKind.InlineMethod => ("Method", ILTokenResolver.ResolveMethod(reader, token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.InlineField => ("Field", ILTokenResolver.ResolveField(reader, token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.InlineType => ("Type", ILTokenResolver.ResolveType(reader, token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.InlineString => ("String", ILTokenResolver.ResolveString(reader, token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.InlineTok => ("Token", ILTokenResolver.ResolveToken(reader, token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.InlineSig => ("Signature", FormatToken(token), FormatToken(token)),
            ILInspector.Instructions.OperandKind.ShortInlineBrTarget or ILInspector.Instructions.OperandKind.InlineBrTarget
                => ("Branch Target", FormatTargets(instruction), null),
            ILInspector.Instructions.OperandKind.InlineSwitch => ("Switch Targets", FormatTargets(instruction), null),
            ILInspector.Instructions.OperandKind.ShortInlineVar or ILInspector.Instructions.OperandKind.InlineVar
                => (IsArgumentOpcode(instruction.OpCode) ? "Argument" : "Local", instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null),
            ILInspector.Instructions.OperandKind.ShortInlineI
                or ILInspector.Instructions.OperandKind.InlineI
                or ILInspector.Instructions.OperandKind.InlineI8
                => ("Constant", instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null),
            ILInspector.Instructions.OperandKind.ShortInlineR
                => ("Constant", BitConverter.Int32BitsToSingle((int)instruction.OperandValue).ToString(CultureInfo.InvariantCulture), null),
            ILInspector.Instructions.OperandKind.InlineR
                => ("Constant", BitConverter.Int64BitsToDouble(instruction.OperandValue).ToString(CultureInfo.InvariantCulture), null),
            _ => (instruction.Operand.ToString(), instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null)
        };
    }

    private static bool IsArgumentOpcode(ILOpCode opcode)
        => opcode is ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3;

    private static string? FormatTargets(DecodedInstruction instruction)
        => instruction.BranchTargets.IsDefaultOrEmpty
            ? null
            : string.Join(", ", instruction.BranchTargets.Select(FormatILOffset));

    private static string FormatILOffset(int offset) => $"IL_{offset:X4}";

    private static string FormatToken(int token) => $"0x{token:X8}";

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
    /// Resolves the assembly path that actually implements a type, following type forwarders.
    /// Returns null if the type is defined in this assembly (not forwarded).
    /// Looks for the target assembly DLL in the same directory as this assembly.
    /// </summary>
    public string? ResolveImplementationAssemblyPath(string typeName)
    {
        var targetAssemblyName = FindTypeForwarder(typeName);
        if (targetAssemblyName == null)
            return null;

        var dir = Path.GetDirectoryName(_assemblyPath);
        if (dir == null)
            return null;

        var targetPath = Path.Combine(dir, targetAssemblyName + ".dll");
        return File.Exists(targetPath) ? targetPath : null;
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

            byte[]? checksum = null;
            string? checksumAlgorithm = null;
            if (!document.Hash.IsNil)
            {
                checksum = _pdbReader.GetBlobBytes(document.Hash);
                checksumAlgorithm = MapHashAlgorithm(_pdbReader.GetGuid(document.HashAlgorithm));
            }

            yield return new SourceDocument(filePath, isEmbedded, resolvedUrl, checksum, checksumAlgorithm);
        }
    }

    // Well-known source document hash algorithm GUIDs (System.Reflection.Metadata).
    private static readonly Guid s_hashSha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid s_hashSha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    private static string? MapHashAlgorithm(Guid algorithm)
    {
        if (algorithm == s_hashSha256) return "SHA256";
        if (algorithm == s_hashSha1) return "SHA1";
        return null;
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink information.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => _resolver?.ExtractRepositoryUrl();

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
