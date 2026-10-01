using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
    public void MethodQuerySource_NormalizesExactBreadthWithoutReadingSubject()
    {
        string nonexistent = Path.Combine(
            Path.GetTempPath(),
            $"method-source-{Guid.NewGuid():N}",
            "missing.dll");
        MethodDefinitionHandle method2 =
            MetadataTokens.MethodDefinitionHandle(2);
        MethodDefinitionHandle method9 =
            MetadataTokens.MethodDefinitionHandle(9);
        TypeDefinitionHandle type3 =
            MetadataTokens.TypeDefinitionHandle(3);
        TypeDefinitionHandle type7 =
            MetadataTokens.TypeDefinitionHandle(7);

        AssemblyAnalysisOperation<int> methods = CreateOperation(
            nonexistent,
            MethodDefinitionSourceBreadth.ExactMethods(
                method9,
                method2,
                method9));
        AssemblyAnalysisOperation<int> types = CreateOperation(
            nonexistent,
            MethodDefinitionSourceBreadth.ExactTypes(
                type7,
                type3,
                type7));

        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            methods.MethodDefinitions.Breadth.Kind);
        Assert.Equal(
            [method2, method9],
            methods.MethodDefinitions.Breadth.Methods);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactTypes,
            types.MethodDefinitions.Breadth.Kind);
        Assert.Equal(
            [type3, type7],
            types.MethodDefinitions.Breadth.Types);
        Assert.Throws<ArgumentException>(
            () => MethodDefinitionSourceBreadth.ExactMethods(
                default(MethodDefinitionHandle)));
        Assert.Throws<ArgumentException>(
            () => MethodDefinitionSourceBreadth.ExactTypes(
                default(TypeDefinitionHandle)));
    }

    [Fact]
    public void MethodQuerySource_EmptyExactSeedsRemainEmptyPopulations()
    {
        ImmutableArray<byte> image =
            BuildCustomModifiedPointerLocalAssembly();
        foreach (MethodDefinitionSourceBreadth breadth in
            new[]
            {
                MethodDefinitionSourceBreadth.ExactMethods(),
                MethodDefinitionSourceBreadth.ExactTypes(),
            })
        {
            AssemblyAnalysisOperation<int> operation =
                CreateOperation("EmptyExactPopulation.dll", breadth);
            using AssemblyInspectionSession session = OpenSession(image);

            AssemblyAnalysisExecution<int> execution =
                Execute(session, operation);

            Assert.Equal(
                ProducerOutcome.Complete,
                execution.ResultOf(
                        UnsafeEvidencePresenceProducer.Instance)
                    .Outcome);
            Assert.Equal(
                MethodDefinitionSourceCompletion.Exhausted,
                execution.SourceReceipt.Completion);
            Assert.Empty(
                execution.SourceReceipt.Coverage
                    .DefinitionsExamined.Ranges);
            Assert.Empty(
                execution.SourceReceipt.Coverage
                    .MethodsSelected.Ranges);
            Assert.Empty(
                execution.SourceReceipt.Coverage
                    .BodiesAcquired.Ranges);
            Assert.Equal(0, execution.SourceReceipt.ModuleLookups);
        }
    }

    [Fact]
    public void
        MethodQuerySource_ExactMethodBreadthVisitsOnlySelectedMethods()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "packages",
            "System.Text.Json.10.0.0.dll");
        JsonDocumentCoordinates coordinates =
            ReadJsonDocumentCoordinates(path);
        WorkDescription complete = CompleteUnsafeEvidenceDescription();
        MethodDefinitionHandle first = coordinates.ParseMethods[0];
        MethodDefinitionHandle last =
            coordinates.ParseMethods[^1];
        ImmutableArray<MethodDefinitionHandle> duplicateParseMethods =
            [
                last,
                .. coordinates.ParseMethods,
                first,
            ];

        AssemblyAnalysisOperation<int> oneMethod = CreateOperation(
            path,
            MethodDefinitionSourceBreadth.ExactMethods(first),
            complete);
        AssemblyAnalysisOperation<int> parseMethods = CreateOperation(
            path,
            MethodDefinitionSourceBreadth.ExactMethods(
                duplicateParseMethods),
            complete);
        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        AssemblyAnalysisExecution<int> oneExecution =
            Execute(session, oneMethod);
        AssemblyAnalysisExecution<int> parseExecution =
            Execute(session, parseMethods);

        Assert.Equal(7, coordinates.ParseMethods.Length);
        AssertCoverage(
            [first],
            oneExecution.SourceReceipt.Coverage);
        AssertCoverage(
            coordinates.ParseMethods,
            parseExecution.SourceReceipt.Coverage);
        Assert.Equal(
            coordinates.ParseMethods,
            parseMethods.MethodDefinitions.Breadth.Methods);
        Assert.All(
            parseExecution.SourceReceipt.Coverage.BodiesAcquired.Ranges,
            range =>
            {
                Assert.True(
                    parseExecution.SourceReceipt.Coverage
                        .MethodsSelected.Contains(range.First));
                Assert.True(
                    parseExecution.SourceReceipt.Coverage
                        .MethodsSelected.Contains(range.Last));
            });
    }

    [Fact]
    public void
        MethodQuerySource_ExactTypeBreadthVisitsOnlyDeclaredMethods()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "packages",
            "System.Text.Json.10.0.0.dll");
        JsonDocumentCoordinates coordinates =
            ReadJsonDocumentCoordinates(path);
        AssemblyAnalysisOperation<int> operation = CreateOperation(
            path,
            MethodDefinitionSourceBreadth.ExactTypes(
                coordinates.Type),
            CompleteUnsafeEvidenceDescription());

        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        AssemblyAnalysisExecution<int> execution =
            Execute(session, operation);

        Assert.True(
            coordinates.TypeMethods.Length
            > coordinates.ParseMethods.Length);
        Assert.True(
            execution.SourceReceipt.Coverage
                .BodiesAcquired.Count > 0);
        AssertCoverage(
            coordinates.TypeMethods,
            execution.SourceReceipt.Coverage);
    }

    [Fact]
    public void
        MethodQuerySource_ReceiptSeparatesExaminedSelectedAndAcquiredWork()
    {
        ImmutableArray<byte> image = BuildSchedulingSensitiveAssembly();
        ImmutableArray<MethodDefinitionHandle> methods =
            ReadAllMethodHandles(image);
        WorkDescription work =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan(
                        [
                            new ProducerRequest(
                                CoverageOnlyProducer.Instance,
                                ProducerTerminal.Complete),
                        ]))
                .Description;
        MethodDefinitionSourceRequest<int> source =
            MethodDefinitionSourceRequest<int>.Create(
                work,
                CoverageOnlyProducer.Instance,
                MethodDefinitionSourceBreadth.ExactMethods(
                    methods.Reverse().ToImmutableArray()));
        AssemblyAnalysisOperation<int> operation =
            AssemblyAnalysisOperation<int>.Create(
                "ExactCoverage.dll",
                source);
        using AssemblyInspectionSession session = OpenSession(image);

        AssemblyAnalysisExecution<int> execution =
            Execute(session, operation);

        AssertCoverage(methods, execution.SourceReceipt.Coverage);
        Assert.Equal(
            methods.Length,
            execution.ResultOf(CoverageOnlyProducer.Instance).Value);
        Assert.Equal(
            0,
            execution.SourceReceipt.Coverage.BodiesAcquired.Count);
        Assert.DoesNotContain(
            execution.WorkReceipt
                .For(CoverageOnlyProducer.Instance)
                .Layers,
            layer => layer.Layer
                == nameof(MethodDefinitionLayers.Body));
    }

    [Fact]
    public void MethodQuerySource_ExactExistsPublishesVisitedSparsePrefix()
    {
        ImmutableArray<byte> image = BuildSchedulingSensitiveAssembly();
        ImmutableArray<MethodDefinitionHandle> methods =
            ReadAllMethodHandles(image);
        AssemblyAnalysisOperation<int> operation = CreateOperation(
            "SparseStopsAtFirstSettledMethod.dll",
            MethodDefinitionSourceBreadth.ExactMethods(
                methods.Reverse().ToImmutableArray()));
        using AssemblyInspectionSession session = OpenSession(image);

        AssemblyAnalysisExecution<int> execution =
            Execute(session, operation);
        MethodDefinitionSourceCoverage coverage =
            execution.SourceReceipt.Coverage;

        Assert.Equal(
            MethodDefinitionSourceCompletion.Satisfied,
            execution.SourceReceipt.Completion);
        Assert.Equal(1, coverage.DefinitionsExamined.Count);
        Assert.Equal(1, coverage.MethodsSelected.Count);
        Assert.True(coverage.MethodsSelected.Contains(methods[0]));
        Assert.False(coverage.MethodsSelected.Contains(methods[^1]));
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
        Assert.Equal(
            1,
            execution.SourceReceipt.Coverage.BodiesAcquired.Count);
        Assert.Equal(
            1,
            execution.WorkReceipt
                .For(UnsafeEvidencePresenceProducer.Instance)
                .Layers.Single(
                    layer => layer.Layer
                        == nameof(MethodDefinitionLayers.Body))
                .Acquired);
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
        => CreateOperation(
            sourceName,
            MethodDefinitionSourceBreadth.AllDefinitions);

    static AssemblyAnalysisOperation<int> CreateOperation(
        string sourceName,
        MethodDefinitionSourceBreadth breadth,
        WorkDescription? work = null)
    {
        MethodDefinitionSourceRequest<int> source =
            MethodDefinitionSourceRequest<int>.Create(
                work ?? UnsafeEvidencePresence.Description,
                UnsafeEvidencePresenceProducer.Instance,
                breadth);
        return AssemblyAnalysisOperation<int>.Create(
            sourceName,
            source);
    }

    static AssemblyAnalysisExecution<int> Execute(
        AssemblyInspectionSession session,
        AssemblyAnalysisOperation<int> operation) =>
        Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                session.SnapshotOperation(
                    operation,
                    access =>
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
            .Execution;

    static WorkDescription CompleteUnsafeEvidenceDescription() =>
        Assert.IsType<ProducerPlanResult.Accepted>(
                ProducerPlanner.Plan(
                    [
                        new ProducerRequest(
                            UnsafeEvidencePresenceProducer.Instance,
                            ProducerTerminal.Complete),
                    ]))
            .Description;

    static void AssertCoverage(
        ImmutableArray<MethodDefinitionHandle> expected,
        MethodDefinitionSourceCoverage actual)
    {
        Assert.Equal(
            expected.ToArray(),
            Expand(actual.DefinitionsExamined).ToArray());
        Assert.Equal(
            expected.ToArray(),
            Expand(actual.MethodsSelected).ToArray());
    }

    static ImmutableArray<MethodDefinitionHandle> Expand(
        MethodDefinitionHandleCoverage coverage)
    {
        var handles =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>(
                coverage.Count);
        foreach (MethodDefinitionHandleRange range in coverage.Ranges)
        {
            int first = MetadataTokens.GetRowNumber(range.First);
            int last = MetadataTokens.GetRowNumber(range.Last);
            for (int row = first; row <= last; row++)
                handles.Add(MetadataTokens.MethodDefinitionHandle(row));
        }

        return handles.MoveToImmutable();
    }

    static ImmutableArray<MethodDefinitionHandle> ReadAllMethodHandles(
        ImmutableArray<byte> image)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return [.. reader.MethodDefinitions];
    }

    static JsonDocumentCoordinates ReadJsonDocumentCoordinates(
        string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!reader.StringComparer.Equals(
                    type.Namespace,
                    "System.Text.Json")
                || !reader.StringComparer.Equals(
                    type.Name,
                    "JsonDocument"))
            {
                continue;
            }

            var methods =
                ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
            var parse =
                ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                methods.Add(methodHandle);
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (reader.StringComparer.Equals(method.Name, "Parse"))
                    parse.Add(methodHandle);
            }

            return new(
                typeHandle,
                parse.ToImmutable(),
                methods.ToImmutable());
        }

        throw new InvalidOperationException(
            "The pinned System.Text.Json does not define JsonDocument.");
    }

    readonly record struct JsonDocumentCoordinates(
        TypeDefinitionHandle Type,
        ImmutableArray<MethodDefinitionHandle> ParseMethods,
        ImmutableArray<MethodDefinitionHandle> TypeMethods);

    readonly struct CoverageOnlyPredicate : IMethodDefinitionPredicate
    {
        public bool Test(scoped MethodDefinitionView view) => true;
    }

    sealed class CoverageOnlyProducer
        : MethodDefinitionPredicateProducer<CoverageOnlyPredicate>
    {
        CoverageOnlyProducer()
            : base(
                "MethodSourceCoverageOnly",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Flags)
        {
        }

        public static CoverageOnlyProducer Instance { get; } = new();
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
