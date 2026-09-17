using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Formats;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Resource-free provenance for one package-backed reference assembly
/// snapshot.
/// </summary>
public sealed record PackageReferenceArtifactProvenance(
    PackagePlatformSourceGeneration SourceGeneration,
    PackageReferencePackCoordinate Coordinate,
    string Path,
    PackageAcquisitionCandidate Candidate,
    ConfiguredPackageAuthority Authority,
    PackageSourceResultIdentity Source,
    PackageContentGenerationIdentity ContentGeneration,
    PackagePayloadOrigin Origin,
    AssemblyReferenceIdentity Identity) : IArtifactProvenance;

/// <summary>
/// Resource-free provenance for one package-backed implementation assembly
/// snapshot.
/// </summary>
public sealed record PackageImplementationArtifactProvenance(
    PackagePlatformSourceGeneration SourceGeneration,
    PackageImplementationPlatformCoordinate Coordinate,
    PlatformFrameworkName FrameworkName,
    PlatformFamily FrameworkFamily,
    PlatformVersion FrameworkVersion,
    string PackageId,
    string RuntimeIdentifier,
    PlatformManifestAssetCoordinate ManifestCoordinate,
    PackageAcquisitionCandidate Candidate,
    ConfiguredPackageAuthority Authority,
    PackageSourceResultIdentity Source,
    PackageContentGenerationIdentity ContentGeneration,
    PackagePayloadOrigin Origin,
    AssemblyReferenceIdentity Identity,
    PackagePlatformContentDigest ContentDigest) : IArtifactProvenance;

/// <summary>
/// Result of materializing successful package-backed source values into one
/// Platform Library.
/// </summary>
public abstract class PackagePlatformLibraryMaterializationResult
{
    private protected PackagePlatformLibraryMaterializationResult(
        PlatformLibraryRealizationResult realization) =>
        Realization = realization;

    public PlatformLibraryRealizationResult Realization { get; }

    /// <summary>
    /// Transfers the Library owner and its adjacent Artifact authority
    /// separately.
    /// </summary>
    public sealed class Completed :
        PackagePlatformLibraryMaterializationResult
    {
        internal Completed(
            PlatformLibraryRealizationResult.Completed library,
            ArtifactSetSession artifacts)
            : base(library)
        {
            ArgumentNullException.ThrowIfNull(artifacts);
            if (library.Value.Reference.Contents.Any(
                    content => !ReferenceEquals(
                        content.ArtifactReference.Generation,
                        artifacts.Generation)))
            {
                throw new ArgumentException(
                    "Every realized Library content item must belong to the returned Artifact session.",
                    nameof(artifacts));
            }

            Library = library;
            Artifacts = artifacts;
        }

        public PlatformLibraryRealizationResult.Completed Library { get; }
        public ArtifactSetSession Artifacts { get; }
    }

    /// <summary>
    /// Retains only resource-free terminal evidence after composition cleanup.
    /// </summary>
    public sealed class Terminal :
        PackagePlatformLibraryMaterializationResult
    {
        internal Terminal(
            PlatformLibraryRealizationResult.Terminal realization)
            : base(realization) =>
            TerminalRealization = realization;

        public PlatformLibraryRealizationResult.Terminal
            TerminalRealization
        { get; }
    }
}

/// <summary>
/// Selects successful package-backed source snapshots for the shared Artifact
/// and exact one-Library ownership handoff.
/// </summary>
public static class PackagePlatformLibraryMaterializer
{
    private const string IdentityPrefix = "package-platform-library";

    /// <summary>
    /// Materializes one successful package-backed reference result.
    /// </summary>
    public static ValueTask<PackagePlatformLibraryMaterializationResult>
        MaterializeReferenceAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded reference,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Reference,
            reference,
            implementation: null,
            consumedWork);

    /// <summary>
    /// Materializes corresponding successful package-backed reference and
    /// implementation results.
    /// </summary>
    public static ValueTask<PackagePlatformLibraryMaterializationResult>
        MaterializeReferenceAndImplementationAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded reference,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            implementation,
            consumedWork);

    /// <summary>
    /// Materializes one successful package-backed implementation result in
    /// both Library roles.
    /// </summary>
    public static ValueTask<PackagePlatformLibraryMaterializationResult>
        MaterializeImplementationAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Implementation,
            reference: null,
            implementation,
            consumedWork);

    static async ValueTask<PackagePlatformLibraryMaterializationResult>
        MaterializeAsync(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded? reference,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded? implementation,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        bool prepared = TryPrepare(
                request,
                expectedView,
                reference,
                implementation,
                out IReadOnlyList<
                    PlatformLibraryArtifactMaterializationItem> items);

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseArtifactMaterializer.MaterializeAsync(
                    request,
                    expectedView,
                    prepared ? items : [],
                    consumedWork,
                    IdentityPrefix)
                .ConfigureAwait(false);
        return outcome switch
        {
            PlatformLibraryArtifactMaterializationOutcome.Completed completed =>
                new PackagePlatformLibraryMaterializationResult.Completed(
                    completed.Library,
                    completed.Artifacts),
            PlatformLibraryArtifactMaterializationOutcome.Terminal terminal =>
                new PackagePlatformLibraryMaterializationResult.Terminal(
                    terminal.TerminalRealization),
            _ => throw new InvalidOperationException(
                "Unknown Platform Library Artifact materialization outcome."),
        };
    }

    static bool TryPrepare(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        PackagePlatformHouseResult<
            PackageReferenceRealization>.Succeeded? reference,
        PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded? implementation,
        out IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items)
    {
        items = [];
        if (request.Target is not PlatformTargetDemand.Exact exact
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: var view,
                Population:
                        PlatformPopulationDemand.Library
                {
                    Value:
                                PlatformLibraryDemand.Assembly assembly,
                },
            }
            || view != expectedView
            || (expectedView is PlatformViewDemand.Reference
                    or PlatformViewDemand.ReferenceAndImplementation)
                != (reference is not null)
            || (expectedView is PlatformViewDemand.Implementation
                    or PlatformViewDemand.ReferenceAndImplementation)
                != (implementation is not null))
        {
            return false;
        }

        var prepared = new List<
            PlatformLibraryArtifactMaterializationItem>(2);
        if (reference is not null)
        {
            if (!ValidContribution(
                    request,
                    exact.Target,
                    reference.Contribution,
                    PlatformSourceFacet.Reference,
                    reference.Value.Generation.Name)
                || reference.Value.Coordinate.Target != exact.Target
                || reference.Value.Population
                    is not PackageReferencePopulationDemand.Assembly
                        population
                || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    assembly.Identity,
                    population.Identity)
                || !TrySingle(
                    reference.Value.Libraries,
                    library =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            assembly.Identity,
                            library.Identity),
                    out PackageReferenceLibrary? library))
            {
                return false;
            }

            var provenance =
                new PackageReferenceArtifactProvenance(
                    reference.Value.Generation,
                    reference.Value.Coordinate,
                    library!.Path,
                    reference.Value.Candidate,
                    reference.Value.Authority,
                    reference.Value.Source,
                    reference.Value.ContentGeneration,
                    reference.Value.Origin,
                    library.Identity);
            prepared.Add(
                new PlatformLibraryArtifactMaterializationItem(
                    (PlatformSourceContribution.Realization)
                        reference.Contribution,
                    provenance,
                    library.Identity,
                    library.ContentLength,
                    _ => library.OpenRead()));
        }

        if (implementation is not null)
        {
            if (!ValidContribution(
                    request,
                    exact.Target,
                    implementation.Contribution,
                    PlatformSourceFacet.Implementation,
                    implementation.Value.Generation.Name)
                || implementation.Value.Coordinate.Target != exact.Target
                || !TrySingle(
                    implementation.Value.Libraries,
                    library =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            assembly.Identity,
                            library.Identity),
                    out PackageImplementationLibrary? library)
                || !string.Equals(
                    library!.Framework.RuntimeIdentifier,
                    implementation.Value.Coordinate.RuntimeIdentifier,
                    StringComparison.Ordinal))
            {
                return false;
            }

            PackageImplementationFramework framework =
                library.Framework;
            var provenance =
                new PackageImplementationArtifactProvenance(
                    implementation.Value.Generation,
                    implementation.Value.Coordinate,
                    framework.Name,
                    framework.Family,
                    framework.Version,
                    framework.PackageId,
                    framework.RuntimeIdentifier,
                    library.ManifestCoordinate,
                    framework.Candidate,
                    framework.Authority,
                    framework.Source,
                    framework.ContentGeneration,
                    framework.Origin,
                    library.Identity,
                    library.ContentDigest);
            prepared.Add(
                new PlatformLibraryArtifactMaterializationItem(
                    (PlatformSourceContribution.Realization)
                        implementation.Contribution,
                    provenance,
                    library.Identity,
                    library.ContentLength,
                    _ => library.OpenRead()));
        }

        items = prepared;
        return true;
    }

    static bool ValidContribution(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSourceContribution contribution,
        PlatformSourceFacet expectedFacet,
        string sourceGenerationName) =>
        contribution is PlatformSourceContribution.Realization realization
        && realization.Facet == expectedFacet
        && realization.RealizationCompleteness
            == PlatformSourceContributionCompleteness.Authoritative
        && string.Equals(
            realization.Generation.Name,
            sourceGenerationName,
            StringComparison.Ordinal)
        && ReferenceEquals(realization.Request, request.Snapshot)
        && realization.Target == target
        && ReferenceEquals(
            realization.Population,
            ((PlatformHouseOperation.Realize)request.Operation).Population)
        && request.Sources.Authorizes(
            expectedFacet,
            realization.Capability);

    static bool TrySingle<T>(
        IEnumerable<T> values,
        Func<T, bool> predicate,
        out T? value)
        where T : class
    {
        value = null;
        foreach (T candidate in values)
        {
            if (!predicate(candidate))
                continue;
            if (value is not null)
                return false;
            value = candidate;
        }
        return value is not null;
    }

}
