using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

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

    private static readonly string s_windowsPersistentProfile =
        OperatingSystem.IsWindows()
            ? CreateWindowsPersistentProfile()
            : string.Empty;

    private LocalPackageSourceIdentity(string canonicalPath)
    {
        CanonicalPath = canonicalPath;
    }

    /// <summary>Gets the absolute canonical directory path.</summary>
    public string CanonicalPath { get; }

    internal string PersistentValue =>
        OperatingSystem.IsWindows()
            ? $"{s_windowsPersistentProfile}:{FoldOrdinalIgnoreCase(CanonicalPath)}"
            : CanonicalPath;

    internal static string FoldOrdinalIgnoreCase(string value)
    {
        ThrowIfIllFormedUtf16(value, nameof(value));

        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (!Rune.TryCreate(value[index], out Rune rune))
            {
                _ = Rune.TryCreate(
                    value[index],
                    value[++index],
                    out rune);
            }

            string runeText = rune.ToString();
            Rune upper = Rune.ToUpperInvariant(rune);
            Rune lower = Rune.ToLowerInvariant(rune);
            Rune representative = Rune.ToLowerInvariant(upper);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                runeText,
                representative.ToString()))
            {
                representative = rune;
            }

            // Some ordinal pairs are newer than the public casing tables. Case
            // ranges conventionally differ by 0x20; admit that relation only
            // when both public mappings are identity and the live comparer
            // confirms it.
            if (upper == rune && lower == rune)
            {
                ConsiderOffset(-0x20);
                ConsiderOffset(0x20);
            }

            builder.Append(representative.ToString());

            void ConsiderOffset(int offset)
            {
                int candidateValue = rune.Value + offset;
                if (Rune.IsValid(candidateValue)
                    && candidateValue < representative.Value
                    && StringComparer.OrdinalIgnoreCase.Equals(
                        runeText,
                        new Rune(candidateValue).ToString()))
                {
                    representative = new Rune(candidateValue);
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reports whether <paramref name="source"/> has local path or
    /// <c>file://</c> syntax rather than an absolute non-file URI.
    /// </summary>
    public static bool IsLocalSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;

        string value = source.Trim();
        return value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
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

        ThrowIfIllFormedUtf16(baseDirectory, nameof(baseDirectory));
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
        ThrowIfIllFormedUtf16(value, nameof(source));
        string path = value;
        bool hasFileScheme =
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        bool hasAbsoluteUri =
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri);

        if (hasFileScheme
            && (!hasAbsoluteUri || uri is null || !uri.IsFile))
        {
            throw new ArgumentException(
                "A local package source file URI is malformed.",
                nameof(source));
        }

        if (hasAbsoluteUri && uri is not null)
        {
            if (!uri.IsFile)
            {
                throw new ArgumentException(
                    "A local package source must be a path or file URI.",
                    nameof(source));
            }

            if (hasFileScheme)
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
                if (!Path.IsPathFullyQualified(path))
                {
                    throw new ArgumentException(
                        "A local package source file URI must identify an absolute path.",
                        nameof(source));
                }
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
        ThrowIfIllFormedUtf16(canonicalPath, nameof(source));
        return new LocalPackageSourceIdentity(canonicalPath);
    }

    private static string CreateWindowsPersistentProfile()
    {
        SortVersion sortVersion =
            CultureInfo.InvariantCulture.CompareInfo.Version;
        return FormattableString.Invariant(
            $"windows-ordinal-ignore-case-v1:{RuntimeInformation.FrameworkDescription}:{sortVersion.FullVersion:X8}:{sortVersion.SortId:N}");
    }

    private static void ThrowIfIllFormedUtf16(
        string value,
        string parameterName)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsSurrogate(current))
                continue;

            if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }

            throw new ArgumentException(
                "A local package source must contain well-formed UTF-16.",
                parameterName);
        }
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
