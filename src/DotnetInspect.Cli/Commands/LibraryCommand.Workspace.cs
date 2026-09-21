using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    static async Task<int> ExecuteWorkspacePackageAsync(
        LibraryOptions options,
        WorkspaceContextLoadOptions? workspaceLoadOptions)
    {
        if (string.IsNullOrWhiteSpace(options.PackagePath))
        {
            CommandError.Write(
                "--workspace on library requires --package to identify one "
                    + "Package in the selected Workspace context.");
            return 1;
        }
        if (options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.PlatformVersion is not null
            || options.Tfm is not null
            || options.IncludePrerelease)
        {
            CommandError.Write(
                "--workspace supplies the Package location and target; it "
                    + "cannot be combined with --platform, --framework, "
                    + "--version, --tfm, or --preview.");
            return 1;
        }
        if (options.NamesakeLibrary
            && !string.IsNullOrWhiteSpace(options.AssemblyName))
        {
            CommandError.Write(
                "--namesake-library cannot be combined with an exact "
                    + "Library source.");
            return 1;
        }
        WorkspaceLibrarySelection selection =
            options.NamesakeLibrary
                ? new WorkspaceLibrarySelection.Namesake()
                : string.IsNullOrWhiteSpace(options.AssemblyName)
                    ? new WorkspaceLibrarySelection.Aggregate()
                    : new WorkspaceLibrarySelection.Exact(
                        options.AssemblyName);
        if (selection is WorkspaceLibrarySelection.Aggregate
            && (options.Trace
                || options.MetadataRoot != MetadataRootKind.Cli
                || options.ExtractResources is not null
                || options.ReferenceHierarchyDepth is not null))
        {
            CommandError.Write(
                "--trace, --metadata-root, --extract-resources, and --depth "
                    + "require one exact Library. Narrow with an exact "
                    + "Library source or --namesake-library.");
            return 1;
        }

        InspectionOptions packageOptions =
            CreateWorkspacePackageOptions(options, selection);
        var context = new CommandContext(options.Verbose);
        return workspaceLoadOptions is null
            ? await PackageCommand.ExecuteAsync(
                packageOptions,
                context).ConfigureAwait(false)
            : await PackageCommand.ExecuteAsync(
                packageOptions,
                context,
                workspaceLoadOptions).ConfigureAwait(false);
    }

    static InspectionOptions CreateWorkspacePackageOptions(
        LibraryOptions options,
        WorkspaceLibrarySelection selection) =>
        new()
        {
            PackageArgs = [options.PackagePath!],
            WorkspacePacket = options.WorkspacePacket,
            WorkspaceLibrarySelection = selection,
            AllLibraries =
                selection is WorkspaceLibrarySelection.Aggregate,
            PackageLibrary = selection switch
            {
                WorkspaceLibrarySelection.Namesake => "",
                WorkspaceLibrarySelection.Exact exact => exact.Library,
                _ => null,
            },
            ShowDependencies = options.IncludeDependencies,
            ReferenceHierarchyDepth = options.ReferenceHierarchyDepth,
            TypeFilter = options.TypeFilter,
            PreferRenderedUrls = options.PreferRenderedUrls,
            IntegrationQuery = options.IntegrationQuery,
            MetadataRoot = options.MetadataRoot,
            JsonOutput = options.JsonOutput,
            Format = options.Format,
            Verbose = options.Verbose,
            Verbosity = options.Verbosity,
            IncludeSections = options.IncludeSections,
            Discover = options.Discover,
            DiscoverDetails = options.DiscoverDetails,
            Effective = options.Effective,
            Tree = options.Tree,
            Select = options.Select,
            SelectDefault = options.SelectDefault,
            SelectExplicitlySet = options.SelectExplicitlySet,
            Columns = options.Columns,
            Fields = options.Fields,
            FieldsExplicitlySet = options.FieldsExplicitlySet,
            Schema = options.Schema,
            Count = options.Count,
            Print = options.Print,
            PrintRow = options.ProjectionRow ?? options.PrintRow,
            Value = options.Value,
            Urls = options.Urls,
            Paths = options.Paths,
            JsonArray = options.JsonArray,
            Rows = options.CloneCandidateRowSelection is null
                ? options.Rows
                : null,
            CloneCandidateRowSelection =
                options.CloneCandidateRowSelection,
            ReferenceRowSelection = options.ReferenceRowSelection,
            EcosystemDependencyRowSelection =
                options.EcosystemDependencyRowSelection,
            PerformanceTriage = options.PerformanceTriage,
            BodyKindQuery = options.BodyKindQuery,
            CloneCandidateQuery = options.CloneCandidateQuery,
            Trace = options.Trace,
            ExtractResources = options.ExtractResources,
            SourceOptions = options.SourceOptions,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            NoHeader = options.NoHeader,
            OutputPath = options.OutputPath,
        };
}
