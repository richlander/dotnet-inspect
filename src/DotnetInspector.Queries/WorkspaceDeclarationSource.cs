using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The source-specific request behind one searchable context.</summary>
public abstract record WorkspaceDeclarationRequest
{
    private protected WorkspaceDeclarationRequest() { }

    public sealed record ContextLoad(WorkspaceContextInput Input)
        : WorkspaceDeclarationRequest;

    public sealed record PlatformReference(
        PackageReferencePackCoordinate Coordinate,
        PackageReferencePopulationDemand Population)
        : WorkspaceDeclarationRequest;

    public sealed record PlatformPopulation(
        PlatformFamilyTarget Target)
        : WorkspaceDeclarationRequest;

    public sealed record PackageScope(
        WorkspacePackageOccurrenceDescriptor Occurrence)
        : WorkspaceDeclarationRequest;
}

/// <summary>Detached source evidence, not a re-acquisition or binding request.</summary>
public abstract record WorkspaceDeclarationOrigin
{
    private protected WorkspaceDeclarationOrigin() { }

    public sealed record ContextLoad(
        WorkspaceMemberCoordinate Declared, RealizedMemberCoordinate Realized)
        : WorkspaceDeclarationOrigin;

    public sealed record PlatformReference(
        WorkspaceReferenceSourceEvidence Source, string Path)
        : WorkspaceDeclarationOrigin;

    public sealed record PlatformPopulation(
        PlatformFamilyTarget Target,
        WorkspacePlatformPopulationMemberRole Role,
        string Producer,
        string Assembly)
        : WorkspaceDeclarationOrigin;

    public sealed record PackageScope(
        WorkspacePackageOccurrenceDescriptor Occurrence,
        PackageCompileAsset Asset)
        : WorkspaceDeclarationOrigin;
}

public enum WorkspacePlatformPopulationMemberRole
{
    Focus,
    BindingSupport,
}

/// <summary>
/// Resource-free evidence projected from a source-issued reference realization.
/// Source and content association tokens retain their owners' identity semantics.
/// </summary>
public sealed class WorkspaceReferenceSourceEvidence
{
    internal WorkspaceReferenceSourceEvidence(PackageReferenceRealization realization)
    {
        Coordinate = realization.Coordinate;
        Population = realization.Population;
        Generation = realization.Generation;
        ContentGeneration = realization.ContentGeneration;
        Source = realization.Source;
        Authority = PackageSourceDisplay.ForDiagnostics(realization.Authority.Source);
        Origin = realization.Origin;
        CandidateKind = realization.Candidate.Kind;
        DiscoveryContract = realization.Candidate.DiscoveryContract;
        ReportingObservations = [
            .. realization.Candidate.Authorities
                .Select(static authority => authority.Observation)
                .OfType<PackageCandidateObservation>()];
        PackageFailures = realization.PackageFailures;
    }

    public PackageReferencePackCoordinate Coordinate { get; }
    public PackageReferencePopulationDemand Population { get; }
    public PackagePlatformSourceGeneration Generation { get; }
    public PackageContentGenerationIdentity ContentGeneration { get; }
    public PackageSourceResultIdentity Source { get; }
    public InertString Authority { get; }
    public PackagePayloadOrigin Origin { get; }
    public PackageAcquisitionCandidateKind CandidateKind { get; }
    public PackageVersionDiscoveryContract? DiscoveryContract { get; }
    public ImmutableArray<PackageCandidateObservation> ReportingObservations { get; }
    public ImmutableArray<PackageAuthorityFailure> PackageFailures { get; }
}

public enum WorkspaceReferenceSourceFailureKind
{
    Unavailable,
    Rejected,
    Incomplete,
    Failed,
}

/// <summary>An attributed context gap, independent of inventory coverage.</summary>
public abstract record WorkspaceDeclarationFailure
{
    private protected WorkspaceDeclarationFailure() { }

    public sealed record ContextLoad(WorkspaceContextLoadFailure Failure)
        : WorkspaceDeclarationFailure;

    public sealed record ReferenceSource(
        PackagePlatformSourceGeneration Generation,
        WorkspaceReferenceSourceFailureKind Kind,
        PackagePlatformSourceDiagnostic Diagnostic)
        : WorkspaceDeclarationFailure;

    public sealed record ReferenceImage(
        WorkspaceReferenceSourceEvidence Source,
        string Path,
        CandidateOpenFailure Failure)
        : WorkspaceDeclarationFailure;

    public sealed record PackageScopeSelection(
        WorkspacePackageOccurrenceDescriptor Occurrence,
        PackageCompileAssetSelectionStatus Status)
        : WorkspaceDeclarationFailure;
}
