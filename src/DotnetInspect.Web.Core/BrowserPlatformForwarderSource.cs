using System.Diagnostics;
using System.Runtime.Versioning;
using DotnetInspect.Web.Core;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed class BrowserPlatformForwarderOperationException(
    string status,
    string message,
    PlatformHouseReceipt? receipt = null,
    PlatformSourceContribution? contribution = null) : Exception(message)
{
    internal string Status { get; } = status;
    internal PlatformHouseReceipt? Receipt { get; } = receipt;
    internal PlatformSourceContribution? Contribution { get; } = contribution;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserPlatformForwarderSource
{
    readonly IPackageSourceClient _client;
    readonly PackagePlatformHouseAdapter _adapter;
    readonly TimeSpan _timeout;

    internal BrowserPlatformForwarderSource(
        IPackageSourceClient client,
        IPackageSourceAuthorization authorization,
        TimeSpan timeout)
    {
        _client = client;
        _timeout = timeout;
        _adapter = new(
            new PackagePlatformSource(
                authorization,
                new PackagePayloadAcquisitionPlan(
                    static (_, _) => BrowserPackageWorkspace.SessionPackageStore,
                    BrowserPackageWorkspace.PackageLimits,
                    BrowserPackageWorkspace.PackageTransferPolicy)),
            "browser-platform-forwarder");
        Sources = new(
            PlatformSourcePlanIdentity.Create("browser-platform-forwarder"),
            PlatformSourcePolicyGeneration.Create("browser-platform-forwarder"),
            [
                new(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [_adapter.ImplementationRealization]),
            ]);
    }

    internal PlatformSourcePlan Sources { get; }

    internal async ValueTask<PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<
            PlatformTypeDefinitionResolutionResult>>> ResolveAsync(
                BrowserPlatformForwarderResolutionRequest activation,
                CancellationToken cancellationToken)
    {
        var request = Request(
            activation.Target,
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation),
            activation.Sources,
            cancellationToken);
        long started = Stopwatch.GetTimestamp();
        PackagePlatformHouseResult<PackageImplementationRealization>.Succeeded source =
            await RealizeAsync(request, activation.RuntimeIdentifier);
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: source.Value.Frameworks.Length,
            targetCandidates: 0,
            assemblies: source.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: source.Value.ConsumedBytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: Stopwatch.GetElapsedTime(started));
        PlatformPopulationArtifactMaterializationOutcome materialized =
            await PackagePlatformLibraryMaterializer.MaterializeImplementationPopulationAsync(
                request, source, consumed);
        if (materialized is not PlatformPopulationArtifactMaterializationOutcome.Completed population)
        {
            PlatformHouseReceipt receipt = materialized.Realization.Receipt.HouseReceipt;
            throw new BrowserPlatformForwarderOperationException(
                receipt.SettlementKind.ToString().ToLowerInvariant(),
                $"Platform forwarding population could not be materialized ({receipt.SettlementKind}).",
                receipt);
        }

        bool transferred = false;
        try
        {
            PlatformPopulationMember? member = population.Population.Value.Members
                .SingleOrDefault(candidate =>
                    candidate.Library.ImplementationAssembly?.AssemblyIdentity is { } identity
                    && identity.Identity.IsEquivalentTo(activation.SourceAssembly));
            if (member is null)
            {
                throw new BrowserPlatformForwarderOperationException(
                    "unavailable", "The exact declaring Library is absent from the Platform implementation.");
            }

            int index = population.Population.Value.Members.ToList().IndexOf(member);
            LibraryContentOwner owner = population.Population.Owners[index];
            if (owner.IssueOperationLease(member.Library)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new BrowserPlatformForwarderOperationException(
                    "failed", "The declaring Library could not issue a resolution lease.");
            }
            ResolvedAssemblyReference descriptor;
            using (LibraryOperationLease lease = issued.Lease)
            {
                descriptor = lease.Snapshot(
                    member.Library.ImplementationAssembly!,
                    static (view, _) =>
                    {
                        byte[] image = view.Content.ToArray();
                        return ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                            view.Reference.Registration,
                            () => new MemoryStream(image, writable: false),
                            AssemblyResolutionProvenance.Designated(
                                "Browser Platform forwarder activation"))
                            ?? throw new BadImageFormatException(
                                "The Platform implementation Library has no managed metadata.");
                    },
                    cancellationToken);
            }
            if (descriptor.Registration.ModuleVersionId != activation.SourceModuleVersionId)
            {
                throw new BrowserPlatformForwarderOperationException(
                    "stale", "The declaring Library no longer matches the published forwarding declaration.");
            }

            var resolutionRequest = Request(
                activation.Target,
                new PlatformHouseOperation.ResolveTypeDefinition.FromImplementation<PlatformPopulationMember>(
                    new(
                        TypeResolutionRequest.FromAssembly(
                            descriptor, AssemblyResolutionScope.Platform, activation.Type),
                        "browser-platform-forwarder"),
                    new(member, "browser-platform-forwarder-start")),
                activation.Sources,
                cancellationToken);
            transferred = true;
            return await PlatformHouseTypeDefinitionResolver.ResolveImplementationAsync(
                resolutionRequest, population, consumed);
        }
        finally
        {
            if (!transferred)
                await RetireAsync(population);
        }
    }

    internal async Task<PackageImplementationLibrary> RealizeDestinationAsync(
        BrowserPlatformForwarderActivationResult.Activated activation,
        CancellationToken cancellationToken)
    {
        PlatformTypeResolutionAssemblyEvidence destination = activation.Destination switch
        {
            BrowserPlatformForwarderDestination.Forwarder forwarder => forwarder.Library,
            BrowserPlatformForwarderDestination.Definition definition => definition.Value.Assembly.Assembly,
            _ => throw new InvalidOperationException("Unknown forwarded Type destination."),
        };
        PackagePlatformHouseResult<PackageImplementationRealization>.Succeeded source =
            await RealizeAsync(
                Request(
                    activation.Target,
                    new PlatformHouseOperation.Realize(
                        new PlatformPopulationDemand.Library(
                            new PlatformLibraryDemand.Assembly(destination.Identity)),
                        PlatformViewDemand.Implementation),
                    activation.Sources,
                    cancellationToken),
                activation.RuntimeIdentifier);
        PackageImplementationLibrary? library = source.Value.Libraries
            .SingleOrDefault(candidate => candidate.Identity.IsEquivalentTo(destination.Identity));
        if (library is null)
        {
            throw new BrowserPlatformForwarderOperationException(
                "refused", "The source did not realize the exact destination Library.");
        }
        return library;
    }

    PlatformHouseRequest Request(
        PlatformFamilyTarget target,
        PlatformHouseOperation operation,
        PlatformSourcePlan sources,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("browser-platform-forwarder"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("browser-platform-forwarder")),
            operation,
            sources,
            new(
                maxSourceOperations: 8,
                maxTargetCandidates: 0,
                maxAssemblies: BrowserApiSurfacePolicy.MaxParticipants,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: BrowserInspectionScope.MaxRetainedImageBytes,
                maxForwardingHops: 32,
                maxDuration: _timeout),
            cancellationToken);

    async Task<PackagePlatformHouseResult<PackageImplementationRealization>.Succeeded> RealizeAsync(
        PlatformHouseRequest request,
        string runtimeIdentifier)
    {
        await using PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(authority =>
                ReferenceEquals(authority.Association, _client.Source.Association)
                    ? _client
                    : throw new InvalidOperationException(
                        "The forwarding operation selected another configured source."));
        PackagePlatformHouseResult<PackageImplementationRealization> result =
            await _adapter.RealizeImplementationAsync(
                request,
                runtimeIdentifier,
                sourceLease.IssueOperationLease(
                    request.CancellationToken, _timeout, _timeout));
        return result switch
        {
            PackagePlatformHouseResult<PackageImplementationRealization>.Succeeded succeeded => succeeded,
            PackagePlatformHouseResult<PackageImplementationRealization>.NotSucceeded terminal =>
                throw new BrowserPlatformForwarderOperationException(
                    terminal.Contribution.Kind.ToString().ToLowerInvariant(),
                    terminal.Diagnostic.Summary,
                    contribution: terminal.Contribution),
            _ => throw new InvalidOperationException("Unknown Platform source realization outcome."),
        };
    }

    static async ValueTask RetireAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed population)
    {
        List<Exception> failures = [];
        foreach (LibraryContentOwner owner in population.Population.Owners)
        {
            try
            {
                await owner.DisposeAsync();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            failures.AddRange(owner.CleanupFailures);
            failures.AddRange(owner.ReleaseFailures.Select(failure => failure.Failure));
        }
        try
        {
            await population.Artifacts.DisposeAsync();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(population.Artifacts.CleanupFailures);
        if (failures.Count != 0)
            throw new AggregateException("Forwarding population cleanup failed.", failures);
    }
}
