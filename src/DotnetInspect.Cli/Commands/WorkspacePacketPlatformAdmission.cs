using System.Diagnostics;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspacePacketPlatformAdmission
{
    internal static async ValueTask<CompleteRestorationPlatformAdmission?>
        AdmitAsync(
            InspectionWorkspace workspace,
            PlatformFamilyTarget target,
            PortableLibraryIdentity library,
            WorkspaceContextLoadOptions capabilities,
            CancellationToken cancellationToken)
    {
        Dictionary<ConfiguredPackageAuthority, IPackageSourceClient> clients =
            new(ReferenceEqualityComparer.Instance);
        IPackageSourceClient GetClient(
            ConfiguredPackageAuthority authority)
        {
            if (!clients.TryGetValue(authority, out IPackageSourceClient? client))
            {
                client = authority.LocalIdentity is { } local
                    ? PackageSourceClientFactory.Create(
                        local, authority.Association)
                    : PackageSourceClientFactory.Create(
                        authority.Source,
                        authority.Association,
                        new BorrowedHttpClientTransport(capabilities.HttpClient),
                        NuGetFetchOptions.FromRequestTimeout(
                            capabilities.HttpClient.Timeout));
                clients.Add(authority, client);
            }
            return client;
        }
        PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(GetClient);
        try
        {
            PackagePlatformHouseAdapter package =
                new(
                    new PackagePlatformSource(
                        capabilities.SourceAuthorization,
                        new PackagePayloadAcquisitionPlan(
                            (_, _) => capabilities.PackageStore,
                            capabilities.PayloadLimits,
                            capabilities.PackageTransferPolicy,
                            capabilities.Log,
                            access: PackagePayloadAccess.Ranged)),
                    "cli-native-platform-package");
            var identity = new AssemblyReferenceIdentity(
                library.Name,
                Version.Parse(library.Version),
                library.Culture,
                library.PublicKeyToken);
            var request = new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create("cli-native-platform"),
                new PlatformTargetDemand.Exact(target),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "cli-native-platform")),
                new PlatformHouseOperation.Realize(
                    new PlatformPopulationDemand.Library(
                        new PlatformLibraryDemand.Assembly(identity)),
                    PlatformViewDemand.Implementation),
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create("cli-native-platform"),
                    PlatformSourcePolicyGeneration.Create("cli-native-platform"),
                    [
                        new(
                            PlatformSourceFacet.Implementation,
                            PlatformSourceSelectionMode.Precedence,
                            [package.ImplementationRealization]),
                    ]),
                new PlatformHouseWorkBudget(
                    maxSourceOperations: 8,
                    maxTargetCandidates: 0,
                    maxAssemblies: 1,
                    maxXmlDocuments: 0,
                    maxPortablePdbs: 0,
                    maxSourceDocuments: 0,
                    maxBytes: capabilities.MaxRetainedImageBytes,
                    maxForwardingHops: 0,
                    maxDuration: TimeSpan.FromMinutes(2)),
                cancellationToken);
            long started = Stopwatch.GetTimestamp();
            PackagePlatformHouseResult<PackageImplementationRealization> realized =
                await package.RealizeImplementationAsync(
                    request,
                    WorkspaceContextLoader.RepresentativeRuntimeIdentifier,
                    sourceLease.IssueOperationLease(
                        cancellationToken,
                        request.Work.MaxDuration,
                        request.Work.MaxDuration))
                    .ConfigureAwait(false);
            if (realized is not PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded succeeded)
            {
                var failure = (PackagePlatformHouseResult<
                    PackageImplementationRealization>.NotSucceeded)realized;
                capabilities.Log?.Invoke(
                    "The exact Runtime Platform Library is unavailable: "
                    + $"{failure.Diagnostic.Kind}: "
                    + failure.Diagnostic.Summary);
                return null;
            }
            PackageImplementationRealization implementation = succeeded.Value;
            var consumed = new PlatformHouseConsumedWork(
                sourceOperations: implementation.Frameworks.Length,
                targetCandidates: 0,
                assemblies: implementation.Libraries.Length,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: implementation.Libraries.Sum(
                    static library => library.ContentLength),
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: Stopwatch.GetElapsedTime(started));
            PackagePlatformLibraryMaterializationResult outcome =
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationAsync(
                        request, succeeded, consumed).ConfigureAwait(false);
            if (outcome
                is not PackagePlatformLibraryMaterializationResult.Completed
                    completed)
            {
                capabilities.Log?.Invoke(
                    "The exact Runtime Platform Library could not be materialized: "
                    + outcome.Realization.Outcome.Receipt.SettlementKind);
                return null;
            }

            bool transferred = false;
            try
            {
                PlatformLibraryReference selected = completed.Library.Value;
                LibraryReference reference = selected.Library;
                ManagedMetadataIdentity.Assembly assembly =
                    reference.ApiAssembly.AssemblyIdentity!;
                string source = selected.Contributions[0].Capability.Name;
                WorkspaceRegistrationRevision revision =
                    ((WorkspaceRegistrationReadResult.Available)
                        workspace.GetRegistrationSnapshot()).Revision;
                WorkspaceLibraryAdmissionOutcome admission =
                    await workspace.AdmitLibraryBatchAsync(
                        revision,
                        completed.Artifacts,
                        [completed.Library.Owner]).ConfigureAwait(false);
                transferred = true;
                if (admission is not WorkspaceLibraryAdmissionOutcome.Accepted
                    accepted)
                    return null;

                var context = WorkspaceLibraryDeclarationContextAdmission.Admit(
                    workspace,
                    accepted.Receipt,
                    new WorkspaceDeclarationRequest.PlatformPopulation(target),
                    [
                        new WorkspaceLibraryDeclarationContextMember(
                            reference.SourceCoordinate!,
                            assembly.Identity,
                            new WorkspaceDeclarationOrigin.PlatformPopulation(
                                target,
                                WorkspacePlatformPopulationMemberRole.Focus,
                                source,
                                assembly.Identity.Name),
                            AssemblyResolutionProvenance.Platform(
                                "Microsoft.NETCore.App",
                                target.Version.Value,
                                source),
                            packageRequest: null),
                    ],
                    new LibraryTypeDeclarationInventoryInspectionBounds(
                        maximumAssemblyBytes: (int)Math.Min(
                            capabilities.MaxRetainedImageBytes, int.MaxValue),
                        maximumRetainedDeclarations: 500_000,
                        maximumMetadataRows: 2_000_000,
                        maximumRetainedTextCharacters: 40_000_000));
                return context is WorkspaceLibraryDeclarationContextAdmissionOutcome
                        .Admitted admitted
                    ? new CompleteRestorationPlatformAdmission(
                        accepted.Receipt, admitted.Context)
                    : null;
            }
            finally
            {
                if (!transferred)
                {
                    Task artifactRetirement =
                        completed.Artifacts.DisposeAsync().AsTask();
                    await completed.Library.Owner.DisposeAsync()
                        .ConfigureAwait(false);
                    await artifactRetirement.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                await sourceLease.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                foreach (IPackageSourceClient client in clients.Values)
                    client.Dispose();
            }
        }
    }

    private sealed class BorrowedHttpClientTransport(HttpClient client)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // HttpClient cannot send the source client's already-dispatched request.
            if (request.Content is not null)
                throw new NotSupportedException(
                    "Workspace source acquisition supports body-free requests.");

            var forwarded = new HttpRequestMessage(
                request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
            };
            foreach (KeyValuePair<string, IEnumerable<string>> header
                in request.Headers)
                forwarded.Headers.TryAddWithoutValidation(
                    header.Key, header.Value);
            return SendForwardedAsync(forwarded, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendForwardedAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using (request)
                return await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
        }
    }
}
