namespace DotnetInspector.Packages;

/// <summary>
/// Shared containment guard for filesystem-backed stores. Resolves a
/// <c>/</c>-separated, store-relative key to an absolute path under a root while
/// rejecting any segment or combination that could escape that root, including
/// Windows volume-qualified segments (for example <c>C:</c> or <c>C:..</c>) that
/// <see cref="Path.Combine(string[])"/> would treat as a new root.
/// </summary>
internal static class StorePath
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

        var segments = key.Split('/');
        foreach (var segment in segments)
        {
            if (!IsSafeSegment(segment))
                throw new ArgumentException($"Invalid store key segment: '{segment}' (key '{key}')");
        }

        var combined = Path.Combine([root, .. segments]);
        var fullRoot = Path.GetFullPath(root);
        var fullCombined = Path.GetFullPath(combined);

        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (fullCombined != fullRoot
            && !fullCombined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Store key escapes root: '{key}'");
        }

        return fullCombined;
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
