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
public static class FrameworkAssemblyKeys
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

    // The real Google.Protobuf NuGet package public-key-token. Generated-code suppression
    // gates on this so a user assembly that merely names itself Google.Protobuf cannot
    // make ordinary product code look protobuf-generated (#1735).
    public const string GoogleProtobufToken = "a7d26565bac4d604";

    /// <summary>
    /// Whether a specific assembly reference is NOT a Google.Protobuf spoof: true unless the
    /// reference is named Google.Protobuf with a public-key-token other than the real one.
    /// Stamped onto each decoded reference so generated-code suppression can reject a spoofed
    /// reference even when an authentic Google.Protobuf reference coexists in the same assembly
    /// (#1735). References to other assemblies are never protobuf, so they return true.
    /// </summary>
    public static bool IsAuthenticProtobufReference(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        if (reader.GetString(reference.Name) != "Google.Protobuf")
            return true;
        return TokenHex(
            reader.GetBlobBytes(reference.PublicKeyOrToken),
            isFullKey: (reference.Flags & AssemblyFlags.PublicKey) != 0) == GoogleProtobufToken;
    }

    /// <summary>
    /// Whether the inspected assembly definition is NOT a Google.Protobuf spoof: true unless the
    /// assembly is itself named Google.Protobuf with a public-key-token other than the real one.
    /// Used when a type resolves to the inspected assembly (a definition or a module-scoped
    /// reference) so a self-named unsigned Google.Protobuf cannot bootstrap-suppress its own
    /// types (#1735).
    /// </summary>
    public static bool IsAuthenticProtobufDefinition(MetadataReader reader)
    {
        if (!reader.IsAssembly || reader.GetString(reader.GetAssemblyDefinition().Name) != "Google.Protobuf")
            return true;
        return TokenHex(reader.GetBlobBytes(reader.GetAssemblyDefinition().PublicKey), isFullKey: true) == GoogleProtobufToken;
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

    // Lowercase hex public-key-token, or "" when unsigned / malformed.
    static string TokenHex(byte[] keyOrToken, bool isFullKey)
    {
        if (keyOrToken.Length == 0)
            return "";
        byte[] token = isFullKey ? ComputeToken(keyOrToken) : keyOrToken;
        return token.Length == 8 ? Convert.ToHexString(token).ToLowerInvariant() : "";
    }
}
