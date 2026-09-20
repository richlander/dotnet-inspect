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
            PackagePlatformSourceDiagnostic diagnostic,
            PlatformSourceContribution contribution,
            PlatformHouseConsumedWork? sourceWork = null)
            : base(contribution)
        {
            Diagnostic = diagnostic;
            SourceWork = sourceWork;
        }

        public PackagePlatformSourceDiagnostic Diagnostic { get; }
        public PlatformHouseConsumedWork? SourceWork { get; }
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
        AssociationRoute =
            PlatformSourceAssociationRouteIdentity.Create(
                capabilityName + "-association-route");
    }

    public PlatformSourceCapabilityIdentity TargetDiscovery { get; }
    public PlatformSourceCapabilityIdentity ReferenceRealization { get; }
    public PlatformSourceCapabilityIdentity ImplementationRealization { get; }
    public PlatformSourceAssociationRouteIdentity AssociationRoute { get; }

    /// <summary>Consumes one Package Source operation authorized for this House request.</summary>
    public Task<PackagePlatformHouseResult<PackagePlatformTargetInventory>> DiscoverTargetsAsync(
        PlatformHouseRequest request,
        PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DiscoverTargetsAsync(request, request.Work, operation);
    }

    public Task<PackagePlatformHouseResult<PackagePlatformTargetInventory>>
        DiscoverTargetsAsync(
            PlatformHouseRequest request,
            PlatformHouseWorkBudget remainingWork,
            PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(remainingWork);
            PlatformFamily family;
            PlatformTargetFramework targetFramework;
            PlatformTargetDiscoveryBudget discoveryWork;
            bool demandAuthorizes;
            switch (request.Target)
            {
                case PlatformTargetDemand.Selecting selecting:
                    family = selecting.Family;
                    targetFramework = selecting.TargetFramework;
                    discoveryWork = selecting.Work;
                    demandAuthorizes =
                        selecting.DiscoveryCapabilities.Any(
                            capability => ReferenceEquals(
                                capability,
                                TargetDiscovery));
                    break;
                case PlatformTargetDemand.FamilyDefault familyDefault
                    when familyDefault.Policy.Fallback.Capabilities.Any(
                        capability => ReferenceEquals(
                            capability,
                            TargetDiscovery))
                    && familyDefault.Policy.Fallback.Scope
                        is PlatformTargetDiscoveryScope.ExactFramework exact:
                    family = familyDefault.Family;
                    targetFramework = exact.TargetFramework;
                    discoveryWork = familyDefault.Work;
                    demandAuthorizes = true;
                    break;
                case PlatformTargetDemand.FamilyDefault:
                    return Task.FromResult(Stop<PackagePlatformTargetInventory>(
                        request, PlatformSourceFacet.TargetDiscovery, null,
                        "Package-backed family-default discovery requires the exact-framework fallback stage."));
                default:
                    throw new ArgumentException(
                        "Target discovery requires a selecting House target.",
                        nameof(request));
            }
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!request.Sources.Authorizes(PlatformSourceFacet.TargetDiscovery, TargetDiscovery)
                || !demandAuthorizes)
                return Task.FromResult(Stop<PackagePlatformTargetInventory>(
                    request, PlatformSourceFacet.TargetDiscovery, null,
                    "The package-backed target-discovery capability is not authorized."));
            if (remainingWork.MaxSourceOperations == 0
                || remainingWork.MaxDuration == TimeSpan.Zero)
                return Task.FromResult(Stop<PackagePlatformTargetInventory>(
                    request, PlatformSourceFacet.TargetDiscovery, null,
                    "The House work allowance does not permit target discovery.", incomplete: true));
            ValidateOperation(
                request,
                operation,
                remainingWork.MaxDuration);
            Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> pending =
                _source.DiscoverAsync(new(family, targetFramework,
                    Math.Min(
                        discoveryWork.MaxCandidates,
                        remainingWork.MaxTargetCandidates)),
                    operation);
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
        RealizeReference(
            request,
            null,
            null,
            selectedTarget: null,
            request.Work,
            operation,
            fromDiscovery: false);

    public Task<PackagePlatformHouseResult<PackageReferenceRealization>>
        RealizeSelectedReferenceAsync(
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformHouseWorkBudget remainingWork,
            PackageSourceOperationLease operation) =>
        RealizeReference(
            request,
            null,
            null,
            target,
            remainingWork,
            operation,
            fromDiscovery: false);

    /// <summary>Contributes the explicitly selected source candidate without settling a House target.</summary>
    public Task<PackagePlatformHouseResult<PackageReferenceRealization>> RealizeReferenceAsync(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded discovery,
        PackagePlatformTargetSelection selection,
        PackageSourceOperationLease operation) =>
        RealizeSelectedReferenceAsync(
            request,
            discovery,
            selection,
            request.Work,
            operation);

    public Task<PackagePlatformHouseResult<PackageReferenceRealization>>
        RealizeSelectedReferenceAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackagePlatformTargetInventory>.Succeeded discovery,
            PackagePlatformTargetSelection selection,
            PlatformHouseWorkBudget remainingWork,
            PackageSourceOperationLease operation) =>
        RealizeReference(
            request,
            discovery,
            selection,
            selectedTarget: null,
            remainingWork,
            operation,
            fromDiscovery: true);

    Task<PackagePlatformHouseResult<PackageReferenceRealization>> RealizeReference(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded? discovery,
        PackagePlatformTargetSelection? selection,
        PlatformFamilyTarget? selectedTarget,
        PlatformHouseWorkBudget workAllowance,
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
                bool corresponds = request.Target switch
                {
                    PlatformTargetDemand.Selecting selecting =>
                        selecting.Family == exact.Family
                        && selecting.TargetFramework
                            == exact.TargetFramework,
                    PlatformTargetDemand.FamilyDefault familyDefault =>
                        familyDefault.Family == exact.Family,
                    _ => false,
                };
                if (!corresponds)
                    throw new ArgumentException(
                        "The selected target must correspond to this discovery House request.", nameof(selection));
            }
            else
            {
                exact = request.Target switch
                {
                    PlatformTargetDemand.Exact target
                        when selectedTarget is null => target.Target,
                    PlatformTargetDemand.FamilyDefault familyDefault
                        when selectedTarget is not null
                            && familyDefault.Family
                                == selectedTarget.Family =>
                        selectedTarget,
                    _ => throw new ArgumentException(
                        "A discovery House request requires either its paired source selection or a corresponding external selected target.",
                        nameof(request)),
                };
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
            if (workAllowance.MaxSourceOperations == 0
                || workAllowance.MaxDuration == TimeSpan.Zero)
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
                && workAllowance.MaxXmlDocuments == 0)
            {
                return Task.FromResult(
                    Stop<PackageReferenceRealization>(
                        request,
                        PlatformSourceFacet.Reference,
                        exact,
                        "The House work allowance does not permit compiled XML realization.",
                        incomplete: true));
            }
            ValidateOperation(
                request,
                operation,
                workAllowance.MaxDuration);

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
            var work = new PackageReferenceWorkBudget(
                workAllowance.MaxAssemblies,
                workAllowance.MaxBytes);
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
            PackageSourceOperationLease operation) =>
        RealizeImplementation(
            request,
            selectedTarget: null,
            request.Work,
            runtimeIdentifier,
            operation);

    public Task<PackagePlatformHouseResult<PackageImplementationRealization>>
        RealizeSelectedImplementationAsync(
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformHouseWorkBudget remainingWork,
            string runtimeIdentifier,
            PackageSourceOperationLease operation) =>
        RealizeImplementation(
            request,
            target,
            remainingWork,
            runtimeIdentifier,
            operation);

    Task<PackagePlatformHouseResult<PackageImplementationRealization>>
        RealizeImplementation(
            PlatformHouseRequest request,
            PlatformFamilyTarget? selectedTarget,
            PlatformHouseWorkBudget workAllowance,
            string runtimeIdentifier,
            PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            PlatformFamilyTarget exact = request.Target switch
            {
                PlatformTargetDemand.Exact target
                    when selectedTarget is null => target.Target,
                PlatformTargetDemand.FamilyDefault familyDefault
                    when selectedTarget is not null
                        && familyDefault.Family == selectedTarget.Family =>
                    selectedTarget,
                _ => throw new ArgumentException(
                    "Package-backed implementation realization requires an exact or corresponding selected House target.",
                    nameof(request)),
            };
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
            if (workAllowance.MaxSourceOperations == 0
                || workAllowance.MaxDuration == TimeSpan.Zero
                || workAllowance.MaxAssemblies == 0
                || workAllowance.MaxBytes == 0)
            {
                return Task.FromResult(
                    Stop<PackageImplementationRealization>(
                        request,
                        PlatformSourceFacet.Implementation,
                        exact,
                        "The House work allowance does not permit implementation realization.",
                        incomplete: true));
            }
            ValidateOperation(
                request,
                operation,
                workAllowance.MaxDuration);

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
                    workAllowance.MaxAssemblies,
                    limits.MaxAssemblies),
                Math.Min(
                    workAllowance.MaxBytes,
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

    static void ValidateOperation(
        PlatformHouseRequest request,
        PackageSourceOperationLease operation,
        TimeSpan? maxDuration = null)
    {
        if (operation.CancellationToken != request.CancellationToken
            || operation.OperationTimeout
                > (maxDuration ?? request.Work.MaxDuration))
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
                            PlatformSourceUnavailabilityKind.Absent),
                        RealizationWork(success.Value));
            }

            static PlatformHouseConsumedWork RealizationWork(
                PackageImplementationRealization realization) =>
                new(
                    sourceOperations: 0,
                    targetCandidates: 0,
                    assemblies: realization.Libraries.Length,
                    xmlDocuments: 0,
                    portablePdbs: 0,
                    sourceDocuments: 0,
                    bytes: realization.ConsumedBytes,
                    forwardingHops: 0,
                    targetComparisons: 0,
                    elapsed: TimeSpan.Zero);

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
