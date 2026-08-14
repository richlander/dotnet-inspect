using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

/// <summary>
/// Shared containment guard for untrusted relative paths. Resolves a
/// <c>/</c>-separated key to an absolute path under a trusted root while rejecting
/// any segment or combination that could escape that root, including Windows
/// volume-qualified segments (for example <c>C:</c> or <c>C:..</c>).
/// </summary>
public static class StorePath
{
    /// <summary>
    /// Returns the normalized absolute path for <paramref name="key"/> beneath
    /// <paramref name="root"/>. Throws <see cref="ArgumentException"/> for an
    /// empty key, an unsafe segment, or a result that would fall outside the
    /// root. Interior dots (a real PDB or assembly file name such as
    /// <c>System.Text.Json.pdb</c>) are permitted.
    /// </summary>
    public static string ResolveUnderRoot(string root, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!TryResolveUnderRoot(root, key, out string? resolved))
            throw new ArgumentException(
                "The relative path is not contained by the supplied root.",
                nameof(key));

        return resolved;
    }

    /// <summary>
    /// Attempts to resolve an untrusted <c>/</c>-separated relative path beneath
    /// a trusted root. Unsafe input is rejected without including it in an error
    /// message.
    /// </summary>
    public static bool TryResolveUnderRoot(
        string root,
        string? key,
        [NotNullWhen(true)] out string? resolved)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        resolved = null;

        if (string.IsNullOrEmpty(key))
            return false;

        var segments = key.Split('/');
        if (segments.Any(segment => !IsSafeSegment(segment)))
            return false;

        var fullRoot = Path.GetFullPath(root);
        string fullCombined;
        try
        {
            fullCombined = Path.GetFullPath(
                Path.Combine([fullRoot, .. segments]));
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }

        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullCombined.StartsWith(rootWithSeparator, comparison))
            return false;

        resolved = fullCombined;
        return true;
    }

    /// <summary>
    /// True when <paramref name="segment"/> is a safe single path component:
    /// non-empty, not <c>.</c> or <c>..</c>, containing no separator, volume
    /// (<c>:</c>), or null character, and not otherwise rooted.
    /// </summary>
    public static bool IsSafeSegment(string segment)
        => segment.Length != 0
            && segment != "."
            && segment != ".."
            && !segment.Contains('/')
            && !segment.Contains('\\')
            && !segment.Contains(':')
            && !segment.Contains('\0')
            && !Path.IsPathRooted(segment);
}
