using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using Markout;
using DotnetInspector.Presentation;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public partial class FindCommand
{
    public const string Name = "find";

    internal readonly record struct FindExecutionResult(
        int ExitCode,
        int? RowCount);

    public static async Task<int> ExecuteAsync(
        FindOptions options,
        CancellationToken cancellationToken = default)
        => (await ExecuteWithResultAsync(options, cancellationToken)).ExitCode;

    internal static async Task<FindExecutionResult> ExecuteWithResultAsync(
        FindOptions options,
        CancellationToken cancellationToken = default)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            RowSelectionIntent<string>? rowSelection =
                options.EffectiveRowSelection;

            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                var schema = options.Tsv && !options.Count
                    ? new DocumentSchema()
                        .Add("Results", "column", [.. FindDiscoveryTsvWriter.Columns])
                    : options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", options.Ecosystems is null
                            ? ["Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source"]
                            : ["Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source", "Ecosystem"])
                    : new DocumentSchema()
                        .Add("Results", "column", options.Ecosystems is null
                            ? ["Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim"]
                            : ["Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Ecosystem", "Match", "Sim"]);
                return new(DiscoverOutput.Execute(options.Discover, schema,
                    DiscoveryOutputRequest.Create(
                        options.JsonOutput ? OutputFormat.Json
                            : options.Jsonl ? OutputFormat.Jsonl
                            : options.Tsv ? OutputFormat.Tsv
                            : options.Tabular ? OutputFormat.Table
                            : OutputFormat.Markdown,
                        options.Tree,
                        options.Tabular,
                        options.NoHeader,
                        (int)options.Verbosity,
                        options),
                    semanticRowSelection: rowSelection,
                    semanticSelectionName: "Find"),
                    RowCount: null);
            }

            var patterns = options.Pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                CommandError.Write("No pattern specified.");
                return new(1, RowCount: null);
            }
            FindOptions searchOptions =
                CreateSearchOptions(options, patterns);
            using FindDiscoveryTsvWriter? tsv =
                (options.Tsv || options.Jsonl) && !options.Count
                    ? new(Console.Out, !options.NoHeader,
                        options.Columns, options.Fields,
                        jsonl: options.Jsonl)
                    : null;
            bool progressive =
                tsv is not null
                && (rowSelection is null
                    || options.Ecosystems is not null
                        && options.Limit is not null);
            int streamedTypes = 0;
            int streamedMembers = 0;
            if (progressive && options.Ecosystems is null)
            {
                if (options.Members)
                {
                    searchOptions = searchOptions with
                    {
                        OnMemberRow = row =>
                        {
                            tsv!.Write(FindDiscoveryOutput.Project(row));
                            streamedMembers++;
                        },
                    };
                }
                else if (patterns.Length == 1
                    && TypeMatcher.IsTypeGlobPattern(patterns[0])
                    && !patterns[0].EndsWith(".*", StringComparison.Ordinal))
                {
                    searchOptions = searchOptions with
                    {
                        OnTypeRow = row =>
                        {
                            tsv!.Write(FindDiscoveryOutput.Project(row));
                            streamedTypes++;
                        },
                    };
                }
            }
            LayeredFindResult? layered = options.Ecosystems is null
                ? null
                : await SearchLayersAsync(
                    searchOptions, patterns, context, cancellationToken,
                    progressive ? tsv : null);

            PlatformFindSearchWorkspace? platformWorkspace = null;
            if (layered is null && searchOptions.UsesImplicitPlatform)
            {
                logger.Log(
                    "No scope specified, defaulting to the platform Workspace");
                platformWorkspace =
                    await PlatformFindSearchWorkspace.OpenAsync(
                        searchOptions,
                        context,
                        cancellationToken);
            }
            await using PlatformFindSearchWorkspace? platformWorkspaceLifetime =
                platformWorkspace;
            ExplicitFindSearchWorkspace? explicitWorkspace =
                layered is null && platformWorkspace is null
                    ? new(
                        searchOptions,
                        context.HttpClient,
                        logger.Log,
                        cancellationToken)
                    : null;
            await using ExplicitFindSearchWorkspace? explicitWorkspaceLifetime =
                explicitWorkspace;

            if (options.Members)
            {
                return await ExecuteMemberSearchAsync(
                    searchOptions,
                    patterns,
                    rowSelection,
                    logger,
                    context.HttpClient,
                    cancellationToken,
                    platformWorkspace,
                    explicitWorkspace,
                    layered?.MemberSearch,
                    tsv,
                    () => layered is not null && progressive
                        ? layered.MemberSearch.Rows.Count
                        : streamedMembers);
            }

            FindSearchResult<TypeFindResult> search =
                layered?.TypeSearch ?? await TypeSearchService.FindTypesAsync(
                    searchOptions,
                    patterns,
                    logger,
                    context.HttpClient,
                    cancellationToken,
                    context,
                    platformWorkspace,
                    explicitWorkspace);
            FindSearchResult<MemberFindResult>? memberTier =
                layered is not null ? layered.MemberSearch : await FindBroadenedMembersAsync(
                    searchOptions,
                    patterns,
                    search.Rows,
                    logger,
                    context.HttpClient,
                    cancellationToken,
                    platformWorkspace,
                    explicitWorkspace);
            List<MemberFindResult> members = memberTier?.Rows ?? [];
            List<TypeFindResult> results =
                layered is not null ? search.Rows : WithoutSupersededWeakRows(search.Rows, members);
            int observedRowCount = results.Count;
            bool rendersMembers =
                !options.Count
                && !options.JsonOutput
                && (!options.Tabular
                    || options.Tsv
                    || options.Jsonl);
            if (options.Count
                && (members.Count > 0
                    || memberTier?.HasFailures is true
                    || memberTier?.SourceSelectionIncomplete is true))
            {
                CommandError.Write(
                    "Cannot count Find rows because the answer includes "
                    + "member matches or an incomplete member search. Count "
                    + "Types with a wildcard pattern or members with --members.");
                return new(1, RowCount: null);
            }

            if (!TrySelectAnswerRows(
                    rowSelection,
                    rendersMembers ? members : [],
                    results,
                    out List<MemberFindResult> selectedMembers,
                    out List<TypeFindResult> selectedTypes))
            {
                WriteUnmatchedPatternWarning(search);
                return new(1, RowCount: null);
            }
            if (members.Count > 0 && !rendersMembers)
                WriteOmittedMembersNote(members);
            members = selectedMembers;
            results = selectedTypes;
            WriteUnmatchedPatternWarning(search);
            var title = patterns.Length == 1 ? $"Find: {patterns[0]}" : "Find Results";

            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                if (search.HasFailures
                    || !CliSemanticRowSelection.ProvidesExactCount(
                        rowSelection,
                        observedRowCount,
                        sourceComplete:
                            !search.SourceSelectionIncomplete))
                {
                    CommandError.Write(
                        "Cannot count type rows because one or more search sources were incomplete.");
                    return new(1, RowCount: null);
                }
                if (!WriteCount(results, title, options))
                    return new(1, RowCount: null);
            }
            else if (tsv is not null)
            {
                if (layered is null || !progressive)
                {
                    foreach (MemberFindResult row in members.Skip(streamedMembers))
                        tsv.Write(FindDiscoveryOutput.Project(row));
                    foreach (TypeFindResult row in results.Skip(streamedTypes))
                    {
                        if (row.Match != TypeFindMatchKind.NotFound)
                            tsv.Write(FindDiscoveryOutput.Project(row));
                    }
                }
                if (members.Count == 0
                    && results.All(row => row.Match == TypeFindMatchKind.NotFound))
                    CommandError.WriteLine("No types found matching the pattern.");
            }
            else if (options.JsonOutput)
            {
                // --fields/--columns name post-lowering vocabulary (computed table columns), so
                // naming one opts into the lowered display view; plain --json keeps the typed
                // root result array (#3494). This combination used to fail closed (#3386) only
                // because the lowered JSON view did not exist yet.
                if (IsColumnProjectionRequested(options))
                {
                    WriteProjectedJson(results, title, options);
                }
                else
                {
                    JsonOutputHelper.Write(
                        results,
                        TypeFindResultJsonContext.Default.ListTypeFindResult,
                        TypeFindResultCompactJsonContext.Default.ListTypeFindResult,
                        options.CompactJson);
                }
            }
            else
            {
                WriteOutput(results, title, options, members);
            }

            return new(0, (tsv is not null
                    ? results.Count(row => row.Match != TypeFindMatchKind.NotFound)
                    : results.Count)
                + (rendersMembers ? members.Count : 0));
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return new(1, RowCount: null);
        }
    }

    private static FindOptions CreateSearchOptions(
        FindOptions options,
        string[] patterns)
    {
        if (options.QueryPlan?.InputRows is not { } inputRows)
        {
            return options;
        }

        RowSelectionIntentOperation<string> operation =
            options.QueryPlan.Rows.Operations.Single();
        bool canDelegate =
            options.Members
            || operation.Kind == RowSelectionStageKind.Head
            || operation.Kind == RowSelectionStageKind.Window
                && patterns.All(static pattern =>
                    !MayRunImplicitMemberFallback(pattern));
        if (!canDelegate)
            return options;

        int effectiveLimit =
            options.Limit is int existingLimit
                ? Math.Min(existingLimit, inputRows.End)
                : inputRows.End;
        return options with
        {
            Limit = effectiveLimit,
            InputRows =
                options.Members
                && operation.Kind == RowSelectionStageKind.Window
                    ? inputRows
                    : null,
        };
    }

    private sealed record LayeredFindResult(
        FindSearchResult<TypeFindResult> TypeSearch,
        FindSearchResult<MemberFindResult> MemberSearch);

    private sealed record LayeredSearchBlock(
        IReadOnlyList<TypeFindResult> Types,
        IReadOnlyList<MemberFindResult> Members,
        int AcceptedMemberCount = 0);

    internal static IReadOnlyList<(
        EcosystemPackId Id,
        WorkspaceEcosystemRegistrationDeclaration Declaration,
        bool Platform)> GetNamedLayers(
        IReadOnlyList<EcosystemPackId> selection)
    {
        var layers = new List<(
            EcosystemPackId Id,
            WorkspaceEcosystemRegistrationDeclaration Declaration,
            bool Platform)>();
        foreach (EcosystemPackId id in EcosystemPackCatalog.OrderLayeredFind(selection))
        {
            var known = (EcosystemWorkspaceRegistrationSelectionResult.Known)
                EcosystemPackCatalog.SelectWorkspaceRegistration(id);
            WorkspaceEcosystemRegistrationDeclaration declaration =
                known.Declaration;
            bool platform = declaration.Populations.Any(population =>
                population is WorkspaceEcosystemPopulationDeclaration.Platform);
            if (platform && !declaration.CorePackages.IsEmpty)
                throw new NotSupportedException(
                    $"Ecosystem '{id}' has both Platform and core-package populations.");
            layers.Add((id, declaration, platform));
        }
        return layers;
    }

    private static async Task<LayeredFindResult> SearchLayersAsync(
        FindOptions options,
        string[] patterns,
        CommandContext context,
        CancellationToken cancellationToken,
        FindDiscoveryTsvWriter? tsv = null)
    {
        var layers = GetNamedLayers(options.Ecosystems!);
        return await SearchEcosystemSessionAsync(
            options, patterns, context, cancellationToken, tsv, layers);
    }

    private static async Task<LayeredFindResult> SearchEcosystemSessionAsync(
        FindOptions options,
        string[] patterns,
        CommandContext context,
        CancellationToken cancellationToken,
        FindDiscoveryTsvWriter? tsv,
        IReadOnlyList<(
            EcosystemPackId Id,
            WorkspaceEcosystemRegistrationDeclaration Declaration,
            bool Platform)> layers)
    {
        var types = new List<TypeFindResult>();
        var members = new List<MemberFindResult>();
        var unansweredPatterns = new HashSet<string>(
            patterns, StringComparer.Ordinal);
        bool failures = false;
        bool incomplete = false;
        int acceptedMemberCount = 0;
        FindInputRowSelection? memberInputRows =
            options.QueryPlan?.InputRows;
        FindSearchCompletion completion = FindSearchCompletion.Exhausted;
        using var session = CreateEcosystemSession(
            options, patterns, context, tsv, layers,
            options.Tsv || options.Jsonl,
            types, members, unansweredPatterns,
            count => acceptedMemberCount += count,
            (hasFailures, isIncomplete) =>
            {
                failures |= hasFailures;
                incomplete |= isIncomplete;
            },
            async (layer, token) =>
        {
            var id = layers.Single(item => item.Declaration.Id == layer.Registration.Id).Id;
            var declaration = layer.Registration;
            if (!layer.Platform && declaration.CorePackages.IsEmpty)
                return new EcosystemFindBlock<LayeredSearchBlock>(
                    new([], []), 0);
            FindOptions scoped = options with
            {
                Ecosystems = [id],
                SourceSelection = null,
                Packages = layer.CorePackageId is { } core
                    ? [core]
                    : [.. declaration.CorePackages
                        .Select(package => package.PackageId)],
                PlatformAssemblies = [],
                PlatformFrameworks = [],
                Limit = null,
                QueryPlan = null,
                InputRows = null,
            };
            context.Logger.Log($"Searching {id}");
            string? sourceFailure = null;
            try
            {
                await using PlatformFindSearchWorkspace? platform =
                    layer.Platform
                        ? await PlatformFindSearchWorkspace.OpenAsync(
                            new WorkspacePlan([new WorkspaceRegistration.Ecosystem(declaration)]),
                            scoped, context, token)
                        : null;
                await using ExplicitFindSearchWorkspace? explicitWorkspace =
                    layer.Platform ? null : new(
                        scoped, context.HttpClient, context.Logger.Log,
                        token);
                if (options.Members)
                {
                    string[] memberPatterns =
                    [
                        .. patterns.Select(MemberPatternSentinel.Strip)
                            .Where(pattern => pattern.Length > 0),
                    ];
                    FindSearchResult<MemberFindResult> found =
                        await MemberSearchService.FindMembersAsync(
                            scoped,
                            memberPatterns,
                            context.Logger,
                            context.HttpClient, token, platform,
                            explicitWorkspace);
                    string memberAttribution = CoreAttribution(
                        layer,
                        layers,
                        id.Value,
                        options.Tsv || options.Jsonl);
                    MemberFindResult[] rows =
                    [
                        .. found.Rows.Select(row => row with
                        {
                            Ecosystem = memberAttribution,
                        }),
                    ];
                    return new EcosystemFindBlock<LayeredSearchBlock>(
                        new([], rows, found.ExactRowCount
                            ?? found.InputRows?.AcceptedCount ?? rows.Length),
                        CountLayeredRows(options, [], rows),
                        found.HasFailures, found.SourceSelectionIncomplete);
                }

                FindSearchResult<TypeFindResult> foundTypes =
                    await TypeSearchService.FindTypesAsync(
                        scoped, patterns, context.Logger, context.HttpClient,
                        token, context, platform, explicitWorkspace);
                FindSearchResult<MemberFindResult>? foundMembers =
                    await FindBroadenedMembersAsync(
                        scoped, patterns, foundTypes.Rows, context.Logger,
                        context.HttpClient, token, platform,
                        explicitWorkspace);
                List<MemberFindResult> band = foundMembers?.Rows ?? [];
                string attribution = CoreAttribution(
                    layer,
                    layers,
                    id.Value,
                    options.Tsv || options.Jsonl);
                MemberFindResult[] attributedMembers =
                [
                    .. band.Select(row => row with { Ecosystem = attribution }),
                ];
                TypeFindResult[] attributedTypes =
                [
                    .. WithoutSupersededWeakRows(foundTypes.Rows, band)
                        .Select(row => row with { Ecosystem = attribution }),
                ];
                return new EcosystemFindBlock<LayeredSearchBlock>(
                    new(attributedTypes, attributedMembers),
                    CountLayeredRows(options, attributedTypes, attributedMembers),
                    foundTypes.HasFailures || foundMembers?.HasFailures is true,
                    foundTypes.SourceSelectionIncomplete
                        || foundMembers?.SourceSelectionIncomplete is true);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                sourceFailure = failure.Message;
            }
            return new EcosystemFindBlock<LayeredSearchBlock>(
                new([], []), 0, HasFailures: true, Incomplete: true)
                { Failure = sourceFailure };
        },
            async (candidate, memberships, token) =>
            {
                var result = await EvaluatePrefixCandidateAsync(
                    options, patterns, context, candidate, memberships, token);
                return new EcosystemFindBlock<LayeredSearchBlock>(
                    result.Content,
                    CountLayeredRows(options, result.Content.Types,
                        result.Content.Members),
                    result.HasFailures, result.IsIncomplete)
                    { Failure = result.Failure };
            });
        EcosystemFindBoundedOutcome<LayeredSearchBlock> bounded =
            await session.RunBoundedAsync(cancellationToken);
        EcosystemFindSearchSummary<LayeredSearchBlock> summary =
            bounded.Completed ?? await bounded.Continuation!.ResumeAsync(
                bounded.Continuation.MaximumRemainingRows, cancellationToken);
        if (summary.Completion == EcosystemFindCompletion.RowLimitReached)
        {
            CommandError.WriteNote(
                $"Stopped before unsearched work: {string.Join(", ", summary.Unsearched)}. "
                + "Raise -n to continue.");
            completion = FindSearchCompletion.ResultLimitReached;
        }
        else if (summary.Completion != EcosystemFindCompletion.Exhausted)
        {
            if (summary.Completion == EcosystemFindCompletion.CandidateLimitReached)
                CommandError.WriteWarning(
                    "Ecosystem package-prefix discovery reached its "
                    + $"{ScopeConstants.PackagePrefixExpansionLimit}-package "
                    + "candidate bound; later prefixes were not searched.");
            incomplete = true;
        }
        if (tsv is null && layers.Any(layer =>
            layer.Declaration.Populations.Any(population =>
                population is WorkspaceEcosystemPopulationDeclaration.PackagePrefix)))
        {
            CommandError.WriteNote(
                "Package-prefix populations were not searched because "
                + "this output format is blocking. Use TSV or JSONL for "
                + "progressive prefix discovery.");
            incomplete = true;
        }
        if (completion == FindSearchCompletion.Exhausted
            && (failures || incomplete))
            completion = FindSearchCompletion.Incomplete;
        return new(
            new(types, failures, [.. unansweredPatterns])
            {
                SourceSelectionIncomplete = incomplete,
                Completion = completion,
            },
            new(members, failures)
            {
                SourceSelectionIncomplete = incomplete,
                Completion = completion,
                InputRows =
                    !options.Count
                    && memberInputRows is { } receiptInputRows
                        ? new(
                            receiptInputRows,
                            acceptedMemberCount)
                        : null,
                ExactRowCount =
                    options.Count
                        ? acceptedMemberCount
                        : null,
            });
    }

    private sealed record PrefixFindResult(
        LayeredSearchBlock Content,
        bool HasFailures,
        bool IsIncomplete,
        string? Failure);

    private static string CoreAttribution(
        EcosystemFindLayer layer,
        IReadOnlyList<(
            EcosystemPackId Id,
            WorkspaceEcosystemRegistrationDeclaration Declaration,
            bool Platform)> layers,
        string fallback,
        bool demandPrefixes)
    {
        if (layer.CorePackageId is not { } packageId)
            return fallback;
        return string.Join(", ", layers
            .Where(selected => selected.Declaration.CorePackages.Any(core =>
                string.Equals(core.PackageId, packageId,
                    StringComparison.OrdinalIgnoreCase))
                || demandPrefixes && selected.Declaration.Populations
                    .OfType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>()
                    .Any(prefix => prefix.Prefix.MatchesPackageId(packageId)))
            .Select(selected =>
                selected.Declaration.CorePackages.Any(core =>
                    string.Equals(core.PackageId, packageId,
                        StringComparison.OrdinalIgnoreCase))
                    ? selected.Declaration.Id.Value
                    : $"{selected.Declaration.Id.Value} (prefix)"));
    }

    private static EcosystemFindSearchSession<LayeredSearchBlock> CreateEcosystemSession(
        FindOptions options,
        string[] patterns,
        CommandContext context,
        FindDiscoveryTsvWriter? writer,
        IReadOnlyList<(
            EcosystemPackId Id,
            WorkspaceEcosystemRegistrationDeclaration Declaration,
            bool Platform)> layers,
        bool demandPrefixes,
        List<TypeFindResult> types,
        List<MemberFindResult> members,
        HashSet<string> unansweredPatterns,
        Action<int> addAcceptedMembers,
        Action<bool, bool> addStatus,
        Func<EcosystemFindLayer, CancellationToken,
            Task<EcosystemFindBlock<LayeredSearchBlock>>> bounded,
        Func<EcosystemFindCandidate,
            IReadOnlyList<WorkspaceEcosystemRegistrationId>,
            CancellationToken, Task<EcosystemFindBlock<LayeredSearchBlock>>> candidate)
    {
        EcosystemFindSearchRequest request =
            CreateEcosystemRequest(
                options,
                patterns,
                layers,
                demandPrefixes);
        return new(request, bounded,
            (prefix, remaining, token) =>
                EnumeratePrefixAsync(prefix, remaining, context, token),
            candidate,
            publish: block =>
            {
                LayeredSearchBlock content = block.Content;
                types.AddRange(content.Types);
                members.AddRange(content.Members);
                addAcceptedMembers(content.AcceptedMemberCount);
                addStatus(block.HasFailures, block.Incomplete);
                if (block.Failure is { } failure)
                    CommandError.WriteWarning(failure);
                foreach (MemberFindResult row in content.Members)
                {
                    foreach (string pattern in patterns)
                    {
                        if (string.Equals(row.Pattern, pattern,
                                StringComparison.Ordinal)
                            || string.Equals(row.Pattern,
                                MemberPatternSentinel.Strip(pattern),
                                StringComparison.Ordinal))
                            unansweredPatterns.Remove(pattern);
                    }
                    writer?.Write(FindDiscoveryOutput.Project(row));
                }
                foreach (TypeFindResult row in content.Types)
                {
                    if (block.Candidate is null
                        || row.Match != TypeFindMatchKind.NotFound)
                    {
                        foreach (string pattern in patterns)
                        {
                            if (string.Equals(row.Pattern, pattern,
                                    StringComparison.Ordinal)
                                || (row.Match is TypeFindMatchKind.Prefix
                                    or TypeFindMatchKind.Namespace)
                                    && string.Equals(row.Pattern, $"{pattern}*",
                                        StringComparison.Ordinal))
                                unansweredPatterns.Remove(pattern);
                        }
                    }
                    if (row.Match != TypeFindMatchKind.NotFound)
                        writer?.Write(FindDiscoveryOutput.Project(row));
                }
                writer?.Flush();
            },
            reuseCore: (layer, source, _) =>
            {
                LayeredSearchBlock content = source.Content;
                string ecosystem = CoreAttribution(layer, layers,
                    layer.Registration.Id.Value, demandPrefixes);
                TypeFindResult[] reusedTypes =
                [
                    .. content.Types.Select(row => row with
                    {
                        Ecosystem = ecosystem,
                    }),
                ];
                MemberFindResult[] reusedMembers =
                [
                    .. content.Members.Select(row => row with
                    {
                        Ecosystem = ecosystem,
                    }),
                ];
                return source with
                {
                    Content = new(reusedTypes, reusedMembers,
                        content.AcceptedMemberCount),
                };
            },
            combineCore: sources => new(
                [.. sources.SelectMany(source => source.Content.Types)],
                [.. sources.SelectMany(source => source.Content.Members)],
                sources.Sum(source => source.Content.AcceptedMemberCount)));
    }

    internal static EcosystemFindSearchRequest CreateEcosystemRequest(
        FindOptions options,
        string[] patterns,
        IReadOnlyList<(
            EcosystemPackId Id,
            WorkspaceEcosystemRegistrationDeclaration Declaration,
            bool Platform)> layers,
        bool demandPrefixes)
    {
        FindVisibility visibility = options.IncludeAll
            ? FindVisibility.All : FindVisibility.Public;
        FindQuestion question = options.Members
            ? MemberFindQuestion.Create(
                patterns.Select(MemberPatternSentinel.Strip),
                visibility, options.TypeFilter)
            : TypeFindQuestion.Create(patterns, visibility);
        return new EcosystemFindSearchRequest(
            question,
            layers.Select(layer =>
                new EcosystemFindLayer(layer.Declaration, layer.Platform)),
            !demandPrefixes
                ? []
                : layers.Select(layer => layer.Declaration.Id),
            ScopeConstants.PackagePrefixExpansionLimit,
            options.Limit);
    }

    private static async IAsyncEnumerable<EcosystemFindPrefixPage>
        EnumeratePrefixAsync(
            PackagePrefixDeclaration prefix,
            int remaining,
            CommandContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                DotnetInspector.Networking.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        deadline.CancelAfter(fetchOptions.OperationTimeout);
        using var operation = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            deadline.Token);
        context.Logger.Log($"Searching package prefix {prefix.Prefix}");
        await foreach (PackageSourceOperationResult<PackageSearchResult> page
            in source.SearchByPrefixPagesAsync(
                prefix.Prefix, remaining, prerelease: false,
                operation.CancellationToken, operation))
        {
            if (page.Failure is { } failure)
            {
                CommandError.WriteWarning(
                    $"Could not search package prefix \"{prefix.Prefix}\": "
                    + failure.Message);
                yield return new([], EcosystemFindPrefixPageCompletion.Failed,
                    failure.Message);
                yield break;
            }
            PackageSearchResult result = page.Value
                ?? throw new InvalidOperationException(
                    "Package-prefix search returned no result or failure.");
            EcosystemFindPrefixPageCompletion completion =
                result.TruncationReason switch
                {
                    PackageSearchTruncationReason.RequestedLimit =>
                        EcosystemFindPrefixPageCompletion.RequestedLimit,
                    PackageSearchTruncationReason.SourcePageLimit =>
                        EcosystemFindPrefixPageCompletion.SourcePageLimit,
                    PackageSearchTruncationReason.ClientPageLimit =>
                        EcosystemFindPrefixPageCompletion.ClientPageLimit,
                    _ => EcosystemFindPrefixPageCompletion.Exhausted,
                };
            if (result.Truncated)
                CommandError.WriteWarning(
                    $"Package-prefix search for \"{prefix.Prefix}\" "
                    + $"was incomplete: {result.TruncationReason}.");
            yield return new(
                [.. result.Matches.Select(match => new EcosystemFindCandidate(
                    match.Metadata.Id, match.Metadata.Version.ToString()))],
                completion);
        }
    }

    private static async Task<PrefixFindResult> EvaluatePrefixCandidateAsync(
        FindOptions options,
        string[] patterns,
        CommandContext context,
        EcosystemFindCandidate candidate,
        IReadOnlyList<WorkspaceEcosystemRegistrationId> memberships,
        CancellationToken cancellationToken)
    {
        bool failures = false;
        bool incomplete = false;
        string? sourceFailure = null;
        int acceptedMemberCount = 0;
        var types = new List<TypeFindResult>();
        var members = new List<MemberFindResult>();
        string ecosystem = string.Join(", ", memberships.Select(id =>
            $"{id.Value} (prefix)"));
        FindOptions scoped = options with
        {
            Ecosystems = null,
            SourceSelection = null,
            Packages = [$"{candidate.PackageId}@{candidate.Version}"],
            PlatformAssemblies = [],
            PlatformFrameworks = [],
            PackagePrefix = null,
            PackagePrefixSpecified = false,
            Limit = null,
            QueryPlan = null,
            InputRows = null,
        };
        try
        {
            await using var workspace = new ExplicitFindSearchWorkspace(
                scoped, context.HttpClient, context.Logger.Log,
                cancellationToken);
            if (options.Members)
            {
                string[] memberPatterns =
                [
                    .. patterns.Select(MemberPatternSentinel.Strip)
                        .Where(pattern => pattern.Length > 0),
                ];
                FindSearchResult<MemberFindResult> found =
                    await MemberSearchService.FindMembersAsync(
                        scoped, memberPatterns, context.Logger,
                        context.HttpClient, cancellationToken,
                        platformWorkspace: null,
                        explicitWorkspace: workspace);
                acceptedMemberCount = found.ExactRowCount
                    ?? found.InputRows?.AcceptedCount
                    ?? found.Rows.Count;
                foreach (MemberFindResult row in found.Rows)
                {
                    MemberFindResult attributed = row with
                    {
                        Ecosystem = ecosystem,
                    };
                    members.Add(attributed);
                }
                failures |= found.HasFailures;
                incomplete |= found.SourceSelectionIncomplete;
            }
            else
            {
                FindSearchResult<TypeFindResult> foundTypes =
                    await TypeSearchService.FindTypesAsync(
                        scoped, patterns, context.Logger, context.HttpClient,
                        cancellationToken, context,
                        explicitWorkspace: workspace);
                FindSearchResult<MemberFindResult>? foundMembers =
                    await FindBroadenedMembersAsync(
                        scoped, patterns, foundTypes.Rows, context.Logger,
                        context.HttpClient, cancellationToken,
                        platformWorkspace: null, explicitWorkspace: workspace);
                List<MemberFindResult> band = foundMembers?.Rows ?? [];
                foreach (MemberFindResult row in band)
                {
                    MemberFindResult attributed = row with
                    {
                        Ecosystem = ecosystem,
                    };
                    members.Add(attributed);
                }
                foreach (TypeFindResult row in DurablePrefixTypeRows(
                    foundTypes.Rows, band))
                {
                    TypeFindResult attributed = row with
                    {
                        Ecosystem = ecosystem,
                    };
                    types.Add(attributed);
                }
                failures |= foundTypes.HasFailures
                    || foundMembers?.HasFailures is true;
                incomplete |= foundTypes.SourceSelectionIncomplete
                    || foundMembers?.SourceSelectionIncomplete is true;
            }
        }
        catch (Exception candidateFailure)
            when (candidateFailure is not OperationCanceledException)
        {
            failures = true;
            incomplete = true;
            sourceFailure = candidateFailure.Message;
        }
        return new(
            new(types, members, acceptedMemberCount),
            failures, incomplete, sourceFailure);
    }

    internal static List<TypeFindResult> DurablePrefixTypeRows(
        List<TypeFindResult> typeRows,
        List<MemberFindResult> members) =>
        [
            .. WithoutSupersededWeakRows(typeRows, members)
                .Where(static row =>
                    row.Match is not TypeFindMatchKind.Partial
                        and not TypeFindMatchKind.NotFound),
        ];

    private static int CountLayeredRows(
        FindOptions options,
        IReadOnlyList<TypeFindResult> types,
        IReadOnlyList<MemberFindResult> members)
    {
        bool memberRowsArePresented =
            options.Members
            || (!options.Count
                && !options.JsonOutput
                && (!options.Tabular
                    || options.Tsv
                    || options.Jsonl));
        return types.Count(row => row.Match != TypeFindMatchKind.NotFound)
            + (memberRowsArePresented ? members.Count : 0);
    }

    /// <summary>
    /// The separately composed member source: an undotted, non-wildcard
    /// pattern whose Type answer has no Exact, Direct, Glob, Namespace, or
    /// Prefix row also runs Member Find's Direct grammar over the same
    /// authorized sources.
    /// </summary>
    private static async Task<FindSearchResult<MemberFindResult>?>
        FindBroadenedMembersAsync(
            FindOptions options,
            string[] patterns,
            List<TypeFindResult> typeRows,
            VerboseLogger logger,
            HttpClient httpClient,
            CancellationToken cancellationToken,
            PlatformFindSearchWorkspace? platformWorkspace,
            ExplicitFindSearchWorkspace? explicitWorkspace)
    {
        if (options.Limit is int limit
            && typeRows.Count >= limit)
        {
            return null;
        }

        HashSet<string> settled = new(
            typeRows
                .Where(static row => row.Match is TypeFindMatchKind.Exact
                    or TypeFindMatchKind.Direct
                    or TypeFindMatchKind.Glob
                    or TypeFindMatchKind.Namespace
                    or TypeFindMatchKind.Prefix)
                .Select(static row => row.Pattern),
            StringComparer.Ordinal);
        string[] memberPatterns =
        [
            .. patterns.Where(pattern =>
                !settled.Contains(pattern)
                && MayRunImplicitMemberFallback(pattern)),
        ];
        if (memberPatterns.Length == 0)
            return null;

        return await MemberSearchService.FindMembersAsync(
            options,
            memberPatterns,
            logger,
            httpClient,
            cancellationToken,
            platformWorkspace,
            explicitWorkspace);
    }

    private static bool MayRunImplicitMemberFallback(
        string pattern) =>
        !pattern.Contains('.')
        && TypeNameMatchRanking.IsBroadenable(pattern);

    /// <summary>
    /// Applies semantic row selection to the answer in presented order: the
    /// broadened band's member rows, then Type rows
    /// (find-search-service.md#result-and-presentation-boundary).
    /// </summary>
    private static bool TrySelectAnswerRows(
        RowSelectionIntent<string>? intent,
        List<MemberFindResult> members,
        List<TypeFindResult> types,
        out List<MemberFindResult> selectedMembers,
        out List<TypeFindResult> selectedTypes)
    {
        selectedMembers = [];
        selectedTypes = [];
        if (members.Count == 0)
        {
            if (!TrySelectRows(
                    intent,
                    types,
                    "type",
                    out IReadOnlyList<TypeFindResult> typeRows))
            {
                return false;
            }

            selectedTypes = [.. typeRows];
            return true;
        }

        List<(MemberFindResult? Member, TypeFindResult? Type)> answer =
        [
            .. members.Select(static member =>
                ((MemberFindResult?)member, (TypeFindResult?)null)),
            .. types.Select(static type =>
                ((MemberFindResult?)null, (TypeFindResult?)type)),
        ];
        if (!TrySelectRows(
                intent,
                answer,
                "find",
                out IReadOnlyList<(MemberFindResult? Member, TypeFindResult? Type)> selected))
        {
            return false;
        }

        foreach ((MemberFindResult? member, TypeFindResult? type) in selected)
        {
            if (member is not null)
                selectedMembers.Add(member);
            else
                selectedTypes.Add(type!);
        }
        return true;
    }

    /// <summary>
    /// A pattern answered by member rows omits its weak Type rows and is no
    /// longer a miss.
    /// </summary>
    private static List<TypeFindResult> WithoutSupersededWeakRows(
        List<TypeFindResult> typeRows,
        List<MemberFindResult> members)
    {
        if (members.Count == 0)
            return typeRows;

        HashSet<string> answered = new(
            members.Select(static member => member.Pattern),
            StringComparer.Ordinal);
        return
        [
            .. typeRows.Where(row =>
                row.Match is not (TypeFindMatchKind.Partial
                    or TypeFindMatchKind.NotFound)
                || !answered.Contains(row.Pattern)),
        ];
    }

    private static void WriteOmittedMembersNote(
        List<MemberFindResult> members)
    {
        foreach (IGrouping<string, MemberFindResult> group
            in members.GroupBy(static member => member.Pattern))
        {
            CommandError.WriteNote(
                $"{group.Count()} member matches for '{group.Key}' appear "
                + "in TSV and Markdown output. Use "
                + $"'find .{group.Key}' for member rows in this format.");
        }
    }

    private static void WriteUnmatchedPatternWarning(
        FindSearchResult<TypeFindResult> search)
    {
        int unmatchedCount =
            search.UnmatchedPatterns?.Count ?? 0;
        if (unmatchedCount == 0 || search.Rows.Count == 0)
            return;

        CommandError.WriteWarning(
            $"{unmatchedCount} search "
            + (unmatchedCount == 1
                ? "pattern matched"
                : "patterns matched")
            + " no types.");
    }

    private static async Task<FindExecutionResult> ExecuteMemberSearchAsync(
        FindOptions options,
        string[] patterns,
        RowSelectionIntent<string>? rowSelection,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken,
        PlatformFindSearchWorkspace? platformWorkspace,
        ExplicitFindSearchWorkspace? explicitWorkspace,
        FindSearchResult<MemberFindResult>? preparedSearch = null,
        FindDiscoveryTsvWriter? tsv = null,
        Func<int>? streamedRowCount = null)
    {
        // Strip the leading '.' sentinel from each segment so ".Serialize" and "Serialize" both search
        // the member named "Serialize". ".ctor"/".cctor" are preserved (they are real member names).
        var memberPatterns = patterns
            .Select(MemberPatternSentinel.Strip)
            .Where(p => p.Length > 0)
            .ToArray();

        if (memberPatterns.Length == 0)
        {
            CommandError.Write("No member pattern specified.");
            return new(1, RowCount: null);
        }

        FindSearchResult<MemberFindResult> search =
            preparedSearch ?? await MemberSearchService.FindMembersAsync(
                options,
                memberPatterns,
                logger,
                httpClient,
                cancellationToken,
                platformWorkspace,
                explicitWorkspace);
        List<MemberFindResult> results = search.Rows;
        int observedRowCount =
            search.ExactRowCount
            ?? search.InputRows?.AcceptedCount
            ?? results.Count;
        var title =
            memberPatterns.Length == 1
                ? $"Find member: {memberPatterns[0]}"
                : "Find Members";

        if (options.Count)
        {
            if (!CliSemanticRowSelection.TrySelectCount(
                    rowSelection,
                    observedRowCount,
                    static (stage, required, available) =>
                        $"Find row selection stage {stage} requires "
                        + $"member row {required}, but only {available} "
                        + "member rows are available.",
                    out int selectedCount))
            {
                return new(1, RowCount: null);
            }
            if (search.HasFailures
                || !CliSemanticRowSelection.ProvidesExactCount(
                    rowSelection,
                    observedRowCount,
                    sourceComplete:
                        !search.SourceSelectionIncomplete))
            {
                CommandError.Write(
                    "Cannot count member rows because one or more search sources were incomplete.");
                return new(1, RowCount: null);
            }
            if (!WriteMemberCount(
                    selectedCount,
                    title,
                    options))
            {
                return new(1, RowCount: null);
            }
            return new(0, selectedCount);
        }

        IReadOnlyList<MemberFindResult> selectedMembers;
        if (search.InputRows is { } receipt)
        {
            if (options.QueryPlan?.InputRows is { } planned
                && planned != receipt.Selection)
            {
                throw new InvalidOperationException(
                    "Member Find returned an accepted-row receipt for a "
                        + "different query-plan selection.");
            }
            if (!CliSemanticRowSelection.TrySelectCount(
                    rowSelection,
                    observedRowCount,
                    static (stage, required, available) =>
                        $"Find row selection stage {stage} requires "
                        + $"member row {required}, but only {available} "
                        + "member rows are available.",
                    out int selectedCount))
            {
                return new(1, RowCount: null);
            }
            if (search.HasFailures
                || search.SourceSelectionIncomplete)
            {
                CommandError.Write(
                    "Cannot select member rows because one or more search sources were incomplete.");
                return new(1, RowCount: null);
            }
            if (selectedCount != results.Count)
            {
                throw new InvalidOperationException(
                    "Member Find's retained rows do not match its "
                        + "accepted-row selection receipt.");
            }
            selectedMembers = results;
        }
        else if (!TrySelectRows(
                     rowSelection,
                     results,
                     "member",
                     out selectedMembers))
        {
            return new(1, RowCount: null);
        }
        results = [.. selectedMembers];

        if (tsv is not null)
        {
            int alreadyStreamed = streamedRowCount?.Invoke() ?? 0;
            foreach (MemberFindResult row in results.Skip(alreadyStreamed))
                tsv.Write(FindDiscoveryOutput.Project(row));
            if (results.Count == 0)
                CommandError.WriteLine("No members found matching the pattern.");
        }
        else if (options.JsonOutput)
        {
            // See the type-search branch: a projection request lowers --json to the display view.
            if (IsColumnProjectionRequested(options))
            {
                WriteMemberProjectedJson(results, title, options);
            }
            else
            {
                JsonOutputHelper.Write(
                    results,
                    MemberFindResultJsonContext.Default.ListMemberFindResult,
                    MemberFindResultCompactJsonContext.Default.ListMemberFindResult,
                    options.CompactJson);
            }
        }
        else
        {
            WriteMemberOutput(results, title, options);
        }

        return new(0, results.Count);
    }

    internal static bool TrySelectRows<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> rows,
        string rowKind,
        out IReadOnlyList<T> selected)
        => CliSemanticRowSelection.TrySelect(
            intent,
            rows,
            rowKind,
            failure =>
                $"Find row selection stage "
                + $"{failure.Failure.StageNumber} requires "
                + $"{rowKind} row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} "
                + $"{rowKind} rows are available.",
            out selected);

    internal static bool TrySelectRowsPreservingContext<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> events,
        Func<T, bool> isRow,
        string rowKind,
        out IReadOnlyList<T> selectedEvents,
        out int availableRowCount)
        where T : class
        => CliSemanticRowSelection.TrySelectPreservingContext(
            intent,
            events,
            isRow,
            rowKind,
            failure =>
                $"Find row selection stage "
                + $"{failure.Failure.StageNumber} requires "
                + $"{rowKind} row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} "
                + $"{rowKind} rows are available.",
            out selectedEvents,
            out availableRowCount);

    private static bool IsColumnProjectionRequested(FindOptions options)
        => options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };

    /// <summary>
    /// Writes the lowered JSON view of a type search: the same section and column projection the
    /// table formats apply, emitted as JSON (#3494).
    /// </summary>
    private static void WriteProjectedJson(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        OutputFormatter.WriteProjectedJson(Console.Out, options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            !options.CompactJson,
            maxRows: null);
    }

    /// <summary>
    /// Writes the lowered JSON view of a member search. See <see cref="WriteProjectedJson"/>.
    /// </summary>
    private static void WriteMemberProjectedJson(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        OutputFormatter.WriteProjectedJson(Console.Out, options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            !options.CompactJson,
            maxRows: null);
    }

    private static void WriteOutput(
        List<TypeFindResult> rawData,
        string title,
        FindOptions options,
        List<MemberFindResult> members)
    {
        var view = FindOutputFormatter.BuildView(
            rawData,
            title,
            options.Tabular ? null : members);

        if (view.Results == null && view.Members == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, rows: null,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }

    private static bool WriteCount(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Results",
            options.Columns,
            options.Fields,
            rows: null);
    }

    private static void WriteMemberOutput(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, rows: null,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }

    private static bool WriteMemberCount(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Members",
            options.Columns,
            options.Fields,
            rows: null);
    }

    private static bool WriteMemberCount(
        int count,
        string title,
        FindOptions options) =>
        CountOutput.TryWriteProjectedCount<FindMembersResultView>(
            count,
            SearchViewContext.Default,
            "Members",
            options.Columns,
            options.Fields);
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public record class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("library")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }

    [JsonIgnore]
    public TypeDeclarationLocatorSectionCandidate? Location { get; set; }

    [JsonIgnore]
    public AssemblyAcquisitionRegistration? AcquisitionRegistration
    {
        get;
        set;
    }

    [JsonIgnore]
    internal string? ClassifiedPattern { get; set; }

    [JsonIgnore]
    internal FindTypeMatchIntent ClassifiedIntent { get; set; }

    [JsonIgnore]
    internal TypeFindResult? Classification { get; set; }
}
