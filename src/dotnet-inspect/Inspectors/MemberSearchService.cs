using System.Collections.Immutable;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Searches member names across the same ordered sources as type search through
/// workspace-backed typed queries. Member search is exact/glob only — there is
/// no fuzzy or namespace-prefix fallback — so the collected matches are the
/// final results. Resolution and streaming early-exit for a result limit are shared via
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
        using var workspace = new AssemblySetInspectionWorkspace();
        return await CollectMembersAsync(
            options,
            patterns,
            logger,
            httpClient,
            workspace);
    }

    /// <summary>
    /// Resolves configured sources and searches their members through typed
    /// participant queries. With a result limit, sources stream one at a time
    /// and later sources are not resolved after the limit is met.
    /// </summary>
    private static async Task<List<MemberFindResult>> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        HttpClient httpClient,
        AssemblySetInspectionWorkspace workspace)
    {
        List<MemberFindResult> results = [];

        bool ReachedLimit() => options.Limit.HasValue && results.Count >= options.Limit.Value;

        async Task CollectAndScanAsync(AssemblySetRequest request)
        {
            using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);

            workspace.RunPerAssembly(
                assemblySet,
                AssemblyContextMemberMatchesQuery.Definition,
                group => AssemblyContextMemberMatchesQuery.Execute(
                    group,
                    patterns,
                    options.IncludeAll,
                    options.Limit.HasValue
                        ? options.Limit.Value - results.Count
                        : null),
                (assembly, entry) =>
                {
                    switch (entry)
                    {
                        case AssemblyContextEntry<
                            ImmutableArray<
                                MemberSearchResult>>.Available available:
                            foreach (MemberSearchResult member
                                in available.Value)
                            {
                                results.Add(new MemberFindResult
                                {
                                    Pattern = member.Pattern,
                                    Match = member.IsGlob
                                        ? MatchKind.Glob
                                        : MatchKind.Exact,
                                    Member = member.MemberName,
                                    Kind = member.Kind,
                                    DeclaringType = member.DeclaringType,
                                    Namespace =
                                        member.DeclaringNamespace ?? "",
                                    Signature = member.Signature,
                                    ReturnType = member.ReturnType,
                                    Library =
                                        Path.GetFileNameWithoutExtension(
                                            assembly.Path),
                                    Source = assembly.Source,
                                    SourceVersion = assembly.Version,
                                });
                            }
                            break;
                        case AssemblyContextEntry<
                            ImmutableArray<
                                MemberSearchResult>>.Rejected rejected:
                            logger.LogWarning(
                                $"Could not read {assembly.Path}: {rejected.Failure.Detail}");
                            break;
                        case AssemblyContextEntry<
                            ImmutableArray<
                                MemberSearchResult>>.Failed failed:
                            logger.LogWarning(
                                $"Could not read {assembly.Path}: {failed.Error.Message}");
                            break;
                    }
                },
                (assembly, failure) =>
                    logger.LogWarning(
                        $"Could not read {assembly.Path}: {failure}"),
                ReachedLimit);
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
