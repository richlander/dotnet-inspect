using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Tests;

internal static class FindingTestData
{
    public static readonly FindingSubject Subject = new("test-library", "Test library");

    public static ExtensionMethodInfo ExtensionMember(
        string methodName,
        string extendedType,
        string extensionClass = "Test.Extensions",
        string kind = "method",
        string? signature = null)
    {
        string canonicalSignature =
            $"{(kind == "property" ? "P" : "M")}:{extensionClass}.{methodName}({extendedType})";
        string fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
        var anchor = new MemberAnchor(
            $"extension:{methodName}~{fingerprint}",
            canonicalSignature,
            fingerprint,
            extensionClass,
            methodName);
        return new ExtensionMethodInfo(
            methodName,
            extensionClass,
            "Test",
            extendedType,
            signature ?? $"void {methodName}(this {extendedType} value)",
            "Test",
            kind)
        {
            Anchor = anchor,
            CanonicalExtendedType = extendedType,
            ReturnType = kind == "property" ? "System.Object" : "System.Void",
        };
    }
}
