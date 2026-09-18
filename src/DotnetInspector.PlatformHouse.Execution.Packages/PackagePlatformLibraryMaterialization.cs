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
/// Result of materializing authoritative package-backed populations.
/// </summary>
public abstract class PackagePlatformPopulationMaterializationResult
{
    private protected PackagePlatformPopulationMaterializationResult(
        PlatformPopulationRealizationResult realization) =>
        Realization = realization;

    public PlatformPopulationRealizationResult Realization { get; }

    /// <summary>
    /// Transfers every Library owner and the adjacent Artifact authority only
    /// through complete population settlement.
    /// </summary>
    public sealed class Completed :
        PackagePlatformPopulationMaterializationResult
    {
        internal Completed(
            PlatformPopulationRealizationResult.Completed population,
            ArtifactSetSession artifacts)
            : base(population)
        {
            ArgumentNullException.ThrowIfNull(artifacts);
            Population = population;
            Artifacts = artifacts;
        }

        public PlatformPopulationRealizationResult.Completed Population
        {
            get;
        }

        public ArtifactSetSession Artifacts { get; }
    }

    /// <summary>
    /// Retains only resource-free terminal evidence after population cleanup.
    /// </summary>
    public sealed class Terminal :
        PackagePlatformPopulationMaterializationResult
    {
        internal Terminal(
            PlatformPopulationRealizationResult.Terminal realization)
            : base(realization) =>
            TerminalRealization = realization;

        public PlatformPopulationRealizationResult.Terminal
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
    private const string PopulationIdentityPrefix =
        "package-platform-population";

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

    /// <summary>
    /// Materializes one authoritative package-backed reference population.
    /// </summary>
    public static async ValueTask<
        PackagePlatformPopulationMaterializationResult>
        MaterializeReferencePopulationAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        bool prepared = TryPrepareReferencePopulation(
            request,
            PlatformViewDemand.Reference,
            reference,
            out IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeReferencesAsync(
                    request,
                    prepared ? items : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return ToPackagePopulationResult(outcome);
    }

    /// <summary>
    /// Materializes authoritative package-backed reference and implementation
    /// populations through PlatformHouse-issued view correspondence.
    /// </summary>
    public static async ValueTask<
        PackagePlatformPopulationMaterializationResult>
        MaterializeReferenceAndImplementationPopulationAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded reference,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        bool referencePrepared = TryPrepareReferencePopulation(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            out IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> references);
        bool implementationPrepared = TryPrepareImplementationPopulation(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            implementation,
            out IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> implementations);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeReferenceAndImplementationAsync(
                    request,
                    referencePrepared ? references : [],
                    implementationPrepared ? implementations : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return ToPackagePopulationResult(outcome);
    }

    /// <summary>
    /// Materializes one authoritative package-backed implementation
    /// population.
    /// </summary>
    public static async ValueTask<
        PackagePlatformPopulationMaterializationResult>
        MaterializeImplementationPopulationAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        bool prepared = TryPrepareImplementationPopulation(
            request,
            PlatformViewDemand.Implementation,
            implementation,
            out IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeImplementationsAsync(
                    request,
                    prepared ? items : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return ToPackagePopulationResult(outcome);
    }

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

            prepared.Add(ReferenceItem(reference, library!));
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

            prepared.Add(ImplementationItem(implementation, library));
        }

        items = prepared;
        return true;
    }

    static bool TryPrepareReferencePopulation(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        PackagePlatformHouseResult<
            PackageReferenceRealization>.Succeeded reference,
        out IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items)
    {
        items = [];
        if (expectedView is not PlatformViewDemand.Reference
                and not PlatformViewDemand.ReferenceAndImplementation
            || request.Target is not PlatformTargetDemand.Exact exact
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: var view,
                Population:
                    PlatformPopulationDemand.CompletePopulation,
            }
            || view != expectedView
            || !ValidContribution(
                request,
                exact.Target,
                reference.Contribution,
                PlatformSourceFacet.Reference,
                reference.Value.Generation.Name)
            || reference.Value.Coordinate.Target != exact.Target
            || reference.Value.Population
                is not PackageReferencePopulationDemand.CompletePopulation
            || reference.Value.Libraries.Length == 0)
        {
            return false;
        }

        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        var prepared =
            new List<PlatformLibraryArtifactMaterializationItem>(
                reference.Value.Libraries.Length);
        foreach (PackageReferenceLibrary library
            in reference.Value.Libraries)
        {
            if (!identities.Add(library.Identity))
                return false;
            prepared.Add(ReferenceItem(reference, library));
        }

        items = prepared;
        return true;
    }

    static bool TryPrepareImplementationPopulation(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded implementation,
        out IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items)
    {
        items = [];
        if (expectedView is not PlatformViewDemand.Implementation
                and not PlatformViewDemand.ReferenceAndImplementation
            || request.Target is not PlatformTargetDemand.Exact exact
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: var view,
                Population:
                    PlatformPopulationDemand.CompletePopulation,
            }
            || view != expectedView
            || !ValidContribution(
                request,
                exact.Target,
                implementation.Contribution,
                PlatformSourceFacet.Implementation,
                implementation.Value.Generation.Name)
            || implementation.Value.Coordinate.Target != exact.Target
            || implementation.Value.Libraries.Length == 0)
        {
            return false;
        }

        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        var prepared =
            new List<PlatformLibraryArtifactMaterializationItem>(
                implementation.Value.Libraries.Length);
        foreach (PackageImplementationLibrary library
            in implementation.Value.Libraries)
        {
            if (!string.Equals(
                    library.Framework.RuntimeIdentifier,
                    implementation.Value.Coordinate.RuntimeIdentifier,
                    StringComparison.Ordinal)
                || !identities.Add(library.Identity))
            {
                return false;
            }
            prepared.Add(ImplementationItem(implementation, library));
        }

        items = prepared;
        return true;
    }

    static PlatformLibraryArtifactMaterializationItem ReferenceItem(
        PackagePlatformHouseResult<
            PackageReferenceRealization>.Succeeded reference,
        PackageReferenceLibrary library) =>
        new(
            (PlatformSourceContribution.Realization)
                reference.Contribution,
            new PackageReferenceArtifactProvenance(
                reference.Value.Generation,
                reference.Value.Coordinate,
                library.Path,
                reference.Value.Candidate,
                reference.Value.Authority,
                reference.Value.Source,
                reference.Value.ContentGeneration,
                reference.Value.Origin,
                library.Identity),
            library.Identity,
            library.ContentLength,
            _ => library.OpenRead());

    static PlatformLibraryArtifactMaterializationItem ImplementationItem(
        PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded implementation,
        PackageImplementationLibrary library)
    {
        PackageImplementationFramework framework = library.Framework;
        return new(
            (PlatformSourceContribution.Realization)
                implementation.Contribution,
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
                library.ContentDigest),
            library.Identity,
            library.ContentLength,
            _ => library.OpenRead());
    }

    static PackagePlatformPopulationMaterializationResult
        ToPackagePopulationResult(
            PlatformPopulationArtifactMaterializationOutcome outcome) =>
        outcome switch
        {
            PlatformPopulationArtifactMaterializationOutcome.Completed
                completed =>
                new PackagePlatformPopulationMaterializationResult.Completed(
                    completed.Population,
                    completed.Artifacts),
            PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal =>
                new PackagePlatformPopulationMaterializationResult.Terminal(
                    terminal.TerminalRealization),
            _ => throw new InvalidOperationException(
                "Unknown Platform population Artifact materialization outcome."),
        };

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
