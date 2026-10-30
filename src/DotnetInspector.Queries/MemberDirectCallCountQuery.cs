using ILInspector.Analysis;
using ILInspector.Analysis.Planning;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact-member direct-invocation Count outcome.
/// </summary>
public abstract record MemberDirectCallCountResult
{
    private MemberDirectCallCountResult()
    {
    }

    /// <summary>Count completed for every admitted physical body.</summary>
    public sealed record Available(
        int Count,
        MemberCallCountAnalysis Analysis)
        : MemberDirectCallCountResult;

    /// <summary>At least one admitted physical body did not issue a count.</summary>
    public sealed record Incomplete(
        MemberCallCountAnalysis Analysis)
        : MemberDirectCallCountResult;

    /// <summary>Count acquisition or Analysis failed.</summary>
    public sealed record Failed(Exception Error)
        : MemberDirectCallCountResult;
}

/// <summary>
/// Lowers one exact logical member to source-native physical-body invocation
/// counts.
/// </summary>
public static class MemberDirectCallCountQuery
{
    public static ImplementationMetricWorkLimits DefaultLimits { get; } =
        new(
            maximumPhysicalBodies: 10_000,
            maximumEncodedIlBytes: 256L * 1024 * 1024,
            maximumAttributionProbeBodies: 1_000_000,
            maximumAttributionProbeIlBytes: 1024L * 1024 * 1024);

    public static MemberDirectCallCountResult Execute(
        string assemblyPath,
        int methodToken) =>
        Execute(
            assemblyPath,
            methodToken,
            DefaultLimits);

    public static MemberDirectCallCountResult Execute(
        string assemblyPath,
        int methodToken,
        ImplementationMetricWorkLimits limits)
    {
        MemberCallCountExecution execution =
            MemberCallCountQuery.Execute(
                assemblyPath,
                methodToken,
                limits,
                MethodCallCountProducer.DirectInvocations,
                "MemberDirectCallCount");
        return execution.Status switch
        {
            MemberCallCountExecutionStatus.Available =>
                new MemberDirectCallCountResult.Available(
                    execution.Count,
                    execution.Analysis!),
            MemberCallCountExecutionStatus.Incomplete =>
                new MemberDirectCallCountResult.Incomplete(
                    execution.Analysis!),
            _ => new MemberDirectCallCountResult.Failed(
                execution.Error!),
        };
    }
}
