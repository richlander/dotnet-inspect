using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private static bool IsNetworkUsingPackageSection(string section) =>
        section.Equals(
            PackageSections.Signals,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            PackageSections.AuditIdentifierConfusion,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            PackageSections.Statistics,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            PackageSections.Vulnerabilities,
            StringComparison.OrdinalIgnoreCase);

    internal static bool AllowsVulnerabilityTraffic(
        InspectionOptions options) =>
        options.Verbosity >= Verbosity.Detailed
        || options.IncludeSections?.Any(
            IsNetworkUsingPackageSection) == true;

    internal static OptionError? GetLibraryInspectionModeError(
        InspectionOptions options,
        bool allowStaticDiscovery = false)
    {
        if (options.AggregateLibraries)
            return GetPackageAggregateLibraryModeError(options);
        if (options.PackageLibrary is not null
            || options.NamesakeLibrary)
            return GetPackageLibraryModeError(options);
        return null;
    }

    private static OptionError? GetPackageAggregateLibraryModeError(
        InspectionOptions options)
    {
        if (options.Discover is null
            && options.ShowDependencies)
        {
            return new OptionError(
                "The selected Library operation requires one exact Library. "
                + "Narrow the package with --library <asset> "
                + "or --namesake-library.");
        }

        List<string> conflicts = [];
        if (options.ListLayout || options.ListLayoutExplicitlySet)
            conflicts.Add("--layout");
        if (HasPathFilter(options))
            conflicts.Add("--path");
        if (options.ListTfms)
            conflicts.Add("--tfms");
        if (options.ListVersions)
            conflicts.Add("--versions/--version/--latest-version");
        if (options.Print)
            conflicts.Add("--print");
        if (options.ShowDependencies)
            conflicts.Add("--dependencies");

        if (conflicts.Count == 0)
            return null;

        return new OptionError(
            "Aggregate Library inspection cannot be combined with "
            + $"{string.Join(", ", conflicts)}.");
    }

    private static OptionError? GetPackageLibraryModeError(
        InspectionOptions options)
    {
        if (options.Tree
            && !options.Count
            && options.Discover == null
            && (options.Format != OutputFormat.Markdown
                || options.Bare
                || options.Tabular
                || options.Tsv
                || options.Jsonl
                || options.JsonArray
                || options.NoHeader))
        {
            return new OptionError(
                "--tree cannot be combined with row projections "
                + "or non-Markdown formats.");
        }

        List<string> conflicts = [];
        if (options.ListLayout || options.ListLayoutExplicitlySet)
            conflicts.Add("--layout");
        if (HasPathFilter(options))
            conflicts.Add("--path");
        if (options.ListTfms)
            conflicts.Add("--tfms");
        if (options.ListVersions)
            conflicts.Add("--versions/--version");
        if (options.Print)
            conflicts.Add("--print");
        if (options.ShowDependencies)
            conflicts.Add("--dependencies");

        if (conflicts.Count == 0)
            return null;

        return new OptionError(
            "--library cannot be combined with "
            + $"{string.Join(", ", conflicts)}.");
    }

    private static Task<int> ExecutePackageLibraryAsync(
        bool isLocalFile,
        string packageArg,
        string? resolvedPackagePath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        string packageReference =
            resolvedPackagePath is not null
                && File.Exists(resolvedPackagePath)
            ? resolvedPackagePath
            : isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;
        return LibraryCommand.ExecuteAsync(
            CreateLibraryOptions(
                options.PackageLibrary,
                packageReference,
                options));
    }

    private static Task<int> ExecutePackageAggregateLibrariesAsync(
        bool isLocalFile,
        string packageArg,
        string? resolvedPackagePath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        string packageReference =
            resolvedPackagePath is not null
                && File.Exists(resolvedPackagePath)
            ? resolvedPackagePath
            : isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;
        return LibraryCommand.ExecuteAsync(
            CreateLibraryOptions(
                assemblyName: null,
                packageReference,
                options));
    }

    internal static bool RequestsAggregateLibraryInspection(
        string[]? select,
        string[]? discover)
    {
        var package =
            PackageSectionDescriptors.CreateCatalog().Sections;
        var library = LibrarySections.CreateCatalog().Sections;
        return (select ?? [])
            .Concat(discover ?? [])
            .Any(selector =>
            {
                SelectResult libraryResult =
                    SelectResolver.ResolveSelectAsSections(
                        [selector],
                        library.SelectableSectionNames,
                        library.InfoSectionNames,
                        library.SelectionCategoryMap);
                if (libraryResult.HasError
                    || libraryResult.Sections is not
                        { Count: > 0 })
                {
                    return false;
                }

                SelectResult packageResult =
                    SelectResolver.ResolveSelectAsSections(
                        [selector],
                        package.SelectableSectionNames,
                        package.InfoSectionNames,
                        package.SelectionCategoryMap);
                return packageResult.HasError
                    || packageResult.Sections is not
                        { Count: > 0 };
            });
    }

    internal static bool WriteIdentifierAuditFailures(
        IEnumerable<(
            string FileName,
            IdentifierConfusionAuditFailureKind FailureKind)>
            auditFailures)
    {
        ArgumentNullException.ThrowIfNull(auditFailures);

        var failures = auditFailures
            .Distinct()
            .ToList();

        foreach (var (fileName, failureKind) in failures)
        {
            CommandError.WriteWarning(
                $"Identifier audit failed for '{fileName}': "
                + IdentifierConfusionAudit.DescribeFailure(
                    failureKind));
        }

        return failures.Count > 0;
    }

    internal static bool RequiresPackageMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return RequiresIdentifierMetadata(
                   options,
                   pipeline,
                   includeSignals)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Statistics,
                   pipeline)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Vulnerabilities,
                   pipeline);
    }

    internal static bool RequiresIdentifierMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return (includeSignals
                && RequestsSelectedOrDiscoveredSection(
                    options,
                    PackageSections.Signals,
                    pipeline))
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.AuditIdentifierConfusion,
                   pipeline);
    }

    private static LibraryOptions CreateLibraryOptions(
        string? assemblyName,
        string packageReference,
        InspectionOptions options) =>
        new()
        {
            AssemblyName = assemblyName,
            NamesakeLibrary = options.NamesakeLibrary,
            IncludeMetadata = true,
            PackagePath = packageReference,
            IncludePrerelease = options.IncludePrerelease,
            Tfm = options.Tfm,
            TypeFilter = options.TypeFilter,
            BrowsableUrls = options.BrowsableUrls,
            JsonOutput = options.JsonOutput,
            PlainText =
                options.Format == OutputFormat.PlainText,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet =
                options.TabularExplicitlySet,
            FormatExplicitlySet =
                options.FormatExplicitlySet,
            Format = options.Format,
            Verbose = options.Verbose,
            Verbosity = options.Verbosity,
            IncludeSections = options.IncludeSections,
            Discover = options.Discover,
            Tree = options.Tree,
            Select = options.Select,
            SelectDefault = options.SelectDefault,
            SelectExplicitlySet = options.SelectExplicitlySet,
            Columns = options.Columns,
            Fields = options.Fields,
            FieldsExplicitlySet =
                options.FieldsExplicitlySet,
            Schema = options.Schema,
            Count = options.Count,
            OutputPath = options.OutputPath,
            Value = options.Value,
            Urls = options.Urls,
            Paths = options.Paths,
            JsonArray = options.JsonArray,
            ProjectionRow = options.PrintRow,
            Rows = options.Rows,
            SourceOptions = options.SourceOptions,
            NoHeader = options.NoHeader,
            UserVerbosityOverride = options.Verbosity,
        };
}
