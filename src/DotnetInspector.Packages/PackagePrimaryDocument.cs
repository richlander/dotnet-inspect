namespace DotnetInspector.Packages;

public enum PackagePrimaryDocumentResolutionStatus
{
    Resolved,
    Missing,
    Ambiguous,
}

public sealed record PackagePrimaryDocumentResolution
{
    internal PackagePrimaryDocumentResolution(
        PackagePrimaryDocumentResolutionStatus status,
        PackageContentEntry? entry,
        string? candidatePath)
    {
        Status = status;
        Entry = entry;
        CandidatePath = candidatePath;
    }

    public PackagePrimaryDocumentResolutionStatus Status { get; }

    public PackageContentEntry? Entry { get; }

    public string? CandidatePath { get; }
}

/// <summary>
/// Selects the package's primary human-readable document.
/// </summary>
public static class PackagePrimaryDocument
{
    private static readonly string[] ConventionalCandidates =
        ["README.md", "PACKAGE.md"];

    public static PackagePrimaryDocumentResolution Resolve(
        IEnumerable<PackageContentEntry> entries,
        string? declaredReadme = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        PackageContentEntry[] snapshot = [.. entries];
        List<string> candidates = [.. ConventionalCandidates];
        if (NormalizePath(declaredReadme) is { } declared
            && !candidates.Contains(
                declared,
                StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(declared);
        }

        foreach (string candidate in candidates)
        {
            PackageContentEntry[] matches =
            [
                .. snapshot
                    .Where(entry => entry.Path.Equals(
                        candidate,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2),
            ];
            if (matches.Length == 1)
            {
                return new(
                    PackagePrimaryDocumentResolutionStatus.Resolved,
                    matches[0],
                    candidate);
            }
            if (matches.Length > 1)
            {
                return new(
                    PackagePrimaryDocumentResolutionStatus.Ambiguous,
                    entry: null,
                    candidate);
            }
        }

        return new(
            PackagePrimaryDocumentResolutionStatus.Missing,
            entry: null,
            candidatePath: null);
    }

    public static bool IsConventionalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        return !normalized.Contains('/')
            && ConventionalCandidates.Contains(
                normalized,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized =
            path.Replace('\\', '/').Trim().TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.Length == 0 ? null : normalized;
    }
}
