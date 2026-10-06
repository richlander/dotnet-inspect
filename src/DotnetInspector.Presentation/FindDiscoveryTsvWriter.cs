using Markout;

namespace DotnetInspector.Presentation;

/// <summary>
/// Lowers settled discovery rows to one append-only TSV table.
/// </summary>
public sealed class FindDiscoveryTsvWriter : IDisposable
{
    private const int BatchSize = 64;

    public static IReadOnlyList<string> Columns { get; } =
        Array.AsReadOnly(new[]
        {
            "Coordinate", "Kind", "Source", "Library", "Pattern",
            "Declaration", "Signature", "Match", "Ecosystem",
        });

    private readonly TextWriter _output;
    private readonly MarkoutWriter _table;
    private readonly MarkoutWriterOptions _options;
    private readonly List<string[]> _pending = new(BatchSize);
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
        string[] cells =
        [
            row.Coordinate.ToString(), row.Kind.ToString(),
            row.Source.ToString(), row.Library.ToString(),
            row.Pattern.ToString(), row.Declaration.ToString(),
            row.Signature.ToString(), row.Match.ToString(),
            row.Ecosystem.ToString(),
        ];
        if (!_started)
        {
            _table.WriteTable([.. Columns], [cells]);
            _started = true;
            _output.Flush();
            return;
        }

        _pending.Add(cells);
        if (_pending.Count == BatchSize)
            Flush();
    }

    /// <summary>
    /// Writes pending rows as one continuation batch and flushes the output.
    /// </summary>
    public void Flush()
    {
        if (_pending.Count > 0)
        {
            var continuationTable =
                new MarkoutWriter(
                    _output,
                    new TableFormatter(showHeader: false),
                    _options);
            continuationTable.WriteTable(
                [.. Columns],
                _pending.ToArray());
            _pending.Clear();
        }
        _output.Flush();
    }

    public void Dispose()
    {
        Flush();
    }
}
