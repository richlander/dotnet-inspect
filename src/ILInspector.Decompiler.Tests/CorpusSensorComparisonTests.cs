using System.Collections.Immutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CorpusSensorComparisonTests
{
    [Fact]
    public void OptInNet11Profile_UsesDistinctDescriptionAndCardHeading()
    {
        Assert.Contains(
            "net11 opt-in compiler-feature corpus",
            CorpusSensor.DescriptionForProfile(CorpusProfile.OptInNet11),
            StringComparison.Ordinal);
        Assert.Equal(
            "### Decompiler net11 opt-in feature diff",
            CorpusSensor.QualityCardHeadingForProfile(CorpusProfile.OptInNet11));
        Assert.True(
            CorpusSensor.ShouldGateAggregateRates(
                CorpusProfile.OptInNet11,
                qualityDiffCard: true,
                qualityCardRisky: false));
        Assert.False(
            CorpusSensor.ShouldGateAggregateRates(
                CorpusProfile.RealWorld,
                qualityDiffCard: true,
                qualityCardRisky: false));
    }

    [Fact]
    public void ExactReferenceRecompileRegressions_FlagsExactToRecompileAndContextFail()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("Recompile", fidelityReference: "Exact", fidelityCheck: "RecompileFail"),
            RtsMethod("Context", fidelityReference: "Exact", fidelityCheck: "ContextFail"));

        var offenders = CorpusSensor.ExactReferenceRecompileRegressions(snapshot);

        Assert.Equal(2, offenders.Length);
        Assert.Contains(offenders, m => m.Method == "Recompile");
        Assert.Contains(offenders, m => m.Method == "Context");
    }

    [Fact]
    public void ExactReferenceRecompileRegressions_IgnoresRescuedSameAndUnpaired()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("Rescued", fidelityReference: "OpcodeDiff", fidelityCheck: "Exact"),
            RtsMethod("Same", fidelityReference: "Exact", fidelityCheck: "Exact"),
            RtsMethod("OpcodeDrift", fidelityReference: "Exact", fidelityCheck: "OpcodeDiff"),
            RtsMethod("NoReference", fidelityReference: null, fidelityCheck: "RecompileFail"));

        Assert.Empty(CorpusSensor.ExactReferenceRecompileRegressions(snapshot));
    }

    [Fact]
    public void ExactReferenceRecompileRegressions_OnlyAppliesToReturnToSenderOracle()
    {
        var snapshot = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: [RtsMethod("Recompile", fidelityReference: "Exact", fidelityCheck: "RecompileFail")],
            fidelityOracle: CorpusFidelityOracle.CompileBack);

        Assert.Empty(CorpusSensor.ExactReferenceRecompileRegressions(snapshot));
    }

    [Fact]
    public void EvaluateRtsParityBurndown_FlagsNewRegressionNotInManifest()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("Known", fidelityReference: "Exact", fidelityCheck: "RecompileFail"),
            RtsMethod("New", fidelityReference: "Exact", fidelityCheck: "RecompileFail"));

        var evaluation = CorpusSensor.EvaluateRtsParityBurndown(
            snapshot,
            ["Pinned!T::Known#0"]);

        Assert.Equal(2, evaluation.KnownGaps.Length);
        var offender = Assert.Single(evaluation.NewRegressions);
        Assert.Equal("New", offender.Method);
        Assert.Empty(evaluation.ResolvedRows);
    }

    [Fact]
    public void EvaluateRtsParityBurndown_PassesWhenAllGapsAreInManifest()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("Known", fidelityReference: "Exact", fidelityCheck: "ContextFail"));

        var evaluation = CorpusSensor.EvaluateRtsParityBurndown(
            snapshot,
            ["Pinned!T::Known#0"]);

        Assert.Empty(evaluation.NewRegressions);
        Assert.Single(evaluation.KnownGaps);
        Assert.Empty(evaluation.ResolvedRows);
    }

    [Fact]
    public void EvaluateRtsParityBurndown_ReportsResolvedRowsNoLongerFailing()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("StillExact", fidelityReference: "Exact", fidelityCheck: "Exact"));

        var evaluation = CorpusSensor.EvaluateRtsParityBurndown(
            snapshot,
            ["Pinned!T::Fixed#0"]);

        Assert.Empty(evaluation.NewRegressions);
        Assert.Empty(evaluation.KnownGaps);
        Assert.Equal("Pinned!T::Fixed#0", Assert.Single(evaluation.ResolvedRows));
    }

    [Fact]
    public void EvaluateRtsParityBurndown_WithEmptyManifestTreatsEveryGapAsNew()
    {
        var snapshot = ReturnToSenderSnapshot(
            RtsMethod("A", fidelityReference: "Exact", fidelityCheck: "RecompileFail"),
            RtsMethod("B", fidelityReference: "Exact", fidelityCheck: "ContextFail"));

        var evaluation = CorpusSensor.EvaluateRtsParityBurndown(snapshot, []);

        Assert.Equal(2, evaluation.NewRegressions.Length);
    }

    [Fact]
    public void Compare_RejectsCorpusProfileMismatch()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            profile: CorpusProfile.OptInNet11);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null);

        var regressions = CorpusSensor.Compare(baseline, current, []);

        Assert.Contains(
            "corpus profile differs (baseline opt-in-net11, current real-world)",
            regressions);
    }

    [Fact]
    public void FeatureCoverageFailures_RejectsMissingOptInEvidence()
    {
        var snapshot = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            profile: CorpusProfile.OptInNet11,
            featureCoverage: CompleteFeatureCoverage().Remove("union-declarations"));

        var failures = CorpusSensor.FeatureCoverageFailures(snapshot);

        Assert.Contains(
            "feature evidence 'union-declarations' is 0; expected at least 1",
            failures);
    }

    [Fact]
    public void Compare_RejectsFeatureEvidenceDrop()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            profile: CorpusProfile.OptInNet11,
            featureCoverage: CompleteFeatureCoverage().SetItem("union-switch-methods", 2));
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            profile: CorpusProfile.OptInNet11,
            featureCoverage: CompleteFeatureCoverage());

        var regressions = CorpusSensor.Compare(baseline, current, []);

        Assert.Contains(
            "feature evidence 'union-switch-methods' dropped (baseline 2, current 1)",
            regressions);
    }

    [Fact]
    public void CompilerFeatureOptions_ReplaysMemorySafetyModeFromModuleMetadata()
    {
        string updatedAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        string legacyAssembly =
            typeof(ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures).Assembly.Location;

        var updated = CompilerFeatureOptions.ParseOptions(updatedAssembly);
        var legacy = CompilerFeatureOptions.ParseOptions(legacyAssembly);
        var updatedFunction = ImportFirstMethod(updatedAssembly);
        var legacyFunction = ImportFirstMethod(legacyAssembly);

        Assert.Contains(
            updated.Features,
            feature => feature.Key == "updated-memory-safety-rules" && feature.Value == "true");
        Assert.DoesNotContain(
            legacy.Features,
            feature => feature.Key == "updated-memory-safety-rules");
        Assert.True(updatedFunction.UsesUpdatedMemorySafetyRules);
        Assert.False(legacyFunction.UsesUpdatedMemorySafetyRules);
    }

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
    public void Compare_DoesNotGateFidelityCountsWhenPinnedSamplesDiffer()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("Two", "OpcodeDiff")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityOpcodeDiffMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.DoesNotContain(regressions, regression => regression.StartsWith("fidelity opcode diffs", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_GatesFidelityCountsWhenPinnedSamplesMatch()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "OpcodeDiff")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityOpcodeDiffMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(regressions, regression => regression.StartsWith("fidelity opcode diffs (pinned)", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_RejectsFidelityOracleMismatch()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1,
            fidelityOracle: CorpusFidelityOracle.ReturnToSender);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(
            regressions,
            regression => regression == "fidelity oracle differs (baseline compile-back, current rts-parity)");
        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);
        Assert.Contains("Fidelity exact (oracle differs)", report);
    }

    [Fact]
    public void AlignReturnToSenderResults_ReportsUnavailableTarget()
    {
        var target = new FidelityCheck.CompileBackResult(
            "Fixture",
            "Method",
            1,
            "(corelib:System.Int32) -> corelib:System.Int32",
            FidelityCheck.CompileBackStatus.Exact,
            "ldarg.1 ret",
            "ldarg.1 ret",
            Detail: null);

        var result = Assert.Single(
            CorpusSensor.AlignReturnToSenderResultsForTesting(
                [target],
                Array.Empty<ReturnToSender.Result>()));

        Assert.Equal(FidelityCheck.CompileBackStatus.ContextFail, result.Status);
        Assert.Equal("return-to-sender-target-unavailable", result.Detail);
        Assert.Equal("return-to-sender", result.CaptureDetail);
        var buckets = FidelityCheck.SummarizeFailures(
            [result],
            FidelityCheck.CompileBackStatus.ContextFail);
        Assert.Equal(1, buckets["return-to-sender target unavailable"].Count);
    }

    [Fact]
    public void SummarizeReturnToSenderParity_ClassifiesRescuedSameAndWorse()
    {
        var exact = CompileBackResult("Exact", FidelityCheck.CompileBackStatus.Exact);
        var rescued = CompileBackResult("Rescued", FidelityCheck.CompileBackStatus.OpcodeDiff);
        var worse = CompileBackResult("Worse", FidelityCheck.CompileBackStatus.Exact);

        var parity = CorpusSensor.SummarizeReturnToSenderParityForTesting(
            [exact, rescued, worse],
            [
                exact,
                rescued with { Status = FidelityCheck.CompileBackStatus.Exact },
                worse with { Status = FidelityCheck.CompileBackStatus.OpcodeDiff },
            ]);

        Assert.Equal(1, parity.RescuedMethods);
        Assert.Equal(1, parity.SameMethods);
        Assert.Equal(1, parity.WorseMethods);
        Assert.Equal(3, parity.ComparedMethods);
    }

    [Fact]
    public void Compare_GatesReturnToSenderParityWhenSampleMatches()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1,
            fidelityOracle: CorpusFidelityOracle.ReturnToSender,
            returnToSenderParity: new ReturnToSenderParityMetrics(0, 1, 0));
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "OpcodeDiff")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityOpcodeDiffMethods: 1,
            fidelityOracle: CorpusFidelityOracle.ReturnToSender,
            returnToSenderParity: new ReturnToSenderParityMetrics(0, 0, 1));

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(
            regressions,
            regression => regression.StartsWith("RTS parity worse methods increased", StringComparison.Ordinal));
    }

    [Fact]
    public void DeterministicCompileBackTargetAttempts_UsesStableSampleRegardlessOfInputOrder()
    {
        string assemblyPath = Path.Combine(Environment.CurrentDirectory, "pinned.dll");
        var methods = Enumerable.Range(0, 105)
            .Select(i => SnapshotMethod($"M{i:000}", assemblyPath: "pinned.dll", fidelityCheck: "not-sampled"))
            .Append(SnapshotMethod("<Generated>", assemblyPath: "pinned.dll", fidelityCheck: "not-sampled"))
            .ToArray();

        var forward = CorpusSensor.DeterministicCompileBackTargetAttemptsForTesting(methods, assemblyPath, cap: 1);
        var reversed = CorpusSensor.DeterministicCompileBackTargetAttemptsForTesting(methods.Reverse().ToArray(), assemblyPath, cap: 1);

        Assert.Equal(100, forward.Count);
        Assert.Equal(
            forward.Select(target => $"{target.Type}::{target.Method}{target.Signature}"),
            reversed.Select(target => $"{target.Type}::{target.Method}{target.Signature}"));
        Assert.DoesNotContain(forward, target => target.Method.Contains('<', StringComparison.Ordinal));
        Assert.All(forward, target => Assert.Equal(assemblyPath, target.AssemblyPath));
    }

    [Fact]
    public void Compare_DoesNotGateSemanticCountsWhenPinnedSamplesDiffer()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "valid")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("Two", "semantic-defect:CS0159")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1,
            semanticDefectMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.DoesNotContain(regressions, regression => regression.StartsWith("semantic defect methods", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_GatesSemanticCountsWhenPinnedSamplesMatch()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "valid")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "semantic-defect:CS0159")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1,
            semanticDefectMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(regressions, regression => regression.StartsWith("semantic defect methods (pinned)", StringComparison.Ordinal));
    }

    [Fact]
    public void PinnedGateSummary_MarksSkippedCountGatesAsUngated()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("One", "valid")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("Two", "semantic-defect:CS0159")),
            validityCompileCap: 1,
            semanticCheckedMethods: 1,
            semanticDefectMethods: 1);

        string summary = Assert.IsType<string>(
            CorpusSensor.PinnedGateSummaryForTesting(baseline, current));

        Assert.Contains("Full malformed ungated (sampling differs)", summary);
        Assert.Contains("semantic defects ungated (sampling differs)", summary);
        Assert.Contains("fidelity ungated (sampling differs; rely on changed-method fidelity)", summary);
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
    public void QualityMetricChanges_SeparatesSyntacticAndSemanticSamples()
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
            pinnedMethods: ValidityMethods(("One", "syntax-valid"), ("Two", "syntax-valid")),
            validityCompileCap: 1,
            semanticCheckedMethods: 0,
            semanticDefectMethods: 0);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);

        Assert.Contains("Full malformed (-)", report);
        Assert.DoesNotContain("Full malformed (sampling differs)", report);
        Assert.Contains("Semantic defects (sampling differs)", report);
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

    [Fact]
    public void QualityMetricChanges_TreatsFidelityMovementAsContextWhenSamplesDiffer()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("Two", "OpcodeDiff")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityOpcodeDiffMethods: 1);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);

        Assert.Contains("Fidelity opcode diffs (sampling differs)", report);
        Assert.Contains("Fidelity exact (sampling differs)", report);
        Assert.DoesNotContain("Fidelity opcode diffs (-)", report);
        Assert.DoesNotContain("(bad)", report);
    }

    static CorpusSensorSnapshot Snapshot(
        int totalMethods,
        int fullyRaisedMethods,
        int fullyRaisedBasisPoints,
        IReadOnlyList<CorpusMethodSnapshot>? pinnedMethods,
        int validityCompileCap = 0,
        int semanticCheckedMethods = 0,
        int semanticDefectMethods = 0,
        int fidelityCompileCap = 0,
        int fidelityCheckedMethods = 0,
        int fidelityExactMethods = 0,
        int fidelityOpcodeDiffMethods = 0,
        CorpusFidelityOracle fidelityOracle = CorpusFidelityOracle.CompileBack,
        ReturnToSenderParityMetrics? returnToSenderParity = null,
        CorpusProfile profile = CorpusProfile.RealWorld,
        IReadOnlyDictionary<string, int>? featureCoverage = null)
    {
        return new CorpusSensorSnapshot(
            SchemaVersion: 1,
            Description: "test",
            GeneratedUtc: DateTimeOffset.UnixEpoch,
            ValidityCompileCap: validityCompileCap,
            FidelityCompileCap: fidelityCompileCap,
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
                Fidelity: new FidelitySensorMetrics(
                    fidelityCheckedMethods,
                    fidelityExactMethods,
                    fidelityOpcodeDiffMethods,
                    RecompileFailMethods: 0,
                    ContextFailMethods: 0,
                    NotFullMethods: 0,
                    ReturnToSenderParity: returnToSenderParity)),
            FidelityOracle: fidelityOracle,
            Profile: profile,
            FeatureCoverage: featureCoverage);
    }

    static ImmutableDictionary<string, int> CompleteFeatureCoverage()
        => new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["await-recovery-methods"] = 1,
            ["cross-assembly-requires-unsafe-methods"] = 1,
            ["legacy-memory-safety-control-methods"] = 1,
            ["runtime-async-awaiter-methods"] = 1,
            ["runtime-async-await-using-methods"] = 1,
            ["runtime-async-exception-methods"] = 1,
            ["runtime-async-loop-methods"] = 1,
            ["runtime-async-methods"] = 1,
            ["union-declarations"] = 1,
            ["union-switch-methods"] = 1,
            ["union-types"] = 1,
            ["updated-memory-safety-methods"] = 1,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    static IrFunction ImportFirstMethod(string assemblyPath)
    {
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        return IrImporter.GetStableSampleCandidates(source, 1).Single().Build(source);
    }

    static FidelityCheck.CompileBackResult CompileBackResult(
        string method,
        FidelityCheck.CompileBackStatus status)
        => new(
            "Fixture",
            method,
            0,
            "() -> corelib:System.Int32",
            status,
            "ldc.i4.0 ret",
            "ldc.i4.0 ret",
            Detail: null);

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

    static CorpusMethodSnapshot SnapshotMethod(
        string method,
        string assemblyPath = "nuget:pinned/lib.dll",
        string validity = "not-sampled",
        string fidelityCheck = "not-sampled",
        string? fidelityReference = null)
        => new(
            Assembly: "Pinned",
            AssemblyPath: assemblyPath,
            Type: "T",
            Method: method,
            Overload: 0,
            Signature: "()",
            Fidelity: "Full",
            FullyRaised: true,
            Residual: null,
            PassBug: null,
            Validity: validity,
            FidelityCheck: fidelityCheck,
            FidelityReference: fidelityReference);

    static CorpusMethodSnapshot RtsMethod(
        string method,
        string? fidelityReference,
        string fidelityCheck)
        => SnapshotMethod(
            method,
            fidelityCheck: fidelityCheck,
            fidelityReference: fidelityReference);

    static CorpusSensorSnapshot ReturnToSenderSnapshot(params CorpusMethodSnapshot[] methods)
        => Snapshot(
            totalMethods: methods.Length,
            fullyRaisedMethods: methods.Length,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: methods,
            fidelityOracle: CorpusFidelityOracle.ReturnToSender);

    static IReadOnlyList<CorpusMethodSnapshot> ValidityMethods(
        params (string Method, string Validity)[] values)
    {
        var methods = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>(values.Length);
        foreach (var value in values)
        {
            methods.Add(SnapshotMethod(value.Method, validity: value.Validity));
        }
        return methods.ToImmutable();
    }

    static IReadOnlyList<CorpusMethodSnapshot> FidelityMethods(
        params (string Method, string FidelityCheck)[] values)
    {
        var methods = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>(values.Length);
        foreach (var value in values)
        {
            methods.Add(SnapshotMethod(value.Method, fidelityCheck: value.FidelityCheck));
        }
        return methods.ToImmutable();
    }
}
