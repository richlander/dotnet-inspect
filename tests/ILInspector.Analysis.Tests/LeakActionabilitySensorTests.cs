using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public sealed class LeakActionabilitySensorTests
{
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
}
