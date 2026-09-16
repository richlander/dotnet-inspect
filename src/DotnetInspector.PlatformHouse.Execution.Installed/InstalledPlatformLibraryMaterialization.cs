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
/// Publishes successful installed source snapshots and performs the shared
/// exact one-Library ownership handoff.
/// </summary>
public static class InstalledPlatformLibraryMaterializer
{
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

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    "installed-platform-library.work-incomplete"));
        }

        if (!TryPrepare(
                request,
                expectedView,
                reference,
                implementation,
                consumedWork,
                out PreparedMaterialization prepared))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    "installed-platform-library.invalid-source-result"));
        }

        if (prepared.ExceedsRequestBudget)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    "installed-platform-library.materialization-incomplete"));
        }

        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = prepared.Items.Count,
                MaxArtifactBytes = prepared.MaxContentLength,
                MaxRetainedBytes = prepared.TotalContentLength,
            });
        var contentLeases = new List<ArtifactContentLease>(
            prepared.Items.Count);
        ArtifactQueryLease? queryLease = null;
        try
        {
            var artifactIdentities = new List<ArtifactIdentity>(
                prepared.Items.Count);
            await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        var contributions =
                            new List<ArtifactContribution>(
                                prepared.Items.Count);
                        foreach (PreparedContent item in prepared.Items)
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new PlatformLibraryArtifactProvenance(
                                        item.Contribution,
                                        item.Provenance),
                                    item.OpenRead);
                            contributions.Add(contribution);
                            ArtifactIdentity identity =
                                contribution.Descriptor.Identity;
                            item.ArtifactIdentity = identity;
                            artifactIdentities.Add(identity);
                        }
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    contributions,
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: request.CancellationToken)
                .ConfigureAwait(false);

            var projections =
                new Dictionary<
                    ArtifactIdentity,
                    ArtifactAssemblyProjection>(
                        ReferenceEqualityComparer.Instance);
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, cancellationToken) =>
                            Project(
                                view,
                                prepared,
                                projections,
                                cancellationToken),
                        request.CancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                return Terminal(
                    PublicationFailure(
                        request,
                        consumedWork,
                        prepared,
                        notPublished));
            }

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            var selections =
                new List<PlatformLibraryContentSelection>(
                    prepared.Items.Count);
            for (int index = 0; index < prepared.Items.Count; index++)
            {
                ArtifactContentReference content =
                    session.GetContentReference(
                        artifactIdentities[index],
                        queryLease);
                selections.Add(
                    new PlatformLibraryContentSelection(
                        content,
                        projections[content.Artifact]));
                contentLeases.Add(
                    session.IssueContentLease(
                        content,
                        queryLease));
            }
            queryLease.Dispose();
            queryLease = null;

            PlatformLibraryRealizationResult realization =
                Realize(
                    request,
                    expectedView,
                    selections,
                    contentLeases,
                    consumedWork);
            if (realization
                is PlatformLibraryRealizationResult.Completed completed)
            {
                return new InstalledPlatformLibraryMaterializationResult
                    .Completed(completed, session);
            }

            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (cleanup.Count != 0)
            {
                return Terminal(
                    CleanupFailure(
                        request,
                        consumedWork,
                        prepared,
                        realization,
                        cancellationObserved: false));
            }

            return Terminal(
                (PlatformLibraryRealizationResult.Terminal)realization);
        }
        catch (OperationCanceledException)
            when (request.CancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (cleanup.Count != 0)
            {
                return Terminal(
                    PlatformHouseLibraryRealizer.Failed(
                        request,
                        consumedWork,
                        prepared.Contributions,
                        [PlatformHouseFailureKind.ArtifactRetirement],
                        cancellationObserved: true,
                        "installed-platform-library.cancellation-cleanup-failed"));
            }
            throw;
        }
        catch (Exception failure)
        {
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(failure, cleanup);
            throw;
        }
    }

    static ArtifactSetAdmissionFailure? Project(
        scoped ArtifactAdmissionContentView view,
        PreparedMaterialization prepared,
        IDictionary<ArtifactIdentity, ArtifactAssemblyProjection> projections,
        CancellationToken cancellationToken)
    {
        ArtifactAssemblyProjectionOutcome outcome =
            ArtifactAssemblyInspection.Project(
                view,
                cancellationToken);
        if (outcome
            is not ArtifactAssemblyProjectionOutcome.Projected projected)
        {
            return Failure(
                "installed-platform-library.metadata-rejected",
                "Metadata could not project one installed assembly snapshot.");
        }

        ArtifactIdentity artifact = view.Artifact;
        PreparedContent item = prepared.Items.Single(
            candidate => ReferenceEquals(
                candidate.ArtifactIdentity,
                artifact));
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                item.Identity,
                projected.Value.Identity))
        {
            return Failure(
                "installed-platform-library.identity-mismatch",
                "Metadata projected an identity different from the installed source identity.");
        }

        projections.Add(artifact, projected.Value);
        return null;
    }

    static PlatformLibraryRealizationResult Realize(
        PlatformHouseRequest request,
        PlatformViewDemand view,
        IReadOnlyList<PlatformLibraryContentSelection> selections,
        IReadOnlyList<ArtifactContentLease> leases,
        PlatformHouseConsumedWork consumedWork) =>
        view switch
        {
            PlatformViewDemand.Reference =>
                PlatformHouseLibraryRealizer.RealizeReference(
                    request,
                    selections[0],
                    leases[0],
                    consumedWork),
            PlatformViewDemand.ReferenceAndImplementation =>
                PlatformHouseLibraryRealizer
                    .RealizeReferenceAndImplementation(
                        request,
                        selections[0],
                        leases[0],
                        selections[1],
                        leases[1],
                        new PlatformLibraryViewCorrespondence(
                            selections[0],
                            selections[1],
                            "installed-platform-library-views"),
                        consumedWork),
            PlatformViewDemand.Implementation =>
                PlatformHouseLibraryRealizer.RealizeImplementation(
                    request,
                    selections[0],
                    leases[0],
                    PlatformLibraryViewCorrespondence
                        .CreateImplementationDeclarationSurface(
                            selections[0],
                            "installed-platform-library-declarations"),
                    consumedWork),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };

    static bool TryPrepare(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded? reference,
        InstalledPlatformHouseResult<
            InstalledImplementationRealization>.Succeeded? implementation,
        PlatformHouseConsumedWork consumedWork,
        out PreparedMaterialization prepared)
    {
        prepared = null!;
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

        var items = new List<PreparedContent>(2);
        if (reference is not null)
        {
            if (!ValidContribution(
                    request,
                    exact.Target,
                    reference.Contribution,
                    PlatformSourceFacet.Reference)
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
            items.Add(
                new PreparedContent(
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
                    PlatformSourceFacet.Implementation)
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
            items.Add(
                new PreparedContent(
                    (PlatformSourceContribution.Realization)
                        implementation.Contribution,
                    provenance,
                    library.Identity,
                    library.ContentLength,
                    _ => library.OpenRead()));
        }

        if (items.Count == 0
            || items.Any(static item => item.ContentLength <= 0)
            || consumedWork.SourceOperations < items.Count
            || consumedWork.Assemblies < items.Count)
        {
            return false;
        }

        long totalContentLength = items.Sum(
            static item => item.ContentLength);
        if (consumedWork.Bytes < totalContentLength)
            return false;

        prepared = new PreparedMaterialization(
            items,
            totalContentLength,
            items.Max(static item => item.ContentLength),
            items.Count > request.Work.MaxAssemblies
                || totalContentLength > request.Work.MaxBytes
                || totalContentLength > int.MaxValue);
        return true;
    }

    static bool ValidContribution(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSourceContribution contribution,
        PlatformSourceFacet expectedFacet) =>
        contribution is PlatformSourceContribution.Realization realization
        && realization.Facet == expectedFacet
        && realization.RealizationCompleteness
            == PlatformSourceContributionCompleteness.Authoritative
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

    static PlatformLibraryRealizationResult PublicationFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PreparedMaterialization prepared,
        ArtifactSetPublicationOutcome.NotPublished publication)
    {
        var failures = new List<PlatformHouseFailureKind>();
        if (publication.Failures.Any(
                failure => failure.Diagnostic.Code.StartsWith(
                    "installed-platform-library.metadata",
                    StringComparison.Ordinal)
                    || failure.Diagnostic.Code.EndsWith(
                        "identity-mismatch",
                        StringComparison.Ordinal)))
        {
            failures.Add(PlatformHouseFailureKind.Metadata);
        }
        else
        {
            failures.Add(PlatformHouseFailureKind.ArtifactPublication);
        }
        if (publication.CleanupFailures.Count != 0)
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);

        return PlatformHouseLibraryRealizer.Failed(
            request,
            consumedWork,
            prepared.Contributions,
            failures,
            cancellationObserved: false,
            "installed-platform-library.publication-failed");
    }

    static PlatformLibraryRealizationResult CleanupFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PreparedMaterialization prepared,
        PlatformLibraryRealizationResult primary,
        bool cancellationObserved)
    {
        var failures = new List<PlatformHouseFailureKind>();
        if (primary.Outcome
            is PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Failed failed)
        {
            failures.AddRange(failed.Evidence.Failures);
        }
        failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
        return PlatformHouseLibraryRealizer.Failed(
            request,
            consumedWork,
            prepared.Contributions,
            failures.Distinct(),
            cancellationObserved,
            "installed-platform-library.cleanup-failed");
    }

    static async ValueTask<IReadOnlyList<Exception>> CleanupAsync(
        ArtifactSetSession session,
        ArtifactQueryLease? queryLease,
        IEnumerable<ArtifactContentLease> contentLeases)
    {
        var failures = new List<Exception>();
        try
        {
            queryLease?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        foreach (ArtifactContentLease lease in contentLeases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(session.CleanupFailures);
        return failures;
    }

    static InstalledPlatformLibraryMaterializationResult.Terminal Terminal(
        PlatformLibraryRealizationResult result) =>
        new((PlatformLibraryRealizationResult.Terminal)result);

    static ArtifactSetAdmissionFailure Failure(
        string code,
        string summary) =>
        new(
            ArtifactSetAdmissionFailureKind.Failed,
            new MaterializationDiagnostic(code, summary));

    sealed record MaterializationDiagnostic(
        string Code,
        string Summary) : IArtifactAcquisitionDiagnostic;

    sealed class PreparedContent(
        PlatformSourceContribution.Realization contribution,
        IArtifactProvenance provenance,
        AssemblyReferenceIdentity identity,
        long contentLength,
        Func<CancellationToken, Stream> openRead)
    {
        internal PlatformSourceContribution.Realization Contribution { get; } =
            contribution;
        internal IArtifactProvenance Provenance { get; } = provenance;
        internal AssemblyReferenceIdentity Identity { get; } = identity;
        internal long ContentLength { get; } = contentLength;
        internal Func<CancellationToken, Stream> OpenRead { get; } = openRead;
        internal ArtifactIdentity? ArtifactIdentity { get; set; }
    }

    sealed class PreparedMaterialization(
        IReadOnlyList<PreparedContent> items,
        long totalContentLength,
        long maxContentLength,
        bool exceedsRequestBudget)
    {
        internal IReadOnlyList<PreparedContent> Items { get; } = items;
        internal long TotalContentLength { get; } = totalContentLength;
        internal int MaxContentLength { get; } = checked((int)maxContentLength);
        internal bool ExceedsRequestBudget { get; } = exceedsRequestBudget;
        internal IEnumerable<PlatformSourceContribution> Contributions =>
            Items.Select(static item => item.Contribution);
    }
}
