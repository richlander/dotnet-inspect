using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

public enum PackagePrefixWorkspaceScopeRejection
{
    ForeignWorkspace,
    StaleRegistrationRevision,
    RegistrationNotCurrent,
    CandidateLimitExceeded,
}

public enum PackagePrefixWorkspaceCandidateDisposition
{
    Existing,
    Admitted,
    PreparationFailed,
    CapacityDeclined,
    NotCommitted,
}

public sealed record PackagePrefixWorkspaceCandidateResult
{
    internal PackagePrefixWorkspaceCandidateResult(
        PackageQueryPackage package,
        PackagePrefixWorkspaceCandidateDisposition disposition,
        WorkspacePackageOccurrenceDescriptor? occurrence,
        ImmutableArray<WorkspaceContextLoadFailure> failures)
    {
        Package = package;
        Disposition = disposition;
        Occurrence = occurrence;
        Failures = failures;
    }

    public PackageQueryPackage Package { get; }

    public PackagePrefixWorkspaceCandidateDisposition Disposition { get; }

    public WorkspacePackageOccurrenceDescriptor? Occurrence { get; }

    public ImmutableArray<WorkspaceContextLoadFailure> Failures { get; }
}

public sealed record PackagePrefixWorkspaceScopeRealizationRequest
{
    public PackagePrefixWorkspaceScopeRealizationRequest(
        InspectionWorkspace workspace,
        WorkspaceRegistrationRevision registrationRevision,
        WorkspaceRegistration.PackagePrefix registration,
        WorkspaceScopeSnapshot scope,
        IPackageSourceClient source,
        PackagePrefixRequest prefixRequest,
        WorkspaceContextLoadOptions rootLoadOptions,
        string targetFramework,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(registrationRevision);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prefixRequest);
        ArgumentNullException.ThrowIfNull(rootLoadOptions);
        if (!RealizedMemberCoordinate.IsCanonicalFramework(targetFramework))
        {
            throw new ArgumentException(
                "The target framework must be one canonical lowercase "
                    + "acquisition moniker.",
                nameof(targetFramework));
        }
        if (!ReferenceEquals(registration.Prefix, prefixRequest.Declaration))
        {
            throw new ArgumentException(
                "The package-prefix request must apply policy to the selected "
                    + "registration declaration.",
                nameof(prefixRequest));
        }

        Workspace = workspace;
        RegistrationRevision = registrationRevision;
        Registration = registration;
        Scope = scope;
        Source = source;
        PrefixRequest = prefixRequest;
        RootLoadOptions = rootLoadOptions;
        TargetFramework = targetFramework;
        Deadline = deadline;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceRegistrationRevision RegistrationRevision { get; }

    public WorkspaceRegistration.PackagePrefix Registration { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public IPackageSourceClient Source { get; }

    public PackagePrefixRequest PrefixRequest { get; }

    public WorkspaceContextLoadOptions RootLoadOptions { get; }

    public string TargetFramework { get; }

    public DateTimeOffset Deadline { get; }
}

public abstract record PackagePrefixWorkspaceScopeRealizationOutcome
{
    private protected PackagePrefixWorkspaceScopeRealizationOutcome()
    {
    }

    public sealed record Rejected(
        PackagePrefixWorkspaceScopeRejection Reason)
        : PackagePrefixWorkspaceScopeRealizationOutcome;

    public sealed record Unavailable(
        ArtifactRootFailure RuntimeFailure)
        : PackagePrefixWorkspaceScopeRealizationOutcome;

    public sealed record Settled(
        PackageQueryDocument Query,
        ImmutableArray<PackagePrefixWorkspaceCandidateResult> Candidates,
        WorkspaceScopeOperationResult ScopeOperation)
        : PackagePrefixWorkspaceScopeRealizationOutcome
    {
        public bool IsComplete =>
            Query.Summary.Completion
                is PackageQueryCompletionKind.Exhausted
                    or PackageQueryCompletionKind.ExactPackageComplete
            && Query.Failures.IsEmpty
            && Candidates.All(static candidate =>
                candidate.Disposition
                    is PackagePrefixWorkspaceCandidateDisposition.Existing
                        or PackagePrefixWorkspaceCandidateDisposition.Admitted)
            && ScopeOperation
                is WorkspaceScopeOperationResult.Committed
                    or WorkspaceScopeOperationResult.NoEffect;
    }
}

public static class PackagePrefixWorkspaceScopeRealization
{
    public static InspectionQuery<
        PackagePrefixWorkspaceScopeRealizationOutcome> Definition { get; } =
        new(
            "Package-prefix Workspace Scope realization",
            InspectionCost.Unbounded);

    public static async ValueTask<
        PackagePrefixWorkspaceScopeRealizationOutcome> ExecuteAsync(
        PackagePrefixWorkspaceScopeRealizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(
                request.RegistrationRevision.Workspace,
                request.Workspace.Identity)
            || !ReferenceEquals(
                request.Scope.Revision.Workspace,
                request.Workspace.Identity))
        {
            return new PackagePrefixWorkspaceScopeRealizationOutcome.Rejected(
                PackagePrefixWorkspaceScopeRejection.ForeignWorkspace);
        }

        WorkspaceRegistrationReadResult registrationRead =
            request.Workspace.GetRegistrationSnapshot();
        if (registrationRead is WorkspaceRegistrationReadResult.Unavailable
                unavailable)
        {
            return new PackagePrefixWorkspaceScopeRealizationOutcome
                .Unavailable(unavailable.RuntimeFailure);
        }

        WorkspaceRegistrationRevision currentRegistration =
            ((WorkspaceRegistrationReadResult.Available)registrationRead)
                .Revision;
        if (!ReferenceEquals(
                currentRegistration.Identity,
                request.RegistrationRevision.Identity))
        {
            return new PackagePrefixWorkspaceScopeRealizationOutcome.Rejected(
                PackagePrefixWorkspaceScopeRejection
                    .StaleRegistrationRevision);
        }
        if (!currentRegistration.Registrations.Any(registration =>
                ReferenceEquals(registration, request.Registration)))
        {
            return new PackagePrefixWorkspaceScopeRealizationOutcome.Rejected(
                PackagePrefixWorkspaceScopeRejection.RegistrationNotCurrent);
        }
        if (request.PrefixRequest.MaxPackages
            > PackageQuery.MaximumCandidates)
        {
            return new PackagePrefixWorkspaceScopeRealizationOutcome.Rejected(
                PackagePrefixWorkspaceScopeRejection.CandidateLimitExceeded);
        }

        PackageQueryPlan plan =
            PackageQuery.PlanInput(
                request.PrefixRequest.Prefix + "*",
                maximumCandidates: request.PrefixRequest.MaxPackages,
                maximumMatches: null,
                includePrerelease:
                    request.PrefixRequest.IncludePrerelease)
            switch
            {
                PackageQueryPlanResult.Accepted accepted => accepted.Plan,
                PackageQueryPlanResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        "A retained package-prefix registration and its "
                            + "consumer policy did not produce a valid Package "
                            + $"Query plan: {rejected.Failure.Message}"),
                _ => throw new InvalidOperationException(
                    "Package Query returned an unknown planning result."),
            };

        PackageQueryDocument query =
            await ExecuteQueryAsync(
                    request.Source,
                    plan,
                    cancellationToken)
                .ConfigureAwait(false);

        int remainingCapacity =
            request.Scope.Revision.Limits.MaxPackages
            - request.Scope.Packages.Length;
        var candidates =
            ImmutableArray.CreateBuilder<CandidatePlan>(
                query.Results.Length);
        var roots = ImmutableArray.CreateBuilder<PackageRootBinding>();
        var seen = new HashSet<RealizedMemberCoordinate.Package>();

        foreach (PackageQueryMatch match in query.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RealizedMemberCoordinate.Package coordinate =
                CandidateCoordinate(
                    match.Package,
                    request.TargetFramework);
            if (!seen.Add(coordinate))
                continue;

            WorkspacePackageOccurrenceDescriptor? existing =
                request.Scope.Packages.FirstOrDefault(occurrence =>
                    Equals(
                        occurrence.Occurrence.Package.Coordinate,
                        coordinate));
            if (existing is not null)
            {
                candidates.Add(CandidatePlan.AlreadyPresent(
                    match.Package,
                    existing));
                continue;
            }

            if (roots.Count >= remainingCapacity)
            {
                candidates.Add(CandidatePlan.CapacityDeclined(
                    match.Package));
                continue;
            }

            WorkspacePackageRootAcquisitionOutcome acquisition =
                await WorkspaceContextLoader
                    .AcquireRealizedPackageRootAsync(
                        coordinate,
                        request.RootLoadOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            switch (acquisition)
            {
                case WorkspacePackageRootAcquisitionOutcome.Acquired
                    acquired:
                    if (ScopePreparationFailure(
                            coordinate,
                            acquired.Root)
                        is { } preparationFailure)
                    {
                        candidates.Add(CandidatePlan.PreparationFailed(
                            match.Package,
                            [preparationFailure]));
                        break;
                    }

                    roots.Add(acquired.Root);
                    candidates.Add(CandidatePlan.Prepared(
                        match.Package,
                        acquired.Root));
                    break;
                case WorkspacePackageRootAcquisitionOutcome.Failed failed:
                    candidates.Add(CandidatePlan.PreparationFailed(
                        match.Package,
                        failed.Failures));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Package Root acquisition returned an unknown result.");
            }
        }

        WorkspaceScopeOperationResult scopeOperation =
            await request.Workspace.AddPackagesAsync(
                    request.Scope.Revision,
                    request.Scope.PublicationBase,
                    roots.ToImmutable(),
                    request.Deadline,
                    cancellationToken)
                .ConfigureAwait(false);
        WorkspaceScopeSnapshot? finalScope = scopeOperation switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => null,
        };

        return new PackagePrefixWorkspaceScopeRealizationOutcome.Settled(
            query,
            [
                .. candidates.Select(candidate =>
                    candidate.Complete(finalScope)),
            ],
            scopeOperation);
    }

    static async ValueTask<PackageQueryDocument> ExecuteQueryAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        CancellationToken cancellationToken)
    {
        ImmutableArray<PackageQueryEvent> events =
            await PackageQuery.ExecuteToArrayAsync(
                    source,
                    plan,
                    cancellationToken)
                .ConfigureAwait(false);
        PackageQuerySummary summary =
            events.OfType<PackageQueryEvent.Completed>()
                .Single()
                .Value;
        return new(
            [
                .. events.OfType<PackageQueryEvent.Match>()
                    .Select(static match => match.Value),
            ],
            [
                .. events.OfType<PackageQueryEvent.Failure>()
                    .Select(static failure => failure.Value),
            ],
            summary);
    }

    static RealizedMemberCoordinate.Package CandidateCoordinate(
        PackageQueryPackage package,
        string targetFramework)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                package.PackageId,
                package.Version);
        return new(
            coordinate.PackageId,
            coordinate.Version,
            package.Source.Producer.PortableKey,
            targetFramework,
            runtimeIdentifier: null);
    }

    static WorkspaceContextLoadFailure? ScopePreparationFailure(
        RealizedMemberCoordinate.Package coordinate,
        PackageRootBinding root) =>
        root.Root.AssetSelection.Status switch
        {
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                new(
                    WorkspaceContextLoadFailureKind.PackageAssetUnavailable,
                    WorkspaceMemberCoordinate.Package(
                        coordinate.PackageId,
                        coordinate.Version),
                    "The candidate package carries no assembly assets for "
                        + "the requested target framework."),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                new(
                    WorkspaceContextLoadFailureKind.PackageAssetUnavailable,
                    WorkspaceMemberCoordinate.Package(
                        coordinate.PackageId,
                        coordinate.Version),
                    "The candidate package's implementation assets are "
                        + "invalid for Workspace Scope preparation."),
            _ => null,
        };

    sealed record CandidatePlan(
        PackageQueryPackage Package,
        PackagePrefixWorkspaceCandidateDisposition Disposition,
        WorkspacePackageOccurrenceDescriptor? ExistingOccurrence,
        PackageRootBinding? Root,
        ImmutableArray<WorkspaceContextLoadFailure> Failures)
    {
        internal static CandidatePlan AlreadyPresent(
            PackageQueryPackage package,
            WorkspacePackageOccurrenceDescriptor occurrence) =>
            new(
                package,
                PackagePrefixWorkspaceCandidateDisposition.Existing,
                occurrence,
                null,
                []);

        internal static CandidatePlan Prepared(
            PackageQueryPackage package,
            PackageRootBinding root) =>
            new(
                package,
                PackagePrefixWorkspaceCandidateDisposition.Admitted,
                null,
                root,
                []);

        internal static CandidatePlan PreparationFailed(
            PackageQueryPackage package,
            ImmutableArray<WorkspaceContextLoadFailure> failures) =>
            new(
                package,
                PackagePrefixWorkspaceCandidateDisposition.PreparationFailed,
                null,
                null,
                failures);

        internal static CandidatePlan CapacityDeclined(
            PackageQueryPackage package) =>
            new(
                package,
                PackagePrefixWorkspaceCandidateDisposition.CapacityDeclined,
                null,
                null,
                []);

        internal PackagePrefixWorkspaceCandidateResult Complete(
            WorkspaceScopeSnapshot? finalScope)
        {
            if (ExistingOccurrence is not null)
            {
                WorkspacePackageOccurrenceDescriptor? finalOccurrence =
                    finalScope?.Packages.FirstOrDefault(candidate =>
                        Equals(
                            candidate.Occurrence.Package.Coordinate,
                            ExistingOccurrence.Occurrence.Package.Coordinate));
                return new(
                    Package,
                    finalOccurrence is null
                        ? PackagePrefixWorkspaceCandidateDisposition
                            .NotCommitted
                        : PackagePrefixWorkspaceCandidateDisposition.Existing,
                    finalOccurrence,
                    []);
            }

            WorkspacePackageOccurrenceDescriptor? occurrence =
                Root is null || finalScope is null
                    ? null
                    : finalScope.Packages.FirstOrDefault(candidate =>
                        candidate.Occurrence.Package.Matches(Root));
            PackagePrefixWorkspaceCandidateDisposition disposition =
                Root is not null && occurrence is null
                    ? PackagePrefixWorkspaceCandidateDisposition.NotCommitted
                    : Disposition;
            return new(
                Package,
                disposition,
                occurrence,
                Failures);
        }
    }
}
