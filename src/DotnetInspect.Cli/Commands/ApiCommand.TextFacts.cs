using System.Globalization;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The type and member Text lowerings that <c>docs/design/section-shapes.md</c>
/// assigns to a Text with no row inventory: its row formats show its one fact
/// row, and its JSON value carries the facts and the complete payload (#9456).
/// Composed Markdown keeps every Text's body, a recorded type/member
/// divergence. <c>Source</c> is excluded: its rows are its exact lines.
/// </summary>
public partial class ApiCommand
{
    /// <summary>
    /// The Text sections with one bare payload. <c>Source</c> has its line
    /// inventory; <c>Finding Census</c> is a JSON census document whose owner
    /// rejects row projections; <c>Annotated Source Document</c> has no bare
    /// payload.
    /// </summary>
    internal static IReadOnlySet<string> BarePayloadTextSections { get; } =
        new HashSet<string>(
            [
                SectionNames.ApiDeclarations,
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.CostOverlay,
                SectionNames.SemanticsOverlay,
                SectionNames.PdbSource,
                SectionNames.SourceDiff,
                SectionNames.IL,
            ],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The bare-payload Texts whose row formats show the generic fact row.
    /// <c>Source Diff</c> is not one: its owner already lowers its rows to the
    /// structured comparison metadata and summary, which stay its row form.
    /// </summary>
    internal static IReadOnlySet<string> FactRowTextSections { get; } =
        new HashSet<string>(
            BarePayloadTextSections.Where(static section => section != SectionNames.SourceDiff),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The bare-payload Texts whose <c>--json</c> is the generic facts-plus-content
    /// value. <c>API Declarations</c> is not one: on the <c>type</c> command, the
    /// only route that has its payload, its JSON is the existing dedicated
    /// inspection envelope, whose content carries the complete text.
    /// </summary>
    internal static IReadOnlySet<string> TextPayloadJsonSections { get; } =
        new HashSet<string>(
            BarePayloadTextSections.Where(static section => section != SectionNames.ApiDeclarations),
            StringComparer.OrdinalIgnoreCase);

    private static readonly string[] FactRowHeaders = ["Section", "Lines", "Characters"];
    private static readonly string[] FactRowKeys = ["section", "lines", "characters"];

    /// <summary>
    /// Whether a bare-payload Text has its payload on this route and catalog,
    /// as measured on the production paths: an exact member populates every
    /// member-code Text; the <c>type</c> command's own view populates only the
    /// type API declarations and the whole-type decompiled source; the member
    /// command's type view populates none of them.
    /// </summary>
    internal static bool HasBarePayload(
        Planning.StructuralViewIdentity view,
        Planning.InspectionCatalogIdentity catalog,
        string section) =>
        BarePayloadTextSections.Contains(section)
        && catalog switch
        {
            Planning.InspectionCatalogIdentity.ApiMemberDetail =>
                !section.Equals(SectionNames.ApiDeclarations, StringComparison.OrdinalIgnoreCase),
            Planning.InspectionCatalogIdentity.ApiMember =>
                view == Planning.StructuralViewIdentity.Type
                && (section.Equals(SectionNames.ApiDeclarations, StringComparison.OrdinalIgnoreCase)
                    || section.Equals(SectionNames.DecompiledSource, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };

    /// <summary>
    /// Whether a bare-payload Text's row formats execute on this route, through
    /// the generic fact row or, for <c>Source Diff</c>, its owner's structured rows.
    /// </summary>
    internal static bool HasTextRowFormats(
        Planning.StructuralViewIdentity view,
        Planning.InspectionCatalogIdentity catalog,
        string section) =>
        HasBarePayload(view, catalog, section);

    /// <summary>
    /// Whether a bare-payload Text's <c>--json</c> is the generic facts-plus-content
    /// value on this route (dedicated JSON lowerings are counted separately).
    /// </summary>
    internal static bool HasTextPayloadJson(
        Planning.StructuralViewIdentity view,
        Planning.InspectionCatalogIdentity catalog,
        string section) =>
        TextPayloadJsonSections.Contains(section)
        && HasBarePayload(view, catalog, section);

    /// <summary>The lone selected fact-row Text section, if the selection is exactly one.</summary>
    internal static string? LoneFactRowTextSection(ApiOptions options) =>
        options.IncludeSections is { Count: 1 } sections
        && FactRowTextSections.Contains(sections.First())
            ? sections.First()
            : null;

    /// <summary>
    /// Whether <c>--json</c> on this selection is a lone Text's facts-plus-content
    /// value rather than the type document.
    /// </summary>
    internal static bool IsTextPayloadJson(ApiOptions options) =>
        options.JsonOutput
        && !options.Count
        && !IsProjectionRequested(options)
        && LonePayloadJsonTextSection(options) is not null;

    /// <summary>The lone selected bare-payload Text section, if the selection is exactly one.</summary>
    internal static string? LonePayloadJsonTextSection(ApiOptions options) =>
        options.IncludeSections is { Count: 1 } sections
        && TextPayloadJsonSections.Contains(sections.First())
            ? sections.First()
            : null;

    /// <summary>
    /// Writes a lone Text's fact row as a table, TSV, or JSONL row. A Text that
    /// produced no payload fails visibly rather than writing an empty row.
    /// </summary>
    private static int WriteTextFactRow(
        TextWriter output,
        TypeView view,
        ApiOptions options,
        string section)
    {
        if (!TryGetTextPayload(view, section, out string payload))
            return 1;

        string[][] cells =
        [
            [
                section,
                TextLineCount(payload).ToString(CultureInfo.InvariantCulture),
                payload.Length.ToString(CultureInfo.InvariantCulture),
            ],
        ];
        // A fact row is a summary presentation, not an inventory: the section
        // schemas declare no fact-row fields, so --fields/--columns are rejected
        // by the ordinary projection check before this point.
        OutputFormatter.WriteProjectedTable(
            output,
            showHeader: !options.NoHeader,
            tsv: options.Tsv,
            jsonl: options.Jsonl,
            columns: null,
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                writerOptions.JsonTypedValues = true;
                var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
                markoutWriter.WriteTable(FactRowHeaders, FactRowKeys, cells);
                markoutWriter.Flush();
            });
        return 0;
    }

    /// <summary>
    /// Writes a lone Text's JSON value: its facts and its complete payload.
    /// </summary>
    private static int WriteTextPayloadJson(
        TextWriter output,
        TypeView view,
        ApiOptions options,
        string section)
    {
        if (IsColumnProjectionRequested(options))
            return RejectColumnProjectionUnderJson(suggestPayloadProjection: true);
        if (!TryGetTextPayload(view, section, out string payload))
            return 1;

        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = !options.CompactJson }))
        {
            json.WriteStartObject();
            json.WriteString("section", section);
            json.WriteNumber("lines", TextLineCount(payload));
            json.WriteNumber("characters", payload.Length);
            json.WriteString("content", payload);
            json.WriteEndObject();
        }

        OutputFormatter.WriteLfLine(output, Encoding.UTF8.GetString(buffer.ToArray()));
        return 0;
    }

    private static bool TryGetTextPayload(TypeView view, string section, out string payload)
    {
        payload = GetApiPayloadContent(view, section) ?? "";
        if (payload.Length > 0)
            return true;

        CommandError.Write($"section '{section}' produced no payload.");
        return false;
    }

    /// <summary>
    /// The number of lines the payload prints as: trailing line breaks do not
    /// add a line, and an empty payload has none.
    /// </summary>
    private static int TextLineCount(string payload)
    {
        string trimmed = payload.TrimEnd('\r', '\n');
        if (trimmed.Length == 0)
            return 0;

        int lines = 1;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '\n')
                lines++;
            else if (trimmed[i] == '\r' && (i + 1 == trimmed.Length || trimmed[i + 1] != '\n'))
                lines++;
        }

        return lines;
    }
}
