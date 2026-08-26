using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Vocabulary;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>Renders product-owned query vocabularies as ordinary sections.</summary>
public static class VocabularyCommand
{
    public const string Name = "vocabulary";

    public static int Execute(VocabularyOptions options)
    {
        VocabularyDocument document = VocabularyCatalog.Document;
        string[] sectionNames = [.. document.Sections.Select(section => section.Name)];
        IReadOnlyDictionary<string, string[]> categoryMap = CreateCategoryMap(document);
        DocumentSchema schema = CreateSchema(document);
        string[]? projectedColumns = ResolveProjectedColumns(options);
        string[]? discover = NormalizeSectionIds(options.Discover, document);
        string[]? select = NormalizeSectionIds(options.Select, document);

        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                discover,
                schema,
                projection: options,
                tree: options.Tree,
                json: options.JsonOutput,
                tsv: options.Tsv,
                jsonl: options.Jsonl,
                markdown: !options.Tabular && !options.JsonOutput && !options.PlainText,
                sectionCategories: categoryMap,
                plainText: options.PlainText);
        }

        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            select,
            sectionNames,
            infoSections: [VocabularyCatalog.SectionsSection],
            categoryMap,
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        HashSet<string> selectedNames = selection.Sections
            ?? new HashSet<string>(
                [VocabularyCatalog.SectionsSection],
                StringComparer.OrdinalIgnoreCase);
        VocabularySection[] sections =
        [
            .. document.Sections.Where(section => selectedNames.Contains(section.Name)),
        ];

        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                selectedNames,
                fields: options.Fields,
                columns: options.Columns))
        {
            return 1;
        }
        VocabularySection[] renderedSections = projectedColumns is { Length: > 0 }
            ?
            [
                .. sections.Where(section =>
                    schema.ValidateProjection(section.Name, projectedColumns)
                        .Resolved.Length > 0),
            ]
            : sections;

        if (options.Count)
        {
            if (sections.Length == 1)
            {
                CountOutput.WriteCount(RowWindow.Apply(options.Rows, sections[0].Values).Count);
            }
            else
            {
                string[] orderedSections = [.. sections.Select(section => section.Name)];
                if (!CountOutput.ValidateMapFormat(
                        options.Format,
                        orderedSections,
                        options.Tree))
                {
                    return 1;
                }

                var renderedNames = renderedSections
                    .Select(section => section.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var projection = new CountProjection();
                foreach (VocabularySection section in sections)
                {
                    projection.SetRows(
                        section.Name,
                        renderedNames.Contains(section.Name)
                            ? RowWindow.Apply(options.Rows, section.Values).Count
                            : 0);
                }
                CountOutput.Write(
                    projection,
                    orderedSections,
                    options.Format,
                    options.NoHeader);
            }
            return 0;
        }

        if (options.Tabular && sections.Length != 1)
        {
            CommandError.Write(
                "Tabular vocabulary output requires exactly one selected section; "
                + "use -S <section>.");
            return 1;
        }

        if (options.JsonOutput)
        {
            if (projectedColumns is { Length: > 0 })
            {
                OutputFormatter.WriteProjectedJson(
                    Console.Out,
                    projectedColumns,
                    fields: null,
                    (writer, formatter, writerOptions) =>
                        WriteSections(
                            new MarkoutWriter(writer, formatter, writerOptions),
                            renderedSections,
                            includeDocumentHeading: true),
                    maxRows: options.Rows);
            }
            else
            {
                VocabularySection[] windowed =
                [
                    .. sections.Select(section => section with
                    {
                        Values = [.. RowWindow.Apply(options.Rows, section.Values)],
                    }),
                ];
                Console.WriteLine(VocabularyJson.Serialize(document, windowed));
            }
            return 0;
        }

        if (options.Tabular)
        {
            VocabularySection section = sections[0];
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                showHeader: !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    var markout = new MarkoutWriter(writer, formatter, writerOptions);
                    WriteTable(markout, section);
                    markout.Flush();
                },
                options.Rows);
            return 0;
        }

        var markdownOptions = OutputFormatter.CreateProjectedWriterOptions(
            projectedColumns,
            fields: null,
            options.Rows);
        var markdown = new MarkoutWriter(
            Console.Out,
            options.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(),
            markdownOptions);
        WriteSections(markdown, renderedSections, includeDocumentHeading: true);
        markdown.Flush();
        return 0;
    }

    private static void WriteSections(
        MarkoutWriter writer,
        IEnumerable<VocabularySection> sections,
        bool includeDocumentHeading)
    {
        if (includeDocumentHeading)
            writer.WriteHeading(1, "Vocabulary");
        foreach (VocabularySection section in sections)
        {
            writer.WriteHeading(2, section.Name);
            writer.WriteParagraph(section.Summary);
            WriteTable(writer, section);
        }
    }

    private static void WriteTable(
        MarkoutWriter writer,
        VocabularySection section)
    {
        string[] labels = [.. section.Fields.Select(field => field.Label)];
        string[] ids = [.. section.Fields.Select(field => field.Id)];
        string[][] rows =
        [
            .. section.Values.Select(row =>
                section.Fields.Select(field =>
                    row.TryGetValue(field.Id, out VocabularyValue value)
                        ? value.ToDisplayString()
                        : "").ToArray()),
        ];
        writer.WriteTable(labels, ids, rows);
    }

    private static DocumentSchema CreateSchema(VocabularyDocument document)
    {
        var schema = new DocumentSchema();
        foreach (VocabularySection section in document.Sections)
        {
            schema.Add(
                section.Name,
                "column",
                [.. section.Fields.Select(field => field.Label)]);
        }
        return schema;
    }

    private static IReadOnlyDictionary<string, string[]> CreateCategoryMap(
        VocabularyDocument document)
    {
        var categories = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (VocabularySection section in document.Sections)
        {
            foreach (string category in section.Categories)
            {
                if (!categories.TryGetValue(category, out List<string>? members))
                {
                    members = [];
                    categories.Add(category, members);
                }
                members.Add(section.Name);
            }
        }
        categories[SelectResolver.AllSelector] =
            [.. document.Sections.Select(section => section.Name)];
        return categories.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string[]? NormalizeSectionIds(
        string[]? values,
        VocabularyDocument document)
    {
        if (values is null)
            return null;

        return
        [
            .. values.Select(value =>
                document.Sections.FirstOrDefault(section =>
                    section.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Name
                ?? value),
        ];
    }

    private static string[]? ResolveProjectedColumns(VocabularyOptions options)
    {
        if (options.Columns is not { Length: > 0 })
            return options.Fields is { Length: > 0 } ? options.Fields : null;
        if (options.Fields is not { Length: > 0 })
            return options.Columns;

        return
        [
            .. options.Columns
                .Concat(options.Fields)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
