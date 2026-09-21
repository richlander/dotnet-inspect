using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// Resource-free provenance for one installed reference compiled-XML
/// companion snapshot.
/// </summary>
public sealed record InstalledReferenceDocumentationArtifactProvenance(
    InstalledPlatformSourceGeneration SourceGeneration,
    InstalledReferencePackCoordinate Coordinate,
    string FileName,
    AssemblyReferenceIdentity AssociatedAssemblyIdentity) :
    IArtifactProvenance;

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
    private const string PopulationIdentityPrefix =
        "installed-platform-population";

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

    /// <summary>
    /// Materializes one authoritative installed reference population.
    /// </summary>
    public static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferencePopulationAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
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
                PlatformPopulationLibraryArtifactMaterializationItem>
                    items);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeReferencesAsync(
                    request,
                    prepared ? items : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Materializes authoritative installed reference and implementation
    /// populations through PlatformHouse-issued view correspondence.
    /// </summary>
    public static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferenceAndImplementationPopulationAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded implementation,
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
                PlatformPopulationLibraryArtifactMaterializationItem>
                    references);
        bool implementationPrepared = TryPrepareImplementationPopulation(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            implementation,
            out IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                    implementations);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeReferenceAndImplementationAsync(
                    request,
                    referencePrepared ? references : [],
                    implementationPrepared ? implementations : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Materializes one authoritative installed implementation population.
    /// </summary>
    public static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeImplementationPopulationAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded implementation,
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
                PlatformPopulationLibraryArtifactMaterializationItem>
                    items);
        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHousePopulationArtifactMaterializer
                .MaterializeImplementationsAsync(
                    request,
                    prepared ? items : [],
                    consumedWork,
                    PopulationIdentityPrefix)
                .ConfigureAwait(false);
        return outcome;
    }

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

            prepared.Add(
                CreateReferenceItem(
                    reference,
                    library!));
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

            prepared.Add(
                CreateImplementationItem(
                    implementation,
                    library!));
        }

        items = prepared;
        return true;
    }

    internal static bool TryPrepareSelectedReference(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded reference,
        out PlatformLibraryArtifactMaterializationItem? item)
    {
        item = null;
        if (request.Operation is not PlatformHouseOperation.Realize
            {
                Population:
                    PlatformPopulationDemand.Library
                    {
                        Value:
                            PlatformLibraryDemand.Assembly assembly,
                    },
            }
            || !ValidContribution(
                request,
                target,
                reference.Contribution,
                PlatformSourceFacet.Reference,
                reference.Value.Generation.Name)
            || !ReferenceTargetMatches(reference.Value, target)
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

        item = CreateReferenceItem(reference, library!);
        return true;
    }

    internal static bool TryPrepareSelectedImplementation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<
            InstalledImplementationRealization>.Succeeded implementation,
        out PlatformLibraryArtifactMaterializationItem? item)
    {
        item = null;
        if (request.Operation is not PlatformHouseOperation.Realize
            {
                Population:
                    PlatformPopulationDemand.Library
                    {
                        Value:
                            PlatformLibraryDemand.Assembly assembly,
                    },
            }
            || !ValidContribution(
                request,
                target,
                implementation.Contribution,
                PlatformSourceFacet.Implementation,
                implementation.Value.Generation.Name)
            || !ImplementationTargetMatches(
                implementation.Value,
                target)
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

        item = CreateImplementationItem(implementation, library!);
        return true;
    }

    static bool TryPrepareReferencePopulation(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded reference,
        out IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> items)
    {
        if (request.Target is not PlatformTargetDemand.Exact exact)
        {
            items = [];
            return false;
        }
        return TryPrepareReferencePopulation(
            request,
            exact.Target,
            expectedView,
            reference,
            items: out items);
    }

    internal static bool TryPrepareSelectedReferencePopulation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded reference,
        out IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> items)
    {
        if (request.Target is not PlatformTargetDemand.FamilyDefault demand
            || demand.Family != target.Family)
        {
            items = [];
            return false;
        }
        return TryPrepareReferencePopulation(
            request,
            target,
            PlatformViewDemand.Reference,
            reference,
            items: out items);
    }

    static bool TryPrepareReferencePopulation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformViewDemand expectedView,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded reference,
        out IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> items)
    {
        items = [];
        if (expectedView is not PlatformViewDemand.Reference
                and not PlatformViewDemand.ReferenceAndImplementation
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: var view,
                Population:
                    PlatformPopulationDemand.CompletePopulation,
            }
            || view != expectedView
            || !ValidContribution(
                request,
                target,
                reference.Contribution,
                PlatformSourceFacet.Reference,
                reference.Value.Generation.Name)
            || !ReferenceTargetMatches(reference.Value, target)
            || reference.Value.Population
                is not InstalledReferencePopulationDemand.CompletePopulation
            || reference.Value.Libraries.Count == 0
            || reference.Value.Libraries.Any(
                static library => library.Documentation is not null))
        {
            return false;
        }

        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        var prepared =
            new List<
                PlatformPopulationLibraryArtifactMaterializationItem>(
                reference.Value.Libraries.Count);
        foreach (InstalledReferenceLibrary library
            in reference.Value.Libraries)
        {
            if (!identities.Add(library.Identity))
                return false;
            prepared.Add(
                new PlatformPopulationLibraryArtifactMaterializationItem(
                    CreateReferenceItem(
                        reference,
                        library),
                    new PlatformPopulationMemberAttribution(
                        target,
                        PlatformPopulationMemberRole.Focus)));
        }

        items = prepared;
        return true;
    }

    internal static PlatformLibraryArtifactMaterializationItem
        CreateReferenceItem(
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
            InstalledReferenceLibrary library) =>
        new(
            (PlatformSourceContribution.Realization)
                reference.Contribution,
            new InstalledReferenceArtifactProvenance(
                reference.Value.Generation,
                reference.Value.Coordinate,
                library.FileName,
                library.Identity),
            library.Identity,
            library.ContentLength,
            _ => library.OpenRead(),
            library.Documentation is not { } documentation
                ? null
                : new(
                    new InstalledReferenceDocumentationArtifactProvenance(
                        reference.Value.Generation,
                        reference.Value.Coordinate,
                        documentation.FileName,
                        library.Identity),
                    documentation.ContentLength,
                    _ => documentation.OpenRead()));

    static PlatformLibraryArtifactMaterializationItem
        CreateImplementationItem(
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded implementation,
            InstalledImplementationLibrary library)
    {
        var provenance =
            new InstalledImplementationArtifactProvenance(
                implementation.Value.Generation,
                implementation.Value.Coordinate,
                library.FrameworkName,
                library.FrameworkVersion,
                library.ManifestCoordinate,
                library.Identity,
                library.ContentDigest);
        return new PlatformLibraryArtifactMaterializationItem(
            (PlatformSourceContribution.Realization)
                implementation.Contribution,
            provenance,
            library.Identity,
            library.ContentLength,
            _ => library.OpenRead());
    }

    static bool TryPrepareImplementationPopulation(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        InstalledPlatformHouseResult<
            InstalledImplementationRealization>.Succeeded implementation,
        out IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> items)
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
            || !ImplementationTargetMatches(
                implementation.Value,
                exact.Target)
            || implementation.Value.Libraries.Count == 0)
        {
            return false;
        }

        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        var prepared =
            new List<
                PlatformPopulationLibraryArtifactMaterializationItem>(
                implementation.Value.Libraries.Count);
        foreach (InstalledImplementationLibrary library
            in implementation.Value.Libraries)
        {
            if (!identities.Add(library.Identity)
                || !TryPopulationAttribution(
                    exact.Target,
                    implementation.Value,
                    library,
                    out PlatformPopulationMemberAttribution attribution))
            {
                return false;
            }
            prepared.Add(
                new PlatformPopulationLibraryArtifactMaterializationItem(
                    new PlatformLibraryArtifactMaterializationItem(
                        (PlatformSourceContribution.Realization)
                            implementation.Contribution,
                        new InstalledImplementationArtifactProvenance(
                            implementation.Value.Generation,
                            implementation.Value.Coordinate,
                            library.FrameworkName,
                            library.FrameworkVersion,
                            library.ManifestCoordinate,
                            library.Identity,
                            library.ContentDigest),
                        library.Identity,
                        library.ContentLength,
                        _ => library.OpenRead()),
                    attribution));
        }

        items = prepared;
        return true;
    }

    static bool TryPopulationAttribution(
        PlatformFamilyTarget requestedTarget,
        InstalledImplementationRealization realization,
        InstalledImplementationLibrary library,
        out PlatformPopulationMemberAttribution attribution)
    {
        if (!TrySingle(
                realization.Frameworks,
                candidate =>
                    candidate.Name.Equals(library.FrameworkName)
                    && candidate.Version == library.FrameworkVersion,
                out InstalledImplementationFramework? framework)
            || framework.Family is not { } family)
        {
            attribution = null!;
            return false;
        }

        PlatformPopulationMemberRole role;
        if (family == requestedTarget.Family)
        {
            role = PlatformPopulationMemberRole.Focus;
        }
        else if (requestedTarget.Family == PlatformFamily.AspNetCore
            && family == PlatformFamily.DotNetRuntime)
        {
            role = PlatformPopulationMemberRole.BindingSupport;
        }
        else
        {
            attribution = null!;
            return false;
        }

        if (!TryTargetFramework(
                library.FrameworkVersion,
                out PlatformTargetFramework? targetFramework))
        {
            attribution = null!;
            return false;
        }

        attribution = new PlatformPopulationMemberAttribution(
            new PlatformFamilyTarget(
                family,
                targetFramework,
                library.FrameworkVersion),
            role);
        return true;
    }

    static bool TryTargetFramework(
        PlatformVersion version,
        [NotNullWhen(true)] out PlatformTargetFramework? targetFramework)
    {
        string prefix = version.Major >= 5 ? "net" : "netcoreapp";
        string value = string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{version.Major}.{version.Minor}");
        return PlatformTargetFramework.TryParse(value, out targetFramework);
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
        [NotNullWhen(true)] out T? value)
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
