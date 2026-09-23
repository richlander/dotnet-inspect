using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    LibraryBodyAnalysisResult PublishResourceOwnership(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan,
        LibraryMethodAnalysisResult[] methodResults)
    {
        if (!plan.IncludesResourceOccurrences)
            return analysis;

        IReadOnlyDictionary<int, ResourceOccurrenceAnalysisResult>
            occurrencesByMethod =
                (analysis.ResourceOccurrences?.Methods ?? [])
                    .ToDictionary(
                        static result => result.Method.MetadataToken);
        var summaries =
            ImmutableArray.CreateBuilder<ResourceOwnershipMethodSummary>();
        foreach (MethodBodyAnalysisContext context in methodResults
            .Select(static result => result.ResourceOccurrenceContext)
            .Where(static context => context is not null)
            .Select(static context => context!)
            .OrderBy(static context =>
                context.Method.MetadataToken))
        {
            if (!occurrencesByMethod.TryGetValue(
                    context.Method.MetadataToken,
                    out ResourceOccurrenceAnalysisResult? occurrences))
            {
                if (analysis.ResourceOccurrences is not
                    { Limitations.IsEmpty: true })
                {
                    summaries.Add(
                        ResourceOwnershipSummaryAnalysis.Unavailable(
                            context));
                    continue;
                }

                occurrences = new(
                    context.Method,
                    [],
                    [],
                    []);
            }

            summaries.Add(
                ResourceOwnershipSummaryAnalysis.Analyze(
                    context,
                    occurrences,
                    analysis.Methods.DirectCalls));
        }

        return analysis with
        {
            ResourceOwnership =
                new(
                    summaries.ToImmutable(),
                    analysis.ResourceOccurrences?.Limitations ?? []),
        };
    }
}
