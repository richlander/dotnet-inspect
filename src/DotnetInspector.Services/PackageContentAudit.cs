using System.Text;
using System.Xml;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Services;

/// <summary>The kind of evidence found while auditing package content and provenance.</summary>
public enum PackageContentFindingKind
{
    /// <summary>One line required visual encoding before it could be rendered safely.</summary>
    NonGraphicText,

    /// <summary>A SourceLink document mapping required visual encoding.</summary>
    NonGraphicSourceLinkText,

    /// <summary>A SourceLink document key or URL contains a parent-path reference.</summary>
    SourceLinkParentPathSegment,

    /// <summary>A SourceLink document map could not be interpreted.</summary>
    InvalidSourceLinkMap,

    /// <summary>A SourceLink document mapping was rejected by the SourceLink grammar.</summary>
    RejectedSourceLinkMapping,

    /// <summary>A NuGet configuration clears the package sources inherited from its parent.</summary>
    RestoreSourcesCleared,

    /// <summary>A NuGet configuration declares a package source.</summary>
    PackageSourceDeclared,

    /// <summary>A text-bearing file could not be decoded under a supported strict encoding.</summary>
    InvalidTextEncoding,

    /// <summary>A NuGet configuration could not be parsed as XML.</summary>
    InvalidNuGetConfiguration,

    /// <summary>A candidate file exceeded an audit resource limit.</summary>
    ScanLimit,

    /// <summary>A candidate file could not be read.</summary>
    ReadFailure,
}

/// <summary>
/// One package-content audit finding. Artifact text is retained only in visually encoded form.
/// </summary>
/// <param name="Path">Package-relative path of the file that supplied the evidence.</param>
/// <param name="Kind">The typed reason the row exists.</param>
/// <param name="Concerns">Unicode concern kinds for <see cref="PackageContentFindingKind.NonGraphicText"/>.</param>
/// <param name="EncodedText">The relevant line or a tool-authored failure description, safe for a prose sink.</param>
/// <param name="Line">One-based source line when the finding came from a line of text.</param>
public sealed record PackageContentAuditFinding(
    string Path,
    PackageContentFindingKind Kind,
    TextConcern Concerns,
    InertString EncodedText,
    int? Line = null);

/// <summary>The completed bounded audit of text-bearing files in one extracted package.</summary>
public sealed record PackageContentAuditResult(
    IReadOnlyList<PackageContentAuditFinding> Findings,
    int EligibleFiles,
    int ScannedFiles,
    long ScannedBytes,
    bool Complete,
    int ScannedSourceLinkMaps = 0);

/// <summary>
/// Finds rendering controls and restore-source declarations in text-bearing package files, and
/// audits decoded SourceLink mappings carried by package PDBs.
/// </summary>
/// <remarks>
/// The scanner is explicit-only at the command layer. It bounds each file and the aggregate
/// byte count, decodes supported Unicode encodings strictly, and never returns untreated file
/// content. <c>PackageContentAuditTests</c> gates the adversarial package-shaped bidi, OSC 52, NuGet
/// configuration, encoding-marker, and resource-limit cases.
/// </remarks>
public static class PackageContentAudit
{
    internal const int MaxFileBytes = 4 * 1024 * 1024;
    internal const int MaxTotalBytes = 32 * 1024 * 1024;
    internal const int MaxCandidatePaths = 16 * 1024;
    internal const int MaxTextFiles = 4096;
    internal const int MaxFindings = 4096;
    internal const int MaxEvidenceCharacters = 2 * 1024 * 1024;
    internal const int MaxSourceLinkMappings = 16 * 1024;
    internal const int MaxSourceLinkMapBytes = 4 * 1024 * 1024;
    internal const int MaxTotalSourceLinkMapBytes = 32 * 1024 * 1024;
    internal const int MaxEmbeddedPdbBytes = 64 * 1024 * 1024;
    internal const int MaxTotalEmbeddedPdbBytes = 256 * 1024 * 1024;
    internal const int MaxSourceLinkCarriers = 256;
    internal const long MaxSourceLinkCarrierBytes = 64L * 1024 * 1024;
    internal const long MaxTotalSourceLinkCarrierBytes = 256L * 1024 * 1024;
    private const int MaxEncodedTextLength = 512;
    private const int MaxNuGetConfigurationDepth = 64;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cjs", ".cmd", ".config", ".cs", ".cshtml", ".csproj", ".css",
        ".editorconfig", ".fs", ".fsproj", ".htm", ".html", ".ini", ".js", ".json",
        ".jsonc", ".jsx", ".less", ".markdown", ".md", ".mjs", ".nuspec", ".props",
        ".ps1", ".razor", ".rsp", ".sass", ".scss", ".sh", ".svg", ".targets", ".toml",
        ".ts", ".tsx", ".txt", ".vb", ".vbproj", ".xml", ".yaml", ".yml",
    };

    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".editorconfig", ".gitignore", "AGENTS.md", "AUTHORS", "CHANGELOG", "LICENSE",
        "NOTICE", "PACKAGE.md", "PROJECT.md", "README", "README.md", "SKILL.md",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".a", ".br", ".bz2", ".dll", ".dylib", ".exe", ".gif", ".gz", ".ico",
        ".jpeg", ".jpg", ".lib", ".nupkg", ".pdb", ".png", ".snupkg", ".so", ".tar",
        ".webp", ".zip",
    };

    /// <summary>Audits the supplied package-relative paths beneath an extracted package root.</summary>
    public static PackageContentAuditResult Scan(
        string extractPath,
        IEnumerable<string> packageRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractPath);
        ArgumentNullException.ThrowIfNull(packageRelativePaths);

        string root = Path.GetFullPath(extractPath);
        var collector = new FindingCollector();
        int eligibleFiles = 0;
        int scannedFiles = 0;
        long scannedBytes = 0;

        string[] paths =
            NormalizePackagePaths(
                packageRelativePaths,
                MaxCandidatePaths,
                out bool candidateLimitReached);
        if (candidateLimitReached)
        {
            collector.AddIncomplete(ToolFinding(
                "<package>",
                PackageContentFindingKind.ScanLimit,
                $"Package audit exceeded the {MaxCandidatePaths} candidate-path limit."));
        }

        foreach (string relativePath in paths)
        {
            if (collector.Saturated)
                break;
            if (!IsTextBearingPath(relativePath))
                continue;
            if (eligibleFiles >= MaxTextFiles)
            {
                collector.AddIncomplete(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ScanLimit,
                    $"Package audit exceeded the {MaxTextFiles} text-file limit."));
                break;
            }

            eligibleFiles++;
            string fullPath;
            try
            {
                fullPath = ResolveBeneath(root, relativePath);
            }
            catch (ArgumentException)
            {
                collector.AddIncomplete(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ReadFailure,
                    "Path could not be resolved beneath the extracted package."));
                continue;
            }

            byte[] bytes;
            try
            {
                using FileStream stream = new(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                long length = stream.Length;
                if (length > MaxFileBytes || scannedBytes + length > MaxTotalBytes)
                {
                    collector.AddIncomplete(ToolFinding(
                        relativePath,
                        PackageContentFindingKind.ScanLimit,
                        length > MaxFileBytes
                            ? $"File exceeds the {MaxFileBytes / (1024 * 1024)} MiB per-file audit limit."
                            : $"Package exceeds the {MaxTotalBytes / (1024 * 1024)} MiB aggregate audit limit."));
                    continue;
                }

                bytes = GC.AllocateUninitializedArray<byte>((int)length);
                stream.ReadExactly(bytes);
                if (stream.ReadByte() >= 0)
                {
                    collector.AddIncomplete(ToolFinding(
                        relativePath,
                        PackageContentFindingKind.ScanLimit,
                        "File grew while it was being read and exceeded its bounded audit snapshot."));
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                collector.AddIncomplete(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ReadFailure,
                    "File content could not be read."));
                continue;
            }

            scannedBytes += bytes.Length;
            if (!TryDecode(bytes, out string? content))
            {
                collector.AddIncomplete(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.InvalidTextEncoding,
                    "Text-bearing file is not valid UTF-8, UTF-16, or UTF-32."));
                continue;
            }

            scannedFiles++;
            using var lineReader = new StringReader(content!);
            int lineNumber = 0;
            while (!collector.Saturated && lineReader.ReadLine() is { } line)
            {
                lineNumber++;
                InertString encoded = new(TextPolicy.Prose, line);
                if (!encoded.RequiredContainment)
                    continue;

                collector.TryAdd(new PackageContentAuditFinding(
                    relativePath,
                    PackageContentFindingKind.NonGraphicText,
                    encoded.Concerns,
                    BoundAroundFirstConcern(encoded),
                    lineNumber));
            }

            if (IsNuGetConfig(relativePath) && !collector.Saturated)
                AddNuGetConfigurationFindings(relativePath, content!, collector);
        }

        int scannedSourceLinkMaps = AddSourceLinkFindings(
            root,
            paths,
            collector);

        return new PackageContentAuditResult(
            collector.Findings,
            eligibleFiles,
            scannedFiles,
            scannedBytes,
            collector.Complete,
            scannedSourceLinkMaps);
    }

    private static int AddSourceLinkFindings(
        string root,
        IReadOnlyList<string> packagePaths,
        FindingCollector collector)
    {
        int scannedMaps = 0;
        int scannedMappings = 0;
        int scannedMapBytes = 0;
        int scannedAssemblyCarriers = 0;
        int scannedPdbCarriers = 0;
        long scannedCarrierBytes = 0;
        var embeddedPdbBudget =
            new PdbExpansionBudget(MaxTotalEmbeddedPdbBytes);

        foreach (string assemblyPath in packagePaths.Where(static path =>
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            if (collector.Saturated)
                break;
            if (!CanInspectAnotherSourceLinkMap(
                    assemblyPath,
                    collector,
                    scannedMappings,
                    scannedMapBytes))
                break;
            if (!TryAccountSourceLinkCarrier(
                    assemblyPath,
                    collector,
                    ref scannedAssemblyCarriers))
                break;
            if (embeddedPdbBudget.RemainingBytes == 0)
            {
                collector.AddIncomplete(ToolFinding(
                    assemblyPath,
                    PackageContentFindingKind.ScanLimit,
                    $"Embedded PDBs reached the {MaxTotalEmbeddedPdbBytes / (1024 * 1024)} MiB aggregate decompression limit."));
                break;
            }
            if (!TryResolveAndAccountSourceLinkCarrier(
                    root,
                    assemblyPath,
                    collector,
                    ref scannedCarrierBytes,
                    out string? fullAssemblyPath))
                continue;
            if (!HasPeHeader(fullAssemblyPath, assemblyPath, collector))
                continue;

            try
            {
                var limits = new SourceLinkReadLimits(
                    MaxEmbeddedPdbBytes,
                    Math.Min(
                        MaxSourceLinkMapBytes,
                        MaxTotalSourceLinkMapBytes - scannedMapBytes),
                    MaxSourceLinkMappings - scannedMappings,
                    embeddedPdbBudget);
                using SourceLinkService source =
                    SourceLinkService.OpenEmbeddedPdbOnly(
                        fullAssemblyPath,
                        limits);
                if (!source.Context.HasMetadata || !source.Context.HasEmbeddedPdb)
                    continue;

                SourceLinkMapAudit audit = source.InspectSourceLinkMap();
                if (!AddSourceLinkMapFindings(
                        assemblyPath,
                        audit,
                        collector,
                        ref scannedMaps,
                        ref scannedMappings,
                        ref scannedMapBytes))
                    break;
            }
            catch (PdbResourceLimitException ex)
            {
                collector.AddIncomplete(ToolFinding(
                    assemblyPath,
                    PackageContentFindingKind.ScanLimit,
                    ex.Message));
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException
                    or ArgumentOutOfRangeException)
            {
                collector.AddIncomplete(ToolFinding(
                    assemblyPath,
                    PackageContentFindingKind.ReadFailure,
                    "Managed PE content could not be opened for embedded SourceLink inspection."));
            }
        }

        foreach (string pdbPath in packagePaths.Where(static path =>
            Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase)))
        {
            if (collector.Saturated)
                break;
            if (!CanInspectAnotherSourceLinkMap(
                    pdbPath,
                    collector,
                    scannedMappings,
                    scannedMapBytes))
                break;
            if (!TryAccountSourceLinkCarrier(
                    pdbPath,
                    collector,
                    ref scannedPdbCarriers))
                break;
            if (!TryResolveAndAccountSourceLinkCarrier(
                    root,
                    pdbPath,
                    collector,
                    ref scannedCarrierBytes,
                    out string? fullPdbPath))
                continue;
            if (!IsPortablePdb(fullPdbPath, pdbPath, collector))
                continue;

            try
            {
                SourceLinkMapAudit audit = SourceLinkService.InspectPortablePdb(
                    fullPdbPath,
                    Math.Min(
                        MaxSourceLinkMapBytes,
                        MaxTotalSourceLinkMapBytes - scannedMapBytes),
                    MaxSourceLinkMappings - scannedMappings);
                if (!AddSourceLinkMapFindings(
                        pdbPath,
                        audit,
                        collector,
                        ref scannedMaps,
                        ref scannedMappings,
                        ref scannedMapBytes))
                    break;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException
                    or ArgumentOutOfRangeException)
            {
                collector.AddIncomplete(ToolFinding(
                    pdbPath,
                    PackageContentFindingKind.InvalidSourceLinkMap,
                    "Portable PDB content could not be read for SourceLink inspection."));
            }
        }

        return scannedMaps;
    }

    private static bool TryAccountSourceLinkCarrier(
        string evidencePath,
        FindingCollector collector,
        ref int scannedCarriers)
    {
        if (scannedCarriers >= MaxSourceLinkCarriers)
        {
            collector.AddIncomplete(ToolFinding(
                evidencePath,
                PackageContentFindingKind.ScanLimit,
                $"SourceLink audit exceeded the {MaxSourceLinkCarriers} carrier limit."));
            return false;
        }

        scannedCarriers++;
        return true;
    }

    private static bool CanInspectAnotherSourceLinkMap(
        string evidencePath,
        FindingCollector collector,
        int scannedMappings,
        int scannedMapBytes)
    {
        if (scannedMappings >= MaxSourceLinkMappings)
        {
            collector.AddLimit(
                evidencePath,
                $"SourceLink audit exceeded the {MaxSourceLinkMappings} mapping limit.");
            return false;
        }

        if (scannedMapBytes >= MaxTotalSourceLinkMapBytes)
        {
            collector.AddLimit(
                evidencePath,
                $"SourceLink maps exceed the {MaxTotalSourceLinkMapBytes / (1024 * 1024)} MiB aggregate audit limit.");
            return false;
        }

        return true;
    }

    private static bool AddSourceLinkMapFindings(
        string evidencePath,
        SourceLinkMapAudit audit,
        FindingCollector collector,
        ref int scannedMaps,
        ref int scannedMappings,
        ref int scannedMapBytes)
    {
        if (!audit.Map.IsPresent)
            return true;

        scannedMaps++;
        if (audit.LimitKind == SourceLinkMapLimitKind.Mappings)
        {
            collector.AddLimit(
                evidencePath,
                $"SourceLink audit exceeded the {MaxSourceLinkMappings} mapping limit.");
            return false;
        }

        if (audit.LimitKind == SourceLinkMapLimitKind.EncodedBytes
            || audit.EncodedBytes > MaxSourceLinkMapBytes
            || scannedMapBytes + audit.EncodedBytes > MaxTotalSourceLinkMapBytes)
        {
            collector.AddIncomplete(ToolFinding(
                evidencePath,
                PackageContentFindingKind.ScanLimit,
                audit.EncodedBytes > MaxSourceLinkMapBytes
                    ? $"SourceLink map exceeds the {MaxSourceLinkMapBytes / (1024 * 1024)} MiB per-map audit limit."
                    : $"SourceLink maps exceed the {MaxTotalSourceLinkMapBytes / (1024 * 1024)} MiB aggregate audit limit."));
            return true;
        }
        scannedMapBytes += audit.EncodedBytes;

        if (audit.Map.Error is { } error)
        {
            collector.AddIncomplete(ToolFinding(
                evidencePath,
                PackageContentFindingKind.InvalidSourceLinkMap,
                error));
        }

        var rejected = audit.Map.RejectedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (SourceLinkMapEntry mapping in audit.Entries)
        {
            if (scannedMappings >= MaxSourceLinkMappings)
            {
                collector.AddLimit(
                    evidencePath,
                    $"SourceLink audit exceeded the {MaxSourceLinkMappings} mapping limit.");
                return false;
            }
            scannedMappings++;

            string evidence = mapping.Url is null
                ? mapping.Document
                : $"{mapping.Document} => {mapping.Url}";
            InertString encoded = EncodeSourceLinkEvidence(evidence);
            if (encoded.RequiredContainment)
            {
                collector.TryAdd(new PackageContentAuditFinding(
                    evidencePath,
                    PackageContentFindingKind.NonGraphicSourceLinkText,
                    encoded.Concerns,
                    BoundAroundFirstConcern(encoded)));
            }

            if (ContainsParentPathReference(mapping.Document)
                || ContainsParentPathReference(mapping.Url))
            {
                collector.TryAdd(new PackageContentAuditFinding(
                    evidencePath,
                    PackageContentFindingKind.SourceLinkParentPathSegment,
                    TextConcern.None,
                    BoundAroundLiteral(encoded, "../")));
            }

            if (rejected.Contains(mapping.Document))
            {
                collector.TryAdd(new PackageContentAuditFinding(
                    evidencePath,
                    PackageContentFindingKind.RejectedSourceLinkMapping,
                    TextConcern.None,
                    BoundAroundFirstConcern(encoded)));
            }

            if (collector.Saturated)
                return false;
        }

        return true;
    }

    private static bool TryResolveAndAccountSourceLinkCarrier(
        string root,
        string relativePath,
        FindingCollector collector,
        ref long scannedCarrierBytes,
        out string fullPath)
    {
        try
        {
            fullPath = ResolveBeneath(root, relativePath);
            long length = new FileInfo(fullPath).Length;
            if (length > MaxSourceLinkCarrierBytes
                || scannedCarrierBytes + length > MaxTotalSourceLinkCarrierBytes)
            {
                collector.AddIncomplete(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ScanLimit,
                    length > MaxSourceLinkCarrierBytes
                        ? $"SourceLink carrier exceeds the {MaxSourceLinkCarrierBytes / (1024 * 1024)} MiB per-file audit limit."
                        : $"SourceLink carriers exceed the {MaxTotalSourceLinkCarrierBytes / (1024 * 1024)} MiB aggregate audit limit."));
                return false;
            }

            scannedCarrierBytes += length;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            fullPath = string.Empty;
            collector.AddIncomplete(ToolFinding(
                relativePath,
                PackageContentFindingKind.ReadFailure,
                "SourceLink carrier could not be resolved or measured."));
            return false;
        }
    }

    private static bool IsPortablePdb(
        string fullPath,
        string relativePath,
        FindingCollector collector)
    {
        try
        {
            Span<byte> header = stackalloc byte[64];
            using FileStream stream = File.OpenRead(fullPath);
            int read = stream.ReadAtLeast(
                header,
                header.Length,
                throwOnEndOfStream: false);
            ReadOnlySpan<byte> available = header[..read];
            if (available.StartsWith("BSJB"u8))
                return true;
            if (IsWindowsPdbHeader(available))
                return false;

            collector.AddIncomplete(ToolFinding(
                relativePath,
                PackageContentFindingKind.InvalidSourceLinkMap,
                "PDB content is truncated or has an unrecognized format."));
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            collector.AddIncomplete(ToolFinding(
                relativePath,
                PackageContentFindingKind.ReadFailure,
                "PDB header could not be read."));
            return false;
        }
    }

    private static bool IsWindowsPdbHeader(ReadOnlySpan<byte> header)
        => header.StartsWith("Microsoft C/C++ MSF "u8)
            || header.StartsWith("Microsoft C/C++ program database "u8);

    private static bool HasPeHeader(
        string fullPath,
        string relativePath,
        FindingCollector collector)
    {
        try
        {
            Span<byte> header = stackalloc byte[2];
            using FileStream stream = File.OpenRead(fullPath);
            int read = stream.ReadAtLeast(
                header,
                header.Length,
                throwOnEndOfStream: false);
            return read == header.Length
                && header[0] == (byte)'M'
                && header[1] == (byte)'Z';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            collector.AddIncomplete(ToolFinding(
                relativePath,
                PackageContentFindingKind.ReadFailure,
                "PE header could not be read."));
            return false;
        }
    }

    /// <summary>
    /// Applies the deliberately review-oriented SourceLink rule: any literal <c>../</c> merits
    /// inspection, without claiming that the mapping is malicious or unusable.
    /// </summary>
    /// <remarks>
    /// <c>PackageContentAuditTests.ParentPathRule_IsLiteralAndReviewOriented</c> gates the exact
    /// positive and close-negative boundary.
    /// </remarks>
    internal static bool ContainsParentPathReference(string? text)
        => text?.Contains("../", StringComparison.Ordinal) == true;

    internal static string[] NormalizePackagePaths(IEnumerable<string> paths)
        =>
        [
            .. paths
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    private static string[] NormalizePackagePaths(
        IEnumerable<string> paths,
        int maxCandidates,
        out bool limitReached)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int candidates = 0;
        limitReached = false;
        foreach (string path in paths)
        {
            if (candidates >= maxCandidates)
            {
                limitReached = true;
                break;
            }
            candidates++;
            if (seen.Add(path))
                normalized.Add(path);
        }

        normalized.Sort(StringComparer.Ordinal);
        return [.. normalized];
    }

    internal static InertString EncodeSourceLinkEvidence(string evidence)
        => new(TextPolicy.Field, evidence);

    private static bool IsTextBearingPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        string extension = Path.GetExtension(fileName);
        if (TextExtensions.Contains(extension) || TextFileNames.Contains(fileName))
            return true;

        if (BinaryExtensions.Contains(extension))
            return false;

        return normalized.StartsWith("content/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("build/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("skills/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNuGetConfig(string path)
        => Path.GetFileName(path).Equals("nuget.config", StringComparison.OrdinalIgnoreCase);

    private static string ResolveBeneath(string root, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)
            && !fullPath.Equals(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Package path escapes the extracted root.", nameof(relativePath));
        }

        return fullPath;
    }

    private static bool TryDecode(ReadOnlySpan<byte> bytes, out string? content)
    {
        try
        {
            if (bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                content = new UTF8Encoding(false, true).GetString(bytes[3..]);
                return true;
            }
            if (bytes.Length >= 4
                && bytes[0] == 0xFF && bytes[1] == 0xFE
                && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                content = new UTF32Encoding(false, false, true).GetString(bytes[4..]);
                return true;
            }
            if (bytes.Length >= 4
                && bytes[0] == 0x00 && bytes[1] == 0x00
                && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                content = new UTF32Encoding(true, false, true).GetString(bytes[4..]);
                return true;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                content = new UnicodeEncoding(false, false, true).GetString(bytes[2..]);
                return true;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                content = new UnicodeEncoding(true, false, true).GetString(bytes[2..]);
                return true;
            }

            content = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            content = null;
            return false;
        }
    }

    private static InertString BoundAroundFirstConcern(InertString encoded)
        => BoundAroundIndex(encoded, Math.Max(0, encoded.IndexOfFirstConcern()));

    private static InertString BoundAroundLiteral(InertString encoded, string literal)
        => BoundAroundIndex(
            encoded,
            Math.Max(0, encoded.ToString().IndexOf(literal, StringComparison.Ordinal)));

    private static InertString BoundAroundIndex(InertString encoded, int first)
    {
        if (encoded.Length <= MaxEncodedTextLength)
            return encoded;

        int start = Math.Max(0, first - (MaxEncodedTextLength / 3));
        int end = Math.Min(encoded.Length, start + MaxEncodedTextLength);
        start = Math.Max(0, end - MaxEncodedTextLength);
        InertString window = encoded.Truncate(start..end);

        return (start > 0, end < encoded.Length) switch
        {
            (true, true) => InertString.Format(TextPolicy.Prose, $"…{window}…"),
            (true, false) => InertString.Format(TextPolicy.Prose, $"…{window}"),
            (false, true) => InertString.Format(TextPolicy.Prose, $"{window}…"),
            _ => window,
        };
    }

    private static void AddNuGetConfigurationFindings(
        string path,
        string content,
        FindingCollector collector)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxFileBytes,
            };
            using var stringReader = new StringReader(content);
            using var reader = XmlReader.Create(stringReader, settings);
            bool rootSeen = false;
            bool insidePackageSources = false;
            while (reader.Read())
            {
                if (reader.Depth > MaxNuGetConfigurationDepth)
                {
                    collector.AddIncomplete(ToolFinding(
                        path,
                        PackageContentFindingKind.ScanLimit,
                        $"NuGet configuration exceeded the {MaxNuGetConfigurationDepth}-level XML depth limit."));
                    return;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0)
                {
                    rootSeen = true;
                    if (!reader.LocalName.Equals("configuration", StringComparison.OrdinalIgnoreCase))
                    {
                        collector.AddIncomplete(ToolFinding(
                            path,
                            PackageContentFindingKind.InvalidNuGetConfiguration,
                            "NuGet configuration does not have a configuration root element."));
                        return;
                    }
                }

                if (!rootSeen)
                    continue;

                if (reader.NodeType == XmlNodeType.Element
                    && reader.Depth == 1
                    && reader.LocalName.Equals("packageSources", StringComparison.Ordinal))
                {
                    insidePackageSources = !reader.IsEmptyElement;
                    continue;
                }

                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == 1
                    && reader.LocalName.Equals("packageSources", StringComparison.Ordinal))
                {
                    insidePackageSources = false;
                    continue;
                }

                if (!insidePackageSources
                    || reader.NodeType != XmlNodeType.Element
                    || reader.Depth != 2)
                {
                    continue;
                }

                if (collector.Saturated)
                    return;

                PackageContentFindingKind? kind = reader.LocalName switch
                {
                    var name when name.Equals("clear", StringComparison.OrdinalIgnoreCase) =>
                        PackageContentFindingKind.RestoreSourcesCleared,
                    var name when name.Equals("add", StringComparison.OrdinalIgnoreCase) =>
                        PackageContentFindingKind.PackageSourceDeclared,
                    _ => null,
                };
                if (kind is null)
                    continue;

                int line = (reader as IXmlLineInfo)?.HasLineInfo() == true
                    ? ((IXmlLineInfo)reader).LineNumber
                    : 0;
                InertString evidence = EncodeNuGetConfigurationEvidence(reader);
                collector.TryAdd(new PackageContentAuditFinding(
                    path,
                    kind.Value,
                    TextConcern.None,
                    BoundAroundFirstConcern(evidence),
                    line > 0 ? line : null));
            }
        }
        catch (XmlException)
        {
            collector.AddIncomplete(ToolFinding(
                path,
                PackageContentFindingKind.InvalidNuGetConfiguration,
                "NuGet configuration could not be parsed as XML."));
        }
    }

    private static InertString EncodeNuGetConfigurationEvidence(XmlReader reader)
    {
        var evidence = new StringBuilder(MaxEncodedTextLength);
        bool complete = AppendEvidenceToken(evidence, "<")
            && AppendEvidenceToken(evidence, reader.LocalName);
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (reader.NamespaceURI.Length > 0)
                    continue;

                complete = complete
                    && AppendEvidenceToken(evidence, " ")
                    && AppendEvidenceToken(evidence, reader.LocalName)
                    && AppendEvidenceToken(evidence, "=\"")
                    && AppendXmlAttributeValue(evidence, reader.Value)
                    && AppendEvidenceToken(evidence, "\"");
                if (!complete)
                    break;
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }
        complete = complete && AppendEvidenceToken(evidence, " />");

        if (!complete)
            evidence.Append('…');

        return new InertString(TextPolicy.Prose, evidence.ToString());
    }

    private static bool AppendXmlAttributeValue(StringBuilder destination, string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            string? escaped = value[index] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => null,
            };
            if (escaped is not null)
            {
                if (!AppendEvidenceToken(destination, escaped))
                    return false;
                continue;
            }

            int length = char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1])
                    ? 2
                    : 1;
            if (!AppendEvidenceToken(destination, value.AsSpan(index, length)))
                return false;
            index += length - 1;
        }

        return true;
    }

    private static bool AppendEvidenceToken(StringBuilder destination, string value)
        => AppendEvidenceToken(destination, value.AsSpan());

    private static bool AppendEvidenceToken(StringBuilder destination, ReadOnlySpan<char> value)
    {
        const int ContentLimit = MaxEncodedTextLength - 1;
        if (value.Length > ContentLimit - destination.Length)
            return false;

        destination.Append(value);
        return true;
    }

    private sealed class FindingCollector
    {
        private int _evidenceCharacters;
        private bool _limitReported;

        public List<PackageContentAuditFinding> Findings { get; } = [];
        public bool Complete { get; private set; } = true;
        public bool Saturated => _limitReported;

        public bool TryAdd(PackageContentAuditFinding finding)
        {
            if (_limitReported)
                return false;

            int evidenceLength = finding.EncodedText.Length;
            if (Findings.Count >= MaxFindings - 1
                || _evidenceCharacters + evidenceLength > MaxEvidenceCharacters)
            {
                AddLimit(
                    finding.Path,
                    $"Audit output exceeded the {MaxFindings} finding or {MaxEvidenceCharacters} encoded-character limit.");
                return false;
            }

            Findings.Add(finding);
            _evidenceCharacters += evidenceLength;
            return true;
        }

        public void AddIncomplete(PackageContentAuditFinding finding)
        {
            Complete = false;
            TryAdd(finding);
        }

        public void AddLimit(string path, string text)
        {
            Complete = false;
            if (_limitReported)
                return;

            _limitReported = true;
            PackageContentAuditFinding limit = ToolFinding(
                path,
                PackageContentFindingKind.ScanLimit,
                text);
            while (Findings.Count >= MaxFindings
                || _evidenceCharacters + limit.EncodedText.Length > MaxEvidenceCharacters)
            {
                PackageContentAuditFinding removed = Findings[^1];
                Findings.RemoveAt(Findings.Count - 1);
                _evidenceCharacters -= removed.EncodedText.Length;
            }

            Findings.Add(limit);
            _evidenceCharacters += limit.EncodedText.Length;
        }
    }

    private static PackageContentAuditFinding ToolFinding(
        string path,
        PackageContentFindingKind kind,
        string text)
        => new(
            path,
            kind,
            TextConcern.None,
            new InertString(TextPolicy.Prose, text));
}
