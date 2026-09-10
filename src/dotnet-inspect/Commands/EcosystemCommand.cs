using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Commands;

/// <summary>Renders the product-owned ecosystem registry as ordinary sections.</summary>
/// <remarks>
/// Discovery, not observation: this command answers what the product knows about an ecosystem.
/// What a specific artifact contains belongs to <c>library</c>, <c>type</c>, and <c>member</c>.
/// </remarks>
public static class EcosystemCommand
{
    public const string Name = "ecosystem";

    public static int Execute(EcosystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EcosystemSectionCatalog catalog = EcosystemSections.CreateCatalog();
        string[] sectionNames = [.. EcosystemSections.SectionOrder];
        IReadOnlyDictionary<string, string[]> categoryMap =
            catalog.Sections.SelectionCategoryMap;
        DocumentSchema schema = CreateSchema();
        string[]? projectedColumns = options.Columns;

        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
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
            options.Select,
            sectionNames,
            infoSections: [EcosystemSections.Ecosystems],
            categoryMap,
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        // Without an explicit selection the registry index is the whole answer: Pruning is
        // explicit-only, so it never joins an automatic view.
        HashSet<string> includeSections = selection.Sections
            ?? new HashSet<string>([EcosystemSections.Ecosystems], StringComparer.OrdinalIgnoreCase);

        // Reading prune data is only warranted when a platform section was actually selected.
        bool wantsPruning = includeSections.Contains(EcosystemSections.Pruning);
        EcosystemProjection projection = EcosystemQuery.Execute(
            new EcosystemQueryContext(wantsPruning ? options.Framework ?? "runtime" : null));

        if (options.Ecosystem is { Length: > 0 } requested)
        {
            projection = NarrowToEcosystem(projection, requested);
            if (projection.Ecosystems.IsEmpty)
            {
                CommandError.Write($"Unknown ecosystem '{requested}'.");
                return 1;
            }
        }

        if (projection.PruningUnavailable is { Length: > 0 } unavailable)
        {
            // Keep the failure visible without withholding the registry answer.
            CommandError.Write(unavailable);
        }

        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                includeSections,
                options.Fields,
                options.Columns))
        {
            return 1;
        }

        if (options.Count)
        {
            CountOutput.WriteCount(
                includeSections
                    .Select(section => EcosystemSections.CountRows(projection, section))
                    .Sum());
            return 0;
        }

        if (options.Tabular)
        {
            EcosystemTableView tables = BuildTableView(projection, includeSections);
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                showHeader: !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        tables, writer, formatter, EcosystemViewContext.Default, writerOptions);
                },
                options.Rows);
            return 0;
        }

        if (options.JsonOutput)
        {
            EcosystemTableView tables = BuildTableView(projection, includeSections);
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        tables, writer, formatter, EcosystemViewContext.Default, writerOptions);
                },
                indented: true);
            return 0;
        }

        EcosystemView view = BuildView(projection, includeSections);
        var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
            projectedColumns, fields: null, options.Rows);
        writerOptions.IncludeSections = includeSections;
        IMarkoutFormatter formatter = options.PlainText
            ? new PlainTextFormatter()
            : new MarkdownFormatter();
        MarkoutSerializer.Serialize(
            view, Console.Out, formatter, EcosystemViewContext.Default, writerOptions);
        return 0;
    }

    internal static DocumentSchema CreateSchema() =>
        EcosystemViewContext.Default.GetSchemaInfo<EcosystemView>()!.ToDocumentSchema();

    internal static DocumentSchema CreateTableSchema() =>
        EcosystemViewContext.Default.GetSchemaInfo<EcosystemTableView>()!.ToDocumentSchema();

    /// <summary>Narrows the registry to one ecosystem by id or title, case-insensitively.</summary>
    private static EcosystemProjection NarrowToEcosystem(
        EcosystemProjection projection,
        string requested) =>
        new()
        {
            Ecosystems =
            [
                .. projection.Ecosystems.Where(row =>
                    row.Id.Equals(requested, StringComparison.OrdinalIgnoreCase)
                    || row.Id.EndsWith("." + requested, StringComparison.OrdinalIgnoreCase)
                    || row.Title.Equals(requested, StringComparison.OrdinalIgnoreCase)),
            ],
            Pruning = projection.Pruning,
            PlatformTarget = projection.PlatformTarget,
            PruningUnavailable = projection.PruningUnavailable,
        };

    internal static EcosystemView BuildView(
        EcosystemProjection projection,
        HashSet<string> includeSections) =>
        new()
        {
            EcosystemCount = projection.Ecosystems.Length,
            PlatformTarget = projection.PlatformTarget,
            SubsumedPackageCount = projection.Pruning.Length,
            Ecosystems = Rows(
                includeSections, EcosystemSections.Ecosystems,
                projection.Ecosystems.Select(EcosystemEntryView.From)),
            Pruning = Rows(
                includeSections, EcosystemSections.Pruning,
                projection.Pruning.Select(EcosystemPruningView.From)),
        };

    internal static EcosystemTableView BuildTableView(
        EcosystemProjection projection,
        HashSet<string> includeSections) =>
        new()
        {
            Ecosystems = Rows(
                includeSections, EcosystemSections.Ecosystems,
                projection.Ecosystems.Select(EcosystemEntryView.From)),
            Pruning = Rows(
                includeSections, EcosystemSections.Pruning,
                projection.Pruning.Select(EcosystemPruningView.From)),
        };

    private static List<T>? Rows<T>(
        HashSet<string> includeSections,
        string section,
        IEnumerable<T> rows) =>
        includeSections.Contains(section) ? [.. rows] : null;
}
