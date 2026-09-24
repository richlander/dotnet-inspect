using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    static async Task<int> ExecutePackageAsync(
        LibraryOptions options,
        PackageReferenceTarget? declaredPackageTarget,
        WorkspaceContextLoadOptions? workspaceLoadOptions)
    {
        if (string.IsNullOrWhiteSpace(options.PackagePath))
        {
            CommandError.Write(
                "--workspace on library requires --package to identify one "
                    + "Package in the selected Workspace context.");
            return 1;
        }
        if (options.WorkspacePacket is not null
            && (options.PlatformAssembly is not null
                || options.PlatformFramework is not null
                || options.PlatformVersion is not null
                || options.Tfm is not null
                || options.IncludePrerelease))
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
        PackageLibraryTarget selection =
            options.NamesakeLibrary
                ? new PackageLibraryTarget.Namesake()
                : string.IsNullOrWhiteSpace(options.AssemblyName)
                    ? new PackageLibraryTarget.Aggregate()
                    : new PackageLibraryTarget.Exact(
                        options.AssemblyName);
        if (options.TypeNamespace is not null
            && selection is PackageLibraryTarget.Aggregate)
        {
            CommandError.Write(
                "library --namespace requires one exact Library. Name the "
                    + "assembly within the package.");
            return 1;
        }

        InspectionOptions packageOptions =
            CreatePackageOptions(
                options,
                selection,
                declaredPackageTarget);
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

    static InspectionOptions CreatePackageOptions(
        LibraryOptions options,
        PackageLibraryTarget selection,
        PackageReferenceTarget? declaredPackageTarget) =>
        new()
        {
            PackageArgs = [options.PackagePath!],
            DeclaredPackageTarget =
                options.WorkspacePacket is null
                    ? declaredPackageTarget
                    : null,
            WorkspacePacket = options.WorkspacePacket,
            WorkspaceLibrarySelection =
                options.WorkspacePacket is null
                    ? null
                    : selection switch
                    {
                        PackageLibraryTarget.Aggregate =>
                            new WorkspaceLibrarySelection.Aggregate(),
                        PackageLibraryTarget.Namesake =>
                            new WorkspaceLibrarySelection.Namesake(),
                        PackageLibraryTarget.Exact exact =>
                            new WorkspaceLibrarySelection.Exact(
                                exact.Library),
                        _ => null,
                    },
            AllLibraries =
                selection is PackageLibraryTarget.Aggregate,
            NamesakeLibrary =
                selection is PackageLibraryTarget.Namesake,
            TypeNamespace = options.TypeNamespace,
            IncludeNamespaceChildren =
                options.IncludeNamespaceChildren,
            PackageLibrary = selection switch
            {
                PackageLibraryTarget.Namesake => "",
                PackageLibraryTarget.Exact exact => exact.Library,
                _ => null,
            },
            Tfm = options.Tfm,
            IncludePrerelease = options.IncludePrerelease,
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

    private abstract record PackageLibraryTarget
    {
        private PackageLibraryTarget()
        {
        }

        internal sealed record Aggregate : PackageLibraryTarget;

        internal sealed record Namesake : PackageLibraryTarget;

        internal sealed record Exact(string Library) : PackageLibraryTarget;
    }
}
