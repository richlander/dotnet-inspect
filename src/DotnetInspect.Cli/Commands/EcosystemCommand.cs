using System.Collections.Immutable;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>Inspects the inert ecosystem-pack catalog configured into this build.</summary>
public static class EcosystemCommand
{
    public const string Name = "ecosystem";

    internal const string EcosystemsSection = "Ecosystems";
    internal const string InfoSection = "Ecosystem Info";
    internal const string NamespaceHintsSection = "Namespace Hints";
    internal const string CorePackagesSection = "Core Packages";
    internal const string ToolPackagesSection = "Tool Packages";
    internal const string KnownIntegrationsSection = "Known Integrations";
    internal const string DemosSection = "Demos";
    internal const string PruningSection = "Pruning";

    private const string CatalogDescription =
        "Product-configured ecosystem knowledge. This catalog is not an exhaustive description of the external ecosystems.";
    private const string ConfiguredKnowledgeScope =
        "Configured product knowledge; not a library observation.";
    private const string UnboundKnowledgeScope =
        "No Integration concepts are explicitly bound to this ecosystem in the current product build. This does not mean the external ecosystem has no integrations.";
    private static readonly IReadOnlyDictionary<string, string[]> NoCategories =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The base shared framework installed on this machine, which is the target the
    /// <c>Pruning</c> section reports.
    /// </summary>
    private static readonly Func<InstalledPlatformPruneSource.Result> DefaultPruneSource =
        static () => InstalledPlatformPruneSource.Read("runtime");

    public static int Execute(EcosystemOptions options) =>
        Execute(options, DefaultPruneSource);

    /// <summary>Runs the command against an explicit platform prune source.</summary>
    /// <remarks>
    /// The source is a factory rather than a value so an unselected <c>Pruning</c> section reads
    /// nothing. Tests supply their own to observe that, and to reach the read-failure path
    /// without an unreadable machine.
    /// </remarks>
    internal static int Execute(
        EcosystemOptions options,
        Func<InstalledPlatformPruneSource.Result> pruneSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pruneSource);
        ImmutableArray<EcosystemPackDescriptor> packs =
            EcosystemPackCatalog.Discover();
        if (!TryResolveFocus(
                options.Ecosystem,
                packs,
                out EcosystemPackDescriptor? focus))
        {
            return 1;
        }

        EcosystemSection[] sections = CreateSections(packs, focus, pruneSource);
        DocumentSchema schema = CreateSchema(sections);
        string[]? projectedColumns = ResolveProjectedColumns(options);
        string[]? discover = NormalizeSectionAliases(options.Discover);
        string[]? select = NormalizeSectionAliases(options.Select);

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
                json: options.Format == OutputFormat.Json,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                markdown: options.Format == OutputFormat.Markdown,
                plainText: options.Format == OutputFormat.PlainText,
                rootLabel: focus?.Title ?? EcosystemsSection);
        }

        if (options.Tree)
        {
            CommandError.Write(
                "--tree is supported only with -D/--discover for ecosystem catalog schema.");
            return 1;
        }

        string defaultSection =
            focus is null ? EcosystemsSection : InfoSection;
        HashSet<string> selectedNames;
        bool defaultSelection = options.Select is null && !options.SelectDefault;
        if (options.SelectDefault)
        {
            selectedNames = new HashSet<string>(
                sections.Select(section => section.Name),
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            SelectResult selection = SelectResolver.ResolveSelectAsSections(
                select,
                [.. sections.Select(section => section.Name)],
                infoSections: [defaultSection],
                NoCategories,
                selectDefault: false);
            if (SelectOutput.WriteUnresolved(selection))
                return 1;

            selectedNames = selection.Sections
                ?? new HashSet<string>(
                    [defaultSection],
                    StringComparer.OrdinalIgnoreCase);
        }

        EcosystemSection[] selected =
        [
            .. sections.Where(section => selectedNames.Contains(section.Name)),
        ];
        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                selectedNames,
                fields: options.Fields,
                columns: options.Columns))
        {
            return 1;
        }
        var renderedNames = projectedColumns is { Length: > 0 }
            ? selected
                .Where(section =>
                    schema.ValidateProjection(section.Name, projectedColumns)
                        .Resolved.Length > 0)
                .Select(section => section.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : selectedNames;

        if (options.Count)
        {
            if (selected.Length == 1)
            {
                CountOutput.WriteCount(
                    RowWindow.Apply(options.Rows, selected[0].Rows).Count);
            }
            else
            {
                string[] ordered = [.. selected.Select(section => section.Name)];
                if (!CountOutput.ValidateMapFormat(options.Format, ordered))
                    return 1;

                var projection = new CountProjection();
                foreach (EcosystemSection section in selected)
                {
                    projection.SetRows(
                        section.Name,
                        renderedNames.Contains(section.Name)
                            ? RowWindow.Apply(options.Rows, section.Rows).Count
                            : 0);
                }
                CountOutput.Write(
                    projection,
                    ordered,
                    options.Format,
                    options.NoHeader);
            }
            return 0;
        }

        if (!ValidateStructuredEmptyProjection(
                selected,
                projectedColumns,
                options.Format))
        {
            return 1;
        }

        if (options.Format
                is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl
            && selected.Length != 1)
        {
            CommandError.Write(
                $"Selection matches {selected.Length} sections: "
                + $"{string.Join(", ", selected.Select(section => section.Name))}.");
            CommandError.WriteBlankLine();
            CommandError.WriteLine(
                "--table, --tsv, and --jsonl display one section at a time.");
            CommandError.WriteLine(
                "Use -S with a specific section name, or --markdown/--json for multi-section output.");
            return 1;
        }

        bool includeDocumentHeading =
            defaultSelection || options.SelectDefault || selected.Length > 1;
        string title = focus?.Title ?? "Ecosystem Catalog";
        string description = focus?.Summary ?? CatalogDescription;
        bool structuredEmptyRows = options.Format
            is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl
                or OutputFormat.Json;
        EcosystemSection[] renderSelected =
        [
            .. selected.Where(section => renderedNames.Contains(section.Name)),
        ];
        EcosystemSection[] rendered = PrepareRenderSections(
            renderSelected,
            options.Rows,
            structuredEmptyRows);

        if (options.Format == OutputFormat.Json)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                    WriteDocument(
                        new MarkoutWriter(writer, formatter, writerOptions),
                        title,
                        description,
                        rendered,
                        includeDocumentHeading,
                        renderEmptyTables: true),
                maxRows: null);
            return 0;
        }

        if (options.Format
                is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl)
        {
            EcosystemSection section = rendered[0];
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                showHeader: !options.NoHeader,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    var markout =
                        new MarkoutWriter(writer, formatter, writerOptions);
                    WriteTable(markout, section);
                    markout.Flush();
                },
                maxRows: null);
            return 0;
        }

        var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
            projectedColumns,
            fields: null,
            null);
        var document = new MarkoutWriter(
            Console.Out,
            options.Format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(),
            writerOptions);
        WriteDocument(
            document,
            title,
            description,
            rendered,
            includeDocumentHeading);
        document.Flush();
        return 0;
    }

    private static bool TryResolveFocus(
        string? value,
        ImmutableArray<EcosystemPackDescriptor> packs,
        out EcosystemPackDescriptor? focus)
    {
        focus = null;
        if (value is null
            || value.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        focus = packs.FirstOrDefault(pack =>
            pack.Id.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (focus is null)
        {
            focus = packs.FirstOrDefault(pack =>
                pack.Id.Value.AsSpan("ecosystem.".Length)
                    .Equals(value.AsSpan(), StringComparison.OrdinalIgnoreCase));
        }
        if (focus is not null)
            return true;

        CommandError.Write($"Unknown ecosystem '{value}'.");
        CommandError.WriteBlankLine();
        CommandError.WriteLine("Available ecosystems:");
        foreach (EcosystemPackDescriptor pack in packs)
        {
            CommandError.WriteLine(
                $"  {pack.Id.Value["ecosystem.".Length..]} ({pack.Id})");
        }
        return false;
    }

    private static EcosystemSection[] CreateSections(
        ImmutableArray<EcosystemPackDescriptor> packs,
        EcosystemPackDescriptor? focus,
        Func<InstalledPlatformPruneSource.Result> pruneSource)
    {
        ImmutableArray<EcosystemPackDescriptor> scope =
            focus is null ? packs : [focus];
        var sections = new List<EcosystemSection>();
        if (focus is null)
            sections.Add(CreateCatalogSection(packs));
        else
            sections.Add(CreateInfoSection(focus));

        sections.Add(CreateNamespaceSection(scope, focus is null));
        sections.Add(CreatePackageSection(
            CorePackagesSection,
            "Product-authored unversioned starting points in pack-local preference order.",
            "No core-package starting points are configured for this ecosystem.",
            scope,
            focus is null,
            static pack => pack.CorePackages));
        sections.Add(CreatePackageSection(
            ToolPackagesSection,
            "Explicit .NET tool references; these are discovery metadata, not installation requests.",
            "No tool-package references are configured for this ecosystem.",
            scope,
            focus is null,
            static pack => pack.ToolPackages));
        sections.Add(CreateKnownIntegrationsSection(scope, focus is null));
        sections.Add(CreateDemosSection(scope, focus is null));

        // Only the platform ecosystem can answer which package identities a target subsumes, so
        // the section exists only when it is the focus. That also keeps its cost off every other
        // route: it is the one section here backed by an installed reference pack rather than a
        // compiled-in descriptor, and its rows are produced on demand.
        if (focus is not null && focus.Id == EcosystemPackIds.Platform)
            sections.Add(CreatePruningSection(pruneSource));

        return [.. sections];
    }

    /// <summary>
    /// The package identities the installed platform target subsumes.
    /// </summary>
    /// <remarks>
    /// Read from the reference pack installed on this machine, so the answer is exact for the
    /// pack actually present and needs no acquisition. <c>Kind</c> separates the two populations:
    /// a frozen entry is subsumed for any plausible request, while a live entry tracks the pack
    /// and turns on the version comparison.
    /// </remarks>
    private static EcosystemSection CreatePruningSection(
        Func<InstalledPlatformPruneSource.Result> pruneSource) =>
        new(
            PruningSection,
            "Package identities the installed platform target supplies, so a reference to one resolves to the platform rather than the package.",
            ["Package", "Supplied By", "Supplied", "Kind"],
            ["package", "supplied_by", "supplied", "kind"],
            new Lazy<string[][]>(() => BuildPruningRows(pruneSource)),
            // Says what was read, not what the platform contains. A pack that is absent, that
            // publishes no prune data, and that could not be read all produce no rows, and none
            // of them licenses the claim that the target subsumes nothing.
            "No platform prune inventory was read for this target.");

    private static string[][] BuildPruningRows(
        Func<InstalledPlatformPruneSource.Result> pruneSource)
    {
        InstalledPlatformPruneSource.Result result = pruneSource();
        if (result.Inventory is not { } inventory)
        {
            // A missing or unreadable pack is not an empty platform. Report it and render no
            // rows rather than asserting that nothing is subsumed.
            if (result.Error is { Length: > 0 } error)
                CommandError.Write(error);
            return [];
        }

        return
        [
            .. inventory.Entries.Select(entry => new[]
            {
                entry.PackageId,
                entry.Family,
                entry.SuppliedVersion.ToNormalizedString(),
                // A supplied version that is the pack's own version moves with the framework;
                // a lower one is pinned to a release the framework has passed. Precision plays
                // no part: this source reads the selected pack, so every entry is exact.
                entry.SuppliedVersion == entry.SourcePackVersion ? "live" : "frozen",
            }),
        ];
    }

    private static EcosystemSection CreateCatalogSection(
        ImmutableArray<EcosystemPackDescriptor> packs) =>
        new(
            EcosystemsSection,
            "Ecosystem packs configured into this product build.",
            ["ID", "Ecosystem", "Summary", "Scanner", "Integration Bindings", "Demos"],
            ["id", "ecosystem", "summary", "scanner", "integration_bindings", "demos"],
            [
                .. packs.Select(pack => new[]
                {
                    pack.Id.Value,
                    pack.Title,
                    pack.Summary,
                    pack.HasScanner ? "configured" : "none",
                    KnownConcepts(pack.Id).Length.ToString(),
                    pack.Demos.Length.ToString(),
                }),
            ],
            "No ecosystem packs are configured in this product build.");

    private static EcosystemSection CreateInfoSection(
        EcosystemPackDescriptor pack) =>
        new(
            InfoSection,
            "Identity and configured capability counts for this product ecosystem pack.",
            ["Field", "Value"],
            ["field", "value"],
            [
                ["ID", pack.Id.Value],
                ["Package Set", pack.PackageSet?.ToString() ?? "none"],
                ["Integration Scanner", pack.HasScanner ? "configured" : "none"],
                ["Namespace Hints", pack.NamespaceRoots.Length.ToString()],
                ["Core Packages", pack.CorePackages.Length.ToString()],
                ["Tool Packages", pack.ToolPackages.Length.ToString()],
                ["Known Integration Bindings", KnownConcepts(pack.Id).Length.ToString()],
                ["Demos", pack.Demos.Length.ToString()],
            ],
            "No ecosystem information is configured.");

    private static EcosystemSection CreateNamespaceSection(
        ImmutableArray<EcosystemPackDescriptor> packs,
        bool includeEcosystem)
    {
        string[][] rows =
        [
            .. packs.SelectMany(pack =>
                pack.NamespaceRoots.Select(root =>
                    includeEcosystem
                        ? new[] { pack.Title, root }
                        : [root])),
        ];
        return new EcosystemSection(
            NamespaceHintsSection,
            "Literal namespace-subtree hints, not an exhaustive namespace inventory.",
            includeEcosystem
                ? ["Ecosystem", "Namespace"]
                : ["Namespace"],
            includeEcosystem
                ? ["ecosystem", "namespace"]
                : ["namespace"],
            rows,
            "No namespace hints are configured for this ecosystem.");
    }

    private static EcosystemSection CreatePackageSection(
        string name,
        string summary,
        string emptyText,
        ImmutableArray<EcosystemPackDescriptor> packs,
        bool includeEcosystem,
        Func<EcosystemPackDescriptor, ImmutableArray<DotnetInspector.Packages.PackageCoordinate>>
            select)
    {
        string[][] rows =
        [
            .. packs.SelectMany(pack =>
                select(pack).Select(package =>
                    includeEcosystem
                        ? new[] { pack.Title, package.PackageId }
                        : [package.PackageId])),
        ];
        return new EcosystemSection(
            name,
            summary,
            includeEcosystem
                ? ["Ecosystem", "Package"]
                : ["Package"],
            includeEcosystem
                ? ["ecosystem", "package"]
                : ["package"],
            rows,
            emptyText);
    }

    private static EcosystemSection CreateKnownIntegrationsSection(
        ImmutableArray<EcosystemPackDescriptor> packs,
        bool includeEcosystem)
    {
        var rows = new List<string[]>();
        string[]? structuredEmptyRow = null;
        foreach (EcosystemPackDescriptor pack in packs)
        {
            ImmutableArray<IntegrationConceptDescriptor> concepts =
                KnownConcepts(pack.Id);
            if (concepts.Length == 0)
            {
                if (!includeEcosystem)
                {
                    structuredEmptyRow =
                    [
                        "(none configured)",
                        "",
                        "",
                        "not configured",
                        UnboundKnowledgeScope,
                    ];
                }
                continue;
            }

            foreach (IntegrationConceptDescriptor concept in concepts)
            {
                string relationships = string.Join(
                        ", ",
                        concept.ProducerPolicies
                            .Select(policy => policy.RelationshipId)
                            .Distinct(StringComparer.Ordinal));
                rows.Add(
                    includeEcosystem
                        ?
                        [
                            pack.Title,
                            concept.DisplayLabel,
                            concept.Id.Value,
                            relationships,
                            "configured",
                            ConfiguredKnowledgeScope,
                        ]
                        :
                        [
                            concept.DisplayLabel,
                            concept.Id.Value,
                            relationships,
                            "configured",
                            ConfiguredKnowledgeScope,
                        ]);
            }
        }

        return new EcosystemSection(
            KnownIntegrationsSection,
            "Integration concepts explicitly bound to this ecosystem by the current product build; these are not observations from a library.",
            includeEcosystem
                ?
                [
                    "Ecosystem",
                    "Integration",
                    "ID",
                    "Evidence Relationships",
                    "Binding",
                    "Knowledge Scope",
                ]
                :
                [
                    "Integration",
                    "ID",
                    "Evidence Relationships",
                    "Binding",
                    "Knowledge Scope",
                ],
            includeEcosystem
                ?
                [
                    "ecosystem",
                    "integration",
                    "id",
                    "evidence_relationships",
                    "binding",
                    "knowledge_scope",
                ]
                :
                [
                    "integration",
                    "id",
                    "evidence_relationships",
                    "binding",
                    "knowledge_scope",
                ],
            [.. rows],
            UnboundKnowledgeScope,
            structuredEmptyRow);
    }

    private static EcosystemSection CreateDemosSection(
        ImmutableArray<EcosystemPackDescriptor> packs,
        bool includeEcosystem)
    {
        string[][] rows =
        [
            .. packs.SelectMany(pack =>
                pack.Demos.Select(demo =>
                    includeEcosystem
                        ? new[]
                        {
                            pack.Title,
                            demo.ScenarioId,
                            demo.Title,
                            demo.Summary,
                        }
                        :
                        [
                            demo.ScenarioId,
                            demo.Title,
                            demo.Summary,
                        ])),
        ];
        return new EcosystemSection(
            DemosSection,
            "Product-resident inspection scenarios that exercise ordinary shipping sections.",
            includeEcosystem
                ? ["Ecosystem", "ID", "Title", "Summary"]
                : ["ID", "Title", "Summary"],
            includeEcosystem
                ? ["ecosystem", "id", "title", "summary"]
                : ["id", "title", "summary"],
            rows,
            "No product demos are configured for this ecosystem.");
    }

    private static ImmutableArray<IntegrationConceptDescriptor> KnownConcepts(
        EcosystemPackId ecosystem) =>
        [
            .. LibraryIntegrationCatalog.All
                .Where(descriptor => descriptor.Ecosystem == ecosystem)
                .Select(descriptor => descriptor.Concept),
        ];

    private static DocumentSchema CreateSchema(
        IEnumerable<EcosystemSection> sections)
    {
        var schema = new DocumentSchema();
        foreach (EcosystemSection section in sections)
            schema.Add(section.Name, "column", section.Labels);
        return schema;
    }

    private static string[]? NormalizeSectionAliases(string[]? values)
    {
        if (values is null)
            return null;

        return
        [
            .. values.Select(value =>
                value.Equals("Integrations", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("@Integrations", StringComparison.OrdinalIgnoreCase)
                    ? KnownIntegrationsSection
                    : value),
        ];
    }

    private static bool ValidateStructuredEmptyProjection(
        IEnumerable<EcosystemSection> sections,
        string[]? projectedColumns,
        OutputFormat format)
    {
        if (projectedColumns is not { Length: > 0 }
            || format is not (
                OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl
                or OutputFormat.Json))
        {
            return true;
        }

        foreach (EcosystemSection section in sections)
        {
            if (section.Rows.Length != 0
                || section.StructuredEmptyRow is null
                || !MarkoutProjection.WithColumns(projectedColumns)
                    .TryResolveColumns(section.Labels, out var resolution)
                || resolution.ColumnMap.Count == 0)
            {
                continue;
            }

            int binding = Array.IndexOf(section.Ids, "binding");
            int knowledgeScope =
                Array.IndexOf(section.Ids, "knowledge_scope");
            if (resolution.ColumnMap.Contains(binding)
                && resolution.ColumnMap.Contains(knowledgeScope))
            {
                continue;
            }

            CommandError.Write(
                $"Projection of an empty '{section.Name}' section must include "
                + "both 'Binding' and 'Knowledge Scope' so its status remains explicit.");
            return false;
        }

        return true;
    }

    private static string[]? ResolveProjectedColumns(EcosystemOptions options)
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

    private static EcosystemSection[] PrepareRenderSections(
        IEnumerable<EcosystemSection> sections,
        RowWindow? rows,
        bool structuredEmptyRows) =>
        [
            .. sections.Select(section =>
            {
                string[][] sectionRows = section.Rows;
                string[][] renderedRows =
                    [.. RowWindow.Apply(rows, sectionRows)];
                if (structuredEmptyRows
                    && sectionRows.Length == 0
                    && section.StructuredEmptyRow is { } emptyRow)
                {
                    renderedRows = [emptyRow];
                }

                return section with
                {
                    RowSource = new Lazy<string[][]>(renderedRows),
                    WasLogicallyEmpty = sectionRows.Length == 0,
                };
            }),
        ];

    private static void WriteDocument(
        MarkoutWriter writer,
        string title,
        string description,
        IEnumerable<EcosystemSection> sections,
        bool includeDocumentHeading,
        bool renderEmptyTables = false)
    {
        if (includeDocumentHeading)
        {
            writer.WriteHeading(1, title);
            writer.WriteParagraph(description);
        }

        bool first = true;
        foreach (EcosystemSection section in sections)
        {
            if (!first || includeDocumentHeading)
                writer.WriteBlankLine();
            first = false;
            writer.WriteHeading(2, section.Name);
            writer.WriteParagraph(section.Summary);
            if (section.Rows.Length == 0
                && section.WasLogicallyEmpty
                && !renderEmptyTables)
                writer.WriteParagraph(section.EmptyText);
            else
                WriteTable(writer, section);
        }
    }

    private static void WriteTable(
        MarkoutWriter writer,
        EcosystemSection section) =>
        writer.WriteTable(section.Labels, section.Ids, section.Rows);

    /// <summary>One rendered ecosystem section.</summary>
    /// <remarks>
    /// Rows are produced on demand rather than at construction. Every section this command
    /// rendered originally read compiled-in descriptors, so materializing the whole set cost
    /// nothing; a section backed by an installed reference pack does not have that property.
    /// Deferring production keeps an unselected section free, which is what lets the section
    /// ladder decide cost rather than the constructor.
    /// </remarks>
    private sealed record EcosystemSection(
        string Name,
        string Summary,
        string[] Labels,
        string[] Ids,
        Lazy<string[][]> RowSource,
        string EmptyText,
        string[]? StructuredEmptyRow = null,
        bool WasLogicallyEmpty = false)
    {
        /// <summary>Declares a section whose rows are already in hand.</summary>
        internal EcosystemSection(
            string name,
            string summary,
            string[] labels,
            string[] ids,
            string[][] rows,
            string emptyText,
            string[]? structuredEmptyRow = null,
            bool wasLogicallyEmpty = false)
            : this(
                name,
                summary,
                labels,
                ids,
                new Lazy<string[][]>(rows),
                emptyText,
                structuredEmptyRow,
                wasLogicallyEmpty)
        {
        }

        /// <summary>The section's rows, produced once on first access.</summary>
        public string[][] Rows => RowSource.Value;
    }
}
