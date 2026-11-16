using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class ExactMethodMetricEvidenceTests
{
    [Fact]
    public void BodySizeOnly_AcquiresNoInstructionOrSemanticWork()
    {
        MethodInfo method = Method(nameof(ForwardToPrivate));
        using AssemblyInspectionSession session = OpenSession();

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "body-size-only test",
                [method.MetadataToken],
                ExactMethodSemanticProducts.BodySize,
                TestContext.Current.CancellationToken);

        ExactMethodSemanticLane<ExactMethodBodySizeProducerResult> lane =
            Assert.IsType<
                ExactMethodSemanticLane<
                    ExactMethodBodySizeProducerResult>>(result.BodySize);
        ExactMethodBodySize body = Assert.Single(lane.Result.Bodies);
        Assert.Equal(method.MetadataToken, body.MethodToken);
        Assert.True(body.HasManagedBody);
        Assert.True(body.EncodedIlBytes > 0);
        Assert.Null(body.Diagnostic);
        Assert.Null(result.SiblingCalls);
        Assert.Null(result.Calls);
        Assert.Null(result.Safety);
        Assert.Null(result.Allocation);

        ProducerParticipation participation =
            Assert.Single(lane.WorkReceipt.Producers);
        Assert.Equal(
            ExactMethodBodySizeProducer.Instance.Identity,
            participation.Producer);
        ProducerLayerParticipation bodyLayer =
            Assert.Single(participation.Layers);
        Assert.Equal(nameof(MethodDefinitionLayers.Body), bodyLayer.Layer);
        Assert.Equal(1, bodyLayer.Acquired);

        MethodDefinitionSourceCoverage coverage = lane.SourceReceipt.Coverage;
        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.Body,
            lane.SourceReceipt.DeclaredLayers);
        Assert.Equal(1, coverage.BodiesAcquired.Count);
        Assert.Equal(
            MethodDefinitionInstructionWorkCoverage.Empty,
            coverage.InstructionWork);
        Assert.Same(
            MethodCallMetadataCoverage.Empty,
            coverage.Calls);
        Assert.Same(
            SameModuleCallTargetCoverage.Empty,
            coverage.CallTargets);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            coverage.MemorySafetyContracts);
        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            coverage.MethodAnalysis);
    }

    [Fact]
    public void SiblingCallsOnly_PreservesPrivateAndMethodSpecTargets()
    {
        MethodInfo ordinaryCaller = Method(nameof(ForwardToPrivate));
        MethodInfo ordinaryTarget = Method(
            nameof(PrivateTarget),
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo genericCaller = Method(nameof(ForwardGeneric));
        MethodInfo genericTarget = Method(
            nameof(PrivateGenericTarget),
            BindingFlags.Static | BindingFlags.NonPublic);
        using AssemblyInspectionSession session = OpenSession();

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "sibling-call test",
                [
                    ordinaryCaller.MetadataToken,
                    genericCaller.MetadataToken,
                ],
                ExactMethodSemanticProducts.SiblingCalls,
                TestContext.Current.CancellationToken);

        ExactMethodSemanticLane<ExactMethodSiblingCallProducerResult> lane =
            Assert.IsType<
                ExactMethodSemanticLane<
                    ExactMethodSiblingCallProducerResult>>(
                        result.SiblingCalls);
        ExactMethodSiblingCallSite ordinary = Assert.Single(
            Assert.Single(
                    lane.Result.Bodies,
                    body =>
                        body.MethodToken
                            == ordinaryCaller.MetadataToken)
                .Calls,
            call =>
                call.DefinitionToken
                    == ordinaryTarget.MetadataToken);
        MethodCallTarget.CurrentModule ordinaryTargetResult =
            Assert.IsType<MethodCallTarget.CurrentModule>(
                ordinary.Target);
        Assert.Equal(
            ordinaryTarget.MetadataToken,
            MetadataTokens.GetToken(ordinaryTargetResult.Method));

        ExactMethodSiblingCallSite generic = Assert.Single(
            Assert.Single(
                    lane.Result.Bodies,
                    body =>
                        body.MethodToken
                            == genericCaller.MetadataToken)
                .Calls,
            call =>
                call.DefinitionToken
                    == genericTarget.MetadataToken);
        Assert.NotEqual(generic.OperandToken, generic.DefinitionToken);
        MethodCallTarget.CurrentModule genericTargetResult =
            Assert.IsType<MethodCallTarget.CurrentModule>(
                generic.Target);
        Assert.Equal(
            genericTarget.MetadataToken,
            MetadataTokens.GetToken(genericTargetResult.Method));

        ProducerParticipation participation =
            Assert.Single(lane.WorkReceipt.Producers);
        Assert.Equal(
            ExactMethodSiblingCallProducer.Instance.Identity,
            participation.Producer);
        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.Body
                | MethodDefinitionLayers.CallTargets
                | MethodDefinitionLayers.Calls,
            lane.SourceReceipt.DeclaredLayers);
        Assert.DoesNotContain(
            participation.Layers,
            layer =>
                layer.Layer
                    is nameof(MethodDefinitionLayers.LocalSignature)
                        or nameof(
                            MethodDefinitionLayers
                                .MemorySafetyContracts)
                        or nameof(
                            MethodDefinitionLayers.MethodAnalysis)
                        or nameof(
                            MethodDefinitionLayers.SemanticIdentity));
        Assert.Equal(2, lane.SourceReceipt.Coverage.BodiesAcquired.Count);
        Assert.Equal(
            2,
            lane.SourceReceipt.Coverage.InstructionWork
                .LazyRetainedSourcesOpened);
        Assert.Equal(
            0,
            lane.SourceReceipt.Coverage.Calls.LocalSignatureReads);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            lane.SourceReceipt.Coverage.MemorySafetyContracts);
        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            lane.SourceReceipt.Coverage.MethodAnalysis);
    }

    [Fact]
    public void BodySizeAndSiblingCalls_ShareOnePhysicalBody()
    {
        MethodInfo method = Method(nameof(ForwardToPrivate));
        using AssemblyInspectionSession session = OpenSession();

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "combined metric evidence test",
                [method.MetadataToken, method.MetadataToken],
                ExactMethodSemanticProducts.BodySize
                    | ExactMethodSemanticProducts.SiblingCalls,
                TestContext.Current.CancellationToken);

        Assert.NotNull(result.BodySize);
        Assert.NotNull(result.SiblingCalls);
        Assert.Equal(2, result.SourceGroupReceipt.LaneReceipts.Length);
        Assert.Equal(
            1,
            result.SourceGroupReceipt
                .PhysicalCoverage.MethodsSelected.Count);
        Assert.Equal(
            1,
            result.SourceGroupReceipt
                .PhysicalCoverage.BodiesAcquired.Count);
        Assert.Equal(
            1,
            result.SourceGroupReceipt
                .PhysicalCoverage.InstructionWork
                .LazyRetainedSourcesOpened);
        Assert.Equal(
            MethodDefinitionInstructionWorkCoverage.Empty,
            result.BodySize!.SourceReceipt.Coverage.InstructionWork);
        Assert.Equal(
            1,
            result.SiblingCalls!.SourceReceipt.Coverage
                .InstructionWork.LazyRetainedSourcesOpened);
    }

    [Fact]
    public void BodylessMethod_RemainsExplicitWithoutBodyWork()
    {
        MethodInfo method =
            typeof(AbstractFixture).GetMethod(
                nameof(AbstractFixture.Bodyless))!;
        using AssemblyInspectionSession session = OpenSession();

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "bodyless metric evidence test",
                [method.MetadataToken],
                ExactMethodSemanticProducts.BodySize
                    | ExactMethodSemanticProducts.SiblingCalls,
                TestContext.Current.CancellationToken);

        ExactMethodBodySize size =
            Assert.Single(result.BodySize!.Result.Bodies);
        Assert.False(size.HasManagedBody);
        Assert.Null(size.EncodedIlBytes);
        Assert.Null(size.Diagnostic);
        ExactMethodSiblingCallBody calls =
            Assert.Single(result.SiblingCalls!.Result.Bodies);
        Assert.False(calls.HasManagedBody);
        Assert.Empty(calls.Calls);
        Assert.Empty(calls.Diagnostics);
        Assert.Equal(
            0,
            result.SourceGroupReceipt
                .PhysicalCoverage.BodiesAcquired.Count);
        Assert.Equal(
            MethodDefinitionInstructionWorkCoverage.Empty,
            result.SourceGroupReceipt
                .PhysicalCoverage.InstructionWork);
    }

    [Fact]
    public void GeneratedExpansionBound_RetainsTheIncompletePrefix()
    {
        MethodInfo kickoff =
            typeof(ClassicAsyncSiblingFixture).GetMethod(
                nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync))!;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(ClassicAsyncSiblingFixture)
                    .Assembly.Location);
        var scope = new ExactMethodSemanticAnalysisScope(
            includeGeneratedExecutionBodies: true,
            new(
                maximumCandidateDefinitions: 1,
                maximumGeneratedMethods: 100,
                maximumProbeBodies: 100,
                maximumProbeEncodedIlBytes: long.MaxValue,
                maximumRelationshipNodes: 100),
            MethodDefinitionTerminalWorkLimits.Unbounded);

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "bounded generated body-size evidence test",
                [kickoff.MetadataToken],
                ExactMethodSemanticProducts.BodySize,
                scope,
                TestContext.Current.CancellationToken);

        ExactMethodSemanticLane<ExactMethodBodySizeProducerResult> lane =
            Assert.IsType<
                ExactMethodSemanticLane<
                    ExactMethodBodySizeProducerResult>>(result.BodySize);
        Assert.Single(lane.Result.Bodies);
        Assert.Empty(result.Failures);
        Assert.NotNull(
            result.SourceGroupReceipt
                .PhysicalCoverage.GeneratedExpansion.ReachedLimit);
        Assert.Equal(
            ProducerOutcome.SourceBounded,
            Assert.Single(lane.WorkReceipt.Producers).Outcome);
    }

    [Fact]
    public void AsyncExecutionBody_UsesTheSameFocusedEvidenceLanes()
    {
        MethodInfo kickoff =
            typeof(ClassicAsyncSiblingFixture).GetMethod(
                nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync))!;
        MethodInfo moveNext = kickoff
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType
            .GetMethod(
                nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public)!;
        MethodInfo target =
            typeof(ClassicAsyncSiblingFixture).GetMethod(
                nameof(ClassicAsyncSiblingFixture.ReadValue))!;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(ClassicAsyncSiblingFixture)
                    .Assembly.Location);

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "async metric evidence test",
                [moveNext.MetadataToken],
                ExactMethodSemanticProducts.BodySize
                    | ExactMethodSemanticProducts.SiblingCalls,
                TestContext.Current.CancellationToken);

        Assert.True(
            Assert.Single(result.BodySize!.Result.Bodies)
                .EncodedIlBytes > 0);
        ExactMethodSiblingCallSite call = Assert.Single(
            Assert.Single(result.SiblingCalls!.Result.Bodies)
                .Calls,
            site =>
                site.DefinitionToken
                    == target.MetadataToken);
        Assert.IsType<MethodCallTarget.CurrentModule>(call.Target);
        Assert.Equal(
            1,
            result.SourceGroupReceipt
                .PhysicalCoverage.BodiesAcquired.Count);
        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            result.SourceGroupReceipt
                .PhysicalCoverage.MethodAnalysis);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            result.SourceGroupReceipt
                .PhysicalCoverage.MemorySafetyContracts);
    }

    [Fact]
    public void GeneratedAsyncBody_SharesExpansionAndPhysicalEvidence()
    {
        MethodInfo kickoff =
            typeof(ClassicAsyncSiblingFixture).GetMethod(
                nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync))!;
        MethodInfo moveNext = kickoff
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType
            .GetMethod(
                nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public)!;
        MethodInfo target =
            typeof(ClassicAsyncSiblingFixture).GetMethod(
                nameof(ClassicAsyncSiblingFixture.ReadValue))!;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(ClassicAsyncSiblingFixture)
                    .Assembly.Location);
        var scope = new ExactMethodSemanticAnalysisScope(
            includeGeneratedExecutionBodies: true,
            MethodDefinitionGeneratedExpansionLimits.Default,
            MethodDefinitionTerminalWorkLimits.Unbounded);

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "generated async metric evidence test",
                [kickoff.MetadataToken],
                ExactMethodSemanticProducts.BodySize
                    | ExactMethodSemanticProducts.SiblingCalls,
                scope,
                TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SourceGroupReceipt.LaneReceipts.Length);
        Assert.Contains(
            result.SourceGroupReceipt
                .PhysicalCoverage.GeneratedExpansion.Origins,
            origin =>
                MetadataTokens.GetToken(origin.Method)
                    == moveNext.MetadataToken);
        Assert.Equal(
            2,
            result.SourceGroupReceipt
                .PhysicalCoverage.MethodsSelected.Count);
        Assert.Equal(
            2,
            result.SourceGroupReceipt
                .PhysicalCoverage.BodiesAcquired.Count);
        Assert.Equal(
            MethodDefinitionInstructionWorkCoverage.Empty,
            result.BodySize!.SourceReceipt.Coverage.InstructionWork);
        Assert.Contains(
            result.SiblingCalls!.Result.Bodies,
            body =>
                body.MethodToken == moveNext.MetadataToken
                && body.Calls.Any(
                    call =>
                        call.DefinitionToken
                            == target.MetadataToken));
    }

    [Fact]
    public void MalformedInstructions_DoNotHideAvailableBodySize()
    {
        byte[] image =
            MetadataMethodPtrFixture.BuildTrailingOutOfRange(
                [0x0A, 0xFE, 0xFF]);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));

        ExactMethodSemanticAnalysisResult result =
            ExactMethodSemanticAnalysis.Execute(
                session,
                "malformed metric evidence test",
                [
                    MetadataTokens.GetToken(
                        MetadataTokens.MethodDefinitionHandle(1)),
                ],
                ExactMethodSemanticProducts.BodySize
                    | ExactMethodSemanticProducts.SiblingCalls,
                TestContext.Current.CancellationToken);

        ExactMethodBodySize size =
            Assert.Single(result.BodySize!.Result.Bodies);
        Assert.Equal(2, size.EncodedIlBytes);
        Assert.Null(size.Diagnostic);
        ExactMethodSiblingCallBody calls =
            Assert.Single(result.SiblingCalls!.Result.Bodies);
        Assert.Empty(calls.Calls);
        Assert.NotEmpty(calls.Diagnostics);
    }

    [Fact]
    public void EmptyProductRequest_IsRejectedBeforeExecution()
    {
        using AssemblyInspectionSession session = OpenSession();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ExactMethodSemanticAnalysis.Execute(
                session,
                "empty metric evidence test",
                [Method(nameof(ForwardToPrivate)).MetadataToken],
                ExactMethodSemanticProducts.None,
                TestContext.Current.CancellationToken));

        Assert.Equal("products", error.ParamName);
    }

    static MethodInfo Method(
        string name,
        BindingFlags bindingFlags =
            BindingFlags.Static
                | BindingFlags.NonPublic) =>
        typeof(ExactMethodMetricEvidenceTests).GetMethod(
            name,
            bindingFlags)
        ?? throw new InvalidOperationException(
            $"Method '{name}' was not found.");

    static AssemblyInspectionSession OpenSession() =>
        AssemblyInspectionSession.Open(
            typeof(ExactMethodMetricEvidenceTests)
                .Assembly.Location);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ForwardToPrivate(int value) =>
        PrivateTarget(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int PrivateTarget(int value) =>
        value + 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static T ForwardGeneric<T>(T value) =>
        PrivateGenericTarget(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static T PrivateGenericTarget<T>(T value) =>
        value;

    abstract class AbstractFixture
    {
        public abstract int Bodyless();
    }
}
