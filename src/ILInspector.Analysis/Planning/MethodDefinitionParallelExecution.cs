namespace ILInspector.Analysis.Planning;

/// <summary>
/// Optional host-owned execution capability for bounded parallel
/// Method-definition partitions.
/// </summary>
public abstract class MethodDefinitionParallelExecution
{
    protected MethodDefinitionParallelExecution(int maximumConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumConcurrency,
            1);
        MaximumConcurrency = maximumConcurrency;
    }

    public int MaximumConcurrency { get; }

    /// <summary>
    /// Executes each partition exactly once. Implementations may schedule
    /// partitions concurrently up to <see cref="MaximumConcurrency"/>.
    /// </summary>
    public abstract void ExecutePartitions(
        int partitionCount,
        Action<int> executePartition);
}
