using ILInspector.Analysis;

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
        LibraryDirectCallCountAnalysisResult Analysis)
        : MemberDirectCallCountResult;

    /// <summary>At least one admitted physical body did not issue a count.</summary>
    public sealed record Incomplete(
        LibraryDirectCallCountAnalysisResult Analysis)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(limits);

        try
        {
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecutePath(
                    assemblyPath,
                    LibraryBodyAnalysisRequest
                        .CreateDirectCallCounts(
                            limits,
                            new HashSet<int> { methodToken }));
            LibraryDirectCallCountAnalysisResult analysis =
                execution.DirectCallCounts;
            if (!analysis.IsComplete)
            {
                return new MemberDirectCallCountResult
                    .Incomplete(analysis);
            }

            int count = 0;
            foreach (MethodDirectCallCountEvidence body
                in analysis.Counts)
            {
                if (body.Method.MetadataToken == methodToken)
                    count = checked(count + body.Count);
            }
            return new MemberDirectCallCountResult.Available(
                count,
                analysis);
        }
        catch (Exception ex)
        {
            return new MemberDirectCallCountResult.Failed(ex);
        }
    }
}
