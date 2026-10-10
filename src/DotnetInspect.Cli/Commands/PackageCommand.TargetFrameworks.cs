using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using NuGetFetch;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private static async ValueTask<int?> TryExecuteArchiveTargetFrameworksAsync(
        PackageReferenceTarget target, InspectionOptions options,
        CommandContext context, SectionPipeline<InspectionResult> pipeline)
    {
        if (target.IsLocalFile
            || DotnetInspector.Networking.HttpClientFactory.IsOffline
            || options.ForceLatest
            || options.WorkspacePacket is not null
            || options.Discover is not null
            || options.IncludeSections is not { Count: 1 }
            || !options.IncludeSections.Contains(PackageSections.TargetFrameworks)
            || (options.JsonOutput && !options.Count)
            || (!options.Tabular && !options.Count)
            || options.Fields is not null || options.Columns is not null
            || options.Value || options.Print || options.Raw
            || options.Paths || options.Roots || options.Urls)
            return null;

        await using DesktopPackageSourceComposition composition =
            context.CreatePackageSourceComposition();
        using var stores = new SearchPackageStores();
        var query = PackageHouseContentQuery.PackageFileList(includeDirectories: true);
        PackageHouseSettlement settlement =
            DotnetInspector.Packages.PackageExtractor.TryNormalizePackageVersion(target.Version, out string version)
                ? await composition.AcquireContentAsync(
                    PackageSourceCoordinate.Create(target.PackageName, version), query,
                    stores.GetStore, options.SourceOptions, context.Logger.Log, rangedSizeCut: 0).ConfigureAwait(false)
                : await composition.AcquireSelectedContentAsync(
                    target.PackageName, target.Version.Length > 0 ? target.Version : null,
                    query, stores.GetStore, options.SourceOptions, context.Logger.Log,
                    options.IncludePrerelease, rangedSizeCut: 0).ConfigureAwait(false);
        if (settlement is not PackageHouseSettlement.Acquired acquired)
        {
            string reason = settlement.Result switch
            {
                PackageHouseResult.NotFound value => value.Reason.ToString(),
                PackageHouseResult.NoMatch value => value.Reason.ToString(),
                PackageHouseResult.Ambiguous value => value.Reason.ToString(),
                PackageHouseResult.Rejected value => value.Reason.ToString(),
                PackageHouseResult.Unavailable value => value.Reason.ToString(),
                PackageHouseResult.Incomplete value => value.Reason.ToString(),
                PackageHouseResult.Failed value => value.Reason.ToString(),
                _ => "Package directory acquisition did not complete.",
            };
            CommandError.Write($"Could not acquire framework directory for {target.PackageName}.", reason);
            return 1;
        }
        var files = acquired.Result.Evidence.FileList
            ?? throw new InvalidOperationException("Directory acquisition requires File List evidence.");
        if (files.Directories is null
            || MayRequireLegacyToolWrapperHandling(files.Entries.Select(entry => entry.Path)))
            return null;
        var result = new InspectionResult
        {
            PackageName = acquired.Payload.Coordinate.PackageId,
            Version = acquired.Payload.Coordinate.Version,
            Source = SourceKind.NuGet,
            TargetFrameworks = TfmSelector.GetPackageFrameworkFolders(
                files.Directories),
        };
        if (!TrySelectPackageTargetFrameworks(result, options))
            return 1;
        if (options.Count)
            CountOutput.WriteCountResult(OutputFormatter.FormatResult(result, options, pipeline),
                options.OutputPath, options.Rows);
        else
            OutputDestination.Write(options.OutputPath, null,
                output => OutputFormatter.WritePackageTable(output, result, options, pipeline,
                    showHeader: !options.NoHeader));
        return 0;
    }
}
