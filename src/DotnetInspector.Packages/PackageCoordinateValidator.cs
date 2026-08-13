using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// Why a package coordinate was rejected before it reached a cache key or a feed request.
/// </summary>
public enum PackageCoordinateRejectionKind
{
    Empty,
    TooLong,
    InvalidCharacter,
    InvalidShape,
    UnparsableVersion,
}

/// <summary>
/// Validates package ids and versions against NuGet's own grammar before they are used to build
/// a cache key or a flat-container request path.
/// </summary>
/// <remarks>
/// <para>
/// Escaping alone is not enough. A coordinate is also an identity: it keys a content cache and it
/// names the package a result is attributed to. An id or version carrying a path separator, a dot
/// segment, a query, or a fragment either rewrites the request path or collides with a different
/// coordinate's key, so both halves are rejected here — before any cache lookup and before any
/// network call — rather than escaped and carried forward.
/// </para>
/// <para>
/// The id rule is NuGet's: word characters separated by single <c>.</c>, <c>-</c>, or <c>_</c>
/// characters, at most <see cref="MaxPackageIdLength"/> long. The version rule is
/// <see cref="NuGetVersion.TryParse(string, out NuGetVersion)"/>, guarded by an explicit
/// character allow list because that parser trims surrounding whitespace and so accepts spellings
/// that are not the caller's exact text. Validation never rewrites a coordinate: the caller's
/// exact id and version text is what reaches the feed, so NuGet's own version behavior — which
/// spellings resolve and which do not — is unchanged.
/// </para>
/// <para><c>PackageCoordinateValidatorTests</c> gates both the accepted and the rejected set.</para>
/// </remarks>
public static class PackageCoordinateValidator
{
    /// <summary>NuGet's own package id length limit.</summary>
    public const int MaxPackageIdLength = 100;

    /// <summary>A hard bound on version text, so an absurd input is rejected before parsing.</summary>
    public const int MaxPackageVersionLength = 128;

    /// <summary>Whether a package id is a valid NuGet package id.</summary>
    public static bool IsValidPackageId(string? packageId) =>
        TryValidatePackageId(packageId, out _);

    /// <summary>Whether a version is exact, parsable NuGet version text.</summary>
    public static bool IsValidPackageVersion(string? version) =>
        TryValidatePackageVersion(version, out _);

    /// <summary>
    /// Validates a package id, reporting the typed rejection rather than a boolean.
    /// </summary>
    public static bool TryValidatePackageId(
        string? packageId,
        out PackageCoordinateRejectionKind? rejection)
    {
        if (string.IsNullOrEmpty(packageId))
        {
            rejection = PackageCoordinateRejectionKind.Empty;
            return false;
        }

        if (packageId.Length > MaxPackageIdLength)
        {
            rejection = PackageCoordinateRejectionKind.TooLong;
            return false;
        }

        // ^\w+([_.-]\w+)*$ — a separator must both follow and precede a word character, so a
        // leading, trailing, doubled, or lone separator is rejected. "." and ".." carry no word
        // character at all and so cannot name a dot segment.
        bool previousWasWord = false;
        foreach (char character in packageId)
        {
            if (IsWordCharacter(character))
            {
                previousWasWord = true;
                continue;
            }

            if (character is not ('.' or '-' or '_'))
            {
                rejection = PackageCoordinateRejectionKind.InvalidCharacter;
                return false;
            }

            if (!previousWasWord)
            {
                rejection = PackageCoordinateRejectionKind.InvalidShape;
                return false;
            }

            previousWasWord = false;
        }

        if (!previousWasWord)
        {
            rejection = PackageCoordinateRejectionKind.InvalidShape;
            return false;
        }

        rejection = null;
        return true;
    }

    /// <summary>
    /// Validates version text, reporting the typed rejection rather than a boolean.
    /// </summary>
    public static bool TryValidatePackageVersion(
        string? version,
        out PackageCoordinateRejectionKind? rejection)
    {
        if (string.IsNullOrEmpty(version))
        {
            rejection = PackageCoordinateRejectionKind.Empty;
            return false;
        }

        if (version.Length > MaxPackageVersionLength)
        {
            rejection = PackageCoordinateRejectionKind.TooLong;
            return false;
        }

        foreach (char character in version)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '+'))
            {
                rejection = PackageCoordinateRejectionKind.InvalidCharacter;
                return false;
            }
        }

        if (!NuGetVersion.TryParse(version, out _))
        {
            rejection = PackageCoordinateRejectionKind.UnparsableVersion;
            return false;
        }

        rejection = null;
        return true;
    }

    /// <summary>The package id, or a visible failure naming why it is not a package id.</summary>
    public static string ValidatePackageId(string packageId)
    {
        if (TryValidatePackageId(packageId, out PackageCoordinateRejectionKind? rejection))
            return packageId;

        throw new ArgumentException(
            $"'{packageId}' is not a valid NuGet package id ({rejection}).",
            nameof(packageId));
    }

    /// <summary>The version text, or a visible failure naming why it is not a version.</summary>
    public static string ValidatePackageVersion(string version)
    {
        if (TryValidatePackageVersion(version, out PackageCoordinateRejectionKind? rejection))
            return version;

        throw new ArgumentException(
            $"'{version}' is not a valid NuGet package version ({rejection}).",
            nameof(version));
    }

    // NuGet's id grammar is \w, which is Unicode word characters, not ASCII only. Underscore is
    // already a word character; it is listed as a separator by NuGet's regex redundantly.
    static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}
