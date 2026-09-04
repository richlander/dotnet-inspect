using System.Collections.Immutable;

using ILInspector.Findings;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// A Source Diff section that retains typed analysis while Markout owns the
/// selected summary or complete mapped-diff presentation.
/// </summary>
public sealed class SourceDiffOutput : IMarkoutFormattable
{
    /// <summary>The plain representation used by bare and print projections.</summary>
    public string Content { get; }

    /// <summary>The producer-owned source-text analysis, when comparison completed.</summary>
    public AnalysisDiff<string>? Analysis { get; }

    /// <summary>The complete mapped diff, when the endpoints differ.</summary>
    public MappedTextDiff? Diff { get; }

    /// <summary>Structured provenance, status, or summary fields rendered before the diff.</summary>
    public ImmutableArray<MarkoutField> Fields { get; }

    /// <summary>Whether the complete mapped diff is part of this projection.</summary>
    public bool ShowDiff { get; }

    /// <summary>Creates a visible status result without a successful comparison.</summary>
    public SourceDiffOutput(string status)
        : this(
            analysis: null,
            diff: null,
            fields: [new MarkoutField("Status", status)],
            showDiff: false)
    {
    }

    /// <summary>Creates a visible status result for a completed comparison.</summary>
    public SourceDiffOutput(string status, AnalysisDiff<string> analysis)
        : this(
            analysis,
            diff: null,
            fields: [new MarkoutField("Status", status)],
            showDiff: false)
    {
    }

    SourceDiffOutput(
        AnalysisDiff<string>? analysis,
        MappedTextDiff? diff,
        IEnumerable<MarkoutField> fields,
        bool showDiff)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Analysis = analysis;
        Diff = diff;
        Fields = [.. fields];
        ShowDiff = showDiff;
        Content = RenderPlain(Fields, showDiff ? diff : null);
    }

    internal static SourceDiffOutput CreateSummary(
        AnalysisDiff<string> analysis,
        MappedTextDiff diff,
        IEnumerable<MarkoutField> fields)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(diff);
        return new SourceDiffOutput(analysis, diff, fields, showDiff: false);
    }

    internal static SourceDiffOutput CreateDetailed(
        AnalysisDiff<string> analysis,
        MappedTextDiff diff)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(diff);
        return new SourceDiffOutput(analysis, diff, [], showDiff: true);
    }

    /// <summary>Returns a copy with source provenance fields before the current projection.</summary>
    public SourceDiffOutput WithMetadata(params ReadOnlySpan<MarkoutField> metadata)
        => metadata.IsEmpty
            ? this
            : new SourceDiffOutput(
                Analysis,
                Diff,
                [.. metadata, .. Fields],
                ShowDiff);

    /// <inheritdoc/>
    public void WriteTo(MarkoutWriter writer)
    {
        if (!Fields.IsEmpty)
        {
            if (writer.DecomposesCompositeCells)
                writer.WriteFieldsTable(Fields.AsSpan());
            else
                writer.WriteFields(Fields.AsSpan());
        }

        if (ShowDiff && Diff is not null)
            writer.WriteTextDiff(Diff);
    }

    /// <inheritdoc/>
    public string? ToMarkoutString() => Content;

    static string RenderPlain(
        ImmutableArray<MarkoutField> fields,
        MappedTextDiff? diff)
    {
        var writer = MarkoutWriter.Create(
            new PlainTextFormatter(),
            new MarkoutWriterOptions
            {
                NewLine = "\n",
                TextDiffContextLines = null
            });
        if (!fields.IsEmpty)
            writer.WriteFields(fields.AsSpan());
        if (diff is not null)
            writer.WriteTextDiff(diff);
        return writer.ToString().TrimEnd('\r', '\n');
    }
}
