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
/// workspace-backed typed queries. Member search is direct/glob only — there is
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
        CancellationToken cancellationToken = default,
        PlatformFindSearchWorkspace? platformWorkspace = null,
        ExplicitFindSearchWorkspace? explicitWorkspace = null)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        if (platformWorkspace is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collector = new MemberResultCollector(options);
            if (collector.RequiresParticipantStop)
            {
                _ = platformWorkspace.QueryMembersEach(
                    patterns,
                    options.IncludeAll,
                    entry =>
                        collector.Add(
                            platformWorkspace.SourceFor(entry.Subject),
                            entry,
                            logger,
                            MarkFailure),
                    collector.ReachedLimit);
            }
            else
            {
                MemberSearchWindow? inputWindow =
                    collector.MetadataWindow();
                AssemblyContextResult<AssemblyMemberMatches> queryResult =
                    inputWindow is null
                        ? platformWorkspace.QueryMembers(
                            patterns,
                            options.IncludeAll,
                            collector.QueryLimit())
                        : platformWorkspace.QueryMemberWindow(
                            patterns,
                            options.IncludeAll,
                            inputWindow);
                foreach (AssemblyContextEntry<AssemblyMemberMatches> entry
                    in queryResult.Assemblies)
                {
                    collector.Add(
                        platformWorkspace.SourceFor(entry.Subject),
                        entry,
                        logger,
                        MarkFailure);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return collector.ToSearchResult(
                hasFailures,
                options.PackagePrefixLimitReached);
        }

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
                    cancellationToken,
                    FindSourceCollector.CreateWorkspacePlan(options));
            MemberResultCollector collector =
                configured is null
                ? new(options)
                : await CollectMembersAsync(
                    options,
                    patterns,
                    logger,
                    configured,
                    MarkFailure,
                    cancellationToken);
            if (configured is null)
                MarkFailure();
            return collector.ToSearchResult(
                hasFailures,
                options.PackagePrefixLimitReached);
        }

        if (explicitWorkspace is not null)
        {
            MemberResultCollector collector =
                await CollectMembersAsync(
                    options,
                    patterns,
                    logger,
                    explicitWorkspace,
                    MarkFailure);
            return collector.ToSearchResult(
                hasFailures,
                options.PackagePrefixLimitReached);
        }

        await using var ownedWorkspace =
            new ExplicitFindSearchWorkspace(
                options,
                httpClient,
                logger.Log,
                cancellationToken);
        MemberResultCollector ownedCollector =
            await CollectMembersAsync(
                options,
                patterns,
                logger,
                ownedWorkspace,
                MarkFailure);
        return ownedCollector.ToSearchResult(
            hasFailures,
            options.PackagePrefixLimitReached);
    }

    /// <summary>
    /// Resolves configured sources and searches their members through typed
    /// participant queries.
    /// </summary>
    private static async Task<MemberResultCollector> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        ExplicitFindSearchWorkspace workspace,
        Action markFailure)
    {
        var collector = new MemberResultCollector(options);

        await workspace.RunPerAssemblyAsync(
            AssemblyContextMemberMatchesQuery.Definition,
            group =>
            {
                MemberSearchWindow? inputWindow =
                    collector.MetadataWindow();
                return inputWindow is null
                    ? AssemblyContextMemberMatchesQuery.Execute(
                        group,
                        patterns,
                        options.IncludeAll,
                        collector.QueryLimit())
                    : AssemblyContextMemberMatchesQuery.ExecuteWindow(
                        group,
                        patterns,
                        inputWindow,
                        options.IncludeAll);
            },
            (assembly, entry) =>
            {
                collector.Add(
                    SearchAssemblySource.FromAssemblySet(assembly),
                    entry,
                    logger,
                    markFailure);
            },
            (assembly, failure) =>
            {
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.Path}: {failure}");
            },
            markFailure,
            options.Limit is not null ? collector.ReachedLimit : null);
        return collector;
    }

    private static async Task<MemberResultCollector> CollectMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        ConfiguredPackageSearchWorkspace workspace,
        Action markFailure,
        CancellationToken cancellationToken)
    {
        var collector = new MemberResultCollector(options);
        ConfiguredPackageSearchQueryResult<
            MemberSearchQueryReceipt>? execution =
                await workspace.QuerySurfaceAsync(
                    context =>
                    {
                        if (collector.RequiresParticipantStop)
                        {
                            _ = AssemblyContextMemberMatchesQuery.ExecuteEach(
                                context.Group,
                                patterns,
                                options.IncludeAll,
                                entry =>
                                    collector.Add(
                                        context.Sources.SourceFor(
                                            entry.Subject),
                                        entry,
                                        logger,
                                        markFailure),
                                collector.ReachedLimit);
                            return MemberSearchQueryReceipt.Instance;
                        }

                        MemberSearchWindow? inputWindow =
                            collector.MetadataWindow();
                        AssemblyContextResult<AssemblyMemberMatches>
                            queryResult =
                                inputWindow is null
                                    ? AssemblyContextMemberMatchesQuery.Execute(
                                        context.Group,
                                        patterns,
                                        options.IncludeAll,
                                        collector.QueryLimit())
                                    : AssemblyContextMemberMatchesQuery
                                        .ExecuteWindow(
                                            context.Group,
                                            patterns,
                                            inputWindow,
                                            options.IncludeAll);
                        foreach (AssemblyContextEntry<AssemblyMemberMatches>
                            entry in queryResult.Assemblies)
                        {
                            collector.Add(
                                context.Sources.SourceFor(entry.Subject),
                                entry,
                                logger,
                                markFailure);
                        }
                        return MemberSearchQueryReceipt.Instance;
                    },
                    cancellationToken);
        if (execution is null)
        {
            markFailure();
            return collector;
        }
        if (execution.Result is null)
            return collector;
        return collector;
    }

    private sealed class MemberSearchQueryReceipt
    {
        internal static MemberSearchQueryReceipt Instance { get; } = new();
    }

    private sealed class MemberResultCollector
    {
        private readonly string? _typeFilter;
        private readonly int? _limit;
        private readonly FindInputRowSelection? _inputRows;

        internal MemberResultCollector(FindOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _typeFilter = options.TypeFilter;
            _limit = options.Limit;
            _inputRows = options.InputRows;
        }

        internal List<MemberFindResult> Results { get; } = [];

        internal int AcceptedCount { get; private set; }

        internal bool RequiresParticipantStop =>
            _typeFilter is not null
            && _limit is not null;

        internal bool ReachedLimit() =>
            _limit is int limit
            && AcceptedCount >= limit;

        internal int? QueryLimit() =>
            _typeFilter is null
            && _limit is int limit
                ? Math.Max(0, limit - AcceptedCount)
                : null;

        internal MemberSearchWindow? MetadataWindow()
        {
            if (_typeFilter is not null
                || _inputRows is null
                || _limit is not int limit)
            {
                return null;
            }

            int start = Math.Max(
                1,
                _inputRows.Start - AcceptedCount);
            int end = limit - AcceptedCount;
            return start <= end
                ? new(start, end)
                : null;
        }

        internal void Add(
            SearchAssemblySource assembly,
            AssemblyContextEntry<AssemblyMemberMatches> entry,
            VerboseLogger logger,
            Action markFailure)
        {
            switch (entry)
            {
                case AssemblyContextEntry<
                    AssemblyMemberMatches>.Available available:
                    AddAvailable(
                        assembly,
                        available.Value);
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

        internal FindSearchResult<MemberFindResult> ToSearchResult(
            bool hasFailures,
            bool sourceSelectionIncomplete) =>
            new(Results, hasFailures)
            {
                SourceSelectionIncomplete = sourceSelectionIncomplete,
                Completion =
                    ReachedLimit()
                        ? FindSearchCompletion.ResultLimitReached
                        : hasFailures || sourceSelectionIncomplete
                            ? FindSearchCompletion.Incomplete
                            : FindSearchCompletion.Exhausted,
                InputRows =
                    _inputRows is null
                        ? null
                        : new(_inputRows, AcceptedCount),
            };

        private void AddAvailable(
            SearchAssemblySource assembly,
            AssemblyMemberMatches matches)
        {
            int skippedProjectionCount =
                matches.AcceptedCount - matches.Members.Length;
            if (skippedProjectionCount < 0)
            {
                throw new InvalidOperationException(
                    "Metadata Member search accepted fewer matches than it "
                    + "projected.");
            }
            if (skippedProjectionCount > 0)
            {
                if (_typeFilter is not null)
                {
                    throw new InvalidOperationException(
                        "Metadata Member Window execution cannot precede "
                        + "the declaring-Type filter.");
                }
                AcceptedCount += skippedProjectionCount;
            }

            foreach (MemberSearchResult member in matches.Members)
            {
                if (ReachedLimit())
                    break;
                if (_typeFilter is not null
                    && !TypeMatcher.MatchesTypeFilter(
                        member.DeclaringType,
                        _typeFilter))
                {
                    continue;
                }
                AcceptedCount++;
                if (_inputRows is not null
                    && AcceptedCount < _inputRows.Start)
                {
                    continue;
                }

                Results.Add(
                    new MemberFindResult
                    {
                        Pattern = member.Pattern,
                        Match = member.IsGlob
                            ? MemberFindMatchKind.Glob
                            : MemberFindMatchKind.Direct,
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
