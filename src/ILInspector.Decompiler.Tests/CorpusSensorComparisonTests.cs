using System.Collections.Immutable;

using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class CorpusSensorComparisonTests
{
    [Fact]
    public void Compare_NormalPrQuickGate_LeavesAggregateRateDropAdvisoryWhenPinnedSubsetIsStable()
    {
        var baseline = Snapshot(
            totalMethods: 100,
            fullyRaisedMethods: 90,
            fullyRaisedBasisPoints: 9000,
            pinnedMethods: PinnedMethods(fullyRaised: 9, conditional: 0));
        var current = Snapshot(
            totalMethods: 110,
            fullyRaisedMethods: 88,
            fullyRaisedBasisPoints: 8000,
            pinnedMethods: PinnedMethods(fullyRaised: 9, conditional: 0));

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Empty(regressions);
    }

    [Fact]
    public void Compare_RiskyGate_StillFailsAggregateRateDrop()
    {
        var baseline = Snapshot(
            totalMethods: 100,
            fullyRaisedMethods: 90,
            fullyRaisedBasisPoints: 9000,
            pinnedMethods: PinnedMethods(fullyRaised: 9, conditional: 0));
        var current = Snapshot(
            totalMethods: 110,
            fullyRaisedMethods: 88,
            fullyRaisedBasisPoints: 8000,
            pinnedMethods: PinnedMethods(fullyRaised: 9, conditional: 0));

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: true);

        Assert.Contains(regressions, regression => regression.StartsWith("fully-raised rate dropped", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_NormalPrQuickGate_FailsPinnedRateDrop()
    {
        var baseline = Snapshot(
            totalMethods: 100,
            fullyRaisedMethods: 90,
            fullyRaisedBasisPoints: 9000,
            pinnedMethods: PinnedMethods(fullyRaised: 9, conditional: 0));
        var current = Snapshot(
            totalMethods: 100,
            fullyRaisedMethods: 90,
            fullyRaisedBasisPoints: 9000,
            pinnedMethods: PinnedMethods(fullyRaised: 8, conditional: 0));

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(regressions, regression => regression.StartsWith("fully-raised rate (pinned) dropped", StringComparison.Ordinal));
    }

    static CorpusSensorSnapshot Snapshot(
        int totalMethods,
        int fullyRaisedMethods,
        int fullyRaisedBasisPoints,
        IReadOnlyList<CorpusMethodSnapshot> pinnedMethods)
    {
        return new CorpusSensorSnapshot(
            SchemaVersion: 1,
            Description: "test",
            GeneratedUtc: DateTimeOffset.UnixEpoch,
            ValidityCompileCap: 0,
            FidelityCompileCap: 0,
            MethodCap: 100,
            Tolerances: CorpusSensorTolerances.Default,
            Assemblies: [new CorpusAssemblySnapshot("Test", "test.dll", totalMethods)],
            Methods: pinnedMethods,
            Metrics: new CorpusSensorMetrics(
                TotalMethods: totalMethods,
                FullyRaisedMethods: fullyRaisedMethods,
                FullyRaisedBasisPoints: fullyRaisedBasisPoints,
                ConditionalBranchMethods: 0,
                ConditionalBranchBasisPoints: 0,
                ForwardMergeStoppedContainers: 0,
                ForwardMergeBasisPoints: 0,
                FullMalformedMethods: 0,
                SemanticCheckedMethods: 0,
                SemanticDefectMethods: 0,
                PassBugs: 0,
                ResidualBuckets: ImmutableDictionary<string, int>.Empty,
                Structuring: new StructuringSensorMetrics(0, 0, 0, 0, 0, ImmutableDictionary<string, int>.Empty),
                Fidelity: new FidelitySensorMetrics(0, 0, 0, 0, 0, 0)));
    }

    static IReadOnlyList<CorpusMethodSnapshot> PinnedMethods(int fullyRaised, int conditional)
    {
        var methods = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>(10);
        for (int i = 0; i < 10; i++)
        {
            bool isConditional = i < conditional;
            bool isFullyRaised = i < fullyRaised;
            methods.Add(new CorpusMethodSnapshot(
                Assembly: "Pinned",
                AssemblyPath: "nuget:pinned/lib.dll",
                Type: "T",
                Method: $"M{i}",
                Overload: 0,
                Signature: "()",
                Fidelity: isFullyRaised ? "Full" : "Partial",
                FullyRaised: isFullyRaised,
                Residual: isConditional ? "structuring: conditional-branch" : isFullyRaised ? null : "fidelity: unsupported-node",
                PassBug: null,
                Validity: "not-sampled",
                FidelityCheck: "not-sampled"));
        }
        return methods.ToImmutable();
    }
}
