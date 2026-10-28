using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

public sealed record LibraryMethodLeverageProducerResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    IReadOnlySet<int> DeclaredSourceSuppressedMethodTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Produces complete whole-library call evidence for method-leverage ranking
/// without acquiring unrelated call-graph analysis families.
/// </summary>
public sealed class LibraryMethodLeverageProducer :
    MethodDefinitionProducer<
        LibraryCallGraphBodyEvidence,
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
        LibraryMethodLeverageProducerResult>
{
    const int PartitionsPerWorker = 2;

    LibraryMethodLeverageProducer()
        : base(
            "Library Method Leverage",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SemanticIdentity
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.CallOperands)
    {
    }

    public static LibraryMethodLeverageProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.ResolvedCalls,
        ];

    internal override bool SharesCalleeIdentities => true;

    internal override bool SupportsPendingRelationKernel => true;

    internal override LibraryCallGraphBodyEvidence Visit(
        scoped MethodDefinitionView view)
    {
        int token = view.Token;
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        MethodSemanticIdentityResult semanticIdentity =
            view.SemanticIdentity;
        if (semanticIdentity
            is MethodSemanticIdentityResult.Malformed malformed)
        {
            diagnostics.Add(
                new(
                    token,
                    $"0x{token:x8}",
                    malformed.Reason));
            return Empty(
                method: null,
                view.HasManagedBody,
                diagnostics);
        }

        MethodIdentity method =
            ExactMethodAnalysisProducer.ToMethodIdentity(
                semanticIdentity,
                ExactMethodAnalysisProducer.ToCallerUnsafeMode(
                    view.GetMemorySafetyMethodContract(
                        MetadataTokens.MethodDefinitionHandle(token))));
        if (!view.HasManagedBody)
        {
            return Empty(
                method,
                hasManagedBody: false,
                diagnostics);
        }

        ImmutableArray<DirectCall> calls = [];
        try
        {
            MethodInstructions instructions =
                view.MaterializeInstructions(
                    out MethodBodyData body);
            MethodCallOperandSites callSites =
                view.GetCallOperands(instructions);
            if (callSites.Failure is { } callFailure)
            {
                diagnostics.Add(
                    new(
                        token,
                        ExactMethodAnalysisProducer.MethodLabel(method),
                        "Call analysis stopped at "
                            + $"IL_{callFailure.ILOffset:x4}: "
                            + callFailure.Message));
            }
            MethodBodyAnalysisContext context =
                MethodBodyAnalysisContext.Create(
                    method,
                    body,
                    instructions,
                    localTypes: []);
            calls =
                ExactMethodAnalysisProducer.CollectDirectCalls(
                    context,
                    callSites);
        }
        catch (Exception ex)
            when (ex
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
        {
            diagnostics.Add(
                new(
                    token,
                    ExactMethodAnalysisProducer.MethodLabel(method),
                    ex.Message));
        }

        return new(
            method,
            HasManagedBody: true,
            calls,
            UnsafeEvidence: [],
            BodySignals: null,
            InAssemblyTypeIsException: null,
            NonHeapNewObjOperandTokens: null,
            DeclaredSource: null,
            DeclaredSourceSuppressesOpportunities: false,
            diagnostics.ToImmutable());
    }

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>();

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Accumulate(
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
            LibraryCallGraphBodyEvidence fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override LibraryMethodLeverageProducerResult Complete(
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
        MethodDefinitionCompletionView completion)
    {
        LibraryCallGraphProducerResult result =
            LibraryCallGraphProducer.CompleteEvidence(
                accumulator,
                completion,
                includeSupplementalEvidence: false);
        return new(
            result.DeclaredMethods,
            result.Methods,
            result.DirectCalls,
            result.DeclaredSources,
            result.DeclaredSourceSuppressedMethodTokens,
            result.Diagnostics);
    }

    internal override bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        out int unitsVisited)
    {
        unitsVisited = 0;
        MethodDefinitionParallelExecution? parallel =
            state.Execution.ParallelExecution;
        if (parallel is null
            || parallel.MaximumConcurrency < 2
            || state.Terminal != ProducerTerminal.Rows
            || state.RowLimit is not null
            || !state.Execution.HasUnboundedTerminalWork
            || reader.MethodDefinitions.Count
                <= LibraryMethodAnalysisRunner.ParallelMethodThreshold)
        {
            return false;
        }

        var workItems = new List<LeverageWorkItem>(
            reader.MethodDefinitions.Count);
        try
        {
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                foreach (MethodDefinitionHandle methodHandle
                    in type.GetMethods())
                {
                    workItems.Add(
                        new(
                            methodHandle,
                            reader.GetMethodDefinition(methodHandle)));
                }
            }
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
            MethodDefinitionExecution.FailSource(
                state,
                0,
                "(method source)",
                ex);
            return true;
        }

        int partitionCount =
            Math.Min(
                checked(
                    parallel.MaximumConcurrency
                    * PartitionsPerWorker),
                (workItems.Count
                    + LibraryMethodAnalysisRunner.ParallelMethodThreshold
                    - 1)
                / LibraryMethodAnalysisRunner.ParallelMethodThreshold);
        if (partitionCount < 2)
            return false;

        var bodies = new MethodBodyBlock?[workItems.Count];
        var partitions =
            new MethodDefinitionKernelPartition<
                ImmutableArray<LibraryCallGraphBodyEvidence>.Builder>?[
                    partitionCount];
        var failures = new Exception?[partitionCount];
        parallel.ExecutePartitions(
            partitionCount,
            partitionIndex =>
            {
                try
                {
                    int start =
                        partitionIndex * workItems.Count
                        / partitionCount;
                    int end =
                        (partitionIndex + 1) * workItems.Count
                        / partitionCount;
                    var methods =
                        ImmutableArray.CreateBuilder<
                            MethodDefinitionHandle>(end - start);
                    for (int index = start; index < end; index++)
                        methods.Add(workItems[index].Handle);

                    int observed = start;
                    partitions[partitionIndex] =
                        state.Execution.ExecuteKernelPartition(
                            this,
                            peReader,
                            methods.MoveToImmutable(),
                            (
                                MethodDefinitionHandle _,
                                MethodDefinition __,
                                MethodBodyBlock? body) =>
                            {
                                bodies[observed++] = body;
                            });
                    if (observed != end)
                    {
                        throw new ProducerContractException(
                            "A Method Leverage kernel partition did not "
                                + "observe every selected MethodDef.");
                    }
                }
                catch (Exception ex)
                {
                    failures[partitionIndex] = ex;
                }
            });

        for (int partitionIndex = 0;
            partitionIndex < partitionCount;
            partitionIndex++)
        {
            if (failures[partitionIndex] is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        var accumulator =
            ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>(
                workItems.Count);
        MethodDefinitionSourceCoverageBuilder coverage =
            state.Execution.SourceCoverageBuilder;
        for (int partitionIndex = 0;
            partitionIndex < partitionCount;
            partitionIndex++)
        {
            MethodDefinitionKernelPartition<
                ImmutableArray<LibraryCallGraphBodyEvidence>.Builder>
                    partition =
                        partitions[partitionIndex]
                        ?? throw new ProducerContractException(
                            "A Method Leverage kernel partition did not "
                                + "publish its ordered result.");
            accumulator.AddRange(partition.Accumulator);
            coverage.MergePartitionSupports(partition.Coverage);
            Absorb(state, partition.State);
        }

        int coveragePartition = 0;
        for (int index = 0; index < workItems.Count; index++)
        {
            LeverageWorkItem item = workItems[index];
            while (index
                >= (coveragePartition + 1) * workItems.Count
                    / partitionCount)
            {
                coveragePartition++;
            }
            MethodDefinitionSourceCoverage partitionCoverage =
                partitions[coveragePartition]!.Value.Coverage;
            coverage.RecordDefinitionExamined(item.Handle);
            coverage.RecordMethodSelected(item.Handle);
            if (partitionCoverage.BodiesAttempted.Contains(item.Handle))
            {
                coverage.RecordTerminalBodyAttempted(item.Handle);
                coverage.RecordBodyAttempted(item.Handle);
            }
            if (bodies[index] is { } body)
            {
                coverage.RecordTerminalBodyAcquired(
                    item.Handle,
                    body);
                coverage.RecordBodyAcquired(item.Handle);
            }
            state.Execution.RecordKernelRelationReference(
                peReader,
                item.Handle,
                item.Definition,
                bodies[index]);
        }

        ((MethodDefinitionProducer<
            LibraryCallGraphBodyEvidence,
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
            LibraryMethodLeverageProducerResult>.State)state)
            .SetAccumulator(accumulator);
        unitsVisited = workItems.Count;
        return true;
    }

    static void Absorb(
        MethodDefinitionExecution.ProducerState target,
        MethodDefinitionExecution.ProducerState source)
    {
        target.SettlingFacts =
            checked(target.SettlingFacts + source.SettlingFacts);
        target.UnitsAttempted =
            checked(target.UnitsAttempted + source.UnitsAttempted);
        target.UnitsCompleted =
            checked(target.UnitsCompleted + source.UnitsCompleted);
        target.UnitsFailed =
            checked(target.UnitsFailed + source.UnitsFailed);
        target.BodyAcquisitions =
            checked(target.BodyAcquisitions + source.BodyAcquisitions);
        target.MemorySafetyUses =
            checked(target.MemorySafetyUses + source.MemorySafetyUses);
        target.SemanticIdentityUses =
            checked(
                target.SemanticIdentityUses
                + source.SemanticIdentityUses);
        target.CallOperandUses =
            checked(target.CallOperandUses + source.CallOperandUses);
        if (target.SourceFailure is null)
            target.SourceFailure = source.SourceFailure;
        if (target.Failure is null)
            target.Failure = source.Failure;
        if (target.Outcome is null
            || (target.Outcome == ProducerOutcome.Complete
                && source.Outcome != ProducerOutcome.Complete))
        {
            target.Outcome = source.Outcome;
        }
    }

    readonly record struct LeverageWorkItem(
        MethodDefinitionHandle Handle,
        MethodDefinition Definition);

    static LibraryCallGraphBodyEvidence Empty(
        MethodIdentity? method,
        bool hasManagedBody,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics) =>
        new(
            method,
            hasManagedBody,
            DirectCalls: [],
            UnsafeEvidence: [],
            BodySignals: null,
            InAssemblyTypeIsException: null,
            NonHeapNewObjOperandTokens: null,
            DeclaredSource: null,
            DeclaredSourceSuppressesOpportunities: false,
            diagnostics.ToImmutable());
}
