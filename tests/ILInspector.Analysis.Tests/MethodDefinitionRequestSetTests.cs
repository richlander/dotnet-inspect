using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace ILInspector.Analysis.Tests;

public sealed class MethodDefinitionRequestSetTests
{
    [Fact]
    public void Plan_PreservesDistinctClosingsInOnePhysicalGroup()
    {
        CountingProducer producer = CountingProducer.Instance;
        MethodDefinitionSourceAssociation[] associations =
        [
            Association(producer, ProducerTerminal.Rows),
            Association(producer, ProducerTerminal.Count),
            Association(producer, ProducerTerminal.Exists),
        ];

        MethodDefinitionSourceRequestSetPlan plan =
            Assert.IsType<
                    MethodDefinitionSourceRequestSetPlanResult.Accepted>(
                        MethodDefinitionSourceRequestSet.Plan(
                            MethodDefinitionSourceResourceIdentity.Create(),
                            associations))
                .Plan;

        MethodDefinitionSourceGroupPlan group =
            Assert.Single(plan.Groups);
        Assert.Equal(3, group.Lanes.Length);
        Assert.Equal(
            [
                ProducerTerminal.Rows,
                ProducerTerminal.Count,
                ProducerTerminal.Exists,
            ],
            group.Lanes.Select(
                static lane =>
                    lane.Associations[0].Request.Terminal));
    }

    [Fact]
    public void Plan_DuplicateAssociationReturnsTypedRejection()
    {
        MethodDefinitionSourceAssociation association =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        var rejected = Assert.IsType<
            MethodDefinitionSourceRequestSetPlanResult.Rejected>(
                MethodDefinitionSourceRequestSet.Plan(
                    MethodDefinitionSourceResourceIdentity.Create(),
                    [association, association]));

        QuerySpaceRequestSetRejection reason =
            Assert.Single(rejected.Reasons);
        Assert.Equal(1, reason.CandidateIndex);
        Assert.Same(association.Identity, reason.Association);
        Assert.Equal(
            QuerySpaceRequestSetRejectionReason
                .DuplicateAssociationIdentity,
            reason.Reason);
    }

    [Fact]
    public void Plan_PreservesOwnerIssuedWorkDescription()
    {
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan(
                        [new ProducerRequest(
                            CountingProducer.Instance,
                            ProducerTerminal.Count)]))
                .Description;
        MethodDefinitionSourceRequest<int> request =
            MethodDefinitionSourceRequest<int>.Create(
                QueryRequest(ProducerTerminal.Count),
                work,
                CountingProducer.Instance);
        MethodDefinitionSourceAssociation association =
            MethodDefinitionSourceAssociation.Create(request);

        MethodDefinitionSourceRequestSetPlan plan =
            AcceptedPlan([association]);

        Assert.Same(
            work,
            Assert.Single(Assert.Single(plan.Groups).Lanes).Work);
    }

    [Fact]
    public void
        Plan_SeparatesInstructionSourceKindsWithoutWideningLanes()
    {
        MethodDefinitionSourceAssociation shallow =
            Association(
                MethodCallCountProducer.DirectInvocations,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation retained =
            Association(
                PrefixRetainedInstructionProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetPlan plan =
            AcceptedPlan([shallow, retained]);
        Assert.Equal(2, plan.Groups.Length);
        MethodDefinitionSourceGroupPlan shallowGroup =
            Assert.Single(
                plan.Groups,
                group => ReferenceEquals(
                    group.Lanes[0].Associations[0].Identity,
                    shallow.Identity));
        MethodDefinitionSourceGroupPlan retainedGroup =
            Assert.Single(
                plan.Groups,
                group => ReferenceEquals(
                    group.Lanes[0].Associations[0].Identity,
                    retained.Identity));
        MethodDefinitionSourceLanePlan shallowLane =
            Assert.Single(shallowGroup.Lanes);
        MethodDefinitionSourceLanePlan retainedLane =
            Assert.Single(retainedGroup.Lanes);

        MethodBodyAnalyzerPlan shallowPlan =
            Assert.IsType<MethodBodyAnalyzerPlan>(
                shallowLane.InstructionPlan);
        Assert.Equal(
            MethodBodyInstructionAccess.ForwardOnly,
            shallowPlan.Demand.Access);
        Assert.Equal(
            MethodBodyInstructionDetail.OpcodeAndExtent,
            shallowPlan.Demand.Detail);
        Assert.Equal(
            MethodBodyInstructionSourceKind.NoRetentionStream,
            shallowPlan.Source);

        MethodBodyAnalyzerPlan retainedPlan =
            Assert.IsType<MethodBodyAnalyzerPlan>(
                retainedLane.InstructionPlan);
        Assert.Equal(
            MethodBodyInstructionSourceKind.LazyRetainedSequence,
            retainedPlan.Source);

        MethodBodyAnalyzerPlan shallowGroupPlan =
            Assert.IsType<MethodBodyAnalyzerPlan>(
                shallowGroup.InstructionPlan);
        Assert.Equal(
            MethodBodyInstructionAccess.ForwardOnly,
            shallowGroupPlan.Demand.Access);
        Assert.Equal(
            MethodBodyInstructionDetail.OpcodeAndExtent,
            shallowGroupPlan.Demand.Detail);
        Assert.Equal(
            MethodBodyInstructionSourceKind.NoRetentionStream,
            shallowGroupPlan.Source);
        MethodBodyAnalyzerPlan retainedGroupPlan =
            Assert.IsType<MethodBodyAnalyzerPlan>(
                retainedGroup.InstructionPlan);
        Assert.Equal(
            MethodBodyInstructionAccess.RetainedPrefix,
            retainedGroupPlan.Demand.Access);
        Assert.Equal(
            MethodBodyInstructionDetail.SelectiveOperands,
            retainedGroupPlan.Demand.Detail);
        Assert.Equal(
            MethodBodyInstructionSourceKind.LazyRetainedSequence,
            retainedGroupPlan.Source);
        Assert.Single(
            shallowGroupPlan.StructuralPlan.Requirements);
        Assert.Single(
            retainedGroupPlan.StructuralPlan.Requirements);
    }

    [Fact]
    public void Execute_SeparatesNoRetentionAndRetainedInstructionSources()
    {
        MethodDefinitionSourceAssociation shallow =
            Association(
                MethodCallCountProducer.DirectInvocations,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation retained =
            Association(
                PrefixRetainedInstructionProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([shallow, retained]));
        Assert.Equal(2, execution.GroupReceipts.Length);
        MethodDefinitionInstructionWorkCoverage shallowWork =
            execution.ResultOf(shallow)
                .SourceReceipt.Coverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage retainedWork =
            execution.ResultOf(retained)
                .SourceReceipt.Coverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage shallowPhysical =
            Assert.Single(
                execution.GroupReceipts,
                group => ReferenceEquals(
                    group.Source,
                    execution.ResultOf(shallow).Source))
                .PhysicalCoverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage retainedPhysical =
            Assert.Single(
                execution.GroupReceipts,
                group => ReferenceEquals(
                    group.Source,
                    execution.ResultOf(retained).Source))
                .PhysicalCoverage.InstructionWork;

        Assert.True(shallowPhysical.NoRetentionSourcesOpened > 0);
        Assert.Equal(0, shallowPhysical.LazyRetainedSourcesOpened);
        Assert.Equal(shallowPhysical, shallowWork);
        Assert.Equal(0, retainedPhysical.NoRetentionSourcesOpened);
        Assert.True(retainedPhysical.LazyRetainedSourcesOpened > 0);
        Assert.Equal(retainedPhysical, retainedWork);
        Assert.True(
            retainedPhysical.InstructionsVisited
            < shallowPhysical.InstructionsVisited);
    }

    [Fact]
    public void Execute_MaterializesOneSelectiveRetainedDecode()
    {
        MethodDefinitionSourceAssociation prefix =
            Association(
                PrefixRetainedInstructionProducer.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation materialized =
            Association(
                MaterializedInstructionProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([prefix, materialized]));
        MethodDefinitionSourceGroupReceipt group =
            Assert.Single(execution.GroupReceipts);
        MethodDefinitionInstructionWorkCoverage materializedWork =
            execution.ResultOf(materialized)
                .SourceReceipt.Coverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage work =
            group.PhysicalCoverage.InstructionWork;

        Assert.True(work.LazyRetainedSourcesOpened > 0);
        Assert.True(
            materializedWork.InstructionsVisited
            > materializedWork.LazyRetainedSourcesOpened);
        Assert.True(
            work.InstructionsVisited
            > work.LazyRetainedSourcesOpened);
        Assert.Equal(0, work.NoRetentionSourcesOpened);
    }

    [Fact]
    public void Execute_AttributesRetainedSourceOpeningToOpeningLane()
    {
        MethodDefinitionSourceAssociation prefix =
            Association(
                PrefixRetainedInstructionProducer.Instance,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation complete =
            Association(
                RetainedInstructionProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([prefix, complete]));
        MethodDefinitionInstructionWorkCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage prefixWork =
            execution.ResultOf(prefix)
                .SourceReceipt.Coverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage completeWork =
            execution.ResultOf(complete)
                .SourceReceipt.Coverage.InstructionWork;

        Assert.Equal(0, physical.NoRetentionSourcesOpened);
        Assert.True(physical.LazyRetainedSourcesOpened > 0);
        Assert.Equal(
            physical.LazyRetainedSourcesOpened,
            prefixWork.LazyRetainedSourcesOpened);
        Assert.Equal(0, completeWork.LazyRetainedSourcesOpened);
        Assert.True(
            completeWork.InstructionsVisited
            > prefixWork.InstructionsVisited);
        Assert.Equal(
            completeWork.InstructionsVisited,
            physical.InstructionsVisited);
    }

    [Fact]
    public void Execute_FusesNoRetentionInstructionSourceAcrossLanes()
    {
        MethodDefinitionSourceAssociation direct =
            Association(
                MethodCallCountProducer.DirectInvocations,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation callSites =
            Association(
                MethodCallCountProducer.CallSites,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([direct, callSites]));
        MethodDefinitionInstructionWorkCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage directWork =
            execution.ResultOf(direct)
                .SourceReceipt.Coverage.InstructionWork;
        MethodDefinitionInstructionWorkCoverage callSiteWork =
            execution.ResultOf(callSites)
                .SourceReceipt.Coverage.InstructionWork;

        Assert.True(physical.NoRetentionSourcesOpened > 0);
        Assert.Equal(0, physical.LazyRetainedSourcesOpened);
        Assert.Equal(
            physical.NoRetentionSourcesOpened,
            directWork.NoRetentionSourcesOpened);
        Assert.Equal(
            physical.NoRetentionSourcesOpened,
            callSiteWork.NoRetentionSourcesOpened);
        Assert.Equal(
            physical.InstructionsVisited,
            directWork.InstructionsVisited);
        Assert.Equal(
            physical.InstructionsVisited,
            callSiteWork.InstructionsVisited);
    }

    [Fact]
    public void
        Execute_RejectsScopeGuardOnPendingFusedInstructionFact()
    {
        FusedClassificationProducer classifier =
            FusedClassificationProducer.Instance;
        var guarded = new GuardedCountProducer(classifier);
        MethodDefinitionSourceAssociation guardedAssociation =
            Association(
                guarded,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation callSites =
            Association(
                MethodCallCountProducer.CallSites,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(
                    AcceptedPlan(
                        [guardedAssociation, callSites])));

        Assert.Contains(
            "before the shared instruction stream completes",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RetainedInstructionFailureReceiptsCompletedPrefix()
    {
        MethodDefinitionSourceAssociation retained =
            Association(
                RetainedInstructionProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([retained]),
                TwoMethodsInExcludedTypeWithFirstBody(
                    [0x0A, 0x00, 0x28]));
        MethodDefinitionInstructionWorkCoverage work =
            execution.ResultOf(retained)
                .SourceReceipt.Coverage.InstructionWork;

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<int>(execution, retained).Outcome);
        Assert.Equal(0, work.NoRetentionSourcesOpened);
        Assert.Equal(1, work.LazyRetainedSourcesOpened);
        Assert.Equal(1, work.InstructionsVisited);
    }

    [Fact]
    public void Execute_SharedClosingsEqualIndependentReferenceResults()
    {
        CountingProducer producer = CountingProducer.Instance;
        MethodDefinitionSourceAssociation rows =
            Association(producer, ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation count =
            Association(producer, ProducerTerminal.Count);
        MethodDefinitionSourceAssociation exists =
            Association(producer, ProducerTerminal.Exists);
        MethodDefinitionSourceRequestSetPlan plan =
            AcceptedPlan([rows, count, exists]);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(plan);

        int rowsValue = ValueOf(execution, rows);
        int countValue = ValueOf(execution, count);
        int existsValue = ValueOf(execution, exists);
        Assert.True(rowsValue > 1);
        Assert.Equal(rowsValue, countValue);
        Assert.Equal(1, existsValue);

        MethodDefinitionSourceRequestResult rowsResult =
            execution.ResultOf(rows);
        MethodDefinitionSourceRequestResult countResult =
            execution.ResultOf(count);
        MethodDefinitionSourceRequestResult existsResult =
            execution.ResultOf(exists);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            rowsResult.SourceReceipt.Completion);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            countResult.SourceReceipt.Completion);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            existsResult.SourceReceipt.Completion);
        Assert.True(
            existsResult.SourceReceipt.DefinitionsVisited
            < rowsResult.SourceReceipt.DefinitionsVisited);

        MethodDefinitionSourceGroupReceipt group =
            Assert.Single(execution.GroupReceipts);
        Assert.Equal(3, group.LaneReceipts.Length);
        Assert.Equal(
            rowsResult.SourceReceipt.DefinitionsVisited,
            group.PhysicalCoverage.MethodsSelected.Count);
    }

    [Fact]
    public void Execute_SettledExistsSurvivesLaterRowsAbort()
    {
        MethodDefinitionSourceAssociation exists =
            Association(
                SettlingProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation rows =
            Association(
                AbortingProducer.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([exists, rows]));

        ProducerResult<int> existsResult =
            ResultOf(execution, exists);
        ProducerResult<int> rowsResult =
            ResultOf(execution, rows);
        Assert.Equal(ProducerOutcome.Stopped, existsResult.Outcome);
        Assert.Equal(1, existsResult.Value);
        Assert.Equal(ProducerOutcome.Aborted, rowsResult.Outcome);
        Assert.NotNull(rowsResult.Critical);
    }

    [Fact]
    public void Execute_SameTerminalSettledRequestSurvivesLaterAbort()
    {
        MethodDefinitionSourceAssociation settled =
            Association(
                SettlingProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation aborted =
            Association(
                AbortingProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([settled, aborted]));

        Assert.Equal(
            new ProducerResult<int>(
                ProducerOutcome.Stopped,
                1),
            ResultOf<int>(execution, settled));
        Assert.Equal(
            ProducerOutcome.Aborted,
            ResultOf<int>(execution, aborted).Outcome);
        Assert.Equal(
            1,
            execution.ResultOf(settled)
                .SourceReceipt.DefinitionsVisited);
        Assert.Equal(
            2,
            execution.ResultOf(aborted)
                .SourceReceipt.DefinitionsVisited);
        MethodDefinitionSourceGroupReceipt group =
            Assert.Single(execution.GroupReceipts);
        Assert.Equal(2, group.LaneReceipts.Length);
    }

    [Fact]
    public void Execute_FailedBodyReadIsNotReportedAsAcquired()
    {
        MethodDefinitionSourceAssociation association =
            Association(
                BodyReadingProducer.Instance,
                ProducerTerminal.Count);
        ImmutableArray<byte> image =
            ImmutableArray.Create(
                MetadataMethodPtrFixture.BuildTrailingOutOfRange(
                    [0x00]));

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]), image);

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<int>(execution, association).Outcome);
        MethodDefinitionSourceRequestResult result =
            execution.ResultOf(association);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            result.SourceReceipt.Completion);
        Assert.Null(result.SourceReceipt.SourceFailure);
        Assert.Equal(
            1,
            result.SourceReceipt.Coverage.BodiesAttempted.Count);
        Assert.True(
            result.SourceReceipt.Coverage.BodiesAttempted.Contains(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(0, result.SourceReceipt.BodiesAcquired);
        Assert.Equal(
            0,
            result.WorkReceipt
                .For(BodyReadingProducer.Instance)
                .Layers.Single(
                    static layer =>
                        layer.Layer
                            == nameof(MethodDefinitionLayers.Body))
                .Acquired);
        Assert.Equal(
            1,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.BodiesAttempted.Count);
        Assert.Equal(
            0,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.BodiesAcquired.Count);
    }

    [Fact]
    public void Execute_TerminalBodyBoundIsLaneLocal()
    {
        var boundedLimits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: long.MaxValue);
        var completeLimits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: int.MaxValue,
            maximumEncodedIlBytes: long.MaxValue);
        MethodDefinitionSourceAssociation bounded =
            Association(
                BodyReadingProducer.Instance,
                ProducerTerminal.Count,
                terminalWorkLimits: boundedLimits);
        MethodDefinitionSourceAssociation complete =
            Association(
                BodyReadingProducer.Instance,
                ProducerTerminal.Count,
                terminalWorkLimits: completeLimits);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([bounded, complete]));

        MethodDefinitionSourceReceipt boundedReceipt =
            execution.ResultOf(bounded).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            boundedReceipt.Completion);
        Assert.Same(boundedLimits, boundedReceipt.TerminalWorkLimits);
        Assert.Contains(
            "terminal physical-body limit",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    boundedReceipt.SourceFailure)
                .Message,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            boundedReceipt.Coverage.TerminalWork.BodiesAdmitted);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.Bodies,
            boundedReceipt.Coverage.TerminalWork.ReachedLimit);
        Assert.NotNull(
            boundedReceipt.Coverage.TerminalWork
                .ReachedAtMethodToken);
        Assert.Equal(
            1,
            boundedReceipt.Coverage.BodiesAcquired.Count);

        MethodDefinitionSourceReceipt completeReceipt =
            execution.ResultOf(complete).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            completeReceipt.Completion);
        Assert.Null(completeReceipt.SourceFailure);
        Assert.True(
            completeReceipt.Coverage.TerminalWork.BodiesAdmitted > 1);
        Assert.Null(
            completeReceipt.Coverage.TerminalWork.ReachedLimit);
    }

    [Fact]
    public void
        Execute_CallCountBodyBoundDoesNotOpenUnacquiredInstructionSource()
    {
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: long.MaxValue);
        MethodDefinitionSourceAssociation association =
            Association(
                MethodCallCountProducer.DirectInvocations,
                ProducerTerminal.Count,
                terminalWorkLimits: limits);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]));
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;

        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            receipt.Completion);
        Assert.Equal(
            1,
            receipt.Coverage.TerminalWork.BodiesAdmitted);
        Assert.Equal(
            1,
            receipt.Coverage.InstructionWork
                .NoRetentionSourcesOpened);
    }

    [Fact]
    public void Execute_TerminalBodyBoundInCountKernelIsSourceIncomplete()
    {
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: long.MaxValue);
        MethodDefinitionSourceAssociation association =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Count,
                terminalWorkLimits: limits);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]));

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<int>(execution, association).Outcome);
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            receipt.Completion);
        Assert.Contains(
            "terminal physical-body limit",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    receipt.SourceFailure)
                .Message,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            receipt.Coverage.TerminalWork.BodiesAdmitted);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.Bodies,
            receipt.Coverage.TerminalWork.ReachedLimit);
    }

    [Fact]
    public void
        Execute_TerminalBodyBoundThroughBodyUseProducerIsSourceIncomplete()
    {
        var producer = new AnalysisLibraryBodyUseProducer(
            new AnalysisLibraryBodyUseLimits(),
            TestContext.Current.CancellationToken);
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: 1,
            maximumEncodedIlBytes: long.MaxValue);
        MethodDefinitionSourceAssociation association =
            Association(
                producer,
                ProducerTerminal.Count,
                terminalWorkLimits: limits);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]));

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<AnalysisLibraryBodyUseProducer.Result>(
                    execution,
                    association)
                .Outcome);
        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            receipt.Completion);
        Assert.Contains(
            "terminal physical-body limit",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    receipt.SourceFailure)
                .Message,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            receipt.Coverage.TerminalWork.BodiesAdmitted);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.Bodies,
            receipt.Coverage.TerminalWork.ReachedLimit);
    }

    [Fact]
    public void Execute_TerminalEncodedIlByteBoundPublishesPartialWork()
    {
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: int.MaxValue,
            maximumEncodedIlBytes: 1);
        MethodDefinitionSourceAssociation association =
            Association(
                BodyReadingProducer.Instance,
                ProducerTerminal.Count,
                terminalWorkLimits: limits);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]));

        MethodDefinitionSourceReceipt receipt =
            execution.ResultOf(association).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            receipt.Completion);
        Assert.Contains(
            "terminal encoded-IL-byte limit",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    receipt.SourceFailure)
                .Message,
            StringComparison.Ordinal);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.EncodedIlBytes,
            receipt.Coverage.TerminalWork.ReachedLimit);
        Assert.InRange(
            receipt.Coverage.TerminalWork.EncodedIlBytes,
            0,
            1);
        Assert.True(
            receipt.Coverage.BodiesAcquired.Count
            > receipt.Coverage.TerminalWork.BodiesAdmitted);
    }

    [Fact]
    public void TerminalWorkBudget_OrderedAdmissionsUseCompactRetention()
    {
        const int MethodCount = 10_000;
        var limits = new MethodDefinitionTerminalWorkLimits(
            maximumBodies: MethodCount,
            maximumEncodedIlBytes: long.MaxValue);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var budget = new MethodDefinitionTerminalWorkBudget(limits);
        for (int row = 1; row <= MethodCount; row++)
        {
            MethodDefinitionHandle method =
                MetadataTokens.MethodDefinitionHandle(row);
            budget.RequireBodyCapacity(method);
            budget.Admit(method, encodedIlBytes: 1);
        }
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        MethodDefinitionTerminalWorkCoverage coverage = budget.Build();
        Assert.Equal(MethodCount, coverage.BodiesAdmitted);
        Assert.Equal(MethodCount, coverage.EncodedIlBytes);
        Assert.True(
            allocated < 4_096,
            $"Ordered terminal admission allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void TerminalWorkBudget_MultipassRevisitIsNotChargedTwice()
    {
        var budget = new MethodDefinitionTerminalWorkBudget(
            new MethodDefinitionTerminalWorkLimits(
                maximumBodies: 2,
                maximumEncodedIlBytes: 2));
        MethodDefinitionHandle first =
            MetadataTokens.MethodDefinitionHandle(1);
        MethodDefinitionHandle second =
            MetadataTokens.MethodDefinitionHandle(2);

        budget.RequireBodyCapacity(first);
        budget.Admit(first, encodedIlBytes: 1);
        budget.RequireBodyCapacity(second);
        budget.Admit(second, encodedIlBytes: 1);
        budget.RequireBodyCapacity(first);
        budget.Admit(first, encodedIlBytes: 1);

        MethodDefinitionTerminalWorkCoverage coverage = budget.Build();
        Assert.Equal(2, coverage.BodiesAdmitted);
        Assert.Equal(2, coverage.EncodedIlBytes);
        Assert.Null(coverage.ReachedLimit);
    }

    [Fact]
    public void Execute_SourceFailureAffectsOnlyRequestsInFailedTypeScope()
    {
        ImmutableArray<byte> image =
            ImmutableArray.Create(
                MetadataMethodPtrFixture.BuildTrailingOutOfRange(
                    [0x06, 0x2A]));
        MethodDefinitionSourceAssociation unsafeEvidence =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation pinvoke =
            Association(
                PInvokeClassificationProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([unsafeEvidence, pinvoke]),
                image);

        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<int>(execution, unsafeEvidence).Outcome);
        MethodDefinitionSourceReceipt unsafeReceipt =
            execution.ResultOf(unsafeEvidence).SourceReceipt;
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            unsafeReceipt.Completion);
        Assert.Equal(
            "(method source)",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    unsafeReceipt.SourceFailure)
                .Unit);
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            pinvokeResult =
                ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        execution,
                        pinvoke);
        Assert.Equal(
            ProducerOutcome.Complete,
            pinvokeResult.Outcome);
        Assert.Empty(pinvokeResult.Value!.Rows);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            execution.ResultOf(pinvoke).SourceReceipt.Completion);
        Assert.Null(
            execution.ResultOf(pinvoke).SourceReceipt.SourceFailure);
        Assert.Equal(
            0,
            execution.ResultOf(pinvoke)
                .SourceReceipt.DefinitionsVisited);
    }

    [Fact]
    public void Execute_SettledTypeScopeStopsPhysicalReadsForThatType()
    {
        MethodDefinitionSourceAssociation unsafeEvidence =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation pinvoke =
            Association(
                PInvokeClassificationProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([unsafeEvidence, pinvoke]),
                TwoMethodsInExcludedTypeWithFirstBody(
                    [0x0A, 0xFE, 0x0F]));

        Assert.Equal(
            new ProducerResult<int>(
                ProducerOutcome.Stopped,
                1),
            ResultOf<int>(execution, unsafeEvidence));
        Assert.Equal(
            ProducerOutcome.Complete,
            ResultOf<ClosedQueryResult<ClassifiedMethodRow>>(
                    execution,
                    pinvoke)
                .Outcome);
        MethodDefinitionSourceGroupReceipt group =
            Assert.Single(execution.GroupReceipts);
        Assert.Equal(
            1,
            execution.ResultOf(unsafeEvidence)
                .SourceReceipt.DefinitionsVisited);
        Assert.Equal(
            0,
            execution.ResultOf(pinvoke)
                .SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, group.PhysicalCoverage.MethodsSelected.Count);
    }

    [Fact]
    public void Execute_SettledRequestSurvivesRequiredSourceFailure()
    {
        byte[] bytes =
            MetadataMethodPtrFixture.BuildTrailingOutOfRange(
                [0x0A, 0xFE, 0x0F]);
        int moduleName = bytes.AsSpan().IndexOf("<Module>\0"u8);
        Assert.True(moduleName >= 0);
        "VisibleT\0"u8.CopyTo(bytes.AsSpan(moduleName));
        MethodDefinitionSourceAssociation unsafeEvidence =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation pinvoke =
            Association(
                PInvokeClassificationProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([unsafeEvidence, pinvoke]),
                ImmutableArray.Create(bytes));

        Assert.Equal(
            new ProducerResult<int>(
                ProducerOutcome.Stopped,
                1),
            ResultOf<int>(
                execution,
                unsafeEvidence));
        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        execution,
                        pinvoke)
                .Outcome);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.ResultOf(unsafeEvidence)
                .SourceReceipt.Completion);
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            execution.ResultOf(pinvoke).SourceReceipt.Completion);
        Assert.Equal(
            "(method source)",
            Assert.IsType<MethodDefinitionSourceFailure>(
                    execution.ResultOf(pinvoke)
                        .SourceReceipt.SourceFailure)
                .Unit);
    }

    [Fact]
    public void Execute_CompletedGroupSurvivesLaterSingletonSourceFailure()
    {
        ImmutableArray<byte> image =
            ImmutableArray.Create(
                MetadataMethodPtrFixture.BuildTrailingOutOfRange(
                    [0x06, 0x2A]));
        MethodDefinitionSourceAssociation completed =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists,
                MethodDefinitionSourceBreadth.ExactMethods(
                    MetadataTokens.MethodDefinitionHandle(1)));
        MethodDefinitionSourceAssociation failed =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([completed, failed]),
                image);

        Assert.Equal(
            ProducerOutcome.Complete,
            ResultOf<int>(execution, completed).Outcome);
        Assert.Equal(
            ProducerOutcome.Failed,
            ResultOf<int>(execution, failed).Outcome);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Exhausted,
            execution.ResultOf(completed)
                .SourceReceipt.Completion);
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            execution.ResultOf(failed).SourceReceipt.Completion);
        MethodDefinitionSourceFailure sourceFailure =
            Assert.IsType<MethodDefinitionSourceFailure>(
                execution.ResultOf(failed)
                    .SourceReceipt.SourceFailure);
        Assert.Equal("(method source)", sourceFailure.Unit);
        Assert.Contains(
            nameof(BadImageFormatException),
            sourceFailure.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            execution.ResultOf(completed)
                .SourceReceipt.DefinitionsVisited);
        Assert.Equal(2, execution.GroupReceipts.Length);
    }

    [Fact]
    public void Execute_DifferentTypeScopesPreserveTypedRowOrdinals()
    {
        ImmutableArray<byte> image =
            [.. File.ReadAllBytes(
                typeof(RequestSetCollapseFixtures)
                    .Assembly.Location)];
        MethodDefinitionSourceAssociation sharedUnsafe =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation sharedPInvoke =
            Association(
                PInvokeClassificationProducer.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation independentPInvoke =
            Association(
                PInvokeClassificationProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution shared =
            Execute(
                AcceptedPlan(
                    [sharedUnsafe, sharedPInvoke]),
                image);
        MethodDefinitionSourceRequestSetExecution independent =
            Execute(
                AcceptedPlan([independentPInvoke]),
                image);

        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            sharedResult =
                ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        shared,
                        sharedPInvoke);
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            independentResult =
                ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        independent,
                        independentPInvoke);
        Assert.Equal(
            independentResult.Value!.Rows.ToArray(),
            sharedResult.Value!.Rows.ToArray());
        Assert.Single(sharedResult.Value.Rows);
        MethodDefinitionHandleCoverage lookupMethods =
            shared.ResultOf(sharedUnsafe)
                .SourceReceipt.Coverage.ModuleLookupMethods;
        Assert.True(lookupMethods.Count > 0);
        Assert.Equal(
            lookupMethods.Count,
            Assert.Single(shared.GroupReceipts)
                .PhysicalCoverage.ModuleLookupMethods.Count);
    }

    [Fact]
    public void Execute_ReorderedMethodPtrMatchesIndependentResults()
    {
        ImmutableArray<byte> image =
            ImmutableArray.Create(
                MetadataMethodPtrFixture
                    .BuildSplitReorderedPointerMethods());
        MethodDefinitionSourceAssociation unsafeEvidence =
            Association(
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceAssociation pointer =
            Association(
                PointerSignatureClassificationProducer.Instance,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution shared =
            Execute(
                AcceptedPlan([unsafeEvidence, pointer]),
                image);
        MethodDefinitionSourceRequestSetExecution independentUnsafe =
            Execute(
                AcceptedPlan([unsafeEvidence]),
                image);
        MethodDefinitionSourceRequestSetExecution independentPointer =
            Execute(
                AcceptedPlan([pointer]),
                image);

        Assert.Equal(
            ResultOf<int>(independentUnsafe, unsafeEvidence),
            ResultOf<int>(shared, unsafeEvidence));
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            sharedPointer =
                ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        shared,
                        pointer);
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            independentPointerResult =
                ResultOf<
                    ClosedQueryResult<ClassifiedMethodRow>>(
                        independentPointer,
                        pointer);
        Assert.Equal(
            independentPointerResult.Outcome,
            sharedPointer.Outcome);
        Assert.Equal(
            independentPointerResult.Value!.Rows.ToArray(),
            sharedPointer.Value!.Rows.ToArray());
        MethodDefinitionHandleCoverage physical =
            Assert.Single(shared.GroupReceipts)
                .PhysicalCoverage.MethodsSelected;
        Assert.Equal(2, physical.Count);
        Assert.True(
            physical.Contains(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.True(
            physical.Contains(
                MetadataTokens.MethodDefinitionHandle(2)));
    }

    [Fact]
    public void RequestSet_RejectsPlanDeclaringReferenceBinding()
    {
        MethodDefinitionSourceRequestSetPlan plan = AcceptedPlan(
            [
                Association(
                    AsyncSiblingProducer.Instance,
                    ProducerTerminal.Rows),
            ]);
        Assert.Equal(
            MethodDefinitionLayers.ReferenceBinding,
            plan.DeclaredLayers & MethodDefinitionLayers.ReferenceBinding);
        string path =
            typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(path, plan);

        var result = session.SnapshotOperation(
            operation,
            access => AssemblyAnalysisService.Instance.Execute(
                operation,
                access));

        Assert.Equal(
            AssemblyAnalysisRejectionKind.ReferenceBindingUnavailable,
            Assert.IsType<
                    AssemblyAnalysisRequestSetServiceResult.Rejected>(result)
                .Kind);
    }

    static MethodDefinitionSourceAssociation Association<TResult>(
        ProducerDeclaration<TResult> producer,
        ProducerTerminal terminal,
        MethodDefinitionSourceBreadth? breadth = null,
        MethodDefinitionTerminalWorkLimits? terminalWorkLimits = null)
    {
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan(
                        [new ProducerRequest(producer, terminal)]))
                .Description;
        MethodDefinitionSourceRequest<TResult> request =
            MethodDefinitionSourceRequest<TResult>.Create(
                QueryRequest(terminal),
                work,
                producer,
                breadth ?? MethodDefinitionSourceBreadth.AllDefinitions,
                terminalWorkLimits);
        return MethodDefinitionSourceAssociation.Create(request);
    }

    static MethodDefinitionSourceRequestSetPlan AcceptedPlan(
        MethodDefinitionSourceAssociation[] associations) =>
        Assert.IsType<
                MethodDefinitionSourceRequestSetPlanResult.Accepted>(
                    MethodDefinitionSourceRequestSet.Plan(
                        MethodDefinitionSourceResourceIdentity.Create(),
                        associations))
            .Plan;

    static MethodDefinitionSourceRequestSetExecution Execute(
        MethodDefinitionSourceRequestSetPlan plan)
    {
        string path =
            typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using PdbContext context =
            PdbContext.OpenMetadataOnly(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(
                path,
                plan);
        return Assert.IsType<
                AssemblyAnalysisRequestSetServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
            .Execution;
    }

    static MethodDefinitionSourceRequestSetExecution Execute(
        MethodDefinitionSourceRequestSetPlan plan,
        ImmutableArray<byte> image)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    image.ToArray(),
                    writable: false));
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(
                "RequestSet.dll",
                plan);
        return Assert.IsType<
                AssemblyAnalysisRequestSetServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
            .Execution;
    }

    static ImmutableArray<byte> TwoMethodsInExcludedTypeWithFirstBody(
        ReadOnlySpan<byte> firstMethodBody)
    {
        byte[] bytes = MetadataMethodPtrFixture.Build(1, 2);
        int searchStart = 0;
        int replacements = 0;
        while (bytes.AsSpan(searchStart).IndexOf("Fixture\0"u8)
            is int relative
            && relative >= 0)
        {
            int match = searchStart + relative;
            "<Scope>\0"u8.CopyTo(bytes.AsSpan(match));
            replacements++;
            searchStart = match + "<Scope>\0"u8.Length;
        }
        Assert.True(replacements > 0);

        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        var reader = peReader.GetMetadataReader();
        int rva = reader
            .GetMethodDefinition(reader.MethodDefinitions.First())
            .RelativeVirtualAddress;
        var section = peReader.PEHeaders.SectionHeaders.Single(
            header => rva >= header.VirtualAddress
                && rva < header.VirtualAddress + header.VirtualSize);
        firstMethodBody.CopyTo(
            bytes.AsSpan(
                rva - section.VirtualAddress + section.PointerToRawData));
        return ImmutableArray.Create(bytes);
    }

    static int ValueOf(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association) =>
        ResultOf(execution, association).Value;

    static ProducerResult<int> ResultOf(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association) =>
        ResultOf<int>(execution, association);

    static ProducerResult<TResult> ResultOf<TResult>(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association) =>
        execution.ResultOf(
            association,
            Assert.IsType<MethodDefinitionSourceRequest<TResult>>(
                association.Request));

    static QuerySpaceRequest QueryRequest(
        ProducerTerminal terminal) =>
        QuerySpaceRequest.Create(
            QueryDescriptor,
            PortableQueryIntent.Empty,
            [MethodsRowSet],
            [],
            terminal switch
            {
                ProducerTerminal.Rows =>
                    QuerySpaceTerminalRequirement.Rows,
                ProducerTerminal.Count =>
                    QuerySpaceTerminalRequirement.Count,
                ProducerTerminal.Exists =>
                    QuerySpaceTerminalRequirement.Exists,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(terminal)),
            });

    const string MethodsRowSet = "methods";

    static readonly QueryOperationDefinition<
        EmptyPredicate,
        EmptyPlan> Operation =
            QueryOperationDefinition<
                EmptyPredicate,
                EmptyPlan>.Create(
                    "test.method-request-set",
                    new EmptyVocabulary(),
                    ["managed-assembly"],
                    ["method"],
                    [MethodsRowSet],
                    [],
                    [],
                    [new("default", [], [])]);

    static readonly QueryOperationRoute<
        EmptyPredicate,
        EmptyPlan> Route =
            QueryOperationRoute<
                EmptyPredicate,
                EmptyPlan>.Create(
                    "test.method-request-set/default",
                    Operation,
                    "managed-assembly",
                    "method",
                    [MethodsRowSet],
                    "default",
                    [],
                    []);

    static readonly QuerySpaceDescriptor QueryDescriptor =
        QuerySpaceDescriptor.Create(
            "test.method-request-set/query-space/v1",
            Route,
            [
                new QuerySpaceRowScopeDescriptor(
                    "test.method-request-set/methods/v1",
                    "test.method-request-set/method-rows/v1",
                    [MethodsRowSet],
                    [],
                    [],
                    []),
            ],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
                QuerySpaceTerminalRequirement.Exists,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "test.method-request-set/rows/v1"),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    "test.method-request-set/count/v1"),
                new(
                    QuerySpaceTerminalRequirement.Exists,
                    "test.method-request-set/exists/v1"),
            ]);

    readonly record struct EmptyPredicate;

    sealed record EmptyPlan;

    sealed class EmptyVocabulary
        : PortableQueryVocabulary<EmptyPredicate, EmptyPlan>
    {
        public override string Identity =>
            "test.method-request-set/operation/v1";

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<EmptyPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<EmptyPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override bool CollapsesDuplicateBindings => true;

        public override bool AreTermsCompatible(
            PortableQueryResolvedTerm<EmptyPredicate> first,
            PortableQueryResolvedTerm<EmptyPredicate> second) =>
            true;

        public override EmptyPlan CreatePlan(
            PortableQueryResolvedIntent<EmptyPredicate> resolved) =>
            new();
    }

    sealed class CountingProducer
        : MethodDefinitionProducer<bool, int, int>
    {
        CountingProducer()
            : base(
                "Test.MethodRequestSet.Counting",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static CountingProducer Instance { get; } = new();

        internal override bool Visit(
            scoped MethodDefinitionView view) =>
            true;

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            bool fact) =>
            fact ? accumulator + 1 : accumulator;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool Settles(bool fact) => fact;
    }

    sealed class SettlingProducer
        : MethodDefinitionProducer<bool, int, int>
    {
        SettlingProducer()
            : base(
                "Test.MethodRequestSet.Settling",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static SettlingProducer Instance { get; } = new();

        internal override bool Visit(
            scoped MethodDefinitionView view) =>
            true;

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            bool fact) =>
            fact ? accumulator + 1 : accumulator;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool Settles(bool fact) => fact;
    }

    sealed class AbortingProducer
        : MethodDefinitionProducer<bool, int, int>
    {
        AbortingProducer()
            : base(
                "Test.MethodRequestSet.Aborting",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static AbortingProducer Instance { get; } = new();

        internal override bool Visit(
            scoped MethodDefinitionView view)
        {
            if (view.Ordinal > 0)
            {
                throw new ProducerAbortException(
                    new(
                        Identity,
                        "TestBudget",
                        view.Token,
                        $"MethodDef 0x{view.Token:X8}",
                        "Test request-set abort."));
            }

            return false;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            bool fact) =>
            fact ? accumulator + 1 : accumulator;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class BodyReadingProducer
        : MethodDefinitionProducer<int, int, int>
    {
        BodyReadingProducer()
            : base(
                "Test.MethodRequestSet.BodyReading",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration
                    | MethodDefinitionLayers.Body)
        {
        }

        public static BodyReadingProducer Instance { get; } =
            new();

        internal override int Visit(
            scoped MethodDefinitionView view) =>
            view.HasManagedBody
                ? view.GetBody().Size
                : 0;

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class RetainedInstructionProducer
        : MethodDefinitionProducer<int, int, int>
    {
        RetainedInstructionProducer()
            : base(
                "RetainedInstruction",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Body)
        {
        }

        public static RetainedInstructionProducer Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [MethodBodyAnalyzerDeclarations.BoundedFlow];

        internal override int Visit(scoped MethodDefinitionView view)
        {
            if (!view.HasManagedBody)
                return 0;

            int instructions = 0;
            view.VisitInstructionShapes(
                ref instructions,
                static (
                    ref int count,
                    ILOpCode _,
                    int _) =>
                {
                    count++;
                    return true;
                });
            return instructions;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class PrefixRetainedInstructionProducer
        : MethodDefinitionProducer<int, int, int>
    {
        PrefixRetainedInstructionProducer()
            : base(
                "PrefixRetainedInstruction",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Body)
        {
        }

        public static PrefixRetainedInstructionProducer Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [MethodBodyAnalyzerDeclarations.BoundedFlow];

        internal override int Visit(scoped MethodDefinitionView view)
        {
            if (!view.HasManagedBody)
                return 0;

            int instructions = 0;
            view.VisitInstructionShapes(
                ref instructions,
                static (
                    ref int count,
                    ILOpCode _,
                    int _) =>
                {
                    count++;
                    return false;
                });
            return instructions;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class MaterializedInstructionProducer
        : MethodDefinitionProducer<int, int, int>
    {
        MaterializedInstructionProducer()
            : base(
                "MaterializedInstruction",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Body)
        {
        }

        public static MaterializedInstructionProducer Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [MethodBodyAnalyzerDeclarations.BoundedFlow];

        internal override int Visit(scoped MethodDefinitionView view)
        {
            if (!view.HasManagedBody)
                return 0;

            MethodInstructions instructions =
                view.MaterializeInstructions(out MethodBodyData _);
            if (!instructions.IsComplete)
            {
                throw new BadImageFormatException(
                    instructions.Blocks.IncompleteReason);
            }
            return instructions.Instructions.Length;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override bool Settles(int fact) => fact > 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class FusedClassificationProducer
        : MethodDefinitionProducer<int, int, int>
    {
        FusedClassificationProducer()
            : base(
                "Test.FusedInstructionClassifier",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Body)
        {
        }

        public static FusedClassificationProducer Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [MethodBodyAnalyzerDeclarations.ThrowPresence];

        internal override MethodDefinitionExecution.ProducerState
            CreateState(
                MethodDefinitionExecution execution,
                ProducerTerminal terminal,
                int? rowLimit,
                ImmutableArray<int> dependencies,
                UnitFactRetention retention) =>
            new FusedState(
                this,
                execution,
                terminal,
                rowLimit,
                dependencies,
                retention);

        internal override int Visit(
            scoped MethodDefinitionView view)
        {
            if (!view.HasManagedBody)
                return 1;

            int ignored = 0;
            view.VisitInstructionShapes(
                ref ignored,
                static (
                    ref int state,
                    ILOpCode opcode,
                    int encodedLength) =>
                {
                    _ = state;
                    _ = opcode;
                    _ = encodedLength;
                    return true;
                });
            return 0;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            int fact) =>
            accumulator + 1;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool ClassifiesUnits => true;

        internal override int UnitClass(int fact) => fact;

        sealed class FusedState : State
        {
            int _methodToken;

            public FusedState(
                FusedClassificationProducer producer,
                MethodDefinitionExecution execution,
                ProducerTerminal terminal,
                int? rowLimit,
                ImmutableArray<int> dependencies,
                UnitFactRetention retention)
                : base(
                    producer,
                    execution,
                    terminal,
                    rowLimit,
                    dependencies,
                    retention)
            {
            }

            public override bool SupportsFusedInstructionShapes =>
                true;

            public override bool TryBeginInstructionShapes(
                scoped MethodDefinitionView view,
                out MethodBodyBlock? body,
                out bool settled)
            {
                _methodToken = view.Token;
                if (!view.HasManagedBody)
                {
                    body = null;
                    settled = AcceptFact(view.Token, 1);
                    return false;
                }

                body = view.GetBody();
                settled = false;
                return true;
            }

            public override bool VisitInstructionShape(
                ILOpCode opcode,
                int encodedLength)
            {
                _ = opcode;
                _ = encodedLength;
                return true;
            }

            public override bool CompleteInstructionShapes(
                Exception? failure)
            {
                if (failure is not null)
                    throw failure;
                return AcceptFact(_methodToken, 0);
            }
        }
    }

    sealed class GuardedCountProducer
        : MethodDefinitionProducer<int, int, int>
    {
        public GuardedCountProducer(
            FusedClassificationProducer classifier)
            : base(
                "Test.GuardedFusedInstructionCount",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Declaration,
                () =>
                [
                    new ProducerDependency(
                        classifier,
                        ProducerDependencyKind.VisitNeedsVisit,
                        AcceptedUnitClasses: 1),
                ])
        {
        }

        internal override int Visit(
            scoped MethodDefinitionView view) =>
            1;

        internal override int Seed() => 0;

        internal override int Accumulate(
            int accumulator,
            int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }
}
