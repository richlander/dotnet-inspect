using System.Collections.Immutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public sealed class LibraryStructuralReportTests
{
    [Fact]
    public void LibraryStructuralReport_RejectsScopedProfilePopulation()
    {
        var method = FakeMethod("Widget", "M", 0x06000001);
        var profiles = ImmutableArray.Create(FakeProfile(method, method));
        LibraryImplementationProfileAnalysisResult analysis = FakeAnalysis(
            profiles,
            fullScope: false);

        LibraryStructuralReportResult result =
            LibraryStructuralReport.Execute(analysis);

        var unavailable =
            Assert.IsType<LibraryStructuralReportResult.Unavailable>(result);
        Assert.Equal(
            LibraryStructuralReportUnavailableReason.NonWholeLibraryScope,
            unavailable.Reason);
        Assert.Same(analysis.Coverage, unavailable.Coverage);
    }

    [Fact]
    public void LibraryStructuralReport_PreservesIssuedBodyCoverage()
    {
        var method = FakeMethod("Widget", "M", 0x06000001);
        var failedDiagnostic = new AnalysisDiagnostic(
            0x06000002,
            "Widget.Failing()",
            "InvalidOperationException: could not decode body");
        var unavailable = new ImplementationProfileUnavailableBody(
            EvidenceMethod: null,
            MethodToken: failedDiagnostic.MethodToken,
            ImplementationProfileUnavailableReason.AnalysisFailed,
            failedDiagnostic);
        var profiles = ImmutableArray.Create(FakeProfile(method, method));
        LibraryImplementationProfileAnalysisResult analysis = FakeAnalysis(
            profiles,
            diagnostics: [failedDiagnostic],
            unavailableBodies: [unavailable]);

        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(analysis));

        Assert.Same(analysis.Receipt, available.Document.AnalysisReceipt);
        Assert.Same(analysis.Coverage, available.Document.Population.Coverage);
        Assert.Equal(
            analysis.Coverage.ManagedMethodBodyCount,
            available.Document.Population.PhysicalEvidenceBodyCount);
        Assert.Equal(
            analysis.Coverage.ProfiledEvidenceBodyCount,
            available.Document.Population.ProfiledPhysicalEvidenceBodyCount);
        Assert.Equal(
            analysis.Receipt.Diagnostics,
            available.Document.Diagnostics);
        LibraryStructuralReasonCount reason = Assert.Single(
            available.Document.Population.UnavailableReasons);
        Assert.Equal(nameof(ImplementationProfileUnavailableReason.AnalysisFailed), reason.Reason);
        Assert.Equal(1, reason.Count);
    }

    [Fact]
    public void LibraryStructuralReport_ExcludesIncompleteProfilesFromStatistics()
    {
        var complete = FakeMethod("Widget", "Complete", 0x06000001);
        var incomplete = FakeMethod("Widget", "Incomplete", 0x06000002);
        var profiles = ImmutableArray.Create(
            FakeProfile(complete, complete, conditionalBranchCount: 9),
            FakeProfile(
                incomplete,
                incomplete,
                conditionalBranchCount: 99,
                isComplete: false,
                incompleteReasons: ["unsupported local signature"]));

        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(FakeAnalysis(profiles)));

        LibraryStructuralMetricDistribution complexity =
            Distribution(
                available.Document,
                LibraryStructuralMetric.NormalFlowCyclomaticComplexity);
        Assert.Equal(1, complexity.CompleteBodyCount);
        Assert.Equal(10, complexity.Maximum);
        Assert.Equal(10, complexity.P99);
        Assert.Equal(1, available.Document.Population.CompleteProfileCount);
        Assert.Equal(1, available.Document.Population.IncompleteProfileCount);
        LibraryStructuralReasonCount reason = Assert.Single(
            available.Document.Population.IncompleteReasons);
        Assert.Equal("unsupported local signature", reason.Reason);
        Assert.Equal(1, reason.Count);
    }

    [Fact]
    public void LibraryStructuralReport_PreservesMultipleEvidenceBodiesPerLogicalOwner()
    {
        LibraryImplementationProfileAnalysisResult analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.ImplementationProfiles))
                .ImplementationProfiles;
        MethodImplementationProfile[] profiles =
        [
            .. analysis.Profiles.Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "AnalyzeAsync"
                    && profile.Method.ParameterTypes.Length == 1
                    && profile.Method.ParameterTypes[0].Name == "Int32"),
        ];
        Assert.Equal(2, profiles.Length);
        MethodImplementationProfile stateMachineBody = Assert.Single(
            profiles,
            profile => profile.Async);
        MethodImplementationProfile kickoffBody = Assert.Single(
            profiles,
            profile => !profile.Async);
        Assert.Equal(stateMachineBody.Method, kickoffBody.Method);
        Assert.NotEqual(
            stateMachineBody.EvidenceMethod,
            kickoffBody.EvidenceMethod);
        Assert.Equal("MoveNext", stateMachineBody.EvidenceMethod.Name);

        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(analysis));

        Assert.Equal(
            analysis.Coverage.ManagedMethodBodyCount,
            available.Document.Population.PhysicalEvidenceBodyCount);
        Assert.Equal(
            analysis.Coverage.ProfiledEvidenceBodyCount,
            available.Document.Population.ProfiledPhysicalEvidenceBodyCount);
        Assert.Equal(
            analysis.Profiles
                .Select(static profile => profile.Method.MetadataToken)
                .Distinct()
                .Count(),
            available.Document.Population.LogicalOwnerCount);
        Assert.True(
            available.Document.Population.PhysicalEvidenceBodyCount
                > available.Document.Population.LogicalOwnerCount);
        Assert.Equal(
            analysis.Profiles.Count(static profile =>
                profile.IsComplete && profile.Async),
            available.Document.AsyncStateMachinePresence.PresentCount);
        Assert.Equal(
            analysis.Profiles.Count(static profile =>
                profile.IsComplete && !profile.Async),
            available.Document.AsyncStateMachinePresence.AbsentCount);
        Assert.True(available.Document.AsyncStateMachinePresence.PresentCount > 0);
    }

    [Fact]
    public void LibraryStructuralReport_UsesDeterministicNearestRankAndMaximumTies()
    {
        var profiles = ImmutableArray.Create(
            FakeProfileValue("M1", 0x06000001, 1),
            FakeProfileValue("M2", 0x06000002, 2),
            FakeProfileValue("M3", 0x06000003, 3),
            FakeProfileValue("M4", 0x06000004, 4),
            FakeProfileValue("T5", 0x06000020, 6),
            FakeProfileValue("T4", 0x06000019, 6),
            FakeProfileValue("T3", 0x06000018, 6),
            FakeProfileValue("T2", 0x06000017, 6),
            FakeProfileValue("T1", 0x06000016, 6),
            FakeProfileValue("T0", 0x06000015, 6));

        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(FakeAnalysis(profiles)));

        LibraryStructuralMetricDistribution instructions =
            Distribution(available.Document, LibraryStructuralMetric.InstructionCount);
        Assert.Equal(10, instructions.CompleteBodyCount);
        Assert.Equal(1, instructions.Minimum);
        Assert.Equal(6, instructions.P50);
        Assert.Equal(6, instructions.P90);
        Assert.Equal(6, instructions.P95);
        Assert.Equal(6, instructions.P99);
        Assert.Equal(6, instructions.Maximum);
        Assert.Equal(1, instructions.AdditionalMaximumBodyCount);
        Assert.Equal(
            [0x06000015, 0x06000016, 0x06000017, 0x06000018, 0x06000019],
            instructions.MaximumBodies
                .Select(static body => body.EvidenceMethod.MetadataToken)
                .ToArray());
    }

    [Fact]
    public void LibraryStructuralReport_RejectsDuplicateOrUnaccountedEvidenceIdentity()
    {
        var logical = FakeMethod("Widget", "M", 0x06000001);
        var evidence = FakeMethod("Widget+<>c", "<M>b__0_0", 0x06000010);
        var unaccounted = FakeMethod("Widget+<>c", "<M>b__0_1", 0x06000011);
        var duplicateProfiles = ImmutableArray.Create(
            FakeProfile(logical, evidence),
            FakeProfile(logical, evidence, instructionCount: 2));

        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(
            () => LibraryStructuralReport.Execute(FakeAnalysis(duplicateProfiles)));
        Assert.Contains("duplicate", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var mismatchedProfiles = ImmutableArray.Create(
            FakeProfile(logical, evidence));
        LibraryImplementationProfileAnalysisResult mismatched = FakeAnalysis(
            mismatchedProfiles,
            profiledBodies: [unaccounted]);

        InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(
            () => LibraryStructuralReport.Execute(mismatched));
        Assert.Contains(
            "coverage receipt",
            mismatch.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    static LibraryStructuralMetricDistribution Distribution(
        LibraryStructuralReportDocument document,
        LibraryStructuralMetric metric) =>
        Assert.Single(document.Distributions, distribution => distribution.Metric == metric);

    static MethodImplementationProfile FakeProfileValue(
        string methodName,
        int token,
        int instructionCount)
    {
        MethodIdentity method = FakeMethod("Widget", methodName, token);
        return FakeProfile(method, method, instructionCount: instructionCount);
    }

    static LibraryImplementationProfileAnalysisResult FakeAnalysis(
        ImmutableArray<MethodImplementationProfile> profiles,
        bool fullScope = true,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default,
        ImmutableArray<ImplementationProfileUnavailableBody> unavailableBodies = default,
        ImmutableArray<MethodIdentity> profiledBodies = default)
    {
        diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        unavailableBodies = unavailableBodies.IsDefault ? [] : unavailableBodies;
        var receipt = new LibraryBodyAnalysisReceipt(
            "fake.dll",
            new LibraryBodyModuleIdentity(
                new AssemblyReferenceIdentity(
                    "Fake",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                Guid.Empty),
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.ImplementationProfiles,
            fullScope,
            diagnostics);
        ImmutableArray<MethodIdentity> declaredMethods =
        [
            .. profiles
                .Select(static profile => profile.Method)
                .DistinctBy(static method => method.MetadataToken),
        ];
        ImmutableArray<MethodIdentity> managedBodies =
        [
            .. profiles
                .Select(static profile => profile.EvidenceMethod)
                .DistinctBy(static method => method.MetadataToken),
        ];
        profiledBodies = profiledBodies.IsDefault
            ? managedBodies
            : profiledBodies;
        var coverage = new ImplementationProfilePopulationCoverageReceipt(
            WasRequested: true,
            fullScope,
            declaredMethods,
            managedBodies,
            profiledBodies,
            unavailableBodies,
            diagnostics);

        return new LibraryImplementationProfileAnalysisResult(
            receipt,
            coverage,
            profiles,
            [],
            []);
    }

    static MethodIdentity FakeMethod(
        string typeName,
        string methodName,
        int token) =>
        new(
            "Fake",
            Guid.Empty,
            TypeRef.Definition("Fake", "Sample", typeName),
            methodName,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: token,
            IsStatic: true);

    static MethodImplementationProfile FakeProfile(
        MethodIdentity logicalMethod,
        MethodIdentity evidenceMethod,
        int conditionalBranchCount = 0,
        int instructionCount = 1,
        int loopCount = 0,
        int catchCount = 0,
        int filterCount = 0,
        int finallyCount = 0,
        int faultCount = 0,
        int directCallCount = 0,
        int allocationCount = 0,
        bool async = false,
        bool isComplete = true,
        ImmutableArray<string> incompleteReasons = default) =>
        new(
            logicalMethod,
            evidenceMethod,
            ILBytes: 1,
            InstructionCount: instructionCount,
            DistinctOpcodeCount: 1,
            BasicBlockCount: 1,
            BranchCount: 0,
            ConditionalBranchCount: conditionalBranchCount,
            SwitchCount: 0,
            SwitchTargetCount: 0,
            LoopCount: loopCount,
            CatchCount: catchCount,
            FilterCount: filterCount,
            FinallyCount: finallyCount,
            FaultCount: faultCount,
            LocalCount: 0,
            DirectCallCount: directCallCount,
            DistinctCalleeCount: 0,
            AllocationCount: allocationCount,
            ThrowCount: 0,
            Async: async,
            Unsafe: false,
            ReflectionCallCount: 0,
            IncomingOverloadCallerCount: 0,
            OutgoingOverloadTargetCount: 0,
            IsComplete: isComplete,
            IncompleteReasons: incompleteReasons.IsDefault ? [] : incompleteReasons);
}
