using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Findings;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal static class MemberSourcePartsOutput
{
    internal static string? ValidateOptions(MemberOptions options)
    {
        if (!options.SourceParts && options.SourcePart is null)
            return null;
        if (options.SourcePart is not null && !options.Print)
            return "--part requires --print.";
        if (options.SourceParts && options.Print && options.SourcePart is null)
            return "Use --print --part to print a member part, or omit --source-parts to print the whole file.";
        if (options.Value || options.Urls || options.Paths || options.Count
            || options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 }
            || options.ShareFormat is not null)
            return "Authored member parts do not compose with scalar, column, count, or Share projections.";
        if (options.MermaidOutput || options.EmbeddedMermaid)
            return "Authored member parts are not a graph; omit the Mermaid format.";
        if (options.Tabular && !options.Jsonl)
            return "Authored member parts use Markdown, plaintext, or JSON rather than table/TSV lowering.";
        if (options.NoHeader && options.SourcePart is null && !options.JsonOutput)
            return "Part catalogs do not support --no-headers; use --json or --print --part.";
        if (options.SourcePart is null && (options.Tabular || options.Jsonl || options.JsonArray || options.Rows is not null))
            return "--source-parts is a complete member document; use Markdown, plaintext, or --json, or select --print --part.";
        return null;
    }

    internal static string? ValidateSections(MemberOptions options) =>
        IsSourceLocationsOnly(options)
            ? null
            : "Authored member parts require Source Locations as the only selected section.";

    internal static bool Handles(MemberOptions options) =>
        options.SourceParts || options.SourcePart is not null
        || (IsSourceLocationsOnly(options) && options.JsonOutput && !options.Print
            && !options.Value && !options.Urls && !options.Paths && !options.Count
            && options.Columns is not { Length: > 0 } && options.Fields is not { Length: > 0 });

    internal static async Task<int> WriteAsync(
        ApiType type,
        MemberOptions options,
        ResolvedAssemblyReference? sourceAssembly,
        string? packageName,
        string? packageVersion,
        HttpClient symbolClient,
        TextWriter output)
    {
        if (ValidateSections(options) is { } sectionError)
        {
            CommandError.Write(sectionError);
            return 1;
        }

        var members = ApiOutputFormatter.GetSourceLocationMembers(type, options)
            .Where(member => member.SourceFilePath is not null || member.SourceUrl is not null
                || member.SourceLineNumber is not null)
            .ToList();
        members = [.. RowWindow.Apply(options.Rows, members)];
        if (!options.SourceParts && options.SourcePart is null)
        {
            WriteJson(members.Select(member => Location(type, member, options)).ToArray(), options, output);
            return 0;
        }

        int selectedIndex = 0;
        if (options.PrintRow is { } row)
        {
            int selected = row.Resolve(Enumerable.Range(1, members.Count).ToList());
            if (selected < 1 || selected > members.Count)
            {
                CommandError.Write($"row {selected} is not in Source Locations.");
                return 1;
            }
            selectedIndex = selected - 1;
        }
        else if (members.Count != 1)
        {
            CommandError.Write(members.Count == 0
                ? "The selected member has no PDB source location."
                : "Authored member parts require one selected member; choose an overload such as Name:1, or use --print --part with --row.");
            return 1;
        }
        var selectedMember = members[selectedIndex];
        if (options.SourceLocationMappings is null
            || !options.SourceLocationMappings.TryGetValue(selectedMember, out var mapping)
            || (sourceAssembly?.Path ?? options.DllPath ?? type.SourceAssemblyPath) is not { } assemblyPath)
        {
            CommandError.Write("The selected member has no exact authored-source acquisition identity.");
            return 1;
        }

        var (participant, context) = AuthoredSourceDocumentPrinter.CreateContext(
            assemblyPath, options, sourceAssembly, packageName, packageVersion, symbolClient);
        ApiMember? sourceMember = selectedMember.MetadataToken == mapping.MetadataToken
            ? selectedMember
            : ApiOutputFormatter.AccessorMethods(selectedMember, type)
                .FirstOrDefault(accessor => accessor.MetadataToken == mapping.MetadataToken);
        if (sourceMember is null)
        {
            CommandError.Write("The PDB location does not identify a selected member or accessor.");
            return 1;
        }
        var request = AssemblyMemberSourceRequest.From(type, sourceMember).WithAuthoredParts();
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using var group = workspace.CreateAssemblyContextGroup([participant]);
            inspection = await MemberSourceInspection.ExecuteAsync(group, participant, request, context);
        }
        if (inspection.Content is not AssemblyMemberSourceEntry.Available
            { Source: AssemblyMemberSource.Pdb { MemberDocument: { } document } source })
        {
            string detail = inspection.Content switch
            {
                AssemblyMemberSourceEntry.Unavailable
                    { PdbAttempt.Lines.Value: FindingInspection<string>.Failed failed } => failed.Error.Reason,
                AssemblyMemberSourceEntry.Unavailable
                    { PdbAttempt.Lines.Value: FindingInspection<string>.Absent absent } =>
                    absent.Detail ?? "The selected authored member is unavailable.",
                AssemblyMemberSourceEntry.Unavailable unavailable => unavailable.Failure.Detail,
                AssemblyMemberSourceEntry.Rejected rejected => rejected.Failure.ToString(),
                _ => throw new InvalidOperationException("Unexpected authored member-parts result."),
            };
            CommandError.Write($"Could not acquire verified member parts: {detail}");
            return 1;
        }

        var catalog = MemberSourcePartsProjection.CreateCatalog(document.Parts);
        var result = Location(type, selectedMember, options) with
        {
            Document = new(source.Inspection.Document!.OriginalPath,
                Url(source.Inspection.Mapping!.ResolvedUrl, options)),
            PdbSpan = new(source.Inspection.Mapping.StartLine, source.Inspection.Mapping.EndLine),
            Parts = catalog.ToDictionary(
                part => MemberSourcePartsProjection.Name(part.Kind).Replace('-', '_'),
                part => new MemberSourcePartRange(
                    part.Spans[0].Lines.StartLine,
                    part.Spans[^1].Lines.EndLine,
                    part.Spans.Length > 1
                        ? part.Spans.Select(span => new MemberSourceLineRange(
                            span.Lines.StartLine, span.Lines.EndLine)).ToArray()
                        : null)),
        };
        if (options.SourcePart is { } kind)
        {
            MemberSourcePart? part = catalog.FirstOrDefault(part => part.Kind == kind);
            if (part is null)
            {
                CommandError.Write($"The selected member has no '{MemberSourcePartsProjection.Name(kind)}' part.");
                return 1;
            }
            string text = MemberSourcePartsProjection.GetText(document, part);
            result = result with { Part = MemberSourcePartsProjection.Name(kind), Content = text };
            if (options.JsonOutput || options.Jsonl || options.JsonArray)
            {
                ProjectionAudit.MarkHonored(ProjectionAudit.Print);
                WriteJson([result], options, output);
                return 0;
            }

            var printable = new PrintableDocument(
                selectedIndex + 1,
                SectionNames.SourceLocations,
                $"{result.Member} ({result.Part})",
                result.Document.Path,
                result.Document.Url,
                MemberSourcePartsProjection.GetDisplayText(document.Text, part))
            {
                Language = "csharp",
            };
            return PrintProjectionOutput.Write(
                [printable],
                new PrintProjectionOptions(
                    Row: null,
                    JsonOutput: false,
                    Jsonl: false,
                    JsonArray: false,
                    Destination: new(null, options.Rows),
                    Markdown: options.UsesMarkdownPayloadFormat));
        }

        if (options.JsonOutput)
            WriteJson([result], options, output);
        else
        {
            var writer = new MarkoutWriter(output,
                options.PlainText ? new PlainTextFormatter() : new MarkdownFormatter());
            writer.WriteTable(["Part", "Start Line", "End Line"], ["part", "start_line", "end_line"],
                catalog.SelectMany(part => part.Spans.Select(span => new[]
                {
                    MemberSourcePartsProjection.Name(part.Kind),
                    span.Lines.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    span.Lines.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })).ToArray());
            writer.Flush();
        }
        return 0;
    }

    private static bool IsSourceLocationsOnly(MemberOptions options) =>
        options.IncludeSections is { Count: 1 } sections && sections.Contains(SectionNames.SourceLocations);

    private static MemberSourceLocationContent Location(ApiType type, ApiMember member, MemberOptions options) =>
        new(ApiMemberIdentity.GetCanonicalSignature(type, member),
            new(member.SourceFilePath, Url(member.SourceUrl, options)),
            member.SourceLineNumber is { } line
                ? new(line, member.SourceEndLineNumber ?? line)
                : null);

    private static string? Url(string? url, MemberOptions options) =>
        options.PreferRenderedUrls && url is not null ? GitHubUrlResolver.ConvertRawToBlobUrl(url) : url;

    private static void WriteJson(MemberSourceLocationContent[] records, MemberOptions options, TextWriter output)
    {
        var context = new MemberSourceLocationJsonContext(new(MemberSourceLocationJsonContext.Default.Options)
        {
            WriteIndented = options.JsonOutput && !options.CompactJson,
        });
        string json = records.Length == 1 && !options.JsonArray
            ? JsonSerializer.Serialize(records[0], context.MemberSourceLocationContent)
            : JsonSerializer.Serialize(records, context.MemberSourceLocationContentArray);
        output.WriteLine(json);
    }
}

internal sealed record MemberSourceLocationContent(
    string Member,
    MemberSourceDocumentLocation Document,
    MemberSourceLineRange? PdbSpan)
{
    public Dictionary<string, MemberSourcePartRange>? Parts { get; init; }
    public string? Part { get; init; }
    public string? Content { get; init; }
}

internal sealed record MemberSourceDocumentLocation(string? Path, string? Url);
internal sealed record MemberSourceLineRange(int StartLine, int EndLine);
internal sealed record MemberSourcePartRange(int StartLine, int EndLine, MemberSourceLineRange[]? Fragments);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberSourceLocationContent))]
[JsonSerializable(typeof(MemberSourceLocationContent[]))]
internal partial class MemberSourceLocationJsonContext : JsonSerializerContext;
