using System.Collections.Immutable;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Inspectors;

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
    public static async Task<FindSearchResult<MemberFindResult>> FindMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        AssemblySetRequest request =
            FindSourceCollector.BuildFindRequest(options);
        if (ConfiguredPackageSearchWorkspace.IsEligible(
                options.SourceSelection,
                request,
                options.Tfm,
                resultLimit:
                    options.TypeFilter is null
                        ? options.Limit
                        : null))
        {
            await using ConfiguredPackageSearchWorkspace? configured =
                await ConfiguredPackageSearchWorkspace.OpenAsync(
                    httpClient,
                    request,
                    options.Tfm!,
                    logger.Log,
                    cancellationToken);
            List<MemberFindResult> configuredResults =
                configured is null
                ? []
                : await CollectMembersAsync(
                    options,
                    patterns,
                    logger,
                    configured,
                    MarkFailure,
                    cancellationToken);
            if (configured is null)
                MarkFailure();
            return new(configuredResults, hasFailures);
        }

        using var workspace = new AssemblySetInspectionWorkspace();
        return new(
            await CollectMembersAsync(
                options,
                patterns,
                logger,
                httpClient,
                workspace,
                MarkFailure),
            hasFailures);
    }

    /// <summary>
    /// Resolves configured sources and searches their members through typed
    /// participant queries.
    /// </summary>
    private static async Task<List<MemberFindResult>> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        HttpClient httpClient,
        AssemblySetInspectionWorkspace workspace,
        Action markFailure)
    {
        List<MemberFindResult> results = [];
        int? operationalLimit =
            options.TypeFilter is null
                ? options.Limit
                : null;
        bool ReachedLimit() =>
            operationalLimit is int limit
            && results.Count >= limit;

        async Task CollectAndScanAsync(AssemblySetRequest request)
        {
            using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);
            if (assemblySet.Diagnostics.Count > 0)
                markFailure();

            workspace.RunPerAssembly(
                assemblySet,
                AssemblyContextMemberMatchesQuery.Definition,
                group => AssemblyContextMemberMatchesQuery.Execute(
                    group,
                    patterns,
                    options.IncludeAll,
                    operationalLimit is int limit
                        ? limit - results.Count
                        : null),
                (assembly, entry) => AddMembers(
                    results,
                    options.TypeFilter,
                    SearchAssemblySource.FromAssemblySet(assembly),
                    entry,
                    logger,
                    markFailure),
                (assembly, failure) =>
                {
                    markFailure();
                    CommandError.WriteWarning(
                        $"Could not read {assembly.Path}: {failure}");
                },
                ReachedLimit);
        }

        if (operationalLimit is not null)
        {
            await FindSourceCollector.StreamSourcesAsync(
                options,
                ReachedLimit,
                CollectAndScanAsync);
            return results;
        }

        await CollectAndScanAsync(FindSourceCollector.BuildFindRequest(options));
        if (options.Limit.HasValue
            && results.Count > options.Limit.Value)
        {
            results = results.Take(options.Limit.Value).ToList();
        }
        return results;
    }

    private static async Task<List<MemberFindResult>> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        ConfiguredPackageSearchWorkspace workspace,
        Action markFailure,
        CancellationToken cancellationToken)
    {
        List<MemberFindResult> results = [];
        ConfiguredPackageSearchQueryResult<
            AssemblyContextResult<AssemblyMemberMatches>>? execution =
                await workspace.QuerySurfaceAsync(
                    context =>
                        AssemblyContextMemberMatchesQuery.Execute(
                            context.Group,
                            patterns,
                            options.IncludeAll),
                    cancellationToken);
        if (execution is null)
        {
            markFailure();
            return results;
        }
        if (execution.Sources is not { } sources
            || execution.Result is not { } queryResult)
        {
            return results;
        }

        foreach (AssemblyContextEntry<AssemblyMemberMatches> entry
            in queryResult.Assemblies)
        {
            AddMembers(
                results,
                options.TypeFilter,
                sources.SourceFor(entry.Subject),
                entry,
                logger,
                markFailure);
        }
        if (options.Limit.HasValue
            && results.Count > options.Limit.Value)
        {
            results = results.Take(options.Limit.Value).ToList();
        }
        return results;
    }

    private static void AddMembers(
        List<MemberFindResult> results,
        string? typeFilter,
        SearchAssemblySource assembly,
        AssemblyContextEntry<AssemblyMemberMatches> entry,
        VerboseLogger logger,
        Action markFailure)
    {
        switch (entry)
        {
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Available available:
                foreach (MemberSearchResult member
                    in available.Value.Members)
                {
                    if (typeFilter is not null
                        && !TypeMatcher.MatchesTypeFilter(
                            member.DeclaringType,
                            typeFilter))
                    {
                        continue;
                    }

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
                        Library = assembly.Library,
                        Source = assembly.Source,
                        SourceVersion = assembly.SourceVersion,
                    });
                }
                WriteInspectionFailures(
                    assembly,
                    available.Value.InspectionFailures,
                    logger,
                    markFailure);
                break;
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Rejected rejected:
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.DiagnosticSubject}: "
                    + rejected.Failure.Detail);
                break;
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Failed failed:
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.DiagnosticSubject}: "
                    + failed.Error.Message);
                break;
        }
    }

    private static void WriteInspectionFailures(
        SearchAssemblySource assembly,
        ImmutableArray<ApiSurfaceInspectionFailure> failures,
        VerboseLogger logger,
        Action markFailure)
    {
        if (failures.IsEmpty)
            return;

        markFailure();
        CommandError.WriteWarning(
            $"Member search in {assembly.DiagnosticSubject} skipped "
            + $"{failures.Length} metadata row(s).");
        foreach (ApiSurfaceInspectionFailure failure in failures)
        {
            logger.LogWarning(
                $"Member search rejected {failure.Operation} at 0x{failure.SubjectToken:X8}: "
                + $"{failure.Kind}: {failure.Detail}");
        }
    }
}
