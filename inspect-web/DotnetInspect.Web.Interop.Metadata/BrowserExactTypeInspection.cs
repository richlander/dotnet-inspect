using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
internal static class BrowserExactTypeInspection
{
    internal static Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteAsync(
            string packageId,
            string version,
            string targetFramework,
            string typeId,
            string assemblyName,
            AssemblyReferenceIdentity? library = null,
            string? compileAssetId = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Exact type inspection requires an exact pinned package version.",
                nameof(version));
        }

        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => ExecuteWithinDeadlineAsync(
                packageId,
                version,
                targetFramework,
                typeId,
                assemblyName,
                library,
                compileAssetId,
                deadline),
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);
    }

    static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteWithinDeadlineAsync(
            string packageId,
            string version,
            string targetFramework,
            string typeId,
            string assemblyName,
            AssemblyReferenceIdentity? library,
            string? compileAssetId,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        var context = new WorkspaceContextInput
        {
            Framework = targetFramework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    packageId,
                    version,
                    targetFramework),
            ],
        };
        var plan = new WorkspacePlan([], [context]);
        await using var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            await RequirePreparedAsync(
                host.BeginCandidateAsync(plan, deadline.Token))
                .ConfigureAwait(false);

        PackageRootBinding binding;
        PackageAssemblyContextRealization realized;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            WorkspacePackageRootAcquisitionOutcome acquisition =
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                        context,
                        LoadOptions(deadline),
                        deadline.Token)
                    .ConfigureAwait(false);
            if (acquisition is WorkspacePackageRootAcquisitionOutcome.Failed failed)
            {
                throw new InvalidOperationException(
                    "Browser exact type package acquisition failed: "
                    + string.Join("; ", failed.Failures.Select(failure => $"{failure.Kind}: {failure.Message}")));
            }
            binding = ((WorkspacePackageRootAcquisitionOutcome.Acquired)acquisition).Root;
            realized = construction.Workspace.RealizePackageAssemblyContextRoles(
                [binding.Root],
                new()
                {
                    MaxAggregateRetainedImageBytes = BrowserInspectionScope.MaxRetainedImageBytes,
                    MaxAssemblyEntryBytes = BrowserInspectionScope.MaxRetainedImageBytes,
                    RequireDeclaredEntryLengths = true,
                },
                deadline.Token);
        }
        using PackageAssemblyContextRealization realization = realized;

        WorkspaceRealizationCandidateCompletionResult completion =
            await host.CompleteCandidateAsync(
                    prepared.Candidate,
                    deadline.Token)
                .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            throw new InvalidOperationException(
                "Browser exact type Workspace construction was rejected "
                    + $"({rejected.Reason}).");
        }

        BrowserWorkspaceRealizationCutoverResult cutover =
            host.CutOver(prepared.Candidate);
        if (cutover is not BrowserWorkspaceRealizationCutoverResult.Activated)
        {
            var rejected =
                (BrowserWorkspaceRealizationCutoverResult.Rejected)cutover;
            throw new InvalidOperationException(
                "Browser exact type Workspace activation was rejected "
                    + $"({rejected.Reason}).");
        }

        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(deadline.Token)
                .ConfigureAwait(false);
        if (admission is not WorkspaceRealizationOperationAdmission.Admitted)
        {
            var unavailable =
                (WorkspaceRealizationOperationAdmission.Unavailable)admission;
            throw new InvalidOperationException(
                "Browser exact type Workspace operation was unavailable "
                    + $"({unavailable.Reason}).");
        }

        using WorkspaceRealizationOperationLease operation =
            ((WorkspaceRealizationOperationAdmission.Admitted)admission).Lease;
        string simpleName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? assemblyName[..^4]
                : assemblyName;
        return await ExactTypeInspection.ExecuteAsync(
            new ExactTypeInspectionRequest(
                ContextIndex: 0,
                TypeSelector: typeId,
                Scope: ApiSurfaceScope.IncludeAll,
                SurfaceLimits: BrowserApiSurfacePolicy.Limits,
                AssemblyName: library is null && compileAssetId is null ? simpleName : null,
                Library: library,
                CompileAssetId: compileAssetId),
            operation,
            binding,
            realization,
            deadline.Token).ConfigureAwait(false);
    }

    static async Task<
        BrowserWorkspaceRealizationCandidateStartResult.Prepared>
        RequirePreparedAsync(
            ValueTask<BrowserWorkspaceRealizationCandidateStartResult> start)
    {
        BrowserWorkspaceRealizationCandidateStartResult result =
            await start.ConfigureAwait(false);
        return result switch
        {
            BrowserWorkspaceRealizationCandidateStartResult.Prepared
                prepared => prepared,
            BrowserWorkspaceRealizationCandidateStartResult.CapacityUnavailable
                unavailable => throw new InvalidOperationException(
                    "Browser exact type Workspace capacity is unavailable "
                        + $"({unavailable.Capacity.Charged}/"
                        + $"{unavailable.Capacity.Limit} realizations charged)."),
            BrowserWorkspaceRealizationCandidateStartResult.Superseded =>
                throw new InvalidOperationException(
                    "Browser exact type Workspace construction was superseded."),
            BrowserWorkspaceRealizationCandidateStartResult.Closed =>
                throw new InvalidOperationException(
                    "Browser exact type Workspace host is closed."),
            _ => throw new InvalidOperationException(
                "Browser exact type Workspace construction returned an unknown outcome."),
        };
    }

    static WorkspaceContextLoadOptions LoadOptions(
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline) =>
        new()
        {
            HttpClient = BrowserPackageWorkspace.NetworkClient,
            SourceAuthorization =
                BrowserPackageWorkspace.PackageSourceAuthorization,
            PackageStore = BrowserPackageWorkspace.SessionPackageStore,
            PackageTransferPolicy =
                new BrowserPackageWorkspace
                    .BrowserPackageOperationTransferPolicy(
                        BrowserPackageWorkspace.PackageTransferPolicy,
                        deadline),
            PayloadLimits = BrowserPackageWorkspace.PackageLimits,
            MaxRetainedImageBytes =
                BrowserInspectionScope.MaxRetainedImageBytes,
            UseVersionCache = false,
            IncludePackageRootBindings = true,
        };
}
