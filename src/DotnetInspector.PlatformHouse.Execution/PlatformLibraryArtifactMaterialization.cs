using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

internal enum PlatformLibraryArtifactPreparationKind
{
    Prepared,
    Invalid,
    Incomplete,
}

/// <summary>
/// One source-prepared immutable companion snapshot for the common Platform
/// Artifact and Library handoff.
/// </summary>
public sealed class PlatformLibraryArtifactCompanionMaterializationItem
{
    public PlatformLibraryArtifactCompanionMaterializationItem(
        IArtifactProvenance provenance,
        long contentLength,
        Func<CancellationToken, Stream> openRead)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ArgumentNullException.ThrowIfNull(openRead);
        Provenance = provenance;
        ContentLength = contentLength;
        OpenRead = openRead;
    }

    internal IArtifactProvenance Provenance { get; }
    internal long ContentLength { get; }
    internal Func<CancellationToken, Stream> OpenRead { get; }
    internal ArtifactIdentity? ArtifactIdentity { get; set; }
}

/// <summary>
/// One source-prepared immutable assembly snapshot for the common Platform
/// Artifact and Library handoff.
/// </summary>
public sealed class PlatformLibraryArtifactMaterializationItem
{
    public PlatformLibraryArtifactMaterializationItem(
        PlatformSourceContribution.Realization contribution,
        IArtifactProvenance provenance,
        AssemblyReferenceIdentity identity,
        long contentLength,
        Func<CancellationToken, Stream> openRead,
        PlatformLibraryArtifactCompanionMaterializationItem?
            compiledXmlDocumentation = null)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ArgumentNullException.ThrowIfNull(openRead);
        Contribution = contribution;
        Provenance = provenance;
        Identity = identity;
        ContentLength = contentLength;
        OpenRead = openRead;
        CompiledXmlDocumentation =
            compiledXmlDocumentation;
    }

    internal PlatformSourceContribution.Realization Contribution { get; }
    internal IArtifactProvenance Provenance { get; }
    internal AssemblyReferenceIdentity Identity { get; }
    internal long ContentLength { get; }
    internal Func<CancellationToken, Stream> OpenRead { get; }
    internal PlatformLibraryArtifactCompanionMaterializationItem?
        CompiledXmlDocumentation { get; }
    internal ArtifactIdentity? ArtifactIdentity { get; set; }
}

/// <summary>
/// One source-prepared assembly snapshot and its exact population membership.
/// </summary>
public sealed class PlatformPopulationLibraryArtifactMaterializationItem
{
    public PlatformPopulationLibraryArtifactMaterializationItem(
        PlatformLibraryArtifactMaterializationItem library,
        PlatformPopulationMemberAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(attribution);
        Library = library;
        Attribution = attribution;
    }

    internal PlatformLibraryArtifactMaterializationItem Library { get; }
    internal PlatformPopulationMemberAttribution Attribution { get; }
}

internal sealed class PlatformLibraryArtifactMaterializationPlan
{
    private PlatformLibraryArtifactMaterializationPlan(
        IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items,
        long totalContentLength,
        int maxContentLength)
    {
        Items = items;
        TotalContentLength = totalContentLength;
        MaxContentLength = maxContentLength;
        ArtifactCount = items.Count
            + items.Count(
                static item =>
                    item.CompiledXmlDocumentation is not null);
    }

    internal IReadOnlyList<PlatformLibraryArtifactMaterializationItem> Items
    {
        get;
    }

    internal long TotalContentLength { get; }
    internal int MaxContentLength { get; }
    internal int ArtifactCount { get; }
    internal IEnumerable<PlatformSourceContribution> Contributions =>
        Items.Select(static item => item.Contribution);

    internal static PlatformLibraryArtifactPreparationKind TryCreate(
        PlatformHouseRequest request,
        PlatformViewDemand view,
        PlatformHouseConsumedWork consumedWork,
        IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items,
        out PlatformLibraryArtifactMaterializationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentNullException.ThrowIfNull(items);
        plan = null;

        PlatformSourceFacet[] expectedFacets = view switch
        {
            PlatformViewDemand.Reference =>
                [PlatformSourceFacet.Reference],
            PlatformViewDemand.ReferenceAndImplementation =>
                [
                    PlatformSourceFacet.Reference,
                    PlatformSourceFacet.Implementation,
                ],
            PlatformViewDemand.Implementation =>
                [PlatformSourceFacet.Implementation],
            _ => [],
        };
        if (request.Target is not PlatformTargetDemand.Exact exact
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: var requestView,
                Population: PlatformPopulationDemand.Library,
            } operation
            || requestView != view
            || expectedFacets.Length == 0
            || items.Count != expectedFacets.Length
            || items.Any(static item => item.ContentLength <= 0)
            || consumedWork.SourceOperations < items.Count
            || consumedWork.Assemblies < items.Count)
        {
            return PlatformLibraryArtifactPreparationKind.Invalid;
        }

        for (int index = 0; index < items.Count; index++)
        {
            PlatformSourceContribution.Realization contribution =
                items[index].Contribution;
            PlatformSourceFacet expectedFacet = expectedFacets[index];
            if (contribution.Facet != expectedFacet
                || contribution.RealizationCompleteness
                    != PlatformSourceContributionCompleteness.Authoritative
                || !ReferenceEquals(
                    contribution.Request,
                    request.Snapshot)
                || contribution.Target != exact.Target
                || !ReferenceEquals(
                    contribution.Population,
                    operation.Population)
                || !request.Sources.Authorizes(
                    expectedFacet,
                    contribution.Capability))
            {
                return PlatformLibraryArtifactPreparationKind.Invalid;
            }
        }

        bool compiledXmlRequested =
            operation.ContentDemand.HasFlag(
                PlatformLibraryContentDemand
                    .CompiledXmlDocumentation);
        int compiledXmlCount = items.Count(
            static item =>
                item.CompiledXmlDocumentation is not null);
        if ((!compiledXmlRequested && compiledXmlCount != 0)
            || compiledXmlCount > 1
            || consumedWork.XmlDocuments < compiledXmlCount
            || items.Any(
                item =>
                    item.CompiledXmlDocumentation is not null
                    && item.Contribution.Facet
                        != PlatformSourceFacet.Reference))
        {
            return PlatformLibraryArtifactPreparationKind.Invalid;
        }

        long totalContentLength;
        try
        {
            totalContentLength = checked(items.Sum(
                static item =>
                    item.ContentLength
                    + (item.CompiledXmlDocumentation?.ContentLength
                        ?? 0)));
        }
        catch (OverflowException)
        {
            return PlatformLibraryArtifactPreparationKind.Incomplete;
        }

        if (consumedWork.Bytes < totalContentLength)
            return PlatformLibraryArtifactPreparationKind.Invalid;
        if (items.Count > request.Work.MaxAssemblies
            || totalContentLength > request.Work.MaxBytes
            || totalContentLength > int.MaxValue)
        {
            return PlatformLibraryArtifactPreparationKind.Incomplete;
        }

        plan = new(
            items,
            totalContentLength,
            checked(
                (int)items.Max(
                    static item => Math.Max(
                        item.ContentLength,
                        item.CompiledXmlDocumentation
                            ?.ContentLength
                            ?? 0))));
        return PlatformLibraryArtifactPreparationKind.Prepared;
    }
}

/// <summary>
/// Common source-neutral Artifact publication and one-Library handoff
/// outcome.
/// </summary>
public abstract class PlatformLibraryArtifactMaterializationOutcome
{
    private protected PlatformLibraryArtifactMaterializationOutcome(
        PlatformLibraryRealizationResult realization) =>
        Realization = realization;

    public PlatformLibraryRealizationResult Realization { get; }

    public sealed class Completed :
        PlatformLibraryArtifactMaterializationOutcome
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

    public sealed class Terminal :
        PlatformLibraryArtifactMaterializationOutcome
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
/// Publishes source-prepared immutable snapshots and performs the shared exact
/// one-Library ownership handoff.
/// </summary>
public static class PlatformHouseArtifactMaterializer
{
    public static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome> MaterializeAsync(
            PlatformHouseRequest request,
            PlatformViewDemand view,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.work-incomplete"));
        }

        PlatformLibraryArtifactPreparationKind preparation =
            PlatformLibraryArtifactMaterializationPlan.TryCreate(
                request,
                view,
                consumedWork,
                items,
                out PlatformLibraryArtifactMaterializationPlan? plan);
        if (preparation == PlatformLibraryArtifactPreparationKind.Invalid)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-result"));
        }
        if (preparation == PlatformLibraryArtifactPreparationKind.Incomplete)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-incomplete"));
        }

        PlatformLibraryArtifactMaterializationPlan preparedPlan = plan!;
        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = preparedPlan.ArtifactCount,
                MaxArtifactBytes = preparedPlan.MaxContentLength,
                MaxRetainedBytes = preparedPlan.TotalContentLength,
            });
        var contentLeases = new List<ArtifactContentLease>(
            preparedPlan.ArtifactCount);
        ArtifactQueryLease? queryLease = null;
        try
        {
            var artifactIdentities = new List<ArtifactIdentity>(
                preparedPlan.Items.Count);
            await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        var contributions =
                            new List<ArtifactContribution>(
                                preparedPlan.Items.Count);
                        foreach (
                            PlatformLibraryArtifactMaterializationItem item
                            in preparedPlan.Items)
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
                        foreach (
                            PlatformLibraryArtifactMaterializationItem item
                            in preparedPlan.Items)
                        {
                            PlatformLibraryArtifactCompanionMaterializationItem?
                                documentation =
                                    item.CompiledXmlDocumentation;
                            if (documentation is null)
                                continue;

                            ArtifactContribution contribution =
                                scope.Register(
                                    new PlatformLibraryArtifactProvenance(
                                        item.Contribution,
                                        documentation.Provenance),
                                    documentation.OpenRead);
                            contributions.Add(contribution);
                            documentation.ArtifactIdentity =
                                contribution.Descriptor.Identity;
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
                                preparedPlan,
                                projections,
                                identityPrefix,
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
                        preparedPlan,
                        notPublished,
                        identityPrefix));
            }

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            var selections =
                new List<PlatformLibraryContentSelection>(
                    preparedPlan.Items.Count);
            for (int index = 0;
                index < preparedPlan.Items.Count;
                index++)
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
            ArtifactContentReference? compiledXmlDocumentation =
                null;
            PlatformLibraryArtifactCompanionMaterializationItem?
                documentationItem =
                    preparedPlan.Items
                        .Select(
                            static item =>
                                item.CompiledXmlDocumentation)
                        .SingleOrDefault(
                            static item => item is not null);
            if (documentationItem is not null)
            {
                compiledXmlDocumentation =
                    session.GetContentReference(
                        documentationItem.ArtifactIdentity!,
                        queryLease);
                contentLeases.Add(
                    session.IssueContentLease(
                        compiledXmlDocumentation,
                        queryLease));
            }
            queryLease.Dispose();
            queryLease = null;

            PlatformLibraryRealizationResult realization =
                Realize(
                    request,
                    view,
                    selections,
                    compiledXmlDocumentation,
                    contentLeases,
                    consumedWork,
                    identityPrefix);
            if (realization
                is PlatformLibraryRealizationResult.Completed completed)
            {
                return new PlatformLibraryArtifactMaterializationOutcome
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
                        preparedPlan,
                        realization,
                        cancellationObserved: false,
                        identityPrefix));
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
                        preparedPlan.Contributions,
                        [PlatformHouseFailureKind.ArtifactRetirement],
                        cancellationObserved: true,
                        $"{identityPrefix}.cancellation-cleanup-failed"));
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
        PlatformLibraryArtifactMaterializationPlan plan,
        IDictionary<ArtifactIdentity, ArtifactAssemblyProjection> projections,
        string identityPrefix,
        CancellationToken cancellationToken)
    {
        ArtifactIdentity artifact = view.Artifact;
        if (plan.Items.Any(
                item => ReferenceEquals(
                    item.CompiledXmlDocumentation
                        ?.ArtifactIdentity,
                    artifact)))
        {
            return null;
        }

        ArtifactAssemblyProjectionOutcome outcome =
            ArtifactAssemblyInspection.Project(
                view,
                cancellationToken);
        if (outcome
            is not ArtifactAssemblyProjectionOutcome.Projected projected)
        {
            return Failure(
                $"{identityPrefix}.metadata-rejected",
                "Metadata could not project one Platform assembly snapshot.");
        }

        PlatformLibraryArtifactMaterializationItem item =
            plan.Items.Single(
                candidate => ReferenceEquals(
                    candidate.ArtifactIdentity,
                    artifact));
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                item.Identity,
                projected.Value.Identity))
        {
            return Failure(
                $"{identityPrefix}.identity-mismatch",
                "Metadata projected an identity different from the Platform source identity.");
        }

        projections.Add(artifact, projected.Value);
        return null;
    }

    static PlatformLibraryRealizationResult Realize(
        PlatformHouseRequest request,
        PlatformViewDemand view,
        IReadOnlyList<PlatformLibraryContentSelection> selections,
        ArtifactContentReference? compiledXmlDocumentation,
        IReadOnlyList<ArtifactContentLease> leases,
        PlatformHouseConsumedWork consumedWork,
        string identityPrefix) =>
        view switch
        {
            PlatformViewDemand.Reference =>
                PlatformHouseLibraryRealizer
                    .RealizeReferenceWithCompanion(
                    request,
                    selections[0],
                    leases[0],
                    compiledXmlDocumentation,
                    compiledXmlDocumentation is null
                        ? null
                        : leases[^1],
                    consumedWork),
            PlatformViewDemand.ReferenceAndImplementation =>
                PlatformHouseLibraryRealizer
                    .RealizeReferenceAndImplementationWithCompanion(
                        request,
                        selections[0],
                        leases[0],
                        selections[1],
                        leases[1],
                        compiledXmlDocumentation,
                        compiledXmlDocumentation is null
                            ? null
                            : leases[^1],
                        new PlatformLibraryViewCorrespondence(
                            selections[0],
                            selections[1],
                            $"{identityPrefix}-views"),
                        consumedWork),
            PlatformViewDemand.Implementation =>
                PlatformHouseLibraryRealizer.RealizeImplementation(
                    request,
                    selections[0],
                    leases[0],
                    PlatformLibraryViewCorrespondence
                        .CreateImplementationDeclarationSurface(
                            selections[0],
                            $"{identityPrefix}-declarations"),
                    consumedWork),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };

    static PlatformLibraryRealizationResult PublicationFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformLibraryArtifactMaterializationPlan plan,
        ArtifactSetPublicationOutcome.NotPublished publication,
        string identityPrefix)
    {
        var failures = new List<PlatformHouseFailureKind>();
        if (publication.Failures.Any(
                failure => failure.Diagnostic.Code.StartsWith(
                    $"{identityPrefix}.metadata",
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
            plan.Contributions,
            failures,
            cancellationObserved: false,
            $"{identityPrefix}.publication-failed");
    }

    static PlatformLibraryRealizationResult CleanupFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformLibraryArtifactMaterializationPlan plan,
        PlatformLibraryRealizationResult primary,
        bool cancellationObserved,
        string identityPrefix)
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
            plan.Contributions,
            failures.Distinct(),
            cancellationObserved,
            $"{identityPrefix}.cleanup-failed");
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

    static PlatformLibraryArtifactMaterializationOutcome.Terminal Terminal(
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
}
