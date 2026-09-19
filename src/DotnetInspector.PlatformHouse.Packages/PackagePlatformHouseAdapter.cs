using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>Live source values stay beside, not inside, resource-free House contributions.</summary>
public abstract record PackagePlatformHouseResult<T> where T : notnull
{
    private protected PackagePlatformHouseResult(PlatformSourceContribution contribution) =>
        Contribution = contribution;

    public PlatformSourceContribution Contribution { get; }

    public sealed record Succeeded : PackagePlatformHouseResult<T>
    {
        internal Succeeded(T value, PlatformSourceContribution contribution) : base(contribution) =>
            Value = value;

        public T Value { get; }
    }

    public sealed record NotSucceeded : PackagePlatformHouseResult<T>
    {
        internal NotSucceeded(
            PackagePlatformSourceDiagnostic diagnostic, PlatformSourceContribution contribution)
            : base(contribution) => Diagnostic = diagnostic;

        public PackagePlatformSourceDiagnostic Diagnostic { get; }
    }
}

/// <summary>Maps explicit House demand to the package-backed reference source.</summary>
public sealed class PackagePlatformHouseAdapter
{
    private static long s_nextAttempt;
    private readonly PackagePlatformSource _source;

    public PackagePlatformHouseAdapter(PackagePlatformSource source, string capabilityName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
        _source = source;
        TargetDiscovery = PlatformSourceCapabilityIdentity.Create(capabilityName + "-target-discovery");
        ReferenceRealization = PlatformSourceCapabilityIdentity.Create(capabilityName + "-reference-realization");
        ImplementationRealization = PlatformSourceCapabilityIdentity.Create(
            capabilityName + "-implementation-realization");
    }

    public PlatformSourceCapabilityIdentity TargetDiscovery { get; }
    public PlatformSourceCapabilityIdentity ReferenceRealization { get; }
    public PlatformSourceCapabilityIdentity ImplementationRealization { get; }

    /// <summary>Consumes one Package Source operation authorized for this House request.</summary>
    public Task<PackagePlatformHouseResult<PackagePlatformTargetInventory>> DiscoverTargetsAsync(
        PlatformHouseRequest request, PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Target is not PlatformTargetDemand.Selecting selecting)
                throw new ArgumentException("Target discovery requires a selecting House target.", nameof(request));
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!request.Sources.Authorizes(PlatformSourceFacet.TargetDiscovery, TargetDiscovery)
                || !selecting.DiscoveryCapabilities.Contains(TargetDiscovery))
                return Task.FromResult(Stop<PackagePlatformTargetInventory>(
                    request, PlatformSourceFacet.TargetDiscovery, null,
                    "The package-backed target-discovery capability is not authorized."));
            if (request.Work.MaxSourceOperations == 0 || request.Work.MaxDuration == TimeSpan.Zero)
                return Task.FromResult(Stop<PackagePlatformTargetInventory>(
                    request, PlatformSourceFacet.TargetDiscovery, null,
                    "The House work allowance does not permit target discovery.", incomplete: true));
            ValidateOperation(request, operation);
            Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> pending =
                _source.DiscoverAsync(new(selecting.Family, selecting.TargetFramework,
                    Math.Min(selecting.Work.MaxCandidates, request.Work.MaxTargetCandidates)), operation);
            transferred = true;
            return ProjectDiscoveryAsync(request, pending);
        }
        finally
        {
            if (!transferred)
                operation.Dispose();
        }
    }

    /// <summary>Realizes an exact House target established outside this source's discovery.</summary>
    public Task<PackagePlatformHouseResult<PackageReferenceRealization>> RealizeReferenceAsync(
        PlatformHouseRequest request,
        PackageSourceOperationLease operation) =>
        RealizeReference(request, null, null, operation, fromDiscovery: false);

    /// <summary>Contributes the explicitly selected source candidate without settling a House target.</summary>
    public Task<PackagePlatformHouseResult<PackageReferenceRealization>> RealizeReferenceAsync(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded discovery,
        PackagePlatformTargetSelection selection,
        PackageSourceOperationLease operation) =>
        RealizeReference(request, discovery, selection, operation, fromDiscovery: true);

    Task<PackagePlatformHouseResult<PackageReferenceRealization>> RealizeReference(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded? discovery,
        PackagePlatformTargetSelection? selection,
        PackageSourceOperationLease operation,
        bool fromDiscovery)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            PlatformFamilyTarget exact;
            if (fromDiscovery)
            {
                ArgumentNullException.ThrowIfNull(discovery);
                ArgumentNullException.ThrowIfNull(selection);
                exact = selection.Target;
                if (request.Target
                        is not PlatformTargetDemand.Selecting selecting
                    || request.Target.Family != exact.Family
                    || selecting.TargetFramework != exact.TargetFramework)
                    throw new ArgumentException(
                        "The selected target must correspond to this selecting House request.", nameof(selection));
            }
            else
            {
                if (request.Target is not PlatformTargetDemand.Exact target)
                    throw new ArgumentException(
                        "A selecting House request requires its paired discovery and an explicit source selection.",
                        nameof(request));
                exact = target.Target;
            }
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!request.Sources.Authorizes(PlatformSourceFacet.Reference, ReferenceRealization))
                return Task.FromResult(Stop<PackageReferenceRealization>(
                    request, PlatformSourceFacet.Reference, exact,
                    "The package-backed reference capability is not authorized."));
            if (!TryGetReferencePopulation(
                    request.Operation,
                    fromDiscovery,
                    out PlatformPopulationDemand? housePopulation))
                return Task.FromResult(Stop<PackageReferenceRealization>(
                    request, PlatformSourceFacet.Reference, exact,
                    "The House operation does not request reference realization."));
            if (housePopulation is PlatformPopulationDemand.Library
                { Value: PlatformLibraryDemand.PlatformLibrary })
                return Task.FromResult(Stop<PackageReferenceRealization>(
                    request, PlatformSourceFacet.Reference, exact,
                    "An opaque Platform library identity has no package-member correspondence."));
            if (request.Work.MaxSourceOperations == 0 || request.Work.MaxDuration == TimeSpan.Zero)
                return Task.FromResult(Stop<PackageReferenceRealization>(
                    request, PlatformSourceFacet.Reference, exact,
                    "The House work allowance does not permit reference realization.", incomplete: true));
            bool includeCompiledXmlDocumentation =
                request.Operation is PlatformHouseOperation.Realize
                {
                    ContentDemand: var contentDemand,
                }
                && contentDemand.HasFlag(
                    PlatformLibraryContentDemand
                        .CompiledXmlDocumentation);
            if (includeCompiledXmlDocumentation
                && request.Work.MaxXmlDocuments == 0)
            {
                return Task.FromResult(
                    Stop<PackageReferenceRealization>(
                        request,
                        PlatformSourceFacet.Reference,
                        exact,
                        "The House work allowance does not permit compiled XML realization.",
                        incomplete: true));
            }
            ValidateOperation(request, operation);

            if (fromDiscovery
                && (!ReferenceEquals(discovery!.Contribution.Request, request.Snapshot)
                    || !ReferenceEquals(discovery.Contribution.Capability, TargetDiscovery)
                    || !discovery.Value.Targets.Contains(selection!)))
                return Task.FromResult(Stop<PackageReferenceRealization>(
                    request, PlatformSourceFacet.Reference, exact,
                    "Reference realization requires the live source inventory paired with the selected discovery contribution."));

            PackageReferencePopulationDemand population = housePopulation switch
            {
                PlatformPopulationDemand.Library { Value: PlatformLibraryDemand.Assembly assembly } =>
                    new PackageReferencePopulationDemand.Assembly(assembly.Identity),
                PlatformPopulationDemand.CompletePopulation => new PackageReferencePopulationDemand.CompletePopulation(),
                _ => throw new InvalidOperationException("Unknown Platform population demand."),
            };
            var work = new PackageReferenceWorkBudget(request.Work.MaxAssemblies, request.Work.MaxBytes);
            Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> pending = !fromDiscovery
                ? _source.RealizeAsync(
                    new PackageReferencePackCoordinate(exact),
                    population,
                    work,
                    includeCompiledXmlDocumentation,
                    operation)
                : _source.RealizeAsync(
                    selection!,
                    population,
                    work,
                    includeCompiledXmlDocumentation,
                    operation);
            transferred = true;
            return ProjectRealizationAsync(
                request,
                exact,
                housePopulation,
                pending);
        }
        finally
        {
            if (!transferred)
                operation.Dispose();
        }
    }

    /// <summary>
    /// Realizes an exact House target through authorized RID-specific runtime
    /// packs.
    /// </summary>
    public Task<PackagePlatformHouseResult<PackageImplementationRealization>>
        RealizeImplementationAsync(
            PlatformHouseRequest request,
            string runtimeIdentifier,
            PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Target is not PlatformTargetDemand.Exact target)
            {
                throw new ArgumentException(
                    "Package-backed implementation realization requires an exact House target.",
                    nameof(request));
            }

            PlatformFamilyTarget exact = target.Target;
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!request.Sources.Authorizes(
                    PlatformSourceFacet.Implementation,
                    ImplementationRealization))
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "The package-backed implementation capability is not authorized."));
            }
            if (request.Operation is not PlatformHouseOperation.Realize realize
                || realize.View == PlatformViewDemand.Reference)
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "The House operation does not request implementation realization."));
            }
            if (realize.Population is PlatformPopulationDemand.Library
                { Value: PlatformLibraryDemand.PlatformLibrary })
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "An opaque Platform library identity has no runtime-package member correspondence."));
            }
            if (request.Work.MaxSourceOperations == 0
                || request.Work.MaxDuration == TimeSpan.Zero
                || request.Work.MaxAssemblies == 0
                || request.Work.MaxBytes == 0)
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "The House work allowance does not permit implementation realization.",
                        incomplete: true));
            }
            ValidateOperation(request, operation);

            PackageImplementationPlatformCoordinate coordinate;
            try
            {
                coordinate = new(
                    exact,
                    runtimeIdentifier);
            }
            catch (ArgumentException)
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "The runtime identifier cannot form a package-backed implementation coordinate."));
            }

            PackagePlatformSourceLimits limits = _source.Limits;
            var work = new PackageImplementationWorkBudget(
                limits.MaxFrameworks,
                limits.MaxResolutionSteps,
                limits.MaxManifestLibraries,
                limits.MaxManifestAssets,
                Math.Min(
                    request.Work.MaxAssemblies,
                    limits.MaxAssemblies),
                Math.Min(
                    request.Work.MaxBytes,
                    limits.MaxBytes));
            Task<PackagePlatformSourceOutcome<PackageImplementationRealization>>
                pending = _source.RealizeImplementationAsync(
                    coordinate,
                    work,
                    operation);
            transferred = true;
            return ProjectImplementationAsync(
                request,
                exact,
                realize,
                pending);
        }
        finally
        {
            if (!transferred)
                operation.Dispose();
        }
    }

    static void ValidateOperation(PlatformHouseRequest request, PackageSourceOperationLease operation)
    {
        if (operation.CancellationToken != request.CancellationToken
            || operation.OperationTimeout > request.Work.MaxDuration)
            throw new ArgumentException(
                "The Package Source operation must carry the House cancellation token and a deadline within its duration allowance.",
                nameof(operation));
    }

    async Task<PackagePlatformHouseResult<PackagePlatformTargetInventory>> ProjectDiscoveryAsync(
        PlatformHouseRequest request,
        Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> pending)
    {
        PackagePlatformSourceOutcome<PackagePlatformTargetInventory> outcome = await pending.ConfigureAwait(false);
        if (outcome is PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded success)
            return new PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded(success.Value,
                new PlatformSourceContribution.TargetDiscovery(TargetDiscovery, request.Snapshot,
                    PlatformSourceGeneration.Create(outcome.Generation.Name),
                    success.Value.Targets.Select(static selection => selection.Target)));
        return ProjectFailure(request, PlatformSourceFacet.TargetDiscovery, null,
            (PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.NotSucceeded)outcome);
    }

    async Task<PackagePlatformHouseResult<PackageReferenceRealization>> ProjectRealizationAsync(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> pending)
    {
        PackagePlatformSourceOutcome<PackageReferenceRealization> outcome = await pending.ConfigureAwait(false);
        if (outcome is PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded success)
            return new PackagePlatformHouseResult<PackageReferenceRealization>.Succeeded(success.Value,
                new PlatformSourceContribution.Realization(PlatformSourceFacet.Reference,
                    ReferenceRealization, request.Snapshot,
                    PlatformSourceGeneration.Create(outcome.Generation.Name), target,
                    PlatformSourceCoordinateIdentity.Create(
                        $"{success.Value.Coordinate.PackageId}:{target.Version.Value}:ref/{target.TargetFramework}"),
                    population,
                    PlatformSourceContributionCompleteness.Authoritative));
        return ProjectFailure(request, PlatformSourceFacet.Reference, target,
            (PackagePlatformSourceOutcome<PackageReferenceRealization>.NotSucceeded)outcome);
    }

    static bool TryGetReferencePopulation(
        PlatformHouseOperation operation,
        bool fromDiscovery,
        out PlatformPopulationDemand? population)
    {
        switch (operation)
        {
            case PlatformHouseOperation.Realize realize
                when realize.View
                    is not PlatformViewDemand.Implementation:
                population = realize.Population;
                return true;
            case PlatformHouseOperation.ResolveAssemblyReference
                {
                    RequiredView: PlatformViewDemand.Reference,
                    Request.Target:
                        AssemblyBindingTarget.AssemblyReference target,
                } when !fromDiscovery:
                population = new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(
                        target.Identity));
                return true;
            default:
                population = null;
                return false;
        }
    }

    async Task<PackagePlatformHouseResult<PackageImplementationRealization>>
        ProjectImplementationAsync(
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformHouseOperation.Realize realize,
            Task<PackagePlatformSourceOutcome<PackageImplementationRealization>>
                pending)
    {
        PackagePlatformSourceOutcome<PackageImplementationRealization>
            outcome = await pending.ConfigureAwait(false);
        if (outcome is PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Succeeded success)
        {
            if (realize.Population is PlatformPopulationDemand.Library
                { Value: PlatformLibraryDemand.Assembly assembly }
                && !success.Value.Libraries.Any(
                    library => assembly.Identity.IsEquivalentTo(
                        library.Identity)))
            {
                var diagnostic = new PackagePlatformSourceDiagnostic(
                    PackagePlatformSourceDiagnosticKind.MemberUnavailable,
                    "The requested assembly is absent from the package-backed implementation closure.",
                    [.. success.Value.Frameworks.SelectMany(
                        static framework => framework.PackageFailures)]);
                return new PackagePlatformHouseResult<
                    PackageImplementationRealization>.NotSucceeded(
                        diagnostic,
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.Implementation,
                            ImplementationRealization,
                            request.Snapshot,
                            PlatformSourceGeneration.Create(
                                outcome.Generation.Name),
                            target,
                            PlatformSourceUnavailabilityKind.Absent));
            }

            return new PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded(
                    success.Value,
                    new PlatformSourceContribution.Realization(
                        PlatformSourceFacet.Implementation,
                        ImplementationRealization,
                        request.Snapshot,
                        PlatformSourceGeneration.Create(
                            outcome.Generation.Name),
                        target,
                        PlatformSourceCoordinateIdentity.Create(
                            $"{success.Value.Coordinate.PackageId}:"
                            + $"{target.Version.Value}:"
                            + $"{success.Value.Coordinate.RuntimeIdentifier}:"
                            + $"{target.TargetFramework}"),
                        ((PlatformHouseOperationSnapshot.Realize)
                            request.Snapshot.Operation).Population,
                        PlatformSourceContributionCompleteness
                            .Authoritative));
        }

        return ProjectFailure(
            request,
            PlatformSourceFacet.Implementation,
            target,
            (PackagePlatformSourceOutcome<
                PackageImplementationRealization>.NotSucceeded)outcome);
    }

    PackagePlatformHouseResult<T> ProjectFailure<T>(
        PlatformHouseRequest request, PlatformSourceFacet facet, PlatformFamilyTarget? target,
        PackagePlatformSourceOutcome<T>.NotSucceeded failure) where T : notnull
    {
        PlatformSourceCapabilityIdentity capability = Capability(facet);
        PlatformSourceGeneration generation = PlatformSourceGeneration.Create(failure.Generation.Name);
        PlatformSourceContribution contribution = failure switch
        {
            PackagePlatformSourceOutcome<T>.Unavailable unavailable =>
                new PlatformSourceContribution.Unavailable(facet, capability, request.Snapshot, generation, target,
                    unavailable.Diagnostic.Kind is PackagePlatformSourceDiagnosticKind.PackageUnavailable
                        or PackagePlatformSourceDiagnosticKind.MemberUnavailable
                        ? PlatformSourceUnavailabilityKind.Absent : PlatformSourceUnavailabilityKind.Unavailable),
            PackagePlatformSourceOutcome<T>.Rejected =>
                new PlatformSourceContribution.Rejected(facet, capability, request.Snapshot, generation, target),
            PackagePlatformSourceOutcome<T>.Incomplete =>
                new PlatformSourceContribution.Incomplete(facet, capability, request.Snapshot, generation, target),
            PackagePlatformSourceOutcome<T>.Failed =>
                new PlatformSourceContribution.Failed(facet, capability, request.Snapshot, generation, target),
            _ => throw new InvalidOperationException("Unknown package-backed Platform outcome."),
        };
        return new PackagePlatformHouseResult<T>.NotSucceeded(failure.Diagnostic, contribution);
    }

    PackagePlatformHouseResult<T> Stop<T>(
        PlatformHouseRequest request, PlatformSourceFacet facet, PlatformFamilyTarget? target,
        string summary, bool incomplete = false) where T : notnull
    {
        var generation = PlatformSourceGeneration.Create(
            $"package-platform-adapter-{Interlocked.Increment(ref s_nextAttempt)}");
        PlatformSourceContribution contribution = incomplete
            ? new PlatformSourceContribution.Incomplete(facet, Capability(facet), request.Snapshot, generation, target)
            : new PlatformSourceContribution.Rejected(facet, Capability(facet), request.Snapshot, generation, target);
        return new PackagePlatformHouseResult<T>.NotSucceeded(
            new(incomplete ? PackagePlatformSourceDiagnosticKind.WorkLimitExceeded
                : PackagePlatformSourceDiagnosticKind.InvalidSelection, summary, []), contribution);
    }

    PlatformSourceCapabilityIdentity Capability(
        PlatformSourceFacet facet) =>
        facet switch
        {
            PlatformSourceFacet.TargetDiscovery => TargetDiscovery,
            PlatformSourceFacet.Reference => ReferenceRealization,
            PlatformSourceFacet.Implementation =>
                ImplementationRealization,
            _ => throw new ArgumentOutOfRangeException(nameof(facet)),
        };
}
