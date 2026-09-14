using System.Collections.ObjectModel;
using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// One validated typed inventory of repository-relative project roots. This is
/// the single reader for the classifier's policy manifests: the routing policy
/// and the project-graph self-tests share it rather than parsing the same
/// files independently.
/// </summary>
internal sealed class ProjectInventory
{
    private readonly ReadOnlyCollection<string> roots;
    private readonly byte[][] prefixes;

    private ProjectInventory(string[] roots)
    {
        this.roots = Array.AsReadOnly(roots);
        prefixes = [.. roots.Select(root => Encoding.UTF8.GetBytes($"{root}/"))];
    }

    /// <summary>
    /// Gets the canonical repository-relative project roots, without a
    /// trailing separator, in manifest order.
    /// </summary>
    internal ReadOnlyCollection<string> Roots => roots;

    /// <summary>
    /// Reports whether a raw path lies at or below any inventory root.
    /// </summary>
    /// <param name="path">The raw path bytes.</param>
    /// <returns>True when a root prefixes the path.</returns>
    internal bool Covers(ReadOnlySpan<byte> path)
    {
        foreach (byte[] prefix in prefixes)
        {
            if (path.StartsWith(prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Loads and validates an inventory manifest. A missing file, a line
    /// outside the allowed root prefixes, a non-canonical line, a line whose
    /// directory does not exist, or an empty manifest when one is required all
    /// produce an unavailable inventory rather than an exception, so the
    /// caller can apply its own conservative policy.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <param name="manifestRelativePath">The manifest's relative path.</param>
    /// <param name="allowedPrefixes">The permitted root prefixes.</param>
    /// <param name="requireNonEmpty">Whether an empty manifest is invalid.</param>
    /// <param name="inventory">The loaded inventory, when valid.</param>
    /// <returns>True when the manifest produced a valid inventory.</returns>
    internal static bool TryLoad(
        string repository,
        string manifestRelativePath,
        IReadOnlyList<string> allowedPrefixes,
        bool requireNonEmpty,
        out ProjectInventory inventory)
    {
        inventory = new ProjectInventory([]);
        string manifestPath = Path.Combine(
            repository,
            manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string[] lines;
        try
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            lines = File.ReadAllLines(manifestPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            if (!IsCanonicalRoot(line, allowedPrefixes)
                || !seen.Add(line)
                || !Directory.Exists(Path.Combine(
                    repository,
                    line.Replace('/', Path.DirectorySeparatorChar))))
            {
                return false;
            }
        }

        if (requireNonEmpty && lines.Length == 0)
        {
            return false;
        }

        inventory = new ProjectInventory(lines);
        return true;
    }

    /// <summary>
    /// Reports whether a manifest line is a canonical relative root under one
    /// of the allowed prefixes.
    /// </summary>
    /// <param name="line">The manifest line.</param>
    /// <param name="allowedPrefixes">The permitted root prefixes.</param>
    /// <returns>True when the line is a canonical admitted root.</returns>
    internal static bool IsCanonicalRoot(
        string line,
        IReadOnlyList<string> allowedPrefixes)
    {
        if (line.Length == 0
            || line != line.Trim()
            || line.EndsWith('/')
            || line.Split('/').Any(part => part is "" or "." or ".."))
        {
            return false;
        }

        return allowedPrefixes.Any(prefix =>
            line.StartsWith(prefix, StringComparison.Ordinal));
    }
}
