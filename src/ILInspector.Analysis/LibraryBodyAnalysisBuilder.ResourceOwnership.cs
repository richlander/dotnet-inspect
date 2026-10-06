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
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            directCallsByEvidenceMethod =
                DirectCallIncidence.ByEvidenceMethod(
                    analysis.Methods.DirectCalls);
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
                if (analysis.ResourceOccurrences is not { } occurrenceLibrary
                    || occurrenceLibrary.Limitations.Any(
                        static limitation => limitation.Method is null))
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

            directCallsByEvidenceMethod.TryGetValue(
                context.Method.MetadataToken,
                out ImmutableArray<DirectCall> directCalls);
            summaries.Add(
                ResourceOwnershipSummaryAnalysis.Analyze(
                    context,
                    occurrences,
                    directCalls.IsDefault ? [] : directCalls));
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
