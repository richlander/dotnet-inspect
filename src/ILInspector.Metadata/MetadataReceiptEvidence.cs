using System.Security.Cryptography;

namespace ILInspector.Metadata;

/// <summary>
/// Projects Metadata-owned opaque identities into stable receipt evidence
/// without exposing their minting values.
/// </summary>
public static class MetadataReceiptEvidence
{
    public static string For(
        AssemblyAcquisitionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return Digest(0, registration.Value);
    }

    public static string For(AssemblyCatalogGenerationId generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        return Digest(1, generation.Value);
    }

    public static string For(DefinitionJoinToken definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Digest(2, definition.Value);
    }

    static string Digest(byte discriminator, Guid value)
    {
        Span<byte> evidence = stackalloc byte[17];
        evidence[0] = discriminator;
        value.TryWriteBytes(
            evidence[1..],
            bigEndian: true,
            out _);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(evidence, digest);
        return Convert.ToHexStringLower(digest);
    }
}
