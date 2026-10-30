using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Catalog;

internal sealed record BrowserIndexedTypeFindInspection(
    InspectionEnvelope<EcosystemFindIndexedIntegrationResult.Type> Find,
    ImmutableArray<BrowserIndexedTypeFindActivationAssociation> Activations);

internal sealed record BrowserIndexedTypeFindActivationAssociation(
    int MatchOrdinal,
    string? Identity,
    BrowserTypeFindActivationStatus Status,
    string? Action,
    string? Reason);

internal sealed class BrowserIndexedTypeFindPackageRequest;

internal sealed class BrowserIndexedTypeFindLibraryIntent;

internal sealed record BrowserIndexedTypeFindCapturedAction(
    PackageQueryEcosystemAdmission Admission,
    BrowserSpotlightPackageTypeIntent Intent);

internal abstract record BrowserIndexedTypeFindExecutionResult
{
    private protected BrowserIndexedTypeFindExecutionResult() { }

    internal sealed record Completed(
        BrowserIndexedTypeFindInspection Result) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Rejected(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Unavailable(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Stale(string Reason) :
        BrowserIndexedTypeFindExecutionResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserIndexedTypeFindOperation
{
    readonly object _gate = new();
    readonly string _issuer = Guid.NewGuid().ToString("N");
    PublicationState? _current;
    string? _retainedDefinitionId;
    string? _realizationId;
    long _latestGeneration = -1;
    long _publicationSequence;

    internal BrowserIndexedTypeFindExecutionResult Execute(
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        string retainedDefinitionId,
        string realizationId,
        string text,
        int maximumRows,
        long resultGeneration,
        CancellationToken cancellationToken = default)
        => ExecuteCore(
            operation,
            artifacts,
            retainedDefinitionId,
            realizationId,
            text,
            maximumRows,
            resultGeneration,
            resultSession: null,
            cancellationToken);

    internal BrowserIndexedTypeFindExecutionResult Execute(
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        string retainedDefinitionId,
        string realizationId,
        string text,
        int maximumRows,
        long resultGeneration,
        FindSemanticResultSession resultSession,
        CancellationToken cancellationToken = default)
        => ExecuteCore(
            operation,
            artifacts,
            retainedDefinitionId,
            realizationId,
            text,
            maximumRows,
            resultGeneration,
            resultSession
                ?? throw new ArgumentNullException(nameof(resultSession)),
            cancellationToken);

    BrowserIndexedTypeFindExecutionResult ExecuteCore(
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        string retainedDefinitionId,
        string realizationId,
        string text,
        int maximumRows,
        long resultGeneration,
        FindSemanticResultSession? resultSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);

        PublicationState publication;
        lock (_gate)
        {
            if (_retainedDefinitionId != retainedDefinitionId
                || _realizationId != realizationId)
            {
                _retainedDefinitionId = retainedDefinitionId;
                _realizationId = realizationId;
                _latestGeneration = -1;
                _current = null;
            }
            if (resultGeneration <= _latestGeneration)
            {
                return new BrowserIndexedTypeFindExecutionResult.Stale(
                    "A newer indexed Type Find result is already current.");
            }

            _latestGeneration = resultGeneration;
            publication = new(
                retainedDefinitionId,
                realizationId,
                resultGeneration,
                ++_publicationSequence);
            _current = publication;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new BrowserIndexedTypeFindExecutionResult.Rejected(
                "Indexed Type Find text must not be empty.");
        }
        if (maximumRows <= 0)
        {
            return new BrowserIndexedTypeFindExecutionResult.Rejected(
                "Indexed Type Find maximum rows must be positive.");
        }

        ImmutableArray<WorkspaceRegistration.Ecosystem> registrations =
        [
            .. operation.Definition.Plan.Registrations
                .OfType<WorkspaceRegistration.Ecosystem>(),
        ];
        var admittedArtifacts =
            ImmutableArray.CreateBuilder<EcosystemFindIndexArtifact>();
        var admittedSources = ImmutableArray.CreateBuilder<AdmittedSource>();
        var demands = ImmutableArray.CreateBuilder<
            EcosystemFindIndexedIntegrationSourceIdentity>();
        foreach (EcosystemFindIndexArtifact artifact in artifacts)
        {
            EcosystemWorkspaceRegistrationSelectionResult selection =
                EcosystemPackCatalog.SelectWorkspaceRegistration(
                    artifact.Ecosystem);
            if (selection
                is not EcosystemWorkspaceRegistrationSelectionResult.Known
                    known)
            {
                continue;
            }
            WorkspaceRegistration.Ecosystem? registration =
                registrations.SingleOrDefault(candidate =>
                    candidate.Declaration.Id == known.Declaration.Id);
            if (registration is null)
                continue;

            int demandCount = demands.Count;
            foreach (PackagePrefixDeclaration prefix in artifact.Prefixes)
            {
                if (registration.Declaration.Populations.Any(population =>
                    population
                        is WorkspaceEcosystemPopulationDeclaration.PackagePrefix
                            declared
                    && declared.Prefix == prefix))
                {
                    demands.Add(new(artifact.Ecosystem, prefix));
                    admittedSources.Add(new(
                        artifact.Ecosystem,
                        prefix,
                        registration.Declaration.Id));
                }
            }
            if (demands.Count > demandCount)
                admittedArtifacts.Add(artifact);
        }

        InspectionEnvelope<EcosystemFindIndexedIntegrationResult.Type> find =
            resultSession is null
                ? EcosystemFindIndexedTypeInspection.Execute(
                    admittedArtifacts,
                    demands,
                    TypeFindQuestion.Create(
                        [text],
                        FindVisibility.Public,
                        maximumRows),
                    maximumRows,
                    cancellationToken: cancellationToken)
                : EcosystemFindIndexedTypeInspection.Execute(
                    admittedArtifacts,
                    demands,
                    TypeFindQuestion.Create(
                        [text],
                        FindVisibility.Public,
                        maximumRows),
                    maximumRows,
                    resultSession,
                    cancellationToken: cancellationToken);
        ImmutableArray<BrowserIndexedTypeFindActivationAssociation> activations =
            CreateActivations(
                operation,
                publication,
                find.Content,
                admittedSources.ToImmutable());
        lock (_gate)
        {
            if (ReferenceEquals(_current, publication))
            {
                publication.Actions = [
                    .. activations
                        .Where(static activation =>
                            activation.Action is not null)
                        .Select(activation =>
                            KeyValuePair.Create(
                                activation.Action!,
                                publication.PendingActions[
                                    activation.MatchOrdinal])),
                ];
                publication.PendingActions =
                    ImmutableDictionary<
                        int,
                        BrowserIndexedTypeFindCapturedAction>.Empty;
                return new BrowserIndexedTypeFindExecutionResult.Completed(
                    new(find, activations));
            }
        }

        return new BrowserIndexedTypeFindExecutionResult.Stale(
            "A newer indexed Type Find result replaced this operation.");
    }

    internal async ValueTask<BrowserTypeFindActivationResult?>
        TryActivateAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string action,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (!action.StartsWith(
                $"indexed-type-find:{_issuer}:",
                StringComparison.Ordinal))
        {
            return null;
        }

        BrowserIndexedTypeFindCapturedAction? captured;
        string retainedDefinitionId;
        string realizationId;
        lock (_gate)
        {
            if (_current is not { } current
                || !current.Actions.TryGetValue(action, out captured))
            {
                return new BrowserTypeFindActivationResult.Blocked(
                    BrowserTypeFindActivationStatus.Stale,
                    "The indexed Type Find action is no longer current.");
            }
            current.Actions = current.Actions.Remove(action);
            retainedDefinitionId = current.RetainedDefinitionId;
            realizationId = current.RealizationId;
        }

        return await owner.ActivateIndexedPackageTypeAsync(
                retainedDefinitionId,
                realizationId,
                captured.Admission,
                captured.Intent,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _retainedDefinitionId = null;
            _realizationId = null;
            _latestGeneration = -1;
            _current = null;
        }
    }

    ImmutableArray<BrowserIndexedTypeFindActivationAssociation>
        CreateActivations(
            WorkspaceRealizationOperationLease operation,
            PublicationState publication,
            EcosystemFindIndexedIntegrationResult.Type find,
            ImmutableArray<AdmittedSource> admittedSources)
    {
        WorkspaceRegistrationReadResult registrationRead =
            operation.Workspace.GetRegistrationSnapshot();
        if (registrationRead
            is not WorkspaceRegistrationReadResult.Available registrations)
        {
            return [
                .. find.Matches.Select((_, index) =>
                    new BrowserIndexedTypeFindActivationAssociation(
                        index + 1,
                        Identity: null,
                        BrowserTypeFindActivationStatus.Unavailable,
                        Action: null,
                        "The active Workspace registration revision is unavailable.")),
            ];
        }

        var basis = new BrowserSpotlightActivationBasis(
            publication.Generation,
            operation.Scope,
            registrations.Revision);
        var activations =
            ImmutableArray.CreateBuilder<
                BrowserIndexedTypeFindActivationAssociation>(
                    find.Matches.Length);
        var pending =
            ImmutableDictionary.CreateBuilder<
                int,
                BrowserIndexedTypeFindCapturedAction>();
        for (int index = 0; index < find.Matches.Length; index++)
        {
            TypeFindSemanticMatch match = find.Matches[index];
            int ordinal = index + 1;
            var projectedActivation =
                BrowserIndexedPackageTypeProjection.Project(match);
            string? identity = projectedActivation.Identity;
            if (projectedActivation.Intent is not { } intent)
            {
                activations.Add(new(
                    ordinal,
                    identity,
                    BrowserTypeFindActivationStatus.Refused,
                    Action: null,
                    projectedActivation.RefusalReason));
                continue;
            }

            EcosystemFindIndexedPrefixCoverage[] coverage =
            [
                .. find.Coverage.Where(prefix =>
                    prefix.Libraries.Any(candidate =>
                        candidate.Source == match.Declaration.Source)),
            ];
            if (coverage.Length != 1)
            {
                activations.Add(new(
                    ordinal,
                    identity,
                    coverage.Length == 0
                        ? BrowserTypeFindActivationStatus.Unavailable
                        : BrowserTypeFindActivationStatus.Ambiguous,
                    Action: null,
                    coverage.Length == 0
                        ? "The indexed Type source has no admitted Ecosystem coverage."
                        : "The indexed Type source has ambiguous Ecosystem coverage."));
                continue;
            }

            AdmittedSource[] sourceAdmissions =
            [
                .. admittedSources.Where(source =>
                    source.Ecosystem == coverage[0].Source.Ecosystem
                    && source.Prefix == coverage[0].Source.Prefix),
            ];
            if (sourceAdmissions.Length != 1)
            {
                activations.Add(new(
                    ordinal,
                    identity,
                    BrowserTypeFindActivationStatus.Ambiguous,
                    Action: null,
                    "The indexed Type source has no unique retained registration."));
                continue;
            }

            var request = new BrowserSpotlightPackageRequest<
                BrowserIndexedTypeFindPackageRequest>(
                    intent.Package,
                    new());
            BrowserSpotlightDestinationProjectionResult<
                BrowserIndexedTypeFindPackageRequest,
                NavigationAction,
                BrowserFrameworkDeclarationAction,
                BrowserIndexedTypeFindLibraryIntent> projected =
                    BrowserSpotlightDestinationProjection.Project(
                        basis,
                        new BrowserSpotlightDestination<
                            BrowserIndexedTypeFindPackageRequest,
                            NavigationAction,
                            BrowserFrameworkDeclarationAction,
                            BrowserIndexedTypeFindLibraryIntent>.PackageType(
                                request,
                                intent));
            if (projected
                is not BrowserSpotlightDestinationProjectionResult<
                    BrowserIndexedTypeFindPackageRequest,
                    NavigationAction,
                    BrowserFrameworkDeclarationAction,
                    BrowserIndexedTypeFindLibraryIntent>.Projected selected
                || selected.Descriptor.Plan
                    is not (BrowserSpotlightDestinationActivationPlan<
                            BrowserIndexedTypeFindPackageRequest,
                            NavigationAction,
                            BrowserFrameworkDeclarationAction,
                            BrowserIndexedTypeFindLibraryIntent>
                        .AddCurrentPackageType
                        or BrowserSpotlightDestinationActivationPlan<
                            BrowserIndexedTypeFindPackageRequest,
                            NavigationAction,
                            BrowserFrameworkDeclarationAction,
                            BrowserIndexedTypeFindLibraryIntent>
                        .ActivateCurrentPackageType))
            {
                activations.Add(new(
                    ordinal,
                    identity,
                    BrowserTypeFindActivationStatus.Stale,
                    Action: null,
                    "The indexed Type activation basis is unavailable."));
                continue;
            }

            string action =
                $"indexed-type-find:{_issuer}:{publication.Sequence}:{ordinal}";
            pending.Add(
                ordinal,
                new(
                    new(
                        sourceAdmissions[0].Registration,
                        PackageQueryEcosystemMembershipBasis.PackagePrefix,
                        sourceAdmissions[0].Prefix.Prefix),
                    intent));
            activations.Add(new(
                ordinal,
                identity,
                BrowserTypeFindActivationStatus.Available,
                action,
                Reason: null));
        }
        publication.PendingActions = pending.ToImmutable();
        return activations.ToImmutable();
    }

    sealed class PublicationState(
        string retainedDefinitionId,
        string realizationId,
        long generation,
        long sequence)
    {
        internal string RetainedDefinitionId { get; } =
            retainedDefinitionId;

        internal string RealizationId { get; } = realizationId;

        internal long Generation { get; } = generation;

        internal long Sequence { get; } = sequence;

        internal ImmutableDictionary<
            string,
            BrowserIndexedTypeFindCapturedAction> Actions { get; set; } =
                ImmutableDictionary<
                    string,
                    BrowserIndexedTypeFindCapturedAction>.Empty
                    .WithComparers(StringComparer.Ordinal);

        internal ImmutableDictionary<
            int,
            BrowserIndexedTypeFindCapturedAction> PendingActions
                { get; set; } =
                    ImmutableDictionary<
                        int,
                        BrowserIndexedTypeFindCapturedAction>.Empty;
    }

    sealed record AdmittedSource(
        EcosystemPackId Ecosystem,
        PackagePrefixDeclaration Prefix,
        WorkspaceEcosystemRegistrationId Registration);
}
