using System.Security.Cryptography;

using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public sealed class LeakActionabilitySensorTests
{
    const string MessagePackSha256 =
        "b2c1cc3fc4c262a0f7cfa14f668afc61c1f8b8b460b51d894d6331b63acc14b2";

    [Fact]
    public void Measure_ConsumesGenericProductClassification()
    {
        var report = LeakActionabilitySensor.Measure(
            [FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath()],
            examplesPerAssembly: 1000);
        LeakActionabilityAssembly assembly =
            Assert.Single(report.Assemblies);

        Assert.True(assembly.Opened);
        Assert.Contains(
            assembly.Examples,
            example =>
                example.Class == LeakActionabilitySensor.Untrusted
                && example.Method.EndsWith(
                    "::RentReadBeforeReturn",
                    StringComparison.Ordinal)
                && example.BoundarySet.Contains(
                    "Stream::Read",
                    StringComparison.Ordinal));
        Assert.Contains(
            assembly.Examples,
            example =>
                example.Class == LeakActionabilitySensor.Trusted
                && example.Method.EndsWith(
                    "::RentEncodeThenUnrelatedReadAfterReturn",
                    StringComparison.Ordinal)
                && example.BoundarySet.Contains(
                    "Encoding::GetBytes",
                    StringComparison.Ordinal)
                && !example.BoundarySet.Contains(
                    "ReadByte",
                    StringComparison.Ordinal));
        Assert.Contains(
            assembly.Examples,
            example =>
                example.Class == LeakActionabilitySensor.Unknown
                && example.Method.EndsWith(
                    "::RentLookalikeReadBeforeReturn",
                    StringComparison.Ordinal)
                && example.BoundarySet.Contains(
                    "LookalikeTextReader::Read",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Measure_RollsOlderPlatformReferencesForward()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "ResourceTriage",
            "MessagePack.dll");
        using (FileStream stream = File.OpenRead(path))
        {
            Assert.Equal(
                MessagePackSha256,
                Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        var report = LeakActionabilitySensor.Measure(
            [path],
            examplesPerAssembly: 1000);
        LeakActionabilityAssembly assembly =
            Assert.Single(report.Assemblies);

        Assert.True(assembly.Opened);
        Assert.False(assembly.TimedOut);
        Assert.Contains(
            assembly.Examples,
            example =>
                example.Class == LeakActionabilitySensor.Untrusted
                && example.Method.EndsWith(
                    "MessagePackReader::ReadStringSlow",
                    StringComparison.Ordinal)
                && example.BoundarySet.Contains(
                    "Decoder::GetChars",
                    StringComparison.Ordinal));
    }
}
