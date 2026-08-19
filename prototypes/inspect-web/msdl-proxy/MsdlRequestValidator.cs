namespace MsdlProxy;

/// <summary>
/// Validates the two path segments this proxy forwards to MSDL.
/// Deliberately reimplemented (rather than referencing
/// DotnetInspector.Packages) to keep this externally-facing, security-
/// sensitive edge service small, self-contained, and easy to audit and trim
/// for Native AOT -- it has no need for the rest of that project's surface.
/// </summary>
internal static class MsdlRequestValidator
{
    // MSDL PDB file names are single path segments ending in ".pdb". Reject
    // anything else outright: this is an allow list, not a sanitizer, per
    // the repository's "reject, don't sanitize" acquisition policy.
    private const int MaxPdbFileNameLength = 255;

    public static bool IsValidPdbFileName(string pdbFileName)
    {
        if (string.IsNullOrEmpty(pdbFileName)
            || pdbFileName.Length > MaxPdbFileNameLength)
        {
            return false;
        }

        if (!pdbFileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSafeSegment(pdbFileName);
    }

    // A symbol key is a hex GUID (32 hex digits) followed by a short hex
    // stamp: "FFFFFFFF" for portable PDBs, or a variable-length hex "age"
    // for Windows PDBs (SymbolPackageDownloader.cs). 33-40 hex digits covers
    // both shapes with no slack for anything else.
    private const int MinSymbolKeyLength = 33;
    private const int MaxSymbolKeyLength = 40;

    public static bool IsValidSymbolKey(string symbolKey)
    {
        if (string.IsNullOrEmpty(symbolKey)
            || symbolKey.Length < MinSymbolKeyLength
            || symbolKey.Length > MaxSymbolKeyLength)
        {
            return false;
        }

        foreach (var c in symbolKey)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }

    private static bool IsSafeSegment(string segment)
        => segment.Length != 0
            && segment != "."
            && segment != ".."
            && !segment.Contains('/')
            && !segment.Contains('\\')
            && !segment.Contains(':')
            && !segment.Contains('\0')
            && !Path.IsPathRooted(segment);
}
