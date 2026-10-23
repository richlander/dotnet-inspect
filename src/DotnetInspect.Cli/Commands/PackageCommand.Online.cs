using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    static async Task<int?> TryExecuteOnlinePackageAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context,
        PackageHouseTargetContext? targetContext)
    {
        if (!CanUseOnlinePackageInspection(target, options))
            return null;

        await using DesktopPackageSourceComposition composition =
            context.CreatePackageSourceComposition();
        using var stores = new SearchPackageStores("inspect-package");
        var workspaces = new List<InspectionWorkspace>();
        return await WorkspacePacketRestorationLifetime.ExecuteAsync<int?>(
            workspaces,
            async () =>
            {
                WorkspacePackageInspectionContent? result =
                    await TryInspectOnlinePackageAsync(
                        target,
                        options,
                        context,
                        targetContext,
                        composition,
                        stores,
                        workspaces,
                        (binding, acquisition) =>
                            ExecuteAdmittedPackageAsync(
                                options,
                                context,
                                binding,
                                contextTargetFramework: null,
                                acquisition: acquisition))
                        .ConfigureAwait(false);
                return result?.ExitCode;
            }).ConfigureAwait(false);
    }

    static bool CanUseOnlinePackageInspection(
        PackageReferenceTarget target,
        InspectionOptions options) =>
        !target.IsLocalFile
        && options.DeclaredPackageTarget is not { IsLocalFile: true }
        && !DotnetInspector.Networking.HttpClientFactory.IsOffline
        && !options.ShowContent
        && !options.AllLibraries
        && options.PackageLibrary is null;

    static async Task<WorkspacePackageInspectionContent?>
        TryInspectOnlinePackageAsync(
            PackageReferenceTarget target,
            InspectionOptions options,
            CommandContext context,
            PackageHouseTargetContext? targetContext,
            DesktopPackageSourceComposition composition,
            SearchPackageStores stores,
            List<InspectionWorkspace> workspaces,
            Func<
                PackageRootBinding,
                ConfiguredPackagePayloadResult,
                Task<WorkspacePackageInspectionContent>> inspect)
    {
        if (!CanUseOnlinePackageInspection(target, options))
            return null;

        bool isPinned = PackageExtractor.TryNormalizePackageVersion(
            target.Version, out string pinnedVersion);
        PackageHouseTargetContext compileTarget =
            targetContext
            ?? (string.IsNullOrWhiteSpace(options.Tfm)
                || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? PackageHouseTargetContext.OwnerDefault()
                    : PackageHouseTargetContext.Exact(options.Tfm));
        ConfiguredPackagePayloadResult acquisition = isPinned
            ? await composition.AcquirePinnedAsync(
                target.PackageName,
                pinnedVersion,
                stores.GetStore,
                options.SourceOptions,
                context.Logger.Log,
                compileTargetContext: compileTarget).ConfigureAwait(false)
            : await composition.AcquireSelectedAsync(
                target.PackageName,
                target.Version.Length > 0 ? target.Version : null,
                stores.GetStore,
                options.SourceOptions,
                context.Logger.Log,
                options.IncludePrerelease,
                compileTargetContext: compileTarget).ConfigureAwait(false);
        if (acquisition.HouseSettlement
            is not PackageHouseSettlement.Acquired settlement)
        {
            WritePackageInfoAcquisitionFailure(
                target.PackageName,
                isPinned ? pinnedVersion : target.Version,
                acquisition,
                isPinned,
                context.Logger.Log);
            return new(null, 1);
        }

        bool hasSelectedLibrary =
            settlement.Result.Evidence.Realization
                is PackageHouseRealizationReceipt.Compile compile
            && compile.Selection.Assets.Any(static asset =>
                !asset.Path.EndsWith(
                    ".resources.dll",
                    StringComparison.OrdinalIgnoreCase));
        if (!hasSelectedLibrary
            && MayRequireLegacyToolWrapperHandling(
                settlement.Payload.Content.EnumerateEntries()))
        {
            context.Logger.Log(
                "Tool-wrapper inspection retains the legacy redirect path.");
            return null;
        }

        foreach (PackageAuthorityFailure failure in acquisition.Failures)
            context.Logger.Log($"{failure.Authority}: {failure.Message}");
        foreach (PackageHouseFailure.Stage failure
            in settlement.Result.Evidence.Failures
                .OfType<PackageHouseFailure.Stage>())
        {
            context.Logger.Log(failure.Reason.ToString());
        }

        PackageHouseRootContributionOutcome rootContribution =
            PackageHouseRootContributionAdapter.Create(settlement);
        if (rootContribution
            is not PackageHouseRootContributionOutcome.Contributed contribution)
        {
            var unavailable =
                (PackageHouseRootContributionOutcome.NoContribution)
                    rootContribution;
            CommandError.Write(
                "The acquired package could not contribute a Workspace Root: "
                + unavailable.Reason);
            return new(null, 1);
        }

        PackageRootBinding binding =
            contribution.Contribution.Binding.ForContentInspection();
        var workspace = new InspectionWorkspace(WorkspacePlan.Empty);
        workspaces.Add(workspace);
        WorkspaceScopeReadResult read =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (read is not WorkspaceScopeReadResult.Available available)
        {
            CommandError.Write(
                "The package Workspace is unavailable: " + read);
            return new(null, 1);
        }

        WorkspaceScopeOperationResult admission =
            await workspace.AddPackagesAsync(
                available.Snapshot.Revision,
                [binding],
                DateTimeOffset.UtcNow.AddMinutes(5))
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceScopeOperationResult.Committed committed)
        {
            CommandError.Write(
                "The package Root could not be admitted: " + admission);
            return new(null, 1);
        }

        WorkspacePackageOccurrenceDescriptor package =
            committed.Snapshot.Packages.Single();
        if (package.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || package.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            throw new InvalidOperationException(
                "The committed package Root did not publish query admission.");
        }

        context.Logger.Log(
            "Using committed content-only package Root for inspection: "
            + $"{binding.Root.PackageId}@{binding.Root.PackageVersion}.");
        ArtifactRootResult<WorkspacePackageInspectionContent> query =
            await workspace.ExecutePackageRootQueryAsync(
                correspondence,
                ready.Generation,
                async (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return await inspect(binding, acquisition)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        if (query
            is ArtifactRootResult<WorkspacePackageInspectionContent>
                .Available inspected)
        {
            return inspected.Value;
        }

        var rejected =
            (ArtifactRootResult<WorkspacePackageInspectionContent>
                .Rejected)query;
        CommandError.Write(
            "The package Root query was rejected: " + rejected.Failure);
        return new(null, 1);
    }
}
