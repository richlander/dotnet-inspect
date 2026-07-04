using System.Security.Cryptography;
using System.Text;

namespace ILInspector.MetadataPrimitives;

public enum MemberAnchorFormat
{
    StableSelector,
    CanonicalSignature,
    Fingerprint,
    Qualified,
}

public sealed record MemberAnchor(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName)
{
    public const string FingerprintPrefix = "dotnet-inspect.member-index.v1\n";

    public string Format(MemberAnchorFormat format)
        => format switch
        {
            MemberAnchorFormat.CanonicalSignature => CanonicalSignature,
            MemberAnchorFormat.Fingerprint => Fingerprint,
            MemberAnchorFormat.Qualified => $"{TypeFullName}.{StableSelector}",
            _ => StableSelector,
        };

    public static string ComputeFingerprint(string canonicalSignature)
    {
        var input = Encoding.UTF8.GetBytes(FingerprintPrefix + canonicalSignature);
        var hash = SHA256.HashData(input);
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }
}
