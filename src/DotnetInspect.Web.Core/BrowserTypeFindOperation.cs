using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using QuerySpace.Rows;

namespace DotnetInspect.Web;

internal enum BrowserTypeFindActivationSource
{
    Package,
    Framework,
    Unsupported,
}

internal enum BrowserTypeFindActivationStatus
{
    Available,
    Unavailable,
    Stale,
    Ambiguous,
    Refused,
    Failed,
}

internal sealed record BrowserTypeFindCandidateReference(
    int AnswerOrdinal,
    int CandidateOrdinal);

internal sealed record BrowserTypeFindCandidateActivation(
    BrowserTypeFindCandidateReference Candidate,
    BrowserTypeFindActivationSource Source,
    BrowserTypeFindActivationStatus Status,
    string? Action,
    string? Reason);

internal sealed record BrowserTypeFindOperationResult(
    InspectionEnvelope<TypeDeclarationLocatorSectionResult> Find,
    ImmutableArray<BrowserTypeFindCandidateActivation> Activations);

internal abstract record BrowserTypeFindExecutionResult
{
    private protected BrowserTypeFindExecutionResult() { }

    internal sealed record Completed(BrowserTypeFindOperationResult Result)
        : BrowserTypeFindExecutionResult;

    internal sealed record Rejected(string Reason)
        : BrowserTypeFindExecutionResult;

    internal sealed record Unavailable(string Reason)
        : BrowserTypeFindExecutionResult;

    internal sealed record Stale(string Reason)
        : BrowserTypeFindExecutionResult;
}

internal sealed class BrowserTypeFindPackageRequest;
internal sealed class BrowserTypeFindLibraryIntent;

internal abstract record BrowserTypeFindCapturedDestination
{
    private protected BrowserTypeFindCapturedDestination() { }

    internal sealed record Package(
        BrowserSpotlightDestinationDescriptor<
            BrowserTypeFindPackageRequest,
            NavigationAction,
            BrowserFrameworkDeclarationAction,
            BrowserTypeFindLibraryIntent> Descriptor)
        : BrowserTypeFindCapturedDestination;

    internal sealed record Framework(
        BrowserSpotlightDestinationDescriptor<
            BrowserTypeFindPackageRequest,
            NavigationAction,
            BrowserFrameworkDeclarationAction,
            BrowserTypeFindLibraryIntent> Descriptor)
        : BrowserTypeFindCapturedDestination;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserTypeFindOperation : IDisposable
{
    readonly object _gate = new();
    readonly BrowserFrameworkDeclarationActivation _framework;
    PublicationState? _current;
    long _latestGeneration = -1;
    bool _disposed;

    internal BrowserTypeFindOperation(BrowserWorkspaceRealizationHost host)
    {
        _framework = new(host);
    }

    internal async ValueTask<BrowserTypeFindExecutionResult> ExecuteAsync(
        WorkspaceRealizationOperationLease operation,
        BrowserNavigationStateSlot navigation,
        string text,
        long resultGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(navigation);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BrowserTypeFindExecutionResult.Rejected(
                "Type Find text must not be empty.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);

        BrowserFrameworkDeclarationResultAuthority frameworkAuthority;
        PublicationState publication;
        lock (_gate)
        {
            if (_disposed)
            {
                return new BrowserTypeFindExecutionResult.Unavailable(
                    "Type Find is closed.");
            }
            if (resultGeneration <= _latestGeneration)
            {
                return new BrowserTypeFindExecutionResult.Stale(
                    "A newer Type Find result is already current.");
            }

            BrowserFrameworkDeclarationResultAdmission frameworkAdmission =
                _framework.BeginResult(resultGeneration);
            if (frameworkAdmission
                is not BrowserFrameworkDeclarationResultAdmission.Admitted
                    admitted)
            {
                return new BrowserTypeFindExecutionResult.Unavailable(
                    "Framework Type activation is unavailable.");
            }

            _latestGeneration = resultGeneration;
            publication = new(resultGeneration);
            _current = publication;
            frameworkAuthority = admitted.Authority;
        }

        InspectionWorkspace workspace = operation.Workspace;
        var packageAdmissions =
            ImmutableArray.CreateBuilder<WorkspacePackageDeclarationAdmission>();
        foreach (WorkspacePackageOccurrenceDescriptor occurrence
            in operation.Scope.Packages)
        {
            WorkspacePackageDeclarationAdmissionResult admission =
                await workspace.AdmitPackageScopeDeclarationAsync(
                        occurrence,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (admission
                is not WorkspacePackageDeclarationAdmissionResult.Admitted
                    admitted)
            {
                return new BrowserTypeFindExecutionResult.Unavailable(
                    "A retained Package declaration population could not be "
                        + $"admitted: {((WorkspacePackageDeclarationAdmissionResult.Rejected)admission).Failure.Kind}.");
            }
            packageAdmissions.Add(admitted.Admission);
        }

        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        if (registrationRead
            is not WorkspaceRegistrationReadResult.Available registrations)
        {
            return new BrowserTypeFindExecutionResult.Unavailable(
                "The active Workspace registration revision is unavailable.");
        }

        var basis = new BrowserSpotlightActivationBasis(
            resultGeneration,
            operation.Scope,
            registrations.Revision);
        TypeDeclarationLocatorInspectionExecution execution =
            await TypeDeclarationLocatorInspection.ExecuteWithResultAsync(
                    workspace,
                    [new TypeDeclarationLocatorRequest.Pattern(text)],
                    new TypeDeclarationLocatorSectionPlan(
                        RowSelectionIntent<string>.Empty,
                        TypeDeclarationVisibilityPlan.Default),
                    cancellationToken)
                .ConfigureAwait(false);
        if (execution.LocatorResult
                is not TypeDeclarationLocatorResult.Evaluated locator
            || execution.Inspection.Content
                is not TypeDeclarationLocatorSectionResult.Evaluated projected)
        {
            return Complete(
                publication,
                new(
                    execution.Inspection,
                    []),
                []);
        }

        ImmutableArray<WorkspaceDeclarationContext> contexts =
            workspace.GetDeclarationContextsSnapshot();
        ImmutableArray<SourcePackageBinding> sourcePackages =
            ResolveSourcePackageBindings(operation, contexts);
        var pendingFramework =
            ImmutableArray.CreateBuilder<PendingFrameworkCandidate>();
        var activations =
            ImmutableArray.CreateBuilder<BrowserTypeFindCandidateActivation>();
        var captured =
            ImmutableArray.CreateBuilder<BrowserTypeFindCapturedDestination>();

        foreach (TypeDeclarationLocatorSectionAnswer answer
            in projected.Answers)
        {
            TypeDeclarationLocatorAnswer rawAnswer =
                locator.Answers[answer.Identity.Ordinal - 1];
            for (int candidateIndex = 0;
                candidateIndex < answer.Candidates.Length;
                candidateIndex++)
            {
                TypeDeclarationLocatorSectionCandidate candidate =
                    answer.Candidates[candidateIndex];
                TypeDeclarationLocatorCandidate rawCandidate =
                    ResolveRawCandidate(rawAnswer, candidate);
                var reference = new BrowserTypeFindCandidateReference(
                    answer.Identity.Ordinal,
                    candidateIndex + 1);

                if (rawCandidate.Observation.Origin
                        is WorkspaceDeclarationOrigin.PackageScope
                    or WorkspaceDeclarationOrigin.ContextLoad
                    {
                        Realized: RealizedMemberCoordinate.Package,
                    })
                {
                    PackageBinding binding = BindPackage(
                        operation,
                        rawCandidate,
                        contexts,
                        sourcePackages,
                        packageAdmissions.ToImmutable());
                    if (binding.Subject is null)
                    {
                        activations.Add(new(
                            reference,
                            BrowserTypeFindActivationSource.Package,
                            binding.Status,
                            Action: null,
                            binding.Reason));
                        continue;
                    }

                    NavigationActionPublicationResult action =
                        navigation.PublishRetainedTypeAction(binding.Subject);
                    if (action.Kind
                            is not NavigationActionPublicationKind.Published
                        || action.Action is null)
                    {
                        activations.Add(new(
                            reference,
                            BrowserTypeFindActivationSource.Package,
                            FromNavigation(action.Kind),
                            Action: null,
                            action.Message ?? action.Rejection?.ToString()));
                        continue;
                    }

                    BrowserSpotlightDestinationProjectionResult<
                        BrowserTypeFindPackageRequest,
                        NavigationAction,
                        BrowserFrameworkDeclarationAction,
                        BrowserTypeFindLibraryIntent> destination =
                        BrowserSpotlightDestinationProjection.Project(
                            basis,
                            new BrowserSpotlightDestination<
                                BrowserTypeFindPackageRequest,
                                NavigationAction,
                                BrowserFrameworkDeclarationAction,
                                BrowserTypeFindLibraryIntent>.Current(
                                    binding.Subject,
                                    action.Action));
                    if (destination
                        is not BrowserSpotlightDestinationProjectionResult<
                            BrowserTypeFindPackageRequest,
                            NavigationAction,
                            BrowserFrameworkDeclarationAction,
                            BrowserTypeFindLibraryIntent>.Projected selected)
                    {
                        activations.Add(new(
                            reference,
                            BrowserTypeFindActivationSource.Package,
                            BrowserTypeFindActivationStatus.Stale,
                            Action: null,
                            "The captured Spotlight activation basis is stale."));
                        continue;
                    }

                    string actionId =
                        $"type-find-{resultGeneration}-{captured.Count + 1}";
                    captured.Add(
                        new BrowserTypeFindCapturedDestination.Package(
                            selected.Descriptor));
                    activations.Add(new(
                        reference,
                        BrowserTypeFindActivationSource.Package,
                        BrowserTypeFindActivationStatus.Available,
                        actionId,
                        Reason: null));
                    continue;
                }

                if (rawCandidate.Observation.Origin
                        is WorkspaceDeclarationOrigin.PlatformPopulation
                    or WorkspaceDeclarationOrigin.PlatformReference
                    or WorkspaceDeclarationOrigin.ContextLoad
                    {
                        Realized: RealizedMemberCoordinate.Platform,
                    })
                {
                    int activationIndex = activations.Count;
                    activations.Add(new(
                        reference,
                        BrowserTypeFindActivationSource.Framework,
                        BrowserTypeFindActivationStatus.Unavailable,
                        Action: null,
                        "Framework activation has not been published."));
                    pendingFramework.Add(new(
                        activationIndex,
                        reference,
                        new BrowserFrameworkDeclarationDestination.Type(
                            rawCandidate)));
                    continue;
                }

                activations.Add(new(
                    reference,
                    BrowserTypeFindActivationSource.Unsupported,
                    BrowserTypeFindActivationStatus.Unavailable,
                    Action: null,
                    "This declaration source has no Browser Type activation."));
            }
        }

        if (pendingFramework.Count > 0)
        {
            BrowserFrameworkDeclarationPublication framework =
                await _framework.PublishAsync(
                        frameworkAuthority,
                        operation,
                        basis,
                        locator,
                        [.. pendingFramework.Select(
                            static pending => pending.Destination)],
                        contexts)
                    .ConfigureAwait(false);
            for (int index = 0; index < pendingFramework.Count; index++)
            {
                PendingFrameworkCandidate pending = pendingFramework[index];
                BrowserFrameworkDeclarationActionPublication action =
                    framework.Actions[index];
                if (action
                    is BrowserFrameworkDeclarationActionPublication.Blocked
                        blocked)
                {
                    activations[pending.ActivationIndex] = new(
                        pending.Reference,
                        BrowserTypeFindActivationSource.Framework,
                        FromFramework(blocked.Block),
                        Action: null,
                        FrameworkReason(blocked.Block));
                    continue;
                }

                BrowserFrameworkDeclarationAction frameworkAction =
                    ((BrowserFrameworkDeclarationActionPublication.Published)
                        action).Action;
                BrowserSpotlightDestinationProjectionResult<
                    BrowserTypeFindPackageRequest,
                    NavigationAction,
                    BrowserFrameworkDeclarationAction,
                    BrowserTypeFindLibraryIntent> destination =
                    BrowserSpotlightDestinationProjection.Project(
                        basis,
                        new BrowserSpotlightDestination<
                            BrowserTypeFindPackageRequest,
                            NavigationAction,
                            BrowserFrameworkDeclarationAction,
                            BrowserTypeFindLibraryIntent>.PlatformDescendant(
                                new(
                                    operation.Realization,
                                    frameworkAction)));
                if (destination
                    is not BrowserSpotlightDestinationProjectionResult<
                        BrowserTypeFindPackageRequest,
                        NavigationAction,
                        BrowserFrameworkDeclarationAction,
                        BrowserTypeFindLibraryIntent>.Projected selected)
                {
                    activations[pending.ActivationIndex] = new(
                        pending.Reference,
                        BrowserTypeFindActivationSource.Framework,
                        BrowserTypeFindActivationStatus.Stale,
                        Action: null,
                        "The captured Spotlight activation basis is stale.");
                    continue;
                }

                string actionId =
                    $"type-find-{resultGeneration}-{captured.Count + 1}";
                captured.Add(
                    new BrowserTypeFindCapturedDestination.Framework(
                        selected.Descriptor));
                activations[pending.ActivationIndex] = new(
                    pending.Reference,
                    BrowserTypeFindActivationSource.Framework,
                    BrowserTypeFindActivationStatus.Available,
                    actionId,
                    Reason: null);
            }
        }

        return Complete(
            publication,
            new(
                execution.Inspection,
                activations.ToImmutable()),
            captured.ToImmutable());
    }

    internal BrowserTypeFindCapturedDestination? ResolveAction(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        lock (_gate)
        {
            if (_disposed || _current is not { } current)
                return null;
            return current.Actions.GetValueOrDefault(action);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _current = null;
        }
        _framework.Dispose();
    }

    BrowserTypeFindExecutionResult Complete(
        PublicationState publication,
        BrowserTypeFindOperationResult result,
        ImmutableArray<BrowserTypeFindCapturedDestination> destinations)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_current, publication))
            {
                return new BrowserTypeFindExecutionResult.Stale(
                    "A newer Type Find result replaced this operation.");
            }
            if (result.Activations.Count(
                    static activation => activation.Action is not null)
                != destinations.Length)
            {
                throw new InvalidOperationException(
                    "Every captured Type Find action must have one descriptor.");
            }

            publication.Actions =
                result.Activations
                    .Where(static activation => activation.Action is not null)
                    .Zip(
                        destinations,
                        static (activation, destination) =>
                            KeyValuePair.Create(
                                activation.Action!,
                                destination))
                    .ToImmutableDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal);
            return new BrowserTypeFindExecutionResult.Completed(result);
        }
    }

    static TypeDeclarationLocatorCandidate ResolveRawCandidate(
        TypeDeclarationLocatorAnswer answer,
        TypeDeclarationLocatorSectionCandidate projected)
    {
        TypeDeclarationLocatorCandidate[] matches =
        [
            .. answer.Candidates.Where(candidate =>
                candidate.Observation.Occurrence.ContextOrder
                    == projected.Observation.ContextOrder
                && candidate.Observation.Occurrence.MemberOrder
                    == projected.Observation.MemberOrder
                && candidate.DeclarationOrder == projected.DeclarationOrder
                && candidate.ModuleVersionId == projected.ModuleVersionId
                && candidate.Name == projected.Name
                && candidate.Kind == projected.DeclarationKind),
        ];
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "A projected Type Find candidate has no locator correspondence."),
            _ => throw new InvalidOperationException(
                "A projected Type Find candidate has ambiguous locator correspondence."),
        };
    }

    static PackageBinding BindPackage(
        WorkspaceRealizationOperationLease operation,
        TypeDeclarationLocatorCandidate candidate,
        ImmutableArray<WorkspaceDeclarationContext> contexts,
        ImmutableArray<SourcePackageBinding> sourcePackages,
        ImmutableArray<WorkspacePackageDeclarationAdmission> admissions)
    {
        if (candidate.Kind != AssemblyTypeDeclarationKind.Definition)
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "Forwarders do not yet have an exact Package Type activation.");
        }
        if (!ReferenceEquals(
                candidate.Observation.Occurrence.Workspace,
                operation.Realization))
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Stale,
                "The declaration belongs to another Workspace realization.");
        }

        return candidate.Observation.Origin switch
        {
            WorkspaceDeclarationOrigin.ContextLoad
            {
                Realized: RealizedMemberCoordinate.Package,
            } origin => BindContextLoadPackage(
                operation,
                candidate,
                origin,
                contexts,
                sourcePackages),
            WorkspaceDeclarationOrigin.PackageScope origin =>
                BindPackageScopePackage(
                    operation,
                    candidate,
                    origin,
                    sourcePackages,
                    admissions),
            _ => new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The declaration is not a Package observation."),
        };
    }

    static PackageBinding BindContextLoadPackage(
        WorkspaceRealizationOperationLease operation,
        TypeDeclarationLocatorCandidate candidate,
        WorkspaceDeclarationOrigin.ContextLoad origin,
        ImmutableArray<WorkspaceDeclarationContext> contexts,
        ImmutableArray<SourcePackageBinding> sourcePackages)
    {
        SourcePackageBindingResult source = ResolveContextLoadPackage(
            operation,
            candidate.Observation,
            origin,
            contexts,
            sourcePackages);
        if (source.Binding is null || source.Library is null)
        {
            return new(
                null,
                source.Status,
                source.Reason);
        }

        return CreatePackageBinding(
            operation.Realization,
            candidate,
            source.Binding.Occurrence.Occurrence,
            source.Library);
    }

    static PackageBinding BindPackageScopePackage(
        WorkspaceRealizationOperationLease operation,
        TypeDeclarationLocatorCandidate candidate,
        WorkspaceDeclarationOrigin.PackageScope origin,
        ImmutableArray<SourcePackageBinding> sourcePackages,
        ImmutableArray<WorkspacePackageDeclarationAdmission> admissions)
    {
        WorkspacePackageDeclarationAdmission[] matches =
        [
            .. admissions.Where(admission =>
                ReferenceEquals(
                    admission.Occurrence.Occurrence,
                    origin.Occurrence.Occurrence)
                && ReferenceEquals(
                    admission.Context.Receipt.Workspace,
                    operation.Realization)
                && admission.Context.Receipt.Order
                    == candidate.Observation.Occurrence.ContextOrder),
        ];
        if (matches.Length != 1)
        {
            return new(
                null,
                matches.Length == 0
                    ? BrowserTypeFindActivationStatus.Unavailable
                    : BrowserTypeFindActivationStatus.Ambiguous,
                matches.Length == 0
                    ? "The exact Package declaration context is unavailable."
                    : "The exact Package declaration context is ambiguous.");
        }

        WorkspacePackageDeclarationAdmission admission = matches[0];
        int memberIndex = candidate.Observation.Occurrence.MemberOrder;
        if (memberIndex < 0
            || memberIndex >= admission.Context.Receipt.Members.Length
            || !ReferenceEquals(
                admission.Context.Receipt.Members[memberIndex],
                candidate.Observation)
            || admission.Context.Group is not { } group
            || memberIndex >= group.Participants.Length
            || admission.Context.Receipt.Members[memberIndex].Origin
                is not WorkspaceDeclarationOrigin.PackageScope memberOrigin
            || !ReferenceEquals(memberOrigin.Asset, origin.Asset))
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The Package declaration observation has no exact live "
                    + "participant association.");
        }

        AssemblyContextParticipant participant =
            group.Participants[memberIndex];
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                participant.Assembly.Identity,
                candidate.Observation.AssemblyIdentity))
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The Package declaration participant identity does not match "
                    + "the locator observation.");
        }

        SourcePackageBinding[] sources =
        [
            .. sourcePackages.Where(source =>
                ReferenceEquals(
                    source.Occurrence.Occurrence,
                    origin.Occurrence.Occurrence)),
        ];
        if (sources.Length != 1)
        {
            return new(
                null,
                sources.Length == 0
                    ? BrowserTypeFindActivationStatus.Unavailable
                    : BrowserTypeFindActivationStatus.Ambiguous,
                sources.Length == 0
                    ? "The original Package declaration source is unavailable."
                    : "The original Package declaration source is ambiguous.");
        }

        SourcePackageBinding source = sources[0];
        IReadOnlyList<PackageCompileAsset> assets =
            source.Binding.Root.AssetSelection.Assets;
        int assetIndex = -1;
        for (int index = 0; index < assets.Count; index++)
        {
            if (!ReferenceEquals(assets[index], origin.Asset))
                continue;
            if (assetIndex >= 0)
            {
                return new(
                    null,
                    BrowserTypeFindActivationStatus.Ambiguous,
                    "The exact Package compile asset occurs more than once.");
            }
            assetIndex = index;
        }
        if (assetIndex < 0
            || source.Libraries.Length != assets.Count
            || assetIndex >= source.Libraries.Length)
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Unavailable,
                "The original Package Library association is unavailable.");
        }

        WorkspaceContextMember library = source.Libraries[assetIndex];
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                library.Participant.Assembly.Identity,
                candidate.Observation.AssemblyIdentity))
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The original Package Library identity does not match the "
                    + "locator observation.");
        }

        return CreatePackageBinding(
            operation.Realization,
            candidate,
            origin.Occurrence.Occurrence,
            library);
    }

    static SourcePackageBindingResult ResolveContextLoadPackage(
        WorkspaceRealizationOperationLease operation,
        WorkspaceDeclarationMember observation,
        WorkspaceDeclarationOrigin.ContextLoad origin,
        ImmutableArray<WorkspaceDeclarationContext> contexts,
        ImmutableArray<SourcePackageBinding> sourcePackages)
    {
        WorkspaceDeclarationContext[] matches =
        [
            .. contexts.Where(context =>
                ReferenceEquals(
                    context.Receipt.Workspace,
                    operation.Realization)
                && context.Receipt.Order
                    == observation.Occurrence.ContextOrder
                && context.Receipt.Request
                    is WorkspaceDeclarationRequest.ContextLoad),
        ];
        if (matches.Length != 1)
        {
            return new(
                null,
                null,
                matches.Length == 0
                    ? BrowserTypeFindActivationStatus.Unavailable
                    : BrowserTypeFindActivationStatus.Ambiguous,
                matches.Length == 0
                    ? "The original Package declaration context is unavailable."
                    : "The original Package declaration context is ambiguous.");
        }

        WorkspaceDeclarationContext context = matches[0];
        int contextIndex = observation.Occurrence.ContextOrder;
        if (context.ContextLoadOutcome
                is not WorkspaceContextLoadOutcome.Loaded loaded
            || context.Receipt.Request
                is not WorkspaceDeclarationRequest.ContextLoad request
            || contextIndex < 0
            || contextIndex >= operation.Definition.Plan.Contexts.Length
            || request.Input != operation.Definition.Plan.Contexts[contextIndex])
        {
            return new(
                null,
                null,
                BrowserTypeFindActivationStatus.Unavailable,
                "The original Package declaration context is no longer live.");
        }

        int memberIndex = observation.Occurrence.MemberOrder;
        if (memberIndex < 0
            || memberIndex >= context.Receipt.Members.Length
            || memberIndex >= loaded.Members.Length
            || !ReferenceEquals(
                context.Receipt.Members[memberIndex],
                observation))
        {
            return new(
                null,
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The Package declaration observation has no exact source "
                    + "member association.");
        }

        WorkspaceContextMember library = loaded.Members[memberIndex];
        if (!ReferenceEquals(library.Declared, origin.Declared)
            || library.Realized != origin.Realized
            || !ReferenceEquals(
                library.Participant,
                loaded.Group.Participants[memberIndex])
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                library.Participant.Assembly.Identity,
                observation.AssemblyIdentity))
        {
            return new(
                null,
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The Package declaration source member does not match the "
                    + "locator observation.");
        }

        SourcePackageBinding[] sources =
        [
            .. sourcePackages.Where(source =>
                ReferenceEquals(source.Context, context)
                && source.Libraries.Any(
                    candidate => ReferenceEquals(candidate, library))),
        ];
        return sources.Length switch
        {
            1 => new(
                sources[0],
                library,
                BrowserTypeFindActivationStatus.Available,
                Reason: null),
            0 => new(
                null,
                null,
                BrowserTypeFindActivationStatus.Unavailable,
                "The exact Package occurrence is unavailable."),
            _ => new(
                null,
                null,
                BrowserTypeFindActivationStatus.Ambiguous,
                "The exact Package occurrence is ambiguous."),
        };
    }

    static ImmutableArray<SourcePackageBinding> ResolveSourcePackageBindings(
        WorkspaceRealizationOperationLease operation,
        ImmutableArray<WorkspaceDeclarationContext> contexts)
    {
        var sources = ImmutableArray.CreateBuilder<SourcePackageBinding>();
        for (int contextIndex = 0;
            contextIndex < operation.Definition.Plan.Contexts.Length;
            contextIndex++)
        {
            WorkspaceContextInput input =
                operation.Definition.Plan.Contexts[contextIndex];
            WorkspaceDeclarationContext[] contextMatches =
            [
                .. contexts.Where(context =>
                    context.Receipt.Order == contextIndex
                    &&
                    context.Receipt.Request
                        is WorkspaceDeclarationRequest.ContextLoad request
                    && request.Input == input),
            ];
            if (contextMatches.Length != 1
                || contextMatches[0].ContextLoadOutcome
                    is not WorkspaceContextLoadOutcome.Loaded loaded)
            {
                continue;
            }

            int packageIndex = 0;
            foreach (WorkspaceMemberCoordinate declared in input.Members)
            {
                if (declared
                    is not WorkspaceMemberCoordinate.PackageMember)
                {
                    continue;
                }
                if (packageIndex >= loaded.PackageRoots.Length)
                    break;

                PackageRootBinding binding =
                    loaded.PackageRoots[packageIndex++];
                WorkspacePackageOccurrenceDescriptor? occurrence =
                    operation.Scope.FindExactPackageOccurrence(binding);
                if (occurrence is null)
                    continue;

                ImmutableArray<WorkspaceContextMember> libraries =
                [
                    .. loaded.Members.Where(member =>
                        ReferenceEquals(member.Declared, declared)),
                ];
                sources.Add(new(
                    contextMatches[0],
                    binding,
                    occurrence,
                    libraries));
            }
        }
        return sources.ToImmutable();
    }

    static PackageBinding CreatePackageBinding(
        InspectionWorkspaceIdentity workspace,
        TypeDeclarationLocatorCandidate candidate,
        WorkspacePackageOccurrence occurrence,
        WorkspaceContextMember library)
    {
        if (library.Realized != occurrence.Package.Coordinate)
        {
            return new(
                null,
                BrowserTypeFindActivationStatus.Refused,
                "The Package Library does not belong to the exact occurrence.");
        }

        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(workspace),
                occurrence);
        StructuralSubjectIdentity.LibrarySubject librarySubject =
            StructuralSubjectIdentity.ForLibrary(package, library);
        return new(
            StructuralSubjectIdentity.ForType(
                librarySubject,
                candidate.Name),
            BrowserTypeFindActivationStatus.Available,
            Reason: null);
    }

    static BrowserTypeFindActivationStatus FromNavigation(
        NavigationActionPublicationKind kind) =>
        kind switch
        {
            NavigationActionPublicationKind.Published =>
                BrowserTypeFindActivationStatus.Available,
            NavigationActionPublicationKind.Stale =>
                BrowserTypeFindActivationStatus.Stale,
            NavigationActionPublicationKind.Unavailable =>
                BrowserTypeFindActivationStatus.Unavailable,
            NavigationActionPublicationKind.Rejected =>
                BrowserTypeFindActivationStatus.Refused,
            NavigationActionPublicationKind.Refused =>
                BrowserTypeFindActivationStatus.Refused,
            _ => throw new InvalidOperationException(
                "Unknown Navigation action publication kind."),
        };

    static BrowserTypeFindActivationStatus FromFramework(
        BrowserFrameworkDeclarationActivationBlock block) =>
        block switch
        {
            BrowserFrameworkDeclarationActivationBlock.Unavailable =>
                BrowserTypeFindActivationStatus.Unavailable,
            BrowserFrameworkDeclarationActivationBlock.Stale =>
                BrowserTypeFindActivationStatus.Stale,
            BrowserFrameworkDeclarationActivationBlock.Ambiguous =>
                BrowserTypeFindActivationStatus.Ambiguous,
            BrowserFrameworkDeclarationActivationBlock.Refused =>
                BrowserTypeFindActivationStatus.Refused,
            BrowserFrameworkDeclarationActivationBlock.Failed =>
                BrowserTypeFindActivationStatus.Failed,
            BrowserFrameworkDeclarationActivationBlock.Canceled =>
                BrowserTypeFindActivationStatus.Stale,
            _ => throw new InvalidOperationException(
                "Unknown framework declaration activation block."),
        };

    static string FrameworkReason(
        BrowserFrameworkDeclarationActivationBlock block) =>
        block switch
        {
            BrowserFrameworkDeclarationActivationBlock.Unavailable unavailable =>
                unavailable.Reason.ToString(),
            BrowserFrameworkDeclarationActivationBlock.Stale stale =>
                stale.Reason.ToString(),
            BrowserFrameworkDeclarationActivationBlock.Ambiguous ambiguous =>
                ambiguous.Reason.ToString(),
            BrowserFrameworkDeclarationActivationBlock.Refused refused =>
                refused.Reason.ToString(),
            BrowserFrameworkDeclarationActivationBlock.Failed failed =>
                failed.Reason.ToString(),
            BrowserFrameworkDeclarationActivationBlock.Canceled =>
                "Canceled",
            _ => throw new InvalidOperationException(
                "Unknown framework declaration activation block."),
        };

    sealed class PublicationState(long generation)
    {
        internal long Generation { get; } = generation;

        internal ImmutableDictionary<
            string,
            BrowserTypeFindCapturedDestination> Actions { get; set; } =
                ImmutableDictionary<
                    string,
                    BrowserTypeFindCapturedDestination>.Empty
                    .WithComparers(StringComparer.Ordinal);
    }

    sealed record PendingFrameworkCandidate(
        int ActivationIndex,
        BrowserTypeFindCandidateReference Reference,
        BrowserFrameworkDeclarationDestination Destination);

    sealed record PackageBinding(
        StructuralSubjectIdentity.TypeSubject? Subject,
        BrowserTypeFindActivationStatus Status,
        string? Reason);

    sealed record SourcePackageBinding(
        WorkspaceDeclarationContext Context,
        PackageRootBinding Binding,
        WorkspacePackageOccurrenceDescriptor Occurrence,
        ImmutableArray<WorkspaceContextMember> Libraries);

    sealed record SourcePackageBindingResult(
        SourcePackageBinding? Binding,
        WorkspaceContextMember? Library,
        BrowserTypeFindActivationStatus Status,
        string? Reason);
}

internal sealed partial class BrowserRetainedWorkspaceActivationOwner
{
    BrowserTypeFindOperation _typeFind = null!;

    internal async ValueTask<BrowserTypeFindExecutionResult> FindTypesAsync(
        string retainedDefinitionId,
        string realizationId,
        string text,
        long resultGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);

        BrowserRetainedWorkspacePosting posting;
        BrowserTypeFindOperation typeFind;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RetainedDefinitionId != retainedDefinitionId
                || active.RealizationId != realizationId)
            {
                return new BrowserTypeFindExecutionResult.Unavailable(
                    "The requested retained Workspace realization is not active.");
            }
            posting = active;
            typeFind = _typeFind;
        }

        WorkspaceRealizationOperationAdmission admission =
            await EnterOperationAsync(
                    retainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserTypeFindExecutionResult.Unavailable(
                "The active Workspace realization is unavailable.");
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        lock (_gate)
        {
            if (!ReferenceEquals(_active, posting)
                || !ReferenceEquals(_typeFind, typeFind))
            {
                return new BrowserTypeFindExecutionResult.Stale(
                    "The active Workspace realization changed.");
            }
        }

        return await typeFind.ExecuteAsync(
                operation,
                posting.NavigationState,
                text,
                resultGeneration,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal BrowserTypeFindCapturedDestination? ResolveTypeFindAction(
        string action)
    {
        BrowserTypeFindOperation typeFind;
        lock (_gate)
            typeFind = _typeFind;
        return typeFind.ResolveAction(action);
    }

    void InitializeTypeFind() =>
        _typeFind = new BrowserTypeFindOperation(_host);

    void ReplaceTypeFindOperation()
    {
        _typeFind.Dispose();
        InitializeTypeFind();
    }

    void DisposeTypeFind() =>
        _typeFind.Dispose();
}
