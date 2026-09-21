using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using InertText;
using Inspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{

    private static readonly string[] PackageSignalsColumnNames =
    [
        "Area",
        "Signal",
        "Value",
        "Evidence"
    ];

    internal static DocumentSchema PackageDiscoverySchema() =>
        AddPackageDynamicDiscoveryItems(
            WithDependencyHierarchySchema(
                InspectionContext.Default
                    .GetSchemaInfo<InspectionResultView>()!
                    .ToDocumentSchema()));

    internal sealed record AllLibrariesRowSchema(
        string Section,
        string[] Headers,
        string[] StableHeaders,
        string[]? AlternateHeaders = null,
        string[]? AlternateStableHeaders = null);

    internal static IReadOnlyList<AllLibrariesRowSchema>
        AllLibrariesRowSchemas { get; } =
        CreateAllLibrariesRowSchemas();

    internal static DocumentSchema PackageAllLibrariesDiscoverySchema()
    {
        var schema = new DocumentSchema();
        foreach (AllLibrariesRowSchema rowSchema in
                 AllLibrariesRowSchemas)
            schema.Add(
                rowSchema.Section,
                "column",
                rowSchema.Headers
                    .Concat(rowSchema.AlternateHeaders ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return schema;
    }

    private static IReadOnlyList<AllLibrariesRowSchema>
        CreateAllLibrariesRowSchemas()
    {
        var schemas = new List<AllLibrariesRowSchema>
        {
            new(
                SectionNames.LibraryInfo,
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Field",
                    "Value",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "field",
                    "value",
                ]),
            new(
                "Switches",
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Kind",
                    "Switch",
                    "API",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "kind",
                    "switch",
                    "api",
                ]),
            new(
                IntegrationSectionNames.Opportunities,
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Integration",
                    "API",
                    "Integration Type",
                    "Look For",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "integration",
                    "api",
                    "integration_type",
                    "look_for",
                ]),
            new(
                IntegrationSectionNames.Integrations,
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Integration",
                    "Kind",
                    "Shape",
                    "Symbol",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "integration",
                    "kind",
                    "shape",
                    "symbol",
                ]),
        };
        return schemas;
    }

    private static bool ValidatePackageProjection(
        InspectionOptions options,
        int packageCount,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.Fields is not { Length: > 0 }
            && options.Columns is not { Length: > 0 })
        {
            return true;
        }
        if (options.Discover != null)
            return true;
        if (options.IncludeSections is { Count: > 1 } projectedSections
            && projectedSections.Contains(
                PackageSections.DependencyHierarchy))
        {
            CommandError.Write(
                "--columns/--fields with Dependency Hierarchy requires that section to be selected alone.");
            return false;
        }

        DocumentSchema schema = PackageDiscoverySchema();
        if (packageCount > 1
            && options.Count)
        {
            IReadOnlyCollection<string> countSections =
                options.FixedOverview
                    ? pipeline.BareSelectSectionNames
                    : options.IncludeSections is { Count: > 0 } includeSections
                        ? includeSections
                        : [PackageSections.PackageInfo];
            return ValidatePackageCountProjection(
                schema,
                countSections,
                options,
                combinedRows: true);
        }

        bool multiPackageRowShape =
            packageCount > 1
            && options.Tabular
            && !SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections)
            && (options.FixedOverview
                || options.IncludeSections is not { Count: > 0 }
                || (options.IncludeSections.Count == 1
                    && (IsMultiPackageFieldSection(
                            options.IncludeSections.Single())
                        || IsPackageFileSection(
                            options.IncludeSections.Single()))));
        if (multiPackageRowShape)
        {
            string section =
                options.IncludeSections is { Count: 1 } includeSections
                    ? includeSections.Single()
                    : PackageSections.PackageInfo;
            return ValidatePackageCountProjection(
                schema,
                [section],
                options,
                combinedRows: true);
        }

        if (options.FixedOverview)
        {
            return ValidatePackageCountProjection(
                schema,
                pipeline.BareSelectSectionNames,
                options,
                combinedRows: false);
        }

        if (options.IncludeSections is not { Count: > 0 })
            return true;

        return ValidatePackageCountProjection(
            schema,
            options.IncludeSections,
            options,
            combinedRows: false);
    }

    private static bool ValidatePackageCountProjection(
        DocumentSchema schema,
        IReadOnlyCollection<string> sections,
        InspectionOptions options,
        bool combinedRows)
    {
        bool valid = true;
        if (options.Fields is { Length: > 0 })
        {
            valid &= ProjectionDiagnostics.ValidateProjection(
                schema,
                sections,
                options.Fields,
                columns: null);
        }

        if (options.Columns is { Length: > 0 })
        {
            valid &= ProjectionDiagnostics.ValidateProjection(
                PackageCountColumnSchema(schema, combinedRows),
                sections,
                fields: null,
                options.Columns);
        }

        return valid;
    }

    private static DocumentSchema PackageCountColumnSchema(
        DocumentSchema schema,
        bool combinedRows)
    {
        var result = new DocumentSchema();
        foreach (string name in schema.SectionNames)
        {
            var section = schema.GetSection(name);
            if (combinedRows && IsMultiPackageFieldSection(name))
            {
                result.Add(
                    name,
                    "column",
                    MultiPackageInfoColumnNames);
            }
            else if (combinedRows && IsPackageFileSection(name))
            {
                result.Add(
                    name,
                    "column",
                    MultiPackageFileColumnNames);
            }
            else if (section is { Items.Length: > 0 }
                && string.Equals(
                    section.ItemKind,
                    "column",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    name,
                    section.ItemKind,
                    section.Items.Select(item => item.Name).ToArray());
            }
            else if (section is { Items.Length: > 0 }
                && string.Equals(
                    section.ItemKind,
                    "field",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    name,
                    "column",
                    ["Field", "Value"]);
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

    private static string[]? ResolvePackageInfoFields(
        string[]? patterns)
        => patterns is not { Length: > 0 }
            ? null
            : ResolveProjectionNames(
                InspectionResultView.PackageInfoFieldNames,
                patterns);

    private static string[]? ResolvePackageFieldSectionFields(
        string section,
        string[]? patterns)
    {
        if (patterns is not { Length: > 0 })
            return null;

        return ResolveProjectionNames(
            GetMultiPackageFieldNames(section),
            patterns);
    }

    private static bool IsMultiPackageFieldSection(string? section)
        => section is not null
            && (section.Equals(
                    PackageSections.PackageInfo,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    PackageSections.Signature,
                    StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> GetMultiPackageFieldNames(
        string section)
        => section.Equals(
            PackageSections.PackageInfo,
            StringComparison.OrdinalIgnoreCase)
            ? InspectionResultView.PackageInfoFieldNames
            : SigningSection.FieldNames;

    private static string[] ResolveProjectionNames(
        IReadOnlyList<string> availableNames,
        IReadOnlyList<string> patterns)
    {
        const string ProbeSection = "probe";
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pattern in patterns)
        {
            foreach (string availableName in availableNames)
            {
                if (!seen.Contains(availableName)
                    && new DocumentSchema()
                        .Add(
                            ProbeSection,
                            "column",
                            [availableName])
                        .ValidateProjection(
                            ProbeSection,
                            [pattern])
                        .Resolved
                        .Length > 0)
                {
                    seen.Add(availableName);
                    resolved.Add(availableName);
                }
            }
        }

        return [.. resolved];
    }

    private static DocumentSchema AddPackageDynamicDiscoveryItems(DocumentSchema schema)
    {
        var result = new DocumentSchema();
        foreach (var name in schema.SectionNames)
        {
            var section = schema.GetSection(name);
            if (string.Equals(name, PackageSections.PackageInfo, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    name,
                    "field",
                    [.. InspectionResultView.PackageInfoFieldNames]);
            }
            else if (string.Equals(name, PackageSections.Signals, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(name, "column", PackageSignalsColumnNames);
            }
            else if (section is { Items.Length: > 0 })
            {
                result.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

    private static DocumentSchema WithDependencyHierarchySchema(
        DocumentSchema schema)
    {
        var hierarchy =
            DependsAssetSections.CreateSchema().GetSection(
                DependsAssetSections.DependencyHierarchy)
            ?? throw new InvalidOperationException(
                "The shared Depends hierarchy schema is unavailable.");
        var result = new DocumentSchema();
        foreach (string name in schema.SectionNames)
        {
            var section =
                name.Equals(
                    PackageSections.DependencyHierarchy,
                    StringComparison.OrdinalIgnoreCase)
                    ? hierarchy
                    : schema.GetSection(name);
            if (section is { Items.Length: > 0 })
            {
                result.Add(
                    name,
                    section.ItemKind,
                    section.Items.Select(static item => item.Name).ToArray());
            }
            else
            {
                result.AddSection(name);
            }
        }

        if (!result.SectionNames.Contains(
                PackageSections.DependencyHierarchy,
                StringComparer.OrdinalIgnoreCase))
        {
            result.Add(
                PackageSections.DependencyHierarchy,
                hierarchy.ItemKind,
                hierarchy.Items.Select(static item => item.Name).ToArray());
        }
        return result;
    }

    internal static bool DiscoverRequestsSection(
        string[]? discover,
        string sectionName,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (discover is null)
            return false;
        if (discover.Length == 0)
            return true;

        var categories = pipeline.GetCategoryMap();
        foreach (var value in discover)
        {
            if (categories.TryGetValue(value, out var categorySections)
                && categorySections.Contains(sectionName, StringComparer.OrdinalIgnoreCase))
                return true;

            var (matches, miss) = SelectResolver.ResolveSingle(value, pipeline.SelectableSectionNames, singleGlob: true);
            if (miss == null && matches.Contains(sectionName, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool RequestsSelectedOrDiscoveredSection(
        InspectionOptions options,
        string sectionName,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.IncludeSections is { } selectedSections)
        {
            return selectedSections.Contains(sectionName)
                && (options.Discover is null
                    || DiscoverRequestsSection(
                        options.Discover,
                        sectionName,
                        pipeline));
        }

        return DiscoverRequestsSection(
            options.Discover,
            sectionName,
            pipeline);
    }

    internal static InspectionOptions CreateProducerOptions(
        InspectionOptions options,
        Verbosity userVerbosity,
        SectionPipeline<InspectionResult> pipeline)
    {
        HashSet<string>? producerSections = options.IncludeSections;
        if (options.Discover is not null)
        {
            HashSet<string> candidates;
            if (options.Discover.Length == 0)
            {
                candidates = pipeline.GetCandidateSections(
                    options.Verbosity,
                    fixedOverview: options.FixedOverview);
                if (options.IncludeSections is { } selectedSections)
                    candidates.IntersectWith(selectedSections);
            }
            else
            {
                candidates = options.IncludeSections is { } selectedSections
                    ? selectedSections.ToHashSet(
                        StringComparer.OrdinalIgnoreCase)
                    : pipeline.SelectableSectionNames.ToHashSet(
                        StringComparer.OrdinalIgnoreCase);
            }

            producerSections = candidates
                .Where(section => DiscoverRequestsSection(
                    options.Discover,
                    section,
                    pipeline))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return options with
        {
            Verbosity = userVerbosity,
            IncludeSections = producerSections,
        };
    }

    internal static bool RequestsRidPackageAvailability(
        InspectionOptions options,
        bool isLocalFile,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (RequestsSelectedOrDiscoveredSection(
                options,
                PackageSections.Manifest,
                pipeline))
        {
            return true;
        }

        return isLocalFile
            && options.IncludeSections is null
            && options.Discover is null
            && pipeline.GetCandidateSections(
                    options.Verbosity,
                    fixedOverview: options.FixedOverview)
                .Contains(PackageSections.Manifest);
    }

    private static bool ValidateMultiPackageMode(InspectionOptions options)
    {
        List<string> conflicts = GetMultiPackageConflicts(options);
        if (conflicts.Count == 0)
            return true;

        CommandError.Write($"Multiple package inspection cannot be combined with {string.Join(", ", conflicts)}.");
        CommandError.WriteLine("Use id@version for per-package version pins.");
        return false;
    }

    internal static List<string> GetMultiPackageConflicts(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.ExplicitVersion != null) conflicts.Add("--version");
        if (options.ListVersions) conflicts.Add("--versions/--version");
        if (options.ListLayout) conflicts.Add("--layout");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.Print) conflicts.Add("--print");
        if (options.Value) conflicts.Add("--value");
        if (options.Urls) conflicts.Add("--urls");
        if (options.Paths) conflicts.Add("--paths");
        if (options.Roots) conflicts.Add("--roots");
        if (options.Tree && options.Discover == null && !options.Count) conflicts.Add("--tree");
        if (options.IncludeSections?.Contains(
                PackageSections.DependencyHierarchy) == true)
        {
            conflicts.Add("-S \"Dependency Hierarchy\"");
        }
        if (options.PackageLibrary != null) conflicts.Add("--library");
        if (options.AllLibraries) conflicts.Add("--library");
        if (options.Discover != null) conflicts.Add("-D/--discover");

        return conflicts;
    }

    private static bool ValidatePackageContentMode(InspectionOptions options)
    {
        bool scopedContent = options.ContentScope != PackageFileContentScope.Full;
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            CommandError.Write("--frontmatter/--yaml-header cannot be combined with --body.");
            return false;
        }

        if (options.Print && options.ShowContent)
        {
            CommandError.Write("--print cannot be combined with --content.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && !options.Value
            && !options.Urls
            && !options.Paths
            && !options.Roots)
        {
            CommandError.Write(
                "--row requires --print, --value, --urls, --paths, or --roots.");
            return false;
        }

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write("--rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return false;
        }

        if (options.ShowContent && !HasPathFilter(options))
        {
            CommandError.Write("--content requires at least one --path selector.");
            return false;
        }

        if (scopedContent && !options.Print && !options.ShowContent)
        {
            CommandError.Write("--frontmatter/--yaml-header and --body require --print or --content.");
            return false;
        }

        if (options.ShowContent && options.JsonOutput)
        {
            CommandError.Write("--content supports --format jsonl for structured output, not --format json.");
            return false;
        }

        if (options.ShowContent && options.Tabular && !options.Jsonl)
        {
            CommandError.Write("--content supports separator output or --format jsonl; it cannot be combined with --format table or --format tsv.");
            return false;
        }

        if (options.ShowContent)
        {
            List<string> conflicts = [];
            if (options.ListLayout) conflicts.Add("--layout");
            if (options.ListTfms) conflicts.Add("--tfms");
            if (options.ListVersions) conflicts.Add("--versions/--version");
            if (options.Roots) conflicts.Add("--roots");
            if (options.PackageLibrary != null) conflicts.Add("--library");
            if (options.AllLibraries) conflicts.Add("--library");
            if (options.Discover != null) conflicts.Add("-D/--discover");
            if (options.Columns != null) conflicts.Add("--columns");
            if (options.Fields != null) conflicts.Add("--fields");
            if (conflicts.Count > 0)
            {
                CommandError.Write($"--content cannot be combined with {string.Join(", ", conflicts)}.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateDependencyHierarchyProjection(
        InspectionOptions options)
    {
        bool dependencyHierarchyProjection =
            options.IncludeSections?.Contains(
                PackageSections.DependencyHierarchy)
                == true;
        if (options.DependencyQueryPlan?.MaximumDepth is not null
            && !dependencyHierarchyProjection)
        {
            CommandError.Write(
                "--depth requires the Dependency Hierarchy section.");
            return false;
        }

        if (options.Discover is not null || !options.Tree)
            return true;

        dependencyHierarchyProjection =
            options.IncludeSections is { Count: 1 }
            && options.IncludeSections.Contains(
                PackageSections.DependencyHierarchy);

        if (!dependencyHierarchyProjection)
        {
            CommandError.Write(
                options.IncludeSections is { Count: 1 }
                && options.IncludeSections.Contains(PackageSections.Dependencies)
                    ? "Dependencies is direct evidence and cannot be rendered as a hierarchy. Use '-S \"Dependency Hierarchy\" --tree'."
                    : "--tree requires exactly '-S \"Dependency Hierarchy\"'.");
            return false;
        }

        if (options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.Count
            || options.Bare
            || options.JsonOutput
            || options.Tabular
            || options.Tsv
            || options.Jsonl
            || options.JsonArray
            || options.NoHeader
            || options.TabularExplicitlySet)
        {
            CommandError.Write(
                "--tree cannot be combined with count, shape, tabular, JSON, or field/column projections.");
            return false;
        }

        return true;
    }

    private static DocumentSchema FilterDiscoverySchema(
        DocumentSchema schema,
        IReadOnlySet<string> selectedSections)
    {
        var result = new DocumentSchema();
        foreach (var name in schema.SectionNames)
        {
            if (!selectedSections.Contains(name))
                continue;

            var section = schema.GetSection(name);
            if (section is { Items.Length: > 0 })
                result.Add(name, section.ItemKind, section.Items.Select(item => item.Name).ToArray());
            else
                result.AddSection(name);
        }

        return result;
    }
}
