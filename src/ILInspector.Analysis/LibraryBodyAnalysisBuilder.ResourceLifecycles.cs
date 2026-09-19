using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    LibraryBodyAnalysisResult PublishResourceLifecycles(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan,
        LibraryMethodAnalysisResult[] methodResults)
    {
        if (!plan.IncludesResourceLifecycle)
            return analysis;

        ResourceOccurrenceLibraryAnalysisResult occurrences =
            analysis.ResourceOccurrences
            ?? throw new InvalidOperationException(
                "Resource Lifecycle Analysis requires Resource Occurrence results.");
        ImmutableArray<ResourceLifecycleLimitation> libraryLimitations =
        [
            .. occurrences.Limitations
                .Where(limitation =>
                    limitation.Method is null
                    && ResourceLifecycleAnalysisService
                        .IsLifecycleBlocking(limitation))
                .Select(limitation =>
                    new ResourceLifecycleLimitation(
                        ResourceLifecycleLimitationKind.OccurrenceAnalysis,
                        limitation.Message)
                    {
                        OccurrenceLimitation = limitation,
                    }),
        ];
        var results =
            ImmutableArray.CreateBuilder<
                ResourceLifecycleMethodAnalysisResult>(
                    occurrences.Methods.Length);
        foreach (ResourceOccurrenceAnalysisResult methodOccurrences in
            occurrences.Methods.OrderBy(result =>
                result.Method.MetadataToken))
        {
            MethodBodyAnalysisContext? context = methodResults
                .Select(result => result.ResourceOccurrenceContext)
                .FirstOrDefault(candidate =>
                    candidate?.Method == methodOccurrences.Method);
            if (context is null)
            {
                results.Add(
                    new(
                        methodOccurrences.Method,
                        [],
                        [
                            new ResourceLifecycleLimitation(
                                ResourceLifecycleLimitationKind.ControlFlow,
                                "The exact method-body context is unavailable.")
                            {
                                Method = methodOccurrences.Method,
                            },
                        ]));
                continue;
            }

            IReadOnlySet<MethodExceptionClauseId> catchAllCleanup;
            try
            {
                catchAllCleanup = CatchAllCleanup(context);
            }
            catch (Exception ex)
                when (LeakTriageAnalyzer.IsRecoverable(ex))
            {
                results.Add(
                    new(
                        methodOccurrences.Method,
                        [],
                        [
                            new ResourceLifecycleLimitation(
                                ResourceLifecycleLimitationKind
                                    .CatchTypeResolution,
                                $"{ex.GetType().Name}: {ex.Message}")
                            {
                                Method = methodOccurrences.Method,
                            },
                        ]));
                continue;
            }

            try
            {
                results.Add(
                    ResourceLifecycleAnalysisService.Analyze(
                        context,
                        methodOccurrences,
                        analysis.Methods.DirectCalls,
                        catchAllCleanup));
            }
            catch (Exception ex)
                when (LeakTriageAnalyzer.IsRecoverable(ex))
            {
                results.Add(
                    new(
                        methodOccurrences.Method,
                        [],
                        [
                            new ResourceLifecycleLimitation(
                                ResourceLifecycleLimitationKind
                                    .ExceptionFlow,
                                $"{ex.GetType().Name}: {ex.Message}")
                            {
                                Method = methodOccurrences.Method,
                            },
                        ]));
            }
        }

        return analysis with
        {
            ResourceLifecycles = new(
                results.ToImmutable(),
                libraryLimitations),
        };
    }

    IReadOnlySet<MethodExceptionClauseId> CatchAllCleanup(
        MethodBodyAnalysisContext context)
    {
        MethodDefinitionHandle methodHandle =
            MetadataTokens.MethodDefinitionHandle(
                context.Method.MetadataToken);
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        TypeDefinition type =
            _reader.GetTypeDefinition(method.GetDeclaringType());
        GenericScope scope =
            _primaryMetadataResolver.CreateScope(type, method);
        return ArrayPoolExceptionPathAnalyzer
            .ComputeCreditableCatchCleanup(
                context.RequireExceptionCatalog(),
                token =>
                    ArrayPoolExceptionPathAnalyzer.ResolveCatchTypeRef(
                        _reader,
                        MetadataTokens.EntityHandle(token),
                        scope));
    }
}
