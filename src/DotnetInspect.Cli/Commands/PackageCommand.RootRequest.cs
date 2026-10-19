using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    internal static async Task<int> ExecuteRootRequestAsync(
        InspectionOptions options,
        CommandContext context,
        WorkspaceContextLoadOptions? loadOptions = null,
        PackageRootHouseExecution? rootHouse = null,
        CancellationToken cancellationToken = default)
    {
        if (options.PackageArgs.Length != 0
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
                "--root-request cannot be combined with positional Packages, "
                    + "--version, --tfm, --workspace, version-population or "
                    + "preview selection, or --share. The token supplies the exact Root source.");
            return 1;
        }

        loadOptions ??= CreateWorkspaceContextLoadOptions(options);
        if (rootHouse is not null)
            return await InspectAsync(rootHouse).ConfigureAwait(false);

        await using var executor = new ConfiguredPackageRootHouseExecutor(
            context.CreatePackageSourceComposition(),
            options.SourceOptions,
            loadOptions);
        return await InspectAsync(executor.Execution).ConfigureAwait(false);

        async Task<int> InspectAsync(PackageRootHouseExecution execution)
        {
            PackageRootBinding? root =
                await PackageRootRequestAdmission.AcquireAsync(
                    options.RootRequest!,
                    loadOptions,
                    execution,
                    cancellationToken).ConfigureAwait(false);
            if (root is null)
                return 1;

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
                    contextTargetFramework: null).ConfigureAwait(false);
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
