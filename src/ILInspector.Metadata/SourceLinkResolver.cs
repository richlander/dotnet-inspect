using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.RegularExpressions;
using SLF = SourceLinkFetch;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves types and members to their source file locations using SourceLink information from PDBs.
/// Delegates URL mapping to the SourceLinkFetch library.
/// </summary>
public class SourceLinkResolver
{
    private readonly SLF.SourceLinkResolver _slfResolver;

    // Lazily-built per-reader indexes so batched enrichment (one ResolveTypeSource call per API
    // type) does not re-scan every TypeDefinition / PDB Document row for each type. Keyed by reader
    // instance because the metadata/pdb readers are passed in per call.
    private MetadataReader? _typeIndexReader;
    private Dictionary<string, TypeDefinitionHandle>? _fullNameIndex;
    private Dictionary<string, TypeDefinitionHandle>? _simpleNameIndex;
    private MetadataReader? _docIndexReader;
    private Dictionary<string, List<string>>? _docsByFirstSegment;

    public enum SourceResolutionMethod
    {
        /// <summary>Source resolved from method debug info (sequence points).</summary>
        SourceLink,
        /// <summary>Source inferred from PDB document name matching type name.</summary>
        Inferred
    }

    public record TypeSourceInfo(
        string? SourceFilePath,
        string? SourceUrl,
        int? LineNumber,
        string? GitHubBrowseUrl,
        SourceResolutionMethod ResolutionMethod = SourceResolutionMethod.SourceLink
    )
    {
        /// <summary>
        /// Additional source files for partial types (e.g., JObject.Async.cs alongside JObject.cs).
        /// Only populated when type has multiple source files.
        /// </summary>
        public List<PartialSourceFile> AdditionalSourceFiles { get; init; } = [];

        /// <summary>
        /// Indicates whether this type is defined across multiple partial files.
        /// </summary>
        public bool IsPartialType => AdditionalSourceFiles.Count > 0;
    }

    /// <summary>
    /// Represents a source file that is part of a partial type definition.
    /// </summary>
    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
        string? GitHubBrowseUrl
    );

    /// <summary>
    /// Source location for a method, including the full line range from sequence points.
    /// <paramref name="Checksum"/> is the portable-PDB document hash and
    /// <paramref name="ChecksumAlgorithm"/> its algorithm name (e.g. "SHA256"); both may be null
    /// when the PDB records no document hash. They let callers authenticate a local source file
    /// on disk before preferring it over the remote SourceLink URL.
    /// </summary>
    public record MethodSourceInfo(
        string FilePath,
        string? SourceUrl,
        int StartLine,
        int EndLine,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null
    );

    /// <summary>
    /// Reconstructs a method's source text from the full file <paramref name="sourceText"/> and the
    /// sequence-point line range (<paramref name="startLine"/>..<paramref name="endLine"/>, 1-based).
    /// Sequence points cover the body, so this scans backward to capture the signature (skipping
    /// doc comments, attributes, and preprocessor lines) and forward to include the closing brace,
    /// then dedents the block. Line numbers outside the file bounds surface as an
    /// <see cref="IndexOutOfRangeException"/>, which callers already handle by treating the source
    /// as unavailable.
    /// <para>
    /// <paramref name="isDestructor"/> must be set by the caller from the resolved member's
    /// identity (its kind/metadata name), not inferred from source text. A C# destructor's source
    /// line is "~Type(...)", which carries no accessibility keyword and whose metadata name
    /// ("Finalize") does not appear in the text, so the backward scan would otherwise walk past it
    /// into the preceding member and leak unrelated declarations. When set, the scan stops at the
    /// destructor's signature line, recognized via <see cref="IsDestructorSignatureLine"/>.
    /// <paramref name="destructorTypeName"/> — the declaring type's simple name — is the authoritative
    /// discriminator: the signature is "~TypeName" (optionally preceded by "extern"/"unsafe"), so a
    /// line matches only when the tilde is followed by exactly that name as a token and then either
    /// an empty-or-open-paren "(" continuation or end-of-line (for a signature whose parameter list
    /// wraps to a following line). This is robust where a single-line grammar is not: it rejects a
    /// "#line hidden" body complement that can become the first visible sequence point — whether a
    /// bare "~mask;", a field "~Preceding;", or an invocation "~Compute()"/"~Compute(x);" — because
    /// none spell the declaring type name, while still accepting a signature whose "()" wraps onto a
    /// later line. When <paramref name="destructorTypeName"/> is null/empty (callers that cannot
    /// supply it), the matcher falls back to requiring the full parameterless "~Identifier()"
    /// grammar on one line.
    /// </para>
    /// </summary>
    public static string ExtractMethodBody(string sourceText, int startLine, int endLine, string methodName, bool isDestructor = false, string? destructorTypeName = null)
    {
        var lines = sourceText.Split('\n');
        int start = startLine;
        int end = Math.Min(endLine, lines.Length);

        // The declaring type name may arrive namespace-qualified/nested/generic; the source
        // destructor spells only the simple name, so reduce it once up front.
        string? simpleTypeName = string.IsNullOrEmpty(destructorTypeName) ? null : SimpleTypeName(destructorTypeName);

        // Scan backward from the first sequence point to capture the method signature.
        int sigStart = start;
        for (int i = start - 2; i >= Math.Max(0, start - 15); i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("///") || trimmed.StartsWith("//")
                || trimmed.StartsWith("[") || trimmed.StartsWith("#"))
                continue;
            if (trimmed == "{")
                continue;
            if (trimmed.StartsWith("}"))
            {
                sigStart = i + 2;
                break;
            }

            sigStart = i + 1;
            if (trimmed.StartsWith("public") || trimmed.StartsWith("private")
                || trimmed.StartsWith("protected") || trimmed.StartsWith("internal")
                || trimmed.StartsWith("static")
                || (isDestructor && IsDestructorSignatureLine(trimmed, simpleTypeName))
                || trimmed.Contains(methodName))
                break;
        }

        int from = sigStart - 1;
        int to = end;

        // Scan forward to include the closing brace.
        for (int i = to; i < Math.Min(to + 3, lines.Length); i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("}"))
            {
                to = i + 1;
                break;
            }
            if (trimmed.Length > 0)
                break;
        }

        if (from < 0) from = 0;
        if (to > lines.Length) to = lines.Length;

        while (from < to && lines[from].TrimStart().Length == 0)
            from++;

        var methodLines = lines[from..to];

        int minIndent = methodLines
            .Where(l => l.TrimStart().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var dedented = methodLines.Select(l => l.Length >= minIndent ? l[minIndent..] : l);
        return string.Join('\n', dedented).TrimEnd();
    }

    /// <summary>
    /// True when <paramref name="trimmed"/> (a leading-whitespace-stripped line) begins a C#
    /// destructor signature. Used only to locate the signature line within an already-identified
    /// destructor scan (see <see cref="ExtractMethodBody"/>).
    /// <para>
    /// When <paramref name="typeName"/> (the declaring type's simple name) is supplied it is the
    /// authoritative discriminator: after the optional <c>extern</c>/<c>unsafe</c> modifiers and the
    /// tilde, the line must spell exactly that name as a token, then either an opening <c>(</c> or
    /// nothing (a signature whose parameter list wraps to a following line). This distinguishes the
    /// signature from a <c>#line hidden</c> body complement that can become the first visible
    /// sequence point — a bare <c>~mask;</c>, a field <c>~Preceding;</c>, or an invocation
    /// <c>~Compute()</c>/<c>~Compute(x);</c> — because none spell the declaring type name, while
    /// still accepting a wrapped-parenthesis signature. A Unicode-escaped type name
    /// (<c>~\u0043()</c> for <c>~C()</c>) is decoded during the comparison.
    /// </para>
    /// <para>
    /// When <paramref name="typeName"/> is null/empty, the matcher falls back to requiring the full
    /// parameterless <c>~Identifier()</c> grammar on a single line, which still rejects the common
    /// bitwise-complement body lines (they lack the empty <c>()</c>).
    /// </para>
    /// <para>
    /// Known limitations (accepted, out of scope). This is a single-line text heuristic, not a C#
    /// tokenizer, so two exotic valid-C# spellings are not handled: (1) a comment between the tilde
    /// and the type name (<c>~ /*x*/ C()</c>) is not recognized; and (2) a body statement that
    /// bitwise-complements an invocation of a local that shadows the enclosing type name
    /// (<c>~C();</c> where a local named <c>C</c> is in scope) can be mistaken for the signature if
    /// <c>#line hidden</c> makes it the first visible sequence point. Both require a member/local
    /// spelled exactly as the enclosing type under a hidden-line body — combinations that do not
    /// occur in real destructors. Fully resolving them would require multi-line tokenization, which
    /// this Roslyn-free path deliberately avoids.
    /// </para>
    /// </summary>
    internal static bool IsDestructorSignatureLine(string trimmed, string? typeName = null)
    {
        var span = trimmed.AsSpan();
        while (true)
        {
            span = span.TrimStart();
            if (TryStripModifier(ref span, "unsafe") || TryStripModifier(ref span, "extern"))
                continue;
            break;
        }

        if (span.Length == 0 || span[0] != '~')
            return false;

        span = span[1..].TrimStart();

        if (!string.IsNullOrEmpty(typeName))
        {
            // Authoritative match: the tilde must be followed by exactly the declaring type name as
            // a token. A destructor is parameterless, so the remainder is either an opening paren or
            // empty (parameter list wrapped to a later line).
            if (!TryMatchTypeName(span, typeName, out int consumed))
                return false;

            var after = span[consumed..].TrimStart();
            return after.Length == 0 || after[0] == '(';
        }

        // Fallback (no type name): require an identifier then an empty "()" on this line.
        if (span.Length == 0 || !(char.IsLetter(span[0]) || span[0] == '_' || span[0] == '@' || span[0] == '\\'))
            return false;

        int i = 1;
        while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_' || span[i] == '\\'))
            i++;

        span = span[i..].TrimStart();
        if (span.Length == 0 || span[0] != '(')
            return false;

        span = span[1..].TrimStart();
        return span.Length > 0 && span[0] == ')';
    }

    /// <summary>
    /// Matches the declaring type name at the start of <paramref name="span"/> as a complete C#
    /// identifier token, decoding <c>\uXXXX</c>/<c>\UXXXXXXXX</c> escapes and an optional verbatim
    /// <c>@</c> prefix. Succeeds only when the whole <paramref name="typeName"/> is consumed and the
    /// following character is not an identifier-continuation char (so <c>~Computed()</c> does not
    /// match the type name <c>Compute</c>). On success <paramref name="consumed"/> is the number of
    /// source characters matched.
    /// </summary>
    private static bool TryMatchTypeName(ReadOnlySpan<char> span, string typeName, out int consumed)
    {
        consumed = 0;
        int si = 0;
        if (si < span.Length && span[si] == '@')
            si++;

        int ti = 0;
        while (ti < typeName.Length)
        {
            if (si >= span.Length)
                return false;

            char decoded;
            int advance;
            if (span[si] == '\\' && si + 1 < span.Length && (span[si + 1] == 'u' || span[si + 1] == 'U'))
            {
                int digits = span[si + 1] == 'u' ? 4 : 8;
                if (si + 2 + digits > span.Length)
                    return false;
                var hex = span.Slice(si + 2, digits);
                if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int codePoint)
                    || codePoint > 0xFFFF)
                    return false;
                decoded = (char)codePoint;
                advance = 2 + digits;
            }
            else
            {
                decoded = span[si];
                advance = 1;
            }

            if (decoded != typeName[ti])
                return false;

            si += advance;
            ti++;
        }

        // Require a token boundary: the type name must not be a prefix of a longer identifier.
        if (si < span.Length)
        {
            char next = span[si];
            if (char.IsLetterOrDigit(next) || next == '_' || next == '\\')
                return false;
        }

        consumed = si;
        return true;
    }

    /// <summary>
    /// The simple (unqualified, arity-stripped) name of a possibly namespace-qualified and/or nested
    /// type full name — e.g. "NS.Outer+Inner`1" -&gt; "Inner". Used to derive the destructor type
    /// name for <see cref="IsDestructorSignatureLine"/>.
    /// </summary>
    internal static string SimpleTypeName(string typeFullName)
    {
        if (string.IsNullOrEmpty(typeFullName))
            return typeFullName;

        int lastSep = typeFullName.LastIndexOfAny(['.', '+']);
        var segment = lastSep >= 0 ? typeFullName[(lastSep + 1)..] : typeFullName;
        int backtick = segment.IndexOf('`');
        return backtick >= 0 ? segment[..backtick] : segment;
    }

    static bool TryStripModifier(ref ReadOnlySpan<char> span, string modifier)
    {
        if (!span.StartsWith(modifier))
            return false;
        // Require a token boundary so an identifier like "unsafeThing" is not stripped.
        if (span.Length > modifier.Length)
        {
            char next = span[modifier.Length];
            if (char.IsLetterOrDigit(next) || next == '_')
                return false;
        }

        span = span[modifier.Length..];
        return true;
    }

    private SourceLinkResolver(SLF.SourceLinkResolver slfResolver)
    {
        _slfResolver = slfResolver;
    }

    /// <summary>
    /// Creates a SourceLinkResolver from a PDB metadata reader.
    /// Returns null if no SourceLink information is available.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        var slfResolver = SLF.SourceLinkResolver.Create(pdbReader);
        if (slfResolver is null)
            return null;

        return new SourceLinkResolver(slfResolver);
    }

    /// <summary>
    /// Resolves source information for a type by finding a method with debug info.
    /// Falls back to document name matching for interfaces/abstract types without implementations.
    /// Also collects all source files for partial types.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var typeDef = metadata.GetTypeDefinition(typeHandle);
        var typeName = metadata.GetString(typeDef.Name);

        // Collect ALL unique source files from all methods of this type
        var allSourceFiles = CollectAllSourceFiles(metadata, pdb, typeHandle);

        // Also check PDB documents for files matching the type name pattern
        // This catches files that may not have any methods (e.g., partial with only fields)
        var documentFiles = FindDocumentsMatchingTypeName(pdb, typeName);
        foreach (var docFile in documentFiles)
        {
            if (!allSourceFiles.ContainsKey(docFile.FilePath))
            {
                allSourceFiles[docFile.FilePath] = docFile;
            }
        }

        if (allSourceFiles.Count == 0)
        {
            // Fallback: search all documents for a file that matches the type name
            // This works for interfaces and abstract types that have no method implementations
            return ResolveTypeSourceByDocumentName(pdb, typeName);
        }

        // Determine the primary file (prefer {TypeName}.cs over {TypeName}.*.cs)
        var primaryFile = SelectPrimarySourceFile(allSourceFiles.Values.ToList(), typeName);

        // Build additional source files list (excluding primary)
        List<PartialSourceFile> additionalFiles = [];
        if (allSourceFiles.Count > 1)
        {
            additionalFiles = allSourceFiles.Values
                .Where(f => f.FilePath != primaryFile.FilePath)
                .Select(f => new PartialSourceFile(f.FilePath, f.SourceUrl, f.GitHubBrowseUrl))
                .ToList();
        }

        return new TypeSourceInfo(
            primaryFile.FilePath,
            primaryFile.SourceUrl,
            null, // Line number not meaningful for type-level
            primaryFile.GitHubBrowseUrl,
            SourceResolutionMethod.SourceLink
        )
        {
            AdditionalSourceFiles = additionalFiles
        };
    }

    /// <summary>
    /// Resolves source information for a type by name, without requiring a TypeDefinitionHandle.
    /// Finds the type definition handle internally.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, string typeName)
    {
        var typeHandle = FindTypeDefinitionHandle(metadata, typeName);
        if (typeHandle == null)
            return null;

        return ResolveTypeSource(metadata, pdb, typeHandle.Value);
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink document mappings.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => _slfResolver.ExtractRepositoryUrl();

    /// <summary>
    /// Extracts the repository URL from a PDB reader's SourceLink information.
    /// </summary>
    public static string? ExtractRepositoryUrl(MetadataReader pdbReader)
    {
        var resolver = Create(pdbReader);
        return resolver?.ExtractRepositoryUrl();
    }

    /// <summary>
    /// Extracts the commit hash from SourceLink URL patterns.
    /// </summary>
    public string? ExtractCommitHash()
        => _slfResolver.ExtractCommitHash();

    /// <summary>
    /// Finds a TypeDefinitionHandle by type name, preferring a full-name match over a simple-name
    /// match. Uses a per-reader index so repeated lookups don't re-scan all TypeDefinitions.
    /// </summary>
    private TypeDefinitionHandle? FindTypeDefinitionHandle(MetadataReader reader, string typeName)
    {
        if (_typeIndexReader != reader || _fullNameIndex == null)
        {
            var fullNames = new Dictionary<string, TypeDefinitionHandle>(StringComparer.OrdinalIgnoreCase);
            var simpleNames = new Dictionary<string, TypeDefinitionHandle>(StringComparer.OrdinalIgnoreCase);
            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                // TryAdd preserves the original "first row wins" behavior on duplicate names.
                fullNames.TryAdd(reader.GetFullTypeName(typeDef), typeDefHandle);
                simpleNames.TryAdd(reader.GetString(typeDef.Name), typeDefHandle);
            }
            _fullNameIndex = fullNames;
            _simpleNameIndex = simpleNames;
            _typeIndexReader = reader;
        }

        if (_fullNameIndex.TryGetValue(typeName, out var handle))
            return handle;
        if (_simpleNameIndex!.TryGetValue(typeName, out handle))
            return handle;
        return null;
    }

    /// <summary>
    /// Collects all unique source files from all methods of a type.
    /// </summary>
    private Dictionary<string, PartialSourceFile> CollectAllSourceFiles(
        MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var sourceFiles = new Dictionary<string, PartialSourceFile>(StringComparer.OrdinalIgnoreCase);
        var typeDef = metadata.GetTypeDefinition(typeHandle);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var sourceInfo = ResolveMethodSource(pdb, methodHandle);
            if (sourceInfo?.SourceFilePath != null && !sourceFiles.ContainsKey(sourceInfo.SourceFilePath))
            {
                sourceFiles[sourceInfo.SourceFilePath] = new PartialSourceFile(
                    sourceInfo.SourceFilePath,
                    sourceInfo.SourceUrl,
                    sourceInfo.GitHubBrowseUrl
                );
            }
        }

        return sourceFiles;
    }

    /// <summary>
    /// Finds PDB documents matching the type name pattern (e.g., JObject.cs, JObject.Async.cs).
    /// </summary>
    private List<PartialSourceFile> FindDocumentsMatchingTypeName(MetadataReader pdb, string typeName)
    {
        // Index documents by the filename segment before the first '.', so {TypeName}.cs and
        // {TypeName}.*.cs both bucket under {TypeName}. Built once per PDB reader instead of
        // scanning every Document for each type.
        if (_docIndexReader != pdb || _docsByFirstSegment == null)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var docHandle in pdb.Documents)
            {
                string filePath = pdb.GetString(pdb.GetDocument(docHandle).Name);
                string fileName = Path.GetFileName(filePath);
                int firstDot = fileName.IndexOf('.');
                string segment = firstDot >= 0 ? fileName[..firstDot] : fileName;
                if (!index.TryGetValue(segment, out var list))
                    index[segment] = list = [];
                list.Add(filePath);
            }
            _docsByFirstSegment = index;
            _docIndexReader = pdb;
        }

        if (!_docsByFirstSegment.TryGetValue(typeName, out var candidates))
            return [];

        // Within the bucket the first segment already equals typeName, so the original pattern
        // reduces to "ends with .cs".
        List<PartialSourceFile> matches = [];
        foreach (var filePath in candidates)
        {
            if (!Path.GetFileName(filePath).EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
            matches.Add(new PartialSourceFile(filePath, sourceUrl, browseUrl));
        }

        return matches;
    }

    /// <summary>
    /// Selects the primary source file from a list of candidates.
    /// Prefers {TypeName}.cs over {TypeName}.*.cs patterns.
    /// </summary>
    private static PartialSourceFile SelectPrimarySourceFile(List<PartialSourceFile> files, string typeName)
    {
        // Prefer exact match: {TypeName}.cs
        var primaryPattern = $"{typeName}.cs";
        var primary = files.FirstOrDefault(f =>
            Path.GetFileName(f.FilePath).Equals(primaryPattern, StringComparison.OrdinalIgnoreCase));

        if (primary != null)
            return primary;

        // Otherwise, return the first one (or the one with shortest name)
        return files.OrderBy(f => Path.GetFileName(f.FilePath).Length).First();
    }

    /// <summary>
    /// Attempts to resolve source info by searching PDB documents for a matching file name.
    /// Used for interfaces and types without method implementations.
    /// </summary>
    private TypeSourceInfo? ResolveTypeSourceByDocumentName(MetadataReader pdb, string typeName)
    {
        foreach (var docHandle in pdb.Documents)
        {
            var document = pdb.GetDocument(docHandle);
            string filePath = pdb.GetString(document.Name);

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                string? sourceUrl = ApplySourceLinkMapping(filePath);
                string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
                return new TypeSourceInfo(filePath, sourceUrl, null, browseUrl, SourceResolutionMethod.Inferred);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves source information for a specific method.
    /// </summary>
    public TypeSourceInfo? ResolveMethodSource(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int? lineNumber = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (!sp.IsHidden)
                {
                    lineNumber = sp.StartLine;
                    break;
                }
            }

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);

            return new TypeSourceInfo(filePath, sourceUrl, lineNumber, browseUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the full source line range for a method using all sequence points.
    /// Returns null if the method has no debug info.
    /// </summary>
    public MethodSourceInfo? ResolveMethodSourceRange(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int minLine = int.MaxValue, maxLine = 0;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                if (sp.StartLine < minLine) minLine = sp.StartLine;
                if (sp.EndLine > maxLine) maxLine = sp.EndLine;
            }

            if (minLine == int.MaxValue)
                return null;

            byte[]? checksum = null;
            string? checksumAlgorithm = null;
            if (!document.Hash.IsNil)
            {
                checksum = pdb.GetBlobBytes(document.Hash);
                checksumAlgorithm = PdbContext.MapHashAlgorithm(pdb.GetGuid(document.HashAlgorithm));
            }

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            return new MethodSourceInfo(filePath, sourceUrl, minLine, maxLine, checksum, checksumAlgorithm);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Source location resolved from a method token and IL offset.
    /// </summary>
    public record ILOffsetSourceInfo(
        string? MethodName,
        string FilePath,
        string? SourceUrl,
        int Line,
        int MatchedOffset,
        string? GitHubBrowseUrl);

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset
    /// by walking PDB sequence points. Applies SourceLink URL mapping when available.
    /// </summary>
    public ILOffsetSourceInfo? ResolveByILOffset(MetadataReader metadata, MetadataReader pdb, int methodToken, int ilOffset)
    {
        if (ResolveByILOffsetDirect(metadata, pdb, methodToken, ilOffset) is not { } info)
            return null;

        string? sourceUrl = ApplySourceLinkMapping(info.FilePath);
        return info with { SourceUrl = sourceUrl, GitHubBrowseUrl = ConvertToGitHubBrowseUrl(sourceUrl) };
    }

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset by walking
    /// PDB sequence points, returning the last visible point at or before the requested offset.
    /// Uses only the PDB reader (no SourceLink URL mapping); used when no resolver is available.
    /// </summary>
    public static ILOffsetSourceInfo? ResolveByILOffsetDirect(MetadataReader metadata, MetadataReader pdb, int methodToken, int ilOffset)
    {
        try
        {
            var handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            var methodDefHandle = (MethodDefinitionHandle)handle;

            var methodDef = metadata.GetMethodDefinition(methodDefHandle);
            var typeDef = metadata.GetTypeDefinition(methodDef.GetDeclaringType());
            string methodName = $"{metadata.GetFullTypeName(typeDef)}.{metadata.GetString(methodDef.Name)}";

            var debugInfo = pdb.GetMethodDebugInformation(methodDefHandle.ToDebugInformationHandle());
            if (debugInfo.SequencePointsBlob.IsNil)
                return null;

            SequencePoint? bestPoint = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.Offset > ilOffset)
                    break;

                if (!sp.IsHidden)
                    bestPoint = sp;
            }

            if (bestPoint is not { } point)
                return null;

            var document = pdb.GetDocument(point.Document);
            string filePath = pdb.GetString(document.Name);

            return new ILOffsetSourceInfo(methodName, filePath, null, point.StartLine, point.Offset, null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies SourceLink URL pattern to convert a file path to a source URL.
    /// </summary>
    public string? ApplySourceLinkMapping(string filePath)
        => _slfResolver.ResolveUrl(filePath);

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com browse URL.
    /// </summary>
    private static string? ConvertToGitHubBrowseUrl(string? rawUrl)
        => SLF.SourceLinkResolver.ConvertToGitHubBrowseUrl(rawUrl);

}
