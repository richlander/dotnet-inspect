using System.Collections.Immutable;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void MethodQuerySource_MethodAnalysisAbsentWhenUnqueried()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation silent =
            Association(
                MethodAnalysisProbe.Silent,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([undeclared, silent]));

        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.MethodAnalysis);
        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.MethodAnalysis);
        Assert.Same(
            MethodAnalysisSupportCoverage.Empty,
            execution.ResultOf(silent)
                .SourceReceipt.Coverage.MethodAnalysis);
        Assert.Contains(
            execution.ResultOf(silent).WorkReceipt.Producers[0].Layers,
            layer => layer
                == new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.MethodAnalysis),
                    0));
    }

    [Fact]
    public void MethodQuerySource_MethodAnalysisRequiresDeclaration()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                UndeclaredMethodAnalysisProbe.Instance,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            "method-analysis support",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        MethodQuerySource_MethodAnalysisMatchesLegacyAndReceiptsSupportingWork()
    {
        string path = typeof(MethodAnalysisSupportFixture).Assembly.Location;
        int token = typeof(MethodAnalysisSupportFixture)
            .GetMethod(nameof(MethodAnalysisSupportFixture.Target))!
            .MetadataToken;
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(token & 0x00FF_FFFF);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition definition =
            reader.GetMethodDefinition(method);
        DecodedInstruction constructor =
            Assert.Single(
                InstructionDecoder.Decode(
                    pe.GetMethodBody(
                            definition.RelativeVirtualAddress)
                        .GetILBytes()!),
                instruction =>
                    instruction.OpCode == ILOpCode.Newobj);
        EntityHandle constructorHandle =
            MetadataTokens.EntityHandle(
                MethodInstructionFacts.OperandInt32(constructor));
        Assert.Equal(
            HandleKind.MemberReference,
            constructorHandle.Kind);
        MemberReferenceHandle constructorReference =
            (MemberReferenceHandle)constructorHandle;
        int constructorSignatureBytes =
            reader.GetBlobReader(
                    reader.GetMemberReference(constructorReference)
                        .Signature)
                .Length;
        Type? asyncStateMachine = Array.Find(
            typeof(MethodAnalysisSupportFixture).Assembly.GetTypes(),
            type => type.IsClass
                && typeof(IAsyncStateMachine).IsAssignableFrom(type)
                && type.Name.Contains(">d__", StringComparison.Ordinal));
        Assert.NotNull(asyncStateMachine);
        var producer = new MethodAnalysisProbe(
            asyncStateMachine.MetadataToken);
        MethodDefinitionSourceAssociation exact =
            Association(
                producer,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution source =
            ExecuteAt(AcceptedPlan([exact]), path);
        ProducerResult<ImmutableArray<MethodAnalysisObservation>>
            producerResult =
                ResultOf<ImmutableArray<MethodAnalysisObservation>>(
                    source,
                    exact);
        Assert.True(
            producerResult.HasValue,
            producerResult.Failure?.Message);
        MethodAnalysisObservation actual =
            Assert.Single(producerResult.Value!);

        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.OptimizationOpportunities,
                    new HashSet<int> { token }));
        try
        {
            Assert.Equal(
                legacy.Allocations.Occurrences.GetValueOrDefault(token),
                actual.Allocations);
            Assert.Equal(
                legacy.Optimization.Opportunities.Where(
                        opportunity =>
                            opportunity.Method.MetadataToken == token)
                    .Select(OpportunityCore.From),
                actual.Opportunities.Select(OpportunityCore.From));
        }
        finally
        {
            legacy.CallGraph.ReleaseCaches();
        }

        Assert.True(actual.StableReceiver);
        Assert.True(actual.AsyncStateMachine);

        MethodDefinitionSourceCoverage coverage =
            source.ResultOf(exact).SourceReceipt.Coverage;
        MethodAnalysisSupportCoverage support = coverage.MethodAnalysis;
        Assert.True(support.Constructed);
        Assert.Equal(1, support.Resolver.Queries);
        Assert.Equal(1, support.Resolver.QueryingMethods.Count);
        Assert.True(support.Resolver.QueryingMethods.Contains(method));
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);
        Assert.Equal(1, coverage.BodiesAcquired.Count);
        Assert.True(
            support.Resolver.QueryWork.StableReceiverQueries > 0);
        Assert.Equal(
            1,
            support.Resolver.QueryWork.MethodReferencesResolved);
        Assert.Equal(
            2L * Math.Max(constructorSignatureBytes, 1),
            support.Resolver.QueryWork.MethodReferenceSignatureWork);
        Assert.Equal(
            1,
            support.Resolver.QueryWork.StableGetterBodiesRead);
        Assert.True(
            support.Resolver.QueryWork.StableGetterEncodedIlBytes > 0);
        Assert.Equal(
            reader.TypeDefinitions.Count,
            support.Resolver.QueryWork.AsyncStateMachineTypeRows);
    }

    [Fact]
    public void
        MethodQuerySource_ExactOptimizationSharesAllocationContextAndFacts()
    {
        string path = typeof(MethodAnalysisSupportFixture).Assembly.Location;
        int token = typeof(MethodAnalysisSupportFixture)
            .GetMethod(nameof(MethodAnalysisSupportFixture.Target))!
            .MetadataToken;
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(token & 0x00FF_FFFF);
        MethodDefinitionSourceAssociation exact =
            Association(
                ExactMethodOptimizationProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution source =
            ExecuteAt(AcceptedPlan([exact]), path);
        ProducerResult<ExactMethodOptimizationProducerResult>
            producerResult =
                ResultOf<ExactMethodOptimizationProducerResult>(
                    source,
                    exact);
        Assert.True(
            producerResult.HasValue,
            producerResult.Failure?.Message);
        ExactMethodOptimizationBody actual =
            Assert.Single(producerResult.Value!.Bodies);

        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.OptimizationOpportunities,
                    new HashSet<int> { token }));
        try
        {
            Assert.Equal(
                legacy.Allocations.Occurrences.GetValueOrDefault(token),
                actual.Allocation.Allocations);
            Assert.Equal(
                legacy.Optimization.Opportunities.Where(
                        opportunity =>
                            opportunity.Method.MetadataToken == token)
                    .Select(OpportunityCore.From),
                actual.Opportunities.Select(OpportunityCore.From));
        }
        finally
        {
            legacy.CallGraph.ReleaseCaches();
        }

        Assert.Same(
            actual.Allocation.Context,
            actual.ConsumedContext);
        Assert.Same(
            actual.Allocation.Facts,
            actual.ConsumedFacts);
        MethodDefinitionSourceRequestResult request =
            source.ResultOf(exact);
        MethodDefinitionSourceCoverage coverage =
            request.SourceReceipt.Coverage;
        Assert.Equal(1, coverage.BodiesAcquired.Count);
        Assert.Equal(
            1,
            coverage.InstructionWork.LazyRetainedSourcesOpened);
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);
        Assert.Equal(1, coverage.Calls.CallReads);
        Assert.Equal(1, coverage.Calls.LocalSignatureReads);
        Assert.True(coverage.CallTargets.Constructed);
        Assert.Equal(2, coverage.MethodAnalysis.Resolver.Queries);
        Assert.Equal(
            1,
            coverage.MethodAnalysis.DeclaredSource.Queries);
        Assert.Equal(
            1,
            coverage.MethodAnalysis.GeneratedCodeEvidence.Queries);
        Assert.Equal(
            0,
            coverage.MethodAnalysis.DeclaredSource.QueryWork
                .CandidateDefinitions);
        Assert.True(
            coverage.MethodAnalysis.DeclaredSource.QueryWork
                .TypeDefinitionRows > 0);
        Assert.True(
            coverage.MethodAnalysis.DeclaredSource.QueryWork
                .RelationshipNodes > 0);

        ProducerParticipation allocation =
            request.WorkReceipt.For(
                ExactMethodAllocationProducer.Instance);
        Assert.DoesNotContain(
            allocation.Layers,
            layer => layer.Layer
                == nameof(MethodDefinitionLayers.LocalSignature));
        Assert.DoesNotContain(
            allocation.Layers,
            layer => layer.Layer
                == nameof(MethodDefinitionLayers.Calls));
        ProducerParticipation methodAnalysis =
            request.WorkReceipt.For(
                ExactMethodAnalysisProducer.Instance);
        Assert.Contains(
            methodAnalysis.Layers,
            layer => layer
                == new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.LocalSignature),
                    1));
        Assert.Contains(
            methodAnalysis.Layers,
            layer => layer
                == new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.Calls),
                    1));
        ProducerParticipation optimization =
            request.WorkReceipt.For(
                ExactMethodOptimizationProducer.Instance);
        Assert.Contains(
            optimization.Layers,
            layer => layer
                == new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.MethodAnalysis),
                    1));
    }

    [Fact]
    public void
        MethodQuerySource_ReceiptsMethodGeneratedCodeEvidence()
    {
        (
            ExactMethodOptimizationBody actual,
            MethodGeneratedCodeEvidenceWork work) =
            ExecuteExactOptimization(
                typeof(MethodAnalysisSupportFixture)
                    .GetMethod(
                        nameof(MethodAnalysisSupportFixture
                            .GeneratedMethodSuppression))!);

        Assert.True(actual.SuppressesDerivedOpportunities);
        Assert.True(work.MethodAttributeRows > 0);
        Assert.Equal(0, work.TypeDefinitionRows);
        Assert.Equal(0, work.TypeAttributeRows);
        Assert.Equal(0, work.RelationshipNodes);
    }

    [Fact]
    public void
        MethodQuerySource_ReceiptsEnclosingTypeGeneratedCodeEvidence()
    {
        (
            ExactMethodOptimizationBody actual,
            MethodGeneratedCodeEvidenceWork work) =
            ExecuteExactOptimization(
                typeof(MethodAnalysisGeneratedContainer.Nested)
                    .GetMethod(
                        nameof(MethodAnalysisGeneratedContainer
                            .Nested.Target))!);

        Assert.True(actual.SuppressesDerivedOpportunities);
        Assert.True(
            work.TypeDefinitionRows >= 2);
        Assert.True(
            work.TypeAttributeRows > 0);
        Assert.True(
            work.RelationshipNodes > 0);
    }

    [Fact]
    public void
        MethodQuerySource_ReceiptsExactDeclaredSourceAttributionProbes()
    {
        string path = typeof(MethodAnalysisSupportFixture).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodInfo lifted = typeof(MethodAnalysisSupportFixture)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains(
                "<Lifted>g__Core|",
                StringComparison.Ordinal));
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(
                lifted.MetadataToken & 0x00FF_FFFF);
        MethodDefinitionSourceAssociation exact =
            Association(
                ExactMethodOptimizationProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution source =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodDefinitionSourceCoverage coverage =
            source.ResultOf(exact).SourceReceipt.Coverage;

        Assert.Equal(1, coverage.BodiesAcquired.Count);
        Assert.Equal(
            0,
            coverage.GeneratedExpansion.ProbeBodiesAttempted.Count);
        ExactDeclaredSourceWork work =
            coverage.MethodAnalysis.DeclaredSource.QueryWork;
        Assert.True(
            work.CandidateDefinitions > 0);
        Assert.True(
            work.ProbeBodies > 0);
        Assert.True(
            work.ProbeEncodedIlBytes > 0);
        Assert.True(work.TypeDefinitionRows > 0);
        Assert.True(work.RelationshipNodes > 0);
        Assert.True(work.ContractQueries > 0);
        Assert.Equal(
            MemorySafetyMetadataWork.Empty,
            work.ContractConstructionWork);
        Assert.Equal(
            0,
            work.ContractQueryWork
                .IntegrityTypeDefRows);
        Assert.Equal(
            reader.TypeDefinitions.Count,
            coverage.MemorySafetyContracts
                .ConstructionWork.IntegrityTypeDefRows);
    }

    static (
        ExactMethodOptimizationBody Body,
        MethodGeneratedCodeEvidenceWork Work)
        ExecuteExactOptimization(MethodInfo target)
    {
        string path = target.DeclaringType!.Assembly.Location;
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(
                target.MetadataToken & 0x00FF_FFFF);
        MethodDefinitionSourceAssociation exact =
            Association(
                ExactMethodOptimizationProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution source =
            ExecuteAt(AcceptedPlan([exact]), path);
        ExactMethodOptimizationBody body =
            Assert.Single(
                ResultOf<ExactMethodOptimizationProducerResult>(
                        source,
                        exact)
                    .Value!.Bodies);
        MethodGeneratedCodeEvidenceWork work =
            source.ResultOf(exact)
                .SourceReceipt.Coverage
                .MethodAnalysis.GeneratedCodeEvidence.QueryWork;
        return (body, work);
    }

    [Theory]
    [InlineData(
        nameof(MethodAnalysisSupportFixture.NamedNonExceptionAllocation),
        false)]
    [InlineData(
        nameof(MethodAnalysisSupportFixture
            .NamedGenericNonExceptionAllocation),
        true)]
    public void
        MethodQuerySource_ReceiptsTargetedLocalExceptionClassification(
            string methodName,
            bool hasTypeSpecification)
    {
        string path = typeof(MethodAnalysisSupportFixture).Assembly.Location;
        int token = typeof(MethodAnalysisSupportFixture)
            .GetMethod(methodName)!
            .MetadataToken;
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(
                token & 0x00FF_FFFF);
        MethodDefinitionSourceAssociation exact =
            Association(
                ExactMethodOptimizationProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));

        MethodDefinitionSourceRequestSetExecution source =
            ExecuteAt(AcceptedPlan([exact]), path);
        ExactMethodOptimizationBody actual =
            Assert.Single(
                ResultOf<ExactMethodOptimizationProducerResult>(
                        source,
                        exact)
                    .Value!.Bodies);
        AllocationOccurrence allocation =
            Assert.Single(actual.Allocation.Allocations);
        TypeRef allocatedType =
            allocation.AllocatedType!;
        TypeRef allocated =
            allocatedType.Kind == TypeRefKind.GenericInstance
                ? allocatedType.ElementType ?? allocatedType
                : allocatedType;
        Assert.False(
            actual.Allocation.InAssemblyTypeIsException[
                (allocated.Namespace, allocated.Name)]);

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodAnalysisResolverWork work =
            source.ResultOf(exact)
                .SourceReceipt.Coverage
                .MethodAnalysis.Resolver.QueryWork;
        Assert.True(work.LocalExceptionTypeQueries > 0);
        Assert.InRange(
            work.LocalExceptionTypeDefinitionRows,
            1,
            reader.TypeDefinitions.Count - 1);
        Assert.Equal(
            work.LocalExceptionTypeDefinitionRows,
            work.LocalExceptionTypeRelationshipNodes);
        Assert.Equal(
            hasTypeSpecification,
            work.LocalExceptionTypeSignatureBytes > 0);
    }

    [Fact]
    public void MethodQuerySource_MethodAnalysisSharesCachesAcrossLanes()
    {
        string path = typeof(MethodAnalysisSupportFixture).Assembly.Location;
        int token = typeof(MethodAnalysisSupportFixture)
            .GetMethod(nameof(MethodAnalysisSupportFixture.Target))!
            .MetadataToken;
        Type? asyncStateMachine = Array.Find(
            typeof(MethodAnalysisSupportFixture).Assembly.GetTypes(),
            type => type.IsClass
                && typeof(IAsyncStateMachine).IsAssignableFrom(type)
                && type.Name.Contains(">d__", StringComparison.Ordinal));
        Assert.NotNull(asyncStateMachine);
        var firstProducer = new MethodAnalysisProbe(
            "FirstMethodAnalysisProbe",
            asyncStateMachine.MetadataToken,
            token);
        var secondProducer = new MethodAnalysisProbe(
            "SecondMethodAnalysisProbe",
            asyncStateMachine.MetadataToken,
            token);
        MethodDefinitionSourceAssociation first =
            Association(
                firstProducer,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                secondProducer,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([first, second]), path);
        MethodAnalysisSupportCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.MethodAnalysis;
        MethodAnalysisSupportCoverage firstCoverage =
            execution.ResultOf(first)
                .SourceReceipt.Coverage.MethodAnalysis;
        MethodAnalysisSupportCoverage secondCoverage =
            execution.ResultOf(second)
                .SourceReceipt.Coverage.MethodAnalysis;

        Assert.True(physical.Constructed);
        Assert.True(firstCoverage.Constructed);
        Assert.False(secondCoverage.Constructed);
        Assert.Equal(2, physical.Resolver.Queries);
        Assert.Equal(1, firstCoverage.Resolver.Queries);
        Assert.Equal(1, secondCoverage.Resolver.Queries);
        Assert.Equal(1, physical.Resolver.QueryingMethods.Count);
        Assert.Equal(
            1,
            physical.Resolver.QueryWork.StableGetterBodiesRead);
        Assert.Equal(
            firstCoverage.Resolver.QueryWork.StableGetterBodiesRead,
            physical.Resolver.QueryWork.StableGetterBodiesRead);
        Assert.Equal(
            0,
            secondCoverage.Resolver.QueryWork.StableGetterBodiesRead);
        Assert.True(
            firstCoverage.Resolver.QueryWork.AsyncStateMachineTypeRows > 0);
        Assert.Equal(
            0,
            secondCoverage.Resolver.QueryWork.AsyncStateMachineTypeRows);
    }

    sealed record MethodAnalysisObservation(
        ImmutableArray<AllocationOccurrence> Allocations,
        ImmutableArray<OptimizationOpportunity> Opportunities,
        bool StableReceiver,
        bool AsyncStateMachine);

    sealed record OpportunityCore(
        MethodIdentity Method,
        string Shape,
        string Evidence,
        string SafeFixDirection,
        string Confidence,
        bool InLoop,
        int? ILOffset,
        string? Caveat,
        string? RuntimeAllocationType,
        string? PathContext,
        string? PathConfidence,
        string? PostDominance,
        string? Multiplicity,
        int? EstimatedSizeBytes,
        string? Weight)
    {
        internal static OpportunityCore From(
            OptimizationOpportunity opportunity) =>
            new(
                opportunity.Method,
                opportunity.Shape,
                opportunity.Evidence,
                opportunity.SafeFixDirection,
                opportunity.Confidence,
                opportunity.InLoop,
                opportunity.ILOffset,
                opportunity.Caveat,
                opportunity.RuntimeAllocationType,
                opportunity.PathContext,
                opportunity.PathConfidence,
                opportunity.PostDominance,
                opportunity.Multiplicity,
                opportunity.EstimatedSizeBytes,
                opportunity.Weight);
    }

    sealed class MethodAnalysisProbe
        : MethodDefinitionProducer<
            MethodAnalysisObservation?,
            ImmutableArray<MethodAnalysisObservation>.Builder,
            ImmutableArray<MethodAnalysisObservation>>
    {
        readonly int _asyncStateMachineToken;
        readonly int? _targetMethodToken;
        readonly bool _query;

        internal MethodAnalysisProbe(int asyncStateMachineToken)
            : this(
                "MethodAnalysisProbe",
                asyncStateMachineToken,
                targetMethodToken: null,
                query: true)
        {
        }

        internal MethodAnalysisProbe(
            string identity,
            int asyncStateMachineToken,
            int targetMethodToken)
            : this(
                identity,
                asyncStateMachineToken,
                targetMethodToken,
                query: true)
        {
        }

        MethodAnalysisProbe(
            string identity,
            int asyncStateMachineToken,
            int? targetMethodToken,
            bool query)
            : base(
                identity,
                version: 1,
                tier: 0,
                MethodDefinitionLayers.SemanticIdentity
                    | MethodDefinitionLayers.LocalSignature
                    | MethodDefinitionLayers.MethodAnalysis,
                parameters:
                    $"{asyncStateMachineToken:x8}:{targetMethodToken:x8}")
        {
            _asyncStateMachineToken = asyncStateMachineToken;
            _targetMethodToken = targetMethodToken;
            _query = query;
        }

        internal static MethodAnalysisProbe Silent { get; } =
            new(
                "SilentMethodAnalysisProbe",
                asyncStateMachineToken: 0,
                targetMethodToken: null,
                query: false);

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [
                MethodBodyAnalyzerDeclarations.BoundedFlow,
            ];

        internal override MethodAnalysisObservation? Visit(
            scoped MethodDefinitionView view)
        {
            if (!_query
                || _targetMethodToken is int targetMethodToken
                && view.Token != targetMethodToken)
                return null;

            MethodIdentity method =
                view.SemanticIdentity switch
                {
                    MethodSemanticIdentityResult.Decoded decoded =>
                        decoded.Identity.ToMethodIdentity(
                            CallerUnsafeMode.None),
                    MethodSemanticIdentityResult.SignatureBoundExceeded
                        exceeded =>
                        exceeded.Identity.ToMethodIdentity(
                            CallerUnsafeMode.None),
                    MethodSemanticIdentityResult.Malformed malformed =>
                        throw new BadImageFormatException(
                            malformed.Reason),
                    _ => throw new InvalidOperationException(
                        "Unknown semantic identity result."),
                };
            MethodBodyBlock body = view.GetBody();
            MethodInstructions instructions =
                view.MaterializeInstructions(out MethodBodyData bodyData);
            MethodLocalSignature locals = view.GetLocalSignature();
            MethodBodyAnalysisContext context =
                MethodBodyAnalysisContext.Create(
                    method,
                    bodyData,
                    instructions,
                    locals.Types,
                    locals.DeclaredCount,
                    locals.IncompleteReason,
                    body.LocalVariablesInitialized);

            using MethodAnalysisQuery query =
                view.BeginMethodAnalysis(method, instructions);
            ILibraryMethodAnalysisResolver resolver = query.Resolver;
            MethodAllocationFacts allocations =
                MethodAllocationFacts.Create(context);
            allocations.Collect(resolver);
            ImmutableArray<OptimizationOpportunity> opportunities =
                OptimizationOpportunityAnalysis.Collect(
                    allocations,
                    resolver);
            bool stableReceiver =
                instructions.Instructions.Any(
                    instruction =>
                        instruction.OpCode is ILOpCode.Call
                            or ILOpCode.Callvirt
                        && resolver.IsStableReceiverGetter(instruction));
            TypeRef stateMachine =
                ((IMethodAllocationResolver)resolver).ResolveType(
                    _asyncStateMachineToken);
            return new(
                allocations.ClassifiedOccurrences,
                opportunities,
                stableReceiver,
                resolver.IsAsyncStateMachineType(stateMachine));
        }

        internal override ImmutableArray<MethodAnalysisObservation>.Builder
            Seed() =>
            ImmutableArray.CreateBuilder<MethodAnalysisObservation>();

        internal override ImmutableArray<MethodAnalysisObservation>.Builder
            Accumulate(
                ImmutableArray<MethodAnalysisObservation>.Builder accumulator,
                MethodAnalysisObservation? fact)
        {
            if (fact is not null)
                accumulator.Add(fact);
            return accumulator;
        }

        internal override ImmutableArray<MethodAnalysisObservation> Complete(
            ImmutableArray<MethodAnalysisObservation>.Builder accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator.ToImmutable();
    }

    sealed class UndeclaredMethodAnalysisProbe
        : MethodDefinitionProducer<int, int, int>
    {
        UndeclaredMethodAnalysisProbe()
            : base(
                "UndeclaredMethodAnalysisProbe",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.SemanticIdentity
                    | MethodDefinitionLayers.Calls)
        {
        }

        internal static UndeclaredMethodAnalysisProbe Instance { get; } =
            new();

        internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
            InstructionAnalyzers =>
            [
                MethodBodyAnalyzerDeclarations.BoundedFlow,
            ];

        internal override int Visit(scoped MethodDefinitionView view)
        {
            MethodIdentity method =
                Assert.IsType<MethodSemanticIdentityResult.Decoded>(
                        view.SemanticIdentity)
                    .Identity
                    .ToMethodIdentity(CallerUnsafeMode.None);
            MethodInstructions instructions =
                view.MaterializeInstructions(out _);
            using MethodAnalysisQuery query =
                view.BeginMethodAnalysis(method, instructions);
            return 1;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }
}

public static class MethodAnalysisSupportFixture
{
    public sealed class NamedException;

    public sealed class NamedGenericException<T>;

    public sealed class Holder
    {
        readonly object _value = new();

        public object Value => _value;
    }

    public static object Target(Holder holder)
    {
        object[] values = new object[2];
        values[0] = holder.Value;
        values[1] = new object();
        return values;
    }

    public static object Lifted(Holder holder)
    {
        return Core(holder);

        static object Core(Holder value) => value.Value;
    }

    public static object NamedNonExceptionAllocation() =>
        new NamedException();

    public static object NamedGenericNonExceptionAllocation() =>
        new NamedGenericException<int>();

    [GeneratedCode("method-query-source", "1")]
    public static object GeneratedMethodSuppression() => new();
}

[GeneratedCode("method-query-source", "1")]
public static class MethodAnalysisGeneratedContainer
{
    public sealed class Nested
    {
        public static object Target() => new();
    }
}
