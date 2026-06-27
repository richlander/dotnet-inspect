using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace ILInspector.Analysis;

/// <summary>
/// Recognizes Microsoft framework assembly identity by public-key-token (#1708 Row A).
/// The product is SRM-direct and does not load referenced assemblies, but the
/// public-key-token travels in metadata (AssemblyReference / AssemblyDefinition), so a
/// framework signal predicate can require strong identity instead of trusting a simple
/// assembly name. This rejects spoofs such as a user assembly named <c>System.Linq</c>
/// exposing a <c>System.Linq.Enumerable</c> lookalike.
/// </summary>
internal static class FrameworkAssemblyKeys
{
    // Public-key-tokens used by the .NET frameworks (lowercase hex).
    static readonly HashSet<string> s_frameworkTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "b77a5c561934e089", // ECMA / .NET Framework: mscorlib, System, System.Core, System.Xml, ...
        "b03f5f7f11d50a3a", // Microsoft .NET ref assemblies / contracts: System.*, System.Linq, ...
        "7cec85d7bea7798e", // System.Private.CoreLib (and the Silverlight key)
        "cc7b13ffcd2ddd51", // netstandard and several System.* packages
        "31bf3856ad364e35", // Microsoft key: WindowsBase, System.ValueTuple, ...
        "adb9793829ddae60", // assorted System.* packages
    };

    public static bool IsFrameworkReference(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        return IsFrameworkKeyOrToken(
            reader.GetBlobBytes(reference.PublicKeyOrToken),
            isFullKey: (reference.Flags & AssemblyFlags.PublicKey) != 0);
    }

    public static bool IsFrameworkDefinition(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;
        // The AssemblyDefinition row stores the full public key, never the token.
        return IsFrameworkKeyOrToken(reader.GetBlobBytes(reader.GetAssemblyDefinition().PublicKey), isFullKey: true);
    }

    static bool IsFrameworkKeyOrToken(byte[] keyOrToken, bool isFullKey)
    {
        if (keyOrToken.Length == 0)
            return false;
        byte[] token = isFullKey ? ComputeToken(keyOrToken) : keyOrToken;
        return token.Length == 8 && s_frameworkTokens.Contains(Convert.ToHexString(token));
    }

    // The public-key-token is the low 8 bytes of the SHA-1 hash of the public key,
    // emitted in reverse order (ECMA-335 II.6.3).
    static byte[] ComputeToken(byte[] publicKey)
    {
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(publicKey, hash);
        var token = new byte[8];
        for (int i = 0; i < token.Length; i++)
            token[i] = hash[hash.Length - 1 - i];
        return token;
    }
}
