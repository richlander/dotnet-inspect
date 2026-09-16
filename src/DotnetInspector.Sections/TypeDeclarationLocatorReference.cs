using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>Association ordinals are local to one projected result, not acquisition handles.</summary>
public sealed record TypeDeclarationLocatorPackageSource(
    string Producer, int Association, PackageSourceKind TransportKind);

public sealed record TypeDeclarationLocatorPackageObservation(
    PackageSourceCoordinate Coordinate,
    TypeDeclarationLocatorPackageSource Source,
    PackageDiscoveryContract DiscoveryContract,
    PackageListingState ListingState);

public sealed record TypeDeclarationLocatorDiscoveryContract(
    int ContractVersion, bool IncludePrerelease, bool IncludeUnlisted, int? Limit);

public sealed record TypeDeclarationLocatorPackageSourceFailure(
    TypeDeclarationLocatorPackageSource Source,
    PackageSourceCapabilities Capability,
    PackageSourceCoordinate? Coordinate,
    PackageSourceFailureKind Kind,
    string Message);

public sealed record TypeDeclarationLocatorPackageFailure(
    string Authority,
    PackageAuthorityFailureKind Kind,
    string Message,
    TypeDeclarationLocatorPackageSourceFailure? SourceFailure,
    TypeDeclarationLocatorPackageSource? ResultSource,
    PackageSourceTimeout? Timeout,
    bool IsRequiredProducerUnavailable);

/// <summary>
/// Reference view evidence. A null RequestedAssembly denotes the complete source population.
/// ContentGeneration is a result-local association ordinal, not a portable version.
/// </summary>
public sealed record TypeDeclarationLocatorReferenceEvidence(
    PlatformFamily Family,
    string Version,
    string Framework,
    string PackageId,
    string Authority,
    AssemblyReferenceIdentity? RequestedAssembly,
    string Generation,
    int ContentGeneration,
    TypeDeclarationLocatorPackageSource Source,
    PackagePayloadOrigin Origin,
    PackageAcquisitionCandidateKind CandidateKind,
    TypeDeclarationLocatorDiscoveryContract? DiscoveryContract,
    ImmutableArray<TypeDeclarationLocatorPackageObservation> ReportingObservations,
    ImmutableArray<TypeDeclarationLocatorPackageFailure> PackageFailures);

/// <summary>Source-specific context gaps retained independently of result rows.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorContextFailure.ContextLoad), "context-load")]
[JsonDerivedType(typeof(TypeDeclarationLocatorContextFailure.ReferenceSource), "reference-source")]
[JsonDerivedType(typeof(TypeDeclarationLocatorContextFailure.ReferenceImage), "reference-image")]
[JsonDerivedType(typeof(TypeDeclarationLocatorContextFailure.PackageScopeSelection), "package-scope-selection")]
public abstract record TypeDeclarationLocatorContextFailure
{
    private protected TypeDeclarationLocatorContextFailure(string message) => Message = message;

    public string Message { get; }

    public sealed record ContextLoad(
        WorkspaceContextLoadFailureKind Code,
        string Message,
        MetadataRootMalformedReason? MetadataRootReason)
        : TypeDeclarationLocatorContextFailure(Message);

    public sealed record ReferenceSource(
        string Generation,
        WorkspaceReferenceSourceFailureKind Outcome,
        PackagePlatformSourceDiagnosticKind Code,
        string Message,
        ImmutableArray<TypeDeclarationLocatorPackageFailure> PackageFailures)
        : TypeDeclarationLocatorContextFailure(Message);

    public sealed record ReferenceImage(
        TypeDeclarationLocatorReferenceEvidence Source,
        string Path,
        CandidateOpenFailure Failure)
        : TypeDeclarationLocatorContextFailure(
            Failure.Detail ?? $"Reference image admission failed: {Failure.Kind}.");

    public sealed record PackageScopeSelection(
        string PackageId,
        string PackageVersion,
        string Producer,
        string? Framework,
        string? RuntimeIdentifier,
        PackageCompileAssetSelectionStatus Status)
        : TypeDeclarationLocatorContextFailure(
            $"Package Scope has no searchable surface assemblies: {Status}.");
}

internal sealed class TypeDeclarationLocatorReferenceProjection
{
    readonly Dictionary<PackageContentGenerationIdentity, int> _contentGenerations = [];
    readonly Dictionary<PackageSourceAssociation, int> _sourceAssociations = [];
    readonly Dictionary<WorkspaceReferenceSourceEvidence, TypeDeclarationLocatorReferenceEvidence>
        _evidence = [];

    internal TypeDeclarationLocatorReferenceEvidence Evidence(WorkspaceReferenceSourceEvidence evidence)
    {
        if (_evidence.TryGetValue(evidence, out var existing))
            return existing;
        var target = evidence.Coordinate.Target;
        TypeDeclarationLocatorReferenceEvidence projected = new(
            target.Family, target.Version.Value, target.TargetFramework.ToString(),
            evidence.Coordinate.PackageId,
            evidence.Authority.ToString(),
            evidence.Population switch
            {
                PackageReferencePopulationDemand.Assembly assembly => assembly.Identity,
                PackageReferencePopulationDemand.CompletePopulation => null,
                _ => throw new InvalidOperationException("Unknown reference population demand."),
            },
            evidence.Generation.Name,
            Ordinal(_contentGenerations, evidence.ContentGeneration),
            Source(evidence.Source), evidence.Origin, evidence.CandidateKind,
            evidence.DiscoveryContract is { } discovery
                ? new(discovery.ContractVersion, discovery.IncludePrerelease,
                    discovery.IncludeUnlisted, discovery.Limit)
                : null,
            [.. evidence.ReportingObservations.Select(observation =>
                new TypeDeclarationLocatorPackageObservation(
                    observation.Coordinate, Source(observation.Source),
                    observation.DiscoveryContract, observation.ListingState))],
            [.. evidence.PackageFailures.Select(PackageFailure)]);
        _evidence.Add(evidence, projected);
        return projected;
    }

    internal TypeDeclarationLocatorContextFailure Failure(WorkspaceDeclarationFailure failure) =>
        failure switch
        {
            WorkspaceDeclarationFailure.ContextLoad load =>
                new TypeDeclarationLocatorContextFailure.ContextLoad(
                    load.Failure.Kind, load.Failure.Message, load.Failure.MetadataRootReason),
            WorkspaceDeclarationFailure.ReferenceSource source =>
                new TypeDeclarationLocatorContextFailure.ReferenceSource(
                    source.Generation.Name, source.Kind,
                    source.Diagnostic.Kind, source.Diagnostic.Summary,
                    [.. source.Diagnostic.PackageFailures.Select(PackageFailure)]),
            WorkspaceDeclarationFailure.ReferenceImage image =>
                new TypeDeclarationLocatorContextFailure.ReferenceImage(
                    Evidence(image.Source), image.Path, image.Failure),
            WorkspaceDeclarationFailure.PackageScopeSelection package =>
                PackageScopeFailure(package),
            _ => throw new InvalidOperationException("Unknown declaration context failure."),
        };

    static TypeDeclarationLocatorContextFailure.PackageScopeSelection
        PackageScopeFailure(
            WorkspaceDeclarationFailure.PackageScopeSelection failure)
    {
        RealizedMemberCoordinate.Package package =
            failure.Occurrence.Occurrence.Package.Coordinate;
        return new(
            package.PackageId,
            package.Version,
            package.Producer,
            package.Framework,
            package.RuntimeIdentifier,
            failure.Status);
    }

    TypeDeclarationLocatorPackageSource Source(PackageSourceResultIdentity source) =>
        new(source.Producer.Key, Ordinal(_sourceAssociations, source.Association), source.TransportKind);

    TypeDeclarationLocatorPackageFailure PackageFailure(PackageAuthorityFailure failure) =>
        new(failure.Authority.ToString(), failure.Kind, failure.Message,
            failure.SourceFailure is { } source
                ? new(Source(source.Source), source.Capability, source.Coordinate, source.Kind, source.Message)
                : null,
            failure.ResultSource is { } result ? Source(result) : null,
            failure.Timeout, failure.IsRequiredProducerUnavailable);

    static int Ordinal<T>(Dictionary<T, int> ordinals, T identity) where T : notnull
    {
        if (ordinals.TryGetValue(identity, out int ordinal))
            return ordinal;
        ordinal = ordinals.Count + 1;
        ordinals.Add(identity, ordinal);
        return ordinal;
    }
}
