using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The CLI adoption of the Source <c>Lines</c> inventory owned by
/// <c>docs/design/source-document-cardinality.md</c> (slice 4, on Complete
/// execution): type and member <c>Source</c> project the already-settled
/// Source result through <see cref="SourceViewInspection.Project(InspectionEnvelope{AssemblyTypeSourceEntry})"/>,
/// so <c>--count</c> reports the exact line Count and <c>--rows</c> and the row
/// formats select line rows. Default output is unchanged.
/// </summary>
public partial class ApiCommand
{
    private static readonly string[] SourceLineHeaders =
        ["Number", "Start", "Content", "Terminator"];

    private static readonly string[] SourceLineKeys =
        ["number", "start", "content", "terminator"];

    /// <summary>
    /// Projects the selected type or member Source into its ordered line
    /// inventory. A Source that is unavailable or rejected fails with the same
    /// message the native and JSON Source paths report.
    /// </summary>
    internal static bool TryProjectSourceLines(
        ApiOptions options,
        out ImmutableArray<SourceViewLine> lines,
        out string failure)
    {
        lines = default;
        if (!TryCreateSourceDocument(options, out _, out failure))
            return false;

        SourceView? view = options switch
        {
            TypeOptions { TypeSourceInspection: { } type } =>
                SourceViewInspection.Project(type) is
                    SourceViewProjection<AssemblyTypeSourceEntry>.Available available
                    ? available.Inspection.Content
                    : null,
            MemberOptions { MemberSourceInspection: { } member } =>
                SourceViewInspection.Project(member) is
                    SourceViewProjection<AssemblyMemberSourceEntry>.Available available
                    ? available.Inspection.Content
                    : null,
            _ => null,
        };
        if (view is null)
        {
            failure = "The completed Source result has no line inventory.";
            return false;
        }

        lines = view.Lines;
        failure = "";
        return true;
    }

    /// <summary>True when the selection is exactly the <c>Source</c> section.</summary>
    internal static bool IsLoneSourceSelection(ApiOptions options) =>
        options.IncludeSections is { Count: 1 } sections
        && sections.Contains(SectionNames.Source);

    /// <summary>
    /// Writes the selected line rows as a table, TSV, or JSONL: one row per
    /// line with its one-based number, UTF-16 start offset, content without
    /// the terminator, and the terminator's name.
    /// </summary>
    internal static int WriteSourceLineTable(
        TextWriter output,
        ApiOptions options,
        ImmutableArray<SourceViewLine> lines)
    {
        IReadOnlyList<SourceViewLine> windowed = RowWindow.Apply(options.Rows, lines);
        string[][] cells =
        [
            .. windowed.Select(static line => new[]
            {
                line.Number.ToString(CultureInfo.InvariantCulture),
                line.Start.ToString(CultureInfo.InvariantCulture),
                line.Content,
                TerminatorName(line.Terminator),
            }),
        ];
        string[] projection = [.. options.Fields ?? [], .. options.Columns ?? []];
        OutputFormatter.WriteProjectedTable(
            output,
            showHeader: !options.NoHeader,
            tsv: options.Tsv,
            jsonl: options.Jsonl,
            columns: projection.Length > 0 ? projection : null,
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                writerOptions.JsonTypedValues = true;
                var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
                markoutWriter.WriteTable(SourceLineHeaders, SourceLineKeys, cells);
                markoutWriter.Flush();
            });
        return 0;
    }

    /// <summary>
    /// The text of a <c>--rows</c> selection: the selected lines' content,
    /// one per line, for the native payload and the Markdown and plain-text
    /// code sections.
    /// </summary>
    internal static string SelectedSourceLineText(
        RowWindow? rows,
        ImmutableArray<SourceViewLine> lines) =>
        string.Join("\n", RowWindow.Apply(rows, lines).Select(static line => line.Content));

    /// <summary>The exact selected line count, under the active row window.</summary>
    internal static int SelectedSourceLineCount(
        RowWindow? rows,
        ImmutableArray<SourceViewLine> lines) =>
        RowWindow.Apply(rows, lines).Count;

    private static string TerminatorName(SourceViewLineTerminator terminator) =>
        terminator switch
        {
            SourceViewLineTerminator.None => "none",
            SourceViewLineTerminator.CarriageReturnLineFeed => "crlf",
            SourceViewLineTerminator.CarriageReturn => "cr",
            SourceViewLineTerminator.LineFeed => "lf",
            SourceViewLineTerminator.NextLine => "nel",
            SourceViewLineTerminator.LineSeparator => "ls",
            SourceViewLineTerminator.ParagraphSeparator => "ps",
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminator),
                terminator,
                "Unknown Source line terminator."),
        };
}
