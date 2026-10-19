using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    internal static async Task<int> ExecutePackageReferenceAsync(
        InspectionOptions options,
        CommandContext context,
        WorkspaceContextLoadOptions? loadOptions = null,
        PackageRootHouseExecution? rootHouse = null,
        CancellationToken cancellationToken = default)
    {
        if (options.PackageArgs.Length != 1
            || options.ExplicitVersion is not null
            || options.Tfm is not null
            || options.WorkspacePacket is not null
            || options.ListVersions
            || options.ListVersionsWithFeed
            || options.IncludePrerelease
            || options.IncludeUnlisted
            || options.ForceLatest
            || options.ShareFormat is not null)
        {
            CommandError.Write(
                "An exact Package reference cannot be combined with other Packages, "
                    + "--version, --tfm, --workspace, version-population or "
                    + "preview selection, or --share. The reference supplies the exact Package source.");
            return 1;
        }

        return await PackageReferenceAdmission.ExecuteAsync(
            options.PackageArgs[0], options.SourceOptions, options.Verbose,
            InspectAsync, loadOptions, rootHouse, cancellationToken)
            .ConfigureAwait(false);

        async Task<int> InspectAsync(PackageRootBinding root)
        {
            // This label feeds ordinary presentation/section validation only.
            // The binding and its admitted content supply the source; the core
            // must not acquire a Package from this label.
            InspectionOptions admittedOptions = options with
            {
                PackageArgs = [root.Root.PackageId],
            };
            WorkspacePackageInspectionContent result =
                await ExecuteAdmittedPackageAsync(
                    admittedOptions,
                    context,
                    root,
                    contextTargetFramework: null, exactReference: true).ConfigureAwait(false);
            if (result.ExitCode == 0
                && options.CompanionOutput != CompanionOutput.None
                && options.PackageLibrary is null
                && !options.AllLibraries)
            {
                TipWriter.WritePackageTips(
                    root.Root.PackageId,
                    options.CompanionOutput,
                    options.Verbosity);
            }
            return result.ExitCode;
        }
    }
}
