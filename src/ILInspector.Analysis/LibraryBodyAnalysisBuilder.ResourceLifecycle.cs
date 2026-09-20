using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    LibraryBodyAnalysisResult PublishResourceLifecycle(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan,
        LibraryMethodAnalysisResult[] methodResults)
    {
        if (!plan.IncludesResourceLifecycle)
            return analysis;

        if (analysis.ResourceOccurrences is not { } occurrenceLibrary)
        {
            return analysis with
            {
                ResourceLifecycle = new(
                    [],
                    [
                        new ResourceLifecycleLimitation(
                            ResourceLifecycleLimitationKind
                                .ResourceOccurrence,
                            "Resource Occurrence Analysis did not publish "
                            + "its lifecycle prerequisite."),
                    ]),
            };
        }

        ImmutableArray<ResourceLifecycleLimitation> limitations =
        [
            .. occurrenceLibrary.Limitations.Select(limitation =>
                new ResourceLifecycleLimitation(
                    ResourceLifecycleLimitationKind.ResourceOccurrence,
                    limitation.Message,
                    limitation.Method,
                    limitation.Root,
                    limitation)),
        ];
        var methods =
            ImmutableArray.CreateBuilder<ResourceLifecycleMethodResult>(
                occurrenceLibrary.Methods.Length);
        foreach (ResourceOccurrenceAnalysisResult occurrences
            in occurrenceLibrary.Methods)
        {
            MethodBodyAnalysisContext? context = methodResults
                .Select(result => result.ResourceOccurrenceContext)
                .FirstOrDefault(candidate =>
                    candidate?.Method == occurrences.Method);
            if (context is null)
            {
                methods.Add(new(
                    occurrences.Method,
                    [],
                    [
                        new ResourceLifecycleLimitation(
                            ResourceLifecycleLimitationKind
                                .AcquisitionFlow,
                            "The same-execution method-body context was "
                            + "not retained.",
                            occurrences.Method),
                    ]));
                continue;
            }

            try
            {
                methods.Add(ResourceLifecycleAnalysisService.Analyze(
                    context,
                    occurrences,
                    analysis.Methods.DirectCalls,
                    token => TypeFromEntity(
                        MetadataTokens.EntityHandle(token))));
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException
                    or OverflowException
                    or IndexOutOfRangeException)
            {
                methods.Add(new(
                    occurrences.Method,
                    [],
                    [
                        new ResourceLifecycleLimitation(
                            ResourceLifecycleLimitationKind
                                .UnsupportedFlow,
                            $"{ex.GetType().Name}: {ex.Message}",
                            occurrences.Method),
                    ]));
            }
        }

        return analysis with
        {
            ResourceLifecycle = new(
                methods.ToImmutable(),
                limitations),
        };
    }
}
