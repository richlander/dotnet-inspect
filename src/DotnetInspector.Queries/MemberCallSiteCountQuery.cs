using ILInspector.Analysis;

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
        LibraryCallSiteCountAnalysisResult Analysis)
        : MemberCallSiteCountResult;

    /// <summary>At least one admitted physical body did not issue a count.</summary>
    public sealed record Incomplete(
        LibraryCallSiteCountAnalysisResult Analysis)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(limits);

        try
        {
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecutePath(
                    assemblyPath,
                    LibraryBodyAnalysisRequest
                        .CreateCallSiteCounts(
                            limits,
                            new HashSet<int> { methodToken }));
            LibraryCallSiteCountAnalysisResult analysis =
                execution.CallSiteCounts;
            if (!analysis.IsComplete)
            {
                return new MemberCallSiteCountResult
                    .Incomplete(analysis);
            }

            int count = 0;
            foreach (MethodCallSiteCountEvidence body
                in analysis.Counts)
            {
                if (body.Method.MetadataToken == methodToken)
                    count = checked(count + body.Count);
            }
            return new MemberCallSiteCountResult.Available(
                count,
                analysis);
        }
        catch (Exception ex)
        {
            return new MemberCallSiteCountResult.Failed(ex);
        }
    }
}
