using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;
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
        if (options.AllLibraries)
            return GetPackageAggregateLibraryModeError(options);
        if (options.PackageLibrary is not null)
            return GetPackageLibraryModeError(options);
        return null;
    }

    private static OptionError? GetPackageAggregateLibraryModeError(
        InspectionOptions options)
    {
        if (options.Discover is null
            && (options.Tree || options.ShowDependencies))
        {
            return new OptionError(
                "The selected Library operation requires one exact Library. "
                + "Narrow the package with --library <asset>.");
        }

        List<string> conflicts = [];
        if (options.PackageLibrary is not null)
            conflicts.Add("--library");
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
        if (options.Roots)
            conflicts.Add("--roots");
        if (options.ShowDependencies)
            conflicts.Add("--dependencies");

        if (conflicts.Count == 0)
            return null;

        return new OptionError(
            "--all-libraries cannot be combined with "
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
        if (options.AllLibraries)
            conflicts.Add("--all-libraries");
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
        if (options.Roots)
            conflicts.Add("--roots");
        if (options.ShowDependencies)
            conflicts.Add("--dependencies");
        if (string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add("--tfm all");
        }

        if (conflicts.Count == 0)
            return null;

        return new OptionError(
            "--library cannot be combined with "
            + $"{string.Join(", ", conflicts)}.");
    }

    private static async Task<int> ExecutePackageLibraryAsync(
        string extractPath,
        bool isLocalFile,
        string packageArg,
        string packageName,
        string version,
        InspectionOptions options)
    {
        PackageLibrarySelection? selected =
            ResolvePackageLibrary(
                extractPath,
                packageName,
                version,
                options);
        if (selected is null)
            return 1;

        string packageReference = isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;

        return await LibraryCommand.ExecuteAsync(
            CreateLibraryOptions(
                Path.GetRelativePath(
                        extractPath,
                        selected.Path)
                    .Replace('\\', '/'),
                packageReference,
                options));
    }

    private static Task<int> ExecutePackageAllLibrariesAsync(
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
                assemblyName: string.Empty,
                packageReference,
                options));
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
            IncludeMetadata = true,
            PackagePath = packageReference,
            IncludePrerelease = options.IncludePrerelease,
            Tfm = options.Tfm,
            TypeFilter = options.TypeFilter,
            PreferRenderedUrls = options.PreferRenderedUrls,
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
            Rows = options.CloneCandidateRowSelection is null
                ? options.Rows
                : null,
            CloneCandidateRowSelection =
                options.CloneCandidateRowSelection,
            SourceOptions = options.SourceOptions,
            NoHeader = options.NoHeader,
            UserVerbosityOverride = options.Verbosity,
        };

    private static PackageLibrarySelection? ResolvePackageLibrary(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        string? requestedLibrary = options.PackageLibrary;
        if (requestedLibrary is null)
            return null;
        string packageId =
            PackageExtractor.ParsePackageReference(packageName).name;
        var resolution = TfmSelector.SelectPackageLibrary(
            extractPath,
            packageId,
            requestedLibrary,
            options.Tfm);
        if (resolution.IsSelected)
            return new PackageLibrarySelection(resolution.Paths[0]);

        if (resolution.Status
            == TfmSelector.PackageLibraryResolutionStatus
                .RequestedLibraryNotFound)
        {
            CommandError.Write(
                $"Library '{requestedLibrary}' not found in package "
                + $"'{packageName}'.");
        }
        else if (resolution.Status
                 == TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
        {
            CommandError.Write(
                $"No DLLs found in package '{packageName}'.");
        }
        else if (resolution.Status
                 == TfmSelector.PackageLibraryResolutionStatus
                     .NoMatchingTargetFramework)
        {
            CommandError.Write(
                $"No library found for TFM '{options.Tfm}' in package "
                + $"'{packageName}'.");
        }
        else
        {
            CommandError.Write(
                resolution.Tfm is null
                    ? $"Package '{packageName}' contains multiple libraries."
                    : $"Package '{packageName}' contains multiple libraries "
                      + $"for {resolution.Tfm}.");
        }

        if (resolution.Status
            != TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
        {
            WritePackageLibraryCandidates(
                extractPath,
                packageName,
                version,
                resolution.Tfm ?? options.Tfm,
                resolution.CandidatePaths.ToList());
        }
        return null;
    }

    private sealed record PackageLibrarySelection(string Path);

    private static void WritePackageLibraryCandidates(
        string extractPath,
        string packageName,
        string version,
        string? tfm,
        List<string>? candidates = null)
    {
        candidates ??= string.IsNullOrWhiteSpace(tfm)
            ? TfmSelector.GetPackageAssemblies(extractPath)
            : TfmSelector.SelectHighestAssembliesFromPackage(
                extractPath,
                tfm).paths;

        if (candidates.Count > 0)
        {
            CommandError.WriteLine("Available libraries:");
            foreach (string candidate in candidates
                         .Select(path => Path.GetRelativePath(
                                 extractPath,
                                 path)
                             .Replace('\\', '/'))
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase))
            {
                CommandError.WriteLine($"  {candidate}");
            }
        }

        string packageReference =
            !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;
        CommandError.WriteBlankLine();
        CommandError.WriteLine("Use:");
        CommandError.WriteLine(
            $"  dotnet-inspect package {packageReference} --library <dll>");
    }
}
