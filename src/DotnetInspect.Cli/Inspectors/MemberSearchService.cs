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
/// final results. Configured package and Platform populations use the shared
/// semantic evaluator; explicit assembly sets retain the compatibility query.
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
        MemberFindAcceptedRows? acceptedRows =
            CreateAcceptedRows(options);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                patterns,
                options.IncludeAll
                    ? FindVisibility.All
                    : FindVisibility.Public,
                options.TypeFilter,
                acceptedRows?.End ?? options.Limit,
                acceptedRows);
        if (platformWorkspace is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<MemberFindResult> platformResults = [];
            MemberFindSemanticPopulation population =
                platformWorkspace.QueryMembers(question);
            MemberFindBlock block =
                FindSemanticReducer.ReduceMember(
                    question,
                    population);
            AddMembers(
                platformResults,
                block,
                platformWorkspace.SourceFor,
                options.OnMemberRow);
            WriteSourceFailures(
                population,
                platformWorkspace.SourceFor,
                logger,
                MarkFailure);
            cancellationToken.ThrowIfCancellationRequested();
            return new(platformResults, hasFailures)
            {
                SourceSelectionIncomplete =
                    options.PackagePrefixLimitReached,
                InputRows = CreateAcceptedReceipt(
                    options,
                    acceptedRows,
                    block.AcceptedCount),
                ExactRowCount =
                    options.Count
                        ? block.AcceptedCount
                        : null,
            };
        }

        AssemblySetRequest request =
            FindSourceCollector.BuildFindRequest(options);
        if (ConfiguredPackageSearchWorkspace.IsEligible(
                options.SourceSelection,
                request,
                options.Tfm))
        {
            await using ConfiguredPackageSearchWorkspace? configured =
                await ConfiguredPackageSearchWorkspace.OpenAsync(
                    httpClient,
                    request,
                    options.Tfm!,
                    logger.Log,
                    cancellationToken,
                    FindSourceCollector.CreateWorkspacePlan(options));
            MemberCollection configuredResult =
                configured is null
                ? new([], 0)
                : await CollectMembersAsync(
                    options,
                    question,
                    logger,
                    configured,
                    MarkFailure,
                    cancellationToken);
            if (configured is null)
                MarkFailure();
            return new(configuredResult.Rows, hasFailures)
            {
                SourceSelectionIncomplete =
                    options.PackagePrefixLimitReached,
                InputRows = CreateAcceptedReceipt(
                    options,
                    acceptedRows,
                    configuredResult.AcceptedCount),
                ExactRowCount =
                    options.Count
                        ? configuredResult.AcceptedCount
                        : null,
            };
        }

        if (explicitWorkspace is not null)
        {
            MemberCollection explicitResult =
                await CollectMembersAsync(
                    options,
                    question,
                    logger,
                    explicitWorkspace,
                    MarkFailure);
            return new(
                explicitResult.Rows,
                hasFailures)
            {
                SourceSelectionIncomplete =
                    options.PackagePrefixLimitReached,
                InputRows = CreateAcceptedReceipt(
                    options,
                    acceptedRows,
                    explicitResult.AcceptedCount),
                ExactRowCount =
                    options.Count
                        ? explicitResult.AcceptedCount
                        : null,
                Completion =
                    hasFailures
                        ? FindSearchCompletion.Incomplete
                        : FindSearchCompletion.Exhausted,
            };
        }

        await using var ownedWorkspace =
            new ExplicitFindSearchWorkspace(
                options,
                httpClient,
                logger.Log,
                cancellationToken);
        MemberCollection ownedResult =
            await CollectMembersAsync(
                options,
                question,
                logger,
                ownedWorkspace,
                MarkFailure);
        return new(
            ownedResult.Rows,
            hasFailures)
        {
            SourceSelectionIncomplete =
                options.PackagePrefixLimitReached,
            InputRows = CreateAcceptedReceipt(
                options,
                acceptedRows,
                ownedResult.AcceptedCount),
            ExactRowCount =
                options.Count
                    ? ownedResult.AcceptedCount
                    : null,
            Completion =
                hasFailures
                    ? FindSearchCompletion.Incomplete
                    : FindSearchCompletion.Exhausted,
        };
    }

    private static MemberFindAcceptedRows? CreateAcceptedRows(
        FindOptions options)
    {
        if (options.Count)
        {
            return options.QueryPlan?.InputRows is { } input
                ? new(
                    input.Start,
                    input.End,
                    materializeRows: false)
                : new(
                    start: 1,
                    end: null,
                    materializeRows: false);
        }
        if (options.InputRows is { } window)
        {
            return new(
                window.Start,
                window.End,
                materializeRows: true);
        }
        if (options.Limit is int limit)
        {
            return new(
                start: 1,
                limit,
                materializeRows: true);
        }

        return null;
    }

    private static FindAcceptedRowReceipt? CreateAcceptedReceipt(
        FindOptions options,
        MemberFindAcceptedRows? acceptedRows,
        int acceptedCount) =>
        !options.Count
        && acceptedRows is
            {
                MaterializeRows: true,
                End: int end,
            }
            ? new(
                new(
                    acceptedRows.Start,
                    end),
                acceptedCount)
            : null;

    /// <summary>
    /// Resolves configured sources and searches their members through typed
    /// participant queries.
    /// </summary>
    private static async Task<MemberCollection> CollectMembersAsync(
        FindOptions options,
        MemberFindQuestion question,
        VerboseLogger logger,
        ExplicitFindSearchWorkspace workspace,
        Action markFailure)
    {
        if (question.AcceptedRows is null)
        {
            List<MemberFindResult> rows =
                await CollectCompatibilityMembersAsync(
                    options,
                    question.Patterns
                        .Select(static pattern => pattern.Text)
                        .ToArray(),
                    logger,
                    workspace,
                    markFailure);
            return new(rows, rows.Count);
        }

        return await CollectSelectiveMembersAsync(
            options,
            question,
            logger,
            workspace,
            markFailure);
    }

    private static async Task<List<MemberFindResult>>
        CollectCompatibilityMembersAsync(
        FindOptions options,
        IReadOnlyList<string> patterns,
        VerboseLogger logger,
        ExplicitFindSearchWorkspace workspace,
        Action markFailure)
    {
        List<MemberFindResult> results = [];
        int? queryLimit =
            options.TypeFilter is null
                ? options.Limit
                : null;
        bool ReachedLimit() =>
            options.Limit is int limit
            && results.Count >= limit;

        await workspace.RunPerAssemblyAsync(
            AssemblyContextMemberMatchesQuery.Definition,
            group => AssemblyContextMemberMatchesQuery.Execute(
                group,
                patterns,
                options.IncludeAll,
                queryLimit is int limit
                    ? limit - results.Count
                    : null),
            (assembly, entry) => AddMembers(
                results,
                options.TypeFilter,
                SearchAssemblySource.FromAssemblySet(assembly),
                entry,
                logger,
                markFailure,
                options.OnMemberRow,
                options.Limit),
            (assembly, failure) =>
            {
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.Path}: {failure}");
            },
            markFailure,
            options.Limit is not null ? ReachedLimit : null);
        if (options.Limit.HasValue
            && results.Count > options.Limit.Value)
        {
            results = results.Take(options.Limit.Value).ToList();
        }
        return results;
    }

    private static async Task<MemberCollection>
        CollectSelectiveMembersAsync(
            FindOptions options,
            MemberFindQuestion question,
            VerboseLogger logger,
            ExplicitFindSearchWorkspace workspace,
            Action markFailure)
    {
        MemberFindAcceptedRows acceptedRows =
            question.AcceptedRows
            ?? throw new InvalidOperationException(
                "Selective Member collection requires accepted-row intent.");
        List<MemberFindResult> results = [];
        int acceptedCount = 0;
        bool ReachedEnd() =>
            acceptedRows.End is int end
            && acceptedCount >= end;

        await workspace.RunPerAssemblyAsync(
            AssemblyContextMemberAcceptedRowsQuery.Definition,
            group =>
            {
                int localStart =
                    Math.Max(
                        1,
                        acceptedRows.Start
                            - acceptedCount);
                int? localEnd =
                    acceptedRows.End is int end
                        ? end - acceptedCount
                        : null;
                return AssemblyContextMemberAcceptedRowsQuery
                    .Execute(
                        group,
                        [
                            .. question.Patterns.Select(
                                static pattern =>
                                    pattern.Text),
                        ],
                        question.Visibility,
                        new(
                            localStart,
                            localEnd,
                            acceptedRows.MaterializeRows),
                        question.DeclaringTypeFilter);
            },
            (assembly, entry) =>
            {
                switch (entry)
                {
                    case AssemblyContextEntry<
                        MemberSearchWindowResult>.Available
                            available:
                        MemberSearchWindowResult value =
                            available.Value;
                        acceptedCount =
                            checked(
                                acceptedCount
                                + value.AcceptedCount);
                        AddMembers(
                            results,
                            SearchAssemblySource
                                .FromAssemblySet(
                                    assembly),
                            value.Results,
                            options.OnMemberRow);
                        WriteInspectionFailures(
                            SearchAssemblySource
                                .FromAssemblySet(
                                    assembly),
                            value.InspectionFailures,
                            logger,
                            markFailure);
                        break;
                    case AssemblyContextEntry<
                        MemberSearchWindowResult>.Rejected
                            rejected:
                        markFailure();
                        CommandError.WriteWarning(
                            $"Could not read {assembly.Path}: "
                            + rejected.Failure.Detail);
                        break;
                    case AssemblyContextEntry<
                        MemberSearchWindowResult>.Failed
                            failed:
                        markFailure();
                        CommandError.WriteWarning(
                            $"Could not read {assembly.Path}: "
                            + failed.Error.Message);
                        break;
                }
            },
            (assembly, failure) =>
            {
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.Path}: {failure}");
            },
            markFailure,
            ReachedEnd);

        return new(results, acceptedCount);
    }

    private static async Task<MemberCollection> CollectMembersAsync(
        FindOptions options,
        MemberFindQuestion question,
        VerboseLogger logger,
        ConfiguredPackageSearchWorkspace workspace,
        Action markFailure,
        CancellationToken cancellationToken)
    {
        ConfiguredPackageSearchQueryResult<
            MemberFindSemanticPopulation>? execution =
                await workspace.QuerySurfaceAsync(
                    context =>
                        MemberFindSourceEvaluator
                            .EvaluateAssemblyContext(
                            question,
                            context.Group,
                            context.Sources
                                .MemberFindSourceFor),
                    cancellationToken);
        if (execution is null)
        {
            markFailure();
            return new([], 0);
        }
        if (execution.Sources is not { } sources
            || execution.Result is not { } population)
        {
            return new([], 0);
        }

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                population);
        List<MemberFindResult> results = [];
        AddMembers(
            results,
            block,
            sources.SourceFor,
            options.OnMemberRow);
        WriteSourceFailures(
            population,
            sources.SourceFor,
            logger,
            markFailure);
        return new(results, block.AcceptedCount);
    }

    private sealed record MemberCollection(
        List<MemberFindResult> Rows,
        int AcceptedCount);

    private static void AddMembers(
        List<MemberFindResult> results,
        SearchAssemblySource assembly,
        IReadOnlyList<MemberSearchResult> members,
        Action<MemberFindResult>? onRow)
    {
        foreach (MemberSearchResult member in members)
        {
            var row = new MemberFindResult
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
            };
            results.Add(row);
            onRow?.Invoke(row);
        }
    }

    private static void AddMembers(
        List<MemberFindResult> results,
        MemberFindBlock block,
        Func<FindSourceIdentity, SearchAssemblySource> sourceFor,
        Action<MemberFindResult>? onRow)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(sourceFor);
        foreach (MemberFindSemanticMatch member in block.Matches)
        {
            SearchAssemblySource assembly =
                sourceFor(member.Declaration.Source);
            var row = new MemberFindResult
            {
                Pattern = member.Pattern.Text,
                Match = member.Match is
                    MemberFindSemanticMatchKind.Glob
                        ? MemberFindMatchKind.Glob
                        : MemberFindMatchKind.Direct,
                Member = member.MemberName,
                Kind = member.Kind,
                DeclaringType = member.DeclaringType,
                Namespace = member.DeclaringNamespace ?? "",
                Signature = member.Signature,
                ReturnType = member.ReturnType,
                Library = assembly.Library,
                Source = assembly.Source,
                SourceVersion = assembly.SourceVersion,
            };
            results.Add(row);
            onRow?.Invoke(row);
        }
    }

    private static void WriteSourceFailures(
        MemberFindSemanticPopulation population,
        Func<FindSourceIdentity, SearchAssemblySource> sourceFor,
        VerboseLogger logger,
        Action markFailure)
    {
        foreach (MemberFindSourceEvaluation source
            in population.Sources)
        {
            SearchAssemblySource assembly = sourceFor(source.Source);
            WriteInspectionFailures(
                assembly,
                source.Coverage.InspectionFailures,
                logger,
                markFailure);
            switch (source)
            {
                case MemberFindSourceEvaluation.Rejected:
                case MemberFindSourceEvaluation.Failed:
                    markFailure();
                    CommandError.WriteWarning(
                        $"Could not read {assembly.DiagnosticSubject}: "
                        + source.Coverage.Detail);
                    break;
            }
        }
    }

    private static void AddMembers(
        List<MemberFindResult> results,
        string? typeFilter,
        SearchAssemblySource assembly,
        AssemblyContextEntry<AssemblyMemberMatches> entry,
        VerboseLogger logger,
        Action markFailure,
        Action<MemberFindResult>? onRow = null,
        int? limit = null)
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

                    var row = new MemberFindResult
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
                    };
                    results.Add(row);
                    if (limit is null || results.Count <= limit)
                        onRow?.Invoke(row);
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
