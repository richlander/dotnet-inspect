using System.Security.Cryptography;
using System.Text;

using ILInspector.Findings;

namespace ILInspector.Analysis;

internal static class PerformanceTriageCandidateId
{
    const string FingerprintNamespace = "dotnet-inspect.performance-triage.v1\n";
    public const int InitialFingerprintLength = 16;
    public const int MaximumFingerprintLength = 64;

    public static string Create(
        OptimizationOpportunity opportunity,
        string? descriptor,
        FindingKey? findingKey,
        int? ordinal,
        int fingerprintLength = InitialFingerprintLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            fingerprintLength,
            InitialFingerprintLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            fingerprintLength,
            MaximumFingerprintLength);

        string coordinate = opportunity.ILOffset is { } offset
            ? $"0x{opportunity.Method.MetadataToken:X8}+0x{offset:X4}"
            : $"0x{opportunity.Method.MetadataToken:X8}";
        string source = descriptor is null || findingKey is null
            ? "performance"
            : $"finding\n{Part(descriptor)}\n{Part(findingKey.Value.IdentityKey)}\n{Part(findingKey.Value.ScopeKey)}";
        string occurrence = ordinal?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        byte[] input = Encoding.UTF8.GetBytes(
            $"{FingerprintNamespace}{Part(source)}\n{Part(coordinate)}\n{Part(occurrence)}\n{Part(opportunity.Shape)}");
        string fingerprint = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        return $"pt~{fingerprint[..fingerprintLength]}";
    }

    static string Part(string? value)
        => value is null
            ? "-1:"
            : $"{value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{value}";
}
