using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace ILInspector.Metadata;

/// <summary>
/// Metadata identity for an assembly reference. It intentionally carries only
/// ECMA metadata identity, not package, project, deps.json, or compiler concepts.
/// </summary>
public sealed record AssemblyReferenceIdentity(
    string Name,
    Version? Version,
    string? Culture,
    string? PublicKeyToken)
{
    public static AssemblyReferenceIdentity From(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        return Create(
            reader,
            reference,
            TokenOrNull(
                reader,
                reference.PublicKeyOrToken,
                (reference.Flags & AssemblyFlags.PublicKey) != 0));
    }

    internal static AssemblyReferenceIdentity From(
        AssemblyReferenceHandle handle,
        AssemblyReferenceProjectionCache cache) =>
        cache.Project(handle);

    internal static AssemblyReferenceIdentity Create(
        MetadataReader reader,
        System.Reflection.Metadata.AssemblyReference reference,
        string? publicKeyToken)
    {
        return new AssemblyReferenceIdentity(
            reader.GetString(reference.Name),
            reference.Version,
            StringOrNull(reader, reference.Culture),
            publicKeyToken);
    }

    public static AssemblyReferenceIdentity FromAssemblyDefinition(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            throw new BadImageFormatException("The metadata image is not an assembly.");

        var definition = reader.GetAssemblyDefinition();
        return new AssemblyReferenceIdentity(
            reader.GetString(definition.Name),
            definition.Version,
            StringOrNull(reader, definition.Culture),
            TokenOrNull(reader, definition.PublicKey, isPublicKey: true));
    }

    internal static string? StringOrNull(
        MetadataReader reader,
        StringHandle handle)
        => handle.IsNil ? null : reader.GetString(handle);

    internal static string? TokenOrNull(
        MetadataReader reader,
        BlobHandle handle,
        bool isPublicKey)
    {
        if (handle.IsNil)
            return null;

        if (!isPublicKey
            && reader.GetBlobReader(handle).Length != 8)
        {
            throw new BadImageFormatException(
                "An assembly-reference public-key token must contain exactly 8 bytes.");
        }

        var bytes = reader.GetBlobBytes(handle);
        return isPublicKey
            ? ComputePublicKeyToken(bytes)
            : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Derives the lowercase-hex public-key token from a full public-key blob
    /// (ECMA-335 II.23.3: SHA-1 hash, last 8 bytes, byte-reversed). Shared with
    /// <see cref="AssemblyDefinition"/> canonicalization, whose <c>PublicKey</c>
    /// column is always the full key, never a pre-computed token.
    /// </summary>
    public static string ComputePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        Span<byte> token = stackalloc byte[8];
        for (int i = 0; i < token.Length; i++)
            token[i] = hash[hash.Length - 1 - i];
        return Convert.ToHexString(token).ToLowerInvariant();
    }
}

internal readonly record struct AssemblyReferenceKeyBlob(
    BlobHandle Handle,
    bool IsPublicKey);

internal readonly record struct AssemblyReferenceRowKey(
    StringHandle Name,
    Version Version,
    StringHandle Culture,
    BlobHandle PublicKeyOrToken,
    bool IsPublicKey);

internal sealed class AssemblyReferenceProjectionCache
{
    readonly MetadataReader _reader;
    readonly Dictionary<
        AssemblyReferenceRowKey,
        AssemblyReferenceIdentity> _identities = [];
    readonly Dictionary<
        AssemblyReferenceKeyBlob,
        string?> _tokens = [];

    internal AssemblyReferenceProjectionCache(
        MetadataReader reader) =>
        _reader = reader;

    internal AssemblyReferenceIdentity Project(
        AssemblyReferenceHandle handle)
    {
        var reference = _reader.GetAssemblyReference(handle);
        bool isPublicKey =
            (reference.Flags & AssemblyFlags.PublicKey) != 0;
        var rowKey = new AssemblyReferenceRowKey(
            reference.Name,
            reference.Version,
            reference.Culture,
            reference.PublicKeyOrToken,
            isPublicKey);
        if (_identities.TryGetValue(
                rowKey,
                out AssemblyReferenceIdentity? identity))
        {
            return identity;
        }

        var key = new AssemblyReferenceKeyBlob(
            reference.PublicKeyOrToken,
            isPublicKey);
        if (!_tokens.TryGetValue(key, out string? token))
        {
            token = AssemblyReferenceIdentity.TokenOrNull(
                _reader,
                reference.PublicKeyOrToken,
                isPublicKey);
            _tokens.Add(key, token);
        }

        identity = AssemblyReferenceIdentity.Create(
            _reader,
            reference,
            token);
        _identities.Add(rowKey, identity);
        return identity;
    }
}

/// <summary>
/// Callback boundary for consumers that know metadata assembly identity but not
/// where an assembly should come from.
/// </summary>
public interface IAssemblyReferenceResolver
{
    ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope);
}
