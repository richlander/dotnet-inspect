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

    [Fact]
    public void QualityMetricChanges_TreatsSemanticDefectMovementAsContextWhenSamplesDiffer()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "semantic-defect:CS0159"), ("Two", "valid")),
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 1);
        var current = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("Three", "valid"), ("Four", "valid")),
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 0);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);
        string semanticRow = report.Split('\n').Single(line => line.Contains("Semantic defects", StringComparison.Ordinal));
        string malformedRow = report.Split('\n').Single(line => line.Contains("Full malformed", StringComparison.Ordinal));

        Assert.Contains("Semantic defects (sampling differs)", semanticRow);
        Assert.DoesNotContain("(good)", semanticRow);
        Assert.EndsWith("| n/a |", semanticRow.TrimEnd());
        Assert.Contains("Full malformed (sampling differs)", malformedRow);
        Assert.EndsWith("| n/a |", malformedRow.TrimEnd());
    }

    [Fact]
    public void QualityMetricChanges_ScoresSemanticDefectMovementWhenSamplesMatch()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "semantic-defect:CS0159"), ("Two", "valid")),
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 1);
        var current = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "valid"), ("Two", "valid")),
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 0);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);
        string semanticRow = report.Split('\n').Single(line => line.Contains("Semantic defects", StringComparison.Ordinal));

        Assert.Contains("Semantic defects (-)", semanticRow);
        Assert.Contains("(good)", semanticRow);
        Assert.EndsWith("| -1 |", semanticRow.TrimEnd());
        Assert.DoesNotContain("sampling differs", report);
    }

    [Fact]
    public void QualityMetricChanges_TreatsValidityMovementAsContextWithoutMethodDetails()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 1);
        var current = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "valid"), ("Two", "valid")),
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 0);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);

        Assert.Contains("Full malformed (sampling differs)", report);
        Assert.Contains("Semantic defects (sampling differs)", report);
    }

    static CorpusSensorSnapshot Snapshot(
        int totalMethods,
        int fullyRaisedMethods,
        int fullyRaisedBasisPoints,
        IReadOnlyList<CorpusMethodSnapshot>? pinnedMethods,
        int validityCompileCap = 0,
        int semanticCheckedMethods = 0,
        int semanticDefectMethods = 0)
    {
        return new CorpusSensorSnapshot(
            SchemaVersion: 1,
            Description: "test",
            GeneratedUtc: DateTimeOffset.UnixEpoch,
            ValidityCompileCap: validityCompileCap,
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
                SemanticCheckedMethods: semanticCheckedMethods,
                SemanticDefectMethods: semanticDefectMethods,
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

    static IReadOnlyList<CorpusMethodSnapshot> ValidityMethods(
        params (string Method, string Validity)[] values)
    {
        var methods = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>(values.Length);
        foreach (var value in values)
        {
            methods.Add(new CorpusMethodSnapshot(
                Assembly: "Pinned",
                AssemblyPath: "nuget:pinned/lib.dll",
                Type: "T",
                Method: value.Method,
                Overload: 0,
                Signature: "()",
                Fidelity: "Full",
                FullyRaised: true,
                Residual: null,
                PassBug: null,
                Validity: value.Validity,
                FidelityCheck: "not-sampled"));
        }
        return methods.ToImmutable();
    }
}
