using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches one assembly for an exact stable rendered-syntax kind.
/// </summary>
public static class BodyShapeCommand
{
    public const string Name = "body-shape";

    public static async Task<int> ExecuteAsync(
        BodyShapeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.Discover is not null
            || options.JsonOutput && !options.Count
                && (options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 }))
        {
            return Execute(options, cancellationToken);
        }

        try
        {
            var context = new CommandContext(options.Verbose);
            string? pdbPath = await ApiCommand.TryAcquirePdbPathAsync(
                options.LibraryPath,
                new ApiOptions
                {
                    AssemblyPath = options.LibraryPath,
                    Verbose = options.Verbose
                },
                context.Logger,
                context.HttpClient,
                cancellationToken);
            return Execute(options with { PdbPath = pdbPath }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CommandError.Write("Body-shape search was cancelled.");
            return 1;
        }
    }

    public static int Execute(BodyShapeOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            if (options.Discover is not null)
            {
                var schema = new DocumentSchema()
                    .Add("Matches", "column",
                        "Kind", "Member", "Token",
                        "Start Line", "Start Column", "End Line", "End Column", "Match");
                return DiscoverOutput.Execute(
                    options.Discover,
                    schema,
                    tree: options.Tree,
                    json: options.JsonOutput,
                    tsv: options.Tsv,
                    jsonl: options.Jsonl,
                    projection: options);
            }

            if (options.JsonOutput && !options.Count
                && (options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 }))
            {
                CommandError.Write(
                    "--fields/--columns select table columns and cannot be combined with --json, "
                    + "which emits the complete match records. Use --tsv, --jsonl, or --table.");
                return 1;
            }

            options.RenderConfigWarnings?.EmitOnce();
            BodyShapeSearchResult result;
            try
            {
                using var source = MetadataSource.Open(options.LibraryPath, options.PdbPath);
                result = BodyShapeSearch.Search(
                    source,
                    options.Kind,
                    options.IncludeAll,
                    options.MatchLimit,
                    cancellationToken,
                    options.RenderOptions);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
            {
                CommandError.Write($"Could not read library: {options.LibraryPath}");
                return 1;
            }

            if (options.Verbose)
            {
                foreach (var failure in result.Failures)
                    CommandError.WriteWarning($"Body-shape search skipped {failure.Subject}: {failure.Reason}");
            }
            else if (result.Failures.Count > 0)
            {
                CommandError.WriteWarning(
                    $"Body-shape search skipped {result.Failures.Count} candidates; "
                    + "rerun with --verbose for details.");
            }
            if (options.Verbose)
            {
                CommandError.WriteNote(
                    $"inspected {result.MethodsInspected} method bodies; found {result.Matches.Count} matches");
            }

            if (options.Count)
            {
                var view = CreateView(result.Matches, options.Kind);
                if (!CountOutput.TryWriteProjected(
                        view,
                        InspectionContext.Default,
                        "Matches",
                        options.Columns,
                        options.Fields,
                        options.Rows))
                {
                    return 1;
                }
            }
            else if (options.JsonOutput)
            {
                var matches = result.Matches
                    .Select(BodyShapeJsonMatch.FromMatch)
                    .ToList();
                JsonOutputHelper.Write(
                    matches,
                    BodyShapeJsonContext.Default.ListBodyShapeJsonMatch,
                    BodyShapeCompactJsonContext.Default.ListBodyShapeJsonMatch,
                    options.CompactJson);
            }
            else
            {
                WriteOutput(result.Matches, options);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            CommandError.Write("Body-shape search was cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    static void WriteOutput(IReadOnlyList<BodyShapeMatch> matches, BodyShapeOptions options)
    {
        var view = CreateView(matches, options.Kind);

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        InspectionContext.Default,
                        writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(
                Console.Out,
                options.Rows,
                writerOptions => MarkoutSerializer.Serialize(
                    view,
                    InspectionContext.Default,
                    writerOptions),
                options.Columns,
                options.Fields);
        }
    }

    private static BodyShapeResultView CreateView(
        IReadOnlyList<BodyShapeMatch> matches,
        string kind)
    {
        var rows = matches.Select(BodyShapeRow.FromMatch).ToList();
        var view = new BodyShapeResultView
        {
            Title = $"Body shape: {kind}",
            Description = rows.Count == 0
                ? $"No {kind} body shapes found."
                : null,
            Matches = rows.Count == 0 ? null : rows
        };
        return view;
    }
}
