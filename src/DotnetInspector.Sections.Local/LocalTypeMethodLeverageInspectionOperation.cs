using DotnetInspector.Queries;
using ILInspector.Analysis.Planning;

namespace DotnetInspector.Sections.Local;

/// <summary>
/// Executes Type Method Leverage with the bounded parallel capability
/// available to local desktop hosts.
/// </summary>
public static class LocalTypeMethodLeverageInspectionOperation
{
    // Each worker owns support caches, so cap concurrency as well as tasks.
    const int MaximumLocalConcurrency = 4;

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

    sealed class LocalMethodDefinitionParallelExecution
        : MethodDefinitionParallelExecution
    {
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
            Parallel.For(
                0,
                partitionCount,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaximumConcurrency,
                },
                executePartition);
        }
    }
}
