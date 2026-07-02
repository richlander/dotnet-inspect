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
        return new AssemblyReferenceIdentity(
            reader.GetString(reference.Name),
            reference.Version,
            StringOrNull(reader, reference.Culture),
            TokenOrNull(reader, reference.PublicKeyOrToken, (reference.Flags & AssemblyFlags.PublicKey) != 0));
    }

    static string? StringOrNull(MetadataReader reader, StringHandle handle)
        => handle.IsNil ? null : reader.GetString(handle);

    static string? TokenOrNull(MetadataReader reader, BlobHandle handle, bool isPublicKey)
    {
        if (handle.IsNil)
            return null;

        var bytes = reader.GetBlobBytes(handle);
        return isPublicKey
            ? ComputePublicKeyToken(bytes)
            : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string ComputePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        Span<byte> token = stackalloc byte[8];
        for (int i = 0; i < token.Length; i++)
            token[i] = hash[hash.Length - 1 - i];
        return Convert.ToHexString(token).ToLowerInvariant();
    }
}

/// <summary>
/// Roslyn-free descriptor for an assembly resolved by identity.
/// </summary>
public sealed record ResolvedAssemblyReference(
    AssemblyReferenceIdentity Identity,
    string? Path,
    Func<Stream> OpenRead,
    string? Provenance = null);

/// <summary>
/// Callback boundary for consumers that know metadata assembly identity but not
/// where an assembly should come from.
/// </summary>
public interface IAssemblyReferenceResolver
{
    ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope);
}

public static class AssemblyReferenceResolverExtensions
{
    public static AssemblyLocator ToAssemblyLocator(this IAssemblyReferenceResolver resolver)
        => (assemblyName, scope) =>
        {
            var resolved = resolver.Resolve(new AssemblyReferenceIdentity(assemblyName, Version: null, Culture: null, PublicKeyToken: null), scope);
            return resolved?.Path ?? (resolved is null
                ? null
                : throw new NotSupportedException("AssemblyLocator requires a file-backed resolver result."));
        };

    public static IAssemblyReferenceResolver ToAssemblyReferenceResolver(this AssemblyLocator locator)
        => new AssemblyLocatorReferenceResolver(locator);

    sealed class AssemblyLocatorReferenceResolver(AssemblyLocator locator) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            if (locator(identity.Name, scope) is not { } path)
                return null;

            return new ResolvedAssemblyReference(
                identity,
                path,
                () => File.OpenRead(path),
                Provenance: "AssemblyLocator");
        }
    }
}
