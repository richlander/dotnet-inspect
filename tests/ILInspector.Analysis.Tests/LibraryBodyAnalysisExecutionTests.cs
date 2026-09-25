using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using DotnetInspector.Fixtures;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.ImplementationProfileFixtures;

namespace ILInspector.Analysis.Tests;

public sealed class LibraryBodyAnalysisExecutionTests
{
    [Fact]
    public void CompleteProfileRequest_CreatesVersionedMetricPlan()
    {
        LibraryBodyAnalysisRequest request =
            LibraryBodyAnalysisRequest
                .CreateCompleteImplementationProfile();

        Assert.Equal(
            LibraryBodyAnalysisFeatures.None,
            request.Features);
        ImplementationMetricAnalysisPlan plan =
            Assert.IsType<ImplementationMetricAnalysisPlan>(
                request.Plan.ImplementationMetrics);
        Assert.Equal(
            ImplementationMetricAnalysisRequest.CompleteProfileV1,
            plan.RequestedEvidence);
        Assert.Equal(
            plan.RequestedEvidence,
            plan.EffectiveEvidence);
        Assert.False(
            plan.EffectiveEvidence.HasFlag(
                ImplementationMetricEvidenceKind
                    .AllocationOccurrences));
        Assert.Equal(
            ImplementationMetricRequestOrigin
                .CompleteProfileCompatibility,
            plan.Origin);
        Assert.True(plan.Limits.IsLegacyUnbounded);
        Assert.True(
            request.Plan.Includes(
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles));
        Assert.True(
            request.Plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence));
    }

    [Fact]
    public void CompleteProfileRequest_PreservesLegacyProfileResult()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        LibraryImplementationProfileAnalysisResult legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles))
            .ImplementationProfiles;
        LibraryImplementationProfileAnalysisResult migrated =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile())
            .ImplementationProfiles;

        Assert.Equal(legacy.Profiles, migrated.Profiles);
        Assert.Equal(
            legacy.OverloadRelationships,
            migrated.OverloadRelationships);
        Assert.Equal(
            legacy.Coverage.WasRequested,
            migrated.Coverage.WasRequested);
        Assert.Equal(
            legacy.Coverage.HasFullMethodEvidenceScope,
            migrated.Coverage.HasFullMethodEvidenceScope);
        Assert.Equal(
            legacy.Coverage.DeclaredMethods,
            migrated.Coverage.DeclaredMethods);
        Assert.Equal(
            legacy.Coverage.ManagedMethodBodies,
            migrated.Coverage.ManagedMethodBodies);
        Assert.Equal(
            legacy.Coverage.ProfiledEvidenceBodies,
            migrated.Coverage.ProfiledEvidenceBodies);
        Assert.Equal(
            legacy.Coverage.UnavailableBodies,
            migrated.Coverage.UnavailableBodies);
        Assert.Equal(
            legacy.Coverage.Diagnostics,
            migrated.Coverage.Diagnostics);
        Assert.True(
            legacy.GeneratedFrameworkTypes.SetEquals(
                migrated.GeneratedFrameworkTypes));
    }

    [Fact]
    public void LegacyProfileFeature_NormalizesToMetricPlan()
    {
        LibraryBodyAnalysisRequest request =
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles);

        ImplementationMetricAnalysisPlan plan =
            Assert.IsType<ImplementationMetricAnalysisPlan>(
                request.Plan.ImplementationMetrics);
        Assert.Equal(
            ImplementationMetricAnalysisRequest.CompleteProfileV1,
            plan.EffectiveEvidence);
        Assert.Equal(
            ImplementationMetricRequestOrigin
                .LegacyFeatureCompatibility,
            plan.Origin);
        Assert.True(plan.Limits.IsLegacyUnbounded);
    }

    [Fact]
    public void MetricPlan_ClosesSiblingRelationshipsOverCalls()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: 10_000,
            maximumAttributionProbeBodies: 20,
            maximumAttributionProbeIlBytes: 20_000);
        var request = new ImplementationMetricAnalysisRequest(
            ImplementationMetricEvidenceKind.BodySize
                | ImplementationMetricEvidenceKind
                    .SiblingOverloadRelationships,
            limits,
            ImplementationMetricRequestOrigin.Explicit);

        ImplementationMetricAnalysisPlan plan =
            ImplementationMetricAnalysisPlan.Create(request);

        Assert.Equal(
            request.RequestedEvidence,
            plan.RequestedEvidence);
        Assert.True(
            plan.EffectiveEvidence.HasFlag(
                ImplementationMetricEvidenceKind.DirectCalls));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .CanonicalMethodContext));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .AllocationSignalCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .AllocationOccurrenceCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .BodySignalCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage.SafetyCollection));
        ImplementationMetricEvidenceKind contextCauses =
            plan.EvidenceCausesFor(
                ImplementationMetricWorkStage
                    .CanonicalMethodContext);
        Assert.False(
            contextCauses.HasFlag(
                ImplementationMetricEvidenceKind.BodySize));
        Assert.True(
            contextCauses.HasFlag(
                ImplementationMetricEvidenceKind.DirectCalls));
        Assert.False(
            contextCauses.HasFlag(
                ImplementationMetricEvidenceKind
                    .SiblingOverloadRelationships));
    }

    [Fact]
    public void MetricPlan_BodySizeAvoidsCanonicalContextWork()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: 10_000,
            maximumAttributionProbeBodies: 20,
            maximumAttributionProbeIlBytes: 20_000);
        var request = new ImplementationMetricAnalysisRequest(
            ImplementationMetricEvidenceKind.BodySize,
            limits,
            ImplementationMetricRequestOrigin.Explicit);

        ImplementationMetricAnalysisPlan plan =
            ImplementationMetricAnalysisPlan.Create(request);

        Assert.Equal(
            ImplementationMetricEvidenceKind.BodySize,
            plan.EffectiveEvidence);
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .ManagedBodyAcquisition));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .LocalSignatureDecode));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .CanonicalMethodContext));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
    }

    [Fact]
    public void MetricExecution_BodySizeStopsAfterBodyAcquisition()
    {
        string path =
            typeof(ImplementationProfileSample).Assembly.Location;
        int token = typeof(ImplementationProfileSample)
            .GetMethod(
                nameof(ImplementationProfileSample.Other),
                BindingFlags.Public | BindingFlags.Static)!
            .MetadataToken;

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        MetricLimits(),
                        new HashSet<int> { token }));

        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        Assert.False(
            execution.ImplementationProfiles.WasRequested);
        LibraryImplementationMetricAnalysisResult metrics =
            execution.ImplementationMetrics;
        Assert.True(metrics.WasRequested);
        MethodImplementationMetricEvidence body =
            Assert.Single(
                metrics.Bodies,
                body => body.EvidenceMethod.MetadataToken == token);
        Assert.True(body.ILBytes > 0);
        Assert.Null(body.ExceptionRegions);
        ImplementationMetricParticipationReceipt receipt =
            Assert.IsType<ImplementationMetricParticipationReceipt>(
                metrics.Participation);
        ImplementationMetricStageParticipation acquisition =
            Assert.Single(
                receipt.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .ManagedBodyAcquisition);
        Assert.Equal(
            ImplementationMetricEvidenceKind.BodySize,
            acquisition.EvidenceCauses);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.None,
            acquisition.FeatureCauses);
        Assert.Equal(1, acquisition.AttemptedBodies);
        Assert.Equal(1, acquisition.CompletedBodies);
        Assert.Equal(0, acquisition.FailedBodies);
        Assert.True(receipt.IsComplete);
        Assert.DoesNotContain(
            receipt.ActualStages,
            stage => stage.Stage
                is ImplementationMetricWorkStage
                    .LocalSignatureDecode
                    or ImplementationMetricWorkStage
                        .CanonicalMethodContext
                    or ImplementationMetricWorkStage
                        .DirectCallCollection
                    or ImplementationMetricWorkStage
                        .AllocationSignalCollection
                    or ImplementationMetricWorkStage
                        .AllocationOccurrenceCollection
                    or ImplementationMetricWorkStage
                        .BodySignalCollection
                    or ImplementationMetricWorkStage
                        .SafetyCollection
                    or ImplementationMetricWorkStage
                        .SiblingRelationshipProjection);
    }

    [Fact]
    public void MetricExecution_ExceptionRegionsStopsAfterBodyAcquisition()
    {
        string path =
            typeof(ImplementationProfileSample).Assembly.Location;
        int token = typeof(ImplementationProfileSample)
            .GetMethod(
                nameof(ImplementationProfileSample.Analyze),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(int), typeof(int)])!
            .MetadataToken;

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind
                            .ExceptionRegions,
                        MetricLimits(),
                        new HashSet<int> { token }));

        MethodImplementationMetricEvidence body =
            Assert.Single(
                execution.ImplementationMetrics.Bodies,
                body => body.EvidenceMethod.MetadataToken == token);
        Assert.Null(body.ILBytes);
        ImplementationMetricExceptionRegionCounts regions =
            Assert.IsType<
                ImplementationMetricExceptionRegionCounts>(
                body.ExceptionRegions);
        Assert.Equal(1, regions.CatchCount);
        Assert.Equal(0, regions.FilterCount);
        Assert.Equal(0, regions.FinallyCount);
        Assert.Equal(0, regions.FaultCount);
        ImplementationMetricParticipationReceipt receipt =
            Assert.IsType<ImplementationMetricParticipationReceipt>(
                execution.ImplementationMetrics.Participation);
        Assert.Single(
            receipt.ActualStages,
            stage => stage.Stage
                == ImplementationMetricWorkStage
                    .ManagedBodyAcquisition);
        Assert.DoesNotContain(
            receipt.ActualStages,
            stage => stage.Stage
                == ImplementationMetricWorkStage
                    .CanonicalMethodContext);
    }

    [Fact]
    public void MetricExecution_BodylessMethodDoesNotStartBodyStages()
    {
        string path =
            typeof(IImplementationProfileBodylessSample)
                .Assembly.Location;
        int token = typeof(IImplementationProfileBodylessSample)
            .GetMethod(
                nameof(IImplementationProfileBodylessSample.Route),
                [typeof(int)])!
            .MetadataToken;

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        MetricLimits(),
                        new HashSet<int> { token }));

        Assert.Contains(
            execution.ImplementationMetrics.DeclaredMethods,
            method => method.MetadataToken == token);
        Assert.Empty(execution.ImplementationMetrics.Bodies);
        Assert.DoesNotContain(
            execution.ImplementationMetrics
                .Participation!.ActualStages,
            stage => stage.Stage
                == ImplementationMetricWorkStage
                    .ManagedBodyAcquisition);
        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(0, work.MetricBodies);
        Assert.Equal(0, work.MetricIlBytes);
    }

    [Fact]
    public void
        MetricExecution_CoRunningFeatureOwnsCanonicalContextParticipation()
    {
        string path =
            typeof(ImplementationProfileSample).Assembly.Location;
        int token = typeof(ImplementationProfileSample)
            .GetMethod(
                nameof(ImplementationProfileSample.Other),
                BindingFlags.Public | BindingFlags.Static)!
            .MetadataToken;

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        MetricLimits(),
                        new HashSet<int> { token },
                        LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.Single(
            execution.ImplementationMetrics.Bodies,
            body => body.EvidenceMethod.MetadataToken == token);
        ImplementationMetricStageParticipation context =
            Assert.Single(
                execution.ImplementationMetrics
                    .Participation!.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .CanonicalMethodContext);
        Assert.Equal(
            ImplementationMetricEvidenceKind.None,
            context.EvidenceCauses);
        Assert.False(
            execution.ImplementationMetrics
                .Participation!.IsComplete);
        Assert.True(
            context.FeatureCauses.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
    }

    [Fact]
    public void
        MetricExecution_BoundsDoNotSuppressCoRunningMethodEvidence()
    {
        string path =
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location;
        int ownerToken = MethodToken(
            path,
            nameof(OptimizationOpportunityFixtures),
            nameof(OptimizationOpportunityFixtures
                .MultipleLiftedFunctions),
            MethodAttributes.Public);
        var scope = new HashSet<int> { ownerToken };
        LibraryBodyAnalysisExecution baseline =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence,
                    bodyScope: scope));
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 1);

        LibraryBodyAnalysisExecution combined =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        limits,
                        scope,
                        LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.Equal(
            baseline.CallGraph.Methods,
            combined.CallGraph.Methods);
        Assert.Equal(
            baseline.CallGraph.DirectCalls,
            combined.CallGraph.DirectCalls);
        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                combined.ImplementationMetricWork);
        Assert.Equal(0, work.AttributionProbeBodies);
        Assert.Equal(0, work.AttributionProbeIlBytes);
        Assert.Null(work.AttributionExhaustedLimit);
        Assert.Equal(
            ImplementationMetricWorkLimitKind.PhysicalBodies,
            work.MetricExhaustedLimit);
        Assert.Single(combined.ImplementationMetrics.Bodies);
        Assert.Contains(
            combined.ImplementationMetrics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "physical-body limit",
                StringComparison.Ordinal));
        ImplementationMetricStageParticipation context =
            Assert.Single(
                combined.ImplementationMetrics
                    .Participation!.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .CanonicalMethodContext);
        Assert.True(context.CompletedBodies > 1);
        Assert.Equal(0, context.FailedBodies);
    }

    [Fact]
    public void
        MetricExecution_UnresolvedNestedOwnerDoesNotPublishIntermediateOwner()
    {
        byte[] image = File.ReadAllBytes(
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location);
        const string unresolvedOwner =
            "<ABCDEFGHIJKLMNOPQRSTUVWX>b__0_0";
        ReplaceAscii(
            image,
            nameof(OptimizationOpportunityFixtures
                .GenericObjectEqualsLocalFunction),
            unresolvedOwner,
            expectedReplacements: 2);
        ImmutableArray<byte> immutableImage =
            ImmutableCollectionsMarshal.AsImmutableArray(
                image);
        LibraryBodyAnalysisExecution identities =
            LibraryBodyAnalysisService.ExecuteImage(
                "UnresolvedLiftedOwner.dll",
                immutableImage,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        MethodIdentity intermediate = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name == unresolvedOwner);
        MethodIdentity evidence = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name.StartsWith(
                $"<{unresolvedOwner}>g__EqualsCore|",
                StringComparison.Ordinal));

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "UnresolvedLiftedOwner.dll",
                immutableImage,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        MetricLimits(),
                        new HashSet<int>
                        {
                            evidence.MetadataToken,
                        }));

        MethodImplementationMetricEvidence body =
            Assert.Single(execution.ImplementationMetrics.Bodies);
        Assert.Equal(
            evidence.MetadataToken,
            body.Method.MetadataToken);
        Assert.NotEqual(
            intermediate.MetadataToken,
            body.Method.MetadataToken);
    }

    [Fact]
    public void MetricRequest_RejectsEmptyUnknownAndInvalidLimits()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 1,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 1);
        Assert.Throws<ArgumentException>(
            () => ImplementationMetricAnalysisPlan.Create(
                new(
                    ImplementationMetricEvidenceKind.None,
                    limits,
                    ImplementationMetricRequestOrigin.Explicit)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImplementationMetricAnalysisPlan.Create(
                new(
                    (ImplementationMetricEvidenceKind)(1 << 20),
                    limits,
                    ImplementationMetricRequestOrigin.Explicit)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 0,
                maximumEncodedIlBytes: 1,
                maximumAttributionProbeBodies: 1,
                maximumAttributionProbeIlBytes: 1));
        Assert.Throws<ArgumentException>(
            () => LibraryBodyAnalysisRequest
                .CreateImplementationMetrics(
                    ImplementationMetricEvidenceKind.BodySize,
                    limits,
                    new HashSet<int>()));
        Assert.False(
            new ImplementationMetricWorkLimits(
                int.MaxValue,
                long.MaxValue,
                int.MaxValue,
                long.MaxValue)
            .IsLegacyUnbounded);
        Assert.Throws<ArgumentException>(
            () => ImplementationMetricAnalysisPlan.Create(
                new(
                    ImplementationMetricEvidenceKind.BodySize,
                    ImplementationMetricWorkLimits
                        .LegacyUnbounded,
                    ImplementationMetricRequestOrigin.Explicit)));
    }

    [Fact]
    public void MetricWorkBounds_StopAtPhysicalBodyLimit()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        int[] tokens = ManagedMethodTokens(path)
            .Take(2)
            .ToArray();
        Assert.Equal(2, tokens.Length);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: int.MaxValue,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        tokens.ToHashSet()));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(1, work.MetricBodies);
        Assert.True(work.MetricIlBytes > 0);
        Assert.Equal(
            ImplementationMetricWorkLimitKind.PhysicalBodies,
            work.MetricExhaustedLimit);
        Assert.Equal(tokens[1], work.MetricExhaustedMethodToken);
        Assert.Contains(
            execution.ImplementationProfiles.Profiles,
            profile =>
                profile.EvidenceMethod.MetadataToken == tokens[0]);
        ImplementationProfileUnavailableBody unavailable =
            Assert.Single(
                execution.ImplementationProfiles.Coverage
                    .UnavailableBodies,
                body => body.MethodToken == tokens[1]);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            unavailable.Reason);
        Assert.Contains(
            "physical-body limit",
            unavailable.Diagnostic!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        MetricWorkBounds_ExhaustionStopsLaterHeaderBodyAcquisition()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        int[] tokens = ManagedMethodTokens(path)
            .Take(3)
            .ToArray();
        Assert.Equal(3, tokens.Length);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: int.MaxValue,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricEvidenceKind.BodySize,
                        limits,
                        tokens.ToHashSet()));

        Assert.Single(execution.ImplementationMetrics.Bodies);
        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(
            ImplementationMetricWorkLimitKind.PhysicalBodies,
            work.MetricExhaustedLimit);
        Assert.Equal(tokens[1], work.MetricExhaustedMethodToken);
        ImplementationMetricStageParticipation acquisition =
            Assert.Single(
                execution.ImplementationMetrics
                    .Participation!.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .ManagedBodyAcquisition);
        Assert.Equal(2, acquisition.AttemptedBodies);
        Assert.Equal(2, acquisition.CompletedBodies);
        Assert.Equal(0, acquisition.FailedBodies);
    }

    [Fact]
    public void MetricWorkBounds_DoNotChargeBodylessMethods()
    {
        string path =
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location;
        int bodylessToken = BodylessMethodToken(path);
        int managedToken = MethodToken(
            path,
            nameof(OptimizationOpportunityFixtures),
            nameof(OptimizationOpportunityFixtures
                .ReadWithoutCompatibleAsyncSibling),
            MethodAttributes.Public);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: int.MaxValue,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int>
                        {
                            bodylessToken,
                            managedToken,
                        }));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(1, work.MetricBodies);
        Assert.Null(work.MetricExhaustedLimit);
        Assert.Contains(
            execution.ImplementationProfiles.Profiles,
            profile =>
                profile.EvidenceMethod.MetadataToken
                    == managedToken);
    }

    [Fact]
    public void MetricWorkBounds_StopAtEncodedIlLimit()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        int token = ManagedMethodTokens(path)[0];
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 1,
            maximumAttributionProbeBodies: int.MaxValue,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int> { token }));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(0, work.MetricBodies);
        Assert.Equal(0, work.MetricIlBytes);
        Assert.Equal(
            ImplementationMetricWorkLimitKind.EncodedIlBytes,
            work.MetricExhaustedLimit);
        Assert.Equal(token, work.MetricExhaustedMethodToken);
        ImplementationProfileUnavailableBody unavailable =
            Assert.Single(
                execution.ImplementationProfiles.Coverage
                    .UnavailableBodies,
                body => body.MethodToken == token);
        Assert.Contains(
            "encoded-IL-byte limit",
            unavailable.Diagnostic!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MetricWorkBounds_StopAtAttributionProbeIlLimit()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        int token = MethodToken(
            path,
            nameof(ClassicAsyncSiblingFixture),
            nameof(ClassicAsyncSiblingFixture
                .ScopedAsyncLocalOwner),
            MethodAttributes.Assembly);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 100,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: 100,
            maximumAttributionProbeIlBytes: 1);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int> { token }));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(1, work.AttributionProbeBodies);
        Assert.Equal(0, work.AttributionProbeIlBytes);
        Assert.Equal(
            ImplementationMetricWorkLimitKind
                .AttributionProbeIlBytes,
            work.AttributionExhaustedLimit);
        Assert.NotNull(work.AttributionExhaustedMethodToken);
        Assert.Contains(
            execution.Receipt.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "attribution-probe encoded-IL-byte limit",
                StringComparison.Ordinal));
    }

    [Fact]
    public void
        MetricWorkBounds_RetainMappingsBeforeAttributionBodyLimit()
    {
        string path =
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location;
        int ownerToken = MethodToken(
            path,
            nameof(OptimizationOpportunityFixtures),
            nameof(OptimizationOpportunityFixtures
                .MultipleLiftedFunctions),
            MethodAttributes.Public);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution Execute() =>
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int> { ownerToken }));
        LibraryBodyAnalysisExecution execution = Execute();
        LibraryBodyAnalysisExecution repeated = Execute();

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        ImplementationMetricWorkBudgetSnapshot repeatedWork =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                repeated.ImplementationMetricWork);
        Assert.Equal(1, work.AttributionProbeBodies);
        Assert.True(work.AttributionProbeIlBytes > 0);
        Assert.Equal(
            ImplementationMetricWorkLimitKind
                .AttributionProbeBodies,
            work.AttributionExhaustedLimit);
        Assert.Equal(work, repeatedWork);
        Assert.Equal(
            execution.ImplementationProfiles.Profiles,
            repeated.ImplementationProfiles.Profiles);
        Assert.True(
            execution.ImplementationProfiles.Profiles.Count(
                profile =>
                    profile.Method.MetadataToken == ownerToken
                    && profile.EvidenceMethod.MetadataToken
                        != ownerToken)
            >= 2);
        Assert.Contains(
            execution.Receipt.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "attribution-probe body limit",
                StringComparison.Ordinal));
    }

    [Fact]
    public void
        MetricWorkBounds_PublishRetainedMappingAfterUnresolvedBody()
    {
        string path =
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location;
        int ownerToken = MethodToken(
            path,
            nameof(OptimizationOpportunityFixtures),
            nameof(OptimizationOpportunityFixtures
                .IndirectLiftedFunction),
            MethodAttributes.Public);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int> { ownerToken }));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(
            ImplementationMetricWorkLimitKind
                .AttributionProbeBodies,
            work.AttributionExhaustedLimit);
        Assert.Contains(
            execution.ImplementationProfiles.Profiles,
            profile =>
                profile.Method.MetadataToken == ownerToken
                && profile.EvidenceMethod.Name.Contains(
                    "g__Later|",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            execution.ImplementationProfiles.Profiles,
            profile =>
                profile.Method.MetadataToken == ownerToken
                && profile.EvidenceMethod.Name.Contains(
                    "g__Earlier|",
                    StringComparison.Ordinal));
        Assert.Single(
            execution.Receipt.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "attribution-probe body limit",
                StringComparison.Ordinal));
    }

    [Fact]
    public void
        MetricWorkBounds_DirectBodyUsesRetainedScopedMapping()
    {
        Type fixture = typeof(MixedGeneratedOverloadFixtures);
        string path = fixture.Assembly.Location;
        int ownerToken = fixture.GetMethods(
                BindingFlags.Public
                | BindingFlags.Static)
            .Single(method =>
                method.Name == "Handle"
                && method.GetParameters()[2].ParameterType
                    == typeof(string))
            .MetadataToken;
        int bodyToken = fixture.GetMethods(
                BindingFlags.NonPublic
                | BindingFlags.Static)
            .Single(method => method.Name.Contains(
                "g__AuthoredCore|",
                StringComparison.Ordinal))
            .MetadataToken;
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 100,
            maximumEncodedIlBytes: long.MaxValue,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: long.MaxValue);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricAnalysisRequest
                            .CompleteProfileV1,
                        limits,
                        new HashSet<int>
                        {
                            ownerToken,
                            bodyToken,
                        }));

        ImplementationMetricWorkBudgetSnapshot work =
            Assert.IsType<ImplementationMetricWorkBudgetSnapshot>(
                execution.ImplementationMetricWork);
        Assert.Equal(
            ImplementationMetricWorkLimitKind
                .AttributionProbeBodies,
            work.AttributionExhaustedLimit);
        Assert.Contains(
            execution.ImplementationProfiles.Profiles,
            profile =>
                profile.EvidenceMethod.MetadataToken == bodyToken
                && profile.Method.MetadataToken == ownerToken);
    }

    [Fact]
    public void ExecutePath_PublishesFocusedResultsWithOneReceipt()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        Assert.Same(
            execution.Receipt,
            execution.Safety.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Allocations.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.ImplementationProfiles.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Optimization.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.CallGraph.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Leverage.Receipt);
        Assert.False(
            execution.Safety.Evidence.IsDefault);
        Assert.True(
            execution.Safety.WasRequested);
        Assert.False(
            execution.Allocations.WasRequested);
        Assert.Empty(
            execution.Allocations.Occurrences);
        Assert.True(
            execution.ImplementationProfiles.WasRequested);
        Assert.True(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.Allocations));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities));
        Assert.False(
            execution.Optimization.WasRequested);
        Assert.True(
            execution.CallGraph
                .HasProjectedPhysicalDirectCalls);
        Assert.True(
            execution.CallGraph
                .HasProjectedMethodSignals);
        Assert.Empty(
            execution.Optimization.Opportunities);
        Assert.Empty(
            execution.Optimization
                .AllocationFanoutOpportunities);
        Assert.True(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.NotEmpty(
            execution.ImplementationProfiles.Profiles);
        Assert.NotEmpty(
            execution.ImplementationProfiles
                .OverloadRelationships);
        Assert.Same(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes,
            execution.Leverage.GeneratedFrameworkTypes);
        Assert.Same(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes,
            execution.Optimization.GeneratedFrameworkTypes);
        Assert.Same(
            execution.CallGraph.DeclaredMethodMap,
            execution.Optimization.DeclaredMethodMap);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedSafetyEvidence()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.None));

        Assert.False(
            execution.Safety.WasRequested);
        Assert.Empty(
            execution.Safety.Evidence);
        Assert.Empty(
            execution.Safety.Occurrences);
        Assert.False(
            execution.Allocations.WasRequested);
        Assert.Empty(
            execution.Allocations.Occurrences);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedProfiles()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.False(
            execution.ImplementationProfiles.WasRequested);
        Assert.Empty(
            execution.ImplementationProfiles.Profiles);
        Assert.Empty(
            execution.ImplementationProfiles
                .OverloadRelationships);
        Assert.Empty(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes);
        Assert.False(
            execution.ImplementationProfiles.Coverage.WasRequested);
        Assert.Equal(
            execution.Receipt.HasFullMethodEvidenceScope,
            execution.ImplementationProfiles
                .Coverage
                .HasFullMethodEvidenceScope);
        Assert.NotEmpty(
            execution.ImplementationProfiles
                .Coverage
                .ManagedMethodBodies);
        Assert.Empty(
            execution.ImplementationProfiles
                .Coverage
                .ProfiledEvidenceBodies);
        Assert.Empty(
            execution.ImplementationProfiles
                .Coverage
                .UnavailableBodies);
    }

    [Fact]
    public void ExecutePath_PublishesImplementationProfileCoverage()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        ImplementationProfilePopulationCoverageReceipt coverage =
            execution.ImplementationProfiles.Coverage;

        Assert.True(coverage.WasRequested);
        Assert.True(coverage.HasFullMethodEvidenceScope);
        Assert.Equal(
            execution.Receipt.Diagnostics,
            coverage.Diagnostics);
        Assert.Equal(
            execution.CallGraph.DeclaredMethods,
            coverage.DeclaredMethods);
        Assert.Equal(
            execution.CallGraph.Methods,
            coverage.ManagedMethodBodies);
        HashSet<int> profiledEvidenceTokens =
        [
            .. execution.ImplementationProfiles
                .Profiles
                .Select(static profile => profile.EvidenceMethod.MetadataToken),
        ];
        Assert.Equal(
            profiledEvidenceTokens.Count,
            coverage.ProfiledEvidenceBodyCount);
        Assert.All(
            coverage.ProfiledEvidenceBodies,
            method => Assert.Contains(
                method.MetadataToken,
                profiledEvidenceTokens));
        Assert.Equal(
            coverage.ManagedMethodBodyCount,
            coverage.ProfiledEvidenceBodyCount);
        Assert.Empty(coverage.UnavailableBodies);
    }

    [Fact]
    public void ExecutePath_ProfileCoverageRecordsScopedUnavailableBodies()
    {
        LibraryBodyAnalysisExecution full =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));
        int selectedBodyToken =
            full.ImplementationProfiles
                .Profiles[0]
                .EvidenceMethod
                .MetadataToken;

        LibraryBodyAnalysisExecution scoped =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope:
                    new HashSet<int> { selectedBodyToken }));

        ImplementationProfilePopulationCoverageReceipt coverage =
            scoped.ImplementationProfiles.Coverage;

        Assert.True(coverage.WasRequested);
        Assert.False(coverage.HasFullMethodEvidenceScope);
        Assert.Contains(
            coverage.ProfiledEvidenceBodies,
            method => method.MetadataToken == selectedBodyToken);
        Assert.True(
            coverage.ManagedMethodBodyCount
                > coverage.ProfiledEvidenceBodyCount);
        Assert.All(
            coverage.UnavailableBodies,
            body => Assert.Equal(
                ImplementationProfileUnavailableReason.ScopeExcluded,
                body.Reason));
    }

    [Fact]
    public void ExecuteImage_ProfileCoverageReportsScopedDiagnosticsAsAnalysisFailed()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ArrayPoolLeakFixtures).Assembly.Location);
        int methodToken =
            ReplaceMethodCodeSizeWithInvalidValue(image);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedProfileBody.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope: new HashSet<int> { methodToken }));

        ImplementationProfileUnavailableBody body =
            Assert.Single(
                execution.ImplementationProfiles
                    .Coverage
                    .UnavailableBodies,
                body => body.MethodToken == methodToken);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            body.Reason);
        Assert.NotNull(body.EvidenceMethod);
        Assert.NotNull(body.Diagnostic);
        Assert.Contains(
            nameof(BadImageFormatException),
            body.Diagnostic.Message);
    }

    [Fact]
    public void ExecuteImage_ProfileCoverageRetainsTokenOnlyIdentityFailures()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ArrayPoolLeakFixtures).Assembly.Location);
        int methodToken =
            ReplaceMethodSignatureWithInvalidValue(image);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedProfileSignature.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        ImplementationProfilePopulationCoverageReceipt coverage =
            execution.ImplementationProfiles.Coverage;
        ImplementationProfileUnavailableBody body =
            Assert.Single(
                coverage.UnavailableBodies,
                body => body.MethodToken == methodToken);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            body.Reason);
        Assert.Null(body.EvidenceMethod);
        Assert.NotNull(body.Diagnostic);
        Assert.Equal(
            coverage.ManagedMethodBodies.Length + 1,
            coverage.ManagedMethodBodyCount);
    }

    [Fact]
    public void CompatibilityIndex_PreservesFocusedProfileResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.Equal(
            execution.ImplementationProfiles.Profiles,
            index.ImplementationProfiles());
        Assert.Equal(
            execution.ImplementationProfiles
                .OverloadRelationships,
            index.OverloadRelationships());
        Assert.True(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
    }

    static ImmutableArray<int> ManagedMethodTokens(
        string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return
        [
            .. reader.MethodDefinitions
                .Where(handle =>
                    reader.GetMethodDefinition(handle)
                        .RelativeVirtualAddress != 0)
                .Select(static handle =>
                    MetadataTokens.GetToken(handle)),
        ];
    }

    static ImplementationMetricWorkLimits MetricLimits() =>
        new(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: 10_000,
            maximumAttributionProbeBodies: 20,
            maximumAttributionProbeIlBytes: 20_000);

    static int BodylessMethodToken(
        string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle =
            reader.MethodDefinitions.First(candidate =>
                reader.GetMethodDefinition(candidate)
                    .RelativeVirtualAddress == 0);
        return MetadataTokens.GetToken(handle);
    }

    static void ReplaceAscii(
        byte[] image,
        string oldValue,
        string newValue,
        int expectedReplacements)
    {
        byte[] oldBytes =
            System.Text.Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes =
            System.Text.Encoding.ASCII.GetBytes(newValue);
        Assert.Equal(oldBytes.Length, newBytes.Length);
        int replacements = 0;
        for (int index = 0;
            index <= image.Length - oldBytes.Length;
            index++)
        {
            if (!image.AsSpan(index, oldBytes.Length)
                    .SequenceEqual(oldBytes))
            {
                continue;
            }
            newBytes.CopyTo(image, index);
            replacements++;
            index += oldBytes.Length - 1;
        }
        Assert.Equal(expectedReplacements, replacements);
    }

    static int MethodToken(
        string path,
        string typeName,
        string methodName,
        MethodAttributes accessibility)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(definition =>
                reader.StringComparer.Equals(
                    definition.Name,
                    typeName));
        MethodDefinitionHandle handle = type.GetMethods()
            .Single(candidate =>
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(candidate);
                return reader.StringComparer.Equals(
                        method.Name,
                        methodName)
                    && (method.Attributes
                            & MethodAttributes.MemberAccessMask)
                        == accessibility;
            });
        return MetadataTokens.GetToken(handle);
    }

    static int ReplaceMethodCodeSizeWithInvalidValue(
        byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    nameof(ArrayPoolLeakFixtures
                        .ExternalReadBeforeReturn)));
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);
        int methodOffset = RvaToFileOffset(
            peReader.PEHeaders,
            method.RelativeVirtualAddress);
        Assert.Equal(3, image[methodOffset] & 3);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(methodOffset + 4, 4),
            0x7F000000);
        return MetadataTokens.GetToken(methodHandle);
    }

    static int ReplaceMethodSignatureWithInvalidValue(
        byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    nameof(ArrayPoolLeakFixtures
                        .ExternalReadBeforeReturn)));
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);
        int methodToken = MetadataTokens.GetToken(methodHandle);
        Assert.NotEqual(0, method.RelativeVirtualAddress);
        int stringIndexSize = MetadataHeapIndexSize(
            reader,
            HeapIndex.String);
        int blobIndexSize = MetadataHeapIndexSize(
            reader,
            HeapIndex.Blob);
        int rowOffset =
            reader.GetTableMetadataOffset(TableIndex.MethodDef)
            + (MetadataTokens.GetRowNumber(methodHandle) - 1)
                * reader.GetTableRowSize(TableIndex.MethodDef);
        int signatureOffset =
            peReader.PEHeaders.MetadataStartOffset
            + rowOffset
            + sizeof(uint)
            + sizeof(ushort)
            + sizeof(ushort)
            + stringIndexSize;
        if (blobIndexSize == sizeof(uint))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0x7FFFFFFF);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0xFFFF);
        }
        return methodToken;
    }

    static int MetadataHeapIndexSize(
        MetadataReader reader,
        HeapIndex heap) =>
        reader.GetHeapSize(heap) < 0x10000
            ? sizeof(ushort)
            : sizeof(uint);

    static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(rva - section.VirtualAddress + section.PointerToRawData);
    }

    [Fact]
    public void CompatibilityIndex_DelegatesCallGraphAndLeverageResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        LibraryBodyIndex index =
            execution.CompatibilityIndex();
        int rootToken =
            execution.CallGraph.Methods[0].MetadataToken;

        Assert.Same(
            execution.CallGraph,
            index.CallGraphAnalysis);
        Assert.Same(
            execution.Leverage,
            index.LeverageAnalysis);
        Assert.Equal(
            execution.CallGraph.BuildCallTree(rootToken),
            index.BuildCallTree(rootToken));
        Assert.Equal(
            execution.CallGraph.BuildCallerTree(rootToken),
            index.BuildCallerTree(rootToken));
        Assert.Equal(
            execution.Leverage.Top(int.MaxValue),
            index.TopLeverage(int.MaxValue));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompatibilityIndex_PreservesFocusedOptimizationResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                typeof(LibraryBodyAnalysisExecutionTests)
                    .Assembly.Location,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.True(execution.Optimization.WasRequested);
        Assert.False(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.NotEmpty(
            execution.Optimization.Opportunities);
        Assert.True(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.Equal(
            execution.Optimization.Opportunities,
            index.OptimizationOpportunities);
        Assert.Equal(
            execution.Optimization
                .AllocationFanoutOpportunities,
            index.AllocationFanoutOpportunities);
        Assert.True(
            execution.Optimization
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
    }
}
