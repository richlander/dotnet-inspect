namespace DotnetInspector.Packages;

/// <summary>Shared limits for package text-document projections.</summary>
public static class PackageDocumentContentLimits
{
    /// <summary>
    /// Maximum expanded bytes that a text projection may decode or detach.
    /// Exact byte-stream transfers remain governed by package payload limits.
    /// </summary>
    public const long MaxDecodedBytes = 16L * 1024 * 1024;
}
