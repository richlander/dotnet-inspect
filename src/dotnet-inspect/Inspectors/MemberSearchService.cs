using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Searches member names across the same six ordered sources as type search (packages, libraries,
/// platform assemblies/frameworks, projects, and bin directories), routed through the closed-set
/// <see cref="Corpus.SearchMembers"/>. Member search is exact/glob only — there is no fuzzy or
/// namespace-prefix fallback — so the collected matches are the final results. Resolution and the
/// streaming early-exit for a result limit are shared with type search via
/// <see cref="FindSourceCollector"/>.
/// </summary>
internal static class MemberSearchService
{
    /// <summary>
    /// Finds members matching one or more name patterns, returning flat results carrying the
    /// provenance of the assembly that supplied each match. This is the entry point for the
    /// <c>find --members</c> lens (and the leading-dot shortcut).
    /// </summary>
    public static async Task<List<MemberFindResult>> FindMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        return await CollectMembersAsync(options, patterns, logger, httpClient);
    }

    /// <summary>
    /// Resolves the configured sources and searches their members through the corpus. When a result
    /// limit is active the sources are streamed one at a time (later sources are never resolved once
    /// the limit is met); otherwise the whole set is resolved once and scanned together.
    /// </summary>
    private static async Task<List<MemberFindResult>> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        List<MemberFindResult> results = [];

        bool ReachedLimit() => options.Limit.HasValue && results.Count >= options.Limit.Value;

        async Task CollectAndScanAsync(AssemblySetRequest request)
        {
            using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);

            // Streaming caps each source at the remaining budget so later sources are never resolved
            // once the limit is met; the unbounded path passes null so the corpus scans the whole set.
            int? remaining = options.Limit.HasValue
                ? options.Limit.Value - results.Count
                : null;

            var corpus = CorpusProducer.ToCorpus(assemblySet);
            var outcome = corpus.SearchMembers(patterns, options.IncludeAll, remaining);

            foreach (var skippedPath in outcome.SkippedAssemblies)
                logger.Log($"Warning: Could not read {skippedPath}");

            foreach (var match in outcome.Results)
            {
                var member = match.Member;
                results.Add(new MemberFindResult
                {
                    Pattern = member.Pattern,
                    Match = member.IsGlob ? MatchKind.Glob : MatchKind.Exact,
                    Member = member.MemberName,
                    Kind = member.Kind,
                    DeclaringType = member.DeclaringType,
                    Namespace = member.DeclaringNamespace ?? "",
                    Signature = member.Signature,
                    ReturnType = member.ReturnType,
                    Library = member.Assembly,
                    Source = match.Source ?? "",
                    SourceVersion = match.Version,
                });
            }
        }

        if (options.Limit.HasValue)
        {
            await FindSourceCollector.StreamSourcesAsync(options, ReachedLimit, CollectAndScanAsync);
            return results;
        }

        await CollectAndScanAsync(FindSourceCollector.BuildFindRequest(options));
        return results;
    }
}
