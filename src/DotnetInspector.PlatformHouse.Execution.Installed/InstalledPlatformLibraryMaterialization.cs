using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Formats;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Resource-free provenance for one installed reference assembly snapshot.
/// </summary>
public sealed record InstalledReferenceArtifactProvenance(
    InstalledPlatformSourceGeneration SourceGeneration,
    InstalledReferencePackCoordinate Coordinate,
    string FileName,
    AssemblyReferenceIdentity Identity) : IArtifactProvenance;

/// <summary>
/// Resource-free provenance for one installed implementation assembly snapshot.
/// </summary>
public sealed record InstalledImplementationArtifactProvenance(
    InstalledPlatformSourceGeneration SourceGeneration,
    InstalledImplementationPlatformCoordinate Coordinate,
    PlatformFrameworkName FrameworkName,
    PlatformVersion FrameworkVersion,
    PlatformManifestAssetCoordinate ManifestCoordinate,
    AssemblyReferenceIdentity Identity,
    InstalledPlatformContentDigest ContentDigest) : IArtifactProvenance;

/// <summary>
/// Result of materializing successful installed source values into one
/// Platform Library.
/// </summary>
public abstract class InstalledPlatformLibraryMaterializationResult
{
    private protected InstalledPlatformLibraryMaterializationResult(
        PlatformLibraryRealizationResult realization) =>
        Realization = realization;

    public PlatformLibraryRealizationResult Realization { get; }

    /// <summary>
    /// Transfers the Library owner and its adjacent Artifact authority
    /// separately.
    /// </summary>
    public sealed class Completed :
        InstalledPlatformLibraryMaterializationResult
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
        InstalledPlatformLibraryMaterializationResult
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
/// Selects successful installed source snapshots for the shared Artifact and
/// exact one-Library ownership handoff.
/// </summary>
public static class InstalledPlatformLibraryMaterializer
{
    private const string IdentityPrefix = "installed-platform-library";

    /// <summary>Materializes one successful installed reference result.</summary>
    public static ValueTask<InstalledPlatformLibraryMaterializationResult>
        MaterializeReferenceAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Reference,
            reference,
            implementation: null,
            consumedWork);

    /// <summary>
    /// Materializes corresponding successful installed reference and
    /// implementation results.
    /// </summary>
    public static ValueTask<InstalledPlatformLibraryMaterializationResult>
        MaterializeReferenceAndImplementationAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            implementation,
            consumedWork);

    /// <summary>
    /// Materializes one successful installed implementation result in both
    /// Library roles.
    /// </summary>
    public static ValueTask<InstalledPlatformLibraryMaterializationResult>
        MaterializeImplementationAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded implementation,
            PlatformHouseConsumedWork consumedWork) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Implementation,
            reference: null,
            implementation,
            consumedWork);

    static async ValueTask<InstalledPlatformLibraryMaterializationResult>
        MaterializeAsync(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded? reference,
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded? implementation,
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
                new InstalledPlatformLibraryMaterializationResult.Completed(
                    completed.Library,
                    completed.Artifacts),
            PlatformLibraryArtifactMaterializationOutcome.Terminal terminal =>
                new InstalledPlatformLibraryMaterializationResult.Terminal(
                    terminal.TerminalRealization),
            _ => throw new InvalidOperationException(
                "Unknown Platform Library Artifact materialization outcome."),
        };
    }

    static bool TryPrepare(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded? reference,
        InstalledPlatformHouseResult<
            InstalledImplementationRealization>.Succeeded? implementation,
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
                || !ReferenceTargetMatches(
                    reference.Value,
                    exact.Target)
                || !TrySingle(
                    reference.Value.Libraries,
                    library =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            assembly.Identity,
                            library.Identity),
                    out InstalledReferenceLibrary? library))
            {
                return false;
            }

            var provenance =
                new InstalledReferenceArtifactProvenance(
                    reference.Value.Generation,
                    reference.Value.Coordinate,
                    library!.FileName,
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
                || !ImplementationTargetMatches(
                    implementation.Value,
                    exact.Target)
                || !TrySingle(
                    implementation.Value.Libraries,
                    library =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            assembly.Identity,
                            library.Identity),
                    out InstalledImplementationLibrary? library))
            {
                return false;
            }

            var provenance =
                new InstalledImplementationArtifactProvenance(
                    implementation.Value.Generation,
                    implementation.Value.Coordinate,
                    library!.FrameworkName,
                    library.FrameworkVersion,
                    library.ManifestCoordinate,
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

    static bool ReferenceTargetMatches(
        InstalledReferenceRealization realization,
        PlatformFamilyTarget target) =>
        TryInstalledFamily(target.Family, out InstalledPlatformFamily family)
        && realization.Coordinate.Family == family
        && realization.Coordinate.TargetFramework == target.TargetFramework
        && realization.Coordinate.Version == target.Version;

    static bool ImplementationTargetMatches(
        InstalledImplementationRealization realization,
        PlatformFamilyTarget target) =>
        TryInstalledFamily(target.Family, out InstalledPlatformFamily family)
        && realization.Coordinate.Family == family
        && realization.Coordinate.Version == target.Version;

    static bool TryInstalledFamily(
        PlatformFamily family,
        out InstalledPlatformFamily installed)
    {
        switch (family)
        {
            case PlatformFamily.DotNetRuntime:
                installed = InstalledPlatformFamily.DotNetRuntime;
                return true;
            case PlatformFamily.AspNetCore:
                installed = InstalledPlatformFamily.AspNetCore;
                return true;
            default:
                installed = default;
                return false;
        }
    }

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
