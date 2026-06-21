using System.IO.Enumeration;
using DotnetInspector.Models;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Lists the files of an extracted NuGet package with their uncompressed sizes,
/// and scopes that listing with the <c>--path</c> pattern. Pure and filesystem-
/// light: the package is already extracted, so sizes come from <see cref="FileInfo.Length"/>
/// and no content is read.
/// </summary>
public static class PackageFileLister
{
    /// <summary>
    /// Enumerates every package file (excluding zip plumbing) with its size,
    /// ordered by path. Paths are package-relative with forward slashes.
    /// <paramref name="declaredReadme"/> is the package-relative path the
    /// <c>.nuspec</c> declares as its readme (e.g. <c>PACKAGE.md</c>); the
    /// matching row is flagged <see cref="PackageFile.IsReadme"/>.
    /// </summary>
    public static List<PackageFile> ListAll(string extractPath, string? declaredReadme = null)
    {
        string? readme = declaredReadme?.Replace('\\', '/').Trim();
        var files = new List<PackageFile>();
        foreach (var full in Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories))
        {
            string rel = System.IO.Path.GetRelativePath(extractPath, full).Replace('\\', '/');
            if (IsPlumbing(rel))
                continue;
            bool isReadme = readme is not null && string.Equals(rel, readme, StringComparison.OrdinalIgnoreCase);
            files.Add(new PackageFile(rel, new FileInfo(full).Length, isReadme));
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }

    /// <summary>
    /// Restricts a listing to the <c>--path</c> pattern. Semantics:
    /// <list type="bullet">
    /// <item><c>@readme</c>: the file the package declares as its readme,
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

    private static bool IsImmediateChild(string path, string dir)
    {
        if (!path.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        // No further separator after the directory prefix => immediate child.
        return path.IndexOf('/', dir.Length + 1) < 0;
    }

    private static bool IsPlumbing(string rel)
        // Zip plumbing (OPC packaging artifacts inside the .nupkg).
        => rel.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase)
            || rel.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase)
            // Restore-folder artifacts added by the NuGet client (not zip content).
            || rel.Equals(".nupkg.metadata", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith(".nupkg.sha512", StringComparison.OrdinalIgnoreCase);
}
