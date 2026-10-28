using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    static async Task<int?> TryExecuteOnlinePackageAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context,
        PackageHouseTargetContext? targetContext)
    {
        if (target.IsLocalFile
            || options.DeclaredPackageTarget is { IsLocalFile: true }
            || DotnetInspector.Networking.HttpClientFactory.IsOffline
            || options.ShowContent
            || options.AllLibraries
            || options.PackageLibrary is not null)
        {
            return null;
        }

        await using DesktopPackageSourceComposition composition =
            context.CreatePackageSourceComposition();
        using var stores = new SearchPackageStores("inspect-package");
        bool isPinned = PackageExtractor.TryNormalizePackageVersion(
            target.Version, out string pinnedVersion);
        PackageHouseTargetContext compileTarget =
            targetContext
            ?? (string.IsNullOrWhiteSpace(options.Tfm)
                || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? PackageHouseTargetContext.OwnerDefault()
                    : PackageHouseTargetContext.Exact(options.Tfm));
        bool childrenOnly = IsPackageChildrenProjection(options);
        ConfiguredPackagePayloadResult acquisition = childrenOnly
            ? await AcquirePackageChildrenInventoryAsync(
                target, options, context, composition, stores, compileTarget)
                .ConfigureAwait(false)
            : isPinned
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
            return 1;
        }

        IEnumerable<string> entryPaths =
            settlement.Result.Evidence.FileList is { } inventory
                ? inventory.Entries.Select(static entry => entry.Path)
                : settlement.Payload.Content.EnumerateEntries();
        if (MayRequireLegacyToolWrapperHandling(entryPaths)
            || childrenOnly && entryPaths.Any(
                RequiresLegacyPackageInfoToolContent))
        {
            context.Logger.Log(
                "Tool-wrapper inspection retains the legacy redirect path.");
            return null;
        }

        InspectionResult? childrenResult = null;
        if (childrenOnly)
        {
            NuspecData? nuspec = ReadCompileInventoryNuspec(
                settlement.Payload.Content,
                ecosystemRecognitionRequested: false);
            if (nuspec?.IsToolPackage == true)
                return null;
            childrenResult = new InspectionResult
            {
                PackageName = nuspec?.PackageName
                    ?? settlement.Payload.Coordinate.PackageId,
                Version = nuspec?.Version
                    ?? settlement.Payload.Coordinate.Version,
                Source = SourceKind.NuGet,
            };
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
            return 1;
        }

        PackageRootBinding binding =
            contribution.Contribution.Binding.ForContentInspection();
        var workspace = new InspectionWorkspace(WorkspacePlan.Empty);
        return await WorkspacePacketRestorationLifetime.ExecuteAsync(
            workspace,
            async () =>
            {
                WorkspaceScopeReadResult read =
                    await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
                if (read is not WorkspaceScopeReadResult.Available available)
                {
                    CommandError.Write(
                        "The package Workspace is unavailable: " + read);
                    return 1;
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
                    return 1;
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
                            if (childrenResult is not null)
                            {
                                var subject = new PackageChildrenSubject(
                                    childrenResult.PackageName,
                                    childrenResult.Version,
                                    targetFramework: null,
                                    childrenResult.Source);
                                PackageChildrenPlan plan =
                                    PlanPackageCompileChildren(
                                        subject,
                                        binding.Root.AssetSelection
                                            ?? throw new InvalidOperationException(
                                                "The package Root has no compile selection."));
                                int exitCode =
                                    await WritePackageChildrenAsync(
                                        plan, childrenResult, options)
                                        .ConfigureAwait(false);
                                return new WorkspacePackageInspectionContent(
                                    childrenResult, exitCode);
                            }
                            return await ExecuteAdmittedPackageAsync(
                                options,
                                context,
                                binding,
                                contextTargetFramework: null,
                                acquisition: acquisition).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                if (query
                    is ArtifactRootResult<WorkspacePackageInspectionContent>
                        .Available inspected)
                {
                    return inspected.Value.ExitCode;
                }

                var rejected =
                    (ArtifactRootResult<WorkspacePackageInspectionContent>
                        .Rejected)query;
                CommandError.Write(
                    "The package Root query was rejected: " + rejected.Failure);
                return 1;
            }).ConfigureAwait(false);
    }

    static async Task<ConfiguredPackagePayloadResult>
        AcquirePackageChildrenInventoryAsync(
            PackageReferenceTarget target,
            InspectionOptions options,
            CommandContext context,
            DesktopPackageSourceComposition composition,
            SearchPackageStores stores,
            PackageHouseTargetContext targetContext)
    {
        PackageHouseDemand demand;
        if (target.Version.Length == 0)
        {
            PackageVersionSelectionRequest selection = options.IncludePrerelease
                ? new PackageVersionSelectionRequest.LatestPrerelease(
                    target.PackageName)
                : new PackageVersionSelectionRequest.LatestStable(
                    target.PackageName);
            demand = new PackageHouseDemand.Selecting(selection);
        }
        else if (!PackageHouseDemand.TryCreate(
                target.PackageName,
                target.Version,
                options.IncludePrerelease,
                out PackageHouseDemand? requestedDemand,
                out string? error))
        {
            throw new ArgumentException(error, nameof(target));
        }
        else
        {
            demand = requestedDemand;
        }

        var request = new PackageHouseRequest(
            demand,
            composition.CreateHouseOperation(
                PackageHouseOperationProfile.Realize),
            targetContext,
            PackageHouseAssetSelectionKind.Compile,
            contentQuery: PackageHouseContentQuery.PackageFileList());
        using PackageSourceOperationLease operation =
            composition.IssueSettlementOperation();
        PackageHouseSettlement settlement =
            await composition.CreateDependencyContentQueryHouse(
                    stores.GetStore,
                    options.SourceOptions,
                    context.Logger.Log)
                .ExecuteAsync(request, operation)
                .ConfigureAwait(false);
        return PackageHousePayloadResultAdapter.Create(settlement);
    }
}
