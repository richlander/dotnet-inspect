using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// Credential-free producer identity retained in dependency Content. It does
/// not contain the runtime association that authorizes source operations.
/// </summary>
public sealed record DependencyInspectionPackageSourceIdentity
{
    public DependencyInspectionPackageSourceIdentity(
        string producerKey,
        string portableProducerKey,
        PackageSourceKind transportKind,
        InertString producerDisplay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        if (!PackageProducerIdentity.IsCanonicalPortableKey(
                portableProducerKey))
        {
            throw new ArgumentException(
                "A dependency Content source requires a canonical portable producer key.",
                nameof(portableProducerKey));
        }
        if (!Enum.IsDefined(transportKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transportKind),
                transportKind,
                "Unknown package source transport kind.");
        }

        ProducerKey = producerKey;
        PortableProducerKey = portableProducerKey;
        TransportKind = transportKind;
        ProducerDisplay = producerDisplay;
    }

    public string ProducerKey { get; }

    public string PortableProducerKey { get; }

    public PackageSourceKind TransportKind { get; }

    public InertString ProducerDisplay { get; }

    public static DependencyInspectionPackageSourceIdentity Create(
        PackageSourceResultIdentity source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Producer.Key,
            source.Producer.PortableKey,
            source.TransportKind,
            source.Producer.Display);
    }
}

public sealed record DependencyInspectionPackageSourceFailure(
    DependencyInspectionPackageSourceIdentity Source,
    PackageSourceCapabilities Capability,
    PackageSourceCoordinate? Coordinate,
    PackageSourceFailureKind Kind,
    string Message)
{
    public static DependencyInspectionPackageSourceFailure Create(
        PackageSourceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            DependencyInspectionPackageSourceIdentity.Create(failure.Source),
            failure.Capability,
            failure.Coordinate,
            failure.Kind,
            failure.Message);
    }
}

public sealed record DependencyInspectionPackageAuthorityFailure(
    InertString Authority,
    PackageAuthorityFailureKind Kind,
    string Message,
    DependencyInspectionPackageSourceFailure? SourceFailure,
    DependencyInspectionPackageSourceIdentity? ResultSource,
    PackageSourceTimeout? Timeout,
    bool IsRequiredProducerUnavailable)
{
    public static DependencyInspectionPackageAuthorityFailure Create(
        PackageAuthorityFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            failure.Authority,
            failure.Kind,
            failure.Message,
            failure.SourceFailure is { } sourceFailure
                ? DependencyInspectionPackageSourceFailure.Create(sourceFailure)
                : null,
            failure.ResultSource is { } resultSource
                ? DependencyInspectionPackageSourceIdentity.Create(resultSource)
                : null,
            failure.Timeout,
            failure.IsRequiredProducerUnavailable);
    }

    public static ImmutableArray<DependencyInspectionPackageAuthorityFailure>
        CreateRange(IEnumerable<PackageAuthorityFailure> failures) =>
        [.. failures.Select(Create)];
}

/// <summary>
/// Portable facts describing a resolved candidate. This value cannot authorize
/// package acquisition and retains no configured authority or correspondence.
/// </summary>
public sealed record DependencyInspectionPackageVersionDiscoveryContract(
    int ContractVersion,
    bool IncludePrerelease,
    bool IncludeUnlisted,
    int? Limit)
{
    public static DependencyInspectionPackageVersionDiscoveryContract Create(
        PackageVersionDiscoveryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new(
            contract.ContractVersion,
            contract.IncludePrerelease,
            contract.IncludeUnlisted,
            contract.Limit);
    }
}

public sealed record DependencyInspectionPackageCandidate(
    PackageSourceCoordinate Coordinate,
    PackageAcquisitionCandidateKind Kind,
    DependencyInspectionPackageVersionDiscoveryContract? DiscoveryContract)
{
    public static DependencyInspectionPackageCandidate Create(
        PackageAcquisitionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new(
            candidate.Coordinate,
            candidate.Kind,
            candidate.DiscoveryContract is { } contract
                ? DependencyInspectionPackageVersionDiscoveryContract.Create(
                    contract)
                : null);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateFailure.AuthorizationDenied),
    "authorization-denied")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateFailure.NoMatchingVersion),
    "no-matching-version")]
[JsonDerivedType(
    typeof(
        DependencyInspectionPackageCandidateFailure
            .ResolvedCoordinateMismatch),
    "resolved-coordinate-mismatch")]
public abstract record DependencyInspectionPackageCandidateFailure
{
    private DependencyInspectionPackageCandidateFailure()
    {
    }

    public sealed record AuthorizationDenied(
        ImmutableArray<DependencyInspectionPackageAuthorityFailure> Failures) :
        DependencyInspectionPackageCandidateFailure
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Failures
        { get; init; } = Failures.IsDefault ? [] : Failures;
    }

    public sealed record NoMatchingVersion :
        DependencyInspectionPackageCandidateFailure;

    public sealed record ResolvedCoordinateMismatch(
        RestoredProjectPackageNodeIdentity ResolvedPackage) :
        DependencyInspectionPackageCandidateFailure;

    public static DependencyInspectionPackageCandidateFailure Create(
        PackageDependencyTraversalCandidateFailure failure) =>
        failure switch
        {
            PackageDependencyTraversalCandidateFailure.AuthorizationDenied
                denied => new AuthorizationDenied(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        denied.Failures)),
            PackageDependencyTraversalCandidateFailure.NoMatchingVersion =>
                new NoMatchingVersion(),
            _ => throw new InvalidOperationException(
                "Unknown traversal package candidate failure."),
        };

    public static DependencyInspectionPackageCandidateFailure Create(
        PackageDependencyCandidateFailure failure) =>
        failure switch
        {
            PackageDependencyCandidateFailure.AuthorizationDenied denied =>
                new AuthorizationDenied(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        denied.Failures)),
            PackageDependencyCandidateFailure.NoMatchingVersion =>
                new NoMatchingVersion(),
            PackageDependencyCandidateFailure.ResolvedCoordinateMismatch
                mismatch => new ResolvedCoordinateMismatch(
                    mismatch.ResolvedPackage),
            _ => throw new InvalidOperationException(
                "Unknown package candidate failure."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateIncomplete.PinnedAuthorization),
    "pinned-authorization")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateIncomplete.VersionDiscovery),
    "version-discovery")]
public abstract record DependencyInspectionPackageCandidateIncomplete
{
    private DependencyInspectionPackageCandidateIncomplete()
    {
    }

    public sealed record PinnedAuthorization(
        ImmutableArray<DependencyInspectionPackageAuthorityFailure> Failures) :
        DependencyInspectionPackageCandidateIncomplete
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Failures
        { get; init; } = Failures.IsDefault ? [] : Failures;
    }

    public sealed record VersionDiscovery(
        PackageVersionDiscoveryState State,
        DependencyInspectionPackageVersionDiscoveryContract Contract,
        int CandidateObservationCount,
        ImmutableArray<DependencyInspectionPackageAuthorityFailure> Failures) :
        DependencyInspectionPackageCandidateIncomplete
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Failures
        { get; init; } = Failures.IsDefault ? [] : Failures;
    }

    public static DependencyInspectionPackageCandidateIncomplete Create(
        PackageDependencyTraversalCandidateIncomplete incomplete) =>
        incomplete switch
        {
            PackageDependencyTraversalCandidateIncomplete.PinnedAuthorization
                pinned => new PinnedAuthorization(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        pinned.Failures)),
            PackageDependencyTraversalCandidateIncomplete.VersionDiscovery
                discovery => new VersionDiscovery(
                    discovery.State,
                    DependencyInspectionPackageVersionDiscoveryContract.Create(
                        discovery.Contract),
                    discovery.CandidateObservationCount,
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        discovery.Failures)),
            _ => throw new InvalidOperationException(
                "Unknown traversal package candidate incompleteness."),
        };

    public static DependencyInspectionPackageCandidateIncomplete Create(
        PackageDependencyCandidateIncomplete incomplete) =>
        incomplete switch
        {
            PackageDependencyCandidateIncomplete.PinnedAuthorization pinned =>
                new PinnedAuthorization(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        pinned.Failures)),
            PackageDependencyCandidateIncomplete.VersionDiscovery discovery =>
                new VersionDiscovery(
                    discovery.State,
                    DependencyInspectionPackageVersionDiscoveryContract.Create(
                        discovery.Contract),
                    discovery.CandidateObservationCount,
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        discovery.Failures)),
            _ => throw new InvalidOperationException(
                "Unknown package candidate incompleteness."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateOutcome.Resolved),
    "resolved")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateOutcome.Failed),
    "failed")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageCandidateOutcome.Incomplete),
    "incomplete")]
public abstract record DependencyInspectionPackageCandidateOutcome
{
    private DependencyInspectionPackageCandidateOutcome()
    {
    }

    public sealed record Resolved(
        DependencyInspectionPackageCandidate Candidate,
        ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Diagnostics) : DependencyInspectionPackageCandidateOutcome
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Diagnostics
        { get; init; } = Diagnostics.IsDefault ? [] : Diagnostics;
    }

    public sealed record Failed(
        DependencyInspectionPackageCandidateFailure Failure) :
        DependencyInspectionPackageCandidateOutcome;

    public sealed record Incomplete(
        DependencyInspectionPackageCandidateIncomplete Evidence) :
        DependencyInspectionPackageCandidateOutcome;

    public static DependencyInspectionPackageCandidateOutcome Create(
        PackageDependencyTraversalCandidateResult outcome) =>
        outcome switch
        {
            PackageDependencyTraversalCandidateResult.Resolved resolved =>
                new Resolved(
                    DependencyInspectionPackageCandidate.Create(
                        resolved.Candidate),
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        resolved.Diagnostics)),
            PackageDependencyTraversalCandidateResult.Failed failed =>
                new Failed(
                    DependencyInspectionPackageCandidateFailure.Create(
                        failed.Failure)),
            PackageDependencyTraversalCandidateResult.Incomplete incomplete =>
                new Incomplete(
                    DependencyInspectionPackageCandidateIncomplete.Create(
                        incomplete.Evidence)),
            _ => throw new InvalidOperationException(
                "Unknown traversal package candidate outcome."),
        };

    public static DependencyInspectionPackageCandidateOutcome Create(
        PackageDependencyCandidateResult outcome) =>
        outcome switch
        {
            PackageDependencyCandidateResult.Resolved resolved =>
                new Resolved(
                    DependencyInspectionPackageCandidate.Create(
                        resolved.Candidate),
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        resolved.Diagnostics)),
            PackageDependencyCandidateResult.Failed failed =>
                new Failed(
                    DependencyInspectionPackageCandidateFailure.Create(
                        failed.Failure)),
            PackageDependencyCandidateResult.Incomplete incomplete =>
                new Incomplete(
                    DependencyInspectionPackageCandidateIncomplete.Create(
                        incomplete.Evidence)),
            _ => throw new InvalidOperationException(
                "Unknown package candidate outcome."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageManifestFailure.Acquisition),
    "acquisition")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageManifestFailure.IncompleteAcquisition),
    "incomplete-acquisition")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageManifestFailure.Identity),
    "identity")]
[JsonDerivedType(
    typeof(DependencyInspectionPackageManifestFailure.Declaration),
    "declaration")]
[JsonDerivedType(
    typeof(
        DependencyInspectionPackageManifestFailure
            .ManifestProjectionBudgetExhausted),
    "manifest-projection-budget-exhausted")]
public abstract record DependencyInspectionPackageManifestFailure
{
    private DependencyInspectionPackageManifestFailure()
    {
    }

    public sealed record Acquisition(
        ImmutableArray<DependencyInspectionPackageAuthorityFailure> Failures) :
        DependencyInspectionPackageManifestFailure
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Failures
        { get; init; } = Failures.IsDefault ? [] : Failures;
    }

    public sealed record IncompleteAcquisition(
        ImmutableArray<DependencyInspectionPackageAuthorityFailure> Failures) :
        DependencyInspectionPackageManifestFailure
    {
        public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
            Failures
        { get; init; } = Failures.IsDefault ? [] : Failures;
    }

    public sealed record Identity(PackageManifestFailure Failure) :
        DependencyInspectionPackageManifestFailure;

    public sealed record Declaration(
        PackageDependencyEvidenceDeclarationFailure Failure) :
        DependencyInspectionPackageManifestFailure;

    public sealed record ManifestProjectionBudgetExhausted(int Limit) :
        DependencyInspectionPackageManifestFailure;

    public static DependencyInspectionPackageManifestFailure Create(
        PackageDependencyTraversalManifestFailureDetail failure) =>
        failure switch
        {
            PackageDependencyTraversalManifestFailureDetail.Acquisition
                acquisition => new Acquisition(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        acquisition.Failures)),
            PackageDependencyTraversalManifestFailureDetail
                .IncompleteAcquisition incomplete => new IncompleteAcquisition(
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        incomplete.Failures)),
            PackageDependencyTraversalManifestFailureDetail.Identity identity =>
                new Identity(identity.Failure),
            PackageDependencyTraversalManifestFailureDetail.Declaration
                declaration => new Declaration(declaration.Failure),
            PackageDependencyTraversalManifestFailureDetail
                .ManifestProjectionBudgetExhausted budget =>
                    new ManifestProjectionBudgetExhausted(budget.Limit),
            _ => throw new InvalidOperationException(
                "Unknown package manifest failure."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionRestoredTraversalOutcomeFailure.Document),
    "document")]
[JsonDerivedType(
    typeof(DependencyInspectionRestoredTraversalOutcomeFailure.Graph),
    "graph")]
public abstract record DependencyInspectionRestoredTraversalOutcomeFailure
{
    private DependencyInspectionRestoredTraversalOutcomeFailure()
    {
    }

    public sealed record Document(RestoredProjectDependencyFailure Failure) :
        DependencyInspectionRestoredTraversalOutcomeFailure;

    public sealed record Graph(RestoredProjectGraphFailure Failure) :
        DependencyInspectionRestoredTraversalOutcomeFailure;

    public static DependencyInspectionRestoredTraversalOutcomeFailure Create(
        RestoredProjectDependencyTraversalFailure failure) =>
        failure switch
        {
            RestoredProjectDependencyTraversalFailure.Document document =>
                new Document(document.Failure),
            RestoredProjectDependencyTraversalFailure.Graph graph =>
                new Graph(graph.Failure),
            _ => throw new InvalidOperationException(
                "Unknown restored traversal failure."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.Evaluated),
    "evaluated")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.ApplicationAuthoredExemption),
    "application-authored-exemption")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.UnattributedAuthorship),
    "unattributed-authorship")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.ProcessingIncomplete),
    "processing-incomplete")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.ProcessingUnavailable),
    "processing-unavailable")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.ProcessingFailed),
    "processing-failed")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.RuntimeProjected),
    "runtime-projected")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.PreviouslyEvaluated),
    "previously-evaluated")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.ProcessingNotEvidenced),
    "processing-not-evidenced")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningResult.TargetUnavailable),
    "target-unavailable")]
public abstract record DependencyInspectionPruningResult
{
    private DependencyInspectionPruningResult()
    {
    }

    public sealed record Evaluated(
        PackageSourceCoordinate Coordinate,
        PlatformFamily PlatformFamily,
        string PlatformTargetFramework,
        string PlatformVersion,
        string? PlatformProvidedVersion,
        PlatformSubsumption Subsumption,
        bool DelegatesToPlatform) : DependencyInspectionPruningResult;

    public sealed record ApplicationAuthoredExemption :
        DependencyInspectionPruningResult;

    public sealed record UnattributedAuthorship :
        DependencyInspectionPruningResult;

    public sealed record ProcessingIncomplete(
        PackageDependencyEvidenceProcessingResult.Available Processing) :
        DependencyInspectionPruningResult;

    public sealed record ProcessingUnavailable(
        PackageDependencyEvidenceProcessingResult.Unavailable Processing) :
        DependencyInspectionPruningResult;

    public sealed record ProcessingFailed(
        PackageDependencyEvidenceProcessingResult.Failed Processing) :
        DependencyInspectionPruningResult;

    public sealed record RuntimeProjected(
        PackageDependencyEvidenceProcessingResult.Available Processing) :
        DependencyInspectionPruningResult;

    public sealed record PreviouslyEvaluated(
        PackageDependencyEvidenceProcessingResult.Available Processing) :
        DependencyInspectionPruningResult;

    public sealed record ProcessingNotEvidenced(
        PackageDependencyEvidenceProcessingResult.Available Processing) :
        DependencyInspectionPruningResult;

    public sealed record TargetUnavailable(
        PackageHouseDependencyPruningTargetUnavailableReason Reason) :
        DependencyInspectionPruningResult;

    public static DependencyInspectionPruningResult Create(
        PackageHouseDependencyPruningResult result,
        string? platformProvidedVersion) =>
        result switch
        {
            PackageHouseDependencyPruningResult.Evaluated evaluated =>
                new Evaluated(
                    evaluated.Pruning.Coordinate,
                    evaluated.Pruning.Target.Family,
                    evaluated.Pruning.Target.TargetFramework.ToString(),
                    evaluated.Pruning.Target.Version.Value,
                    platformProvidedVersion,
                    evaluated.Pruning.Supply.Subsumption,
                    evaluated.Pruning.Supply.DelegatesToPlatform),
            PackageHouseDependencyPruningResult.ApplicationAuthoredExemption =>
                new ApplicationAuthoredExemption(),
            PackageHouseDependencyPruningResult.UnattributedAuthorship =>
                new UnattributedAuthorship(),
            PackageHouseDependencyPruningResult.ProcessingIncomplete
                incomplete => new ProcessingIncomplete(incomplete.Processing),
            PackageHouseDependencyPruningResult.ProcessingUnavailable
                unavailable => new ProcessingUnavailable(
                    unavailable.Processing),
            PackageHouseDependencyPruningResult.ProcessingFailed failed =>
                new ProcessingFailed(failed.Processing),
            PackageHouseDependencyPruningResult.RuntimeProjected projected =>
                new RuntimeProjected(projected.Processing),
            PackageHouseDependencyPruningResult.PreviouslyEvaluated previous =>
                new PreviouslyEvaluated(previous.Processing),
            PackageHouseDependencyPruningResult.ProcessingNotEvidenced
                notEvidenced => new ProcessingNotEvidenced(
                    notEvidenced.Processing),
            PackageHouseDependencyPruningResult.TargetUnavailable unavailable =>
                new TargetUnavailable(unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown dependency pruning result."),
        };
}
