namespace NuGetFetch;

/// <summary>
/// Canonical identity for one local package-source directory.
/// </summary>
/// <remarks>
/// Identity follows host path semantics: Windows compares paths ordinally
/// without case, while other hosts compare them ordinally. Canonicalization is
/// lexical and does not resolve symbolic links.
/// </remarks>
public sealed class LocalPackageSourceIdentity
    : IEquatable<LocalPackageSourceIdentity>
{
    private static readonly StringComparer s_pathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private LocalPackageSourceIdentity(string canonicalPath)
    {
        CanonicalPath = canonicalPath;
    }

    /// <summary>Gets the absolute canonical directory path.</summary>
    public string CanonicalPath { get; }

    internal string PersistentValue =>
        OperatingSystem.IsWindows()
            ? CanonicalPath.ToUpperInvariant()
            : CanonicalPath;

    /// <summary>
    /// Reports whether <paramref name="source"/> has local path or
    /// <c>file://</c> syntax rather than an absolute non-file URI.
    /// </summary>
    public static bool IsLocalSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;

        return !Uri.TryCreate(source.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.IsFile;
    }

    /// <summary>
    /// Resolves a local path or <c>file://</c> URI against an absolute base
    /// directory and returns its canonical identity.
    /// </summary>
    public static LocalPackageSourceIdentity Create(
        string source,
        string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (!Path.IsPathFullyQualified(baseDirectory))
        {
            throw new ArgumentException(
                "A local package source resolution base must be absolute.",
                nameof(baseDirectory));
        }

        return CreateCore(source, Path.GetFullPath(baseDirectory));
    }

    /// <summary>
    /// Creates an identity for an absolute local path or <c>file://</c> URI
    /// when no resolution base is available.
    /// </summary>
    public static LocalPackageSourceIdentity CreateAbsolute(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return CreateCore(source, baseDirectory: null);
    }

    private static LocalPackageSourceIdentity CreateCore(
        string source,
        string? baseDirectory)
    {
        string value = source.Trim();
        string path = value;

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile)
            {
                throw new ArgumentException(
                    "A local package source must be a path or file URI.",
                    nameof(source));
            }

            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (uri.UserInfo.Length > 0
                    || uri.Query.Length > 0
                    || uri.Fragment.Length > 0)
                {
                    throw new ArgumentException(
                        "A local package source file URI cannot contain user information, a query, or a fragment.",
                        nameof(source));
                }

                if (uri.IsUnc && !OperatingSystem.IsWindows())
                {
                    throw new ArgumentException(
                        "A UNC package source is available only on Windows.",
                        nameof(source));
                }

                path = uri.LocalPath;
            }
        }

        string canonicalPath;
        if (Path.IsPathFullyQualified(path))
        {
            canonicalPath = Path.GetFullPath(path);
        }
        else if (baseDirectory is not null)
        {
            canonicalPath = Path.GetFullPath(path, baseDirectory);
        }
        else
        {
            throw new ArgumentException(
                "A local package source must be absolute when no resolution base is available.",
                nameof(source));
        }

        canonicalPath = Path.TrimEndingDirectorySeparator(canonicalPath);
        return new LocalPackageSourceIdentity(canonicalPath);
    }

    /// <inheritdoc />
    public bool Equals(LocalPackageSourceIdentity? other) =>
        other is not null
        && s_pathComparer.Equals(CanonicalPath, other.CanonicalPath);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is LocalPackageSourceIdentity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        s_pathComparer.GetHashCode(CanonicalPath);

    /// <inheritdoc />
    public override string ToString() => CanonicalPath;
}
