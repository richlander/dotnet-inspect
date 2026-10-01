using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace ILInspector.Analysis.Tests;

public partial class UnsafeEvidencePresenceTests
{
    [Fact]
    public void AssemblyAnalysisOperation_PlanningDoesNotOpenSubject()
    {
        string nonexistent = Path.Combine(
            Path.GetTempPath(),
            $"assembly-analysis-{Guid.NewGuid():N}",
            "missing.dll");

        AssemblyAnalysisOperation operation =
            CreateOperation(nonexistent);

        Assert.Equal(nonexistent, operation.SourceName);
        Assert.Same(
            UnsafeEvidencePresence.Description,
            operation.PlanOf(RequestOf(operation)).Work);
    }

    [Fact]
    public void AssemblyAnalysisService_BindsExactOperationAndSubject()
    {
        AssemblyAnalysisOperation operation =
            CreateOperation("ExactSubject.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        var observed = session.SnapshotOperation(
            operation,
            access =>
                (
                    access.Subject,
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access)));

        var completed = Assert.IsType<
            AssemblyAnalysisServiceResult.Completed>(
                observed.Item2);
        Assert.Same(operation, completed.Execution.Operation);
        Assert.Same(observed.Subject, completed.Execution.Subject);
    }

    [Fact]
    public void AssemblyAnalysisService_RejectsMismatchedOperationAccess()
    {
        AssemblyAnalysisOperation issued =
            CreateOperation("Issued.dll");
        AssemblyAnalysisOperation foreign =
            CreateOperation("Foreign.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local));

        AssemblyAnalysisServiceResult result =
            session.SnapshotOperation(
                issued,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        foreign,
                        access));

        var rejected = Assert.IsType<
            AssemblyAnalysisServiceResult.Rejected>(result);
        Assert.Equal(
            AssemblyAnalysisRejectionKind.OperationAccessMismatch,
            rejected.Kind);
    }

    [Fact]
    public void
        AssemblyAnalysisService_PreservesSourceFailureAndCompletion()
    {
        AssemblyAnalysisOperation operation =
            CreateOperation("SourceCompletion.dll");
        byte[] noMetadata = RemoveManagedMetadata(
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local));
        using (AssemblyInspectionSession unavailable =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(noMetadata, writable: false)))
        {
            AssemblyAnalysisServiceResult rejected =
                unavailable.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));

            Assert.Equal(
                AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable,
                Assert.IsType<
                        AssemblyAnalysisServiceResult.Rejected>(
                            rejected)
                    .Kind);
        }

        using AssemblyInspectionSession available =
            OpenSession(BuildCustomModifiedPointerLocalAssembly());
        AssemblyAnalysisServiceResult completedResult =
            available.SnapshotOperation(
                operation,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
        AssemblyAnalysisExecution execution =
            Assert.IsType<AssemblyAnalysisServiceResult.Completed>(
                    completedResult)
                .Execution;

        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.SourceReceiptOf(RequestOf(operation)).Completion);
        Assert.True(
            execution.SourceReceiptOf(RequestOf(operation))
                .DefinitionsVisited > 0);
    }

    [Fact]
    public void AssemblyAnalysisService_PreservesProducerOutcomes()
    {
        AssemblyAnalysisOperation operation =
            CreateOperation("IncompleteThenEvidence.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        AssemblyAnalysisExecution execution =
            Assert.IsType<AssemblyAnalysisServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;
        ProducerResult<int> result =
            execution.ResultOf(RequestOf(operation));
        ProducerParticipation participation =
            execution.WorkReceiptOf(RequestOf(operation)).For(
                UnsafeEvidencePresenceProducer.Instance);

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Equal(ProducerOutcome.Failed, participation.Outcome);
        Assert.Equal(1, participation.UnitsAttempted);
        Assert.Equal(1, participation.UnitsFailed);
    }

    [Fact]
    public void
        AssemblyAnalysisExecution_ContainsNoLiveSubjectAuthority()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"assembly-analysis-detached-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            BuildCustomModifiedPointerLocalAssembly().AsSpan());
        try
        {
            AssemblyAnalysisOperation operation =
                CreateOperation(path);
            AssemblyAnalysisExecution execution;
            AssemblyInspectionSubjectIdentity subject;
            using (PdbContext context = PdbContext.OpenMetadataOnly(path))
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Borrow(context);
                var observed = session.SnapshotOperation(
                    operation,
                    access =>
                        (
                            access.Subject,
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)));
                subject = observed.Subject;
                execution = Assert.IsType<
                        AssemblyAnalysisServiceResult.Completed>(
                            observed.Item2)
                    .Execution;
            }

            Assert.Same(operation, execution.Operation);
            Assert.Same(subject, execution.Subject);
            Assert.Same(
                RequestOf(operation).Identity,
                execution.SourceReceiptOf(RequestOf(operation)).Request);
            Assert.Equal(
                MethodDefinitionSourceCompletion.Satisfied,
                execution.SourceReceiptOf(RequestOf(operation)).Completion);
            Assert.True(
                execution.SourceReceiptOf(RequestOf(operation))
                    .DefinitionsVisited > 0);
            Assert.Equal(
                new ProducerResult<int>(
                    ProducerOutcome.Stopped,
                    1),
                execution.ResultOf(RequestOf(operation)));
            Assert.Equal(
                ProducerOutcome.Stopped,
                execution.WorkReceiptOf(RequestOf(operation)).For(
                        UnsafeEvidencePresenceProducer.Instance)
                    .Outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        AssemblyAnalysisService_SequentialReferenceMatchesInterimExecutor()
    {
        AssemblyAnalysisOperation operation =
            CreateOperation("ReferenceMatch.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        var observed = session.SnapshotOperation(
            operation,
            access =>
            {
                AssemblyAnalysisServiceResult service =
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access);
                MethodDefinitionExecution reference =
                    access.InspectImage(
                        peReader =>
                            UnsafeEvidencePresence.Execute(
                                operation.SourceName,
                                peReader,
                                operation.PlanOf(RequestOf(operation)).Work));
                return (service, reference);
            });
        AssemblyAnalysisExecution execution =
            Assert.IsType<AssemblyAnalysisServiceResult.Completed>(
                    observed.service)
                .Execution;
        ProducerResult<int> actual =
            execution.ResultOf(RequestOf(operation));
        ProducerResult<int> expected =
            observed.reference.ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerParticipation actualParticipation =
            execution.WorkReceiptOf(RequestOf(operation)).For(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerParticipation expectedParticipation =
            observed.reference.Receipt.For(
                UnsafeEvidencePresenceProducer.Instance);

        Assert.Equal(expected, actual);
        Assert.Equal(
            observed.reference.Receipt.UnitsVisited,
            execution.WorkReceiptOf(RequestOf(operation)).UnitsVisited);
        Assert.Equal(
            expectedParticipation.Producer,
            actualParticipation.Producer);
        Assert.Equal(
            expectedParticipation.Outcome,
            actualParticipation.Outcome);
        Assert.Equal(
            expectedParticipation.UnitsAttempted,
            actualParticipation.UnitsAttempted);
        Assert.Equal(
            expectedParticipation.UnitsCompleted,
            actualParticipation.UnitsCompleted);
        Assert.Equal(
            expectedParticipation.UnitsFailed,
            actualParticipation.UnitsFailed);
        Assert.Equal(
            expectedParticipation.Layers.ToArray(),
            actualParticipation.Layers.ToArray());
    }

    [Fact]
    public void
        AssemblyAnalysisOperation_PreservesOwnerIssuedSourceKinds()
    {
        MethodDefinitionSourceRequest<int> request =
            MethodDefinitionSourceRequest<int>.Create(
                UnsafeEvidencePresenceQuery.CreateRequest(),
                UnsafeEvidencePresence.Description,
                UnsafeEvidencePresenceProducer.Instance);
        AssemblyAnalysisOperation operation =
            AssemblyAnalysisOperation.Create(
                "Sources.dll",
                request);

        Assert.Equal(
            [AssemblyAnalysisSourceKind.MethodDefinitions],
            operation.SourceKinds);
        Assert.Equal(
            [request],
            Assert.Single(operation.MethodDefinitions).Requests);
        Assert.Equal(ProducerTerminal.Exists, request.Terminal);
        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup,
            request.DeclaredLayers);
    }

    [Fact]
    public void SettledRequestSurvivesLaterSharedFailure()
    {
        var settling = new SettlingReferenceProducer();
        var failing = new CompletionFailingReferenceProducer();
        MethodDefinitionSourceBinding binding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> settledRequest =
            CreateSourceRequest(
                binding,
                settling,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<int> failedRequest =
            CreateSourceRequest(
                binding,
                failing,
                ProducerTerminal.Rows);
        AssemblyAnalysisOperation operation =
            AssemblyAnalysisOperation.Create(
                "Collapsed.dll",
                settledRequest,
                failedRequest);
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        AssemblyAnalysisExecution execution =
            Assert.IsType<AssemblyAnalysisServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;

        Assert.Equal(2, operation.RequestSet.Associations.Length);
        Assert.Single(operation.RequestSet.Groups);
        Assert.Equal(
            [settledRequest.Association, failedRequest.Association],
            execution.SourceReceipts.Select(
                static receipt => receipt.Association));
        Assert.Equal(
            new ProducerResult<int>(
                ProducerOutcome.Stopped,
                1),
            execution.ResultOf(settledRequest));
        Assert.Equal(
            ProducerOutcome.Failed,
            execution.ResultOf(failedRequest).Outcome);
        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.SourceReceiptOf(settledRequest).Completion);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            execution.SourceReceiptOf(failedRequest).Completion);
        Assert.Same(
            settledRequest.Association,
            execution.SourceReceiptOf(settledRequest).Association);
        Assert.Same(
            failedRequest.Association,
            execution.SourceReceiptOf(failedRequest).Association);
        Assert.Equal(
            QuerySpaceRequestSatisfaction.CoveringRead,
            execution.SourceReceiptOf(settledRequest).Satisfaction);
        Assert.Same(
            operation.PlanOf(settledRequest).Resource,
            execution.SourceReceiptOf(settledRequest).Resource);
        Assert.Same(
            operation.PlanOf(settledRequest).Source,
            execution.SourceReceiptOf(settledRequest).Source);
        Assert.Equal(
            1,
            execution.WorkReceiptOf(settledRequest)
                .For(settling)
                .UnitsAttempted);
        Assert.True(
            execution.WorkReceiptOf(failedRequest)
                .For(failing)
                .UnitsAttempted > 1);
        Assert.True(
            execution.WorkReceiptOf(settledRequest).UnitsVisited > 1);
        Assert.Single(execution.WorkReceipts);
    }

    [Fact]
    public void CoveringReadRequiresOwnerIdentityAndAcceptedCompletion()
    {
        var failing = new CompletionFailingReferenceProducer();
        MethodDefinitionSourceRequest<int> request =
            CreateSourceRequest(
                MethodDefinitionSourceBinding.Create(),
                failing,
                ProducerTerminal.Rows);

        (AssemblyAnalysisOperation operation,
            AssemblyAnalysisExecution execution) =
                ExecuteRequests(request);
        MethodDefinitionSourceReceipt receipt =
            execution.SourceReceiptOf(request);

        Assert.Same(operation.PlanOf(request).Resource, receipt.Resource);
        Assert.Same(operation.PlanOf(request).Source, receipt.Source);
        Assert.Equal(
            QuerySpaceRequestSatisfaction.CoveringRead,
            receipt.Satisfaction);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            receipt.Completion);
        Assert.Equal(
            ProducerOutcome.Failed,
            execution.ResultOf(request).Outcome);
    }

    [Fact]
    public void CollapsePreservesIndependentReferenceResults()
    {
        var producer = new SettlingReferenceProducer();
        MethodDefinitionSourceRequest<int> first =
            CreateSourceRequest(
                MethodDefinitionSourceBinding.Create(),
                producer,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<int> second =
            CreateSourceRequest(
                MethodDefinitionSourceBinding.Create(),
                producer,
                ProducerTerminal.Exists);
        MethodDefinitionSourceBinding sharedBinding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> sharedFirst =
            CreateSourceRequest(
                sharedBinding,
                producer,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<int> sharedSecond =
            CreateSourceRequest(
                sharedBinding,
                producer,
                ProducerTerminal.Exists);

        (AssemblyAnalysisOperation independentOperation,
            AssemblyAnalysisExecution independentExecution) =
                ExecuteRequests(first, second);
        (AssemblyAnalysisOperation sharedOperation,
            AssemblyAnalysisExecution sharedExecution) =
                ExecuteRequests(sharedFirst, sharedSecond);

        Assert.Equal(2, independentOperation.RequestSet.Groups.Length);
        Assert.Equal(2, independentExecution.WorkReceipts.Length);
        Assert.Single(sharedOperation.RequestSet.Groups);
        Assert.Single(sharedExecution.WorkReceipts);
        Assert.Equal(
            independentExecution.ResultOf(first),
            sharedExecution.ResultOf(sharedFirst));
        Assert.Equal(
            independentExecution.ResultOf(second),
            sharedExecution.ResultOf(sharedSecond));
        Assert.Equal(
            independentExecution.SourceReceiptOf(first).Completion,
            sharedExecution.SourceReceiptOf(sharedFirst).Completion);
        Assert.Equal(
            independentExecution.SourceReceiptOf(second).Completion,
            sharedExecution.SourceReceiptOf(sharedSecond).Completion);
    }

    [Fact]
    public void
        CollapsePreservesSettledResultAcrossLaterSourceEnumeration()
    {
        byte[] image = MetadataMethodPtrFixture.BuildTrailingOutOfRange(
            [0x0A, 0xFE, 0x0F]);
        MethodDefinitionSourceRequest<int> independentUnsafe =
            CreateSourceRequest(
                MethodDefinitionSourceBinding.Create(),
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<
            ClosedQueryResult<ClassifiedMethodRow>> independentPInvoke =
                CreateSourceRequest(
                    MethodDefinitionSourceBinding.Create(),
                    PInvokeAnalyzer.Instance,
                    ProducerTerminal.Rows);
        MethodDefinitionSourceBinding sharedBinding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> sharedUnsafe =
            CreateSourceRequest(
                sharedBinding,
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<
            ClosedQueryResult<ClassifiedMethodRow>> sharedPInvoke =
                CreateSourceRequest(
                    sharedBinding,
                    PInvokeAnalyzer.Instance,
                    ProducerTerminal.Rows);

        (_, AssemblyAnalysisExecution independent) =
            ExecuteRequests(
                ImmutableArray.Create(image),
                independentUnsafe,
                independentPInvoke);
        (_, AssemblyAnalysisExecution shared) =
            ExecuteRequests(
                ImmutableArray.Create(image),
                sharedUnsafe,
                sharedPInvoke);

        Assert.Equal(
            independent.ResultOf(independentUnsafe),
            shared.ResultOf(sharedUnsafe));
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            independentPInvokeResult =
                independent.ResultOf(independentPInvoke);
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            sharedPInvokeResult =
                shared.ResultOf(sharedPInvoke);
        Assert.Equal(
            independentPInvokeResult.Outcome,
            sharedPInvokeResult.Outcome);
        Assert.Equal(
            independentPInvokeResult.Value!.Count,
            sharedPInvokeResult.Value!.Count);
        Assert.Equal(
            independentPInvokeResult.Value!.Rows.ToArray(),
            sharedPInvokeResult.Value!.Rows.ToArray());
        Assert.Equal(
            independent.SourceReceiptOf(independentUnsafe).Completion,
            shared.SourceReceiptOf(sharedUnsafe).Completion);
        Assert.Equal(
            independent.SourceReceiptOf(independentPInvoke).Completion,
            shared.SourceReceiptOf(sharedPInvoke).Completion);
        Assert.Equal(
            ProducerOutcome.Stopped,
            shared.ResultOf(sharedUnsafe).Outcome);
        Assert.Empty(sharedPInvokeResult.Value!.Rows);
    }

    [Fact]
    public void CollapsePreservesTypedRowsAcrossDifferentTypeScopes()
    {
        ImmutableArray<byte> image =
            [.. File.ReadAllBytes(
                FixtureCatalog.AnalysisRequestSetCollapse.AssemblyPath())];
        MethodDefinitionSourceRequest<int> independentUnsafe =
            CreateSourceRequest(
                MethodDefinitionSourceBinding.Create(),
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<
            ClosedQueryResult<ClassifiedMethodRow>> independentPInvoke =
                CreateSourceRequest(
                    MethodDefinitionSourceBinding.Create(),
                    PInvokeAnalyzer.Instance,
                    ProducerTerminal.Rows);
        MethodDefinitionSourceBinding sharedBinding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> sharedUnsafe =
            CreateSourceRequest(
                sharedBinding,
                UnsafeEvidencePresenceProducer.Instance,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<
            ClosedQueryResult<ClassifiedMethodRow>> sharedPInvoke =
                CreateSourceRequest(
                    sharedBinding,
                    PInvokeAnalyzer.Instance,
                    ProducerTerminal.Rows);

        (_, AssemblyAnalysisExecution independent) =
            ExecuteRequests(
                image,
                independentUnsafe,
                independentPInvoke);
        (_, AssemblyAnalysisExecution shared) =
            ExecuteRequests(
                image,
                sharedUnsafe,
                sharedPInvoke);

        Assert.Equal(
            independent.ResultOf(independentUnsafe),
            shared.ResultOf(sharedUnsafe));
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            independentPInvokeResult =
                independent.ResultOf(independentPInvoke);
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>>
            sharedPInvokeResult =
                shared.ResultOf(sharedPInvoke);
        Assert.Equal(
            independentPInvokeResult.Outcome,
            sharedPInvokeResult.Outcome);
        Assert.Equal(
            independentPInvokeResult.Value!.Rows.ToArray(),
            sharedPInvokeResult.Value!.Rows.ToArray());
        Assert.Single(sharedPInvokeResult.Value!.Rows);
    }

    [Fact]
    public void RequestSetPublishesEveryAssociationExactlyOnce()
    {
        var producer = new SettlingReferenceProducer();
        MethodDefinitionSourceBinding binding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> first =
            CreateSourceRequest(
                binding,
                producer,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<int> second =
            CreateSourceRequest(
                binding,
                producer,
                ProducerTerminal.Exists);

        (AssemblyAnalysisOperation operation,
            AssemblyAnalysisExecution execution) =
                ExecuteRequests(first, second);

        Assert.Single(operation.RequestSet.Groups);
        Assert.Equal(
            [first.Association, second.Association],
            execution.SourceReceipts.Select(
                static receipt => receipt.Association));
        Assert.Equal(
            new ProducerResult<int>(ProducerOutcome.Stopped, 1),
            execution.ResultOf(first));
        Assert.Equal(
            execution.ResultOf(first),
            execution.ResultOf(second));
    }

    [Fact]
    public void SharedWorkReceiptDoesNotDoubleCharge()
    {
        var producer = new SettlingReferenceProducer();
        MethodDefinitionSourceBinding binding =
            MethodDefinitionSourceBinding.Create();
        MethodDefinitionSourceRequest<int> first =
            CreateSourceRequest(
                binding,
                producer,
                ProducerTerminal.Exists);
        MethodDefinitionSourceRequest<int> second =
            CreateSourceRequest(
                binding,
                producer,
                ProducerTerminal.Exists);

        (_, AssemblyAnalysisExecution execution) =
            ExecuteRequests(first, second);

        MethodDefinitionSourceGroupReceipt physical =
            Assert.Single(execution.WorkReceipts);
        Assert.Equal(1, physical.Work.UnitsVisited);
        Assert.Equal(
            1,
            execution.SourceReceiptOf(first).DefinitionsVisited);
        Assert.Equal(
            1,
            execution.SourceReceiptOf(second).DefinitionsVisited);
        Assert.Same(
            execution.WorkReceiptOf(first),
            execution.WorkReceiptOf(second));
    }

    static AssemblyAnalysisOperation CreateOperation(
        string sourceName)
    {
        MethodDefinitionSourceRequest<int> source =
            MethodDefinitionSourceRequest<int>.Create(
                UnsafeEvidencePresenceQuery.CreateRequest(),
                UnsafeEvidencePresence.Description,
                UnsafeEvidencePresenceProducer.Instance);
        return AssemblyAnalysisOperation.Create(
            sourceName,
            source);
    }

    static MethodDefinitionSourceRequest<TResult> CreateSourceRequest<TResult>(
        MethodDefinitionSourceBinding binding,
        ProducerDeclaration<TResult> producer,
        ProducerTerminal terminal)
    {
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan([new(producer, terminal)]))
                .Description;
        return MethodDefinitionSourceRequest<TResult>.Create(
            CreateReferenceRequest(terminal),
            binding,
            work,
            producer);
    }

    static QuerySpaceRequest CreateReferenceRequest(
        ProducerTerminal terminal)
    {
        QuerySpaceTerminalRequirement queryTerminal = terminal switch
        {
            ProducerTerminal.Exists =>
                QuerySpaceTerminalRequirement.Exists,
            ProducerTerminal.Rows =>
                QuerySpaceTerminalRequirement.Rows,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "The reference fixture supports Exists and Rows."),
        };
        QuerySpaceBinding binding = UnsafeEvidencePresenceQuery.QuerySpace;
        QuerySpaceDescriptor descriptor =
            QuerySpaceDescriptor.Create(
                "assembly-analysis-reference/query-space/v1",
                binding.Operation,
                [.. binding.RowScopes.Select(
                    static scope => scope.Descriptor)],
                [
                    QuerySpaceTerminalRequirement.Exists,
                    QuerySpaceTerminalRequirement.Rows,
                ],
                acceptsContinuation: false,
                [
                    new(
                        QuerySpaceTerminalRequirement.Exists,
                        "assembly-analysis-reference/exists/v1"),
                    new(
                        QuerySpaceTerminalRequirement.Rows,
                        "assembly-analysis-reference/rows/v1"),
                ]);
        return QuerySpaceRequest.Create(
            descriptor,
            UnsafeEvidencePresenceQuery.CreateRequest().Operation,
            [UnsafeEvidencePresenceQuery.MethodDefinitionsRowSet],
            [],
            queryTerminal);
    }

    static (
        AssemblyAnalysisOperation Operation,
        AssemblyAnalysisExecution Execution)
        ExecuteRequests(
            params ReadOnlySpan<MethodDefinitionSourceRequest> requests)
        => ExecuteRequests(
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local,
                appendUnsafeBody: true),
            requests);

    static (
        AssemblyAnalysisOperation Operation,
        AssemblyAnalysisExecution Execution)
        ExecuteRequests(
            ImmutableArray<byte> image,
            params ReadOnlySpan<MethodDefinitionSourceRequest> requests)
    {
        AssemblyAnalysisOperation operation =
            AssemblyAnalysisOperation.Create(
                "RequestSet.dll",
                requests);
        using AssemblyInspectionSession session =
            OpenSession(image);
        AssemblyAnalysisExecution execution =
            Assert.IsType<AssemblyAnalysisServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;
        return (operation, execution);
    }

    static MethodDefinitionSourceRequest<int> RequestOf(
        AssemblyAnalysisOperation operation) =>
        Assert.IsType<MethodDefinitionSourceRequest<int>>(
            Assert.Single(
                Assert.Single(operation.MethodDefinitions).Requests));

    static AssemblyInspectionSession OpenSession(
        ImmutableArray<byte> image) =>
        AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image.ToArray(), writable: false));

    static byte[] RemoveManagedMetadata(
        ImmutableArray<byte> managedImage)
    {
        byte[] image = managedImage.ToArray();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    sealed class SettlingReferenceProducer()
        : MethodDefinitionProducer<int, int, int>(
            "AssemblyAnalysis.Reference.Settling",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;

        internal override bool Settles(int fact) => true;
    }

    sealed class CompletionFailingReferenceProducer()
        : MethodDefinitionProducer<int, int, int>(
            "AssemblyAnalysis.Reference.CompletionFailure",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Declaration)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            throw new BadImageFormatException(
                "Injected reference completion failure.");
    }
}
