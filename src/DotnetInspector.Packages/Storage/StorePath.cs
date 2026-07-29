using ILInspector.MetadataPrimitives;

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
    /// True when <paramref name="segment"/> is a safe single path component.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="HardenedPath.IsSafePathComponent"/>, the single owner of this rule.
    /// This was previously a separate implementation testing only for empty, <c>.</c>, <c>..</c>,
    /// separators, <c>:</c>, NUL and rooted values, so it accepted reserved device names, other
    /// control characters, invisible format characters, unpaired surrogates, trailing dots and
    /// spaces, and overlong components. An untrusted CodeView PDB file name reaches this rule on
    /// its way to filesystem-backed symbol storage (<c>SymbolPackageDownloader</c>), so those gaps
    /// were reachable: <c>CON</c> was accepted here and refused by every other copy of the rule.
    /// </remarks>
    public static bool IsSafeSegment(string segment)
        => HardenedPath.IsSafePathComponent(segment);
}
