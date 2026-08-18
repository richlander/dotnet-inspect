using System.Text;
using System.Xml;
using System.Xml.Linq;
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
    private const int MaxEncodedTextLength = 512;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".config", ".cs", ".csproj", ".editorconfig", ".fs", ".fsproj",
        ".ini", ".json", ".jsonc", ".markdown", ".md", ".nuspec", ".props", ".ps1",
        ".rsp", ".sh", ".targets", ".toml", ".txt", ".vb", ".vbproj", ".xml", ".yaml",
        ".yml",
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
        var findings = new List<PackageContentAuditFinding>();
        int eligibleFiles = 0;
        int scannedFiles = 0;
        long scannedBytes = 0;
        bool complete = true;

        string[] paths =
        [
            .. packageRelativePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal),
        ];

        foreach (string relativePath in paths)
        {
            if (!IsTextBearingPath(relativePath))
                continue;

            eligibleFiles++;
            string fullPath;
            try
            {
                fullPath = ResolveBeneath(root, relativePath);
            }
            catch (ArgumentException)
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ReadFailure,
                    "Path could not be resolved beneath the extracted package."));
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(fullPath).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ReadFailure,
                    "File metadata could not be read."));
                continue;
            }

            if (length > MaxFileBytes || scannedBytes + length > MaxTotalBytes)
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ScanLimit,
                    length > MaxFileBytes
                        ? $"File exceeds the {MaxFileBytes / (1024 * 1024)} MiB per-file audit limit."
                        : $"Package exceeds the {MaxTotalBytes / (1024 * 1024)} MiB aggregate audit limit."));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ReadFailure,
                    "File content could not be read."));
                continue;
            }

            if (bytes.Length > MaxFileBytes || scannedBytes + bytes.Length > MaxTotalBytes)
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.ScanLimit,
                    bytes.Length > MaxFileBytes
                        ? $"File exceeds the {MaxFileBytes / (1024 * 1024)} MiB per-file audit limit."
                        : $"Package exceeds the {MaxTotalBytes / (1024 * 1024)} MiB aggregate audit limit."));
                continue;
            }

            scannedBytes += bytes.Length;
            if (!TryDecode(bytes, out string? content))
            {
                complete = false;
                findings.Add(ToolFinding(
                    relativePath,
                    PackageContentFindingKind.InvalidTextEncoding,
                    "Text-bearing file is not valid UTF-8, UTF-16, or UTF-32."));
                continue;
            }

            scannedFiles++;
            string[] lines = content!.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                InertString encoded = new(TextPolicy.Prose, lines[lineIndex]);
                if (!encoded.RequiredContainment)
                    continue;

                findings.Add(new PackageContentAuditFinding(
                    relativePath,
                    PackageContentFindingKind.NonGraphicText,
                    encoded.Concerns,
                    BoundAroundFirstEncoding(encoded),
                    lineIndex + 1));
            }

            if (IsNuGetConfig(relativePath))
                AddNuGetConfigurationFindings(relativePath, content, lines, findings, ref complete);
        }

        int scannedSourceLinkMaps = AddSourceLinkFindings(
            root,
            paths,
            findings,
            ref complete);

        return new PackageContentAuditResult(
            findings,
            eligibleFiles,
            scannedFiles,
            scannedBytes,
            complete,
            scannedSourceLinkMaps);
    }

    private static int AddSourceLinkFindings(
        string root,
        IReadOnlyList<string> packagePaths,
        List<PackageContentAuditFinding> findings,
        ref bool complete)
    {
        var paths = packagePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int scannedMaps = 0;
        foreach (string assemblyPath in packagePaths.Where(static path =>
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            string fullAssemblyPath;
            try
            {
                fullAssemblyPath = ResolveBeneath(root, assemblyPath);
            }
            catch (ArgumentException)
            {
                continue;
            }

            string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb").Replace('\\', '/');
            string evidencePath = paths.Contains(pdbPath) ? pdbPath : assemblyPath;
            try
            {
                using SourceLinkService source = SourceLinkService.Open(fullAssemblyPath);
                SourceLinkMapInspection map = source.SourceLinkMap;
                if (!map.IsPresent)
                    continue;

                scannedMaps++;
                if (map.Error is { } error)
                {
                    findings.Add(ToolFinding(
                        evidencePath,
                        PackageContentFindingKind.InvalidSourceLinkMap,
                        error));
                }

                var rejected = map.RejectedKeys.ToHashSet(StringComparer.Ordinal);
                foreach (SourceLinkMapEntry mapping in source.SourceLinkMapEntries)
                {
                    string evidence = mapping.Url is null
                        ? mapping.Document
                        : $"{mapping.Document} => {mapping.Url}";
                    InertString encoded = new(TextPolicy.Prose, evidence);
                    if (encoded.RequiredContainment)
                    {
                        findings.Add(new PackageContentAuditFinding(
                            evidencePath,
                            PackageContentFindingKind.NonGraphicSourceLinkText,
                            encoded.Concerns,
                            BoundAroundFirstEncoding(encoded)));
                    }

                    if (ContainsParentPathReference(mapping.Document)
                        || ContainsParentPathReference(mapping.Url))
                    {
                        findings.Add(new PackageContentAuditFinding(
                            evidencePath,
                            PackageContentFindingKind.SourceLinkParentPathSegment,
                            TextConcern.None,
                            BoundAroundLiteral(encoded, "../")));
                    }

                    if (rejected.Contains(mapping.Document))
                    {
                        findings.Add(new PackageContentAuditFinding(
                            evidencePath,
                            PackageContentFindingKind.RejectedSourceLinkMapping,
                            TextConcern.None,
                            BoundAroundFirstEncoding(encoded)));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not every .dll-named package asset is a managed PE. This audit owns decoded
                // SourceLink content, not binary validity, so a file that cannot expose a PDB is
                // outside its census. Once SourceLink is present, SourceLinkService retains map
                // read/parse failures as typed map evidence above rather than throwing them here.
            }
        }

        return scannedMaps;
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

    private static InertString BoundAroundFirstEncoding(InertString encoded)
        => BoundAroundIndex(encoded, Math.Max(0, encoded.IndexOfFirstEncoded()));

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
        string[] lines,
        List<PackageContentAuditFinding> findings,
        ref bool complete)
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
            XDocument document = XDocument.Load(reader, LoadOptions.SetLineInfo);
            foreach (XElement packageSources in document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals(
                    "packageSources",
                    StringComparison.OrdinalIgnoreCase)))
            {
                foreach (XElement child in packageSources.Elements())
                {
                    PackageContentFindingKind? kind = child.Name.LocalName switch
                    {
                        var name when name.Equals("clear", StringComparison.OrdinalIgnoreCase) =>
                            PackageContentFindingKind.RestoreSourcesCleared,
                        var name when name.Equals("add", StringComparison.OrdinalIgnoreCase) =>
                            PackageContentFindingKind.PackageSourceDeclared,
                        _ => null,
                    };
                    if (kind is null)
                        continue;

                    int line = (child as IXmlLineInfo)?.HasLineInfo() == true
                        ? ((IXmlLineInfo)child).LineNumber
                        : 0;
                    string evidence = line > 0 && line <= lines.Length
                        ? lines[line - 1].Trim()
                        : child.ToString(SaveOptions.DisableFormatting);
                    findings.Add(new PackageContentAuditFinding(
                        path,
                        kind.Value,
                        TextConcern.None,
                        new InertString(TextPolicy.Prose, evidence),
                        line > 0 ? line : null));
                }
            }
        }
        catch (XmlException)
        {
            complete = false;
            findings.Add(ToolFinding(
                path,
                PackageContentFindingKind.InvalidNuGetConfiguration,
                "NuGet configuration could not be parsed as XML."));
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
