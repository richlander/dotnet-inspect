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

            using var source = MetadataSource.Open(options.LibraryPath);
            var result = BodyShapeSearch.Search(
                source,
                options.Kind,
                options.IncludeAll,
                options.MatchLimit,
                cancellationToken);

            foreach (var failure in result.Failures)
                CommandError.WriteWarning($"Body-shape search skipped {failure.Subject}: {failure.Reason}");
            if (options.Verbose)
            {
                CommandError.WriteNote(
                    $"inspected {result.MethodsInspected} method bodies; found {result.Matches.Count} matches");
            }

            if (options.Count)
            {
                CountOutput.WriteCount(result.Matches.Count);
            }
            else if (options.JsonOutput)
            {
                JsonOutputHelper.Write(
                    result.Matches.ToList(),
                    BodyShapeJsonContext.Default.ListBodyShapeMatch,
                    BodyShapeCompactJsonContext.Default.ListBodyShapeMatch,
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
        var rows = matches.Select(match => new BodyShapeRow(
            match.Kind,
            match.Member,
            $"0x{match.MethodToken:X8}",
            match.Extent.StartLine + 1,
            match.Extent.StartColumn + 1,
            match.Extent.EndLine + 1,
            match.Extent.EndColumn + 1,
            match.Text)).ToList();
        var view = new BodyShapeResultView
        {
            Title = $"Body shape: {options.Kind}",
            Description = rows.Count == 0
                ? $"No {options.Kind} body shapes found."
                : null,
            Matches = rows.Count == 0 ? null : rows
        };

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
                        BodyShapeViewContext.Default,
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
                    BodyShapeViewContext.Default,
                    writerOptions));
        }
    }
}
