using ILInspector.Analysis;
using ILInspector.Analysis.Planning;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact-member Calls-row Count outcome.
/// </summary>
public abstract record MemberCallSiteCountResult
{
    private MemberCallSiteCountResult()
    {
    }

    /// <summary>Count completed for every admitted physical body.</summary>
    public sealed record Available(
        int Count,
        MemberCallCountAnalysis Analysis)
        : MemberCallSiteCountResult;

    /// <summary>At least one admitted physical body did not issue a count.</summary>
    public sealed record Incomplete(
        MemberCallCountAnalysis Analysis)
        : MemberCallSiteCountResult;

    /// <summary>Count acquisition or Analysis failed.</summary>
    public sealed record Failed(Exception Error)
        : MemberCallSiteCountResult;
}

/// <summary>
/// Lowers one exact logical member to source-native physical-body Calls-row
/// counts.
/// </summary>
public static class MemberCallSiteCountQuery
{
    public static ImplementationMetricWorkLimits DefaultLimits { get; } =
        MemberDirectCallCountQuery.DefaultLimits;

    public static MemberCallSiteCountResult Execute(
        string assemblyPath,
        int methodToken) =>
        Execute(
            assemblyPath,
            methodToken,
            DefaultLimits);

    public static MemberCallSiteCountResult Execute(
        string assemblyPath,
        int methodToken,
        ImplementationMetricWorkLimits limits)
    {
        MemberCallCountExecution execution =
            MemberCallCountQuery.Execute(
                assemblyPath,
                methodToken,
                limits,
                MethodCallCountProducer.CallSites,
                "MemberCallSiteCount");
        return execution.Status switch
        {
            MemberCallCountExecutionStatus.Available =>
                new MemberCallSiteCountResult.Available(
                    execution.Count,
                    execution.Analysis!),
            MemberCallCountExecutionStatus.Incomplete =>
                new MemberCallSiteCountResult.Incomplete(
                    execution.Analysis!),
            _ => new MemberCallSiteCountResult.Failed(
                execution.Error!),
        };
    }
}
