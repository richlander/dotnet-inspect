using Markout;

namespace DotnetInspector.Presentation;

/// <summary>
/// Lowers settled discovery rows to one append-only TSV table.
/// </summary>
public sealed class FindDiscoveryTsvWriter : IDisposable
{
    public static IReadOnlyList<string> Columns { get; } =
        Array.AsReadOnly(new[]
        {
            "Coordinate", "Kind", "Source", "Library", "Pattern",
            "Declaration", "Signature", "Match", "Ecosystem",
        });

    private readonly TextWriter _output;
    private readonly MarkoutWriter _table;
    private readonly MarkoutWriterOptions _options;
    private bool _started;

    public FindDiscoveryTsvWriter(
        TextWriter output,
        bool showHeader = true,
        MarkoutProjection? projection = null)
    {
        _output = output;
        _options = new MarkoutWriterOptions
        {
            TableMode = MarkoutTableMode.Tsv,
            Projection = projection,
        };
        _table = new(output, new TableFormatter(showHeader), _options);
    }

    public void Write(FindDiscoveryRow row)
    {
        // TableFormatter is batch-only. Lower one settled row per batch,
        // preserving Markout's projection and cell encoding without buffering the stream.
        MarkoutWriter table = _started
            ? new(_output, new TableFormatter(showHeader: false), _options)
            : _table;
        table.WriteTable([.. Columns],
        [
            new[]
            {
                row.Coordinate.ToString(), row.Kind.ToString(),
                row.Source.ToString(), row.Library.ToString(),
                row.Pattern.ToString(), row.Declaration.ToString(),
                row.Signature.ToString(), row.Match.ToString(),
                row.Ecosystem.ToString(),
            },
        ]);
        _started = true;
        _output.Flush();
    }

    public void Dispose()
    {
        _output.Flush();
    }
}
