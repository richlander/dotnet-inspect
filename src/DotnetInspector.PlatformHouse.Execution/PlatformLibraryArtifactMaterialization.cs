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
        PlatformTargetSelectionContext? targetSelection,
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
        PlatformFamilyTarget? target = request.Target switch
        {
            PlatformTargetDemand.Exact exact
                when targetSelection is null => exact.Target,
            PlatformTargetDemand.FamilyDefault demand
                when targetSelection is not null
                    && ReferenceEquals(
                        targetSelection.TargetSettlement.Demand,
                        demand) =>
                targetSelection.Target,
            _ => null,
        };
        if (target is null
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
                || contribution.Target != target
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
        => await MaterializeCoreAsync(
                request,
                view,
                items,
                consumedWork,
                identityPrefix,
                targetSelection: null,
                retainedSettlements: null,
                currentWork: null,
                materializationCancellation:
                    request.CancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome>
        MaterializeSelectedAsync(
            PlatformHouseRequest request,
            PlatformViewDemand view,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement>
                retainedSettlements,
            Func<PlatformHouseConsumedWork> currentWork,
            CancellationToken materializationCancellation)
    {
        ArgumentNullException.ThrowIfNull(targetSelection);
        ArgumentNullException.ThrowIfNull(retainedSettlements);
        ArgumentNullException.ThrowIfNull(currentWork);
        return await MaterializeCoreAsync(
                request,
                view,
                items,
                consumedWork,
                identityPrefix,
                targetSelection,
                retainedSettlements,
                currentWork,
                materializationCancellation)
            .ConfigureAwait(false);
    }

    static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome> MaterializeCoreAsync(
            PlatformHouseRequest request,
            PlatformViewDemand view,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix,
            PlatformTargetSelectionContext? targetSelection,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements,
            Func<PlatformHouseConsumedWork>? currentWork,
            CancellationToken materializationCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);
        request.CancellationToken.ThrowIfCancellationRequested();

        PlatformHouseConsumedWork CurrentWork() =>
            currentWork?.Invoke() ?? consumedWork;

        consumedWork = CurrentWork();
        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.work-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }

        PlatformLibraryArtifactPreparationKind preparation =
            PlatformLibraryArtifactMaterializationPlan.TryCreate(
                request,
                view,
                consumedWork,
                items,
                targetSelection,
                out PlatformLibraryArtifactMaterializationPlan? plan);
        if (preparation == PlatformLibraryArtifactPreparationKind.Invalid)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-result",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }
        if (preparation == PlatformLibraryArtifactPreparationKind.Incomplete)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
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
                    cancellationToken:
                        materializationCancellation)
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
                        materializationCancellation)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                consumedWork = CurrentWork();
                return Terminal(
                    PublicationFailure(
                        request,
                        consumedWork,
                        preparedPlan,
                        notPublished,
                        identityPrefix,
                        targetSelection,
                        retainedSettlements));
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

            consumedWork = CurrentWork();
            if (PlatformHouseLibraryRealizer.ExceedsBudget(
                    consumedWork,
                    request))
            {
                return Terminal(
                    PlatformHouseLibraryRealizer.Incomplete(
                        request,
                        consumedWork,
                        $"{identityPrefix}.materialization-duration-incomplete",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements)));
            }

            PlatformLibraryRealizationResult realization =
                Realize(
                    request,
                    view,
                    selections,
                    compiledXmlDocumentation,
                    contentLeases,
                    consumedWork,
                    identityPrefix,
                    targetSelection,
                    retainedSettlements);
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
                        identityPrefix,
                        targetSelection,
                        retainedSettlements));
            }

            return Terminal(
                (PlatformLibraryRealizationResult.Terminal)realization);
        }
        catch (OperationCanceledException)
            when (request.CancellationToken.IsCancellationRequested)
        {
            consumedWork = CurrentWork();
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
                        $"{identityPrefix}.cancellation-cleanup-failed",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements)));
            }
            throw;
        }
        catch (OperationCanceledException)
            when (materializationCancellation.IsCancellationRequested)
        {
            consumedWork = CurrentWork();
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
                        cancellationObserved: false,
                        $"{identityPrefix}.duration-cleanup-failed",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements)));
            }
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-duration-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
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
        string identityPrefix,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements) =>
        targetSelection is null
            ? view switch
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
            }
            : view switch
        {
            PlatformViewDemand.Reference =>
                PlatformHouseLibraryRealizer
                    .RealizeSelectedReferenceWithCompanion(
                    request,
                    selections[0],
                    leases[0],
                    compiledXmlDocumentation,
                    compiledXmlDocumentation is null
                        ? null
                        : leases[^1],
                    consumedWork,
                    targetSelection,
                    retainedSettlements!),
            PlatformViewDemand.ReferenceAndImplementation =>
                PlatformHouseLibraryRealizer
                    .RealizeSelectedReferenceAndImplementationWithCompanion(
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
                        consumedWork,
                        targetSelection,
                        retainedSettlements!),
            PlatformViewDemand.Implementation =>
                PlatformHouseLibraryRealizer
                    .RealizeSelectedImplementation(
                    request,
                    selections[0],
                    leases[0],
                    PlatformLibraryViewCorrespondence
                        .CreateImplementationDeclarationSurface(
                            selections[0],
                            $"{identityPrefix}-declarations"),
                    consumedWork,
                    targetSelection,
                    retainedSettlements!),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };

    static PlatformLibraryRealizationResult PublicationFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformLibraryArtifactMaterializationPlan plan,
        ArtifactSetPublicationOutcome.NotPublished publication,
        string identityPrefix,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements)
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
            $"{identityPrefix}.publication-failed",
            targetSelection?.TargetSettlement,
            TerminalRetainedSettlements(
                targetSelection,
                retainedSettlements));
    }

    static PlatformLibraryRealizationResult CleanupFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformLibraryArtifactMaterializationPlan plan,
        PlatformLibraryRealizationResult primary,
        bool cancellationObserved,
        string identityPrefix,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements)
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
            $"{identityPrefix}.cleanup-failed",
            targetSelection?.TargetSettlement,
            TerminalRetainedSettlements(
                targetSelection,
                retainedSettlements));
    }

    static IReadOnlyList<PlatformSourceSettlement>?
        TerminalRetainedSettlements(
            PlatformTargetSelectionContext? targetSelection,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements) =>
        targetSelection is null || retainedSettlements is null
            ? null
            :
            [.. retainedSettlements.Select(
                settlement =>
                    settlement.Disposition
                            == PlatformSourceSettlementDisposition.Selected
                        && settlement.Contribution.Facet
                            != PlatformSourceFacet.TargetDiscovery
                        ? new PlatformSourceSettlement(
                            settlement.Contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant)
                        : settlement)];

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
