using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Common source-neutral Artifact publication and complete population handoff
/// outcome.
/// </summary>
public abstract class PlatformPopulationArtifactMaterializationOutcome
{
    private protected PlatformPopulationArtifactMaterializationOutcome(
        PlatformPopulationRealizationResult realization) =>
        Realization = realization;

    public PlatformPopulationRealizationResult Realization { get; }

    public sealed class Completed :
        PlatformPopulationArtifactMaterializationOutcome
    {
        internal Completed(
            PlatformPopulationRealizationResult.Completed population,
            ArtifactSetSession artifacts)
            : base(population)
        {
            ArgumentNullException.ThrowIfNull(artifacts);
            if (population.Value.Libraries
                .SelectMany(static library => library.Contents)
                .Any(
                    content => !ReferenceEquals(
                        content.ArtifactReference.Generation,
                        artifacts.Generation)))
            {
                throw new ArgumentException(
                    "Every realized population content item must belong to the returned Artifact session.",
                    nameof(artifacts));
            }

            Population = population;
            Artifacts = artifacts;
        }

        public PlatformPopulationRealizationResult.Completed Population
        {
            get;
        }

        public ArtifactSetSession Artifacts { get; }
    }

    public sealed class Terminal :
        PlatformPopulationArtifactMaterializationOutcome
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
/// Publishes one or two authoritative population views and performs their
/// atomic Library ownership handoff.
/// </summary>
public static class PlatformHousePopulationArtifactMaterializer
{
    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferencesAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Reference,
            items,
            [],
            consumedWork,
            identityPrefix);

    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferenceAndImplementationAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> references,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> implementations,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            references,
            implementations,
            consumedWork,
            identityPrefix);

    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeImplementationsAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeAsync(
            request,
            PlatformViewDemand.Implementation,
            [],
            items,
            consumedWork,
            identityPrefix);

    static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeAsync(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> references,
            IReadOnlyList<
                PlatformLibraryArtifactMaterializationItem> implementations,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(implementations);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Terminal(
                Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.work-incomplete"));
        }

        PopulationMaterializationPlan? plan =
            PopulationMaterializationPlan.TryCreate(
                request,
                expectedView,
                references,
                implementations,
                consumedWork);
        if (plan is null)
        {
            return Terminal(
                Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-result"));
        }
        if (plan.ExceedsMaterializationBudget)
        {
            return Terminal(
                Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-incomplete"));
        }

        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = plan.Items.Count,
                MaxArtifactBytes = plan.MaxContentLength,
                MaxRetainedBytes = plan.TotalContentLength,
            });
        var contentLeases = new List<ArtifactContentLease>(
            plan.Items.Count);
        ArtifactQueryLease? queryLease = null;
        try
        {
            var artifactIdentities = new List<ArtifactIdentity>(
                plan.Items.Count);
            await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        var contributions =
                            new List<ArtifactContribution>(
                                plan.Items.Count);
                        foreach (
                            PlatformLibraryArtifactMaterializationItem item
                            in plan.Items)
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new PlatformLibraryArtifactProvenance(
                                        item.Contribution,
                                        item.Provenance),
                                    item.OpenRead);
                            contributions.Add(contribution);
                            item.ArtifactIdentity =
                                contribution.Descriptor.Identity;
                            artifactIdentities.Add(
                                contribution.Descriptor.Identity);
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
                                plan,
                                projections,
                                identityPrefix,
                                cancellationToken),
                        request.CancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                PlatformPopulationRealizationResult failure =
                    PublicationFailure(
                        request,
                        consumedWork,
                        plan,
                        notPublished,
                        identityPrefix);
                IReadOnlyList<Exception> cleanup =
                    await CleanupAsync(
                            session,
                            queryLease,
                            contentLeases)
                        .ConfigureAwait(false);
                if (cleanup.Count != 0)
                {
                    failure = CleanupFailure(
                        request,
                        consumedWork,
                        plan,
                        failure,
                        cancellationObserved: false,
                        identityPrefix);
                }
                return Terminal(failure);
            }

            queryLease = session.IssueLease(
                session.CreateQueryAuthorization());
            var selections =
                new List<PlatformPopulationLibraryContentSelection>(
                    plan.Items.Count);
            for (int index = 0; index < plan.Items.Count; index++)
            {
                ArtifactContentReference content =
                    session.GetContentReference(
                        artifactIdentities[index],
                        queryLease);
                selections.Add(
                    new PlatformPopulationLibraryContentSelection(
                        content,
                        projections[content.Artifact]));
                contentLeases.Add(
                    session.IssueContentLease(
                        content,
                        queryLease));
            }
            queryLease.Dispose();
            queryLease = null;

            PlatformPopulationRealizationResult realization =
                await RealizePopulationAsync(
                        request,
                        expectedView,
                        selections,
                        contentLeases,
                        consumedWork,
                        plan.ReferenceCount)
                    .ConfigureAwait(false);
            if (realization
                is PlatformPopulationRealizationResult.Completed completed)
            {
                return new PlatformPopulationArtifactMaterializationOutcome
                    .Completed(completed, session);
            }

            static ValueTask<PlatformPopulationRealizationResult>
                RealizePopulationAsync(
                    PlatformHouseRequest request,
                    PlatformViewDemand expectedView,
                    IReadOnlyList<PlatformPopulationLibraryContentSelection>
                        selections,
                    IReadOnlyList<ArtifactContentLease> contentLeases,
                    PlatformHouseConsumedWork consumedWork,
                    int referenceCount) =>
                expectedView switch
                {
                    PlatformViewDemand.Reference =>
                        PlatformHousePopulationRealizer.RealizeReferencesAsync(
                            request,
                            selections,
                            contentLeases,
                            consumedWork),
                    PlatformViewDemand.ReferenceAndImplementation =>
                        PlatformHousePopulationRealizer
                            .RealizeReferenceAndImplementationAsync(
                                request,
                                selections.Take(referenceCount).ToArray(),
                                contentLeases.Take(referenceCount).ToArray(),
                                selections.Skip(referenceCount).ToArray(),
                                contentLeases.Skip(referenceCount).ToArray(),
                                consumedWork),
                    PlatformViewDemand.Implementation =>
                        PlatformHousePopulationRealizer.RealizeImplementationsAsync(
                            request,
                            selections,
                            contentLeases,
                            consumedWork),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(expectedView)),
                };

            IReadOnlyList<Exception> terminalCleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (terminalCleanup.Count != 0)
            {
                return Terminal(
                    CleanupFailure(
                        request,
                        consumedWork,
                        plan,
                        realization,
                        cancellationObserved: false,
                        identityPrefix));
            }
            return Terminal(realization);
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
                    PlatformHousePopulationRealizer.Failed(
                        request,
                        consumedWork,
                        plan.Contributions,
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
        PopulationMaterializationPlan plan,
        IDictionary<ArtifactIdentity, ArtifactAssemblyProjection> projections,
        string identityPrefix,
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
                $"{identityPrefix}.metadata-rejected",
                "Metadata could not project one Platform population assembly.");
        }

        ArtifactIdentity artifact = view.Artifact;
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

    static PlatformPopulationRealizationResult PublicationFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PopulationMaterializationPlan plan,
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

        return PlatformHousePopulationRealizer.Failed(
            request,
            consumedWork,
            plan.Contributions,
            failures,
            cancellationObserved: false,
            $"{identityPrefix}.publication-failed");
    }

    static PlatformPopulationRealizationResult CleanupFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PopulationMaterializationPlan plan,
        PlatformPopulationRealizationResult primary,
        bool cancellationObserved,
        string identityPrefix)
    {
        var failures = new List<PlatformHouseFailureKind>();
        if (primary.Outcome
            is PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Failed failed)
        {
            failures.AddRange(failed.Evidence.Failures);
        }
        failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
        return PlatformHousePopulationRealizer.Failed(
            request,
            consumedWork,
            plan.Contributions,
            failures.Distinct(),
            cancellationObserved,
            $"{identityPrefix}.cleanup-failed");
    }

    static PlatformPopulationRealizationResult Rejected(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseRejectionKind kind,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformPopulationRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected(
                    termination,
                    receipt),
            new PlatformPopulationRealizationReceipt(receipt));
    }

    static PlatformPopulationRealizationResult Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformPopulationRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete(
                    termination,
                    receipt),
            new PlatformPopulationRealizationReceipt(receipt));
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

    static PlatformPopulationArtifactMaterializationOutcome.Terminal Terminal(
        PlatformPopulationRealizationResult result) =>
        new((PlatformPopulationRealizationResult.Terminal)result);

    static ArtifactSetAdmissionFailure Failure(
        string code,
        string summary) =>
        new(
            ArtifactSetAdmissionFailureKind.Failed,
            new MaterializationDiagnostic(code, summary));

    sealed record MaterializationDiagnostic(
        string Code,
        string Summary) : IArtifactAcquisitionDiagnostic;

    sealed class PopulationMaterializationPlan
    {
        private PopulationMaterializationPlan(
            IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items,
            int referenceCount,
            long totalContentLength,
            int maxContentLength,
            bool exceedsMaterializationBudget)
        {
            Items = items;
            ReferenceCount = referenceCount;
            TotalContentLength = totalContentLength;
            MaxContentLength = maxContentLength;
            ExceedsMaterializationBudget =
                exceedsMaterializationBudget;
        }

        internal IReadOnlyList<
            PlatformLibraryArtifactMaterializationItem> Items
        { get; }

        internal int ReferenceCount { get; }
        internal long TotalContentLength { get; }
        internal int MaxContentLength { get; }
        internal bool ExceedsMaterializationBudget { get; }
        internal IEnumerable<PlatformSourceContribution> Contributions =>
            Items.Select(static item => item.Contribution)
                .Distinct<PlatformSourceContribution.Realization>(
                    ReferenceEqualityComparer.Instance);

        internal static PopulationMaterializationPlan? TryCreate(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            IReadOnlyList<PlatformLibraryArtifactMaterializationItem>
                references,
            IReadOnlyList<PlatformLibraryArtifactMaterializationItem>
                implementations,
            PlatformHouseConsumedWork consumedWork)
        {
            PlatformLibraryArtifactMaterializationItem[] items =
                [.. references, .. implementations];
            bool validShape = expectedView switch
            {
                PlatformViewDemand.Reference =>
                    references.Count != 0 && implementations.Count == 0,
                PlatformViewDemand.Implementation =>
                    references.Count == 0 && implementations.Count != 0,
                PlatformViewDemand.ReferenceAndImplementation =>
                    references.Count != 0 && implementations.Count != 0,
                _ => false,
            };
            if (request.Target is not PlatformTargetDemand.Exact exact
                || request.Operation is not PlatformHouseOperation.Realize
                {
                    View: var view,
                    Population:
                        PlatformPopulationDemand.CompletePopulation,
                } operation
                || view != expectedView
                || !validShape
                || items.Any(static item => item.ContentLength <= 0)
                || consumedWork.Assemblies < items.Length)
            {
                return null;
            }

            if (!ValidFacet(
                    PlatformSourceFacet.Reference,
                    references,
                    request,
                    exact.Target,
                    operation.Population)
                || !ValidFacet(
                    PlatformSourceFacet.Implementation,
                    implementations,
                    request,
                    exact.Target,
                    operation.Population))
                return null;

            int sourceOperations = items.Select(
                    static item => item.Contribution)
                .Distinct<PlatformSourceContribution.Realization>(
                    ReferenceEqualityComparer.Instance)
                .Count();
            if (consumedWork.SourceOperations < sourceOperations)
                return null;

            long totalContentLength;
            try
            {
                totalContentLength = checked(items.Sum(
                    static item => item.ContentLength));
            }
            catch (OverflowException)
            {
                return new(
                    items,
                    references.Count,
                    long.MaxValue,
                    int.MaxValue,
                    exceedsMaterializationBudget: true);
            }
            if (consumedWork.Bytes < totalContentLength)
                return null;

            bool exceedsBudget =
                items.Length > request.Work.MaxAssemblies
                || totalContentLength > request.Work.MaxBytes
                || totalContentLength > int.MaxValue
                || items.Any(
                    static item => item.ContentLength > int.MaxValue);
            int maxContentLength = exceedsBudget
                ? 0
                : checked((int)items.Max(
                    static item => item.ContentLength));
            return new(
                items,
                references.Count,
                totalContentLength,
                maxContentLength,
                exceedsBudget);
        }

        static bool ValidFacet(
            PlatformSourceFacet expectedFacet,
            IReadOnlyList<PlatformLibraryArtifactMaterializationItem> items,
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformPopulationDemand population)
        {
            var identities = new HashSet<AssemblyReferenceIdentity>(
                AssemblyReferenceIdentity.EquivalentComparer);
            PlatformSourceContribution.Realization? populationContribution =
                items.Count == 0
                    ? null
                    : items[0].Contribution;
            foreach (
                PlatformLibraryArtifactMaterializationItem item
                in items)
            {
                PlatformSourceContribution.Realization contribution =
                    item.Contribution;
                if (!ReferenceEquals(
                        contribution,
                        populationContribution)
                    || contribution.Facet != expectedFacet
                    || contribution.RealizationCompleteness
                        != PlatformSourceContributionCompleteness
                            .Authoritative
                    || !ReferenceEquals(
                        contribution.Request,
                        request.Snapshot)
                    || contribution.Target != target
                    || !ReferenceEquals(
                        contribution.Population,
                        population)
                    || !request.Sources.Authorizes(
                        expectedFacet,
                        contribution.Capability)
                    || !identities.Add(item.Identity))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
