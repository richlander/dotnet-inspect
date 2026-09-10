using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>House-authorized capabilities of one installed reference source.</summary>
public sealed record InstalledPlatformHouseCapabilities
{
    internal InstalledPlatformHouseCapabilities(
        PlatformSourceCapabilityIdentity targetDiscovery,
        PlatformSourceCapabilityIdentity referenceRealization,
        PlatformSourceCapabilityIdentity implementationRealization,
        InstalledImplementationWorkBudget implementationMaximums)
    {
        TargetDiscovery = targetDiscovery;
        ReferenceRealization = referenceRealization;
        ImplementationRealization = implementationRealization;
        ImplementationMaximums = implementationMaximums;
    }

    public PlatformSourceCapabilityIdentity TargetDiscovery { get; }
    public PlatformSourceCapabilityIdentity ReferenceRealization { get; }
    public PlatformSourceCapabilityIdentity ImplementationRealization { get; }
    public InstalledImplementationWorkBudget ImplementationMaximums { get; }
}

/// <summary>
/// Live installed-source value paired with its resource-free House
/// contribution.
/// </summary>
public abstract record InstalledPlatformHouseResult<T>
    where T : notnull
{
    private protected InstalledPlatformHouseResult(
        PlatformSourceContribution contribution) =>
        Contribution = contribution;

    public PlatformSourceContribution Contribution { get; }

    public sealed record Succeeded : InstalledPlatformHouseResult<T>
    {
        public Succeeded(
            T value,
            PlatformSourceContribution contribution)
            : base(contribution)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record NotSucceeded : InstalledPlatformHouseResult<T>
    {
        public NotSucceeded(
            InstalledPlatformSourceDiagnostic diagnostic,
            PlatformSourceContribution contribution)
            : base(contribution)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public InstalledPlatformSourceDiagnostic Diagnostic { get; }
    }
}

/// <summary>
/// Translates PlatformHouse target currency into one package-free explicit
/// installed platform sources.
/// </summary>
public sealed class InstalledPlatformHouseAdapter
{
    private static long s_nextEvidence;
    private readonly InstalledReferencePackSource _referenceSource;
    private readonly InstalledImplementationPlatformSource
        _implementationSource;

    public InstalledPlatformHouseAdapter(
        InstalledReferencePackSource referenceSource,
        InstalledImplementationPlatformSource implementationSource,
        string capabilityName)
    {
        ArgumentNullException.ThrowIfNull(referenceSource);
        ArgumentNullException.ThrowIfNull(implementationSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
        if (!ReferenceEquals(
                referenceSource.Hive,
                implementationSource.Hive))
        {
            throw new ArgumentException(
                "Installed reference and implementation sources must belong to the same dotnet hive.",
                nameof(implementationSource));
        }

        _referenceSource = referenceSource;
        _implementationSource = implementationSource;
        Capabilities = new InstalledPlatformHouseCapabilities(
            PlatformSourceCapabilityIdentity.Create(
                capabilityName + "-target-discovery"),
            PlatformSourceCapabilityIdentity.Create(
                capabilityName + "-reference-realization"),
            PlatformSourceCapabilityIdentity.Create(
                capabilityName + "-implementation-realization"),
            new InstalledImplementationWorkBudget(
                maxFrameworks: 16,
                maxResolutionSteps: 256,
                maxManifestLibraries: 4096,
                maxManifestAssets: 8192,
                maxAssemblies: 4096,
                maxBytes: 2L * 1024 * 1024 * 1024));
    }

    public InstalledPlatformHouseCapabilities Capabilities { get; }

    public InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
        DiscoverTargets(PlatformHouseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
            ownerOutcome;
        if (request.Target is not PlatformTargetDemand.Selecting selecting)
        {
            throw new ArgumentException(
                "Installed target discovery requires a selecting House target.",
                nameof(request));
        }

        if (!request.Sources.Authorizes(
                PlatformSourceFacet.TargetDiscovery,
                Capabilities.TargetDiscovery)
            || !selecting.DiscoveryCapabilities.Any(
                capability => ReferenceEquals(
                    capability,
                    Capabilities.TargetDiscovery)))
        {
            return RejectDiscovery(
                request,
                "The installed target-discovery capability is not authorized by the House source plan.");
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.Work.MaxSourceOperations == 0
            || request.Work.MaxDuration == TimeSpan.Zero)
        {
            return IncompleteDiscovery(
                request,
                "The House work budget does not permit installed target discovery.");
        }

        using CancellationTokenSource budgetCancellation =
            CreateBudgetCancellation(request);
        try
        {
            ownerOutcome = _referenceSource.Discover(
                new InstalledReferenceDiscoveryRequest(
                    MapFamily(selecting.Family),
                    selecting.TargetFramework,
                    Math.Min(
                        selecting.Work.MaxCandidates,
                        request.Work.MaxTargetCandidates)),
                budgetCancellation.Token);
        }
        catch (OperationCanceledException)
            when (!request.CancellationToken.IsCancellationRequested)
        {
            return IncompleteDiscovery(
                request,
                "Installed target discovery exceeded the House duration budget.");
        }

        return ProjectDiscovery(
            request,
            ownerOutcome);
    }

    public async ValueTask<
        InstalledPlatformHouseResult<InstalledReferenceRealization>>
        RealizeReferenceAsync(PlatformHouseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not PlatformTargetDemand.Exact exact)
        {
            throw new ArgumentException(
                "Installed reference realization requires an exact House target.",
                nameof(request));
        }

        if (!request.Sources.Authorizes(
                PlatformSourceFacet.Reference,
                Capabilities.ReferenceRealization))
        {
            return RejectRealization(
                request,
                exact.Target,
                "The installed reference capability is not authorized by the House source plan.");
        }

        if (request.Operation is not PlatformHouseOperation.Realize realize
            || realize.View is PlatformViewDemand.Implementation)
        {
            return RejectRealization(
                request,
                exact.Target,
                "The House operation does not request reference realization.");
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.Work.MaxSourceOperations == 0
            || request.Work.MaxDuration == TimeSpan.Zero)
        {
            return IncompleteRealization(
                request,
                exact.Target,
                "The House work budget does not permit installed reference realization.");
        }

        if (realize.Population is PlatformPopulationDemand.Library
            {
                Value: PlatformLibraryDemand.PlatformLibrary
            })
        {
            return RejectRealization(
                request,
                exact.Target,
                "An opaque platform-library identity cannot be projected to an installed reference-pack member.");
        }

        InstalledReferencePopulationDemand population =
            realize.Population switch
            {
                PlatformPopulationDemand.Library
                {
                    Value: PlatformLibraryDemand.Assembly assembly
                } => new InstalledReferencePopulationDemand.Assembly(
                    assembly.Identity),
                PlatformPopulationDemand.CompletePopulation =>
                    new InstalledReferencePopulationDemand
                        .CompletePopulation(),
                _ => throw new InvalidOperationException(
                    "Unknown PlatformHouse population demand."),
            };

        InstalledPlatformSourceOutcome<InstalledReferenceRealization>
            ownerOutcome;
        using CancellationTokenSource budgetCancellation =
            CreateBudgetCancellation(request);
        try
        {
            ownerOutcome = await _referenceSource.RealizeAsync(
                    new InstalledReferenceRealizationRequest(
                        new InstalledReferencePackCoordinate(
                            _referenceSource.Hive,
                            MapFamily(exact.Target.Family),
                            exact.Target.TargetFramework,
                            exact.Target.Version),
                        population,
                        new InstalledReferenceWorkBudget(
                            request.Work.MaxAssemblies,
                            request.Work.MaxBytes)),
                    budgetCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!request.CancellationToken.IsCancellationRequested)
        {
            return IncompleteRealization(
                request,
                exact.Target,
                "Installed reference realization exceeded the House duration budget.");
        }

        return ProjectRealization(
            request,
            exact.Target,
            ownerOutcome);
    }

    public async ValueTask<
        InstalledPlatformHouseResult<InstalledImplementationRealization>>
        RealizeImplementationAsync(PlatformHouseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not PlatformTargetDemand.Exact exact)
        {
            throw new ArgumentException(
                "Installed implementation realization requires an exact House target.",
                nameof(request));
        }

        if (!request.Sources.Authorizes(
                PlatformSourceFacet.Implementation,
                Capabilities.ImplementationRealization))
        {
            return RejectImplementation(
                request,
                exact.Target,
                "The installed implementation capability is not authorized by the House source plan.");
        }

        if (request.Operation is not PlatformHouseOperation.Realize realize
            || realize.View == PlatformViewDemand.Reference)
        {
            return RejectImplementation(
                request,
                exact.Target,
                "The House operation does not request implementation realization.");
        }
        if (realize.Population is PlatformPopulationDemand.Library
            {
                Value: PlatformLibraryDemand.PlatformLibrary
            })
        {
            return RejectImplementation(
                request,
                exact.Target,
                "An opaque platform-library identity cannot be projected to an installed implementation member.");
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.Work.MaxSourceOperations == 0
            || request.Work.MaxDuration == TimeSpan.Zero
            || request.Work.MaxAssemblies == 0
            || request.Work.MaxBytes == 0)
        {
            return IncompleteImplementation(
                request,
                exact.Target,
                "The House work budget does not permit installed implementation realization.");
        }

        InstalledImplementationWorkBudget maximums =
            Capabilities.ImplementationMaximums;
        var sourceRequest = new InstalledImplementationRealizationRequest(
            new InstalledImplementationPlatformCoordinate(
                _implementationSource.Hive,
                MapFamily(exact.Target.Family),
                exact.Target.Version),
            new InstalledImplementationWorkBudget(
                maximums.MaxFrameworks,
                maximums.MaxResolutionSteps,
                maximums.MaxManifestLibraries,
                maximums.MaxManifestAssets,
                Math.Min(
                    request.Work.MaxAssemblies,
                    maximums.MaxAssemblies),
                Math.Min(request.Work.MaxBytes, maximums.MaxBytes)));

        InstalledPlatformSourceOutcome<InstalledImplementationRealization>
            ownerOutcome;
        using CancellationTokenSource budgetCancellation =
            CreateBudgetCancellation(request);
        try
        {
            ownerOutcome = await _implementationSource.RealizeAsync(
                    sourceRequest,
                    budgetCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!request.CancellationToken.IsCancellationRequested)
        {
            return IncompleteImplementation(
                request,
                exact.Target,
                "Installed implementation realization exceeded the House duration budget.");
        }

        if (ownerOutcome is InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded succeeded
            && realize.Population is PlatformPopulationDemand.Library
            {
                Value: PlatformLibraryDemand.Assembly assembly
            }
            && !succeeded.Value.Libraries.Any(
                library => assembly.Identity.IsEquivalentTo(
                    library.Identity)))
        {
            return UnavailableImplementation(
                request,
                exact.Target,
                succeeded.Value.Generation,
                "The requested assembly is absent from the installed implementation closure.");
        }

        return ProjectImplementation(
            request,
            exact.Target,
            ownerOutcome);
    }

    InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
        ProjectDiscovery(
            PlatformHouseRequest request,
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory> outcome)
    {
        PlatformSourceGeneration generation =
            PlatformSourceGeneration.Create(outcome.Generation.Name);
        PlatformSourceEvidenceIdentity evidence = NextEvidence();
        return outcome switch
        {
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Succeeded succeeded =>
                new InstalledPlatformHouseResult<
                    InstalledReferenceTargetInventory>.Succeeded(
                        succeeded.Value,
                        new PlatformSourceContribution.TargetDiscovery(
                            Capabilities.TargetDiscovery,
                            request.Snapshot,
                            generation,
                            succeeded.Value.Targets.Select(
                                static target =>
                                    ToHouseTarget(target.Coordinate)),
                            evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Unavailable unavailable =>
                NotSucceeded(
                    unavailable.Diagnostic,
                    new PlatformSourceContribution.Unavailable(
                        PlatformSourceFacet.TargetDiscovery,
                        Capabilities.TargetDiscovery,
                        request.Snapshot,
                        generation,
                        exactTarget: null,
                        MapUnavailable(unavailable.Reason),
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Rejected rejected =>
                NotSucceeded(
                    rejected.Diagnostic,
                    new PlatformSourceContribution.Rejected(
                        PlatformSourceFacet.TargetDiscovery,
                        Capabilities.TargetDiscovery,
                        request.Snapshot,
                        generation,
                        exactTarget: null,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Incomplete incomplete =>
                NotSucceeded(
                    incomplete.Diagnostic,
                    new PlatformSourceContribution.Incomplete(
                        PlatformSourceFacet.TargetDiscovery,
                        Capabilities.TargetDiscovery,
                        request.Snapshot,
                        generation,
                        exactTarget: null,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceTargetInventory>.Failed failed =>
                NotSucceeded(
                    failed.Diagnostic,
                    new PlatformSourceContribution.Failed(
                        PlatformSourceFacet.TargetDiscovery,
                        Capabilities.TargetDiscovery,
                        request.Snapshot,
                        generation,
                        exactTarget: null,
                        evidence)),
            _ => throw new InvalidOperationException(
                "Unknown installed target-discovery outcome."),
        };

        static InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory> NotSucceeded(
                InstalledPlatformSourceDiagnostic diagnostic,
                PlatformSourceContribution contribution) =>
            new InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.NotSucceeded(
                    diagnostic,
                    contribution);
    }

    InstalledPlatformHouseResult<InstalledReferenceRealization>
        ProjectRealization(
            PlatformHouseRequest request,
            PlatformFamilyTarget? exactTarget,
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization> outcome)
    {
        PlatformSourceGeneration generation =
            PlatformSourceGeneration.Create(outcome.Generation.Name);
        PlatformSourceEvidenceIdentity evidence = NextEvidence();
        return outcome switch
        {
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Succeeded succeeded =>
                new InstalledPlatformHouseResult<
                    InstalledReferenceRealization>.Succeeded(
                        succeeded.Value,
                        new PlatformSourceContribution.Realization(
                            PlatformSourceFacet.Reference,
                            Capabilities.ReferenceRealization,
                            request.Snapshot,
                            generation,
                            exactTarget
                                ?? throw new InvalidOperationException(
                                    "Successful realization requires an exact target."),
                            PlatformSourceCoordinateIdentity.Create(
                                CoordinateName(succeeded.Value.Coordinate)),
                            PlatformTargetCorrespondenceIdentity.Create(
                                NextName("installed-reference-target")),
                            ((PlatformHouseOperationSnapshot.Realize)
                                request.Snapshot.Operation).Population,
                            PlatformSourceContributionCompleteness
                                .Authoritative,
                            evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Unavailable unavailable =>
                NotSucceeded(
                    unavailable.Diagnostic,
                    new PlatformSourceContribution.Unavailable(
                        PlatformSourceFacet.Reference,
                        Capabilities.ReferenceRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        MapUnavailable(unavailable.Reason),
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Rejected rejected =>
                NotSucceeded(
                    rejected.Diagnostic,
                    new PlatformSourceContribution.Rejected(
                        PlatformSourceFacet.Reference,
                        Capabilities.ReferenceRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Incomplete incomplete =>
                NotSucceeded(
                    incomplete.Diagnostic,
                    new PlatformSourceContribution.Incomplete(
                        PlatformSourceFacet.Reference,
                        Capabilities.ReferenceRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledReferenceRealization>.Failed failed =>
                NotSucceeded(
                    failed.Diagnostic,
                    new PlatformSourceContribution.Failed(
                        PlatformSourceFacet.Reference,
                        Capabilities.ReferenceRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            _ => throw new InvalidOperationException(
                "Unknown installed reference realization outcome."),
        };

        static InstalledPlatformHouseResult<
            InstalledReferenceRealization> NotSucceeded(
                InstalledPlatformSourceDiagnostic diagnostic,
                PlatformSourceContribution contribution) =>
            new InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded(
                    diagnostic,
                    contribution);
    }

    InstalledPlatformHouseResult<InstalledImplementationRealization>
        ProjectImplementation(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization> outcome)
    {
        PlatformSourceGeneration generation =
            PlatformSourceGeneration.Create(outcome.Generation.Name);
        PlatformSourceEvidenceIdentity evidence = NextEvidence();
        return outcome switch
        {
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded succeeded =>
                new InstalledPlatformHouseResult<
                    InstalledImplementationRealization>.Succeeded(
                        succeeded.Value,
                        new PlatformSourceContribution.Realization(
                            PlatformSourceFacet.Implementation,
                            Capabilities.ImplementationRealization,
                            request.Snapshot,
                            generation,
                            exactTarget,
                            PlatformSourceCoordinateIdentity.Create(
                                CoordinateName(
                                    succeeded.Value.Coordinate)),
                            PlatformTargetCorrespondenceIdentity.Create(
                                NextName("installed-implementation-target")),
                            ((PlatformHouseOperationSnapshot.Realize)
                                request.Snapshot.Operation).Population,
                            PlatformSourceContributionCompleteness
                                .Authoritative,
                            evidence)),
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Unavailable unavailable =>
                NotSucceeded(
                    unavailable.Diagnostic,
                    new PlatformSourceContribution.Unavailable(
                        PlatformSourceFacet.Implementation,
                        Capabilities.ImplementationRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        MapUnavailable(unavailable.Reason),
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Rejected rejected =>
                NotSucceeded(
                    rejected.Diagnostic,
                    new PlatformSourceContribution.Rejected(
                        PlatformSourceFacet.Implementation,
                        Capabilities.ImplementationRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Incomplete incomplete =>
                NotSucceeded(
                    incomplete.Diagnostic,
                    new PlatformSourceContribution.Incomplete(
                        PlatformSourceFacet.Implementation,
                        Capabilities.ImplementationRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Failed failed =>
                NotSucceeded(
                    failed.Diagnostic,
                    new PlatformSourceContribution.Failed(
                        PlatformSourceFacet.Implementation,
                        Capabilities.ImplementationRealization,
                        request.Snapshot,
                        generation,
                        exactTarget,
                        evidence)),
            _ => throw new InvalidOperationException(
                "Unknown installed implementation realization outcome."),
        };

        static InstalledPlatformHouseResult<
            InstalledImplementationRealization> NotSucceeded(
                InstalledPlatformSourceDiagnostic diagnostic,
                PlatformSourceContribution contribution) =>
            new InstalledPlatformHouseResult<
                InstalledImplementationRealization>.NotSucceeded(
                    diagnostic,
                    contribution);
    }

    static InstalledPlatformFamily MapFamily(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime =>
                InstalledPlatformFamily.DotNetRuntime,
            PlatformFamily.AspNetCore =>
                InstalledPlatformFamily.AspNetCore,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    static PlatformFamily MapFamily(InstalledPlatformFamily family) =>
        family switch
        {
            InstalledPlatformFamily.DotNetRuntime =>
                PlatformFamily.DotNetRuntime,
            InstalledPlatformFamily.AspNetCore =>
                PlatformFamily.AspNetCore,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    static PlatformFamilyTarget ToHouseTarget(
        InstalledReferencePackCoordinate coordinate) =>
        new(
            MapFamily(coordinate.Family),
            coordinate.TargetFramework,
            coordinate.Version);

    static PlatformSourceUnavailabilityKind MapUnavailable(
        InstalledPlatformSourceUnavailabilityKind reason) =>
        reason switch
        {
            InstalledPlatformSourceUnavailabilityKind.Absent =>
                PlatformSourceUnavailabilityKind.Absent,
            InstalledPlatformSourceUnavailabilityKind.Unavailable =>
                PlatformSourceUnavailabilityKind.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    static string CoordinateName(
        InstalledReferencePackCoordinate coordinate) =>
        $"{coordinate.Hive.Name}:{coordinate.Family}:"
        + $"{coordinate.Version.Value}:"
        + $"{coordinate.TargetFramework}";

    static string CoordinateName(
        InstalledImplementationPlatformCoordinate coordinate) =>
        $"{coordinate.Hive.Name}:{coordinate.Family}:"
        + coordinate.Version.Value;

    static PlatformSourceEvidenceIdentity NextEvidence() =>
        PlatformSourceEvidenceIdentity.Create(
            NextName("installed-reference-evidence"));

    static string NextName(string prefix) =>
        prefix + "-" + Interlocked.Increment(ref s_nextEvidence);

    InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
        RejectDiscovery(
            PlatformHouseRequest request,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.InvalidRequest,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Rejected(
                    PlatformSourceFacet.TargetDiscovery,
                    Capabilities.TargetDiscovery,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(_referenceSource.Hive.Name + "-bridge")),
                    exactTarget: null,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledReferenceRealization>
        RejectRealization(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.InvalidRequest,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledReferenceRealization>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Rejected(
                    PlatformSourceFacet.Reference,
                    Capabilities.ReferenceRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(_referenceSource.Hive.Name + "-bridge")),
                    exactTarget,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
        IncompleteDiscovery(
            PlatformHouseRequest request,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledReferenceTargetInventory>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Incomplete(
                    PlatformSourceFacet.TargetDiscovery,
                    Capabilities.TargetDiscovery,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(_referenceSource.Hive.Name + "-bridge")),
                    exactTarget: null,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledReferenceRealization>
        IncompleteRealization(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledReferenceRealization>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Incomplete(
                    PlatformSourceFacet.Reference,
                    Capabilities.ReferenceRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(_referenceSource.Hive.Name + "-bridge")),
                    exactTarget,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledImplementationRealization>
        RejectImplementation(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.InvalidRequest,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledImplementationRealization>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Rejected(
                    PlatformSourceFacet.Implementation,
                    Capabilities.ImplementationRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(
                            _implementationSource.Hive.Name + "-bridge")),
                    exactTarget,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledImplementationRealization>
        IncompleteImplementation(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledImplementationRealization>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Incomplete(
                    PlatformSourceFacet.Implementation,
                    Capabilities.ImplementationRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        NextName(
                            _implementationSource.Hive.Name + "-bridge")),
                    exactTarget,
                    NextEvidence()));
    }

    InstalledPlatformHouseResult<InstalledImplementationRealization>
        UnavailableImplementation(
            PlatformHouseRequest request,
            PlatformFamilyTarget exactTarget,
            InstalledPlatformSourceGeneration sourceGeneration,
            string summary)
    {
        var diagnostic = new InstalledPlatformSourceDiagnostic(
            InstalledPlatformSourceDiagnosticKind.InvalidMember,
            summary);
        return new InstalledPlatformHouseResult<
            InstalledImplementationRealization>.NotSucceeded(
                diagnostic,
                new PlatformSourceContribution.Unavailable(
                    PlatformSourceFacet.Implementation,
                    Capabilities.ImplementationRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(sourceGeneration.Name),
                    exactTarget,
                    PlatformSourceUnavailabilityKind.Absent,
                    NextEvidence()));
    }

    static CancellationTokenSource CreateBudgetCancellation(
        PlatformHouseRequest request)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(
            request.CancellationToken);
        if (request.Work.MaxDuration
            <= TimeSpan.FromMilliseconds(uint.MaxValue - 1))
        {
            source.CancelAfter(request.Work.MaxDuration);
        }

        return source;
    }
}
