namespace DotnetInspector.Packages;

/// <summary>
/// An acquisition-issued digest of one retained package-content generation.
/// </summary>
/// <remarks>
/// The digest carries no content handle, path, coordinate, producer, or
/// credentials. Its generation binds the durable byte identity to the
/// process-local retained snapshot without keeping that snapshot alive.
/// </remarks>
public sealed class PackageContentDigest
{
    internal PackageContentDigest(
        PackageContentGenerationIdentity generation,
        string hexValue)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentException.ThrowIfNullOrEmpty(hexValue);
        Generation = generation;
        HexValue = hexValue;
    }

    /// <summary>The retained content generation whose bytes were hashed.</summary>
    public PackageContentGenerationIdentity Generation { get; }

    /// <summary>The digest algorithm.</summary>
    public string Algorithm => "SHA-256";

    /// <summary>The lowercase hexadecimal digest value.</summary>
    public string HexValue { get; }
}

internal interface IPackageContentDigestSource
{
    PackageContentDigest? GetContentDigest(
        Action<long> chargeWork,
        CancellationToken cancellationToken);
}

internal static class PackageContentDigestAcquisition
{
    internal static PackageContentDigest? GetContentDigest(
        IPackageContent content,
        Action<long> chargeWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(chargeWork);
        cancellationToken.ThrowIfCancellationRequested();

        return content is IPackageContentDigestSource source
            ? source.GetContentDigest(chargeWork, cancellationToken)
            : null;
    }
}
