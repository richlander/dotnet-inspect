using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Fixtures;
using ILInspector.Analysis.Planning;
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
            0,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.BodiesAcquired.Count);
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
                PInvokeAnalyzer.Instance,
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
                PInvokeAnalyzer.Instance,
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
                PInvokeAnalyzer.Instance,
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
                PInvokeAnalyzer.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation independentPInvoke =
            Association(
                PInvokeAnalyzer.Instance,
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
                PointerSignatureAnalyzer.Instance,
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

    static MethodDefinitionSourceAssociation Association<TResult>(
        ProducerDeclaration<TResult> producer,
        ProducerTerminal terminal,
        MethodDefinitionSourceBreadth? breadth = null)
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
                breadth ?? MethodDefinitionSourceBreadth.AllDefinitions);
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
}
