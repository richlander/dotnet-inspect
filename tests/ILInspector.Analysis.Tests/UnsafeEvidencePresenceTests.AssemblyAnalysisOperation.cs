using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class UnsafeEvidencePresenceTests
{
    [Fact]
    public void MethodQuerySource_PlanningDoesNotReadSubject()
    {
        string nonexistent = Path.Combine(
            Path.GetTempPath(),
            $"method-source-{Guid.NewGuid():N}",
            "missing.dll");

        AssemblyAnalysisOperation<int> operation =
            CreateOperation(nonexistent);
        MethodDefinitionSourceRequest<int> request =
            operation.MethodDefinitions;

        Assert.Equal(nonexistent, operation.SourceName);
        Assert.Equal(
            MethodDefinitionSourceBreadth.AllDefinitions,
            request.Breadth);
        Assert.Equal(ProducerTerminal.Exists, request.Terminal);
        Assert.Same(
            UnsafeEvidencePresenceProducer.Instance,
            request.Producer);
    }

    [Fact]
    public void AssemblyAnalysisOperation_PlanningDoesNotOpenSubject()
    {
        string nonexistent = Path.Combine(
            Path.GetTempPath(),
            $"assembly-analysis-{Guid.NewGuid():N}",
            "missing.dll");

        AssemblyAnalysisOperation<int> operation =
            CreateOperation(nonexistent);

        Assert.Equal(nonexistent, operation.SourceName);
        Assert.Same(UnsafeEvidencePresence.Description, operation.Work);
    }

    [Fact]
    public void AssemblyAnalysisService_BindsExactOperationAndSubject()
    {
        AssemblyAnalysisOperation<int> operation =
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
            AssemblyAnalysisServiceResult<int>.Completed>(
                observed.Item2);
        Assert.Same(operation, completed.Execution.Operation);
        Assert.Same(observed.Subject, completed.Execution.Subject);
    }

    [Fact]
    public void MethodQuerySource_BindsExactPlanSubjectAndReceipt()
    {
        AssemblyAnalysisOperation<int> operation =
            CreateOperation("ExactMethodSource.dll");
        using AssemblyInspectionSession session =
            OpenSession(BuildCustomModifiedPointerLocalAssembly());

        var observed = session.SnapshotOperation(
            operation,
            access =>
            {
                AssemblyAnalysisExecution<int> execution =
                    Assert.IsType<
                            AssemblyAnalysisServiceResult<int>.Completed>(
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access))
                        .Execution;
                return (access.Subject, execution);
            });

        Assert.Same(
            operation.MethodDefinitions.Identity,
            observed.execution.SourceReceipt.Request);
        Assert.Same(
            observed.Subject,
            observed.execution.SourceReceipt.Subject);
        Assert.Equal(
            MethodDefinitionSourceBreadth.AllDefinitions,
            observed.execution.SourceReceipt.Breadth);
    }

    [Fact]
    public void AssemblyAnalysisService_RejectsMismatchedOperationAccess()
    {
        AssemblyAnalysisOperation<int> issued =
            CreateOperation("Issued.dll");
        AssemblyAnalysisOperation<int> foreign =
            CreateOperation("Foreign.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local));

        AssemblyAnalysisServiceResult<int> result =
            session.SnapshotOperation(
                issued,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        foreign,
                        access));

        var rejected = Assert.IsType<
            AssemblyAnalysisServiceResult<int>.Rejected>(result);
        Assert.Equal(
            AssemblyAnalysisRejectionKind.OperationAccessMismatch,
            rejected.Kind);
    }

    [Fact]
    public void
        AssemblyAnalysisService_PreservesSourceFailureAndCompletion()
    {
        AssemblyAnalysisOperation<int> operation =
            CreateOperation("SourceCompletion.dll");
        byte[] noMetadata = RemoveManagedMetadata(
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local));
        using (AssemblyInspectionSession unavailable =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(noMetadata, writable: false)))
        {
            AssemblyAnalysisServiceResult<int> rejected =
                unavailable.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));

            Assert.Equal(
                AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable,
                Assert.IsType<
                        AssemblyAnalysisServiceResult<int>.Rejected>(
                            rejected)
                    .Kind);
        }

        using AssemblyInspectionSession available =
            OpenSession(BuildCustomModifiedPointerLocalAssembly());
        AssemblyAnalysisServiceResult<int> completedResult =
            available.SnapshotOperation(
                operation,
                access =>
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    completedResult)
                .Execution;

        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.SourceReceipt.Completion);
        Assert.True(execution.SourceReceipt.DefinitionsVisited > 0);
    }

    [Fact]
    public void AssemblyAnalysisService_PreservesProducerOutcomes()
    {
        AssemblyAnalysisOperation<int> operation =
            CreateOperation("IncompleteThenEvidence.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;
        ProducerResult<int> result =
            execution.ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerParticipation participation =
            execution.WorkReceipt.For(
                UnsafeEvidencePresenceProducer.Instance);

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Equal(ProducerOutcome.Failed, participation.Outcome);
        Assert.Equal(1, participation.UnitsAttempted);
        Assert.Equal(1, participation.UnitsFailed);
    }

    [Fact]
    public void
        AssemblyAnalysisExecution_ContainsNoLiveSubjectAuthority()
        => AssertMethodSourceExecutionIsDetached();

    [Fact]
    public void
        MethodQuerySource_ReleasedExecutionRetainsNoSubjectAuthority()
        => AssertMethodSourceExecutionIsDetached();

    static void AssertMethodSourceExecutionIsDetached()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"assembly-analysis-detached-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            BuildCustomModifiedPointerLocalAssembly().AsSpan());
        try
        {
            AssemblyAnalysisOperation<int> operation =
                CreateOperation(path);
            AssemblyAnalysisExecution<int> execution;
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
                        AssemblyAnalysisServiceResult<int>.Completed>(
                            observed.Item2)
                    .Execution;
            }

            Assert.Same(operation, execution.Operation);
            Assert.Same(subject, execution.Subject);
            Assert.Same(
                operation.MethodDefinitions.Identity,
                execution.SourceReceipt.Request);
            Assert.Same(
                subject,
                execution.SourceReceipt.Subject);
            Assert.Equal(
                MethodDefinitionSourceCompletion.Satisfied,
                execution.SourceReceipt.Completion);
            Assert.True(execution.SourceReceipt.DefinitionsVisited > 0);
            Assert.Equal(
                new ProducerResult<int>(
                    ProducerOutcome.Stopped,
                    1),
                execution.ResultOf(
                    UnsafeEvidencePresenceProducer.Instance));
            Assert.Equal(
                ProducerOutcome.Stopped,
                execution.WorkReceipt.For(
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
        => AssertMethodSourceMatchesInterimExecutor();

    [Fact]
    public void
        MethodQuerySource_SequentialReferenceMatchesInterimExecutor()
        => AssertMethodSourceMatchesInterimExecutor();

    static void AssertMethodSourceMatchesInterimExecutor()
    {
        AssemblyAnalysisOperation<int> operation =
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
                AssemblyAnalysisServiceResult<int> service =
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access);
                MethodDefinitionExecution reference =
                    access.InspectImage(
                        peReader =>
                            UnsafeEvidencePresence.Execute(
                                operation.SourceName,
                                peReader,
                                operation.Work));
                return (service, reference);
            });
        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    observed.service)
                .Execution;
        ProducerResult<int> actual =
            execution.ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerResult<int> expected =
            observed.reference.ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerParticipation actualParticipation =
            execution.WorkReceipt.For(
                UnsafeEvidencePresenceProducer.Instance);
        ProducerParticipation expectedParticipation =
            observed.reference.Receipt.For(
                UnsafeEvidencePresenceProducer.Instance);

        Assert.Equal(expected, actual);
        Assert.Equal(
            observed.reference.Receipt.UnitsVisited,
            execution.WorkReceipt.UnitsVisited);
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
    public void MethodQuerySource_ExistsStopsAtFirstSettledMethod()
    {
        AssemblyAnalysisOperation<int> operation =
            CreateOperation("StopsAtFirstSettledMethod.dll");
        using AssemblyInspectionSession session =
            OpenSession(BuildSchedulingSensitiveAssembly());

        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;

        Assert.Equal(
            new ProducerResult<int>(
                ProducerOutcome.Stopped,
                1),
            execution.ResultOf(
                UnsafeEvidencePresenceProducer.Instance));
        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.SourceReceipt.Completion);
        Assert.Equal(1, execution.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, execution.SourceReceipt.BodiesAcquired);
    }

    [Fact]
    public void
        MethodQuerySource_ProducerFailureDoesNotBecomeSuccessfulAbsence()
    {
        AssemblyAnalysisOperation<int> operation =
            CreateOperation("ProducerFailure.dll");
        using AssemblyInspectionSession session =
            OpenSession(
                BuildGuardRejectedUnsafeAssembly(
                    GuardRejectedSignatureKind.Local,
                    appendUnsafeBody: true));

        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
                .Execution;
        ProducerResult<int> result =
            execution.ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            execution.SourceReceipt.Completion);
        Assert.Equal(1, execution.SourceReceipt.DefinitionsVisited);
    }

    [Fact]
    public void
        AssemblyAnalysisOperation_PreservesOwnerIssuedSourceKinds()
    {
        MethodDefinitionSourceRequest<int> request =
            MethodDefinitionSourceRequest<int>.Create(
                UnsafeEvidencePresence.Description,
                UnsafeEvidencePresenceProducer.Instance);
        AssemblyAnalysisOperation<int> operation =
            AssemblyAnalysisOperation<int>.Create(
                "Sources.dll",
                request);

        Assert.Equal(
            [AssemblyAnalysisSourceKind.MethodDefinitions],
            operation.SourceKinds);
        Assert.Same(request, operation.MethodDefinitions);
        Assert.Equal(
            MethodDefinitionSourceBreadth.AllDefinitions,
            request.Breadth);
        Assert.Equal(ProducerTerminal.Exists, request.Terminal);
        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup,
            request.DeclaredLayers);
    }

    static AssemblyAnalysisOperation<int> CreateOperation(
        string sourceName)
    {
        MethodDefinitionSourceRequest<int> source =
            MethodDefinitionSourceRequest<int>.Create(
                UnsafeEvidencePresence.Description,
                UnsafeEvidencePresenceProducer.Instance);
        return AssemblyAnalysisOperation<int>.Create(
            sourceName,
            source);
    }

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
}
