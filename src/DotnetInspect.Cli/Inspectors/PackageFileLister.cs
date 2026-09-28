using System.IO.Enumeration;
using DotnetInspect.Cli.Models;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Lists the files of an extracted NuGet package with their uncompressed sizes,
/// and scopes that listing with the <c>--path</c> pattern. Pure and filesystem-
/// light: the package is already extracted, so sizes come from <see cref="FileInfo.Length"/>
/// and no content is read.
/// </summary>
public static class PackageFileLister
{
    // AGENTS.md is deliberately not a candidate. Agent-facing package documentation is
    // carried by skills/**/SKILL.md (the "Package skill files" section), so the README
    // chain is the plain human-readable one.
    private static readonly string[] PackageReadmeCandidates = ["README.md", "PACKAGE.md"];

    public static string? ResolvePackageReadme(string extractPath, string? declaredReadme = null)
    {
        foreach (var candidate in PackageReadmeCandidates)
        {
            if (TryFindPackageRelativeFile(extractPath, candidate, out var match))
                return match;
        }

        if (!string.IsNullOrWhiteSpace(declaredReadme)
            && TryFindPackageRelativeFile(extractPath, declaredReadme, out var declaredMatch))
        {
            return declaredMatch;
        }

        return null;
    }

    /// <summary>
    /// Enumerates every package file (excluding zip plumbing) with its size,
    /// ordered by path. Paths are package-relative with forward slashes.
    /// <paramref name="declaredReadme"/> is the package-relative path selected
    /// as the package readme (e.g. <c>README.md</c> or <c>PACKAGE.md</c>); the
    /// matching row is flagged <see cref="PackageFile.IsReadme"/>.
    /// </summary>
    public static List<PackageFile> ListAll(
        string extractPath,
        string? declaredReadme = null,
        string? declaredLicense = null)
    {
        string? readme = declaredReadme?.Replace('\\', '/').Trim();
        string? license = NormalizePackagePath(declaredLicense);
        var files = new List<PackageFile>();
        foreach (var full in Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories))
        {
            string rel = System.IO.Path.GetRelativePath(extractPath, full).Replace('\\', '/');
            if (IsPlumbing(rel))
                continue;
            bool isReadme = readme is not null && string.Equals(rel, readme, StringComparison.OrdinalIgnoreCase);
            bool isAgents = string.Equals(rel, "AGENTS.md", StringComparison.OrdinalIgnoreCase);
            bool isLicense = IsLicenseDocumentPath(rel, license);
            files.Add(new PackageFile(
                rel,
                new FileInfo(full).Length,
                isReadme,
                isAgents,
                isLicense));
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }

    /// <summary>
    /// Restricts a listing to the <c>--path</c> pattern. Semantics:
    /// <list type="bullet">
    /// <item><c>@readme</c>: the best package readme
    /// regardless of filename (resolved via <see cref="PackageFile.IsReadme"/>).</item>
    /// <item>Root (<c>/</c>, <c>.</c>, or empty): files directly at the package root.</item>
    /// <item>Directory (trailing <c>/</c>, e.g. <c>lib/</c>): files directly in that directory.</item>
    /// <item>Glob (contains <c>*</c> or <c>?</c>, e.g. <c>*.md</c>): matched against the full
    /// relative path, where <c>*</c> spans directory separators — so <c>*.md</c> finds every
    /// <c>.md</c> file anywhere in the package.</item>
    /// <item>Exact path (<c>README.md</c>): that one file; if it names a directory, that
    /// directory's immediate files.</item>
    /// </list>
    /// </summary>
    public static List<PackageFile> Filter(IEnumerable<PackageFile> files, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return files.ToList();

        string p = pattern.Replace('\\', '/');

        // Declared-readme selector: the nuspec-declared readme, whatever its name.
        if (p.Equals("@readme", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => f.IsReadme).ToList();

        if (p.Equals("@agents", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => f.IsAgents).ToList();

        if (p.Equals("@license", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => f.IsLicense).ToList();

        // Root selector: top-level files only.
        if (p is "/" or "." or "./")
            return files.Where(f => !f.Path.Contains('/')).ToList();

        // Glob: match across the whole relative path, '*' spanning separators.
        if (p.Contains('*') || p.Contains('?'))
        {
            return files
                .Where(f => FileSystemName.MatchesSimpleExpression(p, f.Path, ignoreCase: true))
                .ToList();
        }

        // Directory selector (trailing slash): immediate files in that directory.
        if (p.EndsWith('/'))
        {
            string dir = p.TrimEnd('/');
            return files.Where(f => IsImmediateChild(f.Path, dir)).ToList();
        }

        // Exact file match.
        var exact = files
            .Where(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count > 0)
            return exact;

        // Not an exact file — treat as a directory and list its immediate files.
        return files.Where(f => IsImmediateChild(f.Path, p)).ToList();
    }

    /// <summary>
    /// Retains entries whose directory segments contain the requested target
    /// framework. The filename is never considered a directory segment.
    /// </summary>
    public static List<PackageFile> FilterByTargetFramework(
        IEnumerable<PackageFile> files,
        string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework)
            || targetFramework.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return files.ToList();
        }

        return files
            .Where(file => HasDirectorySegment(
                file.Path,
                targetFramework))
            .ToList();
    }

    /// <summary>
    /// Returns ordered, case-insensitively distinct top-level roots represented
    /// by the selected package entries.
    /// </summary>
    public static List<string> ProjectRoots(IEnumerable<PackageFile> files)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PackageFile file in files)
        {
            int separator = file.Path.IndexOf('/');
            if (separator <= 0)
                continue;

            string root = file.Path[..separator];
            if (seen.Add(root))
                roots.Add(root);
        }

        return roots;
    }

    private static bool HasDirectorySegment(
        string path,
        string value)
    {
        int segmentStart = 0;
        while (true)
        {
            int separator = path.IndexOf('/', segmentStart);
            if (separator < 0)
                return false;

            if (path.AsSpan(segmentStart, separator - segmentStart)
                .Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            segmentStart = separator + 1;
        }
    }

    private static bool IsImmediateChild(string path, string dir)
    {
        if (!path.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        // No further separator after the directory prefix => immediate child.
        return path.IndexOf('/', dir.Length + 1) < 0;
    }

    public static bool IsLicenseDocumentPath(
        string path,
        string? declaredLicense = null)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (declaredLicense is not null
            && normalized.Equals(
                NormalizePackagePath(declaredLicense),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (fileName.Contains("notice", StringComparison.OrdinalIgnoreCase))
            return false;

        int extensionStart = fileName.LastIndexOf('.');
        string extension = extensionStart >= 0
            ? fileName[extensionStart..]
            : "";
        if (extension.Length > 0
            && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = normalized.Split('/');
        if (segments[..^1].Any(segment =>
            segment.Equals("license", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("licenses", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("licence", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("licences", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return fileName.Contains("license", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("licence", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("eula", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("copying", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("copyright", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("unlicense", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePackagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized = path.Replace('\\', '/').Trim().TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    // The .nuspec is deliberately absent: it is authored content (the package
    // manifest), not packaging plumbing, so it belongs in the file listings and
    // behind the Package nuspec file section.
    internal static bool IsPlumbing(string rel) =>
        PackageFileInventoryQuery.IsPlumbingPath(rel);

    private static bool TryFindPackageRelativeFile(string extractPath, string packageRelativePath, out string match)
    {
        var normalized = packageRelativePath.Replace('\\', '/').Trim().TrimStart('/');
        foreach (var fullPath in Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(extractPath, fullPath).Replace('\\', '/');
            if (string.Equals(rel, normalized, StringComparison.OrdinalIgnoreCase))
            {
                match = rel;
                return true;
            }
        }

        match = "";
        return false;
    }
}
