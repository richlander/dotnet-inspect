using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace ILInspector.Metadata.Tests;

public sealed class SignatureOccurrenceCensusContractTests
{
    const string Manifest = """
        {"tiers":[{"tier":"packages","assemblies":1,"orderedSha256":"same-inputs"}]}
        """;
    const string Baseline = """
        {
          "schemaVersion":1,"contractVersion":2,
          "complete":true,"productionCeilingsEnforced":true,
          "inputFingerprints":{"packages":"same-inputs"},
          "manifestSha256":"old-manifest-representation",
          "metadataAssemblySha256":"old-product",
          "tiers":{"packages":{
            "assemblies":1,"decoded":1,"rejected":0,
            "budgets":{
              "nodes":{"ceiling":65536,"maximum":1},
              "copies":{"ceiling":524288,"maximum":2},
              "work":{"ceiling":262144,"maximum":3}
            }
          }}
        }
        """;

    [Fact]
    public void RetainedV2Baseline_IsBoundToItsInputManifest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "docs", "data", "signature-decode", "v2");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
        using JsonDocument manifest = JsonDocument.Parse(bytes);
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "baseline.json")));
        SignatureOccurrenceCensusContract.RequireComparableBaseline(baseline.RootElement, manifest.RootElement);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)),
            baseline.RootElement.GetProperty("manifestSha256").GetString());
        foreach (var tier in baseline.RootElement.GetProperty("tiers").EnumerateObject())
        {
            var rows = baseline.RootElement.GetProperty("assemblies").EnumerateArray()
                .Where(row => row.GetProperty("tier").GetString() == tier.Name).ToArray();
            Assert.Equal(tier.Value.GetProperty("assemblies").GetInt32(), rows.Length);
            Assert.Equal(tier.Value.GetProperty("decoded").GetInt64(), rows.Sum(row =>
                row.GetProperty("methods").GetInt64()
                + row.GetProperty("fields").GetInt64()
                + row.GetProperty("properties").GetInt64()));
        }
    }

    [Fact]
    public void SameContractAndInputs_AreComparableAcrossProductBuilds()
    {
        Assert.Equal(2, SignatureOccurrenceCensusContract.CurrentVersion);
        var baseline = JsonNode.Parse(Baseline)!.AsObject();
        baseline["metadataAssemblySha256"] = "another-product";
        baseline["manifestSha256"] = "another-manifest-representation";
        Check(baseline);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DifferentOrUnversionedContracts_AreNotCompared(int? version)
    {
        var baseline = JsonNode.Parse(Baseline)!.AsObject();
        if (version is { } value)
            baseline["contractVersion"] = value;
        else
            baseline.Remove("contractVersion");
        var error = Assert.Throws<InvalidDataException>(() => Check(baseline));
        Assert.Contains($"baseline v{version ?? 0}, current v2", error.Message);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("incomplete")]
    [InlineData("unbounded")]
    [InlineData("inputs")]
    [InlineData("population")]
    [InlineData("refusal")]
    [InlineData("ceiling")]
    public void IncompatibleEvidence_IsNotCompared(string change)
    {
        var baseline = JsonNode.Parse(Baseline)!.AsObject();
        switch (change)
        {
            case "schema": baseline["schemaVersion"] = 2; break;
            case "incomplete": baseline["complete"] = false; break;
            case "unbounded": baseline["productionCeilingsEnforced"] = false; break;
            case "inputs": baseline["inputFingerprints"]!["packages"] = "different-inputs"; break;
            case "population": baseline["tiers"]!["packages"]!["assemblies"] = 2; break;
            case "refusal": baseline["tiers"]!["packages"]!["rejected"] = 1; break;
            case "ceiling": baseline["tiers"]!["packages"]!["budgets"]!["work"]!["ceiling"] = 262145; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.Throws<InvalidDataException>(() => Check(baseline));
    }

    static void Check(JsonObject baseline)
    {
        using JsonDocument report = JsonDocument.Parse(baseline.ToJsonString());
        using JsonDocument manifest = JsonDocument.Parse(Manifest);
        SignatureOccurrenceCensusContract.RequireComparableBaseline(report.RootElement, manifest.RootElement);
    }
}
