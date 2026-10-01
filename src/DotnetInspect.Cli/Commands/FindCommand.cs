using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
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
                var schema = options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", "Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source")
                    : new DocumentSchema()
                        .Add("Results", "column", "Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim");
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

            PlatformFindSearchWorkspace? platformWorkspace = null;
            if (searchOptions.UsesImplicitPlatform)
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
                platformWorkspace is null
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
                    explicitWorkspace);
            }

            FindSearchResult<TypeFindResult> search =
                await TypeSearchService.FindTypesAsync(
                    searchOptions,
                    patterns,
                    logger,
                    context.HttpClient,
                    cancellationToken,
                    context,
                    platformWorkspace,
                    explicitWorkspace);
            FindSearchResult<MemberFindResult>? memberTier =
                await FindBroadenedMembersAsync(
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
                WithoutSupersededWeakRows(search.Rows, members);
            int observedRowCount = results.Count;
            bool rendersMembers =
                !options.Count
                && !options.JsonOutput
                && !options.Tabular;
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

            return new(0, results.Count + (rendersMembers ? members.Count : 0));
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
                + "only in Markdown output. Use "
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
        ExplicitFindSearchWorkspace? explicitWorkspace)
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
            await MemberSearchService.FindMembersAsync(
                options,
                memberPatterns,
                logger,
                httpClient,
                cancellationToken,
                platformWorkspace,
                explicitWorkspace);
        List<MemberFindResult> results = search.Rows;
        int observedRowCount =
            search.InputRows?.AcceptedCount
            ?? results.Count;
        IReadOnlyList<MemberFindResult> selectedMembers;
        if (search.InputRows is { } receipt)
        {
            if (options.QueryPlan?.InputRows != receipt.Selection)
            {
                throw new InvalidOperationException(
                    "Member Find returned an accepted-row receipt for a "
                    + "different query-plan selection.");
            }
            if (!CliSemanticRowSelection.TrySelectCount(
                    rowSelection,
                    observedRowCount,
                    static (stage, required, available) =>
                        $"Find row selection stage "
                        + $"{stage} requires member row "
                        + $"{required}, but only "
                        + $"{available} member rows are "
                        + "available.",
                    out int selectedCount))
            {
                return new(1, RowCount: null);
            }
            if (selectedCount != results.Count)
            {
                throw new InvalidOperationException(
                    "Member Find's retained rows do not match its accepted-"
                    + $"row selection receipt: selected {selectedCount}, "
                    + $"retained {results.Count}, accepted "
                    + $"{observedRowCount}, input "
                    + $"{receipt.Selection.Start}.."
                    + $"{receipt.Selection.End}.");
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
        var title = memberPatterns.Length == 1 ? $"Find member: {memberPatterns[0]}" : "Find Members";

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
                    "Cannot count member rows because one or more search sources were incomplete.");
                return new(1, RowCount: null);
            }
            if (!WriteMemberCount(results, title, options))
                return new(1, RowCount: null);
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
