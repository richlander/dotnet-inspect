using System.Runtime.ExceptionServices;

using DotnetInspector.Queries;
using ILInspector.Analysis.Planning;

namespace DotnetInspector.Sections.Local;

/// <summary>
/// Executes Type Method Leverage with the bounded parallel capability
/// available to local desktop hosts.
/// </summary>
public static class LocalTypeMethodLeverageInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>>
        Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string typeDefinitionId) =>
        TypeMethodLeverageInspectionOperation.Execute(
            group,
            participant,
            typeDefinitionId,
            LocalMethodDefinitionParallelExecution.Instance);
}

// Each worker owns support caches, so cap concurrency as well as tasks.
sealed class LocalMethodDefinitionParallelExecution
    : MethodDefinitionParallelExecution
{
    const int MaximumLocalConcurrency = 4;

    LocalMethodDefinitionParallelExecution()
        : base(
            Math.Max(
                1,
                Math.Min(
                    MaximumLocalConcurrency,
                    Environment.ProcessorCount)))
    {
    }

    internal static LocalMethodDefinitionParallelExecution Instance
    { get; } = new();

    public override void ExecutePartitions(
        int partitionCount,
        Action<int> executePartition)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            partitionCount,
            1);
        ArgumentNullException.ThrowIfNull(executePartition);
        int workerCount =
            Math.Min(MaximumConcurrency, partitionCount);
        int nextPartition = -1;
        ExceptionDispatchInfo? failure = null;

        // The operation owns only a few coarse partitions, so start its
        // bounded workers immediately instead of waiting for pool growth.
        void ExecuteAvailablePartitions()
        {
            while (Volatile.Read(ref failure) is null)
            {
                int partition =
                    Interlocked.Increment(ref nextPartition);
                if (partition >= partitionCount)
                    return;
                try
                {
                    executePartition(partition);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(
                        ref failure,
                        ExceptionDispatchInfo.Capture(ex),
                        null);
                    return;
                }
            }
        }

        var workers = new Thread[workerCount - 1];
        for (int index = 0; index < workers.Length; index++)
        {
            workers[index] = new(ExecuteAvailablePartitions)
            {
                IsBackground = true,
                Name = "Method definition analysis",
            };
            workers[index].Start();
        }
        ExecuteAvailablePartitions();
        foreach (Thread worker in workers)
            worker.Join();
        failure?.Throw();
    }
}
