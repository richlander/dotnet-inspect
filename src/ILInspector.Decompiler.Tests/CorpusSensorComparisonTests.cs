using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CorpusSensorComparisonTests
{
    [Fact]
    public void GitBaselineReference_ReadsTrackedBlob()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string path = Path.Combine(
            directory.FullName,
            "tools/DecompilerHarness/corpus/pr-quick-baseline.json");

        string baseline = CorpusSensor.ReadBaselineTextForTesting(path, "HEAD");

        Assert.Contains("\"schemaVersion\"", baseline);
        Assert.Contains("\"description\"", baseline);
    }

    [Theory]
    [InlineData("pr-quick-baseline.json")]
    [InlineData("real-world-baseline.json")]
    [InlineData("opt-in-net11-baseline.json")]
    public void CommittedCorpusBaseline_Deserializes(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string path = Path.Combine(
            directory.FullName,
            "tools/DecompilerHarness/corpus",
            fileName);

        var baseline = CorpusSensor.ReadBaselineForTesting(path);

        Assert.True(baseline.SchemaVersion > 0);
    }

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
    public void FidelityResidualPolicy_UsesExactProducerFacets()
    {
        Assert.Equal(
            FidelityResidualDisposition.RecoverableRoadmap,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnrepresentableMetadataName,
                DecompilerFidelityDiscriminators.DisplayClassTypeName)).Disposition);
        Assert.Equal(
            FidelityResidualDisposition.PolicyFloor,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnrepresentableMetadataName,
                DecompilerFidelityDiscriminators.UnspellableTypeName)).Disposition);
        Assert.Equal(
            FidelityResidualDisposition.Unclassified,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnrepresentableMetadataName,
                "new-name-shape")).Disposition);
        Assert.Equal(
            FidelityResidualDisposition.RecoverableRoadmap,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnsupportedConstruct,
                "iterator")).Disposition);
        Assert.Equal(
            FidelityResidualDisposition.Unclassified,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnsupportedConstruct,
                "calli")).Disposition);
        Assert.Equal(
            FidelityResidualDisposition.Unclassified,
            FidelityResidualPolicy.Classify(new CorpusFidelityCauseSnapshot(
                DiagnosticIds.UnsupportedExceptionFilter,
                null)).Disposition);
    }

    [Fact]
    public void FidelityResidualPortfolio_SeparatesSitesMethodsAndStructuralPrimary()
    {
        var displayClass = new CorpusFidelityCauseSnapshot(
            DiagnosticIds.UnrepresentableMetadataName,
            DecompilerFidelityDiscriminators.DisplayClassTypeName,
            Sites: 2);
        var unspellable = new CorpusFidelityCauseSnapshot(
            DiagnosticIds.UnrepresentableMetadataName,
            DecompilerFidelityDiscriminators.UnspellableTypeName);
        var unknownType = new CorpusFidelityCauseSnapshot(
            DiagnosticIds.UnknownResultType,
            null);
        var unsupportedFunctionPointer = new CorpusFidelityCauseSnapshot(
            DiagnosticIds.UnsupportedFunctionPointer,
            null);

        CorpusMethodSnapshot[] methods =
        [
            SnapshotMethod("Full"),
            ResidualMethod(
                "Recoverable",
                "fidelity: DEC0009",
                displayClass,
                unknownType),
            ResidualMethod("Floor", "fidelity: DEC0009", displayClass, unspellable),
            ResidualMethod("Unknown", "fidelity: DEC0006", unsupportedFunctionPointer),
            ResidualMethod("Missing", "fidelity: DEC0009"),
            ResidualMethod("Structural", "structuring: conditional-branch", displayClass),
            ResidualMethod("EhStructural", "eh: leave/endfinally", displayClass),
        ];

        var portfolio = FidelityResidualPortfolioBuilder.Build(
            methods,
            totalMethods: methods.Length,
            fullyRaisedMethods: 1);

        Assert.Equal(4, portfolio.FidelityPrimaryMethods);
        Assert.Equal(7, portfolio.FidelityCauseSites);
        Assert.Equal(1, portfolio.RecoverableMethods);
        Assert.Equal(1, portfolio.PolicyFloorMethods);
        Assert.Equal(2, portfolio.UnclassifiedMethods);
        Assert.Equal(1, portfolio.MissingCauseMethods);
        Assert.Equal(2, portfolio.StructuralPrimaryMethodsWithFidelityCauses);
        Assert.Equal(4, portfolio.StructuralPrimaryFidelityCauseSites);
        Assert.Equal(2, portfolio.RoadmapTargetLowerMethods);
        Assert.Equal(4, portfolio.RoadmapTargetUpperMethods);

        var facet = Assert.Single(
            portfolio.Facets,
            summary => summary.Discriminator
                == DecompilerFidelityDiscriminators.DisplayClassTypeName);
        Assert.Equal(4, facet.CauseSites);
        Assert.Equal(2, facet.Methods);
        Assert.Equal(
            ["nuget:pinned/lib.dll!T::Floor()", "nuget:pinned/lib.dll!T::Recoverable()"],
            facet.Examples);
    }

    [Fact]
    public void CorpusMethodSnapshot_PreV4JsonLeavesFidelityCausesAbsent()
    {
        const string json =
            """
            {
              "assembly": "Pinned",
              "assemblyPath": "pinned.dll",
              "type": "T",
              "method": "M",
              "overload": 0,
              "signature": "()",
              "fidelity": "Partial",
              "fullyRaised": false,
              "residual": "fidelity: DEC0009",
              "passBug": null,
              "validity": "not-sampled",
              "fidelityCheck": "not-sampled"
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var method = JsonSerializer.Deserialize<CorpusMethodSnapshot>(json, options);

        Assert.NotNull(method);
        Assert.Null(method.FidelityCauses);
        Assert.DoesNotContain(
            "stableKey",
            JsonSerializer.Serialize(method, options),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CorpusFidelityCauseSnapshot_RejectsInvalidSiteMultiplicity()
    {
        var cause = new CorpusFidelityCauseSnapshot(
            DiagnosticIds.UnrepresentableMetadataName,
            DecompilerFidelityDiscriminators.DisplayClassTypeName,
            Sites: 0);

        Assert.Throws<InvalidDataException>(() => cause.SiteCount);
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
    public void ValidateRtsParityBurndownFlags_RejectsNonReturnToSenderOracle()
    {
        var error = CorpusSensor.ValidateRtsParityBurndownFlags(
            CorpusFidelityOracle.CompileBack, [3], "burndown.json", null);

        Assert.NotNull(error);
        Assert.Contains("rts-parity", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRtsParityBurndownFlags_RejectsMissingPositiveFidelityCap()
    {
        var error = CorpusSensor.ValidateRtsParityBurndownFlags(
            CorpusFidelityOracle.ReturnToSender, [0], "burndown.json", null);

        Assert.NotNull(error);
        Assert.Contains("--corpus-fidelity-cap", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRtsParityBurndownFlags_RejectsEmitAndEnforceSamePath()
    {
        var error = CorpusSensor.ValidateRtsParityBurndownFlags(
            CorpusFidelityOracle.ReturnToSender, [3], "burndown.json", "burndown.json");

        Assert.NotNull(error);
        Assert.Contains("same file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRtsParityBurndownFlags_AllowsWellFormedGateRun()
    {
        Assert.Null(CorpusSensor.ValidateRtsParityBurndownFlags(
            CorpusFidelityOracle.ReturnToSender, [3], "burndown.json", null));
        Assert.Null(CorpusSensor.ValidateRtsParityBurndownFlags(
            CorpusFidelityOracle.CompileBack, [0], null, null));
    }

    [Fact]
    public void ReadRtsParityBurndown_ManifestWithoutRowsArray_ReadsAsEmptyWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rts-burndown-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        try
        {
            var manifest = CorpusSensor.ReadRtsParityBurndown(path);
            Assert.False(manifest.Rows.IsDefault);
            Assert.Empty(manifest.Rows);
        }
        finally
        {
            File.Delete(path);
        }
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
    public void ClassicStateMachineCoverage_CountsKickoffResultsByFamily()
    {
        var coverage = CorpusSensor.BuildClassicStateMachineCoverage(
        [
            ClassicKickoff("Async_Value", fullyRaised: true),
            ClassicKickoff("Iterator_Sequence", fullyRaised: false),
            ClassicKickoff("AsyncIterator_Sequence", fullyRaised: false),
            ClassicKickoff("MoveNext", fullyRaised: true, type: "T.<Async_Value>d__1"),
            ClassicKickoff("Switch_Control", fullyRaised: true),
        ]);

        Assert.Equal(new ClassicStateMachineFeatureMetrics(1, 1, 0), coverage["classic-async"]);
        Assert.Equal(new ClassicStateMachineFeatureMetrics(1, 0, 1), coverage["classic-iterator"]);
        Assert.Equal(new ClassicStateMachineFeatureMetrics(1, 0, 1), coverage["classic-async-iterator"]);
        Assert.Equal(3, coverage.Count);
    }

    [Fact]
    public void Compare_RejectsClassicStateMachineRaisedRegression()
    {
        var baselineCoverage = CompleteClassicStateMachineCoverage()
            .SetItem("classic-async", new ClassicStateMachineFeatureMetrics(4, 4, 0));
        var currentCoverage = CompleteClassicStateMachineCoverage()
            .SetItem("classic-async", new ClassicStateMachineFeatureMetrics(4, 3, 1));
        var baseline = Snapshot(
            4, 4, 10_000, null,
            profile: CorpusProfile.ClassicStateMachines,
            featureCoverage: CompleteClassicFeatureCoverage(),
            classicStateMachineCoverage: baselineCoverage);
        var current = Snapshot(
            4, 3, 7_500, null,
            profile: CorpusProfile.ClassicStateMachines,
            featureCoverage: CompleteClassicFeatureCoverage(),
            classicStateMachineCoverage: currentCoverage);

        var regressions = CorpusSensor.Compare(baseline, current, []);

        Assert.Contains(
            "classic state-machine fully raised 'classic-async' dropped (baseline 4, current 3)",
            regressions);
        Assert.Contains(
            "classic state-machine residual 'classic-async' increased (baseline 0, current 1)",
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
    public void RuntimeAsyncAwaitForeach_PreservesLoopAndExceptionEvidence()
    {
        using var source =
            MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.AwaitForeach));
        Assert.NotNull(function);
        IrPasses.Run(function);
        Assert.Contains(
            function.Descendants.OfType<ForeachStatement>(),
            statement => statement.IsAwait);

        var coverage =
            CorpusSensor.RecordMethodFeatureCoverageForTesting(function);

        Assert.Equal(1, coverage["runtime-async-loop-methods"]);
        Assert.Equal(1, coverage["runtime-async-exception-methods"]);
    }

    [Fact]
    public void RuntimeAsyncAwaitUsing_PreservesExceptionEvidence()
    {
        using var source =
            MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.NestedAwaitUsingResources));
        Assert.NotNull(function);
        IrPasses.Run(function);

        Assert.Contains(
            function.Descendants.OfType<UsingStatement>(),
            statement => statement.IsAwait);
        Assert.DoesNotContain(
            function.Descendants,
            node => node is TryCatch or TryFinally);

        var coverage =
            CorpusSensor.RecordMethodFeatureCoverageForTesting(function);

        Assert.Equal(1, coverage["runtime-async-await-using-methods"]);
        Assert.Equal(1, coverage["runtime-async-exception-methods"]);
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

        Assert.Contains(regressions, regression => regression.StartsWith("detected lowering residue rate increased", StringComparison.Ordinal));
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

        Assert.Contains(regressions, regression => regression.StartsWith("detected lowering residue rate (pinned) increased", StringComparison.Ordinal));
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
    public void Compare_GatesOperandDiffsWhenPinnedSamplesMatch()
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
            pinnedMethods: FidelityMethods(("One", "OperandDiff")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityOperandDiffMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(
            regressions,
            regression => regression.StartsWith("fidelity operand diffs (pinned)", StringComparison.Ordinal));
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
    public void Compare_RejectsFidelityContractMismatch()
    {
        var baseline = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1,
            fidelityContractVersion: 0);
        var current = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: FidelityMethods(("One", "Exact")),
            fidelityCompileCap: 1,
            fidelityCheckedMethods: 1,
            fidelityExactMethods: 1);

        var regressions = CorpusSensor.Compare(baseline, current, [], gateAggregateRates: false);

        Assert.Contains(
            regressions,
            regression => regression == "fidelity contract differs (baseline v0, current v1)");
        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);
        Assert.Contains("Fidelity exact (contract differs)", report);
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
    public void AlignReturnToSenderResults_PreservesFidelityContractEvidence()
    {
        var target = CompileBackResult("Method", FidelityCheck.CompileBackStatus.Exact);
        var fidelityDiff = new IlBodyDiffResult(IsExact: true, Failure: null, Rows: []);
        var rts = new ReturnToSender.Result(
            MinimalReturnToSenderPlan("Method"),
            Source: "",
            FidelityCheck.CompileBackStatus.Exact,
            OriginalOpcodes: "ldc.i4 ret",
            RecompiledOpcodes: "ldc.i4 ret",
            Detail: null,
            FidelityDiff: fidelityDiff);

        var aligned = Assert.Single(
            CorpusSensor.AlignReturnToSenderResultsForTesting([target], [rts]));

        Assert.Same(fidelityDiff, aligned.FidelityDiff);
    }

    [Fact]
    public void FidelityContractV1_ComposesAllIlBodyNormalizations()
    {
        Assert.Equal(
            IlBodyDiffOptions.NormalizeVariableLayout
            | IlBodyDiffOptions.NormalizeCurrentAssemblyScope
            | IlBodyDiffOptions.NormalizePlatformAssemblyScopes,
            FidelityCheck.ContractV1BodyDiffOptions);
    }

    [Fact]
    public void ClassifyStatus_RequiresV1BodyEqualityForExact()
    {
        var exact = new IlBodyDiffResult(IsExact: true, Failure: null, Rows: []);
        var divergent = new IlBodyDiffResult(
            IsExact: false,
            Failure: null,
            Rows:
            [
                new IlDiffRow(
                    0,
                    IlDiffKind.Remove,
                    new CanonicalIlOperation(0, "ldc.i4", new IlOperandIdentity(IlOperandIdentityKind.Immediate, "5")),
                    "Removed IL operation 'ldc.i4 5'"),
            ]);
        var unavailable = new IlBodyDiffResult(
            IsExact: false,
            Failure: "body decode failed",
            Rows: []);

        Assert.Equal(
            FidelityCheck.CompileBackStatus.Exact,
            FidelityCheck.ClassifyStatus(isFull: true, opcodesExact: true, fidelityDiff: exact));
        Assert.Equal(
            FidelityCheck.CompileBackStatus.OperandDiff,
            FidelityCheck.ClassifyStatus(isFull: true, opcodesExact: true, fidelityDiff: divergent));
        Assert.Equal(
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            FidelityCheck.ClassifyStatus(isFull: true, opcodesExact: false, fidelityDiff: divergent));
        Assert.Equal(
            FidelityCheck.CompileBackStatus.NotFull,
            FidelityCheck.ClassifyStatus(isFull: false, opcodesExact: true, fidelityDiff: divergent));
        Assert.Equal(
            FidelityCheck.CompileBackStatus.FidelityUnavailable,
            FidelityCheck.ClassifyStatus(isFull: true, opcodesExact: true, fidelityDiff: null));
        Assert.Equal(
            FidelityCheck.CompileBackStatus.FidelityUnavailable,
            FidelityCheck.ClassifyStatus(isFull: true, opcodesExact: true, fidelityDiff: unavailable));
    }

    [Fact]
    public void CorpusSchemaV4_DeserializesAsUnversionedFidelityContract()
    {
        var v4 = Snapshot(
            totalMethods: 1,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null) with
        {
            SchemaVersion = 4,
        };

        string json = JsonSerializer.Serialize(v4)
            .Replace("\"ContractVersion\":1,", "", StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<CorpusSensorSnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(4, restored.SchemaVersion);
        Assert.Equal(0, restored.Metrics.Fidelity.ContractVersion);
        Assert.Equal(5, CorpusSensor.CurrentSchemaVersion);
        Assert.Equal(1, CorpusSensor.CurrentFidelityContractVersion);
    }

    [Fact]
    public void SummarizeReturnToSenderParity_ClassifiesRescuedSameAndWorse()
    {
        var exact = CompileBackResult("Exact", FidelityCheck.CompileBackStatus.Exact);
        var rescued = CompileBackResult("Rescued", FidelityCheck.CompileBackStatus.OpcodeDiff);
        var unavailable = CompileBackResult("Unavailable", FidelityCheck.CompileBackStatus.FidelityUnavailable);
        var worse = CompileBackResult("Worse", FidelityCheck.CompileBackStatus.Exact);

        var parity = CorpusSensor.SummarizeReturnToSenderParityForTesting(
            [exact, rescued, unavailable, worse],
            [
                exact,
                rescued with { Status = FidelityCheck.CompileBackStatus.Exact },
                unavailable with { Status = FidelityCheck.CompileBackStatus.Exact },
                worse with { Status = FidelityCheck.CompileBackStatus.OpcodeDiff },
            ]);

        Assert.Equal(2, parity.RescuedMethods);
        Assert.Equal(1, parity.SameMethods);
        Assert.Equal(1, parity.WorseMethods);
        Assert.Equal(4, parity.ComparedMethods);
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
        Assert.Contains("Fidelity operand diffs (sampling differs)", report);
        Assert.Contains("Fidelity unavailable comparisons (sampling differs)", report);
        Assert.Contains("Fidelity exact (sampling differs)", report);
        Assert.DoesNotContain("Fidelity opcode diffs (-)", report);
        Assert.DoesNotContain("(bad)", report);
    }

    [Fact]
    public void CurrentMeasuredDebt_ListsEveryNonZeroFailureClass()
    {
        var snapshot = Snapshot(
            totalMethods: 93,
            fullyRaisedMethods: 87,
            fullyRaisedBasisPoints: 9355,
            pinnedMethods: null,
            validityCompileCap: 25,
            fullMalformedMethods: 1,
            semanticCheckedMethods: 64,
            semanticDefectMethods: 2,
            fidelityCompileCap: 25,
            fidelityCheckedMethods: 64,
            fidelityExactMethods: 45,
            fidelityOpcodeDiffMethods: 10,
            fidelityOperandDiffMethods: 2,
            fidelityUnavailableMethods: 1,
            fidelityRecompileFailMethods: 5,
            fidelityContextFailMethods: 4,
            passBugs: 1);

        string summary = CorpusSensor.CurrentMeasuredDebtForTesting(snapshot);

        Assert.Equal(
            "6 methods with detected lowering residue; 1 malformed Full method; "
            + "2 semantic defects among 64 checked; "
            + "10 fidelity opcode diffs among 64 checked; "
            + "2 fidelity operand diffs among 64 checked; "
            + "1 unavailable fidelity comparison among 64 checked; "
            + "5 fidelity recompile failures among 64 checked; "
            + "4 fidelity context failures among 64 checked; 1 pass bug.",
            summary);
    }

    [Fact]
    public void QualityMetricChanges_SeparatesStructuralAndVerifiedRaises()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods:
            [
                ValidityMethod("One", "valid"),
                ValidityMethod("Two", "semantic-defect:CS0266"),
            ],
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            semanticDefectMethods: 1);
        var current = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods:
            [
                ValidityMethod("One", "valid"),
                ValidityMethod("Two", "valid"),
            ],
            validityCompileCap: 2,
            semanticCheckedMethods: 2);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);

        Assert.Contains("Fully raised (+)", report);
        Assert.Contains("1 (50.00%) → 2 (100.00%) (good)", report);
        Assert.Contains("Detected lowering residue (-)", report);
        Assert.Contains("0 (0.00%) → 0 (0.00%) (neutral)", report);
        Assert.True(
            report.IndexOf("| Fully raised", StringComparison.Ordinal)
            > report.IndexOf("| Detected lowering residue", StringComparison.Ordinal));
        Assert.True(
            report.IndexOf("| Fully raised", StringComparison.Ordinal)
            > report.IndexOf("| Pass bugs", StringComparison.Ordinal));
        Assert.Equal((2, 2), CorpusSensor.VerifiedFullyRaisedForTesting(current));
    }

    [Fact]
    public void VerifiedFullyRaised_UsesCompletedOutcomes()
    {
        var snapshot = Snapshot(
            totalMethods: 3,
            fullyRaisedMethods: 3,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods:
            [
                ValidityMethod("Valid", "valid"),
                ValidityMethod("SyntaxOnly", "syntax-valid"),
                ValidityMethod("Malformed", "full-malformed:CS1002"),
            ],
            validityCompileCap: 1,
            semanticCheckedMethods: 1);

        Assert.Equal((1, 2), CorpusSensor.VerifiedFullyRaisedForTesting(snapshot));
    }

    [Fact]
    public void QualityMetricChanges_TreatsStructuralPopulationDriftAsContext()
    {
        var baseline = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 1,
            fullyRaisedBasisPoints: 5_000,
            pinnedMethods: ValidityMethods(("One", "not-sampled"), ("Two", "not-sampled")));
        var current = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: ValidityMethods(("Two", "not-sampled"), ("Three", "not-sampled")));

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);
        string residue = report.Split('\n').Single(
            line => line.Contains("Detected lowering residue", StringComparison.Ordinal));

        Assert.Contains("Detected lowering residue (population differs)", residue);
        Assert.DoesNotContain("(good)", residue);
        Assert.EndsWith("| n/a |", residue.TrimEnd());
    }

    [Fact]
    public void QualityMetricChanges_ReportsDetectedResidueCount()
    {
        var methods = ValidityMethods(("One", "not-sampled"));
        var baseline = Snapshot(
            totalMethods: 93,
            fullyRaisedMethods: 87,
            fullyRaisedBasisPoints: 9_355,
            pinnedMethods: methods);
        var current = Snapshot(
            totalMethods: 93,
            fullyRaisedMethods: 87,
            fullyRaisedBasisPoints: 9_355,
            pinnedMethods: methods);

        string report = CorpusSensor.QualityMetricChangesForTesting(baseline, current);

        Assert.Contains("Detected lowering residue (-)", report);
        Assert.Contains("6 (6.45%) → 6 (6.45%) (neutral)", report);
    }

    [Fact]
    public void CurrentMeasuredDebt_ReportsNoneForCleanEnabledChecks()
    {
        var snapshot = Snapshot(
            totalMethods: 2,
            fullyRaisedMethods: 2,
            fullyRaisedBasisPoints: 10_000,
            pinnedMethods: null,
            validityCompileCap: 2,
            semanticCheckedMethods: 2,
            fidelityCompileCap: 2,
            fidelityCheckedMethods: 2,
            fidelityExactMethods: 2);

        Assert.Equal(
            "none in enabled checks.",
            CorpusSensor.CurrentMeasuredDebtForTesting(snapshot));
        Assert.Equal(
            "Regression verdict: PASS — corpus sensor matched baseline tolerances.",
            CorpusSensor.RegressionVerdictForTesting(regressionCount: 0));
        Assert.Equal(
            "Regression verdict: FAIL — corpus sensor reported regressions; review before merging.",
            CorpusSensor.RegressionVerdictForTesting(regressionCount: 1));
    }

    static CorpusSensorSnapshot Snapshot(
        int totalMethods,
        int fullyRaisedMethods,
        int fullyRaisedBasisPoints,
        IReadOnlyList<CorpusMethodSnapshot>? pinnedMethods,
        int validityCompileCap = 0,
        int fullMalformedMethods = 0,
        int semanticCheckedMethods = 0,
        int semanticDefectMethods = 0,
        int fidelityCompileCap = 0,
        int fidelityCheckedMethods = 0,
        int fidelityExactMethods = 0,
        int fidelityOpcodeDiffMethods = 0,
        int fidelityOperandDiffMethods = 0,
        int fidelityUnavailableMethods = 0,
        int fidelityRecompileFailMethods = 0,
        int fidelityContextFailMethods = 0,
        int fidelityContractVersion = CorpusSensor.CurrentFidelityContractVersion,
        int passBugs = 0,
        CorpusFidelityOracle fidelityOracle = CorpusFidelityOracle.CompileBack,
        ReturnToSenderParityMetrics? returnToSenderParity = null,
        CorpusProfile profile = CorpusProfile.RealWorld,
        IReadOnlyDictionary<string, int>? featureCoverage = null,
        IReadOnlyDictionary<string, ClassicStateMachineFeatureMetrics>? classicStateMachineCoverage = null)
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
                FullMalformedMethods: fullMalformedMethods,
                SemanticCheckedMethods: semanticCheckedMethods,
                SemanticDefectMethods: semanticDefectMethods,
                PassBugs: passBugs,
                ResidualBuckets: ImmutableDictionary<string, int>.Empty,
                Structuring: new StructuringSensorMetrics(0, 0, 0, 0, 0, ImmutableDictionary<string, int>.Empty),
                Fidelity: new FidelitySensorMetrics(
                    ContractVersion: fidelityContractVersion,
                    CheckedMethods: fidelityCheckedMethods,
                    ExactMethods: fidelityExactMethods,
                    OpcodeDiffMethods: fidelityOpcodeDiffMethods,
                    OperandDiffMethods: fidelityOperandDiffMethods,
                    FidelityUnavailableMethods: fidelityUnavailableMethods,
                    RecompileFailMethods: fidelityRecompileFailMethods,
                    ContextFailMethods: fidelityContextFailMethods,
                    NotFullMethods: 0,
                    ReturnToSenderParity: returnToSenderParity)),
            FidelityOracle: fidelityOracle,
            Profile: profile,
            FeatureCoverage: featureCoverage,
            ClassicStateMachineCoverage: classicStateMachineCoverage);
    }

    static ImmutableDictionary<string, int> CompleteClassicFeatureCoverage()
        => new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["classic-async-methods"] = 1,
            ["classic-iterator-methods"] = 1,
            ["classic-async-iterator-methods"] = 1,
            ["switch-methods"] = 1,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    static ImmutableDictionary<string, ClassicStateMachineFeatureMetrics> CompleteClassicStateMachineCoverage()
        => new Dictionary<string, ClassicStateMachineFeatureMetrics>(StringComparer.Ordinal)
        {
            ["classic-async"] = new(1, 1, 0),
            ["classic-iterator"] = new(1, 1, 0),
            ["classic-async-iterator"] = new(1, 1, 0),
        }.ToImmutableDictionary(StringComparer.Ordinal);

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

    static CompileBackReconstructionPlan MinimalReturnToSenderPlan(string method)
        => new(
            AssemblyPath: "",
            TargetMethod: new CompileBackMethodIdentity(
                Type: "Fixture",
                Method: method,
                Overload: 0,
                Signature: "() -> corelib:System.Int32"),
            Module: new CompileBackModuleRequirement([], [], []),
            Types: [],
            PrintRequests: [],
            Diagnostics: []);

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

    static CorpusMethodSnapshot ValidityMethod(string method, string validity)
        => new(
            Assembly: "Fixture",
            AssemblyPath: "fixture.dll",
            Type: "T",
            Method: method,
            Overload: 0,
            Signature: "()",
            Fidelity: "Full",
            FullyRaised: true,
            Residual: null,
            PassBug: null,
            Validity: validity,
            FidelityCheck: "not-sampled");

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

    static CorpusMethodSnapshot ClassicKickoff(
        string method,
        bool fullyRaised,
        string type = "ClassicStateMachineFixtures")
        => new(
            Assembly: "Fixture",
            AssemblyPath: "fixture.dll",
            Type: type,
            Method: method,
            Overload: 0,
            Signature: "()",
            Fidelity: fullyRaised ? "Full" : "Partial",
            FullyRaised: fullyRaised,
            Residual: fullyRaised ? null : "fidelity: unsupported-node",
            PassBug: null,
            Validity: "not-sampled",
            FidelityCheck: "not-sampled");

    static CorpusMethodSnapshot ResidualMethod(
        string method,
        string residual,
        params CorpusFidelityCauseSnapshot[] causes)
        => new(
            Assembly: "Pinned",
            AssemblyPath: "nuget:pinned/lib.dll",
            Type: "T",
            Method: method,
            Overload: 0,
            Signature: "()",
            Fidelity: "Partial",
            FullyRaised: false,
            Residual: residual,
            PassBug: null,
            Validity: "not-sampled",
            FidelityCheck: "not-sampled",
            FidelityCauses: causes.Length == 0 ? null : causes);

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
