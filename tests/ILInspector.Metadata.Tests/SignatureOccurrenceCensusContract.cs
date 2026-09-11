using System.Text.Json;

namespace ILInspector.Metadata.Tests;

internal static class SignatureOccurrenceCensusContract
{
    internal const int SchemaVersion = 1;
    internal const int CurrentVersion = 2;

    internal static void RequireComparableBaseline(JsonElement baseline, JsonElement manifest)
    {
        int version = baseline.TryGetProperty("contractVersion", out var contract)
            && contract.TryGetInt32(out int recorded) ? recorded : 0;
        if (version != CurrentVersion)
            throw new InvalidDataException(
                $"Signature census contract differs: baseline v{version}, current v{CurrentVersion}. "
                + "Unversioned evidence is v0, not the current contract; establish a new baseline.");
        if (baseline.GetProperty("schemaVersion").GetInt32() != SchemaVersion)
            throw new InvalidDataException("Unsupported signature census report schema.");
        if (!baseline.GetProperty("complete").GetBoolean()
            || !baseline.GetProperty("productionCeilingsEnforced").GetBoolean())
        {
            throw new InvalidDataException("A signature census baseline must be complete and production-bounded.");
        }

        var expected = manifest.GetProperty("tiers");
        var fingerprints = baseline.GetProperty("inputFingerprints");
        var tiers = baseline.GetProperty("tiers");
        if (fingerprints.EnumerateObject().Count() != expected.GetArrayLength()
            || tiers.EnumerateObject().Count() != expected.GetArrayLength())
        {
            throw new InvalidDataException("Signature census tier populations differ.");
        }
        foreach (var input in expected.EnumerateArray())
        {
            string name = input.GetProperty("tier").GetString()!;
            if (!fingerprints.TryGetProperty(name, out var fingerprint)
                || fingerprint.GetString() != input.GetProperty("orderedSha256").GetString()
                || !tiers.TryGetProperty(name, out var tier)
                || tier.GetProperty("assemblies").GetInt32() != input.GetProperty("assemblies").GetInt32())
            {
                throw new InvalidDataException($"Signature census inputs differ for tier '{name}'.");
            }
            if (tier.GetProperty("rejected").GetInt64() != 0)
                throw new InvalidDataException($"The baseline contains refused signatures in tier '{name}'.");
            var budgets = tier.GetProperty("budgets");
            var limits = SignatureOccurrenceLimits.Default;
            if (budgets.GetProperty("nodes").GetProperty("ceiling").GetInt32() != limits.Nodes
                || budgets.GetProperty("copies").GetProperty("ceiling").GetInt32() != limits.Copies
                || budgets.GetProperty("work").GetProperty("ceiling").GetInt32() != limits.Work)
            {
                throw new InvalidDataException($"Signature census budget ceilings differ for tier '{name}'.");
            }
        }
    }
}
