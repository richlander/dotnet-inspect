using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Exact Method-source semantic evidence selected for IL-coordinate projection.
/// </summary>
public sealed record ILOffsetSemanticAnalysisResult(
    ILOffsetAllocationAnalysisInput? Allocation,
    ILOffsetSafetyAnalysisInput? Safety,
    ILOffsetCostAnalysisInput? Cost,
    MethodDefinitionSourceGroupReceipt SourceGroupReceipt);

/// <summary>
/// Plans selected IL-coordinate semantic products as one QuerySpace request
/// set over an already-open assembly image.
/// </summary>
public static class ILOffsetSemanticAnalysis
{
    public static ILOffsetSemanticAnalysisResult Execute(
        AssemblyInspectionSession session,
        string subjectName,
        Guid moduleVersionId,
        IEnumerable<int> methodTokens,
        ILOffsetProjectionCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentNullException.ThrowIfNull(methodTokens);
        cancellationToken.ThrowIfCancellationRequested();

        ExactMethodSemanticProducts products =
            ExactMethodSemanticProducts.None;
        if (Includes(
            capabilities,
            ILOffsetProjectionCapabilities.AllocationContext))
        {
            products |= ExactMethodSemanticProducts.Allocation;
        }
        if (Includes(
            capabilities,
            ILOffsetProjectionCapabilities.SafetyContext))
        {
            products |= ExactMethodSemanticProducts.Safety;
        }
        if (Includes(
            capabilities,
            ILOffsetProjectionCapabilities.CostContext))
        {
            products |= ExactMethodSemanticProducts.Calls;
        }

        ExactMethodSemanticAnalysisResult analysis =
            ExactMethodSemanticAnalysis.Execute(
                session,
                subjectName,
                methodTokens,
                products,
                cancellationToken);

        return new(
            analysis.Allocation is { } allocation
                ? AllocationInput(
                    allocation,
                    moduleVersionId)
                : null,
            analysis.Safety is { } safety
                ? SafetyInput(
                    safety,
                    moduleVersionId)
                : null,
            analysis.Calls is { } calls
                ? CostInput(
                    calls,
                    moduleVersionId)
                : null,
            analysis.SourceGroupReceipt);
    }

    static ILOffsetAllocationAnalysisInput AllocationInput(
        ExactMethodSemanticLane<
            ExactMethodAllocationProducerResult> lane,
        Guid moduleVersionId)
    {
        return new(
            moduleVersionId,
            lane.Result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.Allocations),
            [
                .. lane.Result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            lane.SourceReceipt,
            lane.WorkReceipt);
    }

    static ILOffsetSafetyAnalysisInput SafetyInput(
        ExactMethodSemanticLane<
            ExactMethodAnalysisProducerResult> lane,
        Guid moduleVersionId)
    {
        return new(
            moduleVersionId,
            lane.Result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.UnsafeOccurrences),
            lane.Result.Bodies.ToDictionary(
                static body => body.MethodToken,
                static body => body.SafetyEvidence),
            [
                .. lane.Result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            lane.SourceReceipt,
            lane.WorkReceipt);
    }

    static ILOffsetCostAnalysisInput CostInput(
        ExactMethodSemanticLane<
            FocusedMethodCallProducerResult> lane,
        Guid moduleVersionId)
    {
        return new(
            moduleVersionId,
            lane.Result.Bodies
                .SelectMany(static body => body.Calls)
                .GroupBy(static site => site.Identity.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(static site => site.Call)
                        .ToImmutableArray()),
            [
                .. lane.Result.Bodies.SelectMany(
                    static body => body.Diagnostics),
            ],
            lane.SourceReceipt,
            lane.WorkReceipt);
    }

    static bool Includes(
        ILOffsetProjectionCapabilities capabilities,
        ILOffsetProjectionCapabilities capability) =>
        (capabilities & capability) != 0;
}
