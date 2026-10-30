using Markout;

namespace DotnetInspector.Presentation;

/// <summary>
/// Lowers settled discovery rows to one append-only TSV or JSONL table.
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
        MarkoutProjection? projection = null,
        bool jsonl = false)
    {
        _output = output;
        _options = new MarkoutWriterOptions
        {
            TableMode = jsonl
                ? MarkoutTableMode.Jsonl
                : MarkoutTableMode.Tsv,
            Projection = projection,
        };
        _table = new(output, new TableFormatter(showHeader), _options);
    }

    public FindDiscoveryTsvWriter(
        TextWriter output,
        bool showHeader,
        string[]? columns,
        string[]? fields,
        bool jsonl = false)
        : this(
            output,
            showHeader,
            CreateProjection(columns, fields),
            jsonl)
    {
    }

    private static MarkoutProjection? CreateProjection(
        string[]? columns,
        string[]? fields)
    {
        if (columns is null && fields is null)
            return null;

        RejectDuplicateNames(columns, "column");
        RejectDuplicateNames(fields, "field");
        return new MarkoutProjection
        {
            IncludeColumns = columns,
            IncludeFields = fields,
        };
    }

    private static void RejectDuplicateNames(string[]? names, string kind)
    {
        if (names is not { Length: > 1 })
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!seen.Add(name))
                throw new ArgumentException($"Duplicate {kind} name: {name}");
        }
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
